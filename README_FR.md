# WOTR-CraftingSystem
Ajoute un système de création et d'amélioration d'équipement dans le jeu *Pathfinder: Wrath of the Righteous*.

## État Actuel
Le mod parvient avec succès à s'injecter dans l'arbre de dialogue natif du jeu (notamment auprès de Wilcer Garms et dans d'autres camps), de façon fluide et stable, sans provoquer de blocages d'interface. La prochaine étape consiste à lier ce dialogue à l'interface de fabrication (UI).

## Choix Technologique d'Interface (UI)
Le mod utilisera **l'Interface native de Quête (ItemsCollectionDialog)** pour sélectionner les objets à améliorer (plutôt que de devoir recréer une interface Unity de zéro ou d'utiliser le menu UMM hors contexte). Cela permet une intégration complètement organique dans l'esthétique du jeu lorsque vous passez commande à Wilcer ou au Conteur.

## Fonctionnalités à Implémenter (Roadmap)
Le mod offrira plusieurs options paramétrables pour convenir à tous les styles de jeu :

* **Mode de Règles (Au choix)** :
    * **Règles Pathfinder 1ère Édition** : Respect strict des règles papier (coûts, prérequis, temps).
    * **Règles WOTR (Owlcat)** : Un mode étendu incluant les nombreux enchantements exotiques inventés spécifiquement par Owlcat pour le jeu vidéo.
* **Options de Qualité de Vie (Cheat/QoL)** :
    * **Artisanat instantané** : Option pour ignorer le temps d'artisanat et obtenir l'équipement immédiatement.
    * **Artisanat gratuit** : Option pour supprimer intégralement le coût en pièces d'or ou en matériaux.
* **Amélioration d'équipement** : Possibilité d'améliorer un équipement déjà existant dans l'inventaire plutôt que de devoir forger un objet entièrement from scratch (en payant simplement la différence de prix entre l'ancien et le nouvel enchantement).

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
