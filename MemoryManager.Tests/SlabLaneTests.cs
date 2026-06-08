/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     MemoryManager.Tests
 * FILE:        SlabLaneTests.cs
 * PURPOSE:     Standalone and integration verification for the segregated SlabLane strategy.
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

using MemoryManager.Core;
using MemoryManager.Lanes;

namespace MemoryManager.Tests
{
    [TestClass]
    public class SlabLaneTests
    {
        /// <summary>
        /// Standalone Test: Verifies that SlabLane rounds up to the correct size class bucket,
        /// and allows blazing fast out-of-order deletion and recycling of specific slots.
        /// </summary>
        [TestMethod]
        [TestCategory("SlabLane_Standalone")]
        public unsafe void SlabLane_AllocateFreeRecycle_WorksStandalone()
        {
            // Arrange
            const int totalSize = 1024 * 1024; // 1 MB buffer pool
            using var slowLane = new SlowLane(totalSize);

            // Generate a config where the threshold gives us bins up to 256 bytes (16, 32, 64, 128, 256)
            var config = new MemoryManagerConfig { Threshold = 256 };
            using var slabLane = new SlabLane(totalSize, slowLane, maxEntries: 100, config: config);

            // 1. Allocate a 20-byte item (Should snap to the 32-byte Size Class Bin)
            var h1 = slabLane.Allocate(20);
            var ptr1 = (int*)slabLane.Resolve(h1);
            *ptr1 = 111;

            // 2. Allocate a second 20-byte item (Should take the next slot in the same 32-byte Bin)
            var h2 = slabLane.Allocate(20);
            var ptr2 = (int*)slabLane.Resolve(h2);
            *ptr2 = 222;

            // Assert they are isolated blocks separated by at least the physical slot overhead size
            Assert.AreNotEqual((nint)ptr1, (nint)ptr2, "Different allocations must yield unique address locations.");
            Assert.AreEqual(111, *ptr1);
            Assert.AreEqual(222, *ptr2);

            // 3. Free the first handle to return its physical offset back to the 32-byte LIFO stack
            slabLane.Free(h1);

            // 4. Re-allocate a 20-byte item. It must instantly recycle the slot we just freed!
            var h3 = slabLane.Allocate(20);
            var ptr3 = (int*)slabLane.Resolve(h3);

            Assert.AreEqual((nint)ptr1, (nint)ptr3,
                "Slab allocation must instantly recycle the vacant uniform slot address.");
        }

        /// <summary>
        /// Standalone Test: Verifies that if a specific size class bucket runs out of pre-allocated slots,
        /// it safely hits an OOM bounds check without crashing or corrupting adjacent memory bins.
        /// </summary>
        [TestMethod]
        [TestCategory("SlabLane_Standalone")]
        public void SlabLane_BinExhaustion_ThrowsOutOfMemoryException()
        {
            // Arrange
            // Small lane budget ensures buckets have very tightly bounded slots numbers
            const int smallCapacity = 4096;
            using var slowLane = new SlowLane(64 * 1024);

            var config = new MemoryManagerConfig { Threshold = 64 }; // Generates 16, 32, 64 byte bins (3 bins total)
            using var slabLane = new SlabLane(smallCapacity, slowLane, maxEntries: 50, config: config);

            // Act & Assert
            Assert.ThrowsException<OutOfMemoryException>(() =>
            {
                // Rapidly flood the 64-byte bucket line until its isolated sub-partition layout breaks
                for (var i = 0; i < 100; i++)
                {
                    slabLane.Allocate(60);
                }
            }, "A single saturated size bin must reject requests cleanly via OutOfMemoryException once exhausted.");
        }

        /// <summary>
        /// Integration Test: Plugs the SlabLane directly into the high-level MemoryArena facade
        /// and verifies automated allocation orchestration and internal fragmentation tracking.
        /// </summary>
        [TestMethod]
        [TestCategory("Arena_SlabIntegration")]
        public void Arena_WithSlabLane_OrchestratesAllocationsPerfect()
        {
            // Arrange
            var config = new MemoryManagerConfig
            {
                FastLaneSize = 512 * 1024,
                SlowLaneSize = 2 * 1024 * 1024,
                Threshold = 256,
                FastLaneStrategy = AllocatorStrategy.Slab // Activate our drop-in engine replacement!
            };

            var arena = new MemoryArena(config);

            // 1. Allocate down into the slab lane via the main unified entry door
            var handle = arena.Allocate(45); // Snaps to 64-byte bin inside SlabLane

            Assert.IsInstanceOfType(arena.FastLane, typeof(SlabLane),
                "The active fast lane implementation must match the Slab wrapper config.");
            Assert.IsTrue(handle.Id > 0, "Fast lane allocation handles must maintain positive identity numbers.");

            // 2. Perform safe unmanaged read/write actions
            ref var dataRef = ref arena.Get<int>(handle);
            dataRef = 8888;

            Assert.AreEqual(8888, arena.Get<int>(handle));

            // 3. Verify internal fragmentation reporting (wasted slot slack space analytics)
            var fragPercentage = arena.FastLane.EstimateFragmentation();
            Assert.IsTrue(fragPercentage > 0,
                "Slab lanes must detect and accurately report internal slack-space fragmentation properties.");

            // Cleanup
            arena.Free(handle);
        }

        /// <summary>
        /// Integration Test: Verifies the full cross-lane handshake during eviction lifecycle steps.
        /// Data must drop to the SlowLane, release the slab space, and leave a resolving redirection proxy stub behind.
        /// </summary>
        [TestMethod]
        [TestCategory("Arena_SlabIntegration")]
        public unsafe void Arena_SlabEviction_CreatesFunctionalRedirectionStub()
        {
            // Arrange
            var config = new MemoryManagerConfig
            {
                FastLaneSize = 256 * 1024,
                SlowLaneSize = 1024 * 1024,
                Threshold = 128,
                FastLaneStrategy = AllocatorStrategy.Slab
            };

            var arena = new MemoryArena(config);

            // 1. Establish an item in the slab fast lane and write to it
            var handle = arena.Allocate(32); // Takes slot in 32-byte bin
            var initialPtr = (int*)arena.Resolve(handle);
            *initialPtr = 777;

            // 2. Force an explicit eviction command to push the item out to the SlowLane
            arena.MoveFastToSlow(handle);

            // 3. ARCHITECTURAL BOUNDARY EVALUATIONS
            var entry = arena.GetEntry(handle);
            Assert.IsTrue(entry.IsStub,
                "Evicting data must convert the local metadata slot entry into a proxy tracking stub.");
            Assert.AreEqual(0, entry.Size,
                "Local buffer sizes must register as 0 once vacated by eviction processing paths.");

            // 4. STUB TRANSPARENCY VERIFICATION: Resolving the original handle must STILL work cleanly!
            var postEvictionPtr = (int*)arena.Resolve(handle);
            Assert.AreNotEqual((nint)initialPtr, (nint)postEvictionPtr,
                "The memory address space coordinates must have shifted downstream.");
            Assert.AreEqual(777, *postEvictionPtr,
                "The payload values must survive across transfer boundaries completely unaltered.");

            // 5. RECYCLING VALIDATION: Ensure the original physical slot is immediately available for fresh requests
            var freshHandle = arena.Allocate(32);
            var freshPtr = (int*)arena.Resolve(freshHandle);

            Assert.AreEqual((nint)initialPtr, (nint)freshPtr,
                "The vacated fast lane slab slot must instantly accept new allocation targets.");
        }
    }
}