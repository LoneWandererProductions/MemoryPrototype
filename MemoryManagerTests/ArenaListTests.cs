using Core;
using MemoryManager;
/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     MemoryManagerTests
 * FILE:        ArenaListTests.cs
 * PURPOSE:     Test for our new types
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */


using MemoryManager.Types;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MemoryManagerTests
{
    [TestClass]
    public class ArenaListTests
    {
        /// <summary>
        /// Arenas the list basic operations work correctly.
        /// </summary>
        [TestMethod]
        [TestCategory("Collections")]
        public void ArenaList_BasicOperations_WorkCorrectly()
        {
            var config = new MemoryManagerConfig { FastLaneSize = 1024 * 1024 };
            var arena = new MemoryArena(config);
            var list = new ArenaList<int>(arena, initialCapacity: 4);

            // Add items
            list.Add(10);
            list.Add(20);
            list.Add(30);

            Assert.AreEqual(3, list.Count);
            Assert.AreEqual(10, list.Get(0));
            Assert.AreEqual(30, list.Get(2));
        }

        /// <summary>
        /// Arenas the list growth preserves data and frees old memory.
        /// </summary>
        [TestMethod]
        [TestCategory("Collections")]
        public void ArenaList_Growth_PreservesDataAndFreesOldMemory()
        {
            // Small arena to easily track changes
            var config = new MemoryManagerConfig
            {
                FastLaneSize = 1024,
                FastLaneStrategy = AllocatorStrategy.LinearBump
            };
            var arena = new MemoryArena(config);

            // Start with capacity 2
            var list = new ArenaList<int>(arena, initialCapacity: 2);
            list.Add(1);
            list.Add(2);

            int spaceBeforeGrow = arena.FastLane.FreeSpace();

            // This triggers Grow() -> Capacity becomes 4
            // It should allocate 16 bytes (4 * 4) and FREE 8 bytes (2 * 4)
            list.Add(3);

            Assert.AreEqual(3, list.Count);
            Assert.AreEqual(1, list.Get(0));
            Assert.AreEqual(2, list.Get(1));
            Assert.AreEqual(3, list.Get(2));

            // In a Linear/Bump lane, FreeSpace() decreases on every allocation.
            // However, the "Fragmentation" or "Wasted Space" should now include the old 8 bytes.
            Assert.IsTrue(arena.FastLane.EstimateFragmentation() > 0,
                "The old list buffer should be marked as fragmented/wasted after growth.");
        }

        /// <summary>
        /// Arenas the list as span allows high speed iteration.
        /// </summary>
        [TestMethod]
        [TestCategory("Collections")]
        public void ArenaList_AsSpan_AllowsHighSpeedIteration()
        {
            var arena = new MemoryArena(new MemoryManagerConfig());
            var list = new ArenaList<int>(arena);

            for (int i = 0; i < 10; i++) list.Add(i * 10);

            var span = list.AsSpan();
            int sum = 0;
            foreach (var val in span)
            {
                sum += val;
            }

            Assert.AreEqual(450, sum); // Sum of 0, 10, ... 90
        }
    }
}
