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
                    if (CraftingUI.Instance != null) CraftingUI.Instance.IsOpen = true;
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

        private void ForceItemBackToForge(ItemEntity item)
        {
            // On augmente un peu le délai à 100ms pour être sûr que le jeu a fini son propre transfert
            UniRx.Observable.Timer(TimeSpan.FromMilliseconds(100)).Subscribe(new System.Action<long>(_ => 
            {
                try 
                {
                    if (item.Collection != CraftingBox)
                    {
                        Main.ModEntry.Logger.Log($"[SÉCURITÉ-DEBUG] Récupération de l'objet depuis : {item.Collection?.GetType().Name ?? "Inconnu"}");
                        
                        // On le retire d'où il est
                        item.Collection?.Remove(item);
                        
                        // On le remet dans la forge
                        if (!CraftingBox.Contains(item))
                        {
                            CraftingBox.Add(item);
                            Main.ModEntry.Logger.Log($"[SÉCURITÉ-DEBUG] {item.Name} remis de force dans la forge.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Main.ModEntry.Logger.Error($"[SÉCURITÉ-DEBUG] Erreur lors du retour forcé : {ex.Message}");
                }
            }));
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

    [HarmonyLib.HarmonyPatch(typeof(Kingmaker.Items.ItemsCollection), nameof(Kingmaker.Items.ItemsCollection.Remove), new Type[] { typeof(Kingmaker.Items.ItemEntity), typeof(int) })]
    public static class ItemsCollection_Remove_Security_Patch
    {
        public static bool Prefix(Kingmaker.Items.ItemsCollection __instance, Kingmaker.Items.ItemEntity item)
        {
            // 1. On vérifie si la collection est celle de la forge
            if (__instance == DeferredInventoryOpener.CraftingBox)
            {
                // DIAGNOSTIC PROFOND DEMANDÉ PAR L'UTILISATEUR
                /*
                Main.ModEntry.Logger.Log($"\n[DIAGNOSTIC] --- Analyse de l'objet retiré : {item.Name} ---");
                foreach (var e in item.Enchantments)
                {
                    Main.ModEntry.Logger.Log($"[DIAGNOSTIC] Enchantment: {e.Blueprint.name} ({e.Blueprint.AssetGuid})");
                    foreach (var c in e.Blueprint.Components)
                    {
                        if (c == null) continue;
                        var type = c.GetType();
                        Main.ModEntry.Logger.Log($"[DIAGNOSTIC]   - Component: {type.FullName}");
                        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic))
                        {
                            try {
                                Main.ModEntry.Logger.Log($"[DIAGNOSTIC]     . Field: {field.Name} = {field.GetValue(c)}");
                            } catch { }
                        }
                    }
                }
                Main.ModEntry.Logger.Log($"[DIAGNOSTIC] --- Fin de l'analyse ---\n");
                */

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

                    Main.ModEntry.Logger.Log($"[SÉCURITÉ] Retrait bloqué pour {item.Name} car un projet est actif.");
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
            if (DeferredInventoryOpener.IsCraftingWindowOpen)
            {
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