/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     MemoryManager.Tests
 * FILE:        ArenaSparseSetTests.cs
 * PURPOSE:     MS Unit tests verifying ArenaSparseSet O(1) lookup, swap-back removal, and contiguous span iteration.
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

using MemoryManager.Core;
using MemoryManager.Types;

namespace MemoryManager.Tests
{
    /// <summary>
    /// Tests for the <see cref="ArenaSparseSet{T}"/> sparse entity container.
    /// </summary>
    [TestClass]
    public sealed class ArenaSparseSetTests
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
        public void ArenaSparseSet_NullAllocator_ThrowsArgumentNullException()
        {
            Assert.ThrowsException<ArgumentNullException>(() => new ArenaSparseSet<int>(null!));
        }

        /// <summary>
        /// Validates adding sparse entity IDs and retrieving components.
        /// </summary>
        [TestMethod]
        [TestCategory("Collections")]
        public void ArenaSparseSet_AddAndGet_ReturnsComponentData()
        {
            using var set = new ArenaSparseSet<float>(_arena!, initialMaxEntityId: 100, initialDenseCapacity: 8);

            set.Add(42, 3.14f);
            set.Add(105, 2.71f); // Triggers sparse growth

            Assert.AreEqual(2, set.Count);
            Assert.IsTrue(set.Contains(42));
            Assert.IsTrue(set.Contains(105));
            Assert.IsFalse(set.Contains(7));

            Assert.AreEqual(3.14f, set.Get(42));
            Assert.AreEqual(2.71f, set.Get(105));
        }

        /// <summary>
        /// Validates that removing an entity uses swap-back to keep active components tightly packed in memory.
        /// </summary>
        [TestMethod]
        [TestCategory("Collections")]
        public void ArenaSparseSet_Remove_PreservesContiguousPackedSpan()
        {
            using var set = new ArenaSparseSet<int>(_arena!, initialMaxEntityId: 64, initialDenseCapacity: 8);

            set.Add(10, 100);
            set.Add(20, 200);
            set.Add(30, 300);

            Assert.IsTrue(set.Remove(20));
            Assert.AreEqual(2, set.Count);
            Assert.IsFalse(set.Contains(20));

            // Verify the packed span now contains exactly the remaining 2 components without gaps
            Span<int> span = set.AsSpan();
            Assert.AreEqual(2, span.Length);
            Assert.IsTrue(span.Contains(100));
            Assert.IsTrue(span.Contains(300));
        }

        /// <summary>
        /// Validates AsSpan returns an empty span when set has no active items.
        /// </summary>
        [TestMethod]
        [TestCategory("Collections")]
        public void ArenaSparseSet_EmptyAsSpan_ReturnsEmptySpan()
        {
            using var set = new ArenaSparseSet<int>(_arena!);
            Assert.AreEqual(0, set.AsSpan().Length);
        }
    }
}