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
        
        [JsonProperty("GUID")]
        public string Guid;
        
        public string Type; // "Weapon" or "Armor"
        public string Source = "Mod"; // "TTRPG", "Owlcat", "Mod"
        public bool IsHomebrew = false;
        
        [JsonProperty("Point")]
        public string PointString; // Récupère "+1" ou "4000" depuis le JSON
        
        public int DaysOverride = -1; 
        public List<string> Categories = new List<string>();
        public string Description;

        [JsonIgnore]
        public int PointCost
        {
            get
            {
                if (string.IsNullOrEmpty(PointString)) return 0;
                
                // Si la chaîne commence par un "+", c'est un bonus d'altération (ex: "+1")
                if (PointString.StartsWith("+"))
                {
                    if (int.TryParse(PointString.Replace("+", "").Trim(), out int val))
                        return val;
                }
                return 0; // Si c'est un prix en pièces d'or, le PointCost est 0
            }
        }

        [JsonIgnore]
        public int GoldOverride
        {
            get
            {
                if (string.IsNullOrEmpty(PointString)) return -1;

                // Si ça ne commence pas par "+", on assume que c'est un prix fixe
                if (!PointString.StartsWith("+"))
                {
                    string numericPart = new string(PointString.Where(char.IsDigit).ToArray());
                    if (int.TryParse(numericPart, out int val) && val > 0)
                        return val;
                }
                return -1;
            }
        }

        [JsonIgnore]
        public BlueprintItemEnchantment Blueprint => ResourcesLibrary.TryGetBlueprint(BlueprintGuid.Parse(Guid)) as BlueprintItemEnchantment;
    }

    public static class EnchantmentScanner
    {
        public static List<EnchantmentData> MasterList = new List<EnchantmentData>();
        private static bool _hasSyncedThisSession = false;
        public static bool IsSyncing = false;
        public static int ProcessedCount = 0;
        public static int TotalCount = 0;
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
                    Main.ModEntry.Logger.Log($"[SYNC] JSON d'enchantements chargé : {MasterList.Count} entrées.");
                }
            }
            catch (Exception ex)
            {
                LastSyncMessage = $"Erreur JSON : {ex.Message}";
                Main.ModEntry.Logger.Error($"[SYNC] Erreur chargement JSON : {ex}");
            }
        }

        public static void ForceSync()
        {
            Main.ModEntry.Logger.Log("[SYNC] Forçage de la synchronisation manuelle...");
            _hasSyncedThisSession = false;
            StartSync();
        }

        public static void StartSync()
        {
            if (_hasSyncedThisSession || IsSyncing) return;
            
            IsSyncing = true;
            LastSyncMessage = "Synchronisation en cours...";
            Main.ModEntry.Logger.Log("[SYNC] Lancement de la tâche de synchronisation en arrière-plan...");
            
            Task.Run(() => {
                try
                {
                    var bpCache = ResourcesLibrary.BlueprintsCache;
                    if (bpCache == null) {
                        LastSyncMessage = "Échec : Index du jeu inaccessible.";
                        Main.ModEntry.Logger.Error("[SYNC] Impossible d'accéder au BlueprintsCache du jeu.");
                        return;
                    }

                    // 1. Overrides (Chargement du fichier JSON pour lier vos modifications)
                    var overrides = new Dictionary<string, EnchantmentData>();
                    string path = Path.Combine(Main.ModEntry.Path, "Enchantments.json");
                    if (File.Exists(path))
                    {
                        var json = File.ReadAllText(path);
                        var overrideList = JsonConvert.DeserializeObject<List<EnchantmentData>>(json) ?? new List<EnchantmentData>();
                        foreach (var ov in overrideList) overrides[ov.Guid] = ov;
                    }

                    // 2. Scan (MÉTHODE NATIVE COMPATIBLE)
                    var syncedList = new List<EnchantmentData>();
                    var allGuids = bpCache.m_LoadedBlueprints.Keys.ToList();
                    TotalCount = allGuids.Count;
                    ProcessedCount = 0;
                    
                    Main.ModEntry.Logger.Log($"[SYNC] Scan de {TotalCount} Blueprints en cours...");

                    foreach (var guid in allGuids)
                    {
                        ProcessedCount++;
                        if (ProcessedCount % 1000 == 0) // Mise à jour de l'UI tous les 1000 éléments
                        {
                            LastSyncMessage = $"Synchronisation : {ProcessedCount} / {TotalCount} chargés...";
                        }

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
                                    // On génère automatiquement le PointString (+1, +2 etc) pour le nouveau format JSON
                                    PointString = bp.EnchantmentCost > 0 ? $"+{bp.EnchantmentCost}" : "+1",
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
                    Main.ModEntry.Logger.Log($"[SYNC] Synchronisation terminée avec succès. {MasterList.Count} enchantements répertoriés.");
                }
                catch (Exception ex)
                {
                    LastSyncMessage = $"Échec critique : {ex.Message}";
                    Main.ModEntry.Logger.Error($"[SYNC] Erreur critique pendant le scan : {ex}");
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
            bool isWeapon = item.Blueprint is BlueprintItemWeapon;
            bool isArmor = item.Blueprint is BlueprintItemArmor;

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