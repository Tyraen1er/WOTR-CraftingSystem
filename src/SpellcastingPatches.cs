using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Items.Equipment;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.Items;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.Blueprints.Items;
using Kingmaker.Blueprints.Items.Ecnchantments;
using Kingmaker.Utility;

namespace CraftingSystem
{
    public static class SpellcastingPatches
    {
        private class CachedSpellcastingParams
        {
            public List<ItemEnchantment> Enchantments;
            public bool HasSpellcasting;
            public BlueprintAbility Spell;
            public int Charges;
            public int Cl;
            public int Sl;
            public int Dc;

            public bool IsUpToDate(List<ItemEnchantment> current)
            {
                if (current == null) return Enchantments == null;
                if (Enchantments == null) return false;
                if (current.Count != Enchantments.Count) return false;
                for (int i = 0; i < current.Count; i++)
                {
                    if (current[i] != Enchantments[i]) return false;
                }
                return true;
            }
        }

        private static readonly ConditionalWeakTable<ItemEntity, CachedSpellcastingParams> _cache = 
            new ConditionalWeakTable<ItemEntity, CachedSpellcastingParams>();

        public static bool TryGetSpellcastingParams(ItemEntity item, out BlueprintAbility spell, out int charges, out int cl, out int sl, out int dc)
        {
            spell = null;
            charges = 1;
            cl = 1;
            sl = 1;
            dc = 10;

            if (item == null) return false;

            // During ItemEntity constructor execution (specifically for ItemEntityShield),
            // calling item.Enchantments will trigger UpdateCachedEnchantments, which accesses
            // the subclass fields (like ArmorComponent) before they are initialized, causing a NullReferenceException.
            if (item is ItemEntityShield shield && shield.ArmorComponent == null) return false;

            if (item.Enchantments == null) return false;

            var currentEnchants = item.Enchantments;

            if (_cache.TryGetValue(item, out var cached) && cached.IsUpToDate(currentEnchants))
            {
                if (cached.HasSpellcasting)
                {
                    spell = cached.Spell;
                    charges = cached.Charges;
                    cl = cached.Cl;
                    sl = cached.Sl;
                    dc = cached.Dc;
                    return true;
                }
                return false;
            }

            // Otherwise, compute and update cache
            bool found = false;
            BlueprintAbility foundSpell = null;
            int foundCharges = 1;
            int foundCl = 1;
            int foundSl = 1;
            int foundDc = 10;

            foreach (var ench in currentEnchants)
            {
                if (ench == null || ench.Blueprint == null) continue;
                
                var guid = ench.Blueprint.AssetGuid;
                if (DynamicGuidHelper.TryDecodeGuid(guid, out string enchantId, out var paramValues, out _))
                {
                    if (enchantId == "013")
                    {
                        // paramValues layout: [isFeature, SpellHash0, SpellHash1, SpellHash2, SpellHash3, Charges, CasterLevel, SpellLevel, DC]
                        if (paramValues.Count >= 9)
                        {
                            string spellGuid = CustomEnchantmentsBuilder.GetSpellGuidByHash(paramValues.Skip(1).ToList());
                            if (!string.IsNullOrEmpty(spellGuid))
                            {
                                foundSpell = ResourcesLibrary.TryGetBlueprint(BlueprintGuid.Parse(spellGuid)) as BlueprintAbility;
                                foundCharges = paramValues[5];
                                foundCl = paramValues[6];
                                foundSl = paramValues[7];
                                foundDc = paramValues[8];
                                found = true;
                                break;
                            }
                        }
                    }
                }
            }

            var newCache = new CachedSpellcastingParams
            {
                Enchantments = new List<ItemEnchantment>(currentEnchants),
                HasSpellcasting = found,
                Spell = foundSpell,
                Charges = foundCharges,
                Cl = foundCl,
                Sl = foundSl,
                Dc = foundDc
            };

            lock (_cache)
            {
                _cache.Remove(item);
                _cache.Add(item, newCache);
            }

            if (found)
            {
                spell = foundSpell;
                charges = foundCharges;
                cl = foundCl;
                sl = foundSl;
                dc = foundDc;
                return true;
            }
            return false;
        }

        [HarmonyPatch(typeof(ItemEntity), nameof(ItemEntity.OnDidEquipped), new[] { typeof(UnitEntityData) })]
        public static class ItemEntity_OnDidEquipped_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(ItemEntity __instance, UnitEntityData wielder)
            {
                if (TryGetSpellcastingParams(__instance, out BlueprintAbility spell, out int charges, out int cl, out int sl, out int dc))
                {
                    if (spell != null)
                    {
                        if (__instance.Ability == null || __instance.Ability.Blueprint != spell)
                        {
                            if (__instance.Ability != null)
                            {
                                __instance.Wielder.RemoveFact(__instance.Ability);
                            }
                            Ability ability = __instance.Wielder.AddFact<Ability>(spell, null, null);
                            ability.SetSourceItem(__instance);
                            __instance.Ability = ability;
                        }
                    }
                }
            }
        }

