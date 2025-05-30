using Core;
using MemoryManager;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Formats.Asn1;

namespace MemoryManagerTests
{
    [TestClass]
    public class MemoryArenaTests
    {
        private MemoryManagerConfig _config;

        [TestInitialize]
        public void Setup()
        {
            _config = new MemoryManagerConfig
            {
                FastLaneSize = 1024 * 1024,          // 1 MB
                SlowLaneSize = 4 * 1024 * 1024,      // 4 MB
                BufferSize = 256 * 1024,              // 256 KB
                Threshold = 64 * 1024,                // 64 KB
                FastLaneUsageThreshold = 0.9f,
                SlowLaneUsageThreshold = 0.85f,
                CompactionThreshold = 0.9f,
                SlowLaneSafetyMargin = 0.10f,
                EnableAutoCompaction = true,
                PolicyCheckInterval = TimeSpan.Zero  // no timer for test
            };
        }

        [TestMethod]
        public void Allocate_WithinFastLaneThreshold_AllocatesInFastLane()
        {
            var arena = new MemoryArena(_config);
            var size = 32 * 1024; // 32 KB < Threshold (64 KB)

            var handle = arena.Allocate(size);

            arena.DebugDump();

            Assert.IsTrue(arena.Resolve(handle) != IntPtr.Zero);
            //Assert.IsTrue(arena.FastLane.GetAllocationSize() < _config.FastLaneSize);
        }

        [TestMethod]
        public void Allocate_LargerThanThreshold_AllocatesInSlowLane()
        {
            var arena = new MemoryArena(_config);
            var size = 128 * 1024; // 128 KB > Threshold (64 KB)

            var handle = arena.Allocate(size);

            arena.DebugDump();

            Assert.IsTrue(arena.Resolve(handle) != IntPtr.Zero);
            Assert.IsTrue(arena.SlowLane.FreeSpace() < _config.SlowLaneSize);
        }

        /// <summary>
        /// Moves the fast to slow moves entry and replaces stub.
        /// </summary>
        [TestMethod]
        public void MoveFastToSlow_MovesEntryAndReplacesStub()
        {
            var arena = new MemoryArena(_config);
            var size = 32 * 1024; // allocate in FastLane

            var fastHandle = arena.Allocate(size);

            arena.MoveFastToSlow(fastHandle);

            arena.DebugDump();

            Assert.IsFalse(arena.SlowLane.HasHandle(fastHandle));

            //absolute no no but for test purposes yeah ....
            var moved = new MemoryHandle(1000, null);
            arena.DebugDump();

            Assert.IsTrue(arena.SlowLane.HasHandle(moved));
        }

        [TestMethod]
        public void CompactFastLane_TriggeredWhenUsageHighAndStubPresent()
        {
            var arena = new MemoryArena(_config);

            // Fill FastLane to > 90%
            var size = (int)(_config.FastLaneSize * 0.91);
            var handle = arena.Allocate(size);

            // Mark allocation as cold to trigger move
            SetAllocationHints(arena, handle, AllocationHints.Cold);

            arena.RunMaintenanceCycle();
            arena.DebugDump();

            Assert.IsTrue(GetFastLaneUsage(arena) < 0.9f);
        }


        // Helper reflection-based or internal methods to access internals of arena for test:
        private static void SetAllocationHints(MemoryArena arena, MemoryHandle handle, AllocationHints hints)
        {
            var fastLaneField = typeof(MemoryArena).GetField("FastLane", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            dynamic fastLane = fastLaneField.GetValue(arena);

            var entry = fastLane.GetEntry(handle);
            entry.Hints = hints;
        }

        private static float GetFastLaneUsage(MemoryArena arena)
        {
            var fastLaneField = typeof(MemoryArena).GetField("FastLane", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            dynamic fastLane = fastLaneField.GetValue(arena);
            return fastLane.UsagePercentage();
        }
    }
}
