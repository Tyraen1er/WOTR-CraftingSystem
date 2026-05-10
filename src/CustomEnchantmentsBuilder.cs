using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Items.Ecnchantments;
using Kingmaker.Blueprints.Facts;
using Kingmaker.Modding;
using Kingmaker.UnitLogic.FactLogic;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.Mechanics.Components;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.Designers.Mechanics.Facts;
using Kingmaker.Designers.Mechanics.EquipmentEnchants;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Abilities.Components;
using Kingmaker.ElementsSystem;
using Kingmaker.UnitLogic.Alignments;
using Kingmaker.UnitLogic.Mechanics.Actions;
using Kingmaker.Utility;
using Kingmaker.Localization;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.Blueprints.Classes;
using Kingmaker.Blueprints.Items;
using Kingmaker.Blueprints.Items.Equipment;
using Kingmaker.UnitLogic.ActivatableAbilities;
using UnityEngine;
using System.Security.Cryptography;
using System.Text;

namespace CraftingSystem
{
    // ... rest of the file ...
    // --- Phase 2.A : ContractResolver for Private Fields ---
    public class OwlcatContractResolver : DefaultContractResolver
    {
        protected override IList<JsonProperty> CreateProperties(Type type, MemberSerialization memberSerialization)
        {
            var props = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                            .Select(f => base.CreateProperty(f, memberSerialization))
                            .ToList();

            foreach (var p in props)
            {
                p.Writable = true;
                p.Readable = true;
            }
            return props;
        }


        // --- Configuration spéciale pour les objets Owlcat/Unity ---
        protected override JsonObjectContract CreateObjectContract(Type objectType)
        {
            var contract = base.CreateObjectContract(objectType);

            // Gestion de l'instanciation pour les classes sans constructeur public par défaut
            if (contract.DefaultCreator == null && !objectType.IsAbstract && !objectType.IsInterface)
            {
                if (typeof(ScriptableObject).IsAssignableFrom(objectType))
                {
                    contract.DefaultCreator = () => ScriptableObject.CreateInstance(objectType);
                }
                else
                {
                    contract.DefaultCreator = () =>
                    {
                        try
                        {
                            // On tente d'abord un constructeur non-public
                            return Activator.CreateInstance(objectType, true);
                        }
                        catch
                        {
                            // En dernier recours, on crée l'objet sans appeler de constructeur
                            return System.Runtime.Serialization.FormatterServices.GetUninitializedObject(objectType);
                        }
                    };
                }
            }

            // SÉCURITÉ : Désactiver les callbacks de désérialisation d'Owlcat qui plantent
            // car ils accèdent à des systèmes non encore initialisés (comme le LocalizationManager).
            if (typeof(BlueprintComponent).IsAssignableFrom(objectType))
            {
                contract.OnDeserializedCallbacks.Clear();
            }

            return contract;
        }
    }

