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
using Kingmaker.UnitLogic.Buffs;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.UnitLogic.Mechanics.Actions;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.ElementsSystem;
using Kingmaker.Utility;
using Kingmaker.UnitLogic;
using Kingmaker.Visual.Particles;
using Kingmaker.ResourceLinks;




namespace CraftingSystem
{
    static class Main
    {
        public static UnityModManager.ModEntry ModEntry;
        public static Harmony HarmonyInstance;
        public static UnityModManager.ModEntry.ModLogger log => ModEntry.Logger;

        private static string widthString = null;
        private static string heightString = null;

        static bool Load(UnityModManager.ModEntry modEntry)
        {
            try
            {
                ModEntry = modEntry;
                CraftingSettings.Load(modEntry);

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
            }
            catch (Exception e)
            {
                modEntry.Logger.Error($"Crafting System: Failed to load: {e}");
                return false;
            }
        }

        static void OnGUI(UnityModManager.ModEntry modEntry)
        {
            if (CraftingSettings.Instance.WindowWidth <= 0f)
            {
                CraftingSettings.Instance.WindowWidth = CraftingSettings.GetDefaultWidth();
                CraftingSettings.Instance.Save(modEntry);
            }
            if (CraftingSettings.Instance.WindowHeight <= 0f)
            {
                CraftingSettings.Instance.WindowHeight = CraftingSettings.GetDefaultHeight();
                CraftingSettings.Instance.Save(modEntry);
            }

            UnityEngine.GUILayout.Label(Helpers.GetString("ui_umm_title"));

            UnityEngine.GUILayout.BeginHorizontal();
            UnityEngine.GUILayout.Label(Helpers.GetString("ui_umm_vanilla"), UnityEngine.GUILayout.Width(250));
            if (UnityEngine.GUILayout.Button(Helpers.GetString("ui_umm_open_now"), UnityEngine.GUILayout.Width(250)))
            {
                if (UnityModManager.UI.Instance != null && UnityModManager.UI.Instance.Opened) UnityModManager.UI.Instance.ToggleWindow();
                DeferredInventoryOpener.RequestUI(CraftingWindowMode.LootUI, 0.2f);
            }
            UnityEngine.GUILayout.Space(20);
            UnityModManager.UI.DrawKeybindingSmart(CraftingSettings.Instance.ShortcutInventory, Helpers.GetString("ui_umm_shortcut") + " ", (kb) => CraftingSettings.Instance.Save(modEntry), null, UnityEngine.GUILayout.Width(150));
            UnityEngine.GUILayout.EndHorizontal();

            UnityEngine.GUILayout.BeginHorizontal();
            UnityEngine.GUILayout.Label(Helpers.GetString("ui_umm_imgui"), UnityEngine.GUILayout.Width(250));
            if (UnityEngine.GUILayout.Button(Helpers.GetString("ui_umm_open_now"), UnityEngine.GUILayout.Width(250)))
            {
                if (UnityModManager.UI.Instance != null && UnityModManager.UI.Instance.Opened) UnityModManager.UI.Instance.ToggleWindow();
                DeferredInventoryOpener.RequestUI(CraftingWindowMode.StoredItemIMGUI, 0.2f);
            }
            UnityEngine.GUILayout.Space(20);
            UnityModManager.UI.DrawKeybindingSmart(CraftingSettings.Instance.ShortcutIMGUI, Helpers.GetString("ui_umm_shortcut") + "  ", (kb) => CraftingSettings.Instance.Save(modEntry), null, UnityEngine.GUILayout.Width(150));
            UnityEngine.GUILayout.EndHorizontal();

            UnityEngine.GUILayout.Space(10);
            UnityEngine.GUILayout.Label("<b>Positionnement & Dimensions IMGUI :</b>");

            // Largeur de la fenêtre
            UnityEngine.GUILayout.BeginHorizontal();
            UnityEngine.GUILayout.Label(Helpers.GetString("ui_umm_window_width") + " : ", UnityEngine.GUILayout.Width(180));
            if (UnityEngine.GUILayout.Button("Reset", UnityEngine.GUILayout.Width(100)))
            {
                CraftingSettings.Instance.WindowWidth = CraftingSettings.GetDefaultWidth();
                widthString = CraftingSettings.Instance.WindowWidth.ToString(System.Globalization.CultureInfo.InvariantCulture);
                CraftingSettings.Instance.Save(modEntry);
            }
            UnityEngine.GUILayout.Space(10);
            if (widthString == null)
            {
                widthString = CraftingSettings.Instance.WindowWidth.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            string newWidthStr = UnityEngine.GUILayout.TextField(widthString, UnityEngine.GUILayout.Width(100));
            if (newWidthStr != widthString)
            {
                widthString = newWidthStr;
                bool parsed = float.TryParse(newWidthStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out float val);
                if (!parsed)
                {
                    parsed = float.TryParse(newWidthStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.CurrentCulture, out val);
                }
                if (parsed)
                {
                    float valClamped;
                    if (val > 100f)
                    {
                        valClamped = UnityEngine.Mathf.Clamp(val, 800f, 2000f);
                    }
                    else
                    {
                        valClamped = UnityEngine.Mathf.Clamp(val, 30f, 100f);
                    }
                    if (valClamped != CraftingSettings.Instance.WindowWidth)
                    {
                        CraftingSettings.Instance.WindowWidth = valClamped;
                        CraftingSettings.Instance.Save(modEntry);
                    }
                }
            }
            string displayUnitWidth = (CraftingSettings.Instance.WindowWidth > 100f) ? "px" : "%";
            UnityEngine.GUILayout.Label($"{CraftingSettings.Instance.WindowWidth} {displayUnitWidth}", UnityEngine.GUILayout.Width(80));
            UnityEngine.GUILayout.EndHorizontal();

            // Hauteur de la fenêtre
            UnityEngine.GUILayout.BeginHorizontal();
            UnityEngine.GUILayout.Label(Helpers.GetString("ui_umm_window_height") + " : ", UnityEngine.GUILayout.Width(180));
            if (UnityEngine.GUILayout.Button("Reset", UnityEngine.GUILayout.Width(100)))
            {
                CraftingSettings.Instance.WindowHeight = CraftingSettings.GetDefaultHeight();
                heightString = CraftingSettings.Instance.WindowHeight.ToString(System.Globalization.CultureInfo.InvariantCulture);
                CraftingSettings.Instance.Save(modEntry);
            }
            UnityEngine.GUILayout.Space(10);
            if (heightString == null)
            {
                heightString = CraftingSettings.Instance.WindowHeight.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            string newHeightStr = UnityEngine.GUILayout.TextField(heightString, UnityEngine.GUILayout.Width(100));
            if (newHeightStr != heightString)
            {
                heightString = newHeightStr;
                bool parsed = float.TryParse(newHeightStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out float val);
                if (!parsed)
                {
                    parsed = float.TryParse(newHeightStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.CurrentCulture, out val);
                }
                if (parsed)
                {
                    float valClamped;
                    if (val > 100f)
                    {
                        valClamped = UnityEngine.Mathf.Clamp(val, 600f, 1600f);
                    }
                    else
                    {
                        valClamped = UnityEngine.Mathf.Clamp(val, 30f, 100f);
                    }
                    if (valClamped != CraftingSettings.Instance.WindowHeight)
                    {
                        CraftingSettings.Instance.WindowHeight = valClamped;
                        CraftingSettings.Instance.Save(modEntry);
                    }
                }
            }
            string displayUnitHeight = (CraftingSettings.Instance.WindowHeight > 100f) ? "px" : "%";
            UnityEngine.GUILayout.Label($"{CraftingSettings.Instance.WindowHeight} {displayUnitHeight}", UnityEngine.GUILayout.Width(80));
            UnityEngine.GUILayout.EndHorizontal();

            // Modificateur de scale
            UnityEngine.GUILayout.BeginHorizontal();
            UnityEngine.GUILayout.Label(Helpers.GetString("ui_umm_scale_modifier") + " : ", UnityEngine.GUILayout.Width(180));
            if (UnityEngine.GUILayout.Button("Reset", UnityEngine.GUILayout.Width(100)))
            {
                CraftingSettings.Instance.ScaleModifier = 1.0f;
                CraftingSettings.Instance.Save(modEntry);
            }
            UnityEngine.GUILayout.Space(10);
            float oldScaleMod = CraftingSettings.Instance.ScaleModifier;
            float newScaleMod = UnityEngine.GUILayout.HorizontalSlider(oldScaleMod, 0.5f, 2.0f, UnityEngine.GUILayout.Width(200));
            newScaleMod = (float)Math.Round(newScaleMod, 2);
            UnityEngine.GUILayout.Space(10);
            UnityEngine.GUILayout.Label($"{newScaleMod:F2}x", UnityEngine.GUILayout.Width(80));
            if (newScaleMod != oldScaleMod)
            {
                CraftingSettings.Instance.ScaleModifier = newScaleMod;
                CraftingSettings.Instance.Save(modEntry);
            }
            UnityEngine.GUILayout.EndHorizontal();
        }

        static void OnSaveGUI(UnityModManager.ModEntry modEntry)
        {
            CraftingSettings.Instance.Save(modEntry);
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

            if (CraftingSettings.Instance.ShortcutInventory != null && CraftingSettings.Instance.ShortcutInventory.Down())
            {
                // Main.log.Log($"[SHORTCUT] Inventory triggered. FocusID: {UnityEngine.GUIUtility.keyboardControl}, GameMode: {Game.Instance.CurrentMode}");
                if (UnityModManager.UI.Instance != null && UnityModManager.UI.Instance.Opened) UnityModManager.UI.Instance.ToggleWindow();
                DeferredInventoryOpener.RequestUI(CraftingWindowMode.LootUI, 0.3f);
            }
            if (CraftingSettings.Instance.ShortcutIMGUI != null && CraftingSettings.Instance.ShortcutIMGUI.Down())
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
            // Main.ModEntry.Logger.Log("[DEBUG] BlueprintsCache.Init Postfix started.");
            try
            {
                string locale = "enGB";
                try
                {
                    locale = Kingmaker.Localization.LocalizationManager.CurrentLocale.ToString();
                }
                catch
                {
                    Main.ModEntry.Logger.Log("LocalizationManager not fully initialized yet.");
                }

                Helpers.ApplyLocalization(locale);

                if (Kingmaker.Localization.LocalizationManager.CurrentPack != null)
                {
                    Helpers.InjectStringsIntoPack(Kingmaker.Localization.LocalizationManager.CurrentPack);
                }

                DialogInjector.RegisterDialogChanges();

                // --- CHARGEMENT SIMPLE JSON AU DÉMARRAGE ---
                EnchantmentScanner.Load();

                // --- INJECTION DES ENCHANTEMENTS CUSTOM (JSON COMPLEXE) ---
                CustomEnchantmentsBuilder.BuildAndInjectAll();
            }
            catch (Exception ex)
            {
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
                if (string.IsNullOrEmpty(guidStr) || guidStr.Length < 8) return true;

                // Optimisation : recherche rapide de la signature au début (0) ou après !bp_ (4)
                int signatureIdx = -1;
                if (guidStr.StartsWith("c2af", StringComparison.OrdinalIgnoreCase)) signatureIdx = 0;
                else if (guidStr.Length > 8 && guidStr.StartsWith("!bp_c2af", StringComparison.OrdinalIgnoreCase)) signatureIdx = 4;

                if (signatureIdx != -1)
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
                if (LocalizationManager.CurrentPack != null)
                {
                    Helpers.ApplyLocalization(LocalizationManager.CurrentLocale.ToString());
                    Helpers.InjectStringsIntoPack(LocalizationManager.CurrentPack);
                }
            }
        }

        [HarmonyPatch(typeof(ContextActionKill), "RunAction")]
        public static class ContextActionKill_RunAction_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix(ContextActionKill __instance)
            {
                try
                {
                    var currentData = ContextData<MechanicsContext.Data>.Current;
                    var context = currentData?.Context;
                    var target = currentData?.CurrentTarget;

                    if (context != null && (context.AssociatedBlueprint?.AssetGuid.ToString() == "4c02715a54a497a408a93a5d80e91a24" || context.AssociatedBlueprint?.name?.ToLowerInvariant().Contains("vorpal") == true))
                    {
                        var targetUnit = target?.Unit;
                        var caster = context?.MaybeCaster;

                        if (targetUnit != null && caster != null && targetUnit == caster)
                        {
                            Main.log.Log($"[Vorpal Protection] Blocked self-kill on wielder: {targetUnit.CharacterName}");
                            return false; // Skip the kill action
                        }
                    }
                }
                catch (Exception ex)
                {
                    Main.log.Error($"[Vorpal Protection] Error in patch: {ex}");
                }
                return true; // Execute original kill action
            }
        }
    }

