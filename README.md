# Wilcer Workshop - Crafting System (WotR Mod)
This mod provides an immersive crafting, item customization, and enchantment system for Pathfinder: Wrath of the Righteous, centered around the character Wilcer Garms. It bridges the gap between the video game and the Pathfinder 1st Edition tabletop rules, offering a highly customizable experience.

## Key Features

### Hybrid Enchantment System
- **Full Game Scan:** The mod automatically detects all existing enchantments (including DLCs and other mods) from the blueprint index.
- **External Enchantment Support:** If a specific enchantment is not detected or comes from an external source, it can be manually added to the JSON configuration files using its GUID. Unused GUIDs do not affect mod performance.
- **Intelligent Upgrade System:** The mod identifies enchantment families (e.g., Enhancement, Acid Resistance). When upgrading, it only charges the price difference between the old and the new rank.
- **JSON Overrides:** Precise configuration of properties (point costs, fixed prices, crafting duration, allowed slots, epic status) via JSON configuration files.
- **Dynamic Parameter Formulas:** Parameter boundaries (`Min`, `Max`, `DefaultValue`) in dynamic enchantments support mathematical formulas (e.g., `= SpellMinCasterLevel` or `= 2 * SpellLevel - 1`) that are resolved in topological dependency order.

### Custom Spellcasting Enchantments
- **Spells on Equipment:** Infuse custom spellcasting capabilities into accessory items (rings, belts, neck amulets, boots, gloves, goggles).
- **Adjustable Parameters:** Choose any scanned spell and configure the daily uses/charges (1 to 5), caster level (CL, from minimum spell caster level up to 20), spell level (SL, 0 to 9), and difficulty class (DC, 10 to 40).
- **Tabletop Pricing:** Gold cost is calculated using the official Pathfinder 1e formula: `SpellLevel * CasterLevel * 1800 * Charges / 5` GP.

### Consumables Workshop
- **Scrolls, Wands & Potions:** Dedicated crafting workshops for magical consumables.
- **Official Formulas:**
  - **Scrolls:** `25 * Spell Level * Caster Level` GP (Cantrips/Lvl 0 spells count as Spell Level 0.5; minimum 13 GP).
  - **Potions:** `50 * Spell Level * Caster Level` GP (Cantrips/Lvl 0 spells count as Spell Level 0.5; minimum 25 GP).
  - **Wands (50 charges):** `375 * Spell Level * Caster Level` GP (Cantrips/Lvl 0 spells count as Spell Level 0.5).
- **Caster Level Restrictions:** Caster level matches wizard/cleric minimum level rules (e.g., minimum CL is `Spell Level * 2 - 1`).

### Item Management & Customization
- **Blank Accessories Shop:** Wilcer offers blank non-magical accessories (rings, belts, neck amulets, boots, gloves, goggles) cloned at runtime for 50 GP to serve as blank targets for custom crafting.
- **Appearance & Text Customization:** Customize the name, description, and icon of any item. Includes an automatic name generator based on active enchantments, as well as an in-game icon browser and description editor (free of cost).
- **Enchantment Removal:** Instantly strip custom enchantments from your items via the "Applied Enchantments" panel for free. Note: Native enchantments that are part of the base item's blueprint cannot be removed.
- **Time-based Crafting:** Crafting projects require time depending on the gold cost (`1 day per 1,000 gp`, minimum 1 day). Projects progress when opening or interacting with the workshop. An option in settings allows for instant crafting (disabled by default).

## Balancing and Rules (Pathfinder 1e)
Costs are calculated dynamically based on item type and existing properties:

**Official Formulas:**
- **Weapons:** `(Bonus^2) * 2,000` GP.
- **Armor/Shields:** `(Bonus^2) * 1,000` GP.
- **Wondrous Items:** Custom factors via JSON (defaults to `(Bonus^2) * 1,000` GP).

**Pathfinder 1e Penalties:**
- **Slot Penalty (+50%):** Applied if an enchantment is placed on an item type not intended for that effect (e.g., a ring effect on a belt). Prioritizes explicit `Slots` defined in JSON.
- **Multiple Capacities (+50%):** Adding different capacities to a wondrous item increases the cost of the new capacity by 50% (calculated sequentially in the crafting queue).
- **Epic Costs (x10):** Enchantments marked as "Epic" or exceeding normal limits (e.g., total enhancement bonus > 5 or total point bonus > 10) trigger a x10 multiplier, respecting high-level balance.

