using System;
using System.Reflection;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Items;
using Kingmaker.Blueprints.Items.Equipment;
using UnityEngine;

namespace CraftingSystem
{
    public static class CustomItemBuilder
    {
        public static void BuildBlankAccessories()
        {
            try
            {
                Main.ModEntry.Logger.Log("[CustomItemBuilder] Initializing blank accessories creation...");

                // Template GUIDs:
                // Belt: Belt of Incredible Dexterity +2: "b6af4c1834999e74497b41588d1071cd"
                // Gloves: Gloves of Dexterity +2: "6555965e6540c3b48b9a352214ecba41"
                // Goggles/Glasses: Eyes of the Eagle: "f780cd67dd4e4bce9044d8ebc550cf65"

                var templateBelt = ResourcesLibrary.TryGetBlueprint<BlueprintItemEquipmentBelt>(BlueprintGuid.Parse("b6af4c1834999e74497b41588d1071cd"));
                var templateGloves = ResourcesLibrary.TryGetBlueprint<BlueprintItemEquipmentGloves>(BlueprintGuid.Parse("6555965e6540c3b48b9a352214ecba41"));
                var templateGlasses = ResourcesLibrary.TryGetBlueprint<BlueprintItemEquipmentGlasses>(BlueprintGuid.Parse("f780cd67dd4e4bce9044d8ebc550cf65"));

                if (templateBelt == null) Main.ModEntry.Logger.Error("[CustomItemBuilder] Failed to find template belt b6af4c1834999e74497b41588d1071cd.");
                if (templateGloves == null) Main.ModEntry.Logger.Error("[CustomItemBuilder] Failed to find template gloves 6555965e6540c3b48b9a352214ecba41.");
                if (templateGlasses == null) Main.ModEntry.Logger.Error("[CustomItemBuilder] Failed to find template glasses f780cd67dd4e4bce9044d8ebc550cf65.");

                if (templateBelt != null)
                {
                    CloneSimpleAccessory(
                        templateBelt,
                        "c0010000000000000000000000000001",
                        "BlankBeltItem",
                        "ui_item_blank_belt_name",
                        "Simple Belt",
                        "ui_item_blank_belt_desc",
                        "A simple, non-magical leather belt, ready for custom enchantments."
                    );
                }

                if (templateGloves != null)
                {
                    CloneSimpleAccessory(
                        templateGloves,
                        "c0010000000000000000000000000002",
                        "BlankGlovesItem",
                        "ui_item_blank_gloves_name",
                        "Simple Gloves",
                        "ui_item_blank_gloves_desc",
                        "Simple, non-magical leather gloves, ready for custom enchantments."
                    );
                }

                if (templateGlasses != null)
                {
                    CloneSimpleAccessory(
                        templateGlasses,
                        "c0010000000000000000000000000003",
                        "BlankGlassesItem",
                        "ui_item_blank_glasses_name",
                        "Simple Goggles",
                        "ui_item_blank_glasses_desc",
                        "Simple, non-magical lenses, ready for custom enchantments."
                    );
                }

                Main.ModEntry.Logger.Log("[CustomItemBuilder] Blank accessories creation completed successfully.");
            }
            catch (Exception ex)
            {
                Main.ModEntry.Logger.Error($"[CustomItemBuilder] Exception during blank accessories registration: {ex}");
            }
        }

        private static T CloneSimpleAccessory<T>(
            T template,
            string newGuid,
            string name,
            string displayNameKey,
            string defaultDisplayName,
            string descKey,
            string defaultDesc) where T : BlueprintItemEquipmentSimple, new()
        {
            var bp = Helpers.CreateBlueprint<T>(newGuid, name);

            var itemType = typeof(BlueprintItem);
            var equipSimpleType = typeof(BlueprintItemEquipmentSimple);
            var equipType = typeof(BlueprintItemEquipment);

            // Copy fields from BlueprintItem
            var m_Icon = itemType.GetField("m_Icon", BindingFlags.Instance | BindingFlags.NonPublic);
            m_Icon?.SetValue(bp, m_Icon.GetValue(template));

            var m_Weight = itemType.GetField("m_Weight", BindingFlags.Instance | BindingFlags.NonPublic);
            m_Weight?.SetValue(bp, m_Weight.GetValue(template));

            var m_InventoryPutSound = itemType.GetField("m_InventoryPutSound", BindingFlags.Instance | BindingFlags.NonPublic);
            m_InventoryPutSound?.SetValue(bp, m_InventoryPutSound.GetValue(template));

            var m_InventoryTakeSound = itemType.GetField("m_InventoryTakeSound", BindingFlags.Instance | BindingFlags.NonPublic);
            m_InventoryTakeSound?.SetValue(bp, m_InventoryTakeSound.GetValue(template));

            // Copy fields from BlueprintItemEquipmentSimple
            var m_InventoryEquipSound = equipSimpleType.GetField("m_InventoryEquipSound", BindingFlags.Instance | BindingFlags.NonPublic);
            m_InventoryEquipSound?.SetValue(bp, m_InventoryEquipSound.GetValue(template));

            // Clear enchantments
            var m_Enchantments = equipSimpleType.GetField("m_Enchantments", BindingFlags.Instance | BindingFlags.NonPublic);
            if (m_Enchantments != null)
            {
                m_Enchantments.SetValue(bp, new BlueprintEquipmentEnchantmentReference[0]);
            }

            // Copy fields from BlueprintItemEquipment
            var m_EquipmentEntity = equipType.GetField("m_EquipmentEntity", BindingFlags.Instance | BindingFlags.NonPublic);
            m_EquipmentEntity?.SetValue(bp, m_EquipmentEntity.GetValue(template));

            var m_EquipmentEntityAlternatives = equipType.GetField("m_EquipmentEntityAlternatives", BindingFlags.Instance | BindingFlags.NonPublic);
            m_EquipmentEntityAlternatives?.SetValue(bp, m_EquipmentEntityAlternatives.GetValue(template));

            var m_ForcedRampColorPresetIndex = equipType.GetField("m_ForcedRampColorPresetIndex", BindingFlags.Instance | BindingFlags.NonPublic);
            m_ForcedRampColorPresetIndex?.SetValue(bp, m_ForcedRampColorPresetIndex.GetValue(template));

            // Set custom properties
            var m_Cost = itemType.GetField("m_Cost", BindingFlags.Instance | BindingFlags.NonPublic);
            m_Cost?.SetValue(bp, 50); // 50 gold

            var m_DisplayNameText = itemType.GetField("m_DisplayNameText", BindingFlags.Instance | BindingFlags.NonPublic);
            m_DisplayNameText?.SetValue(bp, Helpers.CreateString(displayNameKey, defaultDisplayName));

            var m_DescriptionText = itemType.GetField("m_DescriptionText", BindingFlags.Instance | BindingFlags.NonPublic);
            m_DescriptionText?.SetValue(bp, Helpers.CreateString(descKey, defaultDesc));

            // Register in the cache
            ResourcesLibrary.BlueprintsCache.AddCachedBlueprint(bp.AssetGuid, bp);

            Main.ModEntry.Logger.Log($"[CustomItemBuilder] Cloned blank accessory '{name}' (GUID: {newGuid}) from template '{template.name}'.");

            return bp;
        }
    }
}
