# Plan d'Implémentation Détaillé : Création d'Enchantements Customisés via JSON

Ce document détaille la marche à suivre pour implémenter un système de génération d'enchantements de A à Z. L'objectif est de permettre la création d'enchantements 100% fonctionnels, reconnus par la Forge, et persistants à travers les sauvegardes, sans écrire de code C# pour chaque nouvel enchantement.

---

## Phase 1 : Format des Données (Le JSON)

Pour éviter de créer une surcouche de mapping, le JSON doit refléter la structure des classes C# d'Owlcat. 

**Fichier cible :** `ModConfig/CustomEnchantments.json`

**Exemple approfondi :**
```json
[
  {
    "Guid": "12345678-ABCD-1234-ABCD-1234567890AB",
    "Name": "NomInterne_MyCustomFlaming",
    "Type": "WeaponEnchantment", 
    "EnchantNameKey": "ui_custom_flaming_name",
    "EnchantDescKey": "ui_custom_flaming_desc",
    "Prefix": "Flaming",
    "Suffix": "",
    "EnchantmentCost": 1,
    "Components": [
      {
        "$type": "Kingmaker.Blueprints.Items.Ecnchantments.WeaponEnergyDamageDice, Assembly-CSharp",
        "EnergyType": "Fire",
        "DamageDice": {
          "m_Type": "D6",
          "m_Count": 1
        }
      },
      {
        "$type": "Kingmaker.UnitLogic.FactLogic.AddInitiatorAttackWithWeaponTrigger, Assembly-CSharp",
        "OnlyHit": true,
        "Action": {
          "Actions": [
            {
              "$type": "Kingmaker.UnitLogic.Mechanics.Actions.ContextActionCastSpell, Assembly-CSharp",
              "m_Spell": "!bp_2d81362af43aeac4387a3d4fced489c3", // GUID d'un sort (Fireball)
              "DC": { "ValueType": "Simple", "Value": 15 }
            }
          ]
        }
      }
    ]
  }
]
```

**Notes importantes sur le format :**
* `"$type"` indique la classe C# exacte. `Assembly-CSharp` est la bibliothèque principale du jeu.
* `"!bp_GUID"` est une syntaxe standard chez les moddeurs (utilisée par *BlueprintCore*) pour indiquer qu'il s'agit d'une référence à un autre Blueprint et non d'une chaîne de caractères classique.

---

## ⚠️ AVERTISSEMENT CRUCIAL SUR LE DUMPER ⚠️

Le fichier JSON généré par le script `EnchantmentDumper.cs` que nous avons écrit précédemment est conçu pour être **lisible par un humain**. Il transforme des objets très complexes en texte simple (ex: les `ContextValue` ou les Enums). **Vous ne pouvez pas l'utiliser tel quel** pour fabriquer des enchantements.

Si vous injectez la structure du Dumper, le constructeur va crasher car il attend de vraies classes C#, pas des résumés textuels !

**Exemple concret avec votre `AddUnitFeatureEquipment` :**

❌ **Ce que sort le Dumper (Lisible, mais inutilisable par le jeu) :**
```json
"m_Feature": {
  "Guid": "4087b1da59fbd884caa213a554fe6c03",
  "Name": "AcidResistance10Feature",
  "Type": "BlueprintFeature",
  "NestedComponents": [ { "Type": "Acid", "$COMP_TYPE": "AddDamageResistanceEnergy" } ]
}
```

✅ **Ce que vous devez écrire dans `CustomEnchantments.json` :**
Vous devez indiquer le type de la classe et utiliser la syntaxe de référence de blueprint (`!bp_GUID`) que notre convertisseur gérera.
```json
{
  "$type": "Kingmaker.Blueprints.Items.Ecnchantments.AddUnitFeatureEquipment, Assembly-CSharp",
  "m_Feature": "!bp_4087b1da59fbd884caa213a554fe6c03"
}
```
*Note : Le jeu appliquera automatiquement le `AcidResistance10Feature` (et ses NestedComponents) car ce feature existe DÉJÀ dans la base de données du jeu sous le GUID `4087...`.*

