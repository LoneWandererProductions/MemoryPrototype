using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Core;

namespace Core.MemoryArenaPrototype.Core
{
    public sealed class BlobManager : IMemoryLane
    {
        private readonly IntPtr _buffer;
        private readonly int _capacity;

        private int _nextId = -10000; // Negative IDs reserved for blob allocations
        private int _nextFreeOffset = 0;

        private readonly Dictionary<int, BlobEntry> _entries = new();

        public BlobManager(IntPtr buffer, int capacity)
        {
            _buffer = buffer;
            _capacity = capacity;
        }

        public MemoryHandle Allocate(
            int size,
            AllocationPriority priority = AllocationPriority.Normal,
            AllocationHints hints = AllocationHints.None,
            string? debugName = null,
            int currentFrame = 0)
        {
            if (_nextFreeOffset + size > _capacity)
                Compact();

            if (_nextFreeOffset + size > _capacity)
                throw new OutOfMemoryException("BlobManager: Out of memory");

            int id = _nextId--;

            _entries[id] = new BlobEntry
            {
                Id = id,
                Offset = _nextFreeOffset,
                Size = size,
                DebugName = debugName,
                AllocationFrame = currentFrame
            };

            var handle = new MemoryHandle(id, this);
            _nextFreeOffset += size;
            return handle;
        }

        public void Free(MemoryHandle handle)
        {
            if (!_entries.Remove(handle.Id))
                throw new InvalidOperationException($"BlobManager: Invalid handle {handle.Id}");
        }

        public IntPtr Resolve(MemoryHandle handle)
        {
            if (!_entries.TryGetValue(handle.Id, out var entry))
                throw new InvalidOperationException($"BlobManager: Invalid handle {handle.Id}");

            return _buffer + entry.Offset;
        }

        public bool HasHandle(MemoryHandle handle) => _entries.ContainsKey(handle.Id);

        public AllocationEntry GetEntry(MemoryHandle handle)
        {
            if (!_entries.TryGetValue(handle.Id, out var blob))
                throw new InvalidOperationException($"BlobManager: Invalid handle {handle.Id}");

            return new AllocationEntry
            {
                HandleId = blob.Id,
                Offset = blob.Offset,
                Size = blob.Size,
                DebugName = blob.DebugName,
                AllocationFrame = blob.AllocationFrame,
                IsStub = false,
                RedirectTo = null,
                Priority = AllocationPriority.Normal,
                Hints = AllocationHints.None,
                LastAccessFrame = blob.AllocationFrame
            };
        }

        public int GetAllocationSize(MemoryHandle handle)
        {
            if (!_entries.TryGetValue(handle.Id, out var blob))
                throw new InvalidOperationException($"BlobManager: Invalid handle {handle.Id}");

            return blob.Size;
        }

        public void FreeMany(IEnumerable<MemoryHandle> handles)
        {
            foreach (var handle in handles)
                Free(handle);
        }

        public void Compact()
        {
            // Trivial "reset" for now — upgrade later if needed
            _entries.Clear();
            _nextFreeOffset = 0;
        }

        public string DebugDump()
        {
            return $"BlobManager Dump\nAllocations: {_entries.Count}\nUsed: {_nextFreeOffset}/{_capacity}";
        }

        public double UsagePercentage()
        {
            return (_nextFreeOffset / (double)_capacity) * 100.0;
        }

        public IEnumerable<MemoryHandle> GetHandles()
        {
            foreach (var kv in _entries)
                yield return new MemoryHandle(kv.Key, this);
        }

        public event Action<string>? OnCompaction;
        public event Action<string, int, int>? OnAllocationExtension;

        public string DebugVisualMap() => "[BlobMap not implemented]";
        public string DebugRedirections() => "[BlobRedirects not applicable]";
        public int FreeSpace() => _capacity - _nextFreeOffset;
        public int EstimateFragmentation() => 0;
        public int StubCount() => 0;

        public bool CanAllocate(int size)
        {
            // Optionally add a small safety margin (e.g., 5–10%) if you want to avoid edge overflows
            return (_nextFreeOffset + size) <= _capacity;
        }

    }
}
