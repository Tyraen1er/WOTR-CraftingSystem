using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.Blueprints.Items.Armors;
using Kingmaker.Blueprints.Items.Ecnchantments;
using Kingmaker.Blueprints.Items.Weapons;
using Kingmaker.Blueprints.Items;
using Kingmaker.Designers;
using Kingmaker.Items;
using Kingmaker.Designers.Mechanics.Facts;using Kingmaker.EntitySystem;
using Newtonsoft.Json;
using System.Reflection;
using HarmonyLib;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.JsonSystem;
using Kingmaker.DialogSystem.Blueprints;
using Kingmaker.Localization;
using UnityModManagerNet;
using Kingmaker;
using Kingmaker.PubSubSystem;
using Kingmaker.UI.Models.Log;
using Kingmaker.Blueprints.Root;
using UnityEngine;


namespace CraftingSystem
{
    public class ItemPartCustomName : EntityPart
    {
        [JsonProperty]
        public string CustomName;
    }

    public class ItemPartCustomIcon : EntityPart
    {
        [JsonProperty]
        public string IconBlueprintGuid;
    }

    public class ItemPartCustomDescription : EntityPart
    {
        [JsonProperty]
        public string CustomDescription;
    }


    // =====================================================================
    // MÉTHODES D'EXTENSION POUR L'EXTRACTION DES TEXTES
    // =====================================================================
    public static class EnchantmentNamingExtensions
    {
        public static string GetEnchantmentPrefixes(this IEnumerable<ItemEnchantment> enchants)
        {
            if (enchants == null || !enchants.Any()) return "";

            var prefixes = enchants
                .Where(e => !e.IsTemporary)
                .Select(e => e.Blueprint.Prefix)
                .Where(p => !string.IsNullOrEmpty(p))
                .Distinct()
                .ToList();

            return prefixes.Count > 0 ? string.Join(" ", prefixes) + " " : "";
        }

        public static string GetEnchantmentPrefixes(this ItemEntity item)
        {
            return item.Enchantments.GetEnchantmentPrefixes();
        }

        public static string GetCustomEnchantmentPrefixes(this ItemEntity item)
        {
            return item.Enchantments
                .Where(e => !item.Blueprint.Enchantments.Contains(e.Blueprint))
                .GetEnchantmentPrefixes();
        }

        public static string GetEnchantmentsuffixes(this IEnumerable<ItemEnchantment> enchants)
        {
            if (enchants == null || !enchants.Any()) return "";

            var suffixes = enchants
                .Where(e => !e.IsTemporary)
                .Select(e => e.Blueprint.Suffix)
                .Where(s => !string.IsNullOrEmpty(s))
                .Distinct()
                .ToList();

            return suffixes.Count > 0 ? " " + string.Join(" ", suffixes) : "";
        }

        public static string GetEnchantmentsuffixes(this ItemEntity item)
        {
            // On ignore les enchantements qui donnent directement des bonus d'altération (CS0246 corrigé ici)
            string text = item.Enchantments
                .Where(e => e.GetComponent<WeaponEnhancementBonus>() == null && e.GetComponent<ArmorEnhancementBonus>() == null)
                .GetEnchantmentsuffixes();

            int totalEnhancement = GameHelper.GetItemEnhancementBonus(item);
            if (totalEnhancement > 0)
            {
                text += $" +{totalEnhancement}";
            }
            return text;
        }

        public static string GetCustomEnchantmentsuffixes(this ItemEntity item)
        {
            string text = item.Enchantments
                .Where(e => e.GetComponent<WeaponEnhancementBonus>() == null && e.GetComponent<ArmorEnhancementBonus>() == null)
                .Where(e => !item.Blueprint.Enchantments.Contains(e.Blueprint))
                .GetEnchantmentsuffixes();

            int totalEnhancement = GameHelper.GetItemEnhancementBonus(item);
            
            int baseEnhancement = 0;
            if (item is ItemEntityWeapon weapon) { baseEnhancement = GameHelper.GetWeaponEnhancementBonus(weapon.Blueprint); }
            else if (item is ItemEntityArmor armor) { baseEnhancement = GameHelper.GetArmorEnhancementBonus(armor.Blueprint); }

            if (totalEnhancement > baseEnhancement)
            {
                text += $" +{totalEnhancement}";
            }
            return text;
        }
    }

    // --- SUPPORT DU RENOMMAGE ET VISUEL PERSISTANT ---
    [HarmonyPatch(typeof(ItemEntity))]
    public static class ItemEntity_Visual_Patch
    {
        [HarmonyPatch("get_Name"), HarmonyPostfix]
        public static void NamePostfix(ItemEntity __instance, ref string __result)
        {
            try {
                var part = __instance.Get<ItemPartCustomName>();
                if (part != null && !string.IsNullOrEmpty(part.CustomName))
                {
                    __result = part.CustomName;
                }
            } catch { }
        }

        [HarmonyPatch("get_Description"), HarmonyPostfix]
        public static void DescriptionPostfix(ItemEntity __instance, ref string __result)
        {
            try {
                var part = __instance.Get<ItemPartCustomDescription>();
                if (part != null && !string.IsNullOrEmpty(part.CustomDescription))
                {
                    __result = part.CustomDescription;
                }
            } catch { }
        }

