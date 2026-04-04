using System;
using System.Linq;
using UnityEngine;
using Kingmaker;
using Kingmaker.Items;
using Kingmaker.Blueprints.Items.Weapons;
using Kingmaker.Blueprints.Items.Armors;

namespace CraftingSystem
{
    public enum CraftFilter { Weapon, Armor }

    public class CraftingUI : MonoBehaviour
    {
        public static CraftingUI Instance;
        public bool IsOpen = false;
        public CraftFilter CurrentFilter = CraftFilter.Weapon;
        
        private Vector2 scrollPosition;
        private string feedbackMessage = "";

        void Awake() { Instance = this; }

        void OnGUI()
        {
            if (!IsOpen) return;

            GUI.Box(new Rect(0, 0, Screen.width, Screen.height), "");
            
            // On élargit un peu la fenêtre pour faire rentrer une belle grille
            Rect windowRect = new Rect(Screen.width / 2f - 250f, Screen.height / 2f - 300f, 500f, 600f);
            
            string titleKey = CurrentFilter == CraftFilter.Weapon ? "ui_title_weapon" : "ui_title_armor";
            // Helpers.GetText(...) commenté pour compilation minimale
            GUI.Window(0, windowRect, DrawWindowContent, titleKey);
        }

        void DrawWindowContent(int windowID)
        {
            if (!string.IsNullOrEmpty(feedbackMessage))
            {
                GUILayout.Label(feedbackMessage, new GUIStyle(GUI.skin.label) { wordWrap = true });
                if (GUILayout.Button("OK", GUILayout.Height(40))) { IsOpen = false; feedbackMessage = ""; }
                return;
            }

            // Helpers.GetText(...) commenté pour compilation minimale
            if (GUILayout.Button("ui_button_close", GUILayout.Height(30)))
            {
                // Helpers.GetText(...) commenté pour compilation minimale
                feedbackMessage = "ui_wilcer_cancel";
            }

            GUILayout.Space(10);
            
            // --- DÉBUT DE LA GRILLE D'INVENTAIRE ---
            scrollPosition = GUILayout.BeginScrollView(scrollPosition);

            var items = Game.Instance.Player.Inventory.Items.Where(item => {
                if (CurrentFilter == CraftFilter.Weapon) return item.Blueprint is BlueprintItemWeapon bp && !bp.IsNatural;
                if (CurrentFilter == CraftFilter.Armor) return item.Blueprint is BlueprintItemArmor;
                return false;
            }).ToList();

            if (!items.Any()) 
            {
                // Helpers.GetText(...) commenté pour compilation minimale
                GUILayout.Label("ui_no_item");
            } 
            else 
            {
                int columns = 6; // Nombre d'objets par ligne
                int currentItemCount = 0;

                GUILayout.BeginHorizontal(); // On commence la première ligne

                foreach (var item in items) 
                {
                    // On récupère l'icône native de l'objet
                    Texture2D icon = item.Blueprint.Icon != null ? item.Blueprint.Icon.texture : null;

                    // On prépare le contenu de la case (L'icône + Le nom caché en "Tooltip" pour le survol)
                    GUIContent buttonContent = icon != null 
                        ? new GUIContent(icon, item.Name) 
                        : new GUIContent(item.Name, item.Name); // Sécurité si l'objet n'a pas d'icône

                    // On dessine un bouton carré de 64x64 pixels
                    if (GUILayout.Button(buttonContent, GUILayout.Width(64), GUILayout.Height(64))) 
                    {
                        // Helpers.GetText(...) commenté pour compilation minimale
                        string rawConfirm = "ui_wilcer_confirm";
                        feedbackMessage = string.Format(rawConfirm, item.Name);
                    }

                    currentItemCount++;
                    
                    // Si on atteint la limite de colonnes, on passe à la ligne suivante
                    if (currentItemCount % columns == 0) 
                    {
                        GUILayout.EndHorizontal();
                        GUILayout.BeginHorizontal();
                    }
                }
                
                GUILayout.EndHorizontal(); // Fin de la dernière ligne
            }

            GUILayout.EndScrollView();
            // --- FIN DE LA GRILLE ---

            GUILayout.Space(10);

            // --- AFFICHAGE DU NOM AU SURVOL (TOOLTIP) ---
            // Lit le "Tooltip" défini dans le bouton que la souris survole actuellement
            string hoverText = GUI.tooltip;
            if (!string.IsNullOrEmpty(hoverText))
            {
                // Suppression des références à FontStyle / TextAnchor pour éviter la dépendance à TextRenderingModule
                GUILayout.Label(hoverText);
            }
            else
            {
                // Espace vide pour éviter que la fenêtre ne saute quand on enlève la souris
                GUILayout.Label(" "); 
            }
        }
    }
}