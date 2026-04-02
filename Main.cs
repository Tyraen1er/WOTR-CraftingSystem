using System;
using System.Reflection;
using System.Collections.Generic;
using HarmonyLib;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.JsonSystem;
using Kingmaker.DialogSystem.Blueprints;
using Kingmaker.Localization;
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
                
                Helpers.LoadLocalization(modEntry.Path);

                ModEntry.Logger.Log("Crafting System: Mod loaded and Harmony patched.");
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
                string locale = "enGB";
                try {
                    locale = Kingmaker.Localization.LocalizationManager.CurrentLocale.ToString();
                } catch {
                    Main.ModEntry.Logger.Log("LocalizationManager not fully initialized yet.");
                }

                Helpers.ApplyLocalization(locale);
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
                            if (PatchAnswersList(list)) patchedCount++;
                        } 
                        else if (bp is BlueprintDialog dialog) {
                            Main.ModEntry.Logger.Log($"Scanning BlueprintDialog: {bp.name}");
                            if (ScanDialogUsingReflection(dialog)) patchedCount++;
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

        private static bool ScanDialogUsingReflection(BlueprintDialog dialog)
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
                                if (PatchAnswersList(list, dialog)) patchedAtLeastOne = true;
                            }
                        }
                    }
                }
            } catch (Exception ex) {
                Main.ModEntry.Logger.Error($"Error scanning dialog tree: {ex}");
            }
            return patchedAtLeastOne;
        }

        private static bool PatchDialog(BlueprintDialog dialog)
        {
            return ScanDialogUsingReflection(dialog);
        }

        private static bool PatchAnswersList(BlueprintAnswersList list, BlueprintDialog parentDialog = null)
        {
            if (list == null || list.Answers == null) return false;
            
            if (HasAnswer(list, "CraftingSystem_RootAnswer")) {
                return false;
            }

            try {
                // 1. Determine Speaker (copy from the first cue of the parent dialog)
                Kingmaker.DialogSystem.DialogSpeaker speaker = null;
                if (parentDialog != null && parentDialog.FirstCue != null && parentDialog.FirstCue.Cues.Count > 0) {
                    var firstCueRef = parentDialog.FirstCue.Cues[0];
                    var firstCue = firstCueRef?.Get() as BlueprintCue;
                    speaker = firstCue?.Speaker;
                }

                if (speaker != null) {
                    Main.ModEntry.Logger.Log($"Extracted Speaker for {list.name}");
                }

                // 2. Root Answer (Initial option) - We use a stable GUID derived from the list's own GUID
                string rootAnswerGuid = Helpers.MergeGuid(list.AssetGuid, "root_answer");
                var rootAnswer = Helpers.CreateAnswer(rootAnswerGuid, $"CraftingSystem_RootAnswer_{list.name}", "dialogue_crafting_intro");
                
                // 3. Intro Cue (Wilcer's "Je connais de bons artisans...")
                string introCueGuid = Helpers.MergeGuid(list.AssetGuid, "intro_cue");
                var introCue = Helpers.CreateCue(introCueGuid, $"CraftingSystem_IntroCue_{list.name}", "intro_reply", speaker);
                
                // 4. Sub-Choices List
                string subListGuid = Helpers.MergeGuid(list.AssetGuid, "sub_list");
                var subList = Helpers.CreateAnswersList(subListGuid, $"CraftingSystem_SubList_{list.name}");
                
                // 5. Sub-Answers
                var ansWeapon = Helpers.CreateAnswer(Helpers.MergeGuid(list.AssetGuid, "ans_weapon"), "CraftingSystem_AnsWeapon", "choice_weapon");
                var ansArmor  = Helpers.CreateAnswer(Helpers.MergeGuid(list.AssetGuid, "ans_armor"),  "CraftingSystem_AnsArmor",  "choice_armor");
                var ansItem   = Helpers.CreateAnswer(Helpers.MergeGuid(list.AssetGuid, "ans_item"),   "CraftingSystem_AnsItem",   "choice_item");
                var ansCancel = Helpers.CreateAnswer(Helpers.MergeGuid(list.AssetGuid, "ans_cancel"), "CraftingSystem_AnsCancel", "choice_cancel");
                
                // 6. Action: Open the UI window!
                var uiAction = (OpenItemSelectorAction)Kingmaker.ElementsSystem.Element.CreateInstance(typeof(OpenItemSelectorAction));
                uiAction.name = $"CraftingSystem_OpenUI_{list.name}";
                
                // Attach UI action to enchantment choices
                ansWeapon.OnSelect.Actions = new Kingmaker.ElementsSystem.GameAction[] { uiAction };
                ansArmor.OnSelect.Actions  = new Kingmaker.ElementsSystem.GameAction[] { uiAction };
                ansItem.OnSelect.Actions   = new Kingmaker.ElementsSystem.GameAction[] { uiAction };
                
                // 7. Result Cues
                var cueCancel    = Helpers.CreateCue(Helpers.MergeGuid(list.AssetGuid, "cue_cancel"), "CraftingSystem_CueCancel", "cancel_reply", speaker);
                var cueSelection = Helpers.CreateCue(Helpers.MergeGuid(list.AssetGuid, "cue_selection"), "CraftingSystem_CueSelection", "selection_reply", speaker);
                
                // --- LINKING ---
                
                // Root -> Intro Cue
                rootAnswer.NextCue.Cues.Add(introCue.ToReference<BlueprintCueBaseReference>());
                
                // Intro Cue -> Sub List
                introCue.Answers.Add(subList.ToReference<BlueprintAnswerBaseReference>());
                
                // Sub List -> Sub Answers
                subList.Answers.Add(ansWeapon.ToReference<BlueprintAnswerBaseReference>());
                subList.Answers.Add(ansArmor.ToReference<BlueprintAnswerBaseReference>());
                subList.Answers.Add(ansItem.ToReference<BlueprintAnswerBaseReference>());
                subList.Answers.Add(ansCancel.ToReference<BlueprintAnswerBaseReference>());
                
                // Sub Answers -> Result Cues
                ansWeapon.NextCue.Cues.Add(cueSelection.ToReference<BlueprintCueBaseReference>());
                ansArmor.NextCue.Cues.Add(cueSelection.ToReference<BlueprintCueBaseReference>());
                ansItem.NextCue.Cues.Add(cueSelection.ToReference<BlueprintCueBaseReference>());
                ansCancel.NextCue.Cues.Add(cueCancel.ToReference<BlueprintCueBaseReference>());
                
                // Result Cues -> Loop Back to Native Start
                if (parentDialog != null && parentDialog.FirstCue != null && parentDialog.FirstCue.Cues.Count > 0) {
                    var startCueRef = parentDialog.FirstCue.Cues[0];
                    Main.ModEntry.Logger.Log($"Setting loop-back for completion cues to: {startCueRef.Guid}");
                    
                    cueCancel.Continue.Cues.Add(startCueRef);
                    cueCancel.Continue.Strategy = Kingmaker.DialogSystem.Strategy.First;
                    
                    cueSelection.Continue.Cues.Add(startCueRef);
                    cueSelection.Continue.Strategy = Kingmaker.DialogSystem.Strategy.First;
                }

                // Inject into target list (above "Leave")
                int insertIndex = Math.Max(0, list.Answers.Count - 1);
                list.Answers.Insert(insertIndex, rootAnswer.ToReference<BlueprintAnswerBaseReference>());
                
                Main.ModEntry.Logger.Log($"Successfully injected complex crafting tree into {list.name} with UI Action attached.");
                return true;
            } catch (Exception ex) {
                Main.ModEntry.Logger.Error($"Error building dialogue tree for {list.name}: {ex}");
                return false;
            }
        }

        private static bool HasAnswer(BlueprintAnswersList list, string nameSubstring)
        {
            if (list.Answers == null) return false;
            foreach (var ansRef in list.Answers) {
                var ans = ansRef.Get();
                if (ans != null && ans.name != null && ans.name.Contains(nameSubstring)) return true;
            }
            return false;
        }
    }
}

