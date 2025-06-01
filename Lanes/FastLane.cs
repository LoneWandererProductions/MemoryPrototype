/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     Lanes
 * FILE:        FastLane.cs
 * PURPOSE:     Your file purpose here
 * PROGRAMMER:  Your name here
 */

// ReSharper disable MemberCanBePrivate.Global

#nullable enable
using Core;
using Core.MemoryArenaPrototype.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace Lanes
{
    public sealed class FastLane : IMemoryLane, IDisposable
    {
        public int Capacity { get; private set; }

        private AllocationEntry[]? _entries = new AllocationEntry[128];

        /// <summary>
        /// Gets the entry count.
        /// </summary>
        /// <value>
        /// The entry count.
        /// </value>
        public int EntryCount { get; private set; } = 0;

        /// <summary>
        /// Occurs when [on compaction].
        /// </summary>
        public event Action<string>? OnCompaction;

        private readonly Dictionary<int, int> _handleIndex = new(); // Maps handleId → index into _entries

        /// <summary>
        /// Only contains handles that were replaced with stubs
        /// </summary>
        private Dictionary<int, MemoryHandle> Redirects { get; set; } = new();

        /// <summary>
        /// The slow lane
        /// </summary>
        private readonly SlowLane _slowLane;

        public OneWayLane? OneWayLane { get; set; }

        /// <summary>
        /// Gets the buffer.
        /// </summary>
        /// <value>
        /// The buffer.
        /// </value>
        public IntPtr Buffer { get; private set; }

        /// <summary>
        /// The next handle identifier
        /// </summary>
        private int NextHandleId { get; set; } = 1;

        /// <summary>
        /// Determines whether this instance can allocate the specified size.
        /// </summary>
        /// <param name="size">The size.</param>
        /// <returns>
        ///   <c>true</c> if this instance can allocate the specified size; otherwise, <c>false</c>.
        /// </returns>
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

        /// <summary>
        /// Initializes a new instance of the <see cref="FastLane"/> class.
        /// </summary>
        /// <param name="size">The size.</param>
        /// <param name="slowLane">The slow lane.</param>
        public FastLane(int size, SlowLane slowLane)
        {
            _slowLane = slowLane;
            Capacity = size;
            Buffer = Marshal.AllocHGlobal(size);
        }

        public MemoryHandle Allocate(
            int size,
            AllocationPriority priority = AllocationPriority.Normal,
            AllocationHints hints = AllocationHints.None,
            string? debugName = null,
            int currentFrame = 0)
        {
            if(_entries == null) throw new InvalidOperationException("FastLane: Memory not reserved");

            var offset = FindFreeSpot(size);
            if (offset + size > Capacity)
                throw new OutOfMemoryException("FastLane: Not enough memory");

            if (EntryCount >= _entries.Length)
                Array.Resize(ref _entries, _entries.Length * 2);

            //todo, that wont fly
            var id = NextHandleId++;

            _entries[EntryCount] = new AllocationEntry
            {
                Offset = offset,
                Size = size,
                HandleId = id,
                Priority = priority,
                Hints = hints,
                DebugName = debugName,
                AllocationFrame = currentFrame,
                LastAccessFrame = currentFrame,
            };

            _handleIndex[id] = EntryCount;
            EntryCount++;

            return new MemoryHandle(id, this);
        }

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

        public void Free(MemoryHandle handle)
        {
            if (_entries == null) throw new InvalidOperationException("FastLane: Memory is corrupted.");

            if (!_handleIndex.TryGetValue(handle.Id, out var index))
                throw new InvalidOperationException("FastLane: Invalid handle");

            var entry = _entries[index];

            if (entry.IsStub && entry.RedirectTo.HasValue)
            {
                _slowLane.Free(entry.RedirectTo.Value);
                //cleanup our Dictionary
                Redirects.Remove(handle.Id);
            }

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

        public IEnumerable<MemoryHandle> GetHandles()
        {
            return _handleIndex.Keys.Select(id => new MemoryHandle(id, this));
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

        private static bool ShouldMoveToSlowLane(AllocationEntry entry)
        {
            // Basic example: offload low-priority entries
            return entry.Priority == AllocationPriority.Low;
        }

        public AllocationEntry GetEntry(MemoryHandle handle) => MemoryLaneUtils.GetEntry(handle, _handleIndex, _entries, nameof(FastLane));

        /// <summary>
        /// Gets the size of the allocation.
        /// </summary>
        /// <param name="handle">The handle.</param>
        /// <returns>Returns the used space of Allocation.</returns>
        public int GetAllocationSize(MemoryHandle handle) => MemoryLaneUtils.GetAllocationSize(handle, _handleIndex, _entries, nameof(FastLane));

        public void ReplaceWithStub(MemoryHandle fastHandle, MemoryHandle slowHandle)
        {
            if (_entries == null) throw new InvalidOperationException("FastLane: Invalid handle");

            if (!_handleIndex.TryGetValue(fastHandle.Id, out var index))
                throw new InvalidOperationException("FastLane: Invalid handle");

            var entry = _entries[index];
            entry.IsStub = true;
            entry.RedirectTo = slowHandle;
            _entries[index] = entry;

            Redirects[fastHandle.Id] = slowHandle;
        }

        /// <summary>
        /// Determines whether the specified handle has handle.
        /// </summary>
        /// <param name="handle">The handle.</param>
        /// <returns>
        ///   <c>true</c> if the specified handle has handle; otherwise, <c>false</c>.
        /// </returns>
        public bool HasHandle(MemoryHandle handle) => MemoryLaneUtils.HasHandle(handle, _handleIndex);

        private int FindFreeSpot(int size) => MemoryLaneUtils.FindFreeSpot(size, _entries, EntryCount);

        // Returns total free bytes in FastLane
        public int FreeSpace() => MemoryLaneUtils.CalculateFreeSpace(_entries, EntryCount, Capacity);

        // Returns count of stub entries
        public int StubCount() => MemoryLaneUtils.StubCount(EntryCount, _entries);

        // Estimate fragmentation percentage (gaps / total capacity)
        public int EstimateFragmentation() => MemoryLaneUtils.EstimateFragmentation(_entries, EntryCount, Capacity);

        /// <summary>
        /// Usages the percentage.
        /// </summary>
        /// <returns>Percentage of used memory.</returns>
        public double UsagePercentage() => MemoryLaneUtils.UsagePercentage(EntryCount, _entries, Capacity);

        /// <summary>
        /// Debugs the dump.
        /// </summary>
        /// <returns>Basic Debug Info</returns>
        public string DebugDump() => MemoryLaneUtils.DebugDump(_entries, EntryCount);

        /// <summary>
        /// Debugs the visual map.
        /// </summary>
        /// <returns>Visual information about the Debug and Memory layout.</returns>
        public string DebugVisualMap() => MemoryLaneUtils.DebugVisualMap(_entries, EntryCount, Capacity);

        /// <summary>
        /// Debugs the redirections.
        /// </summary>
        /// <returns>A overview of Redirections.</returns>
        public string DebugRedirections() => MemoryLaneUtils.DebugRedirections(_entries, EntryCount);

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            Marshal.FreeHGlobal(Buffer);
            _handleIndex.Clear();
            Redirects.Clear();
            _entries = null;
        }
    }
}