using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Items.Ecnchantments;
using Kingmaker.Localization;
using Kingmaker.Blueprints.Items.Weapons;
using Kingmaker.Blueprints.Items.Armors;
using Kingmaker.Blueprints.Items;

namespace CraftingSystem
{
    public static class DescriptionManager
    {
        public static string GetLocalizedDescription(BlueprintScriptableObject bpObj, EnchantmentData data, out DescriptionSource source)
        {
            source = DescriptionSource.None;
            var bp = bpObj as BlueprintItemEnchantment;
            var item = bpObj as BlueprintItem;

            if (bpObj != null)
            {
                var guid = bpObj.AssetGuid;
                if (DynamicGuidHelper.TryDecodeGuid(guid, out string enchantId, out var paramValues, out _))
                {
                    if (enchantId == "013" && paramValues.Count >= 9)
                    {
                        source = DescriptionSource.Generated;
                        return GenerateSpellcastingDescription(paramValues);
                    }
                }
            }

            // 1. Priorité au Jeu (Description localisée officielle)
            if (bp != null || item != null)
            {
                string localized = bp != null ? bp.m_Description?.ToString() : item.m_DescriptionText?.ToString();
                if (!string.IsNullOrEmpty(localized) && localized != bp.name && !localized.StartsWith("[missing", StringComparison.OrdinalIgnoreCase)) 
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

        public static string GetDisplayName(BlueprintScriptableObject bpObj, EnchantmentData data)
        {
            string finalName = "";
            var bp = bpObj as BlueprintItemEnchantment;
            var item = bpObj as BlueprintItem;

            if (bpObj != null)
            {
                var guid = bpObj.AssetGuid;
                if (DynamicGuidHelper.TryDecodeGuid(guid, out string enchantId, out var paramValues, out _))
                {
                    if (enchantId == "013" && paramValues.Count >= 9)
                    {
                        return GenerateSpellcastingDisplayName(paramValues);
                    }
                }
            }

            if (bp != null && bp.m_EnchantName != null)
            {
                string localized = bp.m_EnchantName.ToString();
                if (!string.IsNullOrWhiteSpace(localized) && localized != bp.name && !localized.StartsWith("[missing", StringComparison.OrdinalIgnoreCase)) finalName = localized;
            }
            else if (item != null && item.m_DisplayNameText != null)
            {
                string localized = item.m_DisplayNameText.ToString();
                if (!string.IsNullOrWhiteSpace(localized) && localized != item.name && !localized.StartsWith("[missing", StringComparison.OrdinalIgnoreCase)) finalName = localized;
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

        private static string GenerateSpellcastingDescription(List<int> paramValues)
        {
            string spellGuid = CustomEnchantmentsBuilder.GetSpellGuidByHash(paramValues.Skip(1).ToList());
            string spellName = "Unknown Spell";
            if (!string.IsNullOrEmpty(spellGuid))
            {
                if (SpellScanner.AvailableSpells.TryGetValue(spellGuid, out var sd))
                {
                    spellName = sd.Name;
                }
            }

            int charges = paramValues.Count > 5 ? paramValues[5] : 1;
            int cl = paramValues.Count > 6 ? paramValues[6] : 1;
            int dc = paramValues.Count > 8 ? paramValues[8] : 10;

            string currentLocale = LocalizationManager.CurrentLocale.ToString();
            
            if (currentLocale == "frFR")
            {
                return $"Confère la capacité de lancer le sort <b>{spellName}</b> <b>{charges}</b> fois par jour (Niveau de lanceur : <b>{cl}</b>, DD : <b>{dc}</b>).";
            }
            else if (currentLocale == "ruRU")
            {
                return $"Дарует возможность разыгрывать заклинание <b>{spellName}</b> <b>{charges}</b> раз в день (Уровень заклинателя <b>{cl}</b>, СЛ <b>{dc}</b>).";
            }
            else
            {
                return $"Grants the wielder the ability to cast the spell <b>{spellName}</b> <b>{charges}</b> times per day (Caster Level <b>{cl}</b>, DC <b>{dc}</b>).";
            }
        }

        private static string GenerateSpellcastingDisplayName(List<int> paramValues)
        {
            string spellGuid = CustomEnchantmentsBuilder.GetSpellGuidByHash(paramValues.Skip(1).ToList());
            string spellName = "Unknown Spell";
            if (!string.IsNullOrEmpty(spellGuid))
            {
                if (SpellScanner.AvailableSpells.TryGetValue(spellGuid, out var sd))
                {
                    spellName = sd.Name;
                }
            }

            int charges = paramValues.Count > 5 ? paramValues[5] : 1;
            int cl = paramValues.Count > 6 ? paramValues[6] : 1;
            int dc = paramValues.Count > 8 ? paramValues[8] : 10;

            string currentLocale = LocalizationManager.CurrentLocale.ToString();
            
            if (currentLocale == "frFR")
            {
                return $"{spellName} ({charges}/jour, NLS {cl}, DD {dc})";
            }
            else if (currentLocale == "ruRU")
            {
                return $"{spellName} ({charges}/день, УКС {cl}, СЛ {dc})";
            }
            else
            {
                return $"{spellName} ({charges}/day, CL {cl}, DC {dc})";
            }
        }
    }
}
