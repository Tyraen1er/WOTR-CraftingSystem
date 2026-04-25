using System;
using System.Reflection;
using System.Linq;

class Program {
    static void Main() {
        try {
            var dll = Assembly.LoadFrom(@"C:\Program Files (x86)\Steam\steamapps\common\Pathfinder Second Adventure\Wrath_Data\Managed\Assembly-CSharp.dll");
            var interfaceType = dll.GetType("Kingmaker.PubSubSystem.ILootInterractionHandler");
            if (interfaceType != null) {
                foreach (var method in interfaceType.GetMethods()) {
                    Console.WriteLine("Method: " + method.Name);
                    foreach (var param in method.GetParameters()) {
                        Console.WriteLine("  Param: " + param.ParameterType.Name + " " + param.Name);
                    }
                }
            } else {
                Console.WriteLine("Interface not found.");
            }
            Console.WriteLine("----");
            var methods = dll.GetTypes().Where(t => t.Name.Contains("Loot") || t.Name.Contains("Stash")).Where(t => t.IsInterface).ToList();
            foreach (var t in methods) {
                Console.WriteLine("Interface: " + t.Name);
                foreach(var m in t.GetMethods()) Console.WriteLine("  - " + m.Name);
            }
        } catch (Exception ex) {
            Console.WriteLine(ex);
        }
    }
}
