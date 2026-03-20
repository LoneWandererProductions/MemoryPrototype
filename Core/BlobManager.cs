/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     Core
 * FILE:        BlobManager.cs
 * PURPOSE:     Internal linear/bump allocator for unpredictable or large blob data.
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

using Core.MemoryArenaPrototype.Core;
using System;
using System.Collections.Generic;

namespace Core
{
    /// <summary>
    /// A Linear/Bump allocator designed for large or unpredictable blobs of memory.
    /// Allocations are extremely fast, but memory is only reclaimed when the entire
    /// manager is compacted/cleared.
    /// </summary>
    /// <seealso cref="Core.IMemoryLane" />
    public sealed class BlobManager : IMemoryLane
    {
        /// <summary>
        /// The buffer
        /// </summary>
        private readonly nint _buffer;

        /// <summary>
        /// The capacity
        /// </summary>
        private readonly int _capacity;

        /// <summary>
        /// Negative IDs are standard convention for SlowLane/Blob allocations
        /// </summary>
        private int _nextId = -10000;

        /// <summary>
        /// The next free offset
        /// </summary>
        private int _nextFreeOffset = 0;

        /// <summary>
        ///     Maps the handle IDs to their actual blob metadata.
        /// </summary>
        private readonly Dictionary<int, BlobEntry> _entries = new();

#if DEBUG
        /// <summary>
        /// The debug names
        /// </summary>
        private readonly Dictionary<int, string> _debugNames = new();
#endif

        public event Action<string>? OnCompaction;

        /// <inheritdoc />
        /// <summary>#
        ///  Defined by interface, but not utilized in a fixed-size bump allocator
        /// Occurs when [on allocation extension].
        /// </summary>
        public event Action<string, int, int>? OnAllocationExtension;

        /// <summary>
        ///     Initializes a new instance of the BlobManager.
        /// </summary>
        public BlobManager(nint buffer, int capacity)
        {
            _buffer = buffer;
            _capacity = capacity;
        }

        /// <inheritdoc />
        /// <summary>
        ///     Checks if the memory lane can allocate a block of the specified size.
        /// </summary>
        public bool CanAllocate(int size)
        {
            return (_nextFreeOffset + size) <= _capacity;
        }

        /// <inheritdoc />
        /// <summary>
        ///     Allocates a contiguous block of memory by bumping the offset forward.
        /// </summary>
        public MemoryHandle Allocate(
            int size,
            AllocationPriority priority = AllocationPriority.Normal,
            AllocationHints hints = AllocationHints.None,
            string? debugName = null,
            int currentFrame = 0)
        {
            if (!CanAllocate(size))
                throw new OutOfMemoryException($"BlobManager: Out of memory. Requested {size} bytes, but only {FreeSpace()} available.");

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

            // Bump the allocator forward
            _nextFreeOffset += size;

            return new MemoryHandle(id, this);
        }

        /// <inheritdoc />
        /// <summary>
        ///     Invalidates the handle. 
        ///     Note: As a bump allocator, this does NOT immediately reclaim the physical memory bytes.
        /// </summary>
        public void Free(MemoryHandle handle)
        {
            if (!_entries.Remove(handle.Id))
                throw new InvalidOperationException($"BlobManager: Invalid or double-freed handle {handle.Id}");

#if DEBUG
            _debugNames.Remove(handle.Id);
#endif
        }

        public void FreeMany(IEnumerable<MemoryHandle> handles)
        {
            foreach (var handle in handles)
            {
                Free(handle);
            }
        }

        /// <inheritdoc />
        public nint Resolve(MemoryHandle handle)
        {
            if (!_entries.TryGetValue(handle.Id, out var entry))
                throw new InvalidOperationException($"BlobManager: Cannot resolve invalid handle {handle.Id}");

            return _buffer + entry.Offset;
        }

        /// <inheritdoc />
        public bool HasHandle(MemoryHandle handle) => _entries.ContainsKey(handle.Id);

        /// <inheritdoc />
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
                RedirectToId = 0,
                Priority = AllocationPriority.Normal,
                Hints = AllocationHints.None,
                LastAccessFrame = blob.AllocationFrame
            };
        }

        /// <inheritdoc />
        public int GetAllocationSize(MemoryHandle handle)
        {
            if (!_entries.TryGetValue(handle.Id, out var blob))
                throw new InvalidOperationException($"BlobManager: Invalid handle {handle.Id}");

            return blob.Size;
        }

        /// <inheritdoc />
        /// <summary>
        ///     WARNING: For a linear allocator, compaction means a total reset.
        ///     This wipes all entries and resets the offset to 0. 
        /// </summary>
        public void Compact()
        {
            _entries.Clear();
#if DEBUG
            _debugNames.Clear();
#endif
            _nextFreeOffset = 0;
            OnCompaction?.Invoke(nameof(BlobManager));
        }

        /// <inheritdoc />
        /// <summary>
        /// Usages the percentage.
        /// </summary>
        /// <returns>
        /// Used memory Percentage
        /// </returns>
        public double UsagePercentage()
        {
            return (_nextFreeOffset / (double)_capacity) * 100.0;
        }

        /// <summary>
        /// Frees the space.
        /// </summary>
        /// <returns>
        /// Free Memory
        /// </returns>
        public int FreeSpace() => _capacity - _nextFreeOffset;

        /// <summary>
        /// A linear allocator doesn't suffer from external fragmentation in the traditional sense, 
        /// because it doesn't leave gaps—it just consumes until full.
        /// </summary>
        /// <returns>
        /// Estimated Fragmentation.
        /// </returns>
        public int EstimateFragmentation() => 0;

        /// <summary>
        /// Stubs the count.
        /// </summary>
        /// <returns>
        /// Stub Count.
        /// </returns>
        public int StubCount() => 0;

        /// <summary>
        /// Gets the handles.
        /// </summary>
        /// <returns></returns>
        public IEnumerable<MemoryHandle> GetHandles()
        {
            foreach (var key in _entries.Keys)
            {
                yield return new MemoryHandle(key, this);
            }
        }

        /// <summary>
        /// Provides a debug string dump describing the internal state of the memory lane.
        /// Useful for diagnostics and debugging allocation behavior.
        /// </summary>
        /// <returns>
        /// A string representation of the current memory lane state.
        /// </returns>
        public string DebugDump()
        {
            return $"BlobManager Dump\nAllocations: {_entries.Count}\nUsed: {_nextFreeOffset}/{_capacity} bytes";
        }

        /// <summary>
        /// Debugs the visual map.
        /// </summary>
        /// <returns></returns>
        public string DebugVisualMap() => "[BlobMap not implemented]";

        /// <summary>
        /// Debugs the redirections.
        /// </summary>
        /// <returns></returns>
        public string DebugRedirections() => "[BlobRedirects not applicable]";
    }
}