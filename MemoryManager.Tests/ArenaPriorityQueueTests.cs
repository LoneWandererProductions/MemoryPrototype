/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     MemoryManager.Tests
 * FILE:        ArenaPriorityQueueTests.cs
 * PURPOSE:     MS Unit tests verifying ArenaPriorityQueue binary min-heap ordering and growth.
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

using MemoryManager.Core;
using MemoryManager.Types;

namespace MemoryManager.Tests
{
    /// <summary>
    /// Tests for the <see cref="ArenaPriorityQueue{TElement, TPriority}"/> binary min-heap.
    /// </summary>
    [TestClass]
    public sealed class ArenaPriorityQueueTests
    {
        private MemoryArena? _arena;

        [TestInitialize]
        public void Setup()
        {
            var config = MemoryManagerConfig.CreateForGameLoop(4 * 1024 * 1024);
            _arena = new MemoryArena(config);
        }

        [TestCleanup]
        public void Cleanup()
        {
            _arena?.Dispose();
        }

        /// <summary>
        /// Validates that null allocator throws ArgumentNullException during initialization.
        /// </summary>
        [TestMethod]
        [TestCategory("Collections")]
        public void ArenaPriorityQueue_NullAllocator_ThrowsArgumentNullException()
        {
            Assert.ThrowsException<ArgumentNullException>(() => new ArenaPriorityQueue<int, int>(null!));
        }

        /// <summary>
        /// Validates items are dequeued in ascending priority order (Min-Heap behavior).
        /// </summary>
        [TestMethod]
        [TestCategory("Collections")]
        public void ArenaPriorityQueue_EnqueueAndDequeue_OrdersByMinPriority()
        {
            using var queue = new ArenaPriorityQueue<int, int>(_arena!, initialCapacity: 8);

            // Payload IDs: 101 (Low), 102 (Critical), 103 (Medium)
            queue.Enqueue(101, 50); // Priority 50 (Low)
            queue.Enqueue(102, 1);  // Priority 1  (Critical)
            queue.Enqueue(103, 10); // Priority 10 (Medium)

            Assert.AreEqual(3, queue.Count);

            Assert.AreEqual(102, queue.Dequeue()); // Priority 1
            Assert.AreEqual(103, queue.Dequeue()); // Priority 10
            Assert.AreEqual(101, queue.Dequeue()); // Priority 50
            Assert.AreEqual(0, queue.Count);
        }

        /// <summary>
        /// Validates Peek returns the min-priority element without removing it.
        /// </summary>
        [TestMethod]
        [TestCategory("Collections")]
        public void ArenaPriorityQueue_Peek_ReturnsMinPriorityWithoutRemoving()
        {
            using var queue = new ArenaPriorityQueue<int, float>(_arena!, initialCapacity: 8);

            queue.Enqueue(100, 1.5f);
            queue.Enqueue(200, 0.2f);

            ref int topElement = ref queue.Peek(out float priority);

            Assert.AreEqual(200, topElement);
            Assert.AreEqual(0.2f, priority);
            Assert.AreEqual(2, queue.Count); // Count unchanged
        }

        /// <summary>
        /// Validates that enqueuing items past initial capacity triggers heap growth and preserves ordering.
        /// </summary>
        [TestMethod]
        [TestCategory("Collections")]
        public void ArenaPriorityQueue_Grow_PreservesHeapOrdering()
        {
            using var queue = new ArenaPriorityQueue<int, int>(_arena!, initialCapacity: 2);

            // Insert out of order to force growth and re-sifting
            queue.Enqueue(10, 100);
            queue.Enqueue(20, 20);
            queue.Enqueue(30, 50); // Growth trigger

            Assert.AreEqual(3, queue.Count);
            Assert.IsTrue(queue.Capacity >= 3);

            Assert.AreEqual(20, queue.Dequeue()); // Priority 20
            Assert.AreEqual(30, queue.Dequeue()); // Priority 50
            Assert.AreEqual(10, queue.Dequeue()); // Priority 100
        }

        /// <summary>
        /// Validates dequeuing an empty queue throws InvalidOperationException.
        /// </summary>
        [TestMethod]
        [TestCategory("Collections")]
        public void ArenaPriorityQueue_DequeueEmpty_ThrowsInvalidOperationException()
        {
            using var queue = new ArenaPriorityQueue<int, int>(_arena!);
            Assert.ThrowsException<InvalidOperationException>(() => queue.Dequeue());
        }
    }
}