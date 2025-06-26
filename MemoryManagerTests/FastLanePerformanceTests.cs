/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     MemoryManagerTests
 * FILE:        FastLanePerformanceTests.cs
 * PURPOSE:     Your file purpose here
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

using System;
using System.Diagnostics;
using Core;
using Lanes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MemoryManagerTests
{
    [TestClass]
    public class FastLanePerformanceTests
    {
        /// <summary>
        ///     The object size
        /// </summary>
        private const int ObjectSize = 1024 * 64; // 64KB

        /// <summary>
        ///     The allocation count
        /// </summary>
        private const int AllocationCount = 1000;

        [TestMethod]
        [TestCategory("Performance")]
        public void CompareGcAndFastLane()
        {
            var gcBefore = GC.GetTotalMemory(true);

            var swGc = Stopwatch.StartNew();
            var gcAllocs = new byte[AllocationCount][];
            for (var i = 0; i < AllocationCount; i++)
                gcAllocs[i] = new byte[ObjectSize];
            swGc.Stop();
            var gcAfter = GC.GetTotalMemory(false);
            var gcPressure = gcAfter - gcBefore;

            Trace.WriteLine($"[GC] Time: {swGc.ElapsedMilliseconds} ms");
            Trace.WriteLine($"[GC] Memory Increase: {gcPressure / 1024.0 / 1024.0:F2} MB");

            var log = string.Empty;

            var swFast = Stopwatch.StartNew();
            using (var fastLane = new FastLane(ObjectSize * AllocationCount + 100 * 1024 * 1024, null))
            {
                var handles = new MemoryHandle[AllocationCount];
                for (var i = 0; i < AllocationCount; i++)
                    handles[i] = fastLane.Allocate(ObjectSize);

                log = fastLane.DebugVisualMap();
            }

            swFast.Stop();

            Trace.WriteLine($"[FastLane] Time: {swFast.ElapsedMilliseconds} ms");
            Trace.WriteLine("[FastLane] GC Memory Increase: Negligible (unmanaged)");

            Trace.WriteLine(log);
        }

        [TestMethod]
        [TestCategory("Performance")]
        public void FastLaneAllocations()
        {
            using var fastLane = new FastLane(ObjectSize * AllocationCount + 100 * 1024 * 1024, null);
            var log = string.Empty;

            var stopwatch = Stopwatch.StartNew();

            var handles = new MemoryHandle[AllocationCount];
            for (var i = 0; i < AllocationCount; i++) handles[i] = fastLane.Allocate(ObjectSize);
            log = fastLane.DebugVisualMap();

            stopwatch.Stop();
            Trace.WriteLine($"FastLane Allocation Time: {stopwatch.ElapsedMilliseconds} ms");
            Trace.WriteLine(log);

            // Cleanup
            foreach (var handle in handles)
                fastLane.Free(handle);
        }

        [TestMethod]
        [TestCategory("Performance")]
        public void FastLaneCompactPerformanceBenchmark()
        {
            //ensures that the AllocationArray must be extended
            const int count = 750;
            const int size = 64 * 1024;
            var log = string.Empty;

            using var fastLane = new FastLane(size * count + 20 * 1024 * 1024, null);
            var handles = new MemoryHandle[count];

            for (var i = 0; i < count; i++)
                handles[i] = fastLane.Allocate(size);

            // Fragmentierung erzeugen
            for (var i = 0; i < count; i += 4)
                fastLane.Free(handles[i]);

            log = fastLane.DebugVisualMap();
            Trace.WriteLine(log);
            var fragBefore = fastLane.EstimateFragmentation();
            Trace.WriteLine($"Fragmentation before: {fragBefore}%");

            var sw = Stopwatch.StartNew();
            fastLane.Compact();
            sw.Stop();

            var fragAfter = fastLane.EstimateFragmentation();
            Trace.WriteLine($"Compaction took: {sw.ElapsedMilliseconds} ms");
            Trace.WriteLine($"Fragmentation after: {fragAfter}%");
            log = fastLane.DebugVisualMap();
            Trace.WriteLine(log);

            Assert.IsTrue(fragAfter < fragBefore, "Fragmentation should decrease");
        }
    }
}