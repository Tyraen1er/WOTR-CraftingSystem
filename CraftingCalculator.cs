using System;
using System.Linq;
using Kingmaker.Items;
using Kingmaker.Blueprints.Items.Ecnchantments;

namespace CraftingSystem
{
    public static class CraftingCalculator
    {
        public const int WEAPON_BASE_FACTOR = 2000;
        public const int ARMOR_BASE_FACTOR = 1000;

        /// <summary>
        /// Calcule le prix du marché total d'un objet basé sur ses enchantements actuels.
        /// </summary>
        public static long GetMarketPrice(ItemEntity item)
        {
            if (item == null) return 0;

            int totalBonus = item.Enchantments.Sum(e => e.Blueprint.EnchantmentCost);
            bool isWeapon = item.Blueprint is Kingmaker.Blueprints.Items.Weapons.BlueprintItemWeapon;
            bool isArmor = item.Blueprint is Kingmaker.Blueprints.Items.Armors.BlueprintItemArmor;

            int factor = isWeapon ? WEAPON_BASE_FACTOR : ARMOR_BASE_FACTOR;

            // Formule PF1e : (Bonus^2) * Facteur
            long bonusPrice = (long)totalBonus * totalBonus * factor;

            // On ajoute les coûts fixes (ex: matériaux spéciaux comme l'Adamantine, non gérés ici pour l'instant)
            return bonusPrice;
        }

        /// <summary>
        /// Calcule le coût spécifique pour ajouter un nouvel enchantement.
        /// </summary>
        public static long GetUpgradeCost(ItemEntity item, EnchantmentData newEnchant, float costMultiplier = 1.0f)
        {
            if (item == null || newEnchant == null) return 0;

            // Si l'enchantement a un coût fixe en PO (ex: Slick, Shadow), on utilise l'override du JSON
            if (newEnchant.GoldOverride >= 0) return (long)(newEnchant.GoldOverride * costMultiplier);

            int currentBonus = item.Enchantments.Sum(e => e.Blueprint.EnchantmentCost);
            int newTotalBonus = currentBonus + newEnchant.PointCost;

            bool isWeapon = item.Blueprint is Kingmaker.Blueprints.Items.Weapons.BlueprintItemWeapon;
            int factor = isWeapon ? WEAPON_BASE_FACTOR : ARMOR_BASE_FACTOR;

            long currentMarketPrice = (long)currentBonus * currentBonus * factor;
            long newMarketPrice = (long)newTotalBonus * newTotalBonus * factor;

            // Prix d'amélioration = (Nouveau Prix - Ancien Prix) / 2 (Règle artisanat 50%)
            long baseUpgradeCost = (newMarketPrice - currentMarketPrice) / 2;

            return (long)(baseUpgradeCost * costMultiplier);
        }

        /// <summary>
        /// Calcule le temps de travail nécessaire (1 jour par 1000 po de coût de fabrication)
        /// </summary>
        public static int GetCraftingDays(long gpCost, bool instant = false)
        {
            // Si le cheat de craft instantané est actif (ou si le coût est 0), ça prend 0 jour.
            if (instant || gpCost <= 0) return 0;

            // On divise le prix par 1000. 
            // Math.Ceiling permet d'arrondir à l'entier supérieur (ex: 1250 po -> 2 jours).
            int days = (int)Math.Ceiling(gpCost / 1000.0);

            // On s'assure qu'un objet coûteux prend au minimum 1 jour de travail
            return Math.Max(1, days);
        }
    }
}
