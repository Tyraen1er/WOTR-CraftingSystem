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
        public static bool InstantCrafting = false; // Slider remplacé par Toggle
        public static bool EnforcePointsLimit = true;
        public static bool AllowOwlcatEnchants = false;

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
                        // Augmentation de la largeur à 280 et de la hauteur à 50
                        if (GUILayout.Button(it.Name, entryStyle, GUILayout.Width(280 * scale), GUILayout.Height(50 * scale))) 
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
            GUILayout.BeginVertical(GUI.skin.box);
            
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
            
            // --- FUTUR : LISTE DES ENCHANTEMENTS (TOYBOX INSPIRATION) ---
            GUILayout.Label("Enchantements disponibles :");
            
            // --- RECHERCHE ET FILTRES ---
            GUILayout.BeginHorizontal();
            GUILayout.Label("Recherche :", GUILayout.Width(80 * scale));
            enchantmentSearch = GUILayout.TextField(enchantmentSearch, GUILayout.Width(150 * scale));
            GUILayout.EndHorizontal();

            // --- LISTE DES ENCHANTEMENTS ---
            scrollPosition = GUILayout.BeginScrollView(scrollPosition, GUILayout.Height(200 * scale));
            var available = EnchantmentScanner.GetFor(selectedItem);
            
            foreach (var data in available)
            {
                if (!string.IsNullOrEmpty(enchantmentSearch) && !data.Name.ToLower().Contains(enchantmentSearch.ToLower())) continue;
                
                // Calcul du prix prévisionnel (Pathfinder : Half Market Price)
                int currentPoints = selectedItem.Enchantments.Sum(e => e.Blueprint.EnchantmentCost);
                int totalPoints = currentPoints + data.PointCost;
                
                long currentCost = (long)currentPoints * currentPoints * 1000;
                long totalCost = (long)totalPoints * totalPoints * 1000;
                long costToPay = totalCost - currentCost; 
                if (data.GoldOverride >= 0) costToPay = data.GoldOverride;

                int days = (int)Math.Max(1, costToPay / 1000);
                if (data.DaysOverride >= 0) days = data.DaysOverride;

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
            // Vérification des fonds
            if (Game.Instance.Player.Money < cost)
            {
                feedbackMessage = "Vous n'avez pas assez d'or pour cet enchantement !";
                return;
            }

            // Vérification de la limite de 10 points
            int currentPoints = item.Enchantments.Sum(e => e.Blueprint.EnchantmentCost);
            if (currentPoints + data.PointCost > 10 && EnforcePointsLimit)
            {
                feedbackMessage = "L'objet ne peut pas supporter un bonus supérieur à +10 !";
                return;
            }

            // ICI : Demander Confirmation ou Appliquer l'action
            Game.Instance.Player.Money -= cost;
            
            // Application de l'enchantement (sera raffiné prochainement avec une file d'attente)
            if (data.Blueprint != null)
            {
                item.AddEnchantment(data.Blueprint, new Kingmaker.UnitLogic.Mechanics.MechanicsContext(null, null, null));
                item.Identify();
                feedbackMessage = $"Succès ! {data.Name} a été ajouté à {item.Name} ({cost} po déduits).";
            }
            else
            {
                feedbackMessage = "Erreur : Blueprint introuvable dans les données du jeu.";
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
            
            EnforcePointsLimit = GUILayout.Toggle(EnforcePointsLimit, " Appliquer la limite de 10 points (Pathfinder)");
            
            GUILayout.Space(10);
            
            AllowOwlcatEnchants = GUILayout.Toggle(AllowOwlcatEnchants, " Autoriser les enchantements spéciaux Owlcat / ToyBox");
            
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