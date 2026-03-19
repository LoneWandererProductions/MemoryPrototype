/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     Core.MemoryArenaPrototype.Core
 * FILE:        BlobManager.cs
 * PURPOSE:     Your file purpose here
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

using Core.MemoryArenaPrototype.Core;
using System;
using System.Collections.Generic;

namespace Core
{
    public sealed class BlobManager : IMemoryLane
    {
        private readonly nint _buffer;
        private readonly int _capacity;

        private int _nextId = -10000;
        private int _nextFreeOffset = 0;

        private readonly Dictionary<int, BlobEntry> _entries = new();

#if DEBUG
        private readonly Dictionary<int, string> _debugNames = new();
#endif

        public BlobManager(nint buffer, int capacity)
        {
            _buffer = buffer;
            _capacity = capacity;
        }

        public bool CanAllocate(int size)
        {
            return _nextFreeOffset + size <= _capacity;
        }

        public MemoryHandle Allocate(
            int size,
            AllocationPriority priority = AllocationPriority.Normal,
            AllocationHints hints = AllocationHints.None,
            string debugName = null,
            int currentFrame = 0)
        {
            if (!CanAllocate(size))
                throw new OutOfMemoryException("BlobManager: Out of memory. Buffer is full.");

            int id = _nextId--;
            int allocatedOffset = _nextFreeOffset;

            _entries[id] = new BlobEntry
            {
                Id = id,
                Offset = allocatedOffset,
                Size = size,
                AllocationFrame = currentFrame
            };

#if DEBUG
            if (!string.IsNullOrEmpty(debugName))
            {
                _debugNames[id] = debugName;
            }
#endif

            _nextFreeOffset += size;
            return new MemoryHandle(id, this);
        }

        public void Free(MemoryHandle handle)
        {
            // In a bump allocator, individual frees don't reclaim memory.
            // We just invalidate the handle so it can't be used anymore.
            if (!_entries.Remove(handle.Id))
                throw new InvalidOperationException($"BlobManager: Invalid handle {handle.Id}");

#if DEBUG
            _debugNames.Remove(handle.Id);
#endif
        }

        public nint Resolve(MemoryHandle handle)
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
                AllocationFrame = blob.AllocationFrame,
                IsStub = false,
                RedirectToId = 0, // Updated for Blittable struct
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
            // Only the owner of the BlobManager should call Compact 
            // (e.g., at the end of a render frame).
            // It completely wipes the arena.
            _entries.Clear();
#if DEBUG
            _debugNames.Clear();
#endif
            _nextFreeOffset = 0;
            OnCompaction?.Invoke(nameof(BlobManager));
        }

        public string DebugDump()
        {
            return $"BlobManager Dump\nAllocations: {_entries.Count}\nUsed: {_nextFreeOffset}/{_capacity}";
        }

        public double UsagePercentage()
        {
            return _nextFreeOffset / (double)_capacity * 100.0;
        }

        public IEnumerable<MemoryHandle> GetHandles()
        {
            foreach (var key in _entries.Keys)
                yield return new MemoryHandle(key, this);
        }

        public event Action<string> OnCompaction;
        public event Action<string, int, int> OnAllocationExtension;

        public string DebugVisualMap() => "[BlobMap not implemented]";
        public string DebugRedirections() => "[BlobRedirects not applicable]";
        public int FreeSpace() => _capacity - _nextFreeOffset;
        public int EstimateFragmentation() => 0;
        public int StubCount() => 0;
    }
}