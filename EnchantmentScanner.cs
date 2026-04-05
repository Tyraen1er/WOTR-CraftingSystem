using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Kingmaker;
using Kingmaker.Blueprints;
using Kingmaker.Items;
using Kingmaker.Blueprints.Items.Ecnchantments;
using Kingmaker.Blueprints.Items.Weapons;
using Kingmaker.Blueprints.Items.Armors;
using Newtonsoft.Json;

namespace CraftingSystem
{
    public class EnchantmentData
    {
        public string Name;
        public string Guid;
        public string Type; // "Weapon" or "Armor"
        public int PointCost;
        public int GoldOverride; // -1 for formula
        public int DaysOverride; // -1 for formula
        public string Category;
        public string Description;

        [JsonIgnore]
        public BlueprintItemEnchantment Blueprint => ResourcesLibrary.TryGetBlueprint<BlueprintItemEnchantment>(Guid);
    }

    public static class EnchantmentScanner
    {
        private static List<EnchantmentData> _db = new List<EnchantmentData>();
        
        public static void Load()
        {
            try
            {
                string path = Path.Combine(Main.ModEntry.Path, "Enchantments.json");
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    _db = JsonConvert.DeserializeObject<List<EnchantmentData>>(json);
                    Main.ModEntry.Logger.Log($"[ARTISANAT] Base d'enchantements chargée : {_db.Count} entrées.");
                }
                else
                {
                    Main.ModEntry.Logger.Warning("[ARTISANAT] Fichier Enchantments.json non trouvé.");
                }

                // --- DUMP AUTOMATIQUE DÉSACTIVÉ (Décommenter pour une nouvelle recherche) ---
                // DumpAllEnchantments();
            }
            catch (Exception ex)
            {
                Main.ModEntry.Logger.Error($"Erreur lors du chargement des enchantements : {ex}");
            }
        }

        public static List<EnchantmentData> GetFor(ItemEntity item)
        {
            if (item == null) return new List<EnchantmentData>();

            bool isWeapon = item.Blueprint is Kingmaker.Blueprints.Items.Weapons.BlueprintItemWeapon;
            bool isArmor = item.Blueprint is Kingmaker.Blueprints.Items.Armors.BlueprintItemArmor;

            return _db.Where(e => 
                (isWeapon && e.Type == "Weapon") || 
                (isArmor && e.Type == "Armor")
            ).ToList();
        }

        public static EnchantmentData GetByGuid(string guid)
        {
            return _db.FirstOrDefault(e => e.Guid == guid);
        }

        public static void DumpAllEnchantments()
        {
            /*
            try
            {
                var bpCache = ResourcesLibrary.BlueprintsCache;
                if (bpCache == null) return;

                // On récupère tout ce qui est chargé dans le cache
                var allLoaded = bpCache.m_LoadedBlueprints.Values.Select(e => e.Blueprint).OfType<BlueprintItemEnchantment>().ToList();
                
                Main.ModEntry.Logger.Log($"[CRAFTING_DUMP] Extraction de {allLoaded.Count} enchantements chargés...");

                foreach (var bp in allLoaded)
                {
                    if (bp == null) continue;

                    string name = bp.name;
                    string guid = bp.AssetGuid.ToString();
                    int cost = bp.EnchantmentCost;
                    
                    // Déduction du type
                    string type = "Other";
                    if (bp is BlueprintWeaponEnchantment) type = "Weapon";
                    else if (bp is BlueprintArmorEnchantment) type = "Armor";

                    // Calcul du Rating (Inspiration ToyBox : Cost * 10, min 5)
                    int rating = Math.Max(5, cost * 10);

                    string rawDesc = bp.Description?.ToString() ?? "No description";
                    string cleanDesc = System.Text.RegularExpressions.Regex.Replace(rawDesc, "<.*?>", string.Empty).Replace("\n", " ");

                    Main.ModEntry.Logger.Log($"[CRAFTING_DUMP] | NAME: {name} | GUID: {guid} | TYPE: {type} | COST: {cost} | RATING: {rating} | DESC: {cleanDesc}");
                }

                Main.ModEntry.Logger.Log("[CRAFTING_DUMP] Extraction terminée.");
            }
            catch (Exception ex)
            {
                Main.ModEntry.Logger.Error($"[CRAFTING_DUMP] Échec de l'extraction : {ex}");
            }
            */
        }
    }
}
