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
        private AllocationEntry[] _entries = new AllocationEntry[128];
        private int _entryCount = 0;

        private readonly Dictionary<int, int> _handleIndex = new(); // Maps handleId → index into _entries
        private readonly SlowLane _slowLane;

        public IntPtr Buffer { get; private set; }
        private int _nextHandleId = 1;

        public FastLane(int size, SlowLane slowLane)
        {
            _slowLane = slowLane;
            _capacity = size;
            Buffer = Marshal.AllocHGlobal(size);
        }

        public void Dispose()
        {
            Marshal.FreeHGlobal(Buffer);
            _handleIndex.Clear();
            _entries = null;
        }

        public MemoryHandle Allocate(int size)
        {
            var offset = FindFreeSpot(size);
            if (offset + size > _capacity)
                throw new OutOfMemoryException("FastLane: Not enough memory");

            if (_entryCount >= _entries.Length)
                Array.Resize(ref _entries, _entries.Length * 2);

            var id = _nextHandleId++;
            _entries[_entryCount] = new AllocationEntry { Offset = offset, Size = size, HandleId = id };
            _handleIndex[id] = _entryCount;
            _entryCount++;

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
            var last = _entryCount - 1;
            if (index != last)
            {
                _entries[index] = _entries[last];
                _handleIndex[_entries[index].HandleId] = index;
            }

            _handleIndex.Remove(handle.Id);
            _entryCount--;
        }

        public IEnumerable<MemoryHandle> GetHandles()
        {
            return _handleIndex.Keys.Select(id => new MemoryHandle(id, this));
        }

        public double UsagePercentage()
        {
            var used = 0;
            for (var i = 0; i < _entryCount; i++)
                if (!_entries[i].IsStub) used += _entries[i].Size;

            return (double)used / _capacity;
        }

        public unsafe void Compact()
        {
            var newBuffer = Marshal.AllocHGlobal(_capacity);
            var offset = 0;

            for (var i = 0; i < _entryCount; i++)
            {
                var entry = _entries[i];

                if (!entry.IsStub)
                {
                    void* source = (byte*)Buffer + entry.Offset;
                    void* target = (byte*)newBuffer + offset;

                    System.Buffer.MemoryCopy(source, target, _capacity - offset, entry.Size);
                    entry.Offset = offset;
                    offset += entry.Size;
                }

                _entries[i] = entry;
                _handleIndex[entry.HandleId] = i;
            }

            Marshal.FreeHGlobal(Buffer);
            Buffer = newBuffer;
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

        private int FindFreeSpot(int size)
        {
            // Sort entries in-place by Offset ascending before searching
            Array.Sort(_entries, 0, _entryCount, new AllocationEntryOffsetComparer());

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


        public string DebugDump()
        {
            var sb = new System.Text.StringBuilder(_entryCount * 48); // Rough estimate per line
            for (var i = 0; i < _entryCount; i++)
            {
                var entry = _entries[i];
                sb.Append("[FastLane] ID ").Append(entry.HandleId)
                    .Append(" Offset ").Append(entry.Offset)
                    .Append(" Size ").Append(entry.Size)
                    .AppendLine();
            }

            return sb.ToString();
        }
    }
}