## Installation and Usage
- Install the mod via **Unity Mod Manager (UMM)**.
- Speak to **Wilcer Garms** (in Camp or Drezen), the NPC of the Mage Tower in Act 4, or access the workshop through the UMM menu interface.
- Use the interface to browse your inventory, buy blanks, customize items, and queue crafting projects.
- **Warning:** This mod cannot be safely uninstalled from a save file once it has been used.

## Contributions
**Mod development is open to the community:**
- **Developers:** Pull Requests are welcome for any code improvements or bug fixes.
- **Non-developers:** Updating data (balancing, new enchantments, translations) directly in the CSV or JSON files is greatly appreciated.

---
## Developer Guide

### Environment Setup
1. **Target Framework:** .NET Framework 4.8.
2. **Game References:** Copy or create `UserConfig.props` at the root and set `<WrathPath>` to your game's `Wrath_Data\Managed` directory.
3. **Build Pipeline:** Compiling the project will automatically:
   - Run `convert_csv_to_json.py` to compile `Enchantments.csv` into `ModConfig/Enchantments.json`.
   - Copy the binaries and configuration files to the game's `Mods/CraftingSystem` directory.

### Code Architecture (src/)
- [Main.cs](src/Main.cs): Mod entry point, Unity Mod Manager (UMM) settings panel, key bindings, and patch initialization.
- [CraftingUI.cs](src/CraftingUI.cs): Main crafting workspace IMGUI window, featuring paginated lists, advanced search filters, layout configurations, and custom scaling.
- [CraftingCore.cs](src/CraftingCore.cs): Implements the core crafting mechanics (`CraftingProject`, `CraftingActions`) and the `UnitPartWilcerWorkshop` component that manages active projects and stashed items.
- [CraftingCalculator.cs](src/CraftingCalculator.cs): Evaluates point costs, gold costs, slot penalties (+50%), multiple capacity wondrous item penalties (+50%), and upgrade delta-pricing.
- [CraftingSettings.cs](src/CraftingSettings.cs): Declares settings variables (multipliers, limits, toggle flags), handles XML/JSON serialization via UMM, and configures scaling/GUI resolution.
- [CustomEnchantmentsBuilder.cs](src/CustomEnchantmentsBuilder.cs): Dynamically instantiates and registers new blueprints in the game cache based on templates defined in `ModConfig/`.
- [CustomItemBuilder.cs](src/CustomItemBuilder.cs): Dynamically instantiates base accessory items (rings, belts, amulets) used for custom crafting.
- [DescriptionManager.cs](src/DescriptionManager.cs): Resolves localized display names and descriptions, generating dynamic text for spellcasting or component-based effects.
- [DialogActions.cs](src/DialogActions.cs): Dialogue inject patches and triggers for NPC interactions.
- [DynamicGuidHelper.cs](src/DynamicGuidHelper.cs): Generates and decodes custom parameters and enabled-components bitmasks directly to/from a deterministic GUID.
- [EnchantmentDebug.cs](src/EnchantmentDebug.cs): Debug tools and utilities for inspection of enchantments and structures.
- [EnchantmentDescriptionGenerator.cs](src/EnchantmentDescriptionGenerator.cs): Dynamically formats localized tooltips for custom/injected enchantments using templates.
- [FormulaEvaluator.cs](src/FormulaEvaluator.cs): Mathematical expression parser for dynamic point and gold cost calculations.
- [Helpers.cs](src/Helpers.cs): Shared helper utilities, blueprint caches, UI widgets, and string/color helpers.
- [InventoryHandler.cs](src/InventoryHandler.cs): Synchronizes the mod's virtual chest inventory with the game's Loot UI.
- [NamingItem.cs](src/NamingItem.cs): Handles custom item name, description, and icon modifications.
- [SpellcastingPatches.cs](src/SpellcastingPatches.cs): Harmony patches for custom spellcasting, equipping, spell charges, caster levels, and DC hooks.
- [UnifiedScanner.cs](src/UnifiedScanner.cs): Performs a multithreaded binary scan over game pack blueprints to index items, spells, and vanilla enchantments.
- **Scanners:**
  - [EnchantmentScanner.cs](src/EnchantmentScanner.cs): Parses game blueprints to extract vanilla enchantments.
  - [ItemScanner.cs](src/ItemScanner.cs): Parses game blueprints to extract weapons, armor, shields, and accessories.
  - [SpellScanner.cs](src/SpellScanner.cs): Parses game blueprints to extract spell lists and spellbook metadata.