        [HarmonyPatch("get_Icon"), HarmonyPostfix]
        public static void IconPostfix(ItemEntity __instance, ref Sprite __result)
        {
            try {
                var part = __instance.Get<ItemPartCustomIcon>();
                if (part != null && !string.IsNullOrEmpty(part.IconBlueprintGuid))
                {
                    var bp = ResourcesLibrary.TryGetBlueprint<BlueprintItem>(part.IconBlueprintGuid);
                    if (bp != null && bp.Icon != null) __result = bp.Icon;
                }
            } catch { }
        }
    }

    // --- SÉCURITÉ : PRÉSERVER LES DONNÉES LORS D'UN SPLIT ---
    [HarmonyPatch(typeof(ItemEntity), nameof(ItemEntity.Split))]
    public static class ItemEntity_Split_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ItemEntity __instance, ItemEntity __result)
        {
            if (__instance == null || __result == null || __instance == __result) return;
            
            var originalNamePart = __instance.Get<ItemPartCustomName>();
            if (originalNamePart != null)
            {
                var newPart = __result.Ensure<ItemPartCustomName>();
                newPart.CustomName = originalNamePart.CustomName;
            }

            var originalIconPart = __instance.Get<ItemPartCustomIcon>();
            if (originalIconPart != null)
            {
                var newPart = __result.Ensure<ItemPartCustomIcon>();
                newPart.IconBlueprintGuid = originalIconPart.IconBlueprintGuid;
            }

            var originalDescPart = __instance.Get<ItemPartCustomDescription>();
            if (originalDescPart != null)
            {
                var newPart = __result.Ensure<ItemPartCustomDescription>();
                newPart.CustomDescription = originalDescPart.CustomDescription;
            }
        }
    }

    // =====================================================================
    // GESTIONNAIRE DES ACTIONS DE RENOMMAGE (Nouveau conteneur)
    // =====================================================================
    public static class ItemRenamer
    {
        // --- GÉNÉRATEUR MANUEL DE NOM AUTO ---
        public static string GenerateAutoName(ItemEntity item)
        {
            if (item == null) return "";
            string uniqueName = item.Blueprint.m_DisplayNameText;
            string defaultName = "";

            if (item.Blueprint is BlueprintItemWeapon bpW) defaultName = bpW.Type.DefaultName;
            else if (item.Blueprint is BlueprintItemArmor bpA) defaultName = bpA.Type.DefaultName;

            string name = "";
            if (string.IsNullOrEmpty(uniqueName)) 
            {
                name = item.GetEnchantmentPrefixes() + defaultName + item.GetEnchantmentsuffixes();
            } 
            else 
            {
                var suffixes = item.GetCustomEnchantmentsuffixes();
                if (System.Text.RegularExpressions.Regex.Match(suffixes, @"\+\d").Success) 
                {
                    name = item.GetCustomEnchantmentPrefixes() + System.Text.RegularExpressions.Regex.Replace(uniqueName, @"\+\d", "") + suffixes;
                } 
                else 
                {
                    name = item.GetCustomEnchantmentPrefixes() + uniqueName + suffixes;
                }
            }
            return name.Replace("  ", " ").Trim();
        }

        public static void RenameItem(ItemEntity item, string name)
        {
            if (item == null) return;
            try
            {
                if (string.IsNullOrEmpty(name)) item.Remove<ItemPartCustomName>();
                else item.Ensure<ItemPartCustomName>().CustomName = name;
                
                item.Identify();
                UpdateStackability(item);
                Main.ModEntry.Logger.Log($"[ATELIER] Renommé : {(string.IsNullOrEmpty(name) ? "Original" : name)}");
            }
            catch (Exception ex) { Main.ModEntry.Logger.Error($"Erreur renommage : {ex}"); }
        }

        public static void ChangeIcon(ItemEntity item, string blueprintGuid)
        {
            if (item == null) return;
            try
            {
                if (string.IsNullOrEmpty(blueprintGuid)) item.Remove<ItemPartCustomIcon>();
                else item.Ensure<ItemPartCustomIcon>().IconBlueprintGuid = blueprintGuid;
                
                UpdateStackability(item);
                Main.ModEntry.Logger.Log($"[ATELIER] Icône changée : {(string.IsNullOrEmpty(blueprintGuid) ? "Originale" : blueprintGuid)}");
            }
            catch (Exception ex) { Main.ModEntry.Logger.Error($"Erreur changement icône : {ex}"); }
        }

        public static void ChangeDescription(ItemEntity item, string description)
        {
            if (item == null) return;
            try
            {
                if (string.IsNullOrEmpty(description)) item.Remove<ItemPartCustomDescription>();
                else item.Ensure<ItemPartCustomDescription>().CustomDescription = description;
                
                UpdateStackability(item);
                Main.ModEntry.Logger.Log($"[ATELIER] Description changée : {(string.IsNullOrEmpty(description) ? "Originale" : "Personnalisée")}");
            }
            catch (Exception ex) { Main.ModEntry.Logger.Error($"Erreur changement description : {ex}"); }
        }

        private static void UpdateStackability(ItemEntity item)
        {
            if (item == null) return;
            
            bool isCustomized = item.Get<ItemPartCustomName>() != null 
                || item.Get<ItemPartCustomIcon>() != null
                || item.Get<ItemPartCustomDescription>() != null;
            
            if (isCustomized)
            {
                item.ForceStackable = false;
            }
            else
            {
                // Reset to default (null uses IsMayBeStackable logic)
                item.ForceStackable = null;
            }
        }
    }
}
