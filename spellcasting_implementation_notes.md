# Technical Reference: Custom Spellcasting Enchantments ("Sortilèges")

This document details the mechanics of equipment-granted spellcasting abilities in *Pathfinder: Wrath of the Righteous* (WOTR) and maps out the proposed implementation for the dynamic crafting system mod.

---

## 1. Feature Specifications & Requirements
* **Goal**: Enable a custom enchantment, **"Sortilèges"** (Enchantment ID `013`), which allows any weapon or accessory to grant its wielder a specific spell.
* **Customization Parameters**:
  * **Spell Type**: Target spell (e.g., *Fire Snake*, *Waves of Fatigue*, *Remove Paralysis*, *Transformation*).
  * **Charges per Day**: Number of times the spell can be cast daily (typically 1 to 5).
  * **Caster Level (CL)**: Caster level for the spell effect (typically 1 to 20).
  * **Spell Level (SL)**: Spell level for the spell effect (typically 1 to 9).
  * **Difficulty Class (DC)**: DC for the saving throws of the spell (typically 10 to 40).
* **Constraints**:
  * Must support any vanilla spell dynamically.
  * Must correctly display and track charges in the game's Action Bar.
  * Must utilize correct targeting templates (lines, cones, single-target, point-ground) natively.
  * Must not break game save states or corrupt item serializations.

---

## 2. Vanilla Mechanics (Witness Items Analysis)
To understand how the game handles item-based spells, we analyzed four witness items:
1. **Staff of Burned Oak** (GUID: `bbde81f4b99e26542bf6366990fd5129`) — casts *Fire Snake* (2 charges/day, CL 9, SL 5, DC 19).
2. **Staff of Curse** (GUID: `0993799d4cefa0c4486952d59fa11518`) — casts *Waves of Fatigue* (3 charges/day, CL 9, SL 5, DC 20).
3. **Lightning Strike Axe** (GUID: `0c527fb7c259f4748b037db4e57db3b2`) — casts *Remove Paralysis* (2 charges/day, CL 3, SL 2, DC 10).
4. **Redemptor Longsword** (GUID: `50e7f5c17e327e2438f8878a0af1cc8c`) — casts *Transformation* (1 charge/day, CL 11, SL 6, DC 20).

### Key Findings on Witness Item Blueprints:
* Inherit from `BlueprintItemEquipment`.
* Define the spellcasting properties **directly on the item blueprint root**:
  * `m_Ability`: Reference to the vanilla spell `BlueprintAbility` (e.g., `FireSnake`).
  * `SpendCharges`: `true`
  * `Charges`: Integer limit (e.g., 2).
  * `RestoreChargesOnRest`: `true`
  * `CasterLevel`, `SpellLevel`, and `DC` are populated directly on the item.
* **No Custom Resources**: They do *not* use a character-bound resource (`BlueprintAbilityResource`). Instead, the `ItemEntity` instance tracks remaining uses in its own property.
* **Passive vs Active Enchantments**: The item's passive enchantments (e.g., `StaffOfBurnedOakEnchantment`) use components like `AddUnitFeatureEquipment` to grant passive bonuses, but **they do not grant the active spell casting capability**.
* The game's slot manager natively detects `blueprintItemEquipment.Ability`, registers it on the character during equip events, and binds it to the item.

---

## 3. How the WOTR Engine Handles Equipment Spells
Decompiled code investigation revealed the following flow:

### A. Equipping & Registering
In `Kingmaker.Items.ItemEntity.OnDidEquipped(UnitEntityData wielder)`:
```csharp
BlueprintItemEquipment blueprintItemEquipment = this.Blueprint as BlueprintItemEquipment;
if (this.IsIdentified && blueprintItemEquipment)
{
    if (blueprintItemEquipment.Ability)
    {
        Ability ability = this.Wielder.AddFact(blueprintItemEquipment.Ability, null, null);
        ability.SetSourceItem(this);
        this.Ability = ability;
        return;
    }
    // ...
}
```
This adds the spell as an `Ability` fact to the character and sets the `SourceItem` reference back to the equipping `ItemEntity`.

