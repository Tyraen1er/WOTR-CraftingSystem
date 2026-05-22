using System;
using System.Collections.Generic;
using Kingmaker;
using Kingmaker.ElementsSystem;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.JsonSystem;
using Kingmaker.PubSubSystem;
using Kingmaker.DialogSystem.Blueprints;
using Kingmaker.View;
using Kingmaker.View.MapObjects;
using Kingmaker.Items;
using Kingmaker.UnitLogic; 
using Kingmaker.EntitySystem;
using Kingmaker.Blueprints.Items.Weapons;
using Kingmaker.Blueprints.Items.Armors;
using Kingmaker.Blueprints.Items.Equipment;
using Kingmaker.Blueprints.Items.Ecnchantments;
using Kingmaker.Blueprints.Items;
using Newtonsoft.Json; 
using UniRx;
using System.Linq;
using Kingmaker.UI.Models.Log;
using Kingmaker.Blueprints.Root;
using HarmonyLib;

namespace CraftingSystem
{
    public class CraftingProject
    {
        [JsonProperty]
        public ItemEntity Item; 
        [JsonProperty]
        public string ItemId;
        [JsonProperty]
        public string EnchantmentGuid;
        [JsonProperty]
        public long FinishTimeTicks; 
        [JsonProperty]
        public int GoldPaid;
    }

