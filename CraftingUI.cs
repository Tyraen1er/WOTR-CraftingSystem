using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using Kingmaker;
using Kingmaker.Items;
using Kingmaker.Blueprints.Items.Weapons;
using Kingmaker.Blueprints.Items.Armors;

namespace CraftingSystem
{
    public class CraftingUI : MonoBehaviour
    {
        public static CraftingUI Instance;
        public bool IsOpen = false;
        
        private Vector2 scrollPosition;
        private string feedbackMessage = "";
        
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
            float bW = 600f;
            float bH = 500f;
            float width = bW * scale;
            float height = bH * scale;

            Rect windowRect = new Rect(
                (Screen.width - width) / 2f, 
                (Screen.height - height) / 2f, 
                width, 
                height
            );

            if (Event.current != null && !windowRect.Contains(Event.current.mousePosition))
            {
                if (Event.current.type == EventType.MouseDown || 
                    Event.current.type == EventType.MouseUp || 
                    Event.current.type == EventType.ScrollWheel)
                {
                    Event.current.Use();
                }
            }
            
            GUI.Box(new Rect(0, 0, Screen.width, Screen.height), "");
            GUI.Window(999, windowRect, DrawWindowContent, "Atelier de Wilcer - Sélection d'Équipement");
            GUI.FocusWindow(999);
        }

        void DrawWindowContent(int windowID)
        {
            float scale = m_ScalePercent / 100f;

            if (!string.IsNullOrEmpty(feedbackMessage))
            {
                GUILayout.Label(feedbackMessage, new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = 18 });
                if (GUILayout.Button("OK", GUILayout.Height(40))) feedbackMessage = "";
                return;
            }

            // --- HEADER ---
            GUILayout.BeginHorizontal();
            GUILayout.Label("Faites votre choix parmi les objets stockés :");
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Fermer", GUILayout.Width(100))) IsOpen = false;
            GUILayout.EndHorizontal();

            GUILayout.Space(10);
            
            // --- GRILLE D'OBJETS ---
            scrollPosition = GUILayout.BeginScrollView(scrollPosition, GUILayout.ExpandHeight(true));
            var workshop = Game.Instance.Player.MainCharacter.Value.Get<UnitPartWilcerWorkshop>();
            var items = workshop?.StashedItems ?? new List<ItemEntity>();

            if (!items.Any()) GUILayout.Label("\n   (Aucun objet n'est stocké)");
            else 
            {
                // On prépare un style de bouton plus grand pour accueillir l'icône + texte
                GUIStyle entryStyle = new GUIStyle(GUI.skin.button);
                entryStyle.padding = new RectOffset((int)(55 * scale), 5, 5, 5); // Conserve de la place pour l'icône à gauche
                entryStyle.wordWrap = true;

                int cols = 3; // Réduit à 3 colonnes pour accueillir les noms plus longs proprement
                for (int i = 0; i < items.Count; i += cols)
                {
                    GUILayout.BeginHorizontal();
                    for (int j = 0; j < cols && (i + j) < items.Count; j++)
                    {
                        var it = items[i + j];
                        var sprite = it.Blueprint.Icon;
                        
                        // Dessine le bouton de base
                        if (GUILayout.Button(it.Name, entryStyle, GUILayout.Width(180 * scale), GUILayout.Height(60 * scale))) 
                        {
                            feedbackMessage = $"Prise en charge de : {it.Name}\n(Enchantement prochainement disponible)";
                        }

                        // Superpose l'icône extraite de l'Atlas proprement
                        if (sprite != null && sprite.texture != null)
                        {
                            Rect lastRect = GUILayoutUtility.GetLastRect();
                            // Carré d'icône à gauche
                            Rect iconRect = new Rect(lastRect.x + 5, lastRect.y + 5, 45 * scale, 45 * scale);
                            DrawSprite(iconRect, sprite);
                        }
                    }
                    GUILayout.EndHorizontal();
                }
            }
            GUILayout.EndScrollView();

            // --- FOOTER ---
            GUILayout.Space(10);
            GUILayout.BeginHorizontal(GUI.skin.box);
            GUILayout.Label("Dimension fenêtre :", GUILayout.Width(130));
            if (GUILayout.Button("-", GUILayout.Width(30))) m_ScalePercent = Math.Max(MIN_SCALE_PERCENT, m_ScalePercent - 10);
            GUILayout.Label($"{m_ScalePercent}%", GUILayout.Width(50));
            if (GUILayout.Button("+", GUILayout.Width(30))) 
            {
                int maxW = (int)(Screen.width / 600f * 100);
                int maxH = (int)(Screen.height / 500f * 100);
                m_ScalePercent = Math.Min(Math.Min(maxW, maxH), m_ScalePercent + 10);
            }
            GUILayout.FlexibleSpace();
            GUILayout.Label("Tooltip : " + GUI.tooltip);
            GUILayout.EndHorizontal();
        }

        /// <summary>
        /// Dessine une zone de l'atlas spécifiée par un Sprite Unity dans un Rect IMGUI. 
        /// Crucial dans WOTR car les textures brutes (.texture) pointent souvent vers l'atlas complet (tas de pixels).
        /// </summary>
        private void DrawSprite(Rect rect, Sprite sprite)
        {
            if (sprite == null || sprite.texture == null) return;
            
            // Calcul des UVs normalisés (0.0 à 1.0) par rapport à la texture globale (Atlas)
            Rect tRect = sprite.rect;
            float tw = sprite.texture.width;
            float th = sprite.texture.height;

            // Note: Unity utilise le bas-gauche (0,0) pour les coordonnées de texture 
            Rect uv = new Rect(tRect.x / tw, tRect.y / th, tRect.width / tw, tRect.height / th);
            
            // On dessine uniquement la portion de l'atlas correspondant au Sprite
            GUI.DrawTextureWithTexCoords(rect, sprite.texture, uv, true);
        }
    }
}