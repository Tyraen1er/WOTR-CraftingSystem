using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Items.Ecnchantments;
using Kingmaker.UnitLogic.Mechanics;

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
                    string templateText = template.enGB; // En attendant la localisation complète
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

        private static string ResolveTemplate(string template, object comp)
        {
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
                        activeFlags.Add(fieldName);
                    }
                    else if (val != null && !(val is bool) && !IsDefaultValue(val))
                    {
                        // Si c'est une valeur non-booléenne et non par défaut, on l'affiche aussi comme "active"
                        activeFlags.Add($"{fieldName}: {FormatValue(val)}");
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
            if (val is BlueprintReferenceBase bpRef)
            {
                var referred = bpRef.GetBlueprint();
                return referred != null ? referred.name : "Unknown";
            }
            if (val is ContextValue cv)
            {
                return cv.Value.ToString();
            }
            if (val is IEnumerable<object> list)
            {
                return string.Join(", ", list.Select(FormatValue));
            }
            if (val.GetType().IsEnum)
            {
                return val.ToString();
            }

            return val.ToString();
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
