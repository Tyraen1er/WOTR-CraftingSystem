using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json;
using System.Linq;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Items.Weapons;
using Kingmaker.Blueprints.Items.Armors;
using Kingmaker.Blueprints.Items.Shields;
using Kingmaker.Blueprints.Items.Equipment;
using Kingmaker.Blueprints.Items;
using Kingmaker.UI.Common;
using UnityEngine;

namespace CraftingSystem
{
    public class ItemData
    {
        public string Name;
        public int BaseCost;
        public string Category;
        public Sprite Icon;
        public string Description;
        public string TypeName;
        // Level -> Guid
        public Dictionary<int, string> VariantGuids = new Dictionary<int, string>();
        // Level -> Cost
        public Dictionary<int, int> VariantCosts = new Dictionary<int, int>();
        // Level -> Icon
        public Dictionary<int, Sprite> VariantIcons = new Dictionary<int, Sprite>();

        public string GetGuid(int level)
        {
            if (VariantGuids.TryGetValue(level, out var guid)) return guid;
            return VariantGuids.ContainsKey(0) ? VariantGuids[0] : null;
        }

        public Sprite GetIcon(int level)
        {
            if (VariantIcons.TryGetValue(level, out var icon) && icon != null) return icon;
            return Icon;
        }

        public int GetCost(int level)
        {
            if (VariantCosts.TryGetValue(level, out int cost)) return cost;

            // Fallback: Prix de base + (Bonus^2 * (Weapon ? 2000 : 1000))
            int magicCost = (level * level) * (Category == "Weapon" ? 2000 : 1000);
            return BaseCost + magicCost;
        }

        public string GetDisplayName(int level)
        {
            string name = Name ?? "Unknown Item";
            if (level <= 0) return name;
            return $"{name} +{level}";
        }
    }

    public static class ItemScanner
    {
        public static List<ItemData> Weapons = new List<ItemData>();
        public static List<ItemData> Armors = new List<ItemData>();
        public static List<ItemData> Shields = new List<ItemData>();
        public static List<ItemData> Accessories = new List<ItemData>();

        // Cache des icônes pour le navigateur d'icônes
        public static Dictionary<ItemsFilter.ItemType, List<BlueprintItem>> IconCache = new Dictionary<ItemsFilter.ItemType, List<BlueprintItem>>();

