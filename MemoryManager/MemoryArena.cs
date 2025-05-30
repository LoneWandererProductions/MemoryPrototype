/*
 *
 * FastLane Policy
 *  When to Compact FastLane:
 * Usage > 90% of FastLane and enough memory is wasted by stubs.
 * Large allocation request cannot be fulfilled despite stubs present.
 * Manual trigger (e.g. during maintenance cycle or scene change).
 * What to Move to SlowLane:
 * Entries not accessed recently (age tagging or LRU heuristic).
 * Entries larger than a threshold (e.g., 4 KB+).
 * Entries marked as cold or long-lived (by system tagging).
 * Entries not tagged as frame-critical.
 * On Move:
 * Allocate in SlowLane
 * Copy data
 * Replace original with stub
 * Flag for FastLane.Compact() if space was freed
 * SlowLane Policy
 * When to Compact SlowLane:
 * Usage > 85% of SlowLane
 * Fragmentation is high (e.g., >20% space in gaps)
 * SlowLane allocation fails even though enough space exists
 * Manual trigger, e.g. MemoryArena.CompactAll()
 * Compacting Constraints:
 * Only compact if free space after compaction > 10%
 * Ensures space for future moves from FastLane, at least that is planned, reserver 10% of slow lane for janitory work
 * Do not compact aggressively; it’s slower and costlier
 * Prefer compaction during low activity (non-frame time)
 */

// ReSharper disable NotAccessedField.Local

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Core;
using Lanes;

namespace MemoryManager
{
    public sealed class MemoryArena
    {
        private readonly FastLane _fastLane;
        private readonly SlowLane _slowLane;
        private int Threshold { get; set; }
        private readonly MemoryManagerConfig _config;

        // Optionally: background thread for policies
        private Timer? _policyTimer;

        private object _lock;

        public MemoryArena(MemoryManagerConfig config)
        {
            _lock = new object();

            _config = config;
            Threshold = config.Threshold;
            _slowLane = new SlowLane(config.SlowLaneSize);
            _fastLane = new FastLane(config.FastLaneSize, _slowLane);

            if (config.PolicyCheckInterval > TimeSpan.Zero)
            {
                _policyTimer = new Timer(_ => CheckPolicies(), null, config.PolicyCheckInterval, config.PolicyCheckInterval);
            }
        }

        public unsafe void MoveFastToSlow(MemoryHandle fastHandle)
        {
            var fastEntry = _fastLane.GetEntry(fastHandle);
            var size = fastEntry.Size;

            var slowHandle = _slowLane.Allocate(size);

            Buffer.MemoryCopy(
                (void*)(_fastLane.Buffer + fastEntry.Offset),
                (void*)_slowLane.Resolve(slowHandle),
                size,
                size);

            _fastLane.ReplaceWithStub(fastHandle, slowHandle);
        }

        public unsafe ref T Get<T>(MemoryHandle handle) where T : unmanaged
        {
            var ptr = Resolve(handle);
            return ref System.Runtime.CompilerServices.Unsafe.AsRef<T>(ptr.ToPointer());
        }

        public void RunMaintenanceCycle()
        {
            TryCompactFastLane();
            TryCompactSlowLane();
        }

        private void TryCompactFastLane()
        {
            var fastLaneUsageThreshold = _config.FastLaneUsageThreshold;

            if (_fastLane.UsagePercentage() <= fastLaneUsageThreshold)
                return;

            // Cache handles that should be moved first to avoid modifying collection during enumeration
            var handlesToMove = new List<MemoryHandle>();

            foreach (var handle in _fastLane.GetHandles())
            {
                var entry = _fastLane.GetEntry(handle);
                if (entry.Size > _config.FastLaneUsageThreshold ||
                    entry.Hints.HasFlag(AllocationHints.Cold) ||
                    entry.Hints.HasFlag(AllocationHints.Old))
                {
                    handlesToMove.Add(handle);
                }
            }

            foreach (var handle in handlesToMove)
            {
                MoveFastToSlow(handle);
            }

            _fastLane.Compact();
        }


