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
using UnityEngine;

namespace CraftingSystem
{
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
        public int PriceFactor = 2000; // Multiplicateur de prix (2000 par défaut pour les armes)
        public List<string> Slots = new List<string>(); // Liste des types d'items autorisés (Armor, Weapon, Shield...)
        public List<BlueprintComponent> Components = new List<BlueprintComponent>();
        public List<DynamicParam> DynamicParams = new List<DynamicParam>();
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

    public class CustomEnchantmentsBuilder
    {
        public static HashSet<BlueprintGuid> InjectedGuids = new HashSet<BlueprintGuid>();
        public static List<CustomEnchantmentData> AllModels = new List<CustomEnchantmentData>();
        private static Dictionary<string, CustomEnchantmentData> _customModels = new Dictionary<string, CustomEnchantmentData>();
        private static HashSet<string> _currentlyBuilding = new HashSet<string>();
        private static readonly object _lock = new object();

        public static CustomEnchantmentData GetModelById(string id, bool isFeature = false)
        {
            // Filtrage par ID et type pour éviter les collisions (ex: Feature vs Enchantment partageant le même ID 001)
            return AllModels.FirstOrDefault(m => m.EnchantId == id && (isFeature ? m.Type == "Feature" : m.Type != "Feature"));
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

                var models = JsonConvert.DeserializeObject<List<CustomEnchantmentData>>(json, settings);
                
                if (models == null) {
                    Main.ModEntry.Logger.Error("[CUSTOM_ENCHANTS] Deserialization returned NULL.");
                    return;
                }
                
                Main.ModEntry.Logger.Log($"[CUSTOM_ENCHANTS] Successfully deserialized {models.Count} models.");
                AllModels = models;
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
                            var bp = CreateDynamicBlueprint(model, guid, new List<int>());
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
                    if (!DynamicGuidHelper.TryDecodeGuid(guid, out string enchantId, out List<int> vals))
                    {
                        return null;
                    }

                    // Trouver le modèle
                    var model = GetModelById(enchantId, vals.Count > 0 && vals[0] == 1);
                    if (model == null)
                    {
                        Main.ModEntry.Logger.Warning($"[DYNAMIC_ENCHANT] No model found for EnchantId {enchantId}");
                        return null;
                    }

                    return CreateDynamicBlueprint(model, guid, vals.Skip(1).ToList());
                }
                finally
                {
                    _currentlyBuilding.Remove(guidStr);
                }
            }
        }

        private static BlueprintScriptableObject CreateDynamicBlueprint(CustomEnchantmentData model, BlueprintGuid guid, List<int> paramValues)
        {
            Main.ModEntry.Logger.Log($"[DYNAMIC_ENCHANT] Starting creation of {Helpers.GetLocalizedString(model.BaseName ?? model.NameCompleted)} for GUID {guid}");
            BlueprintScriptableObject bp = null;
            try {
                if (model.Type == "WeaponEnchantment" || model.Type == "Weapon") bp = new BlueprintWeaponEnchantment();
                else if (model.Type == "ArmorEnchantment" || model.Type == "Armor") bp = new BlueprintArmorEnchantment();
                else if (model.Type == "Other" || model.Type == "Equipment") bp = new BlueprintEquipmentEnchantment();
                else if (model.Type == "Feature") bp = new BlueprintFeature();
                
                if (bp != null) {
                    Main.ModEntry.Logger.Log($"[DEBUG] Created instance of {bp.GetType().FullName} using 'new'");
                    // On appelle OnEnable manuellement car on n'utilise pas CreateInstance
                    try { bp.OnEnable(); } catch {}
                } else {
                    Main.ModEntry.Logger.Error($"[DYNAMIC_ENCHANT] FATAL: Could not create instance for type {model.Type}");
                    return null;
                }
                
            } catch (Exception ex) {
                Main.ModEntry.Logger.Error($"[DYNAMIC_ENCHANT] Blueprint instantiation failed: {ex}");
            }

            if (bp == null) {
                Main.ModEntry.Logger.Error($"[DYNAMIC_ENCHANT] FATAL: Could not create instance for type {model.Type}");
                return null;
            }

            bp.name = $"Dynamic_{model.EnchantId}_{guid}";
            bp.AssetGuid = guid;

            // Injection des composants
            var components = new List<BlueprintComponent>();
            foreach (var comp in model.Components)
            {
                try
                {
                    Main.ModEntry.Logger.Log($"[DYNAMIC_ENCHANT] Cloning component: {comp.GetType().Name}");
                    var settings = new JsonSerializerSettings { 
                        ContractResolver = new OwlcatContractResolver(),
                        TypeNameHandling = TypeNameHandling.All
                    };
                    settings.Converters.Add(new BlueprintReferenceConverter());

                    var json = JsonConvert.SerializeObject(comp, settings);
                    var clone = JsonConvert.DeserializeObject(json, comp.GetType(), settings) as BlueprintComponent;
                    if (clone != null)
                    {
                        clone.name = $"${clone.GetType().Name}${Guid.NewGuid()}";
                        clone.OwnerBlueprint = bp;

                        // --- RÉACTIVATION MANUELLE DU COMPOSANT ---
                        try
                        {
                            var onDes = typeof(BlueprintComponent).GetMethod("OnDeserialized", BindingFlags.Instance | BindingFlags.NonPublic);
                            onDes?.Invoke(clone, new object[] { new System.Runtime.Serialization.StreamingContext() });
                        }
                        catch (Exception ex)
                        {
                            Main.ModEntry.Logger.Warning($"[DYNAMIC_ENCHANT] Failed to invoke OnDeserialized on {clone.GetType().Name}: {ex.Message}");
                        }

                        components.Add(clone);
                    }
                }
                catch (Exception ex)
                {
                    Main.ModEntry.Logger.Error($"[DYNAMIC_ENCHANT] Failed to clone component of type {comp.GetType().Name}: {ex}");
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
                            if (!string.IsNullOrEmpty(enumName))
                                resolvedVal = Helpers.GetString("energy_" + enumName, enumName); 
                        }
                    } catch { }
                }

                replacements[p.Name] = resolvedVal;
            }

            // Application des noms, préfixes et suffixes avec remplacements
            string prefix = model.Prefix != null ? Helpers.GetLocalizedString(model.Prefix, replacements) : "";
            string suffix = model.Suffix != null ? Helpers.GetLocalizedString(model.Suffix, replacements) : "";
            string finalName = Helpers.GetLocalizedString(model.NameCompleted ?? model.BaseName, replacements);

            if (!string.IsNullOrEmpty(prefix) && !finalName.StartsWith(prefix)) 
                finalName = prefix + " " + finalName;
            
            if (!string.IsNullOrEmpty(suffix) && !finalName.Contains(suffix)) 
                finalName = finalName + " " + suffix;

            finalName = finalName.Replace("  ", " ").Trim();
            string staticDesc = !string.IsNullOrEmpty(model.EnchantDescKey) ? Helpers.GetString(model.EnchantDescKey, "") : "";

            if (bp is BlueprintItemEnchantment ench)
            {
                var t = typeof(BlueprintItemEnchantment);
                t.GetField("m_EnchantName", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(ench, Helpers.CreateString($"{bp.name}.Name", finalName));
                t.GetField("m_Description", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(ench, Helpers.CreateString($"{bp.name}.Desc", staticDesc));
                t.GetField("m_Prefix", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(ench, Helpers.CreateString($"{bp.name}.Prefix", Helpers.GetLocalizedString(model.Prefix, replacements) ?? ""));
                t.GetField("m_Suffix", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(ench, Helpers.CreateString($"{bp.name}.Suffix", Helpers.GetLocalizedString(model.Suffix, replacements) ?? ""));
                t.GetField("m_EnchantmentCost", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(ench, model.EnchantmentCost);
            }
            else if (bp is BlueprintFeature feature)
            {
                var t = typeof(BlueprintUnitFact);
                t.GetField("m_DisplayName", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(feature, Helpers.CreateString($"{bp.name}.Name", finalName));
                t.GetField("m_Description", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(feature, Helpers.CreateString($"{bp.name}.Desc", staticDesc));
            }

            // Application des paramètres dynamiques aux composants
            for (int i = 0; i < model.DynamicParams.Count && i < paramValues.Count; i++)
            {
                var p = model.DynamicParams[i];
                var val = paramValues[i];

                if (p.ComponentIndex >= 0 && p.ComponentIndex < bp.ComponentsArray.Length)
                {
                    var c = bp.ComponentsArray[p.ComponentIndex];
                    Main.ModEntry.Logger.Log($"[DYNAMIC_ENCHANT] Injecting parameter '{p.Name}': Value={val} into {p.FieldName} of {c.GetType().Name}");
                    ApplyValueToField(c, p.FieldName, val);
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
                    var featGuid = DynamicGuidHelper.GenerateGuid(targetId, paramValues.ToArray(), true);
                    Main.ModEntry.Logger.Log($"[DYNAMIC_ENCHANT] Linking to Feature GUID: {featGuid}");
                    GetOrBuildDynamicBlueprint(featGuid.ToString());

                    var featRef = new BlueprintFeatureReference();
                    featRef.ReadGuidFromGuid(featGuid);
                    typeof(AddUnitFeatureEquipment).GetField("m_Feature", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(featureComp, featRef);
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
                if (lastField.FieldType.IsEnum) lastField.SetValue(current, Enum.ToObject(lastField.FieldType, value));
                else lastField.SetValue(current, Convert.ChangeType(value, lastField.FieldType));
                Main.ModEntry.Logger.Log($"[DYNAMIC_ENCHANT] Successfully set {lastField.Name} to {value}");
            }
            else
            {
                Main.ModEntry.Logger.Error($"[DYNAMIC_ENCHANT] Final field not found: {lastFieldName} (also tried m_{lastFieldName}) in {current.GetType().Name}");
            }
        }
    }
}
