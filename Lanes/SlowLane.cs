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
using System.Collections;
using System.Collections.Concurrent;
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
        /// <summary>
        ///     The safety margin
        /// </summary>
        private const double SafetyMargin = 0.10; // 10% free space reserved

        /// <summary>
        ///     The free ids
        /// </summary>
        private readonly IntList _freeIds = new(128);

        /// <summary>
        ///     The free slots, we reuse freed slots
        /// </summary>
        private readonly IntList _freeSlots = new(128);

        /// <summary>
        ///     The handle index
        /// </summary>
        private readonly ConcurrentDictionary<int, int> _handleIndex = new(); // handleId -> entries array index

        /// <summary>
        ///     The allocated entries
        /// </summary>
        private AllocationEntry[] _entries;

        /// <summary>
        ///     The next handle identifier
        /// </summary>
        private int _nextHandleId = -1;

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

        /// <summary>
        ///     Allocates the specified size.
        /// </summary>
        /// <param name="size">The size.</param>
        /// <param name="priority">The priority.</param>
        /// <param name="hints">The hints.</param>
        /// <param name="debugName">Name of the debug.</param>
        /// <param name="currentFrame">The current frame.</param>
        /// <returns>Allocated memory and a reference.</returns>
        /// <exception cref="OutOfMemoryException">SlowLane: Cannot allocate</exception>
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
            var offset = FindFreeSpot(size);
            var slotIndex = _freeSlots.Count > 0 ? _freeSlots.Pop() : EntryCount++;
            EnsureEntryCapacity(slotIndex);

            //So we reuse freed handles here
            var id = MemoryLaneUtils.GetNextId(_freeIds, ref _nextHandleId);

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
                DebugName = debugName,
                AllocationFrame = currentFrame,
                LastAccessFrame = currentFrame
            };

            _handleIndex[id] = slotIndex;

            return new MemoryHandle(id, this);
        }

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

            return FindFreeSpot(size) != -1;
        }

        /// <summary>
        ///     Resolves the specified handle.
        /// </summary>
        /// <param name="handle">The handle.</param>
        /// <returns>Pointer to the stored data.</returns>
        /// <exception cref="InvalidOperationException">
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

        /// <summary>
        ///     Frees the specified handle.
        /// </summary>
        /// <param name="handle">The handle.</param>
        /// <exception cref="InvalidOperationException">SlowLane: Invalid handle</exception>
        public void Free(MemoryHandle handle)
        {
            if (!_handleIndex.TryRemove(handle.Id, out var index))
                throw new InvalidOperationException($"SlowLane: Invalid handle {handle.Id}");

            _entries[index] = default;
            _freeSlots.Push(index);
            _freeIds.Push(handle.Id);

            EntryCount++;
        }

        /// <summary>
        /// Frees the many.
        /// </summary>
        /// <param name="handles">The handles.</param>
        /// <exception cref="System.InvalidOperationException">SlowLane: Invalid handle {handle.Id}</exception>
        public void FreeMany(IEnumerable<MemoryHandle> handles)
        {
            int freedCount = 0;

            foreach (var handle in handles)
            {
                if (!_handleIndex.TryRemove(handle.Id, out var index))
                    throw new InvalidOperationException($"SlowLane: Invalid handle {handle.Id}");

                _entries[index] = default;
                _freeSlots.Push(index);
                _freeIds.Push(handle.Id);

                freedCount++;
            }

            EntryCount += freedCount;
        }

        /// <summary>
        ///     Compacts this instance.
        /// </summary>
        public unsafe void Compact()
        {
            var newBuffer = Marshal.AllocHGlobal(Capacity);
            var offset = 0;

            // Compact only non-stub entries, moving them to front
            var writeIndex = 0;
            var newHandleIndex = new Dictionary<int, int>(_handleIndex.Count);

            for (var i = 0; i < EntryCount; i++)
            {
                var entry = _entries[i];
                if (entry.IsStub)
                    // Skip stubs (or handle as needed)
                    continue;

                System.Buffer.MemoryCopy((void*)(Buffer + entry.Offset), (void*)(newBuffer + offset), entry.Size,
                    entry.Size);

                entry.Offset = offset;
                offset += entry.Size;

                EnsureEntryCapacity(writeIndex);
                _entries[writeIndex] = entry;
                newHandleIndex[entry.HandleId] = writeIndex;

                writeIndex++;
            }

            // Clear remaining slots if any
            for (var i = writeIndex; i < EntryCount; i++)
                _entries[i] = default;

            // Update state
            Marshal.FreeHGlobal(Buffer);
            Buffer = newBuffer;

            _handleIndex.Clear();

            foreach (var (key, value) in newHandleIndex)
                _handleIndex[key] = value;

            EntryCount = writeIndex;

            // Reset free slots since after compact there are no holes
            _freeSlots.Clear();

            // Entries already sorted by offset due to compact process
            OnCompaction?.Invoke(nameof(SlowLane));
        }

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

        /// <summary>
        ///     Gets the entry.
        /// </summary>
        /// <param name="handle">The handle.</param>
        /// <returns>Get the Entry by handle.</returns>
        public AllocationEntry GetEntry(MemoryHandle handle)
        {
            return MemoryLaneUtils.GetEntry(handle, _handleIndex, _entries, nameof(SlowLane));
        }

        /// <summary>
        ///     Gets the size of the allocation.
        /// </summary>
        /// <param name="handle">The handle.</param>
        /// <returns>Size of allocated space.</returns>
        public int GetAllocationSize(MemoryHandle handle)
        {
            return MemoryLaneUtils.GetAllocationSize(handle, _handleIndex, _entries, nameof(SlowLane));
        }

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
            return _handleIndex.Select(kv => new MemoryHandle(kv.Key, this));
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
        ///     Finds the free spot.
        /// </summary>
        /// <param name="size">The size.</param>
        /// <returns>Returns total free bytes in FastLane</returns>
        private int FindFreeSpot(int size)
        {
            return MemoryLaneUtils.FindFreeSpot(size, _entries, EntryCount);
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
            return MemoryLaneUtils.DebugRedirections(_entries, EntryCount);
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