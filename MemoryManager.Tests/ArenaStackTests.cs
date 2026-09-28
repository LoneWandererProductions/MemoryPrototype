/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     MemoryManager.Tests
 * FILE:        ArenaStackTests.cs
 * PURPOSE:     MS Unit tests verifying ArenaStack LIFO correctness, growth, and boundary checks.
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

using MemoryManager.Core;
using MemoryManager.Types;

namespace MemoryManager.Tests
{
    /// <summary>
    /// Tests for the <see cref="ArenaStack{T}"/> LIFO collection type.
    /// </summary>
    [TestClass]
    public sealed class ArenaStackTests
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
        public void ArenaStack_NullAllocator_ThrowsArgumentNullException()
        {
            Assert.ThrowsException<ArgumentNullException>(() => new ArenaStack<int>(null!));
        }

        /// <summary>
        /// Validates that Push, Peek, and Pop operations preserve LIFO order.
        /// </summary>
        [TestMethod]
        [TestCategory("Collections")]
        public void ArenaStack_PushAndPop_PreservesLifoOrder()
        {
            using var stack = new ArenaStack<int>(_arena, initialCapacity: 4);

            stack.Push(100);
            stack.Push(200);
            stack.Push(300);

            Assert.AreEqual(3, stack.Count);
            Assert.AreEqual(300, stack.Peek());

            Assert.AreEqual(300, stack.Pop());
            Assert.AreEqual(200, stack.Pop());
            Assert.AreEqual(100, stack.Pop());
            Assert.AreEqual(0, stack.Count);
        }

        /// <summary>
        /// Validates that TryPop correctly returns items or false when empty.
        /// </summary>
        [TestMethod]
        [TestCategory("Collections")]
        public void ArenaStack_TryPop_HandlesPopSafely()
        {
            using var stack = new ArenaStack<int>(_arena, initialCapacity: 4);

            stack.Push(42);

            Assert.IsTrue(stack.TryPop(out var val));
            Assert.AreEqual(42, val);
            Assert.IsFalse(stack.TryPop(out _));
        }

        /// <summary>
        /// Validates that pushing beyond initial capacity triggers automatic buffer growth.
        /// </summary>
        [TestMethod]
        [TestCategory("Collections")]
        public void ArenaStack_PushBeyondCapacity_GrowsAutomatically()
        {
            using var stack = new ArenaStack<int>(_arena, initialCapacity: 2);

            for (var i = 0; i < 50; i++)
            {
                stack.Push(i);
            }

            Assert.AreEqual(50, stack.Count);
            Assert.IsTrue(stack.Capacity >= 50);

            for (var i = 49; i >= 0; i--)
            {
                Assert.AreEqual(i, stack.Pop());
            }
        }

        /// <summary>
        /// Validates that popping an empty stack throws InvalidOperationException.
        /// </summary>
        [TestMethod]
        [TestCategory("Collections")]
        public void ArenaStack_PopEmpty_ThrowsInvalidOperationException()
        {
            using var stack = new ArenaStack<int>(_arena, initialCapacity: 4);
            Assert.ThrowsException<InvalidOperationException>(() => stack.Pop());
        }
    }
}