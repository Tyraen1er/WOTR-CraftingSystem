using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using UnityEngine;
using Kingmaker;
using Kingmaker.Items;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Items.Ecnchantments;
using Kingmaker.Blueprints.Items.Weapons;
using Kingmaker.Blueprints.Items.Armors;
using Kingmaker.PubSubSystem;
using UniRx;
 
namespace CraftingSystem
{
    public class CraftingUI : MonoBehaviour
    {
        public static CraftingUI Instance;
        public bool IsOpen = false;
        public bool ShowSettings = false;
        
        // Paramètres
        public static float CostMultiplier = 1.0f;
        public static bool InstantCrafting = false;
        public static bool EnforcePointsLimit = true;
        public static int MaxTotalBonus = 10;
        public static int MaxEnhancementBonus = 5;
        public static bool RequirePlusOneFirst = true;
 
        public enum SourceFilter { All, TTRPG, Owlcat, Mods }
        public static SourceFilter CurrentSourceFilter = SourceFilter.All;
 
        private Vector2 scrollPosition;
        private Vector2 titleScrollPosition; // Ajout : pour le défilement horizontal du titre
        public string feedbackMessage = "";
        private string newNameDraft = "";
        private string enchantmentSearch = "";
        private ItemEntity selectedItem = null;
        private bool lastOpenState = false;
        
        private int m_ScalePercent = 100; 
        private const int MIN_SCALE_PERCENT = 100;
 
        // Largeurs de boutons
        private const float BUTTON_OPTION_WIDTH_BASE = 160f; 
        private const float BUTTON_CLOSE_WIDTH_BASE  = 80f;  
 
        // Auto-scale reference
        private const float REFERENCE_WIDTH = 2560f;
        private const float REFERENCE_HEIGHT = 1440f;
 
        // Settings file
        private const string SETTINGS_FILENAME = "settings.json";
 
        // Sélection multiple
        private HashSet<string> queuedEnchantGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
 
        // Mots clés considérés "spéciaux"
        private static readonly string[] SpecialEnchantKeywords = new[] { "Holy", "Unholy", "Flaming", "Shocking", "Frost", "Corrosive", "Keen", "Spiked", "Foudre", "Feu", "Glace", "électrique" };
 
        void Awake() 
        { 
            Instance = this;
            LoadSettings();
        }
 
        [DataContract]
        private class UiSettings
        {
            [DataMember] public float CostMultiplier = 1.0f;
            [DataMember] public bool InstantCrafting = false;
            [DataMember] public bool EnforcePointsLimit = true;
            [DataMember] public int MaxTotalBonus = 10;
            [DataMember] public int MaxEnhancementBonus = 5;
            [DataMember] public bool RequirePlusOneFirst = true;
            [DataMember] public int ScalePercent = 100;
            [DataMember] public float OptionButtonBase = BUTTON_OPTION_WIDTH_BASE;
            [DataMember] public float CloseButtonBase = BUTTON_CLOSE_WIDTH_BASE;
            [DataMember] public SourceFilter SourceFilterValue = SourceFilter.All;
        }
 
        private void LoadSettings()
        {
            try
            {
                if (Main.ModEntry == null || string.IsNullOrEmpty(Main.ModEntry.Path)) return;
                var path = Path.Combine(Main.ModEntry.Path, SETTINGS_FILENAME);
                if (!File.Exists(path)) return;
 
                using (var fs = File.OpenRead(path))
                {
                    var serializer = new DataContractJsonSerializer(typeof(UiSettings));
                    var obj = serializer.ReadObject(fs) as UiSettings;
                    if (obj == null) return;
 
                    CostMultiplier = obj.CostMultiplier;
                    InstantCrafting = obj.InstantCrafting;
                    EnforcePointsLimit = obj.EnforcePointsLimit;
                    MaxTotalBonus = obj.MaxTotalBonus;
                    MaxEnhancementBonus = obj.MaxEnhancementBonus;
                    RequirePlusOneFirst = obj.RequirePlusOneFirst;
                    if (obj.ScalePercent > 0) m_ScalePercent = obj.ScalePercent;
                    CurrentSourceFilter = obj.SourceFilterValue;
                }
            }
            catch (Exception ex) { Main.ModEntry.Logger.Error($"[ATELIER] Failed to load UI settings: {ex}"); }
        }
 
