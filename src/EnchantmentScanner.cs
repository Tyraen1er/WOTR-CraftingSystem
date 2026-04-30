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
// Enchantmentscanner.cs
//

namespace CraftingSystem
{
    public class DescriptionTemplate
    {
        [JsonProperty("COMP_TYPE")]
        public string ComponentType;
        public string enGB;
        public string frFR;
        public string ruRU;
    }

    public class EnchantmentData
    {
        public string Name;

        // Le JSON actuel utilise "internType" — nous l'acceptons uniquement (plus de compat legacy).
        [JsonProperty("internType", NullValueHandling = NullValueHandling.Ignore)]
        public string Type; // "Weapon" or "Armor" or "Other"

        public string Source = "Mod"; // "TTRPG", "Owlcat", "Ownlcat+", "Mod"

        [JsonProperty("GUID")]
        public string Guid;

        [JsonProperty("Slots", NullValueHandling = NullValueHandling.Ignore)]
        public List<string> Slots = new List<string>();

        [JsonProperty("PointCost", NullValueHandling = NullValueHandling.Ignore)]
        public string PointString; // Accepte "+1", "+2", ou même juste "1", "2"

        public List<string> Categories = new List<string>();
        public string Description;

        [JsonProperty("IsEpic", NullValueHandling = NullValueHandling.Ignore)]
        public bool IsEpic = false;

        public bool IsEnhancement = false;

        [JsonProperty("PriceFactor", NullValueHandling = NullValueHandling.Ignore)]
        public int PriceFactor = -1;

        // Colonne PriceOverride dans le JSON
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
        public static Dictionary<string, EnchantmentData> GuidMap = new Dictionary<string, EnchantmentData>(StringComparer.OrdinalIgnoreCase);
        private static bool _hasSyncedThisSession = false;
        public static bool IsSyncing = false;
        public static int ProcessedCount = 0;
        public static int TotalCount = 0;
        public static string LastSyncMessage = "En attente de synchronisation...";
        public static List<DescriptionTemplate> DescriptionTemplates = new List<DescriptionTemplate>();

