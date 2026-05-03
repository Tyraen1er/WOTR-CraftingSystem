# Regression Campaign HEAD `362a28d` -> `HEAD`

## Scope
- Validate regression safety and new features introduced after `362a28d`.
- Cover startup integrity, dynamic enchant pipeline, cost/rules logic, inventory safety, UI flows, compatibility, and endurance.

## Evidence Collected Automatically
- `dotnet build CraftingSystem.csproj -c Release`: PASS
- JSON syntax checks:
  - `ModConfig/CustomEnchants.json`: PASS
  - `ModConfig/CustomEnchants.example.json`: PASS
  - `ModConfig/Localization.json`: PASS

## P0 Execution Checklist

### P0.1 Startup Integrity
- [x] Build release artifact successfully.
- [x] Ensure generated/copy pipeline runs without errors.
- [ ] Launch game with mod enabled.
- [ ] Confirm no fatal error at `BlueprintsCache.Init`.
- [ ] Confirm `CustomEnchantmentsBuilder.BuildAndInjectAll()` loads models successfully.
- [ ] Change locale (FR/EN, RU if available) and validate injected strings remain valid.

### P0.2 Core Crafting Regression
- [ ] Weapon: apply +1 then special enchant, verify cost/time/apply.
- [ ] Weapon family upgrade (same family): verify delta-only pricing.
- [ ] Armor: repeat equivalent cases and verify armor factor behavior.
- [ ] Confirm replacement by family and no exact duplicate after apply.

### P0.3 Save/Load + Dynamic GUID Persistence
- [ ] Create several dynamic enchants with different params.
- [ ] Save, reload, zone transition, reload again.
- [ ] Confirm `BlueprintConverter.ReadJson` dynamic path resolves all crafted enchants.
- [ ] Validate no enchant loss after reload.
- [ ] Optional: truncated GUID recovery scenario.

### P0.4 Inventory Safety and Anti-Corruption
- [ ] Drag stack count > 1 into workshop box, confirm split + return.
- [ ] Attempt removing item while project active (normal item): blocked.
- [ ] Attempt removing item while project active (shield weapon proxy): blocked.
- [ ] Attempt removing item while project active (double-weapon second part): blocked.
- [ ] Confirm warning notification appears and no state corruption.

## P1 Execution Checklist

### P1.1 Dynamic JSON Engine
- [ ] Validate 4 models from `CustomEnchants.example.json` (including virtual enum case `SaveAll`).
- [ ] Verify dynamic params: slider, enum, enum override localization.
- [ ] Verify naming composition: `BaseName` / `NameCompleted` / `Prefix` / `Suffix`.
- [ ] Validate field injection for `Value` and nested `Value.Value`.
- [ ] Validate `ComponentIndex = -1` broadcast and targeted index injection.

### P1.2 Formulas and PriceTables
- [ ] Evaluate precedence and parentheses behavior in formulas.
- [ ] Validate unknown variable handling (graceful fallback/no crash).
- [ ] Validate enum standard and virtual enum (`EnumOverrides`) mapping to pricing.
- [ ] Validate `DEFAULT` fallback in price tables.
- [ ] Compare UI preview values with final charged/applied values.

### P1.3 Pathfinder Rules and Settings Interactions
- [ ] Toggle `RequirePlusOneFirst` ON/OFF and validate gating.
- [ ] Toggle `EnforcePointsLimit` ON/OFF with max enhancement/total checks.
- [ ] Toggle `ApplySlotPenalty` ON/OFF and validate multiplier effects.
- [ ] Toggle `EnableEpicCosts` ON/OFF and validate epic multiplier.
- [ ] Toggle `InstantCrafting` ON/OFF and validate delayed vs immediate behavior.
- [ ] Validate settings interaction when global enforce is disabled.

### P1.4 UI/UX Flows
- [ ] Open workshop via dialog/loot mode.
- [ ] Open workshop via IMGUI mode.
- [ ] Open via UMM-configured hotkeys.
- [ ] Re-open repeatedly and verify no input soft-lock.
- [ ] Verify world input blocking while IMGUI is open (mouse/keyboard/gamepad).

## P2 Execution Checklist

### P2.1 External Compatibility
- [ ] Validate behavior with external/DLC enchant GUIDs present.
- [ ] Validate behavior when external GUID is missing/unresolvable.
- [ ] Validate extra unused JSON entries do not destabilize runtime.

### P2.2 Endurance and Performance
- [ ] 30-60 min session with repeated craft, saves, map changes.
- [ ] Observe UI open latency over time.
- [ ] Observe save-load time with many dynamic enchants.
- [ ] Confirm no growing repeating error patterns in logs.

## Run Notes Template
- Save profile:
- Mod settings profile:
- Game locale:
- Repro steps:
- Expected:
- Actual:
- Result: PASS / FAIL / BLOCKED
- Log excerpt tag(s):
