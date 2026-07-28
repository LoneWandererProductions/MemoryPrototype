/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     MemoryManager.Types
 * FILE:        ArenaBuffer.cs
 * PURPOSE:     A fixed-capacity, non-growing buffer backed by IMemoryAllocator for high-speed scratchpads.
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

using System.Runtime.CompilerServices;
using MemoryManager.Core;

namespace MemoryManager.Types
{
    /// <summary>
    /// A fixed-capacity unmanaged buffer that allocates once from <see cref="IMemoryAllocator"/>
    /// and disallows dynamic growth, eliminating re-allocation overhead in inner loops.
    /// </summary>
    /// <typeparam name="T">The unmanaged type to store.</typeparam>
    public sealed class ArenaBuffer<T> : IDisposable where T : unmanaged
    {
        /// <summary>
        /// The arena
        /// </summary>
        private readonly IMemoryAllocator _arena;

        /// <summary>
        /// The capacity
        /// </summary>
        private readonly int _capacity;

        /// <summary>
        /// The handle
        /// </summary>
        private MemoryHandle _handle;

        /// <summary>
        /// Gets the number of elements currently contained in the buffer.
        /// </summary>
        public int Count { get; private set; }

        /// <summary>
        /// Gets the total fixed capacity of the buffer.
        /// </summary>
        public int Capacity => _capacity;

        /// <summary>
        /// Initializes a new instance of the <see cref="ArenaBuffer{T}" /> class.
        /// </summary>
        /// <param name="arena">The arena.</param>
        /// <param name="capacity">The capacity.</param>
        /// <param name="priority">The priority.</param>
        /// <param name="hints">The hints.</param>
        /// <exception cref="System.ArgumentOutOfRangeException">capacity - Capacity must be greater than zero.</exception>
        public ArenaBuffer(
                IMemoryAllocator arena,
                int capacity,
                AllocationPriority priority = AllocationPriority.Critical,
                AllocationHints hints = AllocationHints.FrameCritical | AllocationHints.NoSpill)
        {
            if (capacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be > 0.");

            _arena = arena;
            _capacity = capacity;
            _handle = _arena.Allocate(Unsafe.SizeOf<T>() * _capacity, priority, hints);
        }

        /// <summary>
        /// Gets a reference to the element at the specified index.
        /// </summary>
        public ref T this[int index] => ref Get(index);

        /// <summary>
        /// Appends an item to the buffer. Throws if capacity is exceeded.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add(in T item)
        {
            if (Count >= _capacity)
                throw new InvalidOperationException($"ArenaBuffer capacity ({_capacity}) exceeded.");

            var span = _arena.GetSpan<T>(_handle, _capacity);
            span[Count++] = item;
        }

        /// <summary>
        /// Gets a reference to the element at the specified index.
        /// </summary>
        /// <param name="index">The index.</param>
        /// <returns>Data at index.</returns>
        /// <exception cref="System.IndexOutOfRangeException">Index {index} is outside active boundaries (Count: {Count}).</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T Get(int index)
        {
            if (index < 0 || index >= Count)
                throw new IndexOutOfRangeException($"Index {index} is outside active boundaries (Count: {Count}).");

            var span = _arena.GetSpan<T>(_handle, _capacity);
            return ref span[index];
        }

        /// <summary>
        /// Resets the count to 0 without freeing or re-allocating memory.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear() => Count = 0;

        /// <summary>
        /// Returns the active window as a <see cref="Span{T}"/>.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span<T> AsSpan()
        {
            return Count == 0 ? Span<T>.Empty : _arena.GetSpan<T>(_handle, _capacity).Slice(0, Count);
        }

        /// <summary>
        /// Returns an enumerator for clean 'foreach' loops.
        /// </summary>
        /// <returns>Enumeration element.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span<T>.Enumerator GetEnumerator() => AsSpan().GetEnumerator();

        /// <inheritdoc />
        public void Dispose()
        {
            if (_handle.IsInvalid) return;

            _arena.Free(_handle);
            _handle = default;
        }
    }
}