        private void SaveSettings()
        {
            try
            {
                if (Main.ModEntry == null || string.IsNullOrEmpty(Main.ModEntry.Path)) return;
                var path = Path.Combine(Main.ModEntry.Path, SETTINGS_FILENAME);
 
                var s = new UiSettings
                {
                    CostMultiplier = CostMultiplier,
                    InstantCrafting = InstantCrafting,
                    EnforcePointsLimit = EnforcePointsLimit,
                    MaxTotalBonus = MaxTotalBonus,
                    MaxEnhancementBonus = MaxEnhancementBonus,
                    RequirePlusOneFirst = RequirePlusOneFirst,
                    ScalePercent = m_ScalePercent,
                    OptionButtonBase = BUTTON_OPTION_WIDTH_BASE,
                    CloseButtonBase = BUTTON_CLOSE_WIDTH_BASE,
                    SourceFilterValue = CurrentSourceFilter
                };
 
                using (var ms = new MemoryStream())
                {
                    var serializer = new DataContractJsonSerializer(typeof(UiSettings));
                    serializer.WriteObject(ms, s);
                    ms.Position = 0;
                    using (var sr = new StreamReader(ms, Encoding.UTF8))
                    {
                        var json = sr.ReadToEnd();
                        File.WriteAllText(path, json);
                    }
                }
            }
            catch (Exception ex) { Main.ModEntry.Logger.Error($"[ATELIER] Failed to save UI settings: {ex}"); }
        }
 
        private void UpdateAutoScale()
        {
            float resScale = Math.Min(Screen.width / REFERENCE_WIDTH, Screen.height / REFERENCE_HEIGHT);
            float dpiScale = 1f;
            try { if (Screen.dpi > 0) dpiScale = Screen.dpi / 96f; } catch { dpiScale = 1f; }
            float finalScale = Mathf.Clamp(resScale * dpiScale, 0.5f, 2.0f);
            m_ScalePercent = (int)(finalScale * 100f);
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
 
            float scale = m_ScalePercent / 100f;
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
            float scale = m_ScalePercent / 100f;

            if (!string.IsNullOrEmpty(feedbackMessage))
            {
                GUILayout.Label(feedbackMessage, new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = (int)(18 * scale) });
                if (GUILayout.Button("OK", GUILayout.Height(40 * scale))) feedbackMessage = "";
                return;
            }

            // --- HEADER AVEC DÉFILEMENT POUR TEXTE LONG ---
            GUILayout.BeginHorizontal();
            
            string title = "Atelier";
            if (ShowSettings) title = "Configuration";
            else if (selectedItem != null) title = "Détails : " + selectedItem.Name;
            else title = "Sélection d'objet";
            
            // Nouveau conteneur défilable pour éviter que le texte pousse les boutons
            titleScrollPosition = GUILayout.BeginScrollView(titleScrollPosition, GUIStyle.none, GUIStyle.none, GUILayout.ExpandWidth(true), GUILayout.Height(45 * scale));
            GUILayout.Label(title, new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = (int)(16 * scale), wordWrap = false });
            GUILayout.EndScrollView();
            
            GUILayout.Space(10);
            
            if (selectedItem != null && !ShowSettings)
            {
                if (GUILayout.Button("<< RETOUR", GUILayout.Width(130 * scale), GUILayout.Height(30 * scale))) 
                {
                    selectedItem = null;
                    newNameDraft = "";
                    queuedEnchantGuids.Clear();
                }
            }

            float windowWidth = 800f * scale;
            float optionWidth = Mathf.Max(BUTTON_OPTION_WIDTH_BASE * scale, windowWidth * 0.14f);
            float closeWidth  = Mathf.Max(BUTTON_CLOSE_WIDTH_BASE  * scale, windowWidth * 0.06f);

            if (GUILayout.Button(ShowSettings ? "Atelier" : "Options", GUILayout.Width(optionWidth), GUILayout.Height(30 * scale)))
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
 
