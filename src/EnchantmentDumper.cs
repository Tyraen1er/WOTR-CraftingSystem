using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Items.Ecnchantments;
using Kingmaker.Blueprints.Items.Weapons;
using Kingmaker.Blueprints.Items.Armors;

namespace CraftingSystem
{
    public static class EnchantmentDumper
    {
        public class DumpedEnchantment
        {
            [JsonProperty("GUID")]
            public string Guid;
            public string Name;
            public string name;
            public string Description;
            public string generatedDescription;
            public List<Dictionary<string, object>> Components;
        }

        public static void DumpAll()
        {
            try
            {
                Main.ModEntry.Logger.Log("[DUMPER] Lancement de l'extraction des enchantements...");
                
                var dumpList = new List<DumpedEnchantment>();
                
                // On récupère le cache global
                var bpCache = ResourcesLibrary.BlueprintsCache;
                if (bpCache == null) return;

                // CORRECTION : On ajoute .ToList() pour créer une copie des clés
                // On peut aussi ajouter un lock pour être 100% aligné avec ton Scanner
                lock (bpCache.m_Lock) 
                {
                    var keysSnapshot = bpCache.m_LoadedBlueprints.Keys.ToList();
                    
                    foreach (var guid in keysSnapshot)
                    {
                        var bp = ResourcesLibrary.TryGetBlueprint(guid) as BlueprintItemEnchantment;
                        
                        if (bp != null)
                        {

                             var components = new List<Dictionary<string, object>>();
                             var enumerable = GetComponentsFromBlueprint(bp);
                             if (enumerable != null)
                             {
                                 foreach (var comp in enumerable)
                                 {
                                     if (comp == null) continue;
                                     var compData = ExtractComponentData(comp);
                                     compData["$COMP_TYPE"] = comp.GetType().Name;
                                     components.Add(compData);
                                 }
                             }

                             dumpList.Add(new DumpedEnchantment
                             {
                                 Guid = bp.AssetGuid.ToString(), 
                                 Name = EnchantmentDescriptionGenerator.GetBlueprintDisplayName(bp),
                                 name = bp.name,
                                 Description = bp.m_Description?.ToString() ?? "",
                                 generatedDescription = EnchantmentDescriptionGenerator.Generate(bp) ?? "",
                                 Components = components
                             });
                         }
                    }
                }

                string filePath = Path.Combine(Main.ModEntry.Path, "Enchantments_Dump.json");
                string json = JsonConvert.SerializeObject(dumpList, Formatting.Indented);
                File.WriteAllText(filePath, json);

                Main.ModEntry.Logger.Log($"[DUMPER] Succès ! {dumpList.Count} enchantements extraits.");
            }
            catch (Exception ex)
            {
                Main.ModEntry.Logger.Error($"[DUMPER] Erreur : {ex}");
            }
        }

        public static void DumpAllUniqueComponents()
        {
            try
            {
                Main.ModEntry.Logger.Log("[DUMPER] Mass extraction of unique components starting...");

                var uniqueComponents = new Dictionary<string, List<Dictionary<string, object>>>();
                var seenFingerprints = new HashSet<string>();

                var bpCache = ResourcesLibrary.BlueprintsCache;
                if (bpCache == null) return;

                List<string> keysSnapshot;
                lock (EnchantmentScanner.MasterList)
                {
                    keysSnapshot = EnchantmentScanner.MasterList.Select(e => e.Guid).ToList();
                }

                foreach (var guidStr in keysSnapshot)
                {
                    BlueprintGuid guid;
                    try { guid = BlueprintGuid.Parse(guidStr); } catch { continue; }
                    var bp = ResourcesLibrary.TryGetBlueprint(guid) as BlueprintItemEnchantment;
                    if (bp == null) continue;

                    var enumerable = GetComponentsFromBlueprint(bp);

                    if (enumerable != null)
                    {
                        foreach (var comp in enumerable)
                        {
                            if (comp == null) continue;

                            string compType = comp.GetType().Name;
                            var compData = ExtractComponentData(comp);

                            
                            // Création de l'empreinte pour déduplication
                            string fingerprint = compType + ":" + JsonConvert.SerializeObject(compData);

                            if (!seenFingerprints.Contains(fingerprint))
                            {
                                seenFingerprints.Add(fingerprint);
                                
                                if (!uniqueComponents.ContainsKey(compType))
                                {
                                    uniqueComponents[compType] = new List<Dictionary<string, object>>();
                                }
                                uniqueComponents[compType].Add(compData);
                            }
                        }
                    }
                }

                string filePath = Path.Combine(Main.ModEntry.Path, "Unique_Enchantment_Components.json");
                string json = JsonConvert.SerializeObject(uniqueComponents, Formatting.Indented);
                File.WriteAllText(filePath, json);

                Main.ModEntry.Logger.Log($"[DUMPER] Mass extraction finished! Found {seenFingerprints.Count} unique component configurations across {uniqueComponents.Keys.Count} component types.");
            }
            catch (Exception ex)
            {
                Main.ModEntry.Logger.Error($"[DUMPER] Mass extraction error: {ex}");
            }
        }

