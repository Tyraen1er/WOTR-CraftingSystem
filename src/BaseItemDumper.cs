using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Items;
using Kingmaker.Blueprints.Items.Weapons;
using Kingmaker.Blueprints.Items.Armors;
using Kingmaker.Blueprints.Items.Shields;
using Kingmaker.Blueprints.Items.Equipment;

namespace CraftingSystem
{
    public static class BaseItemDumper
    {
        /// <summary>
        /// Version synchrone appelée directement depuis Main.cs (comme EnchantmentDumper).
        /// </summary>
        public static void DumpAll()
        {
            try
            {
                Main.ModEntry.Logger.Log("[DUMP] Début du dumpage multiple (Wilcer + Standard)...");
                
                List<BlueprintGuid> allGuids;
                lock (ResourcesLibrary.BlueprintsCache.m_Lock)
                {
                    allGuids = ResourcesLibrary.BlueprintsCache.m_LoadedBlueprints.Keys.ToList();
                }
                
                var wilcerAccessories = new List<BlueprintItem>();
                var allAccessories = new List<BlueprintItem>();

                foreach (var guid in allGuids)
                {
                    var bp = ResourcesLibrary.TryGetBlueprint(guid);
                    if (bp == null) continue;

                    if (bp is BlueprintItemEquipment acc && 
                        !(bp is BlueprintItemWeapon) && 
                        !(bp is BlueprintItemArmor) && 
                        !(bp is BlueprintItemShield))
                    {
                        if (IsWilcerBaseItem(acc)) wilcerAccessories.Add(acc);
                        allAccessories.Add(acc);
                    }
                }

                ExecuteDump(wilcerAccessories, "WilcerAccessories.csv");
                ExecuteDump(allAccessories, "AllAccessoriesDump.csv");
            }
            catch (Exception ex)
            {
                Main.ModEntry.Logger.Error($"[DUMP] Erreur lors du scan multiple : {ex}");
            }
        }

        private static bool IsWilcerBaseItem(BlueprintItem bp)
        {
            if (bp == null) return false;

            // 1. Sécurité Type, Taille & Coût
            if (bp is BlueprintItemWeapon weapon)
            {
                if (weapon.Type == null) return false;
                if (weapon.Size != Kingmaker.Enums.Size.Medium) return false;
            }
            if (bp is BlueprintItemArmor armor)
            {
                if (armor.Type == null) return false;
                if (armor.Size != Kingmaker.Enums.Size.Medium) return false;
            }
            if (bp is BlueprintItemShield shield)
            {
                if (shield.Type == null) return false;
            }
            
            if (bp.Cost <= 0) return false;

            // 2. Validation du nom (ne doit pas être vide ou nul)
            string name = bp.Name;
            string nonIdName = bp.NonIdentifiedName;
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(nonIdName)) return false;

            // 3. Identité Nommé vs Non-Identifié
            if (name != nonIdName) return false;

            // 4. Enchantements vides
            if (bp.Enchantments != null && bp.Enchantments.Count > 0) return false;

            // 5. Pas d'armes naturelles
            if (bp is BlueprintItemWeapon w && w.IsNatural) return false;

            // 6. Pas d'overrides (Reflexion sur champs privés)
            if (HasAnyOverride(bp)) return false;

            return true;
        }

        private static bool HasAnyOverride(object bp)
        {
            if (bp == null) return false;
            var type = bp.GetType();
            
            string[] overridesToCheck = { 
                "m_OverrideDamageDice", 
                "m_OverrideDamageType", 
                "m_OverrideDestructible", 
                "m_OverrideShardItem" 
            };

            foreach (var name in overridesToCheck)
            {
                var field = type.GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (field != null)
                {
                    var val = field.GetValue(bp);
                    if (val is bool b && b) return true;
                }
            }
            return false;
        }

