using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using Kingmaker;
using Kingmaker.Items;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Items.Weapons;
using Kingmaker.Blueprints.Items.Armors;
using Kingmaker.Blueprints.Items.Ecnchantments;

namespace CraftingSystem
{
    public class CraftingUI : MonoBehaviour
    {
        public static CraftingUI Instance;
        public bool IsOpen = false;
        public bool ShowSettings = false;
        
        private Vector2 scrollPosition;
        private Vector2 titleScrollPosition;
        public string feedbackMessage = "";
        private string newNameDraft = "";
        private string enchantmentSearch = "";
        private ItemEntity selectedItem = null;
        private bool lastOpenState = false;
        private string activeDescriptionPopup = "";
        private string activeDescriptionTitle = "";
        
        // Auto-scale reference
        private const float REFERENCE_WIDTH = 2560f;
        private const float REFERENCE_HEIGHT = 1440f;
        
        // Sélection multiple & Filtres
        private List<string> queuedEnchantGuids = new List<string>();
        private bool showCategoryFilter = false;
        private HashSet<string> activeCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private HashSet<string> activeTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Awake() 
        { 
            Instance = this;
            CraftingSettings.LoadSettings();
        }

        private void UpdateAutoScale()
        {
            float resScale = Math.Min(Screen.width / REFERENCE_WIDTH, Screen.height / REFERENCE_HEIGHT);
            float dpiScale = 1f;
            try { if (Screen.dpi > 0) dpiScale = Screen.dpi / 96f; } catch { dpiScale = 1f; }
            float finalScale = Mathf.Clamp(resScale * dpiScale, 0.5f, 2.0f);
            CraftingSettings.ScalePercent = (int)(finalScale * 100f);
        }

        void OnGUI()
        {
            if (!IsOpen) 
            {
                lastOpenState = false;
                return;
            }

            if (!lastOpenState)
            {
                selectedItem = null;
                ShowSettings = false;
                lastOpenState = true;
            }

            UpdateAutoScale();

            if (Event.current != null && Event.current.isKey && Event.current.keyCode == KeyCode.Escape)
            {
                IsOpen = false;
                Event.current.Use();
                return;
            }

            EnchantmentScanner.StartSync();

            var workshop = Game.Instance.Player.MainCharacter.Value.Get<UnitPartWilcerWorkshop>();
            workshop?.CheckAndFinishProjects();

            float scale = CraftingSettings.ScalePercent / 100f;
            float width = 800f * scale; 
            float height = 600f * scale;
            Rect windowRect = new Rect((Screen.width - width) / 2f, (Screen.height - height) / 2f, width, height);

            if (Event.current != null && !windowRect.Contains(Event.current.mousePosition))
            {
                if (Event.current.type == EventType.MouseDown || Event.current.type == EventType.MouseUp || Event.current.type == EventType.ScrollWheel)
                {
                    Event.current.Use();
                }
            }
            
            GUI.Box(new Rect(0, 0, Screen.width, Screen.height), "");
            GUI.Window(999, windowRect, DrawWindowContent, "");
            
            // --- FENÊTRE DE DESCRIPTION (POPUP) ---
            if (!string.IsNullOrEmpty(activeDescriptionPopup))
            {
                float pW = 500f * scale;
                float pH = 350f * scale;
                Rect popupRect = new Rect((Screen.width - pW) / 2f, (Screen.height - pH) / 2f, pW, pH);
                GUI.Window(998, popupRect, DrawDescriptionPopup, "");
                GUI.BringWindowToFront(998);
            }

            GUI.FocusWindow(string.IsNullOrEmpty(activeDescriptionPopup) ? 999 : 998);
        }

        void DrawWindowContent(int windowID)
        {
            float scale = CraftingSettings.ScalePercent / 100f;

            if (!string.IsNullOrEmpty(feedbackMessage))
            {
                GUILayout.Label(feedbackMessage, new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = (int)(18 * scale) });
                if (GUILayout.Button(Helpers.GetString("ui_btn_ok", "OK"), GUILayout.Height(40 * scale))) feedbackMessage = "";
                return;
            }

            // --- HEADER AVEC DÉFILEMENT POUR TEXTE LONG ---
            GUILayout.BeginHorizontal();
            
            string title = Helpers.GetString("ui_title_workshop", "Workshop");
            if (ShowSettings) title = Helpers.GetString("ui_title_config", "Configuration");
            else if (selectedItem != null) title = Helpers.GetString("ui_title_details", "Details: ") + selectedItem.Name;
            else title = Helpers.GetString("ui_title_select", "Item Selection");
            
            titleScrollPosition = GUILayout.BeginScrollView(titleScrollPosition, GUIStyle.none, GUIStyle.none, GUILayout.ExpandWidth(true), GUILayout.Height(45 * scale));
            GUILayout.Label(title, new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = (int)(16 * scale), wordWrap = false });
            GUILayout.EndScrollView();
            
            GUILayout.Space(10);
            
