/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     MemoryManagerTests
 * FILE:        SlowLaneTests.cs
 * PURPOSE:     Tests for verifying compaction behavior in SlowLane
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

using System;
using System.Diagnostics;
using Core;
using Lanes;
using MemoryManager;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MemoryManagerTests
{
    [TestClass]
    public class SlowLaneTests
    {
        /// <summary>
        ///     The configuration
        /// </summary>
        private MemoryManagerConfig _config;

        /// <summary>
        ///     The FastLane
        /// </summary>
        private SlowLane _slowLane;

        /// <summary>
        ///     Setups this instance.
        /// </summary>
        [TestInitialize]
        public void Setup()
        {
            _slowLane = new SlowLane(1024 * 1024); // 1MB fast lane

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
        public void MoveSlowToFastMovesEntryAndReplacesStub()
        {
            var arena = new MemoryArena(_config);
            var size = 32 * 1024; // allocate in FastLane

            var fastHandle = arena.Allocate(size);

            arena.MoveFastToSlow(fastHandle);

            arena.DebugDump();

            Assert.IsFalse(arena.SlowLane.HasHandle(fastHandle));

            //absolute no no but for test purposes yeah ....
            var moved = new MemoryHandle(-1, _slowLane);
            arena.DebugDump();

            Assert.IsTrue(arena.SlowLane.HasHandle(moved));

            moved = arena.MoveSlowToFast(moved);

            Assert.IsFalse(arena.SlowLane.HasHandle(fastHandle));


            //absolute no no but for test purposes yeah ....
            moved = new MemoryHandle(1, null);

            Assert.IsTrue(arena.FastLane.HasHandle(moved));
        }

        /// <summary>
        ///     SlowLane compact removes gaps after freed entries.
        /// </summary>
        [TestMethod]
        public void SlowLaneCompactRemovesGapsAfterFreedEntries()
        {
            // Arrange
            var handles = new MemoryHandle[6];

            for (var i = 0; i < 6; i++)
            {
                handles[i] = _slowLane.Allocate(128); // 128 bytes each
                Assert.IsTrue(_slowLane.HasHandle(handles[i]), $"Handle {i} should be allocated.");
            }

            //Debug
            Trace.WriteLine("first:");
            _slowLane.LogDump();

            // Free 2nd and 4th
            _slowLane.Free(handles[1]);
            _slowLane.Free(handles[3]);

            Assert.IsFalse(_slowLane.HasHandle(handles[1]), "Handle 1 should have been freed.");
            Assert.IsFalse(_slowLane.HasHandle(handles[3]), "Handle 3 should have been freed.");

            var usedBefore = _slowLane.UsagePercentage();
            //Debug
            Trace.WriteLine("Second:");
            _slowLane.LogDump();


            // Act
            _slowLane.Compact();

            // Assert
            Assert.IsTrue(_slowLane.HasHandle(handles[0]), "Handle 0 should still be valid after compaction.");
            Assert.IsTrue(_slowLane.HasHandle(handles[2]), "Handle 2 should still be valid after compaction.");
            Assert.IsTrue(_slowLane.HasHandle(handles[4]), "Handle 4 should still be valid after compaction.");
            Assert.IsTrue(_slowLane.HasHandle(handles[5]), "Handle 5 should still be valid after compaction.");

            var usedAfter = _slowLane.UsagePercentage();
            Assert.IsTrue(usedAfter <= usedBefore, "Compaction should ideally not increase usage.");

            //Debug
            Trace.WriteLine("Third:");
            _slowLane.LogDump();
        }
    }
}