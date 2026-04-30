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
            
            // Plus besoin de CreateInstance pour les blueprints dans les versions récentes de WOTR (ce ne sont plus des ScriptableObjects)
            /*
            if (typeof(ScriptableObject).IsAssignableFrom(objectType))
            {
                contract.DefaultCreator = () => ScriptableObject.CreateInstance(objectType);
            }
            */

            // Supprimer les appels OnDeserialized qui causent des crashs (NRE sur OwnerBlueprint)
            if (typeof(BlueprintComponent).IsAssignableFrom(objectType) || typeof(BlueprintScriptableObject).IsAssignableFrom(objectType))
            {
                contract.OnDeserializedCallbacks.Clear();
            }

            return contract;
        }
    }

    // --- Phase 2.C : BlueprintReferenceConverter ---
    public class BlueprintReferenceConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return typeof(BlueprintReferenceBase).IsAssignableFrom(objectType);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null) return null;

            string guidString = reader.Value as string;
            if (string.IsNullOrEmpty(guidString)) return null;

            if (guidString.StartsWith("!bp_"))
                guidString = guidString.Substring(4);

            var reference = Activator.CreateInstance(objectType) as BlueprintReferenceBase;
            var guid = BlueprintGuid.Parse(guidString);

            // Important : on doit remplir à la fois le champ 'guid' (string) et 'deserializedGuid' (BlueprintGuid)
            // car le jeu utilise 'deserializedGuid' pour ses accès internes, mais 'guid' pour la sérialisation.
            reference.ReadGuidFromGuid(guid);

            var field = typeof(BlueprintReferenceBase).GetField("guid", BindingFlags.Instance | BindingFlags.NonPublic);
            if (field != null) field.SetValue(reference, guid.ToString());

            return reference;
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
        public string Type; // Slider, Enum
        public string EnumTypeName; // Pour Type == Enum
        public int Min = 0; // Pour Type == Slider
        public int Max = 100; // Pour Type == Slider
        public int Step = 1;
        
        // Cible pour l'injection
        public int ComponentIndex;
        public string FieldName; // ex: "Type", "Value.Value"
    }

    public class CustomEnchantmentData
    {
        public string Guid;
        public string EnchantId; // ID à 3 chiffres (ex: 001) pour le générateur dynamique
        public string Name;
        public string Type; // WeaponEnchantment, ArmorEnchantment, Feature
        public string EnchantNameKey;
        public string EnchantDescKey;
        public string Prefix;
        public string Suffix;
        public int EnchantmentCost;
        public List<BlueprintComponent> Components = new List<BlueprintComponent>();
        public List<DynamicParam> DynamicParams = new List<DynamicParam>();
    }

    // --- Phase 2.B : The Builder Engine ---
    public static class CustomEnchantmentsBuilder
    {
        public static HashSet<BlueprintGuid> InjectedGuids = new HashSet<BlueprintGuid>();
        public static List<CustomEnchantmentData> AllModels = new List<CustomEnchantmentData>();
        private static bool _isBuilding = false;

        public static void BuildAndInjectAll()
        {
            try
            {
                string configPath = Path.Combine(Main.ModEntry.Path, "CustomEnchants.json");
                if (!File.Exists(configPath))
                {
                    Main.ModEntry.Logger.Log($"No CustomEnchants.json found at {configPath}, skipping.");
                    return;
                }

                var settings = new JsonSerializerSettings
                {
                    TypeNameHandling = TypeNameHandling.Auto,
                    ContractResolver = new OwlcatContractResolver(),
                    NullValueHandling = NullValueHandling.Ignore
                };
                settings.Converters.Add(new BlueprintReferenceConverter());

                var jsonContent = File.ReadAllText(configPath);
                var customDataList = JsonConvert.DeserializeObject<List<CustomEnchantmentData>>(jsonContent, settings);
                AllModels = customDataList ?? new List<CustomEnchantmentData>();

                if (customDataList == null) return;

                foreach (var data in customDataList)
                {
                    if (string.IsNullOrEmpty(data.Guid) || string.IsNullOrEmpty(data.Name)) continue;

                    BlueprintScriptableObject bp;
                    if (data.Type == "WeaponEnchantment") bp = new BlueprintWeaponEnchantment();
                    else if (data.Type == "ArmorEnchantment") bp = new BlueprintArmorEnchantment();
                    else if (data.Type == "Feature") bp = new BlueprintFeature();
                    else {
                        Main.ModEntry.Logger.Error($"Unknown Custom Type: {data.Type} for {data.Name}");
                        continue;
                    }

                    bp.name = data.Name;
                    // Directly set AssetGuid
                    bp.AssetGuid = BlueprintGuid.Parse(data.Guid);

                    if (bp is BlueprintItemEnchantment ench)
                    {
                        var tEnch = typeof(BlueprintItemEnchantment);
                        tEnch.GetField("m_EnchantName", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(ench, Helpers.CreateString(data.EnchantNameKey ?? data.Name + "_N", "Custom Enchant Name"));
                        tEnch.GetField("m_Description", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(ench, Helpers.CreateString(data.EnchantDescKey ?? data.Name + "_D", "Custom Enchant Description"));
                        tEnch.GetField("m_Prefix", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(ench, Helpers.CreateString(data.Name + "_Prefix", data.Prefix ?? ""));
                        tEnch.GetField("m_Suffix", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(ench, Helpers.CreateString(data.Name + "_Suffix", data.Suffix ?? ""));
                        tEnch.GetField("m_EnchantmentCost", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(ench, data.EnchantmentCost);
                        ench.ComponentsArray = data.Components.ToArray();
                    }
                    else if (bp is BlueprintFeature feature)
                    {
                        var tFeature = typeof(BlueprintUnitFact);
                        tFeature.GetField("m_DisplayName", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(feature, Helpers.CreateString(data.Name + "_FName", data.EnchantNameKey ?? "Custom Feature"));
                        tFeature.GetField("m_Description", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(feature, Helpers.CreateString(data.Name + "_FDesc", data.EnchantDescKey ?? "Custom Feature Desc"));
                        feature.ComponentsArray = data.Components.ToArray();
                    }

                    if (bp.ComponentsArray != null)
                    {
                        foreach (var comp in bp.ComponentsArray)
                        {
                            if (comp == null) {
                                Main.ModEntry.Logger.Warning($"[DEBUG_CUSTOM_ENCHANT] Null component found in {bp.name}!");
                                continue;
                            }
                            comp.name = $"${comp.GetType().Name}${Guid.NewGuid()}";
                            Main.ModEntry.Logger.Log($"[DEBUG_CUSTOM_ENCHANT] Component in {bp.name}: {comp.GetType().Name} (name: {comp.name})");

                            // Log details for some known components
                            if (comp is Kingmaker.UnitLogic.FactLogic.AddDamageResistanceEnergy dre) {
                                Main.ModEntry.Logger.Log($"  -> Type: {dre.Type}, Value: {dre.Value?.Value} (Type: {dre.Value?.ValueType})");
                            }
                            if (comp is Kingmaker.Designers.Mechanics.EquipmentEnchants.AddUnitFeatureEquipment aufe) {
                                var featRef = (Kingmaker.Blueprints.BlueprintFeatureReference)typeof(Kingmaker.Designers.Mechanics.EquipmentEnchants.AddUnitFeatureEquipment)
                                    .GetField("m_Feature", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(aufe);
                                Main.ModEntry.Logger.Log($"  -> Feature Ref: {featRef?.deserializedGuid}");
                            }
                        }
                    }

                    // Initialiser le blueprint comme le ferait le jeu (OnEnable gère les OwnerBlueprint des composants)
                    Main.ModEntry.Logger.Log($"[DEBUG_CUSTOM_ENCHANT] Initializing {bp.name}...");
                    bp.OnEnable();
                    
                    // Informer le système de modding d'Owlcat (permet le patchage dynamique)
                    object dummy;
                    OwlcatModificationsManager.Instance.OnResourceLoaded(bp, bp.AssetGuid.ToString(), out dummy);

                    // Injection dans le cache avec vérification de collision
                    var existing = ResourcesLibrary.TryGetBlueprint(bp.AssetGuid);
                    if (existing != null)
                    {
                        Main.ModEntry.Logger.Error($"[FATAL] GUID COLLISION: {bp.AssetGuid} is already used by {existing.name} ({existing.GetType().Name}). Injection aborted for {bp.name}.");
                        continue;
                    }

                    ResourcesLibrary.BlueprintsCache.AddCachedBlueprint(bp.AssetGuid, bp);
                    InjectedGuids.Add(bp.AssetGuid);
                    Main.ModEntry.Logger.Log($"[DEBUG_CUSTOM_ENCHANT] SUCCESSFULLY INJECTED: {bp.name} ({bp.AssetGuid})");
                }
            }
            catch (Exception ex)
            {
                Main.ModEntry.Logger.Error($"Error in BuildAndInjectAll: {ex}");
            }
        }

        public static BlueprintScriptableObject GetOrBuildDynamicBlueprint(string guidStr)
        {
            if (_isBuilding) return null;
            _isBuilding = true;
            try 
            {
                Main.ModEntry.Logger.Log($"[DYNAMIC_ENCHANT] Requesting blueprint for GUID: {guidStr}");
                var guid = BlueprintGuid.Parse(guidStr);
                
                // On vérifie si par hasard il n'a pas été injecté entre temps
                var existing = (BlueprintScriptableObject)ResourcesLibrary.TryGetBlueprint(guid);
                if (existing != null) 
                {
                    return existing;
                }

                // Décodage du GUID
                if (!DynamicGuidHelper.TryDecodeGuid(guid, out string enchantId, out List<int> vals))
                {
                    return null;
                }

                // Trouver le modèle
                var model = AllModels.FirstOrDefault(m => m.EnchantId == enchantId && (vals[0] == 1 ? m.Type == "Feature" : m.Type != "Feature"));
                if (model == null)
                {
                    return null;
                }

                return CreateDynamicBlueprint(model, guid, vals.Skip(1).ToList());
            }
            finally
            {
                _isBuilding = false;
            }
        }

        private static BlueprintScriptableObject CreateDynamicBlueprint(CustomEnchantmentData model, BlueprintGuid guid, List<int> paramValues)
        {
            Main.ModEntry.Logger.Log($"[DYNAMIC_ENCHANT] Starting creation of {model.Name} for GUID {guid}");
            BlueprintScriptableObject bp;
            if (model.Type == "WeaponEnchantment") bp = new BlueprintWeaponEnchantment();
            else if (model.Type == "ArmorEnchantment") bp = new BlueprintArmorEnchantment();
            else if (model.Type == "Feature") bp = new BlueprintFeature();
            else return null;

            bp.name = $"{model.Name}_{guid}";
            bp.AssetGuid = guid;

            // Injection des composants
            var components = new List<BlueprintComponent>();
            foreach (var comp in model.Components)
            {
                Main.ModEntry.Logger.Log($"[DYNAMIC_ENCHANT] Cloning component: {comp.GetType().Name}");
                var json = JsonConvert.SerializeObject(comp, new JsonSerializerSettings { ContractResolver = new OwlcatContractResolver() });
                var clone = JsonConvert.DeserializeObject(json, comp.GetType(), new JsonSerializerSettings { ContractResolver = new OwlcatContractResolver() }) as BlueprintComponent;
                if (clone != null) {
                    clone.name = $"${clone.GetType().Name}${Guid.NewGuid()}";
                    components.Add(clone);
                }
            }
            bp.ComponentsArray = components.ToArray();

            // Initialisation des textes (Réflexion car champs privés/protégés)
            if (bp is BlueprintItemEnchantment ench)
            {
                var t = typeof(BlueprintItemEnchantment);
                t.GetField("m_EnchantName", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(ench, Helpers.CreateString($"{bp.name}.Name", model.Name));
                t.GetField("m_Description", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(ench, Helpers.CreateString($"{bp.name}.Desc", ""));
                t.GetField("m_Prefix", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(ench, Helpers.CreateString($"{bp.name}.Prefix", model.Prefix ?? ""));
                t.GetField("m_Suffix", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(ench, Helpers.CreateString($"{bp.name}.Suffix", model.Suffix ?? ""));
                t.GetField("m_EnchantmentCost", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(ench, model.EnchantmentCost);
            }
            else if (bp is BlueprintFeature feature)
            {
                var t = typeof(BlueprintUnitFact);
                t.GetField("m_DisplayName", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(feature, Helpers.CreateString($"{bp.name}.Name", model.Name));
                t.GetField("m_Description", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(feature, Helpers.CreateString($"{bp.name}.Desc", ""));
            }

            // Application des paramètres dynamiques
            for (int i = 0; i < model.DynamicParams.Count && i < paramValues.Count; i++)
            {
                var p = model.DynamicParams[i];
                var val = paramValues[i];
                
                if (p.ComponentIndex < 0)
                {
                    Main.ModEntry.Logger.Log($"[DYNAMIC_ENCHANT] Parameter '{p.Name}' is UI-only for this model (Index -1), skipping local injection.");
                    continue;
                }

                if (p.ComponentIndex < bp.ComponentsArray.Length)
                {
                    var comp = bp.ComponentsArray[p.ComponentIndex];
                    Main.ModEntry.Logger.Log($"[DYNAMIC_ENCHANT] Injecting parameter '{p.Name}': Value={val} into {p.FieldName} of {comp.GetType().Name}");
                    ApplyValueToField(comp, p.FieldName, val);
                }
                else
                {
                    Main.ModEntry.Logger.Warning($"[DYNAMIC_ENCHANT] Parameter '{p.Name}' has invalid ComponentIndex {p.ComponentIndex} for {bp.name}");
                }
            }

            // Gestion spéciale pour les références circulaires (ex: Enchantment -> Feature)
            if (bp is BlueprintItemEnchantment itemEnch)
            {
                var featureComp = itemEnch.ComponentsArray.OfType<Kingmaker.Designers.Mechanics.EquipmentEnchants.AddUnitFeatureEquipment>().FirstOrDefault();
                if (featureComp != null)
                {
                    List<int> featVals = new List<int> { 1 }; // 1 = Feature
                    featVals.AddRange(paramValues);
                    var featGuid = DynamicGuidHelper.GenerateGuid(model.EnchantId, featVals.ToArray());
                    
                    Main.ModEntry.Logger.Log($"[DYNAMIC_ENCHANT] Linking to Feature GUID: {featGuid}");
                    GetOrBuildDynamicBlueprint(featGuid.ToString());

                    var featRef = new Kingmaker.Blueprints.BlueprintFeatureReference();
                    featRef.ReadGuidFromGuid(featGuid);
                    typeof(Kingmaker.Designers.Mechanics.EquipmentEnchants.AddUnitFeatureEquipment)
                        .GetField("m_Feature", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(featureComp, featRef);
                }
            }

            // Initialisation et Injection
            bp.OnEnable();
            
            object dummy;
            OwlcatModificationsManager.Instance.OnResourceLoaded(bp, bp.AssetGuid.ToString(), out dummy);

            ResourcesLibrary.BlueprintsCache.AddCachedBlueprint(bp.AssetGuid, bp);
            InjectedGuids.Add(bp.AssetGuid);
            Main.ModEntry.Logger.Log($"[DYNAMIC_ENCHANT] SUCCESSFULLY CREATED AND INJECTED: {bp.name} ({bp.AssetGuid})");

            return bp;
        }

        private static void ApplyValueToField(object target, string fieldPath, int value)
        {
            var parts = fieldPath.Split('.');
            object current = target;
            
            for (int i = 0; i < parts.Length - 1; i++)
            {
                var field = current.GetType().GetField(parts[i], BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field == null) {
                    Main.ModEntry.Logger.Error($"[DYNAMIC_ENCHANT] Field path not found: {parts[i]} in {current.GetType().Name}");
                    return;
                }
                current = field.GetValue(current);
            }

            var lastField = current.GetType().GetField(parts.Last(), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (lastField != null)
            {
                if (lastField.FieldType.IsEnum) {
                    var enumVal = Enum.ToObject(lastField.FieldType, value);
                    Main.ModEntry.Logger.Log($"[DYNAMIC_ENCHANT] Setting Enum field {lastField.Name} to {enumVal} ({value})");
                    lastField.SetValue(current, enumVal);
                } else {
                    Main.ModEntry.Logger.Log($"[DYNAMIC_ENCHANT] Setting field {lastField.Name} to {value}");
                    lastField.SetValue(current, Convert.ChangeType(value, lastField.FieldType));
                }
            }
            else {
                Main.ModEntry.Logger.Error($"[DYNAMIC_ENCHANT] Final field not found: {parts.Last()} in {current.GetType().Name}");
            }
        }
    }
}
