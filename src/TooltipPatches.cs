using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using Kingmaker.Items;
using Kingmaker.UI.MVVM._VM.Tooltip.Templates;
using Kingmaker.Blueprints.Items.Ecnchantments;
using Kingmaker.UI.Tooltip;
using Kingmaker.Localization;
using Kingmaker.UI.MVVM._VM.Tooltip.Bricks;
using Owlcat.Runtime.UI.Tooltips;

namespace CraftingSystem
{
    [HarmonyPatch(typeof(TooltipTemplateItem), nameof(TooltipTemplateItem.GetBody))]
    public static class TooltipTemplateItem_GetBody_Patch
    {
        // On utilise Postfix pour ajouter du contenu à la fin du corps de l'infobulle
        public static void Postfix(TooltipTemplateItem __instance, ref IEnumerable<ITooltipBrick> __result)
        {
            try
            {
                if (__instance.m_Item == null) return;

                var item = __instance.m_Item;
                var enchantments = item.Enchantments.Where(e => !e.IsTemporary && e.Blueprint != null).ToList();

                if (enchantments.Count == 0) return;

                // Création d'un bloc de texte pour nos descriptions d'enchantements
                StringBuilder sb = new StringBuilder();
                
                // Titre localisé (ex: "Pouvoirs magiques :" ou "Magical Powers:")
                string title = Helpers.GetString("ui_tooltip_magical_powers", "Magical Powers:");
                sb.AppendLine($"<size=115%><b><color=black>{title}</color></b></size>");

                bool addedAny = false;
                foreach (var ench in enchantments)
                {
                    DescriptionSource source;
                    string desc = DescriptionManager.GetLocalizedDescription(ench.Blueprint, null, out source);

                    // On n'affiche QUE les descriptions auto-générées pour éviter les doublons avec le jeu
                    if (source == DescriptionSource.Generated && !string.IsNullOrEmpty(desc) && desc != "TODO")
                    {
                        string name = DescriptionManager.GetDisplayName(ench.Blueprint, null);
                        sb.AppendLine($"<b><color=black>• {name} :</color></b> {desc}");
                        addedAny = true;
                    }
                }

                if (addedAny)
                {
                    // On convertit notre StringBuilder en une liste de briques de tooltip
                    // On l'ajoute au résultat existant
                    var newParts = __result.ToList();
                    newParts.Add(new TooltipBrickSeparator((TooltipBrickElementType)1));
                    newParts.Add(new TooltipBrickText(sb.ToString()));
                    __result = newParts;
                }
            }
            catch (Exception ex)
            {
                Main.ModEntry.Logger.Error($"[TooltipPatch] Error in Postfix: {ex.Message}");
            }
        }
    }
}