### Et si je veux une Résistance à 50 qui n'existe pas dans le jeu ?

C'est ici que l'architecture d'Owlcat vous oblige à faire des "poupées russes".
La valeur `10` n'appartient pas à l'enchantement, elle appartient à la **Compétence** (Feature) `AcidResistance10Feature`. 
Si la compétence `AcidResistance50Feature` n'a pas été codée par les développeurs, vous ne pouvez pas juste changer un chiffre dans l'enchantement. **Vous devez fabriquer la compétence vous-même !**

Heureusement, notre système JSON vous permet de le faire. Il suffit d'ajouter une deuxième entrée dans votre JSON pour construire cette compétence "from scratch", et de la relier à votre enchantement :

```json
[
  {
    "Guid": "Votre-GUID-Pour-La-Nouvelle-Compétence-Resist-50",
    "Name": "MyCustomAcidResist50Feature",
    "Type": "Feature", 
    "Components": [
      {
        "$type": "Kingmaker.UnitLogic.FactLogic.AddDamageResistanceEnergy, Assembly-CSharp",
        "Type": "Acid",
        "Value": { "ValueType": "Simple", "Value": 50 }
      }
    ]
  },
  {
    "Guid": "Votre-GUID-Pour-Le-Nouvel-Enchantement",
    "Name": "MyCustomAcidResist50Enchantment",
    "Type": "ArmorEnchantment", 
    "Components": [
      {
        "$type": "Kingmaker.Blueprints.Items.Ecnchantments.AddUnitFeatureEquipment, Assembly-CSharp",
        "m_Feature": "!bp_Votre-GUID-Pour-La-Nouvelle-Compétence-Resist-50"
      }
    ]
  }
]
```
*(Le `CustomEnchantmentsBuilder.cs` s'occupera d'instancier un `BlueprintFeature` pour la première entrée, puis un `BlueprintArmorEnchantment` pour la deuxième, et les liera ensemble !)*

---

## Phase 2 : Le Moteur de Construction (`CustomEnchantmentsBuilder.cs`)

Cette classe lira le JSON et construira les objets C# correspondants.

### A. Configuration du Désérialiseur
L'API d'Owlcat utilise beaucoup de champs privés (`[SerializeField] private int m_Value;`). Un désérialiseur classique les ignorera. Il faut donc un `ContractResolver` spécifique :

```csharp
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System.Reflection;

public class OwlcatContractResolver : DefaultContractResolver
{
    protected override IList<JsonProperty> CreateProperties(Type type, MemberSerialization memberSerialization)
    {
        var props = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                        .Select(f => base.CreateProperty(f, memberSerialization))
                        .ToList();
        
        foreach (var p in props)
        {
            p.Writable = true;
            p.Readable = true;
        }
        return props;
    }
}
```

### B. Le processus de Build
La boucle principale de construction qui sera exécutée pour chaque enchantement du JSON :

