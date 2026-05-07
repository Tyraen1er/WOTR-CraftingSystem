using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.Blueprints;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.Blueprints.Classes.Spells;
using System.IO;
using Kingmaker.Localization;

namespace CraftingSystem
{
    public class SpellData
    {
        public string Guid;
        public string Name;
        public int MinLevel = 99;
        public string School;
        public List<string> Classes = new List<string>();
        public bool IsFromMod = false;
    }

    public static class SpellScanner
    {
        public static Dictionary<string, SpellData> AvailableSpells = new Dictionary<string, SpellData>();
        private static bool _initialized = false;

        public static void ScanAll()
        {
            if (_initialized) return;
            // On délègue maintenant au scanner unifié
            _ = UnifiedScanner.RunFullScan();
        }

        /// <summary>
        /// Appelée par le UnifiedScanner après avoir collecté tous les objets liés aux sorts.
        /// </summary>
        public static void FinalizeScan(IEnumerable<(BlueprintSpellbook sb, BlueprintGuid guid)> spellbooks, IEnumerable<(BlueprintSpellList sl, BlueprintGuid guid)> spellLists)
        {
            AvailableSpells.Clear();

            // 1. Traitement des Spellbooks (Classes)
            foreach (var item in spellbooks)
            {
                var list = item.sb.SpellList;
                if (list == null) continue;
                ProcessSpellList(list, item.sb.CharacterClass?.Name ?? item.sb.name);
            }

            // 2. Traitement des SpellLists directs (Special/Other)
            foreach (var item in spellLists)
            {
                ProcessSpellList(item.sl, "Special/Other");
            }

            _initialized = true;
            Main.ModEntry.Logger.Log($"[SCROLL-SCAN] Scan unifié terminé. {AvailableSpells.Count} sorts uniques trouvés.");
            
            // DumpToFile();
        }

        private static void ProcessSpellList(BlueprintSpellList list, string className)
        {
            if (list == null || list.SpellsByLevel == null) return;

            for (int level = 0; level < list.SpellsByLevel.Length; level++)
            {
                var levelList = list.SpellsByLevel[level];
                if (levelList == null || levelList.SpellsFiltered == null) continue;

                foreach (var spell in levelList.SpellsFiltered)
                {
                    if (spell == null) continue;
                    
                    string guid = spell.AssetGuid.ToString();
                    if (!AvailableSpells.TryGetValue(guid, out var data))
                    {
                        data = new SpellData
                        {
                            Guid = guid,
                            Name = spell.Name,
                            School = spell.GetComponent<SpellComponent>()?.School.ToString() ?? "None",
                            IsFromMod = !guid.StartsWith("0") && !guid.StartsWith("1") && !guid.StartsWith("2") // Heuristique simple pour les GUIDs vanilla
                        };
                        AvailableSpells[guid] = data;
                    }

                    if (level < data.MinLevel) data.MinLevel = level;
                    if (!data.Classes.Contains(className)) data.Classes.Add(className);
                }
            }
        }

        private static void DumpToFile()
        {
            try
            {
                string logPath = Path.Combine(Main.ModEntry.Path, "Logs");
                if (!Directory.Exists(logPath)) Directory.CreateDirectory(logPath);
                
                string filePath = Path.Combine(logPath, "AvailableSpells.txt");
                using (var writer = new StreamWriter(filePath, false))
                {
                    writer.WriteLine($"--- SCROLL CRAFTING - AVAILABLE SPELLS DUMP ({DateTime.Now}) ---");
                    writer.WriteLine($"Total: {AvailableSpells.Count}");
                    writer.WriteLine("GUID | LVL | MOD | SCHOOL | NAME | CLASSES");
                    writer.WriteLine("------------------------------------------------------------");
                    
                    var sorted = AvailableSpells.Values.OrderBy(x => x.MinLevel).ThenBy(x => x.Name);
                    foreach (var s in sorted)
                    {
                        string modTag = s.IsFromMod ? "[MOD]" : "     ";
                        string classes = string.Join(", ", s.Classes.Take(3));
                        if (s.Classes.Count > 3) classes += "...";

                        writer.WriteLine($"{s.Guid} |  {s.MinLevel}  | {modTag} | {s.School.PadRight(10)} | {s.Name.PadRight(30)} | {classes}");
                    }
                }
                Main.ModEntry.Logger.Log($"[SCROLL-SCAN] Dumped spell list to {filePath}");
            }
            catch (Exception e)
            {
                Main.ModEntry.Logger.Error($"[SCROLL-SCAN] Failed to dump spells: {e.Message}");
            }
        }
    }
}
