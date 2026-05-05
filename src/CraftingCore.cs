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
using Kingmaker.Blueprints.Items;
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
            if (data == null) return;
            
            // Redirection pour les boucliers : les enchantements d'arme vont sur l'arme du bouclier
            if (item != null && item.Blueprint is Kingmaker.Blueprints.Items.Shields.BlueprintItemShield && data.Type == "Weapon")
            {
                var weaponProp = item.GetType().GetProperty("Weapon", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (weaponProp != null)
                {
                    var subWeapon = weaponProp.GetValue(item) as ItemEntityWeapon;
                    if (subWeapon != null)
                    {
                        item = subWeapon;
                        Main.ModEntry.Logger.Log($"[ATELIER] Redirection (via réflexion) vers l'arme du bouclier : {item.Name}");
                    }
                }
                else
                {
                    // Tentative via le champ privé m_Weapon si la propriété n'existe pas
                    var weaponField = item.GetType().GetField("m_Weapon", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    if (weaponField != null)
                    {
                        var subWeapon = weaponField.GetValue(item) as ItemEntityWeapon;
                        if (subWeapon != null)
                        {
                            item = subWeapon;
                            Main.ModEntry.Logger.Log($"[ATELIER] Redirection (via champ m_Weapon) vers l'arme du bouclier : {item.Name}");
                        }
                    }
                }
            }

            // 1. Paiement immédiat
            Game.Instance.Player.Money -= cost;

            BlueprintScriptableObject bp = data.Blueprint;
            if (bp == null) return;

            // 2. CAS PARTICULIER : INSTANTANÉ (0 JOURS)
            if (days <= 0)
            {
                if (item != null && bp is BlueprintItemEnchantment bpEnch)
                {
                    UnitPartWilcerWorkshop.ApplyEnchantmentsafely(item, bpEnch);
                    Main.ModEntry.Logger.Log($"[ATELIER] Application immédiate de {data.Name} sur {item.Name}.");
                }
                else if (bp is BlueprintItem bpItem)
                {
                    var newItem = bpItem.CreateEntity();
                    DeferredInventoryOpener.CraftingBox.Add(newItem);
                    Game.Instance.Player.MainCharacter.Value.Ensure<UnitPartWilcerWorkshop>().SyncFromBox();
                    Main.ModEntry.Logger.Log($"[ATELIER] Création immédiate et livraison dans le coffre de {newItem.Name}.");
                }
                return;
            }

            // 3. MISE EN FILE D'ATTENTE (PROJET)
            long ticksPerDay = TimeSpan.TicksPerDay; 
            long finishTime = Game.Instance.Player.GameTime.Ticks + ((long)days * ticksPerDay);

            var project = new CraftingProject
            {
                Item = item,
                EnchantmentGuid = data.Guid,
                FinishTimeTicks = finishTime,
                GoldPaid = cost
            };

            var workshopMain = Game.Instance.Player.MainCharacter.Value.Ensure<UnitPartWilcerWorkshop>();
            workshopMain.ActiveProjects.Add(project);
            Main.ModEntry.Logger.Log($"[ATELIER] Lancement du craft pour {(item != null ? item.Name : data.Name)}.");
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
                try 
                {
                    if (currentTime >= project.FinishTimeTicks || CraftingSettings.Instance.InstantCrafting)
                    {
                        // Main.ModEntry.Logger.Log($"[ATELIER-DEBUG] Tentative de finition pour : {itemName} avec l'enchantement {project.EnchantmentGuid}");
                        
                        var bp = (project.EnchantmentGuid.Replace("-", "").ToLower().StartsWith("c2af") 
                            ? CustomEnchantmentsBuilder.GetOrBuildDynamicBlueprint(project.EnchantmentGuid) 
                            : ResourcesLibrary.TryGetBlueprint(BlueprintGuid.Parse(project.EnchantmentGuid)));

                        if (bp == null) 
                        {
                            Main.ModEntry.Logger.Error($"[ATELIER-DEBUG] ERREUR: Blueprint introuvable pour le GUID {project.EnchantmentGuid} !");
                            continue;
                        }

                        if (bp is BlueprintItem bpItem)
                        {
                            var newItem = bpItem.CreateEntity();
                            DeferredInventoryOpener.CraftingBox.Add(newItem);
                            SyncFromBox(); // Synchronise la liste interne StashedItems
                            Main.ModEntry.Logger.Log($"[ATELIER] Création et livraison dans le coffre de : {newItem.Name}");
                        }
                        else if (bp is BlueprintItemEnchantment bpEnch)
                        {
                            ApplyEnchantmentsafely(project.Item, bpEnch);
                            Main.ModEntry.Logger.Log($"[ATELIER] Application réussie de {bpEnch.name} sur {project.Item?.Name ?? "???"}");
                        }
                        
                        completedProjects.Add(project);
                    }
                } 
                catch (Exception e) 
                {
                    Main.ModEntry.Logger.Error($"[ATELIER-DEBUG] CRASH dans la boucle d'un projet : {e.Message}\n{e.StackTrace}");
                }
            }

            foreach (var p in completedProjects) 
            {
                ActiveProjects.Remove(p);
            }
            // Main.ModEntry.Logger.Log($"[ATELIER-DEBUG] Fin CheckAndFinishProjects. Projets restants : {ActiveProjects.Count}");
        }

        public static void ApplyEnchantmentsafely(ItemEntity item, BlueprintItemEnchantment bp)
        {
            if (item == null || bp == null) return;

            string family = CraftingCalculator.GetEnchantmentFamily(bp.name);
            if (!string.IsNullOrEmpty(family))
            {
                var toRemove = item.Enchantments
                    .Where(e => !e.IsTemporary && CraftingCalculator.GetEnchantmentFamily(e.Blueprint.name) == family)
                    .ToList();
                foreach (var old in toRemove) item.RemoveEnchantment(old);
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
