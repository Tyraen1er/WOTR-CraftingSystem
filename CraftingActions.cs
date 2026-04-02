using System;
using System.Linq;
using Kingmaker.ElementsSystem;
using Kingmaker.Blueprints.JsonSystem;
using Kingmaker.PubSubSystem; // C'est ici que se cache le vrai nom du déclencheur

namespace CraftingSystem
{
    [TypeId("6d3e1f7d4e3347bdaeb88a1b6c8baab6")]
    public class OpenItemSelectorAction : GameAction
    {
        public override string GetCaption() 
        { 
            return "Recherche de l'EventBus UI"; 
        }

        public override void RunAction()
        {
            try
            {
                Main.ModEntry.Logger.Log("Recherche de l'interface EventBus...");
                
                // On fouille UNIQUEMENT dans le système d'événements
                var types = typeof(EventBus).Assembly.GetTypes()
                    .Where(t => t.IsInterface && t.Namespace != null && t.Namespace.Contains("PubSubSystem"))
                    .OrderBy(t => t.Name);

                foreach (var t in types)
                {
                    if (t.Name.Contains("Insert") || t.Name.Contains("Item") || t.Name.Contains("Slot") || t.Name.Contains("Puzzle") || t.Name.Contains("UI") || t.Name.Contains("Interact"))
                    {
                        var methods = t.GetMethods();
                        var methodStrings = string.Join(" | ", methods.Select(m => m.Name));
                        Main.ModEntry.Logger.Log($"[INTERFACE] {t.Name} -> Méthodes: {methodStrings}");
                    }
                }
                
                Main.ModEntry.Logger.Log("Fin de la recherche.");
            }
            catch (Exception ex) 
            { 
                Main.ModEntry.Logger.Error($"Erreur : {ex}"); 
            }
        }
    }
}