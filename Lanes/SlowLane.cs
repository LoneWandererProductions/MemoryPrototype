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
        private const double SafetyMargin = 0.10; // 10% free space
        public IntPtr Buffer { get; private set; }
        private readonly int _capacity;
        private readonly List<AllocationEntry> _entries = new();

        private readonly Dictionary<int, AllocationEntry> _handleMap = new();
        private int _nextHandleId = 1000; // Separate ID range

        public SlowLane(int size)
        {
            _capacity = size;
            Buffer = Marshal.AllocHGlobal(size);
        }

        public void Dispose()
        {
            Marshal.FreeHGlobal(Buffer);
            _entries.Clear();
            _handleMap.Clear();
        }

        public MemoryHandle Allocate(int size)
        {
            if (!CanAllocate(size))
                throw new OutOfMemoryException("SlowLane: Cannot allocate");

            var offset = FindFreeSpot(size);
            var id = _nextHandleId++;
            var entry = new AllocationEntry {Offset = offset, Size = size, HandleId = id};

            _entries.Add(entry);
            _entries.Sort((a, b) => a.Offset.CompareTo(b.Offset));
            _handleMap[id] = entry;

            return new MemoryHandle(id, this);
        }

        public bool CanAllocate(int size)
        {
            return GetUsed() + size <= _capacity * (1.0 - SafetyMargin);
        }

        public IntPtr Resolve(MemoryHandle handle)
        {
            if (!_handleMap.TryGetValue(handle.Id, out var entry))
                throw new InvalidOperationException("SlowLane: Invalid handle");

            return Buffer + entry.Offset;
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

        public void Free(MemoryHandle handle)
        {
            if (_handleMap.Remove(handle.Id, out var entry))
                _entries.Remove(entry);
        }

        public unsafe void Compact()
        {
            var newBuffer = Marshal.AllocHGlobal(_capacity);
            var offset = 0;
            var newMap = new Dictionary<int, AllocationEntry>();

            foreach (var entry in _entries)
            {
                if (!entry.IsStub)
                {
                    System.Buffer.MemoryCopy((void*)(Buffer + entry.Offset), (void*)(newBuffer + offset), entry.Size, entry.Size);
                    entry.Offset = offset;
                    offset += entry.Size;
                }
                else
                {
                    // Keep stub entries as-is or remove if you want
                }
                newMap[entry.HandleId] = entry;
            }

            Marshal.FreeHGlobal(Buffer);

            // Update buffer reference
            Buffer = newBuffer;

            _handleMap.Clear();
            foreach (var kv in newMap)
                _handleMap[kv.Key] = kv.Value;

            _entries.Sort((a, b) => a.Offset.CompareTo(b.Offset));
        }

        public string DebugDump()
        {
            return string.Join("\n",
                _entries.ConvertAll(e => $"[SlowLane] ID {e.HandleId} Offset {e.Offset} Size {e.Size}"));
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

        private int GetUsed()
        {
            var used = 0;
            foreach (var e in _entries)
                used += e.Size;
            return used;
        }
    }
}