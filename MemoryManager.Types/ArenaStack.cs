/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     MemoryManager.Types
 * FILE:        ArenaStack.cs
 * PURPOSE:     A high-performance, resizable LIFO stack for unmanaged types backed by an IMemoryAllocator.
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

using System.Runtime.CompilerServices;
using MemoryManager.Core;

namespace MemoryManager.Types
{
    /// <summary>
    /// A high-performance, resizable LIFO stack for unmanaged types backed by an <see cref="IMemoryAllocator"/>.
    /// </summary>
    /// <typeparam name="T">The unmanaged type to store.</typeparam>
    public sealed class ArenaStack<T> : IDisposable where T : unmanaged
    {
        /// <summary>
        /// The arena
        /// </summary>
        private readonly IMemoryAllocator? _arena;

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
        /// Gets the count.
        /// </summary>
        /// <value>
        /// The count.
        /// </value>
        public int Count { get; private set; }

        /// <summary>
        /// Gets the capacity.
        /// </summary>
        /// <value>
        /// The capacity.
        /// </value>
        public int Capacity => _capacity;

        /// <summary>
        /// Initializes a new instance of the <see cref="ArenaStack{T}" /> class.
        /// </summary>
        /// <param name="arena">The arena.</param>
        /// <param name="initialCapacity">The initial capacity.</param>
        /// <param name="priority">The priority.</param>
        /// <param name="hints">The hints.</param>
        /// <exception cref="System.ArgumentNullException">arena - Cannot instantiate ArenaStack without a valid allocator.</exception>
        public ArenaStack(IMemoryAllocator? arena, int initialCapacity = 8,
            AllocationPriority priority = AllocationPriority.Normal, AllocationHints hints = AllocationHints.None)
        {
            _arena = arena ?? throw new ArgumentNullException(nameof(arena), $"Cannot instantiate {GetType().Name} without a valid allocator.");

            if (initialCapacity <= 0) initialCapacity = 8;

            _arena = arena;
            _capacity = initialCapacity;
            _priority = priority;
            _hints = hints;
            _handle = _arena.Allocate(Unsafe.SizeOf<T>() * _capacity, _priority, _hints);
        }

        /// <summary>
        /// Pushes the specified item.
        /// </summary>
        /// <param name="item">The item.</param>
        public void Push(T item)
        {
            if (Count == _capacity) Grow();
            var span = _arena.GetSpan<T>(_handle, _capacity);
            span[Count++] = item;
        }

        /// <summary>
        /// Pops this instance.
        /// </summary>
        /// <returns>The item at the top of the stack.</returns>
        /// <exception cref="System.InvalidOperationException">The ArenaStack is empty.</exception>
        public T Pop()
        {
            if (Count == 0)
                throw new InvalidOperationException("The ArenaStack is empty.");

            var span = _arena.GetSpan<T>(_handle, _capacity);
            return span[--Count];
        }

        /// <summary>
        /// Peeks this instance.
        /// </summary>
        /// <returns>The item at the top of the stack.</returns>
        /// <exception cref="System.InvalidOperationException">The ArenaStack is empty.</exception>
        public ref T Peek()
        {
            if (Count == 0)
                throw new InvalidOperationException("The ArenaStack is empty.");

            var span = _arena.GetSpan<T>(_handle, _capacity);
            return ref span[Count - 1];
        }

        /// <summary>
        /// Tries the pop.
        /// </summary>
        /// <param name="result">The result.</param>
        /// <returns>True if an item was successfully popped; otherwise, false.</returns>
        public bool TryPop(out T result)
        {
            if (Count == 0)
            {
                result = default;
                return false;
            }

            var span = _arena.GetSpan<T>(_handle, _capacity);
            result = span[--Count];
            return true;
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
        public Span<T> AsSpan() => Count == 0 ? Span<T>.Empty : _arena.GetSpan<T>(_handle, _capacity).Slice(0, Count);

        /// <summary>
        /// Grows this instance.
        /// </summary>
        /// <exception cref="System.InvalidOperationException">Cannot grow ArenaStack without a valid IMemoryAllocator.</exception>
        private void Grow()
        {
            var newCapacity = _capacity * 2;
            var newHandle = _arena.Allocate(Unsafe.SizeOf<T>() * newCapacity, _priority, _hints);

            var oldSpan = _arena.GetSpan<T>(_handle, _capacity);
            var newSpan = _arena.GetSpan<T>(newHandle, newCapacity);

            oldSpan.Slice(0, Count).CopyTo(newSpan);
            _arena.Free(_handle);

            _handle = newHandle;
            _capacity = newCapacity;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_handle.IsInvalid) return;
            _arena?.Free(_handle);
            _handle = default;
        }
    }
}