    // --- Phase 2.C : BlueprintReferenceConverter ---
    public class BlueprintReferenceConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType) => typeof(BlueprintReferenceBase).IsAssignableFrom(objectType);

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            try
            {
                if (reader.TokenType == JsonToken.Null) return null;
                string guidString = reader.Value as string;
                if (string.IsNullOrEmpty(guidString)) return null;

                if (guidString.StartsWith("!bp_")) guidString = guidString.Substring(4);

                var reference = Activator.CreateInstance(objectType) as BlueprintReferenceBase;
                if (reference == null)
                {
                    // Main.ModEntry.Logger.Warning($"[DEBUG_REF] Activator.CreateInstance returned null for {objectType.Name}");
                    return null;
                }

                var guid = BlueprintGuid.Parse(guidString);

                // Important : on doit remplir à la fois le champ 'guid' (string) et 'deserializedGuid' (BlueprintGuid)
                reference.ReadGuidFromGuid(guid);

                var field = typeof(BlueprintReferenceBase).GetField("guid", BindingFlags.Instance | BindingFlags.NonPublic);
                if (field != null) field.SetValue(reference, guid.ToString());

                return reference;
            }
            catch (Exception ex)
            {
                // Main.ModEntry.Logger.Error($"[DEBUG_REF] Error reading reference {objectType.Name}: {ex}");
                return null;
            }
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var reference = value as BlueprintReferenceBase;
            if (reference == null || reference.IsEmpty()) writer.WriteNull();
            else writer.WriteValue($"!bp_{reference.deserializedGuid}");
        }
    }

    // --- Data Classes for JSON ---
    public class EnumOverrideData
    {
        public string frFR;
        public string enGB;
        public int? MaxNotEpic;
        public int? PriceFactor;
        public List<string> Slots;
        public string PointCostFormula;
        public int? Value;
        public int? MaskValue;
    }

    public class DynamicParam
    {
        public string Name;
        public string Label;
        
        private string _type;
        public string Type 
        { 
            get => !string.IsNullOrEmpty(_type) ? _type : (!string.IsNullOrEmpty(EnumTypeName) ? "Enum" : "Slider");
            set => _type = value; 
        }

        public string EnumTypeName; // Pour Type == Enum
        public int Min = 1; // Pour Type == Slider
        public int Max = 100; // Pour Type == Slider
        public int Step = 1;
        public List<string> EnumOnly = null; // Optionnel : ne garder que ces valeurs d'enum
        public List<string> EnumExclude = null; // Optionnel : exclure ces valeurs d'enum
        public Dictionary<string, object> EnumOverrides = null; // Optionnel : surcharger le texte affiché (localisable) ou données groupées
        public Dictionary<string, int> EnumThresholdOverrides = null; // Optionnel : surcharger MaxNotEpic selon la valeur choisie (Legacy)
        public object DefaultValue = null; // Optionnel : valeur pré-sélectionnée par défaut (int ou string)

        // Cible pour l'injection
        public int ComponentIndex;
        public string FieldName; // ex: "Type", "Value.Value"
    }

    public class CustomEnchantmentData
    {
        public string Guid;
        public bool Hidden;
        public string EnchantId; // ID à 3 chiffres (ex: 001) pour le générateur dynamique
        public object BaseName;
        public object NameCompleted;
        public string Type; // WeaponEnchantment, ArmorEnchantment, Feature
        public string EnchantNameKey;
        public string EnchantDescKey;
        public object Prefix;
        public object Suffix;
        public int EnchantmentCost; // Utilisé si pas de formule
        public string PointCostFormula;
        public string GoldOverrideFormula;
        public string FeatureModelId; // Optionnel : ID du modèle de feature à utiliser (si différent de EnchantId)
        public int MaxNotEpic; // Seuil au-delà duquel l'enchantement devient épique
        public Dictionary<string, int> EpicThresholds; // Seuils spécifiques par paramètre
        public int PriceFactor = 2000; // Multiplicateur de prix (2000 par défaut pour les armes)
        public List<string> Slots = new List<string>(); // Liste des types d'items autorisés (Armor, Weapon, Shield...)
        public List<object> Components = new List<object>();
        public List<DynamicParam> DynamicParams = new List<DynamicParam>();
        public Dictionary<string, int> EpicParams; // Paramètres à vérifier pour le seuil épique
        public Dictionary<string, Dictionary<string, double>> PriceTables = new Dictionary<string, Dictionary<string, double>>();
    }

    // --- Phase 2.B : The Builder Engine ---
    // Binder pour gérer les changements de namespace/type dans WOTR
    public class WOTRTypeBinder : DefaultSerializationBinder
    {
        public override Type BindToType(string assemblyName, string typeName)
        {
            try {
                return base.BindToType(assemblyName, typeName);
            } catch {
                // Si le type n'est pas trouvé, on essaie des namespaces alternatifs
                var typesToTry = new List<string> {
                    typeName,
                    typeName.Replace("Kingmaker.Designers.Mechanics.WeaponEnchants.", "Kingmaker.Designers.Mechanics.Facts."),
                    typeName.Replace("Kingmaker.Designers.Mechanics.WeaponEnchants.", "Kingmaker.UnitLogic.FactLogic."),
                    "Kingmaker.Designers.Mechanics.Facts." + typeName.Split('.').Last(),
                    "Kingmaker.UnitLogic.FactLogic." + typeName.Split('.').Last(),
                    "Kingmaker.Blueprints.Items.Ecnchantments." + typeName.Split('.').Last()
                };

                foreach (var tryType in typesToTry) {
                    try {
                        var t = base.BindToType("Assembly-CSharp", tryType);
                        if (t != null) {
                            Main.ModEntry.Logger.Log($"[CUSTOM_ENCHANTS] Binder: Redirected '{typeName}' to '{tryType}'");
                            return t;
                        }
                    } catch { }
                }
                Main.ModEntry.Logger.Error($"[CUSTOM_ENCHANTS] Binder FATAL: Could not resolve type '{typeName}'");
                return null;
            }
        }
    }

    public class CustomEnchantsFile
    {
        public Dictionary<string, object> ComponentDefinitions = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        public List<CustomEnchantmentData> Models = new List<CustomEnchantmentData>();
    }

    public class CustomEnchantmentsBuilder
    {
        public static HashSet<BlueprintGuid> InjectedGuids = new HashSet<BlueprintGuid>();
        public static List<CustomEnchantmentData> AllModels = new List<CustomEnchantmentData>();
        
        // Bibliothèque globale des composants
        public static Dictionary<string, object> ComponentLibrary = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        
        private static Dictionary<string, CustomEnchantmentData> _customModels = new Dictionary<string, CustomEnchantmentData>();
        private static HashSet<string> _currentlyBuilding = new HashSet<string>();
        private static Dictionary<string, BlueprintScriptableObject> _dynamicCache = new Dictionary<string, BlueprintScriptableObject>();
        private static readonly object _lock = new object();

        public static CustomEnchantmentData GetModelById(string id, bool isFeature = false)
        {
            if (AllModels == null) return null;
            return AllModels.FirstOrDefault(m => string.Equals(m.EnchantId, id, StringComparison.OrdinalIgnoreCase) && (isFeature ? m.Type == "Feature" : m.Type != "Feature"));
        }

        public static void BuildAndInjectAll()
        {
            // Les fichiers sont copiés à la racine du mod par le script de build
            string compPath = Path.Combine(Main.ModEntry.Path, "CustomEnchants_Components.json");
            string enchPath = Path.Combine(Main.ModEntry.Path, "CustomEnchants_Enchantments.json");
            string bpPath = Path.Combine(Main.ModEntry.Path, "CustomEnchants_Blueprints.json");

            var settings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Auto,
                ContractResolver = new OwlcatContractResolver(),
                Binder = new WOTRTypeBinder(),
                NullValueHandling = NullValueHandling.Ignore,
                Formatting = Formatting.Indented
            };
            settings.Converters.Add(new BlueprintReferenceConverter());
            var serializer = JsonSerializer.Create(settings);

            try
            {
                if (File.Exists(compPath) && File.Exists(enchPath) && File.Exists(bpPath))
                {
                    Main.ModEntry.Logger.Log($"[CUSTOM_ENCHANTS] Loading from split files: {compPath}, {enchPath} and {bpPath}");
                    
                    using (var sr = new StreamReader(compPath))
                    using (var jr = new JsonTextReader(sr))
                    {
                        ComponentLibrary = serializer.Deserialize<Dictionary<string, object>>(jr) ?? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                    }
                    
                    AllModels = new List<CustomEnchantmentData>();
                    
                    using (var sr = new StreamReader(enchPath))
                    using (var jr = new JsonTextReader(sr))
                    {
                        var enchants = serializer.Deserialize<List<CustomEnchantmentData>>(jr);
                        if (enchants != null) AllModels.AddRange(enchants);
                    }

                    using (var sr = new StreamReader(bpPath))
                    using (var jr = new JsonTextReader(sr))
                    {
                        var bps = serializer.Deserialize<List<CustomEnchantmentData>>(jr);
                        if (bps != null) AllModels.AddRange(bps);
                    }
                    
                    Main.ModEntry.Logger.Log($"[CUSTOM_ENCHANTS] Loaded {ComponentLibrary?.Count ?? 0} components and {AllModels?.Count ?? 0} models.");
                }
                else
                {
                    Main.ModEntry.Logger.Error($"[CUSTOM_ENCHANTS] FATAL: Missing split configuration files! Expected {compPath}, {enchPath} and {bpPath}");
                    return;
                }
                
                if (AllModels == null) {
                    Main.ModEntry.Logger.Error("[CUSTOM_ENCHANTS] Deserialization returned NULL.");
                    return;
                }
                
                Main.ModEntry.Logger.Log($"[CUSTOM_ENCHANTS] Successfully deserialized {AllModels.Count} models.");
                _customModels.Clear();

                // Validation des modèles chargés
                for (int i = AllModels.Count - 1; i >= 0; i--)
                {
                    var m = AllModels[i];
                    bool isValid = true;
                    if (string.IsNullOrEmpty(m.EnchantId)) {
                        Main.ModEntry.Logger.Error($"[CUSTOM_ENCHANTS] VALIDATION FAILED: Model at index {i} has no EnchantId!");
                        isValid = false;
                    }
                    if (m.BaseName == null && m.NameCompleted == null) {
                        Main.ModEntry.Logger.Error($"[CUSTOM_ENCHANTS] VALIDATION FAILED: Model '{m.EnchantId}' has no name (BaseName/NameCompleted)!");
                        isValid = false;
                    }
                    if (m.Type != "Feature" && (m.Slots == null || m.Slots.Count == 0)) {
                        Main.ModEntry.Logger.Warning($"[CUSTOM_ENCHANTS] VALIDATION WARNING: Model '{m.EnchantId}' has no slots defined. It will not appear in affinity displays.");
                    }
                    
                    if (!isValid) {
                        Main.ModEntry.Logger.Error($"[CUSTOM_ENCHANTS] Removing invalid model from list.");
                        AllModels.RemoveAt(i);
                    }
                }

                Main.ModEntry.Logger.Log($"[CUSTOM_ENCHANTS] Deserialization SUCCESS: Loaded {AllModels.Count} models.");

                foreach (var model in AllModels)
                {
                    string internalKey = Helpers.GetLocalizedString(model.BaseName ?? model.NameCompleted);
                    _customModels[internalKey] = model;
                    Main.ModEntry.Logger.Log($"[CUSTOM_ENCHANTS] REGISTERED: ID='{model.EnchantId}', Name='{internalKey}', Type='{model.Type}'");

                    // On pré-injecte le modèle de base si c'est une Feature (utile pour les résistances)
                    if (model.Type == "Feature")
                    {
                        try
                        {
                            if (string.IsNullOrEmpty(model.Guid))
                            {
                                model.Guid = DynamicGuidHelper.GenerateModelGuid(model.EnchantId, true).ToString();
                            }
                            var guid = BlueprintGuid.Parse(model.Guid);
                            var bp = CreateDynamicBlueprint(model, guid, new List<int>(), 0xFFF);
                            if (bp != null)
                            {
                                ResourcesLibrary.BlueprintsCache.AddCachedBlueprint(guid, bp);
                                Main.ModEntry.Logger.Log($"[CUSTOM_ENCHANTS] Successfully pre-injected feature: {model.BaseName} with GUID {guid}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Main.ModEntry.Logger.Error($"[CUSTOM_ENCHANTS] Error pre-injecting feature {model.BaseName}: {ex}");
                        }
                    }
                }
                Main.ModEntry.Logger.Log("[CUSTOM_ENCHANTS] All models registered successfully.");
            }
            catch (Exception ex)
            {
                Main.ModEntry.Logger.Error($"[CUSTOM_ENCHANTS] Global error loading configuration files: {ex}");
            }
        }

        public static BlueprintScriptableObject GetOrBuildDynamicBlueprint(BlueprintGuid guid)
        {
            return GetOrBuildDynamicBlueprint(guid.ToString());
        }

        public static BlueprintScriptableObject GetOrBuildDynamicBlueprint(string guidStr)
        {
            lock (_lock)
            {
                // 1. Cache local rapide
                if (_dynamicCache.TryGetValue(guidStr, out var cached)) return cached;

                if (_currentlyBuilding.Contains(guidStr)) return null;
                _currentlyBuilding.Add(guidStr);
                
                BlueprintScriptableObject result = null;
                try
                {
                    var guid = BlueprintGuid.Parse(guidStr);

                    // 2. Cache du jeu
                    result = (BlueprintScriptableObject)ResourcesLibrary.TryGetBlueprint(guid);
                    if (result != null) 
                    {
                        _dynamicCache[guidStr] = result;
                        return result;
                    }

                    // Décodage du GUID
                    if (!DynamicGuidHelper.TryDecodeGuid(guid, out string enchantId, out List<int> vals, out int mask))
                        return null;

                    bool isFeature = vals.Count > 0 && vals[0] == 1;

                    // Support spécial pour les objets magiques (Baguettes 901, Potions 902, Parchemins 903)
                    if (enchantId == "901" || enchantId == "902" || enchantId == "903")
                    {
                        string spellGuid = GetSpellGuidByHash(vals.Skip(1).ToList());
                        if (string.IsNullOrEmpty(spellGuid)) return null;
                        
                        int cl = vals.Count > 5 ? vals[5] : 1;
                        int sl = vals.Count > 6 ? vals[6] : 1;
                        
                        if (!SpellScanner.AvailableSpells.TryGetValue(spellGuid, out var spellData))
                            spellData = new SpellData { Guid = spellGuid, Name = "Unknown Spell" };

                        if (enchantId == "901") result = GetOrBuildWand(spellData, cl, sl);
                        else if (enchantId == "902") result = GetOrBuildPotion(spellData, cl, sl);
                        else if (enchantId == "903") result = GetOrBuildScroll(spellData, cl, sl);
                    }

                    if (result == null)
                    {
                        var model = GetModelById(enchantId, isFeature);
                        if (model != null)
                        {
                            result = CreateDynamicBlueprint(model, guid, vals.Skip(1).ToList(), mask);
                        }
                    }

                    if (result != null) 
                    {
                        _dynamicCache[guidStr] = result;
                    }
                }
                catch (Exception ex)
                {
                    Main.ModEntry.Logger.Error($"[DYNAMIC_ENCHANT] EXCEPTION in GetOrBuildDynamicBlueprint for {guidStr}: {ex}");
                }
                finally
                {
                    _currentlyBuilding.Remove(guidStr);
                }
                return result;
            }
        }

        private static BlueprintScriptableObject CreateDynamicBlueprint(CustomEnchantmentData model, BlueprintGuid guid, List<int> paramValues, int mask)
        {
            if (model.EnchantId == "007") return BuildComplexMetamagicRod(model, null, guid, paramValues, mask) as BlueprintScriptableObject;
            Main.ModEntry.Logger.Log($"[DYNAMIC_ENCHANT] Starting creation of {Helpers.GetLocalizedString(model.BaseName ?? model.NameCompleted)} for GUID {guid} (Mask: 0x{mask:X3})");
            BlueprintScriptableObject bp = null;
            try {
                switch (model.Type)
                {
                    case "Weapon":
                    case "WeaponEnchantment": bp = Activator.CreateInstance(typeof(BlueprintWeaponEnchantment)) as BlueprintScriptableObject; break;
                    case "Armor":
                    case "ArmorEnchantment": bp = Activator.CreateInstance(typeof(BlueprintArmorEnchantment)) as BlueprintScriptableObject; break;
                    case "Other":
                    case "EquipmentEnchantment": bp = Activator.CreateInstance(typeof(BlueprintEquipmentEnchantment)) as BlueprintScriptableObject; break;
                    case "Feature": bp = Activator.CreateInstance(typeof(BlueprintFeature)) as BlueprintScriptableObject; break;
                    case "UsableItem": bp = Activator.CreateInstance(Type.GetType("Kingmaker.Blueprints.Items.Equipment.BlueprintItemEquipmentUsable, Assembly-CSharp")) as BlueprintScriptableObject; break;
                    case "ActivatableAbility": bp = Activator.CreateInstance(Type.GetType("Kingmaker.UnitLogic.ActivatableAbilities.BlueprintActivatableAbility, Assembly-CSharp")) as BlueprintScriptableObject; break;
                    case "Buff": bp = Activator.CreateInstance(typeof(BlueprintBuff)) as BlueprintScriptableObject; break;
                    case "AbilityResource": bp = Activator.CreateInstance(typeof(BlueprintAbilityResource)) as BlueprintScriptableObject; break;
                }
                
                if (bp == null) {
                    Main.ModEntry.Logger.Error($"[DYNAMIC_ENCHANT] FATAL: Could not create instance for type {model.Type}");
                    return null;
                }
 
                // (Le cas 007 est déjà géré au début de la fonction)
            } catch (Exception ex) {
                Main.ModEntry.Logger.Error($"[DYNAMIC_ENCHANT] Blueprint instantiation failed: {ex}");
                return null;
            }

            if (bp == null) return null;

            bp.name = $"Dynamic_{model.EnchantId}_{guid}";
            bp.AssetGuid = guid;

            // Injection des composants avec filtrage par masque
            var components = new List<BlueprintComponent>();
            var indexMap = new Dictionary<int, int>(); // Map entre l'index JSON et l'index réel dans le blueprint

            for (int i = 0; i < model.Components.Count; i++)
            {
                // Vérification du masque (bit i)
                if ((mask & (1 << i)) == 0) continue;

                object compObj = model.Components[i];
                object actualComp = compObj;

                // Résolution par ID si c'est une string (Bibliothèque)
                if (compObj is string compId)
                {
                    if (ComponentLibrary.TryGetValue(compId, out object libComp)) {
                        actualComp = libComp;
                    } else {
                        Main.ModEntry.Logger.Error($"[DYNAMIC_ENCHANT] Component ID '{compId}' not found in library!");
                        continue;
                    }
                }

                if (actualComp == null) continue;

                try
                {
                    var settings = new JsonSerializerSettings { 
                        ContractResolver = new OwlcatContractResolver(),
                        TypeNameHandling = TypeNameHandling.All
                    };
                    settings.Converters.Add(new BlueprintReferenceConverter());

                    // Clonage profond du composant pour éviter de modifier la source
                    var json = JsonConvert.SerializeObject(actualComp, settings);
                    var clone = JsonConvert.DeserializeObject(json, actualComp.GetType(), settings) as BlueprintComponent;
                    
                    if (clone != null)
                    {
                        clone.name = $"${clone.GetType().Name}${Guid.NewGuid()}";
                        clone.OwnerBlueprint = bp;

                        try
                        {
                            var onDes = typeof(BlueprintComponent).GetMethod("OnDeserialized", BindingFlags.Instance | BindingFlags.NonPublic);
                            onDes?.Invoke(clone, new object[] { new System.Runtime.Serialization.StreamingContext() });
                        }
                        catch {}

                        indexMap[i] = components.Count;
                        components.Add(clone);
                        Main.ModEntry.Logger.Log($"[DYNAMIC_ENCHANT] Added component {i} ({clone.GetType().Name}) to blueprint.");
                    }
                }
                catch (Exception ex)
                {
                    Main.ModEntry.Logger.Error($"[DYNAMIC_ENCHANT] Error cloning component {i}: {ex}");
                }
            }

            bp.ComponentsArray = components.ToArray();

            Main.ModEntry.Logger.Log($"[DYNAMIC_ENCHANT] BP {bp.name} has {bp.ComponentsArray.Length} components.");

            // Appel de l'initialisation globale sur le blueprint (cela réveille les composants)
            try { bp.OnEnable(); } catch (Exception ex) { Main.ModEntry.Logger.Warning($"OnEnable failed for {bp.name}: {ex.Message}"); }

            // Initialisation des textes
            // --- RÉSOLUTION DES PARAMÈTRES POUR LE NOMMAGE ---
            var replacements = new Dictionary<string, string>();
            for (int i = 0; i < model.DynamicParams.Count && i < paramValues.Count; i++)
            {
                var p = model.DynamicParams[i];
                var val = paramValues[i];
                string resolvedVal = val.ToString();

                // Si c'est un Enum, on essaie de récupérer le nom localisé
                if (p.Type == "Enum" && !string.IsNullOrEmpty(p.EnumTypeName))
                {
                    try {
                        var enumType = Type.GetType(p.EnumTypeName);
                        if (enumType != null) {
                            string enumName = Enum.GetName(enumType, val);
                            
                            // Support des noms virtuels (SaveAll...)
                            if (string.IsNullOrEmpty(enumName) && p.EnumOverrides != null) {
                                foreach (var ovr in p.EnumOverrides) {
                                    int? ovrValue = null;
                                    if (ovr.Value is Newtonsoft.Json.Linq.JObject jo && jo["Value"] != null) ovrValue = (int)jo["Value"];
                                    else if (ovr.Value is EnumOverrideData eod) ovrValue = eod.Value;
                                    else if (ovr.Value is Dictionary<string, object> dict && dict.TryGetValue("Value", out object v)) ovrValue = Convert.ToInt32(v);

                                    if (ovrValue.HasValue && ovrValue.Value == val) {
                                        enumName = ovr.Key;
                                        break;
                                    }
                                }
                            }

                            if (enumName == "None" || val == 0) {
                                resolvedVal = "";
                            } else if (!string.IsNullOrEmpty(enumName)) {
                                if (p.EnumOverrides != null && p.EnumOverrides.TryGetValue(enumName, out object ovrObj))
                                    resolvedVal = Helpers.GetLocalizedString(ovrObj);
                                else if (p.EnumTypeName.Contains("DamageEnergyType"))
                                    resolvedVal = Helpers.GetString("energy_" + enumName, enumName);
                                else
                                    resolvedVal = Helpers.GetString("ui_enum_" + enumName, enumName);
                            }
                        }
                    } catch { }
                }
                
                Main.ModEntry.Logger.Log($"[DYNAMIC_ENCHANT] Resolved parameter '{p.Name}': {val} -> '{resolvedVal}'");
                replacements[p.Name] = resolvedVal;
            }

            string finalName = Helpers.GetLocalizedString(model.NameCompleted ?? model.BaseName, replacements);
            
            // Nettoyage des doubles espaces (si certains enums étaient vides/None)
            while (finalName.Contains("  ")) finalName = finalName.Replace("  ", " ");
            finalName = finalName.Trim();

            string prefix = model.Prefix != null ? Helpers.GetLocalizedString(model.Prefix, replacements) : "";
            string suffix = model.Suffix != null ? Helpers.GetLocalizedString(model.Suffix, replacements) : "";

            if (!string.IsNullOrEmpty(prefix) && !finalName.StartsWith(prefix)) 
                finalName = prefix + " " + finalName;
            
            if (!string.IsNullOrEmpty(suffix) && !finalName.Contains(suffix)) 
                finalName = finalName + " " + suffix;

            finalName = finalName.Replace("  ", " ").Trim();
            string staticDesc = !string.IsNullOrEmpty(model.EnchantDescKey) ? Helpers.GetString(model.EnchantDescKey, "") : "";

            if (bp is BlueprintItemEnchantment ench)
            {
                var tEnch = typeof(BlueprintItemEnchantment);
                tEnch.GetField("m_EnchantName", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(ench, Helpers.CreateString($"{bp.name}.Name", finalName));
                tEnch.GetField("m_Description", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(ench, Helpers.CreateString($"{bp.name}.Desc", staticDesc));
                tEnch.GetField("m_Prefix", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(ench, Helpers.CreateString($"{bp.name}.Prefix", Helpers.GetLocalizedString(model.Prefix, replacements) ?? ""));
                tEnch.GetField("m_Suffix", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(ench, Helpers.CreateString($"{bp.name}.Suffix", Helpers.GetLocalizedString(model.Suffix, replacements) ?? ""));
                tEnch.GetField("m_EnchantmentCost", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(ench, model.EnchantmentCost);
            }
            else if (bp is BlueprintFeature featData)
            {
                var tFact = typeof(BlueprintUnitFact);
                tFact.GetField("m_DisplayName", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(featData, Helpers.CreateString($"{bp.name}.Name", finalName));
                tFact.GetField("m_Description", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(featData, Helpers.CreateString($"{bp.name}.Desc", staticDesc));
            }
            else if (bp is BlueprintItem itemData)
            {
                var tItem = typeof(BlueprintItem);
                tItem.GetField("m_DisplayNameText", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(itemData, Helpers.CreateString($"{bp.name}.Name", finalName));
                tItem.GetField("m_DescriptionText", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(itemData, Helpers.CreateString($"{bp.name}.Desc", staticDesc));
            }
            else if (bp is BlueprintActivatableAbility actAbilityData)
            {
                var tFact = typeof(BlueprintUnitFact);
                tFact.GetField("m_DisplayName", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(actAbilityData, Helpers.CreateString($"{bp.name}.Name", finalName));
                tFact.GetField("m_Description", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(actAbilityData, Helpers.CreateString($"{bp.name}.Desc", staticDesc));

                if (model.EnchantId == "103" || model.EnchantId == "106")
                {
                    actAbilityData.ActivationType = AbilityActivationType.Immediately;
                    actAbilityData.DeactivateImmediately = true;
                }
            }
            else if (bp is BlueprintBuff buffData)
            {
                var tFact = typeof(BlueprintUnitFact);
                tFact.GetField("m_DisplayName", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(buffData, Helpers.CreateString($"{bp.name}.Name", finalName));
                tFact.GetField("m_Description", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(buffData, Helpers.CreateString($"{bp.name}.Desc", staticDesc));
            }

            // --- LIAISONS SPÉCIALES (RODS & RESOURCES) ---
            if (bp is BlueprintItemEquipmentUsable usableItem)
            {
                var abilityGuid = DynamicGuidHelper.GenerateGuid("103", paramValues.ToArray(), false, mask);
                GetOrBuildDynamicBlueprint(abilityGuid);
                var abilityRef = new BlueprintActivatableAbilityReference();
                abilityRef.ReadGuidFromGuid(abilityGuid);
                usableItem.m_ActivatableAbility = abilityRef;
            }

            if (bp is BlueprintActivatableAbility abilityLink)
            {
                // Liaison Resource -> Ability (si présent)
                var resLogic = abilityLink.ComponentsArray.OfType<ActivatableAbilityResourceLogic>().FirstOrDefault();
                if (resLogic != null)
                {
                    var resGuid = DynamicGuidHelper.GenerateGuid("105", paramValues.ToArray(), false, mask);
                    GetOrBuildDynamicBlueprint(resGuid);
                    
                    var resRef = new BlueprintAbilityResourceReference();
                    resRef.ReadGuidFromGuid(resGuid);
                    
                    var resField = resLogic.GetType().GetField("m_RequiredResource", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    if (resField != null) resField.SetValue(resLogic, resRef);
                    Main.ModEntry.Logger.Log($"[DYNAMIC_ENCHANT] Linked Resource {resGuid} to Ability {bp.name}");
                }
            }

            if (bp is BlueprintBuff buffTarget)
            {
                var rodComp = buffTarget.ComponentsArray.OfType<MetamagicRodMechanics>().FirstOrDefault();
                if (rodComp != null)
                {
                    var abilityGuid = DynamicGuidHelper.GenerateGuid("103", paramValues.ToArray(), false, mask);
                    var abilityRef = new BlueprintActivatableAbilityReference();
                    abilityRef.ReadGuidFromGuid(abilityGuid);
                    
                    var field = rodComp.GetType().GetField("m_RodAbility", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    if (field != null) field.SetValue(rodComp, abilityRef);
                }
            }

            if (bp is BlueprintFeature featComp)
            {
                // --- CAS SPÉCIAL : BONUS AUX JETS DE SAUVEGARDE (101) ---
                if (model.EnchantId == "101")
                {
                    var statVal = paramValues.Count > 0 ? paramValues[0] : 0;
                    var valueVal = paramValues.Count > 2 ? paramValues[2] : 0;
                    var saveComps = featComp.ComponentsArray.OfType<AddStatBonus>().ToArray();
                    if (saveComps.Length >= 3)
                    {
                        if (statVal == 99) // TOUS (99 pour éviter le débordement à 255)
                        {
                            saveComps[0].Stat = StatType.SaveFortitude; saveComps[0].Value = valueVal;
                            saveComps[1].Stat = StatType.SaveReflex; saveComps[1].Value = valueVal;
                            saveComps[2].Stat = StatType.SaveWill; saveComps[2].Value = valueVal;
                            Main.ModEntry.Logger.Log($"[DYNAMIC_ENCHANT] Applied ALL SAVES bonus (+{valueVal})");
                        }
                        else
                        {
                            saveComps[0].Stat = (StatType)statVal; saveComps[0].Value = valueVal;
                            saveComps[1].Value = 0;
                            saveComps[2].Value = 0;
                            Main.ModEntry.Logger.Log($"[DYNAMIC_ENCHANT] Applied SINGLE SAVE bonus (Stat={(StatType)statVal}, Value={valueVal})");
                        }
                    }
                }

                var addFacts = featComp.ComponentsArray.OfType<AddFacts>().FirstOrDefault();
                if (addFacts != null)
                {
                    // Si c'est le modèle 107 (Feature Chargée), on lie vers l'Ability 106
                    string abilityId = model.EnchantId == "107" ? "106" : "103";
                    var abilityGuid = DynamicGuidHelper.GenerateGuid(abilityId, paramValues.ToArray(), false, mask);
                    GetOrBuildDynamicBlueprint(abilityGuid);
                    
                    var factRef = new BlueprintUnitFactReference();
                    factRef.ReadGuidFromGuid(abilityGuid);
                    addFacts.m_Facts = new BlueprintUnitFactReference[] { factRef };
                    Main.ModEntry.Logger.Log($"[DYNAMIC_ENCHANT] Linked Fact {abilityGuid} to Feature {bp.name}");
                }
            }

            // Application des paramètres dynamiques aux composants via indexMap
            for (int i = 0; i < model.DynamicParams.Count && i < paramValues.Count; i++)
            {
                var p = model.DynamicParams[i];
                var val = paramValues[i];

                if (p.ComponentIndex == -1)
                {
                    // Injection dans TOUS les composants actifs
                    foreach (var c in bp.ComponentsArray)
                    {
                        ApplyValueToField(c, p.FieldName, val);
                    }
                    Main.ModEntry.Logger.Log($"[DYNAMIC_ENCHANT] Injected parameter '{p.Name}' (Value={val}) into ALL {bp.ComponentsArray.Length} components.");
                }
                else if (p.ComponentIndex >= 0)
                {
                    if (indexMap.TryGetValue(p.ComponentIndex, out int actualIdx))
                    {
                        var c = bp.ComponentsArray[actualIdx];
                        Main.ModEntry.Logger.Log($"[DYNAMIC_ENCHANT] Injecting parameter '{p.Name}': Value={val} into {p.FieldName} of {c.GetType().Name}");
                        ApplyValueToField(c, p.FieldName, val);
                    }
                    else
                    {
                        Main.ModEntry.Logger.Log($"[DYNAMIC_ENCHANT] Parameter '{p.Name}' skipped: component {p.ComponentIndex} is inactive.");
                    }
                }
            }

            // --- AUTO-GÉNÉRATION DE LA DESCRIPTION (si aucune description statique n'est fournie) ---
            if (string.IsNullOrEmpty(staticDesc))
            {
                string generated = EnchantmentDescriptionGenerator.Generate(bp);
                if (!string.IsNullOrEmpty(generated))
                {
                    var field = bp.GetType().GetField("m_Description", BindingFlags.Instance | BindingFlags.NonPublic) 
                                ?? bp.GetType().GetField("m_Description", BindingFlags.Instance | BindingFlags.Public);
                    
                    if (field != null)
                    {
                        field.SetValue(bp, Helpers.CreateString($"{bp.name}.Desc", generated));
                        Main.ModEntry.Logger.Log($"[DYNAMIC_ENCHANT] Auto-generated description for {bp.name}");
                    }
                }
            }

            // Gestion spéciale pour les références circulaires (ex: Enchantment -> Feature)
            if (bp is BlueprintItemEnchantment itemEnch)
            {
                var featureComp = itemEnch.ComponentsArray.OfType<AddUnitFeatureEquipment>().FirstOrDefault();
                if (featureComp != null)
                {
                    string targetId = model.FeatureModelId ?? model.EnchantId;
                    var featGuid = DynamicGuidHelper.GenerateGuid(targetId, paramValues.ToArray(), true, mask);
                    Main.ModEntry.Logger.Log($"[DYNAMIC_ENCHANT] Linking to Feature GUID: {featGuid}");
                    
                    // On s'assure que la feature est construite
                    GetOrBuildDynamicBlueprint(featGuid); 

                    // On met à jour la référence dans le composant
                    var refField = featureComp.GetType().GetField("m_Feature", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (refField != null)
                    {
                        var featRef = new BlueprintFeatureReference();
                        featRef.ReadGuidFromGuid(featGuid);
                        refField.SetValue(featureComp, featRef);
                        Main.ModEntry.Logger.Log($"[DYNAMIC_ENCHANT] Successfully linked feature {featGuid} to enchantment {bp.name}");
                    }
                }
            }

            object dummy;
            OwlcatModificationsManager.Instance.OnResourceLoaded(bp, bp.AssetGuid.ToString(), out dummy);
            ResourcesLibrary.BlueprintsCache.AddCachedBlueprint(bp.AssetGuid, bp);
            InjectedGuids.Add(bp.AssetGuid);
            Main.ModEntry.Logger.Log($"[DYNAMIC_ENCHANT] SUCCESSFULLY CREATED AND INJECTED: {bp.name} ({bp.AssetGuid})");

            _dynamicCache[guid.ToString()] = bp;
            return bp;
        }

        // Suppression de la méthode déplacée dans Helpers
        private static void ApplyValueToField(object target, string fieldPath, int value)
        {
            var parts = fieldPath.Split('.');
            object current = target;
            
            // On garde une trace des parents et des champs pour pouvoir "remonter" les modifications si on croise des structs
            var parents = new List<object>();
            var fields = new List<FieldInfo>();
            
            parents.Add(target);

            for (int i = 0; i < parts.Length - 1; i++)
            {
                var fieldName = parts[i];
                var field = current.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                         ?? current.GetType().GetField("m_" + fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                
                if (field == null)
                {
                    Main.ModEntry.Logger.Warning($"[DYNAMIC_ENCHANT] Field path not found: {fieldName} in {current.GetType().Name} (skipping)");
                    return;
                }

                fields.Add(field);
                object next = field.GetValue(current);
                
                if (next == null)
                {
                    try {
                        next = Activator.CreateInstance(field.FieldType);
                        field.SetValue(current, next);
                    } catch {
                        Main.ModEntry.Logger.Error($"[DYNAMIC_ENCHANT] Null intermediate value and could not instantiate: {fieldName}");
                        return;
                    }
                }
                
                current = next;
                parents.Add(current);
            }

            var lastFieldName = parts.Last();
            var lastField = current.GetType().GetField(lastFieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                         ?? current.GetType().GetField("m_" + lastFieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            
            if (lastField != null)
            {
                if (lastField.FieldType.IsEnum)
                {
                    if (Attribute.IsDefined(lastField.FieldType, typeof(FlagsAttribute)))
                    {
                        int currentVal = 0;
                        try { currentVal = Convert.ToInt32(lastField.GetValue(current)); } catch { }
                        lastField.SetValue(current, Enum.ToObject(lastField.FieldType, currentVal | value));
                    }
                    else
                    {
                        lastField.SetValue(current, Enum.ToObject(lastField.FieldType, value));
                    }
                }
                else
                {
                    lastField.SetValue(current, Convert.ChangeType(value, lastField.FieldType));
                }

                // --- CRITIQUE : Remontée des modifications pour les structs ---
                // Si 'current' est un struct, il a été modifié dans sa version "boxée". 
                // On doit le ré-assigner à son parent, et ainsi de suite.
                for (int i = parents.Count - 2; i >= 0; i--)
                {
                    var p = parents[i];
                    var f = fields[i];
                    var child = parents[i + 1];
                    
                    if (p.GetType().IsValueType || child.GetType().IsValueType)
                    {
                        f.SetValue(p, child);
                    }
                    else break; // Si on arrive sur une classe, plus besoin de remonter
                }

                Main.ModEntry.Logger.Log($"[DYNAMIC_ENCHANT] Successfully set {lastField.Name} to {value}");
            }
            else
            {
                Main.ModEntry.Logger.Warning($"[DYNAMIC_ENCHANT] Field not found: {lastFieldName} in {current.GetType().Name} (skipping)");
            }
        }
        public static BlueprintItemEquipmentUsable GetOrBuildScroll(SpellData spellData, int cl, int sl)
        {
            int[] hash = GetSpellHash(spellData.Guid);
            int[] parameters = new int[] { hash[0], hash[1], hash[2], hash[3], cl, sl };
            BlueprintGuid guid = DynamicGuidHelper.GenerateGuid("903", parameters);
            
            var existing = ResourcesLibrary.TryGetBlueprint(guid) as BlueprintItemEquipmentUsable;
            if (existing != null) return existing;

            Main.ModEntry.Logger.Log($"[SCROLL-BUILD] Building new scroll: {spellData.Name} (CL:{cl}, SL:{sl})");

            var spell = ResourcesLibrary.TryGetBlueprint(BlueprintGuid.Parse(spellData.Guid)) as BlueprintAbility;
            if (spell == null) return null;

            var bp = Activator.CreateInstance(Type.GetType("Kingmaker.Blueprints.Items.Equipment.BlueprintItemEquipmentUsable, Assembly-CSharp")) as BlueprintItemEquipmentUsable;
            bp.name = $"Scroll_{spell.name}_{cl}_{sl}";
            bp.AssetGuid = guid;

            // Propriétés de base du parchemin
            bp.Type = UsableItemType.Scroll;
            bp.m_Ability = spell.ToReference<BlueprintAbilityReference>();
            bp.CasterLevel = cl;
            bp.SpellLevel = sl;
            bp.Charges = 1;
            bp.SpendCharges = true;
            bp.RestoreChargesOnRest = false;

            // Visuel et Identification (via Réflexion car privés dans BlueprintItem)
            var itemType = typeof(BlueprintItem);
            itemType.GetField("m_Icon", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(bp, spell.Icon);
            itemType.GetField("m_DisplayNameText", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(bp, Helpers.CreateString($"{bp.name}.Name", $"Scroll of {spell.Name}"));
            itemType.GetField("m_DescriptionText", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(bp, spell.m_Description);
            itemType.GetField("m_Weight", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(bp, 0.2f);
            itemType.GetField("m_Cost", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(bp, 10); // Prix de base symbolique, recalculé par le jeu

            // Enregistrement
            ResourcesLibrary.BlueprintsCache.AddCachedBlueprint(guid, bp);
            Main.ModEntry.Logger.Log($"[SCROLL-BUILD] Registered dynamic scroll GUID: {guid}");

            return bp;
        }

        public static BlueprintItemEquipmentUsable GetOrBuildWand(SpellData spellData, int cl, int sl)
        {
            int[] hash = GetSpellHash(spellData.Guid);
            int[] parameters = new int[] { hash[0], hash[1], hash[2], hash[3], cl, sl };
            BlueprintGuid guid = DynamicGuidHelper.GenerateGuid("901", parameters);
            
            var existing = ResourcesLibrary.TryGetBlueprint(guid) as BlueprintItemEquipmentUsable;
            if (existing != null) return existing;

            Main.ModEntry.Logger.Log($"[WAND-BUILD] Building new wand: {spellData.Name} (CL:{cl}, SL:{sl})");

            var spell = ResourcesLibrary.TryGetBlueprint(BlueprintGuid.Parse(spellData.Guid)) as BlueprintAbility;
            if (spell == null) return null;

            var bp = Activator.CreateInstance(Type.GetType("Kingmaker.Blueprints.Items.Equipment.BlueprintItemEquipmentUsable, Assembly-CSharp")) as BlueprintItemEquipmentUsable;
            bp.name = $"Wand_{spell.name}_{cl}_{sl}";
            bp.AssetGuid = guid;

            bp.Type = UsableItemType.Wand;
            bp.m_Ability = spell.ToReference<BlueprintAbilityReference>();
            bp.CasterLevel = cl;
            bp.SpellLevel = sl;
            bp.Charges = 50;
            bp.SpendCharges = true;
            bp.RestoreChargesOnRest = false;

            var itemType = typeof(BlueprintItem);
            itemType.GetField("m_Icon", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(bp, spell.Icon);
            itemType.GetField("m_DisplayNameText", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(bp, Helpers.CreateString($"{bp.name}.Name", $"Wand of {spell.Name}"));
            itemType.GetField("m_DescriptionText", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(bp, spell.m_Description);
            itemType.GetField("m_Weight", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(bp, 1.0f);
            itemType.GetField("m_Cost", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(bp, 100);

            // Fetch a vanilla wand to copy its vital components and visuals
            try
            {
                var bpCache = ResourcesLibrary.BlueprintsCache;
                var cacheType = bpCache.GetType();
                var loadedField = cacheType.GetField("m_LoadedBlueprints", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (loadedField != null)
                {
                    var loadedDict = loadedField.GetValue(bpCache) as System.Collections.IDictionary;
                    BlueprintItemEquipmentUsable template = null;
                    if (loadedDict != null)
                    {
                        foreach (System.Collections.DictionaryEntry entry in loadedDict)
                        {
                            // Entry value is typically a BlueprintCacheEntry or directly the Blueprint
                            object val = entry.Value;
                            var bpProp = val?.GetType().GetProperty("Blueprint", BindingFlags.Public | BindingFlags.Instance);
                            object actualBp = bpProp != null ? bpProp.GetValue(val) : val;

                            if (actualBp is BlueprintItemEquipmentUsable wand && wand.Type == UsableItemType.Wand && wand.AssetGuid.ToString() != guid.ToString())
                            {
                                template = wand;
                                break;
                            }
                        }
                    }

                    if (template != null)
                    {
                        if (template.ComponentsArray != null)
                        {
                            bp.ComponentsArray = template.ComponentsArray.ToArray();
                        }
                        
                        var equipmentType = typeof(Kingmaker.Blueprints.Items.Equipment.BlueprintItemEquipment);
                        var visual = equipmentType.GetField("m_EquipmentEntity", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(template);
                        equipmentType.GetField("m_EquipmentEntity", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(bp, visual);

                        var visualAlt = equipmentType.GetField("m_EquipmentEntityAlternatives", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(template);
                        equipmentType.GetField("m_EquipmentEntityAlternatives", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(bp, visualAlt);
                    }
                }
            }
            catch (Exception ex)
            {
                Main.ModEntry.Logger.Error($"[WAND-BUILD] Failed to copy vanilla wand template: {ex.Message}");
            }

            ResourcesLibrary.BlueprintsCache.AddCachedBlueprint(guid, bp);
            return bp;
        }

        public static BlueprintItemEquipmentUsable GetOrBuildPotion(SpellData spellData, int cl, int sl)
        {
            int[] hash = GetSpellHash(spellData.Guid);
            int[] parameters = new int[] { hash[0], hash[1], hash[2], hash[3], cl, sl };
            BlueprintGuid guid = DynamicGuidHelper.GenerateGuid("902", parameters);
            
            var existing = ResourcesLibrary.TryGetBlueprint(guid) as BlueprintItemEquipmentUsable;
            if (existing != null) return existing;

            Main.ModEntry.Logger.Log($"[POTION-BUILD] Building new potion: {spellData.Name} (CL:{cl}, SL:{sl})");

            var spell = ResourcesLibrary.TryGetBlueprint(BlueprintGuid.Parse(spellData.Guid)) as BlueprintAbility;
            if (spell == null) return null;

            var bp = Activator.CreateInstance(Type.GetType("Kingmaker.Blueprints.Items.Equipment.BlueprintItemEquipmentUsable, Assembly-CSharp")) as BlueprintItemEquipmentUsable;
            bp.name = $"Potion_{spell.name}_{cl}_{sl}";
            bp.AssetGuid = guid;

            bp.Type = UsableItemType.Potion;
            bp.m_Ability = spell.ToReference<BlueprintAbilityReference>();
            bp.CasterLevel = cl;
            bp.SpellLevel = sl;
            bp.Charges = 1;
            bp.SpendCharges = true;
            bp.RestoreChargesOnRest = false;

            var itemType = typeof(BlueprintItem);
            itemType.GetField("m_Icon", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(bp, spell.Icon);
            itemType.GetField("m_DisplayNameText", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(bp, Helpers.CreateString($"{bp.name}.Name", $"Potion of {spell.Name}"));
            itemType.GetField("m_DescriptionText", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(bp, spell.m_Description);
            itemType.GetField("m_Weight", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(bp, 0.5f);
                    ResourcesLibrary.BlueprintsCache.AddCachedBlueprint(guid, bp);
            return bp;
        }

        private static BlueprintScriptableObject BuildComplexMetamagicRod(CustomEnchantmentData model, BlueprintItemEquipmentUsable dummy, BlueprintGuid guid, List<int> paramValues, int mask)
        {
            // Debugging des paramètres reçus
            string pstr = string.Join(", ", paramValues);
            Main.ModEntry.Logger.Log($"[DYNAMIC_ROD] Raw Params received: {pstr}");

            // Format RÉEL reçu : [0:Grade] [1:Charges] [2:Count] [3+:Metamagics]
            int grade      = paramValues.Count > 0 ? Math.Max(0, Math.Min(2, paramValues[0])) : 0;
            int charges    = paramValues.Count > 1 ? paramValues[1] : 3;
            int mCount     = paramValues.Count > 2 ? paramValues[2] : 0;

            Main.ModEntry.Logger.Log($"[DYNAMIC_ROD] Extracted: Grade={grade}, Charges={charges}, mCount={mCount}, Mask=0x{mask:X}");

            string baseName = $"Rod_{guid}";

            // GUIDs avec signature c2af
            int[] subParams = paramValues.ToArray();
            var abilityGuid = DynamicGuidHelper.GenerateGuid("103", subParams, false, mask);
            var buffGuid    = DynamicGuidHelper.GenerateGuid("106", subParams, false, mask);

            // --- DOCUMENTATION DES PIÈGES (Gotchas) ---
            // 1. CLONAGE JSON : Créer des blueprints "from-scratch" via new() ou Helpers.CreateBlueprint
            // provoque des SaveError car il manque des structures internes (m_Context, Ranks, etc.).
            // On clone donc des templates stables via JSON pour garantir l'intégrité de l'objet.
            const string vanillaRodItemGuid    = "6f5d788ee7384e47895bc58a291eec7f"; 
            const string vanillaRodAbilityGuid = "fc7bd8b05d6147aab2d8b4378801db05"; 
            const string vanillaRodBuffGuid    = "41a7f08a27e04909aea1c43cd1895260"; 

            var templateItem    = ResourcesLibrary.TryGetBlueprint(BlueprintGuid.Parse(vanillaRodItemGuid))    as BlueprintItemEquipmentUsable;
            var templateAbility = ResourcesLibrary.TryGetBlueprint(BlueprintGuid.Parse(vanillaRodAbilityGuid)) as BlueprintActivatableAbility;
            var templateBuff    = ResourcesLibrary.TryGetBlueprint(BlueprintGuid.Parse(vanillaRodBuffGuid))    as BlueprintBuff;

            if (templateItem == null || templateAbility == null || templateBuff == null)
            {
                Main.ModEntry.Logger.Error("[DYNAMIC_ROD] FATAL: Could not load vanilla templates.");
                return null;
            }

            var cloneSettings = new Newtonsoft.Json.JsonSerializerSettings { 
                TypeNameHandling = Newtonsoft.Json.TypeNameHandling.All, 
                ContractResolver = new OwlcatContractResolver() 
            };
            cloneSettings.Converters.Add(new BlueprintReferenceConverter());

            BlueprintBuff buff;
            BlueprintActivatableAbility ability;

            try {
                buff    = Newtonsoft.Json.JsonConvert.DeserializeObject<BlueprintBuff>(Newtonsoft.Json.JsonConvert.SerializeObject(templateBuff, cloneSettings), cloneSettings);
                ability = Newtonsoft.Json.JsonConvert.DeserializeObject<BlueprintActivatableAbility>(Newtonsoft.Json.JsonConvert.SerializeObject(templateAbility, cloneSettings), cloneSettings);
            } catch (Exception ex) {
                Main.ModEntry.Logger.Error($"[DYNAMIC_ROD] Clone failed: {ex.Message}");
                return null;
            }

            // 2. FIX SAVE-ERROR (Value cannot be null key) :
            // Lors de la sérialisation, Owlcat crée un ComponentsDictionary. Si un composant cloné
            // a son champ 'name' nul, le dictionnaire crash. On force donc un nom sur chaque composant.
            if (buff.ComponentsArray != null) {
                for (int i = 0; i < buff.ComponentsArray.Length; i++) 
                    if (buff.ComponentsArray[i] != null) buff.ComponentsArray[i].name = $"$RodBuffComp${i}";
            }
            if (ability.ComponentsArray != null) {
                for (int i = 0; i < ability.ComponentsArray.Length; i++) 
                    if (ability.ComponentsArray[i] != null) ability.ComponentsArray[i].name = $"$RodAbilComp${i}";
            }

            // 3. SIGNATURE c2af : Les GUIDs dynamiques doivent commencer par 'c2af' pour que notre 
            // BlueprintConverter personnalisé puisse les intercepter et les reconstruire au chargement.
            buff.name = baseName + "_Buff"; buff.AssetGuid = buffGuid;
            ability.name = baseName + "_Ability"; ability.AssetGuid = abilityGuid;

            // 3. INJECTION DES MÉCANIQUES (Multi-Métamagie)
            // Le composant vanilla ne gère bien qu'une seule métamagie à la fois.
            // Pour supporter le cumul (ex: Empower + Extend), on injecte un composant par effet.
            
            // On nettoie d'abord les composants de mécanique d'origine du clone
            var components = buff.ComponentsArray.ToList();
            components.RemoveAll(c => c is Kingmaker.Designers.Mechanics.Facts.MetamagicRodMechanics);
            
            // On ajoute un composant pour chaque bit de métamagie présent dans le masque
            foreach (Kingmaker.UnitLogic.Abilities.Metamagic m in Enum.GetValues(typeof(Kingmaker.UnitLogic.Abilities.Metamagic)))
            {
                if ((int)m == 0) continue;
                if ((mask & (int)m) != 0)
                {
                    var mComp = new Kingmaker.Designers.Mechanics.Facts.MetamagicRodMechanics();
                    mComp.name = $"$MetamagicRodMechanics${m}";
                    mComp.Metamagic = m;
                    mComp.MaxSpellLevel = (grade == 0 ? 3 : (grade == 1 ? 6 : 9));
                    
                    // Liaison à l'Ability
                    typeof(Kingmaker.Designers.Mechanics.Facts.MetamagicRodMechanics)
                        .GetField("m_RodAbility", BindingFlags.Instance | BindingFlags.NonPublic)
                        ?.SetValue(mComp, ability.ToReference<BlueprintActivatableAbilityReference>());
                    
                    components.Add(mComp);
                    Main.ModEntry.Logger.Log($"[DYNAMIC_ROD] Added mechanics component for: {m} (MaxLevel={mComp.MaxSpellLevel})");
                }
            }
            buff.ComponentsArray = components.ToArray();

            // Liaison Identités et Buff
            buff.name = baseName + "_Buff"; buff.AssetGuid = buffGuid;
            ability.name = baseName + "_Ability"; ability.AssetGuid = abilityGuid;
            ability.m_Buff = buff.ToReference<BlueprintBuffReference>();
            ability.Group = ActivatableAbilityGroup.MetamagicRod;

            // 4. RESSOURCE : On désactive la consommation de ressource globale (MetamagicRodLesserResource)
            // pour que le sceptre utilise ses propres charges d'item.
            var resLogic = ability.ComponentsArray?.OfType<ActivatableAbilityResourceLogic>().FirstOrDefault();
            if (resLogic == null) {
                resLogic = new ActivatableAbilityResourceLogic();
                resLogic.name = "$ActivatableAbilityResourceLogic$";
                ability.ComponentsArray = (ability.ComponentsArray ?? new Kingmaker.Blueprints.BlueprintComponent[0]).Concat(new[] { resLogic }).ToArray();
            }
            resLogic.SpendType = ActivatableAbilityResourceLogic.ResourceSpendType.None;

            // 5. ITEM STABILITÉ : On utilise Helpers.CreateBlueprint pour l'item car le clonage JSON 
            // intégral de l'item causait des instabilités. On copie juste les visuels.
            var item = Helpers.CreateBlueprint<BlueprintItemEquipmentUsable>(guid.ToString(), baseName);
            item.Type = UsableItemType.Other;
            
            // 6. CHARGES : Le champ 'Charges' public ne suffit pas toujours pour l'affichage/usage, 
            // il faut forcer le champ interne 'm_Charges' via réflexion.
            item.Charges = charges;
            typeof(BlueprintItemEquipmentUsable).GetField("m_Charges", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(item, charges);
            
            item.SpendCharges = true;
            item.RestoreChargesOnRest = true;
            item.m_Ability = null;
            item.m_InventoryEquipSound = templateItem.m_InventoryEquipSound;
            item.m_Icon = templateItem.m_Icon;
            item.m_Weight = templateItem.m_Weight;
            item.m_Cost = templateItem.m_Cost;

            // Liaison Item -> Ability
            typeof(BlueprintItemEquipment).GetField("m_ActivatableAbility", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.SetValue(item, ability.ToReference<BlueprintActivatableAbilityReference>());
            typeof(BlueprintItemEquipment).GetField("m_EquipmentEntity", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(item, templateItem.m_EquipmentEntity);

            // Injection des textes
            var replacements = new Dictionary<string, string>();
            
            // Grade localisé
            string gradeKey = (grade == 0 ? "ui_grade_lesser" : (grade == 1 ? "ui_grade_normal" : "ui_grade_greater"));
            replacements["Grade"]     = Helpers.GetString(gradeKey, grade == 0 ? "Lesser" : (grade == 1 ? "Normal" : "Greater"));
            replacements["GradeDesc"] = Helpers.GetString(gradeKey + "_desc", "");
            replacements["Charges"]   = charges.ToString();
            
            var mNames = new List<string>();
            // On récupère les noms de métamagie soit via le masque (multi-effet) soit via paramValues
            if (mask != 0)
            {
                foreach (Kingmaker.UnitLogic.Abilities.Metamagic m in Enum.GetValues(typeof(Kingmaker.UnitLogic.Abilities.Metamagic)))
                {
                    if ((int)m != 0 && (mask & (int)m) != 0)
                    {
                        string n = m.ToString();
                        mNames.Add(Helpers.GetString("ui_enum_" + n, n));
                    }
                }
            }
            else
            {
                for (int i = 0; i < mCount; i++) {
                    int index = 3 + i;
                    if (index >= paramValues.Count) break;
                    int val = paramValues[index];
                    if (val == 0) continue;
                    string n = Enum.GetName(typeof(Kingmaker.UnitLogic.Abilities.Metamagic), val) ?? "None";
                    mNames.Add(Helpers.GetString("ui_enum_" + n, n));
                }
            }

            if (mNames.Count == 0) mNames.Add(Helpers.GetString("ui_none", "None"));
            replacements["Metamagic"] = string.Join(", ", mNames);

            // Nom de l'item
            string finalName = Helpers.GetLocalizedString(model.NameCompleted ?? model.BaseName, replacements);
            typeof(BlueprintItem).GetField("m_DisplayNameText", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(item, Helpers.CreateString($"{item.name}.Name", finalName));

            // Description de l'item (FIX: <null> description)
            string finalDesc = Helpers.GetLocalizedString("ui_desc_metamagic_rod", replacements);
            typeof(BlueprintItem).GetField("m_DescriptionText", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(item, Helpers.CreateString($"{item.name}.Description", finalDesc));

            // Enregistrement — les GUIDs portent tous la signature c2af, la sauvegarde fonctionnera
            ResourcesLibrary.BlueprintsCache.AddCachedBlueprint(buff.AssetGuid, buff);
            ResourcesLibrary.BlueprintsCache.AddCachedBlueprint(ability.AssetGuid, ability);
            ResourcesLibrary.BlueprintsCache.AddCachedBlueprint(item.AssetGuid, item);

            Main.ModEntry.Logger.Log($"[DYNAMIC_ROD] Created rod '{finalName}' (item={guid}, ability={abilityGuid}, buff={buffGuid})");
            return item;
        }

 
        private static int[] GetSpellHash(string spellGuid)
        {
            if (string.IsNullOrEmpty(spellGuid)) return new int[] { 0, 0, 0, 0 };
            try {
                var guid = BlueprintGuid.Parse(spellGuid);
                byte[] bytes = guid.ToByteArray();
                return new int[] { bytes[0], bytes[1], bytes[2], bytes[3] };
            } catch {
                return new int[] { 0, 0, 0, 0 };
            }
        }

        private static string GetSpellGuidByHash(List<int> vals)
        {
            if (vals.Count < 4) return null;
            try {
                byte[] target = new byte[] { (byte)vals[0], (byte)vals[1], (byte)vals[2], (byte)vals[3] };
                uint hash = BitConverter.ToUInt32(target, 0);
                
                if (SpellScanner.HashToGuid.TryGetValue(hash, out string guid)) return guid;
            } catch {}
            
            return null;
        }
    }
}
