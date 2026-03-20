/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     Lanes
 * FILE:        LinearLane.cs
 * PURPOSE:     An O(1) Bump Allocator for the FastLane. Absolute maximum speed, but relies on the Janitor to compact.
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

#nullable enable
using Core;
using Core.MemoryArenaPrototype.Core;
using ExtendedSystemObjects;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

namespace Lanes
{
    public sealed class LinearLane : IFastLane, IDisposable
    {
#if DEBUG
        private readonly Dictionary<int, string> _debugNames = new();
#endif
        private readonly UnmanagedIntList _freeIds = new(128);
        private readonly UnmanagedMap<int> _handleIndex = new(7);
        private readonly SlowLane _slowLane;

        private AllocationEntry[]? _entries;
        private int _nextHandleId = 1;

        // --- THE BUMP POINTER ---
        private int _nextFreeOffset = 0;

        public LinearLane(int size, SlowLane slowLane, int maxEntries = 1024)
        {
            _slowLane = slowLane;
            Capacity = size;
            Buffer = Marshal.AllocHGlobal(size);
            _entries = new AllocationEntry[maxEntries];
        }

        public int Capacity { get; }
        public int EntryCount { get; private set; }
        public IntPtr Buffer { get; private set; }
        public OneWayLane? OneWayLane { get; set; }

        private Dictionary<int, MemoryHandle> Redirects { get; } = new();

        public void Dispose()
        {
            Marshal.FreeHGlobal(Buffer);
            _handleIndex.Clear();
            Redirects.Clear();
            _entries = null;
        }

        // O(1) Allocation Check
        public bool CanAllocate(int size) => _nextFreeOffset + size <= Capacity;

        // O(1) Bump Allocation
        public MemoryHandle Allocate(int size, AllocationPriority priority = AllocationPriority.Normal, AllocationHints hints = AllocationHints.None, string? debugName = null, int currentFrame = 0)
        {
            if (_entries == null) throw new InvalidOperationException("LinearLane: Memory not reserved");
            if (!CanAllocate(size)) throw new OutOfMemoryException("LinearLane: Cannot allocate - Buffer is full. Requires Compaction.");

            var offset = _nextFreeOffset;
            _nextFreeOffset += size; // BUMP!

            EnsureEntryCapacity(EntryCount);
            var id = MemoryLaneUtils.GetNextId(_freeIds, ref _nextHandleId);

#if DEBUG
            if (!string.IsNullOrEmpty(debugName)) _debugNames[id] = debugName;
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

        public IntPtr Resolve(MemoryHandle handle)
        {
            if (_entries == null) throw new InvalidOperationException("LinearLane: Memory is corrupted.");
            if (!_handleIndex.TryGetValue(handle.Id, out var index)) throw new InvalidOperationException("LinearLane: Invalid handle");

            ref readonly var entry = ref _entries[index];

            if (entry.IsStub && entry.RedirectToId != 0)
            {
                var slowHandle = new MemoryHandle(entry.RedirectToId, _slowLane);
                return _slowLane.Resolve(slowHandle);
            }

            return Buffer + entry.Offset;
        }

        // O(1) Free. We don't care about the hole it leaves behind!
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
                var slowHandle = new MemoryHandle(entry.RedirectToId, _slowLane);
                _slowLane.Free(slowHandle);
            }

            int lastIdx = --EntryCount;
            if (index != lastIdx)
            {
                var movedEntry = _entries[lastIdx];
                _entries[index] = movedEntry;
                _handleIndex[movedEntry.HandleId] = index;
            }

            _freeIds.Push(handle.Id);
        }

        public void Compact() => Compact(0, new MemoryManagerConfig());

        public unsafe void Compact(int currentFrame, MemoryManagerConfig config)
        {
            if (_entries == null || EntryCount == 0) return;

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
                    if (ShouldMoveToSlowLane(entry, currentFrame, config.MaxFastLaneAgeFrames, config.FastLaneLargeEntryThreshold))
                    {
                        var fastHandle = new MemoryHandle(entry.HandleId, this);
                        if (OneWayLane?.MoveFromFastToSlow(fastHandle) == true)
                        {
                            var actualIndex = _handleIndex[entry.HandleId];
                            entry = _entries[actualIndex];
                            _entries[actualIndex] = entry;
                            continue;
                        }
                    }

                    // Sliding Compaction
                    void* source = (byte*)Buffer + entry.Offset;
                    void* target = (byte*)newBuffer + offset;
                    System.Buffer.MemoryCopy(source, target, Capacity - offset, entry.Size);

                    entry.Offset = offset;
                    offset += entry.Size;
                }

                var originalIndex = _handleIndex[entry.HandleId];
                _entries[originalIndex] = entry;
            }

            Marshal.FreeHGlobal(Buffer);
            Buffer = newBuffer;

            // RESET THE BUMP POINTER TO THE END OF THE SURVIVORS
            _nextFreeOffset = offset;

            OnCompaction?.Invoke(nameof(LinearLane));
        }

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

        private bool ShouldMoveToSlowLane(in AllocationEntry entry, int currentFrame, int maxAgeFrames, int largeThreshold)
        {
            if (entry.Hints.HasFlag(AllocationHints.Cold)) return true;
            if (entry.Size > largeThreshold) return true;
            if (currentFrame - entry.AllocationFrame > maxAgeFrames) return true;
            return false;
        }

        private void EnsureEntryCapacity(int requiredSlotIndex)
        {
            var oldSize = _entries!.Length;
            var newSize = MemoryLaneUtils.EnsureEntryCapacity(ref _entries, requiredSlotIndex);
            OnAllocationExtension?.Invoke(nameof(LinearLane), oldSize, newSize);
        }

        public int FreeSpace() => Capacity - _nextFreeOffset;
        public int StubCount() => MemoryLaneUtils.StubCount(EntryCount, _entries!);

        public int EstimateFragmentation()
        {
            int allocatedBytes = _nextFreeOffset;
            int usedBytes = 0;
            for (int i = 0; i < EntryCount; i++) if (!_entries![i].IsStub) usedBytes += _entries[i].Size;

            if (allocatedBytes == 0) return 0;
            return (int)(((double)(allocatedBytes - usedBytes) / allocatedBytes) * 100);
        }

        public double UsagePercentage() => (double)(Capacity - FreeSpace()) / Capacity;

        // Passthroughs to MemoryLaneUtils
        public AllocationEntry GetEntry(MemoryHandle handle) => MemoryLaneUtils.GetEntry(handle, _handleIndex, _entries!, nameof(LinearLane));
        public int GetAllocationSize(MemoryHandle handle) => MemoryLaneUtils.GetAllocationSize(handle, _handleIndex, _entries!, nameof(LinearLane));
        public bool HasHandle(MemoryHandle handle) => MemoryLaneUtils.HasHandle(handle, _handleIndex);
        public string DebugDump() => MemoryLaneUtils.DebugDump(_entries!, EntryCount);
        public string DebugVisualMap() => MemoryLaneUtils.DebugVisualMap(_entries!, EntryCount, Capacity);
        public string DebugRedirections() => MemoryLaneUtils.DebugRedirections(_entries!, EntryCount, null);
        public IEnumerable<MemoryHandle> GetHandles() => _handleIndex.Keys.Select(id => new MemoryHandle(id, this));

        public event Action<string>? OnCompaction;
        public event Action<string, int, int>? OnAllocationExtension;
        public void LogDump() => Trace.WriteLine(DebugDump());
    }
}