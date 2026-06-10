/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     MemoryArenaPrototype.Lane
 * FILE:        SlabLane.cs
 * PURPOSE:     An ultra-fast, look-free segregated Slab Allocator for uniform size bins.
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

// ReSharper disable MemberCanBePrivate.Global

using ExtendedSystemObjects;
using MemoryManager.Core;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MemoryManager.Lanes
{
    /// <inheritdoc cref="IFastLane" />
    /// <summary>
    /// SlabLane implementing Segregated Size Class Allocation Pools.
    /// </summary>
    public sealed class SlabLane : IFastLane, IDisposable
    {
#if DEBUG
        /// <summary>
        /// The debug names
        /// </summary>
        private readonly Dictionary<int, string> _debugNames = new();
#endif

        /// <summary>
        /// The free ids
        /// </summary>
        private readonly UnmanagedIntList _freeIds = new(128);

        /// <summary>
        /// The handle index
        /// </summary>
        private readonly UnmanagedMap<int> _handleIndex = new(7);

        /// <summary>
        /// The slow lane
        /// </summary>
        private readonly SlowLane _slowLane;

        /// <summary>
        /// The entries
        /// </summary>
        private AllocationEntry[]? _entries;

        /// <summary>
        /// The next handle identifier
        /// </summary>
        private int _nextHandleId = 1;

        /// <summary>
        /// The versions
        /// </summary>
        private unsafe uint* _versions;

        /// <summary>
        /// The versions capacity
        /// </summary>
        private int _versionsCapacity;

        /// <summary>
        /// The bins
        /// Segregated Slabs Infrastructure
        /// </summary>
        private readonly SlabBin[] _bins;

        /// <inheritdoc />
        public event Action<string>? OnCompaction;

        /// <inheritdoc />
        public event Action<string, int, int>? OnAllocationExtension;

        /// <summary>
        /// Initializes a new instance of the <see cref="SlabLane"/> class.
        /// </summary>
        public unsafe SlabLane(int size, SlowLane slowLane, int maxEntries = 1024, MemoryManagerConfig? config = null)
        {
            _slowLane = slowLane;
            Capacity = size;
            Buffer = Marshal.AllocHGlobal(size);

            _versionsCapacity = maxEntries + 1;
            _versions = (uint*)NativeMemory.AllocZeroed((nuint)_versionsCapacity, sizeof(uint));
            _entries = new AllocationEntry[maxEntries];

            // 1. DYNAMIC BIN GENERATION: Establish power-of-two size tracks up to threshold rules
            var maxThreshold = config?.Threshold ?? (1024 * 1024 / 4);
            var sizeClasses = new List<int>();
            var currentClass = 16;

            while (currentClass <= maxThreshold)
            {
                sizeClasses.Add(currentClass);
                currentClass *= 2;
            }

            _bins = new SlabBin[sizeClasses.Count];
            var bytesPerBin = size / _bins.Length;
            var currentOffset = 0;

            // 2. BUFFER SEGMENTATION: Sub-slice buffer evenly into isolated uniform partitions
            for (var i = 0; i < _bins.Length; i++)
            {
                var userSize = sizeClasses[i];
                var physicalSlotSize = MemoryCanary.GetPhysicalSize(userSize);
                var calculatedSlots = bytesPerBin / physicalSlotSize;

                _bins[i] = new SlabBin(userSize, physicalSlotSize, currentOffset, calculatedSlots);
                currentOffset += bytesPerBin;
            }
        }

        /// <summary>
        /// Gets the capacity.
        /// </summary>
        /// <value>
        /// The capacity.
        /// </value>
        public int Capacity { get; }

        /// <inheritdoc />
        public int EntryCount { get; private set; }

        /// <summary>
        /// Gets the native raw heap memory handle.
        /// </summary>
        /// <value>
        /// The buffer.
        /// </value>
        public nint Buffer { get; private set; }

        /// <inheritdoc />
        public OneWayLane? OneWayLane { get; set; }

        /// <inheritdoc />
        public bool CanAllocate(int size)
        {
            var binIndex = FindBinIndex(size);
            if (binIndex == -1) return false;
            return _bins[binIndex].FreeCount > 0;
        }

        /// <inheritdoc />
        public unsafe MemoryHandle Allocate(int size, AllocationPriority priority = AllocationPriority.Normal,
            AllocationHints hints = AllocationHints.None, string? debugName = null, int currentFrame = 0)
        {
            if (_entries == null) throw new InvalidOperationException("SlabLane: Memory not reserved");

            var binIndex = FindBinIndex(size);
            if (binIndex == -1 || _bins[binIndex].FreeCount == 0)
                throw new OutOfMemoryException(
                    $"SlabLane: Size bin exhausted for size {size} bytes. Requires eviction collection.");

            ref var bin = ref _bins[binIndex];
            var physicalOffset = bin.PopSlotOffset();

            // Write safety structures via localized masking coordinates
            var userOffset = MemoryCanary.GetUserOffset(physicalOffset);
            MemoryCanary.WriteGuardBands(Buffer, userOffset, size);

            EnsureEntryCapacity(EntryCount);
            var id = MemoryLaneUtils.GetNextId(_freeIds, ref _nextHandleId);

#if DEBUG
            if (!string.IsNullOrEmpty(debugName)) _debugNames[id] = debugName;
#endif
            if (id >= _versionsCapacity) GrowVersions(id + 1);

            var currentVersion = ++_versions[id];

            _entries[EntryCount] = new AllocationEntry
            {
                Offset = userOffset,
                Size = size, // Record original size requested to capture internal fragmentation analytics
                HandleId = id,
                Version = currentVersion,
                Priority = priority,
                Hints = hints,
                RedirectToId = 0,
                AllocationFrame = currentFrame,
                LastAccessFrame = currentFrame
            };

            _handleIndex[id] = EntryCount;
            EntryCount++;

            return new MemoryHandle(id, currentVersion, this);
        }

        /// <inheritdoc />
        public nint Resolve(MemoryHandle handle)
        {
            if (_entries == null) throw new InvalidOperationException("SlabLane: Memory is corrupted.");
            if (!_handleIndex.TryGetValue(handle.Id, out var index))
                throw new InvalidOperationException("SlabLane: Invalid handle");

            ref readonly var entry = ref _entries[index];

            if (entry.Version != handle.Version)
                throw new AccessViolationException($"Zombie Handle: SlabLane ID {handle.Id} is stale.");

            if (entry.IsStub && entry.RedirectToId != 0)
            {
                var slowHandle = new MemoryHandle(entry.RedirectToId, entry.RedirectVersion, _slowLane);
                return _slowLane.Resolve(slowHandle);
            }

            return Buffer + entry.Offset;
        }

        /// <inheritdoc />
        public void Free(MemoryHandle handle)
        {
            if (!_handleIndex.TryRemove(handle.Id, out var index))
                throw new InvalidOperationException($"SlabLane: Invalid handle {handle.Id}");

#if DEBUG
            _debugNames.Remove(handle.Id);
#endif
            var entry = _entries[index];

            if (!entry.IsStub)
            {
                MemoryCanary.Validate(Buffer, entry.Offset, entry.Size, handle.Id);

                // Return physical address back to its respective size bin stack instantly
                var physicalOffset = MemoryCanary.GetPhysicalOffset(entry.Offset);
                var binIndex = FindBinIndex(entry.Size);
                _bins[binIndex].PushSlotOffset(physicalOffset);
            }
            else if (entry.RedirectToId != 0)
            {
                var slowHandle = new MemoryHandle(entry.RedirectToId, entry.RedirectVersion, _slowLane);
                _slowLane.Free(slowHandle);
            }

            var lastIdx = --EntryCount;
            if (index != lastIdx)
            {
                var movedEntry = _entries[lastIdx];
                _entries[index] = movedEntry;
                _handleIndex[movedEntry.HandleId] = index;
            }

            _freeIds.Push(handle.Id);
        }

        /// <inheritdoc />
        public void Compact() => Compact(0, new MemoryManagerConfig());

        /// <inheritdoc />
        public void Compact(int currentFrame, MemoryManagerConfig? config)
        {
            if (_entries == null || EntryCount == 0) return;

            // Slabs never suffer from physical external gaps. 
            // Maintenance sweeps only evaluate aging evictions to free up tight buckets.
            for (var i = EntryCount - 1; i >= 0; i--)
            {
                var entry = _entries[i];
                if (!entry.IsStub && (entry.Hints.HasFlag(AllocationHints.Cold) ||
                                      currentFrame - entry.AllocationFrame > config.MaxFastLaneAgeFrames))
                {
                    var h = new MemoryHandle(entry.HandleId, entry.Version, this);
                    OneWayLane?.MoveFromFastToSlow(h);
                }
            }

            OnCompaction?.Invoke(nameof(SlabLane));
        }

        /// <inheritdoc />
        public void ReplaceWithStub(MemoryHandle fastHandle, MemoryHandle slowHandle)
        {
            if (_entries == null || !_handleIndex.TryGetValue(fastHandle.Id, out var index))
                throw new InvalidOperationException("SlabLane: Invalid handle");

            var entry = _entries[index];

            // Clean up the local physical slot registration within the parent bucket
            var physicalOffset = MemoryCanary.GetPhysicalOffset(entry.Offset);
            var binIndex = FindBinIndex(entry.Size);
            _bins[binIndex].PushSlotOffset(physicalOffset);

            entry.IsStub = true;
            entry.RedirectToId = slowHandle.Id;
            entry.RedirectVersion = slowHandle.Version;
            entry.Offset = 0;
            entry.Size = 0;

            _entries[index] = entry;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int FindBinIndex(int size)
        {
            for (var i = 0; i < _bins.Length; i++)
            {
                if (_bins[i].SizeClass >= size) return i;
            }

            return -1;
        }

        /// <inheritdoc />
        public int FreeSpace()
        {
            var freeSpace = 0;
            foreach (var bin in _bins) freeSpace += bin.FreeCount * bin.PhysicalSlotSize;
            return freeSpace;
        }

        /// <inheritdoc />
        public int EstimateFragmentation()
        {
            // Slab Allocators measure exact Internal Fragmentation (Slack Space inside uniform slots)
            if (EntryCount == 0) return 0;

            long totalPhysicalActiveBytes = 0;
            long totalUserRequestedBytes = 0;

            for (var i = 0; i < EntryCount; i++)
            {
                var entry = _entries![i];
                if (!entry.IsStub)
                {
                    totalUserRequestedBytes += entry.Size;
                    totalPhysicalActiveBytes += MemoryCanary.GetPhysicalSize(entry.Size);
                }
            }

            if (totalPhysicalActiveBytes == 0) return 0;
            return (int)((double)(totalPhysicalActiveBytes - totalUserRequestedBytes) / totalPhysicalActiveBytes * 100);
        }

        /// <inheritdoc />
        public double UsagePercentage() => (double)(Capacity - FreeSpace()) / Capacity;

        /// <inheritdoc />
        public int StubCount() => MemoryLaneUtils.StubCount(_entries.AsSpan(0, EntryCount));

        /// <inheritdoc />
        public AllocationEntry GetEntry(MemoryHandle handle) =>
            MemoryLaneUtils.GetEntry(handle, _handleIndex, _entries!, nameof(SlabLane));

        /// <inheritdoc />
        public int GetAllocationSize(MemoryHandle handle) =>
            MemoryLaneUtils.GetAllocationSize(handle, _handleIndex, _entries!, nameof(SlabLane));

        /// <inheritdoc />
        public bool HasHandle(MemoryHandle handle) => MemoryLaneUtils.HasHandle(handle, _handleIndex);

        /// <inheritdoc />
        public string DebugDump() => $"SlabLane Tiers: {_bins.Length} Active Bins, Tracked Items: {EntryCount}";

        /// <inheritdoc />
        public string DebugVisualMap() => MemoryLaneUtils.DebugVisualMap(_entries!, Capacity);

        /// <inheritdoc />
        public string DebugRedirections() => MemoryLaneUtils.DebugRedirections(_entries!, EntryCount, null);

        /// <inheritdoc />
        public IEnumerable<MemoryHandle> GetHandles()
        {
            if (_entries == null) yield break;
            foreach (var id in _handleIndex.Keys)
            {
                if (_handleIndex.TryGetValue(id, out var index))
                    yield return new MemoryHandle(id, _entries[index].Version, this);
            }
        }

        /// <summary>
        /// Ensures the entry capacity.
        /// </summary>
        /// <param name="requiredSlotIndex">Index of the required slot.</param>
        private void EnsureEntryCapacity(int requiredSlotIndex)
        {
            var oldSize = _entries!.Length;
            var newSize = MemoryLaneUtils.EnsureEntryCapacity(ref _entries, requiredSlotIndex);
            if (newSize > oldSize) OnAllocationExtension?.Invoke(nameof(SlabLane), oldSize, newSize);
        }

        /// <summary>
        /// Grows the versions.
        /// </summary>
        /// <param name="minCapacity">The minimum capacity.</param>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private unsafe void GrowVersions(int minCapacity)
        {
            var newCapacity = _versionsCapacity * 2;
            if (newCapacity < minCapacity) newCapacity = minCapacity;
            var newVersions = (uint*)NativeMemory.AllocZeroed((nuint)newCapacity, sizeof(uint));
            if (_versions != null)
            {
                Unsafe.CopyBlock(newVersions, _versions, (uint)(_versionsCapacity * sizeof(uint)));
                NativeMemory.Free(_versions);
            }

            _versions = newVersions;
            _versionsCapacity = newCapacity;
        }

        /// <inheritdoc />
        public unsafe void Dispose()
        {
            if (Buffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(Buffer);
                Buffer = IntPtr.Zero;
            }

            if (_versions != null)
            {
                NativeMemory.Free(_versions);
                _versions = null;
            }

            _handleIndex.Clear();
            _entries = null;
            GC.SuppressFinalize(this);
        }
    }
}