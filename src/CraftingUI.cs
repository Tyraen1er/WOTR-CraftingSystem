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
        
        // Auto-scale reference
        private const float REFERENCE_WIDTH = 2560f;
        private const float REFERENCE_HEIGHT = 1440f;
        
        // Sélection multiple & Filtres
        private HashSet<string> queuedEnchantGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
            GUI.FocusWindow(999);
        }

        void DrawWindowContent(int windowID)
        {
            float scale = CraftingSettings.ScalePercent / 100f;

            if (!string.IsNullOrEmpty(feedbackMessage))
            {
                GUILayout.Label(feedbackMessage, new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = (int)(18 * scale) });
                if (GUILayout.Button("OK", GUILayout.Height(40 * scale))) feedbackMessage = "";
                return;
            }

            // --- HEADER AVEC DÉFILEMENT POUR TEXTE LONG ---
            GUILayout.BeginHorizontal();
            
            string title = Helpers.GetString("ui_title_workshop", "Atelier");
            if (ShowSettings) title = Helpers.GetString("ui_title_config", "Configuration");
            else if (selectedItem != null) title = Helpers.GetString("ui_title_details", "Détails : ") + selectedItem.Name;
            else title = Helpers.GetString("ui_title_select", "Sélection d'objet");
            
            titleScrollPosition = GUILayout.BeginScrollView(titleScrollPosition, GUIStyle.none, GUIStyle.none, GUILayout.ExpandWidth(true), GUILayout.Height(45 * scale));
            GUILayout.Label(title, new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = (int)(16 * scale), wordWrap = false });
            GUILayout.EndScrollView();
            
            GUILayout.Space(10);
            
            if (selectedItem != null && !ShowSettings)
            {
                if (GUILayout.Button(Helpers.GetString("ui_btn_back", "<< RETOUR"), GUILayout.Width(130 * scale), GUILayout.Height(30 * scale))) 
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

            if (GUILayout.Button(ShowSettings ? Helpers.GetString("ui_btn_workshop", "Atelier") : Helpers.GetString("ui_btn_options", "Options"), GUILayout.Width(optionWidth), GUILayout.Height(30 * scale)))
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

            if (!items.Any()) GUILayout.Label(Helpers.GetString("ui_no_item_stashed", "\n   (Aucun objet n'est stocké dans l'atelier)"));
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
                        if (project != null) label += Helpers.GetString("ui_in_forge", " (En forge...)");

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
                GUILayout.Label(Helpers.GetString("ui_wilcer_working", "<b>WILCER EST EN TRAIN DE TRAVAILLER SUR CET OBJET</b>"), new GUIStyle(GUI.skin.label) { richText = true, alignment = TextAnchor.MiddleCenter });
                string remText = string.Format(Helpers.GetString("ui_time_remaining", "Temps restant estimé : {0:F1} jours"), remainingDays);
                GUILayout.Label(remText, new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter });
                GUILayout.Space(20);
                if (GUILayout.Button(Helpers.GetString("ui_btn_close_ui", "Fermer l'interface"), GUILayout.Height(40 * scale))) IsOpen = false;
                GUILayout.EndVertical();
                return;
            }

            scrollPosition = GUILayout.BeginScrollView(scrollPosition, false, true, GUILayout.ExpandHeight(true));
            
            // --- SECTION RENOMMAGE ---
            GUILayout.Label(Helpers.GetString("ui_special_action_rename", "Action Spéciale : Renommer l'objet (Gratuit)"));
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
                feedbackMessage = Helpers.GetString("ui_feedback_renamed", "L'objet a été renommé !");
            }

            if (selectedItem != null && GUILayout.Button(Helpers.GetString("ui_btn_auto", "Auto"), GUILayout.Width(80 * scale), GUILayout.Height(30 * scale)))
            {
                string autoName = ItemRenamer.GenerateAutoName(selectedItem);
                ItemRenamer.RenameItem(selectedItem, autoName);
                newNameDraft = autoName;
                feedbackMessage = Helpers.GetString("ui_feedback_autoname_gen", "Nom automatique généré.");
            }
            GUILayout.EndHorizontal();
            
            GUILayout.Space(20);
            Div(scale);
            GUILayout.Space(10);
            
            // --- SECTION : ENCHANTEMENTS DÉJÀ PRÉSENTS ---
            GUILayout.Label(Helpers.GetString("ui_applied_enchants", "Enchantements appliqués :"), new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold });
            var currentEnchants = selectedItem.Enchantments.ToList();
            if (!currentEnchants.Any()) 
            {
                GUILayout.Label(Helpers.GetString("ui_no_magic_enchants", "<i>(Aucun enchantement magique)</i>"), new GUIStyle(GUI.skin.label) { richText = true, fontSize = (int)(12 * scale) });
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
                    if (GUILayout.Button(Helpers.GetString("ui_btn_remove", "Retirer"), GUILayout.Width(80 * scale)))
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
            GUILayout.Label(Helpers.GetString("ui_available_enchants", "Enchantements disponibles :"), new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold });

            // -- UI FILTRE DES TYPES --
            GUILayout.BeginHorizontal();
            bool isWep = GUILayout.Toggle(activeTypes.Contains("Weapon"), Helpers.GetString("ui_filter_weapons", " Armes (Weapon)"), GUILayout.Width(150 * scale));
            bool isArm = GUILayout.Toggle(activeTypes.Contains("Armor"), Helpers.GetString("ui_filter_armors", " Armures (Armor)"), GUILayout.Width(150 * scale));
            bool isOth = GUILayout.Toggle(activeTypes.Contains("Other"), Helpers.GetString("ui_filter_others", " Autres (Other)"), GUILayout.Width(150 * scale));
            
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
                if (cleanCats.Count == 0) uniqueCategories.Add("Untyped");
                else foreach (var cat in cleanCats) uniqueCategories.Add(cat);
            }
            List<string> allCategoriesList = uniqueCategories.ToList();
            allCategoriesList.Sort();

            // -- UI RECHERCHE & FILTRES CATÉGORIES --
            GUILayout.BeginHorizontal();
            GUILayout.Label(Helpers.GetString("ui_search_label", "Recherche : "), GUILayout.Width(100 * scale));
            enchantmentSearch = GUILayout.TextField(enchantmentSearch, GUILayout.ExpandWidth(true));
            
            string filterBtnText = activeCategories.Count > 0 ? string.Format(Helpers.GetString("ui_filter_active_btn", "Filtres ({0}) ▼"), activeCategories.Count) : Helpers.GetString("ui_filter_all_btn", "Filtres (Tous) ▼");
            if (GUILayout.Button(filterBtnText, GUILayout.Width(130 * scale))) showCategoryFilter = !showCategoryFilter;
            GUILayout.EndHorizontal();

            if (showCategoryFilter)
            {
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(Helpers.GetString("ui_filter_check_all", "Tout cocher"), GUILayout.Width(120 * scale))) foreach (var c in allCategoriesList) activeCategories.Add(c);
                if (GUILayout.Button(Helpers.GetString("ui_filter_uncheck_all", "Tout décocher"), GUILayout.Width(120 * scale))) activeCategories.Clear();
                GUILayout.Label(activeCategories.Count == 0 ? Helpers.GetString("ui_filter_none_active", " <i>(Aucun filtre actif = Tout afficher)</i>") : "", new GUIStyle(GUI.skin.label) { richText = true });
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
                
                // On utilise la liste pré-filtrée par type !
                foreach (var data in typeFilteredAvailable)
                {
                    bool isQueued = queuedEnchantGuids.Contains(data.Guid);

                    // --- FILTRE DES CATÉGORIES (LOGIQUE 'ET' STRICTE) ---
                    if (!isQueued && activeCategories.Count > 0)
                    {
                        var cleanCats = data.Categories != null ? data.Categories.Where(c => !string.Equals(c, "Discovered", StringComparison.OrdinalIgnoreCase)).ToList() : new List<string>();
                        if (cleanCats.Count == 0) cleanCats.Add("Untyped");

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

                    // --- FILTRE D'OBJET NORMAL ---
                    if (isWeaponOrArmor && !isReadyForSpecial && !isQueued)
                    {
                        if (!CraftingCalculator.IsEnchantmentAllowedOnNormalItem(data)) continue; 
                    }

                    // --- FILTRE RECHERCHE ---
                    var bp = ResourcesLibrary.TryGetBlueprint<BlueprintItemEnchantment>(BlueprintGuid.Parse(data.Guid));
                    string displayName = GetDisplayName(bp, data);
                    if (!string.IsNullOrEmpty(enchantmentSearch) && !displayName.ToLower().Contains(enchantmentSearch.ToLower())) continue;
                    
                    // --- FILTRE SOURCE ---
                    if (CraftingSettings.CurrentSourceFilter == SourceFilter.TTRPG && data.Source != "TTRPG") continue;
                    if (CraftingSettings.CurrentSourceFilter == SourceFilter.Owlcat && data.Source != "TTRPG" && data.Source != "Owlcat") continue;
                    if (CraftingSettings.CurrentSourceFilter == SourceFilter.Mods && (data.Source == "TTRPG" || data.Source == "Owlcat")) continue;
                    
                    long costToPay = CraftingCalculator.GetUpgradeCost(selectedItem, data, CraftingSettings.CostMultiplier);
                    int days = CraftingCalculator.GetCraftingDays(costToPay, CraftingSettings.InstantCrafting);
                    
                    if (data.GoldOverride >= 0) costToPay = (long)(data.GoldOverride * CraftingSettings.CostMultiplier);
                    if (data.DaysOverride >= 0) days = (int)data.DaysOverride;

                    GUILayout.BeginHorizontal(GUI.skin.box);
                    bool newSelected = GUILayout.Toggle(isQueued, $"{displayName}", GUILayout.Width(320 * scale));
                    GUILayout.FlexibleSpace();
                    GUILayout.Label($"{costToPay} po / {days} j   (+{data.PointCost})", GUILayout.Width(220 * scale));
                    
                    if (newSelected && !isQueued) queuedEnchantGuids.Add(data.Guid);
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

            var selectedList = queuedEnchantGuids.Select(g => EnchantmentScanner.GetByGuid(g)).Where(d => d != null).ToList();
            long totalCost = 0;
            int totalDays = 0;
            foreach (var d in selectedList)
            {
                long c = CraftingCalculator.GetUpgradeCost(selectedItem, d, CraftingSettings.CostMultiplier);
                if (d.GoldOverride >= 0) c = (long)(d.GoldOverride * CraftingSettings.CostMultiplier);
                totalCost += c;
                totalDays += CraftingCalculator.GetCraftingDays(c, CraftingSettings.InstantCrafting);
            }

            int currentLevelPoints = CraftingCalculator.CalculateDisplayedEnchantmentPoints(selectedItem);
            int maxLevel = CraftingSettings.MaxTotalBonus;
            int selectedPoints = selectedList.Sum(d => d.PointCost);

            GUILayout.BeginHorizontal();
            GUILayout.Label(string.Format(Helpers.GetString("ui_current_level", "Niveau actuel : {0}/{1}"), currentLevelPoints, maxLevel), GUILayout.ExpandWidth(false));
            GUILayout.FlexibleSpace();
            GUILayout.Label(string.Format(Helpers.GetString("ui_selection_total", "Sélection : +{0} — Total : {1} po / ~{2} j"), selectedPoints, totalCost, totalDays), GUILayout.ExpandWidth(false));
            GUILayout.EndHorizontal();

            GUILayout.Space(6);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(Helpers.GetString("ui_btn_validate_selection", "Valider la sélection"), GUILayout.Width(200 * scale), GUILayout.Height(32 * scale)))
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
                            long c = CraftingCalculator.GetUpgradeCost(selectedItem, d, CraftingSettings.CostMultiplier);
                            if (d.GoldOverride >= 0) c = (long)(d.GoldOverride * CraftingSettings.CostMultiplier);
                            int days = CraftingCalculator.GetCraftingDays(c, CraftingSettings.InstantCrafting);
                            CraftingActions.StartCraftingProject(selectedItem, d, (int)c, days);
                        }

                        try 
                        {
                            string localizedLogBase = Helpers.GetString("log_project_started", "<color=#E2C675>[Atelier]</color> <b>{0}</b> a été envoyé en forge pour <b>{1} po</b>.");
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
                        
                        string fbBase = Helpers.GetString("ui_feedback_projects_started", "Projets lancés : {0} enchantement(s).");
                        feedbackMessage = string.Format(fbBase, selectedList.Count);
                    }
                    else
                    {
                        feedbackMessage = Helpers.GetString("ui_feedback_no_funds", "Erreur inattendue : fonds insuffisants au moment du paiement.");
                    }
                }
            }

            if (GUILayout.Button(Helpers.GetString("ui_btn_cancel_selection", "Annuler la sélection"), GUILayout.Width(180 * scale), GUILayout.Height(32 * scale)))
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
            SourceFilter prevSourceFilter = CraftingSettings.CurrentSourceFilter;

            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label(Helpers.GetString("ui_settings_title", "Réglages de l'Atelier"), new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold });
            
            GUILayout.Space(10);
            
            GUILayout.BeginHorizontal();
            GUILayout.Label(Helpers.GetString("ui_settings_cost_mult", " Multiplicateur de coût : ") + CraftingSettings.CostMultiplier.ToString("F1"), GUILayout.Width(200 * scale));
            CraftingSettings.CostMultiplier = GUILayout.HorizontalSlider(CraftingSettings.CostMultiplier, 0f, 5f, GUILayout.Width(150 * scale));
            GUILayout.EndHorizontal();

            bool previousInstantCrafting = CraftingSettings.InstantCrafting;
            CraftingSettings.InstantCrafting = GUILayout.Toggle(CraftingSettings.InstantCrafting, Helpers.GetString("ui_settings_instant_craft", "Craft Instantané"));

            if (CraftingSettings.InstantCrafting && !previousInstantCrafting)
            {
                try
                {
                    var workshop = Game.Instance.Player.MainCharacter.Value.Get<UnitPartWilcerWorkshop>();
                    if (workshop != null)
                    {
                        workshop.CheckAndFinishProjects();
                        feedbackMessage = Helpers.GetString("ui_feedback_all_forges_done", "Toutes les forges en cours ont été terminées instantanément !");
                    }
                }
                catch (Exception ex) { Main.ModEntry.Logger.Error($"[UI-DEBUG] CRASH : {ex.Message}"); }
            }
            
            GUILayout.Space(10);
            
            CraftingSettings.EnforcePointsLimit = GUILayout.Toggle(CraftingSettings.EnforcePointsLimit, Helpers.GetString("ui_settings_enforce_limit", " Appliquer les limites de bonus (Pathfinder)"));
            
            if (CraftingSettings.EnforcePointsLimit)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(string.Format(Helpers.GetString("ui_settings_max_enhancement", " Max Altération : +{0}"), CraftingSettings.MaxEnhancementBonus), GUILayout.Width(150 * scale));
                CraftingSettings.MaxEnhancementBonus = (int)GUILayout.HorizontalSlider(CraftingSettings.MaxEnhancementBonus, 1, 20, GUILayout.Width(150 * scale));
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label(string.Format(Helpers.GetString("ui_settings_max_total", " Max Total : +{0}"), CraftingSettings.MaxTotalBonus), GUILayout.Width(150 * scale));
                CraftingSettings.MaxTotalBonus = (int)GUILayout.HorizontalSlider(CraftingSettings.MaxTotalBonus, 1, 50, GUILayout.Width(150 * scale));
                GUILayout.EndHorizontal();

                CraftingSettings.RequirePlusOneFirst = GUILayout.Toggle(CraftingSettings.RequirePlusOneFirst, Helpers.GetString("ui_settings_require_plus_one", " Prérequis : +1 Altération minimum"));
            }

            GUILayout.Space(10);
            GUILayout.Label(Helpers.GetString("ui_settings_source_display", "Affichage des sources :"));
            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(CraftingSettings.CurrentSourceFilter == SourceFilter.TTRPG, "TTRPG")) CraftingSettings.CurrentSourceFilter = SourceFilter.TTRPG;
            if (GUILayout.Toggle(CraftingSettings.CurrentSourceFilter == SourceFilter.Owlcat, "Owlcat + TTRPG")) CraftingSettings.CurrentSourceFilter = SourceFilter.Owlcat;
            if (GUILayout.Toggle(CraftingSettings.CurrentSourceFilter == SourceFilter.Mods, "Mods")) CraftingSettings.CurrentSourceFilter = SourceFilter.Mods;
            if (GUILayout.Toggle(CraftingSettings.CurrentSourceFilter == SourceFilter.All, Helpers.GetString("ui_settings_source_all", "Tout"))) CraftingSettings.CurrentSourceFilter = SourceFilter.All;
            GUILayout.EndHorizontal();

            GUILayout.Space(20);
            
            GUILayout.Label(Helpers.GetString("ui_settings_diagnostic", "Outils de diagnostic :"));
            GUILayout.Label(EnchantmentScanner.LastSyncMessage);
            if (GUILayout.Button(Helpers.GetString("ui_settings_force_sync", "Forcer la synchronisation (Scan intégral)"), GUILayout.Height(30 * scale)))
            {
                EnchantmentScanner.ForceSync();
            }
            
            GUILayout.FlexibleSpace();
            GUILayout.EndVertical();

            if (prevCostMult != CraftingSettings.CostMultiplier || prevInstant != CraftingSettings.InstantCrafting || prevEnforce != CraftingSettings.EnforcePointsLimit
                || prevMaxEnh != CraftingSettings.MaxEnhancementBonus || prevMaxTotal != CraftingSettings.MaxTotalBonus || prevRequirePlus != CraftingSettings.RequirePlusOneFirst
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

        private string GetDisplayName(BlueprintItemEnchantment bp, EnchantmentData data)
        {
            if (bp != null && bp.m_EnchantName != null)
            {
                string localized = bp.m_EnchantName.ToString();
                if (!string.IsNullOrWhiteSpace(localized) && localized != bp.name) return localized;
            }

            if (data != null && !string.IsNullOrWhiteSpace(data.Name)) return data.Name;

            if (bp != null)
            {
                return bp.name.Replace("WeaponEnchantment", "")
                              .Replace("ArmorEnchantment", "")
                              .Replace("Enchantment", "")
                              .Replace("Plus", "+");
            }

            return Helpers.GetString("ui_unknown_enchant_name", "Enchantement inconnu");
        }
    }
}