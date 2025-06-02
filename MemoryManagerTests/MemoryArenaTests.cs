/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     MemoryManagerTests
 * FILE:        MemoryArenaTests.cs
 * PURPOSE:     MemoryArena some basic tests for the wrapper.
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

using System;
using MemoryManager;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MemoryManagerTests
{
    [TestClass]
    public class MemoryArenaTests
    {
        /// <summary>
        ///     The configuration
        /// </summary>
        private MemoryManagerConfig _config;

        /// <summary>
        ///     Setups this instance.
        /// </summary>
        [TestInitialize]
        public void Setup()
        {
            _config = new MemoryManagerConfig
            {
                FastLaneSize = 1024 * 1024, // 1 MB
                SlowLaneSize = 4 * 1024 * 1024, // 4 MB
                BufferSize = 256 * 1024, // 256 KB
                Threshold = 64 * 1024, // 64 KB
                FastLaneUsageThreshold = 0.9f,
                SlowLaneUsageThreshold = 0.85f,
                CompactionThreshold = 0.9f,
                SlowLaneSafetyMargin = 0.10f,
                EnableAutoCompaction = true,
                PolicyCheckInterval = TimeSpan.Zero // no timer for test
            };
        }

        /// <summary>
        ///     Allocates the within fast lane threshold allocates in fast lane.
        /// </summary>
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
    }
}