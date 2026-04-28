using System;
using System.Collections.Generic;
using Kingmaker;
using Kingmaker.ElementsSystem;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.JsonSystem;
using Kingmaker.PubSubSystem;
using Kingmaker.DialogSystem.Blueprints;
using Kingmaker.View;
using Kingmaker.View.MapObjects;
using Kingmaker.Items;
using Kingmaker.UnitLogic; 
using Kingmaker.EntitySystem;
using Kingmaker.Blueprints.Items.Weapons;
using Kingmaker.Blueprints.Items.Armors;
using Kingmaker.Blueprints.Items.Equipment;
using Kingmaker.Blueprints.Items.Ecnchantments;
using Newtonsoft.Json; 
using UniRx;
using System.Linq;
using Kingmaker.UI.Models.Log;
using Kingmaker.Blueprints.Root;
using HarmonyLib;
using System.Reflection;
using Kingmaker.Localization;
using UnityModManagerNet;


namespace CraftingSystem
{
    // --- DEBUG ACTION ---
    /* 
    public class DebugLogAction : Kingmaker.ElementsSystem.GameAction
    {
        public string Message;
        public override string GetCaption() => $"[Debug_storyteller] {Message}";
        public override void RunAction()
        {
            Main.ModEntry.Logger.Log($"[Debug_storyteller] Action Executed: {Message}");
        }
    }
    */

    public static class DialogInjector
    {
        public static void RegisterDialogChanges()
        {
            try {
                int patchedCount = 0;

                string[] targets = new string[] {
                    "f77aadad8ee7d7446973d2e03d6d730b", // Wilcer Dialog Drezen
                    "f77aadadda8b0914da63991873138b17", // Wilcer Dialog Camp
                    "34c40f3791ad4ccda6013c8b6e1b79b0", // Storyteller DLC5 Intro AnswersList_0002
                    "52a8543ea42796145ac8351f841df345", // Storyteller Main Hub AnswersList_0038
                    "2f5b7e0b76d3c5a42a431e1e33a8db09", // Storyteller Act 4 AnswersList_0004
                    "c79011a0c4436584281358317d692881", 
                    "274f85854894392478d108d090b85777", 
                    "0387531777b759648873087090b85777", 
                    "168694851253a654992569614c22cd95"
                };

                foreach (var target in targets) {
                    try {
                        SimpleBlueprint bp = ResourcesLibrary.TryGetBlueprint(BlueprintGuid.Parse(target));
                        if (bp == null) continue;

                        if (bp is BlueprintAnswersList list) {
                            if (PatchAnswersList(list)) patchedCount++;
                        } 
                        else if (bp is BlueprintDialog dialog) {
                            if (ScanDialogUsingReflection(dialog)) patchedCount++;
                        }
                    } catch (Exception ex) {
                        Main.ModEntry.Logger.Warning($"Error checking target {target}: {ex.Message}");
                    }
                }
            } catch (Exception e) {
                Main.ModEntry.Logger.Error($"Exception in RegisterDialogChanges: {e}");
            }
        }

