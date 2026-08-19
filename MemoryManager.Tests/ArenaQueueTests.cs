/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     MemoryManager.Tests
 * FILE:        ArenaQueueTests.cs
 * PURPOSE:     MS Unit tests verifying ArenaQueue FIFO correctness and capacity expansion.
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

using MemoryManager.Core;
using MemoryManager.Types;

namespace MemoryManager.Tests
{
    /// <summary>
    /// Tests for ArenaQueue
    /// </summary>
    [TestClass]
    public sealed class ArenaQueueTests
    {
        /// <summary>
        /// The arena
        /// </summary>
        private MemoryArena? _arena;

        /// <summary>
        /// Setups this instance.
        /// </summary>
        [TestInitialize]
        public void Setup()
        {
            var config = MemoryManagerConfig.CreateForGameLoop(4 * 1024 * 1024);
            _arena = new MemoryArena(config);
        }

        /// <summary>
        /// Cleanups this instance.
        /// </summary>
        [TestCleanup]
        public void Cleanup()
        {
            _arena?.Dispose();
        }

        /// <summary>
        /// Arenas the queue enqueue dequeue preserves fifo order.
        /// </summary>
        [TestMethod]
        public void ArenaQueue_EnqueueDequeue_PreservesFifoOrder()
        {
            using var queue = new ArenaQueue<int>(_arena, initialCapacity: 4);

            queue.Enqueue(10);
            queue.Enqueue(20);
            queue.Enqueue(30);

            Assert.AreEqual(3, queue.Count);
            Assert.AreEqual(10, queue.Peek());

            Assert.AreEqual(10, queue.Dequeue());
            Assert.AreEqual(20, queue.Dequeue());
            Assert.AreEqual(30, queue.Dequeue());
            Assert.AreEqual(0, queue.Count);
        }

        /// <summary>
        /// Arenas the queue enqueue beyond initial capacity grows automatically.
        /// </summary>
        [TestMethod]
        public void ArenaQueue_EnqueueBeyondInitialCapacity_GrowsAutomatically()
        {
            using var queue = new ArenaQueue<int>(_arena, initialCapacity: 2);

            for (var i = 0; i < 100; i++)
            {
                queue.Enqueue(i);
            }

            Assert.AreEqual(100, queue.Count);

            for (var i = 0; i < 100; i++)
            {
                Assert.AreEqual(i, queue.Dequeue());
            }
        }

        /// <summary>
        /// Arenas the queue dequeue empty throws invalid operation exception.
        /// </summary>
        [TestMethod]
        public void ArenaQueue_DequeueEmpty_ThrowsInvalidOperationException()
        {
            using var queue = new ArenaQueue<int>(_arena, initialCapacity: 4);
            Assert.ThrowsException<InvalidOperationException>(() => queue.Dequeue());
        }
    }
}