### B. Parameter Resolution (CL, SL, DC)
When casting the spell, `AbilityData.GetParamsFromItem(ItemEntity itemEntity)` builds the spell casting parameters by calling extension methods from `ItemStatHelper`:
```csharp
private AbilityParams GetParamsFromItem(ItemEntity itemEntity)
{
    int dc = itemEntity.GetDC(this.Caster);
    int num = itemEntity.GetCasterLevel(this.Caster);
    // ... UMD / feat adjustments
    return new AbilityParams
    {
        SpellSource = SpellSource.None,
        CasterLevel = num,
        SpellLevel = itemEntity.GetSpellLevel(),
        DC = dc,
        Metamagic = metamagic
    };
}
```
`ItemStatHelper` maps directly back to the equipped item's blueprint fields:
* `item.GetSpellLevel()` -> returns `blueprintItemEquipment.SpellLevel`.
* `item.GetDC(caster)` -> returns `blueprintItemEquipment.DC`.
* `item.GetCasterLevel(caster)` -> returns `blueprintItemEquipment.CasterLevel`.

### C. Charge Tracking & Consumption
* The remaining charges are stored directly on the item instance in `ItemEntity.Charges`.
* Consumption is managed in `ItemEntity.SpendCharges(UnitDescriptor user)`:
  * Decrements `this.Charges` if `IsSpendCharges` is true.
  * `IsSpendCharges` returns `blueprintItemEquipment != null && blueprintItemEquipment.GainAbility && blueprintItemEquipment.SpendCharges`.

---

## 4. Current Mod Implementation & Issues
### The Old Approach:
The mod builds custom spellcasting using:
* Model `111` (Feature)
* Model `112` (Ability)
* Model `113` (Resource)
The dynamic enchantment applies Feature `111` via `AddUnitFeatureEquipment`. Feature `111` adds Ability `112` (a cloned/deserialized copy of the spell blueprint) and Resource `113` (tracking charges).

### Why the Old Approach Fails:
1. **Damaged Clones**: Deserializing a complex `BlueprintAbility` via JSON (even with type binders and resolver overrides) often strips out or breaks nested `Element` properties, restriction caches, and projectile managers. This is why targeting is broken: the cloned spell behaves as a `<null>` ability or lacks targeting templates in the UI.
2. **Detached Mechanics**: Because the spell is granted via a feature fact rather than the slot manager, the engine does not see it as an item-linked ability. `itemEntity.Ability` remains null.
3. **Mismatched Charges**: The engine expects item spells to utilize `ItemEntity.Charges` and check `IsSpendCharges` to display the charge count in the Action Bar. The character-bound resource `113` bypasses this logic, causing UI discrepancies.

---

## 5. The Unified Solution: Harmony Property Interception
To implement this cleanly without JSON serialization of spells or complex resource logic, we should **intercept the properties of the item at runtime** via Harmony patches.

### The Core Idea:
Instead of creating a new cloned ability blueprint, we reference the **original vanilla spell** directly and make the equipping weapon/accessory act as the spellcaster.

