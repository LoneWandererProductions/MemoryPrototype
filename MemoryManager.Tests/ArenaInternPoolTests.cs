/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     MemoryManager.Tests
 * FILE:        ArenaInternPoolTests.cs
 * PURPOSE:     MS Unit tests verifying ArenaInternPool interning, reference counting, and memory reuse.
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

using MemoryManager.Core;
using MemoryManager.Types;

namespace MemoryManager.Tests
{
    /// <summary>
    /// Tests for the <see cref="ArenaInternPool{T}"/> deduplicating object pool.
    /// </summary>
    [TestClass]
    public sealed class ArenaInternPoolTests
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
        /// Validates that null allocator throws ArgumentNullException during initialization.
        /// </summary>
        [TestMethod]
        [TestCategory("Collections")]
        public void ArenaInternPool_NullAllocator_ThrowsArgumentNullException()
        {
            Assert.ThrowsException<ArgumentNullException>(() => new ArenaInternPool<int>(null!));
        }

        /// <summary>
        /// Validates adding unique items assigns distinct slots and initializes reference count.
        /// </summary>
        [TestMethod]
        [TestCategory("Collections")]
        public void ArenaInternPool_AddUniqueItems_AllocatesDistinctSlots()
        {
            using var pool = new ArenaInternPool<int>(_arena!, initialCapacity: 8);

            int idx1 = pool.Add(42);
            int idx2 = pool.Add(84);

            Assert.AreNotEqual(idx1, idx2);
            Assert.AreEqual(42, pool.Get(idx1));
            Assert.AreEqual(84, pool.Get(idx2));
            Assert.AreEqual(1, pool.GetRefCount(idx1));
            Assert.AreEqual(1, pool.GetRefCount(idx2));
            Assert.AreEqual(2, pool.UniqueCount);
        }

        /// <summary>
        /// Validates that adding identical data returns the existing index and increments the reference count.
        /// </summary>
        [TestMethod]
        [TestCategory("Collections")]
        public void ArenaInternPool_AddDuplicate_DeduplicatesAndIncrementsRefCount()
        {
            using var pool = new ArenaInternPool<int>(_arena!, initialCapacity: 8);

            int idx1 = pool.Add(42);
            int idx2 = pool.Add(42);

            Assert.AreEqual(idx1, idx2);
            Assert.AreEqual(2, pool.GetRefCount(idx1));
            Assert.AreEqual(1, pool.UniqueCount);
        }

        /// <summary>
        /// Validates that releasing an index decrements reference count and only frees the slot when count reaches zero.
        /// </summary>
        [TestMethod]
        [TestCategory("Collections")]
        public void ArenaInternPool_Release_DecrementsRefCountAndFreesAtZero()
        {
            using var pool = new ArenaInternPool<int>(_arena!, initialCapacity: 4);

            int idx1 = pool.Add(100);
            pool.Add(100); // RefCount = 2

            Assert.IsTrue(pool.Release(idx1));
            Assert.AreEqual(1, pool.GetRefCount(idx1));
            Assert.AreEqual(1, pool.UniqueCount);

            Assert.IsTrue(pool.Release(idx1));
            Assert.AreEqual(0, pool.GetRefCount(idx1));
            Assert.AreEqual(0, pool.UniqueCount);

            // Re-adding 100 should recycle a slot now that it was fully released
            int idx3 = pool.Add(100);
            Assert.AreEqual(idx1, idx3);
            Assert.AreEqual(1, pool.GetRefCount(idx3));
        }

        /// <summary>
        /// Validates that adding beyond initial capacity triggers pool growth while retaining interned items and reference counts.
        /// </summary>
        [TestMethod]
        [TestCategory("Collections")]
        public void ArenaInternPool_AddBeyondCapacity_GrowsPoolAndPreservesData()
        {
            using var pool = new ArenaInternPool<int>(_arena!, initialCapacity: 2);

            int idx0 = pool.Add(10);
            int idx1 = pool.Add(20);
            pool.Add(10); // RefCount for 10 = 2

            int idx2 = pool.Add(30); // Triggers growth

            Assert.IsTrue(pool.Capacity >= 3);
            Assert.AreEqual(3, pool.UniqueCount);

            Assert.AreEqual(10, pool.Get(idx0));
            Assert.AreEqual(20, pool.Get(idx1));
            Assert.AreEqual(30, pool.Get(idx2));

            Assert.AreEqual(2, pool.GetRefCount(idx0));
            Assert.AreEqual(1, pool.GetRefCount(idx1));
            Assert.AreEqual(1, pool.GetRefCount(idx2));
        }

        /// <summary>
        /// Validates releasing an unallocated slot returns false without throwing.
        /// </summary>
        [TestMethod]
        [TestCategory("Collections")]
        public void ArenaInternPool_ReleaseUnallocatedSlot_ReturnsFalse()
        {
            using var pool = new ArenaInternPool<int>(_arena!, initialCapacity: 4);

            int idx = pool.Add(50);
            pool.Release(idx);

            bool releasedAgain = pool.Release(idx);
            Assert.IsFalse(releasedAgain);
        }
    }
}