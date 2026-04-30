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

                ModEntry.Logger.Log("!!! CRAFTING SYSTEM - DYNAMIC VERSION 1.7.2 - CHECKPOINT !!!");
                ModEntry.Logger.Log("Crafting System: Mod loaded with Dynamic Persistence Patch.");
                
                modEntry.OnGUI = OnGUI;
                modEntry.OnSaveGUI = OnSaveGUI;
                modEntry.OnUpdate = OnUpdate;
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
            UnityEngine.KeyCode oldInv = CraftingSettings.ShortcutInventory.keyCode;
            byte oldInvMod = CraftingSettings.ShortcutInventory.modifiers;
            UnityModManager.UI.DrawKeybindingSmart(CraftingSettings.ShortcutInventory, Helpers.GetString("ui_umm_shortcut") + " ", null, UnityEngine.GUILayout.Width(150));
            if (CraftingSettings.ShortcutInventory.keyCode != oldInv || CraftingSettings.ShortcutInventory.modifiers != oldInvMod) CraftingSettings.SaveSettings();
            UnityEngine.GUILayout.EndHorizontal();

            UnityEngine.GUILayout.BeginHorizontal();
            UnityEngine.GUILayout.Label(Helpers.GetString("ui_umm_imgui"), UnityEngine.GUILayout.Width(250));
            if (UnityEngine.GUILayout.Button(Helpers.GetString("ui_umm_open_now"), UnityEngine.GUILayout.Width(150)))
            {
                if (UnityModManager.UI.Instance != null && UnityModManager.UI.Instance.Opened) UnityModManager.UI.Instance.ToggleWindow();
                DeferredInventoryOpener.RequestUI(CraftingWindowMode.StoredItemIMGUI, 0.2f);
            }
            UnityEngine.GUILayout.Space(20);
            UnityEngine.KeyCode oldImgui = CraftingSettings.ShortcutIMGUI.keyCode;
            byte oldImguiMod = CraftingSettings.ShortcutIMGUI.modifiers;
            UnityModManager.UI.DrawKeybindingSmart(CraftingSettings.ShortcutIMGUI, Helpers.GetString("ui_umm_shortcut") + "  ", null, UnityEngine.GUILayout.Width(150));
            if (CraftingSettings.ShortcutIMGUI.keyCode != oldImgui || CraftingSettings.ShortcutIMGUI.modifiers != oldImguiMod) CraftingSettings.SaveSettings();
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
            Main.ModEntry.Logger.Log("[DEBUG] BlueprintsCache.Init Postfix started.");
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
                EnchantmentScanner.Load();
                
                // --- INJECTION DES ENCHANTEMENTS CUSTOM (JSON COMPLEXE) ---
                CustomEnchantmentsBuilder.BuildAndInjectAll();

                // DEBUG TEMPORAIRE (Après injection pour être sûr que le cache est prêt)
                EnchantmentDebug.DumpBlueprint("d42fc23b92c640846ac137dc26e000d4"); // Enhancement1
                EnchantmentDebug.DumpBlueprint("f8125dcb57d3463a9a039e4631204cbe"); // Enhancement7
                EnchantmentDebug.DumpBlueprint("dd0e096412423d646929d9b945fd6d4c"); // AcidResistance10Enchant

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

    [HarmonyPatch(typeof(Kingmaker.EntitySystem.Persistence.JsonUtility.BlueprintConverter), "ReadJson")]
    public static class BlueprintConverter_ReadJson_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(Newtonsoft.Json.JsonReader reader, ref object __result)
        {
            if (reader.TokenType == Newtonsoft.Json.JsonToken.String)
            {
                string guidStr = reader.Value as string;
                if (string.IsNullOrEmpty(guidStr) || guidStr.Length < 8) return true;

                // On cherche notre signature "C2AF" ou "c2af" dans la chaîne (peut être précédée de !bp_)
                int signatureIdx = guidStr.IndexOf("c2af", StringComparison.OrdinalIgnoreCase);
                
                if (signatureIdx >= 0)
                {
                    // On nettoie la chaîne pour ne garder que le GUID
                    string cleanGuid = guidStr.Substring(signatureIdx);
                    if (cleanGuid.Length > 32) cleanGuid = cleanGuid.Substring(0, 32);

                    // --- RÉPARATION DES GUIDS TRONQUÉS ---
                    if (cleanGuid.Length < 32)
                    {
                        Main.ModEntry.Logger.Log($"[DYNAMIC_ENCHANT] Repairing truncated GUID: {cleanGuid}");
                        cleanGuid = cleanGuid.PadRight(32, '0');
                    }

                    // On tente la résolution dynamique
                    __result = CustomEnchantmentsBuilder.GetOrBuildDynamicBlueprint(cleanGuid);
                    if (__result != null) return false;
                }
            }
            return true;
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
