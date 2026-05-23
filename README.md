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

## Developer Guide

### Environment Setup
1. **Target Framework:** .NET Framework 4.8.
2. **Game References:** Copy or create `UserConfig.props` at the root and set `<WrathPath>` to your game's `Wrath_Data\Managed` directory.
3. **Build Pipeline:** Compiling the project will automatically:
   - Run `convert_csv_to_json.py` to compile `Enchantments.csv` into `ModConfig/Enchantments.json`.
   - Copy the binaries and configuration files to the game's `Mods/CraftingSystem` directory.

### Code Architecture
- [Main.cs](src/Main.cs): Mod entry point, configuration UI, key bindings, and Harmony patches.
- [UnifiedScanner.cs](src/UnifiedScanner.cs): Performs a multithreaded binary scan over game pack blueprints to index items, spells, and vanilla enchantments.
- [CraftingCalculator.cs](src/CraftingCalculator.cs): Evaluates point costs, gold costs, slot penalties (+50%), multiple capacity penalties (+50%), and upgrade delta-pricing.
- [CustomEnchantmentsBuilder.cs](src/CustomEnchantmentsBuilder.cs): Dynamically instantiates and registers new blueprints in the game cache based on templates defined in `ModConfig/CustomEnchants.json`.
- [DynamicGuidHelper.cs](src/DynamicGuidHelper.cs): Generates and decodes custom parameters and enabled-components bitmasks directly to/from a deterministic GUID.
- [EnchantmentDescriptionGenerator.cs](src/EnchantmentDescriptionGenerator.cs): Dynamically formats localized tooltips for custom/injected enchantments using templates.

### Dynamic Blueprint Engine & GUID Format
To bypass shipping thousands of static assets, custom enchantments are compiled on-the-fly. WotR's `BlueprintConverter.ReadJson` is patched to intercept GUIDs starting with the signature `c2af`, decode their properties, and construct the blueprint at runtime.

The GUID encoding structure (32 hex characters):
`[C2AF (4 chars)] [EnchantId (3 chars)] [ParamCount (1 char)] [Params (2 chars each)] [Zero padding] [ComponentBitmask (3 chars)]`

- **EnchantId**: Identifies the base model in `CustomEnchants.json` (e.g., `109` for elemental damage).
- **ComponentBitmask**: 12-bit bitmask determining which components defined in the model are active.

### Data Configurations (ModConfig/)
- `CustomEnchants.json` (and split files `CustomEnchants_*.json`): Configures dynamic enchantment models, their components, editable properties, and cost formulas.
- `Enchantments.json`: Core index of vanilla and homebrew static enchantments (compiled from `Enchantments.csv`).
- `EnchantmentTemplates.json`: Mapping of blueprint component types to string templates with placeholders (e.g., `<Value>`, `<FlagCondition:...>`) used to generate descriptions.
- `EnchantmentDescriptionGlossary.json`: Translation mappings for technical terms, stats, and enums used in generated descriptions.
- `Localization.json`: Contextual translation keys for the UMM interface and log outputs.

### Testing & QA (tests/)
- Run [RunPreflightChecks.ps1](tests/RunPreflightChecks.ps1) to compile the mod and validate the syntax of all configuration JSON files.
- Run [ExtractCraftingLogSignals.ps1](tests/ExtractCraftingLogSignals.ps1) with the game's log path to quickly check for mod errors or missing blueprints.
- Follow the checklist in [RegressionCampaign_HEAD_362a28d_to_HEAD.md](tests/RegressionCampaign_HEAD_362a28d_to_HEAD.md) for in-game validation steps.
- Note: The game log (`Player.log`) is located in `%localappdatalow%\Owlcat Games\Pathfinder Wrath Of The Righteous\`.

## Thanks
- **Cabarius** For the scanner logic and the renaming system utilized from ToyBox.
- **Paladingineer** For the Woljif Romance Mod, which provided the framework for the dialogue management system.
- **Tyrtyt21** For the help with the report of bugs, the russian translation and the suggestions of features.