        private static System.Collections.IEnumerable GetComponentsFromBlueprint(SimpleBlueprint bp)
        {
            if (bp == null) return null;
            object components = null;
            Type currentType = bp.GetType();

            while (currentType != null && currentType != typeof(object))
            {
                var field = currentType.GetField("Components", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                        ?? currentType.GetField("m_Components", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                        ?? currentType.GetField("ComponentsArray", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                if (field != null)
                {
                    components = field.GetValue(bp);
                    if (components != null) break;
                }
                currentType = currentType.BaseType;
            }

            if (components == null)
            {
                var componentsProp = bp.GetType().GetProperty("ComponentsArray", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (componentsProp != null) components = componentsProp.GetValue(bp);
            }

            return components as System.Collections.IEnumerable;
        }

        private static Dictionary<string, object> ExtractComponentData(object comp, int depth = 0, int maxDepth = 1)
        {
            var data = new Dictionary<string, object>();
            Type type = comp.GetType();
            HashSet<string> seenFields = new HashSet<string>();

            while (type != null && type != typeof(object) && type != typeof(UnityEngine.Object))
            {
                var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly;
                foreach (var field in type.GetFields(flags))
                {
                    if (seenFields.Contains(field.Name)) continue;
                    seenFields.Add(field.Name);

                    try
                    {
                        object val = field.GetValue(comp);
                        if (val == null)
                        {
                            data[field.Name] = null;
                        }
                        else if (val is BlueprintReferenceBase bpRef)
                        {
                            var referred = bpRef.GetBlueprint();
                            if (referred != null && depth < maxDepth)
                            {
                                var nestedList = new List<Dictionary<string, object>>();
                                var nestedComps = GetComponentsFromBlueprint(referred);
                                if (nestedComps != null)
                                {
                                    foreach (var nComp in nestedComps)
                                    {
                                        if (nComp == null) continue;
                                        var ndata = ExtractComponentData(nComp, depth + 1, maxDepth);
                                        ndata["$COMP_TYPE"] = nComp.GetType().Name; 
                                        nestedList.Add(ndata);
                                    }
                                }
                                data[field.Name] = new { 
                                    Guid = bpRef.Guid.ToString(), 
                                    Name = referred.name, 
                                    Type = referred.GetType().Name,
                                    NestedComponents = nestedList 
                                };
                            }
                            else
                            {
                                data[field.Name] = new { Guid = bpRef.Guid.ToString(), Name = referred != null ? referred.name : "Unresolved" };
                            }
                        }
                        else if (val.GetType().IsEnum)
                        {
                            data[field.Name] = $"{val.ToString()} ({(int)val})";
                        }
                        else if (val.GetType() == typeof(Kingmaker.UnitLogic.Mechanics.ContextValue))
                        {
                            var ctxVal = (Kingmaker.UnitLogic.Mechanics.ContextValue)val;
                            if (ctxVal.ValueType == Kingmaker.UnitLogic.Mechanics.ContextValueType.Simple)
                            {
                                data[field.Name] = $"ContextValue [Simple]: {ctxVal.Value}";
                            }
                            else
                            {
                                data[field.Name] = $"ContextValue [DynamicType: {ctxVal.ValueType}]";
                            }
                        }
                        else if (val.GetType().IsPrimitive || val is string)
                        {
                            data[field.Name] = val;
                        }
                        else if (val is System.Collections.IEnumerable enumerable && !(val is string))
                        {
                            var list = new List<object>();
                            int count = 0;
                            foreach (var item in enumerable)
                            {
                                if (count++ > 50) { list.Add("... (truncated)"); break; }
                                if (item == null) list.Add(null);
                                else if (item.GetType().IsPrimitive || item is string) list.Add(item);
                                else if (item.GetType().IsEnum) list.Add($"{item.ToString()} ({(int)item})");
                                else if (item is BlueprintReferenceBase bpRef2) list.Add(new { Guid = bpRef2.Guid.ToString() });
                                else if (depth < maxDepth + 1) // Recurse slightly for elements in lists
                                {
                                    var itemData = ExtractComponentData(item, depth + 1, maxDepth + 1);
                                    itemData["$TYPE"] = item.GetType().Name;
                                    list.Add(itemData);
                                }
                                else list.Add(item.GetType().Name);
                            }
                            data[field.Name] = list;
                        }
                        else if (depth < maxDepth && val != null && !val.GetType().IsPrimitive && !val.GetType().IsEnum && (val.GetType().FullName?.StartsWith("Kingmaker") == true))
                        {
                            var subType = val.GetType();
                            var subData = ExtractComponentData(val, depth + 1, maxDepth);
                            subData["$TYPE"] = subType.Name;
                            data[field.Name] = subData;
                        }
                        else if (val is Kingmaker.Localization.LocalizedString locString)
                        {
                            data[field.Name] = locString.Key;
                        }
                    }
                    catch
                    {
                        data[field.Name] = "<Error reading>";
                    }
                }
                type = type.BaseType;
            }
            return data;
        }
    }
}