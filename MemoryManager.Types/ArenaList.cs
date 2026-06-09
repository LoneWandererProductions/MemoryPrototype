/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     MemoryManager.Types
 * FILE:        ArenaList.cs
 * PURPOSE:     A resizable, unmanaged list backed by an IMemoryAllocator interface contract. 
 *              Automatically handles growth and memory reclamation via handles.
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

using MemoryManager.Core;
using System.Runtime.CompilerServices;

namespace MemoryManager.Types
{
    /// <summary>
    /// A high-performance, resizable list for unmanaged types that allocates
    /// its internal buffer from an <see cref="IMemoryAllocator"/>.
    /// </summary>
    /// <typeparam name="T">The unmanaged type to store.</typeparam>
    public sealed class ArenaList<T> : IDisposable where T : unmanaged
    {
        /// <summary>
        /// The arena
        /// </summary>
        private readonly IMemoryAllocator _arena;

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
        /// Initializes a new instance of the <see cref="ArenaList{T}" /> class.
        /// </summary>
<<<<<<< HEAD
        /// <param name="arena">The arena.</param>
        /// <param name="initialCapacity">The initial capacity.</param>
        /// <param name="priority">The priority.</param>
        /// <param name="hints">The hints.</param>
        public ArenaList(IMemoryAllocator arena, int initialCapacity = 8, AllocationPriority priority = AllocationPriority.Normal, AllocationHints hints = AllocationHints.None)
=======
        public ArenaList(MemoryArena arena, int initialCapacity = 8,
            AllocationPriority priority = AllocationPriority.Normal, AllocationHints hints = AllocationHints.None)
>>>>>>> f41cf23e1c66b3d2e4e393356f32f92443e0ab03
        {
            if (initialCapacity <= 0) initialCapacity = 8;

            _arena = arena;
            _capacity = initialCapacity;
            _priority = priority;
            _hints = hints;

            _handle = _arena.Allocate(Unsafe.SizeOf<T>() * _capacity, _priority, _hints);
        }

        /// <summary>
        /// Gets the <see cref="T"/> at the specified index.
        /// </summary>
        /// <value>
        /// The <see cref="T"/>.
        /// </value>
        /// <param name="index">The index.</param>
        /// <returns></returns>
        public ref T this[int index] => ref Get(index);

        /// <summary>
        /// Adds the specified item.
        /// </summary>
        /// <param name="item">The item.</param>
        public void Add(T item)
        {
            if (Count == _capacity) Grow();

            var span = _arena.GetSpan<T>(_handle, _capacity);
            span[Count++] = item;
        }

        /// <summary>
        /// Gets the specified index.
        /// </summary>
        /// <param name="index">The index.</param>
        /// <returns></returns>
        /// <exception cref="System.IndexOutOfRangeException">Index {index} is outside active boundaries.</exception>
        public ref T Get(int index)
        {
            if (index < 0 || index >= Count)
<<<<<<< HEAD
                throw new IndexOutOfRangeException($"Index {index} is outside active boundaries.");
=======
                throw new IndexOutOfRangeException(
                    $"Index {index} is outside the active boundaries of the ArenaList (Count: {Count}).");
>>>>>>> f41cf23e1c66b3d2e4e393356f32f92443e0ab03

            var span = _arena.GetSpan<T>(_handle, _capacity);
            return ref span[index];
        }

        /// <summary>
        /// Clears this instance.
        /// </summary>
        public void Clear() => Count = 0;

        /// <summary>
        /// Ases the span.
        /// </summary>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span<T> AsSpan()
        {
            if (Count == 0) return Span<T>.Empty;
            return _arena.GetSpan<T>(_handle, _capacity).Slice(0, Count);
        }

        /// <summary>
        /// Exposes a zero-allocation, struct-based enumerator.
        /// Allows clean 'foreach' loop compilation bypassing the managed Garbage Collector entirely.
        /// </summary>
        /// <returns>An enumerator for the list.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span<T>.Enumerator GetEnumerator() => AsSpan().GetEnumerator();

        /// <summary>
        /// Grows this instance.
        /// </summary>
        private void Grow()
        {
            var newCapacity = _capacity * 2;
            var newHandle = _arena.Allocate(Unsafe.SizeOf<T>() * newCapacity, _priority, _hints);

            var oldSpan = _arena.GetSpan<T>(_handle, _capacity);
            var newSpan = _arena.GetSpan<T>(newHandle, newCapacity);

            oldSpan.CopyTo(newSpan);
            _arena.Free(_handle);

            _handle = newHandle;
            _capacity = newCapacity;
        }

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
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