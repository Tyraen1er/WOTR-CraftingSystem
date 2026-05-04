using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Items.Weapons;

namespace CraftingSystem
{
    public static class WeaponDump
    {
        public static void DumpWeapons(string filePath)
        {
            try
            {
                // On utilise les armes déjà scannées et filtrées par ItemScanner
                var weaponData = ItemScanner.Weapons;
                
                using (var writer = new StreamWriter(filePath))
                {
                    writer.WriteLine("DisplayName|InternalName|Guid|Category|AttackType|IsNatural|IsUnarmed|Cost|Icon");
                    foreach (var data in weaponData)
                    {
                        var bp = ResourcesLibrary.TryGetBlueprint<BlueprintItemWeapon>(data.Guid);
                        if (bp == null) continue;

                        string name = data.Name ?? "NULL";
                        string bpName = bp.name ?? "NULL";
                        string guid = data.Guid.ToString();
                        string cat = bp.Category.ToString();
                        string atk = bp.AttackType.ToString();
                        string nat = bp.IsNatural.ToString();
                        string una = bp.IsUnarmed.ToString();
                        string cost = bp.m_Cost.ToString();
                        string icon = (bp.Icon != null).ToString();

                        writer.WriteLine($"{name}|{bpName}|{guid}|{cat}|{atk}|{nat}|{una}|{cost}|{icon}");
                    }
                }
                Main.ModEntry.Logger.Log($"[DUMP] Weapon dump finished: {filePath} ({weaponData.Count} items)");
            }
            catch (Exception ex)
            {
                Main.ModEntry.Logger.Error($"[DUMP] Failed to dump weapons: {ex.Message}");
            }
        }
    }
}
