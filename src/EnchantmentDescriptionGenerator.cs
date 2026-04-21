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
                var template = EnchantmentScanner.DescriptionTemplates.FirstOrDefault(t => t.ComponentType == typeName);

                if (template != null)
                {
                    string templateText = GetLocalizedTemplate(template);
                    string resolved = ResolveTemplate(templateText, comp, bp);
                    if (!string.IsNullOrEmpty(resolved))
                    {
                        lines.Add("- " + resolved);
                    }
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
            }).Trim();
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
                            if (effectiveName == "isTwoHandedEquiped") effectiveName = "OnlyTwoHanded";
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
            string sanitized = SanitizeLocalString(result);
            return string.IsNullOrEmpty(sanitized) ? "???" : sanitized;
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
                return string.IsNullOrEmpty(localized) ? val.ToString() : localized;
            }

            // Gestion spécifique des structures complexes
            if (type.Name.EndsWith("Conditional")) return SummarizeConditional(val, parentBp);
            if (type.Name.EndsWith("ConditionsChecker")) return SummarizeConditions(val, parentBp);
            if (type.Name.EndsWith("SavingThrow")) return SummarizeSavingThrow(val, parentBp);
            if (type.Name.EndsWith("ApplyBuff")) return SummarizeApplyBuff(val, parentBp);
            if (type.Name.EndsWith("CastSpell")) return SummarizeCastSpell(val, parentBp);
            if (type.Name.EndsWith("DealDamage")) return SummarizeDamageAction(val, parentBp);
            if (type.Name.EndsWith("ConditionalSaved")) return SummarizeConditionalSaved(val, parentBp);

            // Conditions Spécifiques pour extraire les noms (Classe, Buff)
            if (type.Name == "ContextConditionCharacterClass") return SummarizeConditionCharacterClass(val, parentBp);
            if (type.Name == "ContextConditionHasBuff") return SummarizeConditionHasBuff(val, parentBp);
            if (type.Name == "ContextConditionCompareStat") return SummarizeConditionCompareStat(val, parentBp);
            if (type.Name == "ContextConditionAlignment") return SummarizeConditionAlignment(val, parentBp);
            if (type.Name == "ContextConditionHasFact") return SummarizeConditionHasFact(val, parentBp);
            if (type.Name == "ContextConditionIsEnemy") return SummarizeConditionIsEnemy(val, parentBp);
            if (type.Name == "ContextConditionDistanceToTarget") return SummarizeConditionDistanceToTarget(val, parentBp);
            if (type.Name == "AddStatBonusEquipment") return SummarizeAddStatBonus(val, parentBp);

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

            return val.ToString();
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
                        
                        if (config != null) return SummarizeRankConfig(config);
                    }
                    return Helpers.GetString("ui_rank_based", "(rank)");
                }
                
                return $"({vt})";
            }
            catch { return cv.Value.ToString(); }
        }

        private static string SummarizeRankConfig(object config)
        {
            try
            {
                string baseValType = GetFieldValueRecursive(config, "m_BaseValueType")?.ToString() ?? "CharacterLevel";
                string progression = GetFieldValueRecursive(config, "m_Progression")?.ToString() ?? "AsIs";
                int step = (int)(GetFieldValueRecursive(config, "m_StepLevel") ?? 0);
                int start = (int)(GetFieldValueRecursive(config, "m_StartLevel") ?? 0);

                string baseLabel = Helpers.GetString("base_" + baseValType, baseValType);

                if (progression == "AsIs") return string.Format(Helpers.GetString("prog_AsIs", "per {0}"), baseLabel);
                if (progression == "Div2") return string.Format(Helpers.GetString("prog_Div2", "every 2 {0}"), baseLabel);
                if (progression == "StartPlusDivStep") return string.Format(Helpers.GetString("prog_StartPlusDivStep", "every {0} {1} (after {2})"), step, baseLabel, start);
                if (progression == "OnePlusDivStep") return string.Format(Helpers.GetString("prog_OnePlusDivStep", "every {0} {1}"), step, baseLabel);
                if (progression == "MultiplyByModifier") return string.Format(Helpers.GetString("prog_MultiplyByModifier", "x{0} {1}"), step, baseLabel);

                return $"{progression}({baseLabel})";
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
                var valueField = type.GetField("Value", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                var damageTypeField = type.GetField("DamageType", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);

                string diceText = "";
                if (valueField != null)
                {
                    diceText = FormatValue(valueField.GetValue(damageAction), parentBp);
                }

                string typeText = "";
                if (damageTypeField != null)
                {
                    typeText = FormatValue(damageTypeField.GetValue(damageAction), parentBp);
                }

                string baseText = Helpers.GetString("action_ContextActionDealDamage", "deals damage");
                if (string.IsNullOrEmpty(diceText) || diceText == "None") return baseText;

                return $"{baseText} {diceText} ({typeText})";
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
                string main = string.Format(format, count, diceSides);

                if (!string.IsNullOrEmpty(bonus) && bonus != "0" && bonus != "None")
                {
                    main += $" + {bonus}";
                }

                return main;
            }
            catch { return "Dice"; }
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
            var factField = cond.GetType().GetField("m_Fact", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (factField == null) return Helpers.GetString("cond_HasFact", "has fact");

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
                string statName = statObj?.ToString() ?? "Stat";
                return string.Format(Helpers.GetString("cond_CompareStatSpecific", "{0} check"), statName);
            }
            catch { return "CompareStat"; }
        }

        private static string GetBlueprintDisplayName(SimpleBlueprint bp)
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
