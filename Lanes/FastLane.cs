/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     Lanes
 * FILE:        FastLane.cs
 * PURPOSE:     Memory store for short lived data, smaller one preferable but that is up to the user.
 *              Ids for Allocations is always positive here.
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

// ReSharper disable MemberCanBePrivate.Global

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
    public sealed class FastLane : IMemoryLane, IDisposable
    {

#if DEBUG
        /// <summary>
        /// The debug names
        /// </summary>
        private readonly Dictionary<int, string> _debugNames = new();
#endif
        /// <summary>
        ///     The free ids
        /// </summary>
        private readonly UnmanagedIntList _freeIds = new(128);

        /// <summary>
        ///     The handle index
        ///     Maps handleId → index into _entries
        /// </summary>
        private readonly UnmanagedMap<int> _handleIndex = new(7);

        /// <summary>
        ///     The slow lane
        /// </summary>
        private readonly SlowLane _slowLane;

        /// <summary>
        ///     The entries
        /// </summary>
        private AllocationEntry[]? _entries = new AllocationEntry[128];

        /// <summary>
        ///     The next handle identifier
        /// </summary>
        private int _nextHandleId = 1;

        private FreeBlock[] _freeBlocks = new FreeBlock[128];

        private int _freeBlockCount = 0;

        //_blockManager = new BlockMemoryManager(Buffer, Capacity, blockSize);

        /// <summary>
        ///     Initializes a new instance of the <see cref="FastLane" /> class.
        /// </summary>
        /// <param name="size">The size.</param>
        /// <param name="slowLane">The slow lane.</param>
        public FastLane(int size, SlowLane slowLane, int maxEntries = 1024)
        {
            _slowLane = slowLane;
            Capacity = size;
            Buffer = Marshal.AllocHGlobal(size);

            // PRE-ALLOCATE everything based on maxEntries
            _entries = new AllocationEntry[maxEntries];
            _freeBlocks = new FreeBlock[maxEntries]; // Free blocks can theoretically equal maxEntries

            // INITIALIZATION: The entire lane starts as one giant free block
            _freeBlocks[0] = new FreeBlock { Offset = 0, Size = Capacity };
            _freeBlockCount = 1;
        }

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
        ///     Only contains handles that were replaced with stubs
        /// </summary>
        private Dictionary<int, MemoryHandle> Redirects { get; } = new();

        /// <summary>
        ///     Gets or sets the one way lane.
        /// </summary>
        /// <value>
        ///     The one way lane.
        /// </value>
        public OneWayLane? OneWayLane { get; set; }

        /// <summary>
        ///     Gets the buffer.
        /// </summary>
        /// <value>
        ///     The buffer.
        /// </value>
        public IntPtr Buffer { get; private set; }

        /// <inheritdoc />
        /// <summary>
        ///     Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            Marshal.FreeHGlobal(Buffer);
            _handleIndex.Clear();
            Redirects.Clear();
            _entries = null;
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
        ///     Allocates the specified size.
        /// </summary>
        /// <param name="size">The size.</param>
        /// <param name="priority">The priority.</param>
        /// <param name="hints">The hints.</param>
        /// <param name="debugName">Name of the debug.</param>
        /// <param name="currentFrame">The current frame.</param>
        /// <returns>Handler to the reserved memory.</returns>
        /// <exception cref="T:System.InvalidOperationException">FastLane: Memory not reserved</exception>
        /// <exception cref="T:System.OutOfMemoryException">FastLane: Not enough memory</exception>
        public MemoryHandle Allocate(
            int size,
            AllocationPriority priority = AllocationPriority.Normal,
            AllocationHints hints = AllocationHints.None,
            string? debugName = null,
            int currentFrame = 0)
        {
            if (_entries == null) throw new InvalidOperationException("FastLane: Memory not reserved");

            var offset = MemoryLaneUtils.FindFreeSpot(size, ref _freeBlocks, ref _freeBlockCount);

            if (offset == -1)
                throw new OutOfMemoryException("SlowLane: Cannot allocate - No contiguous block large enough.");

            EnsureEntryCapacity(EntryCount);
            //if (EntryCount >= _entries.Length)
            //    Array.Resize(ref _entries, _entries.Length * 2);

            //So we reuse freed handles here
            var id = MemoryLaneUtils.GetNextId(_freeIds, ref _nextHandleId);

#if DEBUG
            if (!string.IsNullOrEmpty(debugName))
            {
                _debugNames[id] = debugName;
            }
#endif

            _entries[EntryCount] = new AllocationEntry
            {
                Offset = offset,
                Size = size,
                HandleId = id,
                Priority = priority,
                Hints = hints,
                RedirectToId = 0,
                AllocationFrame = currentFrame,
                LastAccessFrame = currentFrame
            };

            _handleIndex[id] = EntryCount;
            EntryCount++;

            return new MemoryHandle(id, this);
        }

        //public MemoryHandle Allocate(
        //    int size,
        //    AllocationPriority priority = AllocationPriority.Normal,
        //    AllocationHints hints = AllocationHints.None,
        //    string? debugName = null,
        //    int currentFrame = 0)
        //{
        //    if (_entries == null)
        //        throw new InvalidOperationException("FastLane: Memory not reserved");

        //    int blocksNeeded = (size + _blockManager.BlockSize - 1) / _blockManager.BlockSize;

        //    if (!_blockManager.TryAllocateContiguous(blocksNeeded, out int startBlock))
        //        throw new OutOfMemoryException("FastLane: Not enough memory");

        //    EnsureEntryCapacity(EntryCount);

        //    var id = MemoryLaneUtils.GetNextId(_freeIds, ref _nextHandleId);
        //    int offset = startBlock * _blockManager.BlockSize;

        //    _entries[EntryCount] = new AllocationEntry
        //    {
        //        Offset = offset,
        //        Size = blocksNeeded * _blockManager.BlockSize,
        //        HandleId = id,
        //        Priority = priority,
        //        Hints = hints,
        //        DebugName = debugName,
        //        AllocationFrame = currentFrame,
        //        LastAccessFrame = currentFrame
        //    };

        //    _handleIndex[id] = EntryCount;
        //    EntryCount++;

        //    return new MemoryHandle(id, this);
        //}



        /// <inheritdoc />
        /// <summary>
        ///     Resolves the specified handle.
        /// </summary>
        /// <param name="handle">The handle.</param>
        /// <returns>Pointer to the Memory.</returns>
        /// <exception cref="T:System.InvalidOperationException">
        ///     FastLane: Memory is corrupted.
        ///     or
        ///     FastLane: Invalid handle
        /// </exception>
        public IntPtr Resolve(MemoryHandle handle)
        {
            if (_entries == null)
                throw new InvalidOperationException("FastLane: Memory is corrupted.");

            // First try redirect dictionary
            // Redirection handles are stored separately for quick access.
            if (Redirects.TryGetValue(handle.Id, out var redirectHandle))
                return _slowLane.Resolve(redirectHandle);

            if (!_handleIndex.TryGetValue(handle.Id, out var index))
                throw new InvalidOperationException("FastLane: Invalid handle");

            ref readonly var entry = ref _entries[index];

            return Buffer + entry.Offset;
        }

        /// <inheritdoc />
        /// <summary>
        ///     Frees the specified handle.
        /// </summary>
        /// <param name="handle">The handle.</param>
        /// <exception cref="T:System.InvalidOperationException">
        ///     FastLane: Memory is corrupted.
        ///     or
        ///     FastLane: Invalid handle
        /// </exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Free(MemoryHandle handle)
        {
            // 1. Remove from index first
            if (!_handleIndex.TryRemove(handle.Id, out var index))
                throw new InvalidOperationException($"SlowLane: Invalid handle {handle.Id}");

#if DEBUG
            _debugNames.Remove(handle.Id);
#endif

            // 2. Handle SlowLane/Redirects (This is your cold path)
            var entry = _entries[index];
            MemoryLaneUtils.ReturnFreeSpace(entry.Offset, entry.Size, ref _freeBlocks, ref _freeBlockCount);

            if (entry.IsStub && entry.RedirectTo.HasValue)
            {
                _slowLane.Free(entry.RedirectTo.Value);
                Redirects.Remove(handle.Id);
            }

            // 3. Swap-with-tail removal
            int lastIdx = --EntryCount; // Decrement first to get the last valid index
            if (index != lastIdx)
            {
                // Move the last entry into the hole
                var movedEntry = _entries[lastIdx];
                _entries[index] = movedEntry;

                // Update the map to point to the new location
                // OPTIMIZATION: Use a direct 'Set' or 'Update' if your map allows 
                // to avoid tombstone buildup during updates.
                _handleIndex[movedEntry.HandleId] = index;
            }

            // 4. Return ID to pool
            _freeIds.Push(handle.Id);
        }

        /// <inheritdoc />
        /// <summary>
        ///     Compacts this instance.
        /// </summary>
        public unsafe void Compact()
        {
            if (_entries == null || EntryCount == 0) return;

            var newBuffer = Marshal.AllocHGlobal(Capacity);
            var offset = 0;

            // FIX: We must process the entries in order of their memory Offset!
            // Otherwise, Swap-With-Tail removals will cause memory to be copied out of order,
            // leading to overlapping writes and test failures.

            // 1. Create a sorted copy of the active entries
            var sortedEntries = new AllocationEntry[EntryCount];
            Array.Copy(_entries, sortedEntries, EntryCount);
            Array.Sort(sortedEntries, (a, b) => a.Offset.CompareTo(b.Offset));

            for (var i = 0; i < EntryCount; i++)
            {
                var entry = sortedEntries[i]; // Use the sorted entry!

                if (!entry.IsStub)
                {
                    if (ShouldMoveToSlowLane(entry))
                    {
                        var fastHandle = new MemoryHandle(entry.HandleId, this);
                        if (OneWayLane?.MoveFromFastToSlow(fastHandle) == true)
                        {
                            // Update our sorted entry with the stub info
                            // Since it's a struct, we have to re-fetch it from _entries to get the changes 
                            // that OneWayLane.MoveFromFastToSlow just applied!
                            var actualIndex = _handleIndex[entry.HandleId];
                            entry = _entries[actualIndex];

                            _entries[actualIndex] = entry; // Put it back
                            continue;
                        }
                    }

                    void* source = (byte*)Buffer + entry.Offset;
                    void* target = (byte*)newBuffer + offset;
                    System.Buffer.MemoryCopy(source, target, Capacity - offset, entry.Size);
                    entry.Offset = offset;
                    offset += entry.Size;
                }

                // 2. Put the updated entry back into the MAIN _entries array
                var originalIndex = _handleIndex[entry.HandleId];
                _entries[originalIndex] = entry;
            }

            Marshal.FreeHGlobal(Buffer);
            Buffer = newBuffer;

            _freeBlocks[0] = new FreeBlock
            {
                Offset = offset,
                Size = Capacity - offset
            };
            _freeBlockCount = 1;

            OnCompaction?.Invoke(nameof(FastLane));
        }

        /// <inheritdoc />
        /// <summary>
        ///     Retrieves the full allocation entry metadata for a given handle.
        /// </summary>
        /// <param name="handle">The handle identifying the allocation.</param>
        /// <returns>
        ///     The allocation entry associated with the handle.
        /// </returns>
        public AllocationEntry GetEntry(MemoryHandle handle)
        {
            return MemoryLaneUtils.GetEntry(handle, _handleIndex, _entries, nameof(FastLane));
        }

        /// <inheritdoc />
        /// <summary>
        ///     Gets the size of the allocation.
        /// </summary>
        /// <param name="handle">The handle.</param>
        /// <returns>Returns the used space of Allocation.</returns>
        public int GetAllocationSize(MemoryHandle handle)
        {
            return MemoryLaneUtils.GetAllocationSize(handle, _handleIndex, _entries, nameof(FastLane));
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
        /// <returns></returns>
        public IEnumerable<MemoryHandle> GetHandles()
        {
            return _handleIndex.Keys.Select(id => new MemoryHandle(id, this));
        }

        /// <summary>
        ///     Replaces the with stub.
        /// </summary>
        /// <param name="fastHandle">The fast handle.</param>
        /// <param name="slowHandle">The slow handle.</param>
        /// <exception cref="System.InvalidOperationException">FastLane: Invalid handle</exception>
        public void ReplaceWithStub(MemoryHandle fastHandle, MemoryHandle slowHandle)
        {
            if (_entries == null) throw new InvalidOperationException("FastLane: Invalid handle");

            if (!_handleIndex.TryGetValue(fastHandle.Id, out var index))
                throw new InvalidOperationException("FastLane: Invalid handle");

            var entry = _entries[index];
            entry.IsStub = true;

            // Fix: Save the integer ID, not the struct!
            entry.RedirectToId = slowHandle.Id;

            // Fix: We also agreed to zero out the ghost memory stats here!
            // This is vital so the FastLane doesn't think this stub is consuming space.
            MemoryLaneUtils.ReturnFreeSpace(entry.Offset, entry.Size, ref _freeBlocks, ref _freeBlockCount);
            entry.Offset = 0;
            entry.Size = 0;

            _entries[index] = entry;

            Redirects[fastHandle.Id] = slowHandle;
        }

        /// <summary>
        ///     Should the move to slow lane.
        /// </summary>
        /// <param name="entry">The entry.</param>
        /// <returns>Status if a move is necessary.</returns>
        private static bool ShouldMoveToSlowLane(AllocationEntry entry)
        {
            // Basic example: offload low-priority entries
            return entry.Priority == AllocationPriority.Low;
        }

        /// <summary>
        ///     Ensures the entry capacity.
        /// </summary>
        /// <param name="requiredSlotIndex">Index of the required slot.</param>
        private void EnsureEntryCapacity(int requiredSlotIndex)
        {
            var oldSize = _entries.Length;
            var newSize = MemoryLaneUtils.EnsureEntryCapacity(ref _entries, requiredSlotIndex);
            // Allocation Entriesmust be extended
            OnAllocationExtension?.Invoke(nameof(SlowLane), oldSize, newSize);
        }

        /// <summary>
        /// Frees the space.
        /// </summary>
        /// <returns>Returns total free bytes in FastLane</returns>
        /// <exception cref="System.InvalidOperationException">FastLane: Invalid memory.</exception>
        public int FreeSpace()
        {
            if (_entries == null) throw new InvalidOperationException("FastLane: Invalid memory.");

            int totalFree = 0;
            for (int i = 0; i < _freeBlockCount; i++)
            {
                totalFree += _freeBlocks[i].Size;
            }
            return totalFree;
        }

        /// <summary>
        /// Stubs the count.
        /// </summary>
        /// <returns>Returns count of stub entries</returns>
        /// <exception cref="System.InvalidOperationException">FastLane: Invalid memory.</exception>
        public int StubCount()
        {
            if (_entries == null) throw new InvalidOperationException("FastLane: Invalid memory.");

            return MemoryLaneUtils.StubCount(EntryCount, _entries);
        }

        /// <summary>
        /// Estimates the fragmentation.
        /// </summary>
        /// <returns>Estimate fragmentation percentage (gaps / total capacity)</returns>
        /// <exception cref="System.InvalidOperationException">FastLane: Invalid memory.</exception>
        public int EstimateFragmentation()
        {
            if (_entries == null) throw new InvalidOperationException("FastLane: Invalid memory.");

            // A simple metric: If there's more than 1 free block, we have fragmentation.
            // The higher the number of blocks relative to entry count, the worse it is.
            if (_freeBlockCount <= 1) return 0;

            // Simplistic percentage: (holes / total possible holes) * 100
            // Adjust this heuristic as you see fit for your analytics
            return (int)(((double)_freeBlockCount / (EntryCount > 0 ? EntryCount : 1)) * 100);
        }

        /// <summary>
        ///     Usages the percentage.
        /// </summary>
        /// <returns>Percentage of used memory.</returns>
        public double UsagePercentage()
        {
            if (_entries == null) throw new InvalidOperationException("FastLane: Invalid memory.");

            int free = FreeSpace();
            int used = Capacity - free;
            return (double)used / Capacity;
        }

        /// <summary>
        ///     Debugs the visual map.
        /// </summary>
        /// <returns>Visual information about the Debug and Memory layout.</returns>
        public string DebugVisualMap()
        {
            if (_entries == null) throw new InvalidOperationException("FastLane: Invalid memory.");

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