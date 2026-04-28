using System;
using System.Reflection;
using System.Collections.Generic;
using HarmonyLib;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.JsonSystem;
using Kingmaker.DialogSystem.Blueprints;
using Kingmaker.Localization;
using Kingmaker.Items;
using UnityModManagerNet;
using Kingmaker;
using Kingmaker.PubSubSystem;
using Kingmaker.UI.Models.Log;
using Kingmaker.Blueprints.Root;
using System.Linq;

namespace CraftingSystem
{
    static class Main 
    {
        public static UnityModManager.ModEntry ModEntry;
        public static Harmony HarmonyInstance;
        public static UnityModManager.ModEntry.ModLogger log => ModEntry.Logger;

        static bool Load(UnityModManager.ModEntry modEntry)
        {
            try {
                ModEntry = modEntry;
                CraftingSettings.LoadSettings();
                
                modEntry.OnGUI = OnGUI;
                modEntry.OnSaveGUI = OnSaveGUI;
                modEntry.OnUpdate = OnUpdate;

                HarmonyInstance = new Harmony(modEntry.Info.Id);
                HarmonyInstance.PatchAll(Assembly.GetExecutingAssembly());
                
                Helpers.LoadLocalization(modEntry.Path);
                DeferredInventoryOpener.Initialize();

                ModEntry.Logger.Log("Crafting System: Mod loaded and Harmony patched.");
                return true;
            } catch (Exception e) {
                modEntry.Logger.Error($"Crafting System: Failed to load: {e}");
                return false;
            }
        }

        static void OnGUI(UnityModManager.ModEntry modEntry)
        {
            UnityEngine.GUILayout.Label(Helpers.GetString("ui_umm_title"));
            
            UnityEngine.GUILayout.BeginHorizontal();
            UnityEngine.GUILayout.Label(Helpers.GetString("ui_umm_vanilla"), UnityEngine.GUILayout.Width(250));
            if (UnityEngine.GUILayout.Button(Helpers.GetString("ui_umm_open_now"), UnityEngine.GUILayout.Width(150)))
            {
                if (UnityModManager.UI.Instance != null && UnityModManager.UI.Instance.Opened) UnityModManager.UI.Instance.ToggleWindow();
                DeferredInventoryOpener.RequestUI(CraftingWindowMode.LootUI, 0.2f);
            }
            UnityEngine.GUILayout.Space(20);
            UnityModManager.UI.DrawKeybinding(ref CraftingSettings.ShortcutInventory, Helpers.GetString("ui_umm_shortcut"), null, UnityEngine.GUILayout.Width(150));
            UnityEngine.GUILayout.EndHorizontal();

            UnityEngine.GUILayout.BeginHorizontal();
            UnityEngine.GUILayout.Label(Helpers.GetString("ui_umm_imgui"), UnityEngine.GUILayout.Width(250));
            if (UnityEngine.GUILayout.Button(Helpers.GetString("ui_umm_open_now"), UnityEngine.GUILayout.Width(150)))
            {
                if (UnityModManager.UI.Instance != null && UnityModManager.UI.Instance.Opened) UnityModManager.UI.Instance.ToggleWindow();
                DeferredInventoryOpener.RequestUI(CraftingWindowMode.StoredItemIMGUI, 0.2f);
            }
            UnityEngine.GUILayout.Space(20);
            UnityModManager.UI.DrawKeybinding(ref CraftingSettings.ShortcutIMGUI, Helpers.GetString("ui_umm_shortcut"), null, UnityEngine.GUILayout.Width(150));
            UnityEngine.GUILayout.EndHorizontal();
        }

        static void OnSaveGUI(UnityModManager.ModEntry modEntry)
        {
            CraftingSettings.SaveSettings();
        }

        static void OnUpdate(UnityModManager.ModEntry modEntry, float deltaTime)
        {
            if (DeferredInventoryOpener.PendingUIRequest != CraftingWindowMode.None)
            {
                // On utilise Time.unscaledDeltaTime car l'UMM peut mettre Time.timeScale à 0
                DeferredInventoryOpener.PendingUITimer -= UnityEngine.Time.unscaledDeltaTime;
                if (DeferredInventoryOpener.PendingUITimer <= 0)
                {
                    var mode = DeferredInventoryOpener.PendingUIRequest;
                    DeferredInventoryOpener.PendingUIRequest = CraftingWindowMode.None;
                    DeferredInventoryOpener.OpenUI(mode);
                }
            }

            if (CraftingSettings.ShortcutInventory != null && CraftingSettings.ShortcutInventory.Down())
            {
                if (UnityModManager.UI.Instance != null && UnityModManager.UI.Instance.Opened) UnityModManager.UI.Instance.ToggleWindow();
                DeferredInventoryOpener.RequestUI(CraftingWindowMode.LootUI, 0.3f);
            }
            if (CraftingSettings.ShortcutIMGUI != null && CraftingSettings.ShortcutIMGUI.Down())
            {
                if (UnityModManager.UI.Instance != null && UnityModManager.UI.Instance.Opened) UnityModManager.UI.Instance.ToggleWindow();
                DeferredInventoryOpener.RequestUI(CraftingWindowMode.StoredItemIMGUI, 0.3f);
            }
        }
    }

    [HarmonyPatch(typeof(BlueprintsCache), "Init")]
    public static class BlueprintsCache_Init_Patch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            try {
                string locale = "enGB";
                try {
                    locale = Kingmaker.Localization.LocalizationManager.CurrentLocale.ToString();
                } catch {
                    Main.ModEntry.Logger.Log("LocalizationManager not fully initialized yet.");
                }

                Helpers.ApplyLocalization(locale);
                
                if (Kingmaker.Localization.LocalizationManager.CurrentPack != null) {
                    Helpers.InjectStringsIntoPack(Kingmaker.Localization.LocalizationManager.CurrentPack);
                }

                DialogInjector.RegisterDialogChanges();
                
                // --- CHARGEMENT SIMPLE JSON AU DÉMARRAGE ---
                Enchantmentscanner.Load();
                
                // --- INJECTION DES ENCHANTEMENTS CUSTOM (JSON COMPLEXE) ---
                CustomEnchantmentsBuilder.BuildAndInjectAll();

                // --- DUMP DES ENCHANTEMENTS ---
                // À commenter
                //EnchantmentDumper.DumpAll();
                
                // --- DUMP STORYTELLER ---
                // À commenter
                //StorytellerDumper.Initialize();
                
            } catch (Exception ex) {
                Main.ModEntry.Logger.Error($"Error in BlueprintsCache.Init: {ex}");
            }
        }
    }

    [HarmonyPatch(typeof(LocalizationManager), "OnLocaleChanged")]
    public static class LocalizationManager_OnLocaleChanged_Patch
    {
        public static void Postfix()
        {
            if (LocalizationManager.CurrentPack != null) {
                Helpers.ApplyLocalization(LocalizationManager.CurrentLocale.ToString());
                Helpers.InjectStringsIntoPack(LocalizationManager.CurrentPack);
            }
        }
    }
}
