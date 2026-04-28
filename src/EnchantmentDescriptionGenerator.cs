using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Items.Ecnchantments;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.Localization;
using Kingmaker.EntitySystem.Stats;

namespace CraftingSystem
{
    public enum DescriptionSource
    {
        Official,
        Generated,
        None
    }

    public static class EnchantmentDescriptionGenerator
    {
        private static readonly Regex VariableRegex = new Regex(@"<([^>]+)>", RegexOptions.Compiled);

        public static string Generate(BlueprintItemEnchantment bp)
        {
            if (bp == null) return null;

            var components = bp.ComponentsArray;
            if (components == null || components.Length == 0) return null;

            var lines = new List<string>();

            foreach (var comp in components)
            {
                if (comp == null) continue;

                string typeName = comp.GetType().Name;
                
                // On ignore les composants techniques de calcul qui ne sont pas des enchantements directs
                if (typeName == "ContextRankConfig" || typeName == "ContextCalculateSharedValue") continue;

                string resolved = null;

                // 1. Priorité aux handlers spécifiques pour les composants complexes
                if (typeName == "AdditionalDiceOnAttack") 
                {
                    resolved = SummarizeAdditionalDiceOnAttack(comp, bp);
                }
                else if (typeName == "AddStatBonusEquipment")
                {
                    resolved = SummarizeAddStatBonus(comp, bp);
                }
                
                // 2. Sinon, on utilise le système de template JSON
                if (string.IsNullOrEmpty(resolved))
                {
                    var template = EnchantmentScanner.DescriptionTemplates.FirstOrDefault(t => t.ComponentType == typeName);
                    if (template != null)
                    {
                        string templateText = GetLocalizedTemplate(template);
                        resolved = ResolveTemplate(templateText, comp, bp);
                    }
                }

                if (!string.IsNullOrEmpty(resolved))
                {
                    lines.Add("> " + resolved);
                }
            }

            if (lines.Count == 0) return null;

            return string.Join("\n", lines);
        }

        private static string GetLocalizedTemplate(DescriptionTemplate template)
        {
            string currentLocale = LocalizationManager.CurrentLocale.ToString();
            
            if (currentLocale == "frFR" && !string.IsNullOrEmpty(template.frFR))
                return template.frFR;
            if (currentLocale == "ruRU" && !string.IsNullOrEmpty(template.ruRU))
                return template.ruRU;
                
            return template.enGB; // Fallback to English
        }

        private static string ResolveTemplate(string template, object comp, BlueprintItemEnchantment parentBp)
        {
            if (string.IsNullOrEmpty(template)) return "";
            
            return VariableRegex.Replace(template, match =>
            {
                string tag = match.Groups[1].Value;

                // Gestion des FlagCondition
                if (tag.StartsWith("FlagCondition:"))
                {
                    return ResolveFlagCondition(tag, comp, parentBp);
                }

                // Résolution simple de champ
                return GetFieldValue(tag, comp, parentBp);
            }).Replace("+-", "-").Trim();
        }

        private static string ResolveFlagCondition(string tag, object comp, BlueprintItemEnchantment parentBp)
        {
            // Format: FlagCondition: Field1, Field2, ...
            string fieldsPart = tag.Substring("FlagCondition:".Length);
            string[] fields = fieldsPart.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            
            List<string> activeFlags = new List<string>();
            foreach (var f in fields)
            {
                string fieldName = f.Trim();
                var fieldInfo = GetFieldRecursive(comp.GetType(), fieldName);
                if (fieldInfo != null)
                {
                    object val = fieldInfo.GetValue(comp);
                    if (val != null && !IsDefaultValue(val))
                    {
                        // Gestion intelligente des noms de champs et alias
                        string effectiveName = fieldName;
                        string localizedName = Helpers.GetString("field_" + effectiveName, "");
                        
                        if (string.IsNullOrEmpty(localizedName))
                        {
                            // Aliases pour noms techniques ou fautes de frappe dans les blueprints originaux
                            if (effectiveName == "isTwoHandedEquiped" || effectiveName == "IsTwoHandedEquipped") effectiveName = "OnlyTwoHanded";
                            if (effectiveName == "isShieldEquipped") effectiveName = "OnlyShieldEquipped";
                            
                            localizedName = Helpers.GetString("field_" + effectiveName, effectiveName);
                        }

                        string valStr = FormatValue(val, parentBp);
                        
                        if (val is bool b && b)
                        {
                            activeFlags.Add(localizedName);
                        }
                        else
                        {
                            activeFlags.Add($"{localizedName}: {valStr}");
                        }
                    }
                }
            }

            return activeFlags.Count > 0 ? $"({string.Join(", ", activeFlags)})" : "";
        }

