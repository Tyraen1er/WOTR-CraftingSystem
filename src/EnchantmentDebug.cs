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
    }
}
