using System;
using HarmonyLib;
using Kingmaker;
using Kingmaker.Blueprints;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.PubSubSystem;

namespace CraftingSystem
{
    // On utilise IAreaHandler pour déclencher le dump à chaque chargement de zone (incluant les sauvegardes)
    [HarmonyPatch(typeof(Player), nameof(Player.OnAreaLoaded))]
    public static class MetamagicWandAnalysis
    {
        private static bool _alreadyDumped = false;

        public static void Postfix()
        {
            // On ne dump qu'une seule fois par session pour éviter de polluer les logs à chaque transition de zone
            if (_alreadyDumped) return;
            
            Main.ModEntry.Logger.Log("[ANALYSIS] Début de l'analyse des baguettes métamagiques demandées...");

            string[] guids = new string[] 
            { 
                "b50be8b008be40199903dd0c28c6312e", // Metamagic Wand Empowere Lesser
                "3b22e3b884e8470db3b031d71efa6d24", // Metamagic Wand Extended Lesser
                "2c87e12216cb04d4aa87966af9fb6118"  // Metamagic Wand Maximize Lesser
            };

            foreach (var guid in guids)
            {
                try 
                {
                    // Le BlueprintDumper.DumpByGuid gère déjà la récursion et les composants
                    // On augmente la profondeur à 3 pour être sûr de voir les features et enchants liés
                    BlueprintDumper.DumpByGuid(guid, 3);
                }
                catch (Exception ex)
                {
                    Main.ModEntry.Logger.Error($"[ANALYSIS] Erreur lors de l'analyse du GUID {guid}: {ex.Message}");
                }
            }

            Main.ModEntry.Logger.Log("[ANALYSIS] Analyse terminée.");
            _alreadyDumped = true;
        }

        // Optionnel : permettre de reset le flag si besoin (via une commande ou autre)
        public static void ResetDumpFlag()
        {
            _alreadyDumped = false;
        }
    }
}
