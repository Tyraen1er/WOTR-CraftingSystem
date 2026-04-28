using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using HarmonyLib;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.JsonSystem;
using Kingmaker.DialogSystem.Blueprints;
using Kingmaker.Localization;
using UnityEngine;
using Newtonsoft.Json.Linq;

namespace CraftingSystem
{
    public static class Helpers
    {
        public static Dictionary<string, string> CustomStrings = new Dictionary<string, string>();
        public static JObject RawLocalization;
        
        // Static GUIDs for our injected strings so they can be reliably referenced
        public static readonly string CraftingIntroGuid = "fa3e1f7d4e3347bdaeb88a1b6c8baab6";

        public static string GetString(string key, string fallback = null)
        {
            if (CustomStrings.ContainsKey(key))
            {
                return CustomStrings[key];
            }
            return fallback ?? key; // Return fallback or key if not found
        }

        public static void LoadLocalization(string modPath)
        {
            CustomStrings.Clear();
            RawLocalization = new JObject();
            var allStrings = new JObject();
            RawLocalization["strings"] = allStrings;

            LoadFile(Path.Combine(modPath, "Localization.json"), allStrings);
            LoadFile(Path.Combine(modPath, "EnchantmentDescriptionGlossary.json"), allStrings);

            // Applique la localisation immédiatement pour remplir CustomStrings
            string locale = "enGB";
            try {
                locale = LocalizationManager.CurrentLocale.ToString();
            } catch { }

            ApplyLocalization(locale);
        }

        private static void LoadFile(string filePath, JObject targetContainer)
        {
            try {
                if (File.Exists(filePath)) {
                    string json = File.ReadAllText(filePath);
                    var content = JObject.Parse(json);
                    var strings = content["strings"] as JObject;
                    if (strings != null) {
                        foreach (var prop in strings.Properties()) {
                            targetContainer[prop.Name] = prop.Value;
                        }
                        Main.ModEntry.Logger.Log($"Loaded {Path.GetFileName(filePath)} successfully.");
                    }
                } else {
                    Main.ModEntry.Logger.Warning($"{Path.GetFileName(filePath)} not found!");
                }
            } catch (Exception ex) {
                Main.ModEntry.Logger.Error($"Failed to load {Path.GetFileName(filePath)}: {ex.Message}");
            }
        }

        public static void ApplyLocalization(string locale)
        {
            if (RawLocalization == null) return;

            try {
                var strings = RawLocalization["strings"] as JObject;
                if (strings != null) {
                    foreach (var property in strings.Properties()) {
                        var token = property.Value;
                        string translation = token[locale]?.ToString() ?? token["enGB"]?.ToString() ?? "Translation Error";
                        CustomStrings[property.Name] = translation;
                    }
                    Main.ModEntry.Logger.Log($"Applied localization for {locale}: {CustomStrings.Count} strings.");
                }
            } catch (Exception ex) {
                Main.ModEntry.Logger.Error($"Error applying localization for {locale}: {ex.Message}");
            }
        }

        public static T GetBlueprint<T>(string guid) where T : SimpleBlueprint
        {
            var bp = ResourcesLibrary.TryGetBlueprint(BlueprintGuid.Parse(guid));
            return bp as T;
        }

        public static BlueprintAnswer CreateAnswer(string guid, string name, string textKeyParam = null)
        {
            var answer = CreateBlueprint<BlueprintAnswer>(guid, name);
            // In WOTR, text keys MUST be valid GUID formats. We use our pre-determined GUID.
            string textKey = textKeyParam ?? CraftingIntroGuid;
            string fallbackText = CustomStrings.ContainsKey(textKey) ? CustomStrings[textKey] : "Missing Translation";
            answer.Text = CreateString(textKey, fallbackText);
            
            answer.ShowOnce = false;
            answer.ShowOnceCurrentDialog = false;

            // Missing structural properties WILL cause WOTR 2.2+ UI algorithms to throw NullReferenceException!
            // We must instantiate empty arrays/containers exactly like WoljifRomanceMod does.
            answer.ShowConditions = new Kingmaker.ElementsSystem.ConditionsChecker() { Conditions = new Kingmaker.ElementsSystem.Condition[0] };
            answer.SelectConditions = new Kingmaker.ElementsSystem.ConditionsChecker() { Conditions = new Kingmaker.ElementsSystem.Condition[0] };
            
            answer.NextCue = new Kingmaker.DialogSystem.CueSelection() { Cues = new List<BlueprintCueBaseReference>() };
            answer.OnSelect = new Kingmaker.ElementsSystem.ActionList() { Actions = new Kingmaker.ElementsSystem.GameAction[0] };
            answer.AlignmentShift = new Kingmaker.UnitLogic.Alignments.AlignmentShift();
            answer.CharacterSelection = new Kingmaker.DialogSystem.CharacterSelection();
            answer.ShowCheck = new Kingmaker.DialogSystem.Blueprints.ShowCheck();

            return answer;
        }

