// ReSharper disable MemberCanBePrivate.Global

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Core;
using Core.MemoryArenaPrototype.Core;

namespace Lanes
{
    public sealed class FastLane : IMemoryLane, IDisposable
    {
        private readonly Dictionary<int, int> _handleIndex = new(); // Maps handleId → index into _entries

        private readonly SlowLane _slowLane;

        private AllocationEntry[]? _entries = new AllocationEntry[128];

        private int _nextHandleId = 1;

        public FastLane(int size, SlowLane slowLane)
        {
            _slowLane = slowLane;
            Capacity = size;
            Buffer = Marshal.AllocHGlobal(size);
        }

        public int Capacity { get; }
        public int EntryCount { get; private set; }

        public OneWayLane? OneWayLane { get; set; }

        /// <summary>
        ///     Gets the buffer.
        /// </summary>
        /// <value>
        ///     The buffer.
        /// </value>
        public IntPtr Buffer { get; private set; }

        /// <summary>
        ///     Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            Marshal.FreeHGlobal(Buffer);
            _handleIndex.Clear();
            _entries = null;
        }

        public bool CanAllocate(int size)
        {
            try
            {
                return FindFreeSpot(size) + size <= Capacity;
            }
            catch
            {
                return false;
            }
        }

        public MemoryHandle Allocate(
            int size,
            AllocationPriority priority = AllocationPriority.Normal,
            AllocationHints hints = AllocationHints.None,
            string? debugName = null,
            int currentFrame = 0)
        {
            if (_entries == null) throw new InvalidOperationException("FastLane: Memory not reserved");

            var offset = FindFreeSpot(size);
            if (offset + size > Capacity)
                throw new OutOfMemoryException("FastLane: Not enough memory");

            if (EntryCount >= _entries.Length)
                Array.Resize(ref _entries, _entries.Length * 2);

            var id = _nextHandleId++;
            _entries[EntryCount] = new AllocationEntry
            {
                Offset = offset,
                Size = size,
                HandleId = id,
                Priority = priority,
                Hints = hints,
                DebugName = debugName,
                AllocationFrame = currentFrame,
                LastAccessFrame = currentFrame
            };

            _handleIndex[id] = EntryCount;
            EntryCount++;

            return new MemoryHandle(id, this);
        }

        public IntPtr Resolve(MemoryHandle handle)
        {
            if (!_handleIndex.TryGetValue(handle.Id, out var index))
                throw new InvalidOperationException("FastLane: Invalid handle");

            var entry = _entries[index];
            if (entry.IsStub && entry.RedirectTo.HasValue)
                return _slowLane.Resolve(entry.RedirectTo.Value);

            return Buffer + entry.Offset;
        }

        public void Free(MemoryHandle handle)
        {
            if (!_handleIndex.TryGetValue(handle.Id, out var index))
                throw new InvalidOperationException("FastLane: Invalid handle");

            var entry = _entries[index];

            if (entry.IsStub && entry.RedirectTo.HasValue) _slowLane.Free(entry.RedirectTo.Value);

            // Remove by shifting tail and updating map
            var last = EntryCount - 1;
            if (index != last)
            {
                _entries[index] = _entries[last];
                _handleIndex[_entries[index].HandleId] = index;
            }

            _handleIndex.Remove(handle.Id);
            EntryCount--;
        }

        public unsafe void Compact()
        {
            if (_entries == null) return;

            var newBuffer = Marshal.AllocHGlobal(Capacity);
            var offset = 0;

            for (var i = 0; i < EntryCount; i++)
            {
                var entry = _entries[i];

                if (!entry.IsStub)
                {
                    // Try offloading to OneWayLane, if available and policy allows
                    if (OneWayLane != null && ShouldMoveToSlowLane(entry))
                    {
                        var fastHandle = new MemoryHandle(entry.HandleId, this);

                        if (OneWayLane.MoveFromFastToSlow(fastHandle))
                        {
                            entry.IsStub = true;
                            entry.Size = 0;
                            entry.Offset = 0;
                            entry.RedirectTo = null;
                            _entries[i] = entry;
                            _handleIndex[entry.HandleId] = i;
                            continue;
                        }
                    }
                    else
                    {
                        void* source = (byte*)Buffer + entry.Offset;
                        void* target = (byte*)newBuffer + offset;

                        System.Buffer.MemoryCopy(source, target, Capacity - offset, entry.Size);
                        entry.Offset = offset;
                        offset += entry.Size;
                    }
                }

                _entries[i] = entry;
                _handleIndex[entry.HandleId] = i;
            }

            Marshal.FreeHGlobal(Buffer);
            Buffer = newBuffer;
            OnCompaction?.Invoke(nameof(FastLane));
        }

        public AllocationEntry GetEntry(MemoryHandle handle)
        {
            return MemoryLaneUtils.GetEntry(handle, _handleIndex, _entries, nameof(FastLane));
        }

        public int GetAllocationSize(MemoryHandle handle)
        {
            return MemoryLaneUtils.GetAllocationSize(handle, _handleIndex, _entries, nameof(FastLane));
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
        ///     Debugs the dump.
        /// </summary>
        /// <returns>Basic Debug Info</returns>
        public string DebugDump()
        {
            return MemoryLaneUtils.DebugDump(_entries, EntryCount);
        }

        public event Action<string>? OnCompaction;

        public IEnumerable<MemoryHandle> GetHandles()
        {
            return _handleIndex.Keys.Select(id => new MemoryHandle(id, this));
        }

        private static bool ShouldMoveToSlowLane(AllocationEntry entry)
        {
            // Basic example: offload low-priority entries
            return entry.Priority == AllocationPriority.Low;
        }

        public void ReplaceWithStub(MemoryHandle fastHandle, MemoryHandle slowHandle)
        {
            if (_entries == null) throw new InvalidOperationException("FastLane: Invalid handle");

            if (!_handleIndex.TryGetValue(fastHandle.Id, out var index))
                throw new InvalidOperationException("FastLane: Invalid handle");

            var entry = _entries[index];
            entry.IsStub = true;
            entry.RedirectTo = slowHandle;
            _entries[index] = entry;
        }

        private int FindFreeSpot(int size)
        {
            return MemoryLaneUtils.FindFreeSpot(size, _entries, EntryCount);
        }

        // Returns total free bytes in FastLane
        public int FreeSpace()
        {
            return MemoryLaneUtils.CalculateFreeSpace(_entries, EntryCount, Capacity);
        }

        // Returns count of stub entries
        public int StubCount()
        {
            return MemoryLaneUtils.StubCount(EntryCount, _entries);
        }

        // Estimate fragmentation percentage (gaps / total capacity)
        public int EstimateFragmentation()
        {
            return MemoryLaneUtils.EstimateFragmentation(_entries, EntryCount, Capacity);
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
    }
}