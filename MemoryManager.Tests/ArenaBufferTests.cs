/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     MemoryManager.Tests
 * FILE:        ArenaBufferTests.cs
 * PURPOSE:     MS Unit tests verifying ArenaBuffer correctness and capacity limits.
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

using MemoryManager.Core;
using MemoryManager.Types;

namespace MemoryManager.Tests
{
    /// <summary>
    /// Simple Arena tests.
    /// </summary>
    [TestClass]
    public sealed class ArenaBufferTests
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
        /// Arenas the buffer add and read validates data integrity.
        /// </summary>
        [TestMethod]
        public void ArenaBuffer_AddAndRead_ValidatesDataIntegrity()
        {
            using var buffer = new ArenaBuffer<int>(_arena, 10);

            for (var i = 0; i < 10; i++)
            {
                buffer.Add(i * 10);
            }

            Assert.AreEqual(10, buffer.Count);
            Assert.AreEqual(10, buffer.Capacity);

            for (var i = 0; i < 10; i++)
            {
                Assert.AreEqual(i * 10, buffer[i]);
            }
        }

        /// <summary>
        /// Arenas the buffer exceed capacity throws invalid operation exception.
        /// </summary>
        [TestMethod]
        public void ArenaBuffer_ExceedCapacity_ThrowsInvalidOperationException()
        {
            using var buffer = new ArenaBuffer<int>(_arena, 3);

            buffer.Add(1);
            buffer.Add(2);
            buffer.Add(3);

            Assert.ThrowsException<InvalidOperationException>(() => buffer.Add(4));
        }

        /// <summary>
        /// Arenas the buffer clear resets count without reallocating.
        /// </summary>
        [TestMethod]
        public void ArenaBuffer_Clear_ResetsCountWithoutReallocating()
        {
            using var buffer = new ArenaBuffer<int>(_arena, 5);

            buffer.Add(100);
            buffer.Add(200);
            Assert.AreEqual(2, buffer.Count);

            buffer.Clear();
            Assert.AreEqual(0, buffer.Count);
            Assert.AreEqual(5, buffer.Capacity); // Memory reserved, zero allocation overhead

            buffer.Add(300);
            Assert.AreEqual(1, buffer.Count);
            Assert.AreEqual(300, buffer[0]);
        }

        /// <summary>
        /// Arenas the buffer index out of bounds throws index out of range exception.
        /// </summary>
        [TestMethod]
        public void ArenaBuffer_IndexOutOfBounds_ThrowsIndexOutOfRangeException()
        {
            using var buffer = new ArenaBuffer<int>(_arena, 5);
            buffer.Add(42);

            Assert.ThrowsException<IndexOutOfRangeException>(() => _ = buffer[1]);
            Assert.ThrowsException<IndexOutOfRangeException>(() => _ = buffer[-1]);
        }
    }
}