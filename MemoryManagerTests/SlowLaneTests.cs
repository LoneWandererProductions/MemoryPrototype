/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     MemoryManagerTests
 * FILE:        SlowLaneTests.cs
 * PURPOSE:     Tests for verifying compaction behavior in SlowLane
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

using Microsoft.VisualStudio.TestTools.UnitTesting;
using Lanes;
using Core;
using System.Diagnostics;

namespace MemoryManagerTests
{
    [TestClass]
    public class SlowLaneTests
    {
        /// <summary>
        /// The FastLane
        /// </summary>
        private SlowLane _slowLane;

        /// <summary>
        /// Setups this instance.
        /// </summary>
        [TestInitialize]
        public void Setup()
        {
            _slowLane = new SlowLane(1024 * 1024); // 1MB fast lane
        }

        /// <summary>
        /// SlowLane compact removes gaps after freed entries.
        /// </summary>
        [TestMethod]
        public void SlowLaneCompactRemovesGapsAfterFreedEntries()
        {
            // Arrange
            var handles = new MemoryHandle[6];

            for (int i = 0; i < 6; i++)
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
