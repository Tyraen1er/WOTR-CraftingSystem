# WOTR-CraftingSystem
Ajoute un système de création et d'amélioration d'équipement dans le jeu *Pathfinder: Wrath of the Righteous*.

> [!IMPORTANT]
> **Compatibilité des sauvegardes** : Une fois que ce mod est installé et utilisé (objets confiés à Wilcer, choix de dialogue faits), vos fichiers de sauvegarde deviendront **dépendants** de ce mod. Le supprimer après usage pourrait causer des erreurs irréversibles au chargement.

## État Actuel
Le mod est désormais pleinement fonctionnel pour le dépôt et le retrait d'objets :
* **Stockage Persistant** : Les objets confiés à Wilcer Garms sont stockés dans une `UnitPart` persistante attachée au personnage principal, garantissant leur sécurité entre les sessions.
* **Hybride UI Native & IMGUI** : Utilise l'interface native de Loot pour un dépôt d'objets immersif, tout en exploitant une fenêtre IMGUI personnalisée pour la sélection et modification des objets stockés.
* **Filtrage des Objets** : Seul l'équipement valide (armes, armures, objets magiques) est accepté dans la boîte d'artisanat ; les consommables et le bric-à-brac sont automatiquement rendus au joueur.
* **Dialogue Dynamique** : Un arbre de dialogue à plusieurs niveaux permet des choix intuitifs entre "Donner un nouvel équipement" et "Modifier un équipement existant".
* **Blocage des Entrées Sécurisé** : Des patchs Harmony empêchent tout mouvement ou interaction dans le monde 3D tant que l'interface de modification est ouverte.

## Choix Technologique d'Interface (UI)
Le mod utilise un système d'interface hybride pour offrir la meilleure expérience utilisateur possible :
1. **Dépôt (UI Native Loot)** : Pour confier des objets à Wilcer, nous utilisons le conteneur d'inventaire natif du jeu. Cela préserve l'immersion de "donner" de l'équipement au quartier-maître.
2. **Révision & Modification (IMGUI Personnalisée)** : Pour sélectionner les objets déjà confiés, nous utilisons un HUD IMGUI sur mesure. Cette interface permet d'accueillir les futures options d'enchantement modulaires et dispose actuellement d'un système de redimensionnement dynamique (100% à plein écran) et d'un blocage robuste des clics.

## Fonctionnalités à Implémenter (Roadmap)
Le mod offrira plusieurs options paramétrables pour convenir à tous les styles de jeu :

* **Mode de Règles (Au choix)** :
    * **Règles Pathfinder 1ère Édition** : Respect strict des règles papier (coûts, prérequis, temps).
    * **Règles WOTR (Owlcat)** : Un mode étendu incluant les nombreux enchantements exotiques inventés spécifiquement par Owlcat pour le jeu vidéo.
* **Options de Qualité de Vie (Cheat/QoL)** :
    * **Artisanat instantané** : Option pour ignorer le temps d'artisanat et obtenir l'équipement immédiatement.
    * **Artisanat gratuit** : Option pour supprimer intégralement le coût en pièces d'or ou en matériaux.
* **Amélioration d'équipement** : Possibilité d'améliorer un équipement déjà existant dans l'inventaire plutôt que de devoir forger un objet entièrement from scratch (en payant simplement la différence de prix entre l'ancien et le nouvel enchantement).
* **Nettoyage de la Dette Technique** : Une fois que le mod aura atteint une version stable 1.0, le code [LEGACY_COMPATIBILITY] dans `Main.cs` sera supprimé pour garantir une base de code propre pour les nouvelles sauvegardes.

## PNJ Artisans (Qui fabrique ?)
Le mod passe par des PNJ spécifiques pour gérer vos commandes d'artisanat. **Vous n'avez aucun jet de compétence à faire et il n'y a aucun risque d'échec de création**, vous passez simplement commande en payant les coûts requis.
* **Wilcer Garms** : Gère l'artisanat pour l'Acte 2, l'Acte 3 et l'Acte 5. (Déjà implémenté).
* **Le Conteur (Storyteller)** : Gérera l'artisanat lors de l'Acte 4. (En file d'attente d'implémentation).

## Fonctionnement de l'Artisanat (Règles Pathfinder 1e)
Pour référence et conception interne, voici les règles papier de *Pathfinder 1st Edition* qui serviront de base au mode de calcul strict pour vos commandes auprès des PNJ :

### 1. Coûts de Fabrication
Le coût de base d'un objet (Prix du Marché) détermine le coût des matières premières exigées par le PNJ.
* **Armes** : (Bonus)² × 2 000 po.
* **Armures / Boucliers** : (Bonus)² × 1 000 po.
* **Coût Matériel** : Le personnage paie exactement **la moitié (50%)** du prix du marché de l'objet de base à l'artisan pour couvrir les matières premières. Un objet qui coûte 4 000 po à l'achat coûtera 2 000 po à fabriquer. (Il faut également rajouter le coût de l'équipement de base de maître, que l'artisan peut fournir ou que le joueur peut posséder).
* **Amélioration** : Pour demander à l'artisan d'améliorer une arme de +1 à +2, on paie uniquement la différence des matériaux de base : ((2² × 2000) - (1² × 2000)) / 2 = 3 000 po de coût.

### 2. Temps de Création
Une fois l'or payé, l'artisan se met au travail. Vous devez attendre que l'équipement soit prêt.
* **Règle de Base** : L'artisan prend **1 jour complet de travail pour chaque tranche de 1 000 po** du prix de base de l'objet. (Ex: Un objet valant 4 000 po au marché mettra 4 jours à être fabriqué).
* **Vitesse accélérée** : (À déterminer) Il sera potentiellement possible de payer l'expert plus cher pour doubler son rythme de travail.
* Le temps s'écoule naturellement en explorant la carte du monde ou en se reposant dans les camps. Repasser voir le PNJ une fois le délai écoulé permet de récupérer l'objet terminé.

## Configuration Développeur (Compilation)
Pour compiler le projet sur un nouvel environnement ou un autre ordinateur :
1. **UserConfig.props** : Vérifiez que ce fichier existe à la racine du projet.
2. **Chemins** : Réglez la variable `<WrathPath>` pour qu'elle pointe vers votre propre dossier de jeu `Wrath_Data\Managed`.
3. **Framework** : Le projet cible le **.NET Framework 4.8**. Assurez-vous d'avoir le SDK correspondant installé.
4. **Auto-Installation** : Le mod s'installe automatiquement dans votre dossier `Mods` après chaque compilation réussie via la cible `PostBuild` du projet.



TODO : 
Ne lancer la forge qu'après confirmation du joueur afin qu'il puisse prévoir plusieurs enchantement à la fois.