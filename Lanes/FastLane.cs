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
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using Core;
using Core.MemoryArenaPrototype.Core;

namespace Lanes
{
    public sealed class FastLane : IMemoryLane, IDisposable
    {
        /// <summary>
        ///     The free ids
        /// </summary>
        private readonly Stack<int> _freeIds = new();

        /// <summary>
        ///     The handle index
        ///     Maps handleId → index into _entries
        /// </summary>
        private readonly Dictionary<int, int> _handleIndex = new();

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
        ///     Initializes a new instance of the <see cref="FastLane" /> class.
        /// </summary>
        /// <param name="size">The size.</param>
        /// <param name="slowLane">The slow lane.</param>
        public FastLane(int size, SlowLane slowLane)
        {
            _slowLane = slowLane;
            Capacity = size;
            Buffer = Marshal.AllocHGlobal(size);
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

        /// <summary>
        ///     Determines whether this instance can allocate the specified size.
        /// </summary>
        /// <param name="size">The size.</param>
        /// <returns>
        ///     <c>true</c> if this instance can allocate the specified size; otherwise, <c>false</c>.
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
        ///     Allocates the specified size.
        /// </summary>
        /// <param name="size">The size.</param>
        /// <param name="priority">The priority.</param>
        /// <param name="hints">The hints.</param>
        /// <param name="debugName">Name of the debug.</param>
        /// <param name="currentFrame">The current frame.</param>
        /// <returns>Handler to the reserved memory.</returns>
        /// <exception cref="InvalidOperationException">FastLane: Memory not reserved</exception>
        /// <exception cref="OutOfMemoryException">FastLane: Not enough memory</exception>
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

            EnsureEntryCapacity(EntryCount);
            //if (EntryCount >= _entries.Length)
            //    Array.Resize(ref _entries, _entries.Length * 2);

            //So we reuse freed handles here
            var id = MemoryLaneUtils.GetNextId(_freeIds, ref _nextHandleId);

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

        /// <summary>
        ///     Resolves the specified handle.
        /// </summary>
        /// <param name="handle">The handle.</param>
        /// <returns>Pointer to the Memory.</returns>
        /// <exception cref="InvalidOperationException">
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

        /// <summary>
        ///     Frees the specified handle.
        /// </summary>
        /// <param name="handle">The handle.</param>
        /// <exception cref="InvalidOperationException">
        ///     FastLane: Memory is corrupted.
        ///     or
        ///     FastLane: Invalid handle
        /// </exception>
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
            _freeIds.Push(handle.Id);
        }

        /// <summary>
        ///     Compacts this instance.
        /// </summary>
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
                            // still update handleIndex to keep consistent
                            _handleIndex[entry.HandleId] = i;
                            continue;
                        }
                    }

                    void* source = (byte*)Buffer + entry.Offset;
                    void* target = (byte*)newBuffer + offset;
                    System.Buffer.MemoryCopy(source, target, Capacity - offset, entry.Size);
                    entry.Offset = offset;
                    offset += entry.Size;
                }

                _entries[i] = entry;
                _handleIndex[entry.HandleId] = i;
            }

            Marshal.FreeHGlobal(Buffer);
            Buffer = newBuffer;
            OnCompaction?.Invoke(nameof(FastLane));
        }

        /// <summary>
        /// Retrieves the full allocation entry metadata for a given handle.
        /// </summary>
        /// <param name="handle">The handle identifying the allocation.</param>
        /// <returns>
        /// The allocation entry associated with the handle.
        /// </returns>
        public AllocationEntry GetEntry(MemoryHandle handle)
        {
            return MemoryLaneUtils.GetEntry(handle, _handleIndex, _entries, nameof(FastLane));
        }

        /// <summary>
        ///     Gets the size of the allocation.
        /// </summary>
        /// <param name="handle">The handle.</param>
        /// <returns>Returns the used space of Allocation.</returns>
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

        /// <summary>
        ///     Occurs when [on compaction].
        /// </summary>
        public event Action<string>? OnCompaction;

        /// <summary>
        /// Occurs when [on allocation extension].
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
            entry.RedirectTo = slowHandle;
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
        /// Ensures the entry capacity.
        /// </summary>
        /// <param name="requiredSlotIndex">Index of the required slot.</param>
        private void EnsureEntryCapacity(int requiredSlotIndex)
        {
            var oldSize = _entries.Count();
            var newSize = MemoryLaneUtils.EnsureEntryCapacity(ref _entries, requiredSlotIndex);
            // Allocation Entriesmust be extended
            OnAllocationExtension?.Invoke(nameof(SlowLane), oldSize, newSize);
        }

        /// <summary>
        /// Finds the free spot.
        /// </summary>
        /// <param name="size">The size.</param>
        /// <returns></returns>
        /// <exception cref="System.InvalidOperationException">FastLane: Invalid memory.</exception>
        private int FindFreeSpot(int size)
        {
            if (_entries == null) throw new InvalidOperationException("FastLane: Invalid memory.");

            return MemoryLaneUtils.FindFreeSpot(size, _entries, EntryCount);
        }

        // Returns total free bytes in FastLane
        public int FreeSpace()
        {
            if (_entries == null) throw new InvalidOperationException("FastLane: Invalid memory.");

            return MemoryLaneUtils.CalculateFreeSpace(_entries, EntryCount, Capacity);
        }

        // Returns count of stub entries
        public int StubCount()
        {
            if (_entries == null) throw new InvalidOperationException("FastLane: Invalid memory.");

            return MemoryLaneUtils.StubCount(EntryCount, _entries);
        }

        // Estimate fragmentation percentage (gaps / total capacity)
        public int EstimateFragmentation()
        {
            if (_entries == null) throw new InvalidOperationException("FastLane: Invalid memory.");

            return MemoryLaneUtils.EstimateFragmentation(_entries, EntryCount, Capacity);
        }

        /// <summary>
        ///     Usages the percentage.
        /// </summary>
        /// <returns>Percentage of used memory.</returns>
        public double UsagePercentage()
        {
            if (_entries == null) throw new InvalidOperationException("FastLane: Invalid memory.");

            return MemoryLaneUtils.UsagePercentage(EntryCount, _entries, Capacity);
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