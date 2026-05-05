using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Items.Weapons;
using Kingmaker.Blueprints.Items.Armors;
using Kingmaker.Blueprints.Items.Shields;
using Kingmaker.Blueprints.Items.Equipment;
using Kingmaker.Blueprints.Items;
using UnityEngine;

namespace CraftingSystem
{
    public class ItemData
    {
        public string Name;
        public int BaseCost;
        public string Category;
        public Sprite Icon;
        public string Description;
        public string TypeName;
        // Level -> Guid
        public Dictionary<int, string> VariantGuids = new Dictionary<int, string>();
        // Level -> Cost
        public Dictionary<int, int> VariantCosts = new Dictionary<int, int>();
        // Level -> Icon
        public Dictionary<int, Sprite> VariantIcons = new Dictionary<int, Sprite>();

        public string GetGuid(int level)
        {
            if (VariantGuids.TryGetValue(level, out var guid)) return guid;
            return VariantGuids.ContainsKey(0) ? VariantGuids[0] : null;
        }

        public Sprite GetIcon(int level)
        {
            if (VariantIcons.TryGetValue(level, out var icon) && icon != null) return icon;
            return Icon;
        }

        public int GetCost(int level)
        {
            if (VariantCosts.TryGetValue(level, out int cost)) return cost;

            // Fallback: Prix de base + (Bonus^2 * (Weapon ? 2000 : 1000))
            int magicCost = (level * level) * (Category == "Weapon" ? 2000 : 1000);
            return BaseCost + magicCost;
        }

        public string GetDisplayName(int level)
        {
            string name = Name ?? "Unknown Item";
            if (level <= 0) return name;
            return $"{name} +{level}";
        }
    }

    public static class ItemScanner
    {
        public static List<ItemData> Weapons = new List<ItemData>();
        public static List<ItemData> Armors = new List<ItemData>();
        public static List<ItemData> Shields = new List<ItemData>();
        public static List<ItemData> Accessories = new List<ItemData>();

        public static void FinalizeScan(
            IEnumerable<(BlueprintItemWeapon bp, BlueprintGuid guid)> weapons,
            IEnumerable<(BlueprintItemArmor bp, BlueprintGuid guid)> armors,
            IEnumerable<(BlueprintItemShield bp, BlueprintGuid guid)> shields,
            IEnumerable<(BlueprintItemEquipment bp, BlueprintGuid guid)> accessories)
        {
            Weapons.Clear();
            Armors.Clear();
            Shields.Clear();
            Accessories.Clear();

            var weaponMap = new Dictionary<string, ItemData>();
            var armorMap = new Dictionary<string, ItemData>();
            var shieldMap = new Dictionary<string, ItemData>();

            // 1. SCAN WEAPONS
            foreach (var item in weapons)
            {
                if (item.bp.Type == null) continue;
                string typeName = item.bp.Type.name;
                int level = DetectLevel(item.bp.name, typeName);
                
                if (level == -1) continue; // Pas un item standard/plus

                if (!weaponMap.TryGetValue(typeName, out var data))
                {
                    data = CreateBaseData(item.bp, "Weapon", typeName);
                    weaponMap[typeName] = data;
                }

                if (level == 0)
                {
                    data.Name = item.bp.Name;
                    data.Icon = item.bp.Icon;
                    data.Description = item.bp.Description;
                    data.BaseCost = (int)item.bp.m_Cost;
                }

                data.VariantGuids[level] = item.guid.ToString();
                data.VariantCosts[level] = (int)item.bp.m_Cost;
                data.VariantIcons[level] = item.bp.Icon;
            }
            Weapons.AddRange(weaponMap.Values.OrderBy(x => x.Name));

            // 2. SCAN ARMORS
            foreach (var item in armors)
            {
                if (item.bp.Type == null) continue;
                string typeName = item.bp.Type.name;
                int level = DetectLevel(item.bp.name, typeName);

                if (level == -1) continue;

                if (!armorMap.TryGetValue(typeName, out var data))
                {
                    data = CreateBaseData(item.bp, "Armor", typeName);
                    armorMap[typeName] = data;
                }

                if (level == 0)
                {
                    data.Name = item.bp.Name;
                    data.Icon = item.bp.Icon;
                    data.Description = item.bp.Description;
                    data.BaseCost = (int)item.bp.m_Cost;
                }

                data.VariantGuids[level] = item.guid.ToString();
                data.VariantCosts[level] = (int)item.bp.m_Cost;
                data.VariantIcons[level] = item.bp.Icon;
            }
            Armors.AddRange(armorMap.Values.OrderBy(x => x.Name));

            // 3. SCAN SHIELDS
            foreach (var item in shields)
            {
                if (item.bp.Type == null) continue;
                string typeName = item.bp.Type.name;
                int level = DetectLevel(item.bp.name, typeName);

                if (level == -1) continue;

                if (!shieldMap.TryGetValue(typeName, out var data))
                {
                    data = CreateBaseData(item.bp, "Shield", typeName);
                    shieldMap[typeName] = data;
                }

                if (level == 0)
                {
                    data.Name = item.bp.Name;
                    data.Icon = item.bp.Icon;
                    data.Description = item.bp.Description;
                    data.BaseCost = (int)item.bp.m_Cost;
                }

                data.VariantGuids[level] = item.guid.ToString();
                data.VariantCosts[level] = (int)item.bp.m_Cost;
                data.VariantIcons[level] = item.bp.Icon;
            }
            Shields.AddRange(shieldMap.Values.OrderBy(x => x.Name));

            // 4. SCAN ACCESSORIES (No levels)
            foreach (var item in accessories)
            {
                if (IsBaseOrEnhancementOnly(item.bp) && !item.bp.IsNotable)
                {
                    var data = CreateBaseData(item.bp, "Accessory", "");
                    data.VariantGuids[0] = item.guid.ToString();
                    data.VariantCosts[0] = (int)item.bp.m_Cost;
                    Accessories.Add(data);
                }
            }
            Accessories = Accessories.OrderBy(x => x.Name).ToList();

            Main.ModEntry.Logger.Log($"[ITEM-SCAN] Finalisé. W:{Weapons.Count} A:{Armors.Count} S:{Shields.Count} Acc:{Accessories.Count}");
        }

