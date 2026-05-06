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
        public static ConcurrentDictionary<string, EnchantmentData> GuidMap = new ConcurrentDictionary<string, EnchantmentData>(StringComparer.OrdinalIgnoreCase);
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
                    GuidMap = new ConcurrentDictionary<string, EnchantmentData>(StringComparer.OrdinalIgnoreCase);
                    foreach (var e in list)
                    {
                        if (string.IsNullOrEmpty(e.Guid)) continue;
                        if (!GuidMap.TryAdd(e.Guid, e)) Main.ModEntry.Logger.Warning($"[SYNC] Doublon de GUID détecté dans Enchantments.json : {e.Guid} ({e.Name})");
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
                    GuidMap = new ConcurrentDictionary<string, EnchantmentData>(StringComparer.OrdinalIgnoreCase);
                    foreach (var e in syncedList)
                    {
                        GuidMap.TryAdd(e.Guid, e);
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
            if (GuidMap.TryGetValue(guid, out var result)) return result;

            string cleanGuid = guid.Replace("-", "").ToLower();
            if (cleanGuid.StartsWith(DynamicGuidHelper.Signature.ToLower()))
            {
                var dynamicData = ResolveDynamicEnchantment(guid);
                if (dynamicData != null)
                {
                    GuidMap.TryAdd(guid, dynamicData);
                    return dynamicData;
                }
            }

            return null;
        }

        public static EnchantmentData ResolveDynamicEnchantment(string guid)
        {
            if (string.IsNullOrEmpty(guid)) return null;
            if (!DynamicGuidHelper.TryDecodeGuid(BlueprintGuid.Parse(guid), out string modelId, out List<int> values, out int mask)) return null;

            // On cherche le modèle (vals[0] == 1 signifie Feature)
            bool isFeature = values.Count > 0 && values[0] == 1;
            var model = CustomEnchantmentsBuilder.AllModels.FirstOrDefault(m =>
                m.EnchantId == modelId && (isFeature ? m.Type == "Feature" : m.Type != "Feature"));

            if (model == null) return null;

            // Préparation des variables pour les formules et remplacements de texte
            var formulaVars = new Dictionary<string, double>();
            var replacements = new Dictionary<string, string>();

            if (modelId == "007")
            {
                // Format 007 : [0:isFeature] [1:Grade] [2:Charges] [3:Count] [4+:Metamagics...]
                int grade = values.Count > 1 ? values[1] : 0;
                int charges = values.Count > 2 ? values[2] : 3;
                int mCount = values.Count > 3 ? values[3] : 0;

                formulaVars["Grade"] = grade;
                formulaVars["Charges"] = charges;
                formulaVars["MetamagicCount"] = mCount;

                var mCosts = new List<double>();
                var mNames = new List<string>();

                for (int i = 0; i < mCount; i++)
                {
                    int val = values.Count > (4 + i) ? values[4 + i] : 0;
                    if (val == 0) continue;

                    int cost = GetMetamagicLevelCost(val);
                    mCosts.Add(cost);

                    string mName = Enum.GetName(typeof(Kingmaker.UnitLogic.Abilities.Metamagic), val) ?? val.ToString();
                    mNames.Add(Helpers.GetString("ui_enum_" + mName, mName));
                }

                replacements["Grade"] = (grade == 0 ? "Lesser" : (grade == 1 ? "Normal" : "Greater"));
                replacements["Charges"] = charges.ToString();
                replacements["Metamagic"] = string.Join(", ", mNames);

                var sortedCosts = mCosts.OrderByDescending(c => c).ToList();
                double[] resolvedCosts = new double[3] { 0, 0, 0 };
                string gradeKey = (grade == 0 ? "Lesser" : (grade == 1 ? "Normal" : "Greater"));

                if (model.PriceTables != null && model.PriceTables.TryGetValue("Rod", out var rodTable))
                {
                    for (int i = 0; i < Math.Min(3, sortedCosts.Count); i++)
                    {
                        string key = $"{gradeKey}_{(int)sortedCosts[i]}";
                        if (rodTable.TryGetValue(key, out double p)) resolvedCosts[i] = p;
                    }
                }

                formulaVars["Metamagic"] = sortedCosts.Count > 0 ? sortedCosts[0] : 0;
                formulaVars["Metamagic2"] = sortedCosts.Count > 1 ? sortedCosts[1] : 0;
                formulaVars["Metamagic3"] = sortedCosts.Count > 2 ? sortedCosts[2] : 0;

                formulaVars["RodPrice1"] = resolvedCosts[0];
                formulaVars["RodPrice2"] = resolvedCosts[1];
                formulaVars["RodPrice3"] = resolvedCosts[2];
            }
            else
            {
                for (int i = 0; i < model.DynamicParams.Count; i++)
                {
                    if (i + 1 < values.Count)
                    {
                        var p = model.DynamicParams[i];
                        int val = values[i + 1];
                        string resolvedVal = val.ToString();

                        if (p.Type == "Enum" && !string.IsNullOrEmpty(p.EnumTypeName))
                        {
                            try
                            {
                                var enumType = Type.GetType(p.EnumTypeName);
                                if (enumType != null)
                                {
                                    string enumName = Enum.GetName(enumType, val);
                                    if (!string.IsNullOrEmpty(enumName))
                                    {
                                        if (p.EnumOverrides != null && p.EnumOverrides.TryGetValue(enumName, out object overrideObj))
                                            resolvedVal = Helpers.GetLocalizedString(overrideObj);
                                        else
                                            resolvedVal = Helpers.GetString("energy_" + enumName, enumName);
                                    }
                                }
                            }
                            catch { }
                        }
                        replacements[p.Name] = resolvedVal;
                        formulaVars[p.Name] = val;
                    }
                }
            }

            string finalName = Helpers.GetLocalizedString(model.NameCompleted ?? model.BaseName, replacements);
                    string prefix = model.Prefix != null ? Helpers.GetLocalizedString(model.Prefix, replacements) : "";
            string suffix = model.Suffix != null ? Helpers.GetLocalizedString(model.Suffix, replacements) : "";

            string fullDisplayName = finalName;
            if (!string.IsNullOrEmpty(prefix) && !fullDisplayName.StartsWith(prefix)) fullDisplayName = prefix + " " + fullDisplayName;
            if (!string.IsNullOrEmpty(suffix) && !fullDisplayName.Contains(suffix)) fullDisplayName = fullDisplayName + " " + suffix;

            // Détermination si c'est une altération pure (Weapon/Armor Enhancement Bonus)
            bool isEnhancement = false;
            for (int i = 0; i < model.Components.Count; i++)
            {
                if ((mask & (1 << i)) == 0) continue;

                object compObj = model.Components[i];
                string compTypeName = "";

                if (compObj is string compId && CustomEnchantmentsBuilder.ComponentLibrary.TryGetValue(compId, out object libComp))
                    compTypeName = libComp?.GetType().Name ?? "";
                else
                    compTypeName = compObj?.GetType().Name ?? "";

                if (compTypeName == "WeaponEnhancementBonus" || compTypeName == "ArmorEnhancementBonus")
                {
                    isEnhancement = true;
                    break;
                }
            }

            int currentMaxNotEpic = model.MaxNotEpic == 0 ? 100 : model.MaxNotEpic;
            int currentPriceFactor = model.PriceFactor;
            List<string> currentSlots = new List<string>(model.Slots);

            foreach (var p in model.DynamicParams)
            {
                if (formulaVars.TryGetValue(p.Name, out double val))
                {
                    int intVal = (int)val;
                    string enumName = null;
                    if (p.Type == "Enum" && !string.IsNullOrEmpty(p.EnumTypeName))
                    {
                        try {
                            var enumType = Type.GetType(p.EnumTypeName);
                            if (enumType != null) enumName = Enum.GetName(enumType, intVal);
                        } catch {}
                    }

                    // 1. Legacy check (Threshold)
                    if (enumName != null && p.EnumThresholdOverrides != null && p.EnumThresholdOverrides.TryGetValue(enumName, out int overVal))
                        currentMaxNotEpic = overVal;

                    // 2. New Grouped Overrides check
                    if (enumName != null && p.EnumOverrides != null && p.EnumOverrides.TryGetValue(enumName, out object ovrObj))
                    {
                        if (ovrObj is Newtonsoft.Json.Linq.JObject jo)
                        {
                            if (jo["MaxNotEpic"] != null) currentMaxNotEpic = (int)jo["MaxNotEpic"];
                            if (jo["PriceFactor"] != null) currentPriceFactor = (int)jo["PriceFactor"];
                            if (jo["Slots"] != null) currentSlots = jo["Slots"].ToObject<List<string>>();
                        }
                        else if (ovrObj is EnumOverrideData eod)
                        {
                            if (eod.MaxNotEpic.HasValue) currentMaxNotEpic = eod.MaxNotEpic.Value;
                            if (eod.PriceFactor.HasValue) currentPriceFactor = eod.PriceFactor.Value;
                            if (eod.Slots != null) currentSlots = eod.Slots;
                        }
                    }
                }
            }

            var dynamicData = new EnchantmentData
            {
                Guid = guid,
                Name = fullDisplayName.Replace("  ", " ").Trim(),
                Type = model.Type.Replace("Enchantment", ""),
                Source = "Custom",
                PointString = model.EnchantmentCost.ToString(),
                IsEnhancement = isEnhancement,
                PriceFactor = currentPriceFactor,
                Slots = currentSlots
            };
            bool isEpic = currentMaxNotEpic > 0 && model.EnchantmentCost > currentMaxNotEpic;
            if (!isEpic)
            {
                foreach (var pDef in model.DynamicParams)
                {
                    if (formulaVars.TryGetValue(pDef.Name, out double pVal))
                    {
                        if (model.EpicThresholds != null && model.EpicThresholds.TryGetValue(pDef.Name, out int threshold))
                        {
                            if (pVal > threshold) { isEpic = true; break; }
                        }
                        else
                        {
                            if (pDef.Type != "Enum" && pVal > currentMaxNotEpic)
                            {
                                isEpic = true; break;
                            }
                        }
                    }
                }
            }
            dynamicData.IsEpic = isEpic;

            if (!string.IsNullOrEmpty(model.PointCostFormula))
            {
                try {
                    dynamicData.PointString = FormulaEvaluator.EvaluateInt(model.PointCostFormula, formulaVars).ToString();
                } catch (Exception ex) {
                    Main.ModEntry.Logger.Error($"[SCANNER] Erreur de formule (Points) pour {model.EnchantId}: {ex.Message}");
                    dynamicData.PointString = "-1";
                }
            }

            if (model.PriceTables != null)
            {
                foreach (var table in model.PriceTables)
                {
                    string tableName = table.Key;
                    foreach (var pDef in model.DynamicParams)
                    {
                        if (formulaVars.TryGetValue(pDef.Name, out double pVal))
                        {
                            double resolvedPrice = 0;
                            bool found = false;
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
                            if (!found)
                            {
                                string key = GetEnumKey(pDef, pVal);
                                if (table.Value.TryGetValue(key, out double m)) { resolvedPrice = m; found = true; }
                            }
                            if (!found && table.Value.TryGetValue("DEFAULT", out double dm)) resolvedPrice = dm;
                            formulaVars[$"PriceTable.{tableName}.{pDef.Name}"] = resolvedPrice;
                            if (tableName == pDef.Name) formulaVars["PriceTable." + tableName] = resolvedPrice;
                        }
                    }
                }
            }

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
                try {
                    dynamicData.GoldOverride = (int)FormulaEvaluator.EvaluateLong(model.GoldOverrideFormula, formulaVars);
                } catch (Exception ex) {
                    Main.ModEntry.Logger.Error($"[SCANNER] Erreur de formule (Or) pour {model.EnchantId}: {ex.Message}");
                    dynamicData.GoldOverride = -1;
                }
            }

            return dynamicData;
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
            var metamagic = (Kingmaker.UnitLogic.Abilities.Metamagic)maskValue;

            if (metamagic.HasFlag(Kingmaker.UnitLogic.Abilities.Metamagic.Quicken)) return 4;
            if (metamagic.HasFlag(Kingmaker.UnitLogic.Abilities.Metamagic.Maximize)) return 3;
            if (metamagic.HasFlag(Kingmaker.UnitLogic.Abilities.Metamagic.Empower)) return 2;
            if (metamagic.HasFlag(Kingmaker.UnitLogic.Abilities.Metamagic.Persistent)) return 2;

            // Quintessence : On pourrait utiliser une valeur dynamique, mais par défaut on met 1 
            // car le prix est déjà capé par le Grade du sceptre (Mineur/Normal/Supérieur)
            if (metamagic.HasFlag(Kingmaker.UnitLogic.Abilities.Metamagic.Heighten)) return 1;

            if (metamagic.HasFlag(Kingmaker.UnitLogic.Abilities.Metamagic.Extend)) return 1;
            if (metamagic.HasFlag(Kingmaker.UnitLogic.Abilities.Metamagic.Reach)) return 1;
            if (metamagic.HasFlag(Kingmaker.UnitLogic.Abilities.Metamagic.Selective)) return 1;
            if (metamagic.HasFlag(Kingmaker.UnitLogic.Abilities.Metamagic.Bolstered)) return 1;
            if (metamagic.HasFlag(Kingmaker.UnitLogic.Abilities.Metamagic.Piercing)) return 1;
            if (metamagic.HasFlag(Kingmaker.UnitLogic.Abilities.Metamagic.Intensified)) return 1;

            return 0;
        }
    }
}
