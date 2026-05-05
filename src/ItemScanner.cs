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
        public string Guid;
        public string Name;
        public int Cost;
        public string Category;
        public Sprite Icon;
        public string Description;
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

            // Filtrage : On ne garde que les items "base" (pas d'enchantements)
            // On exclut aussi les items nommés "Placeholder", "Test" ou avec des GUIDs suspects si nécessaire

            foreach (var item in weapons)
            {
                if (item.bp.Type == null) continue;
                string typeName = item.bp.Type.name;
                bool isStandardName = item.bp.name == "Standard" + typeName || item.bp.name == typeName;
                
                if (isStandardName && item.bp.Enchantments.Count == 0 && IsBaseItem(item.bp))
                    Weapons.Add(CreateData(item.bp, item.guid, "Weapon"));
            }

            foreach (var item in armors)
            {
                if (item.bp.Type == null) continue;
                string typeName = item.bp.Type.name;
                bool isStandardName = item.bp.name == "Standard" + typeName || item.bp.name == typeName;

                if (isStandardName && item.bp.Enchantments.Count == 0 && IsBaseItem(item.bp))
                    Armors.Add(CreateData(item.bp, item.guid, "Armor"));
            }

            foreach (var item in shields)
            {
                if (item.bp.Type == null) continue;
                string typeName = item.bp.Type.name;
                bool isStandardName = item.bp.name == "Standard" + typeName || item.bp.name == typeName;

                if (isStandardName && item.bp.Enchantments.Count == 0 && IsBaseItem(item.bp))
                    Shields.Add(CreateData(item.bp, item.guid, "Shield"));
            }

            foreach (var item in accessories)
            {
                if (item.bp.Enchantments.Count == 0 && IsBaseItem(item.bp) && !item.bp.IsNotable)
                    Accessories.Add(CreateData(item.bp, item.guid, "Accessory"));
            }


            Main.ModEntry.Logger.Log($"[ITEM-SCAN] Finalisé. W:{Weapons.Count} A:{Armors.Count} S:{Shields.Count} Acc:{Accessories.Count}");
        }

        public static bool IsBaseItem(BlueprintItem bp)
        {
            return GetBaseItemError(bp) == null;
        }

        public static string GetBaseItemError(BlueprintItem bp)
        {
            if (bp == null) return "NullBP";

            // --- TEST DES ENCHANTEMENTS ---
            var enchants = bp.Enchantments;
            if (enchants != null)
            {
                foreach (var enchRef in enchants)
                {
                    var ench = enchRef;
                    if (ench == null) continue;

                    // On autorise UNIQUEMENT le Masterwork
                    bool isMasterwork = ench.name != null && ench.name.ToLower().Contains("masterwork");
                    if (!isMasterwork) return $"MagicEnch:{ench.name ?? "Unknown"}";
                }
            }

            // --- TEST DES COMPOSANTS MAGIQUES DIRECTS ---
            var comps = bp.Components;
            if (comps != null)
            {
                foreach (var comp in comps)
                {
                    if (comp == null) continue;
                    string cName = comp.GetType().Name;
                    if (cName.Contains("EnhancementBonus") || 
                        cName.Contains("WeaponSpecialAbility") ||
                        cName.Contains("AddDamage") ||
                        cName.Contains("ArmorBonusBlueprints")) 
                        return $"MagicComp:{cName}";
                }
            }

            // --- FILTRAGE SPÉCIFIQUE AUX ARMES ---
            if (bp is BlueprintItemWeapon weapon)
            {
                if (weapon.Type == null) return "InvalidType";
                if (weapon.IsNatural || weapon.Type.IsNatural) return "Natural";
                
                var atkType = weapon.AttackType;
                if (atkType == Kingmaker.RuleSystem.AttackType.Touch || atkType == Kingmaker.RuleSystem.AttackType.RangedTouch) return "Touch/Ray";

                var cat = weapon.Category;
                if (cat == Kingmaker.Enums.WeaponCategory.Touch ||
                    cat == Kingmaker.Enums.WeaponCategory.Ray ||
                    cat == Kingmaker.Enums.WeaponCategory.Bomb ||
                    cat == Kingmaker.Enums.WeaponCategory.KineticBlast) return "TechnicalCategory";

                if (weapon.Icon == null) return "NoIcon";
            }

            // --- FILTRAGE NOM/MÉTADONNÉES ---
            string name = bp.name.ToLower();
            if (name.Contains("placeholder") || name.Contains("test") || name.Contains("broken") || name.Contains("internal")) return "TechnicalName";
            if (name.Contains("army") || name.Contains("croisade")) return "ArmyItem";
            if (name.Contains("standard") && name.Contains("natural")) return "NaturalStandard";

            if (bp.m_DisplayNameText == null || string.IsNullOrEmpty(bp.m_DisplayNameText.ToString())) return "NoDisplayName";

            // On évite les items qui n'ont pas de coût réel (souvent des items techniques)
            if (bp.m_Cost <= 1) return "LowCost";

            return null; // OK
        }

        private static ItemData CreateData(BlueprintItem bp, BlueprintGuid guid, string cat)
        {
            return new ItemData
            {
                Guid = guid.ToString(),
                Name = bp.Name,
                Cost = bp.m_Cost,
                Category = cat,
                Icon = bp.Icon,
                Description = bp.Description
            };
        }
    }
}
