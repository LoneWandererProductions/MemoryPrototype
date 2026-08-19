/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     MemoryManager.Types
 * FILE:        ArenaPool.cs
 * PURPOSE:     A zero-GC object pool for recycling unmanaged struct instances using index tracking backed by an IMemoryAllocator.
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

using System.Runtime.CompilerServices;
using MemoryManager.Core;

namespace MemoryManager.Types
{
    /// <summary>
    /// A zero-GC object pool for recycling unmanaged struct instances using index tracking backed by an <see cref="IMemoryAllocator"/>.
    /// </summary>
    /// <typeparam name="T">The unmanaged type to pool.</typeparam>
    public sealed class ArenaPool<T> : IDisposable where T : unmanaged
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
        /// The data handle
        /// </summary>
        private MemoryHandle _dataHandle;

        /// <summary>
        /// The free indices handle
        /// </summary>
        private MemoryHandle _freeIndicesHandle;

        /// <summary>
        /// The capacity
        /// </summary>
        private int _capacity;

        /// <summary>
        /// The free count
        /// </summary>
        private int _freeCount;

        /// <summary>
        /// Gets the capacity.
        /// </summary>
        /// <value>
        /// The capacity.
        /// </value>
        public int Capacity => _capacity;

        /// <summary>
        /// Gets the active count.
        /// </summary>
        /// <value>
        /// The active count.
        /// </value>
        public int ActiveCount => _capacity - _freeCount;

        /// <summary>
        /// Initializes a new instance of the <see cref="ArenaPool{T}" /> class.
        /// </summary>
        /// <param name="arena">The arena.</param>
        /// <param name="initialCapacity">The initial capacity.</param>
        /// <param name="priority">The priority.</param>
        /// <param name="hints">The hints.</param>
        /// <exception cref="System.ArgumentNullException">arena - Cannot instantiate ArenaPool without a valid allocator.</exception>
        public ArenaPool(IMemoryAllocator? arena, int initialCapacity = 16,
            AllocationPriority priority = AllocationPriority.Normal, AllocationHints hints = AllocationHints.None)
        {
            _arena = arena ?? throw new ArgumentNullException(nameof(arena), $"Cannot instantiate {GetType().Name} without a valid allocator.");

            if (initialCapacity <= 0) initialCapacity = 16;

            _arena = arena;
            _capacity = initialCapacity;
            _priority = priority;
            _hints = hints;

            _dataHandle = _arena.Allocate(Unsafe.SizeOf<T>() * _capacity, _priority, _hints);
            _freeIndicesHandle = _arena.Allocate(sizeof(int) * _capacity, _priority, _hints);

            InitializeFreeIndices(0, _capacity);
        }

        public ref T Rent(out int index)
        {
            if (_freeCount == 0) Grow();

            var freeIndices = _arena.GetSpan<int>(_freeIndicesHandle, _capacity);
            index = freeIndices[--_freeCount];

            var dataSpan = _arena.GetSpan<T>(_dataHandle, _capacity);
            return ref dataSpan[index];
        }

        /// <summary>
        /// Returns the specified index.
        /// </summary>
        /// <param name="index">The index.</param>
        /// <exception cref="System.ArgumentOutOfRangeException">index</exception>
        public void Return(int index)
        {
            if (index < 0 || index >= _capacity)
                throw new ArgumentOutOfRangeException(nameof(index));

            var freeIndices = _arena.GetSpan<int>(_freeIndicesHandle, _capacity);
            freeIndices[_freeCount++] = index;
        }

        /// <summary>
        /// Gets the specified index.
        /// </summary>
        /// <param name="index">The index.</param>
        /// <returns>Get Data at Index.</returns>
        /// <exception cref="System.ArgumentOutOfRangeException">index</exception>
        public ref T Get(int index)
        {
            if (index < 0 || index >= _capacity)
                throw new ArgumentOutOfRangeException(nameof(index));

            var dataSpan = _arena.GetSpan<T>(_dataHandle, _capacity);
            return ref dataSpan[index];
        }

        /// <summary>
        /// Initializes the free indices.
        /// </summary>
        /// <param name="start">The start.</param>
        /// <param name="end">The end.</param>
        private void InitializeFreeIndices(int start, int end)
        {
            var freeIndices = _arena.GetSpan<int>(_freeIndicesHandle, _capacity);
            for (int i = start; i < end; i++)
            {
                freeIndices[_freeCount++] = i;
            }
        }

        /// <summary>
        /// Grows this instance.
        /// </summary>
        private void Grow()
        {
            var newCapacity = _capacity * 2;
            var newDataHandle = _arena.Allocate(Unsafe.SizeOf<T>() * newCapacity, _priority, _hints);
            var newFreeHandle = _arena.Allocate(sizeof(int) * newCapacity, _priority, _hints);

            var oldData = _arena.GetSpan<T>(_dataHandle, _capacity);
            var newData = _arena.GetSpan<T>(newDataHandle, newCapacity);
            oldData.CopyTo(newData);

            var oldFree = _arena.GetSpan<int>(_freeIndicesHandle, _capacity);
            var newFree = _arena.GetSpan<int>(newFreeHandle, newCapacity);
            oldFree.Slice(0, _freeCount).CopyTo(newFree);

            _arena.Free(_dataHandle);
            _arena.Free(_freeIndicesHandle);

            _dataHandle = newDataHandle;
            _freeIndicesHandle = newFreeHandle;

            var oldCap = _capacity;
            _capacity = newCapacity;
            InitializeFreeIndices(oldCap, _capacity);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (!_dataHandle.IsInvalid) _arena.Free(_dataHandle);
            if (!_freeIndicesHandle.IsInvalid) _arena.Free(_freeIndicesHandle);
            _dataHandle = default;
            _freeIndicesHandle = default;
        }
    }
}