        private static bool ScanDialogUsingReflection(BlueprintDialog dialog)
        {
            bool patchedAtLeastOne = false;
            try {
                var firstCue = dialog.FirstCue;
                if (firstCue == null) return false;

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
                    
                    var cueBp = ResourcesLibrary.TryGetBlueprint(BlueprintGuid.Parse(guidObj.ToString()));
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
                            
                            var ansBp = ResourcesLibrary.TryGetBlueprint(BlueprintGuid.Parse(ansGuidProp.GetValue(ansRef).ToString()));
                            if (ansBp is BlueprintAnswersList list) {
                                if (PatchAnswersList(list, dialog)) patchedAtLeastOne = true;
                            }
                        }
                    }
                }
            } catch { }
            return patchedAtLeastOne;
        }

        private static bool PatchAnswersList(BlueprintAnswersList list, BlueprintDialog parentDialog = null)
        {
            if (list == null || list.Answers == null) return false;
            foreach (var ansRef in list.Answers) {
                var ans = ansRef.Get();
                if (ans != null && ans.name != null && ans.name.Contains("CraftingSystem_RootAnswer")) return false;
            }

            try {
                Kingmaker.DialogSystem.DialogSpeaker speaker = null;
                bool isStoryteller = list.AssetGuid.ToString() == "34c40f3791ad4ccda6013c8b6e1b79b0" || 
                                    list.AssetGuid.ToString() == "52a8543ea42796145ac8351f841df345" ||
                                    list.AssetGuid.ToString() == "2f5b7e0b76d3c5a42a431e1e33a8db09";

                if (parentDialog != null && parentDialog.FirstCue != null && parentDialog.FirstCue.Cues.Count > 0) {
                    var firstCue = parentDialog.FirstCue.Cues[0].Get() as BlueprintCue;
                    speaker = firstCue?.Speaker;
                }

                // If speaker is still null, we'll let it be. The game usually defaults to the current interlocutor.
                string rootKey = "fa3e1f7d4e3347bdaeb88a1b6c8baab6";
                string introKey = "fa3e1f7d4e3347bdaeb88a1b6c8baab7";
                string giveKey = "fa3e1f7d4e3347bdaeb88a1b6c8baab8";
                string modifyKey = "fa3e1f7d4e3347bdaeb88a1b6c8baab9";
                
                // Use the storyteller keys if we are in one of the storyteller lists
                if (isStoryteller)
                {
                    rootKey = "storyteller_wilcer_contact_key";
                    introKey = "storyteller_wilcer_reply_key";
                    giveKey = "storyteller_wilcer_workshop_key";
                    modifyKey = "storyteller_wilcer_enchant_key";
                }

                string rAG = Helpers.MergeGuid(list.AssetGuid, "root_answer");
                var rA = Helpers.CreateAnswer(rAG, $"CraftingSystem_RootAnswer_{list.name}", rootKey);
                
                string iCG = Helpers.MergeGuid(list.AssetGuid, "intro_cue");
                var iC = Helpers.CreateCue(iCG, $"CraftingSystem_IntroCue_{list.name}", introKey, speaker, parentDialog);
                
                string sLG = Helpers.MergeGuid(list.AssetGuid, "sub_list");
                var sL = Helpers.CreateAnswersList(sLG, $"CraftingSystem_SubList_{list.name}");
                
                var aG = Helpers.CreateAnswer(Helpers.MergeGuid(list.AssetGuid, "ans_give"), "CraftingSystem_AnsGive", giveKey);
                var aM = Helpers.CreateAnswer(Helpers.MergeGuid(list.AssetGuid, "ans_modify"), "CraftingSystem_AnsModify", modifyKey);
                var aC = Helpers.CreateAnswer(Helpers.MergeGuid(list.AssetGuid, "ans_cancel"), "CraftingSystem_AnsCancel", "fa3e1f7d4e3347bdaeba8a1b6c8baab1");
                
                // --- DEBUG LOGS FOR ANSWERS ---
                /*
                var debugRoot = (DebugLogAction)Kingmaker.ElementsSystem.Element.CreateInstance(typeof(DebugLogAction));
                debugRoot.Message = $"Selected: {rootKey} (Root)";
                rA.OnSelect.Actions = new Kingmaker.ElementsSystem.GameAction[] { debugRoot };

                var debugGive = (DebugLogAction)Kingmaker.ElementsSystem.Element.CreateInstance(typeof(DebugLogAction));
                debugGive.Message = $"Selected: {giveKey} (Give Item)";
                
                var debugModify = (DebugLogAction)Kingmaker.ElementsSystem.Element.CreateInstance(typeof(DebugLogAction));
                debugModify.Message = $"Selected: {modifyKey} (Modify Item)";
                */

                
                // bool isStoryteller already defined above

                var actG = (Kingmaker.ElementsSystem.GameAction)Kingmaker.ElementsSystem.Element.CreateInstance(typeof(OpenItemSelectorAction));
                var actM = (Kingmaker.ElementsSystem.GameAction)Kingmaker.ElementsSystem.Element.CreateInstance(typeof(OpenStoredItemSelectorAction));

                aG.OnSelect.Actions = new Kingmaker.ElementsSystem.GameAction[] { actG };
                aM.OnSelect.Actions = new Kingmaker.ElementsSystem.GameAction[] { actM };
                
                var cCan = Helpers.CreateCue(Helpers.MergeGuid(list.AssetGuid, "cue_cancel"), "CraftingSystem_CueCancel", "fa3e1f7d4e3347bdaeba8a1b6c8baab3", speaker, parentDialog);
                var cSel = Helpers.CreateCue(Helpers.MergeGuid(list.AssetGuid, "cue_selection"), "CraftingSystem_CueSelection", "fa3e1f7d4e3347bdaeba8a1b6c8baab2", speaker, parentDialog);
                var cSelStored = Helpers.CreateCue(Helpers.MergeGuid(list.AssetGuid, "cue_selection_stored"), "CraftingSystem_CueSelection_Stored", "dialog_cue_stored_item", speaker, parentDialog);
                
                rA.NextCue.Cues.Add(iC.ToReference<BlueprintCueBaseReference>());
                iC.Answers.Add(sL.ToReference<BlueprintAnswerBaseReference>());
                sL.Answers.Add(aG.ToReference<BlueprintAnswerBaseReference>());
                sL.Answers.Add(aM.ToReference<BlueprintAnswerBaseReference>());
                sL.Answers.Add(aC.ToReference<BlueprintAnswerBaseReference>());
                
                if (!isStoryteller)
                {
                    aG.NextCue.Cues.Add(cSel.ToReference<BlueprintCueBaseReference>());
                    aM.NextCue.Cues.Add(cSelStored.ToReference<BlueprintCueBaseReference>());
                    aC.NextCue.Cues.Add(cCan.ToReference<BlueprintCueBaseReference>());
                    
                    if (parentDialog != null && parentDialog.FirstCue != null && parentDialog.FirstCue.Cues.Count > 0) {
                        cCan.Continue.Cues.Add(parentDialog.FirstCue.Cues[0]);
                        cCan.Continue.Strategy = Kingmaker.DialogSystem.Strategy.First;
                    }
                }
                else
                {
                    // For storyteller, we want the conversation to stop after these choices
                    // No NextCue means dialog ends.
                }

                list.Answers.Insert(Math.Max(0, list.Answers.Count - 1), rA.ToReference<BlueprintAnswerBaseReference>());
                return true;
            } catch (Exception ex) {
                Main.ModEntry.Logger.Error($"Error building dialogue: {ex}");
                return false;
            }
        }
    }

    [TypeId("6d3e1f7d4e3347bdaeb88a1b6c8baab6")]
    public class OpenItemSelectorAction : GameAction
    {
        public override string GetCaption() { return "Atelier (Dépôt)"; }
        public override void RunAction()
        {
            DeferredInventoryOpener.Initialize();
            DeferredInventoryOpener.CurrentMode = CraftingWindowMode.LootUI;
        }
    }

    [TypeId("7d3e1f7d4e3347bdaeb88a1b6c8baab7")]
    public class OpenStoredItemSelectorAction : GameAction
    {
        public override string GetCaption() { return "Atelier (Modification)"; }
        public override void RunAction()
        {
            DeferredInventoryOpener.Initialize();
            DeferredInventoryOpener.CurrentMode = CraftingWindowMode.StoredItemIMGUI;
        }
    }
}