        private static string GetFieldValue(string tag, object comp, BlueprintItemEnchantment parentBp)
        {
            // Support rudimentaire pour m_Spell.GUID ou m_Spell.name
            string[] parts = tag.Split('.');
            string fieldName = parts[0];

            var fieldInfo = GetFieldRecursive(comp.GetType(), fieldName);
            if (fieldInfo == null) return $"<{tag}>"; // On garde le tag si non trouvé

            object val = fieldInfo.GetValue(comp);
            return FormatValue(val, parentBp);
        }

        private static string FormatValue(object val, BlueprintItemEnchantment parentBp)
        {
            string result = InternalFormatValue(val, parentBp);
            return SanitizeLocalString(result);
        }

        private static string InternalFormatValue(object val, BlueprintItemEnchantment parentBp)
        {
            if (val == null) return "None";

            var type = val.GetType();

            if (val is BlueprintReferenceBase bpRef)
            {
                var referred = bpRef.GetBlueprint();
                if (referred == null) return "None";
                
                string displayName = GetBlueprintDisplayName(referred);
                if (!string.IsNullOrEmpty(displayName)) return $"[{displayName}]";
                
                // Si le nom d'affichage est nul, on utilise le nom interne, mais on le nettoie aussi
                string internalName = SanitizeLocalString(referred.name);
                if (string.IsNullOrEmpty(internalName)) return referred.GetType().Name;

                // Tente de localiser le nom technique (ex: ConstructClass)
                string localizedInternal = Helpers.GetString(internalName, "");
                if (!string.IsNullOrEmpty(localizedInternal)) return localizedInternal;

                return internalName;
            }
            if (val is ContextValue cv)
            {
                return SummarizeContextValue(cv, parentBp);
            }
            if (type.Name == "ContextDiceValue")
            {
                return SummarizeDiceValue(val, parentBp);
            }
            if (type.Name == "DamageTypeDescription")
            {
                return SummarizeDamageType(val);
            }
            
            // Gestion des listes d'actions (Kingmaker.ElementsSystem.ActionList)
            if (type.Name.Contains("ActionList") || type.Name.Contains("ElementsList"))
            {
                return SummarizeElementsList(val, parentBp);
            }

            if (val is IEnumerable<object> list)
            {
                return string.Join(", ", list.Select(v => FormatValue(v, parentBp)));
            }
            if (type.IsEnum)
            {
                string key = $"enum_{type.Name}_{val}";
                string localized = Helpers.GetString(key, "");
                if (string.IsNullOrEmpty(localized))
                {
                    // Fallback : essayer d'utiliser la valeur brute comme clé (ex: SkillAthletics)
                    string fallbackKey = val.ToString();
                    localized = Helpers.GetString(fallbackKey, "");
                }

                if (string.IsNullOrEmpty(localized))
                {
                    // Nettoyage des enums techniques pour l'affichage
                    string fallback = val.ToString();
                    if (fallback == "None" || fallback == "Untyped") return "";
                    return fallback;
                }
                return localized;
            }

            // Gestion spécifique des structures complexes
            if (type.Name.EndsWith("Conditional")) return SummarizeConditional(val, parentBp);
            if (type.Name.EndsWith("ConditionsChecker")) return SummarizeConditions(val, parentBp);
            if (type.Name.EndsWith("SavingThrow")) return SummarizeSavingThrow(val, parentBp);
            if (type.Name.EndsWith("ApplyBuff")) return SummarizeApplyBuff(val, parentBp);
            if (type.Name.EndsWith("CastSpell")) return SummarizeCastSpell(val, parentBp);
            if (type.Name.EndsWith("DealDamage")) return SummarizeDamageAction(val, parentBp);
            if (type.Name.EndsWith("ConditionalSaved")) return SummarizeConditionalSaved(val, parentBp);
            if (type.Name == "ContextActionOnOwner" || type.Name == "ContextActionOnInitiator" || type.Name == "ContextActionOnContextCaster") 
                return SummarizeActionOnSelf(val, GetContextLabel(type.Name), parentBp);
            if (type.Name == "ContextActionsOnPet") return SummarizeActionsOnPet(val, parentBp);

            // Conditions Spécifiques pour extraire les noms (Classe, Buff)
            if (type.Name == "ContextConditionCharacterClass") return SummarizeConditionCharacterClass(val, parentBp);
            if (type.Name == "ContextConditionHasBuff") return SummarizeConditionHasBuff(val, parentBp);
            if (type.Name == "ContextConditionCompareStat") return SummarizeConditionCompareStat(val, parentBp);
            if (type.Name == "ContextConditionAlignment") return SummarizeConditionAlignment(val, parentBp);
            if (type.Name.EndsWith("HasFact")) return SummarizeConditionHasFact(val, parentBp);
            if (type.Name == "ContextConditionHasBuffWithDescriptor") return SummarizeConditionHasBuffWithDescriptor(val, parentBp);
            if (type.Name == "AdditionalDiceOnAttack") return SummarizeAdditionalDiceOnAttack(val, parentBp);
            if (type.Name == "AddStatBonusEquipment") return SummarizeAddStatBonus(val, parentBp);
            
            if (type.Name == "SpellDescriptorWrapper")
            {
                string s = val.ToString() ?? "";
                if (s.StartsWith("SpellDescriptorWrapper [") && s.EndsWith("]"))
                {
                    s = s.Substring("SpellDescriptorWrapper [".Length, s.Length - "SpellDescriptorWrapper [".Length - 1);
                }
                return s;
            }

            // Pour éviter les noms de types techniques comme Kingmaker.ActionList
            if (type.FullName.StartsWith("Kingmaker") && !type.IsPrimitive && type != typeof(string))
            {
                // On essaye de mapper le type si possible (Action, Condition, ou Champ)
                string typeKey = "action_" + type.Name;
                if (type.Name.StartsWith("ContextCondition")) typeKey = "cond_" + type.Name;

                string localizedType = Helpers.GetString(typeKey, "");
                if (!string.IsNullOrEmpty(localizedType)) return localizedType;
                
                // Fallback technique simplifiée et robuste
                string fallback = type.Name;
                if (fallback.StartsWith("ContextAction")) fallback = fallback.Substring("ContextAction".Length);
                if (fallback.StartsWith("ContextCondition")) fallback = fallback.Substring("ContextCondition".Length);
                
                // On ne retire Action/Condition que si ce sont des suffixes et qu'il reste au moins 3 caractères
                if (fallback.Length > 9 && fallback.EndsWith("Action")) fallback = fallback.Substring(0, fallback.Length - 6);
                if (fallback.Length > 12 && fallback.EndsWith("Condition")) fallback = fallback.Substring(0, fallback.Length - 9);

                if (fallback == "al") fallback = "Conditional";

                string shortKey = "cond_" + fallback;
                string localizedShort = Helpers.GetString(shortKey, "");
                if (!string.IsNullOrEmpty(localizedShort)) return localizedShort;

                return string.IsNullOrEmpty(fallback) ? type.Name : fallback;
            }

            // Fallback final : tente de trouver une traduction pour la représentation textuelle brute dans le glossaire
            string raw = val.ToString();
            string localizedRaw = Helpers.GetString(raw, "");
            if (!string.IsNullOrEmpty(localizedRaw)) return localizedRaw;

            return raw;
        }

