using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.Blueprints.Items.Armors;
using Kingmaker.Blueprints.Items.Ecnchantments;
using Kingmaker.Blueprints.Items.Weapons;
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


namespace CraftingSystem
{
    public class ItemPartCustomName : EntityPart
    {
        [JsonProperty]
        public string CustomName;
    }

    // =====================================================================
    // DICTIONNAIRE DES PRÉFIXES/SUFFIXES
    // Remplace la modification des Blueprints qui causait l'erreur CS0200
    // =====================================================================
    public static class EnchantmentAffixes
    {
        public static readonly Dictionary<string, (string Prefix, string Suffix)> Data = new Dictionary<string, (string, string)>()
        {
            // --- ARMES : BONUS D'ALTÉRATION ---
            { "d42fc23b92c640846ac137dc26e000d4", ("", "+1") },
            { "eb2faccc4c9487d43b3575d7e77ff3f5", ("", "+2") },
            { "80bb8a737579e35498177e1e3c75899b", ("", "+3") },
            { "783d7d496da6ac44f9511011fc5f1979", ("", "+4") },
            { "bdba267e951851449af552aa9f9e3992", ("", "+5") },

            // --- ARMES : ÉLÉMENTS ET EFFETS ---
            { "30f90becaaac51f41bf56641966c4121", ("Flaming", "") },
            { "421e54078b7719d40915ce0672511d0b", ("Frost", "") },
            { "7bda5277d36ad114f9f9fd21d0dab658", ("Shocking", "") },
            { "633b38ff1d11de64a91d490c683ab1c8", ("Corrosive", "") },
            { "102a9c8c9b7a75e4fb5844e79deaf4c0", ("Keen", "") },
            { "28a9964d81fedae44bae3ca45710c140", ("Holy", "") },
            { "d05753b8df780fc4bb55b318f06af453", ("Unholy", "") },

            // --- ARMURES & BOUCLIERS : BONUS D'ALTÉRATION ---
            { "a9ea95c5e02f9b7468447bc1010fe152", ("", "+1") }, // Armure +1
            { "e90c252e08035294eba39bafce76c119", ("", "+1") }, // Bouclier +1
            
            // TODO: Ajoute ici les GUIDs de tes propres enchantements de mod !
            // { "TON-GUID", ("TonPréfixe", "TonSuffixe") }
        };
    }

    // =====================================================================
    // MÉTHODES D'EXTENSION POUR L'EXTRACTION DES TEXTES
    // =====================================================================
    public static class EnchantmentNamingExtensions
    {
        public static string GetEnchantmentPrefixes(this IEnumerable<ItemEnchantment> enchants)
        {
            if (enchants == null || !enchants.Any()) return "";

            string text = "";
            foreach (var bp in enchants.Where(e => !e.IsTemporary).Select(e => e.Blueprint))
            {
                string guid = bp.AssetGuid.ToString();
                
                // On cherche dans notre dictionnaire d'abord. Sinon, on prend le Prefix de base du jeu.
                string prefix = EnchantmentAffixes.Data.ContainsKey(guid) 
                    ? EnchantmentAffixes.Data[guid].Prefix 
                    : bp.Prefix; 

                if (!string.IsNullOrEmpty(prefix))
                {
                    text += prefix + " ";
                }
            }
            return text;
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

        public static string GetEnchantmentSuffixes(this IEnumerable<ItemEnchantment> enchants)
        {
            if (enchants == null || !enchants.Any()) return "";

            string text = "";
            foreach (var bp in enchants.Where(e => !e.IsTemporary).Select(e => e.Blueprint))
            {
                string guid = bp.AssetGuid.ToString();
                
                // On cherche dans notre dictionnaire d'abord. Sinon, on prend le Suffix de base du jeu.
                string suffix = EnchantmentAffixes.Data.ContainsKey(guid) 
                    ? EnchantmentAffixes.Data[guid].Suffix 
                    : bp.Suffix;

                if (!string.IsNullOrEmpty(suffix))
                {
                    text += " " + suffix;
                }
            }
            return text;
        }

        public static string GetEnchantmentSuffixes(this ItemEntity item)
        {
            // On ignore les enchantements qui donnent directement des bonus d'altération (CS0246 corrigé ici)
            string text = item.Enchantments
                .Where(e => e.GetComponent<WeaponEnhancementBonus>() == null && e.GetComponent<ArmorEnhancementBonus>() == null)
                .GetEnchantmentSuffixes();

            int totalEnhancement = GameHelper.GetItemEnhancementBonus(item);
            if (totalEnhancement > 0)
            {
                text += $" +{totalEnhancement}";
            }
            return text;
        }

        public static string GetCustomEnchantmentSuffixes(this ItemEntity item)
        {
            string text = item.Enchantments
                .Where(e => e.GetComponent<WeaponEnhancementBonus>() == null && e.GetComponent<ArmorEnhancementBonus>() == null)
                .Where(e => !item.Blueprint.Enchantments.Contains(e.Blueprint))
                .GetEnchantmentSuffixes();

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

    // --- SUPPORT DU RENOMMAGE PERSISTANT ---
    [HarmonyPatch(typeof(ItemEntity), "get_Name")]
    public static class ItemEntity_Name_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ItemEntity __instance, ref string __result)
        {
            try {
                var part = __instance.Get<ItemPartCustomName>();
                if (part != null && !string.IsNullOrEmpty(part.CustomName))
                {
                    __result = part.CustomName;
                }
            } catch { }
        }
    }

    // --- SÉCURITÉ : PRÉSERVER LE NOM LORS D'UN SPLIT ---
    [HarmonyPatch(typeof(ItemEntity), nameof(ItemEntity.Split))]
    public static class ItemEntity_Split_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ItemEntity __instance, ItemEntity __result)
        {
            if (__instance == null || __result == null || __instance == __result) return;
            var originalPart = __instance.Get<ItemPartCustomName>();
            if (originalPart != null)
            {
                var newPart = __result.Ensure<ItemPartCustomName>();
                newPart.CustomName = originalPart.CustomName;
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
                name = item.GetEnchantmentPrefixes() + defaultName + item.GetEnchantmentSuffixes();
            } 
            else 
            {
                var suffixes = item.GetCustomEnchantmentSuffixes();
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
                Main.ModEntry.Logger.Log($"[ATELIER] Renommé : {(string.IsNullOrEmpty(name) ? "Original" : name)}");
            }
            catch (Exception ex) { Main.ModEntry.Logger.Error($"Erreur renommage : {ex}"); }
        }
    }
}