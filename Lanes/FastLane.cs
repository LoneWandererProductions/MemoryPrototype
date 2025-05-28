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
        public readonly IntPtr Buffer;
        private int _nextHandleId = 1;

        /// <summary>
        /// Initializes a new instance of the <see cref="FastLane"/> class.
        /// </summary>
        /// <param name="size">The size.</param>
        /// <param name="slowLane">The slow lane. FastLane is tightly coupled to slowLane. Memorymanager will handle the memory of Fastlane.</param>
        public FastLane(int size, SlowLane slowLane)
        {
            _slowLane = slowLane;
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
                // Redirect resolution to slow lane
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
                // Optionally, remove stub entry or keep for tracking
                _handleMap.Remove(handle.Id);
                _entries.Remove(entry);
            }
            else
            {
                // Free in fast lane normally
                _handleMap.Remove(handle.Id);
                _entries.Remove(entry);
            }
        }


        public unsafe void Compact()
        {
            var newBuffer = Marshal.AllocHGlobal(_capacity);
            var offset = 0;
            var newMap = new Dictionary<int, AllocationEntry>();

            foreach (var entry in _entries)
            {
                System.Buffer.MemoryCopy((void*)(Buffer + entry.Offset), (void*)(newBuffer + offset), entry.Size,
                    entry.Size);
                entry.Offset = offset;
                offset += entry.Size;
                newMap[entry.HandleId] = entry;
            }

            Marshal.FreeHGlobal(Buffer);
            _handleMap.Clear();
            foreach (var kv in newMap)
                _handleMap[kv.Key] = kv.Value;

            _entries.Sort((a, b) => a.Offset.CompareTo(b.Offset));
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

            // Update map entry (if necessary)
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
    }
}