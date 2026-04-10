# Wilcer Workshop - Crafting System (WotR Mod)

This mod provides an immersive crafting and enchantment system for Pathfinder: Wrath of the Righteous, centered around the NPC Wilcer Garms.

## Current Features

### 🛠️ Hybrid Enchantment System
- **Comprehensive Game Scan**: The mod automatically detects all enchantments present in your installation (including those from other mods) directly from the game's blueprint index.
- **JSON Overrides**: You can configure specific properties (point cost, gold overrides, crafting days, categories, etc.) via the `Enchantments.json` file. Data in the JSON seamlessly overrides the automatically detected blueprint data.
- **Granular Source Filtering**: Filter available enchantments by source in the UI:
  - **TTRPG**: Only enchantments marked strictly as TTRPG in the JSON.
  - **Owlcat + TTRPG**: All standard and tabletop enchantments defined in the JSON.
  - **Mods**: Everything else (all other JSON entries not labeled TTRPG or Owlcat, plus any dynamically extracted enchantments from the game).
- **Full UI Localization**: Fully supports dynamic translations (English and French provided) for the entire GUI, combat logs, and dialogue via `Localization.json`.

### ⚖️ Balance and Ruleset (Pathfinder 1e)
- **Official Formulas**:
  - Weapons: `(Bonus^2) * 2000 gp` (Market price).
  - Armors/Shields: `(Bonus^2) * 1000 gp` (Market price).
  - **Crafting Cost**: Adding enchantments costs 50% of the market price by default. (A Cost Multiplier slider is available).
- **Enhancement Rules**: 
  - **Auto-Replacement**: Obsolete enhancement bonuses are safely removed when upgraded (e.g., adding +2 removes a pre-existing +1).
  - **Prerequisites**: Optionally require a minimum +1 base enhancement before adding special abilities.
- **Configurable Limits**: Set the maximum total bonus (default +10) and maximum enhancement bonus (default +5) securely in the mod's options menu.

### 💠 Item Management
- **Item Renaming**: Rename your items for free. Features a one-click "Auto" generator to name the item based on its exact magical properties.
- **Remove Enchantments**: Cleanly remove applied enchantments via the "Applied Enchantments" section. 
  - *Note*: Removal is currently **FREE** (development phase).
- **Delayed & Instant Crafting**: Crafting takes a dynamic amount of in-game time based on the cost. You can bypass this by toggling "Instant Crafting" in the settings to finish projects immediately.

## Installation & Usage
1. Install using Unity Mod Manager.
2. Talk to Wilcer Garms (in the Crusader Camp or Drezen) and ask him to handle your equipment.
3. Access the workshop UI to browse inventory or queue multiple enchantments at once.

---
*Developed for immersion and strict adherence to TTRPG rules.*

## Developer Setup (Compilation)
To compile this project on a new device or environment:
1. **UserConfig.props**: If missing, create or edit `UserConfig.props` in the root directory.
2. **Paths**: Set `<WrathPath>` to point to your local `Wrath_Data\Managed` folder.
3. **Framework**: This project targets **.NET Framework 4.8**. Ensure you have the appropriate SDK installed.
4. **Auto-Install**: The build process automatically copies the DLL and JSON files to your game's `Mods` folder using the `<ModInstallPath>` defined in your config.