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
                    string resolved = ResolveTemplate(templateText, comp);
                    if (!string.IsNullOrEmpty(resolved))
                    {
                        lines.Add(resolved);
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

        private static string ResolveTemplate(string template, object comp)
        {
            if (string.IsNullOrEmpty(template)) return "";
            
            return VariableRegex.Replace(template, match =>
            {
                string tag = match.Groups[1].Value;

                // Gestion des FlagCondition
                if (tag.StartsWith("FlagCondition:"))
                {
                    return ResolveFlagCondition(tag, comp);
                }

                // Résolution simple de champ
                return GetFieldValue(tag, comp);
            }).Trim();
        }

        private static string ResolveFlagCondition(string tag, object comp)
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
                    if (val is bool b && b)
                    {
                        // On cherche une traduction pour le nom du champ
                        string localizedName = Helpers.GetString("field_" + fieldName, fieldName);
                        activeFlags.Add(localizedName);
                    }
                    else if (val != null && !(val is bool) && !IsDefaultValue(val))
                    {
                        string localizedName = Helpers.GetString("field_" + fieldName, fieldName);
                        activeFlags.Add($"{localizedName}: {FormatValue(val)}");
                    }
                }
            }

            return activeFlags.Count > 0 ? $"({string.Join(", ", activeFlags)})" : "";
        }

        private static string GetFieldValue(string tag, object comp)
        {
            // Support rudimentaire pour m_Spell.GUID ou m_Spell.name
            string[] parts = tag.Split('.');
            string fieldName = parts[0];

            var fieldInfo = GetFieldRecursive(comp.GetType(), fieldName);
            if (fieldInfo == null) return $"<{tag}>"; // On garde le tag si non trouvé

            object val = fieldInfo.GetValue(comp);
            if (val == null) return "None";

            return FormatValue(val);
        }

        private static string FormatValue(object val)
        {
            if (val == null) return "None";

            var type = val.GetType();

            if (val is BlueprintReferenceBase bpRef)
            {
                var referred = bpRef.GetBlueprint();
                return referred != null ? referred.name : "Unknown";
            }
            if (val is ContextValue cv)
            {
                return cv.Value.ToString();
            }
            
            // Gestion des listes d'actions (Kingmaker.ElementsSystem.ActionList)
            if (type.Name.Contains("ActionList") || type.Name.Contains("ElementsList"))
            {
                return SummarizeElementsList(val);
            }

            if (val is IEnumerable<object> list)
            {
                return string.Join(", ", list.Select(FormatValue));
            }
            if (type.IsEnum)
            {
                return val.ToString();
            }

            // Gestion spécifique des structures complexes
            if (type.Name == "ContextActionConditional") return SummarizeConditional(val);
            if (type.Name == "ConditionsChecker") return SummarizeConditions(val);

            // Pour éviter les noms de types techniques comme Kingmaker.ActionList
            if (type.FullName.StartsWith("Kingmaker") && !type.IsPrimitive && type != typeof(string))
            {
                // On essaye de mapper le type si possible (Action, Condition, ou Champ)
                string typeKey = "action_" + type.Name;
                if (type.Name.StartsWith("ContextCondition")) typeKey = "cond_" + type.Name;

                string localizedType = Helpers.GetString(typeKey, "");
                if (!string.IsNullOrEmpty(localizedType)) return localizedType;
                
                // Fallback technique simplifié
                string fallback = type.Name.Replace("ContextAction", "").Replace("Action", "").Replace("ContextCondition", "").Replace("Condition", "");
                return string.IsNullOrEmpty(fallback) ? type.Name : fallback;
            }

            return val.ToString();
        }

        private static string SummarizeElementsList(object listObj)
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
                    
                    string summary = FormatValue(item);
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

        private static string SummarizeConditional(object condAction)
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
                    conditionText = SummarizeConditions(conditionsField.GetValue(condAction));
                }

                string trueText = "";
                if (ifTrueField != null)
                {
                    trueText = SummarizeElementsList(ifTrueField.GetValue(condAction));
                }

                string falseText = "";
                if (ifFalseField != null)
                {
                    falseText = SummarizeElementsList(ifFalseField.GetValue(condAction));
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

        private static string SummarizeConditions(object checker)
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
                    summaries.Add(FormatValue(cond));
                }

                string uiAnd = Helpers.GetString("ui_and", "and");
                return string.Join($" {uiAnd} ", summaries);
            }
            catch { return ""; }
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

        private static bool IsDefaultValue(object val)
        {
            if (val == null) return true;
            if (val is int i && i == 0) return true;
            if (val is float f && f == 0f) return true;
            if (val is string s && string.IsNullOrEmpty(s)) return true;
            return false;
        }
    }
}
