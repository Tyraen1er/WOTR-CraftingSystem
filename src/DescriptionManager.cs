using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Items.Ecnchantments;
using Kingmaker.Localization;
using Kingmaker.Blueprints.Items.Weapons;
using Kingmaker.Blueprints.Items.Armors;

namespace CraftingSystem
{
    public static class DescriptionManager
    {
        public static string GetLocalizedDescription(BlueprintItemEnchantment bp, EnchantmentData data, out DescriptionSource source)
        {
            source = DescriptionSource.None;

            // 1. Priorité au Jeu (Description localisée officielle)
            if (bp != null)
            {
                string localized = bp.m_Description?.ToString();
                if (!string.IsNullOrEmpty(localized) && localized != bp.name) 
                {
                    source = DescriptionSource.Official;
                    return System.Text.RegularExpressions.Regex.Replace(localized, "<.*?>", string.Empty);
                }
            }

            // 2. Génération Dynamique via Composants (Fall-back si le jeu est vide)
            if (bp != null)
            {
                string generated = EnchantmentDescriptionGenerator.Generate(bp);
                if (!string.IsNullOrEmpty(generated)) 
                {
                    source = DescriptionSource.Generated;
                    string prefix = Helpers.GetString("ui_description_autogen_prefix", "[auto-generated] ");
                    return prefix + generated;
                }
            }

            // 3. Fallback sur le Commentaire développeur
            if (bp != null && !string.IsNullOrEmpty(bp.Comment)) 
            {
                source = DescriptionSource.Official;
                return bp.Comment;
            }

            source = DescriptionSource.None;
            return "";
        }

        public static string GetDisplayName(BlueprintItemEnchantment bp, EnchantmentData data)
        {
            string finalName = "";

            if (bp != null && bp.m_EnchantName != null)
            {
                string localized = bp.m_EnchantName.ToString();
                if (!string.IsNullOrWhiteSpace(localized) && localized != bp.name) finalName = localized;
            }

            if (string.IsNullOrEmpty(finalName) && data != null && !string.IsNullOrWhiteSpace(data.Name)) 
                finalName = data.Name;

            if (string.IsNullOrEmpty(finalName) && bp != null)
            {
                finalName = bp.name.Replace("WeaponEnchantment", "")
                              .Replace("ArmorEnchantment", "")
                              .Replace("Enchantment", "")
                              .Replace("Plus", "+");
            }

            if (string.IsNullOrEmpty(finalName)) 
                finalName = Helpers.GetString("ui_unknown_enchant_name", "Unknown Enchantment");

            // Troncation à 50 caractères
            if (finalName.Length > 50) 
                finalName = finalName.Substring(0, 47) + "...";

            return finalName;
        }
    }
}
