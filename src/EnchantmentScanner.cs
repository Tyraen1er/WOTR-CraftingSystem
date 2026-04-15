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
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using Newtonsoft.Json;
using Kingmaker.Blueprints.JsonSystem;
using Kingmaker.Blueprints.JsonSystem.BinaryFormat;
using Kingmaker.Blueprints.JsonSystem.Converters;

//
// EnchantmentScanner.cs
//

namespace CraftingSystem
{
    public class EnchantmentData
    {
        public string Name;
        public string Type; // "Weapon" or "Armor" or "Other"
        public string Source = "Mod"; // "TTRPG", "Owlcat", "Ownlcat+", "Mod"
        public bool IsHomebrew = false;
        
        [JsonProperty("GUID")]
        public string Guid;

        [JsonProperty("PointCost", NullValueHandling = NullValueHandling.Ignore)]
        public string PointString; // Accepte "+1", "+2", ou même juste "1", "2"
        
        public List<string> Categories = new List<string>();
        public string Description;
        
        [JsonProperty("IsEpic", NullValueHandling = NullValueHandling.Ignore)]
        public bool IsEpic = false;

        [JsonProperty("PriceFactor", NullValueHandling = NullValueHandling.Ignore)]
        public int PriceFactor = -1;

        // NOUVEAU : On récupère directement la colonne PriceOverride du JSON
        [JsonProperty("PriceOverride", NullValueHandling = NullValueHandling.Ignore)]
        public int GoldOverride = -1;

