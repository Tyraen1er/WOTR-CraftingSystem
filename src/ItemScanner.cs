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
                if (item.bp.Enchantments.Count == 0 && IsBaseItem(item.bp))
                    Weapons.Add(CreateData(item.bp, item.guid, "Weapon"));
            }

            foreach (var item in armors)
            {
                if (item.bp.Enchantments.Count == 0 && IsBaseItem(item.bp))
                    Armors.Add(CreateData(item.bp, item.guid, "Armor"));
            }

            foreach (var item in shields)
            {
                if (item.bp.Enchantments.Count == 0 && IsBaseItem(item.bp))
                    Shields.Add(CreateData(item.bp, item.guid, "Shield"));
            }

            foreach (var item in accessories)
            {
                // Pour les accessoires, c'est plus large (bagues, amulettes, etc.)
                // On évite les items de quête ou uniques
                if (item.bp.Enchantments.Count == 0 && IsBaseItem(item.bp) && !item.bp.IsNotable)
                    Accessories.Add(CreateData(item.bp, item.guid, "Accessory"));
            }


            Main.ModEntry.Logger.Log($"[ITEM-SCAN] Finalisé. W:{Weapons.Count} A:{Armors.Count} S:{Shields.Count} Acc:{Accessories.Count}");
        }

        private static bool IsBaseItem(BlueprintItem bp)
        {
            if (bp == null) return false;
            
            // On exclut les items déjà enchantés
            if (bp.Enchantments.Count > 0) return false;

            // --- FILTRAGE SPÉCIFIQUE AUX ARMES ---
            if (bp is BlueprintItemWeapon weapon)
            {
                // On exclut les armes naturelles (morsures, griffes, etc.)
                if (weapon.IsNatural || (weapon.Type != null && weapon.Type.IsNatural)) return false;

                // On exclut les attaques de contact et les rayons (Touch/RangedTouch)
                var atkType = weapon.AttackType;
                if (atkType == Kingmaker.RuleSystem.AttackType.Touch || atkType == Kingmaker.RuleSystem.AttackType.RangedTouch) return false;

                // On exclut les catégories techniques ou de contact
                var cat = weapon.Category;
                if (cat == Kingmaker.Enums.WeaponCategory.Touch || 
                    cat == Kingmaker.Enums.WeaponCategory.Ray || 
                    cat == Kingmaker.Enums.WeaponCategory.Bomb || 
                    cat == Kingmaker.Enums.WeaponCategory.Spike ||
                    cat == Kingmaker.Enums.WeaponCategory.KineticBlast) return false;

                // On exclut les armes sans icône (items purement techniques)
                if (weapon.Icon == null) return false;
            }

            string name = bp.name.ToLower();
            if (name.Contains("placeholder") || name.Contains("test") || name.Contains("broken") || name.Contains("internal")) return false;
            
            // On évite les doublons d'items spécifiques qui sont souvent des variantes techniques
            if (name.Contains("standard") && name.Contains("natural")) return false;
            if (name.Contains("rayon") || name.Contains("beam") || name.Contains("bombe") || name.Contains("bomb")) return false;

            if (bp.m_DisplayNameText == null || string.IsNullOrEmpty(bp.m_DisplayNameText.ToString())) return false;
            
            // On évite les items qui n'ont pas de coût réel (souvent des items techniques)
            if (bp.m_Cost <= 1) return false; // Un item à 0 ou 1 po est rarement une vraie arme de forge

            return true;
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
