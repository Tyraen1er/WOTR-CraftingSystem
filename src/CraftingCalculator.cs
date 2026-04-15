using System;
using System.Linq;
using System.Collections.Generic;
using Kingmaker;
using Kingmaker.Items;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Items.Ecnchantments;
using Kingmaker.Blueprints.Items.Weapons;
using Kingmaker.Blueprints.Items.Armors;
using Kingmaker.Designers.Mechanics.Facts;

//
// CraftingCalculator.cs
//

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
            
            // Approche robuste : On scanne les noms des composants pour éviter les problèmes de versions/types
            foreach (var c in bp.Components)
            {
                if (c == null) continue;
                string typeName = c.GetType().Name;
                
                // Egalité stricte pour exclure les Bane (WeaponConditionalEnhancementBonus)
                if (typeName == "WeaponEnhancementBonus" || typeName == "ArmorEnhancementBonus")
                {
                    return true;
                }
            }
            return false;
        }

        public static int GetEnhancementValue(BlueprintItemEnchantment bp)
        {
            if (bp == null) return 0;
            
            foreach (var c in bp.Components)
            {
                if (c == null) continue;
                var type = c.GetType();
                
                // On cherche dynamiquement les champs EnhancementBonus (Armes) ou EnhancementValue (Armures)
                // Cela fonctionne même si la classe est une variante (ex: WeaponConditionalEnhancementBonus)
                var field = type.GetField("EnhancementBonus") ?? type.GetField("EnhancementValue");
                if (field != null)
                {
                    try {
                        return (int)field.GetValue(c);
                    } catch { }
                }
            }
            return 0;
        }

        public static bool IsPureEnhancement(EnchantmentData data)
        {
            if (data == null || string.IsNullOrEmpty(data.Guid)) return false;
            var bp = ResourcesLibrary.TryGetBlueprint<BlueprintItemEnchantment>(BlueprintGuid.Parse(data.Guid));
            
            /*
            if (bp == null) {
                Main.ModEntry.Logger.Warning($"[DEBUG] IsPureEnhancement: Blueprint INTROUVABLE pour le GUID {data.Guid}");
                return false;
            }
            */

            return IsPureEnhancement(bp);
        }

        // ====================================================================

        public static long GetEnchantmentCost(ItemEntity item, EnchantmentData newEnchant = null, float costMultiplier = 1.0f)
        {
            return GetMarginalCost(item, new List<EnchantmentData>(), newEnchant, costMultiplier);
        }

        public static long GetMarginalCost(ItemEntity item, IEnumerable<EnchantmentData> queuedEnchants, EnchantmentData nextEnchant = null, float costMultiplier = 1.0f)
        {
            if (item == null) return 0;

            // 1. Si on demande le prix TOTAL d'un panier (Calcul récursif marginal pour la précision)
            if (nextEnchant == null)
            {
                long basketTotal = 0;
                var currentQueue = new List<EnchantmentData>();
                if (queuedEnchants != null)
                {
                    foreach (var enchant in queuedEnchants)
                    {
                        basketTotal += GetMarginalCost(item, currentQueue, enchant, costMultiplier);
                        currentQueue.Add(enchant);
                    }
                }
                return basketTotal;
            }

            // 2. Cas particulier : Coût fixe
            if (nextEnchant.GoldOverride >= 0)
            {
                long fixedCost = (long)(nextEnchant.GoldOverride * costMultiplier);
                if (CraftingSettings.EnableEpicCosts && nextEnchant.IsEpic) fixedCost *= 10;
                return fixedCost;
            }

            // 3. Facteur de base (Objet ou Surcharge JSON)
            int factor;
            if (nextEnchant.PriceFactor > 0)
            {
                factor = nextEnchant.PriceFactor;
            }
            else
            {
                switch (item.Blueprint)
                {
                    case BlueprintItemWeapon: factor = WEAPON_BASE_FACTOR; break;
                    case BlueprintItemArmor: factor = ARMOR_BASE_FACTOR; break;
                    default: factor = 1000; break;
                }
            }

            // 4. Calcul du bonus (Objet + Panier)
            int initialBonus = CalculateDisplayedEnchantmentPoints(item);
            int basketBonus = queuedEnchants != null ? queuedEnchants.Sum(e => e.PointCost) : 0;
            int totalBefore = initialBonus + basketBonus;

            // 5. Calcul marginal (Nouveau Prix - Prix Panier)
            int totalAfter = totalBefore + nextEnchant.PointCost;
            long priceWithBasket = (long)totalBefore * totalBefore * factor;
            long priceWithNext = (long)totalAfter * totalAfter * factor;

            long marginalCost = (long)((priceWithNext - priceWithBasket) * costMultiplier);

            // 6. Multiplicateur Épique
            if (CraftingSettings.EnableEpicCosts && nextEnchant.IsEpic)
            {
                marginalCost *= 10;
            }

            return marginalCost;
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
                // Sécurité : On ignore les bonus temporaires (sorts, capacités) pour les prérequis de craft permanent
                if (e.IsTemporary) continue;

                if (IsPureEnhancement(e.Blueprint))
                {
                    // On récupère la valeur réelle du composant (+1, +2...)
                    int val = GetEnhancementValue(e.Blueprint);
                    currentEnhancement = Math.Max(currentEnhancement, val);
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
                            return Helpers.GetString("err_require_plus_one", "Un équipement doit posséder (ou recevoir simultanément) une 'Altération' (+1) avant de recevoir des enchantements spéciaux.");
                        }
                    }
                }
            }

            int addedPoints = selectedList.Sum(d => d.PointCost);
            if (CraftingSettings.EnforcePointsLimit && currentPoints + addedPoints > CraftingSettings.MaxTotalBonus)
                return string.Format(Helpers.GetString("err_limit_total", "Limite de puissance totale dépassée (max +{0})."), CraftingSettings.MaxTotalBonus);

            int selectedMaxEnh = 0;
            foreach (var d in selectedList)
            {
                if (IsPureEnhancement(d))
                    selectedMaxEnh = Math.Max(selectedMaxEnh, d.PointCost);
            }
            if (CraftingSettings.EnforcePointsLimit && Math.Max(currentEnhancement, selectedMaxEnh) > CraftingSettings.MaxEnhancementBonus)
                return string.Format(Helpers.GetString("err_limit_enhancement", "Limite d'altération pure dépassée (max +{0})."), CraftingSettings.MaxEnhancementBonus);

            return null;
        }

        /// <summary>
        /// Calcule le bonus total actuel de l'objet (ex: +3) en respectant ta règle :
        /// Priorité absolue au JSON si l'enchantement y est présent.
        /// </summary>
        public static int CalculateDisplayedEnchantmentPoints(ItemEntity item)
        {
            if (item == null) return 0;
            int points = 0;
            foreach (var e in item.Enchantments)
            {
                // On ignore les bonus temporaires pour le calcul du niveau réel
                if (e.IsTemporary) continue;

                string guid = e.Blueprint.AssetGuid.ToString();
                var overrideData = EnchantmentScanner.GetByGuid(guid);
                
                // Si l'enchantement est dans le JSON, on prend sa valeur PointCost (qui est 0 pour les prix fixes)
                // Sinon on prend la valeur native du jeu.
                int bonusValue = (overrideData != null) ? overrideData.PointCost : e.Blueprint.EnchantmentCost;
                
                if (bonusValue > 0) points += bonusValue;
            }
            return points;
        }

        // ====================================================================
        // RÈGLES D'AFFICHAGE DYNAMIQUE (UI)
        // ====================================================================
        
        public static bool IsItemReadyForSpecialEnchants(ItemEntity item, IEnumerable<EnchantmentData> queuedEnchants)
        {
            if (item == null) return false;

            bool hasEnhancementOriginally = item.Enchantments.Any(e => !e.IsTemporary && IsPureEnhancement(e.Blueprint));
            bool queueHasEnhancement = queuedEnchants != null && queuedEnchants.Any(d => IsPureEnhancement(d) && d.PointCost > 0);

            // Main.ModEntry.Logger.Log($"[DEBUG] IsItemReadyForSpecialEnchants: {item.Name} -> hasOriginal={hasEnhancementOriginally}, hasInQueue={queueHasEnhancement}");

            // Débloque tous les enchantements spéciaux instantanément si un +1 est coché
            return hasEnhancementOriginally || queueHasEnhancement;
        }

        public static bool IsEnchantmentAllowedOnNormalItem(EnchantmentData data)
        {
            if (data == null || string.IsNullOrEmpty(data.Guid)) return false;
            
            var bp = ResourcesLibrary.TryGetBlueprint<BlueprintItemEnchantment>(BlueprintGuid.Parse(data.Guid));
            if (bp == null) return false;

            // 1. Matériaux (via catégories du JSON)
            bool isMaterial = data.Categories != null && data.Categories.Contains("Material", StringComparer.OrdinalIgnoreCase);
            if (isMaterial) return true;

            // 2. Altérations pures (+1, +2, +3...)
            if (IsPureEnhancement(bp)) return true;

            return false;
        }
    }
}