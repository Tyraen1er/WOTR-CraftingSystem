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
                    Main.ModEntry.Logger.Warning($"[DEBUG_REF] Activator.CreateInstance returned null for {objectType.Name}");
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
                Main.ModEntry.Logger.Error($"[DEBUG_REF] Error reading reference {objectType.Name}: {ex}");
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
    public class DynamicParam
    {
        public string Name;
        public string Label;
        public string Type; // Slider, Enum
        public string EnumTypeName; // Pour Type == Enum
        public int Min = 0; // Pour Type == Slider
        public int Max = 100; // Pour Type == Slider
        public int Step = 1;
        public List<string> EnumOnly = null; // Optionnel : ne garder que ces valeurs d'enum
        public List<string> EnumExclude = null; // Optionnel : exclure ces valeurs d'enum
        public Dictionary<string, object> EnumOverrides = null; // Optionnel : surcharger le texte affiché (localisable)
        public object DefaultValue = null; // Optionnel : valeur pré-sélectionnée par défaut (int ou string)

        // Cible pour l'injection
        public int ComponentIndex;
        public string FieldName; // ex: "Type", "Value.Value"
    }

    public class CustomEnchantmentData
    {
        public string Guid;
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
        private static readonly object _lock = new object();

        public static CustomEnchantmentData GetModelById(string id, bool isFeature = false)
        {
            if (AllModels == null) return null;
            return AllModels.FirstOrDefault(m => string.Equals(m.EnchantId, id, StringComparison.OrdinalIgnoreCase) && (isFeature ? m.Type == "Feature" : m.Type != "Feature"));
        }

        public static void BuildAndInjectAll()
        {
            // Le fichier semble être à la racine d'après les logs
            string path = Path.Combine(Main.ModEntry.Path, "CustomEnchants.json");
            if (!File.Exists(path)) {
                path = Path.Combine(Main.ModEntry.Path, "ModConfig", "CustomEnchants.json");
            }
            Main.ModEntry.Logger.Log($"[CUSTOM_ENCHANTS] STARTING: Searching file at '{path}'");
            
            if (!File.Exists(path))
            {
                Main.ModEntry.Logger.Error($"[CUSTOM_ENCHANTS] FATAL: File not found at {path}!");
                return;
            }

            try
            {
                Main.ModEntry.Logger.Log($"[CUSTOM_ENCHANTS] File read success. Content length: {File.ReadAllText(path).Length} chars.");
                
                string json = File.ReadAllText(path, System.Text.Encoding.UTF8);
                var settings = new JsonSerializerSettings
                {
                    TypeNameHandling = TypeNameHandling.Auto,
                    ContractResolver = new OwlcatContractResolver(),
                    Binder = new WOTRTypeBinder(),
                    NullValueHandling = NullValueHandling.Ignore,
                    Formatting = Formatting.Indented
                };
                settings.Converters.Add(new BlueprintReferenceConverter());

                var token = Newtonsoft.Json.Linq.JToken.Parse(json);
                if (token is Newtonsoft.Json.Linq.JArray)
                {
                    AllModels = token.ToObject<List<CustomEnchantmentData>>(JsonSerializer.Create(settings));
                    ComponentLibrary.Clear();
                }
                else
                {
                    var file = token.ToObject<CustomEnchantsFile>(JsonSerializer.Create(settings));
                    AllModels = file.Models;
                    ComponentLibrary = file.ComponentDefinitions ?? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                    Main.ModEntry.Logger.Log($"[CUSTOM_ENCHANTS] Loaded {ComponentLibrary.Count} component definitions from library.");
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
                    string internalKey = Helpers.GetLocalizedString(model.BaseName ?? model.NameCompleted); // Utilise la locale actuelle, mais au moins c'est une string
                    _customModels[internalKey] = model;
                    Main.ModEntry.Logger.Log($"[CUSTOM_ENCHANTS] Registered model: {internalKey} (ID: {model.EnchantId})");

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
                Main.ModEntry.Logger.Error($"[CUSTOM_ENCHANTS] Global error loading CustomEnchants.json: {ex}");
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
                if (_currentlyBuilding.Contains(guidStr)) return null;
                _currentlyBuilding.Add(guidStr);
                try
                {
                    Main.ModEntry.Logger.Log($"[DYNAMIC_ENCHANT] Requesting blueprint for GUID: {guidStr}");
                    var guid = BlueprintGuid.Parse(guidStr);

                    // On vérifie si par hasard il n'a pas été injecté entre temps
                    var existing = (BlueprintScriptableObject)ResourcesLibrary.TryGetBlueprint(guid);
                    if (existing != null) return existing;

                    // Décodage du GUID
                    if (!DynamicGuidHelper.TryDecodeGuid(guid, out string enchantId, out List<int> vals, out int mask))
                    {
                        Main.ModEntry.Logger.Warning($"[DYNAMIC_ENCHANT] Failed to decode GUID: {guidStr}");
                        return null;
                    }

                    // Trouver le modèle
                    bool isFeature = vals.Count > 0 && vals[0] == 1;
                    var model = GetModelById(enchantId, isFeature);
                    
                    Main.ModEntry.Logger.Log($"[DYNAMIC_ENCHANT] Decoded: ID={enchantId}, isFeature={isFeature}, Mask=0x{mask:X3}, Params={string.Join(",", vals)}");

                    if (model == null)
                    {
                        Main.ModEntry.Logger.Warning($"[DYNAMIC_ENCHANT] No model found for EnchantId {enchantId} (isFeature: {isFeature}). AllModels count: {AllModels?.Count ?? 0}");
                        return null;
                    }

                    return CreateDynamicBlueprint(model, guid, vals.Skip(1).ToList(), mask);
                }
                catch (Exception ex)
                {
                    Main.ModEntry.Logger.Error($"[DYNAMIC_ENCHANT] EXCEPTION in GetOrBuildDynamicBlueprint for {guidStr}: {ex}");
                    return null;
                }
                finally
                {
                    _currentlyBuilding.Remove(guidStr);
                }
            }
        }

        private static BlueprintScriptableObject CreateDynamicBlueprint(CustomEnchantmentData model, BlueprintGuid guid, List<int> paramValues, int mask)
        {
            Main.ModEntry.Logger.Log($"[DYNAMIC_ENCHANT] Starting creation of {Helpers.GetLocalizedString(model.BaseName ?? model.NameCompleted)} for GUID {guid} (Mask: 0x{mask:X3})");
            BlueprintScriptableObject bp = null;
            try {
                switch (model.Type)
                {
                    case "WeaponEnchantment": bp = Activator.CreateInstance(typeof(BlueprintWeaponEnchantment)) as BlueprintScriptableObject; break;
                    case "ArmorEnchantment": bp = Activator.CreateInstance(typeof(BlueprintArmorEnchantment)) as BlueprintScriptableObject; break;
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
                                    if (ovr.Value is Newtonsoft.Json.Linq.JObject jo && jo["Value"] != null) {
                                        try {
                                            if (Convert.ToInt32(jo["Value"]) == val) {
                                                enumName = ovr.Key;
                                                break;
                                            }
                                        } catch {}
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

            return bp;
        }

        // Suppression de la méthode déplacée dans Helpers
        private static void ApplyValueToField(object target, string fieldPath, int value)
        {
            var parts = fieldPath.Split('.');
            object current = target;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                var fieldName = parts[i];
                var field = current.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                         ?? current.GetType().GetField("m_" + fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                
                if (field == null)
                {
                    Main.ModEntry.Logger.Error($"[DYNAMIC_ENCHANT] Field path not found: {fieldName} in {current.GetType().Name}");
                    return;
                }
                current = field.GetValue(current);
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
                        // Pour les enums [Flags], on combine (OR) au lieu d'écraser
                        int currentVal = 0;
                        try {
                            currentVal = Convert.ToInt32(lastField.GetValue(current));
                        } catch { }
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
                Main.ModEntry.Logger.Log($"[DYNAMIC_ENCHANT] Successfully set/combined {lastField.Name} with {value}");
            }
            else
            {
                Main.ModEntry.Logger.Error($"[DYNAMIC_ENCHANT] Final field not found: {lastFieldName} (also tried m_{lastFieldName}) in {current.GetType().Name}");
            }
        }
        public static BlueprintItemEquipmentUsable GetOrBuildScroll(SpellData spellData, int cl, int sl)
        {
            string seed = $"Scroll_{spellData.Guid}_{cl}_{sl}";
            BlueprintGuid guid = CreateGuidFromSeed(seed);
            
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

        private static BlueprintGuid CreateGuidFromSeed(string seed)
        {
            using (MD5 md5 = MD5.Create())
            {
                byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(seed));
                // On injecte notre signature "c2af" au début du GUID pour l'identifier
                hash[0] = 0xC2;
                hash[1] = 0xAF;
                return new BlueprintGuid(new Guid(hash));
            }
        }
    }
}
