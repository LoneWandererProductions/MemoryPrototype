/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     MemoryManager.Tests
 * FILE:        ArenaMapTests.cs
 * PURPOSE:     MS Unit tests verifying ArenaMap open-addressing lookup, update, and growth behavior.
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

using MemoryManager.Core;
using MemoryManager.Types;

namespace MemoryManager.Tests
{
    /// <summary>
    /// Tests for the <see cref="ArenaMap{TKey, TValue}"/> open-addressing hash table.
    /// </summary>
    [TestClass]
    public sealed class ArenaMapTests
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
        public void ArenaMap_NullAllocator_ThrowsArgumentNullException()
        {
            Assert.ThrowsException<ArgumentNullException>(() => new ArenaMap<int, int>(null!));
        }

        /// <summary>
        /// Validates adding keys and fetching values.
        /// </summary>
        [TestMethod]
        [TestCategory("Collections")]
        public void ArenaMap_AddAndTryGetValue_ReturnsCorrectValues()
        {
            using var map = new ArenaMap<int, int>(_arena!, initialCapacity: 8);

            map.AddOrUpdate(10, 100);
            map.AddOrUpdate(20, 200);

            Assert.AreEqual(2, map.Count);
            Assert.IsTrue(map.TryGetValue(10, out int value1));
            Assert.AreEqual(100, value1);

            Assert.IsTrue(map.TryGetValue(20, out int value2));
            Assert.AreEqual(200, value2);

            Assert.IsFalse(map.TryGetValue(99, out _));
        }

        /// <summary>
        /// Validates updating an existing key replaces the value without increasing count.
        /// </summary>
        [TestMethod]
        [TestCategory("Collections")]
        public void ArenaMap_UpdateExistingKey_OverwritesValue()
        {
            using var map = new ArenaMap<int, int>(_arena!, initialCapacity: 8);

            map.AddOrUpdate(1, 500);
            Assert.AreEqual(1, map.Count);

            map.AddOrUpdate(1, 999);
            Assert.AreEqual(1, map.Count);

            Assert.IsTrue(map.TryGetValue(1, out int value));
            Assert.AreEqual(999, value);
        }

        /// <summary>
        /// Validates removing keys correctly updates count and frees slot for future lookup.
        /// </summary>
        [TestMethod]
        [TestCategory("Collections")]
        public void ArenaMap_Remove_RemovesKeyAndDecrementsCount()
        {
            using var map = new ArenaMap<int, int>(_arena!, initialCapacity: 8);

            map.AddOrUpdate(1, 10);
            map.AddOrUpdate(2, 20);

            Assert.IsTrue(map.Remove(1));
            Assert.AreEqual(1, map.Count);
            Assert.IsFalse(map.TryGetValue(1, out _));

            Assert.IsFalse(map.Remove(99)); // Key non-existent
        }

        /// <summary>
        /// Validates map expansion when exceeding the 70% load factor.
        /// </summary>
        [TestMethod]
        [TestCategory("Collections")]
        public void ArenaMap_Grow_PreservesAllElementsAfterExpansion()
        {
            using var map = new ArenaMap<int, int>(_arena!, initialCapacity: 8);

            // Insert 16 items to force table rehashing
            for (int i = 0; i < 16; i++)
            {
                map.AddOrUpdate(i, i * 10);
            }

            Assert.AreEqual(16, map.Count);
            Assert.IsTrue(map.Capacity > 8);

            for (int i = 0; i < 16; i++)
            {
                Assert.IsTrue(map.TryGetValue(i, out int val));
                Assert.AreEqual(i * 10, val);
            }
        }
    }
}