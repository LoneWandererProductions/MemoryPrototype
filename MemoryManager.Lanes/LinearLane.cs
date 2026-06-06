/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     MemoryArenaPrototype.Lane
 * FILE:        LinearLane.cs
 * PURPOSE:     An O(1) Bump Allocator for the FastLane. Absolute maximum speed, but relies on the Janitor to compact.
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

using ExtendedSystemObjects;
using MemoryManager.Core;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MemoryManager.Lanes
{
    /// <inheritdoc cref="IFastLane" />
    /// <summary>
    /// LinearLane  with Bump Allocator
    /// </summary>
    /// <seealso cref="MemoryManager.Lanes.IFastLane" />
    /// <seealso cref="System.IDisposable" />
    public sealed class LinearLane : IFastLane, IDisposable
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
        /// The next free offset
        /// </summary>
        private int _nextFreeOffset;

        /// <summary>
        /// The versions
        /// </summary>
        private readonly byte[] _versions;

        /// <summary>
        /// Initializes a new instance of the <see cref="LinearLane"/> class.
        /// </summary>
        /// <param name="size">The size.</param>
        /// <param name="slowLane">The slow lane.</param>
        /// <param name="maxEntries">The maximum entries.</param>
        public LinearLane(int size, SlowLane slowLane, int maxEntries = 1024)
        {
            _slowLane = slowLane;
            Capacity = size;
            Buffer = Marshal.AllocHGlobal(size);
            _versions = new byte[maxEntries];
            _entries = new AllocationEntry[maxEntries];
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
        /// Gets the buffer.
        /// </summary>
        /// <value>
        /// The buffer.
        /// </value>
        public nint Buffer { get; private set; }

        /// <inheritdoc />
        public OneWayLane? OneWayLane { get; set; }

        /// <summary>
        /// Gets the redirects.
        /// </summary>
        /// <value>
        /// The redirects.
        /// </value>
        private Dictionary<int, MemoryHandle> Redirects { get; } = new();

        /// <inheritdoc />
        public void Dispose()
        {
            Marshal.FreeHGlobal(Buffer);
            _handleIndex.Clear();
            Redirects.Clear();
            _entries = null;
        }

        /// <inheritdoc />
        public bool CanAllocate(int size) => _nextFreeOffset + size <= Capacity;

        /// <inheritdoc />
        public MemoryHandle Allocate(int size, AllocationPriority priority = AllocationPriority.Normal,
            AllocationHints hints = AllocationHints.None, string? debugName = null, int currentFrame = 0)
        {
            if (_entries == null) throw new InvalidOperationException("LinearLane: Memory not reserved");
            if (!CanAllocate(size))
                throw new OutOfMemoryException("LinearLane: Cannot allocate - Buffer is full. Requires Compaction.");

            var offset = _nextFreeOffset;
            _nextFreeOffset += size; // BUMP!

            EnsureEntryCapacity(EntryCount);
            var id = MemoryLaneUtils.GetNextId(_freeIds, ref _nextHandleId);

#if DEBUG
            if (!string.IsNullOrEmpty(debugName)) _debugNames[id] = debugName;
#endif
            _versions[id % _versions.Length]++;
            var currentVersion = _versions[id % _versions.Length];

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

            return new MemoryHandle(id, currentVersion, this);
        }

        /// <inheritdoc />
        public nint Resolve(MemoryHandle handle)
        {
            if (_entries == null) throw new InvalidOperationException("LinearLane: Memory is corrupted.");
            if (!_handleIndex.TryGetValue(handle.Id, out var index))
                throw new InvalidOperationException("LinearLane: Invalid handle");

            ref readonly var entry = ref _entries[index];

            if (!entry.IsStub || entry.RedirectToId == 0) return Buffer + entry.Offset;

            var slowHandle = new MemoryHandle(entry.RedirectToId, entry.RedirectVersion, _slowLane);
            return _slowLane.Resolve(slowHandle);
        }

        /// <inheritdoc />
        public void Free(MemoryHandle handle)
        {
            if (!_handleIndex.TryRemove(handle.Id, out var index))
                throw new InvalidOperationException($"LinearLane: Invalid handle {handle.Id}");

#if DEBUG
            _debugNames.Remove(handle.Id);
#endif

            var entry = _entries[index];
            if (entry.IsStub && entry.RedirectToId != 0)
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
        public unsafe void Compact(int currentFrame, MemoryManagerConfig config)
        {
            if (_entries == null || EntryCount == 0) return;

            // PASS 1: Evict old data to SlowLane (This creates stubs)
            for (var i = EntryCount - 1; i >= 0; i--)
            {
                var entry = _entries[i];
                if (!entry.IsStub && ShouldMoveToSlowLane(entry, currentFrame, config.MaxFastLaneAgeFrames,
                        config.FastLaneLargeEntryThreshold))
                {
                    var h = new MemoryHandle(entry.HandleId, entry.Version, this);
                    OneWayLane?.MoveFromFastToSlow(h);
                }
            }

            // PASS 2: Physical Slide
            var newBuffer = Marshal.AllocHGlobal(Capacity);
            var offset = 0;

            var sortedEntries = new AllocationEntry[EntryCount];
            Array.Copy(_entries, sortedEntries, EntryCount);
            Array.Sort(sortedEntries, (a, b) => a.Offset.CompareTo(b.Offset));

            for (var i = 0; i < EntryCount; i++)
            {
                var entry = sortedEntries[i];
                if (!entry.IsStub)
                {
                    void* source = (byte*)Buffer + entry.Offset;
                    void* target = (byte*)newBuffer + offset;
                    System.Buffer.MemoryCopy(source, target, Capacity - offset, entry.Size);

                    entry.Offset = offset;
                    offset += entry.Size;
                }

                // Sync the new offset back to the main metadata
                var originalIndex = _handleIndex[entry.HandleId];
                _entries[originalIndex] = entry;
            }

            Marshal.FreeHGlobal(Buffer);
            Buffer = newBuffer;
            _nextFreeOffset = offset; // Reset bump pointer to the end of survivors
        }

        /// <inheritdoc />
        public void ReplaceWithStub(MemoryHandle fastHandle, MemoryHandle slowHandle)
        {
            if (_entries == null || !_handleIndex.TryGetValue(fastHandle.Id, out var index))
                throw new InvalidOperationException("LinearLane: Invalid handle");

            var entry = _entries[index];
            entry.IsStub = true;
            entry.RedirectToId = slowHandle.Id;
            entry.Offset = 0;
            entry.Size = 0;

            _entries[index] = entry;
            Redirects[fastHandle.Id] = slowHandle;
        }

        /// <summary>
        /// Should move to slow lane.
        /// </summary>
        /// <param name="entry">The entry.</param>
        /// <param name="currentFrame">The current frame.</param>
        /// <param name="maxAgeFrames">The maximum age frames.</param>
        /// <param name="largeThreshold">The large threshold.</param>
        /// <returns></returns>
        private bool ShouldMoveToSlowLane(in AllocationEntry entry, int currentFrame, int maxAgeFrames,
            int largeThreshold)
        {
            if (entry.Hints.HasFlag(AllocationHints.Cold)) return true;
            if (entry.Size > largeThreshold) return true;
            if (currentFrame - entry.AllocationFrame > maxAgeFrames) return true;

            return false;
        }

        /// <summary>
        /// Ensures the entry capacity.
        /// </summary>
        /// <param name="requiredSlotIndex">Index of the required slot.</param>
        private void EnsureEntryCapacity(int requiredSlotIndex)
        {
            var oldSize = _entries!.Length;
            var newSize = MemoryLaneUtils.EnsureEntryCapacity(ref _entries, requiredSlotIndex);
            OnAllocationExtension?.Invoke(nameof(LinearLane), oldSize, newSize);
        }

        /// <inheritdoc />
        public int FreeSpace() => Capacity - _nextFreeOffset;

        /// <inheritdoc />
        public int StubCount()
        {
            ReadOnlySpan<AllocationEntry> span = _entries.AsSpan(0, EntryCount);
            return MemoryLaneUtils.StubCount(span);
        }

        /// <inheritdoc />
        public int EstimateFragmentation()
        {
            var allocatedBytes = _nextFreeOffset;
            var usedBytes = 0;
            for (var i = 0; i < EntryCount; i++)
                if (!_entries![i].IsStub)
                    usedBytes += _entries[i].Size;

            if (allocatedBytes == 0) return 0;

            return (int)((double)(allocatedBytes - usedBytes) / allocatedBytes * 100);
        }

        /// <summary>
        /// Usages the percentage.
        /// </summary>
        /// <returns>
        /// Used memory Percentage
        /// </returns>
        public double UsagePercentage() => (double)(Capacity - FreeSpace()) / Capacity;

        /// <inheritdoc />
        /// <summary>
        /// Retrieves the full allocation entry metadata for a given handle.
        /// Passthroughs to MemoryLaneUtils
        /// </summary>
        /// <param name="handle">The handle identifying the allocation.</param>
        /// <returns>
        /// The allocation entry associated with the handle.
        /// </returns>
        public AllocationEntry GetEntry(MemoryHandle handle) =>
            MemoryLaneUtils.GetEntry(handle, _handleIndex, _entries!, nameof(LinearLane));

        /// <inheritdoc />
        public int GetAllocationSize(MemoryHandle handle) =>
            MemoryLaneUtils.GetAllocationSize(handle, _handleIndex, _entries!, nameof(LinearLane));

        /// <inheritdoc />
        public bool HasHandle(MemoryHandle handle) => MemoryLaneUtils.HasHandle(handle, _handleIndex);

        /// <inheritdoc />
        public string DebugDump() => MemoryLaneUtils.DebugDump(_entries!, EntryCount);

        /// <inheritdoc />
        public string DebugVisualMap() => MemoryLaneUtils.DebugVisualMap(_entries!, Capacity);

        /// <inheritdoc />
        public string DebugRedirections() => MemoryLaneUtils.DebugRedirections(_entries!, EntryCount, null);

        /// <inheritdoc />
        public IEnumerable<MemoryHandle> GetHandles()
        {
            if (_entries == null) yield break;

            // We must iterate the keys and look up the version in the metadata
            foreach (var id in _handleIndex.Keys)
            {
                if (_handleIndex.TryGetValue(id, out var index))
                {
                    // Grab the generation from the actual entry
                    var version = _entries[index].Version;

                    // Return the "Smart Handle" with its proof-of-life
                    yield return new MemoryHandle(id, version, this);
                }
            }
        }

        /// <inheritdoc />
        public event Action<string>? OnCompaction;

        /// <inheritdoc />
        public event Action<string, int, int>? OnAllocationExtension;

        /// <summary>
        /// Logs the dump.
        /// </summary>
        public void LogDump() => Trace.WriteLine(DebugDump());
    }
}