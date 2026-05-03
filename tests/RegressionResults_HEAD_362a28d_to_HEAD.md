# Regression Results HEAD `362a28d` -> `HEAD`

## Campaign Status
- Overall status: IN PROGRESS
- Automatic checks status: PARTIAL PASS
- In-game checks status: PENDING (requires game runtime execution)

## Automatic Checks (Executed)

| Check | Command | Result |
|---|---|---|
| Build release | `dotnet build CraftingSystem.csproj -c Release` | PASS |
| JSON syntax custom enchants | `ConvertFrom-Json ModConfig/CustomEnchants.json` | PASS |
| JSON syntax custom enchants example | `ConvertFrom-Json ModConfig/CustomEnchants.example.json` | PASS |
| JSON syntax localization | `ConvertFrom-Json ModConfig/Localization.json` | PASS |

## Risks Identified Before In-Game Validation
- Runtime correctness of dynamic blueprint injection still depends on live game execution paths.
- Save/load dynamic GUID rebuild behavior must be confirmed on real saves.
- Inventory protection for shield/double weapon relies on reflection access and must be validated in gameplay.

## Blocking Conditions
- None for automated/static checks.
- In-game validation requires launching WotR with the mod and representative save states.

## Final Release Recommendation
- Current recommendation: CONDITIONAL GO
- Condition: complete all P0 and P1 in-game checks from `tests/RegressionCampaign_HEAD_362a28d_to_HEAD.md`.
