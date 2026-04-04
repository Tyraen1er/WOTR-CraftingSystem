using System;
using System.Collections.Generic;
using Kingmaker;
using Kingmaker.ElementsSystem;
using Kingmaker.Blueprints.JsonSystem;
using Kingmaker.PubSubSystem;
using Kingmaker.DialogSystem.Blueprints;
using Kingmaker.View;
using Kingmaker.View.MapObjects;
using Kingmaker.Items;
using Kingmaker.UnitLogic; 
using Kingmaker.Blueprints.Items.Weapons;
using Kingmaker.Blueprints.Items.Armors;
using Kingmaker.Blueprints.Items.Equipment;
using Newtonsoft.Json; 
using UniRx;
using System.Linq;

namespace CraftingSystem
{
    // =========================================================================
    // 1. LE COMPOSANT DE SAUVEGARDE SÉCURISÉ (ANTI-CORRUPTION)
    // =========================================================================
    public class UnitPartWilcerWorkshop : UnitPart
    {
        // On sauvegarde une LISTE SIMPLE, le jeu adore ça et ne crashera pas.
        [JsonProperty]
        public List<ItemEntity> StashedItems = new List<ItemEntity>();

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

        // On appelle ça à la fermeture de l'UI pour mettre à jour la sauvegarde
        public void SyncFromBox()
        {
            if (_virtualBox != null) {
                StashedItems = _virtualBox.Items.ToList();
            }
        }
    }

    // =========================================================================
    // 2. LES ACTIONS DE DIALOGUE
    // =========================================================================
    public enum CraftingWindowMode { None, LootUI, StoredItemIMGUI }

    [TypeId("6d3e1f7d4e3347bdaeb88a1b6c8baab6")]
    public class OpenItemSelectorAction : GameAction
    {
        public override string GetCaption() { return "Ouverture de l'Atelier (Dépôt)"; }

        public override void RunAction()
        {
            DeferredInventoryOpener.Initialize();
            DeferredInventoryOpener.CurrentMode = CraftingWindowMode.LootUI;
        }
    }

    [TypeId("7d3e1f7d4e3347bdaeb88a1b6c8baab7")]
    public class OpenStoredItemSelectorAction : GameAction
    {
        public override string GetCaption() { return "Ouverture de l'Atelier (Modification)"; }

        public override void RunAction()
        {
            DeferredInventoryOpener.Initialize();
            DeferredInventoryOpener.CurrentMode = CraftingWindowMode.StoredItemIMGUI;
        }
    }

    // =========================================================================
    // 3. LE GESTIONNAIRE D'INVENTAIRE ET DE SAUVEGARDE
    // =========================================================================
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
            if (Instance == null)
            {
                Instance = new DeferredInventoryOpener();
                EventBus.Subscribe(Instance); 
            }
        }

        public void HandleDialogFinished(BlueprintDialog dialog, bool success)
        {
            if (CurrentMode != CraftingWindowMode.None)
            {
                var mode = CurrentMode;
                CurrentMode = CraftingWindowMode.None; // Reset

                Observable.Timer(TimeSpan.FromMilliseconds(200)).Subscribe(_ => 
                {
                    var player = Game.Instance.Player.MainCharacter.Value;

                    if (mode == CraftingWindowMode.LootUI)
                    {
                        IsCraftingWindowOpen = true; 
                        EntityViewBase[] targetObjects = new EntityViewBase[] { player.View };
                        Main.ModEntry.Logger.Log($"[UI] Ouverture de l'atelier persistant (Objets en stock : {CraftingBox.Items.Count})");

                        EventBus.RaiseEvent<ILootInterractionHandler>(h => 
                            h.HandleLootInterraction(player, targetObjects, LootContainerType.PlayerChest, OnInventoryClosed)
                        );
                    }
                    else if (mode == CraftingWindowMode.StoredItemIMGUI)
                    {
                        Main.ModEntry.Logger.Log("[UI] Tentative d'ouverture de l'IMGUI de modification...");
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
        }

        public void HandleItemsAdded(ItemsCollection collection, ItemEntity item, int count)
        {
            if (collection == CraftingBox)
            {
                bool isWeapon = item.Blueprint is BlueprintItemWeapon;
                bool isArmor = item.Blueprint is BlueprintItemArmor;
                // Exclut les potions et parchemins
                bool isEquipable = item.Blueprint is BlueprintItemEquipment && !(item.Blueprint is BlueprintItemEquipmentUsable);

                if (!isWeapon && !isArmor && !isEquipable)
                {
                    Main.ModEntry.Logger.Log($"[LOG AJOUT] REFUS : '{item.Name}' n'est pas un équipement valide.");
                    SpitItemBack(item);
                }
                else
                {
                    Main.ModEntry.Logger.Log($"[LOG AJOUT] ACCEPTÉ : '{item.Name}' a été ajouté à la sauvegarde.");
                }
            }
        }

        public void HandleItemsRemoved(ItemsCollection collection, ItemEntity item, int count)
        {
            if (collection == CraftingBox)
            {
                Main.ModEntry.Logger.Log($"[LOG RETRAIT] '{item.Name}' a été récupéré par le joueur.");
            }
        }

        private void SpitItemBack(ItemEntity item)
        {
            // Délai raccourci à 10ms pour un rebond visuel quasi-immédiat
            Observable.Timer(TimeSpan.FromMilliseconds(10)).Subscribe(_ => 
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
            
            // On synchronise la mémoire volatile vers la sauvegarde du jeu !
            Game.Instance.Player.MainCharacter.Value.Ensure<UnitPartWilcerWorkshop>().SyncFromBox();

            Main.ModEntry.Logger.Log($"[UI] Fermeture de l'atelier. Wilcer garde {CraftingBox.Items.Count} objets en dépôt.");
            
            if (CraftingBox.Items.Count > 0)
            {
                foreach (var item in CraftingBox.Items)
                {
                    Main.ModEntry.Logger.Log($">> EN SÉQUESTRE (Sauvegardé) : {item.Name}");
                }
            }
        }
    }
}