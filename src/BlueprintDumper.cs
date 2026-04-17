using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Items.Ecnchantments;
using UnityEngine;

namespace CraftingSystem
{
    public static class BlueprintDumper
    {
        private static HashSet<string> _dumpedGuids = new HashSet<string>();

        public static void DumpByGuid(string guid, int maxDepth = 2)
        {
            try
            {
                var bp = ResourcesLibrary.TryGetBlueprint(BlueprintGuid.Parse(guid));
                if (bp == null)
                {
                    Main.ModEntry.Logger.Log($"[DUMPER] Blueprint non trouvé pour le GUID : {guid}");
                    return;
                }

                _dumpedGuids.Clear();
                StringBuilder sb = new StringBuilder();
                sb.AppendLine($"\n[STRUCTURAL DUMP] Lancement pour : {bp.name} ({guid})");
                DumpStructure(bp, sb, 0, maxDepth);
                Main.ModEntry.Logger.Log(sb.ToString());
            }
            catch (Exception ex)
            {
                Main.ModEntry.Logger.Error($"[DUMPER] Erreur lors du dump du GUID {guid} : {ex}");
            }
        }

        private static void DumpStructure(SimpleBlueprint bp, StringBuilder sb, int depth, int maxDepth)
        {
            if (bp == null || depth > maxDepth) return;

            string guid = bp.AssetGuid.ToString();
            string indent = new string(' ', depth * 4);
            
            sb.AppendLine($"{indent}=== OBJECT: {bp.name} ({bp.GetType().Name}) ===");
            sb.AppendLine($"{indent}GUID: {guid}");

            if (_dumpedGuids.Contains(guid))
            {
                sb.AppendLine($"{indent}(Déjà dumpé, arrêt de la recursion)");
                return;
            }
            _dumpedGuids.Add(guid);

            // Dump fields across the hierarchy
            DumpFields(bp, sb, indent + "  ");

            // --- RECHERCHE ROBUSTE DES COMPOSANTS VIA HIÉRARCHIE ---
            object components = null;
            Type currentType = bp.GetType();
            
            while (currentType != null && currentType != typeof(object))
            {
                var field = currentType.GetField("Components", BindingFlags.NonPublic | BindingFlags.Instance)
                           ?? currentType.GetField("m_Components", BindingFlags.NonPublic | BindingFlags.Instance)
                           ?? currentType.GetField("ComponentsArray", BindingFlags.NonPublic | BindingFlags.Instance);
                
                if (field != null)
                {
                    components = field.GetValue(bp);
                    if (components != null) break;
                }
                currentType = currentType.BaseType;
            }

            if (components == null)
            {
                var componentsProp = bp.GetType().GetProperty("ComponentsArray", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (componentsProp != null) components = componentsProp.GetValue(bp);
            }

            if (components is IEnumerable enumerable)
            {
                int compIdx = 0;
                foreach (var comp in enumerable)
                {
                    if (comp == null) continue;
                    sb.AppendLine($"{indent}  [Component #{compIdx}] Type: {comp.GetType().Name}");
                    DumpFields(comp, sb, indent + "    ", depth, maxDepth);
                    compIdx++;
                }
            }
            else
            {
                sb.AppendLine($"{indent}  (AUCUN COMPOSANT TROUVÉ - Vérifiez la version du jeu)");
            }
        }

        private static void DumpFields(object obj, StringBuilder sb, string indent, int depth = 0, int maxDepth = 0)
        {
            if (obj == null) return;

            Type type = obj.GetType();
            HashSet<string> seenFields = new HashSet<string>();

            // On remonte la hiérarchie pour capturer les champs privés des parents
            while (type != null && type != typeof(object) && type != typeof(UnityEngine.Object))
            {
                var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;
                foreach (var field in type.GetFields(flags))
                {
                    if (seenFields.Contains(field.Name)) continue;
                    seenFields.Add(field.Name);

                    try
                    {
                        object val = field.GetValue(obj);
                        ProcessValue(field.Name, val, sb, indent, depth, maxDepth);
                    }
                    catch (Exception ex)
                    {
                        sb.AppendLine($"{indent}{field.Name}: <Erreur: {ex.Message}>");
                    }
                }
                type = type.BaseType;
            }
        }

        private static void ProcessValue(string name, object val, StringBuilder sb, string indent, int depth, int maxDepth)
        {
            if (val == null)
            {
                sb.AppendLine($"{indent}{name}: null");
                return;
            }

            // Cas spécial : Référence de Blueprint
            if (val is BlueprintReferenceBase bpRef)
            {
                var referred = bpRef.GetBlueprint();
                string refName = referred != null ? referred.name : "Unresolved";
                sb.AppendLine($"{indent}{name} (BlueprintRef): {refName} [GUID: {bpRef.Guid}]");
                
                // --- SMART FOLLOW: On plonge même si on est à profondeur max si c'est un enchantement ---
                bool isEnch = referred is BlueprintItemEnchantment;
                bool shouldFollow = isEnch && (depth <= maxDepth); // On autorise un niveau de plus pour les enchantements
                
                if (shouldFollow)
                {
                    sb.AppendLine($"{indent}  --> Diving into enchantment reference...");
                    DumpStructure(referred, sb, depth + 1, maxDepth + 1);
                }
            }
            // Cas spécial : Collections
            else if (val is IEnumerable enumerable && !(val is string))
            {
                sb.AppendLine($"{indent}{name} (Collection):");
                int idx = 0;
                foreach (var item in enumerable)
                {
                    ProcessValue($"[{idx}]", item, sb, indent + "  ", depth, maxDepth);
                    idx++;
                    if (idx > 20) { sb.AppendLine($"{indent}  ... (Tronqué à 20)"); break; }
                }
            }
            // Cas général
            else
            {
                string valStr = val.ToString();
                if (valStr.Length > 200) valStr = valStr.Substring(0, 197) + "...";
                sb.AppendLine($"{indent}{name}: {valStr} ({val.GetType().Name})");
            }
        }
    }
}
