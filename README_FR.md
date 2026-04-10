# Wilcer Workshop - Crafting System (WotR Mod)

Ce mod ajoute un système d'artisanat immersif pour Pathfinder: Wrath of the Righteous, centré sur le personnage de Wilcer Garms.

## Fonctionnalités Actuelles

### 🛠️ Système d'Enchantement Hybride
- **Scan Complet du Jeu** : Le mod détecte automatiquement tous les enchantements présents dans votre installation (incluant ceux ajoutés par d'autres mods) directement depuis l'index du jeu.
- **Surcharge JSON** : Vous pouvez configurer des propriétés spécifiques (coût en points, prix en or manuel, temps de craft, catégories, etc.) via le fichier `Enchantments.json`. Ses données écrasent naturellement les données natives du jeu.
- **Filtrage Granulaire des Sources** : Triez les enchantements dans l'interface :
  - **TTRPG** : Uniquement les enchantements marqués strictement comme "TTRPG" dans le JSON.
  - **Owlcat + TTRPG** : L'ensemble des enchantements papier et des spécificités du jeu vidéo, listés dans le JSON.
  - **Mods** : Tout le reste (les autres entrées du JSON non étiquetées, et l'intégralité des autres enchantements extraits dynamiquement du jeu).
- **Interface Entièrement Localisée** : Support par traduction dynamique (Anglais et Français inclus) couvrant toute la GUI, les journaux de combat et les dialogues avec Wilcer via `Localization.json`.

### ⚖️ Équilibre et Règles (Pathfinder 1e)
- **Formules Officielles** :
  - Armes : `(Bonus^2) * 2000 po` (Prix du marché).
  - Armures / Boucliers : `(Bonus^2) * 1000 po` (Prix du marché).
  - **Coût de Fabrication** : Par défaut, fabriquer ou l'ajouter d'enchantements coûte la moitié (50%) du prix du marché. (Un multiplicateur de coût global est paramétrable dans les options).
- **Règles d'Altération** : 
  - **Remplacement Automatique** : Les anciens bonus d'altération sont supprimés en toute sécurité lors d'une amélioration (ex: appliquer un +2 supprime l'éventuel +1 pré-existant).
  - **Prérequis** : Vous pouvez (en cochant l'option) exiger une altération de base +1 au minimum avant d'autoriser l'ajout de capacités magiques spéciales.
- **Limites Configurables** : Paramétrez le bonus total maximum (défaut +10) et le bonus d'altération max (défaut +5) directement depuis l'interface des réglages.

### 💠 Gestion de l'Objet
- **Renommage d'objet** : Modifiez le nom de l'équipement gratuitement, ou fiez-vous au générateur "Auto" en un clic pour refléter ses affixes exacts.
- **Retrait d'Enchantements** : Retirez sans contrainte tout enchantement via le panneau "Enchantements Appliqués".
  - *Note* : Le retrait est actuellement **GRATUIT** (phase de développement).
- **Artisanat Différé ou Instantané** : La forge prend un temps in-game réaliste basé sur le coût de la commande. Vous pouvez contourner ce délai avec l'option "Craft Instantané" pour que Wilcer finisse immédiatement son travail.

## Installation & Utilisation
1. Installez le mod avec Unity Mod Manager.
2. Parlez à Wilcer Garms (dans le Camp des Croisés ou à Drezen) et demandez-lui de s'occuper de votre équipement.
3. Ouvrez l'interface de l'atelier pour gérer le stockage ou valider plusieurs enchantements simultanément dans la file de projets.

---
*Développé pour l'immersion et le respect strict des règles TTRPG.*

## Configuration Développeur (Compilation)
Pour compiler le projet sur un nouvel environnement ou un autre ordinateur :
1. **UserConfig.props** : Vérifiez que ce fichier existe à la racine du projet.
2. **Chemins** : Réglez la variable `<WrathPath>` pour qu'elle pointe vers votre propre dossier de jeu `Wrath_Data\Managed`.
3. **Framework** : Le projet cible le **.NET Framework 4.8**. Assurez-vous d'avoir le SDK correspondant installé.
4. **Auto-Installation** : Le mod s'installe automatiquement dans votre dossier `Mods` après chaque compilation réussie via la cible `PostBuild` du projet.