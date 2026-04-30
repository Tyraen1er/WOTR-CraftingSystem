using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Items.Ecnchantments;

namespace CraftingSystem
{
    public static class EnchantmentDebug
    {
        public static void CheckMissingDescriptions()
        {
            var enchants = EnchantmentScanner.MasterList;
            int missing = 0;
            Dictionary<string, int> unknownComponents = new Dictionary<string, int>();

            foreach (var data in enchants)
            {
                var bp = data.Blueprint;
                if (bp == null) continue;

                DescriptionSource source;
                string desc = DescriptionManager.GetLocalizedDescription(bp, data, out source);

                if (source == DescriptionSource.None || string.IsNullOrEmpty(desc))
                {
                    missing++;
                    foreach (var comp in bp.ComponentsArray)
                    {
                        if (comp == null) continue;
                        string typeName = comp.GetType().Name;
                        if (typeName == "ContextRankConfig" || typeName == "ContextCalculateSharedValue") continue;

                        if (!unknownComponents.ContainsKey(typeName)) unknownComponents[typeName] = 0;
                        unknownComponents[typeName]++;
                    }
                }
            }

            Main.log.Log($"[DEBUG] Enchantments without description: {missing} / {enchants.Count}");
            var sorted = unknownComponents.OrderByDescending(x => x.Value).Take(10);
            foreach (var kvp in sorted)
            {
                Main.log.Log($"[DEBUG] Missing component handler for: {kvp.Key} ({kvp.Value} times)");
            }
        }
        public static void DumpBlueprint(string guidStr)
        {
            try
            {
                var guid = BlueprintGuid.Parse(guidStr);
                var bp = ResourcesLibrary.TryGetBlueprint(guid) as BlueprintItemEnchantment;
                if (bp == null)
                {
                    Main.log.Error($"[DEBUG] Blueprint not found for GUID: {guidStr}");
                    return;
                }

                Main.log.Log($"[DEBUG] --- DUMPING {bp.name} ({guidStr}) ---");
                Main.log.Log($"[DEBUG] Type: {bp.GetType().Name}");
                Main.log.Log($"[DEBUG] Description: {bp.m_Description}");

                foreach (var comp in bp.ComponentsArray)
                {
                    if (comp == null) continue;
                    string typeName = comp.GetType().Name;
                    Main.log.Log($"[DEBUG] [COMPONENT] {typeName}");
                    
                    // Utilisons la réflexion pour voir les champs de base
                    foreach (var field in comp.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                    {
                        try { Main.log.Log($"[DEBUG]   - {field.Name}: {field.GetValue(comp)}"); } catch { }
                    }
                }
                Main.log.Log("[DEBUG] --- END DUMP ---");
            }
            catch (Exception ex)
            {
                Main.log.Error($"[DEBUG] Error dumping blueprint {guidStr}: {ex}");
            }
        }
    }
}
