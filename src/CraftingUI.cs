using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using Kingmaker;
using Kingmaker.Items;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Facts;
using Kingmaker.Blueprints.Items.Weapons;
using Kingmaker.Blueprints.Items.Armors;
using Kingmaker.Blueprints.Items.Shields;
using Kingmaker.Blueprints.Items.Ecnchantments;
using Kingmaker.UI;

namespace CraftingSystem
{
    public class CraftingUI : MonoBehaviour
    {
        // --- DESIGN SYSTEM : POLICES ---
        public const int FONT_HUGE = 20;    // Titres principaux, En-têtes de fenêtre
        public const int FONT_LARGE = 16;   // Titres de sections, Nom de l'objet sélectionné
        public const int FONT_NORMAL = 14;  // Labels de listes, Noms d'enchantements, Coûts
        public const int FONT_SMALL = 12;   // Noms internes, Indications secondaires
        public const int FONT_TINY = 10;    // Boutons Description, Micro-indications

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
                    currentPageType = CraftingPage.MainMenu;
                    itemsPerPageInput = CraftingSettings.ItemsPerPage.ToString();
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
        private string enchantmentSearch = "";
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
        private bool filtersDirty = true;
        private string lastSearch = "";
        private int lastCategoryCount = -1;
        private int lastTypeCount = -1;
        private ItemEntity lastSelectedItem = null;
        private string pageInput = "";
        private string itemsPerPageInput = "";

        // Auto-scale reference
        private const float REFERENCE_WIDTH = 2560f;
        private const float REFERENCE_HEIGHT = 1440f;

        // Controller Navigation Variables
        public enum CraftingPage
        {
            MainMenu,
            WorkshopInventory,
            CreateWeapon,
            CreateArmor,
            CreateAccessory,
            CreateMetamagicRod,
            CreateWand,
            CreateScroll
        }
        private CraftingPage currentPageType = CraftingPage.MainMenu;

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
            try
            {
                h = UnityEngine.Input.GetAxisRaw("Horizontal");
                v = UnityEngine.Input.GetAxisRaw("Vertical");
            }
            catch { }

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

                        if (focusedRect.yMax > scrollPosition.y + estimatedViewHeight)
                        {
                            scrollPosition.y = focusedRect.yMax - estimatedViewHeight + padding;
                        }
                        else if (focusedRect.yMin < scrollPosition.y)
                        {
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

        // État pour les enchantements dynamiques
        private CustomEnchantmentData selectedModel = null;
        private Dictionary<string, int> dynamicParamValues = new Dictionary<string, int>();
        private string openDropdownParam = null;
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

            if (inputCancelDown)
            {
                if (!string.IsNullOrEmpty(activeDescriptionPopup)) activeDescriptionPopup = "";
                else if (selectedItem != null)
                {
                    selectedItem = null;
                    newNameDraft = "";
                    queuedEnchantGuids.Clear();
                    activeCategories.Clear();
                    activeTypes.Clear();
                    showCategoryFilter = false;
                }
                else if (ShowSettings) { ShowSettings = false; }
                else IsOpen = false;
                inputCancelDown = false;
            }

            if (UnityEngine.Input.GetKeyDown(KeyCode.Escape) || UnityEngine.Input.GetKeyDown(KeyCode.JoystickButton7))
            {
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

            EnchantmentScanner.StartSync();

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
                GUILayout.Label(feedbackMessage, new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = (int)(FONT_LARGE * scale) });
                if (CButton(Helpers.GetString("ui_btn_ok", "OK"), GUILayout.Height(40 * scale))) feedbackMessage = "";
                return;
            }

            // --- HEADER AVEC DÉFILEMENT POUR TEXTE LONG ---
            GUILayout.BeginHorizontal();

            string title = Helpers.GetString("ui_title_workshop", "Workshop");
            if (ShowSettings) title = Helpers.GetString("ui_title_config", "Configuration");
            else if (selectedItem != null) title = Helpers.GetString("ui_title_details", "Details: ") + selectedItem.Name;
            else {
                switch(currentPageType) {
                    case CraftingPage.MainMenu: title = Helpers.GetString("ui_title_main_menu", "Main Menu"); break;
                    case CraftingPage.WorkshopInventory: title = Helpers.GetString("ui_title_select", "Item Selection"); break;
                    case CraftingPage.CreateWeapon: title = Helpers.GetString("ui_menu_create_weapon", "Create Weapon"); break;
                    case CraftingPage.CreateArmor: title = Helpers.GetString("ui_menu_create_armor", "Create Armor"); break;
                    case CraftingPage.CreateAccessory: title = Helpers.GetString("ui_menu_create_accessory", "Create Accessory"); break;
                    case CraftingPage.CreateMetamagicRod: title = Helpers.GetString("ui_menu_create_rod", "Create Metamagic Rod"); break;
                    case CraftingPage.CreateWand: title = Helpers.GetString("ui_menu_create_wand", "Create Wand"); break;
                    case CraftingPage.CreateScroll: title = Helpers.GetString("ui_menu_create_scroll", "Create Scroll"); break;
                }
            }

            titleScrollPosition = GUILayout.BeginScrollView(titleScrollPosition, GUIStyle.none, GUIStyle.none, GUILayout.ExpandWidth(true), GUILayout.Height(45 * scale));
            GUILayout.Label(title, new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = (int)(FONT_LARGE * scale), wordWrap = false });
            GUILayout.EndScrollView();

            GUILayout.Space(10);