### A. Detecting our Dynamic Enchantment
Our dynamic GUID helper encodes parameters into the GUID. We can detect if an `ItemEntity` has our spellcasting enchantment by checking its active enchantments for one with the dynamic signature:
```csharp
public static bool TryGetSpellcastingParams(ItemEntity item, out BlueprintAbility spell, out int charges, out int cl, out int sl, out int dc)
{
    spell = null;
    charges = 1;
    cl = 1;
    sl = 1;
    dc = 10;

    if (item?.Enchantments == null) return false;

    foreach (var ench in item.Enchantments)
    {
        // Check if the enchantment GUID belongs to our dynamic spellcasting model (ID 013 / Feature 111)
        var guidStr = ench.Blueprint.AssetGuid.ToString();
        if (DynamicGuidHelper.TryDecodeGuid(ench.Blueprint.AssetGuid, out string enchantId, out var paramValues, out _))
        {
            if (enchantId == "013" || enchantId == "111")
            {
                int spellTypeVal = paramValues.Count > 0 ? paramValues[0] : 1;
                string spellGuid = null;
                switch (spellTypeVal)
                {
                    case 1: spellGuid = "ebade19998e1f8542a1b55bd4da766b3"; break; // Fire Snake
                    case 2: spellGuid = "8878d0c46dfbd564e9d5756349d5e439"; break; // Waves of Fatigue
                    case 3: spellGuid = "f8bce986adfc88544a42bf4ab7ae75b2"; break; // Remove Paralysis
                    case 4: spellGuid = "27203d62eb3d4184c9aced94f22e1806"; break; // Transformation
                }
                
                if (spellGuid != null)
                {
                    spell = ResourcesLibrary.TryGetBlueprint(BlueprintGuid.Parse(spellGuid)) as BlueprintAbility;
                    charges = paramValues.Count > 1 ? paramValues[1] : 1;
                    cl = paramValues.Count > 2 ? paramValues[2] : 9;
                    sl = paramValues.Count > 3 ? paramValues[3] : 5;
                    dc = paramValues.Count > 4 ? paramValues[4] : 20;
                    return true;
                }
            }
        }
    }
    return false;
}
```

### B. Harmony Patches List

1. **`BlueprintItemEquipment.Ability` [Getter Patch]**
   If the query originates from an equipped item containing our enchantment, return the target spell's original `BlueprintAbility`.
   * *Benefit*: The slot manager will automatically find and register the spell on the wielder, linking it natively to `ItemEntity.Ability`. Since it is the original vanilla blueprint, targeting and templates are 100% correct and unbroken.

2. **`ItemEntity.IsSpendCharges` [Getter Patch]**
   Force it to return `true` if the item has our spellcasting enchantment.

3. **`ItemStatHelper.GetCasterLevel` / `GetSpellLevel` / `GetDC` [Prefix Patches]**
   Intercept calls to read these stats. If the item has our enchantment, intercept the method and return the values decoded from the dynamic parameters:
   ```csharp
   [HarmonyPatch(typeof(ItemStatHelper), nameof(ItemStatHelper.GetCasterLevel), new[] { typeof(ItemEntity), typeof(UnitEntityData) })]
   public static class ItemStatHelper_GetCasterLevel_Patch
   {
       public static bool Prefix(ItemEntity item, ref int __result)
       {
           if (TryGetSpellcastingParams(item, out _, out _, out int cl, out _, out _))
           {
               __result = cl;
               return false; // Skip original method
           }
           return true;
       }
   }
   ```

4. **`ItemEntity` Equip Initialization**
   Hook `ItemEntity.OnDidEquipped` or `ItemEntity` creation to ensure `ItemEntity.Charges` is initialized to the enchantment's charge parameter if it hasn't been set yet.

This architecture ensures perfect, native UI representation, bug-free targeting templates, and generic scalability for any spell.

---

## 6. JSON Deserialization & Resolver Constraints

### The Field-Only Resolver Quirk
The mod uses a custom `OwlcatContractResolver` to handle the complex and private nested game data structures. This resolver is explicitly configured to reflect and deserialize **fields** rather than C# properties:
```csharp
var props = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Select(f => base.CreateProperty(f, memberSerialization))
                .ToList();
```
Because it targets fields and ignores standard C# properties, fields backing custom property logic (such as private backing fields) will not match incoming JSON keys if the JSON keys match the public property names.

