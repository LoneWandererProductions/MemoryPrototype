// ReSharper disable MemberCanBePrivate.Global

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Core;
using Core.MemoryArenaPrototype.Core;

namespace Lanes
{
    public sealed class FastLane : IMemoryLane, IDisposable
    {
        public int Capacity { get; private set; }

        private AllocationEntry[]? _entries = new AllocationEntry[128];
        public int EntryCount { get; private set; } = 0;

        public event Action<string>? OnCompaction;

        private readonly Dictionary<int, int> _handleIndex = new(); // Maps handleId → index into _entries

        private readonly SlowLane _slowLane;

        public IntPtr Buffer { get; private set; }

        private int _nextHandleId = 1;

        public bool CanAllocate(int size)
        {
            try
            {
                return FindFreeSpot(size) + size <= Capacity;
            }
            catch
            {
                return false;
            }
        }

        public FastLane(int size, SlowLane slowLane)
        {
            _slowLane = slowLane;
            Capacity = size;
            Buffer = Marshal.AllocHGlobal(size);
        }

        public MemoryHandle Allocate(
            int size,
            AllocationPriority priority = AllocationPriority.Normal,
            AllocationHints hints = AllocationHints.None,
            string? debugName = null,
            int currentFrame = 0)
        {
            var offset = FindFreeSpot(size);
            if (offset + size > Capacity)
                throw new OutOfMemoryException("FastLane: Not enough memory");

            if (EntryCount >= _entries.Length)
                Array.Resize(ref _entries, _entries.Length * 2);

            var id = _nextHandleId++;
            _entries[EntryCount] = new AllocationEntry
            {
                Offset = offset,
                Size = size,
                HandleId = id,
                Priority = priority,
                Hints = hints,
                DebugName = debugName,
                AllocationFrame = currentFrame,
                LastAccessFrame = currentFrame,
            };

            _handleIndex[id] = EntryCount;
            EntryCount++;

            return new MemoryHandle(id, this);
        }

        public IntPtr Resolve(MemoryHandle handle)
        {
            if (!_handleIndex.TryGetValue(handle.Id, out var index))
                throw new InvalidOperationException("FastLane: Invalid handle");

            var entry = _entries[index];
            if (entry.IsStub && entry.RedirectTo.HasValue)
                return _slowLane.Resolve(entry.RedirectTo.Value);

            return Buffer + entry.Offset;
        }

        public void Free(MemoryHandle handle)
        {
            if (!_handleIndex.TryGetValue(handle.Id, out var index))
                throw new InvalidOperationException("FastLane: Invalid handle");

            var entry = _entries[index];

            if (entry.IsStub && entry.RedirectTo.HasValue)
            {
                _slowLane.Free(entry.RedirectTo.Value);
            }

            // Remove by shifting tail and updating map
            var last = EntryCount - 1;
            if (index != last)
            {
                _entries[index] = _entries[last];
                _handleIndex[_entries[index].HandleId] = index;
            }

            _handleIndex.Remove(handle.Id);
            EntryCount--;
        }

        public IEnumerable<MemoryHandle> GetHandles()
        {
            return _handleIndex.Keys.Select(id => new MemoryHandle(id, this));
        }

        public unsafe void Compact()
        {
            if (_entries == null) return;

            var newBuffer = Marshal.AllocHGlobal(Capacity);
            var offset = 0;

            for (var i = 0; i < EntryCount; i++)
            {
                var entry = _entries[i];

                if (!entry.IsStub)
                {
                    void* source = (byte*)Buffer + entry.Offset;
                    void* target = (byte*)newBuffer + offset;

                    System.Buffer.MemoryCopy(source, target, Capacity - offset, entry.Size);
                    entry.Offset = offset;
                    offset += entry.Size;
                }

                _entries[i] = entry;
                _handleIndex[entry.HandleId] = i;
            }

            Marshal.FreeHGlobal(Buffer);
            Buffer = newBuffer;
            OnCompaction?.Invoke(nameof(FastLane));
        }

        public AllocationEntry GetEntry(MemoryHandle handle)
        {
            if (!_handleIndex.TryGetValue(handle.Id, out var index))
                throw new InvalidOperationException("FastLane: Invalid handle");

            return _entries[index];
        }

        public void ReplaceWithStub(MemoryHandle fastHandle, MemoryHandle slowHandle)
        {
            if (!_handleIndex.TryGetValue(fastHandle.Id, out var index))
                throw new InvalidOperationException("FastLane: Invalid handle");

            var entry = _entries[index];
            entry.IsStub = true;
            entry.RedirectTo = slowHandle;
            _entries[index] = entry;
        }

        public bool HasHandle(MemoryHandle handle)
        {
            return _handleIndex.ContainsKey(handle.Id);
        }

        private int FindFreeSpot(int size) => MemoryLaneUtils.FindFreeSpot(size, _entries, EntryCount);

        // Returns total free bytes in FastLane
        public int FreeSpace() => MemoryLaneUtils.CalculateFreeSpace(_entries, EntryCount, Capacity);

        // Returns count of stub entries
        public int StubCount() => MemoryLaneUtils.StubCount(EntryCount, _entries);

        // Estimate fragmentation percentage (gaps / total capacity)
        public int EstimateFragmentation() => MemoryLaneUtils.EstimateFragmentation(_entries, EntryCount, Capacity);
        public double UsagePercentage() => MemoryLaneUtils.UsagePercentage(EntryCount, _entries, Capacity);

        public string DebugDump() => MemoryLaneUtils.DebugDump(_entries, EntryCount);

        public void Dispose()
        {
            Marshal.FreeHGlobal(Buffer);
            _handleIndex.Clear();
            _entries = null;
        }
    }
}