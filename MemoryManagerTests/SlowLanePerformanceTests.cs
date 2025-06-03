using System;
using System.Diagnostics;
using Core;
using Lanes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MemoryManagerTests
{
    [TestClass]
    public class SlowLanePerformanceTests
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
        public void CompareGcAndSlowLane()
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

            var swSlow = Stopwatch.StartNew();
            using (var slowLane = new SlowLane(ObjectSize * AllocationCount + 100 * 1024 * 1024))
            {
                var handles = new MemoryHandle[AllocationCount];
                for (var i = 0; i < AllocationCount; i++)
                    handles[i] = slowLane.Allocate(ObjectSize);

                log = slowLane.DebugVisualMap();
            }

            swSlow.Stop();

            Trace.WriteLine($"[SlowLane] Time: {swSlow.ElapsedMilliseconds} ms");
            Trace.WriteLine("[SlowLane] GC Memory Increase: Negligible (unmanaged)");

            Trace.WriteLine(log);
        }

        [TestMethod]
        [TestCategory("Performance")]
        public void SlowLaneAllocations()
        {
            using var slowLane = new SlowLane(ObjectSize * AllocationCount + 100 * 1024 * 1024);
            var log = string.Empty;

            var stopwatch = Stopwatch.StartNew();

            var handles = new MemoryHandle[AllocationCount];
            for (var i = 0; i < AllocationCount; i++) handles[i] = slowLane.Allocate(ObjectSize);
            log = slowLane.DebugVisualMap();

            stopwatch.Stop();
            Trace.WriteLine($"SlowLane Allocation Time: {stopwatch.ElapsedMilliseconds} ms");
            Trace.WriteLine(log);

            // Cleanup
            foreach (var handle in handles)
                slowLane.Free(handle);
        }

        [TestMethod]
        [TestCategory("Performance")]
        public void SlowLaneCompactPerformanceBenchmark()
        {
            //ensures that the AllocationArray must be extended
            const int count = 750;
            const int size = 64 * 1024;
            var log = string.Empty;

            using var slowLane = new SlowLane(size * count + 20 * 1024 * 1024);
            var handles = new MemoryHandle[count];

            for (var i = 0; i < count; i++)
                handles[i] = slowLane.Allocate(size);

            // Fragmentierung erzeugen
            for (var i = 0; i < count; i += 4)
                slowLane.Free(handles[i]);

            log = slowLane.DebugVisualMap();
            Trace.WriteLine(log);
            var fragBefore = slowLane.EstimateFragmentation();
            Trace.WriteLine($"Fragmentation before: {fragBefore}%");

            var sw = Stopwatch.StartNew();
            slowLane.Compact();
            sw.Stop();

            var fragAfter = slowLane.EstimateFragmentation();
            Trace.WriteLine($"Compaction took: {sw.ElapsedMilliseconds} ms");
            Trace.WriteLine($"Fragmentation after: {fragAfter}%");
            log = slowLane.DebugVisualMap();
            Trace.WriteLine(log);

            Assert.IsTrue(fragAfter < fragBefore, "Fragmentation should decrease");
        }
    }
}