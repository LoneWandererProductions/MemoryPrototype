/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     MemoryManager.Tests
 * FILE:        BlobManagerTests.cs
 * PURPOSE:     Check our BlobManager
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

using MemoryManager.Core;
using MemoryManager.Lanes;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MemoryManager.Tests
{
    [TestClass]
    public class BlobManagerTests
    {
        /// <summary>
        /// BLOBs the manager allocate free and compact works correctly.
        /// </summary>
        [TestMethod]
        [TestCategory("BlobManager")]
        public void BlobManager_AllocateFreeAndCompact_WorksCorrectly()
        {
            int capacity = 1024; // 1 KB buffer
            nint buffer = Marshal.AllocHGlobal(capacity);

            try
            {
                var blobManager = new BlobManager(buffer, capacity);

                // 1. Allocate 3 blocks of 100 bytes
                var h1 = blobManager.Allocate(100);
                var h2 = blobManager.Allocate(100);
                var h3 = blobManager.Allocate(100);

                Assert.AreEqual(1024 - 300, blobManager.FreeSpace(), "Should have exactly 724 bytes free.");

                // 2. Free the middle block (Creates a 100-byte hole/fragmentation)
                blobManager.Free(h2);

                int fragBefore = blobManager.EstimateFragmentation();
                Assert.IsTrue(fragBefore > 0, "Fragmentation should be greater than 0 after freeing the middle blob.");

                Trace.WriteLine("--- BEFORE COMPACTION ---");
                Trace.WriteLine(blobManager.DebugVisualMap());

                // 3. Compact (The Snowplow)
                blobManager.Compact();

                int fragAfter = blobManager.EstimateFragmentation();
                Assert.AreEqual(0, fragAfter, "Fragmentation should be exactly 0 after compaction.");

                Trace.WriteLine("\n--- AFTER COMPACTION ---");
                Trace.WriteLine(blobManager.DebugVisualMap());

                // 4. Ensure surviving handles still resolve properly
                Assert.IsTrue(blobManager.HasHandle(h1), "Handle 1 should survive compaction.");
                Assert.IsTrue(blobManager.HasHandle(h3), "Handle 3 should survive compaction.");
            }
            finally
            {
                // Always clean up unmanaged memory in tests!
                Marshal.FreeHGlobal(buffer);
            }
        }

        /// <summary>
        /// Performances the fast lane vs linear lane showdown.
        /// </summary>
        [TestMethod]
        [TestCategory("Performance")]
        public void Performance_FastLane_Vs_LinearLane_Showdown()
        {
            const int count = 50000;
            const int size = 32; // 32-byte structs (e.g., matrices, vectors)
            const int capacity = count * size * 2; // Plenty of room

            // Both lanes require a SlowLane reference in their constructors
            using var slowLane = new SlowLane(capacity);

            using var fastLane = new FastLane(capacity, slowLane, count);
            using var linearLane = new LinearLane(capacity, slowLane, count);

            var fastHandles = new MemoryHandle[count];
            var linearHandles = new MemoryHandle[count];

            // --- WARMUP (JIT Compilation) ---
            var w1 = fastLane.Allocate(16); fastLane.Free(w1);
            var w2 = linearLane.Allocate(16); linearLane.Free(w2);

            var sw = new Stopwatch();

            // --- 1. ALLOCATION RACE ---

            // LinearLane (Bump Allocator)
            sw.Start();
            for (int i = 0; i < count; i++)
                linearHandles[i] = linearLane.Allocate(size);
            sw.Stop();
            long linearAllocTicks = sw.ElapsedTicks;

            // FastLane (Free-List Allocator)
            sw.Restart();
            for (int i = 0; i < count; i++)
                fastHandles[i] = fastLane.Allocate(size);
            sw.Stop();
            long fastAllocTicks = sw.ElapsedTicks;

            // --- 2. DEALLOCATION RACE ---

            // LinearLane (Bump Allocator)
            sw.Restart();
            for (int i = 0; i < count; i++)
                linearLane.Free(linearHandles[i]);
            sw.Stop();
            long linearFreeTicks = sw.ElapsedTicks;

            // FastLane (Free-List Allocator)
            sw.Restart();
            for (int i = 0; i < count; i++)
                fastLane.Free(fastHandles[i]);
            sw.Stop();
            long fastFreeTicks = sw.ElapsedTicks;

            // --- 3. THE RESULTS ---
            Trace.WriteLine("===== ALLOCATION SPEED (50,000 objects) =====");
            Trace.WriteLine($"LinearLane (Bump) : {linearAllocTicks:N0} ticks");
            Trace.WriteLine($"FastLane (List)   : {fastAllocTicks:N0} ticks");
            Trace.WriteLine("");
            Trace.WriteLine("===== DEALLOCATION SPEED (50,000 objects) =====");
            Trace.WriteLine($"LinearLane (Bump) : {linearFreeTicks:N0} ticks");
            Trace.WriteLine($"FastLane (List)   : {fastFreeTicks:N0} ticks");

            // The Bump allocator should absolutely destroy the Free-List in allocation speed
            // because it doesn't have to loop through the _freeBlocks array!
        }
    }
}
