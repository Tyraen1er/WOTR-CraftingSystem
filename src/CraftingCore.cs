using System;
using System.Collections.Generic;
using Kingmaker;
using Kingmaker.ElementsSystem;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.JsonSystem;
using Kingmaker.PubSubSystem;
using Kingmaker.DialogSystem.Blueprints;
using Kingmaker.View;
using Kingmaker.View.MapObjects;
using Kingmaker.Items;
using Kingmaker.UnitLogic; 
using Kingmaker.EntitySystem;
using Kingmaker.Blueprints.Items.Weapons;
using Kingmaker.Blueprints.Items.Armors;
using Kingmaker.Blueprints.Items.Equipment;
using Kingmaker.Blueprints.Items.Ecnchantments;
using Newtonsoft.Json; 
using UniRx;
using System.Linq;
using Kingmaker.UI.Models.Log;
using Kingmaker.Blueprints.Root;
using HarmonyLib;

namespace CraftingSystem
{
    public class CraftingProject
    {
        [JsonProperty]
        public ItemEntity Item; 
        [JsonProperty]
        public string EnchantmentGuid;
        [JsonProperty]
        public long FinishTimeTicks; 
        [JsonProperty]
        public int GoldPaid;
    }

    public static class CraftingActions
    {
        public static void StartCraftingProject(ItemEntity item, EnchantmentData data, int cost, int days)
        {
            if (item == null || data == null) return;

            // 1. Paiement immédiat
            Game.Instance.Player.Money -= cost;

            var bp = data.Blueprint;
            if (bp == null) return;

            // 2. CAS PARTICULIER : INSTANTANÉ (0 JOURS)
            if (days <= 0)
            {
                UnitPartWilcerWorkshop.ApplyEnchantmentSafely(item, bp);
                Main.ModEntry.Logger.Log($"[ATELIER] Application immédiate de {data.Name} sur {item.Name}.");
                return;
            }

            // 3. MISE EN FILE D'ATTENTE (PROJET)
            // 🛠️ CORRECTION : On utilise la durée exacte d'un jour définie par le système
            long ticksPerDay = TimeSpan.TicksPerDay; 
            long finishTime = Game.Instance.Player.GameTime.Ticks + ((long)days * ticksPerDay);

            var project = new CraftingProject
            {
                Item = item,
                EnchantmentGuid = data.Guid,
                FinishTimeTicks = finishTime,
                GoldPaid = cost
            };

            var workshop = Game.Instance.Player.MainCharacter.Value.Ensure<UnitPartWilcerWorkshop>();
            workshop.ActiveProjects.Add(project);
            Main.ModEntry.Logger.Log($"[ATELIER] Lancement du craft pour {item.Name}.");
        }
    }

    public class UnitPartWilcerWorkshop : UnitPart
    {
        [JsonProperty]
        public List<ItemEntity> StashedItems = new List<ItemEntity>();

        [JsonProperty]
        public List<CraftingProject> ActiveProjects = new List<CraftingProject>();

        private ItemsCollection _virtualBox;

        public ItemsCollection GetBox()
        {
            if (_virtualBox == null)
            {
                _virtualBox = new ItemsCollection();
                foreach (var item in StashedItems) {
                    if (item != null) _virtualBox.Add(item);
                }
            }
            return _virtualBox;
        }

        public void SyncFromBox()
        {
            if (_virtualBox != null) {
                StashedItems = _virtualBox.Items.ToList();
            }
        }

        public void CheckAndFinishProjects()
        {
            // Main.ModEntry.Logger.Log($"[ATELIER-DEBUG] Début CheckAndFinishProjects. Projets à vérifier : {ActiveProjects.Count}");
            if (ActiveProjects.Count == 0) return;

            long currentTime = Game.Instance.TimeController.GameTime.Ticks;
            var completedProjects = new List<CraftingProject>();

            foreach (var project in ActiveProjects)
            {
                /*
                try 
                {
                    if (currentTime >= project.FinishTimeTicks || CraftingSettings.InstantCrafting)
                    {
                        string itemName = project.Item != null ? project.Item.Name : "Objet Null";
                        Main.ModEntry.Logger.Log($"[ATELIER-DEBUG] Tentative de finition pour : {itemName} avec l'enchantement {project.EnchantmentGuid}");

                        var bp = ResourcesLibrary.TryGetBlueprint(BlueprintGuid.Parse(project.EnchantmentGuid)) as BlueprintItemEnchantment;
                        if (bp == null) 
                        {
                            Main.ModEntry.Logger.Error($"[ATELIER-DEBUG] ERREUR: Blueprint introuvable pour le GUID {project.EnchantmentGuid} !");
                            continue; // On passe au projet suivant pour éviter de tout crasher
                        }

                        ApplyEnchantmentSafely(project.Item, bp);
                        completedProjects.Add(project);
                        Main.ModEntry.Logger.Log($"[ATELIER-DEBUG] Application réussie sur {itemName}");
                    }
                } 
                catch (Exception e) 
                {
                    Main.ModEntry.Logger.Error($"[ATELIER-DEBUG] CRASH dans la boucle d'un projet : {e.Message}\n{e.StackTrace}");
                }
                */
            }

            foreach (var p in completedProjects) 
            {
                ActiveProjects.Remove(p);
            }
            // Main.ModEntry.Logger.Log($"[ATELIER-DEBUG] Fin CheckAndFinishProjects. Projets restants : {ActiveProjects.Count}");
        }

        public static void ApplyEnchantmentSafely(ItemEntity item, BlueprintItemEnchantment bp)
        {
            if (item == null || bp == null) return;

            // NETTOYAGE : Si on ajoute une altération (+2), on retire l'ancienne (+1)
            bool isEnhancement = bp.AssetGuid.ToString().Contains("Enhancement") || bp.name.Contains("Plus"); 
            if (isEnhancement)
            {
                var oldEnhance = item.Enchantments.Where(e => e.Blueprint.name.Contains("Plus") || e.Blueprint.name.Contains("Enhancement")).ToList();
                foreach (var old in oldEnhance) item.RemoveEnchantment(old);
            }

            // On évite les doublons exacts
            if (!item.Enchantments.Any(e => e.Blueprint.AssetGuid == bp.AssetGuid))
            {
                // 🛠️ CORRECTION FINALE : On donne au jeu un "lanceur" pour cet enchantement
                var player = Game.Instance.Player.MainCharacter.Value;
                
                var context = new Kingmaker.UnitLogic.Mechanics.MechanicsContext(
                    caster: player, 
                    owner: player.Descriptor, 
                    blueprint: bp
                );
                
                item.AddEnchantment(bp, context);
                item.Identify();
            }
        }
    }
}