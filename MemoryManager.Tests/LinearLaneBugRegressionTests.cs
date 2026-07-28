/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     MemoryManager.Tests
 * FILE:        LinearLaneBugRegressionTests.cs
 * PURPOSE:     Explicit unit tests reproducing and verifying the fix for the LinearLane 
 *              bump-pointer capacity exhaustion bug when EntryCount hits zero.
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

using MemoryManager.Core;
using MemoryManager.Lanes;
using MemoryManager.Types;

namespace MemoryManager.Tests
{
    /// <summary>
    /// Test for a bug found in LinearLane
    /// </summary>
    [TestClass]
    public sealed class LinearLaneBugRegressionTests
    {
        /// <summary>
        /// Directly tests LinearLane in isolation with a small 1KB buffer.
        /// Repeatedly allocates 256 bytes and frees it 100 times (25,600 total cumulative bytes requested).
        /// Without resetting _nextFreeOffset on zero entries, this fails on iteration 4.
        /// </summary>
        [TestMethod]
        public void LinearLane_RepeatedAllocateAndFree_ResetsBumpPointerWhenEmpty()
        {
            // 1. Create a SlowLane dummy for dependencies and a tight 1024-byte LinearLane
            using var slowLane = new SlowLane(4096);
            using var lane = new LinearLane(size: 1024, slowLane: slowLane, maxEntries: 16);

            const int requestedSize = 256;
            const int iterations = 100; // Cumulative 25,600 bytes >> 1,024 byte capacity

            for (var i = 0; i < iterations; i++)
            {
                // Verify the lane states it can allocate before requesting
                Assert.IsTrue(lane.CanAllocate(requestedSize),
                    $"Iteration {i}: LinearLane reported it cannot allocate {requestedSize} bytes despite being empty.");

                // Allocate
                var handle = lane.Allocate(requestedSize);
                Assert.AreEqual(1, lane.EntryCount);

                // Free immediately
                lane.Free(handle);

                // CRITICAL REGRESSION ASSERTION: 
                // Once freed, EntryCount is 0, so free space must return to 100% full capacity (1024 bytes).
                Assert.AreEqual(0, lane.EntryCount);
                Assert.AreEqual(1024, lane.FreeSpace(),
                    $"Iteration {i}: Bump pointer was not reset when EntryCount dropped to 0! Free space: {lane.FreeSpace()}");
            }
        }

        /// <summary>
        /// Integration test reproducing the exact exception thrown in SpeedBenchmark_1MillionRents
        /// through the MemoryArena facade configured with AllocatorStrategy.LinearBump.
        /// </summary>
        [TestMethod]
        public void MemoryArena_GameLoopPreset_PreventsBumpExhaustionViaArenaRent()
        {
            // Tight 1MB GameLoop Arena (FastLane will be ~256KB)
            var config = MemoryManagerConfig.CreateForGameLoop(totalBudget: 1024 * 1024);
            using var arena = new MemoryArena(config);

            const int rentElementCount = 64; // 64 * 4 bytes = 256 bytes per rent
            const int totalIterations = 10_000; // 2.56 MB cumulative >> 256KB FastLane

            // Should complete cleanly without spilling to SlowLane or throwing OutOfMemoryException
            for (var i = 0; i < totalIterations; i++)
            {
                using var rent = new ArenaRent<int>(arena, rentElementCount);
                rent[0] = i;
                rent[rentElementCount - 1] = i * 2;
            }

            // Assert FastLane is back to zero active entries and ready for new frames
            Assert.AreEqual(0, arena.FastLane!.EntryCount);
        }
    }
}