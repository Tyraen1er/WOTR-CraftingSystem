using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using UnityModManagerNet;

namespace CraftingSystem
{
    // L'Enumération des filtres (déplacée depuis CraftingUI)
    public enum SourceFilter { TTRPG = 0, Owlcat = 1, OwlcatPlus = 2, Mods = 3, All = 4 }

    public static class CraftingSettings
    {
        // =========================================================================
        // VARIABLES GLOBALES (Accessibles de partout via CraftingSettings.Variable)
        // =========================================================================
        public static float CostMultiplier = 1.0f;
        public static bool InstantCrafting = false;
        public static bool EnforcePointsLimit = true;
        public static int MaxTotalBonus = 10;
        public static int MaxEnhancementBonus = 5;
        public static bool RequirePlusOneFirst = true;
        public static bool ApplySlotPenalty = true;
        public static bool EnableEpicCosts = true;
        public const float EpicCostMultiplier = 10.0f;
        public static int ScalePercent = 100;
        public static SourceFilter CurrentSourceFilter = SourceFilter.TTRPG;

        // Raccourcis clavier (Synchronisés avec settings.json)
        public static KeyBinding ShortcutInventory = new KeyBinding();
        public static KeyBinding ShortcutIMGUI = new KeyBinding();

        // Constantes d'interface
        public const float BUTTON_OPTION_WIDTH_BASE = 160f;
        public const float BUTTON_CLOSE_WIDTH_BASE = 80f;
        private const string SETTINGS_FILENAME = "settings.json";

        // =========================================================================
        // CLASSE PRIVÉE POUR LA SÉRIALISATION (Ce qui est écrit dans le fichier)
        // =========================================================================
        [DataContract]
        private class UiSettingsData
        {
            [DataMember] public float CostMultiplier = 1.0f;
            [DataMember] public bool InstantCrafting = false;
            [DataMember] public bool EnforcePointsLimit = true;
            [DataMember] public int MaxTotalBonus = 10;
            [DataMember] public int MaxEnhancementBonus = 5;
            [DataMember] public bool RequirePlusOneFirst = true;
            [DataMember] public bool ApplySlotPenalty = true;
            [DataMember] public bool EnableEpicCosts = true;
            [DataMember] public int ScalePercent = 100;
            [DataMember] public float OptionButtonBase = 160f;
            [DataMember] public float CloseButtonBase = 80f;
            [DataMember] public SourceFilter SourceFilterValue = SourceFilter.TTRPG;
            [DataMember] public int ShortcutInventoryKey = 0;
            [DataMember] public int ShortcutInventoryMod = 0;
            [DataMember] public int ShortcutIMGUIKey = 0;
            [DataMember] public int ShortcutIMGUIMod = 0;
        }

        // =========================================================================
        // MÉTHODES DE CHARGEMENT / SAUVEGARDE
        // =========================================================================
        public static void LoadSettings()
        {
            try
            {
                if (Main.ModEntry == null || string.IsNullOrEmpty(Main.ModEntry.Path)) return;
                var path = Path.Combine(Main.ModEntry.Path, SETTINGS_FILENAME);
                if (!File.Exists(path))
                {
                    SaveSettings(); // Créer le fichier par défaut s'il n'existe pas
                    return;
                }

                using (var fs = File.OpenRead(path))
                {
                    var serializer = new DataContractJsonSerializer(typeof(UiSettingsData));
                    var obj = serializer.ReadObject(fs) as UiSettingsData;
                    if (obj == null) return;

                    CostMultiplier = obj.CostMultiplier;
                    InstantCrafting = obj.InstantCrafting;
                    EnforcePointsLimit = obj.EnforcePointsLimit;
                    MaxTotalBonus = obj.MaxTotalBonus;
                    MaxEnhancementBonus = obj.MaxEnhancementBonus;
                    RequirePlusOneFirst = obj.RequirePlusOneFirst;
                    ApplySlotPenalty = obj.ApplySlotPenalty;
                    EnableEpicCosts = obj.EnableEpicCosts;
                    if (obj.ScalePercent > 0) ScalePercent = obj.ScalePercent;
                    CurrentSourceFilter = obj.SourceFilterValue;

                    // Reconstitution des KeyBindings
                    ShortcutInventory = new KeyBinding { keyCode = (UnityEngine.KeyCode)obj.ShortcutInventoryKey, modifiers = (byte)obj.ShortcutInventoryMod };
                    ShortcutIMGUI = new KeyBinding { keyCode = (UnityEngine.KeyCode)obj.ShortcutIMGUIKey, modifiers = (byte)obj.ShortcutIMGUIMod };
                }
            }
            catch (Exception ex) { Main.ModEntry.Logger.Error($"[ATELIER] Failed to load UI settings: {ex}"); }
        }

        public static void SaveSettings()
        {
            try
            {
                if (Main.ModEntry == null || string.IsNullOrEmpty(Main.ModEntry.Path)) return;
                var path = Path.Combine(Main.ModEntry.Path, SETTINGS_FILENAME);

                var s = new UiSettingsData
                {
                    CostMultiplier = CostMultiplier,
                    InstantCrafting = InstantCrafting,
                    EnforcePointsLimit = EnforcePointsLimit,
                    MaxTotalBonus = MaxTotalBonus,
                    MaxEnhancementBonus = MaxEnhancementBonus,
                    RequirePlusOneFirst = RequirePlusOneFirst,
                    ApplySlotPenalty = ApplySlotPenalty,
                    EnableEpicCosts = EnableEpicCosts,
                    ScalePercent = ScalePercent,
                    OptionButtonBase = BUTTON_OPTION_WIDTH_BASE,
                    CloseButtonBase = BUTTON_CLOSE_WIDTH_BASE,
                    SourceFilterValue = CurrentSourceFilter,
                    ShortcutInventoryKey = (int)ShortcutInventory.keyCode,
                    ShortcutInventoryMod = ShortcutInventory.modifiers,
                    ShortcutIMGUIKey = (int)ShortcutIMGUI.keyCode,
                    ShortcutIMGUIMod = ShortcutIMGUI.modifiers
                };

                using (var ms = new MemoryStream())
                {
                    var serializer = new DataContractJsonSerializer(typeof(UiSettingsData));
                    serializer.WriteObject(ms, s);
                    ms.Position = 0;
                    using (var sr = new StreamReader(ms, Encoding.UTF8))
                    {
                        var json = sr.ReadToEnd();
                        File.WriteAllText(path, json);
                    }
                }
            }
            catch (Exception ex) { Main.ModEntry.Logger.Error($"[ATELIER] Failed to save UI settings: {ex}"); }
        }
    }
}