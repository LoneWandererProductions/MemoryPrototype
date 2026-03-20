/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     MemoryManager.Tests
 * FILE:        SlowLaneTests.cs
 * PURPOSE:     Tests for verifying compaction behavior in SlowLane
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

using MemoryManager.Core;
using MemoryManager.Lanes;
using System.Diagnostics;

namespace MemoryManager.Tests
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
        public void MoveSlowToFastMovesEntry() // Removed "AndReplacesStub" because it doesn't do that!
        {
            var arena = new MemoryArena(_config);
            var size = 32 * 1024;

            // 1. Bypass the Arena Threshold and allocate DIRECTLY in the SlowLane
            var slowHandle = arena.SlowLane.Allocate(size);

            // Write a magic number so we can verify the copy worked
            unsafe
            {
                byte* ptr = (byte*)arena.SlowLane.Resolve(slowHandle);
                ptr[0] = 99;
            }

            // Verify initial state
            Assert.IsTrue(slowHandle.Id < 0, "SlowLane handle must be negative.");
            Assert.IsTrue(arena.SlowLane.HasHandle(slowHandle), "SlowLane must contain the allocated handle.");

            // 2. Move to FastLane (The method returns the NEW handle!)
            var fastHandle = arena.MoveSlowToFast(slowHandle);

            arena.DebugDump();

            // 3. Verify the old handle is dead
            Assert.IsFalse(arena.SlowLane.HasHandle(slowHandle), "SlowLane should no longer have the old handle.");

            // 4. Verify the new handle is alive
            Assert.IsTrue(fastHandle.Id > 0, "FastLane handle must be positive.");
            Assert.IsTrue(arena.FastLane.HasHandle(fastHandle), "FastLane must own the new handle.");

            // 5. Verify the data actually moved successfully
            unsafe
            {
                byte* newPtr = (byte*)arena.FastLane.Resolve(fastHandle);
                Assert.AreEqual(99, newPtr[0], "Data was corrupted or not copied during the Slow-to-Fast move.");
            }
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
                handles[i] = _slowLane.Allocate(512); // 512 bytes each
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