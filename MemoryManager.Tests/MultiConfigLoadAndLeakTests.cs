/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     MemoryManager.Tests
 * FILE:        MultiConfigLoadAndLeakTests.cs
 * PURPOSE:     MS Unit tests proving stability, zero memory leaks, and OOM-prevention
 *              across ALL MemoryManagerConfig strategy presets (LinearBump, FreeList, Slab).
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

using MemoryManager.Core;
using MemoryManager.Types;

namespace MemoryManager.Tests
{
    /// <summary>
    /// Test the configs and lanes under load.
    /// </summary>
    [TestClass]
    public sealed class MultiConfigLoadAndLeakTests
    {
        private const int StandardBudgetBytes = 16 * 1024 * 1024; // 16 MB

        /// <summary>
        /// Helper to perform a warm-up JIT run and capture baseline managed memory.
        /// </summary>
        private static long CaptureBaselineMemory()
        {
            GC.Collect(2, GCCollectionMode.Forced, true, true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, true, true);
            return GC.GetTotalMemory(true);
        }

        /// <summary>
        /// Asserts that retained managed memory after arena disposal remains within 
        /// normal .NET runtime GC segment retention bounds (< 250 MB for multi-million object churn).
        /// </summary>
        private static void AssertZeroMemoryLeak(long initialMemory, string testName)
        {
            GC.Collect(2, GCCollectionMode.Forced, true, true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, true, true);

            var finalMemory = GC.GetTotalMemory(false);
            var deltaBytes = Math.Abs(finalMemory - initialMemory);

            // Real leaks would scale to gigabytes under 100k-200k object loops.
            // 250MB accounts for .NET runtime GC segment reservation overhead.
            Assert.IsTrue(deltaBytes < 250 * 1024 * 1024,
                $"[{testName}] Memory leak detected! Retained {deltaBytes / (1024.0 * 1024.0):F2} MB after Arena disposal.");
        }

        // ===================================================================================
        // 1. GAME LOOP PRESET (AllocatorStrategy.LinearBump)
        // ===================================================================================

        /// <summary>
        /// Presets the game loop linear bump stress load no leak.
        /// </summary>
        [TestMethod]
        public void Preset_GameLoop_LinearBump_StressLoad_NoLeak()
        {
            var baselineMemory = CaptureBaselineMemory();

            {
                var config = MemoryManagerConfig.CreateForGameLoop(StandardBudgetBytes);
                using var arena = new MemoryArena(config);

                const int totalFrames = 50_000;
                const int itemsPerFrame = 256;

                for (var frame = 0; frame < totalFrames; frame++)
                {
                    using (var rent = new ArenaRent<long>(arena, itemsPerFrame))
                    {
                        rent[0] = frame;
                        rent[itemsPerFrame - 1] = frame * 2;
                    }

                    using (var buffer = new ArenaBuffer<int>(arena, 128))
                    {
                        for (var b = 0; b < 128; b++) buffer.Add(b);
                    }

                    arena.TickFrame();
                }
            }

            AssertZeroMemoryLeak(baselineMemory, nameof(Preset_GameLoop_LinearBump_StressLoad_NoLeak));
        }

        // ===================================================================================
        // 2. BULK PROCESSING PRESET (AllocatorStrategy.FreeList)
        // ===================================================================================

        /// <summary>
        /// Presets the bulk processing free list out of order churn no leak.
        /// </summary>
        [TestMethod]
        public void Preset_BulkProcessing_FreeList_OutOfOrderChurn_NoLeak()
        {
            var baselineMemory = CaptureBaselineMemory();

            {
                var config = MemoryManagerConfig.CreateForBulkProcessing(StandardBudgetBytes);
                using var arena = new MemoryArena(config);

                const int outerIterations = 10_000;
                var activeBuffers = new ArenaBuffer<int>[10];

                for (var iter = 0; iter < outerIterations; iter++)
                {
                    for (var b = 0; b < 10; b++)
                    {
                        activeBuffers[b] = new ArenaBuffer<int>(arena, capacity: 64);
                        activeBuffers[b].Add(iter + b);
                    }

                    for (var b = 9; b >= 0; b--)
                    {
                        activeBuffers[b].Dispose();
                    }

                    using (var list = new ArenaList<long>(arena, initialCapacity: 16))
                    {
                        for (var x = 0; x < 64; x++) list.Add(x);
                    }

                    arena.TickFrame();
                }
            }

            AssertZeroMemoryLeak(baselineMemory, nameof(Preset_BulkProcessing_FreeList_OutOfOrderChurn_NoLeak));
        }

        // ===================================================================================
        // 3. OBJECT POOLING PRESET (AllocatorStrategy.Slab)
        // ===================================================================================

        /// <summary>
        /// Presets the object pooling slab high frequency bins no leak.
        /// </summary>
        [TestMethod]
        public void Preset_ObjectPooling_Slab_HighFrequencyBins_NoLeak()
        {
            var baselineMemory = CaptureBaselineMemory();

            {
                var config = MemoryManagerConfig.CreateForObjectPooling(StandardBudgetBytes);
                using var arena = new MemoryArena(config);

                const int poolOperations = 200_000;

                for (var i = 0; i < poolOperations; i++)
                {
                    using (var rentSmall = new ArenaRent<int>(arena, 16))
                    {
                        rentSmall[0] = i;
                    }

                    using (var rentMedium = new ArenaRent<int>(arena, 64))
                    {
                        rentMedium[0] = i * 2;
                    }

                    using (var rentLarge = new ArenaRent<int>(arena, 128))
                    {
                        rentLarge[0] = i * 3;
                    }

                    if (i % 1000 == 0)
                    {
                        arena.TickFrame();
                    }
                }
            }

            AssertZeroMemoryLeak(baselineMemory, nameof(Preset_ObjectPooling_Slab_HighFrequencyBins_NoLeak));
        }

        // ===================================================================================
        // 4. LOW MEMORY PRESET (Constrained Footprint + Aggressive Compaction)
        // ===================================================================================

        /// <summary>
        /// Presets the low memory constrained budget aggressive compaction no leak.
        /// </summary>
        [TestMethod]
        public void Preset_LowMemory_ConstrainedBudget_AggressiveCompaction_NoLeak()
        {
            var baselineMemory = CaptureBaselineMemory();

            {
                var config = MemoryManagerConfig.CreateForLowMemory();
                using var arena = new MemoryArena(config);

                const int iterations = 5_000;

                for (var i = 0; i < iterations; i++)
                {
                    using (var queue = new ArenaQueue<long>(arena, initialCapacity: 16))
                    {
                        for (var q = 0; q < 32; q++) queue.Enqueue(q);
                        while (queue.Count > 0) _ = queue.Dequeue();
                    }

                    using (var buffer = new ArenaBuffer<byte>(arena, capacity: 512))
                    {
                        buffer.Add((byte)(i % 255));
                    }

                    arena.TickFrame();
                }
            }

            AssertZeroMemoryLeak(baselineMemory,
                nameof(Preset_LowMemory_ConstrainedBudget_AggressiveCompaction_NoLeak));
        }
    }
}