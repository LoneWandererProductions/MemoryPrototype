/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     MemoryArenaPrototype.Lane
 * FILE:        SlowLane.cs
 * PURPOSE:     Memory store for long lived data and stuff we could not hold into he slow lane.
 *              Ids for Allocations is always negative here.
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

// ReSharper disable EventNeverSubscribedTo.Global

#nullable enable
using ExtendedSystemObjects;
using MemoryManager.Core;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MemoryManager.Lanes
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
        ///     The threshold. Anything smaller than this goes to the BlobManager.
        ///     e.g., 256 bytes
        /// </summary>
        private readonly int _blobThreshold = 256;

        /// <summary>
        ///     The specialized manager for tiny/unpredictable allocations.
        /// </summary>
        private readonly BlobManager? _blobManager;

        /// <summary>
        /// Initializes a new instance of the <see cref="SlowLane" /> class.
        /// </summary>
        /// <param name="capacity">The capacity.</param>
        /// <param name="blobCapacityFraction">The BLOB capacity fraction.</param>
        /// <param name="blobThreshold">The BLOB threshold.</param>
        /// <param name="maxEntries">The maximum entries.</param>
        public SlowLane(int capacity, double blobCapacityFraction = 0.20, int blobThreshold = 256,
            int maxEntries = 1024)
        {
            Capacity = capacity;
            _blobThreshold = blobThreshold;

            Buffer = Marshal.AllocHGlobal(capacity);
            _entries = new AllocationEntry[maxEntries];

            // Use the fraction provided by the config/constructor
            var blobCapacity = (int)(capacity * blobCapacityFraction);

            _freeBlocks[0] = new FreeBlock { Offset = blobCapacity, Size = Capacity - blobCapacity };
            _freeBlockCount = 1;

            _blobManager = new BlobManager(Buffer, blobCapacity);
        }

        /// <summary>
        ///     Gets or sets the buffer.
        /// </summary>
        /// <value>
        ///     The buffer.
        /// </value>
        public nint Buffer { get; private set; }

        /// <summary>
        ///     Gets the capacity.
        /// </summary>
        /// <value>
        ///     The capacity.
        /// </value>
        public int Capacity { get; }


        /// <inheritdoc />
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

            _blobManager?.Compact();
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

            // --- ROUTE TO BLOB MANAGER FOR SMALL SIZES ---
            if (size <= _blobThreshold && _blobManager != null && _blobManager.CanAllocate(size))
            {
                return _blobManager.Allocate(size, priority, hints, debugName, currentFrame);
            }

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
        public bool CanAllocate(int size)
        {
            // --- ROUTE TO BLOB MANAGER FOR SMALL SIZES ---
            if (size <= _blobThreshold && _blobManager != null)
            {
                return _blobManager.CanAllocate(size);
            }

            // --- STANDARD BLOCK ALLOCATION LOGIC ---
            if (GetUsed() + size > Capacity * (1.0 - SafetyMargin))
                return false;

            for (var i = 0; i < _freeBlockCount; i++)
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
        public nint Resolve(MemoryHandle handle)
        {
            // Fast O(1) integer check! No dictionary lookup needed here.
            if (handle.Id <= BlobManager.StartingId && _blobManager != null)
            {
                return _blobManager.Resolve(handle);
            }

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
            // Fast O(1) integer check!
            if (handle.Id <= BlobManager.StartingId && _blobManager != null)
            {
                _blobManager.Free(handle);
                return;
            }

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
        /// <exception cref="InvalidOperationException">SlowLane: Invalid handle {handle.Id}</exception>
        public unsafe void FreeMany(MemoryHandle[] handles) // Or ReadOnlySpan<MemoryHandle>
        {
            var span = handles.AsSpan();
            var count = span.Length;

            // We'll collect the IDs and Indices in temporary buffers to batch-push
            // Using stackalloc for small-to-medium batches avoids GC pressure
            var ids = stackalloc int[count];
            var indices = stackalloc int[count];

            for (var i = 0; i < count; i++)
            {
                var id = span[i].Id;

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
        public bool HasHandle(MemoryHandle handle)
        {
            if (handle.Id <= BlobManager.StartingId && _blobManager != null)
            {
                return _blobManager.HasHandle(handle);
            }

            return MemoryLaneUtils.HasHandle(handle, _handleIndex);
        }

        /// <inheritdoc />
        public AllocationEntry GetEntry(MemoryHandle handle)
        {
            if (handle.Id <= BlobManager.StartingId && _blobManager != null)
            {
                return _blobManager.GetEntry(handle);
            }

            return MemoryLaneUtils.GetEntry(handle, _handleIndex, _entries, nameof(SlowLane));
        }

        /// <inheritdoc />
        public int GetAllocationSize(MemoryHandle handle)
        {
            if (handle.Id <= BlobManager.StartingId && _blobManager != null)
            {
                return _blobManager.GetAllocationSize(handle);
            }

            return MemoryLaneUtils.GetAllocationSize(handle, _handleIndex, _entries, nameof(SlowLane));
        }

        /// <inheritdoc />
        public string DebugDump()
        {
            return MemoryLaneUtils.DebugDump(_entries, EntryCount);
        }

        /// <inheritdoc />
        public event Action<string>? OnCompaction;

        /// <inheritdoc />
        public event Action<string, int, int>? OnAllocationExtension;

        /// <inheritdoc />
        public IEnumerable<MemoryHandle> GetHandles()
        {
            var mainHandles = _handleIndex.Select(kv => new MemoryHandle(kv.Item1, this));

            if (_blobManager != null)
            {
                return mainHandles.Concat(_blobManager.GetHandles());
            }

            return mainHandles;
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

        /// <inheritdoc />
        public int FreeSpace()
        {
            var mainFreeSpace = MemoryLaneUtils.CalculateFreeSpace(_entries, EntryCount, Capacity);

            var blobFreeSpace = _blobManager?.FreeSpace() ?? 0;

            return mainFreeSpace + blobFreeSpace;
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

        /// <inheritdoc />
        public int StubCount()
        {
            return MemoryLaneUtils.StubCount(EntryCount, _entries);
        }

        /// <inheritdoc />
        public int EstimateFragmentation()
        {
            return MemoryLaneUtils.EstimateFragmentation(_entries, EntryCount);
        }

        /// <inheritdoc />
        public double UsagePercentage()
        {
            return MemoryLaneUtils.UsagePercentage(EntryCount, _entries, Capacity);
        }

        /// <inheritdoc />
        public string DebugVisualMap()
        {
            return MemoryLaneUtils.DebugVisualMap(_entries, EntryCount, Capacity);
        }

        /// <inheritdoc />
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