using System;
using System.Linq;
using System.Collections.Generic;
using Kingmaker;
using Kingmaker.Items;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Items.Ecnchantments;
using Kingmaker.Blueprints.Items.Weapons;
using Kingmaker.Blueprints.Items.Armors;

namespace CraftingSystem
{
    public static class CraftingCalculator
    {
        public const int WEAPON_BASE_FACTOR = 2000;
        public const int ARMOR_BASE_FACTOR = 1000;

        // ====================================================================
        // IDENTIFICATION DYNAMIQUE (MOTEUR DU JEU)
        // ====================================================================
        
        /// <summary>
        /// Vérifie si le Blueprint est une altération PURE en regardant ses composants internes,
        /// sans jamais lire son nom. Ignore automatiquement les effets spéciaux (Flaming, Agile...).
        /// </summary>
        public static bool IsPureEnhancement(BlueprintItemEnchantment bp)
        {
            if (bp == null) return false;
            return bp.ComponentsArray.Any(c => 
                c.GetType().Name == "WeaponEnhancementBonus" || 
                c.GetType().Name == "ArmorEnhancementBonus"
            );
        }

        public static bool IsPureEnhancement(EnchantmentData data)
        {
            if (data == null || string.IsNullOrEmpty(data.Guid)) return false;
            var bp = ResourcesLibrary.TryGetBlueprint<BlueprintItemEnchantment>(BlueprintGuid.Parse(data.Guid));
            return IsPureEnhancement(bp);
        }

        // ====================================================================

        public static long GetMarketPrice(ItemEntity item)
        {
            if (item == null) return 0;
            int totalBonus = item.Enchantments.Sum(e => e.Blueprint.EnchantmentCost);
            bool isWeapon = item.Blueprint is BlueprintItemWeapon;
            int factor = isWeapon ? WEAPON_BASE_FACTOR : ARMOR_BASE_FACTOR;
            return (long)totalBonus * totalBonus * factor;
        }

        public static long GetUpgradeCost(ItemEntity item, EnchantmentData newEnchant, float costMultiplier = 1.0f)
        {
            if (item == null || newEnchant == null) return 0;
            if (newEnchant.GoldOverride >= 0) return (long)(newEnchant.GoldOverride * costMultiplier);

            int currentBonus = item.Enchantments.Sum(e => e.Blueprint.EnchantmentCost);
            int newTotalBonus = currentBonus + newEnchant.PointCost;

            bool isWeapon = item.Blueprint is BlueprintItemWeapon;
            int factor = isWeapon ? WEAPON_BASE_FACTOR : ARMOR_BASE_FACTOR;

            long currentMarketPrice = (long)currentBonus * currentBonus * factor;
            long newMarketPrice = (long)newTotalBonus * newTotalBonus * factor;

            return (long)((newMarketPrice - currentMarketPrice) / 2 * costMultiplier);
        }

        public static int GetCraftingDays(long gpCost, bool instant = false)
        {
            if (instant || gpCost <= 0) return 0;
            return Math.Max(1, (int)Math.Ceiling(gpCost / 1000.0));
        }

        public static string FormatRemainingTime(long remainingTicks)
        {
            if (remainingTicks <= 0) return "Terminé";
            TimeSpan ts = TimeSpan.FromTicks(remainingTicks);
            if (ts.Days > 0 && ts.Hours > 0) return $"{ts.Days} jour(s) et {ts.Hours} heure(s)";
            if (ts.Days > 0) return $"{ts.Days} jour(s)";
            if (ts.Hours > 0) return $"{ts.Hours} heure(s)";
            return "Moins d'une heure";
        }