        [HarmonyPatch(typeof(ItemEntity), "ReapplyAbilities")]
        public static class ItemEntity_ReapplyAbilities_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix(ItemEntity __instance)
            {
                if (TryGetSpellcastingParams(__instance, out BlueprintAbility spell, out int charges, out int cl, out int sl, out int dc))
                {
                    if (__instance.Wielder == null) return false;

                    if (__instance.IsIdentified)
                    {
                        if (__instance.Ability != null && __instance.Ability.Blueprint != spell)
                        {
                            __instance.Wielder.RemoveFact(__instance.Ability);
                            __instance.Ability = null;
                        }
                        if (__instance.Ability == null && spell != null)
                        {
                            // Fix B: Prevents synchronous re-entry loop by checking if the wielder already has the fact associated with this item
                            var alreadyHasFact = __instance.Wielder.Facts.GetAll<Ability>().FirstOrDefault(a => a.Blueprint == spell && a.SourceItem == __instance);
                            if (alreadyHasFact == null)
                            {
                                Ability ability = __instance.Wielder.AddFact<Ability>(spell, null, null);
                                ability.SetSourceItem(__instance);
                                __instance.Ability = ability;
                            }
                            else
                            {
                                __instance.Ability = alreadyHasFact;
                            }
                        }
                    }
                    else
                    {
                        if (__instance.Ability != null)
                        {
                            __instance.Wielder.RemoveFact(__instance.Ability);
                            __instance.Ability = null;
                        }
                    }
                    return false; // Skip original method
                }
                return true; // Run original method
            }
        }

        [HarmonyPatch(typeof(ItemEntity), nameof(ItemEntity.IsSpendCharges), MethodType.Getter)]
        public static class ItemEntity_IsSpendCharges_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix(ItemEntity __instance, ref bool __result)
            {
                if (TryGetSpellcastingParams(__instance, out _, out _, out _, out _, out _))
                {
                    __result = true;
                    return false; // Skip original getter
                }
                return true;
            }
        }

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

        [HarmonyPatch(typeof(ItemEntity), nameof(ItemEntity.RestoreCharges))]
        public static class ItemEntity_RestoreCharges_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix(ItemEntity __instance)
            {
                if (TryGetSpellcastingParams(__instance, out _, out int charges, out _, out _, out _))
                {
                    __instance.Charges = charges;
                    return false; // Skip original method
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(ItemStatHelper), nameof(ItemStatHelper.GetSpellLevel), new[] { typeof(ItemEntity) })]
        public static class ItemStatHelper_GetSpellLevel_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix(ItemEntity item, ref int __result)
            {
                if (TryGetSpellcastingParams(item, out _, out _, out _, out int sl, out _))
                {
                    __result = sl;
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(ItemStatHelper), nameof(ItemStatHelper.GetCasterLevel), new[] { typeof(ItemEntity) })]
        public static class ItemStatHelper_GetCasterLevel_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix(ItemEntity item, ref int __result)
            {
                if (TryGetSpellcastingParams(item, out _, out _, out int cl, out _, out _))
                {
                    __result = cl;
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(ItemStatHelper), nameof(ItemStatHelper.GetCasterLevel), new[] { typeof(ItemEntity), typeof(UnitEntityData) })]
        public static class ItemStatHelper_GetCasterLevelWithCaster_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix(ItemEntity item, UnitEntityData caster, ref int __result)
            {
                if (TryGetSpellcastingParams(item, out _, out _, out int cl, out _, out _))
                {
                    __result = cl;
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(ItemStatHelper), nameof(ItemStatHelper.GetDC), new[] { typeof(ItemEntity) })]
        public static class ItemStatHelper_GetDC_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix(ItemEntity item, ref int __result)
            {
                if (TryGetSpellcastingParams(item, out _, out _, out _, out _, out int dc))
                {
                    __result = dc;
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(ItemStatHelper), nameof(ItemStatHelper.GetDC), new[] { typeof(ItemEntity), typeof(UnitEntityData) })]
        public static class ItemStatHelper_GetDCWithCaster_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix(ItemEntity item, UnitEntityData caster, ref int __result)
            {
                if (TryGetSpellcastingParams(item, out _, out _, out _, out _, out int dc))
                {
                    __result = dc;
                    return false;
                }
                return true;
            }
        }
    }
}