            if (!items.Any()) GUILayout.Label("\n   (Aucun objet n'est stocké dans l'atelier)");
            else 
            {
                GUIStyle entryStyle = new GUIStyle(GUI.skin.button) { 
                    wordWrap = true, 
                    alignment = TextAnchor.UpperCenter, // Aligne le texte vers le haut (centré horizontalement)
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
                        if (project != null) label += " (En forge...)";
 
                        if (GUILayout.Button(label, entryStyle, GUILayout.Width(350 * scale), GUILayout.Height(50 * scale))) 
                        {
                            selectedItem = it;
                            newNameDraft = it.Name;
                            queuedEnchantGuids.Clear();
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
                GUILayout.Label($"<b>WILCER EST EN TRAIN DE TRAVAILLER SUR CET OBJET</b>", new GUIStyle(GUI.skin.label) { richText = true, alignment = TextAnchor.MiddleCenter });
                GUILayout.Label($"Temps restant estimé : {remainingDays:F1} jours", new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter });
                GUILayout.Space(20);
                if (GUILayout.Button("Fermer l'interface", GUILayout.Height(40 * scale))) IsOpen = false;
                GUILayout.EndVertical();
                return;
            }

            // DÉBUT DU GRAND SCROLLVIEW GLOBAL (Pour tout le contenu dynamique)
            // On force l'affichage de la barre verticale systématiquement (GUI.skin.verticalScrollbar)
            scrollPosition = GUILayout.BeginScrollView(scrollPosition, false, true, GUILayout.ExpandHeight(true));
            
            // --- SECTION RENOMMAGE (ENCHANTEMENT GRATUIT) ---
            GUILayout.Label("Action Spéciale : Renommer l'objet (Gratuit)");
            GUILayout.BeginHorizontal();
            
            float windowWidth = 800f * scale;
            float buttonsSpace = (100f + 80f + 25f) * scale;
            // On ajoute 20px (la largeur de la barre de scroll) au padding pour éviter de créer un scroll horizontal
            float padding = (45f + 20f) * scale; 
            float exactTextWidth = windowWidth - buttonsSpace - padding;
 
            GUIStyle textFieldStyle = new GUIStyle(GUI.skin.textField);
            textFieldStyle.wordWrap = false; 
 
            newNameDraft = GUILayout.TextField(
                newNameDraft, 
                textFieldStyle, 
                GUILayout.Width(exactTextWidth), 
                GUILayout.Height(30 * scale)
            );
            
            GUILayout.Space(10 * scale);
            
            if (GUILayout.Button("Renommer", GUILayout.Width(100 * scale), GUILayout.Height(30 * scale)))
            {
                RenameItem(selectedItem, newNameDraft);
                feedbackMessage = "L'objet a été renommé !";
            }
 
            if (selectedItem != null && GUILayout.Button("Auto", GUILayout.Width(80 * scale), GUILayout.Height(30 * scale)))
            {
                string autoName = GenerateAutoName(selectedItem);
                RenameItem(selectedItem, autoName);
                newNameDraft = autoName;
                feedbackMessage = "Nom automatique généré.";
            }
            GUILayout.EndHorizontal();
            
            GUILayout.Space(20);
            Div(scale);
            GUILayout.Space(10);
            
            // --- SECTION : ENCHANTEMENTS DÉJÀ PRÉSENTS ---
            GUILayout.Label("Enchantements appliqués :", new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold });
            var currentEnchants = selectedItem.Enchantments.ToList();
            if (!currentEnchants.Any()) 
            {
                GUILayout.Label("<i>(Aucun enchantement magique)</i>", new GUIStyle(GUI.skin.label) { richText = true, fontSize = (int)(12 * scale) });
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
                    GUILayout.Label($"{ench.Blueprint.name} (+{pointValue})", GUILayout.ExpandWidth(true));
                    if (GUILayout.Button("Retirer", GUILayout.Width(80 * scale)))
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
            GUILayout.Label("Enchantements disponibles :", new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold });
 
