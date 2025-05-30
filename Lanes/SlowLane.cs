// ReSharper disable EventNeverSubscribedTo.Global

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Core;
using Core.MemoryArenaPrototype.Core;

namespace Lanes
{
    public sealed class SlowLane : IMemoryLane, IDisposable
    {
        private const double SafetyMargin = 0.10; // 10% free space reserved

        public IntPtr Buffer { get; private set; }

        public int Capacity { get; private set; }

        private readonly AllocationEntry[] _entries;

        private readonly Dictionary<int, int> _handleIndex = new(); // handleId -> entries array index
        public int EntryCount { get; private set; } = 0;

        public event Action<string>? OnCompaction;

        private readonly Stack<int> _freeSlots = new(); // reuse freed slots

        private int _nextHandleId = 1000; // Separate ID range for slow lane

        public SlowLane(int capacity, int maxEntries = 1024)
        {
            Capacity = capacity;
            Buffer = Marshal.AllocHGlobal(capacity);
            _entries = new AllocationEntry[maxEntries];
        }

        public MemoryHandle Allocate(
            int size,
            AllocationPriority priority = AllocationPriority.Normal,
            AllocationHints hints = AllocationHints.None,
            string? debugName = null,
            int currentFrame = 0)
        {
            if (!CanAllocate(size))
                throw new OutOfMemoryException("SlowLane: Cannot allocate");

            var offset = FindFreeSpot(size);
            var slotIndex = (_freeSlots.Count > 0) ? _freeSlots.Pop() : EntryCount++;

            var id = _nextHandleId++;
            _entries[slotIndex] = new AllocationEntry
            {
                Offset = offset,
                Size = size,
                HandleId = id,
                IsStub = false,
                RedirectTo = null,

                // Metadata assignment
                Priority = priority,
                Hints = hints,
                DebugName = debugName,
                AllocationFrame = currentFrame,
                LastAccessFrame = currentFrame
            };

            _handleIndex[id] = slotIndex;

            SortEntriesByOffset();

            return new MemoryHandle(id, this);
        }

        public bool CanAllocate(int size)
        {
            return GetUsed() + size <= Capacity * (1.0 - SafetyMargin);
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
            return _handleIndex.Select(kv => new MemoryHandle(kv.Key, this));
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
            if (_entries == null) return;

            var newBuffer = Marshal.AllocHGlobal(Capacity);
            var offset = 0;

            // Compact only non-stub entries, moving them to front
            var writeIndex = 0;
            var newHandleIndex = new Dictionary<int, int>(_handleIndex.Count);

            for (var i = 0; i < EntryCount; i++)
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
            for (var i = writeIndex; i < EntryCount; i++)
                _entries[i] = default;

            // Update state
            Marshal.FreeHGlobal(Buffer);
            Buffer = newBuffer;

            _handleIndex.Clear();
            foreach (var kvp in newHandleIndex)
                _handleIndex[kvp.Key] = kvp.Value;

            EntryCount = writeIndex;

            // Reset free slots since after compact there are no holes
            _freeSlots.Clear();

            // Entries already sorted by offset due to compact process
            OnCompaction?.Invoke(nameof(SlowLane));
        }

        /// <summary>
        /// Determines whether the specified handle has handle.
        /// </summary>
        /// <param name="handle">The handle.</param>
        /// <returns>
        ///   <c>true</c> if the specified handle has handle; otherwise, <c>false</c>.
        /// </returns>
        public bool HasHandle(MemoryHandle handle) => MemoryLaneUtils.HasHandle(handle, _handleIndex);


        public AllocationEntry GetEntry(MemoryHandle handle) => MemoryLaneUtils.GetEntry(handle, _handleIndex, _entries, nameof(SlowLane));


        public int GetAllocationSize(MemoryHandle handle) => MemoryLaneUtils.GetAllocationSize(handle, _handleIndex, _entries, nameof(SlowLane));

        private int GetUsed()
        {
            var used = 0;
            for (var i = 0; i < EntryCount; i++)
            {
                if (!_entries[i].IsStub)
                    used += _entries[i].Size;
            }

            return used;
        }

        private void SortEntriesByOffset()
        {
            // Simple insertion sort for small arrays, else replace with more efficient if needed
            Array.Sort(_entries, 0, EntryCount, new AllocationEntryOffsetComparer());
        }

        // Returns total free bytes in FastLane
        public int FreeSpace() => MemoryLaneUtils.CalculateFreeSpace(_entries, EntryCount, Capacity);

        private int FindFreeSpot(int size) => MemoryLaneUtils.FindFreeSpot(size, _entries, EntryCount);

        // Returns count of stub entries
        public int StubCount() => MemoryLaneUtils.StubCount(EntryCount, _entries);

        // Estimate fragmentation percentage (gaps / total capacity)
        public int EstimateFragmentation() => MemoryLaneUtils.EstimateFragmentation(_entries, EntryCount, Capacity);

        public double UsagePercentage() => MemoryLaneUtils.UsagePercentage(EntryCount, _entries, Capacity);

        public string DebugDump() => MemoryLaneUtils.DebugDump(_entries, EntryCount);

        public void Dispose()
        {
            Marshal.FreeHGlobal(Buffer);
            EntryCount = 0;
            _handleIndex.Clear();
            _freeSlots.Clear();
        }
    }
}
