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
    /// <inheritdoc />
    /// <summary>
    /// A Linear/Bump allocator designed for large or unpredictable blobs of memory.
    /// Allocations are extremely fast, but memory is only reclaimed when the entire
    /// manager is compacted/cleared.
    /// </summary>
    /// <seealso cref="Core.IMemoryLane" />
    public sealed class BlobManager : IMemoryLane
    {
        /// <summary>
        /// The starting identifier
        /// </summary>
        public const int StartingId = -1000000;

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
        private int _nextId = StartingId;

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

        /// <inheritdoc />
        public int EntryCount => _entries.Count;

        /// <inheritdoc />
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
            if (_entries.Count == 0) return;

            // 1. Get all surviving entries and sort them by their physical offset
            var validEntries = new List<BlobEntry>(_entries.Values);
            validEntries.Sort((a, b) => a.Offset.CompareTo(b.Offset));

            int currentOffset = 0;

            // 2. Slide each entry as far left as possible
            foreach (var entry in validEntries)
            {
                if (entry.Offset > currentOffset)
                {
                    unsafe
                    {
                        System.Buffer.MemoryCopy(
                            (void*)(_buffer + entry.Offset),
                            (void*)(_buffer + currentOffset),
                            entry.Size,
                            entry.Size);
                    }
                }

                // 3. Update the entry's metadata with its new physical address
                var updatedEntry = entry;
                updatedEntry.Offset = currentOffset;
                _entries[entry.Id] = updatedEntry;

                currentOffset += entry.Size;
            }

            // 4. Update the global allocator offset
            _nextFreeOffset = currentOffset;

            OnCompaction?.Invoke(nameof(BlobManager));
        }

        /// <inheritdoc />
        public double UsagePercentage()
        {
            return (_nextFreeOffset / (double)_capacity) * 100.0;
        }

        /// <inheritdoc />
        public int FreeSpace() => _capacity - _nextFreeOffset;

        /// <inheritdoc />
        /// <summary>
        /// Calculates the percentage of "dead" space behind the bump pointer
        /// that can be reclaimed via Compaction.
        /// </summary>
        public int EstimateFragmentation()
        {
            int allocatedBytes = _nextFreeOffset;
            if (allocatedBytes == 0) return 0;

            int livingBytes = 0;
            foreach (var blob in _entries.Values)
            {
                livingBytes += blob.Size;
            }

            int wastedBytes = allocatedBytes - livingBytes;

            return (int)(((double)wastedBytes / allocatedBytes) * 100);
        }

        /// <inheritdoc />
        public int StubCount() => 0;

        /// <inheritdoc />
        public IEnumerable<MemoryHandle> GetHandles()
        {
            foreach (var key in _entries.Keys)
            {
                yield return new MemoryHandle(key, this);
            }
        }

        /// <inheritdoc />
        public string DebugDump()
        {
            return $"BlobManager Dump\nAllocations: {_entries.Count}\nUsed: {_nextFreeOffset}/{_capacity} bytes";
        }

        /// <inheritdoc />
        /// <summary>
        /// Generates a visual string representation of the BlobManager's memory layout.
        /// █ = Living Data, - = Dead Gap (Wasted), ░ = Untouched Capacity
        /// </summary>
        public string DebugVisualMap()
        {
            if (_capacity == 0) return "[]";

            const int mapResolution = 80; // Width of the console map
            char[] map = new char[mapResolution];
            double bytesPerChar = (double)_capacity / mapResolution;

            for (int i = 0; i < mapResolution; i++)
            {
                double startByte = i * bytesPerChar;
                double endByte = (i + 1) * bytesPerChar;

                // If this bucket is completely past the bump pointer, it's untouched.
                if (startByte >= _nextFreeOffset)
                {
                    map[i] = '░';
                    continue;
                }

                // Otherwise, check if any living blob intersects this bucket
                bool isLiving = false;
                foreach (var blob in _entries.Values)
                {
                    // Intersection math: Blob starts before bucket ends AND Blob ends after bucket starts
                    if (blob.Offset < endByte && (blob.Offset + blob.Size) > startByte)
                    {
                        isLiving = true;
                        break;
                    }
                }

                map[i] = isLiving ? '█' : '-';
            }

            return $"Blob Map: [{new string(map)}]\nLegend: █=Used, -=Gap, ░=Free";
        }

        /// <inheritdoc />
        public string DebugRedirections() => "[BlobRedirects not applicable]";
    }
}