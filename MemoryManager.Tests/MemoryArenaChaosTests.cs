/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     MemoryManager.Tests
 * FILE:        MemoryArenaChaosTests.cs
 * PURPOSE:     Hardening and edge-case validation for the unmanaged memory arena.
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

using MemoryManager.Core;
using MemoryManager.Lanes;

namespace MemoryManager.Tests
{
    [TestClass]
    public class MemoryArenaChaosTests
    {
        /// <summary>
        /// Ensures that attempting to free an already freed handle throws an exception
        /// instead of corrupting the free-list or leaking structural slot array trackers.
        /// </summary>
        [TestMethod]
        [TestCategory("ChaosSafety")]
        public void Arena_DoubleFree_ThrowsInvalidOperationException()
        {
            // Arrange
            const int capacity = 1024 * 1024;
            using var slowLane = new SlowLane(capacity);
            using var fastLane = new FastLane(capacity, slowLane);

            var handle = fastLane.Allocate(64);

            // First free is the valid happy path
            fastLane.Free(handle);

            // Act & Assert
            Assert.ThrowsException<InvalidOperationException>(() =>
            {
                // Second free is an exploitation vector or developer bug
                fastLane.Free(handle);
            }, "The lane must block a double-free attempt to protect the structural integrity of the free-list.");

            fastLane.DebugVisualMap();
        }

        /// <summary>
        /// Verifies that when an ID is recycled, its generational version increments.
        /// An old handle pointing to the recycled ID must trigger a safe access violation,
        /// while the new handle resolves perfectly to the newly assigned block.
        /// </summary>
        [TestMethod]
        [TestCategory("ChaosSafety")]
        public unsafe void Arena_HandleRecycling_IncrementsVersion_BlocksStaleHandles()
        {
            // Arrange
            const int capacity = 1024 * 1024;
            using var slowLane = new SlowLane(capacity);
            using var fastLane = new FastLane(capacity, slowLane);

            // 1. Allocate block A, populate it, and record the original handle signature
            var oldHandle = fastLane.Allocate(32);
            int* oldPtr = (int*)fastLane.Resolve(oldHandle);
            *oldPtr = 42;

            // 2. Free block A to return its sequential integer ID back to the pool
            fastLane.Free(oldHandle);

            // 3. Force a new allocation. This must recycle the old ID, but increment the generation version
            var newHandle = fastLane.Allocate(32);

            // Assert that the identifier was recycled but the version is explicitly distinct
            Assert.AreEqual(oldHandle.Id, newHandle.Id, "The allocator should recycle tracking IDs to prevent footprint bloat.");
            Assert.AreNotEqual(oldHandle.Version, newHandle.Version, "Recycled IDs must feature an incremented generation value.");

            int* newPtr = (int*)fastLane.Resolve(newHandle);
            *newPtr = 99; // Write new distinct data to the recycled slot

            // Act & Assert

            // Verifying the new handle operates perfectly
            int* verifyPtr = (int*)fastLane.Resolve(newHandle);
            Assert.AreEqual(99, *verifyPtr);

            // Verifying the zombie check blocks the legacy handle signature
            Assert.ThrowsException<AccessViolationException>(() =>
            {
                // Attempting to resolve via the stale handle version must break deterministically
                fastLane.Resolve(oldHandle);
            }, "Resolving a stale generational handle version must trigger a structural AccessViolationException.");

            fastLane.DebugVisualMap();
        }

        /// <summary>
        /// Pushes the allocator exactly to its saturation limit down to the last byte.
        /// Verifies that compaction behaves perfectly at saturation thresholds and that
        /// allocating even one single byte past capacity constraints emits a clean OutOfMemoryException.
        /// </summary>
        [TestMethod]
        [TestCategory("ChaosSafety")]
        public void Arena_AbsoluteSaturation_HandlesCompactionAndThrowsOOM()
        {
            // Arrange
            const int elementSize = 64;
            int physicalBlockSize = MemoryCanary.GetPhysicalSize(elementSize);

            // Establish a strict budget that fits exactly 5 aligned physical blocks
            int customCapacity = physicalBlockSize * 5;

            using var slowLane = new SlowLane(customCapacity);
            using var fastLane = new FastLane(customCapacity, slowLane);

            var handles = new List<MemoryHandle>();

            // 1. Fully saturate the lane down to the literal final byte line
            for (int i = 0; i < 5; i++)
            {
                handles.Add(fastLane.Allocate(elementSize));
            }

            // Assert that the available free space tracking has hit zero
            Assert.AreEqual(0, fastLane.FreeSpace(), "Lane should have exactly 0 bytes remaining at absolute structural saturation.");

            // 2. CRITICAL BOUNDARY CHECK: Try to force an extra allocation while completely full
            Assert.ThrowsException<OutOfMemoryException>(() =>
            {
                // Because the arena is packed at 100% saturation, this must cleanly fail
                fastLane.Allocate(1);
            }, "Allocating past strict capacity parameters when full must cleanly emit an OutOfMemoryException.");

            // 3. Free alternating items to introduce calculated structural holes
            fastLane.Free(handles[1]);
            fastLane.Free(handles[3]);

            // 4. Execute a complete compaction pass at high stress capacity limits
            fastLane.Compact();

            // 5. Verify surviving blocks are still completely safe post-compaction slide
            Assert.IsTrue(fastLane.HasHandle(handles[0]), "Survivor ID 0 must maintain valid entry properties post-compaction.");
            Assert.IsTrue(fastLane.HasHandle(handles[2]), "Survivor ID 2 must maintain valid entry properties post-compaction.");
            Assert.IsTrue(fastLane.HasHandle(handles[4]), "Survivor ID 4 must maintain valid entry properties post-compaction.");

            // 6. VERIFY RECLAIMED WORKSPACE: Request a new item now that compaction freed up consolidated space
            var hNew = fastLane.Allocate(elementSize);
            Assert.IsTrue(fastLane.HasHandle(hNew), "The allocator should be able to accept new allocations again after compaction consolidates holes.");
        }
    }
}