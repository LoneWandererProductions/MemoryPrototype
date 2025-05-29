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
 *  SlowLane Policy
 * When to Compact SlowLane:
 * Usage > 85% of SlowLane
 * Fragmentation is high (e.g., >20% space in gaps)
 * SlowLane allocation fails even though enough space exists
 * Manual trigger, e.g. MemoryArena.CompactAll()
 * Compacting Constraints:
 * Only compact if free space after compaction > 10%
 * Ensures space for future moves from FastLane
 * Do not compact aggressively; it’s slower and costlier
 * Prefer compaction during low activity (non-frame time)
 */

using System;
using System.Diagnostics;
using Core;
using Lanes;

namespace MemoryManager
{
    public sealed class MemoryArena
    {
        private readonly FastLane _fastLane;
        private readonly SlowLane _slowLane;
        private readonly int _threshold;

        public MemoryArena(int fastLaneSize, int slowLaneSize, int fastLaneThreshold)
        {
            _slowLane = new SlowLane(slowLaneSize);
            _fastLane = new FastLane(fastLaneSize, _slowLane);
            _threshold = fastLaneThreshold;
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

        public void AutoManageMemory()
        {
            TryCompactFastLane();
            TryCompactSlowLane();
        }

        private void TryCompactFastLane()
        {
            const double fastLaneUsageThreshold = 0.9;

            if (_fastLane.UsagePercentage() > fastLaneUsageThreshold)
            {
                foreach (var handle in _fastLane.GetHandles())
                {
                    var entry = _fastLane.GetEntry(handle);
                    if (entry.Size > 4096 || entry.IsStub) // Simple heuristic: large or unused
                    {
                        MoveFastToSlow(handle);
                    }
                }

                _fastLane.Compact();
            }
        }

        private void TryCompactSlowLane()
        {
            const double slowLaneUsageThreshold = 0.85;
            const double safetyMargin = 0.10; // 10%

            if (_slowLane.UsagePercentage() > slowLaneUsageThreshold)
            {
                var availablePostCompact = 1.0 - _slowLane.UsagePercentage();
                if (availablePostCompact >= safetyMargin)
                {
                    _slowLane.Compact();
                }
            }
        }

        public MemoryHandle Allocate(int size)
        {
            if (size <= _threshold && _fastLane.CanAllocate(size))
                return _fastLane.Allocate(size);
            if (_slowLane.CanAllocate(size))
                return _slowLane.Allocate(size);

            throw new OutOfMemoryException("Neither lane could allocate memory.");
        }

        public void CompactAll()
        {
            _fastLane.Compact();
            _slowLane.Compact();
        }

        public void Free(MemoryHandle handle)
        {
            handle.GetPointer(); // Validate
            handle.GetType().GetMethod("Free")?.Invoke(handle, new object[] { handle });
        }

        public void DebugDump()
        {
            Trace.WriteLine("Fast Lane:");
            Trace.WriteLine(_fastLane.DebugDump());
            Trace.WriteLine("Slow Lane:");
            Trace.WriteLine(_slowLane.DebugDump());
        }
    }
}