            GUILayout.BeginHorizontal();
            GUILayout.Label("Recherche : ", GUILayout.Width(100 * scale));
            enchantmentSearch = GUILayout.TextField(enchantmentSearch, GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();
 
            GUILayout.Space(5);
 
            if (EnchantmentScanner.IsSyncing)
            {
                GUILayout.Label($"({EnchantmentScanner.LastSyncMessage})", new GUIStyle(GUI.skin.label) { richText = true, fontSize = (int)(12 * scale), alignment = TextAnchor.MiddleCenter });
            }
 
            GUILayout.Space(5);
 
            GUILayout.BeginVertical(GUI.skin.box); // L'expandHeight est retiré ici car c'est le ScrollView global qui gère l'espace
 
            if (EnchantmentScanner.IsSyncing)
            {
                GUILayout.Label("Scan en cours — aucun enchantement disponible pour l'instant.", new GUIStyle(GUI.skin.label) { fontSize = (int)(12 * scale), alignment = TextAnchor.MiddleCenter });
            }
            else
            {
                var available = EnchantmentScanner.GetFor(selectedItem);
                
                foreach (var data in available)
                {
                    if (!string.IsNullOrEmpty(enchantmentSearch) && !data.Name.ToLower().Contains(enchantmentSearch.ToLower())) continue;
                    
                    if (CurrentSourceFilter == SourceFilter.TTRPG && data.Source != "TTRPG") continue;
                    if (CurrentSourceFilter == SourceFilter.Owlcat && data.Source != "TTRPG" && data.Source != "Owlcat") continue;
                    if (CurrentSourceFilter == SourceFilter.Mods && data.Source != "Mod") continue;
                    
                    long costToPay = CraftingCalculator.GetUpgradeCost(selectedItem, data, CostMultiplier);
                    int days = CraftingCalculator.GetCraftingDays(costToPay, InstantCrafting);
                    
                    if (data.GoldOverride >= 0) costToPay = (long)(data.GoldOverride * CostMultiplier);
                    if (data.DaysOverride >= 0) days = (int)data.DaysOverride;
 
                    GUILayout.BeginHorizontal(GUI.skin.box);
                    bool isSelected = queuedEnchantGuids.Contains(data.Guid);
                    bool newSelected = GUILayout.Toggle(isSelected, $"{data.Name}", GUILayout.Width(320 * scale)); // Réduit légèrement pour la barre de scroll
                    GUILayout.FlexibleSpace();
                    GUILayout.Label($"{costToPay} po / {days} j   (+{data.PointCost})", GUILayout.Width(220 * scale));
                    
                    if (newSelected && !isSelected) queuedEnchantGuids.Add(data.Guid);
                    if (!newSelected && isSelected) queuedEnchantGuids.Remove(data.Guid);
 
                    GUILayout.EndHorizontal();
                }
            }
 
            GUILayout.EndVertical();
            GUILayout.EndScrollView(); // FIN DU GRAND SCROLLVIEW
 
            // =========================================================================
            // BLOC FIXE EN BAS (Toujours visible, ne scrolle pas)
            // =========================================================================
            GUILayout.Space(8);
 
            var selectedList = queuedEnchantGuids.Select(g => EnchantmentScanner.GetByGuid(g)).Where(d => d != null).ToList();
            long totalCost = 0;
            int totalDays = 0;
            foreach (var d in selectedList)
            {
                long c = CraftingCalculator.GetUpgradeCost(selectedItem, d, CostMultiplier);
                if (d.GoldOverride >= 0) c = (long)(d.GoldOverride * CostMultiplier);
                totalCost += c;
                totalDays += CraftingCalculator.GetCraftingDays(c, InstantCrafting);
            }
 
            int currentLevelPoints = CalculateDisplayedEnchantmentPoints(selectedItem);
            int maxLevel = MaxTotalBonus;
            int selectedPoints = selectedList.Sum(d => d.PointCost);
 
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Niveau actuel : {currentLevelPoints}/{maxLevel}", GUILayout.ExpandWidth(false));
            GUILayout.FlexibleSpace();
            GUILayout.Label($"Sélection : +{selectedPoints} — Total : {totalCost} po / ~{totalDays} j", GUILayout.ExpandWidth(false));
            GUILayout.EndHorizontal();
 
            GUILayout.Space(6);
 
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Valider la sélection", GUILayout.Width(200 * scale), GUILayout.Height(32 * scale)))
            {
                string validationError = ValidateSelectionBeforeStart(selectedItem, selectedList, totalCost);
                if (!string.IsNullOrEmpty(validationError))
                {
                    feedbackMessage = validationError;
                }
                else
                {
                    // Retirer l'or en une fois
                    if (Game.Instance.Player.Money >= totalCost)
                    {
                        Game.Instance.Player.Money -= (int)totalCost;
                        
                        foreach (var d in selectedList)
                        {
                            long c = CraftingCalculator.GetUpgradeCost(selectedItem, d, CostMultiplier);
                            if (d.GoldOverride >= 0) c = (long)(d.GoldOverride * CostMultiplier);
                            int days = CraftingCalculator.GetCraftingDays(c, InstantCrafting);
                            CraftingActions.StartCraftingProject(selectedItem, d, (int)c, days);
                        }

                        // =========================================================================
                        // NOUVEAU : ENVOI DU MESSAGE DANS LE LOG DE COMBAT
                        // =========================================================================
                        try 
                        {
                            string logText = $"<color=#E2C675>[Atelier]</color> <b>{selectedItem.Name}</b> a été envoyé en forge pour <b>{totalCost} po</b>.";
                            
                            // L'interface attend un string, on lui passe directement !
                            Kingmaker.PubSubSystem.EventBus.RaiseEvent<Kingmaker.PubSubSystem.ILogMessageUIHandler>(
                                h => h.HandleLogMessage(logText)
                            );
                        }
                        catch (Exception ex)
                        {
                            Main.ModEntry.Logger.Error($"[ATELIER] Impossible d'écrire dans le log de combat : {ex.Message}");
                        }
                        // =========================================================================

                        queuedEnchantGuids.Clear();
                        selectedItem = null;
                        feedbackMessage = $"Projets lancés : {selectedList.Count} enchantement(s).";
                    }
                    else
                    {
                        feedbackMessage = "Erreur inattendue : fonds insuffisants au moment du paiement.";
                    }
                }
            }
 
            if (GUILayout.Button("Annuler la sélection", GUILayout.Width(180 * scale), GUILayout.Height(32 * scale)))
            {
                queuedEnchantGuids.Clear();
            }
            GUILayout.EndHorizontal();
 
            GUILayout.EndVertical(); 
        }
 