            if (selectedItem != null && !ShowSettings)
            {
                if (GUILayout.Button(Helpers.GetString("ui_btn_back", "<< BACK"), GUILayout.Width(130 * scale), GUILayout.Height(30 * scale))) 
                {
                    selectedItem = null;
                    newNameDraft = "";
                    queuedEnchantGuids.Clear();
                    activeCategories.Clear();
                    activeTypes.Clear();
                    showCategoryFilter = false;
                }
            }

            float windowWidth = 800f * scale;
            float optionWidth = Mathf.Max(CraftingSettings.BUTTON_OPTION_WIDTH_BASE * scale, windowWidth * 0.14f);
            float closeWidth  = Mathf.Max(CraftingSettings.BUTTON_CLOSE_WIDTH_BASE  * scale, windowWidth * 0.06f);

            if (GUILayout.Button(ShowSettings ? Helpers.GetString("ui_btn_workshop_short", "Workshop") : Helpers.GetString("ui_btn_options", "Options"), GUILayout.Width(optionWidth), GUILayout.Height(30 * scale)))
            {
                ShowSettings = !ShowSettings;
            }
            
            if (GUILayout.Button("X", GUILayout.Width(closeWidth), GUILayout.Height(30 * scale))) IsOpen = false;
            GUILayout.EndHorizontal();

            GUILayout.Space(10);
            
