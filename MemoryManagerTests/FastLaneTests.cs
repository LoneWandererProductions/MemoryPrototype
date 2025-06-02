/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     MemoryManagerTests
 * FILE:        FastLaneTests.cs
 * PURPOSE:     Tests for verifying compaction behavior in FastLane
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

using Microsoft.VisualStudio.TestTools.UnitTesting;
using Lanes;
using Core;
using System.Diagnostics;

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
        /// Setups this instance.
        /// </summary>
        [TestInitialize]
        public void Setup()
        {
            var slowLane = new SlowLane(1024 * 1024); // 1MB for slow lane
            _fastLane = new FastLane(1024 * 1024, slowLane); // 1MB fast lane
            _fastLane.OneWayLane = new OneWayLane(1024, _fastLane, slowLane); // Required setup
        }

        /// <summary>
        /// FastLane compact removes gaps after freed entries.
        /// </summary>
        [TestMethod]
        public void FastLaneCompactRemovesGapsAfterFreedEntries()
        {
            // Arrange
            var handles = new MemoryHandle[6];

            for (int i = 0; i < 6; i++)
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
