/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     MemoryManagerTests
 * FILE:        FastLaneTests.cs
 * PURPOSE:     Tests for verifying compaction behavior in FastLane
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Lanes;
using Core;
using System.Diagnostics;
using MemoryManager;

namespace MemoryManagerTests
{
    [TestClass]
    public class FastLaneTests
    {
        /// <summary>
        /// The FastLane
        /// </summary>
        private FastLane _fastLane;

        /// <summary>
        /// The configuration
        /// </summary>
        private MemoryManagerConfig _config;

        /// <summary>
        /// Setups this instance.
        /// </summary>
        [TestInitialize]
        public void Setup()
        {
            var slowLane = new SlowLane(1024 * 1024); // 1MB for slow lane
            _fastLane = new FastLane(1024 * 1024, slowLane); // 1MB fast lane
            _fastLane.OneWayLane = new OneWayLane(1024, _fastLane, slowLane); // Required setup

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
        ///     Moves the fast to slow moves entry and replaces stub.
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

        /// <summary>
        /// FastLane compact removes gaps after freed entries.
        /// </summary>
        [TestMethod]
        public void FastLaneCompactRemovesGapsAfterFreedEntries()
        {
            // Arrange
            var handles = new MemoryHandle[6];

            for (var i = 0; i < 6; i++)
            {
                handles[i] = _fastLane.Allocate(128); // 128 bytes each
                Assert.IsTrue(_fastLane.HasHandle(handles[i]), $"Handle {i} should be allocated.");
            }

            //Debug 
            Trace.WriteLine("first:");
            _fastLane.LogDump();

            // Free 2nd and 4th
            _fastLane.Free(handles[1]);
            _fastLane.Free(handles[3]);

            Assert.IsFalse(_fastLane.HasHandle(handles[1]), "Handle 1 should have been freed.");
            Assert.IsFalse(_fastLane.HasHandle(handles[3]), "Handle 3 should have been freed.");

            var usedBefore = _fastLane.UsagePercentage();
            //Debug 
            Trace.WriteLine("Second:");
            _fastLane.LogDump();


            // Act
            _fastLane.Compact();

            // Assert
            Assert.IsTrue(_fastLane.HasHandle(handles[0]), "Handle 0 should still be valid after compaction.");
            Assert.IsTrue(_fastLane.HasHandle(handles[2]), "Handle 2 should still be valid after compaction.");
            Assert.IsTrue(_fastLane.HasHandle(handles[4]), "Handle 4 should still be valid after compaction.");
            Assert.IsTrue(_fastLane.HasHandle(handles[5]), "Handle 5 should still be valid after compaction.");

            var usedAfter = _fastLane.UsagePercentage();
            Assert.IsTrue(usedAfter <= usedBefore, "Compaction should ideally not increase usage.");

            //Debug 
            Trace.WriteLine("Third:");
            _fastLane.LogDump();

        }
    }
}