        // --- GÉNÉRATEUR MANUEL DE NOM AUTO ---
        private string GenerateAutoName(ItemEntity item)
        {
            if (item == null) return "";
            string uniqueName = item.Blueprint.m_DisplayNameText;
            string defaultName = "";

            if (item.Blueprint is BlueprintItemWeapon bpW) defaultName = bpW.Type.DefaultName;
            else if (item.Blueprint is BlueprintItemArmor bpA) defaultName = bpA.Type.DefaultName;

            string name = "";
            if (string.IsNullOrEmpty(uniqueName)) 
            {
                name = item.GetEnchantmentPrefixes() + defaultName + item.GetEnchantmentSuffixes();
            } 
            else 
            {
                var suffixes = item.GetCustomEnchantmentSuffixes();
                if (System.Text.RegularExpressions.Regex.Match(suffixes, @"\+\d").Success) 
                {
                    name = item.GetCustomEnchantmentPrefixes() + System.Text.RegularExpressions.Regex.Replace(uniqueName, @"\+\d", "") + suffixes;
                } 
                else 
                {
                    name = item.GetCustomEnchantmentPrefixes() + uniqueName + suffixes;
                }
            }
            return name.Replace("  ", " ").Trim();
        }

        private int CalculateDisplayedEnchantmentPoints(ItemEntity item)
        {
            if (item == null) return 0;
            int points = 0;
            foreach (var e in item.Enchantments)
            {
                string guid = e.Blueprint.AssetGuid.ToString();
                var overrideData = EnchantmentScanner.GetByGuid(guid);
                int baseCost = overrideData?.PointCost ?? e.Blueprint.EnchantmentCost;
                if (baseCost <= 0) baseCost = 0;
                bool isSpecial = SpecialEnchantKeywords.Any(k => e.Blueprint.name.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0);
                int weight = isSpecial ? 2 : 1;
                points += baseCost * weight;
            }
            return points;
        }
 
