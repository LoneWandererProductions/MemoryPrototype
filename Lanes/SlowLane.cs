using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Linq;
using Core;
using Core.MemoryArenaPrototype.Core;

namespace Lanes
{
    public sealed class SlowLane : IMemoryLane, IDisposable
    {
        private const double SafetyMargin = 0.10; // 10% free space reserved

        public IntPtr Buffer { get; private set; }
        private readonly int _capacity;

        private readonly AllocationEntry[] _entries;
        private readonly Dictionary<int, int> _handleIndex = new(); // handleId -> entries array index
        private int _entryCount = 0;

        private readonly Stack<int> _freeSlots = new(); // reuse freed slots

        private int _nextHandleId = 1000; // Separate ID range for slow lane

        public SlowLane(int capacity, int maxEntries = 1024)
        {
            _capacity = capacity;
            Buffer = Marshal.AllocHGlobal(capacity);
            _entries = new AllocationEntry[maxEntries];
        }

        public void Dispose()
        {
            Marshal.FreeHGlobal(Buffer);
            _entryCount = 0;
            _handleIndex.Clear();
            _freeSlots.Clear();
        }

        public MemoryHandle Allocate(int size)
        {
            if (!CanAllocate(size))
                throw new OutOfMemoryException("SlowLane: Cannot allocate");

            var offset = FindFreeSpot(size);

            var slotIndex = (_freeSlots.Count > 0) ? _freeSlots.Pop() : _entryCount++;

            var id = _nextHandleId++;
            _entries[slotIndex] = new AllocationEntry { Offset = offset, Size = size, HandleId = id, IsStub = false, RedirectTo = null };
            _handleIndex[id] = slotIndex;

            SortEntriesByOffset();

            return new MemoryHandle(id, this);
        }

        public bool CanAllocate(int size)
        {
            return GetUsed() + size <= _capacity * (1.0 - SafetyMargin);
        }

        public IntPtr Resolve(MemoryHandle handle)
        {
            if (!_handleIndex.TryGetValue(handle.Id, out var index))
                throw new InvalidOperationException("SlowLane: Invalid handle");

            var entry = _entries[index];
            if (entry.IsStub && entry.RedirectTo.HasValue)
                throw new InvalidOperationException("SlowLane: Cannot resolve stub entry without redirection");

            return Buffer + entry.Offset;
        }

        public IEnumerable<MemoryHandle> GetHandles()
        {
            foreach (var kv in _handleIndex)
                yield return new MemoryHandle(kv.Key, this);
        }

        public double UsagePercentage()
        {
            long used = 0;
            for (var i = 0; i < _entryCount; i++)
            {
                if (!_entries[i].IsStub)
                    used += _entries[i].Size;
            }
            return (double)used / _capacity;
        }

        public void Free(MemoryHandle handle)
        {
            if (!_handleIndex.TryGetValue(handle.Id, out var index))
                throw new InvalidOperationException("SlowLane: Invalid handle");

            _handleIndex.Remove(handle.Id);

            // Mark slot free, clear entry
            _entries[index] = default;
            _freeSlots.Push(index);
        }

        public unsafe void Compact()
        {
            var newBuffer = Marshal.AllocHGlobal(_capacity);
            var offset = 0;

            // Compact only non-stub entries, moving them to front
            var writeIndex = 0;
            var newHandleIndex = new Dictionary<int, int>(_handleIndex.Count);

            for (var i = 0; i < _entryCount; i++)
            {
                var entry = _entries[i];
                if (entry.IsStub)
                {
                    // Skip stubs (or handle as needed)
                    continue;
                }

                System.Buffer.MemoryCopy((void*)(Buffer + entry.Offset), (void*)(newBuffer + offset), entry.Size, entry.Size);

                entry.Offset = offset;
                offset += entry.Size;

                _entries[writeIndex] = entry;
                newHandleIndex[entry.HandleId] = writeIndex;

                writeIndex++;
            }

            // Clear remaining slots if any
            for (var i = writeIndex; i < _entryCount; i++)
                _entries[i] = default;

            // Update state
            Marshal.FreeHGlobal(Buffer);
            Buffer = newBuffer;

            _handleIndex.Clear();
            foreach (var kvp in newHandleIndex)
                _handleIndex[kvp.Key] = kvp.Value;

            _entryCount = writeIndex;

            // Reset free slots since after compact there are no holes
            _freeSlots.Clear();

            // Entries already sorted by offset due to compact process
        }

        public bool HasHandle(MemoryHandle handle)
        {
            return _handleIndex.ContainsKey(handle.Id);
        }

        public string DebugDump()
        {
            var lines = new List<string>(_entryCount);
            for (var i = 0; i < _entryCount; i++)
            {
                var e = _entries[i];
                if (e.HandleId != 0) // ignore empty
                    lines.Add($"[SlowLane] ID {e.HandleId} Offset {e.Offset} Size {e.Size}");
            }
            return string.Join("\n", lines);
        }

        private int FindFreeSpot(int size)
        {
            var offset = 0;
            for (var i = 0; i < _entryCount; i++)
            {
                var entry = _entries[i];
                if (offset + size <= entry.Offset)
                    return offset;

                offset = entry.Offset + entry.Size;
            }

            return offset;
        }

        private int GetUsed()
        {
            var used = 0;
            for (var i = 0; i < _entryCount; i++)
            {
                if (!_entries[i].IsStub)
                    used += _entries[i].Size;
            }
            return used;
        }

        private void SortEntriesByOffset()
        {
            // Simple insertion sort for small arrays, else replace with more efficient if needed
            Array.Sort(_entries, 0, _entryCount, new AllocationEntryOffsetComparer());
        }

        // Helper comparer to sort entries by Offset ascending
        private class AllocationEntryOffsetComparer : IComparer<AllocationEntry>
        {
            public int Compare(AllocationEntry x, AllocationEntry y)
            {
                return x.Offset.CompareTo(y.Offset);
            }
        }
    }
}
