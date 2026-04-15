# Wilcer Workshop - Crafting System (WotR Mod)
Ce mod propose un système de craft et d'enchantement immersif pour Pathfinder: Wrath of the Righteous, centré sur le personnage de Wilcer Garms. Il fait le pont entre le jeu vidéo et les règles papier de Pathfinder 1ère Edition.

## Fonctionnalites principales

### Systeme d'enchantement hybride
- **Scan complet du jeu :** Le mod détecte automatiquement tous les enchantements présents (incluant les DLC et les autres mods) depuis l'index des blueprints.
- **Ajout d'enchantements externes :** Si un enchantement spécifique n'est pas détecté ou provient d'une source externe, il suffit de l'ajouter manuellement dans le fichier JSON avec son GUID. Les GUID inutiles ne perturbent pas le bon fonctionnement du mod.
- **Système d'upgrade intelligent :** Le mod identifie les familles d'enchantements (ex: Altération, Résistance à l'Acide). Lors d'une amélioration, il ne facture que la différence de prix entre l'ancien et le nouveau rang.
- **Surcharges JSON :** Configuration précise des propriétés (coût en points, prix fixes, jours de craft, slots autorisés, statut épique) via le fichier Enchantments.json. 

### Equilibrage et regles (Pathfinder 1e)
Le mod calcule les coûts dynamiquement selon le type d'objet et les propriétés existantes :

**Formules officielles :**
- **Armes :** (Bonus^2) * 2000 po.
- **Armures/Boucliers :** (Bonus^2) * 1000 po.
- **Objets merveilleux :** Facteurs personnalisés via JSON (par défaut Bonus^2 * 1000 po).

**Penalites Pathfinder 1e :**
- **Malus d'emplacement (+50%) :** Appliqué si un enchantement est placé sur un type d'objet non prévu à cet effet (ex: un effet d'anneau sur une ceinture).
- **Capacites multiples (+50%) :** Conformément aux règles TTRPG, l'ajout de capacités différentes sur un objet merveilleux augmente le coût de la nouvelle capacité de 50%.
- **Coûts Epiques (x10) :** Les enchantements marqués comme Epiques déclenchent un multiplicateur x10, respectant l'équilibrage de haut niveau.

### Gestion des objets
- **Renommage d'objet :** Changez le nom de votre équipement. Inclut un générateur automatique basé sur les propriétés magiques réelles de l'objet.
- **Suppression d'enchantements :** Nettoyez vos objets via la section Enchantements Appliqués.
- **Crafting temporel ou instantané :** Le temps de fabrication dépend du coût en or. Une option permet de rendre le craft instantané dans les réglages.

### Installation et utilisation
- Installez le mod via **Unity Mod Manager**.
- Parlez à **Wilcer Garms** (Campement ou Drezen) pour accéder à l'atelier.
- Utilisez l'interface pour parcourir votre inventaire et mettre des enchantements en file d'attente.

## Contributions
**Le développement du mod est ouvert à la communauté :**
- **Développeurs :** Les Pull Requests sont acceptées pour toute amélioration du code ou correction de bug.
- **Non-développeurs :** La mise à jour des données (équilibrage, nouveaux enchantements, traductions) directement dans les fichiers CSV ou JSON est grandement appréciée.

## Configuration de developpement
1. **UserConfig.props :** Definissez  vers votre dossier Wrath_Data\Managed.
2. **Framework :** Cible .NET Framework 4.8.
3. **Conversion des donnees :** Le processus de build declenche l'execution automatique d'un script Python charge de convertir le fichier Enchantments.csv en Enchantments.json.
4. **Auto-Installation :** Le processus de build copie automatiquement les fichiers DLL et JSON dans le dossier Mods du jeu.

### Configuration et structure JSON
Les réglages sont modifiables dans le menu du mod :
- **Multiplicateur de coût :** Ajuste le coût global (Par défaut 0.5 pour le coût de création).
- **Appliquer le malus de slot :** Active ou désactive la surcharge de 50%.
- **Activer les coûts épiques :** Active ou désactive le multiplicateur x10.