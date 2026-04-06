using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;
using Kingmaker;
using Kingmaker.Items;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Items.Ecnchantments;
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
        public string feedbackMessage = "";
        private string newNameDraft = "";
        private string enchantmentSearch = "";
        private ItemEntity selectedItem = null;
        private bool lastOpenState = false;
        
        private int m_ScalePercent = 100; 
        private const int MIN_SCALE_PERCENT = 100;

        void Awake() 
        { 
            Instance = this; 
        }

        void OnGUI()
        {
            if (!IsOpen) 
            {
                lastOpenState = false;
                return;
            }

            // Réinitialisation de la navigation lors de l'ouverture
            if (!lastOpenState)
            {
                selectedItem = null;
                ShowSettings = false;
                lastOpenState = true;
            }

            // TOUCHE DE SECOURS : ECHAP pour fermer quoi qu'il arrive
            if (Event.current != null && Event.current.isKey && Event.current.keyCode == KeyCode.Escape)
            {
                IsOpen = false;
                Event.current.Use();
                return;
            }

            // Déclenchement unique de la sync au premier dialogue de la session
            EnchantmentScanner.StartSync();

            // Vérification des projets terminés à l'ouverture
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

            // --- HEADER ---
            GUILayout.BeginHorizontal();
            
            string title = "Atelier";
            if (ShowSettings) title = "Configuration";
            else if (selectedItem != null) title = "Détails : " + selectedItem.Name;
            else title = "Sélection d'objet";
            
            GUILayout.Label(title, new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = (int)(16 * scale) }, GUILayout.ExpandWidth(true));
            
            GUILayout.FlexibleSpace();
            
            if (selectedItem != null && !ShowSettings)
            {
                if (GUILayout.Button("<< RETOUR", GUILayout.Width(150 * scale), GUILayout.Height(30 * scale))) 
                {
                    selectedItem = null;
                    newNameDraft = "";
                }
            }
            
            if (GUILayout.Button(ShowSettings ? "Atelier" : "Options", GUILayout.Width(80 * scale), GUILayout.Height(30 * scale)))
            {
                ShowSettings = !ShowSettings;
            }
            
            if (GUILayout.Button("X", GUILayout.Width(40 * scale), GUILayout.Height(30 * scale))) IsOpen = false;
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
                GUIStyle entryStyle = new GUIStyle(GUI.skin.button) { wordWrap = true, alignment = TextAnchor.MiddleCenter, fontSize = (int)(14 * scale) };
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
            
            // --- SECTION RENOMMAGE (ENCHANTEMENT GRATUIT) ---
            GUILayout.Label("Action Spéciale : Renommer l'objet (Gratuit)");
            GUILayout.BeginHorizontal();
            newNameDraft = GUILayout.TextField(newNameDraft, GUILayout.ExpandWidth(true), GUILayout.Height(30 * scale));
            
            if (GUILayout.Button("Renommer", GUILayout.Width(100 * scale), GUILayout.Height(30 * scale)))
            {
                RenameItem(selectedItem, newNameDraft);
                feedbackMessage = "L'objet a été renommé !";
            }

            if (selectedItem != null && GUILayout.Button("Auto", GUILayout.Width(80 * scale), GUILayout.Height(30 * scale)))
            {
                string baseName = RestoreIntelligentName(selectedItem);
                RenameItem(selectedItem, baseName);
                newNameDraft = baseName;
                feedbackMessage = "Nom mis à jour.";
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
                    GUILayout.BeginHorizontal(GUI.skin.box);
                    GUILayout.Label($"{ench.Blueprint.name} (+{ench.Blueprint.EnchantmentCost})", GUILayout.ExpandWidth(true));
                    if (GUILayout.Button("Retirer", GUILayout.Width(80 * scale)))
                    {
                        selectedItem.RemoveEnchantment(ench);
                        selectedItem.Identify(); 
                        //feedbackMessage = $"Enchantement {ench.Blueprint.name} retiré !";
                    }
                    GUILayout.EndHorizontal();
                }
            }

            GUILayout.Space(20);
            Div(scale);
            GUILayout.Space(10);
            
            // --- RECHERCHE ---
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

            // --- LISTE DES ENCHANTEMENTS (SCROLLABLE) ---
            scrollPosition = GUILayout.BeginScrollView(scrollPosition, GUILayout.ExpandHeight(true));
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
                GUILayout.Label($"{data.Name} (+{data.PointCost})", GUILayout.Width(180 * scale));
                GUILayout.FlexibleSpace();
                GUILayout.Label($"{costToPay} po / {days} j", GUILayout.Width(120 * scale));
                
                if (GUILayout.Button("Ajouter", GUILayout.Width(100 * scale)))
                {
                    TryAddEnchantment(selectedItem, data, (int)costToPay, days);
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
            
            GUILayout.FlexibleSpace();
            GUILayout.EndVertical();
        }

        private void TryAddEnchantment(ItemEntity item, EnchantmentData data, int cost, int days)
        {
            if (item == null || data == null) return;

            if (Game.Instance.Player.Money < cost)
            {
                feedbackMessage = "Vous n'avez pas assez d'or pour cet enchantement !";
                return;
            }

            if (EnforcePointsLimit)
            {
                int currentPoints = item.Enchantments.Sum(e => e.Blueprint.EnchantmentCost);
                int currentEnhancement = 0;
                foreach(var e in item.Enchantments) {
                    if (e.Blueprint.name.Contains("Plus") || e.Blueprint is BlueprintWeaponEnchantment we && we.EnchantmentCost > 0 && e.Blueprint.name.StartsWith("Enhancement"))
                        currentEnhancement = Math.Max(currentEnhancement, e.Blueprint.EnchantmentCost);
                }

                if (data.Categories.Contains("Enhancement") && (currentEnhancement + data.PointCost > MaxEnhancementBonus))
                {
                    feedbackMessage = $"Limite d'altération (+{MaxEnhancementBonus}) dépassée !";
                    return;
                }

                if (currentPoints + data.PointCost > MaxTotalBonus)
                {
                    feedbackMessage = $"Limite de puissance totale (+{MaxTotalBonus}) dépassée !";
                    return;
                }

                if (RequirePlusOneFirst && currentEnhancement == 0 && !data.Categories.Contains("Enhancement"))
                {
                    feedbackMessage = "Une arme doit avoir au moins +1 d'altération avant d'être enchantée spécial.";
                    return;
                }
            }

            CraftingActions.StartCraftingProject(item, data, cost, days);
            
            if (days > 0)
            {
                selectedItem = null; 
                feedbackMessage = $"Projet lancé ! Wilcer Garms a pris l'objet en forge pour {days} jours.";
            }
            else
            {
                feedbackMessage = $"Succès immédiat ! {data.Name} a été appliqué à {item.Name} ({cost} po déduits).";
            }
        }

        void DrawSettingsGUI(float scale)
        {
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
                Main.ModEntry.Logger.Log("[UI-DEBUG] Toggle 'Craft Instantané' activé.");
                try
                {
                    var workshop = Game.Instance.Player.MainCharacter.Value.Get<UnitPartWilcerWorkshop>();
                    if (workshop == null)
                    {
                        Main.ModEntry.Logger.Error("[UI-DEBUG] ERREUR : Le workshop est null !");
                    }
                    else
                    {
                        Main.ModEntry.Logger.Log($"[UI-DEBUG] Lancement CheckAndFinishProjects. Projets en cours : {workshop.ActiveProjects.Count}");
                        workshop.CheckAndFinishProjects();
                        Main.ModEntry.Logger.Log("[UI-DEBUG] CheckAndFinishProjects terminé avec succès.");
                        feedbackMessage = "Toutes les forges en cours ont été terminées instantanément !";
                    }
                }
                catch (Exception ex)
                {
                    Main.ModEntry.Logger.Error($"[UI-DEBUG] CRASH lors du clic sur Instantané : {ex.Message}\n{ex.StackTrace}");
                }
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

        private string RestoreIntelligentName(ItemEntity item)
        {
            if (item == null) return "";
            
            string baseDisplayName = item.Blueprint.m_DisplayNameText.ToString();
            
            string[] magicPrefixes = { "Shocking", "Flaming", "Frost", "Corrosive", "Keen", "Holy", "Unholy", "Spiked", "Foudre", "Feu", "Glace", "électrique" };

            string cleanName = baseDisplayName;
            foreach (var p in magicPrefixes) {
                cleanName = cleanName.Replace(p, "").Replace(p.ToLower(), "");
            }

            cleanName = System.Text.RegularExpressions.Regex.Replace(cleanName, @"\+\d+", "");
            cleanName = cleanName.Replace("  ", " ").Trim();

            int bonus = item.Enchantments.Sum(e => e.Blueprint.EnchantmentCost);
            return cleanName + (bonus > 0 ? $" +{bonus}" : "");
        }
    }
}