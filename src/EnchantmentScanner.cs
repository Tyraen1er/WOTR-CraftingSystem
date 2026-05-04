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
        public BlueprintScriptableObject Blueprint
        {
            get
            {
                if (string.IsNullOrEmpty(Guid)) return null;
                if (Guid.Replace("-", "").ToLower().StartsWith("c2af"))
                {
                    return CustomEnchantmentsBuilder.GetOrBuildDynamicBlueprint(Guid);
                }
                return (BlueprintScriptableObject)ResourcesLibrary.TryGetBlueprint(BlueprintGuid.Parse(Guid));
            }
        }
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
            
            // On délègue au scanner unifié
            _ = UnifiedScanner.RunFullScan();
        }

        /// <summary>
        /// Appelée par le UnifiedScanner après la collecte binaire.
        /// Gère l'intégration des overrides JSON et la construction de la MasterList.
        /// </summary>
        public static void FinalizeScan(IEnumerable<(BlueprintItemEnchantment bp, BlueprintGuid guid)> foundEnchants)
        {
            try
            {
                var syncedList = new List<EnchantmentData>();
                
                // 1. Chargement des Overrides (Chargement du fichier JSON pour lier vos modifications)
                var overrides = new Dictionary<string, EnchantmentData>(StringComparer.OrdinalIgnoreCase);
                string path = Path.Combine(Main.ModEntry.Path, "Enchantments.json");
                if (File.Exists(path))
                {
                    try
                    {
                        var overrideList = JsonConvert.DeserializeObject<List<EnchantmentData>>(File.ReadAllText(path)) ?? new List<EnchantmentData>();
                        foreach (var ov in overrideList)
                        {
                            if (!string.IsNullOrEmpty(ov.Guid) && !string.IsNullOrEmpty(ov.Type))
                                overrides[ov.Guid] = ov;
                        }
                    }
                    catch (Exception ex) { Main.ModEntry.Logger.Error($"[SYNC] Impossible de parser Enchantments.json : {ex.Message}"); }
                }

                // 2. Intégration des Blueprints
                foreach (var item in foundEnchants)
                {
                    var bp = item.bp;
                    string guidStr = item.guid.ToString();

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

                _hasSyncedThisSession = true;
                LastSyncMessage = string.Format(Helpers.GetString("ui_sync_success", "Sync successful ({0} Enchantments)."), MasterList.Count);
            }
            catch (Exception ex)
            {
                LastSyncMessage = $"Échec finalisation : {ex.Message}";
                Main.ModEntry.Logger.Error($"[SYNC] Erreur critique FinalizeScan : {ex}");
            }
            finally
            {
                IsSyncing = false;
            }
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
                                                {
                                                    // On vérifie s'il y a une surcharge de nom dans le JSON
                                                    if (p.EnumOverrides != null && p.EnumOverrides.TryGetValue(enumName, out object overrideObj))
                                                    {
                                                        resolvedVal = Helpers.GetLocalizedString(overrideObj);
                                                    }
                                                    else
                                                    {
                                                        resolvedVal = Helpers.GetString("energy_" + enumName, enumName);
                                                    }
                                                }
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
                                Type = model.Type.Replace("Enchantment", ""), // Standardisation: WeaponEnchantment -> Weapon
                                Source = "Custom",
                                PointString = model.EnchantmentCost.ToString(),
                                IsEnhancement = model.Components.Any(c => c.GetType().Name == "WeaponEnhancementBonus" || c.GetType().Name == "ArmorEnhancementBonus"),
                                PriceFactor = model.PriceFactor,
                                Slots = new List<string>(model.Slots)
                            };

                            // --- GESTION DU SEUIL ÉPIQUE (Paramètres Joueurs uniquement) ---
                            // On vérifie si un des paramètres (numériques) dépasse son seuil spécifique ou le seuil global
                            bool isEpic = false;
                            foreach (var pDef in model.DynamicParams)
                            {
                                if (formulaVars.TryGetValue(pDef.Name, out double pVal))
                                {
                                    // 1. Seuil spécifique au paramètre
                                    if (model.EpicThresholds != null && model.EpicThresholds.TryGetValue(pDef.Name, out int threshold))
                                    {
                                        if (pVal > threshold) { isEpic = true; break; }
                                    }
                                    // 2. Seuil global hérité
                                    else if (model.MaxNotEpic > 0 && pVal > model.MaxNotEpic)
                                    {
                                        isEpic = true; break;
                                    }
                                }
                            }
                            dynamicData.IsEpic = isEpic;

                            // Évaluation des formules si présentes
                            if (!string.IsNullOrEmpty(model.PointCostFormula))
                            {
                                dynamicData.PointString = FormulaEvaluator.EvaluateInt(model.PointCostFormula, formulaVars).ToString();
                            }

                            // --- RÉSOLUTION DES TABLES DE PRIX ---
                            if (model.PriceTables != null)
                            {
                                foreach (var table in model.PriceTables)
                                {
                                    string tableName = table.Key;
                                    
                                    // On itère sur TOUS les paramètres pour voir s'ils peuvent être résolus par cette table
                                    foreach (var pDef in model.DynamicParams)
                                    {
                                        if (formulaVars.TryGetValue(pDef.Name, out double pVal))
                                        {
                                            double resolvedPrice = 0;
                                            bool found = false;

                                            // 1. Recherche de clé composite (priorité au paramètre actuel + un autre)
                                            // On cherche si ce paramètre pDef peut former une clé composite avec un autre
                                            foreach (var pOther in model.DynamicParams)
                                            {
                                                if (pDef.Name == pOther.Name) continue;
                                                if (formulaVars.TryGetValue(pOther.Name, out double vOther))
                                                {
                                                    string k1 = GetEnumKey(pDef, pVal);
                                                    string k2 = GetEnumKey(pOther, vOther);

                                                    string compositeKey = $"{k1}_{k2}";
                                                    if (table.Value.TryGetValue(compositeKey, out double cp)) { resolvedPrice = cp; found = true; break; }
                                                    
                                                    compositeKey = $"{k2}_{k1}";
                                                    if (table.Value.TryGetValue(compositeKey, out double cp2)) { resolvedPrice = cp2; found = true; break; }
                                                }
                                            }

                                            // 2. Recherche par paramètre simple
                                            if (!found)
                                            {
                                                string key = GetEnumKey(pDef, pVal);
                                                if (table.Value.TryGetValue(key, out double m)) { resolvedPrice = m; found = true; }
                                            }

                                            if (!found && table.Value.TryGetValue("DEFAULT", out double dm)) resolvedPrice = dm;

                                            // Injection : PriceTable.TableName.ParamName
                                            formulaVars[$"PriceTable.{tableName}.{pDef.Name}"] = resolvedPrice;
                                            
                                            // Compatibilité descendante : si le nom de la table est EXACTEMENT le nom du paramètre
                                            if (tableName == pDef.Name) formulaVars["PriceTable." + tableName] = resolvedPrice;
                                    }
                                }
                            }
                        }

                            // --- INJECTION DE VARIABLES DE COMPTAGE (Metamagies multiples) ---
                            int metamagicCount = 0;
                            foreach (var p in model.DynamicParams)
                            {
                                if (p.Name.StartsWith("Metamagic") && formulaVars.TryGetValue(p.Name, out double val) && val > 0)
                                {
                                    metamagicCount++;
                                    formulaVars["Has" + p.Name] = 1;
                                }
                                else if (p.Name.StartsWith("Metamagic"))
                                {
                                    formulaVars["Has" + p.Name] = 0;
                                }
                            }
                            formulaVars["MetamagicCount"] = metamagicCount;

                            if (!string.IsNullOrEmpty(model.GoldOverrideFormula))
                            {
                                dynamicData.GoldOverride = (int)FormulaEvaluator.EvaluateLong(model.GoldOverrideFormula, formulaVars);
                                Main.ModEntry.Logger.Log($"[DEBUG_SCANNER] Calculated Price: {dynamicData.GoldOverride} for {guid} (Variables: {string.Join(", ", formulaVars.Select(kvp => kvp.Key + "=" + kvp.Value))})");
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
        private static string GetEnumKey(DynamicParam pDef, double val)
        {
            // --- CAS SPÉCIAL : METAMAGIC -> LEVEL COST ---
            if (pDef.EnumTypeName != null && pDef.EnumTypeName.Contains("Kingmaker.UnitLogic.Abilities.Metamagic"))
            {
                return GetMetamagicLevelCost((int)val).ToString();
            }

            string key = ((int)val).ToString();
            
            // Mapping spécial pour Grade
            if (pDef.Name == "Grade")
            {
                if ((int)val == 0) return "Lesser";
                if ((int)val == 1) return "Normal";
                if ((int)val == 2) return "Greater";
            }

            if (pDef.Type == "Enum" && !string.IsNullOrEmpty(pDef.EnumTypeName))
            {
                try
                {
                    var enumType = Type.GetType(pDef.EnumTypeName);
                    if (enumType != null) key = Enum.GetName(enumType, (int)val) ?? key;

                    if (key == ((int)val).ToString() && pDef.EnumOverrides != null)
                    {
                        foreach (var ovr in pDef.EnumOverrides)
                        {
                            if (ovr.Value is Newtonsoft.Json.Linq.JObject jo && jo["Value"] != null && (int)jo["Value"] == (int)val)
                            {
                                return ovr.Key;
                            }
                        }
                    }
                }
                catch { }
            }
            return key;
        }

        private static int GetMetamagicLevelCost(int maskValue)
        {
            // Mapping officiel Pathfinder WOTR pour les coûts de niveau des sceptres
            // Note: On cherche ici la valeur individuelle du masque (puisqu'on itère sur Metamagic, Metamagic2, etc.)
            switch (maskValue)
            {
                case 1:    return 4; // Quicken
                case 2:    return 1; // Extend
                case 4:    return 3; // Maximize
                case 8:    return 2; // Empower
                case 32:   return 1; // Reach
                case 64:   return 2; // Persistent
                case 128:  return 1; // Selective
                case 256:  return 1; // Bolstered
                case 512:  return 1; // Piercing
                case 2048: return 2; // Echoing
                case 1024: return 0; // Completely Normal
                default:   return 0;
            }
        }
    }
}
