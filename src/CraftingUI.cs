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
using Kingmaker.UI;

namespace CraftingSystem
{
    public class CraftingUI : MonoBehaviour
    {
        public static CraftingUI Instance;
        private bool _isOpen = false;
        public bool IsOpen 
        {
            get => _isOpen;
            set 
            {
                if (_isOpen == value) return;
                _isOpen = value;
                
                // Reset de la navigation quand on ferme ou on ouvre
                if (!_isOpen || _isOpen) 
                {
                    selectedItem = null;
                    ShowSettings = false;
                    newNameDraft = "";
                    enchantmentSearch = "";
                    queuedEnchantGuids.Clear();
                    activeCategories.Clear();
                    activeTypes.Clear();
                    showCategoryFilter = false;
                    scrollPosition = Vector2.zero;
                    titleScrollPosition = Vector2.zero;
                    descriptionScrollPosition = Vector2.zero;
                    activeDescriptionPopup = "";
                    currentPage = 0;
                    filtersDirty = true;
                }

                ToggleHUD(!_isOpen);
            }
        }
        public bool ShowSettings = false;
        
        private Vector2 scrollPosition;
        private Vector2 titleScrollPosition;
        private Vector2 descriptionScrollPosition;
        public string feedbackMessage = "";
        private string newNameDraft = "";
        private string Enchantmentsearch = "";
        private ItemEntity selectedItem = null;
        private bool lastOpenState = false;
        private string activeDescriptionPopup = "";
        private string activeDescriptionTitle = "";
        private bool showAbadarWarning = false;
        private bool showCustomEnchantPage = false;
        private List<EnchantmentData> customEnchantments = new List<EnchantmentData>();
        
        // Paging & Optimization
        private List<EnchantmentData> cachedFilteredEnchantments = new List<EnchantmentData>();
        private int currentPage = 0;
        private int itemsPerPage = 15;
        private bool filtersDirty = true;
        private string lastSearch = "";
        private int lastCategoryCount = -1;
        private int lastTypeCount = -1;
        private ItemEntity lastSelectedItem = null;
        private string pageInput = "";
        
        // Auto-scale reference
        private const float REFERENCE_WIDTH = 2560f;
        private const float REFERENCE_HEIGHT = 1440f;

        // Controller Navigation Variables
        private int currentFocusIndex = 0;
        private int processIndex = 0;
        private int maxFocusIndex = 0;
        private bool inputSubmitDown = false;
        private bool inputCancelDown = false;
        public float RightStickScrollAmount = 0f; // Mis à jour par le patch InGameInputLayer
        private float lastInputTime = 0f;
        private Rect focusedRect = Rect.zero;

        private void Update()
        {
            if (!IsOpen) return;
            // RB (Bumper Droit) est utilisé pour soumettre la sélection au lieu du bouton A
            bool submit = UnityEngine.Input.GetKeyDown(KeyCode.Space) || UnityEngine.Input.GetKeyDown(KeyCode.Return) || UnityEngine.Input.GetKeyDown(KeyCode.JoystickButton5);
            bool cancel = UnityEngine.Input.GetKeyDown(KeyCode.JoystickButton1);
            bool start = UnityEngine.Input.GetKeyDown(KeyCode.Escape) || UnityEngine.Input.GetKeyDown(KeyCode.JoystickButton7);

            if (submit) inputSubmitDown = true;
            if (cancel) inputCancelDown = true;

            float h = 0f; float v = 0f;
            try { 
                h = UnityEngine.Input.GetAxisRaw("Horizontal"); 
                v = UnityEngine.Input.GetAxisRaw("Vertical"); 
            } catch { }

            // L'application du scroll (défilement) continu via le stick droit est géré par la variable exposée RightStickScrollAmount
            if (Mathf.Abs(RightStickScrollAmount) > 0.1f)
            {
                scrollPosition.y -= RightStickScrollAmount * 15f; // Ajuster la vitesse ici
                if (scrollPosition.y < 0) scrollPosition.y = 0;
            }
            // Réinitialiser la magnitude après application pour la frame courante
            RightStickScrollAmount = 0f;

            if (Math.Abs(h) > 0.5f || Math.Abs(v) > 0.5f)
            {
                if (Time.unscaledTime - lastInputTime > 0.2f)
                {
                    int oldIndex = currentFocusIndex;
                    if (v > 0.5f || h < -0.5f) currentFocusIndex--;
                    else if (v < -0.5f || h > 0.5f) currentFocusIndex++;
                    lastInputTime = Time.unscaledTime;

                    if (currentFocusIndex < 0) currentFocusIndex = 0;
                    if (maxFocusIndex > 0 && currentFocusIndex >= maxFocusIndex) currentFocusIndex = maxFocusIndex - 1;
                    
                    if (oldIndex != currentFocusIndex && focusedRect != Rect.zero)
                    {
                        float estimatedViewHeight = REFERENCE_HEIGHT * (CraftingSettings.ScalePercent / 100f) * 0.5f; // Rough estimate of scrollview
                        float padding = 50f * (CraftingSettings.ScalePercent / 100f);
                        
                        if (focusedRect.yMax > scrollPosition.y + estimatedViewHeight) {
                            scrollPosition.y = focusedRect.yMax - estimatedViewHeight + padding;
                        } else if (focusedRect.yMin < scrollPosition.y) {
                            scrollPosition.y = focusedRect.yMin - padding;
                        }
                        if (scrollPosition.y < 0) scrollPosition.y = 0;
                    }
                }
            }
            else
            {
                lastInputTime = 0f;
            }
        }

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
            float finalScale = Mathf.Clamp(resScale * dpiScale * 1.5f, 0.75f, 3.0f);
            CraftingSettings.ScalePercent = (int)(finalScale * 100f);
        }

        
        void OnGUI()
        {
            if (Event.current.type == EventType.Layout) maxFocusIndex = processIndex;
            processIndex = 0;

            if (inputCancelDown) {
                if (!string.IsNullOrEmpty(activeDescriptionPopup)) activeDescriptionPopup = "";
                else if (selectedItem != null) {
                    selectedItem = null;
                    newNameDraft = "";
                    queuedEnchantGuids.Clear();
                    activeCategories.Clear();
                    activeTypes.Clear();
                    showCategoryFilter = false;
                } else if (ShowSettings) { ShowSettings = false; }
                else IsOpen = false;
                inputCancelDown = false;
            }

            if (UnityEngine.Input.GetKeyDown(KeyCode.Escape) || UnityEngine.Input.GetKeyDown(KeyCode.JoystickButton7)) {
                IsOpen = false;
            }

            if (!IsOpen) 
            {
                lastOpenState = false;
                return;
            }

            if (!lastOpenState)
            {
                lastOpenState = true;
            }

            UpdateAutoScale();
            processIndex = 0;
            inputSubmitDown = false;

            Enchantmentscanner.StartSync();

            var workshop = Game.Instance.Player.MainCharacter.Value.Get<UnitPartWilcerWorkshop>();
            workshop?.CheckAndFinishProjects();

            float scale = CraftingSettings.ScalePercent / 100f;
            float width = 1000f * scale; 
            float height = Mathf.Min(900f * scale, Screen.height * 0.9f);
            Rect windowRect = new Rect((Screen.width - width) / 2f, (Screen.height - height) / 2f, width, height);

            if (Event.current != null && !windowRect.Contains(Event.current.mousePosition))
            {
                if (Event.current.type == EventType.MouseDown || Event.current.type == EventType.MouseUp || Event.current.type == EventType.ScrollWheel)
                {
                    Event.current.Use();
                }
            }
            
            Color oldColor = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0, 0, 0, 1.0f); // Totalement opaque
            
