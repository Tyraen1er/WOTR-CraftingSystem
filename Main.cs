using System;
using System.Reflection;
using System.Collections.Generic;
using HarmonyLib;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.JsonSystem;
using Kingmaker.DialogSystem.Blueprints;
using Kingmaker.Localization;
using Kingmaker.Items;
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
                
                if (Kingmaker.Localization.LocalizationManager.CurrentPack != null) {
                    Helpers.InjectStringsIntoPack(Kingmaker.Localization.LocalizationManager.CurrentPack);
                }

                ModMain.RegisterDialogChanges();
                
                // --- CHARGEMENT DES ENCHANTEMENTS ---
                EnchantmentScanner.Load();
            } catch (Exception ex) {
                Main.ModEntry.Logger.Error($"Error in BlueprintsCache.Init: {ex}");
            }
        }
    }

    public static class ModMain
    {
        public static void RegisterDialogChanges()
        {
            try {
                int patchedCount = 0;

                string[] targets = new string[] {
                    "f77aadad8ee7d7446973d2e03d6d730b", // Wilcer Dialog Drezen
                    "f77aadadda8b0914da63991873138b17", // Wilcer Dialog Camp
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
                if (parentDialog != null && parentDialog.FirstCue != null && parentDialog.FirstCue.Cues.Count > 0) {
                    var firstCue = parentDialog.FirstCue.Cues[0].Get() as BlueprintCue;
                    speaker = firstCue?.Speaker;
                }

                string rAG = Helpers.MergeGuid(list.AssetGuid, "root_answer");
                var rA = Helpers.CreateAnswer(rAG, $"CraftingSystem_RootAnswer_{list.name}", "fa3e1f7d4e3347bdaeb88a1b6c8baab6");
                
                string iCG = Helpers.MergeGuid(list.AssetGuid, "intro_cue");
                var iC = Helpers.CreateCue(iCG, $"CraftingSystem_IntroCue_{list.name}", "fa3e1f7d4e3347bdaeb88a1b6c8baab7", speaker);
                
                string sLG = Helpers.MergeGuid(list.AssetGuid, "sub_list");
                var sL = Helpers.CreateAnswersList(sLG, $"CraftingSystem_SubList_{list.name}");
                
                var aG = Helpers.CreateAnswer(Helpers.MergeGuid(list.AssetGuid, "ans_give"), "CraftingSystem_AnsGive", "fa3e1f7d4e3347bdaeb88a1b6c8baab8");
                var aM = Helpers.CreateAnswer(Helpers.MergeGuid(list.AssetGuid, "ans_modify"), "CraftingSystem_AnsModify", "fa3e1f7d4e3347bdaeb88a1b6c8baab9");
                var aC = Helpers.CreateAnswer(Helpers.MergeGuid(list.AssetGuid, "ans_cancel"), "CraftingSystem_AnsCancel", "fa3e1f7d4e3347bdaeba8a1b6c8baab1");
                
                // [LEGACY_COMPATIBILITY]
                Helpers.CreateAnswer(Helpers.MergeGuid(list.AssetGuid, "ans_weapon"), "CraftingSystem_Legacy_Weapon", "fa3e1f7d4e3347bdaeb88a1b6c8baab8");
                Helpers.CreateAnswer(Helpers.MergeGuid(list.AssetGuid, "ans_armor"),  "CraftingSystem_Legacy_Armor",  "fa3e1f7d4e3347bdaeb88a1b6c8baab8");
                Helpers.CreateAnswer(Helpers.MergeGuid(list.AssetGuid, "ans_item"),   "CraftingSystem_Legacy_Item",   "fa3e1f7d4e3347bdaeb88a1b6c8baab8");
                Helpers.CreateAnswer("bdfc738cca072e29cf4bbcaf21d14546", "CraftingSystem_Ghost_Answer", "fa3e1f7d4e3347bdaeb88a1b6c8baab6");
                
                var actD = (OpenItemSelectorAction)Kingmaker.ElementsSystem.Element.CreateInstance(typeof(OpenItemSelectorAction));
                var actM = (OpenStoredItemSelectorAction)Kingmaker.ElementsSystem.Element.CreateInstance(typeof(OpenStoredItemSelectorAction));
                
                aG.OnSelect.Actions = new Kingmaker.ElementsSystem.GameAction[] { actD };
                aM.OnSelect.Actions = new Kingmaker.ElementsSystem.GameAction[] { actM };
                
                var cCan = Helpers.CreateCue(Helpers.MergeGuid(list.AssetGuid, "cue_cancel"), "CraftingSystem_CueCancel", "fa3e1f7d4e3347bdaeba8a1b6c8baab3", speaker);
                var cSel = Helpers.CreateCue(Helpers.MergeGuid(list.AssetGuid, "cue_selection"), "CraftingSystem_CueSelection", "fa3e1f7d4e3347bdaeba8a1b6c8baab2", speaker);
                
                rA.NextCue.Cues.Add(iC.ToReference<BlueprintCueBaseReference>());
                iC.Answers.Add(sL.ToReference<BlueprintAnswerBaseReference>());
                sL.Answers.Add(aG.ToReference<BlueprintAnswerBaseReference>());
                sL.Answers.Add(aM.ToReference<BlueprintAnswerBaseReference>());
                sL.Answers.Add(aC.ToReference<BlueprintAnswerBaseReference>());
                
                aG.NextCue.Cues.Add(cSel.ToReference<BlueprintCueBaseReference>());
                aM.NextCue.Cues.Add(cSel.ToReference<BlueprintCueBaseReference>());
                aC.NextCue.Cues.Add(cCan.ToReference<BlueprintCueBaseReference>());
                
                if (parentDialog != null && parentDialog.FirstCue != null && parentDialog.FirstCue.Cues.Count > 0) {
                    cCan.Continue.Cues.Add(parentDialog.FirstCue.Cues[0]);
                    cCan.Continue.Strategy = Kingmaker.DialogSystem.Strategy.First;
                }

                list.Answers.Insert(Math.Max(0, list.Answers.Count - 1), rA.ToReference<BlueprintAnswerBaseReference>());
                return true;
            } catch (Exception ex) {
                Main.ModEntry.Logger.Error($"Error building dialogue: {ex}");
                return false;
            }
        }
    }

    [HarmonyPatch(typeof(Kingmaker.UI.MVVM._VM.Loot.LootVM), MethodType.Constructor, new Type[] { typeof(Kingmaker.UI.MVVM._VM.Loot.LootContextVM.LootWindowMode), typeof(Kingmaker.View.EntityViewBase[]), typeof(Action) })]
    public static class LootVM_Crafting_Patch
    {
        public static void Postfix(Kingmaker.UI.MVVM._VM.Loot.LootVM __instance)
        {
            if (DeferredInventoryOpener.IsCraftingWindowOpen)
            {
                var mockLoot = new Kingmaker.UI.MVVM._VM.Loot.LootObjectVM("Atelier", "Enchantement", DeferredInventoryOpener.CraftingBox, Kingmaker.UI.MVVM._VM.Loot.LootContextVM.LootWindowMode.PlayerChest, 1);
                var prop = typeof(Kingmaker.UI.MVVM._VM.Loot.LootVM).GetProperty("LootObjects") ?? typeof(Kingmaker.UI.MVVM._VM.Loot.LootVM).GetProperty("ContextLoot");
                if (prop != null)
                {
                    var coll = prop.GetValue(__instance);
                    if (coll != null) {
                        coll.GetType().GetMethod("Clear")?.Invoke(coll, null);
                        coll.GetType().GetMethod("Add")?.Invoke(coll, new object[] { mockLoot });
                    }
                }
            }
        }
    }

    // --- BLOCAGE INPUTS ---
    [HarmonyPatch(typeof(Kingmaker.Controllers.Clicks.PointerController), "Tick")]
    public static class PointerController_Block_Patch
    {
        public static bool Prefix()
        {
            if (CraftingUI.Instance != null && CraftingUI.Instance.IsOpen) return false;
            return true;
        }
    }

    // --- EMPÊCHER LE STACKING VIA CanBeMerged ---
    [HarmonyPatch(typeof(ItemEntity), nameof(ItemEntity.CanBeMerged), new Type[] { typeof(ItemEntity) })]
    public static class ItemEntity_NoMerge_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(ItemEntity __instance, ItemEntity other, ref bool __result)
        {
            // Si l'un des deux items appartient à notre coffre d'artisanat, on interdit la fusion.
            if ((__instance.Collection != null && __instance.Collection == DeferredInventoryOpener.CraftingBox) ||
                (other.Collection != null && other.Collection == DeferredInventoryOpener.CraftingBox))
            {
                __result = false;
                return false;
            }
            return true;
        }
    }

    // --- SUPPORT DU RENOMMAGE PERSISTANT ---
    [HarmonyPatch(typeof(ItemEntity), "get_Name")]
    public static class ItemEntity_Name_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ItemEntity __instance, ref string __result)
        {
            try {
                var part = __instance.Get<ItemPartCustomName>();
                if (part != null && !string.IsNullOrEmpty(part.CustomName))
                {
                    __result = part.CustomName;
                }
            } catch { }
        }
    }

    // --- SÉCURITÉ : PRÉSERVER LE NOM LORS D'UN SPLIT ---
    [HarmonyPatch(typeof(ItemEntity), nameof(ItemEntity.Split))]
    public static class ItemEntity_Split_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ItemEntity __instance, ItemEntity __result)
        {
            if (__instance == null || __result == null || __instance == __result) return;
            var originalPart = __instance.Get<ItemPartCustomName>();
            if (originalPart != null)
            {
                var newPart = __result.Ensure<ItemPartCustomName>();
                newPart.CustomName = originalPart.CustomName;
            }
        }
    }
}