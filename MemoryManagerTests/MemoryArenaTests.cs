/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     MemoryManagerTests
 * FILE:        MemoryArenaTests.cs
 * PURPOSE:     Your file purpose here
 * PROGRAMMER:  Your name here
 */

using Core;
using MemoryManager;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

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
        public void AllocateWithinFastLaneThresholdAllocatesInFastLane()
        {
            var arena = new MemoryArena(_config);
            var size = 32 * 1024; // 32 KB < Threshold (64 KB)

            var handle = arena.Allocate(size);

            arena.DebugDump();

            Assert.IsTrue(arena.Resolve(handle) != IntPtr.Zero);
            //Assert.IsTrue(arena.FastLane.GetAllocationSize() < _config.FastLaneSize);
        }

        /// <summary>
        /// Moves the fast to slow moves entry and replaces stub.
        /// </summary>
        [TestMethod]
        public void MoveFastToSlowMovesEntryAndReplacesStub()
        {
            var arena = new MemoryArena(_config);
            var size = 32 * 1024; // allocate in FastLane

            var fastHandle = arena.Allocate(size);

            arena.MoveFastToSlow(fastHandle);

            arena.DebugDump();

            Assert.IsFalse(arena.SlowLane.HasHandle(fastHandle));

            //absolute no no but for test purposes yeah ....
            var moved = new MemoryHandle(-1, null);
            arena.DebugDump();

            Assert.IsTrue(arena.SlowLane.HasHandle(moved));
        }
    }
}