- **Utility & Debug Dumpers:**
  - [BlueprintDumper.cs](src/BlueprintDumper.cs): Utility for dumping general blueprints for debugging.
  - [EnchantmentDumper.cs](src/EnchantmentDumper.cs): Dumps game enchantments for validation.
  - [StorytellerDumper.cs](src/StorytellerDumper.cs): Utility to dump and analyze Storyteller-related blueprints.
  - [WeaponDump.cs](src/WeaponDump.cs): Utility to dump weapon templates and details.
- [TooltipPatches.cs](src/TooltipPatches.cs): Tooltip intercept patches to display dynamic parameters and spell details on custom items.

### Dynamic Blueprint Engine & GUID Format
To bypass shipping thousands of static assets, custom enchantments are compiled on-the-fly. WotR's `BlueprintConverter.ReadJson` is patched to intercept GUIDs starting with the signature `c2af`, decode their properties, and construct the blueprint at runtime.

The GUID encoding structure (32 hex characters):
`[C2AF (4 chars)] [EnchantId (3 chars)] [ParamCount (1 char)] [Params (2 chars each)] [Zero padding] [ComponentBitmask (3 chars)]`

- **EnchantId**: Identifies the base model in `CustomEnchants.json` or split files (e.g., `109` for elemental damage, `013` for spellcasting).
- **ParamCount**: Hexadecimal count of parameters. Note that the **first** parameter is always the `isFeature` boolean flag (`01` for true, `00` for false). Custom parameters start at index 1.
- **ComponentBitmask**: 12-bit bitmask determining which components defined in the model are active.

### Data Configurations (ModConfig/)
- `CustomEnchants.json` (and split files `CustomEnchants_*.json`): Configures dynamic enchantment models, their components, editable properties, and cost formulas.
- `Enchantments.json`: Core index of vanilla and homebrew static enchantments (compiled from `Enchantments.csv`).
- `EnchantmentTemplates.json`: Mapping of blueprint component types to string templates with placeholders used to generate descriptions.
- `EnchantmentDescriptionGlossary.json`: Translation mappings for technical terms, stats, and enums used in generated descriptions.
- `Localization.json`: Contextual translation keys for the UMM interface and log outputs (supports English, French, Russian).
- `buyable_accessories.json`: Configures base non-magical accessories cloned at runtime to serve as blank targets for custom crafting.
- `SpellCache.json`: A pre-scanned cache of spell lists used to optimize game boot times.

### Testing & QA (tests/)
- Run [RunPreflightChecks.ps1](tests/RunPreflightChecks.ps1) to compile the mod, validate JSON syntax, and run `validate_enchantment_formulas.py` to automatically detect invalid variable references or circular formula dependency loops before compiling.
- Run [ExtractCraftingLogSignals.ps1](tests/ExtractCraftingLogSignals.ps1) with the game's log path to quickly check for mod errors or missing blueprints.
- Follow the checklist in [RegressionCampaign_HEAD_362a28d_to_HEAD.md](tests/RegressionCampaign_HEAD_362a28d_to_HEAD.md) for in-game validation steps.
- Note: The game log (`Player.log`) is located in `%localappdatalow%\Owlcat Games\Pathfinder Wrath Of The Righteous\`.

## Thanks
- **Cabarius** For the scanner logic and the renaming system utilized from ToyBox.
- **Paladingineer** For the Woljif Romance Mod, which provided the framework for the dialogue management system.
- **Tyrtyt21** For the help with the report of bugs, the russian translation and the suggestions of features.