        public static void Load()
        {
            try
            {
                string path = Path.Combine(Main.ModEntry.Path, "Enchantments.json");
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    var list = JsonConvert.DeserializeObject<List<EnchantmentData>>(json) ?? new List<EnchantmentData>();

                    // Validation stricte : on exige GUID et internType (Type) présents — sinon on rejette l'entrée.
                    int before = list.Count;
                    list = list.Where(e => !string.IsNullOrEmpty(e.Guid) && !string.IsNullOrEmpty(e.Type)).ToList();
                    int after = list.Count;
                    if (after != before)
                    {
                        Main.ModEntry.Logger.Warning($"[SYNC] {before - after} entrée(s) Enchantments.json rejetée(s) : GUID ou internType manquant.");
                    }

                    MasterList = list;
                    GuidMap = new Dictionary<string, EnchantmentData>(StringComparer.OrdinalIgnoreCase);
                    foreach (var e in list)
                    {
                        if (string.IsNullOrEmpty(e.Guid)) continue;
                        if (!GuidMap.ContainsKey(e.Guid)) GuidMap.Add(e.Guid, e);
                        else Main.ModEntry.Logger.Warning($"[SYNC] Doublon de GUID détecté dans Enchantments.json : {e.Guid} ({e.Name})");
                    }
                    LastSyncMessage = $"JSON chargé ({MasterList.Count} entrées).";
                    // Main.ModEntry.Logger.Log($"[SYNC] JSON d'enchantements chargé : {MasterList.Count} entrées.");
                }
                else
                {
                    Main.ModEntry.Logger.Log("[SYNC] Aucun Enchantments.json trouvé — le scanner continuera sans overrides.");
                }

                // --- CHARGEMENT DES TEMPLATES DE DESCRIPTION ---
                string descPath = Path.Combine(Main.ModEntry.Path, "EnchantmentTemplates.json");
                if (File.Exists(descPath))
                {
                    string json = File.ReadAllText(descPath);
                    DescriptionTemplates = JsonConvert.DeserializeObject<List<DescriptionTemplate>>(json) ?? new List<DescriptionTemplate>();
                    Main.ModEntry.Logger.Log($"[SYNC] {DescriptionTemplates.Count} templates de description chargés.");
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

            Task.Run(() =>
            {
                try
                {
                    var bpCache = ResourcesLibrary.BlueprintsCache;
                    if (bpCache == null)
                    {
                        LastSyncMessage = Helpers.GetString("ui_sync_error_index", "Failed: Game index inaccessible.");
                        Main.ModEntry.Logger.Error("[SYNC] Impossible d'accéder au BlueprintsCache du jeu.");
                        return;
                    }

                    // 1. Overrides (Chargement du fichier JSON pour lier vos modifications)
                    var overrides = new Dictionary<string, EnchantmentData>(StringComparer.OrdinalIgnoreCase);
                    string path = Path.Combine(Main.ModEntry.Path, "Enchantments.json");
                    if (File.Exists(path))
                    {
                        try
                        {
                            var overrideList = JsonConvert.DeserializeObject<List<EnchantmentData>>(File.ReadAllText(path)) ?? new List<EnchantmentData>();
                            // Validation stricte : n'ajouter que les overrides valides (GUID + Type)
                            foreach (var ov in overrideList)
                            {
                                if (string.IsNullOrEmpty(ov.Guid) || string.IsNullOrEmpty(ov.Type))
                                {
                                    Main.ModEntry.Logger.Warning($"[SYNC] Override JSON ignoré (GUID ou internType manquant).");
                                    continue;
                                }
                                overrides[ov.Guid] = ov;
                            }
                            // Main.ModEntry.Logger.Log($"[SYNC] Overrides chargés : {overrides.Count}");
                        }
                        catch (Exception ex) { Main.ModEntry.Logger.Error($"[SYNC] Impossible de parser Enchantments.json : {ex}"); }
                    }

                    // 2. Scan Multithreadé avec lecture binaire brute de ToyBox (Ultra-Rapide & Read-Only)
                    var syncedList = new List<EnchantmentData>();

                    var allKeys = bpCache.m_LoadedBlueprints.OrderBy(e => e.Value.Offset).Select(e => e.Key).ToList();
                    TotalCount = allKeys.Count;
                    ProcessedCount = 0;

                    Main.ModEntry.Logger.Log($"[SYNC] Préparation du scan multithread binaire ({TotalCount} blueprints)...");

                    var memStream = new MemoryStream();
                    lock (bpCache.m_Lock)
                    {
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

                    for (int i = 0; i < numThreads; i++)
                    {
                        tasks.Add(Task.Run(() =>
                        {
                            Stream stream = new MemoryStream(bytes);
                            stream.Position = 0;
                            var serializer = new ReflectionBasedSerializer(new PrimitiveSerializer(new BinaryReader(stream), UnityObjectConverter.AssetList));

                            while (chunkQueue.TryDequeue(out var chunkGuids))
                            {
                                foreach (var guid in chunkGuids)
                                {
                                    int currentCount = Interlocked.Increment(ref ProcessedCount);
                                    if (currentCount % 1000 == 0)
                                    {
                                        LastSyncMessage = string.Format(Helpers.GetString("ui_sync_scanner_progress", "Multithreaded binary scanner: {0} / {1} processed..."), currentCount, TotalCount);
                                    }

                                    try
                                    {
                                        var mLoaded = bpCache.m_LoadedBlueprints;
                                        if (mLoaded.TryGetValue(guid, out var entry))
                                        {
                                            // Si déjà officiellement chargé par le jeu (ou injecté manuellement) on l'ajoute directement.
                                            if (entry.Blueprint != null)
                                            {
                                                if (entry.Blueprint is BlueprintItemEnchantment bpEnchChecked)
                                                {
                                                    // On marque les enchantements injectés par notre CustomEnchantmentsBuilder
                                                    if (CustomEnchantmentsBuilder.InjectedGuids.Contains(guid))
                                                    {
                                                        Main.ModEntry.Logger.Log($"[DEBUG_CUSTOM_ENCHANT] Scanner found injected blueprint: {bpEnchChecked.name} ({guid})");
                                                    }
                                                    foundEnchants.Add(bpEnchChecked);
                                                }
                                                continue;
                                            }

                                            if (entry.Offset == 0U) continue;

                                            // Lecture binaire brute du blueprint (sans l'injecter de force pour éviter la corruption du jeu)
                                            stream.Seek(entry.Offset, SeekOrigin.Begin);
                                            SimpleBlueprint simpleBlueprint = null;
                                            serializer.Blueprint(ref simpleBlueprint);

                                            if (simpleBlueprint is BlueprintItemEnchantment ench)
                                            {
                                                ench.AssetGuid = guid;
                                                foundEnchants.Add(ench);
                                            }
                                        }
                                    }
                                    catch { } // on ignore les erreurs isolées de parsing (ToyBox fait pareil)
                                }
                            }
                        }));
                    }

                    // Attend que tous les workers aient fini la lecture brute
                    Task.WaitAll(tasks.ToArray());
                    // Main.ModEntry.Logger.Log($"[SYNC] Scan binaire terminé : {foundEnchants.Count} enchantements extraits parmis {TotalCount} blueprints !");

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
                                IsEnhancement = CraftingCalculator.IsPureEnhancement(bp),
                                Categories = new List<string> { "Discovered" }
                            });
                        }
                    }

                    lock (MasterList)
                    {
                        MasterList = syncedList;
                        GuidMap = new Dictionary<string, EnchantmentData>(StringComparer.OrdinalIgnoreCase);
                        foreach (var e in syncedList)
                        {
                            if (!GuidMap.ContainsKey(e.Guid)) GuidMap.Add(e.Guid, e);
                        }
                    }

                    // --- RÉUSSITE TOTALE ---
                    _hasSyncedThisSession = true;
                    LastSyncMessage = string.Format(Helpers.GetString("ui_sync_success", "Sync successful ({0} Enchantments)."), MasterList.Count);
                    // Main.ModEntry.Logger.Log($"[SYNC] Synchronisation terminée avec succès. {MasterList.Count} enchantements répertoriés.");
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
                    (isArmor && e.Type == "Armor") ||
                    (!isWeapon && !isArmor && e.Type == "Other") // Support pour les objets merveilleux
                ).ToList();
            }
        }