```csharp
public static void BuildAndInjectAll()
{
    var settings = new JsonSerializerSettings {
        TypeNameHandling = TypeNameHandling.Auto,
        ContractResolver = new OwlcatContractResolver(),
        NullValueHandling = NullValueHandling.Ignore
    };

    var jsonContent = File.ReadAllText(Path.Combine(ModPath, "ModConfig", "CustomEnchantments.json"));
    var customDataList = JsonConvert.DeserializeObject<List<CustomEnchantmentData>>(jsonContent, settings);

    foreach (var data in customDataList)
    {
        // 1. Instanciation dynamique selon le type
        BlueprintScriptableObject bp;
        if (data.Type == "WeaponEnchantment") bp = ScriptableObject.CreateInstance<BlueprintWeaponEnchantment>();
        else if (data.Type == "ArmorEnchantment") bp = ScriptableObject.CreateInstance<BlueprintArmorEnchantment>();
        else if (data.Type == "Feature") bp = ScriptableObject.CreateInstance<BlueprintFeature>();
        else throw new Exception($"Type inconnu : {data.Type}");

        bp.name = data.Name;
        bp.AssetGuid = new BlueprintGuid(Guid.Parse(data.Guid));

        // 2. Gestion de l'UI et autres propriétés (Uniquement pour les enchantements et features)
        if (bp is BlueprintItemEnchantment ench)
        {
            ench.m_EnchantName = Helpers.CreateString(data.EnchantNameKey ?? data.Name+"_N", "Nom par défaut (à localiser)");
            ench.m_Description = Helpers.CreateString(data.EnchantDescKey ?? data.Name+"_D", "Description par défaut (à localiser)");
            ench.Prefix = Helpers.CreateString(data.Name + "_Prefix", data.Prefix);
            ench.Suffix = Helpers.CreateString(data.Name + "_Suffix", data.Suffix);
            ench.EnchantmentCost = data.EnchantmentCost;
            ench.ComponentsArray = data.Components.ToArray();
        }
        else if (bp is BlueprintFeature feature)
        {
            // Les features utilisent des propriétés différentes pour le nom et la description
            feature.m_DisplayName = Helpers.CreateString(data.Name + "_FName", "Nom de compétence");
            feature.m_Description = Helpers.CreateString(data.Name + "_FDesc", "Description de compétence");
            feature.ComponentsArray = data.Components.ToArray();
        }

        // 5. Initialisation interne du moteur
        foreach (var comp in bp.ComponentsArray)
        {
            comp.OwnerBlueprint = bp;
            comp.name = $"${comp.GetType().Name}${Guid.NewGuid()}";
        }

        // 6. Injection dans le cache
        ResourcesLibrary.BlueprintsCache.AddCachedBlueprint(bp.AssetGuid, bp);
        Main.ModEntry.Logger.Log($"[CUSTOM ENCHANT] Injecté : {bp.name} ({bp.AssetGuid})");
    }
}
```

---

## Phase 3 : Le Hook d'Injection (Le point de montage)

Pour que les enchantements survivent au chargement d'une partie (le moteur lisant la sauvegarde va chercher le GUID), ils doivent être présents dans la `ResourcesLibrary` **avant** que la sauvegarde ne soit traitée.

Le meilleur endroit pour injecter des Blueprints de mod est à la fin de l'initialisation du cache :

```csharp
[HarmonyPatch(typeof(Kingmaker.Blueprints.JsonSystem.BlueprintsCache), nameof(Kingmaker.Blueprints.JsonSystem.BlueprintsCache.Init))]
public static class BlueprintsCache_Init_Patch
{
    public static void Postfix()
    {
        try
        {
            CustomEnchantmentsBuilder.BuildAndInjectAll();
        }
        catch (Exception ex)
        {
            Main.ModEntry.Logger.Error($"Erreur fatale lors de l'injection des enchantements custom : {ex}");
        }
    }
}
```

---

## Phase 4 : Intégration Transparente à la Forge

Une fois le code ci-dessus exécuté, le moteur du jeu considère votre Blueprint comme officiel.
Pour que Wilcer le propose dans l'atelier, la démarche reste identique aux enchantements Vanilla :

1. Ouvrez `ModConfig/Enchantments.json`.
2. Ajoutez une nouvelle entrée avec le GUID inventé (`12345678-ABCD-1234-ABCD-1234567890AB`).
3. Définissez son prix et sa catégorie (`"Source": "Custom"` ou `"Owlcat+"`).

**Bonus de l'architecture :** `CraftingCalculator` lira `bp.EnchantmentCost` que nous avons paramétré à l'étape 2, et le calcul du coût marginal fonctionnera parfaitement.

---

## Le Chaînon Manquant : Gérer les BlueprintReferences (Le secret d'Owlcat)

C'est la principale difficulté pour un développeur C# qui ne connaît pas WOTR. Les composants font sans arrêt référence à d'autres Blueprints (ex: un sort) via des types comme `BlueprintSpellReference` ou `AnyBlueprintReference`. 

Pour que notre JSON puisse utiliser la syntaxe magique `"!bp_GUID"` ou `"GUID"`, **il faut fournir ce convertisseur à Newtonsoft**. Copiez-collez cette classe dans votre projet :

