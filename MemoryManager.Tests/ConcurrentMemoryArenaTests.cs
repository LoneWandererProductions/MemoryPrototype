/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     MemoryManager.Tests
 * FILE:        ConcurrentMemoryArenaTests.cs
 * PURPOSE:     Concurrency, thread-isolation, and cross-thread freeing verification for ConcurrentMemoryArena.
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

using System.Collections.Concurrent;
using MemoryManager.Core;

namespace MemoryManager.Tests
{
    [TestClass]
    public class ConcurrentMemoryArenaTests
    {
        /// <summary>
        /// Concurrency Test: Fires up parallel worker tasks simultaneously.
        /// Verifies that every thread allocates out of its own isolated lock-free fast lane
        /// without block cross-contamination or address collision crashes.
        /// </summary>
        [TestMethod]
        [TestCategory("Arena_Concurrency")]
        public void Arena_ParallelAllocation_MaintainsThreadIsolation()
        {
            // Arrange
            var config = new MemoryManagerConfig
            {
                FastLaneSize = 256 * 1024,      // 256 KB per thread lane
                SlowLaneSize = 2 * 1024 * 1024, // 2 MB shared backup
                Threshold = 128,
                FastLaneStrategy = AllocatorStrategy.Slab // Test with our high-speed Slab strategy
            };

            var arena = new ConcurrentMemoryArena(config);
            const int threadCount = 8;
            const int allocationsPerThread = 100;

            // Thread-safe collection to aggregate all generated handles across parallel tasks
            var allHandles = new ConcurrentBag<MemoryHandle>();
            var tasks = new List<Task>();

            // Act
            for (int t = 0; t < threadCount; t++)
            {
                tasks.Add(Task.Run(() =>
                {
                    for (int i = 0; i < allocationsPerThread; i++)
                    {
                        // Every thread allocates concurrently under zero-lock hot paths
                        var handle = arena.Allocate(32);
                        allHandles.Add(handle);

                        // Basic write verification to hit cache-lines simultaneously
                        unsafe
                        {
                            int* ptr = (int*)arena.Resolve(handle);
                            *ptr = Thread.CurrentThread.ManagedThreadId;
                        }
                    }
                }));
            }

            // Wait for all worker threads to finish their flooding passes
            Task.WaitAll(tasks.ToArray());

            // Assert
            Assert.AreEqual(threadCount * allocationsPerThread, allHandles.Count, "All requested allocations must succeed across threads.");

            // Verify address uniqueness across the batch
            var uniqueAddresses = new HashSet<nint>();
            foreach (var handle in allHandles)
            {
                nint ptr = arena.Resolve(handle);
                Assert.IsTrue(uniqueAddresses.Add(ptr), $"Duplicate address collision discovered at pointer: {ptr}");

                // Free the handles to verify concurrent deallocation pathways
                arena.Free(handle);
            }

            arena.DebugDump(); // Optional: Visualize the arena state after the test
            arena.Dispose();
        }

        /// <summary>
        /// Concurrency Test: Verifies the "Chaos Path" where Thread A allocates an object,
        /// hands it to Thread B, and Thread B calls Free(). 
        /// Ensures the handle safely passes through the ConcurrentQueue and is recycled by Thread A.
        /// </summary>
        [TestMethod]
        [TestCategory("Arena_Concurrency")]
        public unsafe void Arena_CrossThreadFree_RecyclesViaRemoteQueue()
        {
            // Arrange
            var config = new MemoryManagerConfig
            {
                FastLaneSize = 64 * 1024,
                SlowLaneSize = 256 * 1024,
                Threshold = 64,
                FastLaneStrategy = AllocatorStrategy.Slab // Slab allows us to easily track slot index recycling
            };

            var arena = new ConcurrentMemoryArena(config);
            MemoryHandle sharedHandle = default;
            nint originalPointer = nint.Zero;

            // ManualResetEvents to orchestrate our two worker threads cleanly
            using var allocationComplete = new ManualResetEvent(false);
            using var freeComplete = new ManualResetEvent(false);

            // 1. Thread A: Allocates a 32-byte slot
            var threadA = new Thread(() =>
            {
                sharedHandle = arena.Allocate(32);
                originalPointer = arena.Resolve(sharedHandle);
                *(int*)originalPointer = 999;

                allocationComplete.Set(); // Wake up Thread B
                freeComplete.WaitOne();   // Wait until Thread B frees our slot

                // Thread A triggers a new allocation. This forces it to drain its remote inbox queue first!
                var recycledHandle = arena.Allocate(32);
                nint recycledPointer = arena.Resolve(recycledHandle);

                // ASSERT: The slot must be perfectly recycled back onto Thread A's local stack context
                Assert.AreEqual(originalPointer, recycledPointer, "Thread A must successfully reclaim its slot after Thread B remote-freed it.");
            });

            // 2. Thread B: Intercepts the handle and deletes it cross-thread lines
            var threadB = new Thread(() =>
            {
                allocationComplete.WaitOne(); // Wait until Thread A finishes allocation

                // Verify the payload survived data transitions
                int currentVal = *(int*)arena.Resolve(sharedHandle);
                Assert.AreEqual(999, currentVal);

                // CROSS-THREAD FREE: Thread B deletes memory it doesn't own
                arena.Free(sharedHandle);

                freeComplete.Set(); // Wake up Thread A
            });

            // Act
            threadA.Start();
            threadB.Start();

            threadA.Join();
            threadB.Join();

            arena.DebugDump(); // Optional: Visualize the arena state after the test
            // Cleanup
            arena.Dispose();
        }

        /// <summary>
        /// Edge Case Test: Verifies that allocations exceeding the fast-lane threshold
        /// bypass thread-local memory structures completely and fall back cleanly into the
        /// thread-safe locked global SlowLane pool.
        /// </summary>
        [TestMethod]
        [TestCategory("Arena_Concurrency")]
        public void Arena_LargeAllocations_FallbackToGlobalSlowLane()
        {
            // Arrange
            var config = new MemoryManagerConfig
            {
                FastLaneSize = 64 * 1024,
                SlowLaneSize = 512 * 1024,
                Threshold = 128 // Allocations greater than 128 bytes fall down to SlowLane
            };

            var arena = new ConcurrentMemoryArena(config);

            // Act
            // Allocate 200 bytes (Exceeds 128 byte threshold)
            var largeHandle = arena.Allocate(200);

            // Assert
            Assert.IsTrue(largeHandle.Id < 0, "Allocations shifting to the SlowLane must register with negative handle identifiers.");

            var entry = arena.GetEntry(largeHandle);
            Assert.AreEqual(200, entry.Size);
            Assert.IsFalse(entry.IsStub);

            // Clean verification pass
            arena.Free(largeHandle);

            arena.LogDump(); // Optional: Visualize the arena state after the test
            arena.Dispose();
        }
    }
}