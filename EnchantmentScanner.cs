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
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace CraftingSystem
{
    public class EnchantmentData
    {
        public string Name;
        public string Guid;
        public string Type; // "Weapon" or "Armor"
        public string Source = "Mod"; // "TTRPG", "Owlcat", "Mod"
        public bool IsHomebrew = false;
        public int PointCost = 0;
        public int GoldOverride = -1; 
        public int DaysOverride = -1; 
        public List<string> Categories = new List<string>();
        public string Description;

        [JsonIgnore]
        public BlueprintItemEnchantment Blueprint => ResourcesLibrary.TryGetBlueprint(BlueprintGuid.Parse(Guid)) as BlueprintItemEnchantment;
    }

    public static class EnchantmentScanner
    {
        public static List<EnchantmentData> MasterList = new List<EnchantmentData>();
        private static bool _hasSyncedThisSession = false;
        public static bool IsSyncing = false;
        public static string LastSyncMessage = "En attente de synchronisation...";

        public static void Load()
        {
            try
            {
                string path = Path.Combine(Main.ModEntry.Path, "Enchantments.json");
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    MasterList = JsonConvert.DeserializeObject<List<EnchantmentData>>(json) ?? new List<EnchantmentData>();
                    LastSyncMessage = $"JSON chargé ({MasterList.Count} entrées).";
                }
                else
                {
                    MasterList = new List<EnchantmentData>();
                    LastSyncMessage = "JSON non trouvé.";
                }
            }
            catch (Exception ex)
            {
                LastSyncMessage = $"Erreur JSON : {ex.Message}";
            }
        }

        public static void ForceSync()
        {
            _hasSyncedThisSession = false;
            StartSync();
        }

        public static void StartSync()
        {
            if (_hasSyncedThisSession || IsSyncing) return;
            
            IsSyncing = true;
            LastSyncMessage = "Synchronisation en cours...";
            
            Task.Run(() => {
                try
                {
                    var bpCache = ResourcesLibrary.BlueprintsCache;
                    if (bpCache == null) {
                        LastSyncMessage = "Erreur : Index du jeu inaccessible.";
                        return;
                    }

                    // 1. Overrides
                    var overrides = new Dictionary<string, EnchantmentData>();
                    string path = Path.Combine(Main.ModEntry.Path, "Enchantments.json");
                    if (File.Exists(path))
                    {
                        var json = File.ReadAllText(path);
                        var overrideList = JsonConvert.DeserializeObject<List<EnchantmentData>>(json) ?? new List<EnchantmentData>();
                        foreach (var ov in overrideList) overrides[ov.Guid] = ov;
                    }

                    // 2. Scan
                    var syncedList = new List<EnchantmentData>();
                    var allGuids = bpCache.m_LoadedBlueprints.Keys.ToList();

                    foreach (var guid in allGuids)
                    {
                        var bpRaw = ResourcesLibrary.TryGetBlueprint(guid);
                        if (bpRaw is BlueprintItemEnchantment bp)
                        {
                            string guidStr = guid.ToString();
                            if (overrides.TryGetValue(guidStr, out var ovData))
                            {
                                syncedList.Add(ovData);
                            }
                            else
                            {
                                string type = "Other";
                                if (bp is BlueprintWeaponEnchantment) type = "Weapon";
                                else if (bp is BlueprintArmorEnchantment) type = "Armor";

                                syncedList.Add(new EnchantmentData
                                {
                                    Name = bp.name,
                                    Guid = guidStr,
                                    Type = type,
                                    Source = "Mod",
                                    PointCost = bp.EnchantmentCost > 0 ? bp.EnchantmentCost : 1,
                                    Description = System.Text.RegularExpressions.Regex.Replace(bp.Description?.ToString() ?? "", "<.*?>", string.Empty),
                                    Categories = new List<string> { "Discovered" }
                                });
                            }
                        }
                    }

                    lock (MasterList)
                    {
                        MasterList = syncedList;
                    }

                    // --- RÉUSSITE TOTALE ---
                    _hasSyncedThisSession = true;
                    LastSyncMessage = $"Sync réussie ({MasterList.Count} enchantements).";
                }
                catch (Exception ex)
                {
                    LastSyncMessage = $"Échec critique : {ex.Message}";
                }
                finally
                {
                    IsSyncing = false;
                }
            });
        }

        public static List<EnchantmentData> GetFor(ItemEntity item)
        {
            if (item == null) return new List<EnchantmentData>();
            bool isWeapon = item.Blueprint is Kingmaker.Blueprints.Items.Weapons.BlueprintItemWeapon;
            bool isArmor = item.Blueprint is Kingmaker.Blueprints.Items.Armors.BlueprintItemArmor;

            lock (MasterList)
            {
                return MasterList.Where(e => 
                    (isWeapon && e.Type == "Weapon") || 
                    (isArmor && e.Type == "Armor")
                ).ToList();
            }
        }

        public static EnchantmentData GetByGuid(string guid)
        {
            lock (MasterList)
            {
                return MasterList.FirstOrDefault(e => e.Guid == guid);
            }
        }
    }
}
