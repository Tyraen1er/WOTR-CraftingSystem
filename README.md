# Wilcer Workshop - Crafting System (WotR Mod)

Ce mod ajoute un système d'artisanat immersif pour Pathfinder: Wrath of the Righteous, centré sur le personnage de Wilcer Garms.

## Fonctionnalités Actuelles

### 🛠️ Système d'Enchantement Hybride
- **Scan du Jeu** : Le mod détecte automatiquement tous les enchantements présents dans votre installation (incluant ceux d'autres mods).
- **Surcharge JSON** : Vous pouvez configurer des coûts spécifiques et des catégories dans `Enchantments.json`. Les données du JSON écrasent les données détectées automatiquement.
- **Filtrage Granulaire** : Choisissez d'afficher le contenu TTRPG, Owlcat ou celui des autres mods séparément.

### ⚖️ Équilibre et Règles (Pathfinder 1e)
- **Formules Officielles** :
  - Armes : `(Bonus^2) * 2000 po` (Prix du marché).
  - Armures : `(Bonus^2) * 1000 po` (Prix du marché).
  - **Coût de Fabrication** : Le mod facture 50% du prix du marché (Half-Market Price).
- **Règles d'Altération** : 
  - Remplacement automatique des anciens bonus (+1 remplacé par +2).
  - Prérequis de +1 altération minimum avant d'ajouter des capacités spéciales.
- **Limites Configurables** : Ajustez le bonus total maximum (défaut +10) et le bonus d'altération max (défaut +5) dans les options.

### 💠 Gestion de l'Objet
- **Renommer l'objet** : Gratuit, persistant après sauvegarde et reload.
- **Retirer des enchantements** : Accessible via la section "Enchantements Appliqués". 
  - **Note** : Le retrait est actuellement **GRATUIT** (phase de développement).

## Installation
1. Installez via Unity Mod Manager.
2. Parlez à Wilcer Garms (Camp ou Drezen) pour accéder à l'atelier.

---
*Développé pour l'immersion et le respect des règles TTRPG.*

## Developer Setup (Compilation)
To compile this project on a new device or environment:
1. **UserConfig.props**: If missing, create or edit `UserConfig.props` in the root directory.
2. **Paths**: Set `<WrathPath>` to point to your local `Wrath_Data\Managed` folder.
3. **Framework**: This project targets **.NET Framework 4.8**. Ensure you have the appropriate SDK installed.
4. **Auto-Install**: The build process automatically copies the DLL and JSON files to your game's `Mods` folder using the `<ModInstallPath>` defined in your config.
