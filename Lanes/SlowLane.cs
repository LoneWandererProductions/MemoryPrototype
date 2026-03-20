/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     Lanes
 * FILE:        SlowLane.cs
 * PURPOSE:     Memory store for long lived data and stuff we could not hold into he slow lane.
 *              Ids for Allocations is always negative here.
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

// ReSharper disable EventNeverSubscribedTo.Global

#nullable enable
using Core;
using Core.MemoryArenaPrototype.Core;
using ExtendedSystemObjects;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Lanes
{
    /// <inheritdoc cref="IMemoryLane" />
    /// <summary>
    ///     The SlowLane for all the sorted out stuff and bigger longer resting data.
    /// </summary>
    /// <seealso cref="T:Core.IMemoryLane" />
    /// <seealso cref="T:System.IDisposable" />
    public sealed class SlowLane : IMemoryLane, IDisposable
    {
#if DEBUG
        /// <summary>
        /// The debug names
        /// </summary>
        private readonly Dictionary<int, string> _debugNames = new();
#endif

        /// <summary>
        ///     The safety margin
        /// </summary>
        private const double SafetyMargin = 0.10; // 10% free space reserved

        /// <summary>
        ///     The free ids
        /// </summary>
        private readonly UnmanagedIntList _freeIds = new(128);

        /// <summary>
        ///     The free slots, we reuse freed slots
        /// </summary>
        private readonly UnmanagedIntList _freeSlots = new(128);

        /// <summary>
        ///     The handle index
        /// </summary>
        private readonly UnmanagedMap<int> _handleIndex = new(7); // handleId -> entries array index

        /// <summary>
        ///     The allocated entries
        /// </summary>
        private AllocationEntry[] _entries;

        /// <summary>
        ///     The next handle identifier
        /// </summary>
        private int _nextHandleId = -1;

        /// <summary>
        /// The free blocks
        /// </summary>
        private FreeBlock[] _freeBlocks = new FreeBlock[128];

        /// <summary>
        /// The free block count
        /// </summary>
        private int _freeBlockCount = 0;

        /// <summary>
        ///     Initializes a new instance of the <see cref="SlowLane" /> class.
        /// </summary>
        /// <param name="capacity">The capacity.</param>
        /// <param name="maxEntries">The maximum entries.</param>
        public SlowLane(int capacity, int maxEntries = 1024)
        {
            Capacity = capacity;
            Buffer = Marshal.AllocHGlobal(capacity);
            _entries = new AllocationEntry[maxEntries];

            // Initialize the entire SlowLane as one free block
            _freeBlocks[0] = new FreeBlock { Offset = 0, Size = Capacity };
            _freeBlockCount = 1;
        }

        /// <summary>
        ///     Gets or sets the buffer.
        /// </summary>
        /// <value>
        ///     The buffer.
        /// </value>
        public IntPtr Buffer { get; private set; }

        /// <summary>
        ///     Gets the capacity.
        /// </summary>
        /// <value>
        ///     The capacity.
        /// </value>
        public int Capacity { get; }

        /// <summary>
        ///     Gets the entry count.
        /// </summary>
        /// <value>
        ///     The entry count.
        /// </value>
        public int EntryCount { get; private set; }

        /// <inheritdoc />
        /// <summary>
        ///     Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            Marshal.FreeHGlobal(Buffer);
            EntryCount = 0;
            _handleIndex.Clear();
            _freeSlots.Clear();
            _freeIds.Clear();
        }

        /// <inheritdoc />
        /// <summary>
        ///     Allocates the specified size.
        /// </summary>
        /// <param name="size">The size.</param>
        /// <param name="priority">The priority.</param>
        /// <param name="hints">The hints.</param>
        /// <param name="debugName">Name of the debug.</param>
        /// <param name="currentFrame">The current frame.</param>
        /// <returns>Allocated memory and a reference.</returns>
        /// <exception cref="T:System.OutOfMemoryException">SlowLane: Cannot allocate</exception>
        public MemoryHandle Allocate(
            int size,
            AllocationPriority priority = AllocationPriority.Normal,
            AllocationHints hints = AllocationHints.None,
            string? debugName = null,
            int currentFrame = 0)
        {
            if (!CanAllocate(size))
                throw new OutOfMemoryException("SlowLane: Cannot allocate");

            //TODO  Optimize
            var offset = MemoryLaneUtils.FindFreeSpot(size, ref _freeBlocks, ref _freeBlockCount);

            if (offset == -1)
                throw new OutOfMemoryException("SlowLane: Cannot allocate - No contiguous block large enough.");

            var slotIndex = _freeSlots.Length > 0 ? _freeSlots.Pop() : EntryCount++;
            EnsureEntryCapacity(slotIndex);

            //So we reuse freed handles here
            var id = MemoryLaneUtils.GetNextId(_freeIds, ref _nextHandleId);

#if DEBUG
            if (!string.IsNullOrEmpty(debugName))
            {
                _debugNames[id] = debugName;
            }
#endif

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
                RedirectToId = 0,
                AllocationFrame = currentFrame,
                LastAccessFrame = currentFrame
            };

            _handleIndex[id] = slotIndex;

            return new MemoryHandle(id, this);
        }

        /// <inheritdoc />
        /// <summary>
        ///     Determines whether this instance can allocate the specified size.
        /// </summary>
        /// <param name="size">The size.</param>
        /// <returns>
        ///     <c>true</c> if this instance can allocate the specified size; otherwise, <c>false</c>.
        /// </returns>
        public bool CanAllocate(int size)
        {
            if (GetUsed() + size > Capacity * (1.0 - SafetyMargin))
                return false;

            // Fast, read-only check to see if a contiguous block exists
            for (int i = 0; i < _freeBlockCount; i++)
            {
                if (_freeBlocks[i].Size >= size)
                    return true;
            }

            return false;
        }

        /// <inheritdoc />
        /// <summary>
        ///     Resolves the specified handle.
        /// </summary>
        /// <param name="handle">The handle.</param>
        /// <returns>Pointer to the stored data.</returns>
        /// <exception cref="T:System.InvalidOperationException">
        ///     SlowLane: Invalid handle
        ///     or
        ///     SlowLane: Cannot resolve stub entry without redirection
        /// </exception>
        public IntPtr Resolve(MemoryHandle handle)
        {
            if (!_handleIndex.TryGetValue(handle.Id, out var index))
                throw new InvalidOperationException("SlowLane: Invalid handle");

            var entry = _entries[index];
            if (entry.IsStub && !entry.RedirectTo.HasValue)
                throw new InvalidOperationException("SlowLane: Cannot resolve stub entry without redirection");

            return Buffer + entry.Offset;
        }

        /// <inheritdoc />
        /// <summary>
        ///     Frees the specified handle.
        /// </summary>
        /// <param name="handle">The handle.</param>
        /// <exception cref="T:System.InvalidOperationException">SlowLane: Invalid handle</exception>
        public void Free(MemoryHandle handle)
        {
            if (!_handleIndex.TryRemove(handle.Id, out var index))
                throw new InvalidOperationException($"SlowLane: Invalid handle {handle.Id}");

            _entries[index] = default;
            _freeSlots.Push(index);
            _freeIds.Push(handle.Id);

#if DEBUG
            _debugNames.Remove(handle.Id);
#endif

            EntryCount++;
        }

        /// <summary>
        /// Frees the many.
        /// </summary>
        /// <param name="handles">The handles.</param>
        /// <exception cref="System.InvalidOperationException">SlowLane: Invalid handle {handle.Id}</exception>
        public unsafe void FreeMany(MemoryHandle[] handles) // Or ReadOnlySpan<MemoryHandle>
        {
            var span = handles.AsSpan();
            int count = span.Length;

            // We'll collect the IDs and Indices in temporary buffers to batch-push
            // Using stackalloc for small-to-medium batches avoids GC pressure
            int* ids = stackalloc int[count];
            int* indices = stackalloc int[count];

            for (int i = 0; i < count; i++)
            {
                int id = span[i].Id;

                if (!_handleIndex.TryRemove(id, out var index))
                    throw new InvalidOperationException($"SlowLane: Invalid handle {id}");

                // Clear entry data
                _entries[index] = default;

                ids[i] = id;
                indices[i] = index;
            }

            // Batch add to our unmanaged lists
            _freeIds.PushRange(new ReadOnlySpan<int>(ids, count));
            _freeSlots.PushRange(new ReadOnlySpan<int>(indices, count));

            EntryCount += count;
        }

        /// <inheritdoc />
        /// <summary>
        ///     Compacts this instance.
        /// </summary>
        public unsafe void Compact()
        {
            if (_entries == null || _handleIndex.Count == 0) return;

            var newBuffer = Marshal.AllocHGlobal(Capacity);
            var offset = 0;

            // 1. Extract only the living, valid entries using our dictionary
            var validEntries = new List<AllocationEntry>(_handleIndex.Count);
            foreach (var index in _handleIndex.Values)
            {
                var entry = _entries[index];
                if (!entry.IsStub)
                {
                    validEntries.Add(entry);
                }
            }

            // 2. Sort them by their physical Offset in the buffer
            validEntries.Sort((a, b) => a.Offset.CompareTo(b.Offset));

            var writeIndex = 0;
            var newHandleIndex = new Dictionary<int, int>(validEntries.Count);

            // 3. Copy them sequentially to the new buffer
            foreach (var entry in validEntries)
            {
                var currentEntry = entry; // Make a local copy to modify

                System.Buffer.MemoryCopy(
                    (void*)(Buffer + currentEntry.Offset),
                    (void*)(newBuffer + offset),
                    currentEntry.Size,
                    currentEntry.Size);

                currentEntry.Offset = offset;
                offset += currentEntry.Size;

                EnsureEntryCapacity(writeIndex);
                _entries[writeIndex] = currentEntry;
                newHandleIndex[currentEntry.HandleId] = writeIndex;

                writeIndex++;
            }

            // 4. Clear all remaining slots
            for (var i = writeIndex; i < _entries.Length; i++)
            {
                _entries[i] = default;
            }

            // 5. Update the internal state
            Marshal.FreeHGlobal(Buffer);
            Buffer = newBuffer;

            _handleIndex.Clear();
            foreach (var kv in newHandleIndex)
            {
                _handleIndex[kv.Key] = kv.Value;
            }

            EntryCount = writeIndex;
            _freeSlots.Clear(); // No more holes in the array!

            // 6. THE MISSING FIX: Reset the Free-List!
            _freeBlocks[0] = new FreeBlock
            {
                Offset = offset,
                Size = Capacity - offset
            };
            _freeBlockCount = 1;

            OnCompaction?.Invoke(nameof(SlowLane));
        }

        /// <inheritdoc />
        /// <summary>
        ///     Determines whether the specified handle has handle.
        /// </summary>
        /// <param name="handle">The handle.</param>
        /// <returns>
        ///     <c>true</c> if the specified handle has handle; otherwise, <c>false</c>.
        /// </returns>
        public bool HasHandle(MemoryHandle handle)
        {
            return MemoryLaneUtils.HasHandle(handle, _handleIndex);
        }

        /// <inheritdoc />
        /// <summary>
        ///     Gets the entry.
        /// </summary>
        /// <param name="handle">The handle.</param>
        /// <returns>Get the Entry by handle.</returns>
        public AllocationEntry GetEntry(MemoryHandle handle)
        {
            return MemoryLaneUtils.GetEntry(handle, _handleIndex, _entries, nameof(SlowLane));
        }

        /// <inheritdoc />
        /// <summary>
        ///     Gets the size of the allocation.
        /// </summary>
        /// <param name="handle">The handle.</param>
        /// <returns>Size of allocated space.</returns>
        public int GetAllocationSize(MemoryHandle handle)
        {
            return MemoryLaneUtils.GetAllocationSize(handle, _handleIndex, _entries, nameof(SlowLane));
        }

        /// <inheritdoc />
        /// <summary>
        ///     Debugs the dump.
        /// </summary>
        /// <returns>Basic Debug Info</returns>
        public string DebugDump()
        {
            return MemoryLaneUtils.DebugDump(_entries, EntryCount);
        }

        /// <summary>
        ///     Occurs when [on compaction].
        /// </summary>
        public event Action<string>? OnCompaction;

        /// <summary>
        ///     Occurs when [on allocation extension].
        /// </summary>
        public event Action<string, int, int>? OnAllocationExtension;

        /// <summary>
        ///     Gets the handles.
        /// </summary>
        /// <returns>
        ///     List of handles.
        /// </returns>
        public IEnumerable<MemoryHandle> GetHandles()
        {
            return _handleIndex.Select(kv => new MemoryHandle(kv.Item1, this));
        }

        /// <summary>
        ///     Gets the used.
        /// </summary>
        /// <returns>Get used Id.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int GetUsed()
        {
            var used = 0;
            for (var i = 0; i < EntryCount; i++)
                if (!_entries[i].IsStub)
                    used += _entries[i].Size;

            return used;
        }

        /// <summary>
        ///     Frees the space.
        /// </summary>
        /// <returns>The Free space</returns>
        public int FreeSpace()
        {
            return MemoryLaneUtils.CalculateFreeSpace(_entries, EntryCount, Capacity);
        }

        /// <summary>
        ///     Ensures the entry capacity.
        /// </summary>
        /// <param name="requiredSlotIndex">Index of the required slot.</param>
        private void EnsureEntryCapacity(int requiredSlotIndex)
        {
            var oldSize = _entries.Length;
            var newSize = MemoryLaneUtils.EnsureEntryCapacity(ref _entries, requiredSlotIndex);
            // Allocation Entries must be extended
            OnAllocationExtension?.Invoke(nameof(SlowLane), oldSize, newSize);
        }

        /// <summary>
        ///     Stubs the count.
        /// </summary>
        /// <returns>Returns count of stub entries</returns>
        public int StubCount()
        {
            return MemoryLaneUtils.StubCount(EntryCount, _entries);
        }

        // Estimate fragmentation percentage (gaps / total capacity)
        public int EstimateFragmentation()
        {
            return MemoryLaneUtils.EstimateFragmentation(_entries, EntryCount);
        }

        /// <summary>
        ///     Usages the percentage.
        /// </summary>
        /// <returns>Percentage of used memory.</returns>
        public double UsagePercentage()
        {
            return MemoryLaneUtils.UsagePercentage(EntryCount, _entries, Capacity);
        }

        /// <summary>
        ///     Debugs the visual map.
        /// </summary>
        /// <returns>Visual information about the Debug and Memory layout.</returns>
        public string DebugVisualMap()
        {
            return MemoryLaneUtils.DebugVisualMap(_entries, EntryCount, Capacity);
        }

        /// <summary>
        ///     Debugs the redirections.
        /// </summary>
        /// <returns>A overview of Redirections.</returns>
        public string DebugRedirections()
        {
            if (_entries == null) throw new InvalidOperationException("FastLane: Invalid memory.");

#if DEBUG
            // Pass the debug names dictionary in Debug mode
            return MemoryLaneUtils.DebugRedirections(_entries, EntryCount, _debugNames);
#else
    // Pass null in Release mode since the dictionary doesn't exist
    return MemoryLaneUtils.DebugRedirections(_entries, EntryCount, null);
#endif
        }

        /// <summary>
        ///     Dump all Debug Infos.
        /// </summary>
        public void LogDump()
        {
            Trace.WriteLine($"--- {GetType().Name} Dump Start ---");
            Trace.WriteLine(DebugDump());
            Trace.WriteLine(DebugVisualMap());
            Trace.WriteLine(DebugRedirections());
            Trace.WriteLine($"--- {GetType().Name} Dump End ---");
        }
    }
}