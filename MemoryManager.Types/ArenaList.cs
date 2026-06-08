/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     MemoryManager.Types
 * FILE:        ArenaList.cs
 * PURPOSE:     A resizable, unmanaged list backed by a MemoryArena. 
 *              Automatically handles growth and memory reclamation via handles.
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

using MemoryManager.Core;
using System.Runtime.CompilerServices;

namespace MemoryManager.Types
{
    /// <summary>
    /// A high-performance, resizable list for unmanaged types that allocates
    /// its internal buffer from a <see cref="MemoryArena"/>.
    /// </summary>
    /// <typeparam name="T">The unmanaged type to store.</typeparam>
    public sealed class ArenaList<T> : IDisposable where T : unmanaged
    {
        /// <summary>
        /// The arena
        /// </summary>
        private readonly MemoryArena _arena;

        /// <summary>
        /// The priority
        /// </summary>
        private readonly AllocationPriority _priority;

        /// <summary>
        /// The hints
        /// </summary>
        private readonly AllocationHints _hints;

        /// <summary>
        /// The handle
        /// </summary>
        private MemoryHandle _handle;

        /// <summary>
        /// The capacity
        /// </summary>
        private int _capacity;

        /// <summary>
        /// Gets the number of elements currently contained in the list.
        /// </summary>
        public int Count { get; private set; }

        /// <summary>
        /// Gets the total number of elements the internal memory buffer can hold before resizing.
        /// </summary>
        public int Capacity => _capacity;

        /// <summary>
        /// Initializes a new instance of the <see cref="ArenaList{T}"/> class.
        /// </summary>
        public ArenaList(MemoryArena arena, int initialCapacity = 8, AllocationPriority priority = AllocationPriority.Normal, AllocationHints hints = AllocationHints.None)
        {
            if (initialCapacity <= 0) initialCapacity = 8;

            _arena = arena;
            _capacity = initialCapacity;
            _priority = priority;
            _hints = hints;

            // Allocate the initial contiguous block from the arena using explicit design parameters
            _handle = _arena.Allocate(Unsafe.SizeOf<T>() * _capacity, _priority, _hints);
        }

        /// <summary>
        /// Gets or sets the element at the specified index via direct address reference tracking expressions.
        /// </summary>
        /// <remarks>
        /// Warning: Avoid using this indexer heavily inside tight loops, as each invocation incurs a synchronization check.
        /// For performance-critical iteration paths, extract a transient block layout using <see cref="AsSpan"/> instead.
        /// </remarks>
        public ref T this[int index] => ref Get(index);

        /// <summary>
        /// Adds an item to the list, automatically growing the internal buffer if necessary.
        /// </summary>
        public void Add(T item)
        {
            if (Count == _capacity) Grow();

            // Resolve layout constraints safely and insert item at tracking high-water marks
            var span = _arena.GetSpan<T>(_handle, _capacity);
            span[Count++] = item;
        }

        /// <summary>
        /// Returns a reference to the element at the specified index.
        /// </summary>
        public ref T Get(int index)
        {
            if (index < 0 || index >= Count)
                throw new IndexOutOfRangeException($"Index {index} is outside the active boundaries of the ArenaList (Count: {Count}).");

            var span = _arena.GetSpan<T>(_handle, _capacity);
            return ref span[index];
        }

        /// <summary>
        /// Resets the count to zero. Note: Does not clear memory tracking until an arena cycle triggers maintenance.
        /// </summary>
        public void Clear()
        {
            Count = 0;
        }

        /// <summary>
        /// Exposes a fast, direct raw hardware <see cref="Span{T}"/> over the active items list.
        /// This completely bypasses inner index loops lock overhead constraints.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span<T> AsSpan()
        {
            if (Count == 0) return Span<T>.Empty;
            return _arena.GetSpan<T>(_handle, _capacity).Slice(0, Count);
        }

        /// <summary>
        /// Doubles the capacity of the list by allocating a new handle and migrating data.
        /// </summary>
        private void Grow()
        {
            var newCapacity = _capacity * 2;
            var newHandle = _arena.Allocate(Unsafe.SizeOf<T>() * newCapacity, _priority, _hints);

            // Fetch structural views over original and destination arrays atomically
            var oldSpan = _arena.GetSpan<T>(_handle, _capacity);
            var newSpan = _arena.GetSpan<T>(newHandle, newCapacity);

            // Execute blazing fast bitwise memory migration copy operations
            oldSpan.CopyTo(newSpan);

            // Reclaim legacy positions within the parent lane array trackers instantly
            _arena.Free(_handle);

            _handle = newHandle;
            _capacity = newCapacity;
        }

        /// <summary>
        /// Releases the unmanaged memory handle pool block back to the parent arena coordinator framework.
        /// </summary>
        public void Dispose()
        {
            if (!_handle.IsInvalid)
            {
                _arena.Free(_handle);
                _handle = default;
            }
        }
    }
}