        private string ValidateSelectionBeforeStart(ItemEntity item, List<EnchantmentData> selectedList, long totalCost)
        {
            if (item == null || selectedList == null || selectedList.Count == 0) return "Aucun enchantement sélectionné.";
            if (Game.Instance.Player.Money < totalCost) return "Vous n'avez pas assez d'or pour lancer tous les projets sélectionnés.";
 
            int currentPoints = CalculateDisplayedEnchantmentPoints(item);
            int currentEnhancement = 0;
            foreach (var e in item.Enchantments)
            {
                string guid = e.Blueprint.AssetGuid.ToString();
                var overrideData = EnchantmentScanner.GetByGuid(guid);
                int p = overrideData?.PointCost ?? e.Blueprint.EnchantmentCost;
                if (p <= 0) continue;
 
                bool looksLikeEnhancement = false;
                if (overrideData != null && overrideData.Categories != null && overrideData.Categories.Contains("Enhancement")) looksLikeEnhancement = true;
                if (e.Blueprint.name.Contains("Plus") || e.Blueprint.name.StartsWith("Enhancement")) looksLikeEnhancement = true;
                if (looksLikeEnhancement)
                {
                    currentEnhancement = Math.Max(currentEnhancement, p);
                }
            }
 
            bool hasMasterwork = item.Enchantments.Any(e => string.Equals(e.Blueprint.AssetGuid.ToString(), "6b38844e2bffbac48b63036b66e735be", StringComparison.OrdinalIgnoreCase));
            bool hasEnhancementOriginally = currentEnhancement > 0;
            bool anySelectedEnh = selectedList.Any(d => d.Categories.Contains("Enhancement") || d.PointCost > 0);
 
            bool isWeapon = item.Blueprint is BlueprintItemWeapon;
            if (RequirePlusOneFirst && isWeapon)
            {
                if (!hasEnhancementOriginally && !anySelectedEnh && !hasMasterwork)
                    return "Une arme doit être masterwork ou posséder (ou recevoir) une altération (+1) avant d'être enchantée spécial.";
            }
 
            int addedPoints = selectedList.Sum(d => d.PointCost);
            if (EnforcePointsLimit && currentPoints + addedPoints > MaxTotalBonus)
                return $"Limite de puissance totale dépassée (max +{MaxTotalBonus}).";
 
            int selectedMaxEnh = 0;
            foreach (var d in selectedList)
            {
                if (d.Categories.Contains("Enhancement") || d.PointCost > 0)
                    selectedMaxEnh = Math.Max(selectedMaxEnh, d.PointCost);
            }
            if (EnforcePointsLimit && Math.Max(currentEnhancement, selectedMaxEnh) > MaxEnhancementBonus)
                return $"Limite d'altération dépassée (max +{MaxEnhancementBonus}).";
 
            return null;
        }
 
        void DrawSettingsGUI(float scale)
        {
            float prevCostMult = CostMultiplier;
            bool prevInstant = InstantCrafting;
            bool prevEnforce = EnforcePointsLimit;
            int prevMaxEnh = MaxEnhancementBonus;
            int prevMaxTotal = MaxTotalBonus;
            bool prevRequirePlus = RequirePlusOneFirst;
            SourceFilter prevSourceFilter = CurrentSourceFilter;
 
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("Réglages de l'Atelier", new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold });
            
            GUILayout.Space(10);
            
            GUILayout.BeginHorizontal();
            GUILayout.Label(" Multiplicateur de coût : " + CostMultiplier.ToString("F1"), GUILayout.Width(200 * scale));
            CostMultiplier = GUILayout.HorizontalSlider(CostMultiplier, 0f, 5f, GUILayout.Width(150 * scale));
            GUILayout.EndHorizontal();
 
