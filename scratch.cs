using System;
using System.Reflection;
using System.Linq;

class Program
{
    static void Main()
    {
        var asm = Assembly.LoadFrom(@"C:\Program Files (x86)\Steam\steamapps\common\Pathfinder Second Adventure\Wrath_Data\Managed\Assembly-CSharp.dll");
        var type = asm.GetType("Kingmaker.Blueprints.Items.Equipment.BlueprintItemEquipmentUsable");
        foreach(var f in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            Console.WriteLine(f.Name + " : " + f.FieldType.Name);
        }
    }
}
