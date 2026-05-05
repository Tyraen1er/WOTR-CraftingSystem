using System;
using System.IO;
using Newtonsoft.Json;
using UnityModManagerNet;

namespace CraftingSystem
{
    public enum SourceFilter { TTRPG = 0, Owlcat = 1, OwlcatPlus = 2, Mods = 3, All = 4 }

    public class CraftingSettings : UnityModManager.ModSettings
    {
        public const float BUTTON_OPTION_WIDTH_BASE = 160f;
        public const float BUTTON_CLOSE_WIDTH_BASE = 80f;
        public const float EpicCostMultiplier = 10.0f;

        public float CostMultiplier = 1.0f;
        public bool InstantCrafting = false;
        public bool EnforcePointsLimit = true;
        public int MaxTotalBonus = 10;
        public int MaxEnhancementBonus = 5;
        public bool RequirePlusOneFirst = true;
        public bool ApplySlotPenalty = true;
        public bool EnableEpicCosts = true;
        public int ScalePercent = 100;
        public SourceFilter CurrentSourceFilter = SourceFilter.TTRPG;
        public bool HasOpenedCheats = false;
        public int ItemsPerPage = 15;

        // Raccourcis clavier
        public KeyBinding ShortcutInventory = new KeyBinding();
        public KeyBinding ShortcutIMGUI = new KeyBinding();

        // Instance statique pour un accès facile
        public static CraftingSettings Instance;

        private static string GetSettingsPath(UnityModManager.ModEntry modEntry) => Path.Combine(modEntry.Path, "settings.json");

        public static void Load(UnityModManager.ModEntry modEntry)
        {
            var path = GetSettingsPath(modEntry);
            if (File.Exists(path))
            {
                try
                {
                    Instance = JsonConvert.DeserializeObject<CraftingSettings>(File.ReadAllText(path));
                    if (Instance != null) return;
                }
                catch (Exception e)
                {
                    modEntry.Logger.Error($"[ATELIER] Erreur lors du chargement des paramètres : {e.Message}");
                }
            }
            
            Instance = new CraftingSettings();
            Instance.Save(modEntry);
        }

        public override void Save(UnityModManager.ModEntry modEntry)
        {
            try
            {
                var path = GetSettingsPath(modEntry);
                string json = JsonConvert.SerializeObject(this, Formatting.Indented);
                File.WriteAllText(path, json);
            }
            catch (Exception e)
            {
                modEntry.Logger.Error($"[ATELIER] Erreur lors de la sauvegarde des paramètres : {e.Message}");
            }
        }

        // --- Rétro-compatibilité pour le reste du code ---
        public static float CostMultiplier_Static => Instance.CostMultiplier;
        // On pourrait ajouter des propriétés statiques si nécessaire, 
        // mais il vaut mieux mettre à jour les appels vers CraftingSettings.Instance.Field
    }
}
