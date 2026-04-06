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

namespace CraftingSystem
{
    public class ItemPartCustomName : EntityPart
    {
        [JsonProperty]
        public string CustomName;
    }

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
            Main.ModEntry.Logger.Log($"[ATELIER-DEBUG] Début CheckAndFinishProjects. Projets à vérifier : {ActiveProjects.Count}");
            if (ActiveProjects.Count == 0) return;

            long currentTime = Game.Instance.TimeController.GameTime.Ticks;
            var completedProjects = new List<CraftingProject>();

            foreach (var project in ActiveProjects)
            {
                try 
                {
                    if (currentTime >= project.FinishTimeTicks || CraftingUI.InstantCrafting)
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
            }

            foreach (var p in completedProjects) 
            {
                ActiveProjects.Remove(p);
            }
            Main.ModEntry.Logger.Log($"[ATELIER-DEBUG] Fin CheckAndFinishProjects. Projets restants : {ActiveProjects.Count}");
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
            long ticksPerDay = 24L * 3600L * 10000000L / 6L; 
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

    public enum CraftingWindowMode { None, LootUI, StoredItemIMGUI }

    [TypeId("6d3e1f7d4e3347bdaeb88a1b6c8baab6")]
    public class OpenItemSelectorAction : GameAction
    {
        public override string GetCaption() { return "Atelier (Dépôt)"; }
        public override void RunAction()
        {
            DeferredInventoryOpener.Initialize();
            DeferredInventoryOpener.CurrentMode = CraftingWindowMode.LootUI;
        }
    }

    [TypeId("7d3e1f7d4e3347bdaeb88a1b6c8baab7")]
    public class OpenStoredItemSelectorAction : GameAction
    {
        public override string GetCaption() { return "Atelier (Modification)"; }
        public override void RunAction()
        {
            DeferredInventoryOpener.Initialize();
            DeferredInventoryOpener.CurrentMode = CraftingWindowMode.StoredItemIMGUI;
        }
    }

    public class DeferredInventoryOpener : IDialogFinishHandler, IItemsCollectionHandler
    {
        public static CraftingWindowMode CurrentMode = CraftingWindowMode.None;
        public static bool IsCraftingWindowOpen = false; 

        public static ItemsCollection CraftingBox
        {
            get
            {
                var mainChar = Game.Instance.Player.MainCharacter.Value;
                var part = mainChar.Ensure<UnitPartWilcerWorkshop>();
                return part.GetBox();
            }
        }

        public static DeferredInventoryOpener Instance;

        public static void Initialize()
        {
            if (Instance == null) Instance = new DeferredInventoryOpener();
            EventBus.Subscribe(Instance);
        }

        public void HandleDialogFinished(BlueprintDialog dialog, bool success)
        {
            if (CurrentMode == CraftingWindowMode.None) return;
            var mode = CurrentMode;
            CurrentMode = CraftingWindowMode.None;

            Observable.Timer(TimeSpan.FromMilliseconds(300)).Subscribe(_ => 
            {
                var player = Game.Instance.Player.MainCharacter.Value;
                if (mode == CraftingWindowMode.LootUI)
                {
                    IsCraftingWindowOpen = true; 
                    EventBus.RaiseEvent<ILootInterractionHandler>(h => 
                        h.HandleLootInterraction(player, new EntityViewBase[] { player.View }, LootContainerType.PlayerChest, OnInventoryClosed)
                    );
                }
                else if (mode == CraftingWindowMode.StoredItemIMGUI)
                {
                    if (CraftingUI.Instance == null)
                    {
                        var go = new UnityEngine.GameObject("CraftingSystem_UI");
                        UnityEngine.Object.DontDestroyOnLoad(go);
                        go.AddComponent<CraftingUI>();
                    }
                    CraftingUI.Instance.IsOpen = true;
                }
            });
        }

        public void HandleItemsAdded(ItemsCollection collection, ItemEntity item, int count)
        {
            if (collection != CraftingBox) return;

            if (item.Count > 1)
            {
                int toReturn = item.Count - 1;
                var leftover = item.Split(toReturn);
                Observable.Timer(TimeSpan.FromMilliseconds(50)).Subscribe(_ => 
                {
                    Game.Instance.Player.Inventory.Add(leftover);
                });
            }

            bool isWeapon = item.Blueprint is BlueprintItemWeapon;
            bool isArmor = item.Blueprint is BlueprintItemArmor;
            bool isEquipable = item.Blueprint is BlueprintItemEquipment && !(item.Blueprint is BlueprintItemEquipmentUsable);

            if (!isWeapon && !isArmor && !isEquipable) SpitItemBack(item);
        }

        public void HandleItemsRemoved(ItemsCollection collection, ItemEntity item, int count) { }

        private void SpitItemBack(ItemEntity item)
        {
            Observable.Timer(TimeSpan.FromMilliseconds(50)).Subscribe(_ => 
            {
                if (CraftingBox.Items.Contains(item))
                {
                    CraftingBox.Remove(item); 
                    Game.Instance.Player.Inventory.Add(item); 
                }
            });
        }

        private void OnInventoryClosed()
        {
            IsCraftingWindowOpen = false; 
            Game.Instance.Player.MainCharacter.Value.Ensure<UnitPartWilcerWorkshop>().SyncFromBox();
        }
    }
}