        private void CheckPolicies()
        {
            var usage = _fastLane.UsagePercentage();
            if (!_config.EnableAutoCompaction || !(usage >= _config.CompactionThreshold)) return;

            TryCompactFastLane();
            TryCompactSlowLane();
            // Optional: log compaction stats or notify observers
        }

        private void TryCompactSlowLane()
        {
            // Usage threshold before considering compaction (e.g., 85%)
            var slowLaneUsageThreshold = _config.SlowLaneUsageThreshold;

            if (!(_slowLane.UsagePercentage() > slowLaneUsageThreshold))
                return;

            // Estimated fragmentation fraction (0.0 - 1.0)
            double fragmentation = _slowLane.EstimateFragmentation();

            // Current free space in bytes
            double currentFreeSpace = _slowLane.FreeSpace();

            // Total capacity of the slow lane in bytes
            double totalSize = _slowLane.Capacity;

            // Predicted free space after compaction (current free + fragmented gaps)
            var predictedFreeAfterCompaction = currentFreeSpace + fragmentation * totalSize;

            // Use slow lane specific safety margin to decide if compaction is worthwhile
            var safetyMargin = _config.SlowLaneSafetyMargin;

            // If predicted free space ratio meets or exceeds safety margin, perform compaction
            if (predictedFreeAfterCompaction / totalSize >= safetyMargin)
            {
                _slowLane.Compact();
            }
        }

        public MemoryHandle Allocate(
            int size,
            AllocationPriority priority = AllocationPriority.Normal,
            AllocationHints hints = AllocationHints.None,
            string? debugName = null,
            int currentFrame = 0)
        {
            lock (_lock)
            {
                if (size <= Threshold && _fastLane.CanAllocate(size))
                    return _fastLane.Allocate(size, priority, hints, debugName, currentFrame);
                if (_slowLane.CanAllocate(size))
                    return _slowLane.Allocate(size, priority, hints, debugName, currentFrame);

                throw new OutOfMemoryException(
                    $"Neither lane could allocate memory. Requested size: {size}, " +
                    $"FastLane free: {_fastLane.FreeSpace()}, SlowLane free: {_slowLane.FreeSpace()}");
            }
        }

        public IntPtr Resolve(MemoryHandle handle)
        {
            if (_fastLane.HasHandle(handle)) return _fastLane.Resolve(handle);
            if (_slowLane.HasHandle(handle)) return _slowLane.Resolve(handle);
            throw new InvalidOperationException("Handle not found.");
        }

        public void CompactAll()
        {
            _fastLane.Compact();
            _slowLane.Compact();
        }

        public void Free(MemoryHandle handle)
        {
            if (_fastLane.HasHandle(handle))
                _fastLane.Free(handle);
            else if (_slowLane.HasHandle(handle))
                _slowLane.Free(handle);
            else
                throw new InvalidOperationException("Handle not recognized by any lane.");
        }

        public void DebugDump()
        {
            Trace.WriteLine("===== MemoryArena Dump =====");
            Trace.WriteLine($"Fast Lane Usage: {_fastLane.UsagePercentage():P2}, Free: {_fastLane.FreeSpace()} bytes, Entries: {_fastLane.EntryCount}, Stubs: {_fastLane.StubCount()}");

            Trace.WriteLine($"Estimated Fragmentation: {_fastLane.EstimateFragmentation():P2}");
            Trace.WriteLine(_fastLane.DebugDump());

            Trace.WriteLine($"Slow Lane Usage: {_slowLane.UsagePercentage():P2}, Free: {_slowLane.FreeSpace()} bytes, Entries: {_slowLane.EntryCount}, Stubs: {_slowLane.StubCount()}");
            Trace.WriteLine($"Estimated Fragmentation: {_slowLane.EstimateFragmentation():P2}");
            Trace.WriteLine(_slowLane.DebugDump());
            Trace.WriteLine("============================");
        }
    }
}
