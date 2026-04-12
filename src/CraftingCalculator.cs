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
            
            if (bp == null) {
                // Main.ModEntry.Logger.Warning($"[DEBUG] IsPureEnhancement: Blueprint INTROUVABLE pour le GUID {data.Guid}");
                return false;
            }

            return IsPureEnhancement(bp);
        }

        // ====================================================================

        public static long GetEnchantmentCost(ItemEntity item, EnchantmentData newEnchant = null, float costMultiplier = 1.0f)
        {
            if (item == null) return 0;

            // 1. Cas particulier : Le nouvel enchantement a un coût fixe imposé (ex: Ombre, Résistance)
            if (newEnchant != null && newEnchant.GoldOverride >= 0)
            {
                return (long)(newEnchant.GoldOverride * costMultiplier);
            }

            // 2. Détermination du facteur de base selon le type d'objet
            int factor;
            switch (item.Blueprint)
            {
                case BlueprintItemWeapon:
                    factor = WEAPON_BASE_FACTOR;
                    break;
                case BlueprintItemArmor:
                    factor = ARMOR_BASE_FACTOR;
                    break;
                default:
                    factor = 1000; // Objets merveilleux
                    break;
            }

            // 3. Calcul du prix de marché actuel de l'objet (Unifié : JSON > Blueprint)
            int currentBonus = CalculateDisplayedEnchantmentPoints(item);
            long currentMarketPrice = (long)currentBonus * currentBonus * factor;

            // 4. Si aucun nouvel enchantement n'est fourni, on retourne simplement le prix de marché
            if (newEnchant == null)
            {
                return currentMarketPrice;
            }

            // 5. Si on ajoute un enchantement, on calcule le nouveau prix total
            int newTotalBonus = currentBonus + newEnchant.PointCost;
            long newMarketPrice = (long)newTotalBonus * newTotalBonus * factor;

            // 6. Règle de craft Pathfinder : (Nouveau Prix - Ancien Prix) / 2
            return (long)(((newMarketPrice - currentMarketPrice) / 2) * costMultiplier);
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
                            return "Un équipement doit posséder (ou recevoir simultanément) une 'Altération' avant de recevoir des enchantements spéciaux.";
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

            // 2. Est-ce que le jeu considère que ça coûte 0 point ? (ex: Adamantium, Mithral)
            if (bp.EnchantmentCost == 0 || GetEnhancementValue(bp) > 0) return true;

            // On respecte la règle : Priorité au JSON si présent
            int pointCost = data.PointCost; 
            
            // Si l'enchantement n'est pas dans le JSON (donc découvert par scan), pointCost sera 0 par défaut.
            // Dans ce cas précis, on utilise la valeur du Blueprint.
            var isInJson = EnchantmentScanner.MasterList.Any(d => string.Equals(d.Guid, data.Guid, StringComparison.OrdinalIgnoreCase));
            if (!isInJson)
            {
                pointCost = bp.EnchantmentCost;
            }

            return pointCost == 1 && IsPureEnhancement(bp);
        }
    }
}