        public static string ValidateSelectionBeforeStart(ItemEntity item, List<EnchantmentData> selectedList, long totalCost)
        {
            if (item == null || selectedList == null || selectedList.Count == 0) return "Aucun enchantement sélectionné.";
            if (Game.Instance.Player.Money < totalCost) return "Vous n'avez pas assez d'or pour lancer tous les projets sélectionnés.";

            int currentPoints = CalculateDisplayedEnchantmentPoints(item);
            int currentEnhancement = 0;
            
            foreach (var e in item.Enchantments)
            {
                if (IsPureEnhancement(e.Blueprint))
                {
                    string guid = e.Blueprint.AssetGuid.ToString();
                    var overrideData = EnchantmentScanner.GetByGuid(guid);
                    // On utilise la valeur du JSON en priorité
                    int p = overrideData?.PointCost ?? e.Blueprint.EnchantmentCost;
                    currentEnhancement = Math.Max(currentEnhancement, p);
                }
            }

            bool hasEnhancementOriginally = currentEnhancement > 0;
            bool queueAddsEnhancement = selectedList.Any(d => IsPureEnhancement(d) && d.PointCost > 0);
            
            bool isWeaponOrArmor = item.Blueprint is BlueprintItemWeapon || item.Blueprint is BlueprintItemArmor;

            if (CraftingSettings.RequirePlusOneFirst && isWeaponOrArmor)
            {
                // Si l'objet n'a pas déjà de +1 ET qu'on ne l'a pas mis dans le "panier" actuel
                if (!hasEnhancementOriginally && !queueAddsEnhancement)
                {
                    foreach (var selected in selectedList)
                    {
                        if (!IsEnchantmentAllowedOnNormalItem(selected))
                        {
                            return "Un équipement doit posséder (ou recevoir simultanément) une 'Altération pure' avant de recevoir des enchantements spéciaux.";
                        }
                    }
                }
            }

            int addedPoints = selectedList.Sum(d => d.PointCost);
            if (CraftingSettings.EnforcePointsLimit && currentPoints + addedPoints > CraftingSettings.MaxTotalBonus)
                return $"Limite de puissance totale dépassée (max +{CraftingSettings.MaxTotalBonus}).";

            int selectedMaxEnh = 0;
            foreach (var d in selectedList)
            {
                if (IsPureEnhancement(d))
                    selectedMaxEnh = Math.Max(selectedMaxEnh, d.PointCost);
            }
            if (CraftingSettings.EnforcePointsLimit && Math.Max(currentEnhancement, selectedMaxEnh) > CraftingSettings.MaxEnhancementBonus)
                return $"Limite d'altération pure dépassée (max +{CraftingSettings.MaxEnhancementBonus}).";

            return null;
        }

        public static int CalculateDisplayedEnchantmentPoints(ItemEntity item)
        {
            if (item == null) return 0;
            int points = 0;
            foreach (var e in item.Enchantments)
            {
                string guid = e.Blueprint.AssetGuid.ToString();
                var overrideData = EnchantmentScanner.GetByGuid(guid);
                
                // Exécution stricte de tes consignes : On ne prend plus que la valeur réelle (JSON ou Jeu),
                // sans aucun multiplicateur arbitraire basé sur le nom.
                int baseCost = overrideData?.PointCost ?? e.Blueprint.EnchantmentCost;
                if (baseCost > 0) points += baseCost;
            }
            return points;
        }

        // ====================================================================
        // RÈGLES D'AFFICHAGE DYNAMIQUE (UI)
        // ====================================================================
        
        public static bool IsItemReadyForSpecialEnchants(ItemEntity item, IEnumerable<EnchantmentData> queuedEnchants)
        {
            if (item == null) return false;

            bool hasEnhancementOriginally = item.Enchantments.Any(e => IsPureEnhancement(e.Blueprint));
            bool queueHasEnhancement = queuedEnchants != null && queuedEnchants.Any(d => IsPureEnhancement(d) && d.PointCost > 0);

            // Débloque tous les enchantements spéciaux instantanément si un +1 est coché
            return hasEnhancementOriginally || queueHasEnhancement;
        }

        public static bool IsEnchantmentAllowedOnNormalItem(EnchantmentData data)
        {
            if (data == null || string.IsNullOrEmpty(data.Guid)) return false;
            
            var bp = ResourcesLibrary.TryGetBlueprint<BlueprintItemEnchantment>(BlueprintGuid.Parse(data.Guid));
            if (bp == null) return false;

            // 1. Est-ce un coût fixe en or défini dans ton JSON ?
            if (data.GoldOverride >= 0) return true;

            // 2. Est-ce que le jeu considère que ça coûte 0 point ? (ex: Adamantium, Mithral)
            if (bp.EnchantmentCost == 0) return true;

            // 3. Si on arrive ici, on vérifie si c'est une altération +1 pure
            // On prend la valeur du JSON s'il y en a une (supérieure à 0), sinon on lit le Blueprint
            int pointCost = data.PointCost > 0 ? data.PointCost : bp.EnchantmentCost;

            return pointCost == 1 && IsPureEnhancement(bp);
        }
    }
}