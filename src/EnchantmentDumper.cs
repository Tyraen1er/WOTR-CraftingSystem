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
            public string Guid;
            public string Name;
            public string Type;
            public string Comment;
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
                            string type = "Other";
                            if (bp is BlueprintWeaponEnchantment) type = "Weapon";
                            else if (bp is BlueprintArmorEnchantment) type = "Armor";

                            dumpList.Add(new DumpedEnchantment
                            {
                                Guid = bp.AssetGuid.ToString(), 
                                Name = bp.name,
                                Type = type,
                                Comment = bp.Comment
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
    }
}