        public static EnchantmentData GetByGuid(string guid)
        {
            if (string.IsNullOrEmpty(guid)) return null;
            lock (MasterList)
            {
                if (GuidMap.TryGetValue(guid, out var result)) return result;

                string cleanGuid = guid.Replace("-", "").ToLower();
                Main.ModEntry.Logger.Log($"[DEBUG_SCANNER] GetByGuid: {guid} (clean: {cleanGuid})");

                // --- RÉSOLUTION DYNAMIQUE DES GUIDS C2AF ---
                if (cleanGuid.StartsWith(DynamicGuidHelper.Signature.ToLower()))
                {
                    Main.ModEntry.Logger.Log($"[DEBUG_SCANNER] Dynamic Signature detected for {cleanGuid}");
                    if (DynamicGuidHelper.TryDecodeGuid(BlueprintGuid.Parse(guid), out string modelId, out List<int> values))
                    {
                        Main.ModEntry.Logger.Log($"[DEBUG_SCANNER] Decoded ModelId: {modelId}, Values: {string.Join(",", values)}");

                        // On cherche le modèle (vals[0] == 1 signifie Feature)
                        bool isFeature = values.Count > 0 && values[0] == 1;
                        var model = CustomEnchantmentsBuilder.AllModels.FirstOrDefault(m =>
                            m.EnchantId == modelId && (isFeature ? m.Type == "Feature" : m.Type != "Feature"));

                        if (model != null)
                        {
                            Main.ModEntry.Logger.Log($"[DEBUG_SCANNER] Found Model: {model.BaseName} for ID {modelId}");

                            // Préparation des variables pour les formules et remplacements de texte
                            var formulaVars = new Dictionary<string, double>();
                            var replacements = new Dictionary<string, string>();
                            
                            for (int i = 0; i < model.DynamicParams.Count; i++)
                            {
                                if (i + 1 < values.Count)
                                {
                                    var p = model.DynamicParams[i];
                                    int val = values[i + 1];
                                    string resolvedVal = val.ToString();

                                    // Résolution des noms d'Enums pour l'affichage (ex: DamageEnergyType.Fire -> Feu)
                                    if (p.Type == "Enum" && !string.IsNullOrEmpty(p.EnumTypeName))
                                    {
                                        try {
                                            var enumType = Type.GetType(p.EnumTypeName);
                                            if (enumType != null) {
                                                string enumName = Enum.GetName(enumType, val);
                                                if (!string.IsNullOrEmpty(enumName))
                                                    resolvedVal = Helpers.GetString("energy_" + enumName, enumName);
                                            }
                                        } catch { }
                                    }

                                    replacements[p.Name] = resolvedVal;
                                    formulaVars[p.Name] = val;
                                }
                            }

                            // Création du nom complet avec gestion intelligente des espaces et doublons
                            string finalName = Helpers.GetLocalizedString(model.NameCompleted ?? model.BaseName, replacements);
                            string prefix = model.Prefix != null ? Helpers.GetLocalizedString(model.Prefix, replacements) : "";
                            string suffix = model.Suffix != null ? Helpers.GetLocalizedString(model.Suffix, replacements) : "";

                            string fullDisplayName = finalName;
                            if (!string.IsNullOrEmpty(prefix) && !fullDisplayName.StartsWith(prefix)) 
                                fullDisplayName = prefix + " " + fullDisplayName;
                            
                            if (!string.IsNullOrEmpty(suffix) && !fullDisplayName.Contains(suffix)) 
                                fullDisplayName = fullDisplayName + " " + suffix;

                            var dynamicData = new EnchantmentData
                            {
                                Guid = guid,
                                Name = fullDisplayName.Replace("  ", " ").Trim(),
                                Type = model.Type,
                                Source = "Custom",
                                PointString = model.EnchantmentCost.ToString(),
                                IsEnhancement = model.Components.Any(c => c.GetType().Name == "WeaponEnhancementBonus" || c.GetType().Name == "ArmorEnhancementBonus"),
                                PriceFactor = model.PriceFactor,
                                Slots = new List<string>(model.Slots)
                            };

                                // Évaluation des formules si présentes
                                if (!string.IsNullOrEmpty(model.PointCostFormula))
                                {
                                    dynamicData.PointString = FormulaEvaluator.EvaluateInt(model.PointCostFormula, formulaVars).ToString();
                                }

                                if (!string.IsNullOrEmpty(model.GoldOverrideFormula))
                                {
                                    dynamicData.GoldOverride = (int)FormulaEvaluator.EvaluateLong(model.GoldOverrideFormula, formulaVars);
                                    Main.ModEntry.Logger.Log($"[DEBUG_SCANNER] Calculated Price: {dynamicData.GoldOverride} for {guid}");
                                }

                                // --- GESTION DU SEUIL ÉPIQUE ---
                                if (model.MaxNotEpic > 0)
                                {
                                    // On vérifie si un des paramètres (numériques) dépasse le seuil
                                    foreach (var val in formulaVars.Values)
                                    {
                                        if (val > model.MaxNotEpic)
                                        {
                                            dynamicData.IsEpic = true;
                                            Main.ModEntry.Logger.Log($"[DEBUG_SCANNER] Epic threshold exceeded ({val} > {model.MaxNotEpic}). Setting IsEpic = true.");
                                            break;
                                        }
                                    }
                                }

                                return dynamicData;
                        }
                        else
                        {
                            Main.ModEntry.Logger.Warning($"[DYNAMIC] Model {modelId} not found for GUID {guid} (isFeature: {isFeature})");
                        }
                    }
                }
            }

            return null;
        }
    }
}
