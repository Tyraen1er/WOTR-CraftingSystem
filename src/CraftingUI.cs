using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Kingmaker;
using Kingmaker.Items;
using Kingmaker.Blueprints.Items;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Facts;
using Kingmaker.Blueprints.Items.Weapons;
using Kingmaker.Blueprints.Items.Armors;
using Kingmaker.Blueprints.Items.Shields;
using Kingmaker.Blueprints.Items.Equipment;
using Kingmaker.Blueprints.Items.Ecnchantments;
using Kingmaker.UI;
using Kingmaker.UI.Common;
using Kingmaker.View;

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

                // Blocage/Déblocage de la caméra Rig (mouvement/zoom)
                try
                {
                    var rig = Game.Instance.UI.GetCameraRig();
                    if (rig != null)
                    {
                        rig.FixCamera.Value = _isOpen;
                    }
                }
                catch (Exception e)
                {
                    Main.ModEntry.Logger.Error($"[UI] Erreur lors du blocage caméra: {e.Message}");
                }

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
                    showIconBrowser = false;
                    showDescriptionEditor = false;
                    editedDescription = "";
                    scrollPosition = Vector2.zero;
                    titleScrollPosition = Vector2.zero;
                    descriptionScrollPosition = Vector2.zero;
                    activeDescriptionPopup = "";
                    currentPage = 0;
                    currentPageType = CraftingPage.MainMenu;
                    itemsPerPageInput = CraftingSettings.Instance.ItemsPerPage.ToString();
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

        // Description Editor State
        private bool showDescriptionEditor = false;
        private string editedDescription = "";

        // Scroll Creation State
        private string scrollSearch = "";
        private SpellData selectedScrollSpell = null;
        private int scrollCasterLevel = 1;
        private int scrollSpellLevel = 1;
        private Vector2 scrollListPos = Vector2.zero;
        private Vector2 metamagicScrollPos = Vector2.zero;
        private int selectedAlteration = 0;
        private bool showIconBrowser = false;
        private Vector2 iconScrollPos = Vector2.zero;

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
            CreateScroll,
            CreatePotion
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

            // Détection de la saisie pour bloquer les raccourcis du jeu
            bool isTyping = GUIUtility.keyboardControl != 0;
            try
            {
                if (Game.Instance.Keyboard != null)
                {
                    // On utilise le CountingGuard pour bloquer/débloquer sans écraser d'autres bloqueurs
                    if (isTyping && Game.Instance.Keyboard.Disabled.GuardCount == 0) Game.Instance.Keyboard.Disabled.Value = true;
                    else if (!isTyping && Game.Instance.Keyboard.Disabled.GuardCount > 0) Game.Instance.Keyboard.Disabled.Value = false;
                }
            }
            catch { }

            if (UnityEngine.Input.anyKeyDown)
            {
                foreach (KeyCode kcode in Enum.GetValues(typeof(KeyCode)))
                {
                    if (UnityEngine.Input.GetKeyDown(kcode))
                    {
                        // Main.ModEntry.Logger.Log($"[UI-INPUT] Key: {kcode} | Focused: {isTyping}");
                    }
                }
            }

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
                if (Time.unscaledTime - lastInputTime > 0.1f)
                {
                    int oldIndex = currentFocusIndex;
                    if (v > 0.5f || h < -0.5f) currentFocusIndex--;
                    else if (v < -0.5f || h > 0.5f) currentFocusIndex++;
                    lastInputTime = Time.unscaledTime;

                    if (currentFocusIndex < 0) currentFocusIndex = 0;
                    if (maxFocusIndex > 0 && currentFocusIndex >= maxFocusIndex) currentFocusIndex = maxFocusIndex - 1;

                    if (oldIndex != currentFocusIndex && focusedRect != Rect.zero)
                    {
                        float estimatedViewHeight = REFERENCE_HEIGHT * (CraftingSettings.Instance.ScalePercent / 100f) * 0.5f; // Rough estimate of scrollview
                        float padding = 50f * (CraftingSettings.Instance.ScalePercent / 100f);

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

        private ItemEntity doubleWeaponCandidate = null;
        private bool showDoubleWeaponChoice = false;

        void Awake()
        {
            Instance = this;
            CraftingSettings.Load(Main.ModEntry);
        }

        private void UpdateAutoScale()
        {
            float resScale = Math.Min(Screen.width / REFERENCE_WIDTH, Screen.height / REFERENCE_HEIGHT);
            float dpiScale = 1f;
            try { if (Screen.dpi > 0) dpiScale = Screen.dpi / 96f; } catch { dpiScale = 1f; }
            float finalScale = Mathf.Clamp(resScale * dpiScale * 1.5f, 0.75f, 3.0f);
            CraftingSettings.Instance.ScalePercent = (int)(finalScale * 100f);
        }


        void OnGUI()
        {
            if (Event.current.type == EventType.Layout) maxFocusIndex = processIndex;
            processIndex = 0;

            if (inputCancelDown)
            {
                if (showDescriptionEditor) showDescriptionEditor = false;
                else if (!string.IsNullOrEmpty(activeDescriptionPopup)) activeDescriptionPopup = "";
                else if (selectedItem != null)
                {
                    selectedItem = null;
                    newNameDraft = "";
                    queuedEnchantGuids.Clear();
                    activeCategories.Clear();
                    activeTypes.Clear();
                    showCategoryFilter = false;
                    showIconBrowser = false;
                }
                else if (showDoubleWeaponChoice)
                {
                    showDoubleWeaponChoice = false;
                    doubleWeaponCandidate = null;
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
                // Sécurité : on s'assure que le clavier est débloqué si on ferme brusquement
                try { if (Game.Instance.Keyboard != null && Game.Instance.Keyboard.Disabled.GuardCount > 0) Game.Instance.Keyboard.Disabled.Value = false; } catch { }
                return;
            }

            if (!lastOpenState)
            {
                lastOpenState = true;
            }

            UpdateAutoScale();
            processIndex = 0;
            inputSubmitDown = false;

            if (Event.current.type == EventType.Layout)
            {
                EnchantmentScanner.StartSync();
            }

            var workshop = Game.Instance.Player.MainCharacter.Value.Get<UnitPartWilcerWorkshop>();
            // workshop?.CheckAndFinishProjects(); // Defer to enchant button as requested

            float scale = CraftingSettings.Instance.ScalePercent / 100f;
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

            // --- FENÊTRE D'ÉDITION DE DESCRIPTION (POPUP) ---
            if (showDescriptionEditor)
            {
                float pW = 800f * scale;
                float pH = 600f * scale;
                Rect popupRect = new Rect((Screen.width - pW) / 2f, (Screen.height - pH) / 2f, pW, pH);

                GUI.color = new Color(0.3f, 0.3f, 0.3f, 1.0f);
                GUI.DrawTexture(popupRect, Texture2D.whiteTexture);
                GUI.color = oldGUIColor;

                GUI.Window(997, popupRect, DrawDescriptionEditor, "");
                DrawBorder(popupRect, 2f, Color.gray);
                GUI.BringWindowToFront(997);
            }

            GUI.backgroundColor = oldColor;

            int focusId = 999;
            if (showDescriptionEditor) focusId = 997;
            else if (!string.IsNullOrEmpty(activeDescriptionPopup)) focusId = 998;

            GUI.FocusWindow(focusId);
        }

        void DrawDoubleWeaponChoice(int id)
        {
            float scale = CraftingSettings.Instance.ScalePercent / 100f;
            GUILayout.BeginVertical();

            GUIStyle textStyle = new GUIStyle(GUI.skin.label)
            {
                wordWrap = true,
                alignment = TextAnchor.MiddleCenter,
                fontSize = (int)(FONT_LARGE * scale),
                richText = true
            };

            GUILayout.Label(Helpers.GetString("ui_double_weapon_choice", "Le jeu considère que les armes doubles sont 2 armes distinctes, choisissez quelle partie souhaitez vous enchanter"), textStyle);
            GUILayout.Space(30 * scale);

            GUILayout.BeginHorizontal();
            if (CButton(Helpers.GetString("ui_btn_primary_weapon", "Arme principale"), GUILayout.Height(60 * scale)))
            {
                Main.ModEntry.Logger.Log($"[UI] Double weapon: selecting primary part {doubleWeaponCandidate.Name}");
                FinalizeSelection(doubleWeaponCandidate);
                showDoubleWeaponChoice = false;
                doubleWeaponCandidate = null;
                GUI.FocusWindow(999);
            }
            GUILayout.Space(20 * scale);
            if (CButton(Helpers.GetString("ui_btn_secondary_weapon", "Arme secondaire"), GUILayout.Height(60 * scale)))
            {
                // Récupération de la seconde partie (Field 'Second' sur ItemEntityWeapon ou Property 'SecondWeapon')
                var type = doubleWeaponCandidate.GetType();
                var secondField = type.GetField("Second", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                var secondProp = type.GetProperty("SecondWeapon", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);

                var secondWeapon = (secondField?.GetValue(doubleWeaponCandidate) ?? secondProp?.GetValue(doubleWeaponCandidate)) as ItemEntity;

                Main.ModEntry.Logger.Log($"[UI] Double weapon: selecting secondary part {secondWeapon?.Name ?? "null"}");
                FinalizeSelection(secondWeapon ?? doubleWeaponCandidate);
                showDoubleWeaponChoice = false;
                doubleWeaponCandidate = null;
                GUI.FocusWindow(999);
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(20 * scale);
            if (CButton(Helpers.GetString("ui_btn_cancel", "Annuler"), GUILayout.Height(30 * scale)))
            {
                showDoubleWeaponChoice = false;
                doubleWeaponCandidate = null;
            }

            GUILayout.EndVertical();
        }

        void FinalizeSelection(ItemEntity it)
        {
            if (it == null) return;
            Main.ModEntry.Logger.Log($"[UI] Finalizing selection for: {it.Name} (Type: {it.GetType().Name})");
            selectedItem = it;
            newNameDraft = it.Name;
            queuedEnchantGuids.Clear();
            activeCategories.Clear();
            showCategoryFilter = false;

            activeTypes.Clear();
            if (it.Blueprint is BlueprintItemWeapon) activeTypes.Add("Weapon");
            else if (it.Blueprint is BlueprintItemShield) { activeTypes.Add("Armor"); activeTypes.Add("Weapon"); }
            else if (it.Blueprint is BlueprintItemArmor) activeTypes.Add("Armor");
            else activeTypes.Add("Other");

            filtersDirty = true;
            currentPage = 0;
        }

        void DrawWindowContent(int windowID)
        {
            float scale = CraftingSettings.Instance.ScalePercent / 100f;
            float windowWidth = 1000f * scale;
            float windowHeight = Mathf.Min(900f * scale, Screen.height * 0.9f);

            // Force l'opacité interne de la fenêtre principale
            Color oldColor = GUI.color;
            GUI.color = new Color(0.3f, 0.3f, 0.3f, 1.0f);
            GUI.DrawTexture(new Rect(0, 0, 1000f * scale, Mathf.Min(900f * scale, Screen.height * 0.9f)), Texture2D.whiteTexture);
            GUI.color = oldColor;

            if (showDoubleWeaponChoice)
            {
                DrawDoubleWeaponChoice(windowID);
                return;
            }

            if (!string.IsNullOrEmpty(feedbackMessage))
            {
                // Overlay sombre sur toute la fenêtre pour focaliser l'attention
                Color oldGuiColorOverlay = GUI.color;
                GUI.color = new Color(0, 0, 0, 0.6f);
                GUI.DrawTexture(new Rect(0, 0, windowWidth, windowHeight), Texture2D.whiteTexture);
                GUI.color = oldGuiColorOverlay;

                float popupWidth = 600f * scale;
                float popupHeight = 220f * scale;
                Rect feedbackRect = new Rect((windowWidth - popupWidth) / 2f, (windowHeight - popupHeight) / 2f, popupWidth, popupHeight);

                // Fond du popup (Gris très sombre dégradé simulé)
                DrawRectBorder(feedbackRect, 2, new Color(0.6f, 0.5f, 0.2f, 1f)); // Bordure dorée/bronze
                GUI.color = new Color(0.12f, 0.12f, 0.12f, 1.0f);
                GUI.DrawTexture(feedbackRect, Texture2D.whiteTexture);
                GUI.color = Color.white;

                GUILayout.BeginArea(feedbackRect);
                GUILayout.Space(35 * scale);

                GUIStyle msgStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    wordWrap = true,
                    fontSize = (int)(FONT_HUGE * scale),
                    richText = true,
                    fontStyle = FontStyle.Normal,
                    normal = { textColor = new Color(0.9f, 0.9f, 0.9f) }
                };
                GUILayout.Label(feedbackMessage, msgStyle);

                GUILayout.FlexibleSpace();

                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                // Bouton OK stylisé
                if (CButton(Helpers.GetString("ui_btn_ok", "OK"), GUILayout.Width(140 * scale), GUILayout.Height(45 * scale)))
                    feedbackMessage = "";
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                GUILayout.Space(25 * scale);
                GUILayout.EndArea();

                return;
            }

            // --- HEADER AVEC DÉFILEMENT POUR TEXTE LONG ---
            GUILayout.BeginHorizontal();

            string title = Helpers.GetString("ui_title_workshop", "Workshop");
            if (ShowSettings) title = Helpers.GetString("ui_title_config", "Configuration");
            else if (selectedItem != null) title = Helpers.GetString("ui_title_details", "Details: ") + selectedItem.Name;
            else
            {
                switch (currentPageType)
                {
                    case CraftingPage.MainMenu: title = Helpers.GetString("ui_title_main_menu", "Main Menu"); break;
                    case CraftingPage.WorkshopInventory: title = Helpers.GetString("ui_title_select", "Item Selection"); break;
                    case CraftingPage.CreateWeapon: title = Helpers.GetString("ui_menu_create_weapon", "Create Weapon"); break;
                    case CraftingPage.CreateArmor: title = Helpers.GetString("ui_menu_create_armor", "Create Armor"); break;
                    case CraftingPage.CreateAccessory: title = Helpers.GetString("ui_menu_create_accessory", "Create Accessory"); break;
                    case CraftingPage.CreateMetamagicRod: title = Helpers.GetString("ui_menu_create_rod", "Create Metamagic Rod"); break;
                    case CraftingPage.CreateWand: title = Helpers.GetString("ui_menu_create_wand", "Create Wand"); break;
                    case CraftingPage.CreateScroll: title = Helpers.GetString("ui_menu_create_scroll", "Create Scroll"); break;
                    case CraftingPage.CreatePotion: title = Helpers.GetString("ui_menu_create_potion", "Create Potion"); break;
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
                    if (selectedItem != null)
                    {
                        if (showIconBrowser)
                        {
                            showIconBrowser = false;
                        }
                        else
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
                    else
                    {
                        currentPageType = CraftingPage.MainMenu;
                    }
                }
            }

            windowWidth = 800f * scale;
            float optionWidth = Mathf.Max(CraftingSettings.BUTTON_OPTION_WIDTH_BASE * scale, windowWidth * 0.14f);
            float closeWidth = Mathf.Max(CraftingSettings.BUTTON_CLOSE_WIDTH_BASE * scale, windowWidth * 0.06f);

            string optLabel = ShowSettings ? Helpers.GetString("ui_btn_workshop_short", "Workshop") : Helpers.GetString("ui_btn_cheats", "Options");
            if (CButtonStyled(new GUIContent(optLabel), navStyle, GUILayout.Width(optionWidth), GUILayout.Height(35 * scale)))
            {
                if (!ShowSettings && !CraftingSettings.Instance.HasOpenedCheats)
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
            else
            {
                switch (currentPageType)
                {
                    case CraftingPage.MainMenu: DrawMainMenuGUI(scale); break;
                    case CraftingPage.WorkshopInventory: DrawInventoryGUI(scale); break;
                    case CraftingPage.CreateWeapon: DrawCreateWeaponGUI(scale); break;
                    case CraftingPage.CreateArmor: DrawCreateArmorGUI(scale); break;
                    case CraftingPage.CreateAccessory: DrawCreateAccessoryGUI(scale); break;
                    case CraftingPage.CreateMetamagicRod: DrawCreateMetamagicRodGUI(scale); break;
                    case CraftingPage.CreateWand: DrawCreateWandGUI(scale); break;
                    case CraftingPage.CreateScroll: DrawCreateScrollGUI(scale); break;
                    case CraftingPage.CreatePotion: DrawCreatePotionGUI(scale); break;
                }
            }
        }

        void DrawMainMenuGUI(float scale)
        {
            scrollPosition = GUILayout.BeginScrollView(scrollPosition, GUILayout.ExpandHeight(true));

            float windowWidth = 1000f * scale;
            float contentWidth = windowWidth - (120f * scale);

            GUIStyle sectionHeaderStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = (int)(FONT_LARGE * scale),
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter, // Centré
                normal = { textColor = new Color(0.9f, 0.8f, 0.4f) }
            };

            GUIStyle btnStyle = new GUIStyle(GUI.skin.button)
            {
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

            float colWidth4 = (contentWidth / 4f) - (10 * scale);
            Color magicTint = new Color(0.4f, 0.2f, 0.6f);

            GUILayout.BeginHorizontal();
            DrawMenuButton(new GUIContent("<b>🪄 " + Helpers.GetString("ui_menu_create_rod", "Rod") + "</b>"), btnStyle, CraftingPage.CreateMetamagicRod, magicTint, GUILayout.Width(colWidth4));
            GUILayout.Space(10 * scale);
            DrawMenuButton(new GUIContent("<b>⚡ " + Helpers.GetString("ui_menu_create_wand", "Wand") + "</b>"), btnStyle, CraftingPage.CreateWand, magicTint, GUILayout.Width(colWidth4));
            GUILayout.Space(10 * scale);
            DrawMenuButton(new GUIContent("<b>📜 " + Helpers.GetString("ui_menu_create_scroll", "Scroll") + "</b>"), btnStyle, CraftingPage.CreateScroll, magicTint, GUILayout.Width(colWidth4));
            GUILayout.Space(10 * scale);
            DrawMenuButton(new GUIContent("<b>🧪 " + Helpers.GetString("ui_menu_create_potion", "Potion") + "</b>"), btnStyle, CraftingPage.CreatePotion, magicTint, GUILayout.Width(colWidth4));
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
            if (CButtonStyled(content, style, options))
            {
                if (targetPage == CraftingPage.WorkshopInventory)
                {
                    var workshop = Game.Instance.Player.MainCharacter.Value.Get<UnitPartWilcerWorkshop>();
                    workshop?.CheckAndFinishProjects();
                    workshop?.SyncFromBox(); // Crucial pour actualiser la liste même si aucun projet n'est fini
                }
                // Désactivation temporaire pour les accessoires (TODO)
                if (targetPage != CraftingPage.CreateAccessory)
                    currentPageType = targetPage;
            }
            GUI.backgroundColor = oldBG;
        }

        private void DrawSeparator(float width, Color color)
        {
            Rect rect = GUILayoutUtility.GetRect(width, 2f);
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        void DrawCreateWeaponGUI(float scale) { DrawItemBrowser(scale, ItemScanner.Weapons, Helpers.GetString("ui_menu_weapon_workshop", "Weapon Workshop")); }
        void DrawCreateArmorGUI(float scale) { DrawItemBrowser(scale, ItemScanner.Armors, Helpers.GetString("ui_menu_armor_workshop", "Armor Workshop")); }
        void DrawCreateAccessoryGUI(float scale) { DrawItemBrowser(scale, ItemScanner.Accessories, Helpers.GetString("ui_menu_accessory_workshop", "Accessory Workshop")); }

        private void DrawItemBrowser(float scale, List<ItemData> items, string title)
        {
            float windowWidth = 1000f * scale;
            float contentWidth = windowWidth - (120f * scale);

            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.BeginVertical(GUILayout.Width(contentWidth));

            GUILayout.Space(20 * scale);
            GUIStyle headerStyle = new GUIStyle(GUI.skin.label) { fontSize = (int)(FONT_HUGE * scale), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            GUILayout.Label(string.Format(Helpers.GetString("ui_page_forge_title", "{0}"), title), headerStyle);
            GUILayout.Space(10 * scale);

            // --- NOUVEAU : SÉLECTION D'ALTÉRATION GLOBALE (+0 à +5) ---
            if (currentPageType == CraftingPage.CreateWeapon || currentPageType == CraftingPage.CreateArmor)
            {
                GUILayout.BeginHorizontal("box");
                GUILayout.Label("<b>" + Helpers.GetString("ui_enhancement_level", "Enhancement Level:") + "</b>", new GUIStyle(GUI.skin.label) { richText = true, fontSize = (int)(FONT_NORMAL * scale) }, GUILayout.Width(180 * scale));
                for (int i = 0; i <= 5; i++)
                {
                    Color oldBG = GUI.backgroundColor;
                    if (selectedAlteration == i) GUI.backgroundColor = Color.cyan;
                    if (CButton($"+{i}", GUILayout.Width(50 * scale), GUILayout.Height(30 * scale)))
                    {
                        selectedAlteration = i;
                    }
                    GUI.backgroundColor = oldBG;
                    GUILayout.Space(5 * scale);
                }
                GUILayout.EndHorizontal();
                GUILayout.Space(10 * scale);
            }

            scrollPosition = GUILayout.BeginScrollView(scrollPosition, "box");
            if (items == null || items.Count == 0)
            {
                GUILayout.Label(Helpers.GetString("ui_no_items_found", "No items found. Please run a scan."), new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter });
            }
            else
            {
                int index = 0;
                foreach (var item in items)
                {
                    // NOUVEAU : On cache l'item si la variante pour le niveau sélectionné (+0, +1...) n'existe pas
                    if (!item.VariantGuids.ContainsKey(selectedAlteration)) continue;

                    index++;
                    Color oldBG = GUI.backgroundColor;
                    // Zebra stripping : alternance de couleur une ligne sur deux
                    if (index % 2 == 0) GUI.backgroundColor = new Color(0.8f, 0.8f, 0.9f, 0.15f);

                    GUILayout.BeginHorizontal("box");
                    GUI.backgroundColor = oldBG;

                    if (item.Icon != null)
                    {
                        DrawSprite(item.GetIcon(selectedAlteration), 40 * scale);
                    }

                    GUILayout.BeginVertical();
                    GUILayout.Label($"<b>{item.GetDisplayName(selectedAlteration)}</b>", new GUIStyle(GUI.skin.label) { richText = true, fontSize = (int)(FONT_NORMAL * scale) });
                    GUILayout.Label(string.Format(Helpers.GetString("ui_item_cost", "Cost: {0} GP"), item.GetCost(selectedAlteration)), new GUIStyle(GUI.skin.label) { fontSize = (int)(FONT_SMALL * scale) });
                    GUILayout.EndVertical();

                    GUILayout.FlexibleSpace();

                    // Bouton d'achat dynamique basé sur l'altération sélectionnée
                    string targetGuid = item.GetGuid(selectedAlteration);
                    bool canBuy = !string.IsNullOrEmpty(targetGuid);
                    int currentCost = item.GetCost(selectedAlteration);

                    GUI.enabled = canBuy;

                    // Bouton 1 : Vers l'établi (Workshop)
                    if (CButton(Helpers.GetString("ui_btn_buy_workshop", "WORKSHOP"), GUILayout.Width(100 * scale), GUILayout.Height(40 * scale)))
                    {
                        BuyItem(targetGuid, currentCost, false);
                    }

                    GUILayout.Space(5 * scale);

                    // Bouton 2 : Directement dans l'inventaire
                    if (CButton(Helpers.GetString("ui_btn_buy_inventory", "INVENTORY"), GUILayout.Width(100 * scale), GUILayout.Height(40 * scale)))
                    {
                        BuyItem(targetGuid, currentCost, true);
                    }

                    GUI.enabled = true;

                    GUILayout.EndHorizontal();
                }
            }
            GUILayout.EndScrollView();

            GUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        void DrawCreateWandGUI(float scale)
        {
            DrawMagicItemGUI(scale, Helpers.GetString("ui_menu_wand_workshop", "Wand Workshop"), 750, 50, (s, cl, sl) => CustomEnchantmentsBuilder.GetOrBuildWand(s, cl, sl), WandFilter, CraftingSettings.Instance.ApplyWandRestrictions ? 4 : 9);
        }

        private bool PotionFilter(SpellData s)
        {
            if (!CraftingSettings.Instance.ApplyPotionRestrictions) return true;

            // Règle JDR : Niveau 0 à 3
            if (s.MinLevel > 3) return false;

            // Règle JDR : Portée non Personnelle
            if (s.Range == Kingmaker.UnitLogic.Abilities.Blueprints.AbilityRange.Personal) return false;

            // Règle JDR : Doit pouvoir cibler une créature (soi-même ou un allié)
            // Dans WOTR, les potions sont bues, donc elles doivent pouvoir affecter le buveur.
            if (!s.CanTargetFriends && !s.CanTargetSelf) return false;

            return true;
        }

        private bool ScrollFilter(SpellData s)
        {
            if (!CraftingSettings.Instance.ApplyScrollRestrictions) return true;

            // Règle JDR : Niveau 0 à 9
            if (s.MinLevel > 9) return false;

            return true;
        }

        private bool WandFilter(SpellData s)
        {
            if (!CraftingSettings.Instance.ApplyWandRestrictions) return true;

            // Règle JDR : Niveau 0 à 4
            if (s.MinLevel > 4) return false;

            return true;
        }

        void DrawCreatePotionGUI(float scale)
        {
            DrawMagicItemGUI(scale, Helpers.GetString("ui_menu_potion_workshop", "Potion Workshop"), 50, 1, (s, cl, sl) => CustomEnchantmentsBuilder.GetOrBuildPotion(s, cl, sl), PotionFilter, CraftingSettings.Instance.ApplyPotionRestrictions ? 3 : 9);
        }

        private void DrawMagicItemGUI(float scale, string title, int basePrice, int charges, Func<SpellData, int, int, BlueprintItemEquipmentUsable> builder, Predicate<SpellData> filter = null, int maxLevel = 9)
        {
            float windowWidth = 1000f * scale;
            float contentWidth = windowWidth - (120f * scale);

            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.BeginVertical(GUILayout.Width(contentWidth));

            GUILayout.Space(20 * scale);
            GUIStyle headerStyle = new GUIStyle(GUI.skin.label) { fontSize = (int)(FONT_HUGE * scale), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            GUILayout.Label(string.Format(Helpers.GetString("ui_page_magic_title", "{0}"), title), headerStyle);
            GUILayout.Space(10 * scale);

            // Barre de recherche stylisée
            GUILayout.BeginHorizontal(GUI.skin.box);
            GUIStyle searchLabelStyle = new GUIStyle(GUI.skin.label) { fontSize = (int)(FONT_NORMAL * scale), alignment = TextAnchor.MiddleLeft };
            GUILayout.Label("🔍 " + Helpers.GetString("ui_search", "Search:"), searchLabelStyle, GUILayout.Width(110 * scale), GUILayout.Height(35 * scale));
            scrollSearch = CTextField(scrollSearch, GUILayout.ExpandWidth(true), GUILayout.Height(35 * scale));
            if (CButton("X", GUILayout.Width(35 * scale), GUILayout.Height(35 * scale))) scrollSearch = "";
            GUILayout.EndHorizontal();

            GUILayout.Space(10 * scale);

            GUILayout.BeginHorizontal();

            // LEFT: Spell List
            GUILayout.BeginVertical(GUILayout.Width(contentWidth * 0.6f));
            scrollListPos = GUILayout.BeginScrollView(scrollListPos, "box", GUILayout.Height(400 * scale));

            var filteredSpells = SpellScanner.AvailableSpells.Values
                .Where(s => (string.IsNullOrEmpty(scrollSearch) || s.Name.IndexOf(scrollSearch, StringComparison.OrdinalIgnoreCase) >= 0) && (filter == null || filter(s)))
                .OrderBy(s => s.MinLevel).ThenBy(s => s.Name);

            foreach (var spell in filteredSpells)
            {
                GUIStyle itemStyle = new GUIStyle(GUI.skin.button) { alignment = TextAnchor.MiddleLeft, fontSize = (int)(FONT_NORMAL * scale) };
                if (selectedScrollSpell != null && selectedScrollSpell.Guid == spell.Guid)
                {
                    itemStyle.normal.background = itemStyle.active.background;
                    itemStyle.normal.textColor = Color.cyan;
                }

                string modTag = spell.IsFromMod ? "<color=#88BBFF>[MOD]</color> " : "";
                string btnText = $"{modTag}{string.Format(Helpers.GetString("ui_lvl_format", "Lvl {0} - {1}"), spell.MinLevel, spell.Name)}";

                if (CButtonStyled(new GUIContent(btnText), itemStyle, GUILayout.Height(35 * scale)))
                {
                    selectedScrollSpell = spell;
                    scrollSpellLevel = spell.MinLevel;
                    scrollCasterLevel = Math.Max(1, spell.MinLevel * 2 - 1);
                    if (spell.MinLevel == 0) scrollCasterLevel = 1;
                }
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            GUILayout.Space(20 * scale);

            // RIGHT: Config
            GUILayout.BeginVertical();
            if (selectedScrollSpell != null)
            {
                GUILayout.Label($"<b>{selectedScrollSpell.Name}</b>", new GUIStyle(GUI.skin.label) { richText = true, fontSize = (int)(FONT_LARGE * scale) });
                if (charges > 1)
                {
                    GUILayout.Label($"{Helpers.GetString("ui_charges", "Charges")}: {charges}", new GUIStyle(GUI.skin.label) { fontSize = (int)(FONT_SMALL * scale) });
                }

                GUIStyle infoStyle = new GUIStyle(GUI.skin.label) { fontSize = (int)(FONT_SMALL * scale), richText = true };
                if (!string.IsNullOrEmpty(selectedScrollSpell.School))
                    GUILayout.Label($"<color=#CCCCCC>{Helpers.GetString("ui_school", "School")}:</color> {selectedScrollSpell.School}", infoStyle);

                if (selectedScrollSpell.Classes != null && selectedScrollSpell.Classes.Count > 0)
                    GUILayout.Label($"<color=#CCCCCC>{Helpers.GetString("ui_classes", "Classes")}:</color> {string.Join(", ", selectedScrollSpell.Classes.Take(3))}", infoStyle);

                GUILayout.Space(15 * scale);

                // Spell Level
                GUILayout.Label($"{Helpers.GetString("ui_spell_level", "Spell Level")}: {scrollSpellLevel}", new GUIStyle(GUI.skin.label) { fontSize = (int)(FONT_NORMAL * scale) });
                int nextSL = (int)GUILayout.HorizontalSlider(scrollSpellLevel, selectedScrollSpell.MinLevel, maxLevel);
                if (nextSL != scrollSpellLevel)
                {
                    scrollSpellLevel = nextSL;
                    int minCL = (scrollSpellLevel == 0) ? 1 : Math.Max(1, scrollSpellLevel * 2 - 1);
                    if (scrollCasterLevel < minCL) scrollCasterLevel = minCL;
                }

                // Caster Level
                GUILayout.Label($"{Helpers.GetString("ui_caster_level", "Caster Level")}: {scrollCasterLevel}", new GUIStyle(GUI.skin.label) { fontSize = (int)(FONT_NORMAL * scale) });
                int minAllowedCL = (scrollSpellLevel == 0) ? 1 : Math.Max(1, scrollSpellLevel * 2 - 1);
                scrollCasterLevel = (int)GUILayout.HorizontalSlider(scrollCasterLevel, minAllowedCL, 20);

                GUILayout.Space(20 * scale);

                int cost = (int)Math.Ceiling(basePrice * scrollCasterLevel * Math.Max(0.5f, scrollSpellLevel) * CraftingSettings.Instance.CostMultiplier);

                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label($"{Helpers.GetString("ui_total_cost", "Total Cost")}:", new GUIStyle(GUI.skin.label) { fontSize = (int)(FONT_NORMAL * scale) });
                GUILayout.Label(string.Format(Helpers.GetString("ui_gp_format", "<color=yellow>{0} GP</color>"), cost), new GUIStyle(GUI.skin.label) { fontSize = (int)(FONT_LARGE * scale), richText = true });
                GUILayout.EndVertical();

                GUILayout.Space(30 * scale);

                if (CButton(Helpers.GetString("ui_craft_button", "CRAFT"), GUILayout.Height(50 * scale)))
                {
                    if (Game.Instance.Player.Money >= cost)
                    {
                        var bp = builder(selectedScrollSpell, scrollCasterLevel, scrollSpellLevel);
                        if (bp != null)
                        {
                            Game.Instance.Player.Money -= cost;
                            var item = bp.CreateEntity();
                            DeferredInventoryOpener.CraftingBox.Add(item);
                            feedbackMessage = string.Format(Helpers.GetString("ui_success_created", "<color=green>Success!</color> Created: {0}"), item.Name);
                        }
                    }
                    else
                    {
                        feedbackMessage = string.Format(Helpers.GetString("ui_err_not_enough_gold_need", "<color=red>Not enough gold!</color> (Need {0} GP)"), cost);
                    }
                }
            }
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }
        void DrawCreateMetamagicRodGUI(float scale)
        {
            if (selectedModel == null || selectedModel.EnchantId != "007")
            {
                selectedModel = CustomEnchantmentsBuilder.GetModelById("007");
                if (selectedModel != null)
                {
                    dynamicParamValues.Clear();
                    foreach (var p in selectedModel.DynamicParams)
                    {
                        int defVal = p.Min;
                        if (p.DefaultValue != null)
                        {
                            if (p.DefaultValue is long || p.DefaultValue is int) defVal = Convert.ToInt32(p.DefaultValue);
                            else if (p.DefaultValue is string defStr && p.Type == "Enum")
                            {
                                try
                                {
                                    var enumType = Type.GetType(p.EnumTypeName);
                                    if (enumType != null) defVal = (int)Enum.Parse(enumType, defStr, true);
                                }
                                catch { }
                            }
                        }
                        dynamicParamValues[p.Name] = defVal;
                    }

                    dynamicParamValues["MetamagicCount"] = 1;
                    int initialMetamagic = dynamicParamValues.ContainsKey("Metamagic") ? dynamicParamValues["Metamagic"] : 1;
                    dynamicParamValues["Metamagic_0"] = initialMetamagic;
                }
                else
                {
                    Main.ModEntry.Logger.Error("[ATELIER] CRITICAL: Metamagic Rod model (007) not found in CustomEnchantmentsBuilder.AllModels!");
                    currentPageType = CraftingPage.MainMenu;
                    return;
                }
            }

            DrawCustomEnchantmentGUI_Content(scale);
        }
        void DrawCreateScrollGUI(float scale)
        {
            DrawMagicItemGUI(scale, Helpers.GetString("ui_menu_scroll_workshop", "Scroll Workshop"), 25, 1, (s, cl, sl) => CustomEnchantmentsBuilder.GetOrBuildScroll(s, cl, sl), ScrollFilter, CraftingSettings.Instance.ApplyScrollRestrictions ? 9 : 10);
        }


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
            var filteredItems = allItems.Where(it =>
            {
                if (it.Blueprint is BlueprintItemEquipmentUsable) return false;
                if (!string.IsNullOrEmpty(inventorySearch) && !it.Name.ToLower().Contains(inventorySearch.ToLower())) return false;
                if (activeTypes.Count > 0 && activeTypes.Count < 3)
                {
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
                GUIStyle entryStyle = new GUIStyle(GUI.skin.button)
                {
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
                            // On vérifie si c'est une arme double
                            bool isDouble = false;
                            if (it.Blueprint is BlueprintItemWeapon bpw)
                            {
                                isDouble = bpw.Double || bpw.CountAsDouble;
                            }

                            if (isDouble)
                            {
                                doubleWeaponCandidate = it;
                                showDoubleWeaponChoice = true;
                            }
                            else
                            {
                                FinalizeSelection(it);
                            }
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
            if (showIconBrowser)
            {
                DrawIconBrowserGUI(scale);
                return;
            }

            var workshop = Game.Instance.Player.MainCharacter.Value.Get<UnitPartWilcerWorkshop>();
            var activeProject = workshop?.ActiveProjects.FirstOrDefault(p => p.Item == selectedItem);

            GUILayout.BeginVertical(GUI.skin.box);

            if (activeProject != null)
            {
                long remainingTicks = activeProject.FinishTimeTicks - Game.Instance.Player.GameTime.Ticks;
                double remainingDays = Math.Max(0, remainingTicks / (double)TimeSpan.TicksPerDay);
                GUILayout.Label(Helpers.GetString("ui_wilcer_working", "<b>WILCER IS CURRENTLY WORKING ON THIS ITEM</b>"), new GUIStyle(GUI.skin.label) { richText = true, alignment = TextAnchor.MiddleCenter });
                string remText = string.Format(Helpers.GetString("ui_time_remaining", "Estimated remaining time: {0:F1} days"), remainingDays);
                GUILayout.Label(remText, new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = (int)(FONT_NORMAL * scale) });
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

            if (CButton(Helpers.GetString("ui_btn_rename_action", "Rename"), GUILayout.Width(120 * scale), GUILayout.Height(35 * scale)))
            {
                ItemRenamer.RenameItem(selectedItem, newNameDraft);
                feedbackMessage = Helpers.GetString("ui_feedback_renamed", "The item has been renamed!");
            }

            if (selectedItem != null && CButton(Helpers.GetString("ui_btn_auto_name", "Auto"), GUILayout.Width(100 * scale), GUILayout.Height(35 * scale)))
            {
                string autoName = ItemRenamer.GenerateAutoName(selectedItem);
                ItemRenamer.RenameItem(selectedItem, autoName);
                newNameDraft = autoName;
                feedbackMessage = Helpers.GetString("ui_feedback_autoname_gen", "Automatic name generated.");
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(10 * scale);

            // --- SECTION ICÔNE ---
            GUILayout.Label(Helpers.GetString("ui_special_action_icon", "Special Action: Change icon (Free)"), new GUIStyle(GUI.skin.label) { fontSize = (int)(FONT_NORMAL * scale) });
            GUILayout.BeginHorizontal();

            // Aperçu de l'icône actuelle (Patchée par Harmony si personnalisée)
            DrawSprite(selectedItem.Icon, 40 * scale);
            GUILayout.Space(10 * scale);

            if (CButton(Helpers.GetString("ui_btn_change_icon", "Change Icon"), GUILayout.Width(150 * scale), GUILayout.Height(40 * scale)))
            {
                showIconBrowser = true;
                iconScrollPos = Vector2.zero;
            }

            GUILayout.Space(10 * scale);

            if (CButton(Helpers.GetString("ui_btn_reset_icon", "Reset"), GUILayout.Width(100 * scale), GUILayout.Height(40 * scale)))
            {
                ItemRenamer.ChangeIcon(selectedItem, null);
                feedbackMessage = Helpers.GetString("ui_feedback_icon_reset", "Icon reset to default.");
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Space(10 * scale);

            // --- SECTION DESCRIPTION ---
            GUILayout.Label(Helpers.GetString("ui_btn_change_description", "Edit Description"), new GUIStyle(GUI.skin.label) { fontSize = (int)(FONT_NORMAL * scale) });
            GUILayout.BeginHorizontal();

            if (CButton(Helpers.GetString("ui_btn_change_description", "Edit Description"), GUILayout.Width(200 * scale), GUILayout.Height(40 * scale)))
            {
                editedDescription = selectedItem.Description;
                showDescriptionEditor = true;
            }

            GUILayout.FlexibleSpace();
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

                    bool isNative = (selectedItem.Blueprint.Enchantments?.Any(bp => bp == ench.Blueprint) ?? false);

                    if (isNative)
                    {
                        GUIStyle cantStyle = new GUIStyle(GUI.skin.label)
                        {
                            fontSize = (int)(FONT_NORMAL * scale),
                            alignment = TextAnchor.MiddleCenter,
                            normal = { textColor = new Color(0.7f, 0.7f, 0.7f, 0.8f) }
                        };
                        GUILayout.Label(Helpers.GetString("ui_btn_cant_remove", "Can't remove"), cantStyle, GUILayout.Width(100 * scale), GUILayout.Height(20 * scale));
                    }
                    else if (CButton(Helpers.GetString("ui_btn_remove", "Remove"), GUILayout.Width(100 * scale), GUILayout.Height(20 * scale)))
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
                    GUILayout.Label(activeCategories.Count == 0 ? Helpers.GetString("ui_filter_none_active", " <i>(No filter active = Show all)</i>") : "", new GUIStyle(GUI.skin.label) { richText = true, fontSize = (int)(FONT_NORMAL * scale) });
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
                    int totalPages = Mathf.Max(1, Mathf.CeilToInt((float)totalItems / CraftingSettings.Instance.ItemsPerPage));
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
                    if (string.IsNullOrEmpty(itemsPerPageInput)) itemsPerPageInput = CraftingSettings.Instance.ItemsPerPage.ToString();
                    itemsPerPageInput = CTextFieldStyled(itemsPerPageInput, pageInputStyle, GUILayout.Width(40 * scale), GUILayout.Height(22 * scale));

                    // Détection Entrée pour ItemsPerPage
                    if (Event.current.isKey && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter))
                    {
                        if (int.TryParse(itemsPerPageInput, out int val) && val > 0)
                        {
                            CraftingSettings.Instance.ItemsPerPage = val;
                            CraftingSettings.Instance.Save(Main.ModEntry);
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

                    int startIdx = currentPage * CraftingSettings.Instance.ItemsPerPage;
                    int endIdx = Mathf.Min(startIdx + CraftingSettings.Instance.ItemsPerPage, cachedFilteredEnchantments.Count);

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
            long totalCost = CraftingCalculator.GetMarginalCost(selectedItem, selectedList, null, CraftingSettings.Instance.CostMultiplier);
            int totalDays = CraftingCalculator.GetCraftingDays(totalCost, CraftingSettings.Instance.InstantCrafting);

            int currentLevelPoints = CraftingCalculator.CalculateDisplayedEnchantmentPoints(selectedItem);
            int maxLevel = CraftingSettings.Instance.MaxTotalBonus;
            int selectedPoints = selectedList.Sum(d => d.PointCost);

            GUILayout.BeginHorizontal();
            GUILayout.Label(string.Format(Helpers.GetString("ui_current_level", "Current level: {0}/{1}"), currentLevelPoints, maxLevel), new GUIStyle(GUI.skin.label) { fontSize = (int)(FONT_NORMAL * scale) }, GUILayout.ExpandWidth(false));
            GUILayout.FlexibleSpace();
            GUILayout.Label(string.Format(Helpers.GetString("ui_selection_total_summary", "Selection: +{0} \u2014 Total: {1} gp / ~{2} d"), selectedPoints, totalCost, totalDays), new GUIStyle(GUI.skin.label) { fontSize = (int)(FONT_NORMAL * scale) }, GUILayout.ExpandWidth(false));
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
                            long c = CraftingCalculator.GetEnchantmentCost(selectedItem, d, CraftingSettings.Instance.CostMultiplier);
                            int days = CraftingCalculator.GetCraftingDays(c, CraftingSettings.Instance.InstantCrafting);
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

        private void DrawAbadarWarningGUI(float scale)
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label(Helpers.GetString("ui_abadar_warning_title", "<b><color=red>WARNING: CHEATS & EXPERIMENTAL OPTIONS</color></b>"), new GUIStyle(GUI.skin.label) { richText = true, fontSize = (int)(FONT_LARGE * scale), alignment = TextAnchor.MiddleCenter });
            GUILayout.Space(20);
            GUILayout.Label(Helpers.GetString("ui_abadar_warning", "Abadar is watching you..."), new GUIStyle(GUI.skin.label) { fontSize = (int)(FONT_NORMAL * scale), alignment = TextAnchor.MiddleCenter });
            GUILayout.Space(40);

            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (CButton(Helpers.GetString("ui_btn_yes_proceed", "Yes, Proceed"), GUILayout.Width(200 * scale), GUILayout.Height(50 * scale)))
            {
                CraftingSettings.Instance.HasOpenedCheats = true;
                CraftingSettings.Instance.Save(Main.ModEntry);
                showAbadarWarning = false;
                ShowSettings = true;
            }
            GUILayout.Space(20);
            if (CButton(Helpers.GetString("ui_btn_no_cancel", "No, Go Back"), GUILayout.Width(200 * scale), GUILayout.Height(50 * scale)))
            {
                showAbadarWarning = false;
                ShowSettings = false;
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
        }

        private void DrawIconBrowserGUI(float scale)
        {
            GUIStyle titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = (int)(FONT_LARGE * scale),
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            GUILayout.Label(Helpers.GetString("ui_icon_browser_title", "Select a new icon"), titleStyle);
            GUILayout.Space(20 * scale);

            var itemType = selectedItem.Blueprint.ItemType;
            if (!ItemScanner.IconCache.TryGetValue(itemType, out var icons) || icons.Count == 0)
            {
                GUILayout.Label(Helpers.GetString("ui_icon_browser_empty", "No icons found for this item type."), new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter });
                return;
            }

            iconScrollPos = GUILayout.BeginScrollView(iconScrollPos, false, true, GUILayout.ExpandHeight(true));

            int cols = (int)Mathf.Max(1, (800f * scale) / (120f * scale));
            int currentCol = 0;
            float iconSize = 80 * scale;

            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace(); // Centrage de la grille
            foreach (var bp in icons)
            {
                GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(iconSize + 20 * scale), GUILayout.Height(iconSize + 50 * scale));

                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                DrawSprite(bp.Icon, iconSize);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                GUILayout.Space(5 * scale);

                if (CButton(Helpers.GetString("ui_btn_select", "Select"), GUILayout.Width(iconSize + 10 * scale), GUILayout.Height(30 * scale)))
                {
                    ItemRenamer.ChangeIcon(selectedItem, bp.AssetGuid.ToString());
                    feedbackMessage = Helpers.GetString("ui_feedback_icon_changed", "Icon changed successfully.");
                    showIconBrowser = false;
                }

                GUILayout.EndVertical();

                currentCol++;
                if (currentCol >= cols)
                {
                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();
                    GUILayout.Space(10 * scale);
                    GUILayout.BeginHorizontal();
                    GUILayout.FlexibleSpace();
                    currentCol = 0;
                }
                else
                {
                    GUILayout.Space(10 * scale);
                }
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.EndScrollView();
        }

        void DrawSettingsGUI(float scale)
        {
            float prevCostMult = CraftingSettings.Instance.CostMultiplier;
            bool prevInstant = CraftingSettings.Instance.InstantCrafting;
            bool prevEnforce = CraftingSettings.Instance.EnforcePointsLimit;
            int prevMaxEnh = CraftingSettings.Instance.MaxEnhancementBonus;
            int prevMaxTotal = CraftingSettings.Instance.MaxTotalBonus;
            bool prevRequirePlus = CraftingSettings.Instance.RequirePlusOneFirst;
            bool prevSlotPenalty = CraftingSettings.Instance.ApplySlotPenalty;
            bool prevEnableEpic = CraftingSettings.Instance.EnableEpicCosts;
            float prevEpicMult = CraftingSettings.Instance.EpicCostMultiplier;
            SourceFilter prevSourceFilter = CraftingSettings.Instance.CurrentSourceFilter;
            bool prevPotionRestr = CraftingSettings.Instance.ApplyPotionRestrictions;
            bool prevScrollRestr = CraftingSettings.Instance.ApplyScrollRestrictions;
            bool prevWandRestr = CraftingSettings.Instance.ApplyWandRestrictions;

            Color oldBG = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.4f, 0.4f, 0.4f, 1.0f); // Sous-menu plus clair
            GUILayout.BeginVertical(GUI.skin.box);
            GUI.backgroundColor = oldBG;
            GUILayout.Label(Helpers.GetString("ui_settings_title", "Workshop Settings"), new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = (int)(FONT_LARGE * scale) });

            GUILayout.Space(10);

            GUIStyle settingsLabelStyle = new GUIStyle(GUI.skin.label) { fontSize = (int)(FONT_NORMAL * scale) };

            GUILayout.BeginHorizontal();
            GUILayout.Label(Helpers.GetString("ui_settings_cost_mult", " Cost Multiplier: ") + CraftingSettings.Instance.CostMultiplier.ToString("F1"), settingsLabelStyle, GUILayout.Width(200 * scale));
            CraftingSettings.Instance.CostMultiplier = (float)Math.Round(GUILayout.HorizontalSlider(CraftingSettings.Instance.CostMultiplier, 0f, 5f, GUILayout.Width(150 * scale)), 1);
            GUILayout.EndHorizontal();

            bool previousInstantCrafting = CraftingSettings.Instance.InstantCrafting;
            CraftingSettings.Instance.InstantCrafting = CToggle(CraftingSettings.Instance.InstantCrafting, Helpers.GetString("ui_settings_instant_craft", "Instant Crafting"));

            GUILayout.Space(5);
            CraftingSettings.Instance.ApplyPotionRestrictions = CToggle(CraftingSettings.Instance.ApplyPotionRestrictions, Helpers.GetString("ui_settings_potion_restrictions", " Apply TTRPG restrictions on potions"));
            GUILayout.Space(5);
            CraftingSettings.Instance.ApplyScrollRestrictions = CToggle(CraftingSettings.Instance.ApplyScrollRestrictions, Helpers.GetString("ui_settings_scroll_restrictions", " Apply TTRPG restrictions on scrolls"));
            GUILayout.Space(5);
            CraftingSettings.Instance.ApplyWandRestrictions = CToggle(CraftingSettings.Instance.ApplyWandRestrictions, Helpers.GetString("ui_settings_wand_restrictions", " Apply TTRPG restrictions on wands"));
            GUILayout.Space(5);

            if (CraftingSettings.Instance.InstantCrafting && !previousInstantCrafting)
            {
                try
                {
                    var workshop = Game.Instance.Player.MainCharacter.Value.Get<UnitPartWilcerWorkshop>();
                    if (workshop != null && workshop.ActiveProjects.Count > 0)
                    {
                        workshop.CheckAndFinishProjects();
                        feedbackMessage = Helpers.GetString("ui_feedback_all_forges_done", "All active forging projects have been completed instantly!");
                    }
                }
                catch (Exception ex) { Main.ModEntry.Logger.Error($"[UI-DEBUG] CRASH : {ex.Message}"); }
            }

            GUILayout.Space(10);

            bool oldEnforce = CraftingSettings.Instance.EnforcePointsLimit;
            CraftingSettings.Instance.EnforcePointsLimit = CToggle(CraftingSettings.Instance.EnforcePointsLimit, Helpers.GetString("ui_settings_enforce_limit", " Enforce Bonus Limits (Pathfinder)"));
            if (oldEnforce != CraftingSettings.Instance.EnforcePointsLimit)
            {
                filtersDirty = true;
                if (!CraftingSettings.Instance.EnforcePointsLimit)
                {
                    CraftingSettings.Instance.RequirePlusOneFirst = false;
                    CraftingSettings.Instance.ApplySlotPenalty = false;
                    CraftingSettings.Instance.EnableEpicCosts = false;
                }
            }

            if (CraftingSettings.Instance.EnforcePointsLimit)
            {
                GUILayout.Space(5);
                bool oldReq = CraftingSettings.Instance.RequirePlusOneFirst;
                CraftingSettings.Instance.RequirePlusOneFirst = CToggle(CraftingSettings.Instance.RequirePlusOneFirst, Helpers.GetString("ui_settings_require_plus_one", " Prerequisite: At least +1 Enhancement"));
                if (oldReq != CraftingSettings.Instance.RequirePlusOneFirst) filtersDirty = true;

                GUILayout.Space(5);
                CraftingSettings.Instance.ApplySlotPenalty = CToggle(CraftingSettings.Instance.ApplySlotPenalty, Helpers.GetString("ui_settings_slot_penalty", " Apply Slot Penalty (x1.5)"));

                GUILayout.Space(5);
                bool oldEpic = CraftingSettings.Instance.EnableEpicCosts;
                CraftingSettings.Instance.EnableEpicCosts = CToggle(CraftingSettings.Instance.EnableEpicCosts, Helpers.GetString("ui_settings_enable_epic", " Enable Epic Multiplier"));
                if (oldEpic != CraftingSettings.Instance.EnableEpicCosts) filtersDirty = true;

                if (CraftingSettings.Instance.EnableEpicCosts)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Space(20 * scale);
                    float oldMult = CraftingSettings.Instance.EpicCostMultiplier;
                    GUILayout.Label(string.Format(Helpers.GetString("ui_settings_epic_multiplier", " Epic Multiplier: x{0:F1}"), CraftingSettings.Instance.EpicCostMultiplier), settingsLabelStyle, GUILayout.Width(200 * scale));
                    CraftingSettings.Instance.EpicCostMultiplier = (float)Math.Round(GUILayout.HorizontalSlider(CraftingSettings.Instance.EpicCostMultiplier, 1f, 10f, GUILayout.Width(150 * scale)) * 2) / 2f;
                    if (oldMult != CraftingSettings.Instance.EpicCostMultiplier) filtersDirty = true;
                    GUILayout.EndHorizontal();
                }

                GUILayout.Space(8); // Un peu plus d'espace avant les sliders
                GUILayout.BeginHorizontal();
                int oldMaxEnh = CraftingSettings.Instance.MaxEnhancementBonus;
                GUILayout.Label(string.Format(Helpers.GetString("ui_settings_max_enhancement", " Max Enhancement: +{0}"), CraftingSettings.Instance.MaxEnhancementBonus), settingsLabelStyle, GUILayout.Width(150 * scale));
                if (CButton("-", GUILayout.Width(30 * scale))) CraftingSettings.Instance.MaxEnhancementBonus--;
                CraftingSettings.Instance.MaxEnhancementBonus = (int)GUILayout.HorizontalSlider(CraftingSettings.Instance.MaxEnhancementBonus, 1, 20, GUILayout.Width(90 * scale));
                if (CButton("+", GUILayout.Width(30 * scale))) CraftingSettings.Instance.MaxEnhancementBonus++;
                if (oldMaxEnh != CraftingSettings.Instance.MaxEnhancementBonus) filtersDirty = true;
                GUILayout.EndHorizontal();

                GUILayout.Space(5);
                GUILayout.BeginHorizontal();
                int oldMaxTotal = CraftingSettings.Instance.MaxTotalBonus;
                GUILayout.Label(string.Format(Helpers.GetString("ui_settings_max_total", " Max Total: +{0}"), CraftingSettings.Instance.MaxTotalBonus), settingsLabelStyle, GUILayout.Width(150 * scale));
                if (CButton("-", GUILayout.Width(30 * scale))) CraftingSettings.Instance.MaxTotalBonus--;
                CraftingSettings.Instance.MaxTotalBonus = (int)GUILayout.HorizontalSlider(CraftingSettings.Instance.MaxTotalBonus, 1, 50, GUILayout.Width(90 * scale));
                if (CButton("+", GUILayout.Width(30 * scale))) CraftingSettings.Instance.MaxTotalBonus++;
                if (oldMaxTotal != CraftingSettings.Instance.MaxTotalBonus) filtersDirty = true;
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(10);
            GUILayout.Label(Helpers.GetString("ui_settings_source_display", "Source display:"), settingsLabelStyle);
            GUILayout.BeginHorizontal();
            int sliderVal = (int)CraftingSettings.Instance.CurrentSourceFilter;
            if (CButton("<", GUILayout.Width(30 * scale))) sliderVal--;
            sliderVal = Mathf.RoundToInt(GUILayout.HorizontalSlider(sliderVal, 0, 4, GUILayout.Width(240 * scale)));
            if (CButton(">", GUILayout.Width(30 * scale))) sliderVal++;
            sliderVal = Mathf.Clamp(sliderVal, 0, 4);
            CraftingSettings.Instance.CurrentSourceFilter = (SourceFilter)sliderVal;
            GUILayout.Space(20 * scale);

            string sourceLabel = "";
            switch (CraftingSettings.Instance.CurrentSourceFilter)
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
            GUILayout.Label(string.Format(Helpers.GetString("ui_settings_models_loaded", "Custom Models Loaded: {0}"), CustomEnchantmentsBuilder.AllModels?.Count ?? 0), settingsLabelStyle);
            if (CButton(Helpers.GetString("ui_settings_force_sync", "Force Synchronization (Full Scan)"), GUILayout.Height(35 * scale)))
            {
                EnchantmentScanner.ForceSync();
            }


            GUILayout.FlexibleSpace();
            GUILayout.EndVertical();

            if (prevCostMult != CraftingSettings.Instance.CostMultiplier || prevInstant != CraftingSettings.Instance.InstantCrafting || prevEnforce != CraftingSettings.Instance.EnforcePointsLimit
                || prevMaxEnh != CraftingSettings.Instance.MaxEnhancementBonus || prevMaxTotal != CraftingSettings.Instance.MaxTotalBonus || prevRequirePlus != CraftingSettings.Instance.RequirePlusOneFirst
                || prevSlotPenalty != CraftingSettings.Instance.ApplySlotPenalty || prevEnableEpic != CraftingSettings.Instance.EnableEpicCosts
                || prevEpicMult != CraftingSettings.Instance.EpicCostMultiplier
                || prevSourceFilter != CraftingSettings.Instance.CurrentSourceFilter
                || prevPotionRestr != CraftingSettings.Instance.ApplyPotionRestrictions
                || prevScrollRestr != CraftingSettings.Instance.ApplyScrollRestrictions
                || prevWandRestr != CraftingSettings.Instance.ApplyWandRestrictions)
            {
                CraftingSettings.Instance.Save(Main.ModEntry);
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
                if (CraftingSettings.Instance.RequirePlusOneFirst && isWeaponOrArmor && !isReadyForSpecial && !isQueued)
                {
                    if (!CraftingCalculator.IsEnchantmentAllowedOnNormalItem(data)) continue;
                }

                // --- FILTRE RECHERCHE ---
                string displayName = DescriptionManager.GetDisplayName(bp, data);
                if (!string.IsNullOrEmpty(lastSearch) && !displayName.ToLower().Contains(lastSearch.ToLower())) continue;

                // --- FILTRE SOURCE ---
                if (CraftingSettings.Instance.CurrentSourceFilter == SourceFilter.TTRPG && data.Source != "TTRPG") continue;
                if (CraftingSettings.Instance.CurrentSourceFilter == SourceFilter.Owlcat && data.Source != "TTRPG" && data.Source != "Owlcat") continue;
                if (CraftingSettings.Instance.CurrentSourceFilter == SourceFilter.OwlcatPlus && data.Source != "TTRPG" && data.Source != "Owlcat" && data.Source != "Owlcat+") continue;
                if (CraftingSettings.Instance.CurrentSourceFilter == SourceFilter.Mods && (data.Source == "TTRPG" || data.Source == "Owlcat" || data.Source == "Owlcat+")) continue;

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
            float scale = CraftingSettings.Instance.ScalePercent / 100f;

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

        private void DrawSprite(Sprite sprite, float size)
        {
            if (sprite == null) return;
            try
            {
                Rect rect = GUILayoutUtility.GetRect(size, size, GUILayout.Width(size), GUILayout.Height(size));
                if (Event.current.type == EventType.Repaint)
                {
                    Texture2D tex = sprite.texture;
                    if (tex == null) return;

                    // Utilisation des UVs du Sprite (plus fiable que textureRect pour les textures compressées)
                    Vector2[] uvs = sprite.uv;
                    // uvs[0] = BL, uvs[1] = TL, uvs[2] = TR, uvs[3] = BR (ordre habituel Unity)
                    // On calcule la bounding box des UVs
                    float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
                    foreach (var v in uvs)
                    {
                        if (v.x < minX) minX = v.x;
                        if (v.y < minY) minY = v.y;
                        if (v.x > maxX) maxX = v.x;
                        if (v.y > maxY) maxY = v.y;
                    }
                    Rect uvRect = new Rect(minX, minY, maxX - minX, maxY - minY);

                    GUI.DrawTextureWithTexCoords(rect, tex, uvRect);
                }
            }
            catch (Exception e)
            {
                Main.ModEntry.Logger.Error($"[UI] Erreur DrawSprite: {e.Message}");
            }
        }

        private bool CButton(string text, params GUILayoutOption[] options)
        {
            float scale = CraftingSettings.Instance.ScalePercent / 100f;
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
            float scale = CraftingSettings.Instance.ScalePercent / 100f;
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
            float scale = CraftingSettings.Instance.ScalePercent / 100f;
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

        private string CTextField(string text, params GUILayoutOption[] options)
        {
            float scale = CraftingSettings.Instance.ScalePercent / 100f;
            GUIStyle style = new GUIStyle(GUI.skin.textField) { fontSize = (int)(FONT_NORMAL * scale) };
            return CTextFieldStyled(text, style, options);
        }
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
            GUILayout.BeginVertical();
            // --- RAPPEL DES SÉLECTIONS CUSTOM ---
            var queuedCustoms = queuedEnchantGuids.Where(g => g.Replace("-", "").ToUpper().StartsWith(DynamicGuidHelper.Signature)).ToList();
            if (queuedCustoms.Count > 0)
            {
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label(Helpers.GetString("ui_custom_queued_title", "Custom Selection in queue:"), new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = (int)(FONT_NORMAL * scale) });
                int customIdx = 0;
                foreach (var g in queuedCustoms)
                {
                    customIdx++;
                    string displayName = Helpers.GetString("ui_custom_enchantment_placeholder", "Custom Enchantment");
                    EnchantmentData data = EnchantmentScanner.GetByGuid(g);
                    BlueprintScriptableObject bp = null;

                    if (data != null)
                    {
                        displayName = data.Name;
                        bp = data.Blueprint;
                    }
                    else
                    {
                        bp = CustomEnchantmentsBuilder.GetOrBuildDynamicBlueprint(g);
                        if (bp != null) displayName = bp.name;
                    }

                    GUILayout.BeginHorizontal(GUI.skin.box);

                    // Nom et métadonnées
                    string metadata = "";
                    if (data != null)
                    {
                        metadata = $" <color=#E2C675>[+{data.PointString}]</color>";
                        if (data.IsEpic) metadata += Helpers.GetString("ui_epic_tag", " <color=#FF4500>(Epic)</color>");
                    }

                    GUILayout.Label(" • " + displayName + metadata, new GUIStyle(GUI.skin.label) { fontSize = (int)(FONT_NORMAL * scale), richText = true }, GUILayout.ExpandWidth(true));

                    // Bouton Description
                    if (bp != null)
                    {
                        DescriptionSource descSource = DescriptionSource.None;
                        string desc = DescriptionManager.GetLocalizedDescription(bp, data, out descSource);

                        string color;
                        if (!string.IsNullOrEmpty(desc) && descSource == DescriptionSource.Official)
                        {
                            color = "#3498db"; // Bleu (Official)
                        }
                        else if (!string.IsNullOrEmpty(desc) && descSource == DescriptionSource.Generated)
                        {
                            color = "#f1c40f"; // Jaune (Généré)
                        }
                        else
                        {
                            color = "#e74c3c"; // Rouge (Fallback / TODO)
                            if (string.IsNullOrEmpty(desc)) desc = Helpers.GetString("ui_desc_needed", "TODO: Description needed for this enchantment.");
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

                var models = CustomEnchantmentsBuilder.AllModels.Where(m => !m.Hidden).ToList();

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
                    int modelIdx = 0;
                    foreach (var model in models)
                    {
                        modelIdx++;
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
                                        try
                                        {
                                            var enumType = Type.GetType(p.EnumTypeName);
                                            if (enumType != null)
                                            {
                                                defVal = (int)Enum.Parse(enumType, defStr, true);
                                            }
                                        }
                                        catch
                                        {
                                            Main.ModEntry.Logger.Error($"[ATELIER] Failed to parse DefaultValue '{defStr}' as enum '{p.EnumTypeName}'");
                                        }
                                    }
                                }
                                else if (p.Type == "Enum")
                                {
                                    // Sélection intelligente du défaut : première valeur valide après filtrage
                                    try
                                    {
                                        var enumType = Type.GetType(p.EnumTypeName);
                                        if (enumType != null)
                                        {
                                            var allNames = Enum.GetNames(enumType);
                                            var allValues = Enum.GetValues(enumType);
                                            for (int i = 0; i < allNames.Length; i++)
                                            {
                                                string n = allNames[i];
                                                if (p.EnumOnly != null && p.EnumOnly.Count > 0 && !p.EnumOnly.Any(eo => eo.Equals(n, StringComparison.OrdinalIgnoreCase))) continue;
                                                if (p.EnumExclude != null && p.EnumExclude.Count > 0 && p.EnumExclude.Any(ee => ee.Equals(n, StringComparison.OrdinalIgnoreCase))) continue;

                                                defVal = (int)allValues.GetValue(i);
                                                break;
                                            }
                                        }
                                    }
                                    catch { }
                                }
                                dynamicParamValues[p.Name] = defVal;
                                filtersDirty = true;
                            }
                        }
                        GUILayout.EndHorizontal();

                        if (Event.current.type == EventType.Repaint && modelIdx % 2 == 0)
                        {
                            Rect lastRect = GUILayoutUtility.GetLastRect();
                            Color oldC = GUI.color;
                            GUI.color = new Color(1f, 1f, 1f, 0.06f);
                            GUI.DrawTexture(lastRect, Texture2D.whiteTexture);
                            GUI.color = oldC;
                        }
                    }
                }
            }
            else
            {
                // --- MODE CONFIGURATION ---
                GUILayout.BeginHorizontal();
                if (CButton(Helpers.GetString("ui_btn_back", "Back"), GUILayout.Width(100 * scale), GUILayout.Height(30 * scale)))
                {
                    if (currentPageType == CraftingPage.CreateMetamagicRod) currentPageType = CraftingPage.MainMenu;
                    selectedModel = null;
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                GUILayout.Space(10 * scale);

                // En-tête stylisé
                GUIStyle titleStyle = new GUIStyle(GUI.skin.label) { richText = true, fontSize = (int)(FONT_HUGE * scale), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                GUILayout.Label(string.Format(Helpers.GetString("ui_configuring_model", "Configuring: <color=#E2C675>{0}</color>"), Helpers.GetLocalizedString(selectedModel.BaseName ?? selectedModel.NameCompleted)), titleStyle);

                GUILayout.Space(20 * scale);

                string currentlyOpenParam = openDropdownParam;

                if (selectedModel.EnchantId == "007")
                {
                    // --- LAYOUT SPÉCIFIQUE SCEPTRES (2 COLONNES) ---
                    GUILayout.BeginHorizontal();

                    // COLONNE GAUCHE : Paramètres techniques
                    GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(450 * scale));
                    GUILayout.Label($"<b>{Helpers.GetString("ui_section_basics", "Basic Parameters")}</b>", new GUIStyle(GUI.skin.label) { richText = true, fontSize = (int)(FONT_LARGE * scale) });
                    GUILayout.Space(10 * scale);

                    foreach (var p in selectedModel.DynamicParams)
                    {
                        if (p.Name.StartsWith("Metamagic")) continue;

                        GUILayout.BeginVertical();
                        string paramLabel = Helpers.GetString("ui_param_" + p.Name.ToLower().Replace(" ", "_"), p.Name);
                        GUILayout.Label($"<color=#CCCCCC>{paramLabel} :</color>", new GUIStyle(GUI.skin.label) { richText = true, fontSize = (int)(FONT_LARGE * scale) });

                        if (p.Type == "Slider")
                        {
                            int val = dynamicParamValues.ContainsKey(p.Name) ? dynamicParamValues[p.Name] : p.Min;
                            GUILayout.BeginHorizontal();
                            int newVal = (int)GUILayout.HorizontalSlider(val, p.Min, p.Max, GUILayout.ExpandWidth(true));
                            GUILayout.Space(10);
                            GUILayout.Label(val.ToString(), new GUIStyle(GUI.skin.label) { fontSize = (int)(FONT_LARGE * scale), alignment = TextAnchor.MiddleRight }, GUILayout.Width(45 * scale));
                            GUILayout.EndHorizontal();

                            if (p.Step > 1) newVal = (newVal / p.Step) * p.Step;
                            dynamicParamValues[p.Name] = newVal;
                        }
                        else if (p.Type == "Enum")
                        {
                            DrawEnumSelector(p, scale, currentlyOpenParam);
                        }
                        GUILayout.EndVertical();
                        GUILayout.Space(10 * scale);
                    }

                    GUILayout.EndVertical();

                    GUILayout.Space(20 * scale);

                    // COLONNE DROITE : Métamagie
                    GUILayout.BeginVertical(GUI.skin.box);
                    GUILayout.Label($"<b>{Helpers.GetString("ui_section_metamagic", "Metamagic Configuration")}</b>", new GUIStyle(GUI.skin.label) { richText = true, fontSize = (int)(FONT_LARGE * scale) });
                    GUILayout.Space(10 * scale);

                    metamagicScrollPos = GUILayout.BeginScrollView(metamagicScrollPos, GUILayout.ExpandHeight(true));
                    DrawDynamicMetamagicList(scale, currentlyOpenParam);
                    GUILayout.EndScrollView();

                    GUILayout.EndVertical();
                    GUILayout.EndHorizontal();
                }
                else
                {
                    // --- LAYOUT STANDARD (BOX CENTRALE) ---
                    GUILayout.BeginVertical(GUI.skin.box);
                    foreach (var p in selectedModel.DynamicParams)
                    {
                        GUILayout.BeginHorizontal();
                        string labelText = Helpers.GetString("ui_param_" + p.Name.ToLower().Replace(" ", "_"), p.Name) + ": ";
                        GUILayout.Label(labelText, new GUIStyle(GUI.skin.label) { fontSize = (int)(FONT_LARGE * scale) }, GUILayout.Width(200 * scale));

                        if (p.Type == "Slider")
                        {
                            int val = dynamicParamValues.ContainsKey(p.Name) ? dynamicParamValues[p.Name] : p.Min;
                            GUILayout.Label(val.ToString(), new GUIStyle(GUI.skin.label) { fontSize = (int)(FONT_LARGE * scale), alignment = TextAnchor.MiddleRight }, GUILayout.Width(60 * scale));
                            int newVal = (int)GUILayout.HorizontalSlider(val, p.Min, p.Max);
                            if (p.Step > 1) newVal = (newVal / p.Step) * p.Step;
                            dynamicParamValues[p.Name] = newVal;
                        }
                        else if (p.Type == "Enum")
                        {
                            DrawEnumSelector(p, scale, currentlyOpenParam);
                        }
                        GUILayout.EndHorizontal();
                        GUILayout.Space(5);
                    }
                    GUILayout.EndVertical();
                }

                GUILayout.Space(30 * scale);

                // --- CALCUL DU RÉSULTAT ET PRIX (BARRE DU BAS) ---
                int[] orderedValues = selectedModel.DynamicParams.Select(p => dynamicParamValues.ContainsKey(p.Name) ? dynamicParamValues[p.Name] : 0).ToArray();
                int mask = 0;
                bool hasMaskControl = false;

                if (selectedModel.EnchantId == "007")
                {
                    int count = dynamicParamValues.ContainsKey("MetamagicCount") ? dynamicParamValues["MetamagicCount"] : 1;
                    List<int> vals = new List<int>();
                    vals.Add(dynamicParamValues.ContainsKey("Grade") ? dynamicParamValues["Grade"] : 0);
                    vals.Add(dynamicParamValues.ContainsKey("Charges") ? dynamicParamValues["Charges"] : 3);
                    vals.Add(count);
                    for (int i = 0; i < count; i++)
                    {
                        int m = dynamicParamValues.ContainsKey("Metamagic_" + i) ? dynamicParamValues["Metamagic_" + i] : 0;
                        vals.Add(m);
                        mask |= m;
                    }
                    orderedValues = vals.ToArray();
                    hasMaskControl = true;
                }
                else
                {
                    foreach (var p in selectedModel.DynamicParams)
                    {
                        if (p.Type == "Enum" && !string.IsNullOrEmpty(p.EnumTypeName) && p.EnumOverrides != null)
                        {
                            int val = dynamicParamValues.ContainsKey(p.Name) ? dynamicParamValues[p.Name] : 0;
                            try
                            {
                                var enumType = Type.GetType(p.EnumTypeName);
                                if (enumType != null)
                                {
                                    string enumName = Enum.GetName(enumType, val);
                                    string keyToUse = enumName;
                                    if (string.IsNullOrEmpty(keyToUse))
                                    {
                                        foreach (var kvp in p.EnumOverrides)
                                        {
                                            if (kvp.Value is Newtonsoft.Json.Linq.JObject jo && jo["Value"] != null && (int)jo["Value"] == val) { keyToUse = kvp.Key; break; }
                                        }
                                    }
                                    if (!string.IsNullOrEmpty(keyToUse) && p.EnumOverrides.TryGetValue(keyToUse, out object ovr))
                                    {
                                        if (ovr is Newtonsoft.Json.Linq.JObject jo && jo["MaskValue"] != null) { mask |= (int)jo["MaskValue"]; hasMaskControl = true; }
                                    }
                                }
                            }
                            catch { }
                        }
                    }
                }
                if (!hasMaskControl) mask = 0xFFF;

                var tempGuid = DynamicGuidHelper.GenerateGuid(selectedModel.EnchantId, orderedValues, selectedModel.Type == "Feature", mask);
                var tempEnch = EnchantmentScanner.GetByGuid(tempGuid.ToString());

                long totalCost = 0;
                int totalPoints = 0;
                if (tempEnch != null)
                {
                    totalCost = CraftingCalculator.GetEnchantmentCost(selectedItem, tempEnch, CraftingSettings.Instance.CostMultiplier);
                    totalPoints = tempEnch.PointCost;
                }
                int totalDays = CraftingCalculator.GetCraftingDays(totalCost, CraftingSettings.Instance.InstantCrafting);

                GUILayout.BeginHorizontal();
                GUILayout.Label(string.Format(Helpers.GetString("ui_selection_total_custom", "Selection: +{0} \u2014 Total: {1} gp / ~{2} d"), totalPoints, totalCost, totalDays), new GUIStyle(GUI.skin.label) { richText = true, fontSize = (int)(FONT_LARGE * scale) });
                GUILayout.FlexibleSpace();

                string btnLabel = selectedModel.Type == "UsableItem" ? Helpers.GetString("ui_btn_create_item", "Craft this item") : Helpers.GetString("ui_btn_add_to_selection", "Add to selection");

                bool canCraft = totalCost >= 0 && totalPoints >= 0;
                bool hasMoney = selectedModel.Type != "UsableItem" || Game.Instance.Player.Money >= totalCost;

                if (!canCraft)
                {
                    GUI.enabled = false;
                    btnLabel = Helpers.GetString("ui_err_invalid_formula", "Invalid Formula (Cost -1)");
                }
                else if (!hasMoney)
                {
                    GUI.enabled = false;
                    btnLabel = string.Format(Helpers.GetString("ui_err_no_gold_button", "Not enough gold ({0} GP)"), totalCost);
                }

                if (CButton(btnLabel, GUILayout.Width(250 * scale), GUILayout.Height(40 * scale)))
                {
                    try
                    {
                        var finalGuidStr = tempGuid.ToString();
                        // Pour les objets utilisables (Sceptres), on doit s'assurer que le Blueprint est généré avant de lancer le projet
                        if (selectedModel.Type == "UsableItem")
                        {
                            var builtBp = CustomEnchantmentsBuilder.GetOrBuildDynamicBlueprint(finalGuidStr);
                            var finalEnch = EnchantmentScanner.GetByGuid(finalGuidStr);

                            if (finalEnch != null)
                            {
                                // Les sceptres sont des services (instantané)
                                CraftingActions.StartCraftingProject(null, finalEnch, (int)totalCost, 0);
                                feedbackMessage = "<color=green>" + string.Format(Helpers.GetString("ui_success_created_chest", "Success! Item created and delivered to the <b>Workshop Chest</b>.")) + "</color>";
                                selectedModel = null;
                            }
                            else
                            {
                                feedbackMessage = "<color=red>" + Helpers.GetString("ui_err_generate_item", "Error: Could not generate item data.") + "</color>";
                            }
                        }
                        else
                        {
                            if (!queuedEnchantGuids.Contains(finalGuidStr))
                            {
                                queuedEnchantGuids.Add(finalGuidStr);
                                CustomEnchantmentsBuilder.GetOrBuildDynamicBlueprint(finalGuidStr);
                            }
                            selectedModel = null;
                            filtersDirty = true;
                        }
                    }
                    catch (Exception ex) { Main.ModEntry.Logger.Error($"Error finalizing custom: {ex}"); }
                }
                GUI.enabled = true; // IMPORTANT: Restore GUI state
                GUILayout.EndHorizontal();
            }
            GUILayout.EndVertical();
        }

        private void DrawEnumSelector(DynamicParam p, float scale, string currentlyOpenParam)
        {
            var enumType = Type.GetType(p.EnumTypeName);
            if (enumType == null) return;

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

            // 2. On ajoute les entrées virtuelles de EnumOverrides
            if (p.EnumOverrides != null)
            {
                foreach (var kvp in p.EnumOverrides)
                {
                    if (!filteredNames.Contains(kvp.Key))
                    {
                        if (p.EnumOnly != null && p.EnumOnly.Count > 0 && !p.EnumOnly.Any(eo => eo.Equals(kvp.Key, StringComparison.OrdinalIgnoreCase))) continue;
                        if (kvp.Value is Newtonsoft.Json.Linq.JObject jo && jo["Value"] != null)
                        {
                            filteredNames.Add(kvp.Key);
                            filteredValues.Add((int)jo["Value"]);
                        }
                    }
                }
            }

            // 3. Tri final par valeur numérique pour garantir l'ordre (ex: Mineur -> Normal -> Supérieur)
            var combined = filteredNames.Zip(filteredValues, (n, v) => new { Name = n, Value = v })
                            .OrderBy(x =>
                            {
                                // Si une override de valeur existe pour ce nom, on l'utilise pour le tri
                                if (p.EnumOverrides != null && p.EnumOverrides.TryGetValue(x.Name, out object ovr) && ovr is Newtonsoft.Json.Linq.JObject jo && jo["Value"] != null)
                                    return (int)jo["Value"];
                                return x.Value;
                            })
                            .ToList();

            var names = combined.Select(x => x.Name).ToArray();
            var values = combined.Select(x => x.Value).ToArray();

            if (names.Length == 1)
            {
                string displayName = names[0];
                if (p.EnumOverrides != null && p.EnumOverrides.TryGetValue(names[0], out object ovrObj)) displayName = Helpers.GetLocalizedString(ovrObj);
                else displayName = Helpers.GetString("ui_enum_" + names[0], names[0]);

                GUILayout.Label(displayName, new GUIStyle(GUI.skin.label) { fontSize = (int)(FONT_NORMAL * scale) });
                dynamicParamValues[p.Name] = values[0];
            }
            else
            {
                int currentVal = dynamicParamValues.ContainsKey(p.Name) ? dynamicParamValues[p.Name] : 0;
                string currentName = Enum.GetName(enumType, currentVal);

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
                if (p.EnumOverrides != null && !string.IsNullOrEmpty(currentName) && p.EnumOverrides.TryGetValue(currentName, out object overrideObj))
                    displayName = Helpers.GetLocalizedString(overrideObj);
                else
                    displayName = Helpers.GetString("ui_enum_" + displayName, displayName);

                GUIStyle enumBtnStyle = new GUIStyle(GUI.skin.button) { fontSize = (int)(FONT_LARGE * scale) };
                if (CButtonStyled(new GUIContent(displayName), enumBtnStyle, GUILayout.Width(250 * scale)))
                {
                    openDropdownParam = (openDropdownParam == p.Name) ? null : p.Name;
                }

                if (currentlyOpenParam == p.Name)
                {
                    GUILayout.BeginVertical(GUI.skin.box);
                    for (int i = 0; i < names.Length; i++)
                    {
                        string optName = names[i];
                        if (p.EnumOverrides != null && p.EnumOverrides.TryGetValue(names[i], out object optOverrideObj)) optName = Helpers.GetLocalizedString(optOverrideObj);
                        else optName = Helpers.GetString("ui_enum_" + optName, optName);

                        if (CButtonStyled(new GUIContent(optName), enumBtnStyle)) { dynamicParamValues[p.Name] = (int)values[i]; openDropdownParam = null; }
                    }
                    GUILayout.EndVertical();
                }
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
                costToPay = CraftingCalculator.GetMarginalCost(selectedItem, preceding, data, CraftingSettings.Instance.CostMultiplier);
            }
            else
            {
                costToPay = CraftingCalculator.GetMarginalCost(selectedItem, currentSelectedList, data, CraftingSettings.Instance.CostMultiplier);
            }
            int days = CraftingCalculator.GetCraftingDays(costToPay, CraftingSettings.Instance.InstantCrafting);
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

        private void DrawDynamicMetamagicList(float scale, string currentlyOpenParam)
        {
            int count = dynamicParamValues.ContainsKey("MetamagicCount") ? dynamicParamValues["MetamagicCount"] : 1;
            GUIStyle paramLabelStyle = new GUIStyle(GUI.skin.label) { fontSize = (int)(FONT_NORMAL * scale) };

            var enumType = typeof(Kingmaker.UnitLogic.Abilities.Metamagic);
            var allNames = Enum.GetNames(enumType);
            var allValues = (int[])Enum.GetValues(enumType);

            List<int> selectedMetamagics = new List<int>();
            for (int i = 0; i < count; i++)
            {
                string key = "Metamagic_" + i;
                if (dynamicParamValues.ContainsKey(key)) selectedMetamagics.Add(dynamicParamValues[key]);
                else { dynamicParamValues[key] = 0; selectedMetamagics.Add(0); }
            }

            for (int i = 0; i < count; i++)
            {
                string key = "Metamagic_" + i;
                int currentVal = dynamicParamValues[key];

                // Filtrage pour ne pas proposer ce qui est déjà sélectionné (sauf la valeur actuelle du slot)
                var availableNames = new List<string>();
                var availableValues = new List<int>();
                for (int j = 0; j < allNames.Length; j++)
                {
                    int val = allValues[j];
                    if (val != 0 && val != currentVal && selectedMetamagics.Contains(val)) continue;
                    availableNames.Add(allNames[j]);
                    availableValues.Add(val);
                }

                string currentName = Enum.GetName(enumType, currentVal) ?? "None";
                string displayName = Helpers.GetString("ui_enum_" + currentName, currentName);

                // --- RENDU DU SLOT DANS UNE BOX ---
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.BeginHorizontal();

                GUILayout.Label($"{Helpers.GetString("ui_metamagic_slot", "Slot")} {i + 1}", new GUIStyle(GUI.skin.label) { richText = true, fontSize = (int)(FONT_NORMAL * scale) }, GUILayout.Width(100 * scale));

                if (CButton(displayName, GUILayout.ExpandWidth(true)))
                {
                    openDropdownParam = (openDropdownParam == key) ? null : key;
                }

                if (i > 0) // Bouton pour supprimer le slot (uniquement après le premier)
                {
                    if (CButton("<color=red><b>X</b></color>", GUILayout.Width(30 * scale)))
                    {
                        // Décalage des slots suivants
                        for (int k = i; k < count - 1; k++) dynamicParamValues["Metamagic_" + k] = dynamicParamValues["Metamagic_" + (k + 1)];
                        dynamicParamValues.Remove("Metamagic_" + (count - 1));
                        dynamicParamValues["MetamagicCount"] = count - 1;
                        openDropdownParam = null;
                        return;
                    }
                }
                GUILayout.EndHorizontal();

                if (currentlyOpenParam == key)
                {
                    GUILayout.BeginVertical(GUI.skin.box);
                    for (int j = 0; j < availableNames.Count; j++)
                    {
                        string optName = availableNames[j];
                        string displayOpt = Helpers.GetString("ui_enum_" + optName, optName);
                        if (CButton(displayOpt)) { dynamicParamValues[key] = availableValues[j]; openDropdownParam = null; }
                    }
                    GUILayout.EndVertical();
                }
                GUILayout.EndVertical();
                GUILayout.Space(10 * scale);
            }

            var allMetamagics = Enum.GetValues(typeof(Kingmaker.UnitLogic.Abilities.Metamagic)).Cast<int>().Where(v => v != 0).ToList();
            int maxPossibleMetamagics = allMetamagics.Count;

            if (count < maxPossibleMetamagics && CButton("+ " + Helpers.GetString("ui_btn_add_metamagic", "Add Metamagic"), GUILayout.Width(200 * scale)))
            {
                // Trouver la première métamagie non déjà utilisée
                int nextValidMetamagic = 1; // Empower par défaut

                foreach (var v in allMetamagics)
                {
                    if (!selectedMetamagics.Contains(v))
                    {
                        nextValidMetamagic = v;
                        break;
                    }
                }

                dynamicParamValues["Metamagic_" + count] = nextValidMetamagic;
                dynamicParamValues["MetamagicCount"] = count + 1;
            }
        }
        private void BuyItem(string guid, int cost, bool toInventory)
        {
            if (Game.Instance.Player.Money >= cost)
            {
                var bp = ResourcesLibrary.TryGetBlueprint(BlueprintGuid.Parse(guid)) as BlueprintItem;
                if (bp != null)
                {
                    Game.Instance.Player.Money -= cost;
                    var entity = bp.CreateEntity();

                    if (toInventory)
                    {
                        Game.Instance.Player.Inventory.Add(entity);
                        feedbackMessage = string.Format(Helpers.GetString("ui_success_purchased_inv", "<color=green>Succès !</color> {0} ajouté à l'inventaire."), entity.Name);
                    }
                    else
                    {
                        DeferredInventoryOpener.CraftingBox.Add(entity);
                        feedbackMessage = string.Format(Helpers.GetString("ui_success_purchased_box", "<color=green>Succès !</color> {0} ajouté à l'établi."), entity.Name);
                    }
                }
            }
            else
            {
                feedbackMessage = string.Format(Helpers.GetString("ui_err_not_enough_gold_need", "<color=red>Or insuffisant !</color> (Besoin de {0} PO)"), cost);
            }
        }

        private void DrawRectBorder(Rect rect, float thickness, Color color)
        {
            Color oldColor = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, thickness), Texture2D.whiteTexture); // Haut
            GUI.DrawTexture(new Rect(rect.x, rect.y + rect.height - thickness, rect.width, thickness), Texture2D.whiteTexture); // Bas
            GUI.DrawTexture(new Rect(rect.x, rect.y, thickness, rect.height), Texture2D.whiteTexture); // Gauche
            GUI.DrawTexture(new Rect(rect.x + rect.width - thickness, rect.y, thickness, rect.height), Texture2D.whiteTexture); // Droite
            GUI.color = oldColor;
        }
        void DrawDescriptionEditor(int id)
        {
            float scale = CraftingSettings.Instance.ScalePercent / 100f;
            GUILayout.BeginVertical(GUI.skin.box);

            GUIStyle titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = (int)(FONT_LARGE * scale),
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            GUILayout.Label(Helpers.GetString("ui_title_edit_description", "Edit Item Description"), titleStyle);
            GUILayout.Space(10 * scale);

            GUIStyle areaStyle = new GUIStyle(GUI.skin.textArea)
            {
                fontSize = (int)(FONT_NORMAL * scale),
                wordWrap = true
            };

            descriptionScrollPosition = GUILayout.BeginScrollView(descriptionScrollPosition, GUILayout.ExpandHeight(true));
            editedDescription = GUILayout.TextArea(editedDescription, areaStyle, GUILayout.ExpandHeight(true));
            GUILayout.EndScrollView();

            GUILayout.Space(10 * scale);
            GUILayout.BeginHorizontal();

            if (CButton(Helpers.GetString("ui_btn_save", "Save"), GUILayout.Height(40 * scale)))
            {
                ItemRenamer.ChangeDescription(selectedItem, editedDescription);
                showDescriptionEditor = false;
                feedbackMessage = Helpers.GetString("ui_feedback_description_changed", "Description updated!");
            }

            GUILayout.Space(10 * scale);

            if (CButton(Helpers.GetString("ui_btn_cancel", "Cancel"), GUILayout.Height(40 * scale)))
            {
                showDescriptionEditor = false;
            }

            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }
    }
}
