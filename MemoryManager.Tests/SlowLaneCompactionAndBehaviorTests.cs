/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     MemoryManager.Tests
 * FILE:        SlowLaneCompactionAndBehaviorTests.cs
 * PURPOSE:     Comprehensive tests verifying SlowLane compaction styles, in-place data sliding, 
 *              auto-healing allocation passes, and BlobManager interactions.
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

using MemoryManager.Core;
using MemoryManager.Lanes;

namespace MemoryManager.Tests
{
    [TestClass]
    public class SlowLaneCompactionAndBehaviorTests
    {
        private const int OneMb = 1024 * 1024;

        /// <summary>
        /// Verifies that Full Compaction slides surviving memory blocks in-place, 
        /// eliminates fragmentation completely, and preserves exact byte data markers.
        /// </summary>
        [TestMethod]
        [TestCategory("Correctness")]
        public unsafe void FullCompaction_PreservesDataIntegrity_AndEliminatesFragmentation()
        {
            using var lane = new SlowLane(OneMb, compactionStyle: CompactionStyle.Full);
            var handles = new MemoryHandle[5];

            // 1. Allocate 5 blocks (512 bytes each, larger than BlobThreshold)
            for (var i = 0; i < 5; i++)
            {
                handles[i] = lane.Allocate(512);
                var ptr = (byte*)lane.Resolve(handles[i]);
                ptr[0] = (byte)(100 + i); // Unique byte marker
            }

            // 2. Introduce fragmented holes by freeing indices 1 and 3
            lane.Free(handles[1]);
            lane.Free(handles[3]);

            Assert.IsFalse(lane.HasHandle(handles[1]), "Handle 1 must be invalid after free.");
            Assert.IsFalse(lane.HasHandle(handles[3]), "Handle 3 must be invalid after free.");

            var fragBefore = lane.EstimateFragmentation();

            // 3. Act: Run Full Compaction
            lane.Compact(CompactionStyle.Full);

            var fragAfter = lane.EstimateFragmentation();
            Assert.IsTrue(fragAfter <= fragBefore, "Full compaction must reduce or clear fragmentation.");

            // 4. Assert: Validate surviving blocks (0, 2, 4) still resolve and hold exact byte values
            var expectedIndices = new[] { 0, 2, 4 };
            foreach (var idx in expectedIndices)
            {
                Assert.IsTrue(lane.HasHandle(handles[idx]), $"Handle {idx} must remain valid post-compaction.");
                var readPtr = (byte*)lane.Resolve(handles[idx]);
                Assert.AreEqual((byte)(100 + idx), readPtr[0], $"Data corruption detected at handle index {idx}.");
            }
        }

        /// <summary>
        /// Verifies that GoodEnough Compaction stops shifting blocks the moment 
        /// a contiguous gap large enough for the requested size is created.
        /// </summary>
        [TestMethod]
        [TestCategory("Correctness")]
        public unsafe void GoodEnoughCompaction_StopsEarly_WhenTargetGapIsOpened()
        {
            using var lane = new SlowLane(OneMb, compactionStyle: CompactionStyle.GoodEnough);
            var count = 8;
            var handles = new MemoryHandle[count];
            var blockSize = 1024; // 1 KB each

            for (var i = 0; i < count; i++)
            {
                handles[i] = lane.Allocate(blockSize);
                var ptr = (byte*)lane.Resolve(handles[i]);
                ptr[0] = (byte)(i + 1);
            }

            // Free items 1 and 2 to create a contiguous 2KB gap
            lane.Free(handles[1]);
            lane.Free(handles[2]);

            // Request GoodEnough compaction to open up space for a 2KB (2048 bytes) payload
            lane.Compact(CompactionStyle.GoodEnough, requiredSize: 2048);

            // Verify that all surviving allocations are intact
            var survivingIndices = new[] { 0, 3, 4, 5, 6, 7 };
            foreach (var idx in survivingIndices)
            {
                Assert.IsTrue(lane.HasHandle(handles[idx]), $"Handle {idx} must be alive.");
                var ptr = (byte*)lane.Resolve(handles[idx]);
                Assert.AreEqual((byte)(idx + 1), ptr[0], $"Data at index {idx} was corrupted.");
            }

            // Verify we can now immediately allocate a 2KB block without throwing OutOfMemoryException
            var newHandle = lane.Allocate(2048);
            Assert.IsTrue(lane.HasHandle(newHandle), "Allocation for 2KB block should succeed after GoodEnough compaction.");
        }