        private static string SummarizeContextValue(ContextValue cv, BlueprintItemEnchantment parentBp)
        {
            try
            {
                var valueTypeField = typeof(ContextValue).GetField("ValueType", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (valueTypeField == null) return cv.Value.ToString();

                var vt = valueTypeField.GetValue(cv).ToString();
                if (vt == "Simple") return cv.Value.ToString();
                
                if (vt == "Rank")
                {
                    var rankTypeField = typeof(ContextValue).GetField("ValueRank", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (rankTypeField != null && parentBp != null)
                    {
                        var rankType = rankTypeField.GetValue(cv).ToString();
                        // Chercher le ContextRankConfig sur le blueprint
                        var config = parentBp.ComponentsArray.FirstOrDefault(c => c.GetType().Name == "ContextRankConfig" && 
                                     GetFieldValueRecursive(c, "m_Type")?.ToString() == rankType);
                        
                        if (config != null) return SummarizeRankConfig(config, parentBp);
                    }
                    return Helpers.GetString("ui_rank_based", "(rank)");
                }
                
                return $"({vt})";
            }
            catch { return cv.Value.ToString(); }
        }

        private static string SummarizeRankConfig(object config, BlueprintItemEnchantment parentBp)
        {
            try
            {
                // Traduction des types de base techniques en noms conviviaux
                string baseValType = (GetFieldValueRecursive(config, "m_BaseValueType")?.ToString() ?? "CharacterLevel")
                                    .Replace("SummClassLevelWithArchetype", "ClassLevel")
                                    .Replace("BaseStat", "Stat");

                string progression = GetFieldValueRecursive(config, "m_Progression")?.ToString() ?? "AsIs";
                int step = (int)(GetFieldValueRecursive(config, "m_StepLevel") ?? 0);
                int start = (int)(GetFieldValueRecursive(config, "m_StartLevel") ?? 0);

                string baseLabel = Helpers.GetString("base_" + baseValType, baseValType);
                if (baseLabel == "ClassLevel") baseLabel = Helpers.GetString("ui_class_level", "class level");
                if (baseLabel == "CharacterLevel") baseLabel = Helpers.GetString("ui_character_level", "character level");

                string finalLabel = baseLabel;
                if (baseValType == "StatBonus" || baseValType == "Stat")
                {
                    var statField = GetFieldRecursive(config.GetType(), "m_Stat");
                    if (statField != null)
                    {
                        string statName = FormatValue(statField.GetValue(config), parentBp);
                        finalLabel = string.Format(baseLabel, statName);
                    }
                }

                if (progression == "AsIs") return string.Format(Helpers.GetString("prog_AsIs", "per {0}"), finalLabel);
                if (progression == "Div2") return string.Format(Helpers.GetString("prog_Div2", "per partial level of {0}"), finalLabel);
                if (progression == "StartPlusDivStep") return string.Format(Helpers.GetString("prog_StartPlusDivStep", "every {0} levels of {1} (after level {2})"), step, finalLabel, start);
                if (progression == "OnePlusDivStep") return string.Format(Helpers.GetString("prog_OnePlusDivStep", "every {0} levels of {1}"), step, finalLabel);
                if (progression == "MultiplyByModifier") return string.Format(Helpers.GetString("prog_MultiplyByModifier", "x{0} {1}"), step, finalLabel);

                return $"{progression} ({finalLabel})";
            }
            catch { return "(rank)"; }
        }

        private static string SummarizeElementsList(object listObj, BlueprintItemEnchantment parentBp)
        {
            if (listObj == null) return "";
            try
            {
                var type = listObj.GetType();
                var fieldsField = type.GetField("Actions", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic) 
                                ?? type.GetField("Elements", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                
                if (fieldsField == null) return "";

                var array = fieldsField.GetValue(listObj) as Array;
                if (array == null || array.Length == 0) return "";

                var summaries = new List<string>();
                foreach (var item in array)
                {
                    if (item == null) continue;
                    
                    string summary = FormatValue(item, parentBp);
                    if (!string.IsNullOrEmpty(summary) && !summaries.Contains(summary))
                    {
                        summaries.Add(summary);
                    }
                }

                if (summaries.Count == 0) return Helpers.GetString("ui_special_effects", "special effects");
                return string.Join(", ", summaries);
            }
            catch
            {
                return "";
            }
        }

        private static string SummarizeConditional(object condAction, BlueprintItemEnchantment parentBp)
        {
            try
            {
                var type = condAction.GetType();
                var conditionsField = type.GetField("ConditionsChecker", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                var ifTrueField = type.GetField("IfTrue", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                var ifFalseField = type.GetField("IfFalse", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);

                string conditionText = "";
                if (conditionsField != null)
                {
                    conditionText = SummarizeConditions(conditionsField.GetValue(condAction), parentBp);
                }

                string trueText = "";
                if (ifTrueField != null)
                {
                    trueText = SummarizeElementsList(ifTrueField.GetValue(condAction), parentBp);
                }

                string falseText = "";
                if (ifFalseField != null)
                {
                    falseText = SummarizeElementsList(ifFalseField.GetValue(condAction), parentBp);
                }

                StringBuilder sb = new StringBuilder();
                string uiIf = Helpers.GetString("ui_if", "if");
                string uiThen = Helpers.GetString("ui_then", "then");
                string uiElse = Helpers.GetString("ui_else", "else");

                sb.Append($"{uiIf} [{conditionText}]");
                if (!string.IsNullOrEmpty(trueText)) sb.Append($" {uiThen} ({trueText})");
                if (!string.IsNullOrEmpty(falseText)) sb.Append($" {uiElse} ({falseText})");

                return sb.ToString();
            }
            catch { return "Conditional"; }
        }

        private static string SummarizeSavingThrow(object saveAction, BlueprintItemEnchantment parentBp)
        {
            try
            {
                var type = saveAction.GetType();
                var actionsField = type.GetField("Actions", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                
                string baseText = Helpers.GetString("action_ContextActionSavingThrow", "requires a save");
                if (actionsField == null) return baseText;

                string outcomeText = SummarizeElementsList(actionsField.GetValue(saveAction), parentBp);
                if (string.IsNullOrEmpty(outcomeText)) return baseText;

                string uiOnFail = Helpers.GetString("ui_on_save_failed", "if failed:");
                string uiOnPass = Helpers.GetString("ui_on_save_passed", "if success:");
                
                // Si le contenu contient déjà des étiquettes de réussite/échec (ex: via ConditionalSaved),
                // on ne rajoute pas le préfixe "si échec" par défaut.
                if (outcomeText.Contains(uiOnFail) || outcomeText.Contains(uiOnPass))
                {
                    return $"{baseText} ({outcomeText})";
                }

                return $"{baseText} ({uiOnFail} {outcomeText})";
            }
            catch { return "SavingThrow"; }
        }

        private static string SummarizeApplyBuff(object applyBuff, BlueprintItemEnchantment parentBp)
        {
            try
            {
                var type = applyBuff.GetType();
                var buffField = type.GetField("m_Buff", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                if (buffField == null) return Helpers.GetString("action_ContextActionApplyBuff", "applies an effect");

                string buffName = FormatValue(buffField.GetValue(applyBuff), parentBp);
                string uiBuff = Helpers.GetString("ui_buff", "effect");
                return $"{uiBuff} {buffName}";
            }
            catch { return "ApplyBuff"; }
        }

        private static string SummarizeCastSpell(object castSpell, BlueprintItemEnchantment parentBp)
        {
            try
            {
                var type = castSpell.GetType();
                var spellField = type.GetField("m_Spell", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic)
                               ?? type.GetField("Ability", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                if (spellField == null) return Helpers.GetString("action_ContextActionCastSpell", "casts a spell");

                string spellName = FormatValue(spellField.GetValue(castSpell), parentBp);
                string uiSpell = Helpers.GetString("ui_spell", "spell");
                return $"{uiSpell} {spellName}";
            }
            catch { return "CastSpell"; }
        }

        private static string GetContextLabel(string typeName)
        {
            if (typeName.Contains("Owner")) return Helpers.GetString("ui_label_owner", "Porteur");
            if (typeName.Contains("Initiator")) return Helpers.GetString("ui_label_initiator", "Lanceur");
            if (typeName.Contains("Caster")) return Helpers.GetString("ui_label_caster", "Lanceur");
            if (typeName.Contains("Pet")) return Helpers.GetString("ui_label_pet", "Familier");
            return typeName;
        }

        private static string SummarizeActionOnSelf(object action, string label, BlueprintItemEnchantment parentBp)
        {
            try
            {
                var type = action.GetType();
                var actionsField = GetFieldRecursive(type, "Actions") ?? GetFieldRecursive(type, "m_Actions");
                if (actionsField == null) return $"[{label}]";
                
                string inner = SummarizeElementsList(actionsField.GetValue(action), parentBp);
                if (string.IsNullOrEmpty(inner)) return $"[{label}]";
                
                return $"[{label}] {inner}";
            }
            catch { return $"[{label}]"; }
        }

        private static string SummarizeActionsOnPet(object action, BlueprintItemEnchantment parentBp)
        {
            try
            {
                var type = action.GetType();
                var actionsField = GetFieldRecursive(type, "Actions") ?? GetFieldRecursive(type, "m_Actions");
                string label = Helpers.GetString("ui_label_pet", "Familier");
                
                if (actionsField == null) return $"[{label}]";
                
                string inner = SummarizeElementsList(actionsField.GetValue(action), parentBp);
                if (string.IsNullOrEmpty(inner)) return $"[{label}]";
                
                return $"[{label}] {inner}";
            }
            catch { return "[Familier]"; }
        }

        private static string SummarizeConditionalSaved(object action, BlueprintItemEnchantment parentBp)
        {
            try
            {
                var type = action.GetType();
                var succeedField = type.GetField("Succeed", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic) 
                                 ?? type.GetField("Success", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                var failedField = type.GetField("Failed", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic)
                                 ?? type.GetField("Failure", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);

                string succeedText = "";
                if (succeedField != null) succeedText = SummarizeElementsList(succeedField.GetValue(action), parentBp);

                string failedText = "";
                if (failedField != null) failedText = SummarizeElementsList(failedField.GetValue(action), parentBp);

                List<string> branches = new List<string>();
                if (!string.IsNullOrEmpty(succeedText))
                {
                    string uiPass = Helpers.GetString("ui_on_save_passed", "if success:");
                    branches.Add($"{uiPass} {succeedText}");
                }
                if (!string.IsNullOrEmpty(failedText))
                {
                    string uiFail = Helpers.GetString("ui_on_save_failed", "if failed:");
                    branches.Add($"{uiFail} {failedText}");
                }

                return string.Join(", ", branches);
            }
            catch { return "ConditionalSaved"; }
        }

        private static string SummarizeDamageAction(object damageAction, BlueprintItemEnchantment parentBp)
        {
            try
            {
                var type = damageAction.GetType();
                var valueField = GetFieldRecursive(type, "Value");
                var value = valueField?.GetValue(damageAction);
                string diceText = FormatValue(value, parentBp);
                
                var damageTypeField = GetFieldRecursive(type, "DamageType");
                object damageTypeObj = damageTypeField?.GetValue(damageAction);
                string typeText = damageTypeObj != null ? FormatValue(damageTypeObj, parentBp) : "";
                
                // Flags de dégâts
                bool half = (bool)(GetFieldRecursive(type, "Half")?.GetValue(damageAction) ?? false);
                bool noCrit = (bool)(GetFieldRecursive(type, "IgnoreCritical")?.GetValue(damageAction) ?? false);
                bool drain = (bool)(GetFieldRecursive(type, "Drain")?.GetValue(damageAction) ?? false);
                
                string baseText = Helpers.GetString("action_ContextActionDealDamage", "deals damage");
                if (string.IsNullOrEmpty(diceText) || diceText == "None") return baseText;

                List<string> extras = new List<string>();
                if (!string.IsNullOrEmpty(typeText) && typeText != "None" && typeText != "False") extras.Add(typeText);
                if (half) extras.Add(Helpers.GetString("ui_damage_half", "mi-dégâts"));
                if (noCrit) extras.Add(Helpers.GetString("ui_damage_no_crit", "pas de critique"));
                if (drain) extras.Add(Helpers.GetString("ui_damage_drain", "drain"));

                string details = extras.Count > 0 ? $" ({string.Join(", ", extras)})" : "";
                return $"{baseText} {diceText}{details}";
            }
            catch { return "DealDamage"; }
        }

        private static string SummarizeDiceValue(object diceValue, BlueprintItemEnchantment parentBp)
        {
            if (diceValue == null) return "";
            try
            {
                var type = diceValue.GetType();
                var diceTypeField = type.GetField("DiceType", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                var diceCountField = type.GetField("DiceCount", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                var bonusValueField = type.GetField("BonusValue", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);

                string diceTypeName = diceTypeField?.GetValue(diceValue)?.ToString() ?? "Zero";
                if (diceTypeName == "Zero") return "0";

                int diceSides = 0;
                if (diceTypeName.StartsWith("D")) int.TryParse(diceTypeName.Substring(1), out diceSides);
                else if (diceTypeName == "One") diceSides = 1;

                string count = FormatValue(diceCountField?.GetValue(diceValue), parentBp);
                if (count == "None" || count == "0") count = "1";

                string bonus = FormatValue(bonusValueField?.GetValue(diceValue), parentBp);

                string format = Helpers.GetString("ui_dice_format", "{0}d{1}");
                string main = "";
                
                if (diceSides > 1) 
                {
                    main = string.Format(format, count, diceSides);
                }
                else 
                {
                    // Cas spécial pour 1d1 ou les valeurs plates déguisées en dés
                    main = count;
                }

                if (!string.IsNullOrEmpty(bonus) && bonus != "0" && bonus != "None")
                {
                    main += $" + {bonus}";
                }

                return main;
            }
            catch { return "Dice"; }
        }

        private static string SummarizeAdditionalDiceOnAttack(object comp, BlueprintItemEnchantment parentBp)
        {
            try
            {
                var type = comp.GetType();
                var useWeaponDiceField = GetFieldRecursive(type, "m_UseWeaponDice");
                var valueField = GetFieldRecursive(type, "m_Value") ?? GetFieldRecursive(type, "Value");
                var damageTypeField = GetFieldRecursive(type, "m_DamageType") ?? GetFieldRecursive(type, "DamageType");
                
                var initiatorCondField = GetFieldRecursive(type, "InitiatorConditions");
                var targetCondField = GetFieldRecursive(type, "TargetConditions");
                
                var onHitField = GetFieldRecursive(type, "OnHit");
                var onCritField = GetFieldRecursive(type, "CriticalHit");

                bool useWeaponDice = (bool)(useWeaponDiceField?.GetValue(comp) ?? false);
                string valueStr = useWeaponDice ? Helpers.GetString("ui_weapon_damage", "weapon damage") : FormatValue(valueField?.GetValue(comp), parentBp);
                string damageType = FormatValue(damageTypeField?.GetValue(comp), parentBp);

                if (valueStr == "0" || string.IsNullOrEmpty(valueStr)) valueStr = "";
                if (damageType == "False" || damageType == "None") damageType = "";

                string template = Helpers.GetString("template_AdditionalDiceOnAttack", "Adds {0} {1}");
                string result = string.Format(template, valueStr, damageType).Trim();

                // Conditions
                string initiatorCondStr = initiatorCondField != null ? SummarizeConditions(initiatorCondField.GetValue(comp), parentBp) : "";
                string targetCondStr = targetCondField != null ? SummarizeConditions(targetCondField.GetValue(comp), parentBp) : "";
                
                string allConds = "";
                if (!string.IsNullOrEmpty(initiatorCondStr)) allConds = initiatorCondStr;
                if (!string.IsNullOrEmpty(targetCondStr)) 
                {
                    if (!string.IsNullOrEmpty(allConds)) allConds += " et ";
                    allConds += targetCondStr;
                }

                if (!string.IsNullOrEmpty(allConds))
                {
                    string uiIf = Helpers.GetString("ui_if", "if");
                    result += $" {uiIf} [{allConds}]";
                }

                // Hit types
                bool onHit = (bool)(onHitField?.GetValue(comp) ?? true);
                bool onCrit = (bool)(onCritField?.GetValue(comp) ?? false);

                if (onCrit && !onHit) result += $" ({Helpers.GetString("ui_on_crit", "sur coup critique")})";
                else if (onHit && onCrit) result += $" ({Helpers.GetString("ui_on_hit_crit", "sur coup porté ou critique")})";
                else if (onHit) result += $" ({Helpers.GetString("ui_on_hit", "sur coup porté")})";

                return result;
            }
            catch { return "AdditionalDice"; }
        }

        private static string SummarizeConditionHasBuffWithDescriptor(object cond, BlueprintItemEnchantment parentBp)
        {
            try
            {
                var type = cond.GetType();
                var descriptorField = GetFieldRecursive(type, "m_SpellDescriptor") ?? GetFieldRecursive(type, "SpellDescriptor");
                var notField = GetFieldRecursive(type, "Not");

                if (descriptorField == null) return Helpers.GetString("cond_HasBuffWithDescriptor_Generic", "possède un effet spécifique");

                object descriptor = descriptorField.GetValue(cond);
                string descriptorStr = FormatValue(descriptor, parentBp);
                
                // On essaie de localiser le ou les descripteurs (peuvent être séparés par des virgules)
                var parts = descriptorStr.Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries);
                var localizedParts = parts.Select(p => {
                    string loc = Helpers.GetString("descriptor_" + p, p);
                    return loc == "descriptor_" + p ? p : loc;
                });
                
                string localizedDescriptor = string.Join(", ", localizedParts);
                
                bool isNot = (bool)(notField?.GetValue(cond) ?? false);
                string templateKey = isNot ? "cond_NotHasBuffWithDescriptor" : "cond_HasBuffWithDescriptor";
                string defaultTemplate = isNot ? "doesn't have {0} effect" : "has {0} effect";
                
                return string.Format(Helpers.GetString(templateKey, defaultTemplate), localizedDescriptor);
            }
            catch { return "BuffDescriptor"; }
        }

        private static string SummarizeDamageType(object dmgType)
        {
            if (dmgType == null) return "";
            try
            {
                var type = dmgType.GetType();
                var categoryField = type.GetField("Type", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                var energyField = type.GetField("Energy", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                var commonField = type.GetField("Common", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);

                string category = categoryField?.GetValue(dmgType)?.ToString() ?? "Untyped";

                if (category == "Energy" && energyField != null)
                {
                    string energyType = energyField.GetValue(dmgType).ToString();
                    return Helpers.GetString("energy_" + energyType, energyType);
                }
                if (category == "Physical" && commonField != null)
                {
                    object common = commonField.GetValue(dmgType);
                    var formField = common.GetType().GetField("Precision", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic)
                                  ?? common.GetType().GetField("Form", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                    
                    string form = formField?.GetValue(common)?.ToString() ?? "Physical";
                    return Helpers.GetString("phys_" + form, form);
                }

                return Helpers.GetString("type_" + category, category);
            }
            catch { return "Damage"; }
        }

        private static string SummarizeConditionCharacterClass(object cond, BlueprintItemEnchantment parentBp)
        {
            try
            {
                var classField = GetFieldRecursive(cond.GetType(), "m_Class");
                if (classField == null) return "CharacterClass";

                string className = FormatValue(classField.GetValue(cond), parentBp);
                return string.Format(Helpers.GetString("cond_CharacterClass", "if class {0}"), className);
            }
            catch { return "CharacterClass"; }
        }

        private static string SummarizeConditionAlignment(object cond, BlueprintItemEnchantment parentBp)
        {
            var alignmentField = cond.GetType().GetField("Alignment", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (alignmentField == null) return Helpers.GetString("cond_Alignment", "alignment check");

            string alignment = alignmentField.GetValue(cond).ToString();
            string localizedAlign = Helpers.GetString("align_" + alignment, alignment);
            
            return string.Format(Helpers.GetString("cond_Alignment", "alignment is {0}"), localizedAlign);
        }

        private static string SummarizeConditionHasFact(object cond, BlueprintItemEnchantment parentBp)
        {
            var type = cond.GetType();
            var factField = GetFieldRecursive(type, "m_Fact") 
                         ?? GetFieldRecursive(type, "m_CheckedFact")
                         ?? GetFieldRecursive(type, "Fact");

            if (factField == null) return Helpers.GetString("cond_HasFact_Generic", "possède une capacité spécifique");

            string factName = FormatValue(factField.GetValue(cond), parentBp);
            return string.Format(Helpers.GetString("cond_HasFact", "has {0}"), factName);
        }

        private static string SummarizeConditionIsEnemy(object cond, BlueprintItemEnchantment parentBp)
        {
            var notField = cond.GetType().GetField("Not", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            bool isNot = (notField != null && (bool)notField.GetValue(cond));
            
            if (isNot) return Helpers.GetString("cond_IsAlly", "target is an ally");
            return Helpers.GetString("cond_ContextConditionIsEnemy", "target is an enemy");
        }

        private static string SummarizeConditionDistanceToTarget(object cond, BlueprintItemEnchantment parentBp)
        {
            var distField = cond.GetType().GetField("DistanceGreater", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (distField == null) distField = cond.GetType().GetField("Distance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            
            if (distField != null)
            {
                var val = distField.GetValue(cond);
                return string.Format(Helpers.GetString("cond_Distance", "distance is {0} ft"), val);
            }
            return Helpers.GetString("cond_ContextConditionDistanceToTarget", "distance check");
        }

        private static string SummarizeConditionHasBuff(object cond, BlueprintItemEnchantment parentBp)
        {
            try
            {
                var buffField = GetFieldRecursive(cond.GetType(), "m_Buff");
                if (buffField == null) return "HasBuff";

                string buffName = FormatValue(buffField.GetValue(cond), parentBp);
                return string.Format(Helpers.GetString("cond_HasBuff", "if effect {0} is active"), buffName);
            }
            catch { return "HasBuff"; }
        }

        private static string SummarizeConditionCompareStat(object cond, BlueprintItemEnchantment parentBp)
        {
            try
            {
                var statField = GetFieldRecursive(cond.GetType(), "Stat");
                if (statField == null) return Helpers.GetString("cond_CompareStat", "stat check");

                object statObj = statField.GetValue(cond);
                string statName = FormatValue(statObj, parentBp);
                return string.Format(Helpers.GetString("cond_CompareStatSpecific", "{0} check"), statName);
            }
            catch { return "CompareStat"; }
        }

        public static string GetBlueprintDisplayName(SimpleBlueprint bp)
        {
            if (bp == null) return "";
            try
            {
                Type t = bp.GetType();
                while (t != null && t != typeof(object))
                {
                    var field = t.GetField("m_DisplayName", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (field != null)
                    {
                        var localized = field.GetValue(bp) as LocalizedString;
                        if (localized != null) {
                            string text = SanitizeLocalString(localized.ToString());
                            if (!string.IsNullOrEmpty(text) && !text.Contains(bp.AssetGuid.ToString())) return text;
                        }
                    }

                    var enchField = t.GetField("m_EnchantName", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (enchField != null)
                    {
                        var localized = enchField.GetValue(bp) as LocalizedString;
                        if (localized != null) {
                            string text = SanitizeLocalString(localized.ToString());
                            if (!string.IsNullOrEmpty(text) && !text.Contains(bp.AssetGuid.ToString())) return text;
                        }
                    }

                    t = t.BaseType;
                }
            }
            catch { }
            return "";
        }

        private static string SummarizeConditions(object checker, BlueprintItemEnchantment parentBp)
        {
            if (checker == null) return "";
            try
            {
                var type = checker.GetType();
                var conditionsField = type.GetField("Conditions", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                if (conditionsField == null) return "";

                var array = conditionsField.GetValue(checker) as Array;
                if (array == null || array.Length == 0) return "";

                var summaries = new List<string>();
                foreach (var cond in array)
                {
                    if (cond == null) continue;
                    summaries.Add(FormatValue(cond, parentBp));
                }

                string uiAnd = Helpers.GetString("ui_and", "and");
                return string.Join($" {uiAnd} ", summaries);
            }
            catch { return ""; }
        }

        private static object GetFieldValueRecursive(object obj, string fieldName)
        {
            if (obj == null) return null;
            var field = GetFieldRecursive(obj.GetType(), fieldName);
            return field?.GetValue(obj);
        }

        private static FieldInfo GetFieldRecursive(Type type, string fieldName)
        {
            while (type != null && type != typeof(object))
            {
                var f = type.GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null) return f;
                type = type.BaseType;
            }
            return null;
        }

        private static string SummarizeAddStatBonus(object comp, BlueprintItemEnchantment parentBp)
        {
            try
            {
                var type = comp.GetType();
                var statField = GetFieldRecursive(type, "Stat");
                var valueField = GetFieldRecursive(type, "Value");
                var descriptorField = GetFieldRecursive(type, "Descriptor");

                string statName = statField != null ? FormatValue(statField.GetValue(comp), parentBp) : "Stat";
                int value = valueField != null ? (int)valueField.GetValue(comp) : 0;
                string descriptor = descriptorField != null ? FormatValue(descriptorField.GetValue(comp), parentBp) : "";

                string sign = value >= 0 ? "+" : "";
                string result = $"{sign}{value} {statName}";
                if (!string.IsNullOrEmpty(descriptor) && descriptor != "Untyped" && descriptor != "None")
                {
                    result += $" ({descriptor})";
                }
                return result;
            }
            catch { return "StatBonus"; }
        }

        private static string SanitizeLocalString(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            // Détecte les patterns d'erreurs de localisation du moteur : [[unknownkey: ...]]
            if (s.IndexOf("unknownkey", StringComparison.OrdinalIgnoreCase) >= 0) return "";
            if (s.StartsWith("[[") && s.EndsWith("]]")) return "";
            return s;
        }

        private static bool IsDefaultValue(object val)
        {
            if (val == null) return true;
            if (val is bool b && !b) return true; // Cache les drapeaux non cochés
            if (val is int i && i == 0) return true;
            if (val is float f && f == 0f) return true;
            if (val is string s && string.IsNullOrEmpty(s)) return true;
            return false;
        }
    }
}