        public static LocalizedString CreateString(string key, string text)
        {
            var localizedString = new LocalizedString();
            
            var keyField = typeof(LocalizedString).GetField("m_Key", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            if (keyField != null) keyField.SetValue(localizedString, key);
            
            var shared = typeof(LocalizedString).GetField("Shared", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            if (shared != null) shared.SetValue(localizedString, null);

            if (!CustomStrings.ContainsKey(key)) {
                CustomStrings[key] = text;
            }

            // Native injection into the current language pack
            var currentPack = LocalizationManager.CurrentPack;
            if (currentPack != null) {
                InjectStringsIntoPack(currentPack);
            }

            return localizedString;
        }

        public static void InjectStringsIntoPack(LocalizationPack pack)
        {
            try {
                // Let's use clean dynamic dispatch if possible, or standard reflection
                var putMethod = pack.GetType().GetMethod("PutString", new Type[] { typeof(string), typeof(string) });
                if (putMethod != null) {
                    foreach (var kvp in CustomStrings) {
                        putMethod.Invoke(pack, new object[] { kvp.Key, kvp.Value });
                    }
                    Main.ModEntry.Logger.Log($"Injected {CustomStrings.Count} localized strings using PutString.");
                    return;
                }

                var stringsField = pack.GetType().GetField("m_Strings", BindingFlags.NonPublic | BindingFlags.Instance);
                if (stringsField == null) return;
                var dict = stringsField.GetValue(pack);
                if (dict == null) return;
                
                var put = dict.GetType().GetMethod("set_Item");
                if (put == null) return;

                var entryType = dict.GetType().GetGenericArguments()[1];
                
                foreach (var kvp in CustomStrings) {
                    string key = kvp.Key;
                    string value = kvp.Value;
                    
                    if (entryType == typeof(string)) {
                        put.Invoke(dict, new object[] { key, value });
                    } else {
                        var entry = Activator.CreateInstance(entryType);
                        var textF = entryType.GetField("Text", BindingFlags.Public | BindingFlags.Instance);
                        if (textF != null) textF.SetValue(entry, value);
                        put.Invoke(dict, new object[] { key, entry });
                    }
                }
                Main.ModEntry.Logger.Log($"Injected {CustomStrings.Count} localized strings via m_Strings dictionary.");
            } catch (Exception ex) {
                Main.ModEntry.Logger.Error($"Failed to inject custom strings: {ex.Message}");
            }
        }

        public static BlueprintCue CreateCue(string guid, string name, string textKey, Kingmaker.DialogSystem.DialogSpeaker speaker = null, BlueprintDialog parent = null)
        {
            var cue = CreateBlueprint<BlueprintCue>(guid, name);
            string fallbackText = CustomStrings.ContainsKey(textKey) ? CustomStrings[textKey] : "Missing Translation";
            cue.Text = CreateString(textKey, fallbackText);

            cue.ShowOnce = false;
            cue.ShowOnceCurrentDialog = false;
            cue.Conditions = new Kingmaker.ElementsSystem.ConditionsChecker() { Conditions = new Kingmaker.ElementsSystem.Condition[0] };
            cue.OnShow = new Kingmaker.ElementsSystem.ActionList() { Actions = new Kingmaker.ElementsSystem.GameAction[0] };
            cue.OnStop = new Kingmaker.ElementsSystem.ActionList() { Actions = new Kingmaker.ElementsSystem.GameAction[0] };
            cue.Answers = new List<BlueprintAnswerBaseReference>();
            cue.Continue = new Kingmaker.DialogSystem.CueSelection() { Cues = new List<BlueprintCueBaseReference>() };
            
            if (speaker != null) cue.Speaker = speaker;
            cue.Continue = new Kingmaker.DialogSystem.CueSelection() { Cues = new List<BlueprintCueBaseReference>(), Strategy = Kingmaker.DialogSystem.Strategy.First };
            cue.AlignmentShift = new Kingmaker.UnitLogic.Alignments.AlignmentShift();
            
            if (parent != null) {
                var dialogRef = parent.ToReference<BlueprintDialogReference>();
                // Try both possible names for the field across different game versions
                var field = typeof(BlueprintCue).GetField("m_Dialog", BindingFlags.NonPublic | BindingFlags.Instance) 
                         ?? typeof(BlueprintCue).GetField("m_ParentDialog", BindingFlags.NonPublic | BindingFlags.Instance);
                
                if (field != null) {
                    field.SetValue(cue, dialogRef);
                } else {
                    Main.ModEntry.Logger.Warning($"[Debug_storyteller] Could not find m_Dialog field on BlueprintCue via reflection.");
                }
            }

            if (cue.Speaker == null) {
                cue.Speaker = new Kingmaker.DialogSystem.DialogSpeaker();
            }

            return cue;
        }

        public static string MergeGuid(BlueprintGuid parent, string suffix)
        {
            // Simple deterministic way to generate stable guids for child blueprints
            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                byte[] inputBytes = System.Text.Encoding.ASCII.GetBytes(parent.ToString() + suffix);
                byte[] hashBytes = md5.ComputeHash(inputBytes);
                return new Guid(hashBytes).ToString("N");
            }
        }

        public static BlueprintAnswersList CreateAnswersList(string guid, string name)
        {
            var list = CreateBlueprint<BlueprintAnswersList>(guid, name);
            list.Answers = new List<BlueprintAnswerBaseReference>();
            list.ShowOnce = false;
            list.Conditions = new Kingmaker.ElementsSystem.ConditionsChecker() { Conditions = new Kingmaker.ElementsSystem.Condition[0] };
            
            return list;
        }

        public static T CreateBlueprint<T>(string guid, string name, Action<T> init = null) where T : SimpleBlueprint, new()
        {
            var bp = new T();
            bp.name = name;

            // Initialize minimum required fields to prevent WOTR UI exceptions
            var componentsField = typeof(BlueprintScriptableObject).GetField("ComponentsArray", BindingFlags.Public | BindingFlags.Instance);
            if (componentsField != null && componentsField.GetValue(bp) == null) 
            {
                Type compType = typeof(BlueprintScriptableObject).Assembly.GetType("Kingmaker.Blueprints.BlueprintComponent");
                if (compType != null) {
                    componentsField.SetValue(bp, Array.CreateInstance(compType, 0));
                }
            }

            // Simply set AssetGuid natively so Owlcat's property setter handles all thread safe backing strings!
            bp.AssetGuid = BlueprintGuid.Parse(guid);
            
            var cache = ResourcesLibrary.BlueprintsCache;
            var addMethod = cache.GetType().GetMethod("AddBlueprint", new Type[] { typeof(SimpleBlueprint) })
                         ?? cache.GetType().GetMethod("AddCachedBlueprint", new Type[] { typeof(SimpleBlueprint) })
                         ?? cache.GetType().GetMethod("AddCachedBlueprint", new Type[] { typeof(BlueprintGuid), typeof(SimpleBlueprint) });

            if (addMethod != null)
            {
                if (addMethod.GetParameters().Length == 1)
                    addMethod.Invoke(cache, new object[] { bp });
                else
                    addMethod.Invoke(cache, new object[] { bp.AssetGuid, bp });
                
                // --- DEBUG LOG ---
                if (bp.name.Contains("CraftingSystem")) {
                    Main.ModEntry.Logger.Log($"[Debug_storyteller] Registered Blueprint: {bp.name} (Guid: {bp.AssetGuid})");
                }
            }
            
            init?.Invoke(bp);
            return bp;
        }
    }
}