```csharp
using Newtonsoft.Json;
using Kingmaker.Blueprints;
using System;

public class BlueprintReferenceConverter : JsonConverter
{
    // Ce convertisseur s'applique à toute classe qui hérite de BlueprintReferenceBase
    public override bool CanConvert(Type objectType)
    {
        return typeof(BlueprintReferenceBase).IsAssignableFrom(objectType);
    }

    public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null) return null;
        
        string guidString = reader.Value as string;
        if (string.IsNullOrEmpty(guidString)) return null;

        // Nettoyage de la syntaxe magique "!bp_" (si utilisée)
        if (guidString.StartsWith("!bp_")) 
            guidString = guidString.Substring(4);

        // Instanciation magique par réflexion : 
        // On crée l'instance du bon type (ex: BlueprintSpellReference)
        var reference = Activator.CreateInstance(objectType) as BlueprintReferenceBase;
        
        // On lui injecte l'ID
        var guid = BlueprintGuid.Parse(guidString);
        
        // Dans WOTR, la propriété AssetGuid n'a pas de setter public direct, on utilise la méthode interne de la librairie ou un champ
        // Méthode la plus sûre via la classe parente :
        var field = typeof(BlueprintReferenceBase).GetField("guid", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if (field != null) field.SetValue(reference, guid.ToString());

        return reference;
    }

    public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
    {
        var reference = value as BlueprintReferenceBase;
        if (reference == null || reference.IsEmpty()) writer.WriteNull();
        else writer.WriteValue($"!bp_{reference.deserializedGuid}");
    }
}
```

**Mise à jour du Builder (`CustomEnchantmentsBuilder.cs`) :**
Il suffit d'ajouter ce convertisseur dans les réglages vus à la Phase 2 :
```csharp
    var settings = new JsonSerializerSettings {
        TypeNameHandling = TypeNameHandling.Auto,
        ContractResolver = new OwlcatContractResolver(),
        NullValueHandling = NullValueHandling.Ignore
    };
    // AJOUT VITAL POUR WOTR :
    settings.Converters.Add(new BlueprintReferenceConverter());
```

Avec ce convertisseur, n'importe quel développeur C# classique pourra lire et appliquer le plan sans jamais trébucher sur l'architecture obscure d'Owlcat !

---

## Foire Aux Questions (FAQ)

### Comment trouver le `$type` exact (avec le namespace) d'un composant ?
Dans les exemples ci-dessus, le `$type` nécessite le chemin complet de la classe (`Kingmaker.UnitLogic.FactLogic.AddDamageResistanceEnergy`) suivi de l'assembly (`Assembly-CSharp`). Comment le deviner quand on a que le nom `AddDamageResistanceEnergy` ?

Vous avez deux méthodes :

**Méthode 1 : L'approche "Pro" (Recommandée pour l'exploration)**
Utilisez un décompilateur gratuit comme **dnSpy** ou **dotPeek**. Ouvrez le fichier `Assembly-CSharp.dll` (qui se trouve dans `Wrath_Data/Managed/` dans le dossier du jeu). Utilisez la fonction de recherche sur `AddDamageResistanceEnergy` et le logiciel vous donnera son "Namespace" exact.

**Méthode 2 : L'approche "Astucieuse" (Modifier votre Dumper)**
Puisque vous avez déjà un `EnchantmentDumper.cs` fonctionnel, modifiez-le pour qu'il vous donne directement la réponse !
Dans le fichier `EnchantmentDumper.cs`, trouvez la ligne où vous définissez le type de composant :
```csharp
ndata["$COMP_TYPE"] = nComp.GetType().Name; 
```
Et remplacez-la par :
```csharp
ndata["$type"] = nComp.GetType().FullName + ", " + nComp.GetType().Assembly.GetName().Name;
```
Ainsi, la prochaine fois que vous extrairez les données du jeu avec votre Dumper, il écrira automatiquement la chaîne de caractères exacte que vous n'aurez plus qu'à copier-coller dans votre `CustomEnchantments.json` !
