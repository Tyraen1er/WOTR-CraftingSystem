# WOTR-CraftingSystem
Add a fully featured crafting system to *Pathfinder: Wrath of the Righteous*.

## Current State
The mod successfully injects itself into the game's native dialogue tree (notably with Wilcer Garms and in other camps) in a fluid and stable manner, without causing UI freezes. The next step is to link this dialogue to the crafting User Interface (UI).

## UI Technology Choice
The mod will utilize the **game's native Quest Insert Item UI (ItemsCollectionDialog)** to select equipment for upgrading (rather than recreating a custom Unity interface from scratch or relying on UMM). This allows for completely natural integration into the game's aesthetic when placing orders with Wilcer or the Storyteller.

## Planned Features (Roadmap)
The mod will offer several customizable options to suit different playstyles:

* **Ruleset Selection**:
    * **Pathfinder 1st Edition Rules**: Strict adherence to the tabletop rules (costs, prerequisites, time).
    * **WOTR (Owlcat) Rules**: An extended mode including the numerous exotic enchantments specifically invented by Owlcat for the video game.
* **Quality of Life Options (Cheat/QoL)**:
    * **Instant Crafting**: Option to ignore crafting time and obtain the equipment immediately.
    * **Free Crafting**: Option to completely remove the cost in gold or raw materials.
* **Equipment Upgrading**: The ability to upgrade existing equipment in the inventory rather than having to forge an entirely new item from scratch (by simply paying the price difference between the old and new enchantment).

## NPC Crafters (Who does the crafting?)
The mod relies on specific NPCs to handle your crafting orders. **You do not need to make any skill checks and there is no risk of failure**. You simply place an order and pay the required costs.
* **Wilcer Garms**: Handles crafting during Act 2, Act 3, and Act 5. (Already implemented).
* **The Storyteller**: Will handle crafting during Act 4. (Pending implementation).

## Crafting Mechanics (Pathfinder 1e Rules)
For reference and internal design, here are the tabletop rules from *Pathfinder 1st Edition* that will serve as the foundation for calculating costs and time when placing orders with NPCs:

### 1. Crafting Costs
An item's base cost (Market Price) determines the raw material cost demanded by the NPC.
* **Weapons**: (Total Bonus)² × 2,000 gp.
* **Armor / Shields**: (Total Bonus)² × 1,000 gp.
* **Material Cost**: The character pays exactly **half (50%)** of the base item's market price to the craftsman to cover raw materials. An item that costs 4,000 gp to buy will cost 2,000 gp to craft. (The cost of the masterwork base equipment must also be added, which the NPC can supply or the player can provide).
* **Upgrading**: To ask the craftsman to upgrade a weapon from +1 to +2, you only pay the difference in base materials: ((2² × 2000) - (1² × 2000)) / 2 = 3,000 gp in crafting costs.

### 2. Crafting Time
Once the gold is paid, the craftsman gets to work. You must wait for the equipment to be ready.
* **Base Rule**: The craftsman takes **1 full day of work for every 1,000 gp** of the item's base price. (e.g., An item worth 4,000 gp on the market will take 4 days to craft).
* **Accelerated Crafting**: (To be determined) It may be possible to pay the expert extra to double their working pace.
* Time passes naturally as you explore the world map or rest in camps. Returning to the NPC after the deadline has passed allows you to retrieve the finished item.