### The Spell UI Slider Bug
In `DynamicParam`, the parameter type was defined using a private backing field and a public property:
```csharp
private string _type;
public string Type 
{ 
    get => !string.IsNullOrEmpty(_type) ? _type : (!string.IsNullOrEmpty(EnumTypeName) ? "Enum" : "Slider");
    set => _type = value; 
}
```
During deserialization of the enchantment data (e.g. from `CustomEnchants_Enchantments.json`):
1. The JSON contains `"Type": "Spell"`.
2. The contract resolver registers `_type` as a field but has no knowledge of the public `Type` property.
3. Newtonsoft.Json fails to map the `"Type"` JSON key to the `_type` field because the names differ.
4. Consequently, `_type` remains `null`.
5. The getter for the `Type` property falls back to the default `"Slider"` value.
6. The crafting UI renders a slider from 1 to 100 instead of a spell selection dialog.
7. Setting the slider to a number (such as `1`) generates an invalid 4-byte hash that maps to no valid vanilla spell `BlueprintAbility` GUID, meaning no spell is granted upon equipping.

### The Resolution
Decorate the private backing field `_type` in `DynamicParam` with `[JsonProperty("Type")]`:
```csharp
[JsonProperty("Type")]
private string _type;
```
This instructs Newtonsoft.Json to map the `"Type"` JSON key directly to `_type` during deserialization, restoring the expected `"Spell"` type and enabling the spell selection search UI.

---

## 7. Charge Spending & Blueprint Constraints

### The Infinite Spell Cast Bug
Even though the spell has a charges-per-day parameter (e.g., 3 charges) and displays it on the UI action bar, the charges did not decrease when casting the spell.

### Root Cause
Investigation into the decompiled `ItemEntity.SpendCharges(UnitDescriptor user)` method revealed the following check:
```csharp
BlueprintItemEquipment blueprintItemEquipment = this.Blueprint as BlueprintItemEquipment;
if (blueprintItemEquipment == null || !blueprintItemEquipment.GainAbility)
{
    PFLog.Default.Error(this.Blueprint, string.Format("Item {0} doesn't gain ability", this.Blueprint), Array.Empty<object>());
    return false;
}
```
For custom enchantments, the enchantment is applied dynamically to an existing item (e.g. a weapon or a ring). The base blueprint of these items (such as `BlueprintItemWeapon` or `BlueprintItemEquipment`) does NOT have `GainAbility` set to `true`.
Consequently:
1. When casting the spell, `AbilityData.Spend()` calls `sourceItem.SpendCharges(this.Caster)`.
2. `ItemEntity.SpendCharges` checks `blueprintItemEquipment.GainAbility` and exits early (returning `false` and logging an error) because it is false.
3. Charges are never decremented.

### The Resolution
We added a prefix patch to `ItemEntity.SpendCharges(UnitDescriptor user)` in [SpellcastingPatches.cs](file:///c:/Users/emman/Desktop/dev/Crafting-system/src/SpellcastingPatches.cs):
```csharp
[HarmonyPatch(typeof(ItemEntity), nameof(ItemEntity.SpendCharges), new[] { typeof(UnitDescriptor) })]
public static class ItemEntity_SpendCharges_Patch
{
    [HarmonyPrefix]
    public static bool Prefix(ItemEntity __instance, UnitDescriptor user, ref bool __result)
    {
        if (TryGetSpellcastingParams(__instance, out _, out _, out _, out _, out _))
        {
            if (!__instance.IsSpendCharges)
            {
                __result = true;
                return false;
            }

            bool hasNoCharges = false;
            if (__instance.Charges > 0)
            {
                if (__instance.Charges > 1 && __instance.Count > 1)
                {
                    ItemEntity itemEntity = __instance.Split(1);
                    itemEntity.Charges--;
                    ItemsCollection collection = itemEntity.Collection;
                    if (collection != null)
                    {
                        collection.TryMergeContainedItem(itemEntity);
                    }
                }
                else
                {
                    __instance.Charges--;
                }
            }
            else
            {
                hasNoCharges = true;
            }

            __result = !hasNoCharges;
            return false; // Skip original method
        }
        return true; // Run original method
    }
}
```
This safely intercepts charge spending for custom spellcasting items, decrements the charges correctly, and prevents the equipped item from being destroyed or degraded when charges reach zero.


