using System;
using System.Reflection;
using System.Collections.Generic;
using HarmonyLib;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.JsonSystem;
using Kingmaker.DialogSystem.Blueprints;
using UnityModManagerNet;

namespace CraftingSystem
{
    static class Main 
    {
        public static UnityModManager.ModEntry ModEntry;
        public static Harmony HarmonyInstance;

        static bool Load(UnityModManager.ModEntry modEntry)
        {
            try {
                ModEntry = modEntry;
                HarmonyInstance = new Harmony(modEntry.Info.Id);
                HarmonyInstance.PatchAll(Assembly.GetExecutingAssembly());
                
                ModEntry.Logger.Log("Crafting System: Mod loaded and Harmony patched.");
                
                // In case of reload
                ModMain.RegisterDialogChanges();
                
                return true;
            } catch (Exception e) {
                modEntry.Logger.Error($"Crafting System: Failed to load: {e}");
                return false;
            }
        }
    }

    [HarmonyPatch(typeof(BlueprintsCache), "Init")]
    public static class BlueprintsCache_Init_Patch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            try {
                ModMain.RegisterDialogChanges();
            } catch (Exception ex) {
                Main.ModEntry.Logger.Error($"Error in BlueprintsCache.Init: {ex}");
            }
        }
    }

    public static class ModMain
    {
        // We no longer use a single static Answer_Crafting_Guid because we need unique instances per list!
        // Otherwise, patching multiple lists causes them to overwrite the single answer's NextCue!

        public static void RegisterDialogChanges()
        {
            try {
                int patchedCount = 0;
                string optionText = "[TEST] Je voudrais fabriquer quelque chose.";

                // Priority targets (GUIDs)
                string[] targets = new string[] {
                    "f77aadad8ee7d7446973d2e03d6d730b", // Wilcer Dialog Drezen (User provided)
                    "f77aadadda8b0914da63991873138b17", // Wilcer Dialog Camp
                    "c79011a0c4436584281358317d692881", // AnswersList_0002
                    "274f85854894392478d108d090b85777", // AnswersList_0019
                    "0387531777b759648873087090b85777", // AnswersList_0009
                    "168694851253a654992569614c22cd95"
                };

                foreach (var target in targets) {
                    try {
                        SimpleBlueprint bp = ResourcesLibrary.TryGetBlueprint(BlueprintGuid.Parse(target));
                        
                        if (bp == null) {
                            Main.ModEntry.Logger.Log($"Target {target} not found in game data.");
                            continue;
                        }

                        Main.ModEntry.Logger.Log($"Found target {target} as {bp.GetType().Name} ({bp.name})");

                        if (bp is BlueprintAnswersList list) {
                            if (PatchAnswersList(list, optionText)) patchedCount++;
                        } 
                        else if (bp is BlueprintDialog dialog) {
                            Main.ModEntry.Logger.Log($"Scanning BlueprintDialog: {bp.name}");
                            if (ScanDialogUsingReflection(dialog, optionText)) patchedCount++;
                        }
                    } catch (Exception ex) {
                        Main.ModEntry.Logger.Warning($"Error checking target {target}: {ex.Message}");
                    }
                }

                if (patchedCount > 0)
                    Main.ModEntry.Logger.Log($"Success: Added crafting dialogue option to {patchedCount} locations.");
                else
                    Main.ModEntry.Logger.Warning("Could not find any suitable dialogue list for Wilcer Garms automatically. Trying common names...");

            } catch (Exception e) {
                Main.ModEntry.Logger.Error($"Exception in RegisterDialogChanges: {e}");
            }
        }

        private static bool ScanDialogUsingReflection(BlueprintDialog dialog, string optionText)
        {
            bool patchedAtLeastOne = false;
            try {
                var firstCue = dialog.FirstCue;
                if (firstCue == null) {
                    Main.ModEntry.Logger.Log("FirstCue is null.");
                    return false;
                }

                var cuesField = firstCue.GetType().GetField("Cues", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) 
                             ?? firstCue.GetType().GetField("m_Cues", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (cuesField == null) return false;

                var cuesList = cuesField.GetValue(firstCue) as System.Collections.IEnumerable;
                if (cuesList == null) return false;

                foreach (var cueRef in cuesList) {
                    var guidProp = cueRef.GetType().GetProperty("Guid", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) 
                                ?? cueRef.GetType().GetProperty("deserializedGuid", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    
                    if (guidProp == null) continue;
                    
                    var guidObj = guidProp.GetValue(cueRef);
                    if (guidObj == null) continue;
                    
                    string cueGuidStr = guidObj.ToString();
                    var cueBp = ResourcesLibrary.TryGetBlueprint(BlueprintGuid.Parse(cueGuidStr));
                    
                    if (cueBp is BlueprintCue cue) {
                        var answersField = cue.GetType().GetField("Answers", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) 
                                        ?? cue.GetType().GetField("m_Answers", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (answersField == null) continue;

                        var answersList = answersField.GetValue(cue) as System.Collections.IEnumerable;
                        if (answersList == null) continue;

                        foreach (var ansRef in answersList) {
                            var ansGuidProp = ansRef.GetType().GetProperty("Guid", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                                           ?? ansRef.GetType().GetProperty("deserializedGuid", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                            if (ansGuidProp == null) continue;
                            
                            var ansGuidObj = ansGuidProp.GetValue(ansRef);
                            if (ansGuidObj == null) continue;

                            string expectedListGuid = ansGuidObj.ToString();
                            var ansBp = ResourcesLibrary.TryGetBlueprint(BlueprintGuid.Parse(expectedListGuid));
                            
                            if (ansBp is BlueprintAnswersList list) {
                                Main.ModEntry.Logger.Log($"Discovered AnswersList from dialog cue: {list.name} ({expectedListGuid})");
                                if (PatchAnswersList(list, optionText)) patchedAtLeastOne = true;
                            }
                        }
                    }
                }
            } catch (Exception ex) {
                Main.ModEntry.Logger.Error($"Error scanning dialog tree: {ex}");
            }
            return patchedAtLeastOne;
        }

        private static bool PatchDialog(BlueprintDialog dialog, string optionText)
        {
            return ScanDialogUsingReflection(dialog, optionText);
        }

        private static bool PatchAnswersList(BlueprintAnswersList list, string optionText)
        {
            if (list == null || list.Answers == null || list.Answers.Count == 0) return false;
            
            if (HasAnswer(list, "CraftingSystem_Answer")) {
                Main.ModEntry.Logger.Log($"List {list.name} already has crafting answer. Skipping duplicate insertion.");
                return false;
            }

            // Generate a unique GUID for the answer specific to this list so we don't corrupt state!
            string answerGuid = Guid.NewGuid().ToString("N");
            var answer = Helpers.CreateAnswer(answerGuid, $"CraftingSystem_Answer_{list.name}", optionText);

            Main.ModEntry.Logger.Log($"[DIAGNOSTIC] Created answer {answer.name} -> AssetGuid: '{answer.AssetGuid}', ThreadSafe: '{answer.AssetGuidThreadSafe}'");

            try {
                // Clone the exit strategy of the existing last answer (which is usually the "Leave" button)
                var exitRef = list.Answers[list.Answers.Count - 1];
                if (exitRef != null) {
                    var guidProp = exitRef.GetType().GetProperty("Guid", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic) 
                                ?? exitRef.GetType().GetProperty("deserializedGuid", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                    
                    if (guidProp != null) {
                        var guidVal = guidProp.GetValue(exitRef);
                        if (guidVal != null) {
                            var exitBp = ResourcesLibrary.TryGetBlueprint(BlueprintGuid.Parse(guidVal.ToString()));
                            if (exitBp is BlueprintAnswer exitAnswer) {
                                // Copy NextCue
                                var nextCueInfo = exitAnswer.GetType().GetField("NextCue", BindingFlags.Public | BindingFlags.Instance);
                                if (nextCueInfo != null) {
                                    nextCueInfo.SetValue(answer, nextCueInfo.GetValue(exitAnswer));
                                }

                                // Copy OnSelect (CRITICAL for closing the dialogue box!)
                                var onSelectInfo = exitAnswer.GetType().GetField("OnSelect", BindingFlags.Public | BindingFlags.Instance);
                                if (onSelectInfo != null) {
                                    onSelectInfo.SetValue(answer, onSelectInfo.GetValue(exitAnswer));
                                }

                                Main.ModEntry.Logger.Log($"Successfully cloned exit NextCue & OnSelect properties for {list.name}.");
                            }
                        }
                    }
                }
                
                // Insert before the last answer (e.g. above "Leave")
                list.Answers.Insert(Math.Max(0, list.Answers.Count - 1), answer.ToReference<BlueprintAnswerBaseReference>());
                Main.ModEntry.Logger.Log($"Successfully patched dialogue list: {list.name} ({list.AssetGuid})");
                return true;
            } catch (Exception ex) {
                Main.ModEntry.Logger.Error($"Failed to insert answer: {ex}");
            }
            return false;
        }

        private static bool HasAnswer(BlueprintAnswersList list, string partialName)
        {
            if (list.Answers == null) return false;
            foreach (var ansRef in list.Answers) {
                try {
                    var bp = ansRef?.Get();
                    if (bp != null && bp.name != null && bp.name.Contains(partialName)) {
                        return true;
                    }
                } catch { } // Ignore resolution errors for broken other mods
            }
            return false;
        }


        private static bool HasAnswer(BlueprintAnswersList list, BlueprintAnswer answer)
        {
            if (list.Answers == null) return false;
            foreach (var a in list.Answers) {
                if (a != null && a.Guid == answer.AssetGuid) return true;
            }
            return false;
        }
    }
}

