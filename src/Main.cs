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

            if (Game.Instance.CurrentMode == Kingmaker.GameModes.GameModeType.FullScreenUi 
                || Game.Instance.CurrentMode == Kingmaker.GameModes.GameModeType.EscMode
                || Game.Instance.CurrentMode == Kingmaker.GameModes.GameModeType.Dialog
                || Game.Instance.CurrentMode == Kingmaker.GameModes.GameModeType.Cutscene)
            {
                return;
            }

            // Sécurité avancée : Détection de la saisie (Jeu, UMM, IMGUI)
            bool isTyping = UnityEngine.GUIUtility.keyboardControl != 0;
            
            // On vérifie aussi les InputFields de TextMeshPro (utilisés par le jeu et certains mods) via Réflexion
            try
            {
                var isFieldSelected = typeof(Kingmaker.UI.KeyboardAccess)
                    .GetMethod("IsInputFieldSelected", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)
                    ?.Invoke(null, null) as bool? ?? false;
                if (isFieldSelected) isTyping = true;
            }
            catch { }

            if (isTyping) return;

            // Sécurité : si le menu UMM est ouvert, on bloque nos raccourcis pour éviter les conflits
            if (UnityModManager.UI.Instance != null && UnityModManager.UI.Instance.Opened) return;

            if (CraftingSettings.ShortcutInventory != null && CraftingSettings.ShortcutInventory.Down())
            {
                // Main.log.Log($"[SHORTCUT] Inventory triggered. FocusID: {UnityEngine.GUIUtility.keyboardControl}, GameMode: {Game.Instance.CurrentMode}");
                if (UnityModManager.UI.Instance != null && UnityModManager.UI.Instance.Opened) UnityModManager.UI.Instance.ToggleWindow();
                DeferredInventoryOpener.RequestUI(CraftingWindowMode.LootUI, 0.3f);
            }
            if (CraftingSettings.ShortcutIMGUI != null && CraftingSettings.ShortcutIMGUI.Down())
            {
                // Main.log.Log($"[SHORTCUT] IMGUI triggered. FocusID: {UnityEngine.GUIUtility.keyboardControl}, GameMode: {Game.Instance.CurrentMode}");
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

                // --- SCAN DES SORTS POUR LES PARCHEMINS ---
                SpellScanner.ScanAll();

                // DEBUG TEMPORAIRE (Après injection pour être sûr que le cache est prêt)
                //EnchantmentDebug.DumpBlueprint("d42fc23b92c640846ac137dc26e000d4"); // Enhancement1
                //EnchantmentDebug.DumpBlueprint("f8125dcb57d3463a9a039e4631204cbe"); // Enhancement7
                //EnchantmentDebug.DumpBlueprint("dd0e096412423d646929d9b945fd6d4c"); // AcidResistance10Enchant

                // --- DUMP DES ITEMS DE BASE ---
                BaseItemDumper.DumpAll();
                
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
            // 1. Inversion de la condition (Early Exit) pour éviter l'indentation profonde
            if (reader.TokenType != Newtonsoft.Json.JsonToken.String) 
                return true;

            // 2. Cast direct : puisqu'on a vérifié le TokenType, le cast explicite est plus rapide que 'as'
            string guidStr = (string)reader.Value;
            
            if (string.IsNullOrEmpty(guidStr) || guidStr.Length < 8) 
                return true;

            // 3. Recherche de la signature
            int signatureIdx = guidStr.IndexOf("c2af", StringComparison.OrdinalIgnoreCase);
            
            // On ne traite que si la signature est au début (0) ou juste après le préfixe "!bp_" (4)
            if (signatureIdx == 0 || (signatureIdx == 4 && guidStr.StartsWith("!bp_", StringComparison.OrdinalIgnoreCase)))
            {
                string finalGuid;
                int remainingLength = guidStr.Length - signatureIdx;

                // 4. Réduction drastique des allocations (Zéro Substring inutile)
                if (remainingLength >= 32)
                {
                    // Cas idéal : on extrait directement les 32 caractères (1 seule allocation)
                    finalGuid = guidStr.Substring(signatureIdx, 32);
                }
                else
                {
                    // --- RÉPARATION DES GUIDS TRONQUÉS ---
                    // Cas de secours : on extrait le reste et on pad directement
                    finalGuid = guidStr.Substring(signatureIdx).PadRight(32, '0');

#if DEBUG
                    // Ne JAMAIS logger dans ReadJson en production (génère des strings et ralentit le jeu)
                    Main.ModEntry.Logger.Log($"[DYNAMIC_ENCHANT] Repairing truncated GUID: {finalGuid}");
#endif
                }

                // On tente la résolution dynamique
                __result = CustomEnchantmentsBuilder.GetOrBuildDynamicBlueprint(finalGuid);
                
                if (__result != null) 
                    return false; // Blueprint trouvé, on court-circuite la méthode originale
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