            GUIStyle navStyle = new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold, fontSize = (int)(FONT_NORMAL * scale) };

            if (CButtonStyled(new GUIContent(Helpers.GetString("ui_btn_info", "Information")), navStyle, GUILayout.Width(130 * scale), GUILayout.Height(35 * scale)))
            {
                activeDescriptionTitle = Helpers.GetString("ui_info_title", "Crafting Rules");
                activeDescriptionPopup = Helpers.GetString("ui_info_text");
            }

            if ((selectedItem != null || currentPageType != CraftingPage.MainMenu) && !ShowSettings)
            {
                if (CButtonStyled(new GUIContent(Helpers.GetString("ui_btn_back", "<< BACK")), navStyle, GUILayout.Width(130 * scale), GUILayout.Height(35 * scale)))
                {
                    if (selectedItem != null) {
                        selectedItem = null;
                        showCustomEnchantPage = false;
                        newNameDraft = "";
                        queuedEnchantGuids.Clear();
                        activeCategories.Clear();
                        activeTypes.Clear();
                        showCategoryFilter = false;
                    } else {
                        currentPageType = CraftingPage.MainMenu;
                    }
                }
            }

            float windowWidth = 800f * scale;
            float optionWidth = Mathf.Max(CraftingSettings.BUTTON_OPTION_WIDTH_BASE * scale, windowWidth * 0.14f);
            float closeWidth = Mathf.Max(CraftingSettings.BUTTON_CLOSE_WIDTH_BASE * scale, windowWidth * 0.06f);

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
            else if (selectedItem != null) DrawItemModificationGUI(scale);
            else {
                switch(currentPageType) {
                    case CraftingPage.MainMenu: DrawMainMenuGUI(scale); break;
                    case CraftingPage.WorkshopInventory: DrawInventoryGUI(scale); break;
                    case CraftingPage.CreateWeapon: DrawCreateWeaponGUI(scale); break;
                    case CraftingPage.CreateArmor: DrawCreateArmorGUI(scale); break;
                    case CraftingPage.CreateAccessory: DrawCreateAccessoryGUI(scale); break;
                    case CraftingPage.CreateMetamagicRod: DrawCreateMetamagicRodGUI(scale); break;
                    case CraftingPage.CreateWand: DrawCreateWandGUI(scale); break;
                    case CraftingPage.CreateScroll: DrawCreateScrollGUI(scale); break;
                }
            }
        }

        void DrawMainMenuGUI(float scale)
        {
            scrollPosition = GUILayout.BeginScrollView(scrollPosition, GUILayout.ExpandHeight(true));
            
            float windowWidth = 1000f * scale;
            float contentWidth = windowWidth - (120f * scale); 

            GUIStyle sectionHeaderStyle = new GUIStyle(GUI.skin.label) {
                fontSize = (int)(FONT_LARGE * scale),
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter, // Centré
                normal = { textColor = new Color(0.9f, 0.8f, 0.4f) }
            };

            GUIStyle btnStyle = new GUIStyle(GUI.skin.button) { 
                fontSize = (int)(FONT_NORMAL * scale), 
                fixedHeight = 75 * scale,
                wordWrap = true,
                padding = new RectOffset(20, 20, 10, 10),
                alignment = TextAnchor.MiddleCenter, // Centré
                richText = true
            };

            // Conteneur centré pour toute la page
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.BeginVertical(GUILayout.Width(contentWidth));
            GUILayout.Space(25 * scale);

            // --- SECTION 1 : GESTION (BLEU) ---
            GUILayout.Label("⛭  " + Helpers.GetString("ui_section_management", "GESTION DE L'ATELIER") + "  ⛭", sectionHeaderStyle);
            DrawSeparator(contentWidth, new Color(0.3f, 0.5f, 0.7f, 0.8f));
            GUILayout.Space(12 * scale);

            DrawMenuButton(new GUIContent("<b><size=" + (int)(FONT_LARGE * scale) + ">📦 " + Helpers.GetString("ui_menu_inventory", "Workshop Inventory") + "</size></b>\n" + 
                                        "<color=#aaaaaa>" + Helpers.GetString("ui_desc_inventory", "Modify and upgrade your existing items") + "</color>"), 
                           btnStyle, CraftingPage.WorkshopInventory, new Color(0.2f, 0.3f, 0.5f), GUILayout.Height(85 * scale));

            GUILayout.Space(40 * scale);

            // --- SECTION 2 : FORGE D'ÉQUIPEMENT (ROUGE/BRUN) ---
            GUILayout.Label("⚒  " + Helpers.GetString("ui_section_forge", "FORGE : CRÉATION D'ÉQUIPEMENT") + "  ⚒", sectionHeaderStyle);
            DrawSeparator(contentWidth, new Color(0.7f, 0.3f, 0.2f, 0.8f));
            GUILayout.Space(15 * scale);

            float colWidth3 = (contentWidth / 3f) - (10 * scale);
            Color forgeTint = new Color(0.5f, 0.25f, 0.2f);

            GUILayout.BeginHorizontal();
            DrawMenuButton(new GUIContent("<b>⚔ " + Helpers.GetString("ui_menu_create_weapon", "Create Weapon") + "</b>"), btnStyle, CraftingPage.CreateWeapon, forgeTint, GUILayout.Width(colWidth3));
            GUILayout.Space(10 * scale);
            DrawMenuButton(new GUIContent("<b>🛡 " + Helpers.GetString("ui_menu_create_armor", "Create Armor") + "</b>"), btnStyle, CraftingPage.CreateArmor, forgeTint, GUILayout.Width(colWidth3));
            GUILayout.Space(10 * scale);
            DrawMenuButton(new GUIContent("<b>💍 " + Helpers.GetString("ui_menu_create_accessory", "Create Accessory") + "</b>"), btnStyle, CraftingPage.CreateAccessory, forgeTint, GUILayout.Width(colWidth3));
            GUILayout.EndHorizontal();

            GUILayout.Space(40 * scale);

            // --- SECTION 3 : ARTISANAT MAGIQUE (VIOLET) ---
            GUILayout.Label("✨  " + Helpers.GetString("ui_section_magic", "ARTS ARCANES : OBJETS MAGIQUES") + "  ✨", sectionHeaderStyle);
            DrawSeparator(contentWidth, new Color(0.6f, 0.3f, 0.8f, 0.8f));
            GUILayout.Space(15 * scale);

            Color magicTint = new Color(0.4f, 0.2f, 0.6f);

            GUILayout.BeginHorizontal();
            DrawMenuButton(new GUIContent("<b>🪄 " + Helpers.GetString("ui_menu_create_rod", "Create Metamagic Rod") + "</b>"), btnStyle, CraftingPage.CreateMetamagicRod, magicTint, GUILayout.Width(colWidth3));
            GUILayout.Space(10 * scale);
            DrawMenuButton(new GUIContent("<b>⚡ " + Helpers.GetString("ui_menu_create_wand", "Create Wand") + "</b>"), btnStyle, CraftingPage.CreateWand, magicTint, GUILayout.Width(colWidth3));
            GUILayout.Space(10 * scale);
            DrawMenuButton(new GUIContent("<b>📜 " + Helpers.GetString("ui_menu_create_scroll", "Create Scroll") + "</b>"), btnStyle, CraftingPage.CreateScroll, magicTint, GUILayout.Width(colWidth3));
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.EndScrollView();
        }

        private void DrawMenuButton(GUIContent content, GUIStyle style, CraftingPage targetPage, Color tint, params GUILayoutOption[] options)
        {
            Color oldBG = GUI.backgroundColor;
            GUI.backgroundColor = tint;
            if (CButtonStyled(content, style, options)) currentPageType = targetPage;
            GUI.backgroundColor = oldBG;
        }

        private void DrawSeparator(float width, Color color)
        {
            Rect rect = GUILayoutUtility.GetRect(width, 2f);
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        void DrawCreateWeaponGUI(float scale) { GUILayout.Label("Coming Soon: Weapon Creation"); }
        void DrawCreateArmorGUI(float scale) { GUILayout.Label("Coming Soon: Armor Creation"); }
        void DrawCreateAccessoryGUI(float scale) { GUILayout.Label("Coming Soon: Accessory Creation"); }
        void DrawCreateMetamagicRodGUI(float scale) { GUILayout.Label("Coming Soon: Metamagic Rod Creation"); }
        void DrawCreateWandGUI(float scale) { GUILayout.Label("Coming Soon: Wand Creation"); }
        void DrawCreateScrollGUI(float scale) { GUILayout.Label("Coming Soon: Scroll Creation"); }

        private string inventorySearch = "";
        void DrawInventoryGUI(float scale)
        {
            var workshop = Game.Instance.Player.MainCharacter.Value.Get<UnitPartWilcerWorkshop>();
            var allItems = workshop?.StashedItems ?? new List<ItemEntity>();

            float windowWidth = 1000f * scale;
            float contentWidth = windowWidth - (120f * scale);

            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.BeginVertical(GUILayout.Width(contentWidth));

            // --- BARRE DE RECHERCHE ET FILTRES ---
            GUILayout.BeginHorizontal();
            GUIStyle searchLabelStyle = new GUIStyle(GUI.skin.label) { fontSize = (int)(FONT_NORMAL * scale), alignment = TextAnchor.MiddleLeft };
            GUIStyle searchFieldStyle = new GUIStyle(GUI.skin.textField) { fontSize = (int)(FONT_NORMAL * scale), alignment = TextAnchor.MiddleLeft };
            GUILayout.Label("🔍 " + Helpers.GetString("ui_search", "Search:"), searchLabelStyle, GUILayout.Width(110 * scale), GUILayout.Height(35 * scale));
            inventorySearch = CTextFieldStyled(inventorySearch, searchFieldStyle, GUILayout.ExpandWidth(true), GUILayout.Height(35 * scale));
            if (CButton("X", GUILayout.Width(35 * scale), GUILayout.Height(35 * scale))) inventorySearch = "";
            GUILayout.EndHorizontal();

            GUILayout.Space(10 * scale);

            GUILayout.BeginHorizontal();
            GUIStyle filterBtnStyle = new GUIStyle(GUI.skin.button) { fontSize = (int)(FONT_SMALL * scale), fixedHeight = 30 * scale };
            
            bool filterAll = activeTypes.Count == 0 || activeTypes.Count == 3;
            if (CButtonStyled(new GUIContent(Helpers.GetString("ui_filter_all", "Show All")), filterAll ? new GUIStyle(filterBtnStyle) { normal = { textColor = Color.yellow } } : filterBtnStyle, GUILayout.Width(120 * scale))) 
                activeTypes.Clear();
            
            GUILayout.Space(5 * scale);
            bool filterWep = activeTypes.Contains("Weapon") && activeTypes.Count == 1;
            if (CButtonStyled(new GUIContent("⚔ " + Helpers.GetString("ui_type_weapon", "Weapon")), filterWep ? new GUIStyle(filterBtnStyle) { normal = { textColor = Color.yellow } } : filterBtnStyle, GUILayout.Width(120 * scale))) 
                { activeTypes.Clear(); activeTypes.Add("Weapon"); }

            GUILayout.Space(5 * scale);
            bool filterArm = activeTypes.Contains("Armor") && activeTypes.Count == 1;
            if (CButtonStyled(new GUIContent("🛡 " + Helpers.GetString("ui_type_armor", "Armor")), filterArm ? new GUIStyle(filterBtnStyle) { normal = { textColor = Color.yellow } } : filterBtnStyle, GUILayout.Width(120 * scale))) 
                { activeTypes.Clear(); activeTypes.Add("Armor"); }

            GUILayout.Space(5 * scale);
            bool filterAcc = activeTypes.Contains("Other") && activeTypes.Count == 1;
            if (CButtonStyled(new GUIContent("💍 " + Helpers.GetString("ui_type_other", "Accessory")), filterAcc ? new GUIStyle(filterBtnStyle) { normal = { textColor = Color.yellow } } : filterBtnStyle, GUILayout.Width(120 * scale))) 
                { activeTypes.Clear(); activeTypes.Add("Other"); }
            
            GUILayout.EndHorizontal();

            GUILayout.Space(15 * scale);
            DrawSeparator(contentWidth, new Color(0.4f, 0.4f, 0.4f));
            GUILayout.Space(15 * scale);

            scrollPosition = GUILayout.BeginScrollView(scrollPosition, GUILayout.ExpandHeight(true));

            // Filtrage des items
            var filteredItems = allItems.Where(it => {
                if (!string.IsNullOrEmpty(inventorySearch) && !it.Name.ToLower().Contains(inventorySearch.ToLower())) return false;
                if (activeTypes.Count > 0 && activeTypes.Count < 3) {
                    if (activeTypes.Contains("Weapon") && it.Blueprint is BlueprintItemWeapon) return true;
                    if (activeTypes.Contains("Armor") && (it.Blueprint is BlueprintItemArmor || it.Blueprint is BlueprintItemShield)) return true;
                    if (activeTypes.Contains("Other") && !(it.Blueprint is BlueprintItemWeapon) && !(it.Blueprint is BlueprintItemArmor) && !(it.Blueprint is BlueprintItemShield)) return true;
                    return false;
                }
                return true;
            }).ToList();

            if (!filteredItems.Any())
            {
                GUIStyle emptyStyle = new GUIStyle(GUI.skin.label) { fontSize = (int)(FONT_NORMAL * scale), alignment = TextAnchor.MiddleCenter, richText = true };
                GUILayout.Label("\n\n" + Helpers.GetString("ui_no_item_match", "(No items match the current filters)"), emptyStyle);
            }
            else
            {
                GUIStyle entryStyle = new GUIStyle(GUI.skin.button) {
                    wordWrap = true,
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = (int)(FONT_NORMAL * scale),
                    richText = true,
                    padding = new RectOffset(10, 10, 10, 10)
                };

                int cols = 2;
                float itemWidth = (contentWidth / (float)cols) - (15 * scale);

                for (int i = 0; i < filteredItems.Count; i += cols)
                {
                    GUILayout.BeginHorizontal();
                    for (int j = 0; j < cols && (i + j) < filteredItems.Count; j++)
                    {
                        var it = filteredItems[i + j];
                        var project = workshop?.ActiveProjects.FirstOrDefault(p => p.Item == it);
                        
                        string typePrefix = "";
                        Color typeColor = new Color(0.4f, 0.4f, 0.4f); // Gris par défaut

                        if (it.Blueprint is BlueprintItemWeapon) { typePrefix = "⚔ "; typeColor = new Color(0.5f, 0.25f, 0.2f); }
                        else if (it.Blueprint is BlueprintItemArmor || it.Blueprint is BlueprintItemShield) { typePrefix = "🛡 "; typeColor = new Color(0.3f, 0.4f, 0.5f); }
                        else { typePrefix = "💍 "; typeColor = new Color(0.4f, 0.3f, 0.5f); }

                        string label = $"<b>{typePrefix}{it.Name}</b>";
                        if (project != null) label += "\n<color=#ffcc00>" + Helpers.GetString("ui_in_forge", "(In forge...)") + "</color>";

                        Color oldBG = GUI.backgroundColor;
                        GUI.backgroundColor = typeColor;
                        if (CButtonStyled(new GUIContent(label), entryStyle, GUILayout.Width(itemWidth), GUILayout.Height(70 * scale)))
                        {
                            selectedItem = it;
                            newNameDraft = it.Name;
                            queuedEnchantGuids.Clear();
                            activeCategories.Clear();
                            showCategoryFilter = false;
                            
                            activeTypes.Clear();
                            if (it.Blueprint is BlueprintItemWeapon) activeTypes.Add("Weapon");
                            else if (it.Blueprint is BlueprintItemArmor || it.Blueprint is BlueprintItemShield) activeTypes.Add("Armor");
                            else activeTypes.Add("Other");

                            filtersDirty = true;
                            currentPage = 0;
                        }
                        GUI.backgroundColor = oldBG;
                        if (j < cols - 1) GUILayout.Space(10 * scale);
                    }
                    GUILayout.EndHorizontal();
                    GUILayout.Space(10 * scale);
                }
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
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
            GUILayout.Label(Helpers.GetString("ui_special_action_rename", "Special Action: Rename item (Free)"), new GUIStyle(GUI.skin.label) { fontSize = (int)(FONT_NORMAL * scale) });
            GUILayout.BeginHorizontal();

            float windowWidth = 800f * scale;
            float buttonsSpace = (120f + 100f + 25f) * scale;
            float padding = (45f + 20f) * scale;
            float exactTextWidth = windowWidth - buttonsSpace - padding;

            GUIStyle textFieldStyle = new GUIStyle(GUI.skin.textField);
            textFieldStyle.wordWrap = false;
            textFieldStyle.fontSize = (int)(FONT_NORMAL * scale);

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
            if (selectedItem != lastSelectedItem || enchantmentSearch != lastSearch || activeCategories.Count != lastCategoryCount || activeTypes.Count != lastTypeCount || selectionChanged)
            {
                // On détecte si c'est UNIQUEMENT la sélection qui a changé
                bool filtersUnchanged = (selectedItem == lastSelectedItem && enchantmentSearch == lastSearch && activeCategories.Count == lastCategoryCount && activeTypes.Count == lastTypeCount);

                lastSelectedItem = selectedItem;
                lastSearch = enchantmentSearch;
                lastCategoryCount = activeCategories.Count;
                lastTypeCount = activeTypes.Count;
                filtersDirty = false;

                RebuildFilteredList();

                // On ne reset la page que si les filtres (recherche, catégories, types) ont changé.
                // Si c'est juste une coche/décoche, on reste sur la même page pour éviter de perdre sa place.
                if (!filtersUnchanged) currentPage = 0;
            }


            // --- SECTION : ENCHANTEMENTS DÉJÀ PRÉSENTS ---
            GUILayout.Label(Helpers.GetString("ui_applied_enchants", "Applied Enchantments:"), new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = (int)(FONT_NORMAL * scale) });
            var currentEnchants = selectedItem.Enchantments.ToList();
            if (!currentEnchants.Any())
            {
                GUILayout.Label(Helpers.GetString("ui_no_magic_enchants", "<i>(No magic enchantment)</i>"), new GUIStyle(GUI.skin.label) { richText = true, fontSize = (int)(FONT_SMALL * scale) });
            }
            else
            {
                foreach (var ench in currentEnchants)
                {
                    string guid = ench.Blueprint.AssetGuid.ToString();
                    var overrideData = EnchantmentScanner.GetByGuid(guid);
                    int pointValue = overrideData?.PointCost ?? ench.Blueprint.EnchantmentCost;
                    if (pointValue < 0) pointValue = 0;
                    GUIStyle rowLabelStyle = new GUIStyle(GUI.skin.label)
                    {
                        fontSize = (int)(FONT_NORMAL * scale),
                        alignment = TextAnchor.MiddleLeft
                    };
                    GUILayout.BeginHorizontal(GUI.skin.box);
                    string displayName = DescriptionManager.GetDisplayName(ench.Blueprint, overrideData);
                    GUILayout.Label($"{displayName} (+{pointValue})", rowLabelStyle, GUILayout.ExpandWidth(true), GUILayout.Height(20 * scale));

                    // POUR LES OBJETS DÉJÀ APPLIQUÉS (RÉSOLU DYNAMIQUEMENT)
                    DescriptionSource genSource = DescriptionSource.None;
                    string appliedDesc = DescriptionManager.GetLocalizedDescription(ench.Blueprint, overrideData, out genSource);
                    string color;
                    if (!string.IsNullOrEmpty(appliedDesc) && genSource == DescriptionSource.Official)
                    {
                        color = "#3498db"; // Bleu (Official)
                    }
                    else if (!string.IsNullOrEmpty(appliedDesc) && genSource == DescriptionSource.Generated)
                    {
                        color = "#f1c40f"; // Jaune (Généré)
                    }
                    else
                    {
                        color = "#e74c3c"; // Rouge (Fallback / TODO)
                        if (string.IsNullOrEmpty(appliedDesc)) appliedDesc = Helpers.GetString("ui_desc_needed", "TODO: Description needed for this enchantment.");
                    }

                    GUIContent infoContent = new GUIContent($"<color={color}>{Helpers.GetString("ui_btn_description", "Description")}</color>");
                    GUIStyle infoStyle = new GUIStyle(GUI.skin.button)
                    {
                        richText = true,
                        fontStyle = FontStyle.Bold,
                        fontSize = (int)(FONT_TINY * scale),
                        alignment = TextAnchor.MiddleCenter,
                        padding = new RectOffset(2, 2, 0, 0)
                    };

                    if (CButtonStyled(infoContent, infoStyle, GUILayout.Width(100 * scale), GUILayout.Height(20 * scale)))
                    {
                        activeDescriptionTitle = displayName;
                        activeDescriptionPopup = appliedDesc;
                    }

                    if (CButton(Helpers.GetString("ui_btn_remove", "Remove"), GUILayout.Width(100 * scale), GUILayout.Height(20 * scale)))
                    {
                        selectedItem.RemoveEnchantment(ench);
                        selectedItem.Identify();
                        filtersDirty = true;
                    }
                    GUILayout.EndHorizontal();
                }
            }

            GUILayout.Space(20);
            Div(scale);
            GUILayout.Space(10);

            // --- ONGLETS : STANDART VS CUSTOM ---
            GUILayout.BeginHorizontal();

            GUIStyle tabStyleNormal = new GUIStyle(GUI.skin.button) { fontSize = (int)(FONT_NORMAL * scale), fontStyle = FontStyle.Normal };
            GUIStyle tabStyleActive = new GUIStyle(GUI.skin.button) { fontSize = (int)(FONT_NORMAL * scale), fontStyle = FontStyle.Bold };
            tabStyleActive.normal.textColor = Color.yellow;

            if (CButtonStyled(new GUIContent(Helpers.GetString("ui_tab_standard", "Available Enchantments")), !showCustomEnchantPage ? tabStyleActive : tabStyleNormal, GUILayout.Height(40 * scale), GUILayout.ExpandWidth(true)))
            {
                showCustomEnchantPage = false;
            }
            if (CButtonStyled(new GUIContent(Helpers.GetString("ui_tab_custom", "Custom Enchantments")), showCustomEnchantPage ? tabStyleActive : tabStyleNormal, GUILayout.Height(40 * scale), GUILayout.ExpandWidth(true)))
            {
                showCustomEnchantPage = true;
                selectedModel = null; // Reset selection when switching to tab
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(15);

            if (showCustomEnchantPage)
            {
                DrawCustomEnchantmentGUI_Content(scale);
            }
            else
            {
                // --- RECHERCHE + LISTE STANDART ---
                // -- UI FILTRE DES TYPES --
                DrawTypeFilters(scale);

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
                GUILayout.Label(Helpers.GetString("ui_search_label", "Search: "), new GUIStyle(GUI.skin.label) { fontSize = (int)(FONT_NORMAL * scale) }, GUILayout.Width(100 * scale));
                enchantmentSearch = CTextField(enchantmentSearch, GUILayout.ExpandWidth(true));

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

                if (EnchantmentScanner.IsSyncing)
                {
                    GUILayout.Label($"({EnchantmentScanner.LastSyncMessage})", new GUIStyle(GUI.skin.label) { richText = true, fontSize = (int)(FONT_SMALL * scale), alignment = TextAnchor.MiddleCenter });
                }

                GUILayout.Space(5);

                GUILayout.BeginVertical(GUI.skin.box);

                if (EnchantmentScanner.IsSyncing)
                {
                    GUILayout.Label(Helpers.GetString("ui_scan_in_progress", "Scan en cours — aucun enchantement disponible pour l'instant."), new GUIStyle(GUI.skin.label) { fontSize = (int)(FONT_SMALL * scale), alignment = TextAnchor.MiddleCenter });
                }
                else
                {
                    // -- NAVIGATION DE PAGINATION --
                    // -- DYNAMIC PAGINATION CALCULATION --
                    float currentWindowHeight = Mathf.Min(900f * scale, Screen.height * 0.9f);
                    // Pagination manuelle basée sur les réglages
                    int totalItems = cachedFilteredEnchantments.Count;
                    int totalPages = Mathf.Max(1, Mathf.CeilToInt((float)totalItems / CraftingSettings.ItemsPerPage));
                    if (currentPage >= totalPages) currentPage = totalPages - 1;


                    GUILayout.BeginHorizontal();
                    GUIStyle paginationStyle = new GUIStyle(GUI.skin.label) { fontSize = (int)(FONT_SMALL * scale), alignment = TextAnchor.MiddleLeft };
                    GUIStyle pageInputStyle = new GUIStyle(GUI.skin.textField) { fontSize = (int)(FONT_SMALL * scale), alignment = TextAnchor.MiddleCenter };
                    GUIStyle pageInfoStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = (int)(FONT_SMALL * scale) };

                    // --- GAUCHE : NAVIGATION ---
                    if (string.IsNullOrEmpty(pageInput)) pageInput = (currentPage + 1).ToString();
                    GUI.enabled = (currentPage > 0);
                    if (CButton("<<", GUILayout.Width(40 * scale))) { currentPage = 0; pageInput = "1"; }
                    if (CButton("<", GUILayout.Width(30 * scale))) { currentPage--; pageInput = (currentPage + 1).ToString(); }
                    GUI.enabled = true;

                    GUILayout.Space(10 * scale);

                    // --- CENTRE : PAGE INTERACTIVE ---
                    GUILayout.BeginHorizontal(GUILayout.Width(180 * scale));
                    GUILayout.Label(Helpers.GetString("ui_pagination_page", "Page "), paginationStyle);
                    pageInput = CTextFieldStyled(pageInput, pageInputStyle, GUILayout.Width(40 * scale), GUILayout.Height(22 * scale));

                    // Détection Entrée pour la page
                    if (Event.current.isKey && Event.current.keyCode == KeyCode.Return)
                    {
                        if (int.TryParse(pageInput, out int p))
                        {
                            currentPage = Mathf.Clamp(p - 1, 0, totalPages - 1);
                            pageInput = (currentPage + 1).ToString();
                        }
                    }

                    GUILayout.Label(" / " + totalPages, paginationStyle);
                    GUILayout.EndHorizontal();

                    GUILayout.Space(10 * scale);

                    // --- CENTRE-DROITE : INFO TOTAL ---
                    GUILayout.Label($"({totalItems} {Helpers.GetString("ui_pagination_items", "items")})", pageInfoStyle);

                    GUILayout.Space(10 * scale);

                    GUI.enabled = (currentPage < totalPages - 1);
                    if (CButton(">", GUILayout.Width(30 * scale))) { currentPage++; pageInput = (currentPage + 1).ToString(); }
                    if (CButton(">>", GUILayout.Width(40 * scale))) { currentPage = totalPages - 1; pageInput = (currentPage + 1).ToString(); }
                    GUI.enabled = true;

                    GUILayout.FlexibleSpace();

                    // --- DROITE : RÉGLAGES AFFICHAGE ---
                    GUILayout.Label(Helpers.GetString("ui_pagination_items_per_page", "Items: "), paginationStyle, GUILayout.Width(100 * scale));
                    if (string.IsNullOrEmpty(itemsPerPageInput)) itemsPerPageInput = CraftingSettings.ItemsPerPage.ToString();
                    itemsPerPageInput = CTextFieldStyled(itemsPerPageInput, pageInputStyle, GUILayout.Width(40 * scale), GUILayout.Height(22 * scale));

                    // Détection Entrée pour ItemsPerPage
                    if (Event.current.isKey && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter))
                    {
                        if (int.TryParse(itemsPerPageInput, out int val) && val > 0)
                        {
                            CraftingSettings.ItemsPerPage = val;
                            CraftingSettings.SaveSettings();
                            filtersDirty = true;
                        }
                    }

                    GUILayout.EndHorizontal();

                    GUILayout.Space(5);

                    // -- EN-TÊTES DU TABLEAU --
                    GUILayout.BeginHorizontal();
                    GUIStyle headerStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = (int)(FONT_SMALL * scale) };
                    GUIStyle headerCenterStyle = new GUIStyle(headerStyle) { alignment = TextAnchor.MiddleCenter };

                    GUILayout.Label(Helpers.GetString("ui_header_name", "Name"), headerStyle, GUILayout.ExpandWidth(true));
                    GUILayout.Space(100 * scale); // Espace pour le bouton description (non titré)
                    GUILayout.Label(Helpers.GetString("ui_header_slot_affinity", "Slot Affinity"), headerCenterStyle, GUILayout.Width(120 * scale));
                    GUILayout.Label(Helpers.GetString("ui_header_cost", "Cost / Time"), headerCenterStyle, GUILayout.Width(180 * scale));
                    GUILayout.EndHorizontal();

                    var currentSelectedList = queuedEnchantGuids.Select(g => EnchantmentScanner.GetByGuid(g)).Where(d => d != null).ToList();

                    int startIdx = currentPage * CraftingSettings.ItemsPerPage;
                    int endIdx = Mathf.Min(startIdx + CraftingSettings.ItemsPerPage, cachedFilteredEnchantments.Count);

                    for (int i = startIdx; i < endIdx; i++)
                    {
                        var data = cachedFilteredEnchantments[i];
                        DrawEnchantmentRow(data, scale, i - startIdx);
                    }
                }

                GUILayout.EndVertical(); // Fin du box de la liste standard (L664)
            }

            GUILayout.EndScrollView(); // Fin du scroll principal (L438)

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
            }

            // Calcul du coût total (Marginal Pricing prend en charge la liste complète)
            long totalCost = CraftingCalculator.GetMarginalCost(selectedItem, selectedList, null, CraftingSettings.CostMultiplier);
            int totalDays = CraftingCalculator.GetCraftingDays(totalCost, CraftingSettings.InstantCrafting);

            int currentLevelPoints = CraftingCalculator.CalculateDisplayedEnchantmentPoints(selectedItem);
            int maxLevel = CraftingSettings.MaxTotalBonus;
            int selectedPoints = selectedList.Sum(d => d.PointCost);

            GUILayout.BeginHorizontal();
            GUILayout.Label(string.Format(Helpers.GetString("ui_current_level", "Current level: {0}/{1}"), currentLevelPoints, maxLevel), new GUIStyle(GUI.skin.label) { fontSize = (int)(FONT_NORMAL * scale) }, GUILayout.ExpandWidth(false));
            GUILayout.FlexibleSpace();
            GUILayout.Label(string.Format(Helpers.GetString("ui_selection_total", "Selection: +{0} \u2014 Total: {1} gp / ~{2} d"), selectedPoints, totalCost, totalDays), new GUIStyle(GUI.skin.label) { fontSize = (int)(FONT_NORMAL * scale) }, GUILayout.ExpandWidth(false));
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
                        // On ne prélève plus l'or ici, car StartCraftingProject le fait pour chaque enchantement.
                        // Game.Instance.Player.Money -= totalCost; 

                        foreach (var d in selectedList)
                        {
                            long c = CraftingCalculator.GetEnchantmentCost(selectedItem, d, CraftingSettings.CostMultiplier);
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

            if (CButton(Helpers.GetString("ui_btn_cancel_selection", "Cancel Selection"), GUILayout.Width(250 * scale), GUILayout.Height(40 * scale)))
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
            GUILayout.Label(Helpers.GetString("ui_settings_title", "Workshop Settings"), new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = (int)(FONT_LARGE * scale) });

            GUILayout.Space(10);

            GUIStyle settingsLabelStyle = new GUIStyle(GUI.skin.label) { fontSize = (int)(FONT_NORMAL * scale) };

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

            bool oldEnforce = CraftingSettings.EnforcePointsLimit;
            CraftingSettings.EnforcePointsLimit = CToggle(CraftingSettings.EnforcePointsLimit, Helpers.GetString("ui_settings_enforce_limit", " Enforce Bonus Limits (Pathfinder)"));
            if (oldEnforce != CraftingSettings.EnforcePointsLimit)
            {
                filtersDirty = true;
                if (!CraftingSettings.EnforcePointsLimit)
                {
                    CraftingSettings.RequirePlusOneFirst = false;
                    CraftingSettings.ApplySlotPenalty = false;
                    CraftingSettings.EnableEpicCosts = false;
                }
            }

            if (CraftingSettings.EnforcePointsLimit)
            {
                GUILayout.Space(5);
                bool oldReq = CraftingSettings.RequirePlusOneFirst;
                CraftingSettings.RequirePlusOneFirst = CToggle(CraftingSettings.RequirePlusOneFirst, Helpers.GetString("ui_settings_require_plus_one", " Prerequisite: At least +1 Enhancement"));
                if (oldReq != CraftingSettings.RequirePlusOneFirst) filtersDirty = true;

                GUILayout.Space(5);
                CraftingSettings.ApplySlotPenalty = CToggle(CraftingSettings.ApplySlotPenalty, Helpers.GetString("ui_settings_slot_penalty", " Apply Slot Penalty (x1.5)"));

                GUILayout.Space(5);
                bool oldEpic = CraftingSettings.EnableEpicCosts;
                CraftingSettings.EnableEpicCosts = CToggle(CraftingSettings.EnableEpicCosts, Helpers.GetString("ui_settings_enable_epic", " Enable Epic Multiplier (x10)"));
                if (oldEpic != CraftingSettings.EnableEpicCosts) filtersDirty = true;

                GUILayout.Space(8); // Un peu plus d'espace avant les sliders
                GUILayout.BeginHorizontal();
                int oldMaxEnh = CraftingSettings.MaxEnhancementBonus;
                GUILayout.Label(string.Format(Helpers.GetString("ui_settings_max_enhancement", " Max Enhancement: +{0}"), CraftingSettings.MaxEnhancementBonus), settingsLabelStyle, GUILayout.Width(150 * scale));
                if (CButton("-", GUILayout.Width(30 * scale))) CraftingSettings.MaxEnhancementBonus--;
                CraftingSettings.MaxEnhancementBonus = (int)GUILayout.HorizontalSlider(CraftingSettings.MaxEnhancementBonus, 1, 20, GUILayout.Width(90 * scale));
                if (CButton("+", GUILayout.Width(30 * scale))) CraftingSettings.MaxEnhancementBonus++;
                if (oldMaxEnh != CraftingSettings.MaxEnhancementBonus) filtersDirty = true;
                GUILayout.EndHorizontal();

                GUILayout.Space(5);
                GUILayout.BeginHorizontal();
                int oldMaxTotal = CraftingSettings.MaxTotalBonus;
                GUILayout.Label(string.Format(Helpers.GetString("ui_settings_max_total", " Max Total: +{0}"), CraftingSettings.MaxTotalBonus), settingsLabelStyle, GUILayout.Width(150 * scale));
                if (CButton("-", GUILayout.Width(30 * scale))) CraftingSettings.MaxTotalBonus--;
                CraftingSettings.MaxTotalBonus = (int)GUILayout.HorizontalSlider(CraftingSettings.MaxTotalBonus, 1, 50, GUILayout.Width(90 * scale));
                if (CButton("+", GUILayout.Width(30 * scale))) CraftingSettings.MaxTotalBonus++;
                if (oldMaxTotal != CraftingSettings.MaxTotalBonus) filtersDirty = true;
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
            GUILayout.Label(sourceLabel, new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Italic, fontSize = (int)(FONT_NORMAL * scale) });
            GUILayout.EndHorizontal();

            GUILayout.Space(20);

            GUILayout.Label(Helpers.GetString("ui_settings_diagnostic", "Diagnostic Tools:"), settingsLabelStyle);
            GUILayout.Label(EnchantmentScanner.LastSyncMessage, settingsLabelStyle);
            if (CButton(Helpers.GetString("ui_settings_force_sync", "Force Synchronization (Full Scan)"), GUILayout.Height(35 * scale)))
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
            lock (EnchantmentScanner.MasterList)
            {
                rawAvailable = EnchantmentScanner.MasterList.ToList();
            }

            // 1. Filtrage par Type
            var typeFiltered = rawAvailable.Where(d => activeTypes.Contains(d.Type) || queuedEnchantGuids.Contains(d.Guid)).ToList();

            // 2. Préparation des helpers de filtrage
            bool isWeaponOrArmor = selectedItem.Blueprint is BlueprintItemWeapon || selectedItem.Blueprint is BlueprintItemArmor;
            var currentSelectedList = queuedEnchantGuids.Select(g => EnchantmentScanner.GetByGuid(g)).Where(d => d != null).ToList();
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
                var bp = data.Blueprint;
                if (bp == null) continue;

                // --- FILTRE DES ENCHANTEMENTS CUSTOM (Exclusion de la liste classique) ---
                if (CustomEnchantmentsBuilder.InjectedGuids.Contains(bp.AssetGuid)) continue;

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
            GUILayout.Label(activeDescriptionTitle, new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = (int)(FONT_LARGE * scale) });
            GUILayout.FlexibleSpace();
            if (CButton(Helpers.GetString("ui_btn_close_x", "X"), GUILayout.Width(30 * scale))) activeDescriptionPopup = "";
            GUILayout.EndHorizontal();

            GUILayout.Space(10);

            descriptionScrollPosition = GUILayout.BeginScrollView(descriptionScrollPosition, GUI.skin.box);
            GUILayout.Label(activeDescriptionPopup, new GUIStyle(GUI.skin.label) { wordWrap = true, richText = true, fontSize = (int)(FONT_NORMAL * scale) });
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
            GUIStyle warningStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = (int)(FONT_LARGE * scale),
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
            GUIStyle style = new GUIStyle(GUI.skin.button) { fontSize = (int)(FONT_NORMAL * scale) };
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

            if (isFocused && inputSubmitDown)
            {
                clicked = true;
                inputSubmitDown = false;
            }
            return clicked;
        }

        private bool CToggle(bool value, string text, params GUILayoutOption[] options)
        {
            float scale = CraftingSettings.ScalePercent / 100f;
            GUIStyle style = new GUIStyle(GUI.skin.label)
            {
                fontSize = (int)(FONT_NORMAL * scale),
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
            GUIStyle boxStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = (int)(FONT_LARGE * scale),
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

            if (isFocused && inputSubmitDown)
            {
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

            if (isFocused && Event.current.type == EventType.Repaint)
            {
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

        private void DrawTypeFilters(float scale)
        {
            GUILayout.BeginHorizontal();
            bool isWep = CToggle(activeTypes.Contains("Weapon"), Helpers.GetString("ui_filter_weapons", " Weapons"), GUILayout.Width(150 * scale));
            bool isArm = CToggle(activeTypes.Contains("Armor"), Helpers.GetString("ui_filter_armors", " Armors"), GUILayout.Width(150 * scale));
            bool isOth = CToggle(activeTypes.Contains("Other"), Helpers.GetString("ui_filter_others", " Others"), GUILayout.Width(150 * scale));

            if (isWep) activeTypes.Add("Weapon"); else activeTypes.Remove("Weapon");
            if (isArm) activeTypes.Add("Armor"); else activeTypes.Remove("Armor");
            if (isOth) activeTypes.Add("Other"); else activeTypes.Remove("Other");
            GUILayout.EndHorizontal();
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

        void DrawCustomEnchantmentGUI_Content(float scale)
        {
            // --- RAPPEL DES SÉLECTIONS CUSTOM ---
            var queuedCustoms = queuedEnchantGuids.Where(g => g.Replace("-", "").ToUpper().StartsWith(DynamicGuidHelper.Signature)).ToList();
            if (queuedCustoms.Count > 0)
            {
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label(Helpers.GetString("ui_custom_queued_title", "Custom Selection in queue:"), new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = (int)(FONT_NORMAL * scale) });
                foreach (var g in queuedCustoms)
                {
                    string displayName = Helpers.GetString("ui_custom_enchantment_placeholder", "Custom Enchantment");
                    EnchantmentData data = EnchantmentScanner.GetByGuid(g);
                    BlueprintItemEnchantment bp = null;

                    if (data != null)
                    {
                        displayName = data.Name;
                        bp = data.Blueprint;
                    }
                    else
                    {
                        bp = CustomEnchantmentsBuilder.GetOrBuildDynamicBlueprint(g) as BlueprintItemEnchantment;
                        if (bp != null) displayName = bp.Name ?? bp.name;
                    }

                    GUILayout.BeginHorizontal(GUI.skin.box);

                    // Nom et métadonnées
                    string metadata = "";
                    if (data != null)
                    {
                        metadata = $" <color=#E2C675>[+{data.PointString}]</color>";
                        if (data.IsEpic) metadata += " <color=#FF4500>(Epic)</color>";
                    }

                    GUILayout.Label(" • " + displayName + metadata, new GUIStyle(GUI.skin.label) { fontSize = (int)(FONT_NORMAL * scale), richText = true }, GUILayout.ExpandWidth(true));

                    // Bouton Description
                    if (bp != null)
                    {
                        DescriptionSource descSource = DescriptionSource.None;
                        string desc = DescriptionManager.GetLocalizedDescription(bp, data, out descSource);

                        GUIStyle infoStyle = new GUIStyle(GUI.skin.button);
                        infoStyle.fontSize = (int)(12 * scale);
                        string color = descSource == DescriptionSource.Generated ? "#88BBFF" : "#E2C675";
                        GUIContent infoContent = new GUIContent($"<color={color}>{Helpers.GetString("ui_btn_description", "Description")}</color>");

                        if (CButtonStyled(infoContent, infoStyle, GUILayout.Width(100 * scale), GUILayout.Height(20 * scale)))
                        {
                            activeDescriptionTitle = displayName;
                            activeDescriptionPopup = desc;
                        }
                    }

                    // -- AFFICHAGE DES SLOTS (AFFINITY) --
                    string expectedSlotsText = "";
                    if (data != null && data.Slots != null && data.Slots.Count > 0)
                    {
                        var localizedSlots = data.Slots.Select(s => Helpers.GetString("ui_slot_" + s.ToLower(), s));
                        expectedSlotsText = string.Join(", ", localizedSlots);
                    }
                    else if (data != null)
                    {
                        expectedSlotsText = Helpers.GetString("ui_slot_" + (data.Type?.ToLower() ?? "other"), data.Type ?? "Other");
                    }
                    GUILayout.Label($"<color=#2ecc71>[{expectedSlotsText}]</color>", new GUIStyle(GUI.skin.label) { richText = true, fontSize = (int)(FONT_TINY * scale), alignment = TextAnchor.MiddleCenter }, GUILayout.Width(120 * scale));

                    if (CButton(Helpers.GetString("ui_btn_remove", "Remove"), GUILayout.Width(80 * scale), GUILayout.Height(25 * scale)))
                    {
                        queuedEnchantGuids.Remove(g);
                        filtersDirty = true;
                    }
                    GUILayout.EndHorizontal();
                }
                GUILayout.EndVertical();
                GUILayout.Space(10);
            }

            if (selectedModel == null)
            {
                // --- MODE LISTE DES MODÈLES ---
                DrawTypeFilters(scale);
                GUILayout.Space(5);

                var models = CustomEnchantmentsBuilder.AllModels.Where(m => m.Type != "Feature").ToList();

                // Filtrage par type (basé sur les slots d'affinité)
                if (activeTypes.Count > 0)
                {
                    models = models.Where(m =>
                    {
                        bool isWeapon = m.Slots != null && m.Slots.Any(s => string.Equals(s, "Weapon", StringComparison.OrdinalIgnoreCase));
                        bool isArmor = m.Slots != null && m.Slots.Any(s => string.Equals(s, "Armor", StringComparison.OrdinalIgnoreCase) || string.Equals(s, "Shield", StringComparison.OrdinalIgnoreCase));
                        bool isOther = (m.Slots == null || m.Slots.Count == 0) || m.Slots.Any(s => 
                            !string.Equals(s, "Weapon", StringComparison.OrdinalIgnoreCase) && 
                            !string.Equals(s, "Armor", StringComparison.OrdinalIgnoreCase) && 
                            !string.Equals(s, "Shield", StringComparison.OrdinalIgnoreCase));

                        if (activeTypes.Contains("Weapon") && isWeapon) return true;
                        if (activeTypes.Contains("Armor") && isArmor) return true;
                        if (activeTypes.Contains("Other") && isOther) return true;
                        return false;
                    }).ToList();
                }

                if (models.Count == 0)
                {
                    GUILayout.Label(Helpers.GetString("ui_no_custom_models", "No enchantment models found."), new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = (int)(FONT_NORMAL * scale) });
                }
                else
                {
                    foreach (var model in models)
                    {
                        GUILayout.BeginHorizontal(GUI.skin.box);
                        GUILayout.Label(Helpers.GetLocalizedString(model.BaseName ?? model.NameCompleted), new GUIStyle(GUI.skin.label) { fontSize = (int)(FONT_NORMAL * scale) }, GUILayout.ExpandWidth(true));

                        // -- AFFICHAGE DES SLOTS (AFFINITY) --
                        string modelSlotsText = "";
                        if (model.Slots != null && model.Slots.Count > 0)
                        {
                            var localizedSlots = model.Slots.Select(s => Helpers.GetString("ui_slot_" + s.ToLower(), s));
                            modelSlotsText = string.Join(", ", localizedSlots);
                        }
                        else
                        {
                            modelSlotsText = Helpers.GetString("ui_slot_" + (model.Type?.ToLower() ?? "other"), model.Type ?? "Other");
                        }
                        GUILayout.Label($"<color=#2ecc71>[{modelSlotsText}]</color>", new GUIStyle(GUI.skin.label) { richText = true, fontSize = (int)(FONT_TINY * scale), alignment = TextAnchor.MiddleCenter }, GUILayout.Width(120 * scale));

                        if (CButton(Helpers.GetString("ui_btn_configure", "Configure"), GUILayout.Width(100 * scale), GUILayout.Height(20 * scale)))
                        {
                            selectedModel = model;
                            dynamicParamValues.Clear();
                            foreach (var p in model.DynamicParams)
                            {
                                int defVal = p.Min;
                                if (p.DefaultValue != null) 
                                {
                                    if (p.DefaultValue is long || p.DefaultValue is int)
                                    {
                                        defVal = Convert.ToInt32(p.DefaultValue);
                                    }
                                    else if (p.DefaultValue is string defStr && p.Type == "Enum" && !string.IsNullOrEmpty(p.EnumTypeName))
                                    {
                                        try {
                                            var enumType = Type.GetType(p.EnumTypeName);
                                            if (enumType != null) {
                                                defVal = (int)Enum.Parse(enumType, defStr, true);
                                            }
                                        } catch {
                                            Main.ModEntry.Logger.Error($"[ATELIER] Failed to parse DefaultValue '{defStr}' as enum '{p.EnumTypeName}'");
                                        }
                                    }
                                }
                                else if (p.Type == "Enum")
                                {
                                    // Sélection intelligente du défaut : première valeur valide après filtrage
                                    try {
                                        var enumType = Type.GetType(p.EnumTypeName);
                                        if (enumType != null) {
                                            var allNames = Enum.GetNames(enumType);
                                            var allValues = Enum.GetValues(enumType);
                                                for (int i = 0; i < allNames.Length; i++) {
                                                    string n = allNames[i];
                                                    if (p.EnumOnly != null && p.EnumOnly.Count > 0 && !p.EnumOnly.Any(eo => eo.Equals(n, StringComparison.OrdinalIgnoreCase))) continue;
                                                    if (p.EnumExclude != null && p.EnumExclude.Count > 0 && p.EnumExclude.Any(ee => ee.Equals(n, StringComparison.OrdinalIgnoreCase))) continue;
                                                    
                                                    defVal = (int)allValues.GetValue(i);
                                                    break;
                                                }
                                        }
                                    } catch {}
                                }
                                dynamicParamValues[p.Name] = defVal;
                            }
                        }
                        GUILayout.EndHorizontal();
                    }

                    GUILayout.Space(10);
                    GUILayout.BeginHorizontal(GUI.skin.box);
                    GUILayout.Label("<i>" + Helpers.GetString("ui_custom_todo_more", "TODO: there are more custom enchants to come in next releases") + "</i>", new GUIStyle(GUI.skin.label) { richText = true, fontSize = (int)(FONT_SMALL * scale), alignment = TextAnchor.MiddleCenter }, GUILayout.ExpandWidth(true));
                    GUILayout.EndHorizontal();
                }
            }
            else
            {
                // --- MODE CONFIGURATION ---
                GUILayout.BeginVertical(GUI.skin.box);
                if (CButton("<< " + Helpers.GetString("ui_btn_back_to_list", "Back to list"), GUILayout.Width(150 * scale)))
                {
                    selectedModel = null;
                }
                else
                {
                    GUILayout.Space(10);
                    GUILayout.Label(string.Format(Helpers.GetString("ui_configuring_model", "Configuring: <b>{0}</b>"), Helpers.GetLocalizedString(selectedModel.BaseName ?? selectedModel.NameCompleted)), new GUIStyle(GUI.skin.label) { richText = true, fontSize = (int)(FONT_LARGE * scale) });
                    GUILayout.Space(15);

                    GUIStyle paramLabelStyle = new GUIStyle(GUI.skin.label) { fontSize = (int)(FONT_NORMAL * scale) };
                    foreach (var p in selectedModel.DynamicParams)
                    {
                        GUILayout.BeginHorizontal();
                        GUILayout.Label(Helpers.GetString("ui_param_" + p.Name.ToLower().Replace(" ", "_"), p.Name) + ": ", paramLabelStyle, GUILayout.Width(150 * scale));

                        if (p.Type == "Slider")
                        {
                            int val = dynamicParamValues.ContainsKey(p.Name) ? dynamicParamValues[p.Name] : p.Min;
                            GUILayout.Label(val.ToString(), paramLabelStyle, GUILayout.Width(50 * scale));
                            int newVal = (int)GUILayout.HorizontalSlider(val, p.Min, p.Max);

                            // Snap to Step
                            if (p.Step > 1) newVal = (newVal / p.Step) * p.Step;
                            dynamicParamValues[p.Name] = newVal;
                        }
                        else if (p.Type == "Enum")
                        {
                            var enumType = Type.GetType(p.EnumTypeName);
                            if (enumType != null)
                            {
                                var allNames = Enum.GetNames(enumType);
                                var allValues = Enum.GetValues(enumType);

                                var filteredNames = new List<string>();
                                var filteredValues = new List<int>();

                                 // 1. On récupère les valeurs de l'Enum réel
                                 for (int i = 0; i < allNames.Length; i++)
                                 {
                                     string name = allNames[i];
                                     if (p.EnumOnly != null && p.EnumOnly.Count > 0 && !p.EnumOnly.Any(eo => eo.Equals(name, StringComparison.OrdinalIgnoreCase))) continue;
                                     if (p.EnumExclude != null && p.EnumExclude.Count > 0 && p.EnumExclude.Any(ee => ee.Equals(name, StringComparison.OrdinalIgnoreCase))) continue;

                                     filteredNames.Add(name);
                                     filteredValues.Add((int)allValues.GetValue(i));
                                 }

                                 // 2. On ajoute les entrées virtuelles de EnumOverrides (ex: SaveAll)
                                 if (p.EnumOverrides != null)
                                 {
                                     foreach (var kvp in p.EnumOverrides)
                                     {
                                         if (!filteredNames.Contains(kvp.Key))
                                         {
                                             // Si on a un filtre EnumOnly, l'entrée virtuelle doit y être listée
                                             if (p.EnumOnly != null && p.EnumOnly.Count > 0 && !p.EnumOnly.Any(eo => eo.Equals(kvp.Key, StringComparison.OrdinalIgnoreCase))) continue;

                                             // On cherche la valeur numérique dans le JObject de l'override
                                             if (kvp.Value is Newtonsoft.Json.Linq.JObject jo && jo["Value"] != null)
                                             {
                                                 filteredNames.Add(kvp.Key);
                                                 filteredValues.Add((int)jo["Value"]);
                                             }
                                         }
                                     }
                                 }

                                var names = filteredNames.ToArray();
                                var values = filteredValues.ToArray();

                                // Si un seul choix possible, on l'affiche simplement et on le sélectionne d'office
                                if (names.Length == 1)
                                {
                                    string displayName = names[0];
                                    // Application de la surcharge de nom pour le label aussi
                                    if (p.EnumOverrides != null && p.EnumOverrides.TryGetValue(names[0], out object ovrObj))
                                    {
                                        displayName = Helpers.GetLocalizedString(ovrObj);
                                    }
                                    else
                                    {
                                        if (p.EnumTypeName.Contains("DamageEnergyType"))
                                            displayName = Helpers.GetString("energy_" + names[0], names[0]);
                                        else
                                            displayName = Helpers.GetString("ui_enum_" + names[0], names[0]);
                                    }

                                    GUILayout.Label(displayName, paramLabelStyle);
                                    dynamicParamValues[p.Name] = values[0];
                                }
                                else
                                {
                                    int currentVal = dynamicParamValues.ContainsKey(p.Name) ? dynamicParamValues[p.Name] : 0;
                                    string currentName = Enum.GetName(enumType, currentVal);
                                    
                                    // Support des noms virtuels (SaveAll...)
                                    if (string.IsNullOrEmpty(currentName) && p.EnumOverrides != null)
                                    {
                                        foreach (var kvp in p.EnumOverrides)
                                        {
                                            if (kvp.Value is Newtonsoft.Json.Linq.JObject jo && jo["Value"] != null && (int)jo["Value"] == currentVal)
                                            {
                                                currentName = kvp.Key;
                                                break;
                                            }
                                        }
                                    }

                                    string displayName = currentName ?? "";
                                    if (p.EnumTypeName.Contains("DamageEnergyType"))
                                        displayName = Helpers.GetString("energy_" + displayName, displayName);
                                    else
                                        displayName = Helpers.GetString("ui_enum_" + displayName, displayName);
 
                                    if (p.EnumOverrides != null && !string.IsNullOrEmpty(currentName) && p.EnumOverrides.TryGetValue(currentName, out object overrideObj))
                                        displayName = Helpers.GetLocalizedString(overrideObj);

                                    if (CButton(displayName, GUILayout.Width(200 * scale)))
                                    {
                                        openDropdownParam = (openDropdownParam == p.Name) ? null : p.Name;
                                    }

                                    if (openDropdownParam == p.Name)
                                    {
                                        // Afficher les options (on ferme le horizontal pour la liste)
                                        GUILayout.EndHorizontal();
                                        GUILayout.BeginVertical(GUI.skin.box);
                                        for (int i = 0; i < names.Length; i++)
                                        {
                                            string optName = names[i];
                                            if (p.EnumTypeName.Contains("DamageEnergyType"))
                                                optName = Helpers.GetString("energy_" + optName, optName);
                                            else
                                                optName = Helpers.GetString("ui_enum_" + optName, optName);

                                            // Application de la surcharge de nom (EnumOverrides)
                                            if (p.EnumOverrides != null && p.EnumOverrides.TryGetValue(names[i], out object optOverrideObj))
                                            {
                                                optName = Helpers.GetLocalizedString(optOverrideObj);
                                            }

                                            if (CButton(optName))
                                            {
                                                dynamicParamValues[p.Name] = (int)values[i];
                                                openDropdownParam = null;
                                            }
                                        }
                                        GUILayout.EndVertical();
                                        GUILayout.BeginHorizontal(); // On réouvre pour la suite
                                    }
                                }
                            }
                        }
                        GUILayout.EndHorizontal();
                        GUILayout.Space(5);
                    }

                    GUILayout.Space(20);

                    // On extrait les valeurs dans l'ordre EXACT des paramètres du modèle
                    int[] orderedValues = selectedModel.DynamicParams
                        .Select(p => dynamicParamValues.ContainsKey(p.Name) ? dynamicParamValues[p.Name] : 0)
                        .ToArray();

                    // --- CALCUL DU MASQUE BINAIRE DYNAMIQUE ---
                    int mask = 0;
                    bool hasMaskControl = false;
                    foreach (var p in selectedModel.DynamicParams)
                    {
                        if (p.Type == "Enum" && !string.IsNullOrEmpty(p.EnumTypeName) && p.EnumOverrides != null)
                        {
                            int val = dynamicParamValues.ContainsKey(p.Name) ? dynamicParamValues[p.Name] : 0;
                            try {
                                var enumType = Type.GetType(p.EnumTypeName);
                                if (enumType != null) {
                                    string enumName = Enum.GetName(enumType, val);
                                    if (!string.IsNullOrEmpty(enumName) && p.EnumOverrides.TryGetValue(enumName, out object ovr)) {
                                        if (ovr is Newtonsoft.Json.Linq.JObject jo && jo["MaskValue"] != null) {
                                            mask |= (int)jo["MaskValue"];
                                            hasMaskControl = true;
                                        }
                                    }
                                }
                            } catch {}
                        }
                    }
                    if (!hasMaskControl) mask = 0xFFF; // Compatibilité : tout actif si pas de masque défini

                    var tempGuid = DynamicGuidHelper.GenerateGuid(selectedModel.EnchantId, orderedValues, selectedModel.Type == "Feature", mask);
                    var tempEnch = EnchantmentScanner.GetByGuid(tempGuid.ToString());

                    long totalCost = 0;
                    int totalPoints = 0;
                    if (tempEnch != null)
                    {
                        totalCost = CraftingCalculator.GetEnchantmentCost(selectedItem, tempEnch, CraftingSettings.CostMultiplier);
                        totalPoints = tempEnch.PointCost;
                    }
                    int totalDays = CraftingCalculator.GetCraftingDays(totalCost, CraftingSettings.InstantCrafting);

                    GUILayout.BeginHorizontal();
                    GUILayout.Label(string.Format(Helpers.GetString("ui_selection_total_custom", "Selection Custom: +{0} \u2014 Total: {1} gp / ~{2} d"), totalPoints, totalCost, totalDays), new GUIStyle(GUI.skin.label) { fontSize = (int)(FONT_NORMAL * scale) });
                    GUILayout.FlexibleSpace();

                    if (CButton(Helpers.GetString("ui_btn_add_to_selection", "Add to selection"), GUILayout.Width(250 * scale), GUILayout.Height(40 * scale)))
                    {
                        try
                        {
                            var finalGuid = DynamicGuidHelper.GenerateGuid(selectedModel.EnchantId, orderedValues, selectedModel.Type == "Feature", mask).ToString();
                            if (!queuedEnchantGuids.Contains(finalGuid))
                            {
                                queuedEnchantGuids.Add(finalGuid);
                                // On s'assure que le blueprint est généré pour l'affichage
                                CustomEnchantmentsBuilder.GetOrBuildDynamicBlueprint(finalGuid);
                            }
                            selectedModel = null;
                            filtersDirty = true;
                        }
                        catch (Exception ex)
                        {
                            Main.ModEntry.Logger.Error($"Error adding custom enchantment: {ex}");
                        }
                    }
                    GUILayout.EndHorizontal();
                }
                GUILayout.EndVertical();
            }
        }

        void DrawEnchantmentRow(EnchantmentData data, float scale, int relativeIndex)
        {
            bool isQueued = queuedEnchantGuids.Contains(data.Guid);
            var currentSelectedList = queuedEnchantGuids.Select(g => EnchantmentScanner.GetByGuid(g)).Where(d => d != null).ToList();

            var bp = data.Blueprint;
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

            string label = $"<size={(int)(FONT_NORMAL * scale)}>{displayName}</size> <color=#888888><size={(int)(FONT_SMALL * scale)}>({internalName})</size></color>";
            bool newSelected = CToggleStyled(isQueued, label, toggleStyle, GUILayout.ExpandWidth(true));

            DescriptionSource descSource = DescriptionSource.None;
            string descForData = DescriptionManager.GetLocalizedDescription(bp, data, out descSource);

            string color;
            if (!string.IsNullOrEmpty(descForData) && descSource == DescriptionSource.Official)
            {
                color = "#3498db"; // Bleu
            }
            else if (!string.IsNullOrEmpty(descForData) && descSource == DescriptionSource.Generated)
            {
                color = "#f1c40f"; // Jaune
            }
            else
            {
                color = "#e74c3c"; // Rouge
                if (string.IsNullOrEmpty(descForData)) descForData = Helpers.GetString("ui_desc_needed", "TODO: Description needed for this enchantment.");
            }

            GUIContent infoContent = new GUIContent($"<color={color}>{Helpers.GetString("ui_btn_description", "Description")}</color>");
            GUIStyle infoStyle = new GUIStyle(GUI.skin.button)
            {
                richText = true,
                fontStyle = FontStyle.Bold,
                fontSize = (int)(FONT_TINY * scale),
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(2, 2, 0, 0)
            };

            if (CButtonStyled(infoContent, infoStyle, GUILayout.Width(100 * scale), GUILayout.Height(20 * scale)))
            {
                activeDescriptionTitle = displayName;
                activeDescriptionPopup = descForData;
            }

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

            GUILayout.Label($"<color={slotColor}>[{expectedSlotsText}]</color>", new GUIStyle(GUI.skin.label) { richText = true, fontSize = (int)(FONT_TINY * scale), alignment = TextAnchor.MiddleCenter }, GUILayout.Width(120 * scale));

            string currency = Helpers.GetString("ui_currency_gp", "gp");
            string daysLabel = Helpers.GetString("ui_time_days_short", "d");
            GUILayout.Label($"{costToPay} {currency} / {days} {daysLabel}   (+{data.PointCost})", new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleRight, fontSize = (int)(FONT_NORMAL * scale) }, GUILayout.Width(180 * scale));

            if (newSelected && !isQueued)
            {
                string baseName = CraftingCalculator.GetEnchantmentFamily(internalName);
                if (!string.IsNullOrEmpty(baseName))
                {
                    queuedEnchantGuids.RemoveAll(guid =>
                    {
                        var otherData = EnchantmentScanner.GetByGuid(guid);
                        if (otherData != null)
                        {
                            var otherBp = otherData.Blueprint;
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
