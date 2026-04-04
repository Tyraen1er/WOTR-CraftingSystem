using System;
using Kingmaker;
using Kingmaker.ElementsSystem;
using Kingmaker.Blueprints.JsonSystem;
using Kingmaker.PubSubSystem;
using Kingmaker.DialogSystem.Blueprints;
using Kingmaker.View;
using Kingmaker.View.MapObjects;
using Kingmaker.Items;
using Kingmaker.UnitLogic; // Indispensable pour créer un composant de personnage
using Kingmaker.Blueprints.Items.Weapons;
using Kingmaker.Blueprints.Items.Armors;
using Kingmaker.Blueprints.Items.Equipment;
using Newtonsoft.Json; // Indispensable pour la sauvegarde native
using UniRx;
using System.Linq;

namespace CraftingSystem
{
    // =========================================================================
    // 1. LE COMPOSANT DE SAUVEGARDE (La magie opère ici)
    // S'attache au joueur et s'intègre automatiquement dans ses sauvegardes !
    // =========================================================================
    public class UnitPartWilcerWorkshop : UnitPart
    {
        // [JsonProperty] dit au jeu : "N'oublie pas de sauvegarder cette boîte dans le fichier .zks !"
        [JsonProperty]
        public ItemsCollection CraftingBox;

        public ItemsCollection GetBox()
        {
            if (CraftingBox == null)
                CraftingBox = new ItemsCollection();
            return CraftingBox;
        }
    }

    // =========================================================================
    // 2. L'ACTION DE DIALOGUE
    // =========================================================================
    [TypeId("6d3e1f7d4e3347bdaeb88a1b6c8baab6")]
    public class OpenItemSelectorAction : GameAction
    {
        public override string GetCaption() { return "Ouverture de l'Atelier Persistant"; }

        public override void RunAction()
        {
            DeferredInventoryOpener.Initialize();
            DeferredInventoryOpener.IsWaiting = true;
        }
    }

    // =========================================================================
    // 3. LE GESTIONNAIRE D'INVENTAIRE ET DE SAUVEGARDE
    // =========================================================================
    public class DeferredInventoryOpener : IDialogFinishHandler, IItemsCollectionHandler
    {
        public static bool IsWaiting = false;
        public static bool IsCraftingWindowOpen = false; 

        // Raccourci surpuissant pour toujours pointer vers la boîte du personnage principal
        public static ItemsCollection CraftingBox
        {
            get
            {
                var mainChar = Game.Instance.Player.MainCharacter.Value;
                
                // Ensure<T> crée le sac à dos la première fois, ou le récupère s'il existe déjà !
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
            if (IsWaiting)
            {
                IsWaiting = false;
                Observable.Timer(TimeSpan.FromMilliseconds(200)).Subscribe(_ => 
                {
                    IsCraftingWindowOpen = true; 
                    var player = Game.Instance.Player.MainCharacter.Value;
                    EntityViewBase[] targetObjects = new EntityViewBase[] { player.View };

                    Main.ModEntry.Logger.Log($"[UI] Ouverture de l'atelier persistant (Objets en stock : {CraftingBox.Items.Count})");

                    EventBus.RaiseEvent<ILootInterractionHandler>(h => 
                        h.HandleLootInterraction(player, targetObjects, LootContainerType.PlayerChest, OnInventoryClosed)
                    );
                });
            }
        }

        public void HandleItemsAdded(ItemsCollection collection, ItemEntity item, int count)
        {
            if (collection == CraftingBox)
            {
                bool isWeapon = item.Blueprint is BlueprintItemWeapon;
                bool isArmor = item.Blueprint is BlueprintItemArmor;
                bool isEquipment = item.Blueprint is BlueprintItemEquipment;

                if (!isWeapon && !isArmor && !isEquipment)
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
            
            Main.ModEntry.Logger.Log($"[UI] Fermeture de l'atelier. Wilcer garde farouchement {CraftingBox.Items.Count} objets en dépôt.");
            
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