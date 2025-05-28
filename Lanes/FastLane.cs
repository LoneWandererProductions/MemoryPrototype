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
        private readonly int _capacity;
        private readonly List<AllocationEntry> _entries = new();
        private readonly Dictionary<int, AllocationEntry> _handleMap = new();
        private readonly SlowLane _slowLane;
        private int _nextHandleId = 1;

        public FastLane(int size, SlowLane slowLane)
        {
            _slowLane = slowLane;
            _capacity = size;
            Buffer = Marshal.AllocHGlobal(size);
        }

        public IntPtr Buffer { get; private set; }

        public void Dispose()
        {
            Marshal.FreeHGlobal(Buffer);
            _entries.Clear();
            _handleMap.Clear();
        }

        public MemoryHandle Allocate(int size)
        {
            var offset = FindFreeSpot(size);
            if (offset + size > _capacity)
                throw new OutOfMemoryException("FastLane: Not enough memory");

            var id = _nextHandleId++;
            var entry = new AllocationEntry { Offset = offset, Size = size, HandleId = id };

            _entries.Add(entry);
            _entries.Sort((a, b) => a.Offset.CompareTo(b.Offset));
            _handleMap[id] = entry;

            return new MemoryHandle(id, this);
        }

        public bool CanAllocate(int size)
        {
            try
            {
                return FindFreeSpot(size) + size <= _capacity;
            }
            catch
            {
                return false;
            }
        }

        public IntPtr Resolve(MemoryHandle handle)
        {
            if (!_handleMap.TryGetValue(handle.Id, out var entry))
                throw new InvalidOperationException("Invalid handle.");

            if (entry.IsStub && entry.RedirectTo.HasValue)
                return _slowLane.Resolve(entry.RedirectTo.Value);

            return Buffer + entry.Offset;
        }

        public void Free(MemoryHandle handle)
        {
            if (!_handleMap.TryGetValue(handle.Id, out var entry))
                throw new InvalidOperationException("Invalid handle.");

            if (entry.IsStub && entry.RedirectTo.HasValue)
            {
                _slowLane.Free(entry.RedirectTo.Value);
                _handleMap.Remove(handle.Id);
                _entries.Remove(entry);
            }
            else
            {
                _handleMap.Remove(handle.Id);
                _entries.Remove(entry);
            }
        }

        public unsafe void Compact()
        {
            var newBuffer = Marshal.AllocHGlobal(_capacity);
            var offset = 0;
            var newMap = new Dictionary<int, AllocationEntry>();

            for (var i = 0; i < _entries.Count; i++)
            {
                var entry = _entries[i];

                if (!entry.IsStub)
                {
                    System.Buffer.MemoryCopy((void*)(Buffer + entry.Offset), (void*)(newBuffer + offset), entry.Size,
                        entry.Size);
                    entry.Offset = offset;
                    offset += entry.Size;
                }

                _entries[i] = entry;
                newMap[entry.HandleId] = entry;
            }

            Marshal.FreeHGlobal(Buffer);
            Buffer = newBuffer;

            _handleMap.Clear();
            foreach (var kv in newMap)
                _handleMap[kv.Key] = kv.Value;

            _entries.Sort((a, b) => a.Offset.CompareTo(b.Offset));

            ValidateEntries();
        }

        public string DebugDump()
        {
            return string.Join("\n",
                _entries.ConvertAll(e => $"[FastLane] ID {e.HandleId} Offset {e.Offset} Size {e.Size}"));
        }

        public IEnumerable<MemoryHandle> GetHandles()
        {
            return _handleMap.Keys.Select(id => new MemoryHandle(id, this));
        }

        public double UsagePercentage()
        {
            var used = _entries.Where(entry => !entry.IsStub).Sum(entry => entry.Size);
            return (double)used / _capacity;
        }

        public bool HasHandle(MemoryHandle handle)
        {
            return _handleMap.ContainsKey(handle.Id);
        }

        public AllocationEntry GetEntry(MemoryHandle handle)
        {
            if (!_handleMap.TryGetValue(handle.Id, out var entry))
                throw new InvalidOperationException("FastLane: Invalid handle");

            return entry;
        }

        public void ReplaceWithStub(MemoryHandle fastHandle, MemoryHandle slowHandle)
        {
            if (!_handleMap.TryGetValue(fastHandle.Id, out var entry))
                throw new InvalidOperationException("FastLane: Invalid handle");

            entry.IsStub = true;
            entry.RedirectTo = slowHandle;

            // Update the map entry since AllocationEntry is a struct
            _handleMap[fastHandle.Id] = entry;
        }

        private int FindFreeSpot(int size)
        {
            var offset = 0;
            foreach (var entry in _entries)
            {
                if (offset + size <= entry.Offset)
                    return offset;

                offset = entry.Offset + entry.Size;
            }

            return offset;
        }

        private void ValidateEntries()
        {
            var lastEnd = 0;
            foreach (var entry in _entries)
            {
                if (entry.Offset < lastEnd)
                    throw new InvalidOperationException(
                        $"Entries overlap or are not sorted: entry ID {entry.HandleId}");

                lastEnd = entry.Offset + entry.Size;

                if (lastEnd > _capacity)
                    throw new InvalidOperationException($"Entry ID {entry.HandleId} exceeds buffer capacity");
            }
        }
    }
}