            bool previousInstantCrafting = InstantCrafting;
            InstantCrafting = GUILayout.Toggle(InstantCrafting, "Craft Instantané");
 
            if (InstantCrafting && !previousInstantCrafting)
            {
                try
                {
                    var workshop = Game.Instance.Player.MainCharacter.Value.Get<UnitPartWilcerWorkshop>();
                    if (workshop != null)
                    {
                        workshop.CheckAndFinishProjects();
                        feedbackMessage = "Toutes les forges en cours ont été terminées instantanément !";
                    }
                }
                catch (Exception ex) { Main.ModEntry.Logger.Error($"[UI-DEBUG] CRASH : {ex.Message}"); }
            }
            
            GUILayout.Space(10);
            
            EnforcePointsLimit = GUILayout.Toggle(EnforcePointsLimit, " Appliquer les limites de bonus (Pathfinder)");
            
            if (EnforcePointsLimit)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($" Max Altération : +{MaxEnhancementBonus}", GUILayout.Width(150 * scale));
                MaxEnhancementBonus = (int)GUILayout.HorizontalSlider(MaxEnhancementBonus, 1, 20, GUILayout.Width(150 * scale));
                GUILayout.EndHorizontal();
 
                GUILayout.BeginHorizontal();
                GUILayout.Label($" Max Total : +{MaxTotalBonus}", GUILayout.Width(150 * scale));
                MaxTotalBonus = (int)GUILayout.HorizontalSlider(MaxTotalBonus, 1, 50, GUILayout.Width(150 * scale));
                GUILayout.EndHorizontal();
 
                RequirePlusOneFirst = GUILayout.Toggle(RequirePlusOneFirst, " Prérequis : +1 Altération minimum");
            }
 
            GUILayout.Space(10);
            GUILayout.Label("Affichage des sources :");
            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(CurrentSourceFilter == SourceFilter.TTRPG, "TTRPG")) CurrentSourceFilter = SourceFilter.TTRPG;
            if (GUILayout.Toggle(CurrentSourceFilter == SourceFilter.Owlcat, "Owlcat")) CurrentSourceFilter = SourceFilter.Owlcat;
            if (GUILayout.Toggle(CurrentSourceFilter == SourceFilter.Mods, "Mods")) CurrentSourceFilter = SourceFilter.Mods;
            if (GUILayout.Toggle(CurrentSourceFilter == SourceFilter.All, "Tout")) CurrentSourceFilter = SourceFilter.All;
            GUILayout.EndHorizontal();
 
            GUILayout.Space(20);
            
            GUILayout.Label("Outils de diagnostic :");
            GUILayout.Label(EnchantmentScanner.LastSyncMessage);
            if (GUILayout.Button("Forcer la synchronisation (Scan intégral)", GUILayout.Height(30 * scale)))
            {
                EnchantmentScanner.ForceSync();
            }
            
            GUILayout.FlexibleSpace();
            GUILayout.EndVertical();
 
            if (prevCostMult != CostMultiplier || prevInstant != InstantCrafting || prevEnforce != EnforcePointsLimit
                || prevMaxEnh != MaxEnhancementBonus || prevMaxTotal != MaxTotalBonus || prevRequirePlus != RequirePlusOneFirst
                || prevSourceFilter != CurrentSourceFilter)
            {
                SaveSettings();
            }
        }
 
        private void Div(float scale)
        {
            Rect rect = GUILayoutUtility.GetLastRect();
            GUI.Box(new Rect(rect.x, rect.y + rect.height + 5, rect.width, 2 * scale), "");
        }
 
        private void RenameItem(ItemEntity item, string name)
        {
            if (item == null) return;
            try
            {
                if (string.IsNullOrEmpty(name)) item.Remove<ItemPartCustomName>();
                else item.Ensure<ItemPartCustomName>().CustomName = name;
                
                item.Identify();
                Main.ModEntry.Logger.Log($"[ATELIER] Renommé : {(string.IsNullOrEmpty(name) ? "Original" : name)}");
            }
            catch (Exception ex) { Main.ModEntry.Logger.Error($"Erreur renommage : {ex}"); }
        }
    }
}