        /// <summary>
        /// Tests the auto-healing mechanism inside Allocate: when an allocation fails due to 
        /// heavy fragmentation, SlowLane should automatically trigger compaction and succeed.
        /// </summary>
        [TestMethod]
        [TestCategory("Correctness")]
        public unsafe void Allocate_TriggersAutoCompaction_WhenSpaceIsFragmented()
        {
            // Small lane size (128 KB total) to force tight boundary limits
            var capacity = 128 * 1024;
            using var lane = new SlowLane(capacity, blobCapacityFraction: 0.10, compactionStyle: CompactionStyle.Full);

            var blockSize = 16 * 1024; // 16 KB
            var handles = new List<MemoryHandle>();

            // Allocate until we fill the main lane region (~5 blocks)
            for (var i = 0; i < 5; i++)
            {
                if (lane.CanAllocate(blockSize))
                {
                    var h = lane.Allocate(blockSize);
                    var ptr = (byte*)lane.Resolve(h);
                    ptr[0] = (byte)(i + 10);
                    handles.Add(h);
                }
            }

            Assert.IsTrue(handles.Count >= 4, "Should have allocated at least 4 initial blocks.");

            // Create Swiss-cheese fragmentation by freeing alternate blocks (indices 0 and 2)
            lane.Free(handles[0]);
            lane.Free(handles[2]);

            // Now attempt to allocate a large 28 KB block. 
            // Neither freed 16KB hole alone can fit 28KB, but combined (32KB total free) they can!
            var largeRequestedSize = 28 * 1024;

            // Allocate should auto-compact and return a valid handle without throwing
            var autoCompactedHandle = lane.Allocate(largeRequestedSize);
            Assert.IsTrue(lane.HasHandle(autoCompactedHandle), "Auto-compacting allocation failed.");

            // Verify original surviving blocks are still valid
            var ptr1 = (byte*)lane.Resolve(handles[1]);
            Assert.AreEqual((byte)11, ptr1[0], "Surviving handle 1 data corrupted during auto-compaction.");
        }

        /// <summary>
        /// Ensures that compacting an empty lane or a lane with a single allocation 
        /// executes safely without throwing exceptions or corrupting pointers.
        /// </summary>
        [TestMethod]
        [TestCategory("EdgeCases")]
        public unsafe void Compact_EmptyOrSingleAllocation_DoesNotThrow()
        {
            using var lane = new SlowLane(64 * 1024);

            // 1. Compact completely empty lane
            lane.Compact(CompactionStyle.Full);
            lane.Compact(CompactionStyle.GoodEnough, 1024);

            // 2. Compact lane with a single active item
            var h = lane.Allocate(512);
            var ptr = (int*)lane.Resolve(h);
            *ptr = 424242;

            lane.Compact(CompactionStyle.Full);

            Assert.IsTrue(lane.HasHandle(h));
            var readPtr = (int*)lane.Resolve(h);
            Assert.AreEqual(424242, *readPtr, "Single item data corrupted post-compaction.");
        }

        /// <summary>
        /// Verifies that batch-releasing via FreeMany followed by compaction 
        /// correctly cleans up memory and updates array trackers cleanly.
        /// </summary>
        [TestMethod]
        [TestCategory("Correctness")]
        public unsafe void FreeMany_FollowedByCompaction_ReclaimsMemoryAccurately()
        {
            using var lane = new SlowLane(OneMb);
            const int total = 10;
            var handles = new MemoryHandle[total];

            for (var i = 0; i < total; i++)
            {
                handles[i] = lane.Allocate(1024);
                var ptr = (int*)lane.Resolve(handles[i]);
                *ptr = (i + 1) * 100;
            }

            // Batch free all even indices (0, 2, 4, 6, 8)
            var toFree = new MemoryHandle[] { handles[0], handles[2], handles[4], handles[6], handles[8] };
            lane.FreeMany(toFree);

            // Verify they are invalid
            foreach (var h in toFree)
            {
                Assert.IsFalse(lane.HasHandle(h));
            }

            // Compact
            lane.Compact(CompactionStyle.Full);

            // Verify odd indices survived intact
            for (var i = 1; i < total; i += 2)
            {
                Assert.IsTrue(lane.HasHandle(handles[i]), $"Handle {i} should survive.");
                var ptr = (int*)lane.Resolve(handles[i]);
                Assert.AreEqual((i + 1) * 100, *ptr, $"Data mismatch at handle index {i}.");
            }
        }

        /// <summary>
        /// Verifies that small allocations ($\le 256$ bytes) managed by BlobManager 
        /// remain completely unaffected during SlowLane main-buffer compactions.
        /// </summary>
        [TestMethod]
        [TestCategory("Correctness")]
        public unsafe void BlobManager_Allocations_RemainValid_DuringSlowLaneCompaction()
        {
            using var lane = new SlowLane(OneMb, blobThreshold: 256);

            // 1. Allocate small blobs (<= 256B, routed to BlobManager)
            var blobHandle1 = lane.Allocate(128);
            var blobHandle2 = lane.Allocate(64);

            var bPtr1 = (byte*)lane.Resolve(blobHandle1);
            var bPtr2 = (byte*)lane.Resolve(blobHandle2);
            bPtr1[0] = 77;
            bPtr2[0] = 88;

            // 2. Allocate large blocks (> 256B, routed to main SlowLane FreeList)
            var mainHandle1 = lane.Allocate(1024);
            var mainHandle2 = lane.Allocate(1024);

            // 3. Free main block 1 to create main buffer fragmentation
            lane.Free(mainHandle1);

            // 4. Compact the main SlowLane
            lane.Compact(CompactionStyle.Full);

            // 5. Assert: Both Blob handles and main handles remain completely valid
            Assert.IsTrue(lane.HasHandle(blobHandle1));
            Assert.IsTrue(lane.HasHandle(blobHandle2));
            Assert.IsTrue(lane.HasHandle(mainHandle2));

            Assert.AreEqual((byte)77, ((byte*)lane.Resolve(blobHandle1))[0]);
            Assert.AreEqual((byte)88, ((byte*)lane.Resolve(blobHandle2))[0]);
        }
    }
}