    public static class CraftingActions
    {
        public static void StartCraftingProject(ItemEntity item, EnchantmentData data, int cost, int days)
        {
            if (data == null) return;
            
            // Redirection pour les boucliers : les enchantements d'arme vont sur l'arme du bouclier
            if (item != null && item.Blueprint is Kingmaker.Blueprints.Items.Shields.BlueprintItemShield && data.Type == "Weapon")
            {
                var weaponProp = item.GetType().GetProperty("WeaponComponent", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (weaponProp != null)
                {
                    var subWeapon = weaponProp.GetValue(item) as ItemEntityWeapon;
                    if (subWeapon != null)
                    {
                        item = subWeapon;
                        Main.ModEntry.Logger.Log($"[ATELIER] Redirection (via réflexion) vers l'arme du bouclier : {item.Name}");
                    }
                }
            }
            // Redirection pour les boucliers : les enchantements d'armure vont sur l'armure du bouclier
            else if (item != null && item.Blueprint is Kingmaker.Blueprints.Items.Shields.BlueprintItemShield && data.Type == "Armor")
            {
                var armorProp = item.GetType().GetProperty("ArmorComponent", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (armorProp != null)
                {
                    var subArmor = armorProp.GetValue(item) as ItemEntityArmor;
                    if (subArmor != null)
                    {
                        item = subArmor;
                        Main.ModEntry.Logger.Log($"[ATELIER] Redirection (via réflexion) vers l'armure du bouclier : {item.Name}");
                    }
                }
            }

            // 1. Paiement immédiat (avec vérification de sécurité)
            if (Game.Instance.Player.Money < cost)
            {
                Main.ModEntry.Logger.Error($"[ATELIER] Erreur critique: Tentative de retrait de {cost} PO alors que le joueur n'en a que {Game.Instance.Player.Money}.");
                return;
            }
            Game.Instance.Player.Money -= cost;

            BlueprintScriptableObject bp = data.Blueprint;
            if (bp == null) return;

            // 2. CAS PARTICULIER : INSTANTANÉ (0 JOURS)
            if (days <= 0)
            {
                if (item != null && bp is BlueprintItemEnchantment bpEnch)
                {
                    UnitPartWilcerWorkshop.ApplyEnchantmentsafely(item, bpEnch);
                    Main.ModEntry.Logger.Log($"[ATELIER] Application immédiate de {data.Name} sur {item.Name}.");
                }
                else if (bp is BlueprintItem bpItem)
                {
                    var newItem = bpItem.CreateEntity();
                    DeferredInventoryOpener.CraftingBox.Add(newItem);
                    Game.Instance.Player.MainCharacter.Value.Ensure<UnitPartWilcerWorkshop>().SyncFromBox();
                    Main.ModEntry.Logger.Log($"[ATELIER] Création immédiate et livraison dans le coffre de {newItem.Name}.");
                }
                return;
            }

            // 3. MISE EN FILE D'ATTENTE (PROJET)
            long ticksPerDay = TimeSpan.TicksPerDay; 
            long finishTime = Game.Instance.Player.GameTime.Ticks + ((long)days * ticksPerDay);

            var project = new CraftingProject
            {
                Item = item,
                ItemId = item?.UniqueId,
                EnchantmentGuid = data.Guid,
                FinishTimeTicks = finishTime,
                GoldPaid = cost
            };

            var workshopMain = Game.Instance.Player.MainCharacter.Value.Ensure<UnitPartWilcerWorkshop>();
            workshopMain.ActiveProjects.Add(project);
            Main.ModEntry.Logger.Log($"[ATELIER] Lancement du craft pour {(item != null ? item.Name : data.Name)}.");
        }
    }

    public class UnitPartWilcerWorkshop : UnitPart
    {
        [JsonProperty]
        public List<ItemEntity> StashedItems = new List<ItemEntity>();

        [JsonProperty]
        public HashSet<string> StashedItemIds = new HashSet<string>();

        [JsonProperty]
        public List<CraftingProject> ActiveProjects = new List<CraftingProject>();

        private ItemsCollection _virtualBox;
        private bool _isBoxActive = false;
        private bool _isSyncing = false;

        public ItemsCollection VirtualBox => _virtualBox;

        public ItemsCollection GetBox()
        {
            if (_virtualBox == null)
            {
                _virtualBox = new ItemsCollection();
            }

            if (_isBoxActive || _isSyncing)
            {
                return _virtualBox;
            }

            Main.ModEntry.Logger.Log($"[new-inventory] GetBox() appelé. _isBoxActive={_isBoxActive}, StashedItemIds count={StashedItemIds.Count}");

            var playerInv = Game.Instance?.Player?.Inventory;
            if (playerInv != null)
            {
                var toMove = playerInv.Items.Where(item => item != null && StashedItemIds.Contains(item.UniqueId)).ToList();
                foreach (var item in toMove)
                {
                    Main.ModEntry.Logger.Log($"[new-inventory] GetBox(): Déplacement de l'item {item.Name} (ID: {item.UniqueId}) de l'inventaire du joueur vers _virtualBox.");
                    playerInv.Remove(item);
                    _virtualBox.Add(item);
                }

                if (StashedItemIds.Count > 0 && toMove.Count == 0)
                {
                    Main.ModEntry.Logger.Log($"[new-inventory-debug] GetBox(): StashedItemIds has {StashedItemIds.Count} entries but toMove is empty. Listing player inventory items:");
                    foreach (var item in playerInv.Items)
                    {
                        if (item != null)
                        {
                            Main.ModEntry.Logger.Log($"  - Inventory item: {item.Name}, ID: {item.UniqueId}, Collection: {item.Collection?.GetType().Name ?? "null"}");
                        }
                    }
                    Main.ModEntry.Logger.Log($"[new-inventory-debug] Listing expected StashedItemIds:");
                    foreach (var id in StashedItemIds)
                    {
                        Main.ModEntry.Logger.Log($"  - expected ID: {id}");
                    }
                }
            }

            _isBoxActive = true;
            Main.ModEntry.Logger.Log($"[new-inventory] GetBox() terminé. _virtualBox contient désormais {_virtualBox.Items.Count()} items. _isBoxActive passé à true.");
            return _virtualBox;
        }

        public void SyncFromBox()
        {
            Main.ModEntry.Logger.Log($"[new-inventory] SyncFromBox() appelé. _isBoxActive={_isBoxActive}");
            if (!_isBoxActive)
            {
                Main.ModEntry.Logger.Log("[new-inventory] SyncFromBox(): Ignoré car _isBoxActive est false (boîte déjà inactive).");
                return;
            }

            _isSyncing = true;
            try
            {
                if (_virtualBox != null)
                {
                    var playerInv = Game.Instance?.Player?.Inventory;
                    if (playerInv != null)
                    {
                        var boxItems = _virtualBox.Items.ToList();
                        StashedItemIds.Clear();
                        foreach (var item in boxItems)
                        {
                            if (item == null) continue;
                            Main.ModEntry.Logger.Log($"[new-inventory] SyncFromBox(): Enregistrement de l'ID {item.UniqueId} pour l'item {item.Name}.");
                            StashedItemIds.Add(item.UniqueId);
                            if (!playerInv.Items.Contains(item))
                            {
                                Main.ModEntry.Logger.Log($"[new-inventory] SyncFromBox(): Déplacement de l'item {item.Name} (ID: {item.UniqueId}) de _virtualBox vers l'inventaire du joueur.");
                                _virtualBox.Remove(item);
                                playerInv.Add(item);
                            }
                            else
                            {
                                Main.ModEntry.Logger.Log($"[new-inventory-debug] SyncFromBox(): Item {item.Name} (ID: {item.UniqueId}) est déjà dans l'inventaire du joueur.");
                            }
                        }
                    }
                }
            }
            finally
            {
                _isSyncing = false;
                _isBoxActive = false;
            }
            Main.ModEntry.Logger.Log($"[new-inventory] SyncFromBox() terminé. StashedItemIds count={StashedItemIds.Count}. _isBoxActive passé à false.");
        }

        public override void OnPreSave()
        {
            Main.ModEntry.Logger.Log("[new-inventory] OnPreSave() appelé.");
            base.OnPreSave();
            SyncFromBox();
        }

        public override void OnApplyPostLoadFixes()
        {
            Main.ModEntry.Logger.Log($"[new-inventory] OnApplyPostLoadFixes() appelé. StashedItemIds count={StashedItemIds.Count}");
            base.OnApplyPostLoadFixes();

            if (StashedItems != null && StashedItems.Count > 0)
            {
                Main.ModEntry.Logger.Log($"[new-inventory] OnApplyPostLoadFixes(): Détection de {StashedItems.Count} items hérités à migrer.");
                var playerInv = Game.Instance?.Player?.Inventory;
                if (playerInv != null)
                {
                    foreach (var item in StashedItems)
                    {
                        if (item == null) continue;
                        StashedItemIds.Add(item.UniqueId);
                        if (!playerInv.Items.Contains(item))
                        {
                            Main.ModEntry.Logger.Log($"[new-inventory] OnApplyPostLoadFixes(): Migration de l'item {item.Name} (ID: {item.UniqueId}) vers l'inventaire natif.");
                            playerInv.Add(item);
                        }
                    }
                    Main.ModEntry.Logger.Log($"[new-inventory] Migration réussie de {StashedItems.Count} items vers l'inventaire natif.");
                    StashedItems.Clear();
                }
            }
        }

        public void CheckAndFinishProjects()
        {
            // Main.ModEntry.Logger.Log($"[ATELIER-DEBUG] Début CheckAndFinishProjects. Projets à vérifier : {ActiveProjects.Count}");
            if (ActiveProjects.Count == 0) return;

            long currentTime = Game.Instance.TimeController.GameTime.Ticks;
            var completedProjects = new List<CraftingProject>();

            foreach (var project in ActiveProjects)
            {
                try 
                {
                    if (currentTime >= project.FinishTimeTicks || CraftingSettings.Instance.InstantCrafting)
                    {
                        // Résolution de l'item si la référence a été perdue au chargement (restart game)
                        if (project.Item == null && !string.IsNullOrEmpty(project.ItemId))
                        {
                            project.Item = Game.Instance?.Player?.Inventory?.Items?.FirstOrDefault(i => i != null && i.UniqueId == project.ItemId);
                            if (project.Item == null && _virtualBox != null)
                            {
                                project.Item = _virtualBox.Items.FirstOrDefault(i => i != null && i.UniqueId == project.ItemId);
                            }
                            if (project.Item != null) Main.ModEntry.Logger.Log($"[ATELIER] Récupération de l'item {project.Item.Name} via UniqueId après chargement.");
                        }

                        var bp = (project.EnchantmentGuid.Replace("-", "").ToLower().StartsWith("c2af") 
                            ? CustomEnchantmentsBuilder.GetOrBuildDynamicBlueprint(project.EnchantmentGuid) 
                            : ResourcesLibrary.TryGetBlueprint(BlueprintGuid.Parse(project.EnchantmentGuid)));

                        if (bp == null) 
                        {
                            Main.ModEntry.Logger.Error($"[ATELIER-DEBUG] ERREUR: Blueprint introuvable pour le GUID {project.EnchantmentGuid} !");
                            continue;
                        }

                        if (bp is BlueprintItem bpItem)
                        {
                            var newItem = bpItem.CreateEntity();
                            DeferredInventoryOpener.CraftingBox.Add(newItem);
                            SyncFromBox(); // Synchronise la liste interne StashedItems
                            Main.ModEntry.Logger.Log($"[ATELIER] Création et livraison dans le coffre de : {newItem.Name}");
                        }
                        else if (bp is BlueprintItemEnchantment bpEnch)
                        {
                            ApplyEnchantmentsafely(project.Item, bpEnch);
                            Main.ModEntry.Logger.Log($"[ATELIER] Application réussie de {bpEnch.name} sur {project.Item?.Name ?? "???"}");
                        }
                        
                        completedProjects.Add(project);
                    }
                } 
                catch (Exception e) 
                {
                    Main.ModEntry.Logger.Error($"[ATELIER-DEBUG] CRASH dans la boucle d'un projet : {e.Message}\n{e.StackTrace}");
                }
            }

            foreach (var p in completedProjects) 
            {
                ActiveProjects.Remove(p);
            }
            // Main.ModEntry.Logger.Log($"[ATELIER-DEBUG] Fin CheckAndFinishProjects. Projets restants : {ActiveProjects.Count}");
        }

        public static void ApplyEnchantmentsafely(ItemEntity item, BlueprintItemEnchantment bp)
        {
            if (item == null || bp == null) return;

            string family = CraftingCalculator.GetEnchantmentFamily(bp.name);
            if (!string.IsNullOrEmpty(family))
            {
                var toRemove = item.Enchantments
                    .Where(e => !e.IsTemporary && CraftingCalculator.GetEnchantmentFamily(e.Blueprint.name) == family)
                    .ToList();
                foreach (var old in toRemove) item.RemoveEnchantment(old);
            }

            // On évite les doublons exacts
            if (!item.Enchantments.Any(e => e.Blueprint.AssetGuid == bp.AssetGuid))
            {
                // 🛠️ CORRECTION FINALE : On donne au jeu un "lanceur" pour cet enchantement
                var player = Game.Instance.Player.MainCharacter.Value;
                
                var context = new Kingmaker.UnitLogic.Mechanics.MechanicsContext(
                    caster: player, 
                    owner: player.Descriptor, 
                    blueprint: bp
                );
                
                item.AddEnchantment(bp, context);
                item.Identify();
            }
        }
    }
}
