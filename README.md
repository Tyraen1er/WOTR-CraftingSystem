# Wilcer Workshop - Crafting System (WotR Mod)
This mod provides an immersive crafting and enchantment system for Pathfinder: Wrath of the Righteous, centered around the character Wilcer Garms. It bridges the gap between the video game and the Pathfinder 1st Edition tabletop rules.

## Key Features

### Hybrid Enchantment System
- **Full Game Scan:** The mod automatically detects all existing enchantments (including DLCs and other mods) from the blueprint index.
- **External Enchantment Support:** If a specific enchantment is not detected or comes from an external source, it can be manually added to the JSON file using its GUID. Unused GUIDs do not affect mod performance.
- **Intelligent Upgrade System:** The mod identifies enchantment families (e.g., Enhancement, Acid Resistance). When upgrading, it only charges the price difference between the old and the new rank.
- **JSON Overrides:** Precise configuration of properties (point costs, fixed prices, crafting duration, allowed slots, epic status) via the `Enchantments.json` file.

### Balancing and Rules (Pathfinder 1e)
The mod calculates costs dynamically based on the item type and existing properties:

**Official Formulas:**
- **Weapons:** (Bonus^2) * 2,000 gp.
- **Armor/Shields:** (Bonus^2) * 1,000 gp.
- **Wondrous Items:** Custom factors via JSON (defaults to Bonus^2 * 1,000 gp).

**Pathfinder 1e Penalties:**
- **Slot Penalty (+50%):** Applied if an enchantment is placed on an item type not intended for that effect (e.g., a ring effect on a belt).
- **Multiple Capacities (+50%):** In accordance with TTRPG rules, adding different capacities to a wondrous item increases the cost of the new capacity by 50%.
- **Epic Costs (x10):** Enchantments marked as "Epic" trigger a x10 multiplier, respecting high-level game balance.

### Item Management
- **Item Renaming:** Change the name of your equipment. Includes an automatic generator based on the actual magical properties of the item.
- **Enchantment Removal:** Clean your items via the "Applied Enchantments" section.
- **Time-based or Instant Crafting:** Crafting time depends on the gold cost. An option in the settings allows for instant crafting.

### Installation and Usage
- Install the mod via **Unity Mod Manager**.
- Speak to **Wilcer Garms** (Camp or Drezen) to access the workshop, or The NPC of the mage tower in Act4, or through the UMM menu.
- Use the interface to browse your inventory and queue enchantments.
- **Warning:** This mod cannot be safely uninstalled from a save file once it has been used.

## Contributions
**Mod development is open to the community:**
- **Developers:** Pull Requests are welcome for any code improvements or bug fixes.
- **Non-developers:** Updating data (balancing, new enchantments, translations) directly in the CSV or JSON files is greatly appreciated.

## Development Configuration
1. **UserConfig.props:** Define the path to your `Wrath_Data\Managed` folder.
2. **Framework:** Targets .NET Framework 4.8.
3. **Data Conversion:** The build process triggers a Python script that automatically converts the `Enchantments.csv` file into `Enchantments.json`.
4. **Auto-Install:** The build process automatically copies the DLL and JSON files into the game's Mods folder.

### JSON Configuration and Structure
Settings can be modified within the mod menu:
- **Cost Multiplier:** Adjusts the global cost (Default is 0.5 for creation cost).
- **Apply Slot Penalty:** Enables or disables the 50% surcharge.
- **Enable Epic Costs:** Enables or disables the x10 multiplier.