        private static void ExecuteDump(List<BlueprintItem> allItems, string fileName)
        {
            try
            {
                string folder = Path.Combine(Main.ModEntry.Path, "Dumps");
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                string filePath = Path.Combine(folder, fileName);

                if (allItems.Count == 0) { Main.ModEntry.Logger.Log($"[DUMP] Aucun item trouvé pour {fileName}."); return; }

                // Collecter TOUTES les propriétés uniques de TOUS les types présents dans la liste
                var allTypes = allItems.Select(x => x.GetType()).Distinct().ToList();
                var membersMap = new Dictionary<string, System.Reflection.MemberInfo>();
                foreach (var type in allTypes)
                {
                    foreach (var m in GetInspectableMembers(type))
                    {
                        if (!membersMap.ContainsKey(m.Name))
                            membersMap[m.Name] = m;
                    }
                }
                var sortedMembers = membersMap.Values.OrderBy(m => m.Name).ToList();

                using (var writer = new StreamWriter(filePath, false))
                {
                    writer.WriteLine("Category|" + string.Join("|", sortedMembers.Select(m => m.Name)));

                    foreach (var bp in allItems)
                    {
                        string category = "Accessory";
                        if (bp is BlueprintItemEquipmentRing) category = "Ring";
                        else if (bp is BlueprintItemEquipmentNeck) category = "Neck/Amulet";
                        else if (bp is BlueprintItemEquipmentBelt) category = "Belt";
                        else if (bp is BlueprintItemEquipmentFeet) category = "Boots";
                        else if (bp is BlueprintItemEquipmentGloves) category = "Gloves";
                        else if (bp is BlueprintItemEquipmentHead) category = "Helmet/Headband";
                        else if (bp is BlueprintItemEquipmentShoulders) category = "Cape";
                        else if (bp is BlueprintItemEquipmentWrist) category = "Bracers";

                        var values = new List<string> { category };
                        foreach (var member in sortedMembers)
                        {
                            // On vérifie si le membre appartient au type de l'objet (ou un de ses parents)
                            object val = null;
                            if (member.DeclaringType.IsAssignableFrom(bp.GetType()))
                            {
                                val = GetMemberValue(member, bp);
                            }
                            values.Add(FormatValue(val));
                        }
                        writer.WriteLine(string.Join("|", values));
                    }
                }

                Main.ModEntry.Logger.Log($"[DUMP] Succès ! {allItems.Count} items dumpés dans {fileName}");
            }
            catch (Exception ex)
            {
                Main.ModEntry.Logger.Error($"[DUMP] Erreur pendant l'écriture CSV ({fileName}) : {ex}");
            }
        }

        private static List<System.Reflection.MemberInfo> GetInspectableMembers(Type type)
        {
            var list = new List<System.Reflection.MemberInfo>();
            var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            
            list.AddRange(type.GetFields(flags).Where(f => !f.Name.Contains("BackingField")));
            list.AddRange(type.GetProperties(flags).Where(p => p.CanRead));
            
            return list.OrderBy(m => m.Name).ToList();
        }

        private static object GetMemberValue(System.Reflection.MemberInfo member, object obj)
        {
            try
            {
                if (member is System.Reflection.FieldInfo f) return f.GetValue(obj);
                if (member is System.Reflection.PropertyInfo p) return p.GetValue(obj);
            }
            catch (Exception ex)
            {
                return $"ERROR:{ex.Message}";
            }
            return null;
        }

        private static string FormatValue(object val)
        {
            if (val == null) return "null";
            if (val is string sErr && sErr.StartsWith("ERROR:")) return sErr;
            
            if (val is System.Collections.IEnumerable en && !(val is string))
            {
                var items = new List<string>();
                int count = 0;
                foreach (var i in en)
                {
                    if (count++ > 10) { items.Add("..."); break; }
                    items.Add(i?.ToString() ?? "null");
                }
                return "[" + string.Join(";", items) + "]";
            }

            if (val.GetType().Name.Contains("Reference")) return val.ToString();

            string s = val.ToString().Replace("\n", " ").Replace("\r", " ").Replace("|", "/");
            return s.Length > 200 ? s.Substring(0, 197) + "..." : s;
        }
    }
}