        public static void FinalizeScan(
            IEnumerable<(BlueprintItemWeapon bp, BlueprintGuid guid)> weapons,
            IEnumerable<(BlueprintItemArmor bp, BlueprintGuid guid)> armors,
            IEnumerable<(BlueprintItemShield bp, BlueprintGuid guid)> shields,
            IEnumerable<(BlueprintItemEquipment bp, BlueprintGuid guid)> accessories)
        {
            Weapons.Clear();
            Armors.Clear();
            Shields.Clear();
            Accessories.Clear();
            IconCache.Clear();

            var weaponMap = new Dictionary<string, ItemData>();
            var armorMap = new Dictionary<string, ItemData>();
            var shieldMap = new Dictionary<string, ItemData>();

            HashSet<Sprite> uniqueWeaponIcons = new HashSet<Sprite>();
            HashSet<Sprite> uniqueArmorIcons = new HashSet<Sprite>();
            HashSet<Sprite> uniqueShieldIcons = new HashSet<Sprite>();
            HashSet<Sprite> uniqueAccessoryIcons = new HashSet<Sprite>();

            IconCache[ItemsFilter.ItemType.Weapon] = new List<BlueprintItem>();
            IconCache[ItemsFilter.ItemType.Armor] = new List<BlueprintItem>();
            IconCache[ItemsFilter.ItemType.Shield] = new List<BlueprintItem>();
            // Pour les accessoires, on les regroupe dans les autres catégories si on veut, ou on ajoute tout l'équipement
            IconCache[ItemsFilter.ItemType.Usable] = new List<BlueprintItem>();

            // 1. SCAN WEAPONS
            foreach (var item in weapons)
            {
                if (item.bp == null) continue;

                string typeName = item.bp.Type?.name ?? "";
                if (typeName == "GnomeHookedHammerHead") typeName = "GnomeHookedHammer"; // exception pour le marteau gnome
                int level = DetectLevel(item.bp.name, typeName);
                if (level < 0) continue;

                if (!weaponMap.TryGetValue(typeName, out var data))
                {
                    data = CreateBaseData(item.bp, "Weapon", typeName);
                    weaponMap[typeName] = data;
                }

                data.VariantGuids[level] = item.guid.ToString();
                data.VariantCosts[level] = (int)item.bp.m_Cost;
                data.VariantIcons[level] = item.bp.Icon;

                // On garde le nom du +0 comme nom de référence s'il existe
                if (level == 0) data.Name = item.bp.Name;

                if (item.bp.Icon != null && uniqueWeaponIcons.Add(item.bp.Icon)) IconCache[ItemsFilter.ItemType.Weapon].Add(item.bp);
            }
            Weapons.AddRange(weaponMap.Values.OrderBy(x => x.Name));

            // 2. SCAN ARMORS
            foreach (var item in armors)
            {
                if (item.bp == null) continue;

                string typeName = item.bp.Type?.name ?? "";
                int level = DetectLevel(item.bp.name, typeName);
                if (level < 0) continue;

                if (!armorMap.TryGetValue(typeName, out var data))
                {
                    data = CreateBaseData(item.bp, "Armor", typeName);
                    armorMap[typeName] = data;
                }

                data.VariantGuids[level] = item.guid.ToString();
                data.VariantCosts[level] = (int)item.bp.m_Cost;
                data.VariantIcons[level] = item.bp.Icon;

                if (level == 0) data.Name = item.bp.Name;

                if (item.bp.Icon != null && uniqueArmorIcons.Add(item.bp.Icon)) IconCache[ItemsFilter.ItemType.Armor].Add(item.bp);
            }
            Armors.AddRange(armorMap.Values.OrderBy(x => x.Name));

            // 3. SCAN SHIELDS
            foreach (var item in shields)
            {
                if (item.bp == null) continue;

                // Pour les boucliers, le type est souvent dans m_Type ou simplement le BlueprintItemShield lui-même
                // Mais DetectLevel utilise typeName pour matcher les patterns.
                string typeName = item.bp.Type?.name ?? "";
                int level = DetectLevel(item.bp.name, typeName, true);
                if (level < 0) continue;

                if (!shieldMap.TryGetValue(typeName, out var data))
                {
                    data = CreateBaseData(item.bp, "Armor", typeName); // On les traite comme des armures pour la forge
                    shieldMap[typeName] = data;
                }

                data.VariantGuids[level] = item.guid.ToString();
                data.VariantCosts[level] = (int)item.bp.m_Cost;
                data.VariantIcons[level] = item.bp.Icon;

                if (level == 0) data.Name = item.bp.Name;

                if (item.bp.Icon != null && uniqueShieldIcons.Add(item.bp.Icon)) IconCache[ItemsFilter.ItemType.Shield].Add(item.bp);
            }
            var finalShields = shieldMap.Values.OrderBy(x => x.Name).ToList();
            Shields.AddRange(finalShields);
            // On ajoute les boucliers à la liste des armures pour qu'ils soient achetables dans le même menu
            Armors.AddRange(finalShields);
            Armors = Armors.OrderBy(x => x.Name).ToList();

            // 4. SCAN ACCESSORIES (No levels - Icon Cache only, items loaded from CSV)
            foreach (var item in accessories)
            {
                if (item.bp == null) continue;

                if (item.bp.Icon != null && uniqueAccessoryIcons.Add(item.bp.Icon))
                {
                    if (!IconCache.ContainsKey(item.bp.ItemType)) IconCache[item.bp.ItemType] = new List<BlueprintItem>();
                    IconCache[item.bp.ItemType].Add(item.bp);
                }
            }

            // Load buyable accessories from JSON
            string jsonPath = Path.Combine(Main.ModEntry.Path, "buyable_accessories.json");
            if (File.Exists(jsonPath))
            {
                try
                {
                    string json = File.ReadAllText(jsonPath);
                    var configs = JsonConvert.DeserializeObject<List<AccessoryConfig>>(json);
                    if (configs != null)
                    {
                        foreach (var cfg in configs)
                        {
                            if (string.IsNullOrEmpty(cfg.Guid)) continue;

                            BlueprintItemEquipmentSimple bp = null;
                            string finalGuid = cfg.Guid;

                            if (cfg.Guid.StartsWith("c001"))
                            {
                                bp = ResourcesLibrary.TryGetBlueprint<BlueprintItemEquipmentSimple>(BlueprintGuid.Parse(cfg.Guid));
                            }
                            else
                            {
                                try
                                {
                                    string clonedGuid = GenerateBlankGuid(cfg.Guid);
                                    finalGuid = clonedGuid;

                                    bp = ResourcesLibrary.TryGetBlueprint<BlueprintItemEquipmentSimple>(BlueprintGuid.Parse(clonedGuid));
                                    if (bp == null)
                                    {
                                        var vanillaBp = ResourcesLibrary.TryGetBlueprint<BlueprintItemEquipmentSimple>(BlueprintGuid.Parse(cfg.Guid));
                                        if (vanillaBp != null)
                                        {
                                            bp = (BlueprintItemEquipmentSimple)Activator.CreateInstance(vanillaBp.GetType());
                                            CopyFields(vanillaBp, bp);
                                            bp.name = vanillaBp.name + "_BlankClone";
                                            bp.AssetGuid = BlueprintGuid.Parse(clonedGuid);

                                            // Clear enchantments
                                            var enchantmentsField = typeof(BlueprintItemEquipmentSimple).GetField("m_Enchantments", BindingFlags.NonPublic | BindingFlags.Instance);
                                            if (enchantmentsField != null) enchantmentsField.SetValue(bp, new BlueprintEquipmentEnchantmentReference[0]);

                                            // Clear components
                                            var componentsField = typeof(BlueprintScriptableObject).GetField("ComponentsArray", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                                                               ?? typeof(BlueprintScriptableObject).GetField("m_Components", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                            if (componentsField != null)
                                            {
                                                Type compType = typeof(BlueprintScriptableObject).Assembly.GetType("Kingmaker.Blueprints.BlueprintComponent");
                                                if (compType != null) componentsField.SetValue(bp, Array.CreateInstance(compType, 0));
                                            }

                                            // Set cost to 50 GP
                                            var costField = typeof(BlueprintItem).GetField("m_Cost", BindingFlags.NonPublic | BindingFlags.Instance);
                                            if (costField != null) costField.SetValue(bp, 50);

                                            // Set localized display name and description
                                            string locale = "enGB";
                                            try { locale = Kingmaker.Localization.LocalizationManager.CurrentLocale.ToString(); } catch { }

                                            string suffix = " (Blank)";
                                            string blankDesc = "A simple, non-magical accessory, ready for custom enchantments.";
                                            if (locale == "frFR")
                                            {
                                                suffix = " (Vierge)";
                                                blankDesc = "Un accessoire simple et non magique, prêt à recevoir des enchantements personnalisés.";
                                            }
                                            else if (locale == "ruRU")
                                            {
                                                suffix = " (Чистый)";
                                                blankDesc = "Простой немагический аксессуар, готовый к наложению зачарований.";
                                            }

                                            string originalName = vanillaBp.Name;
                                            if (string.IsNullOrEmpty(originalName)) originalName = vanillaBp.name;
                                            string baseName = System.Text.RegularExpressions.Regex.Replace(originalName, @"\s*\+\d+", "");

                                            string blankName = baseName + suffix;

                                            var displayNameText = Helpers.CreateString($"blank_item_name_{clonedGuid}", blankName);
                                            var descriptionText = Helpers.CreateString($"blank_item_desc_{clonedGuid}", blankDesc);

                                            var displayNameField = typeof(BlueprintItem).GetField("m_DisplayNameText", BindingFlags.NonPublic | BindingFlags.Instance);
                                            if (displayNameField != null) displayNameField.SetValue(bp, displayNameText);

                                            var descriptionField = typeof(BlueprintItem).GetField("m_DescriptionText", BindingFlags.NonPublic | BindingFlags.Instance);
                                            if (descriptionField != null) descriptionField.SetValue(bp, descriptionText);

                                            // Add to cache
                                            ResourcesLibrary.BlueprintsCache.AddCachedBlueprint(bp.AssetGuid, bp);
                                        }
                                        else
                                        {
                                            Main.ModEntry.Logger.Error($"[ITEM-SCAN] Failed to find original blueprint {cfg.Guid} ({cfg.InternalName}) for cloning.");
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Main.ModEntry.Logger.Error($"[ITEM-SCAN] Error cloning accessory {cfg.InternalName} ({cfg.Guid}): {ex}");
                                }
                            }

                            if (bp != null)
                            {
                                string resolvedName = !string.IsNullOrEmpty(cfg.DisplayName) ? cfg.DisplayName : bp.Name;
                                string category = GetCategoryForAccessory(bp);

                                var data = new ItemData
                                {
                                    Name = resolvedName,
                                    BaseCost = (int)bp.m_Cost,
                                    Category = category,
                                    Icon = bp.Icon,
                                    Description = bp.Description,
                                    TypeName = cfg.InternalName
                                };
                                data.VariantGuids[0] = finalGuid;
                                data.VariantCosts[0] = (int)bp.m_Cost;
                                Accessories.Add(data);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Main.ModEntry.Logger.Error($"[ITEM-SCAN] Error loading buyable_accessories.json: {ex.Message}");
                }
            }
            else
            {
                Main.ModEntry.Logger.Warning($"[ITEM-SCAN] buyable_accessories.json not found at {jsonPath}!");
            }

            Accessories = Accessories.OrderBy(x => x.Category).ThenBy(x => x.Name).ToList();

            Main.ModEntry.Logger.Log($"[ITEM-SCAN] Finalisé. W:{Weapons.Count} A:{Armors.Count} S:{Shields.Count} Acc:{Accessories.Count}");
        }

        public static void PreloadAndRegisterAccessories()
        {
            string jsonPath = Path.Combine(Main.ModEntry.Path, "buyable_accessories.json");
            if (!File.Exists(jsonPath))
            {
                Main.ModEntry.Logger.Warning($"[ITEM-SCAN] buyable_accessories.json not found for preloading at {jsonPath}");
                return;
            }

            try
            {
                string json = File.ReadAllText(jsonPath);
                var configs = JsonConvert.DeserializeObject<List<AccessoryConfig>>(json);
                if (configs != null)
                {
                    int clonedCount = 0;
                    foreach (var cfg in configs)
                    {
                        if (string.IsNullOrEmpty(cfg.Guid) || cfg.Guid.StartsWith("c001")) continue;

                        try
                        {
                            string clonedGuid = GenerateBlankGuid(cfg.Guid);
                            var bp = ResourcesLibrary.TryGetBlueprint<BlueprintItemEquipmentSimple>(BlueprintGuid.Parse(clonedGuid));
                            if (bp == null)
                            {
                                var vanillaBp = ResourcesLibrary.TryGetBlueprint<BlueprintItemEquipmentSimple>(BlueprintGuid.Parse(cfg.Guid));
                                if (vanillaBp != null)
                                {
                                    bp = (BlueprintItemEquipmentSimple)Activator.CreateInstance(vanillaBp.GetType());
                                    CopyFields(vanillaBp, bp);
                                    bp.name = vanillaBp.name + "_BlankClone";
                                    bp.AssetGuid = BlueprintGuid.Parse(clonedGuid);

                                    // Clear enchantments
                                    var enchantmentsField = typeof(BlueprintItemEquipmentSimple).GetField("m_Enchantments", BindingFlags.NonPublic | BindingFlags.Instance);
                                    if (enchantmentsField != null) enchantmentsField.SetValue(bp, new BlueprintEquipmentEnchantmentReference[0]);

                                    // Clear components
                                    var componentsField = typeof(BlueprintScriptableObject).GetField("ComponentsArray", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                                                       ?? typeof(BlueprintScriptableObject).GetField("m_Components", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                    if (componentsField != null)
                                    {
                                        Type compType = typeof(BlueprintScriptableObject).Assembly.GetType("Kingmaker.Blueprints.BlueprintComponent");
                                        if (compType != null) componentsField.SetValue(bp, Array.CreateInstance(compType, 0));
                                    }

                                    // Set cost to 50 GP
                                    var costField = typeof(BlueprintItem).GetField("m_Cost", BindingFlags.NonPublic | BindingFlags.Instance);
                                    if (costField != null) costField.SetValue(bp, 50);

                                    // Set localized display name and description
                                    string locale = "enGB";
                                    try { locale = Kingmaker.Localization.LocalizationManager.CurrentLocale.ToString(); } catch { }

                                    string suffix = " (Blank)";
                                    string blankDesc = "A simple, non-magical accessory, ready for custom enchantments.";
                                    if (locale == "frFR")
                                    {
                                        suffix = " (Vierge)";
                                        blankDesc = "Un accessoire simple et non magique, prêt à recevoir des enchantements personnalisés.";
                                    }
                                    else if (locale == "ruRU")
                                    {
                                        suffix = " (Чистый)";
                                        blankDesc = "Простой немагический аксессуар, готовый к наложению зачарований.";
                                    }

                                    string originalName = vanillaBp.Name;
                                    if (string.IsNullOrEmpty(originalName)) originalName = vanillaBp.name;
                                    string baseName = System.Text.RegularExpressions.Regex.Replace(originalName, @"\s*\+\d+", "");

                                    string blankName = baseName + suffix;

                                    var displayNameText = Helpers.CreateString($"blank_item_name_{clonedGuid}", blankName);
                                    var descriptionText = Helpers.CreateString($"blank_item_desc_{clonedGuid}", blankDesc);

                                    var displayNameField = typeof(BlueprintItem).GetField("m_DisplayNameText", BindingFlags.NonPublic | BindingFlags.Instance);
                                    if (displayNameField != null) displayNameField.SetValue(bp, displayNameText);

                                    var descriptionField = typeof(BlueprintItem).GetField("m_DescriptionText", BindingFlags.NonPublic | BindingFlags.Instance);
                                    if (descriptionField != null) descriptionField.SetValue(bp, descriptionText);

                                    // Add to cache
                                    ResourcesLibrary.BlueprintsCache.AddCachedBlueprint(bp.AssetGuid, bp);
                                    clonedCount++;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Main.ModEntry.Logger.Error($"[ITEM-SCAN] Preloading error for accessory {cfg.InternalName} ({cfg.Guid}): {ex}");
                        }
                    }
                    Main.ModEntry.Logger.Log($"[ITEM-SCAN] Preloaded and registered {clonedCount} custom blank accessories at boot.");
                }
            }
            catch (Exception ex)
            {
                Main.ModEntry.Logger.Error($"[ITEM-SCAN] Fatal error in PreloadAndRegisterAccessories: {ex}");
            }
        }

        private static string GetCategoryForAccessory(BlueprintItemEquipmentSimple bp)
        {
            string category = "Accessory";
            string typeName = bp.GetType().Name;
            if (bp is BlueprintItemEquipmentRing || typeName.Contains("Ring")) category = "Ring";
            else if (bp is BlueprintItemEquipmentNeck || typeName.Contains("Neck")) category = "Neck_Amulet";
            else if (bp is BlueprintItemEquipmentBelt || typeName.Contains("Belt")) category = "Belt";
            else if (bp is BlueprintItemEquipmentFeet || typeName.Contains("Feet") || typeName.Contains("Boots")) category = "Boots";
            else if (bp is BlueprintItemEquipmentGloves || typeName.Contains("Gloves") || typeName.Contains("Hand")) category = "Gloves";
            else if (bp is BlueprintItemEquipmentGlasses || typeName.Contains("Glasses") || typeName.Contains("Goggles")) category = "Glasses";
            else if (bp is BlueprintItemEquipmentHead || typeName.Contains("Head") || typeName.Contains("Helmet") || typeName.Contains("Circlet")) category = "Helmet_Headband";
            else if (bp is BlueprintItemEquipmentShoulders || typeName.Contains("Shoulders") || typeName.Contains("Cape") || typeName.Contains("Cloak")) category = "Cape";
            else if (bp is BlueprintItemEquipmentWrist || typeName.Contains("Wrist") || typeName.Contains("Bracers")) category = "Bracers";
            else if (typeName.Contains("Shirt") || typeName.Contains("Robe") || typeName.Contains("Body")) category = "Robe";
            return category;
        }

        private static int DetectLevel(string bpName, string typeName, bool isShield = false)
        {
            // Nettoyage du type : "CouvertureType" -> "Couverture"
            string baseType = typeName;
            if (typeName.EndsWith("Type", StringComparison.OrdinalIgnoreCase))
            {
                baseType = typeName.Substring(0, typeName.Length - 4);
            }

            // --- RÈGLE POUR +0 ---
            // 1. Format Standard (ex: StandardLongsword)
            // 2. Format Armure Spécifique (ex: CouvertureStandard)
            if (bpName == "Standard" + typeName || bpName == baseType + "Standard")
                return 0;

            // 3. Exception Bouclier (ex: HeavyShieldType -> HeavyShield)
            if (isShield && bpName == baseType)
                return 0;

            // --- RÈGLE POUR +1 à +5 ---

            // A. Format "StandartPlus" (avec un 't') - Spécifique aux nouvelles armures
            string standartPattern = "StandartPlus";
            if (bpName.Contains(standartPattern))
            {
                int tIdx = bpName.IndexOf(standartPattern, StringComparison.OrdinalIgnoreCase);
                string prefix = bpName.Substring(0, tIdx);
                if (prefix == baseType)
                {
                    string levelStr = bpName.Substring(tIdx + standartPattern.Length);
                    if (int.TryParse(levelStr, out int level) && level >= 1 && level <= 5)
                        return level;
                }
            }

            // B. Format "ItemPlus" - Spécifique aux boucliers (ex: TowerShieldItemPlus2)
            string itemPlusPattern = "ItemPlus";
            if (bpName.Contains(itemPlusPattern))
            {
                int iIdx = bpName.IndexOf(itemPlusPattern, StringComparison.OrdinalIgnoreCase);
                string prefix = bpName.Substring(0, iIdx);
                if (prefix == baseType)
                {
                    string levelStr = bpName.Substring(iIdx + itemPlusPattern.Length);
                    if (int.TryParse(levelStr, out int level) && level >= 1 && level <= 5)
                        return level;
                }
            }

            // C. Format classique "Plus" (ex: StandardLongswordPlus1 ou LongswordPlus1)
            string plusPattern = "Plus";
            if (bpName.Contains(plusPattern))
            {
                int pIdx = bpName.IndexOf(plusPattern, StringComparison.OrdinalIgnoreCase);
                string prefix = bpName.Substring(0, pIdx);

                if (prefix == "Standard" + typeName || prefix == typeName || prefix == baseType + "Standard")
                {
                    string levelStr = bpName.Substring(pIdx + plusPattern.Length);
                    if (int.TryParse(levelStr, out int level) && level >= 1 && level <= 5)
                        return level;
                }
            }

            return -1;
        }

        private static bool IsBaseOrEnhancementOnly(BlueprintItem bp)
        {
            if (bp == null) return false;

            // On évite les items techniques ou de l'armée
            string name = bp.name.ToLower();
            if (name.Contains("placeholder") || name.Contains("test") || name.Contains("broken") || name.Contains("internal")) return false;
            if (name.Contains("army") || name.Contains("croisade")) return false;

            if (bp.m_DisplayNameText == null || string.IsNullOrEmpty(bp.m_DisplayNameText.ToString())) return false;
            if (bp.m_Cost < 1) return false;

            return true;
        }

        private static ItemData CreateBaseData(BlueprintItem bp, string cat, string typeName)
        {
            string cleanName = bp.Name;
            // Nettoyage du suffixe +N pour le nom de base
            int plusIdx = cleanName.LastIndexOf(" +");
            if (plusIdx != -1) cleanName = cleanName.Substring(0, plusIdx);

            return new ItemData
            {
                Name = cleanName,
                BaseCost = (int)bp.m_Cost,
                Category = cat,
                Icon = bp.Icon,
                Description = bp.Description,
                TypeName = typeName
            };
        }

        private static string GenerateBlankGuid(string originalGuid)
        {
            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                byte[] inputBytes = System.Text.Encoding.UTF8.GetBytes(originalGuid);
                byte[] hashBytes = md5.ComputeHash(inputBytes);
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                for (int i = 0; i < hashBytes.Length; i++)
                {
                    sb.Append(hashBytes[i].ToString("x2"));
                }
                return "c009" + sb.ToString().Substring(4);
            }
        }

        private static void CopyFields(object source, object target)
        {
            Type type = source.GetType();
            while (type != null && type != typeof(object) && type != typeof(UnityEngine.Object))
            {
                var fields = type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly);
                foreach (var field in fields)
                {
                    try
                    {
                        field.SetValue(target, field.GetValue(source));
                    }
                    catch { }
                }
                type = type.BaseType;
            }
        }
    }

    public class AccessoryConfig
    {
        public string DisplayName { get; set; }
        public string InternalName { get; set; }
        public string Guid { get; set; }
    }
}