            if (ShowSettings) DrawSettingsGUI(scale);
            else if (selectedItem != null) DrawItemModificationGUI(scale);
            else DrawInventoryGUI(scale);
        }

        void DrawInventoryGUI(float scale)
        {
            scrollPosition = GUILayout.BeginScrollView(scrollPosition, GUILayout.ExpandHeight(true));
            var workshop = Game.Instance.Player.MainCharacter.Value.Get<UnitPartWilcerWorkshop>();
            var items = workshop?.StashedItems ?? new List<ItemEntity>();

            if (!items.Any()) GUILayout.Label(Helpers.GetString("ui_no_item_stashed", "\n   (No item is stored in the workshop)"));
            else 
            {
                GUIStyle entryStyle = new GUIStyle(GUI.skin.button) { 
                    wordWrap = true, 
                    alignment = TextAnchor.UpperCenter,
                    fontSize = (int)(14 * scale) 
                };
                int cols = 2; 
                for (int i = 0; i < items.Count; i += cols)
                {
                    GUILayout.BeginHorizontal();
                    for (int j = 0; j < cols && (i + j) < items.Count; j++)
                    {
                        var it = items[i + j];
                        var project = workshop?.ActiveProjects.FirstOrDefault(p => p.Item == it);
                        string label = it.Name;
                        if (project != null) label += Helpers.GetString("ui_in_forge", " (In forge...)");

                        if (GUILayout.Button(label, entryStyle, GUILayout.Width(350 * scale), GUILayout.Height(50 * scale))) 
                        {
                            selectedItem = it;
                            newNameDraft = it.Name;
                            queuedEnchantGuids.Clear();
                            activeCategories.Clear();
                            showCategoryFilter = false;
                            
                            // -- PRÉ-SÉLECTION DU TYPE --
                            activeTypes.Clear();
                            if (it.Blueprint is BlueprintItemWeapon) activeTypes.Add("Weapon");
                            else if (it.Blueprint is BlueprintItemArmor) activeTypes.Add("Armor");
                            else activeTypes.Add("Other");
                        }
                    }
                    GUILayout.EndHorizontal();
                }
            }
            GUILayout.EndScrollView();
        }

        void DrawItemModificationGUI(float scale)
        {
            var workshop = Game.Instance.Player.MainCharacter.Value.Get<UnitPartWilcerWorkshop>();
            var activeProject = workshop?.ActiveProjects.FirstOrDefault(p => p.Item == selectedItem);

            GUILayout.BeginVertical(GUI.skin.box);

            if (activeProject != null)
            {
                long remainingTicks = activeProject.FinishTimeTicks - Game.Instance.Player.GameTime.Ticks;
                double remainingDays = Math.Max(0, remainingTicks / (double)TimeSpan.TicksPerDay);
                GUILayout.Label(Helpers.GetString("ui_wilcer_working", "<b>WILCER IS CURRENTLY WORKING ON THIS ITEM</b>"), new GUIStyle(GUI.skin.label) { richText = true, alignment = TextAnchor.MiddleCenter });
                string remText = string.Format(Helpers.GetString("ui_time_remaining", "Estimated remaining time: {0:F1} days"), remainingDays);
                GUILayout.Label(remText, new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter });
                GUILayout.Space(20);
                if (GUILayout.Button(Helpers.GetString("ui_btn_close_ui", "Close Interface"), GUILayout.Height(40 * scale))) IsOpen = false;
                GUILayout.EndVertical();
                return;
            }

            scrollPosition = GUILayout.BeginScrollView(scrollPosition, false, true, GUILayout.ExpandHeight(true));
            
            // --- SECTION RENOMMAGE ---
            GUILayout.Label(Helpers.GetString("ui_special_action_rename", "Special Action: Rename item (Free)"));
            GUILayout.BeginHorizontal();
            
            float windowWidth = 800f * scale;
            float buttonsSpace = (100f + 80f + 25f) * scale;
            float padding = (45f + 20f) * scale; 
            float exactTextWidth = windowWidth - buttonsSpace - padding;

            GUIStyle textFieldStyle = new GUIStyle(GUI.skin.textField);
            textFieldStyle.wordWrap = false; 

            newNameDraft = GUILayout.TextField(newNameDraft, textFieldStyle, GUILayout.Width(exactTextWidth), GUILayout.Height(30 * scale));
            
            GUILayout.Space(10 * scale);
            
            if (GUILayout.Button(Helpers.GetString("ui_btn_rename", "Renommer"), GUILayout.Width(100 * scale), GUILayout.Height(30 * scale)))
            {
                ItemRenamer.RenameItem(selectedItem, newNameDraft);
                feedbackMessage = Helpers.GetString("ui_feedback_renamed", "The item has been renamed!");
            }

            if (selectedItem != null && GUILayout.Button(Helpers.GetString("ui_btn_auto", "Auto"), GUILayout.Width(80 * scale), GUILayout.Height(30 * scale)))
            {
                string autoName = ItemRenamer.GenerateAutoName(selectedItem);
                ItemRenamer.RenameItem(selectedItem, autoName);
                newNameDraft = autoName;
                feedbackMessage = Helpers.GetString("ui_feedback_autoname_gen", "Automatic name generated.");
            }
            GUILayout.EndHorizontal();
            
            GUILayout.Space(20);
            Div(scale);
            GUILayout.Space(10);
            
            // --- SECTION : ENCHANTEMENTS DÉJÀ PRÉSENTS ---
            GUILayout.Label(Helpers.GetString("ui_applied_enchants", "Applied Enchantments:"), new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold });
            var currentEnchants = selectedItem.Enchantments.ToList();
            if (!currentEnchants.Any()) 
            {
                GUILayout.Label(Helpers.GetString("ui_no_magic_enchants", "<i>(No magic enchantment)</i>"), new GUIStyle(GUI.skin.label) { richText = true, fontSize = (int)(12 * scale) });
            }
            else 
            {
                foreach (var ench in currentEnchants)
                {
                    string guid = ench.Blueprint.AssetGuid.ToString();
                    var overrideData = EnchantmentScanner.GetByGuid(guid);
                    int pointValue = overrideData?.PointCost ?? ench.Blueprint.EnchantmentCost;
                    if (pointValue < 0) pointValue = 0;
                    GUILayout.BeginHorizontal(GUI.skin.box);
                    string displayName = GetDisplayName(ench.Blueprint, overrideData);
                    GUILayout.Label($"{displayName} (+{pointValue})", GUILayout.ExpandWidth(true));

                    // POUR LES OBJETS DÉJÀ APPLIQUÉS (RÉSOLU DYNAMIQUEMENT)
                    string appliedDesc = GetLocalizedDescription(ench.Blueprint, overrideData);
                    if (!string.IsNullOrEmpty(appliedDesc))
                    {
                        GUIContent infoContent = new GUIContent("<color=#3498db>?</color>");
                        GUIStyle infoStyle = new GUIStyle(GUI.skin.button) { 
                            richText = true, 
                            fontStyle = FontStyle.Bold, 
                            fontSize = (int)(9 * scale),
                            padding = new RectOffset(0, 0, 0, 0)
                        };
                        if (GUILayout.Button(infoContent, infoStyle, GUILayout.Width(15 * scale), GUILayout.Height(15 * scale))) 
                        {
                            activeDescriptionTitle = displayName;
                            activeDescriptionPopup = appliedDesc;
                        }
                    }
                    else
                    {
                        GUILayout.Space(25 * scale);
                    }
                    if (GUILayout.Button(Helpers.GetString("ui_btn_remove", "Remove"), GUILayout.Width(80 * scale)))
                    {
                        selectedItem.RemoveEnchantment(ench);
                        selectedItem.Identify(); 
                    }
                    GUILayout.EndHorizontal();
                }
            }

            GUILayout.Space(20);
            Div(scale);
            GUILayout.Space(10);
            
            // --- RECHERCHE + LISTE ---
            GUILayout.Label(Helpers.GetString("ui_available_enchants", "Available Enchantments:"), new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold });

            // -- UI FILTRE DES TYPES --
            GUILayout.BeginHorizontal();
            bool isWep = GUILayout.Toggle(activeTypes.Contains("Weapon"), Helpers.GetString("ui_filter_weapons", " Weapons"), GUILayout.Width(150 * scale));
            bool isArm = GUILayout.Toggle(activeTypes.Contains("Armor"), Helpers.GetString("ui_filter_armors", " Armors"), GUILayout.Width(150 * scale));
            // TODO check if it's correct
            //bool isOth = GUILayout.Toggle(activeTypes.Contains("Other"), Helpers.GetString("ui_filter_others", " Others"), GUILayout.Width(150 * scale));
            bool isOth = isWep || isArm;

            if (isWep) activeTypes.Add("Weapon"); else activeTypes.Remove("Weapon");
            if (isArm) activeTypes.Add("Armor"); else activeTypes.Remove("Armor");
            if (isOth) activeTypes.Add("Other"); else activeTypes.Remove("Other");
            GUILayout.EndHorizontal();
            
            GUILayout.Space(10);

            // On récupère TOUTE la liste directement, pour laisser l'UI gérer les types
            List<EnchantmentData> rawAvailable;
            lock (EnchantmentScanner.MasterList)
            {
                rawAvailable = EnchantmentScanner.MasterList.ToList();
            }

            // On filtre d'abord par Type
            var typeFilteredAvailable = new List<EnchantmentData>();
            foreach (var data in rawAvailable)
            {
                if (activeTypes.Contains(data.Type) || queuedEnchantGuids.Contains(data.Guid))
                {
                    typeFilteredAvailable.Add(data);
                }
            }

            // -- EXTRACTION DES CATÉGORIES (basée uniquement sur les types actifs) --
            HashSet<string> uniqueCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var data in typeFilteredAvailable)
            {
                var cleanCats = data.Categories != null ? data.Categories.Where(c => !string.Equals(c, "Discovered", StringComparison.OrdinalIgnoreCase)).ToList() : new List<string>();
                if (cleanCats.Count == 0) uniqueCategories.Add(Helpers.GetString("ui_cat_untyped", "Sans catégorie"));
                else foreach (var cat in cleanCats) uniqueCategories.Add(cat);
            }
            List<string> allCategoriesList = uniqueCategories.ToList();
            allCategoriesList.Sort();

            // -- UI RECHERCHE & FILTRES CATÉGORIES --
            GUILayout.BeginHorizontal();
            GUILayout.Label(Helpers.GetString("ui_search_label", "Search: "), GUILayout.Width(100 * scale));
            enchantmentSearch = GUILayout.TextField(enchantmentSearch, GUILayout.ExpandWidth(true));
            
            string filterBtnText = activeCategories.Count > 0 ? string.Format(Helpers.GetString("ui_filter_active_btn", "Filters ({0}) \u25bc"), activeCategories.Count) : Helpers.GetString("ui_filter_all_btn", "Filters (All) \u25bc");
            if (GUILayout.Button(filterBtnText, GUILayout.Width(130 * scale))) showCategoryFilter = !showCategoryFilter;
            GUILayout.EndHorizontal();

            if (showCategoryFilter)
            {
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(Helpers.GetString("ui_filter_check_all", "Check All"), GUILayout.Width(120 * scale))) foreach (var c in allCategoriesList) activeCategories.Add(c);
                if (GUILayout.Button(Helpers.GetString("ui_filter_uncheck_all", "Uncheck All"), GUILayout.Width(120 * scale))) activeCategories.Clear();
                GUILayout.Label(activeCategories.Count == 0 ? Helpers.GetString("ui_filter_none_active", " <i>(No filter active = Show all)</i>") : "", new GUIStyle(GUI.skin.label) { richText = true });
                GUILayout.EndHorizontal();
                GUILayout.Space(5);

                int cols = 3;
                int currentCol = 0;
                GUILayout.BeginHorizontal();
                foreach (var cat in allCategoriesList)
                {
                    bool isActive = activeCategories.Contains(cat);
                    bool toggled = GUILayout.Toggle(isActive, cat, GUILayout.Width(240 * scale));
                    if (toggled && !isActive) activeCategories.Add(cat);
                    if (!toggled && isActive) activeCategories.Remove(cat);

                    currentCol++;
                    if (currentCol >= cols)
                    {
                        GUILayout.EndHorizontal();
                        GUILayout.BeginHorizontal();
                        currentCol = 0;
                    }
                }
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
            }

            GUILayout.Space(5);

            if (EnchantmentScanner.IsSyncing)
            {
                GUILayout.Label($"({EnchantmentScanner.LastSyncMessage})", new GUIStyle(GUI.skin.label) { richText = true, fontSize = (int)(12 * scale), alignment = TextAnchor.MiddleCenter });
            }

            GUILayout.Space(5);

            GUILayout.BeginVertical(GUI.skin.box);

            if (EnchantmentScanner.IsSyncing)
            {
                GUILayout.Label(Helpers.GetString("ui_scan_in_progress", "Scan en cours — aucun enchantement disponible pour l'instant."), new GUIStyle(GUI.skin.label) { fontSize = (int)(12 * scale), alignment = TextAnchor.MiddleCenter });
            }
            else
            {
                var currentSelectedList = queuedEnchantGuids.Select(g => EnchantmentScanner.GetByGuid(g)).Where(d => d != null).ToList();
                bool isReadyForSpecial = CraftingCalculator.IsItemReadyForSpecialEnchants(selectedItem, currentSelectedList);
                bool isWeaponOrArmor = selectedItem.Blueprint is BlueprintItemWeapon || selectedItem.Blueprint is BlueprintItemArmor;
                
                // On utilise la liste pré-filtrée par type ET on trie pour mettre les cochés en haut !
                var displayedEnchants = typeFilteredAvailable
                    .OrderByDescending(e => queuedEnchantGuids.Contains(e.Guid)) 
                    .ThenBy(e => e.Name)
                    .ToList();

                foreach (var data in displayedEnchants)
                {
                    bool isQueued = queuedEnchantGuids.Contains(data.Guid);

                    // --- FILTRE DES CATÉGORIES (LOGIQUE 'ET' STRICTE) ---
                    if (!isQueued && activeCategories.Count > 0)
                    {
                        var cleanCats = data.Categories != null ? data.Categories.Where(c => !string.Equals(c, "Discovered", StringComparison.OrdinalIgnoreCase)).ToList() : new List<string>();
                        if (cleanCats.Count == 0) cleanCats.Add(Helpers.GetString("ui_cat_untyped", "Untyped"));

                        bool matchAll = true; 
                        foreach (var activeCat in activeCategories)
                        {
                            if (!cleanCats.Contains(activeCat, StringComparer.OrdinalIgnoreCase))
                            {
                                matchAll = false;
                                break;
                            }
                        }
                        if (!matchAll) continue; 
                    }

                    // --- FILTRE D'OBJET NORMAL (SI PAS DE +1) ---
                    if (CraftingSettings.RequirePlusOneFirst && isWeaponOrArmor && !isReadyForSpecial && !isQueued)
                    {
                        bool isAllowed = CraftingCalculator.IsEnchantmentAllowedOnNormalItem(data);
                        
                        // LOG DE TRAÇAGE POUR LE +1 REQUIRED
                        /*if (CraftingSettings.RequirePlusOneFirst && UnityEngine.Time.frameCount % 300 == 0) // Toutes les 5s env
                        {
                            Main.ModEntry.Logger.Log($"[FILTRE-REPORT] {data.Name} -> Caché car isReadyForSpecial={isReadyForSpecial} et IsAllowedOnNormal={isAllowed}");
                        }*/

                        if (!isAllowed) continue; 
                    }

                    // --- FILTRE RECHERCHE ---
                    var bp = ResourcesLibrary.TryGetBlueprint<BlueprintItemEnchantment>(BlueprintGuid.Parse(data.Guid));
                    string displayName = GetDisplayName(bp, data);
                    if (!string.IsNullOrEmpty(enchantmentSearch) && !displayName.ToLower().Contains(enchantmentSearch.ToLower())) continue;
                    
                    // --- FILTRE SOURCE ---
                    if (CraftingSettings.CurrentSourceFilter == SourceFilter.TTRPG && data.Source != "TTRPG") continue;
                    if (CraftingSettings.CurrentSourceFilter == SourceFilter.Owlcat && data.Source != "TTRPG" && data.Source != "Owlcat") continue;
                    if (CraftingSettings.CurrentSourceFilter == SourceFilter.OwlcatPlus && data.Source != "TTRPG" && data.Source != "Owlcat" && data.Source != "Owlcat+") continue;
                    if (CraftingSettings.CurrentSourceFilter == SourceFilter.Mods && (data.Source == "TTRPG" || data.Source == "Owlcat" || data.Source == "Owlcat+")) continue;
                    
                    long costToPay;
                    if (isQueued)
                    {
                        // Pour les déjà sélectionnés : prix basé sur l'ordre historique
                        int idx = currentSelectedList.FindIndex(e => e.Guid == data.Guid);
                        var preceding = currentSelectedList.Take(idx);
                        costToPay = CraftingCalculator.GetMarginalCost(selectedItem, preceding, data, CraftingSettings.CostMultiplier);
                    }
                    else
                    {
                        // Pour les nouveaux : prix par rapport à TOUT le panier
                        costToPay = CraftingCalculator.GetMarginalCost(selectedItem, currentSelectedList, data, CraftingSettings.CostMultiplier);
                    }
                    int days = CraftingCalculator.GetCraftingDays(costToPay, CraftingSettings.InstantCrafting);
                    
                    if (data.GoldOverride >= 0) costToPay = (long)(data.GoldOverride * CraftingSettings.CostMultiplier);

                    string internalName = bp != null ? bp.name : (data.Name ?? "");

                    GUILayout.BeginHorizontal(GUI.skin.box);
                    GUIStyle toggleStyle = new GUIStyle(GUI.skin.toggle) { richText = true };
                    
                    // -- NOM + ICÔNE INFO [?] --
                    bool newSelected = GUILayout.Toggle(isQueued, $"{displayName} <color=#888888>({internalName})</color>", toggleStyle, GUILayout.ExpandWidth(true));
                    
                    string descForData = GetLocalizedDescription(bp, data);
                    if (!string.IsNullOrEmpty(descForData))
                    {
                        GUIContent infoContent = new GUIContent("<color=#3498db>?</color>");
                        GUIStyle infoStyle = new GUIStyle(GUI.skin.button) { 
                            richText = true, 
                            fontStyle = FontStyle.Bold, 
                            fontSize = (int)(9 * scale),
                            padding = new RectOffset(0, 0, 0, 0)
                        };
                        if (GUILayout.Button(infoContent, infoStyle, GUILayout.Width(15 * scale), GUILayout.Height(15 * scale))) 
                        {
                            activeDescriptionTitle = displayName;
                            activeDescriptionPopup = descForData;
                        }
                    }
                    else
                    {
                        GUILayout.Space(25 * scale);
                    }

                    string currency = Helpers.GetString("ui_currency_gp", "gp");
                    string daysLabel = Helpers.GetString("ui_time_days_short", "d");
                    GUILayout.Label($"{costToPay} {currency} / {days} {daysLabel}   (+{data.PointCost})", new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleRight }, GUILayout.Width(180 * scale));
                    
                    if (newSelected && !isQueued) 
                    {
                        string baseName = System.Text.RegularExpressions.Regex.Replace(internalName, @"\d+$", "");
                        
                        if (baseName.ToLower().Contains("enhancement"))
                        {
                            queuedEnchantGuids.RemoveAll(guid => 
                            {
                                var otherData = EnchantmentScanner.GetByGuid(guid);
                                if (otherData != null)
                                {
                                    var otherBp = ResourcesLibrary.TryGetBlueprint<BlueprintItemEnchantment>(BlueprintGuid.Parse(guid));
                                    string otherInternal = otherBp != null ? otherBp.name : (otherData.Name ?? "");
                                    string otherBase = System.Text.RegularExpressions.Regex.Replace(otherInternal, @"\d+$", "");
                                    return otherBase == baseName;
                                }
                                return false;
                            });
                        }

                        queuedEnchantGuids.Add(data.Guid);
                    }
                    if (!newSelected && isQueued) queuedEnchantGuids.Remove(data.Guid);

                    GUILayout.EndHorizontal();
                }
            }

            GUILayout.EndVertical();
            GUILayout.EndScrollView();

            // =========================================================================
            // BLOC FIXE EN BAS
            // =========================================================================
            GUILayout.Space(8);

            var selectedList = new List<EnchantmentData>();
            foreach (var g in queuedEnchantGuids)
            {
                var d = EnchantmentScanner.GetByGuid(g);
                if (d != null)
                {
                    selectedList.Add(d);
                }
                else if (UnityEngine.Time.frameCount % 60 == 0)
                {
                    Main.ModEntry.Logger.Warning($"[PANIER-DEBUG] Guid={g} -> NON TROUVÉ DANS LA MASTERLIST");
                }
            }


            long totalCost = CraftingCalculator.GetMarginalCost(selectedItem, selectedList, null, CraftingSettings.CostMultiplier);
            int totalDays = CraftingCalculator.GetCraftingDays(totalCost, CraftingSettings.InstantCrafting);

            int currentLevelPoints = CraftingCalculator.CalculateDisplayedEnchantmentPoints(selectedItem);
            int maxLevel = CraftingSettings.MaxTotalBonus;
            int selectedPoints = selectedList.Sum(d => d.PointCost);

            GUILayout.BeginHorizontal();
            GUILayout.Label(string.Format(Helpers.GetString("ui_current_level", "Current level: {0}/{1}"), currentLevelPoints, maxLevel), GUILayout.ExpandWidth(false));
            GUILayout.FlexibleSpace();
            GUILayout.Label(string.Format(Helpers.GetString("ui_selection_total", "Selection: +{0} \u2014 Total: {1} gp / ~{2} d"), selectedPoints, totalCost, totalDays), GUILayout.ExpandWidth(false));
            GUILayout.EndHorizontal();

            GUILayout.Space(6);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(Helpers.GetString("ui_btn_validate_selection", "Confirm Selection"), GUILayout.Width(200 * scale), GUILayout.Height(32 * scale)))
            {
                string validationError = CraftingCalculator.ValidateSelectionBeforeStart(selectedItem, selectedList, totalCost);
                if (!string.IsNullOrEmpty(validationError))
                {
                    feedbackMessage = validationError;
                }
                else
                {
                    if (Game.Instance.Player.Money >= totalCost)
                    {
                        Game.Instance.Player.Money -= (int)totalCost;
                        
                        foreach (var d in selectedList)
                        {
                            long c = CraftingCalculator.GetEnchantmentCost(selectedItem, d, CraftingSettings.CostMultiplier);
                            if (d.GoldOverride >= 0) c = (long)(d.GoldOverride * CraftingSettings.CostMultiplier);
                            int days = CraftingCalculator.GetCraftingDays(c, CraftingSettings.InstantCrafting);
                            CraftingActions.StartCraftingProject(selectedItem, d, (int)c, days);
                        }

                        try 
                        {
                            string localizedLogBase = Helpers.GetString("log_project_started", "<color=#E2C675>[Workshop]</color> <b>{0}</b> has been sent to the forge for <b>{1} gp</b>.");
                            string logText = string.Format(localizedLogBase, selectedItem.Name, totalCost);
                            Kingmaker.PubSubSystem.EventBus.RaiseEvent<Kingmaker.PubSubSystem.ILogMessageUIHandler>(
                                h => h.HandleLogMessage(logText)
                            );
                        }
                        catch (Exception ex)
                        {
                            Main.ModEntry.Logger.Error($"[ATELIER] Impossible d'écrire dans le log de combat : {ex.Message}");
                        }

                        queuedEnchantGuids.Clear();
                        selectedItem = null;
                        
                        string fbBase = Helpers.GetString("ui_feedback_projects_started", "Projects started: {0} enchantment(s).");
                        feedbackMessage = string.Format(fbBase, selectedList.Count);
                    }
                    else
                    {
                        feedbackMessage = Helpers.GetString("ui_feedback_no_funds", "Unexpected error: insufficient funds at the time of payment.");
                    }
                }
            }

            if (GUILayout.Button(Helpers.GetString("ui_btn_cancel_selection", "Cancel Selection"), GUILayout.Width(180 * scale), GUILayout.Height(32 * scale)))
            {
                queuedEnchantGuids.Clear();
            }
            GUILayout.EndHorizontal();

            GUILayout.EndVertical(); 
        }

        void DrawSettingsGUI(float scale)
        {
            float prevCostMult = CraftingSettings.CostMultiplier;
            bool prevInstant = CraftingSettings.InstantCrafting;
            bool prevEnforce = CraftingSettings.EnforcePointsLimit;
            int prevMaxEnh = CraftingSettings.MaxEnhancementBonus;
            int prevMaxTotal = CraftingSettings.MaxTotalBonus;
            bool prevRequirePlus = CraftingSettings.RequirePlusOneFirst;
            bool prevSlotPenalty = CraftingSettings.ApplySlotPenalty;
            bool prevEnableEpic = CraftingSettings.EnableEpicCosts;
            SourceFilter prevSourceFilter = CraftingSettings.CurrentSourceFilter;

            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label(Helpers.GetString("ui_settings_title", "Workshop Settings"), new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold });
            
            GUILayout.Space(10);
            
            GUILayout.BeginHorizontal();
            GUILayout.Label(Helpers.GetString("ui_settings_cost_mult", " Cost Multiplier: ") + CraftingSettings.CostMultiplier.ToString("F1"), GUILayout.Width(200 * scale));
            CraftingSettings.CostMultiplier = (float)Math.Round(GUILayout.HorizontalSlider(CraftingSettings.CostMultiplier, 0f, 5f, GUILayout.Width(150 * scale)), 1);
            GUILayout.EndHorizontal();

            bool previousInstantCrafting = CraftingSettings.InstantCrafting;
            CraftingSettings.InstantCrafting = GUILayout.Toggle(CraftingSettings.InstantCrafting, Helpers.GetString("ui_settings_instant_craft", "Instant Crafting"));

            if (CraftingSettings.InstantCrafting && !previousInstantCrafting)
            {
                try
                {
                    var workshop = Game.Instance.Player.MainCharacter.Value.Get<UnitPartWilcerWorkshop>();
                    if (workshop != null)
                    {
                        workshop.CheckAndFinishProjects();
                        feedbackMessage = Helpers.GetString("ui_feedback_all_forges_done", "All active forging projects have been completed instantly!");
                    }
                }
                catch (Exception ex) { Main.ModEntry.Logger.Error($"[UI-DEBUG] CRASH : {ex.Message}"); }
            }
            
            GUILayout.Space(10);
            
            CraftingSettings.EnforcePointsLimit = GUILayout.Toggle(CraftingSettings.EnforcePointsLimit, Helpers.GetString("ui_settings_enforce_limit", " Enforce Bonus Limits (Pathfinder)"));
            
            if (CraftingSettings.EnforcePointsLimit)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(string.Format(Helpers.GetString("ui_settings_max_enhancement", " Max Enhancement: +{0}"), CraftingSettings.MaxEnhancementBonus), GUILayout.Width(150 * scale));
                CraftingSettings.MaxEnhancementBonus = (int)GUILayout.HorizontalSlider(CraftingSettings.MaxEnhancementBonus, 1, 20, GUILayout.Width(150 * scale));
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label(string.Format(Helpers.GetString("ui_settings_max_total", " Max Total: +{0}"), CraftingSettings.MaxTotalBonus), GUILayout.Width(150 * scale));
                CraftingSettings.MaxTotalBonus = (int)GUILayout.HorizontalSlider(CraftingSettings.MaxTotalBonus, 1, 50, GUILayout.Width(150 * scale));
                GUILayout.EndHorizontal();

                CraftingSettings.RequirePlusOneFirst = GUILayout.Toggle(CraftingSettings.RequirePlusOneFirst, Helpers.GetString("ui_settings_require_plus_one", " Prerequisite: At least +1 Enhancement"));
                CraftingSettings.ApplySlotPenalty = GUILayout.Toggle(CraftingSettings.ApplySlotPenalty, Helpers.GetString("ui_settings_slot_penalty", " Apply Slot Penalty (x1.5)"));
                CraftingSettings.EnableEpicCosts = GUILayout.Toggle(CraftingSettings.EnableEpicCosts, Helpers.GetString("ui_settings_epic_multiplier", " Enable Epic Multiplier (x10)"));
            }

            GUILayout.Space(10);
            GUILayout.Label(Helpers.GetString("ui_settings_source_display", "Source display:"));
            GUILayout.BeginHorizontal();
            int sliderVal = (int)CraftingSettings.CurrentSourceFilter;
            sliderVal = Mathf.RoundToInt(GUILayout.HorizontalSlider(sliderVal, 0, 4, GUILayout.Width(300 * scale)));
            CraftingSettings.CurrentSourceFilter = (SourceFilter)sliderVal;
            GUILayout.Space(20 * scale);
            
            string sourceLabel = "";
            switch (CraftingSettings.CurrentSourceFilter)
            {
                case SourceFilter.TTRPG: sourceLabel = Helpers.GetString("ui_settings_source_ttrpg", "TTRPG (TTRPG enchantments only)"); break;
                case SourceFilter.Owlcat: sourceLabel = Helpers.GetString("ui_settings_source_owlcat", "Owlcat (TTRPG + Owlcat)"); break;
                case SourceFilter.OwlcatPlus: sourceLabel = Helpers.GetString("ui_settings_source_owlcatplus", "Owlcat+ (TTRPG + Owlcat + Owlcat+)"); break;
                case SourceFilter.Mods: sourceLabel = Helpers.GetString("ui_settings_source_mods", "Mod (All non-base game enchantments)"); break;
                case SourceFilter.All: sourceLabel = Helpers.GetString("ui_settings_source_all_desc", "Show all"); break;
            }
            GUILayout.Label(sourceLabel, new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Italic });
            GUILayout.EndHorizontal();

            GUILayout.Space(20);
            
            GUILayout.Label(Helpers.GetString("ui_settings_diagnostic", "Diagnostic Tools:"));
            GUILayout.Label(EnchantmentScanner.LastSyncMessage);
            if (GUILayout.Button(Helpers.GetString("ui_settings_force_sync", "Force Synchronization (Full Scan)"), GUILayout.Height(30 * scale)))
            {
                EnchantmentScanner.ForceSync();
            }
            
            GUILayout.FlexibleSpace();
            GUILayout.EndVertical();

            if (prevCostMult != CraftingSettings.CostMultiplier || prevInstant != CraftingSettings.InstantCrafting || prevEnforce != CraftingSettings.EnforcePointsLimit
                || prevMaxEnh != CraftingSettings.MaxEnhancementBonus || prevMaxTotal != CraftingSettings.MaxTotalBonus || prevRequirePlus != CraftingSettings.RequirePlusOneFirst
                || prevSlotPenalty != CraftingSettings.ApplySlotPenalty || prevEnableEpic != CraftingSettings.EnableEpicCosts
                || prevSourceFilter != CraftingSettings.CurrentSourceFilter)
            {
                CraftingSettings.SaveSettings();
            }
        }

        private void Div(float scale)
        {
            Rect rect = GUILayoutUtility.GetLastRect();
            GUI.Box(new Rect(rect.x, rect.y + rect.height + 5, rect.width, 2 * scale), "");
        }

        private string GetLocalizedDescription(BlueprintItemEnchantment bp, EnchantmentData data)
        {
            // 1. Priorité au Jeu (Description localisée officielle)
            if (bp != null)
            {
                string localized = bp.m_Description?.ToString();
                if (!string.IsNullOrEmpty(localized) && localized != bp.name) 
                {
                    return System.Text.RegularExpressions.Regex.Replace(localized, "<.*?>", string.Empty);
                }
            }

            // 2. Surcharge JSON (Seulement si le jeu est vide)
            if (data != null && !string.IsNullOrEmpty(data.Description)) return data.Description;

            // 3. Fallback sur le Commentaire développeur
            if (bp != null && !string.IsNullOrEmpty(bp.Comment)) return bp.Comment;

            return null;
        }

        private string GetDisplayName(BlueprintItemEnchantment bp, EnchantmentData data)
        {
            string finalName = "";

            if (bp != null && bp.m_EnchantName != null)
            {
                string localized = bp.m_EnchantName.ToString();
                if (!string.IsNullOrWhiteSpace(localized) && localized != bp.name) finalName = localized;
            }

            if (string.IsNullOrEmpty(finalName) && data != null && !string.IsNullOrWhiteSpace(data.Name)) 
                finalName = data.Name;

            if (string.IsNullOrEmpty(finalName) && bp != null)
            {
                finalName = bp.name.Replace("WeaponEnchantment", "")
                              .Replace("ArmorEnchantment", "")
                              .Replace("Enchantment", "")
                              .Replace("Plus", "+");
            }

            if (string.IsNullOrEmpty(finalName)) 
                finalName = Helpers.GetString("ui_unknown_enchant_name", "Unknown Enchantment");

            // Troncation à 50 caractères
            if (finalName.Length > 50) 
                finalName = finalName.Substring(0, 47) + "...";

            return finalName;
        }

        private void DrawDescriptionPopup(int windowID)
        {
            float scale = CraftingSettings.ScalePercent / 100f;
            GUILayout.BeginVertical();
            
            GUILayout.BeginHorizontal();
            GUILayout.Label(activeDescriptionTitle, new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = (int)(16 * scale) });
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("X", GUILayout.Width(30 * scale))) activeDescriptionPopup = "";
            GUILayout.EndHorizontal();
            
            GUILayout.Space(10);
            
            scrollPosition = GUILayout.BeginScrollView(scrollPosition, GUI.skin.box);
            GUILayout.Label(activeDescriptionPopup, new GUIStyle(GUI.skin.label) { wordWrap = true, richText = true, fontSize = (int)(14 * scale) });
            GUILayout.EndScrollView();
            
            GUILayout.Space(10);
            if (GUILayout.Button(Helpers.GetString("ui_btn_ok", "OK"), GUILayout.Height(40 * scale))) activeDescriptionPopup = "";
            
            GUILayout.EndVertical();
            GUI.DragWindow();
        }
    }
}