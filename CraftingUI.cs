using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;
using Kingmaker;
using Kingmaker.Items;

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
        private string feedbackMessage = "";
        private string newNameDraft = "";
        private string enchantmentSearch = "";
        private string selectedCategory = "All";
        private ItemEntity selectedItem = null;
        
        private int m_ScalePercent = 100; 
        private const int MIN_SCALE_PERCENT = 100;

        void Awake() 
        { 
            Instance = this; 
        }

        void OnGUI()
        {
            if (!IsOpen) return;

            // Déclenchement unique de la sync au premier dialogue de la session
            EnchantmentScanner.StartSync();

            // Vérification des projets terminés à l'ouverture
            var workshop = Game.Instance.Player.MainCharacter.Value.Get<UnitPartWilcerWorkshop>();
            workshop?.CheckAndFinishProjects();

            float scale = m_ScalePercent / 100f;
            float width = 600f * scale;
            float height = 500f * scale;
            Rect windowRect = new Rect((Screen.width - width) / 2f, (Screen.height - height) / 2f, width, height);

            if (Event.current != null && !windowRect.Contains(Event.current.mousePosition))
            {
                if (Event.current.type == EventType.MouseDown || Event.current.type == EventType.MouseUp || Event.current.type == EventType.ScrollWheel)
                {
                    Event.current.Use();
                }
            }
            
            GUI.Box(new Rect(0, 0, Screen.width, Screen.height), "");
            GUI.Window(999, windowRect, DrawWindowContent, "Atelier de Wilcer - Artisanat");
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
                if (GUILayout.Button("Désélectionner", GUILayout.Width(120 * scale))) 
                {
                    selectedItem = null;
                    newNameDraft = "";
                }
            }
            
            if (GUILayout.Button(ShowSettings ? "Retour" : "Options", GUILayout.Width(80 * scale)))
            {
                ShowSettings = !ShowSettings;
            }
            
            if (GUILayout.Button("Fermer", GUILayout.Width(80 * scale))) IsOpen = false;
            GUILayout.EndHorizontal();

            GUILayout.Space(10);
            
            if (ShowSettings) DrawSettingsGUI(scale);
            else if (selectedItem != null) DrawItemModificationGUI(scale);
            else DrawInventoryGUI(scale);

            // --- FOOTER ---
            GUILayout.Space(10);
            GUILayout.BeginHorizontal(GUI.skin.box);
            GUILayout.Label("Zoom :", GUILayout.Width(60 * scale));
            if (GUILayout.Button("-", GUILayout.Width(25 * scale))) m_ScalePercent = Math.Max(MIN_SCALE_PERCENT, m_ScalePercent - 10);
            GUILayout.Label($"{m_ScalePercent}%", GUILayout.Width(50 * scale));
            if (GUILayout.Button("+", GUILayout.Width(25 * scale))) 
            {
                int maxW = (int)(Screen.width / 600f * 100);
                int maxH = (int)(Screen.height / 500f * 100);
                m_ScalePercent = Math.Min(Math.Min(maxW, maxH), m_ScalePercent + 10);
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
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
                int cols = 2; // Passage à 2 colonnes pour plus de largeur par bouton
                for (int i = 0; i < items.Count; i += cols)
                {
                    GUILayout.BeginHorizontal();
                    for (int j = 0; j < cols && (i + j) < items.Count; j++)
                    {
                        var it = items[i + j];
                        var project = workshop?.ActiveProjects.FirstOrDefault(p => p.Item == it);
                        string label = it.Name;
                        if (project != null) label += " (En forge...)";

                        if (GUILayout.Button(label, entryStyle, GUILayout.Width(280 * scale), GUILayout.Height(50 * scale))) 
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
                if (GUILayout.Button("Fermer", GUILayout.Height(40 * scale))) IsOpen = false;
                GUILayout.EndVertical();
                return;
            }
            
            // --- SECTION RENOMMAGE (ENCHANTEMENT GRATUIT) ---
            GUILayout.Label("Action Spéciale : Renommer l'objet (Gratuit)");
            GUILayout.BeginHorizontal();
            newNameDraft = GUILayout.TextField(newNameDraft, GUILayout.ExpandWidth(true), GUILayout.Height(30 * scale));
            if (GUILayout.Button("Renommer", GUILayout.Width(120 * scale), GUILayout.Height(30 * scale)))
            {
                RenameItem(selectedItem, newNameDraft);
                feedbackMessage = "L'objet a été renommé !";
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
                        feedbackMessage = $"Enchantement {ench.Blueprint.name} retiré !";
                    }
                    GUILayout.EndHorizontal();
                }
            }

            GUILayout.Space(20);
            Div(scale);
            GUILayout.Space(10);
            
            // --- SECTION : ENCHANTEMENTS DISPONIBLES ---
            GUILayout.Label("Enchantements disponibles :", new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold });

            // --- RÉCEPTION DU DISCOVERY STATUS ---
            if (EnchantmentScanner.IsSyncing || !EnchantmentScanner.LastSyncMessage.Contains("réussie"))
            {
                GUILayout.Label($"<i>({EnchantmentScanner.LastSyncMessage})</i>", new GUIStyle(GUI.skin.label) { richText = true, fontSize = (int)(12 * scale), alignment = TextAnchor.MiddleCenter });
            }

            // --- LISTE DES ENCHANTEMENTS ---
            var available = EnchantmentScanner.GetFor(selectedItem);
            
            foreach (var data in available)
            {
                // Filtrage Recherche
                if (!string.IsNullOrEmpty(enchantmentSearch) && !data.Name.ToLower().Contains(enchantmentSearch.ToLower())) continue;
                
                // Filtrage Source (Granulaire)
                if (CurrentSourceFilter == SourceFilter.TTRPG && data.Source != "TTRPG") continue;
                if (CurrentSourceFilter == SourceFilter.Owlcat && data.Source != "TTRPG" && data.Source != "Owlcat") continue;
                if (CurrentSourceFilter == SourceFilter.Mods && data.Source != "Mod") continue;
                
                // Calcul du prix via le Calculator PF1e
                long costToPay = CraftingCalculator.GetUpgradeCost(selectedItem, data, CostMultiplier);
                int days = CraftingCalculator.GetCraftingDays(costToPay, InstantCrafting);
                
                // Priorité aux Overrides du JSON
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

            // 1. Vérification des fonds
            if (Game.Instance.Player.Money < cost)
            {
                feedbackMessage = "Vous n'avez pas assez d'or pour cet enchantement !";
                return;
            }

            bool isEnhancement = data.Categories.Contains("Enhancement");
            int currentPoints = item.Enchantments.Sum(e => e.Blueprint.EnchantmentCost);
            
            // Calcul du bonus d'altération actuel (en cherchant dans notre DB)
            int currentEnhancement = 0;
            var currentEnhancementEnchant = item.Enchantments.FirstOrDefault(e => 
                EnchantmentScanner.MasterList.Any(db => db.Guid == e.Blueprint.AssetGuid.ToString() && db.Categories.Contains("Enhancement"))
            );
            if (currentEnhancementEnchant != null) currentEnhancement = currentEnhancementEnchant.Blueprint.EnchantmentCost;

            // 2. Vérification de la limite d'altération (+5 / +X)
            if (isEnhancement && data.PointCost > MaxEnhancementBonus && EnforcePointsLimit)
            {
                feedbackMessage = $"Limite d'altération dépassée ! (Max configuré : +{MaxEnhancementBonus})";
                return;
            }

            // 3. Vérification du prérequis +1 (Sauf si c'est justement un bonus d'altération qu'on ajoute)
            if (!isEnhancement && currentEnhancement < 1 && RequirePlusOneFirst && EnforcePointsLimit)
            {
                feedbackMessage = "L'objet doit posséder au moins un bonus d'altération de +1 avant de recevoir des capacités spéciales.";
                return;
            }

            // 4. Vérification de la limite totale (+10 / +X)
            int newTotal = isEnhancement ? (currentPoints - currentEnhancement + data.PointCost) : (currentPoints + data.PointCost);
            if (newTotal > MaxTotalBonus && EnforcePointsLimit)
            {
                feedbackMessage = $"Limite totale de puissance dépassée ! (Max configuré : +{MaxTotalBonus})";
                return;
            }

            // --- APPLICATION ---
            Game.Instance.Player.Money -= cost;
            
            if (data.Blueprint != null)
            {
                if (days > 0 && !InstantCrafting)
                {
                    // CRÉATION D'UN PROJET (DIFFÉRÉ)
                    var workshop = Game.Instance.Player.MainCharacter.Value.Get<UnitPartWilcerWorkshop>();
                    if (workshop != null)
                    {
                        var project = new CraftingProject
                        {
                            Item = item,
                            EnchantmentGuid = data.Guid,
                            FinishTimeTicks = Game.Instance.Player.GameTime.Ticks + (days * TimeSpan.TicksPerDay),
                            GoldPaid = cost
                        };
                        workshop.ActiveProjects.Add(project);
                        feedbackMessage = $"Wilcer s'est mis au travail ! Il lui faudra environ {days} jours pour terminer. L'or a été encaissé.";
                    }
                }
                else
                {
                    // APPLICATION IMMÉDIATE (0 JOURS OU INSTANT)
                    if (isEnhancement && currentEnhancementEnchant != null)
                    {
                        item.RemoveEnchantment(currentEnhancementEnchant);
                    }

                    item.AddEnchantment(data.Blueprint, new Kingmaker.UnitLogic.Mechanics.MechanicsContext(null, null, null));
                    item.Identify();
                    feedbackMessage = $"Succès ! {data.Name} a été appliqué à {item.Name} ({cost} po déduits).";
                }
            }
            else
            {
                feedbackMessage = "Erreur : Blueprint introuvable (Sync Error).";
            }
        }

        void DrawSettingsGUI(float scale)
        {
            GUILayout.BeginVertical(GUI.skin.box);
            
            GUILayout.Label($"Coût de fabrication : {CostMultiplier:F1}x");
            CostMultiplier = GUILayout.HorizontalSlider(CostMultiplier, 0.1f, 5.0f);
            
            GUILayout.Space(20);
            
            InstantCrafting = GUILayout.Toggle(InstantCrafting, " Artisanat instantané (Terminé en 0 jours)"); // Slider remplacé par Toggle
            
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

            GUILayout.Space(20);
            
            GUILayout.Label("Affichage des sources d'enchantement :");
            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(CurrentSourceFilter == SourceFilter.TTRPG, "TTRPG")) CurrentSourceFilter = SourceFilter.TTRPG;
            if (GUILayout.Toggle(CurrentSourceFilter == SourceFilter.Owlcat, "Owlcat")) CurrentSourceFilter = SourceFilter.Owlcat;
            if (GUILayout.Toggle(CurrentSourceFilter == SourceFilter.Mods, "Mods")) CurrentSourceFilter = SourceFilter.Mods;
            if (GUILayout.Toggle(CurrentSourceFilter == SourceFilter.All, "Tout")) CurrentSourceFilter = SourceFilter.All;
            GUILayout.EndHorizontal();

            GUILayout.Space(20);
            Div(scale);
            GUILayout.Space(10);
            
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
                // On utilise notre nouveau composant de nom personnalisé (ItemPartCustomName)
                // Ce composant est attaché à l'entité et survit à la sauvegarde.
                // Le patch Harmony dans Main.cs s'occupe d'afficher ce nom à la place de l'original.
                var part = item.Ensure<ItemPartCustomName>();
                part.CustomName = name;
                
                // On force l'identification pour tenter un rafraîchissement immédiat de l'UI du jeu
                item.Identify();
                
                Main.ModEntry.Logger.Log($"[ATELIER] Objet renommé via Part : {name}");
            }
            catch (Exception ex)
            {
                Main.ModEntry.Logger.Error($"Erreur lors du renommage : {ex}");
            }
        }

    }
}