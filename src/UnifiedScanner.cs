using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Items.Ecnchantments;
using Kingmaker.Blueprints.Classes.Spells;
using Kingmaker.Blueprints.JsonSystem;
using Kingmaker.Blueprints.JsonSystem.BinaryFormat;
using Kingmaker.Blueprints.JsonSystem.Converters;

namespace CraftingSystem
{
    /// <summary>
    /// Moteur de scan optimisé permettant de parcourir l'intégralité des blueprints du jeu
    /// en une seule passe binaire multithreadée.
    /// </summary>
    public static class UnifiedScanner
    {
        public static bool IsScanning { get; private set; }
        public static float Progress { get; private set; }
        public static string StatusMessage { get; private set; } = "";

        public static async Task RunFullScan()
        {
            if (IsScanning) return;
            IsScanning = true;
            StatusMessage = "Initializing unified scan...";
            
            try
            {
                var bpCache = ResourcesLibrary.BlueprintsCache;
                var allKeys = bpCache.m_LoadedBlueprints.Keys.ToList();
                int total = allKeys.Count;

                Main.ModEntry.Logger.Log($"[UNIFIED-SCAN] Starting scan of {total} blueprints...");

                // 1. Lecture du fichier pack en mémoire (évite les accès disque concurrents lents)
                byte[] bytes;
                lock (bpCache.m_Lock)
                {
                    using var ms = new MemoryStream();
                    bpCache.m_PackFile.Position = 0;
                    bpCache.m_PackFile.CopyTo(ms);
                    bytes = ms.GetBuffer();
                }

                // Accumulateurs pour les différents types de données
                var enchants = new ConcurrentBag<(BlueprintItemEnchantment bp, BlueprintGuid guid)>();
                var spellbooks = new ConcurrentBag<(BlueprintSpellbook bp, BlueprintGuid guid)>();
                var spellLists = new ConcurrentBag<(BlueprintSpellList bp, BlueprintGuid guid)>();

                int processed = 0;

                // 2. Scan Multithreadé
                await Task.Run(() =>
                {
                    Parallel.ForEach(Partitioner.Create(0, total), new ParallelOptions { MaxDegreeOfParallelism = 4 }, range =>
                    {
                        using var stream = new MemoryStream(bytes);
                        var serializer = new ReflectionBasedSerializer(new PrimitiveSerializer(new BinaryReader(stream), UnityObjectConverter.AssetList));

                        for (int i = range.Item1; i < range.Item2; i++)
                        {
                            var guid = allKeys[i];
                            
                            // On incrémente le compteur global pour la progression
                            var currentProcessed = Interlocked.Increment(ref processed);
                            if (currentProcessed % 2000 == 0)
                            {
                                Progress = (float)currentProcessed / total;
                                StatusMessage = $"Processing blueprints: {currentProcessed}/{total}";
                            }

                            if (bpCache.m_LoadedBlueprints.TryGetValue(guid, out var entry) && entry.Offset != 0)
                            {
                                try
                                {
                                    stream.Seek(entry.Offset, SeekOrigin.Begin);
                                    SimpleBlueprint bp = null;
                                    serializer.Blueprint(ref bp);

                                    if (bp == null) continue;

                                    // Distribution par type
                                    if (bp is BlueprintItemEnchantment ench)
                                    {
                                        enchants.Add((ench, guid));
                                    }
                                    else if (bp is BlueprintSpellbook sb)
                                    {
                                        spellbooks.Add((sb, guid));
                                    }
                                    else if (bp is BlueprintSpellList sl)
                                    {
                                        spellLists.Add((sl, guid));
                                    }
                                }
                                catch { /* On ignore les erreurs individuelles de lecture (blueprints corrompus) */ }
                            }
                        }
                    });
                });

                StatusMessage = "Finalizing data registration...";
                
                // 3. Finalisation des sous-systèmes
                // On passe les résultats aux scanners spécifiques qui vont transformer les BPs en Data
                EnchantmentScanner.FinalizeScan(enchants);
                SpellScanner.FinalizeScan(spellbooks, spellLists);

                Main.ModEntry.Logger.Log($"[UNIFIED-SCAN] Scan completed: Found {enchants.Count} enchants and {spellbooks.Count + spellLists.Count} spell-related objects.");
            }
            catch (Exception ex)
            {
                Main.ModEntry.Logger.Error($"[UNIFIED-SCAN] Fatal error during scan: {ex}");
            }
            finally
            {
                IsScanning = false;
                StatusMessage = "Scan completed.";
                Progress = 1.0f;
            }
        }
    }
}
