using System;
using System.Diagnostics;
using Core;
using Lanes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MemoryManagerTests
{
    [TestClass]
    public class PerformanceTests
    {
        [TestMethod]
        [TestCategory("RealWorld")]
        public void LargeObjectHeapFragmentationVsSlowLane()
        {
            const int count = 200;
            const int size = 90 * 1024; // >85K = goes to LOH

            // .NET allocations
            var managed = new byte[count][];
            for (var i = 0; i < count; i++)
                managed[i] = new byte[size];

            for (var i = 0; i < count; i += 3)
                managed[i] = null; // fragment

            GC.Collect();
            GC.WaitForPendingFinalizers();

            var afterGc = GC.GetTotalMemory(true);

            Trace.WriteLine("Managed LOH fragmentation simulated");
            Trace.WriteLine($"GC Memory usage: {afterGc / 1024.0 / 1024.0:F2} MB");

            // SlowLane equivalent
            using var slowLane = new SlowLane(size * count + 50 * 1024 * 1024);
            var handles = new MemoryHandle[count];

            for (var i = 0; i < count; i++)
                handles[i] = slowLane.Allocate(size);

            for (var i = 0; i < count; i += 3)
                slowLane.Free(handles[i]);

            var beforeFrag = slowLane.EstimateFragmentation();
            slowLane.Compact();
            var afterFrag = slowLane.EstimateFragmentation();

            Trace.WriteLine($"SlowLane fragmentation: {beforeFrag}% → {afterFrag}%");
        }

        [TestMethod]
        [TestCategory("RealWorld")]
        public void DeterministicVsFinalizerFastLaneTiming()
        {
            var sw = Stopwatch.StartNew();
            var refs = new WeakReference[5000];

            for (var i = 0; i < refs.Length; i++)
                refs[i] = new WeakReference(new byte[1024 * 100]); // 100KB

            GC.Collect();
            GC.WaitForPendingFinalizers();
            sw.Stop();

            Trace.WriteLine($"GC cleanup time: {sw.ElapsedMilliseconds} ms");

            // FastLane version
            using var fastLane = new FastLane(600 * 1024 * 1024, null);
            var handles = new MemoryHandle[refs.Length];

            sw.Restart();
            for (var i = 0; i < refs.Length; i++)
                handles[i] = fastLane.Allocate(100 * 1024);

            for (var i = 0; i < refs.Length; i++)
                fastLane.Free(handles[i]);
            sw.Stop();

            Trace.WriteLine($"FastLane cleanup time: {sw.ElapsedMilliseconds} ms");
        }

        [TestMethod]
        [TestCategory("RealWorld")]
        public void DeterministicVsFinalizerSlowLaneTiming()
        {
            var sw = Stopwatch.StartNew();
            var refs = new WeakReference[5000];

            for (var i = 0; i < refs.Length; i++)
                refs[i] = new WeakReference(new byte[1024 * 100]); // 100KB

            GC.Collect();
            GC.WaitForPendingFinalizers();
            sw.Stop();

            Trace.WriteLine($"GC cleanup time: {sw.ElapsedMilliseconds} ms");

            // SlowLane version
            using var slowLane = new SlowLane(600 * 1024 * 1024);
            var handles = new MemoryHandle[refs.Length];

            sw.Restart();
            for (var i = 0; i < refs.Length; i++)
                handles[i] = slowLane.Allocate(100 * 1024);

            for (var i = 0; i < refs.Length; i++)
                slowLane.Free(handles[i]);
            sw.Stop();

            Trace.WriteLine($"SlowLane cleanup time: {sw.ElapsedMilliseconds} ms");
        }

        [TestMethod]
        [TestCategory("RealWorld")]
        public void DeterministicVsFinalizerSlowLaneTimingMassFree()
        {
            var sw = Stopwatch.StartNew();
            var refs = new WeakReference[5000];

            for (var i = 0; i < refs.Length; i++)
                refs[i] = new WeakReference(new byte[1024 * 100]); // 100KB

            GC.Collect();
            GC.WaitForPendingFinalizers();
            sw.Stop();

            Trace.WriteLine($"GC cleanup time: {sw.ElapsedMilliseconds} ms");

            // SlowLane version
            using var slowLane = new SlowLane(600 * 1024 * 1024);
            var handles = new MemoryHandle[refs.Length];

            sw.Restart();
            for (var i = 0; i < refs.Length; i++)
                handles[i] = slowLane.Allocate(100 * 1024);

            slowLane.FreeMany(handles);
            sw.Stop();

            Trace.WriteLine($"SlowLane cleanup time: {sw.ElapsedMilliseconds} ms");
        }

        [TestMethod]
        [TestCategory("GCComparison")]
        public void ManagedVsUnmanagedPressure()
        {
            const int count = 5000;
            const int size = 50 * 1024; // 50KB each

            var beforeManaged = GC.GetTotalMemory(true);
            var arr = new byte[count][];

            for (var i = 0; i < count; i++)
                arr[i] = new byte[size];

            var afterManaged = GC.GetTotalMemory(false);
            Trace.WriteLine($"Managed GC pressure: {(afterManaged - beforeManaged) / 1024.0 / 1024.0:F2} MB");

            var beforeSlow = GC.GetTotalMemory(true);
            using var slowLane = new SlowLane(count * size + 50 * 1024 * 1024);
            for (var i = 0; i < count; i++)
                slowLane.Allocate(size);

            var afterSlow = GC.GetTotalMemory(false);
            Trace.WriteLine($"SlowLane GC pressure: {(afterSlow - beforeSlow) / 1024.0 / 1024.0:F2} MB");
        }

        [TestMethod]
        [TestCategory("Performance")]
        public void HighReuseWithNoGc()
        {
            const int size = 1024;
            var count = 10;

            using var fastLane = new FastLane(count * size + 20 * 1024 * 1024, null);

            var stopwatch = Stopwatch.StartNew();
            for (var cycle = 0; cycle < 5; cycle++)
            {
                var handles = new MemoryHandle[count];
                for (var i = 0; i < count; i++)
                    handles[i] = fastLane.Allocate(size);
                for (var i = 0; i < count; i++)
                    fastLane.Free(handles[i]);
            }

            stopwatch.Stop();

            Trace.WriteLine($"10 FastLane reuse cycles done in: {stopwatch.ElapsedMilliseconds} ms");

            count = 100;

            stopwatch = Stopwatch.StartNew();
            for (var cycle = 0; cycle < 5; cycle++)
            {
                var handles = new MemoryHandle[count];
                for (var i = 0; i < count; i++)
                    handles[i] = fastLane.Allocate(size);
                for (var i = 0; i < count; i++)
                    fastLane.Free(handles[i]);
            }

            stopwatch.Stop();

            Trace.WriteLine($"100 FastLane reuse cycles done in: {stopwatch.ElapsedMilliseconds} ms");

            //TODO issue here is the Allocation Array must grow and that kills performance

            count = 1000;

            stopwatch = Stopwatch.StartNew();
            for (var cycle = 0; cycle < 5; cycle++)
            {
                var handles = new MemoryHandle[count];
                for (var i = 0; i < count; i++)
                    handles[i] = fastLane.Allocate(size);
                for (var i = 0; i < count; i++)
                    fastLane.Free(handles[i]);
            }

            stopwatch.Stop();

            Trace.WriteLine($"1000 FastLane reuse cycles done in: {stopwatch.ElapsedMilliseconds} ms");
        }
    }
}