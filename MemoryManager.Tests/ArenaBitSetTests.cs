/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     MemoryManager.Tests
 * FILE:        ArenaBitSetTests.cs
 * PURPOSE:     MS Unit tests verifying ArenaBitSet bitwise operations, clearing, and word expansion.
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

using MemoryManager.Core;
using MemoryManager.Types;

namespace MemoryManager.Tests
{
    /// <summary>
    /// Tests for the <see cref="ArenaBitSet"/> bit vector collection type.
    /// </summary>
    [TestClass]
    public sealed class ArenaBitSetTests
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
        public void ArenaBitSet_NullAllocator_ThrowsArgumentNullException()
        {
            Assert.ThrowsException<ArgumentNullException>(() => new ArenaBitSet(null!));
        }

        /// <summary>
        /// Validates setting, checking, and unsetting individual bit indices.
        /// </summary>
        [TestMethod]
        [TestCategory("Collections")]
        public void ArenaBitSet_SetAndGet_TogglesBitsCorrectly()
        {
            using var bitSet = new ArenaBitSet(_arena, initialBitCapacity: 64);

            bitSet.Set(0);
            bitSet.Set(31);
            bitSet.Set(63);

            Assert.IsTrue(bitSet.Get(0));
            Assert.IsTrue(bitSet.Get(31));
            Assert.IsTrue(bitSet.Get(63));
            Assert.IsFalse(bitSet.Get(1));

            bitSet.Unset(31);
            Assert.IsFalse(bitSet.Get(31));
        }

        /// <summary>
        /// Validates that setting bits beyond initial capacity automatically resizes the bitset.
        /// </summary>
        [TestMethod]
        [TestCategory("Collections")]
        public void ArenaBitSet_SetBeyondCapacity_GrowsBuffer()
        {
            using var bitSet = new ArenaBitSet(_arena, initialBitCapacity: 64);

            bitSet.Set(500);

            Assert.IsTrue(bitSet.Get(500));
            Assert.IsTrue(bitSet.BitCapacity >= 501);
            Assert.IsFalse(bitSet.Get(499));
        }

        /// <summary>
        /// Validates that Clear unsets all bits across allocated ulong words.
        /// </summary>
        [TestMethod]
        [TestCategory("Collections")]
        public void ArenaBitSet_Clear_ResetsAllBits()
        {
            using var bitSet = new ArenaBitSet(_arena, initialBitCapacity: 128);

            bitSet.Set(10);
            bitSet.Set(85);

            bitSet.Clear();

            Assert.IsFalse(bitSet.Get(10));
            Assert.IsFalse(bitSet.Get(85));
        }
    }
}