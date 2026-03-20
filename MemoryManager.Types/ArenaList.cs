/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     MemoryManager.Types
 * FILE:        ArenaList.cs
 * PURPOSE:     A resizable, unmanaged list backed by a MemoryArena. 
 * Automatically handles growth and memory reclamation via handles.
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

using Core;
using System.Runtime.CompilerServices;

namespace MemoryManager.Types
{
    /// <summary>
    /// A high-performance, resizable list for unmanaged types that allocates 
    /// its internal buffer from a <see cref="MemoryArena"/>.
    /// </summary>
    /// <typeparam name="T">The unmanaged type to store.</typeparam>
    public sealed class ArenaList<T> where T : unmanaged, IDisposable
    {
        private readonly MemoryArena _arena;
        private MemoryHandle _handle;
        private int _capacity;

        /// <summary>
        /// Gets the number of elements currently contained in the list.
        /// </summary>
        public int Count { get; private set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ArenaList{T}"/> class.
        /// </summary>
        /// <param name="arena">The arena to allocate from.</param>
        /// <param name="initialCapacity">The starting number of elements the list can hold.</param>
        public ArenaList(MemoryArena arena, int initialCapacity = 8)
        {
            _arena = arena;
            _capacity = initialCapacity;

            // Allocate the initial contiguous block from the arena
            _handle = _arena.Allocate(Unsafe.SizeOf<T>() * _capacity);
        }

        /// <summary>
        /// Adds an item to the list, automatically growing the internal buffer if necessary.
        /// </summary>
        /// <param name="item">The item to add.</param>
        public void Add(T item)
        {
            if (Count == _capacity) Grow();

            // Resolve the handle and write the data
            var span = _arena.GetSpan<T>(_handle, _capacity);
            span[Count++] = item;
        }

        /// <summary>
        /// Returns a reference to the element at the specified index.
        /// </summary>
        /// <param name="index">The zero-based index of the element to get.</param>
        /// <returns>A reference to the element at the specified index.</returns>
        /// <exception cref="IndexOutOfRangeException">Thrown when index is out of bounds.</exception>
        public ref T Get(int index)
        {
            if (index < 0 || index >= Count) throw new IndexOutOfRangeException();

            var span = _arena.GetSpan<T>(_handle, _capacity);
            return ref span[index];
        }

        /// <summary>
        /// Resets the count to zero. Note: Does not reclaim memory until the Arena compacts.
        /// </summary>
        public void Clear()
        {
            Count = 0;
        }

        /// <summary>
        /// Gets a <see cref="Span{T}"/> over the active elements in the list.
        /// </summary>
        public Span<T> AsSpan()
        {
            return _arena.GetSpan<T>(_handle, _capacity).Slice(0, Count);
        }

        /// <summary>
        /// Doubles the capacity of the list by allocating a new handle and migrating data.
        /// The old handle is freed, allowing the Arena to reclaim space during compaction.
        /// </summary>
        private void Grow()
        {
            int newCapacity = _capacity * 2;
            var newHandle = _arena.Allocate(Unsafe.SizeOf<T>() * newCapacity);

            // Copy data from the old buffer to the new one
            var oldSpan = _arena.GetSpan<T>(_handle, _capacity);
            var newSpan = _arena.GetSpan<T>(newHandle, newCapacity);
            oldSpan.CopyTo(newSpan);

            // Return the old memory to the arena for recycling
            _arena.Free(_handle);

            _handle = newHandle;
            _capacity = newCapacity;
        }

        /// <summary>
        /// Releases unmanaged and - optionally - managed resources.
        /// </summary>
        public void Dispose()
        {
            _arena.Free(_handle);
        }
    }
}