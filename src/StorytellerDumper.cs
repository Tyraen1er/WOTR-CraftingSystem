using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Kingmaker;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.JsonSystem;
using Kingmaker.DialogSystem.Blueprints;
using Kingmaker.Designers.EventConditionActionSystem.Conditions;
using Kingmaker.ElementsSystem;
using Kingmaker.Localization;
using UnityEngine;

namespace CraftingSystem
{
    public static class StorytellerDumper
    {
        public static void Initialize()
        {
        }

        public static IEnumerable<T> GetAllBlueprints<T>() where T : SimpleBlueprint
        {
            var cache = ResourcesLibrary.BlueprintsCache;
            var field = typeof(BlueprintsCache).GetField("m_LoadedBlueprints", BindingFlags.NonPublic | BindingFlags.Instance);
            if (field == null) yield break;

            var dict = field.GetValue(cache) as System.Collections.IDictionary;
            if (dict == null) yield break;

            var guids = new List<BlueprintGuid>();
            foreach (var key in dict.Keys)
            {
                if (key is BlueprintGuid guid) guids.Add(guid);
            }

            foreach (var guid in guids)
            {
                var bp = ResourcesLibrary.TryGetBlueprint(guid);
                if (bp is T t) yield return t;
            }
        }

        public static void DumpStorytellerDialogues()
        {
            Main.log.Log("Starting Targeted Storyteller Dialogue Scan...");
            
            string modPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string outputPath = Path.Combine(modPath, "Storyteller_Verification_Dump.txt");

            try 
            {
                using (StreamWriter writer = new StreamWriter(outputPath))
                {
                    writer.WriteLine("=== STORYTELLER GUID VERIFICATION DUMP ===");
                    writer.WriteLine($"Generated on: {DateTime.Now}");
                    writer.WriteLine("===========================================");
                    writer.WriteLine();

                    // 1. Scan for the specific greeting mentioned by the user
                    writer.WriteLine("--- SEARCHING FOR GREETING TEXT ---");
                    var allCues = GetAllBlueprints<BlueprintCue>();
                    string targetFragment = "M'apportez-vous une nouvelle histoire";
                    string targetFragmentEn = "Do you bring me a new story";

                    foreach (var cue in allCues)
                    {
                        string text = "";
                        try { text = cue.DisplayText; } catch { }

                        if (text.IndexOf(targetFragment, StringComparison.OrdinalIgnoreCase) >= 0 || 
                            text.IndexOf(targetFragmentEn, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            writer.WriteLine($"FOUND MATCHING CUE:");
                            writer.WriteLine($"[CUE] GUID: {cue.AssetGuid}");
                            writer.WriteLine($"Name: {cue.name}");
                            writer.WriteLine($"Text: {text}");
                            if (cue.Answers != null && cue.Answers.Count > 0)
                            {
                                writer.WriteLine("Linked Answers:");
                                foreach (var ans in cue.Answers)
                                {
                                    var bp = ans.Get();
                                    writer.WriteLine($"  - [LIST] {bp?.name} (GUID: {bp?.AssetGuid})");
                                    if (bp is BlueprintAnswersList list)
                                    {
                                        foreach (var subAns in list.Answers)
                                        {
                                            var sa = subAns.Get();
                                            if (sa is BlueprintAnswer subAnswer)
                                            {
                                                string saText = ""; try { saText = subAnswer.DisplayText; } catch { }
                                                writer.WriteLine($"    - [ANSWER] {subAnswer.name} (GUID: {subAnswer.AssetGuid}): {saText}");
                                            }
                                        }
                                    }
                                }
                            }
                            writer.WriteLine("----------------------------------");
                        }
                    }

                    // 2. Specifically dump the suspected Hub GUID
                    writer.WriteLine();
                    writer.WriteLine("--- VERIFYING SUSPECTED HUB GUID (d2868ef3094c40f429661445749f7062) ---");
                    var suspectedHub = ResourcesLibrary.TryGetBlueprint(BlueprintGuid.Parse("d2868ef3094c40f429661445749f7062")) as BlueprintAnswersList;
                    if (suspectedHub != null)
                    {
                        writer.WriteLine($"Suspected Hub FOUND: {suspectedHub.name}");
                        foreach (var ansRef in suspectedHub.Answers)
                        {
                            var ans = ansRef.Get();
                            if (ans is BlueprintAnswer answer)
                            {
                                string ansText = ""; try { ansText = answer.DisplayText; } catch { }
                                writer.WriteLine($"- [ANSWER] {answer.name} (GUID: {answer.AssetGuid}): {ansText}");
                            }
                        }
                    }
                    else
                    {
                        writer.WriteLine("Suspected Hub GUID NOT FOUND in ResourcesLibrary.");
                    }

                    // 3. Original dump logic (filtered by name)
                    writer.WriteLine();
                    writer.WriteLine("--- FULL STORYTELLER NAME-BASED DUMP ---");
                    int count = 0;
                    foreach (var cue in allCues)
                    {
                        bool isStoryteller = false;
                        if (cue.Speaker != null && cue.Speaker.Blueprint != null)
                        {
                            if (cue.Speaker.Blueprint.name.IndexOf("Storyteller", StringComparison.OrdinalIgnoreCase) >= 0)
                                isStoryteller = true;
                        }
                        else if (cue.name.IndexOf("Storyteller", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            isStoryteller = true;
                        }

                        if (isStoryteller)
                        {
                            string text = "[[ERROR: Could not retrieve text]]";
                            try { text = cue.DisplayText; } catch { }

                            writer.WriteLine($"[CUE] GUID: {cue.AssetGuid}");
                            writer.WriteLine($"Name: {cue.name}");
                            writer.WriteLine($"Text: {text}");
                            if (cue.Answers != null && cue.Answers.Count > 0)
                            {
                                writer.WriteLine("Player Choices (Answers):");
                                foreach (var answerRef in cue.Answers)
                                {
                                    DumpAnswerBase(answerRef.Get(), writer, "  ");
                                }
                            }
                            writer.WriteLine("----------------------------------");
                            count++;
                        }
                    }

                    writer.WriteLine();
                    writer.WriteLine($"Scan finished. Found {count} lines for the Storyteller.");
                }
                Main.log.Log($"Storyteller verification dump completed. File saved at: {outputPath}");
            }
            catch (Exception ex)
            {
                Main.log.Error($"Failed to write verification dump: {ex}");
            }
        }

        public static void PerformGlobalAnswerSearch()
        {
            Main.log.Log("Starting Global Answer Search...");
            
            string modPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string outputPath = Path.Combine(modPath, "Storyteller_Search_Results.txt");

            try 
            {
                using (StreamWriter writer = new StreamWriter(outputPath))
                {
                    writer.WriteLine("=== GLOBAL DIALOGUE SEARCH ===");
                    writer.WriteLine($"Target: 'plein de fournitures'");
                    writer.WriteLine("==============================");
                    writer.WriteLine();

                    var allAnswers = GetAllBlueprints<BlueprintAnswer>();
                    string target = "plein de fournitures";
                    
                    int foundCount = 0;
                    foreach (var ans in allAnswers)
                    {
                        string text = "";
                        try { text = ans.DisplayText; } catch { }

                        if (text.IndexOf(target, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            writer.WriteLine($"FOUND MATCHING ANSWER:");
                            writer.WriteLine($"[ANSWER] GUID: {ans.AssetGuid}");
                            writer.WriteLine($"Name: {ans.name}");
                            writer.WriteLine($"Text: {text}");
                            
                            writer.WriteLine("Conditions (ShowConditions):");
                            DumpConditions(ans.ShowConditions, writer, "  ");
                            
                            // Find which lists contain this answer
                            var allLists = GetAllBlueprints<BlueprintAnswersList>();
                            foreach (var list in allLists)
                            {
                                if (list.Answers.Any(a => a.Guid == ans.AssetGuid))
                                {
                                    writer.WriteLine($"  Found in LIST: {list.name} (GUID: {list.AssetGuid})");
                                }
                            }
                            
                            writer.WriteLine("----------------------------------");
                            foundCount++;
                        }
                    }

                    writer.WriteLine();
                    writer.WriteLine($"Search finished. Found {foundCount} matches.");
                }
                Main.log.Log($"Search completed. File saved at: {outputPath}");
            }
            catch (Exception ex)
            {
                Main.log.Error($"Failed to perform search: {ex}");
            }
        }

        private static void DumpAnswerBase(BlueprintAnswerBase answerBase, StreamWriter writer, string indent)
        {
            if (answerBase == null) return;

            if (answerBase is BlueprintAnswer answer)
            {
                string ansText = "[[ERROR]]";
                try { ansText = answer.DisplayText; }
                catch { ansText = "[RAW] " + (LocalizationManager.CurrentPack?.GetText(answer.Text?.Key) ?? "Key: " + answer.Text?.Key); }
                
                writer.WriteLine($"{indent}- [ANSWER] {answer.name} (GUID: {answer.AssetGuid}): {ansText}");
                if (answer.ShowOnce)
                {
                    writer.WriteLine($"{indent}  Show Once: True (Global: {!answer.ShowOnceCurrentDialog})");
                }
                if (answer.MythicRequirement != Kingmaker.DialogSystem.Blueprints.Mythic.None)
                {
                    writer.WriteLine($"{indent}  Mythic Requirement: {answer.MythicRequirement}");
                }
                if (answer.AlignmentRequirement != Kingmaker.Enums.AlignmentComponent.None)
                {
                    writer.WriteLine($"{indent}  Alignment Requirement: {answer.AlignmentRequirement}");
                }
                if (answer.ShowCheck.Type != Kingmaker.EntitySystem.Stats.StatType.Unknown)
                {
                    writer.WriteLine($"{indent}  Show Check: {answer.ShowCheck.Type} (DC: {answer.ShowCheck.DC})");
                }
                if (answer.RequireValidCue)
                {
                    writer.WriteLine($"{indent}  Require Valid Next Cue: True");
                }
                if (answer.NextCue != null && answer.NextCue.Cues.Count > 0)
                {
                    writer.WriteLine($"{indent}  Next Cue(s): {string.Join(", ", answer.NextCue.Cues.Select(c => c.Get()?.name ?? "NULL"))}");
                }
                if (answer.Components != null && answer.Components.Length > 0)
                {
                    writer.WriteLine($"{indent}  Components:");
                    foreach (var comp in answer.Components)
                    {
                        writer.WriteLine($"{indent}    - {comp.GetType().Name}: {comp}");
                    }
                }
                if (answer.OnSelect != null && answer.OnSelect.Actions.Length > 0)
                {
                    writer.WriteLine($"{indent}  Actions on Select:");
                    foreach (var action in answer.OnSelect.Actions)
                    {
                        writer.WriteLine($"{indent}    - {action.GetType().Name}: {action}");
                    }
                }
                if (answer.ShowConditions != null && answer.ShowConditions.HasConditions)
                {
                    writer.WriteLine($"{indent}  Show Conditions:");
                    DumpConditions(answer.ShowConditions, writer, indent + "    ");
                }
                if (answer.SelectConditions != null && answer.SelectConditions.HasConditions)
                {
                    writer.WriteLine($"{indent}  Select Conditions:");
                    DumpConditions(answer.SelectConditions, writer, indent + "    ");
                }
            }
            else if (answerBase is BlueprintAnswersList list)
            {
                writer.WriteLine($"{indent}- [LIST] {list.name} (GUID: {list.AssetGuid}):");
                if (list.Conditions != null && list.Conditions.HasConditions)
                {
                    writer.WriteLine($"{indent}  List Conditions:");
                    DumpConditions(list.Conditions, writer, indent + "    ");
                }
                foreach (var innerRef in list.Answers)
                {
                    DumpAnswerBase(innerRef.Get(), writer, indent + "  ");
                }
            }
        }

        private static void DumpConditions(ConditionsChecker checker, StreamWriter writer, string indent)
        {
            if (checker == null || checker.Conditions == null || checker.Conditions.Length == 0)
            {
                writer.WriteLine($"{indent}None");
                return;
            }

            foreach (var cond in checker.Conditions)
            {
                if (cond == null) continue;
                
                string notPrefix = cond.Not ? "NOT " : "";
                string condDesc = cond.ToString();
                
                if (cond is FlagUnlocked flagCond)
                {
                    condDesc = $"Flag '{flagCond.ConditionFlag?.name}' must be unlocked";
                    if (flagCond.SpecifiedValues.Count > 0)
                    {
                        condDesc += $" with value in [{string.Join(", ", flagCond.SpecifiedValues)}]";
                    }
                }
                else if (cond is CueSeen seenCond)
                {
                    condDesc = $"Cue '{seenCond.Cue?.name}' has been seen";
                }
                else if (cond is EtudeStatus etudeCond)
                {
                    var statusList = new List<string>();
                    if (etudeCond.NotStarted) statusList.Add("NotStarted");
                    if (etudeCond.Started) statusList.Add("Started");
                    if (etudeCond.Playing) statusList.Add("Playing");
                    if (etudeCond.CompletionInProgress) statusList.Add("CompletionInProgress");
                    if (etudeCond.Completed) statusList.Add("Completed");
                    
                    condDesc = $"Etude '{etudeCond.Etude?.name}' status in [{string.Join(", ", statusList)}]";
                }

                writer.WriteLine($"{indent}- {notPrefix}{cond.GetType().Name}: {condDesc}");
            }
        }
    }
}