        private static int DetectLevel(string bpName, string typeName)
        {
            // Nettoyage du type : "CouvertureType" -> "Couverture"
            string baseType = typeName;
            if (typeName.EndsWith("Type", StringComparison.OrdinalIgnoreCase))
            {
                baseType = typeName.Substring(0, typeName.Length - 4);
            }

            // --- RÈGLE POUR +0 ---
            // 1. Format Standard (ex: StandardLongsword)
            // 2. Format Armure Spécifique (ex: CouvertureStandard)
            if (bpName == "Standard" + typeName || bpName == baseType + "Standard") 
                return 0;
            
            // --- RÈGLE POUR +1 à +5 ---
            
            // A. Format "StandartPlus" (avec un 't') - Spécifique aux nouvelles armures
            string standartPattern = "StandartPlus";
            if (bpName.Contains(standartPattern))
            {
                int tIdx = bpName.IndexOf(standartPattern, StringComparison.OrdinalIgnoreCase);
                string prefix = bpName.Substring(0, tIdx);
                if (prefix == baseType)
                {
                    string levelStr = bpName.Substring(tIdx + standartPattern.Length);
                    if (int.TryParse(levelStr, out int level) && level >= 1 && level <= 5)
                        return level;
                }
            }

            // B. Format classique "Plus" (ex: StandardLongswordPlus1 ou LongswordPlus1)
            string plusPattern = "Plus";
            if (bpName.Contains(plusPattern))
            {
                int pIdx = bpName.IndexOf(plusPattern, StringComparison.OrdinalIgnoreCase);
                string prefix = bpName.Substring(0, pIdx);
                
                if (prefix == "Standard" + typeName || prefix == typeName || prefix == baseType + "Standard")
                {
                    string levelStr = bpName.Substring(pIdx + plusPattern.Length);
                    if (int.TryParse(levelStr, out int level) && level >= 1 && level <= 5)
                        return level;
                }
            }

            return -1;
        }

        private static bool IsBaseOrEnhancementOnly(BlueprintItem bp)
        {
            if (bp == null) return false;

            // On évite les items techniques ou de l'armée
            string name = bp.name.ToLower();
            if (name.Contains("placeholder") || name.Contains("test") || name.Contains("broken") || name.Contains("internal")) return false;
            if (name.Contains("army") || name.Contains("croisade")) return false;
            
            if (bp.m_DisplayNameText == null || string.IsNullOrEmpty(bp.m_DisplayNameText.ToString())) return false;
            if (bp.m_Cost < 1) return false;

            return true;
        }

        private static ItemData CreateBaseData(BlueprintItem bp, string cat, string typeName)
        {
            string cleanName = bp.Name;
            // Nettoyage du suffixe +N pour le nom de base
            int plusIdx = cleanName.LastIndexOf(" +");
            if (plusIdx != -1) cleanName = cleanName.Substring(0, plusIdx);

            return new ItemData
            {
                Name = cleanName,
                BaseCost = (int)bp.m_Cost,
                Category = cat,
                Icon = bp.Icon,
                Description = bp.Description,
                TypeName = typeName
            };
        }
    }
}
