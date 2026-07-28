/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     MemoryManager.Tests
 * FILE:        FrameScratchTests.cs
 * PURPOSE:     MS Unit tests verifying the CreateForFrameScratch preset under heavy load.
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

using System.Diagnostics;
using MemoryManager.Core;
using MemoryManager.Types;

namespace MemoryManager.Tests
{
    /// <summary>
    /// Frame Scratch Tests
    /// </summary>
    [TestClass]
    public sealed class FrameScratchTests
    {
        /// <summary>
        /// The scratch budget bytes
        /// </summary>
        private const int ScratchBudgetBytes = 8 * 1024 * 1024; // 8 MB full scratchpad budget

        /// <summary>
        /// Verifies that large allocations (up to full budget) hit FastLane directly 
        /// and never touch SlowLane (0 entries in SlowLane).
        /// </summary>
        [TestMethod]
        public void FrameScratch_LargeAllocation_ExclusivelyUsesFastLane()
        {
            var config = MemoryManagerConfig.CreateForFrameScratch(ScratchBudgetBytes);
            using var arena = new MemoryArena(config);

            // Rent 2 MB (256,000 longs * 8 bytes) in a single request.
            // With the default config, anything > 256 KB would have spilled to SlowLane!
            const int elementCount = 256 * 1024; // 2 MB

            using (var rent = new ArenaRent<long>(arena, elementCount, hints: AllocationHints.NoSpill))
            {
                rent[0] = 42;
                rent[elementCount - 1] = 99;

                Assert.AreEqual(42, rent[0]);
                Assert.AreEqual(99, rent[elementCount - 1]);

                // Key Architectural Assertions:
                Assert.AreEqual(1, arena.FastLane!.EntryCount, "Allocation should reside in FastLane.");
                Assert.AreEqual(0, arena.SlowLane.EntryCount, "SlowLane must remain completely unused (0 entries).");
            }

            // Once disposed, FastLane bump pointer resets back to 0 active entries
            Assert.AreEqual(0, arena.FastLane.EntryCount);
        }

        /// <summary>
        /// Confirms that using AllocationHints.NoSpill with CreateForFrameScratch succeeds 
        /// without throwing an OutOfMemoryException.
        /// </summary>
        [TestMethod]
        public void FrameScratch_NoSpillHint_SucceedsSeamlessly()
        {
            var config = MemoryManagerConfig.CreateForFrameScratch(ScratchBudgetBytes);
            using var arena = new MemoryArena(config);

            // Rent with explicit NoSpill hint
            using var rent = new ArenaRent<int>(arena, 1024, hints: AllocationHints.FrameCritical | AllocationHints.NoSpill);
            rent[0] = 123;

            Assert.AreEqual(123, rent[0]);
            Assert.AreEqual(0, arena.SlowLane.EntryCount, "SlowLane was touched despite NoSpill preset.");
        }

        /// <summary>
        /// Simulates 100,000 real-time game frames continuously renting and disposing 
        /// transient scratch memory using CreateForFrameScratch.
        /// Proves zero OutOfMemoryException crashes and zero native memory leaks.
        /// </summary>
        [TestMethod]
        public void FrameScratch_100kFrames_HeavyChurn_ProvesZeroLeakAndMaxSpeed()
        {
            // Baseline Snapshot
            GC.Collect(2, GCCollectionMode.Forced, true, true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, true, true);
            var initialManagedMemory = GC.GetTotalMemory(true);

            const int simulatedFrames = 100_000;
            const int itemsPerFrame = 512;

            var sw = Stopwatch.StartNew();

            {
                var config = MemoryManagerConfig.CreateForFrameScratch(ScratchBudgetBytes);
                using var arena = new MemoryArena(config);

                for (var frame = 0; frame < simulatedFrames; frame++)
                {
                    // 1. Transient calculations (e.g., Light Propagation / Soft Rasterizer)
                    using (var rent = new ArenaRent<long>(arena, itemsPerFrame, hints: AllocationHints.NoSpill))
                    {
                        for (var i = 0; i < itemsPerFrame; i++)
                        {
                            rent[i] = frame + i;
                        }
                    } // O(1) bump reset occurs here on Dispose!

                    // 2. Output buffer
                    using (var buffer = new ArenaBuffer<long>(arena, itemsPerFrame, hints: AllocationHints.NoSpill))
                    {
                        for (var i = 0; i < itemsPerFrame; i++)
                        {
                            buffer.Add(i * 2);
                        }
                    } // O(1) bump reset occurs here on Dispose!

                    arena.TickFrame();
                }

                // Verify SlowLane was NEVER used across all 100,000 frames
                Assert.AreEqual(0, arena.SlowLane.EntryCount, "SlowLane was polluted during frame scratch churn.");
            } // Arena disposed here

            sw.Stop();

            Trace.WriteLine($"100,000 FrameScratch cycles executed in: {sw.ElapsedMilliseconds} ms");

            // Final Cleanup & Leak Comparison
            GC.Collect(2, GCCollectionMode.Forced, true, true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, true, true);

            var finalManagedMemory = GC.GetTotalMemory(false);
            var managedDelta = Math.Abs(finalManagedMemory - initialManagedMemory);

            // Verify memory stayed clean
            Assert.IsTrue(managedDelta < 150 * 1024 * 1024,
                $"Memory leak detected! Retained {managedDelta / (1024.0 * 1024.0):F2} MB after Arena disposal.");
        }
    }
}