        [JsonIgnore]
        public int PointCost
        {
            get
            {
                if (string.IsNullOrEmpty(PointString)) return 0;
                
                // On nettoie la chaîne (on enlève les "+" et les espaces)
                // Comme ça, que tu écrives "+2" ou "2" dans ton CSV, ça marchera.
                string cleanString = PointString.Replace("+", "").Trim();
                
                if (int.TryParse(cleanString, out int val))
                {
                    return val;
                }
                return 0;
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
            string cachePath = Path.Combine(Main.ModEntry.Path, "EnchantmentGuidsCache.json");
            if (File.Exists(cachePath)) File.Delete(cachePath);
            _hasSyncedThisSession = false;
            StartSync();
        }

        public static void StartSync()
        {
            if (_hasSyncedThisSession || IsSyncing) return;
            
            IsSyncing = true;
            LastSyncMessage = Helpers.GetString("ui_sync_in_progress", "Synchronization in progress...");
            Main.ModEntry.Logger.Log("[SYNC] Lancement de la tâche de synchronisation en arrière-plan...");
            
            Task.Run(() => {
                try
                {
                    var bpCache = ResourcesLibrary.BlueprintsCache;
                    if (bpCache == null) {
                        LastSyncMessage = Helpers.GetString("ui_sync_error_index", "Failed: Game index inaccessible.");
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

                    // 2. Scan Multithreadé avec lecture binaire brute de ToyBox (Ultra-Rapide & Read-Only)
                    var syncedList = new List<EnchantmentData>();
                    
                    var allKeys = bpCache.m_LoadedBlueprints.OrderBy(e => e.Value.Offset).Select(e => e.Key).ToList();
                    TotalCount = allKeys.Count;
                    ProcessedCount = 0;
                    
                    Main.ModEntry.Logger.Log($"[SYNC] Préparation du scan multithread binaire ({TotalCount} blueprints)...");
                    
                    var memStream = new MemoryStream();
                    lock (bpCache.m_Lock) {
                        bpCache.m_PackFile.Position = 0;
                        bpCache.m_PackFile.CopyTo(memStream);
                    }
                    var bytes = memStream.GetBuffer();
                    
                    var chunks = allKeys.Select((k, i) => new { Index = i, Value = k })
                                        .GroupBy(x => x.Index / 1000)
                                        .Select(x => x.Select(v => v.Value).ToList())
                                        .ToList();
                    
                    var chunkQueue = new ConcurrentQueue<List<BlueprintGuid>>(chunks);
                    var foundEnchants = new ConcurrentBag<BlueprintItemEnchantment>();
                    
                    int numThreads = 4;
                    var tasks = new List<Task>();
                    
                    for (int i = 0; i < numThreads; i++) {
                        tasks.Add(Task.Run(() => {
                            Stream stream = new MemoryStream(bytes);
                            stream.Position = 0;
                            var serializer = new ReflectionBasedSerializer(new PrimitiveSerializer(new BinaryReader(stream), UnityObjectConverter.AssetList));
                            
                            while (chunkQueue.TryDequeue(out var chunkGuids)) {
                                foreach(var guid in chunkGuids) {
                                    int currentCount = Interlocked.Increment(ref ProcessedCount);
                                    if (currentCount % 1000 == 0) {
                                        LastSyncMessage = string.Format(Helpers.GetString("ui_sync_scanner_progress", "Multithreaded binary scanner: {0} / {1} processed..."), currentCount, TotalCount);
                                    }
                                    
                                    try {
                                        var mLoaded = bpCache.m_LoadedBlueprints;
                                        if (mLoaded.TryGetValue(guid, out var entry)) {
                                            if (entry.Offset == 0U) continue;
                                            
                                            // Si déjà officiellement chargé par le jeu on vérifie vite.
                                            if (entry.Blueprint != null) {
                                                if (entry.Blueprint is BlueprintItemEnchantment bpEnchChecked) {
                                                    foundEnchants.Add(bpEnchChecked);
                                                }
                                                continue;
                                            }
                                            
                                            // Lecture binaire brute du blueprint (sans l'injecter de force pour éviter la corruption du jeu)
                                            stream.Seek(entry.Offset, SeekOrigin.Begin);
                                            SimpleBlueprint simpleBlueprint = null;
                                            serializer.Blueprint(ref simpleBlueprint);
                                            
                                            if (simpleBlueprint is BlueprintItemEnchantment ench) {
                                                ench.AssetGuid = guid;
                                                foundEnchants.Add(ench);
                                            }
                                        }
                                    } catch { } // on ignore les erreurs isolées de parsing (ToyBox fait pareil)
                                }
                            }
                        }));
                    }
                    
                    // Attend que tous les workers aient fini la lecture brute
                    Task.WaitAll(tasks.ToArray());
                    Main.ModEntry.Logger.Log($"[SYNC] Scan binaire terminé : {foundEnchants.Count} enchantements extraits parmis {TotalCount} blueprints !");
                    
                    LastSyncMessage = Helpers.GetString("ui_sync_integration", "Integration and filtering of overrides (JSON)...");

                    foreach (var bp in foundEnchants)
                    {
                        string guidStr = bp.AssetGuid.ToString();

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
                                PointString = bp.EnchantmentCost > 0 ? $"+{bp.EnchantmentCost}" : "+1",
                                Description = "", // Sera résolu dynamiquement par l'UI
                                Categories = new List<string> { "Discovered" }
                            });
                        }
                    }

                    lock (MasterList)
                    {
                        MasterList = syncedList;
                    }

                    // --- RÉUSSITE TOTALE ---
                    _hasSyncedThisSession = true;
                    LastSyncMessage = string.Format(Helpers.GetString("ui_sync_success", "Sync successful ({0} enchantments)."), MasterList.Count);
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
            if (string.IsNullOrEmpty(guid)) return null;
            lock (MasterList)
            {
                // On passe en OrdinalIgnoreCase pour être sur de ne pas rater un GUID à cause de la casse
                var result = MasterList.FirstOrDefault(e => string.Equals(e.Guid, guid, StringComparison.OrdinalIgnoreCase));
                if (result == null && guid.Length > 10)
                {
                    Main.ModEntry.Logger.Warning($"[DEBUG] GetByGuid FAILED to find: {guid}. MasterList count: {MasterList.Count}");
                }
                return result;
            }
        }
    }
}