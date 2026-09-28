/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     MemoryManager.Types
 * FILE:        ArenaInternPool.cs
 * PURPOSE:     A zero-GC deduplicating object pool that inters identical unmanaged struct instances with reference counting backed by an IMemoryAllocator.
 *              (Structural Interning or the Flyweight Pattern)
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

using System.Runtime.CompilerServices;
using MemoryManager.Core;

namespace MemoryManager.Types
{
    /// <summary>
    /// A zero-GC deduplicating object pool that inters identical unmanaged struct instances with reference counting backed by an <see cref="IMemoryAllocator"/>.
    /// </summary>
    /// <typeparam name="T">The unmanaged struct type to intern and pool.</typeparam>
    public sealed class ArenaInternPool<T> : IDisposable where T : unmanaged
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
        /// The data handle
        /// </summary>
        private MemoryHandle _dataHandle;

        /// <summary>
        /// The free indices handle
        /// </summary>
        private MemoryHandle _freeIndicesHandle;

        /// <summary>
        /// The reference counts handle
        /// </summary>
        private MemoryHandle _refCountsHandle;

        /// <summary>
        /// The capacity
        /// </summary>
        private int _capacity;

        /// <summary>
        /// The free count
        /// </summary>
        private int _freeCount;

        /// <summary>
        /// The lookup
        /// </summary>
        private readonly Dictionary<T, int> _lookup;

        /// <summary>
        /// Gets the total allocated slot capacity.
        /// </summary>
        public int Capacity => _capacity;

        /// <summary>
        /// Gets the number of unique active items currently stored.
        /// </summary>
        public int UniqueCount => _lookup.Count;

        /// <summary>
        /// Gets the remaining free slots available before expansion.
        /// </summary>
        public int FreeCount => _freeCount;

        /// <summary>
        /// Initializes a new instance of the <see cref="ArenaInternPool{T}"/> class.
        /// </summary>
        public ArenaInternPool(IMemoryAllocator arena, int initialCapacity = 16,
            AllocationPriority priority = AllocationPriority.Normal, AllocationHints hints = AllocationHints.None)
        {
            _arena = arena ?? throw new ArgumentNullException(nameof(arena), $"Cannot instantiate {GetType().Name} without a valid allocator.");

            if (initialCapacity <= 0) initialCapacity = 16;

            _capacity = initialCapacity;
            _priority = priority;
            _hints = hints;

            _dataHandle = _arena.Allocate(Unsafe.SizeOf<T>() * _capacity, _priority, _hints);
            _freeIndicesHandle = _arena.Allocate(sizeof(int) * _capacity, _priority, _hints);
            _refCountsHandle = _arena.Allocate(sizeof(int) * _capacity, _priority, _hints);

            _lookup = new Dictionary<T, int>(_capacity);

            InitializeFreeIndices(0, _capacity);
        }

        /// <summary>
        /// Adds an item to the pool or increments the reference count if an identical item exists.
        /// </summary>
        /// <param name="item">The value to intern.</param>
        /// <returns>The slot index containing the pooled instance.</returns>
        public int Add(in T item)
        {
            if (_lookup.TryGetValue(item, out int existingIndex))
            {
                var refCounts = _arena.GetSpan<int>(_refCountsHandle, _capacity);
                refCounts[existingIndex]++;
                return existingIndex;
            }

            if (_freeCount == 0) Grow();

            var freeIndices = _arena.GetSpan<int>(_freeIndicesHandle, _capacity);
            int index = freeIndices[--_freeCount];

            var dataSpan = _arena.GetSpan<T>(_dataHandle, _capacity);
            dataSpan[index] = item;

            var refCountSpan = _arena.GetSpan<int>(_refCountsHandle, _capacity);
            refCountSpan[index] = 1;

            _lookup[item] = index;
            return index;
        }

        /// <summary>
        /// Decrements reference count for the specified slot. Frees the slot when the count drops to 0.
        /// </summary>
        /// <param name="index">The slot index to release.</param>
        /// <returns>True if released successfully; false if already unallocated.</returns>
        public bool Release(int index)
        {
            if (index < 0 || index >= _capacity)
                throw new ArgumentOutOfRangeException(nameof(index));

            var refCounts = _arena.GetSpan<int>(_refCountsHandle, _capacity);
            if (refCounts[index] <= 0) return false;

            refCounts[index]--;

            if (refCounts[index] == 0)
            {
                var dataSpan = _arena.GetSpan<T>(_dataHandle, _capacity);
                T value = dataSpan[index];

                _lookup.Remove(value);
                dataSpan[index] = default;

                var freeIndices = _arena.GetSpan<int>(_freeIndicesHandle, _capacity);
                freeIndices[_freeCount++] = index;
            }

            return true;
        }

        /// <summary>
        /// Gets a read-only reference to the value at the specified slot index.
        /// </summary>
        public ref readonly T Get(int index)
        {
            if (index < 0 || index >= _capacity)
                throw new ArgumentOutOfRangeException(nameof(index));

            var dataSpan = _arena.GetSpan<T>(_dataHandle, _capacity);
            return ref dataSpan[index];
        }

        /// <summary>
        /// Gets the current reference count for a slot index.
        /// </summary>
        public int GetRefCount(int index)
        {
            if (index < 0 || index >= _capacity)
                throw new ArgumentOutOfRangeException(nameof(index));

            var refCounts = _arena.GetSpan<int>(_refCountsHandle, _capacity);
            return refCounts[index];
        }

        private void InitializeFreeIndices(int start, int end)
        {
            var freeIndices = _arena.GetSpan<int>(_freeIndicesHandle, _capacity);
            for (int i = start; i < end; i++)
            {
                freeIndices[_freeCount++] = i;
            }
        }

        private void Grow()
        {
            var newCapacity = _capacity * 2;
            var newDataHandle = _arena.Allocate(Unsafe.SizeOf<T>() * newCapacity, _priority, _hints);
            var newFreeHandle = _arena.Allocate(sizeof(int) * newCapacity, _priority, _hints);
            var newRefHandle = _arena.Allocate(sizeof(int) * newCapacity, _priority, _hints);

            var oldData = _arena.GetSpan<T>(_dataHandle, _capacity);
            var newData = _arena.GetSpan<T>(newDataHandle, newCapacity);
            oldData.CopyTo(newData);

            var oldFree = _arena.GetSpan<int>(_freeIndicesHandle, _capacity);
            var newFree = _arena.GetSpan<int>(newFreeHandle, newCapacity);
            oldFree.Slice(0, _freeCount).CopyTo(newFree);

            var oldRef = _arena.GetSpan<int>(_refCountsHandle, _capacity);
            var newRef = _arena.GetSpan<int>(newRefHandle, newCapacity);
            oldRef.CopyTo(newRef);

            _arena.Free(_dataHandle);
            _arena.Free(_freeIndicesHandle);
            _arena.Free(_refCountsHandle);

            _dataHandle = newDataHandle;
            _freeIndicesHandle = newFreeHandle;
            _refCountsHandle = newRefHandle;

            var oldCap = _capacity;
            _capacity = newCapacity;
            InitializeFreeIndices(oldCap, _capacity);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (!_dataHandle.IsInvalid) _arena.Free(_dataHandle);
            if (!_freeIndicesHandle.IsInvalid) _arena.Free(_freeIndicesHandle);
            if (!_refCountsHandle.IsInvalid) _arena.Free(_refCountsHandle);

            _dataHandle = default;
            _freeIndicesHandle = default;
            _refCountsHandle = default;

            _lookup.Clear();
        }
    }
}