using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using Kingmaker;
using Kingmaker.ElementsSystem;
using Kingmaker.Items;
using Kingmaker.Blueprints.JsonSystem;

namespace CraftingSystem
{
    [TypeId("6d3e1f7d4e3347bdaeb88a1b6c8baab6")]
    public class OpenItemSelectorAction : GameAction
    {
        public override string GetCaption()
        {
            return "Advanced UI Discovery (Slab/InsertItem Hunter)";
        }

        public override void RunAction()
        {
            try
            {
                Main.ModEntry.Logger.Log("OpenItemSelectorAction: Starting ADVANCED UI HUNTER SCAN...");

                var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                // BindingFlags pour récupérer absolument TOUT (public, privé, instance, statique)
                var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

                foreach (var a in assemblies) 
                {
                    try 
                    {
                        Type[] types;
                        try { types = a.GetTypes(); } 
                        catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }

                        foreach (var t in types) 
                        {
                            var n = t.Name;
                            
                            // Ciblage des mots-clés d'interfaces d'insertion
                            if (n.Contains("Puzzle") || n.Contains("Slab") || n.Contains("InsertItem") || n.Contains("Slot") || n.Contains("Selector")) 
                            {
                                // Filtre par namespace
                                if (t.FullName.Contains("Kingmaker.UI") || t.FullName.Contains("Kingmaker.Designers")) 
                                {
                                    string baseType = t.BaseType != null ? t.BaseType.Name : "None";
                                    Main.ModEntry.Logger.Log($"\n===============================================");
                                    Main.ModEntry.Logger.Log($"[CLASSE] {t.FullName} (Hérite de : {baseType})");
                                    Main.ModEntry.Logger.Log($"===============================================");

                                    // 1. Scan des CHAMPS (Variables globales de la classe)
                                    var fields = t.GetFields(flags);
                                    if (fields.Length > 0)
                                    {
                                        Main.ModEntry.Logger.Log("  --- CHAMPS (Fields) ---");
                                        foreach (var f in fields)
                                        {
                                            Main.ModEntry.Logger.Log($"  ├─ [Champ] {f.FieldType.Name} {f.Name}");
                                        }
                                    }

                                    // 2. Scan des PROPRIÉTÉS (Getters/Setters, très utilisés par l'UI réactive)
                                    var properties = t.GetProperties(flags);
                                    if (properties.Length > 0)
                                    {
                                        Main.ModEntry.Logger.Log("  --- PROPRIÉTÉS (Properties) ---");
                                        foreach (var p in properties)
                                        {
                                            Main.ModEntry.Logger.Log($"  ├─ [Propriété] {p.PropertyType.Name} {p.Name}");
                                        }
                                    }

                                    // 3. Scan des MÉTHODES (Fonctions et actions)
                                    var methods = t.GetMethods(flags);
                                    // On filtre les getters/setters générés automatiquement pour éviter de polluer le log
                                    var realMethods = methods.Where(m => !m.IsSpecialName).ToArray(); 
                                    
                                    if (realMethods.Length > 0)
                                    {
                                        Main.ModEntry.Logger.Log("  --- MÉTHODES (Methods) ---");
                                        foreach (var m in realMethods) 
                                        {
                                            var pars = m.GetParameters();
                                            var pStr = string.Join(", ", pars.Select(p => $"{p.ParameterType.Name} {p.Name}"));
                                            Main.ModEntry.Logger.Log($"  ├─ [Méthode] {m.ReturnType.Name} {m.Name}({pStr})");
                                        }
                                    }
                                }
                            }
                        }
                    } 
                    catch { }
                }

                Main.ModEntry.Logger.Log("\nADVANCED UI HUNTER SCAN Complete. Please check logs.");
            }
            catch (Exception ex)
            {
                Main.ModEntry.Logger.Error($"OpenItemSelectorAction Advanced Hunter Error: {ex}");
            }
        }
    }
}