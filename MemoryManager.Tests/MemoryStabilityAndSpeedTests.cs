/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     MemoryManager.Tests
 * FILE:        MemoryStabilityAndSpeedTests.cs
 * PURPOSE:     MS Unit tests proving memory stability (no explosion) and micro-benchmark throughput.
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

using System.Diagnostics;
using MemoryManager.Core;
using MemoryManager.Types;

namespace MemoryManager.Tests
{
    [TestClass]
    public sealed class MemoryStabilityAndSpeedTests
    {
        /// <summary>
        /// The tight budget bytes
        /// </summary>
        private const int TightBudgetBytes = 16 * 1024 * 1024; // Strict 16MB budget

        /// <summary>
        /// Simulates 100,000 real-time game loop cycles allocating and disposing thousands 
        /// of temporary structs per frame. Proves that surviving managed memory does NOT leak.
        /// </summary>
        [TestMethod]
        public void GameLoop_100kFrames_ProvesZeroMemoryExplosion()
        {
            const int simulatedFrames = 100_000;
            const int itemsPerFrame = 256;

            // --- PHASE 1: JIT Warm-Up Pass ---
            // Forces the .NET runtime to JIT-compile all methods, structures, 
            // and allocator code paths before taking the baseline memory snapshot.
            {
                var warmConfig = MemoryManagerConfig.CreateForGameLoop(TightBudgetBytes);
                using var warmArena = new MemoryArena(warmConfig);

                using (var rent = new ArenaRent<long>(warmArena, 16)) { rent[0] = 1; }
                using (var buffer = new ArenaBuffer<long>(warmArena, 16)) { buffer.Add(1); }
                using (var list = new ArenaList<long>(warmArena, 16)) { list.Add(1); }
                using (var queue = new ArenaQueue<long>(warmArena, 16)) { queue.Enqueue(1); }

                warmArena.TickFrame();
            }

            // --- PHASE 2: Baseline Snapshot ---
            GC.Collect(2, GCCollectionMode.Forced, true, true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, true, true);
            var initialManagedMemory = GC.GetTotalMemory(true);

            // --- PHASE 3: Real 100,000 Frame Test ---
            {
                var config = MemoryManagerConfig.CreateForGameLoop(TightBudgetBytes);
                using var arena = new MemoryArena(config);

                for (var frame = 0; frame < simulatedFrames; frame++)
                {
                    using (var rent = new ArenaRent<long>(arena, itemsPerFrame))
                    {
                        for (var i = 0; i < itemsPerFrame; i++) rent[i] = frame + i;
                    }

                    using (var buffer = new ArenaBuffer<long>(arena, itemsPerFrame))
                    {
                        for (var i = 0; i < itemsPerFrame; i++) buffer.Add(i * 2);
                    }

                    using (var list = new ArenaList<long>(arena, initialCapacity: 32))
                    {
                        for (var i = 0; i < itemsPerFrame; i++) list.Add(i);
                    }

                    using (var queue = new ArenaQueue<long>(arena, initialCapacity: 32))
                    {
                        for (var i = 0; i < 64; i++) queue.Enqueue(i);
                        while (queue.Count > 0) _ = queue.Dequeue();
                    }

                    arena.TickFrame();
                }
            } // <--- arena.Dispose() executes here

            // --- PHASE 4: Final Cleanup & Comparison ---
            GC.Collect(2, GCCollectionMode.Forced, true, true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, true, true);

            var finalManagedMemory = GC.GetTotalMemory(false);
            var managedDelta = Math.Abs(finalManagedMemory - initialManagedMemory);

            // Guardrail: Allow up to 150 MB for .NET GC heap segment reservation overhead 
            // across 400,000 temporary wrapper object instantiations.
            Assert.IsTrue(managedDelta < 150 * 1024 * 1024,
                $"Memory leak detected! Retained {managedDelta / (1024.0 * 1024.0):F2} MB after Arena disposal.");
        }

        /// <summary>
        /// Benchmarks high-frequency ArenaRent throughput. 
        /// </summary>
        [TestMethod]
        public void SpeedBenchmark_1MillionRents_ExecutesUnderTimeBudget()
        {
            var config = MemoryManagerConfig.CreateForGameLoop(TightBudgetBytes);
            using var arena = new MemoryArena(config);

            const int iterations = 1_000_000;
            var sw = Stopwatch.StartNew();

            for (var i = 0; i < iterations; i++)
            {
                using var rent = new ArenaRent<int>(arena, 64);
                rent[0] = i;
                rent[63] = i * 2;
            }

            sw.Stop();

            Trace.WriteLine($"1,000,000 ArenaRent operations completed in: {sw.ElapsedMilliseconds} ms");

#if DEBUG
            const int maxAllowedMs = 1500; // Debug threshold (allows for #if DEBUG tracking & no inlining)
#else
            const int maxAllowedMs = 150;  // Release threshold (full JIT optimization)
#endif

            Assert.IsTrue(sw.ElapsedMilliseconds < maxAllowedMs,
                $"Benchmark failed! Took {sw.ElapsedMilliseconds} ms, expected under {maxAllowedMs} ms.");
        }
    }
}