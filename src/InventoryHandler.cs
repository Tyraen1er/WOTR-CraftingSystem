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
using System.Reflection;
using Kingmaker.Localization;
using UnityModManagerNet;

namespace CraftingSystem
{
    public enum CraftingWindowMode { None, LootUI, StoredItemIMGUI }

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

            RequestUI(mode, 0.3f);
        }

        public static CraftingWindowMode PendingUIRequest = CraftingWindowMode.None;
        public static float PendingUITimer = 0f;

        public static void RequestUI(CraftingWindowMode mode, float delay = 0.3f)
        {
            PendingUIRequest = mode;
            PendingUITimer = delay;
        }

        public static void OpenUI(CraftingWindowMode mode)
        {
            try
            {
                if (Instance == null) Initialize();
                Main.ModEntry.Logger.Log($"[ATELIER-DEBUG] Exécution de OpenUI sur le Main Thread pour le mode : {mode}");

                if (Game.Instance == null || Game.Instance.Player == null || Game.Instance.Player.MainCharacter == null)
                {
                    Main.ModEntry.Logger.Log("[ATELIER-DEBUG] ERREUR : Game.Instance, Player ou MainCharacter est null.");
                    return;
                }

                var player = Game.Instance.Player.MainCharacter.Value;
                if (player == null)
                {
                    Main.ModEntry.Logger.Log("[ATELIER-DEBUG] ERREUR : Le personnage principal (player) est null.");
                    return;
                }

                if (mode == CraftingWindowMode.LootUI)
                {
                    Main.ModEntry.Logger.Log("[ATELIER-DEBUG] Lancement de l'événement ILootInterractionHandler...");
                    IsCraftingWindowOpen = true; 
                    EventBus.RaiseEvent<ILootInterractionHandler>(h => 
                        h.HandleLootInterraction(player, new EntityViewBase[] { player.View }, LootContainerType.PlayerChest, Instance.OnInventoryClosed)
                    );
                    Main.ModEntry.Logger.Log("[ATELIER-DEBUG] Événement ILootInterractionHandler envoyé avec succès.");
                }
                else if (mode == CraftingWindowMode.StoredItemIMGUI)
                {
                    Main.ModEntry.Logger.Log("[ATELIER-DEBUG] Ouverture de l'IMGUI...");
                    if (CraftingUI.Instance == null)
                    {
                        var go = new UnityEngine.GameObject("CraftingSystem_UI");
                        UnityEngine.Object.DontDestroyOnLoad(go);
                        go.AddComponent<CraftingUI>();
                    }
                    if (CraftingUI.Instance != null) CraftingUI.Instance.IsOpen = true;
                    Main.ModEntry.Logger.Log("[ATELIER-DEBUG] IMGUI ouvert avec succès.");
                }
            }
            catch (Exception ex)
            {
                Main.ModEntry.Logger.Error($"[ATELIER-DEBUG] EXCEPTION CRITIQUE dans OpenUI : {ex}");
            }
        }

        public void HandleItemsAdded(ItemsCollection collection, ItemEntity item, int count)
        {
            if (collection != CraftingBox) return;

            // (La séparation des piles est désormais gérée par ItemsCollection_Add_Split_Patch)

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

        public void OnInventoryClosed()
        {
            Main.ModEntry.Logger.Log($"[ATELIER-DEBUG] OnInventoryClosed appelé ! IsCraftingWindowOpen était : {IsCraftingWindowOpen}");
            IsCraftingWindowOpen = false; 
            Game.Instance.Player.MainCharacter.Value.Ensure<UnitPartWilcerWorkshop>().SyncFromBox();
        }
    }

    // --- BLOCAGE INPUTS ---
    [HarmonyPatch(typeof(Kingmaker.Controllers.Clicks.PointerController), "Tick")]
    public static class PointerController_Block_Patch
    {
        public static bool Prefix()
        {
            if (CraftingUI.Instance != null && CraftingUI.Instance.IsOpen) return false;
            return true;
        }
    }

    [HarmonyPatch(typeof(Kingmaker.Controllers.Rest.CameraController), "Tick")]
    public static class RestCameraController_Block_Patch
    {
        public static bool Prefix()
        {
            if (CraftingUI.Instance != null && CraftingUI.Instance.IsOpen) return false;
            return true;
        }
    }

    [HarmonyPatch(typeof(Kingmaker.UI._ConsoleUI.InputLayers.InGameLayer.InGameInputLayer), "UpdateMovement")]
    public static class GamepadMovement_Block_Patch
    {
        public static bool Prefix()
        {
            if (CraftingUI.Instance != null && CraftingUI.Instance.IsOpen) return false;
            return true;
        }
    }

    [HarmonyPatch(typeof(Kingmaker.UI._ConsoleUI.InputLayers.InGameLayer.InGameInputLayer), "OnInteract")]
    public static class GamepadInteraction_Block_Patch
    {
        public static bool Prefix()
        {
            if (CraftingUI.Instance != null && CraftingUI.Instance.IsOpen) return false;
            return true;
        }
    }

    [HarmonyPatch(typeof(Kingmaker.UI._ConsoleUI.InputLayers.InGameLayer.InGameInputLayer), "OnMoveRightStick")]
    public static class GamepadRightStick_Scroll_Patch
    {
        public static bool Prefix(UnityEngine.Vector2 vec)
        {
            if (CraftingUI.Instance != null && CraftingUI.Instance.IsOpen)
            {
                CraftingUI.Instance.RightStickScrollAmount = vec.y;
                return false;
            }
            return true;
        }
    }

    // --- EMPÊCHER LE STACKING VIA CanBeMerged ---
    [HarmonyPatch(typeof(ItemEntity), nameof(ItemEntity.CanBeMerged), new Type[] { typeof(ItemEntity) })]
    public static class ItemEntity_NoMerge_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(ItemEntity __instance, ItemEntity other, ref bool __result)
        {
            // Si l'un des deux items appartient à notre coffre d'artisanat, on interdit la fusion.
            if ((__instance.Collection != null && __instance.Collection == DeferredInventoryOpener.CraftingBox) ||
                (other.Collection != null && other.Collection == DeferredInventoryOpener.CraftingBox))
            {
                __result = false;
                return false;
            }
            return true;
        }
    }

    // --- SÉPARATION SYNCHRONE AVANT L'AJOUT DANS LE COFFRE ---
    [HarmonyPatch(typeof(Kingmaker.Items.ItemsCollection), nameof(Kingmaker.Items.ItemsCollection.Add), new Type[] { typeof(Kingmaker.Items.ItemEntity), typeof(bool) })]
    public static class ItemsCollection_Add_Split_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(Kingmaker.Items.ItemsCollection __instance, ItemEntity newItem)
        {
            if (__instance == DeferredInventoryOpener.CraftingBox && newItem.Count > 1)
            {
                // On extrait tout sauf 1
                int toReturn = newItem.Count - 1;
                var leftover = newItem.Split(toReturn);
                
                // On renvoie le reste au joueur immédiatement
                // L'UI de l'inventaire se mettra à jour normalement
                Kingmaker.Game.Instance.Player.Inventory.Add(leftover);
                
                // La référence 'newItem' originale (qui est trackée par l'UI pendant le drag&drop)
                // ne contient plus qu'un seul exemplaire. On la laisse s'ajouter au coffre.
            }
            return true;
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(Kingmaker.Items.ItemsCollection), nameof(Kingmaker.Items.ItemsCollection.Remove), new Type[] { typeof(Kingmaker.Items.ItemEntity), typeof(int) })]
    public static class ItemsCollection_Remove_Security_Patch
    {
        public static bool Prefix(Kingmaker.Items.ItemsCollection __instance, Kingmaker.Items.ItemEntity item)
        {
            // 1. On vérifie si la collection est celle de la forge
            if (__instance == DeferredInventoryOpener.CraftingBox)
            {


                // Accès sécurisé au joueur
                var player = Kingmaker.Game.Instance.Player.MainCharacter.Value;
                if (player == null) return true;

                var workshop = player.Get<UnitPartWilcerWorkshop>();
                
                // 2. Si l'objet est en cours de forge, on bloque !
                if (workshop != null && workshop.ActiveProjects.Any(p => p.Item == item))
                {
                    Kingmaker.PubSubSystem.EventBus.RaiseEvent<Kingmaker.PubSubSystem.IWarningNotificationUIHandler>(
                        h => h.HandleWarning("FORGE : Cet équipement est en cours de modification !")
                    );

                    // Main.ModEntry.Logger.Log($"[SÉCURITÉ] Retrait bloqué pour {item.Name} car un projet est actif.");
                    return false; 
                }
            }
            return true; // Autorise le retrait pour tout le reste
        }
    }

    [HarmonyPatch(typeof(Kingmaker.UI.MVVM._VM.Loot.LootVM), MethodType.Constructor, new Type[] { typeof(Kingmaker.UI.MVVM._VM.Loot.LootContextVM.LootWindowMode), typeof(Kingmaker.View.EntityViewBase[]), typeof(Action) })]
    public static class LootVM_Crafting_Patch
    {
        public static void Postfix(Kingmaker.UI.MVVM._VM.Loot.LootVM __instance)
        {
            Main.ModEntry.Logger.Log($"[ATELIER-DEBUG] Un LootVM vient d'être instancié. IsCraftingWindowOpen = {DeferredInventoryOpener.IsCraftingWindowOpen}");
            if (DeferredInventoryOpener.IsCraftingWindowOpen)
            {
                Main.ModEntry.Logger.Log("[ATELIER-DEBUG] LootVM intercepté avec succès, injection de l'Atelier...");
                var mockLoot = new Kingmaker.UI.MVVM._VM.Loot.LootObjectVM("Atelier", "Enchantement", DeferredInventoryOpener.CraftingBox, Kingmaker.UI.MVVM._VM.Loot.LootContextVM.LootWindowMode.PlayerChest, 1);
                var prop = typeof(Kingmaker.UI.MVVM._VM.Loot.LootVM).GetProperty("LootObjects") ?? typeof(Kingmaker.UI.MVVM._VM.Loot.LootVM).GetProperty("ContextLoot");
                if (prop != null)
                {
                    var coll = prop.GetValue(__instance);
                    if (coll != null) {
                        coll.GetType().GetMethod("Clear")?.Invoke(coll, null);
                        coll.GetType().GetMethod("Add")?.Invoke(coll, new object[] { mockLoot });
                    }
                }
            }
        }
    }
}