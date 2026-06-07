/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     MemoryArenaPrototype.Lane
 * FILE:        FastLane.cs
 * PURPOSE:     Memory store for short lived data, smaller one preferable but that is up to the user.
 *              Ids for Allocations is always positive here.
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

// ReSharper disable MemberCanBePrivate.Global

#nullable enable
using ExtendedSystemObjects;
using MemoryManager.Core;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MemoryManager.Lanes
{
    /// <inheritdoc cref="IFastLane" />
    /// <summary>
    /// Fastlane Implementation
    /// </summary>
    /// <seealso cref="MemoryManager.Lanes.IFastLane" />
    /// <seealso cref="System.IDisposable" />
    public sealed class FastLane : IFastLane, IDisposable
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

        /// <summary>
        /// The free blocks
        /// </summary>
        private FreeBlock[] _freeBlocks = new FreeBlock[128];

        /// <summary>
        /// The versions
        /// </summary>
        private readonly uint[] _versions;

        /// <summary>
        /// The free block count
        /// </summary>
        private int _freeBlockCount;

        /// <summary>
        /// Initializes a new instance of the <see cref="FastLane" /> class.
        /// </summary>
        /// <param name="size">The size.</param>
        /// <param name="slowLane">The slow lane.</param>
        /// <param name="maxEntries">The maximum entries.</param>
        public FastLane(int size, SlowLane slowLane, int maxEntries = 1024)
        {
            _slowLane = slowLane;
            Capacity = size;
            Buffer = Marshal.AllocHGlobal(size);

            // PRE-ALLOCATE everything based on maxEntries
            _entries = new AllocationEntry[maxEntries];
            _freeBlocks = new FreeBlock[maxEntries]; // Free blocks can theoretically equal maxEntries
            _versions = new uint[maxEntries];

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

        /// <inheritdoc />
        public int EntryCount { get; private set; }


        /// <inheritdoc />
        public OneWayLane? OneWayLane { get; set; }

        /// <summary>
        ///     Gets the buffer.
        /// </summary>
        /// <value>
        ///     The buffer.
        /// </value>
        public nint Buffer { get; private set; }

        /// <inheritdoc />
        /// <summary>
        ///     Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            Marshal.FreeHGlobal(Buffer);
            _handleIndex.Clear();
            _entries = null;
        }

        /// <inheritdoc />
        public bool CanAllocate(int size)
        {
            // Fast, read-only check to see if a contiguous block exists
            for (var i = 0; i < _freeBlockCount; i++)
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

            //So we reuse freed handles here
            var id = MemoryLaneUtils.GetNextId(_freeIds, ref _nextHandleId);

            // --- ZOMBIE KILLER: Increment the version for this ID slot ---
            // This ensures that any old handles still pointing to this ID will be rejected.
            _versions[id % _versions.Length]++;
            var currentVersion = _versions[id % _versions.Length];

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
                Version = currentVersion,
                AllocationFrame = currentFrame,
                LastAccessFrame = currentFrame
            };

            _handleIndex[id] = EntryCount;
            EntryCount++;

            return new MemoryHandle(id, currentVersion, this);
        }

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
        public nint Resolve(MemoryHandle handle)
        {
            if (_entries == null) throw new InvalidOperationException("Corrupted");

            if (!_handleIndex.TryGetValue(handle.Id, out var index))
                throw new InvalidOperationException("FastLane: Invalid handle");

            ref readonly var entry = ref _entries[index];

            // 1. ZOMBIE CHECK (FastLane)
            // Is the handle the user is holding still valid for this FastLane slot?
            if (entry.Version != handle.Version)
                throw new AccessViolationException($"Zombie Handle: FastLane ID {handle.Id} is stale.");

            // 2. REDIRECTION (Stub Logic)
            if (entry.IsStub && entry.RedirectToId != 0)
            {
                // Reconstruct the handle using the EXACT version the SlowLane gave us
                var slowHandle = new MemoryHandle(entry.RedirectToId, entry.RedirectVersion, _slowLane);

                // This will now perform its OWN zombie check inside the SlowLane!
                return _slowLane.Resolve(slowHandle);
            }

            // 3. PHYSICAL RESOLVE
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

            if (entry.IsStub && entry.RedirectToId != 0)
            {
                var slowHandle = new MemoryHandle(entry.RedirectToId, entry.Version, _slowLane);
                _slowLane.Free(slowHandle);
                // Redirects.Remove(handle.Id); // We are going to delete this dictionary entirely in a second!
            }

            // 3. Swap-with-tail removal
            var lastIdx = --EntryCount; // Decrement first to get the last valid index
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


        /// <summary>
        /// Compacts the memory lane by consolidating free space and possibly rearranging allocations.
        /// Useful to reduce fragmentation and improve allocation efficiency.
        /// </summary>
        public void Compact()
        {
            // Fallback if called blindly from the interface without Janitor context
            Compact(0, new MemoryManagerConfig());
        }

        /// <inheritdoc />
        /// <summary>
        ///     Compacts this instance.
        /// </summary>
        public unsafe void Compact(int currentFrame, MemoryManagerConfig config)
        {
            if (_entries == null || EntryCount == 0) return;

            // --- PASS 1: THE JANITOR ---
            // Update the "Source of Truth" array directly.
            // We don't sort here; we just find cold data and kick it to the SlowLane.
            for (var i = 0; i < EntryCount; i++)
            {
                if (!_entries[i].IsStub && ShouldMoveToSlowLane(_entries[i], currentFrame, config))
                {
                    var h = new MemoryHandle(_entries[i].HandleId, _entries[i].Version, this);
                    OneWayLane?.MoveFromFastToSlow(h);
                    // MoveFromFastToSlow calls ReplaceWithStub, which updates _entries[i] safely.
                }
            }

            // --- PASS 2: THE SNOWPLOW (Physical Slide) ---
            var newBuffer = Marshal.AllocHGlobal(Capacity);
            var currentOffset = 0;

            // Now take your snapshot for sorting, but ONLY for survivors (non-stubs)
            var survivors = _entries.Take(EntryCount)
                .Where(e => !e.IsStub)
                .OrderBy(e => e.Offset)
                .ToList();

            foreach (var survivor in survivors)
            {
                // 1. Move the bytes
                void* source = (byte*)Buffer + survivor.Offset;
                void* target = (byte*)newBuffer + currentOffset;
                System.Buffer.MemoryCopy(source, target, Capacity - currentOffset, survivor.Size);

                // 2. Update the OFFSET ONLY in the real metadata
                var index = _handleIndex[survivor.HandleId];
                _entries[index].Offset = currentOffset;

                currentOffset += survivor.Size;
            }

            // --- PASS 3: CLEANUP ---
            Marshal.FreeHGlobal(Buffer);
            Buffer = newBuffer;
            _freeBlocks[0] = new FreeBlock { Offset = currentOffset, Size = Capacity - currentOffset };
            _freeBlockCount = 1;
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
        /// <returns>Return all Handles</returns>
        public IEnumerable<MemoryHandle> GetHandles()
        {
            if (_entries == null) yield break;

            // We iterate through every active ID in the map
            foreach (var id in _handleIndex.Keys)
            {
                // Find where this ID's metadata lives
                if (_handleIndex.TryGetValue(id, out var index))
                {
                    // Grab the version from the actual allocation entry
                    var version = _entries[index].Version;

                    // Return the "Smart Handle" with its generational proof-of-life
                    yield return new MemoryHandle(id, version, this);
                }
            }
        }

        /// <summary>
        ///     Replaces the with stub.
        /// </summary>
        /// <param name="fastHandle">The fast handle.</param>
        /// <param name="slowHandle">The slow handle.</param>
        /// <exception cref="InvalidOperationException">FastLane: Invalid handle</exception>
        public void ReplaceWithStub(MemoryHandle fastHandle, MemoryHandle slowHandle)
        {
            if (!_handleIndex.TryGetValue(fastHandle.Id, out var index))
                throw new InvalidOperationException("FastLane: Invalid handle");

            // Grab the existing entry
            var entry = _entries[index];

            entry.IsStub = true;
            entry.RedirectToId = slowHandle.Id;
            entry.RedirectVersion = slowHandle.Version;

            // Cleanup FastLane stats
            MemoryLaneUtils.ReturnFreeSpace(entry.Offset, entry.Size, ref _freeBlocks, ref _freeBlockCount);
            entry.Offset = 0;
            entry.Size = 0;

            _entries[index] = entry;
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
            OnAllocationExtension?.Invoke(nameof(FastLane), oldSize, newSize);
        }

        /// <summary>
        /// Frees the space.
        /// </summary>
        /// <returns>Returns total free bytes in FastLane</returns>
        /// <exception cref="InvalidOperationException">FastLane: Invalid memory.</exception>
        public int FreeSpace()
        {
            if (_entries == null) throw new InvalidOperationException("FastLane: Invalid memory.");

            var totalFree = 0;
            for (var i = 0; i < _freeBlockCount; i++)
            {
                totalFree += _freeBlocks[i].Size;
            }

            return totalFree;
        }

        /// <summary>
        /// Stubs the count.
        /// </summary>
        /// <returns>Returns count of stub entries</returns>
        /// <exception cref="InvalidOperationException">FastLane: Invalid memory.</exception>
        public int StubCount()
        {
            if (_entries == null) throw new InvalidOperationException("FastLane: Invalid memory.");

            // Convert the relevant segment of the array into a ReadOnlySpan
            ReadOnlySpan<AllocationEntry> span = _entries.AsSpan(0, EntryCount);

            return MemoryLaneUtils.StubCount(span);
        }

        /// <summary>
        /// Estimates the fragmentation.
        /// </summary>
        /// <returns>Estimate fragmentation percentage (gaps / total capacity)</returns>
        /// <exception cref="InvalidOperationException">FastLane: Invalid memory.</exception>
        public int EstimateFragmentation()
        {
            if (_entries == null) throw new InvalidOperationException("FastLane: Invalid memory.");

            // A simple metric: If there's more than 1 free block, we have fragmentation.
            // The higher the number of blocks relative to entry count, the worse it is.
            if (_freeBlockCount <= 1) return 0;

            // Simplistic percentage: (holes / total possible holes) * 100
            // Adjust this heuristic as you see fit for your analytics
            return (int)((double)_freeBlockCount / (EntryCount > 0 ? EntryCount : 1) * 100);
        }

        /// <summary>
        ///     Usages the percentage.
        /// </summary>
        /// <returns>Percentage of used memory.</returns>
        public double UsagePercentage()
        {
            if (_entries == null) throw new InvalidOperationException("FastLane: Invalid memory.");

            var free = FreeSpace();
            var used = Capacity - free;
            return (double)used / Capacity;
        }

        /// <summary>
        ///     Debugs the visual map.
        /// </summary>
        /// <returns>Visual information about the Debug and Memory layout.</returns>
        public string DebugVisualMap()
        {
            if (_entries == null) throw new InvalidOperationException("FastLane: Invalid memory.");

            return MemoryLaneUtils.DebugVisualMap(_entries, Capacity);
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

        /// <summary>
        /// Evaluates an allocation against current policies to see if it should be evicted.
        /// </summary>
        /// <param name="entry">The entry.</param>
        /// <param name="currentFrame">The current frame.</param>
        /// <param name="config">The configuration.</param>
        /// <returns>True if the entry should be moved to the slow lane; otherwise, false.</returns>
        private bool ShouldMoveToSlowLane(in AllocationEntry entry, int currentFrame, MemoryManagerConfig config)
        {
            // 1. EXPLICIT TELEMETRY:
            // Did the developer tag this as "Cold" or "LongLived" during allocation?
            if (entry.Hints.HasFlag(AllocationHints.Cold))
                return true;

            // 2. SIZE ANALYTICS:
            // Is this entry so large that it's clogging the FastLane?
            // Large objects cause more fragmentation during bump-allocations.
            if (entry.Size > config.FastLaneLargeEntryThreshold)
                return true;

            // 3. LIFETIME ANALYTICS (The "Janitor" Rule):
            // How many frames has this object survived?
            // If it has outstayed its welcome (e.g., > 600 frames), it's no longer "temporary."
            var age = currentFrame - entry.AllocationFrame;
            if (age > config.MaxFastLaneAgeFrames)
                return true;

            // 4. PRIORITY OVERRIDE:
            // Critical objects (like UI state or frame-buffers) stay in the FastLane
            // regardless of age, unless memory is extremely tight.
            if (entry.Priority == AllocationPriority.Critical)
                return false;

            return false;
        }
    }
}