            GUI.Box(new Rect(0, 0, Screen.width, Screen.height), "");
            
            // On force l'opacité par un DrawTexture gris derrière la fenêtre
            Color oldGUIColor = GUI.color;
            GUI.color = new Color(0.3f, 0.3f, 0.3f, 1.0f); // Gris principal
            GUI.DrawTexture(windowRect, Texture2D.whiteTexture);
            GUI.color = oldGUIColor;

            GUI.Window(999, windowRect, DrawWindowContent, "");
            DrawBorder(windowRect, 2f, Color.gray);
            
            // --- FENÊTRE DE DESCRIPTION (POPUP) ---
            if (!string.IsNullOrEmpty(activeDescriptionPopup))
            {
                float pW = 650f * scale;
                float pH = 500f * scale;
                Rect popupRect = new Rect((Screen.width - pW) / 2f, (Screen.height - pH) / 2f, pW, pH);
                
                // Force l'opacité de la popup
                GUI.color = new Color(0.3f, 0.3f, 0.3f, 1.0f);
                GUI.DrawTexture(popupRect, Texture2D.whiteTexture);
                GUI.color = oldGUIColor;

                GUI.Window(998, popupRect, DrawDescriptionPopup, "");
                DrawBorder(popupRect, 2f, Color.gray);
                GUI.BringWindowToFront(998);
            }
            
            GUI.backgroundColor = oldColor;

