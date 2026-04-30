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
using System.Text.RegularExpressions;

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
            if (data == null) return false;
            
            // Priorité au flag explicite (calculé lors du scan ou de la résolution dynamique)
            if (data.IsEnhancement) return true;

            if (string.IsNullOrEmpty(data.Guid)) return false;
            var bp = ResourcesLibrary.TryGetBlueprint<BlueprintItemEnchantment>(BlueprintGuid.Parse(data.Guid));
            return IsPureEnhancement(bp);
        }

        /// <summary>
        /// Extrait la "famille" de l'enchantement en supprimant tous les chiffres de son nom interne.
        /// Exemples : "ArmorBonus5" -> "ArmorBonus", "AcidResistance10Enchant" -> "AcidResistanceEnchant"
        /// </summary>
        public static string GetEnchantmentFamily(string blueprintName)
        {
            if (string.IsNullOrEmpty(blueprintName)) return string.Empty;
            // On retire tous les chiffres présents dans le nom (ex: AcidResistance10Enchant -> AcidResistanceEnchant)
            return Regex.Replace(blueprintName, @"\d+", "");
        }

        public static long GetEnchantmentCost(ItemEntity item, EnchantmentData newEnchant = null, float costMultiplier = 1.0f)
        {
            return GetMarginalCost(item, new List<EnchantmentData>(), newEnchant, costMultiplier);
        }

        /// <summary>
        /// Calcule le coût marginal en pièces d'or pour ajouter un enchantement spécifique à un objet, 
        /// ou le coût total d'une file d'attente d'enchantements.
        /// </summary>
        /// <remarks>
        /// Cette méthode implémente les règles de calcul suivantes :
        /// <list type="bullet">
        /// <item><description><b>Calcul Marginal :</b> Facture uniquement la différence de prix entre l'état actuel (item + panier) et l'état final.</description></item>
        /// <item><description><b>Système d'Upgrade :</b> Détecte via <see cref="GetEnchantmentFamily"/> si un enchantement de même type est déjà présent pour ne facturer que la différence (ex: passer d'une Résistance 10 à 30).</description></item>
        /// <item><description><b>Pénalité de Slot (+50%) :</b> Appliquée si l'enchantement est posé sur un <c>ItemType</c> non listé dans ses slots autorisés.</description></item>
        /// <item><description><b>Capacités Multiples (+50%) :</b> Appliquée sur les Objets Merveilleux possédant déjà au moins un enchantement permanent ou en attente.</description></item>
        /// <item><description><b>Coûts Épiques :</b> Appliqués en fin de calcul si l'enchantement est marqué comme <c>IsEpic</c> (multiplicateur configurable).</description></item>
        /// </list>
        /// </remarks>
        /// <param name="item">L'entité de l'objet à enchanter.</param>
        /// <param name="queuedEnchants">Liste des enchantements déjà présents dans le panier de modification.</param>
        /// <param name="nextEnchant">L'enchantement à chiffrer. Si <c>null</c>, la méthode calcule récursivement le total de <paramref name="queuedEnchants"/>.</param>
        /// <param name="costMultiplier">Multiplicateur global de prix (issu des réglages du mod).</param>
        /// <returns>Le montant en pièces d'or à payer pour l'opération.</returns>
        public static long GetMarginalCost(ItemEntity item, IEnumerable<EnchantmentData> queuedEnchants, EnchantmentData nextEnchant = null, float costMultiplier = 1.0f)
        {
            if (item == null) return 0;

            // 1. Calcul récursif pour le prix TOTAL d'un panier
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

            // --- CALCUL DES MULTIPLICATEURS DE PÉNALITÉ ---
            float slotMultiplier = (CraftingSettings.ApplySlotPenalty && IsWrongSlot(item, nextEnchant)) ? 1.5f : 1.0f;
            float wondrousMultiplier = (IsWondrousItem(item) && HasMultipleAbilities(item, queuedEnchants)) ? 1.5f : 1.0f;
            float totalPenaltyMultiplier = slotMultiplier * wondrousMultiplier;

            // --- LOGIQUE DE REMPLACEMENT (UPGRADE FAMILLE) ---
            int existingPointCost = 0;
            long existingGoldOverride = 0;

            var nextBp = ResourcesLibrary.TryGetBlueprint<BlueprintItemEnchantment>(BlueprintGuid.Parse(nextEnchant.Guid));
            string nextFamily = nextBp != null ? GetEnchantmentFamily(nextBp.name) : string.Empty;

            if (!string.IsNullOrEmpty(nextFamily))
            {
                // A. Chercher dans la file d'attente (panier)
                var replacedInQueue = queuedEnchants?.FirstOrDefault(e => 
                {
                    var bp = ResourcesLibrary.TryGetBlueprint<BlueprintItemEnchantment>(BlueprintGuid.Parse(e.Guid));
                    return bp != null && GetEnchantmentFamily(bp.name) == nextFamily;
                });

                if (replacedInQueue != null)
                {
                    existingPointCost = replacedInQueue.PointCost;
                    existingGoldOverride = replacedInQueue.GoldOverride;
                }
                else
                {
                    // B. Chercher directement sur l'objet
                    var replacedOnItem = item.Enchantments.FirstOrDefault(e => 
                        !e.IsTemporary && GetEnchantmentFamily(e.Blueprint.name) == nextFamily);

                    if (replacedOnItem != null)
                    {
                        string replacedGuid = replacedOnItem.Blueprint.AssetGuid.ToString();
                        var oldData = EnchantmentScanner.GetByGuid(replacedGuid);
                        if (oldData != null) { 
                            existingPointCost = oldData.PointCost; 
                            existingGoldOverride = oldData.GoldOverride; 
                        }
                    }
                }
            }

            // 2. Cas particulier : Coût fixe (GoldOverride)
            if (nextEnchant.GoldOverride >= 0)
            {
                long baseCostToPay = nextEnchant.GoldOverride;
                
                // On déduit le prix de l'enchantement remplacé (s'il y en a un)
                if (existingGoldOverride > 0)
                {
                    baseCostToPay = Math.Max(0, baseCostToPay - existingGoldOverride);
                }

                long fixedCost = (long)(baseCostToPay * costMultiplier);
                
                // Application des pénalités
                fixedCost = (long)(fixedCost * totalPenaltyMultiplier);

                // Multiplicateur Épique
                if (CraftingSettings.EnableEpicCosts && nextEnchant.IsEpic) fixedCost = (long)(fixedCost * CraftingSettings.EpicCostMultiplier);
                
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

            // 4. Calcul du bonus
            int initialBonus = CalculateDisplayedEnchantmentPoints(item);
            int basketBonus = queuedEnchants != null ? queuedEnchants.Sum(e => e.PointCost) : 0;
            int totalBefore = initialBonus + basketBonus;

            // 5. Calcul marginal (Nouveau Prix - Prix Panier) avec déduction
            int pointsToAdd = nextEnchant.PointCost;
            if (existingPointCost > 0)
            {
                pointsToAdd = Math.Max(0, pointsToAdd - existingPointCost);
            }

            int totalAfter = totalBefore + pointsToAdd;
            long priceWithBasket = (long)totalBefore * totalBefore * factor;
            long priceWithNext = (long)totalAfter * totalAfter * factor;

            long marginalCost = (long)((priceWithNext - priceWithBasket) * costMultiplier);

            // Application des pénalités sur le coût marginal
            marginalCost = (long)(marginalCost * totalPenaltyMultiplier);

            // 6. Multiplicateur Épique
            if (CraftingSettings.EnableEpicCosts && nextEnchant.IsEpic)
            {
                marginalCost = (long)(marginalCost * CraftingSettings.EpicCostMultiplier);
            }

            return marginalCost;
        }

        /// <summary>
        /// Vérifie si la catégorie de l'item ("Weapon", "Armor" ou "Other") 
        /// est absente de la liste des emplacements autorisés (EnchantmentData.Slots).
        /// Retourne 'true' si c'est le mauvais emplacement (déclenche la pénalité de +50%).
        /// </summary>
        public static bool IsWrongSlot(ItemEntity item, EnchantmentData enchant)
        {
            if (enchant == null || item == null || item.Blueprint == null)
                return false;

            string itemTypeStr = item.Blueprint.ItemType.ToString();
            bool isWrong = false;

            // 1. Priorité absolue aux restrictions explicites du JSON ("Slots")
            if (enchant.Slots != null && enchant.Slots.Count > 0)
            {
                // Recherche insensible à la casse dans la liste des slots du JSON
                isWrong = !enchant.Slots.Contains(itemTypeStr, StringComparer.OrdinalIgnoreCase);
            }
            else
            {
                // 2. Repli sur le type d'enchantement ("Type") si "Slots" est vide dans le JSON
                bool isWeapon = item.Blueprint is BlueprintItemWeapon;
                bool isArmor = item.Blueprint is BlueprintItemArmor;

                if (string.Equals(enchant.Type, "Weapon", StringComparison.OrdinalIgnoreCase))
                {
                    isWrong = !isWeapon;
                }
                else if (string.Equals(enchant.Type, "Armor", StringComparison.OrdinalIgnoreCase))
                {
                    isWrong = !isArmor;
                }
            }

            return isWrong;
        }

        public static bool IsWondrousItem(ItemEntity item)
        {
            if (item == null || item.Blueprint == null) return false;
            string itemType = item.Blueprint.ItemType.ToString();
            
            // Tout ce qui n'est pas une arme, une armure ou un bouclier est considéré comme objet merveilleux
            return itemType != "Weapon" && itemType != "Armor" && itemType != "Shield";
        }

        // Nouvelle méthode pour vérifier si l'objet possède déjà des capacités
        public static bool HasMultipleAbilities(ItemEntity item, IEnumerable<EnchantmentData> queuedEnchants)
        {
            // Vérifier s'il y a déjà des enchantements non-temporaires sur l'objet de base
            bool hasBaseEnchants = item.Enchantments.Any(e => !e.IsTemporary);
            
            // Ou s'il y a déjà d'autres enchantements en attente dans le panier
            bool hasQueuedEnchants = queuedEnchants != null && queuedEnchants.Any();

            return hasBaseEnchants || hasQueuedEnchants;
        }

        public static int GetCraftingDays(long gpCost, bool instant = false)
        {
            if (instant || gpCost <= 0) return 0;
            return Math.Max(1, (int)Math.Ceiling(gpCost / 1000.0));
        }

        public static string FormatRemainingTime(long remainingTicks)
        {
            if (remainingTicks <= 0) return Helpers.GetString("ui_time_done", "Terminé");
            TimeSpan ts = TimeSpan.FromTicks(remainingTicks);
            if (ts.Days > 0 && ts.Hours > 0) return string.Format(Helpers.GetString("ui_time_days_hours", "{0} jour(s) et {1} heure(s)"), ts.Days, ts.Hours);
            if (ts.Days > 0) return string.Format(Helpers.GetString("ui_time_days", "{0} jour(s)"), ts.Days);
            if (ts.Hours > 0) return string.Format(Helpers.GetString("ui_time_hours", "{0} heure(s)"), ts.Hours);
            return Helpers.GetString("ui_time_less_than_hour", "Moins d'une heure");
        }

        public static string ValidateSelectionBeforeStart(ItemEntity item, List<EnchantmentData> selectedList, long totalCost)
        {
            if (item == null || selectedList == null || selectedList.Count == 0) return Helpers.GetString("err_no_enchant_selected", "Aucun enchantement sélectionné.");
            if (Game.Instance.Player.Money < totalCost) return Helpers.GetString("err_not_enough_gold", "Vous n'avez pas assez d'or pour lancer tous les projets sélectionnés.");

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
