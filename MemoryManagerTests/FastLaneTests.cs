/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     MemoryManagerTests
 * FILE:        FastLaneTests.cs
 * PURPOSE:     Tests for verifying compaction behavior in FastLane
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

using System;
using Core;
using Lanes;
using MemoryManager;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MemoryManagerTests
{
    [TestClass]
    public class FastLaneTests
    {
        /// <summary>
        ///     The configuration
        /// </summary>
        private MemoryManagerConfig _config;

        /// <summary>
        ///     The FastLane
        /// </summary>
        private FastLane _fastLane;

        /// <summary>
        ///     Setups this instance.
        /// </summary>
        [TestInitialize]
        public void Setup()
        {
            var slowLane = new SlowLane(1024 * 1024); // 1MB for slow lane
            _fastLane = new FastLane(1024 * 1024, slowLane); // 1MB fast lane
            _fastLane.OneWayLane = new OneWayLane(_fastLane, slowLane);

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
        public void MoveFastToSlowMovesEntryAndReplacesStub()
        {
            var arena = new MemoryArena(_config);
            var size = 32 * 1024;

            // 1. Allocate in FastLane
            var fastHandle = arena.Allocate(size);

            // Write some dummy data so we can verify it moved!
            unsafe
            {
                byte* ptr = (byte*)arena.Resolve(fastHandle);
                ptr[0] = 42; // Magic number
            }

            // 2. Move to SlowLane
            arena.MoveFastToSlow(fastHandle);

            arena.DebugDump();

            // 3. Assertions
            // The FastLane MUST still have the handle (it's a stub now!)
            Assert.IsTrue(arena.FastLane.HasHandle(fastHandle), "FastLane should retain the handle as a stub.");

            var entry = arena.FastLane.GetEntry(fastHandle);
            Assert.IsTrue(entry.IsStub, "The original entry should be marked as a stub.");
            Assert.AreNotEqual(0, entry.RedirectToId, "The stub should have a valid RedirectToId.");

            // 4. Verify data survived the move using the ORIGINAL handle
            unsafe
            {
                // Because arena.Resolve() checks the FastLane, sees the stub, 
                // and follows the RedirectToId to the SlowLane automatically!
                byte* resolvedPtr = (byte*)arena.Resolve(fastHandle);
                Assert.AreEqual(42, resolvedPtr[0], "Data was corrupted or not moved properly during stub replacement.");
            }
        }

        /// <summary>
        ///     FastLane compact removes gaps after freed entries.
        /// </summary>
        [TestMethod]
        public void FastLaneCompactRemovesGapsAfterFreedEntries()
        {
            // Arrange
            var handles = new MemoryHandle[6];

            for (var i = 0; i < 6; i++)
            {
                handles[i] = _fastLane.Allocate(128);
            }

            // Free 2nd and 4th (Creates holes at Offset 128 and Offset 384)
            _fastLane.Free(handles[1]);
            _fastLane.Free(handles[3]);

            var expectedFreeSpaceBefore = _fastLane.FreeSpace();

            // Act
            _fastLane.Compact();

            // Assert handles survived
            Assert.IsTrue(_fastLane.HasHandle(handles[0]));
            Assert.IsTrue(_fastLane.HasHandle(handles[2]));
            Assert.IsTrue(_fastLane.HasHandle(handles[4]));
            Assert.IsTrue(_fastLane.HasHandle(handles[5]));

            // Assert that Free Space did not change (compaction doesn't free memory, it just moves it)
            Assert.AreEqual(expectedFreeSpaceBefore, _fastLane.FreeSpace(), "Free space should remain identical before and after compaction.");

            // THE CRITICAL CHECK: Verify they are perfectly packed!
            // Handle 0 is at offset 0
            Assert.AreEqual(0, _fastLane.GetEntry(handles[0]).Offset);
            // Handle 2 should have slid down into the first hole (Offset 128)
            Assert.AreEqual(128, _fastLane.GetEntry(handles[2]).Offset);
            // Handle 4 should have slid down to Offset 256
            Assert.AreEqual(256, _fastLane.GetEntry(handles[4]).Offset);
            // Handle 5 should have slid down to Offset 384
            Assert.AreEqual(384, _fastLane.GetEntry(handles[5]).Offset);

            // Check that our FreeList is correctly reset to 1 giant block
            Assert.AreEqual(0, _fastLane.EstimateFragmentation(), "Fragmentation should be exactly 0 after compaction.");
        }
    }
}