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
    public class CustomEnchantmentData
    {
        public string Guid;
        public string Name;
        public string Type; // WeaponEnchantment, ArmorEnchantment, Feature
        public string EnchantNameKey;
        public string EnchantDescKey;
        public string Prefix;
        public string Suffix;
        public int EnchantmentCost;
        public List<BlueprintComponent> Components = new List<BlueprintComponent>();
    }

    // --- Phase 2.B : The Builder Engine ---
    public static class CustomEnchantmentsBuilder
    {
        public static HashSet<BlueprintGuid> InjectedGuids = new HashSet<BlueprintGuid>();

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
                            if (comp == null) continue;
                            comp.name = $"${comp.GetType().Name}${Guid.NewGuid()}";
                        }
                    }

                    // Initialiser le blueprint comme le ferait le jeu (OnEnable gère les OwnerBlueprint des composants)
                    Main.ModEntry.Logger.Log($"[DEBUG_CUSTOM_ENCHANT] Initializing {bp.name}...");
                    bp.OnEnable();
                    
                    // Informer le système de modding d'Owlcat (permet le patchage dynamique)
                    object dummy;
                    OwlcatModificationsManager.Instance.OnResourceLoaded(bp, bp.AssetGuid.ToString(), out dummy);

                    // Injection dans le cache
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
    }
}
