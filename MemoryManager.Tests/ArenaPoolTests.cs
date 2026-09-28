/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     MemoryManager.Tests
 * FILE:        ArenaPoolTests.cs
 * PURPOSE:     MS Unit tests verifying ArenaPool rent/return cycling and memory reuse.
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

using MemoryManager.Core;
using MemoryManager.Types;

namespace MemoryManager.Tests
{
    /// <summary>
    /// Tests for the <see cref="ArenaPool{T}"/> object instance recycling pool.
    /// </summary>
    [TestClass]
    public sealed class ArenaPoolTests
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
        public void ArenaPool_NullAllocator_ThrowsArgumentNullException()
        {
            Assert.ThrowsException<ArgumentNullException>(() => new ArenaPool<int>(null!));
        }

        /// <summary>
        /// Validates renting items and accessing them by index reference.
        /// </summary>
        [TestMethod]
        [TestCategory("Collections")]
        public void ArenaPool_RentAndGet_ReturnsValidSlots()
        {
            using var pool = new ArenaPool<int>(_arena, initialCapacity: 8);

            ref var slot1 = ref pool.Rent(out var index1);
            slot1 = 42;

            ref var slot2 = ref pool.Rent(out var index2);
            slot2 = 84;

            Assert.AreNotEqual(index1, index2);
            Assert.AreEqual(42, pool.Get(index1));
            Assert.AreEqual(84, pool.Get(index2));
            Assert.AreEqual(2, pool.ActiveCount);
        }

        /// <summary>
        /// Validates returning an index recycles that slot for future Rent calls.
        /// </summary>
        [TestMethod]
        [TestCategory("Collections")]
        public void ArenaPool_Return_RecyclesIndices()
        {
            using var pool = new ArenaPool<int>(_arena, initialCapacity: 4);

            _ = ref pool.Rent(out var idx1);
            _ = ref pool.Rent(out var idx2);

            pool.Return(idx1);
            Assert.AreEqual(1, pool.ActiveCount);

            _ = ref pool.Rent(out var recycledIdx);
            Assert.AreEqual(idx1, recycledIdx);
            Assert.AreEqual(2, pool.ActiveCount);
        }

        /// <summary>
        /// Validates that renting past initial capacity triggers growth without breaking active items.
        /// </summary>
        [TestMethod]
        [TestCategory("Collections")]
        public void ArenaPool_RentBeyondCapacity_GrowsPool()
        {
            using var pool = new ArenaPool<int>(_arena, initialCapacity: 2);

            _ = ref pool.Rent(out var idx0);
            _ = ref pool.Rent(out var idx1);
            pool.Get(idx0) = 10;
            pool.Get(idx1) = 20;

            _ = ref pool.Rent(out var idx2);
            pool.Get(idx2) = 30;

            Assert.AreEqual(3, pool.ActiveCount);
            Assert.IsTrue(pool.Capacity >= 3);
            Assert.AreEqual(10, pool.Get(idx0));
            Assert.AreEqual(20, pool.Get(idx1));
            Assert.AreEqual(30, pool.Get(idx2));
        }
    }
}