            GUI.FocusWindow(string.IsNullOrEmpty(activeDescriptionPopup) ? 999 : 998);
        }

        void DrawWindowContent(int windowID)
        {
            float scale = CraftingSettings.ScalePercent / 100f;
            
            // Force l'opacité interne de la fenêtre principale
            Color oldColor = GUI.color;
            GUI.color = new Color(0.3f, 0.3f, 0.3f, 1.0f);
            GUI.DrawTexture(new Rect(0, 0, 1000f * scale, Mathf.Min(900f * scale, Screen.height * 0.9f)), Texture2D.whiteTexture);
            GUI.color = oldColor;

            if (!string.IsNullOrEmpty(feedbackMessage))
            {
                GUILayout.Label(feedbackMessage, new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = (int)(18 * scale) });
                if (CButton(Helpers.GetString("ui_btn_ok", "OK"), GUILayout.Height(40 * scale))) feedbackMessage = "";
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

            GUIStyle navStyle = new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold, fontSize = (int)(14 * scale) };

            if (CButtonStyled(new GUIContent(Helpers.GetString("ui_btn_info", "Information")), navStyle, GUILayout.Width(130 * scale), GUILayout.Height(35 * scale)))
            {
                activeDescriptionTitle = Helpers.GetString("ui_info_title", "Crafting Rules");
                activeDescriptionPopup = Helpers.GetString("ui_info_text");
            }
            
            if (selectedItem != null && !ShowSettings)
            {
                if (CButtonStyled(new GUIContent(Helpers.GetString("ui_btn_custom_enchants", "Custom Enchantments")), navStyle, GUILayout.Width(180 * scale), GUILayout.Height(35 * scale)))
                {
                    showCustomEnchantPage = !showCustomEnchantPage;
                    if (showCustomEnchantPage)
                    {
                        // Charger les enchants custom
                        customEnchantments = Enchantmentscanner.MasterList
                            .Where(e => CustomEnchantmentsBuilder.InjectedGuids.Contains(BlueprintGuid.Parse(e.Guid)))
                            .ToList();
                    }
                }

                if (CButtonStyled(new GUIContent(Helpers.GetString("ui_btn_back", "<< BACK")), navStyle, GUILayout.Width(130 * scale), GUILayout.Height(35 * scale))) 
                {
                    selectedItem = null;
                    showCustomEnchantPage = false;
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

            string optLabel = ShowSettings ? Helpers.GetString("ui_btn_workshop_short", "Workshop") : Helpers.GetString("ui_btn_cheats", "Options");
            if (CButtonStyled(new GUIContent(optLabel), navStyle, GUILayout.Width(optionWidth), GUILayout.Height(35 * scale)))
            {
                if (!ShowSettings && !CraftingSettings.HasOpenedCheats)
                {
                    showAbadarWarning = true;
                }
                else
                {
                    ShowSettings = !ShowSettings;
                    showAbadarWarning = false; // Reset if toggling back
                }
            }
            
            if (CButton(Helpers.GetString("ui_btn_close_x", "X"), GUILayout.Width(closeWidth), GUILayout.Height(35 * scale))) IsOpen = false;
            GUILayout.EndHorizontal();

            GUILayout.Space(10);
            
            if (showAbadarWarning) DrawAbadarWarningGUI(scale);
            else if (ShowSettings) DrawSettingsGUI(scale);
            else if (showCustomEnchantPage) DrawCustomEnchantmentGUI(scale);
            else if (selectedItem != null) DrawItemModificationGUI(scale);
            else DrawInventoryGUI(scale);
        }

        void DrawInventoryGUI(float scale)
        {
            scrollPosition = GUILayout.BeginScrollView(scrollPosition, GUILayout.ExpandHeight(true));
            var workshop = Game.Instance.Player.MainCharacter.Value.Get<UnitPartWilcerWorkshop>();
            var items = workshop?.StashedItems ?? new List<ItemEntity>();

            if (!items.Any()) 
            {
                GUIStyle emptyStyle = new GUIStyle(GUI.skin.label) { 
                    fontSize = (int)(14 * scale), 
                    alignment = TextAnchor.MiddleCenter,
                    richText = true
                };
                GUILayout.Label(Helpers.GetString("ui_no_item_stashed", "\n   (No item is stored in the workshop)"), emptyStyle);
            }
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

                        if (CButtonStyled(new GUIContent(label), entryStyle, GUILayout.Width(350 * scale), GUILayout.Height(50 * scale))) 
                        {
                            selectedItem = it;
                            newNameDraft = it.Name;
                            queuedEnchantGuids.Clear();
                            activeCategories.Clear();
                            showCategoryFilter = false;
                            
                            activeTypes.Clear();
                            if (it.Blueprint is BlueprintItemWeapon) activeTypes.Add("Weapon");
                            else if (it.Blueprint is BlueprintItemArmor) activeTypes.Add("Armor");
                            else activeTypes.Add("Other");

                            filtersDirty = true;
                            currentPage = 0;
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
                if (CButton(Helpers.GetString("ui_btn_close_ui", "Close Interface"), GUILayout.Height(40 * scale))) IsOpen = false;
                GUILayout.EndVertical();
                return;
            }

            scrollPosition = GUILayout.BeginScrollView(scrollPosition, false, true, GUILayout.ExpandHeight(true));
            
            // --- SECTION RENOMMAGE ---
            GUILayout.Label(Helpers.GetString("ui_special_action_rename", "Special Action: Rename item (Free)"), new GUIStyle(GUI.skin.label) { fontSize = (int)(14 * scale) });
            GUILayout.BeginHorizontal();
            
            float windowWidth = 800f * scale;
            float buttonsSpace = (120f + 100f + 25f) * scale;
            float padding = (45f + 20f) * scale; 
            float exactTextWidth = windowWidth - buttonsSpace - padding;

            GUIStyle textFieldStyle = new GUIStyle(GUI.skin.textField);
            textFieldStyle.wordWrap = false; 
            textFieldStyle.fontSize = (int)(14 * scale);

            newNameDraft = CTextFieldStyled(newNameDraft, textFieldStyle, GUILayout.Width(exactTextWidth), GUILayout.Height(35 * scale));
            
            GUILayout.Space(10 * scale);
            
            if (CButton(Helpers.GetString("ui_btn_rename", "Renommer"), GUILayout.Width(120 * scale), GUILayout.Height(35 * scale)))
            {
                ItemRenamer.RenameItem(selectedItem, newNameDraft);
                feedbackMessage = Helpers.GetString("ui_feedback_renamed", "The item has been renamed!");
            }

            if (selectedItem != null && CButton(Helpers.GetString("ui_btn_auto", "Auto"), GUILayout.Width(100 * scale), GUILayout.Height(35 * scale)))
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
            
            // --- GESTION DU REFILTRAGE ---
            bool selectionChanged = filtersDirty;
            if (selectedItem != lastSelectedItem || Enchantmentsearch != lastSearch || activeCategories.Count != lastCategoryCount || activeTypes.Count != lastTypeCount || selectionChanged)
            {
                // On détecte si c'est UNIQUEMENT la sélection qui a changé
                bool filtersUnchanged = (selectedItem == lastSelectedItem && Enchantmentsearch == lastSearch && activeCategories.Count == lastCategoryCount && activeTypes.Count == lastTypeCount);
                
                lastSelectedItem = selectedItem;
                lastSearch = Enchantmentsearch;
                lastCategoryCount = activeCategories.Count;
                lastTypeCount = activeTypes.Count;
                filtersDirty = false;

                RebuildFilteredList();
                
                // On ne reset la page que si les filtres (recherche, catégories, types) ont changé.
                // Si c'est juste une coche/décoche, on reste sur la même page pour éviter de perdre sa place.
                if (!filtersUnchanged) currentPage = 0;
            }
            
            
            // --- SECTION : ENCHANTEMENTS DÉJÀ PRÉSENTS ---
            GUILayout.Label(Helpers.GetString("ui_applied_enchants", "Applied Enchantments:"), new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = (int)(14 * scale) });
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
                    var overrideData = Enchantmentscanner.GetByGuid(guid);
                    int pointValue = overrideData?.PointCost ?? ench.Blueprint.EnchantmentCost;
                    if (pointValue < 0) pointValue = 0;
                    GUILayout.BeginHorizontal(GUI.skin.box);
                    string displayName = DescriptionManager.GetDisplayName(ench.Blueprint, overrideData);
                    GUILayout.Label($"{displayName} (+{pointValue})", new GUIStyle(GUI.skin.label) { fontSize = (int)(14 * scale) }, GUILayout.ExpandWidth(true));

                    // POUR LES OBJETS DÉJÀ APPLIQUÉS (RÉSOLU DYNAMIQUEMENT)
                    DescriptionSource genSource = DescriptionSource.None;
                    string appliedDesc = DescriptionManager.GetLocalizedDescription(ench.Blueprint, overrideData, out genSource);
                    if (!string.IsNullOrEmpty(appliedDesc))
                    {
                        string color = "#3498db"; // Bleu (Official) par défaut
                        if (genSource == DescriptionSource.Generated) color = "#f1c40f"; // Jaune
                        else if (genSource == DescriptionSource.None) color = "#e74c3c"; // Rouge

                        GUIContent infoContent = new GUIContent($"<color={color}>?</color>");
                        GUIStyle infoStyle = new GUIStyle(GUI.skin.button) { 
                            richText = true, 
                            fontStyle = FontStyle.Bold, 
                            fontSize = (int)(9 * scale),
                            padding = new RectOffset(0, 0, 0, 0)
                        };
                        if (CButtonStyled(infoContent, infoStyle, GUILayout.Width(15 * scale), GUILayout.Height(15 * scale))) 
                        {
                            activeDescriptionTitle = displayName;
                            activeDescriptionPopup = appliedDesc;
                        }
                    }
                    else
                    {
                        GUILayout.Space(25 * scale);
                    }
                    if (CButton(Helpers.GetString("ui_btn_remove", "Remove"), GUILayout.Width(100 * scale), GUILayout.Height(30 * scale)))
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
            GUILayout.Label(Helpers.GetString("ui_available_enchants", "Available Enchantments:"), new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = (int)(14 * scale) });

            // -- UI FILTRE DES TYPES --
            GUILayout.BeginHorizontal();
            bool isWep = CToggle(activeTypes.Contains("Weapon"), Helpers.GetString("ui_filter_weapons", " Weapons"), GUILayout.Width(150 * scale));
            bool isArm = CToggle(activeTypes.Contains("Armor"), Helpers.GetString("ui_filter_armors", " Armors"), GUILayout.Width(150 * scale));
            bool isOth = CToggle(activeTypes.Contains("Other"), Helpers.GetString("ui_filter_others", " Others"), GUILayout.Width(150 * scale));


            if (isWep) activeTypes.Add("Weapon"); else activeTypes.Remove("Weapon");
            if (isArm) activeTypes.Add("Armor"); else activeTypes.Remove("Armor");
            if (isOth) activeTypes.Add("Other"); else activeTypes.Remove("Other");
            GUILayout.EndHorizontal();
            
            GUILayout.Space(10);

            // On récupère TOUTE la liste directement, pour laisser l'UI gérer les types
            List<EnchantmentData> rawAvailable;
            lock (Enchantmentscanner.MasterList)
            {
                rawAvailable = Enchantmentscanner.MasterList.ToList();
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
            Enchantmentsearch = CTextField(Enchantmentsearch, GUILayout.ExpandWidth(true));
            
            string filterBtnText = activeCategories.Count > 0 ? string.Format(Helpers.GetString("ui_filter_active_btn", "Filters ({0}) \u25bc"), activeCategories.Count) : Helpers.GetString("ui_filter_all_btn", "Filters (All) \u25bc");
            if (CButton(filterBtnText, GUILayout.Width(130 * scale))) showCategoryFilter = !showCategoryFilter;
            GUILayout.EndHorizontal();

            if (showCategoryFilter)
            {
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.BeginHorizontal();
                if (CButton(Helpers.GetString("ui_filter_check_all", "Check All"), GUILayout.Width(120 * scale))) foreach (var c in allCategoriesList) activeCategories.Add(c);
                if (CButton(Helpers.GetString("ui_filter_uncheck_all", "Uncheck All"), GUILayout.Width(120 * scale))) activeCategories.Clear();
                GUILayout.Label(activeCategories.Count == 0 ? Helpers.GetString("ui_filter_none_active", " <i>(No filter active = Show all)</i>") : "", new GUIStyle(GUI.skin.label) { richText = true });
                GUILayout.EndHorizontal();
                GUILayout.Space(5);

                int cols = 3;
                int currentCol = 0;
                GUILayout.BeginHorizontal();
                foreach (var cat in allCategoriesList)
                {
                    bool isActive = activeCategories.Contains(cat);
                    bool toggled = CToggle(isActive, cat, GUILayout.Width(240 * scale));
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

            if (Enchantmentscanner.IsSyncing)
            {
                GUILayout.Label($"({Enchantmentscanner.LastSyncMessage})", new GUIStyle(GUI.skin.label) { richText = true, fontSize = (int)(12 * scale), alignment = TextAnchor.MiddleCenter });
            }

            GUILayout.Space(5);

            GUILayout.BeginVertical(GUI.skin.box);

            if (Enchantmentscanner.IsSyncing)
            {
                GUILayout.Label(Helpers.GetString("ui_scan_in_progress", "Scan en cours — aucun enchantement disponible pour l'instant."), new GUIStyle(GUI.skin.label) { fontSize = (int)(12 * scale), alignment = TextAnchor.MiddleCenter });
            }
            else
            {
                // -- NAVIGATION DE PAGINATION --
                // -- DYNAMIC PAGINATION CALCULATION --
                float currentWindowHeight = Mathf.Min(900f * scale, Screen.height * 0.9f);
                // Estimation très agressive des éléments fixes (Header, Search, Pagination, Bottom) pour UHD
                float fixedHeight = 120f * scale; 
                float availableHeight = currentWindowHeight - fixedHeight;
                float itemHeight = 26f * scale; // Hauteur minimale d'une ligne d'enchantement
                
                int calculatedItemsPerPage = Mathf.Max(1, Mathf.FloorToInt(availableHeight / itemHeight));
                if (calculatedItemsPerPage != itemsPerPage)
                {
                    itemsPerPage = calculatedItemsPerPage;
                    filtersDirty = true; 
                }

                int totalItems = cachedFilteredEnchantments.Count;
                int totalPages = Mathf.Max(1, Mathf.CeilToInt((float)totalItems / itemsPerPage));
                if (currentPage >= totalPages) currentPage = totalPages - 1;

                GUILayout.BeginHorizontal();
                GUI.enabled = (currentPage > 0);
                if (CButton(Helpers.GetString("ui_btn_prev", "<<"), GUILayout.Width(50 * scale))) currentPage--;
                GUI.enabled = true;
                
                GUILayout.FlexibleSpace();
                
                // -- ALLER À LA PAGE --
                GUIStyle paginationStyle = new GUIStyle(GUI.skin.label) { fontSize = (int)(12 * scale), alignment = TextAnchor.MiddleLeft };
                GUILayout.Label(Helpers.GetString("ui_pagination_goto", "Aller à : "), paginationStyle, GUILayout.Width(70 * scale));
                
                GUIStyle pageInputStyle = new GUIStyle(GUI.skin.textField) { fontSize = (int)(12 * scale), alignment = TextAnchor.MiddleCenter };
                pageInput = CTextFieldStyled(pageInput, pageInputStyle, GUILayout.Width(50 * scale), GUILayout.Height(25 * scale));
                if (CButton(Helpers.GetString("ui_btn_go", "Go"), GUILayout.Width(40 * scale)))
                {
                    if (int.TryParse(pageInput, out int p))
                    {
                        currentPage = Mathf.Clamp(p - 1, 0, totalPages - 1);
                        pageInput = (currentPage + 1).ToString();
                    }
                }
                
                GUILayout.FlexibleSpace();
                GUIStyle pageInfoStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = (int)(12 * scale) };
                GUILayout.Label(string.Format(Helpers.GetString("ui_pagination_info", "Page {0} / {1} ({2} items)"), currentPage + 1, totalPages, totalItems), pageInfoStyle);
                GUILayout.FlexibleSpace();
                
                GUI.enabled = (currentPage < totalPages - 1);
                if (CButton(Helpers.GetString("ui_btn_next", ">>"), GUILayout.Width(50 * scale))) currentPage++;
                GUI.enabled = true;
                GUILayout.EndHorizontal();

                GUILayout.Space(5);

                // -- EN-TÊTES DU TABLEAU --
                GUILayout.BeginHorizontal();
                GUIStyle headerStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = (int)(12 * scale) };
                GUILayout.Label(Helpers.GetString("ui_header_name", "Name"), headerStyle, GUILayout.ExpandWidth(true));
                GUILayout.Space(100 * scale); // Espace pour le bouton description (non titré)
                GUILayout.Label(Helpers.GetString("ui_header_slot_affinity", "Slot Affinity"), headerStyle, GUILayout.Width(120 * scale));
                GUILayout.Label(Helpers.GetString("ui_header_cost", "Cost / Time"), headerStyle, GUILayout.Width(180 * scale));
                GUILayout.EndHorizontal();

                var currentSelectedList = queuedEnchantGuids.Select(g => Enchantmentscanner.GetByGuid(g)).Where(d => d != null).ToList();
                
                // On ne boucle QUE sur les éléments de la page actuelle
                int startIdx = currentPage * itemsPerPage;
                int endIdx = Mathf.Min(startIdx + itemsPerPage, cachedFilteredEnchantments.Count);

                for (int i = startIdx; i < endIdx; i++)
                {
                    var data = cachedFilteredEnchantments[i];
                    DrawEnchantmentRow(data, scale, i - startIdx);
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
                var d = Enchantmentscanner.GetByGuid(g);
                if (d != null)
                {
                    selectedList.Add(d);
                }
                /*
                else if (UnityEngine.Time.frameCount % 60 == 0)
                {
                    Main.ModEntry.Logger.Warning($"[PANIER-DEBUG] Guid={g} -> NON TROUVÉ DANS LA MASTERLIST");
                }
                */
            }


            long totalCost = CraftingCalculator.GetMarginalCost(selectedItem, selectedList, null, CraftingSettings.CostMultiplier);
            int totalDays = CraftingCalculator.GetCraftingDays(totalCost, CraftingSettings.InstantCrafting);

            int currentLevelPoints = CraftingCalculator.CalculateDisplayedEnchantmentPoints(selectedItem);
            int maxLevel = CraftingSettings.MaxTotalBonus;
            int selectedPoints = selectedList.Sum(d => d.PointCost);

            GUILayout.BeginHorizontal();
            GUILayout.Label(string.Format(Helpers.GetString("ui_current_level", "Current level: {0}/{1}"), currentLevelPoints, maxLevel), new GUIStyle(GUI.skin.label) { fontSize = (int)(14 * scale) }, GUILayout.ExpandWidth(false));
            GUILayout.FlexibleSpace();
            GUILayout.Label(string.Format(Helpers.GetString("ui_selection_total", "Selection: +{0} \u2014 Total: {1} gp / ~{2} d"), selectedPoints, totalCost, totalDays), new GUIStyle(GUI.skin.label) { fontSize = (int)(14 * scale) }, GUILayout.ExpandWidth(false));
            GUILayout.EndHorizontal();

            GUILayout.Space(6);

            GUILayout.BeginHorizontal();
            if (CButton(Helpers.GetString("ui_btn_validate_selection", "Confirm Selection"), GUILayout.Width(250 * scale), GUILayout.Height(40 * scale)))
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
                        filtersDirty = true;
                    }
                    else
                    {
                        feedbackMessage = Helpers.GetString("ui_feedback_no_funds", "Unexpected error: insufficient funds at the time of payment.");
                    }
                }
            }

            if (CButton(Helpers.GetString("ui_btn_cancel_selection", "Cancel Selection"), GUILayout.Width(220 * scale), GUILayout.Height(40 * scale)))
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

            Color oldBG = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.4f, 0.4f, 0.4f, 1.0f); // Sous-menu plus clair
            GUILayout.BeginVertical(GUI.skin.box);
            GUI.backgroundColor = oldBG;
            GUILayout.Label(Helpers.GetString("ui_settings_title", "Workshop Settings"), new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = (int)(16 * scale) });
            
            GUILayout.Space(10);
            
            GUIStyle settingsLabelStyle = new GUIStyle(GUI.skin.label) { fontSize = (int)(14 * scale) };

            GUILayout.BeginHorizontal();
            GUILayout.Label(Helpers.GetString("ui_settings_cost_mult", " Cost Multiplier: ") + CraftingSettings.CostMultiplier.ToString("F1"), settingsLabelStyle, GUILayout.Width(200 * scale));
            CraftingSettings.CostMultiplier = (float)Math.Round(GUILayout.HorizontalSlider(CraftingSettings.CostMultiplier, 0f, 5f, GUILayout.Width(150 * scale)), 1);
            GUILayout.EndHorizontal();

            bool previousInstantCrafting = CraftingSettings.InstantCrafting;
            CraftingSettings.InstantCrafting = CToggle(CraftingSettings.InstantCrafting, Helpers.GetString("ui_settings_instant_craft", "Instant Crafting"));

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
            
            CraftingSettings.EnforcePointsLimit = CToggle(CraftingSettings.EnforcePointsLimit, Helpers.GetString("ui_settings_enforce_limit", " Enforce Bonus Limits (Pathfinder)"));
            
            if (CraftingSettings.EnforcePointsLimit)
            {
                GUILayout.Space(5);
                CraftingSettings.RequirePlusOneFirst = CToggle(CraftingSettings.RequirePlusOneFirst, Helpers.GetString("ui_settings_require_plus_one", " Prerequisite: At least +1 Enhancement"));
                GUILayout.Space(5);
                CraftingSettings.ApplySlotPenalty = CToggle(CraftingSettings.ApplySlotPenalty, Helpers.GetString("ui_settings_slot_penalty", " Apply Slot Penalty (x1.5)"));
                GUILayout.Space(5);
                CraftingSettings.EnableEpicCosts = CToggle(CraftingSettings.EnableEpicCosts, Helpers.GetString("ui_settings_enable_epic", " Enable Epic Multiplier (x10)"));

                GUILayout.Space(8); // Un peu plus d'espace avant les sliders
                GUILayout.BeginHorizontal();
                GUILayout.Label(string.Format(Helpers.GetString("ui_settings_max_enhancement", " Max Enhancement: +{0}"), CraftingSettings.MaxEnhancementBonus), settingsLabelStyle, GUILayout.Width(150 * scale));
                if (CButton("-", GUILayout.Width(30 * scale))) CraftingSettings.MaxEnhancementBonus--;
                CraftingSettings.MaxEnhancementBonus = (int)GUILayout.HorizontalSlider(CraftingSettings.MaxEnhancementBonus, 1, 20, GUILayout.Width(90 * scale));
                if (CButton("+", GUILayout.Width(30 * scale))) CraftingSettings.MaxEnhancementBonus++;
                GUILayout.EndHorizontal();

                GUILayout.Space(5);
                GUILayout.BeginHorizontal();
                GUILayout.Label(string.Format(Helpers.GetString("ui_settings_max_total", " Max Total: +{0}"), CraftingSettings.MaxTotalBonus), settingsLabelStyle, GUILayout.Width(150 * scale));
                if (CButton("-", GUILayout.Width(30 * scale))) CraftingSettings.MaxTotalBonus--;
                CraftingSettings.MaxTotalBonus = (int)GUILayout.HorizontalSlider(CraftingSettings.MaxTotalBonus, 1, 50, GUILayout.Width(90 * scale));
                if (CButton("+", GUILayout.Width(30 * scale))) CraftingSettings.MaxTotalBonus++;
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(10);
            GUILayout.Label(Helpers.GetString("ui_settings_source_display", "Source display:"), settingsLabelStyle);
            GUILayout.BeginHorizontal();
            int sliderVal = (int)CraftingSettings.CurrentSourceFilter;
            if (CButton("<", GUILayout.Width(30 * scale))) sliderVal--;
            sliderVal = Mathf.RoundToInt(GUILayout.HorizontalSlider(sliderVal, 0, 4, GUILayout.Width(240 * scale)));
            if (CButton(">", GUILayout.Width(30 * scale))) sliderVal++;
            sliderVal = Mathf.Clamp(sliderVal, 0, 4);
            CraftingSettings.CurrentSourceFilter = (SourceFilter)sliderVal;
            GUILayout.Space(20 * scale);
            
            string sourceLabel = "";
            switch (CraftingSettings.CurrentSourceFilter)
            {
                case SourceFilter.TTRPG: sourceLabel = Helpers.GetString("ui_settings_source_ttrpg", "TTRPG (TTRPG Enchantments only)"); break;
                case SourceFilter.Owlcat: sourceLabel = Helpers.GetString("ui_settings_source_owlcat", "Owlcat (TTRPG + Owlcat)"); break;
                case SourceFilter.OwlcatPlus: sourceLabel = Helpers.GetString("ui_settings_source_owlcatplus", "Owlcat+ (TTRPG + Owlcat + Owlcat+)"); break;
                case SourceFilter.Mods: sourceLabel = Helpers.GetString("ui_settings_source_mods", "Mod (All non-base game Enchantments)"); break;
                case SourceFilter.All: sourceLabel = Helpers.GetString("ui_settings_source_all_desc", "Show all"); break;
            }
            GUILayout.Label(sourceLabel, new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Italic, fontSize = (int)(14 * scale) });
            GUILayout.EndHorizontal();

            GUILayout.Space(20);
            
            GUILayout.Label(Helpers.GetString("ui_settings_diagnostic", "Diagnostic Tools:"), settingsLabelStyle);
            GUILayout.Label(Enchantmentscanner.LastSyncMessage, settingsLabelStyle);
            if (CButton(Helpers.GetString("ui_settings_force_sync", "Force Synchronization (Full Scan)"), GUILayout.Height(35 * scale)))
            {
                Enchantmentscanner.ForceSync();
            }
            
            GUILayout.FlexibleSpace();
            GUILayout.EndVertical();

            if (prevCostMult != CraftingSettings.CostMultiplier || prevInstant != CraftingSettings.InstantCrafting || prevEnforce != CraftingSettings.EnforcePointsLimit
                || prevMaxEnh != CraftingSettings.MaxEnhancementBonus || prevMaxTotal != CraftingSettings.MaxTotalBonus || prevRequirePlus != CraftingSettings.RequirePlusOneFirst
                || prevSlotPenalty != CraftingSettings.ApplySlotPenalty || prevEnableEpic != CraftingSettings.EnableEpicCosts
                || prevSourceFilter != CraftingSettings.CurrentSourceFilter)
            {
                CraftingSettings.SaveSettings();
                filtersDirty = true;
            }
        }

        private void RebuildFilteredList()
        {
            if (selectedItem == null) 
            {
                cachedFilteredEnchantments.Clear();
                return;
            }

            List<EnchantmentData> rawAvailable;
            lock (Enchantmentscanner.MasterList)
            {
                rawAvailable = Enchantmentscanner.MasterList.ToList();
            }

            // 1. Filtrage par Type
            var typeFiltered = rawAvailable.Where(d => activeTypes.Contains(d.Type) || queuedEnchantGuids.Contains(d.Guid)).ToList();

            // 2. Préparation des helpers de filtrage
            bool isWeaponOrArmor = selectedItem.Blueprint is BlueprintItemWeapon || selectedItem.Blueprint is BlueprintItemArmor;
            var currentSelectedList = queuedEnchantGuids.Select(g => Enchantmentscanner.GetByGuid(g)).Where(d => d != null).ToList();
            bool isReadyForSpecial = CraftingCalculator.IsItemReadyForSpecialEnchants(selectedItem, currentSelectedList);
            var presentGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var presentNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in selectedItem.Enchantments)
            {
                if (e.IsTemporary || e.Blueprint == null) continue;
                presentGuids.Add(e.Blueprint.AssetGuid.ToString());
                presentNames.Add(e.Blueprint.name);
            }

            var result = new List<EnchantmentData>();

            foreach (var data in typeFiltered)
            {
                bool isQueued = queuedEnchantGuids.Contains(data.Guid);
                var bp = ResourcesLibrary.TryGetBlueprint<BlueprintItemEnchantment>(BlueprintGuid.Parse(data.Guid));
                if (bp == null) continue;

                // --- FILTRE D'ENCHANTEMENTS DÉJÀ PRÉSENTS ---
                // On compare le GUID (normalisé via le Blueprint) ET le nom interne
                if (!isQueued && (presentGuids.Contains(bp.AssetGuid.ToString()) || presentNames.Contains(bp.name))) continue;

                // --- FILTRE DES CATÉGORIES ---
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

                // --- FILTRE D'OBJET NORMAL (+1) ---
                if (CraftingSettings.RequirePlusOneFirst && isWeaponOrArmor && !isReadyForSpecial && !isQueued)
                {
                    if (!CraftingCalculator.IsEnchantmentAllowedOnNormalItem(data)) continue; 
                }

                // --- FILTRE RECHERCHE ---
                string displayName = DescriptionManager.GetDisplayName(bp, data);
                if (!string.IsNullOrEmpty(lastSearch) && !displayName.ToLower().Contains(lastSearch.ToLower())) continue;

                // --- FILTRE SOURCE ---
                if (CraftingSettings.CurrentSourceFilter == SourceFilter.TTRPG && data.Source != "TTRPG") continue;
                if (CraftingSettings.CurrentSourceFilter == SourceFilter.Owlcat && data.Source != "TTRPG" && data.Source != "Owlcat") continue;
                if (CraftingSettings.CurrentSourceFilter == SourceFilter.OwlcatPlus && data.Source != "TTRPG" && data.Source != "Owlcat" && data.Source != "Owlcat+") continue;
                if (CraftingSettings.CurrentSourceFilter == SourceFilter.Mods && (data.Source == "TTRPG" || data.Source == "Owlcat" || data.Source == "Owlcat+")) continue;

                result.Add(data);
            }

            // 3. Tri (Alphabétique uniquement pour garder une liste stable et éviter que les items ne sautent)
            cachedFilteredEnchantments = result
                .OrderBy(e => e.Name)
                .ToList();
        }

        private void Div(float scale)
        {
            GUILayout.Space(5 * scale);
            GUIStyle lineStyle = new GUIStyle(GUI.skin.box);
            lineStyle.border = new RectOffset(0, 0, 0, 0);
            lineStyle.margin = new RectOffset(0, 0, 0, 0);
            lineStyle.padding = new RectOffset(0, 0, 0, 0);
            GUILayout.Box("", lineStyle, GUILayout.Height(1 * scale), GUILayout.ExpandWidth(true));
            GUILayout.Space(5 * scale);
        }


        private void DrawDescriptionPopup(int windowID)
        {
            float scale = CraftingSettings.ScalePercent / 100f;
            
            // Force l'opacité interne
            Color oldColor = GUI.color;
            GUI.color = new Color(0.3f, 0.3f, 0.3f, 1.0f);
            GUI.DrawTexture(new Rect(0, 0, 650f * scale, 500f * scale), Texture2D.whiteTexture);
            GUI.color = oldColor;

            GUILayout.BeginVertical();
            
            GUILayout.BeginHorizontal();
            GUILayout.Label(activeDescriptionTitle, new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = (int)(16 * scale) });
            GUILayout.FlexibleSpace();
            if (CButton(Helpers.GetString("ui_btn_close_x", "X"), GUILayout.Width(30 * scale))) activeDescriptionPopup = "";
            GUILayout.EndHorizontal();
            
            GUILayout.Space(10);
            
            descriptionScrollPosition = GUILayout.BeginScrollView(descriptionScrollPosition, GUI.skin.box);
            GUILayout.Label(activeDescriptionPopup, new GUIStyle(GUI.skin.label) { wordWrap = true, richText = true, fontSize = (int)(14 * scale) });
            GUILayout.EndScrollView();
            
            GUILayout.Space(10);
            if (CButton(Helpers.GetString("ui_btn_ok", "OK"), GUILayout.Height(40 * scale))) activeDescriptionPopup = "";
            
            GUILayout.EndVertical();
            GUI.DragWindow();
        }
    
        // ======================= CONTROLLER WRAPPERS =======================
        private void DrawAbadarWarningGUI(float scale)
        {
            Color oldBG = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.3f, 0.3f, 0.3f, 1.0f);
            GUILayout.BeginVertical(GUI.skin.box);
            GUI.backgroundColor = oldBG;

            GUILayout.Space(20);
            GUIStyle warningStyle = new GUIStyle(GUI.skin.label) {
                fontSize = (int)(16 * scale),
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true
            };
            
            GUILayout.Label(Helpers.GetString("ui_abadar_warning", "Abadar, dieu des lois, du commerce et des cités, vous regarde avec sévérité.\n\nÊtes-vous certain de vouloir accéder à ce menu ?"), warningStyle);
            
            GUILayout.Space(40);
            
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            
            if (CButton(Helpers.GetString("ui_btn_yes", "Oui"), GUILayout.Width(150 * scale), GUILayout.Height(40 * scale)))
            {
                CraftingSettings.HasOpenedCheats = true;
                CraftingSettings.SaveSettings();
                showAbadarWarning = false;
                ShowSettings = true;
            }
            
            GUILayout.Space(50);
            
            if (CButton(Helpers.GetString("ui_btn_no", "Non"), GUILayout.Width(150 * scale), GUILayout.Height(40 * scale)))
            {
                showAbadarWarning = false;
                ShowSettings = false;
            }
            
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            
            GUILayout.Space(20);
            GUILayout.EndVertical();
        }

        private bool CButton(string text, params GUILayoutOption[] options) 
        { 
            float scale = CraftingSettings.ScalePercent / 100f;
            GUIStyle style = new GUIStyle(GUI.skin.button) { fontSize = (int)(14 * scale) };
            return CButtonStyled(new GUIContent(text), style, options); 
        }
        private bool CButtonStyled(GUIContent content, GUIStyle style, params GUILayoutOption[] options)
        {
            int index = processIndex++;
            bool isFocused = (index == currentFocusIndex);
            Color oldColor = GUI.color;
            if (isFocused) GUI.color = Color.yellow;
            
            bool clicked = false;
            if (style != null) clicked = GUILayout.Button(content, style, options);
            else clicked = GUILayout.Button(content, options);
            
            if (isFocused && Event.current.type == EventType.Repaint) focusedRect = GUILayoutUtility.GetLastRect();
            
            GUI.color = oldColor;
            
            if (isFocused && inputSubmitDown) {
                clicked = true;
                inputSubmitDown = false;
            }
            return clicked;
        }

        private bool CToggle(bool value, string text, params GUILayoutOption[] options) 
        { 
            float scale = CraftingSettings.ScalePercent / 100f;
            GUIStyle style = new GUIStyle(GUI.skin.label) { 
                fontSize = (int)(14 * scale),
                alignment = TextAnchor.MiddleLeft
            };
            return CToggleStyled(value, text, style, options); 
        }
        private bool CToggleStyled(bool value, string text, GUIStyle style, params GUILayoutOption[] options)
        {
            int index = processIndex++;
            bool isFocused = (index == currentFocusIndex);
            float scale = CraftingSettings.ScalePercent / 100f;
            float boxSize = 18f * scale;

            GUILayout.BeginHorizontal(options);
            
            // -- CASE À COCHER --
            GUIStyle boxStyle = new GUIStyle(GUI.skin.button) {
                fontSize = (int)(16 * scale),
                padding = new RectOffset(0, 0, 0, 0),
                alignment = TextAnchor.MiddleCenter
            };
            
            Color oldColor = GUI.color;
            if (isFocused) GUI.color = Color.yellow;

            bool newValue = value;
            if (GUILayout.Button(value ? "✔" : "", boxStyle, GUILayout.Width(boxSize), GUILayout.Height(boxSize)))
            {
                newValue = !value;
            }
            
            GUILayout.Space(8 * scale);
            
            // -- TEXTE --
            GUIStyle textStyle = new GUIStyle(style ?? GUI.skin.label);
            if (isFocused) textStyle.normal.textColor = Color.yellow;

            if (GUILayout.Button(text, textStyle, GUILayout.Height(boxSize)))
            {
                newValue = !value;
            }
            
            if (isFocused && Event.current.type == EventType.Repaint) focusedRect = GUILayoutUtility.GetLastRect();
            
            if (isFocused && inputSubmitDown) {
                newValue = !value;
                inputSubmitDown = false;
            }

            GUI.color = oldColor;
            GUILayout.EndHorizontal();
            
            return newValue;
        }

        private string CTextField(string text, params GUILayoutOption[] options) { return CTextFieldStyled(text, GUI.skin.textField, options); }
        private string CTextFieldStyled(string text, GUIStyle style, params GUILayoutOption[] options)
        {
            int index = processIndex++;
            bool isFocused = (index == currentFocusIndex);
            Color oldColor = GUI.color;
            if (isFocused) GUI.color = Color.yellow;
            
            string ctrlName = "CTF_" + index;
            GUI.SetNextControlName(ctrlName);
            
            string newText;
            if (style != null) newText = GUILayout.TextField(text, style, options);
            else newText = GUILayout.TextField(text, options);
            
            if (isFocused && Event.current.type == EventType.Repaint) {
                focusedRect = GUILayoutUtility.GetLastRect();
                GUI.FocusControl(ctrlName);
            }
            
            GUI.color = oldColor;
            return newText;
        }

        private static void ToggleHUD(bool visible)
        {
            try
            {
                // Méthode propre via le moteur du jeu : masquer le StaticCanvas entier
                // Cela cache le HUD, le Log, les fenêtres de service, etc.
                if (StaticCanvas.Instance != null && StaticCanvas.Instance.CanvasGroup != null)
                {
                    StaticCanvas.Instance.CanvasGroup.alpha = visible ? 1f : 0f;
                    StaticCanvas.Instance.CanvasGroup.blocksRaycasts = visible;
                }

                // Masquage du DynamicCanvas (bulles de texte, noms au-dessus des persos)
                var dynamicCanvas = GameObject.Find("DynamicCanvas");
                if (dynamicCanvas != null)
                {
                    var cg = dynamicCanvas.GetComponent<CanvasGroup>();
                    if (cg != null)
                    {
                        cg.alpha = visible ? 1f : 0f;
                        cg.blocksRaycasts = visible;
                    }
                    else
                    {
                        // Fallback si pas de CanvasGroup
                        dynamicCanvas.SetActive(visible);
                    }
                }
            }
            catch (Exception ex)
            {
                Main.ModEntry.Logger.Error($"[ATELIER] Failed to toggle cinematic UI: {ex.Message}");
            }
        }

        private void DrawBorder(Rect rect, float thickness, Color color)
        {
            Color old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, thickness), Texture2D.whiteTexture); // Haut
            GUI.DrawTexture(new Rect(rect.x, rect.y + rect.height - thickness, rect.width, thickness), Texture2D.whiteTexture); // Bas
            GUI.DrawTexture(new Rect(rect.x, rect.y, thickness, rect.height), Texture2D.whiteTexture); // Gauche
            GUI.DrawTexture(new Rect(rect.x + rect.width - thickness, rect.y, thickness, rect.height), Texture2D.whiteTexture); // Droite
            GUI.color = old;
        }

        void DrawCustomEnchantmentGUI(float scale)
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label(Helpers.GetString("ui_custom_enchants_title", "<b>CUSTOM Enchantments</b>"), new GUIStyle(GUI.skin.label) { richText = true, alignment = TextAnchor.MiddleCenter, fontSize = (int)(16 * scale) });
            GUILayout.Space(10);

            scrollPosition = GUILayout.BeginScrollView(scrollPosition, false, true, GUILayout.ExpandHeight(true));

            if (customEnchantments.Count == 0)
            {
                GUILayout.Label(Helpers.GetString("ui_no_custom_enchants", "No custom Enchantments found."), new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter });
            }
            else
            {
                for (int i = 0; i < customEnchantments.Count; i++)
                {
                    DrawEnchantmentRow(customEnchantments[i], scale, i);
                }
            }

            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        void DrawEnchantmentRow(EnchantmentData data, float scale, int relativeIndex)
        {
            bool isQueued = queuedEnchantGuids.Contains(data.Guid);
            var currentSelectedList = queuedEnchantGuids.Select(g => Enchantmentscanner.GetByGuid(g)).Where(d => d != null).ToList();

            var bp = ResourcesLibrary.TryGetBlueprint<BlueprintItemEnchantment>(BlueprintGuid.Parse(data.Guid));
            string displayName = DescriptionManager.GetDisplayName(bp, data);
            
            long costToPay;
            if (isQueued)
            {
                int idx = currentSelectedList.FindIndex(e => e.Guid == data.Guid);
                var preceding = currentSelectedList.Take(idx);
                costToPay = CraftingCalculator.GetMarginalCost(selectedItem, preceding, data, CraftingSettings.CostMultiplier);
            }
            else
            {
                costToPay = CraftingCalculator.GetMarginalCost(selectedItem, currentSelectedList, data, CraftingSettings.CostMultiplier);
            }
            int days = CraftingCalculator.GetCraftingDays(costToPay, CraftingSettings.InstantCrafting);

            string internalName = bp != null ? bp.name : (data.Name ?? "");

            GUILayout.BeginHorizontal(GUI.skin.box);
            GUIStyle toggleStyle = new GUIStyle(GUI.skin.label) { richText = true, alignment = TextAnchor.MiddleLeft };
            
            string label = $"<size={(int)(14 * scale)}>{displayName}</size> <color=#888888><size={(int)(12 * scale)}>({internalName})</size></color>";
            bool newSelected = CToggleStyled(isQueued, label, toggleStyle, GUILayout.ExpandWidth(true));
            
            DescriptionSource descSource = DescriptionSource.None;
            string descForData = DescriptionManager.GetLocalizedDescription(bp, data, out descSource);
            if (!string.IsNullOrEmpty(descForData))
            {
                string color = "#3498db"; // Bleu (Official)
                if (descSource == DescriptionSource.Generated) color = "#f1c40f"; // Jaune
                else if (descSource == DescriptionSource.None) color = "#e74c3c"; // Rouge

                GUIContent infoContent = new GUIContent($"<color={color}>{Helpers.GetString("ui_btn_description", "Description")}</color>");
                GUIStyle infoStyle = new GUIStyle(GUI.skin.button) { 
                    richText = true, 
                    fontStyle = FontStyle.Bold, 
                    fontSize = (int)(9 * scale),
                    padding = new RectOffset(0, 0, 0, 0)
                };
                if (CButtonStyled(infoContent, infoStyle, GUILayout.Width(100 * scale), GUILayout.Height(15 * scale))) 
                {
                    activeDescriptionTitle = displayName;
                    activeDescriptionPopup = descForData;
                }
            }
            else GUILayout.Space(25 * scale);

            // -- AFFICHAGE DU SLOT ATTENDU (LOCALISÉ) --
            bool isWrong = CraftingCalculator.IsWrongSlot(selectedItem, data);
            string slotColor = isWrong ? "#f1c40f" : "#2ecc71"; // Jaune (Avertissement) / Vert (Correct)
            
            string expectedSlotsText = "";
            if (data.Slots != null && data.Slots.Count > 0)
            {
                var localizedSlots = data.Slots.Select(s => Helpers.GetString("ui_slot_" + s.ToLower(), s));
                expectedSlotsText = string.Join(", ", localizedSlots);
            }
            else
            {
                expectedSlotsText = Helpers.GetString("ui_slot_" + (data.Type?.ToLower() ?? "other"), data.Type ?? "Other");
            }

            GUILayout.Label($"<color={slotColor}>[{expectedSlotsText}]</color>", new GUIStyle(GUI.skin.label) { richText = true, fontSize = (int)(10 * scale), alignment = TextAnchor.MiddleRight }, GUILayout.Width(120 * scale));

            string currency = Helpers.GetString("ui_currency_gp", "gp");
            string daysLabel = Helpers.GetString("ui_time_days_short", "d");
            GUILayout.Label($"{costToPay} {currency} / {days} {daysLabel}   (+{data.PointCost})", new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleRight, fontSize = (int)(14 * scale) }, GUILayout.Width(180 * scale));
            
            if (newSelected && !isQueued) 
            {
                string baseName = CraftingCalculator.GetEnchantmentFamily(internalName);
                if (!string.IsNullOrEmpty(baseName))
                {
                    queuedEnchantGuids.RemoveAll(guid => 
                    {
                        var otherData = Enchantmentscanner.GetByGuid(guid);
                        if (otherData != null)
                        {
                            var otherBp = ResourcesLibrary.TryGetBlueprint<BlueprintItemEnchantment>(BlueprintGuid.Parse(guid));
                            string otherInternal = otherBp != null ? otherBp.name : (otherData.Name ?? "");
                            string otherBase = CraftingCalculator.GetEnchantmentFamily(otherInternal);
                            return otherBase == baseName;
                        }
                        return false;
                    });
                }
                queuedEnchantGuids.Add(data.Guid);
                filtersDirty = true;
            }
            if (!newSelected && isQueued) 
            {
                queuedEnchantGuids.Remove(data.Guid);
                filtersDirty = true;
            }

            GUILayout.EndHorizontal();
            if (Event.current.type == EventType.Repaint && relativeIndex % 2 != 0)
            {
                Rect lastRect = GUILayoutUtility.GetLastRect();
                Color oldC = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, 0.06f); // Calque blanc très léger (6% d'opacité)
                GUI.DrawTexture(lastRect, Texture2D.whiteTexture);
                GUI.color = oldC;
            }
        }
    }
}
