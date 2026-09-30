/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     MemoryManager.Types
 * FILE:        ArenaSparseSet.cs
 * PURPOSE:     A cache-coherent sparse set for Entity-Component mapping and fast O(1) removals.
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

using System.Runtime.CompilerServices;
using MemoryManager.Core;

namespace MemoryManager.Types
{
    /// <inheritdoc />
    /// <summary>
    /// A high-performance unmanaged Sparse Set providing O(1) lookups and contiguous iteration.
    /// </summary>
    /// <typeparam name="T">The unmanaged component data type.</typeparam>
    public sealed class ArenaSparseSet<T> : IDisposable where T : unmanaged
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
        /// The sparse handle
        /// </summary>
        private MemoryHandle _sparseHandle;

        /// <summary>
        /// The dense handle
        /// </summary>
        private MemoryHandle _denseHandle;

        /// <summary>
        /// The values handle
        /// </summary>
        private MemoryHandle _valuesHandle;

        /// <summary>
        /// The sparse capacity
        /// </summary>
        private int _sparseCapacity;

        /// <summary>
        /// The dense capacity
        /// </summary>
        private int _denseCapacity;

        /// <summary>
        /// The count
        /// </summary>
        private int _count;

        /// <summary>
        /// Gets the count.
        /// </summary>
        /// <value>
        /// The count.
        /// </value>
        public int Count => _count;

        /// <summary>
        /// Initializes a new instance of the <see cref="ArenaSparseSet{T}"/> class.
        /// </summary>
        /// <param name="arena">The arena.</param>
        /// <param name="initialMaxEntityId">The initial maximum entity identifier.</param>
        /// <param name="initialDenseCapacity">The initial dense capacity.</param>
        /// <param name="priority">The priority.</param>
        /// <param name="hints">The hints.</param>
        /// <exception cref="System.ArgumentNullException">arena - Cannot instantiate {GetType().Name} without a valid allocator.</exception>
        public ArenaSparseSet(IMemoryAllocator arena, int initialMaxEntityId = 512, int initialDenseCapacity = 64,
            AllocationPriority priority = AllocationPriority.Normal, AllocationHints hints = AllocationHints.None)
        {
            _arena = arena ?? throw new ArgumentNullException(nameof(arena), $"Cannot instantiate {GetType().Name} without a valid allocator.");

            _sparseCapacity = initialMaxEntityId <= 0 ? 512 : initialMaxEntityId;
            _denseCapacity = initialDenseCapacity <= 0 ? 64 : initialDenseCapacity;
            _priority = priority;
            _hints = hints;

            _sparseHandle = _arena.Allocate(sizeof(int) * _sparseCapacity, _priority, _hints);
            _denseHandle = _arena.Allocate(sizeof(int) * _denseCapacity, _priority, _hints);
            _valuesHandle = _arena.Allocate(Unsafe.SizeOf<T>() * _denseCapacity, _priority, _hints);

            // Initialize sparse array with invalid sentinel markers (-1)
            var sparseSpan = _arena.GetSpan<int>(_sparseHandle, _sparseCapacity);
            sparseSpan.Fill(-1);
        }

        /// <summary>
        /// Adds the specified entity identifier.
        /// </summary>
        /// <param name="entityId">The entity identifier.</param>
        /// <param name="component">The component.</param>
        /// <exception cref="System.ArgumentOutOfRangeException">entityId</exception>
        public void Add(int entityId, in T component)
        {
            if (entityId < 0) throw new ArgumentOutOfRangeException(nameof(entityId));

            EnsureSparseCapacity(entityId + 1);

            if (Contains(entityId))
            {
                // Entity already exists, update component value directly
                var sparse = _arena.GetSpan<int>(_sparseHandle, _sparseCapacity);
                var values = _arena.GetSpan<T>(_valuesHandle, _denseCapacity);
                values[sparse[entityId]] = component;
                return;
            }

            EnsureDenseCapacity(_count + 1);

            var sparseSpan = _arena.GetSpan<int>(_sparseHandle, _sparseCapacity);
            var denseSpan = _arena.GetSpan<int>(_denseHandle, _denseCapacity);
            var valuesSpan = _arena.GetSpan<T>(_valuesHandle, _denseCapacity);

            int denseIndex = _count++;
            sparseSpan[entityId] = denseIndex;
            denseSpan[denseIndex] = entityId;
            valuesSpan[denseIndex] = component;
        }

        /// <summary>
        /// Determines whether this instance contains the object.
        /// </summary>
        /// <param name="entityId">The entity identifier.</param>
        /// <returns>
        ///   <c>true</c> if [contains] [the specified entity identifier]; otherwise, <c>false</c>.
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(int entityId)
        {
            if (entityId < 0 || entityId >= _sparseCapacity) return false;

            var sparse = _arena.GetSpan<int>(_sparseHandle, _sparseCapacity);
            int denseIndex = sparse[entityId];

            if (denseIndex < 0 || denseIndex >= _count) return false;

            var dense = _arena.GetSpan<int>(_denseHandle, _denseCapacity);
            return dense[denseIndex] == entityId;
        }

        /// <summary>
        /// Removes the specified entity identifier.
        /// </summary>
        /// <param name="entityId">The entity identifier.</param>
        /// <returns>Remove element <true>, else <false> if nothing was removed.</returns>
        public bool Remove(int entityId)
        {
            if (!Contains(entityId)) return false;

            var sparse = _arena.GetSpan<int>(_sparseHandle, _sparseCapacity);
            var dense = _arena.GetSpan<int>(_denseHandle, _denseCapacity);
            var values = _arena.GetSpan<T>(_valuesHandle, _denseCapacity);

            int removedDenseIndex = sparse[entityId];
            int lastDenseIndex = --_count;
            int lastEntityId = dense[lastDenseIndex];

            // Swap-Back Technique: Move the last dense element into the deleted slot to preserve tight packing
            dense[removedDenseIndex] = lastEntityId;
            values[removedDenseIndex] = values[lastDenseIndex];
            sparse[lastEntityId] = removedDenseIndex;

            sparse[entityId] = -1; // Invalidate sparse entry
            return true;
        }

        /// <summary>
        /// Gets the specified entity identifier.
        /// </summary>
        /// <param name="entityId">The entity identifier.</param>
        /// <returns>et the Element with the id.</returns>
        /// <exception cref="System.InvalidOperationException">Entity ID {entityId} does not exist in the SparseSet.</exception>
        public ref T Get(int entityId)
        {
            if (!Contains(entityId))
                throw new InvalidOperationException($"Entity ID {entityId} does not exist in the SparseSet.");

            var sparse = _arena.GetSpan<int>(_sparseHandle, _sparseCapacity);
            var values = _arena.GetSpan<T>(_valuesHandle, _denseCapacity);
            return ref values[sparse[entityId]];
        }

        /// <summary>
        /// Returns the packed active component values as a continuous Span for zero-GC, L1/L2 cache-friendly iteration.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span<T> AsSpan()
        {
            return _count == 0 ? Span<T>.Empty : _arena.GetSpan<T>(_valuesHandle, _denseCapacity).Slice(0, _count);
        }

        /// <summary>
        /// Ensures the sparse capacity.
        /// </summary>
        /// <param name="requiredCapacity">The required capacity.</param>
        private void EnsureSparseCapacity(int requiredCapacity)
        {
            if (requiredCapacity <= _sparseCapacity) return;

            int newCapacity = Math.Max(_sparseCapacity * 2, requiredCapacity);
            var newHandle = _arena.Allocate(sizeof(int) * newCapacity, _priority, _hints);

            var oldSpan = _arena.GetSpan<int>(_sparseHandle, _sparseCapacity);
            var newSpan = _arena.GetSpan<int>(newHandle, newCapacity);

            oldSpan.CopyTo(newSpan);
            newSpan.Slice(_sparseCapacity).Fill(-1); // Fill newly expanded region with invalid sentinels

            _arena.Free(_sparseHandle);
            _sparseHandle = newHandle;
            _sparseCapacity = newCapacity;
        }

        /// <summary>
        /// Ensures the dense capacity.
        /// </summary>
        /// <param name="requiredCapacity">The required capacity.</param>
        private void EnsureDenseCapacity(int requiredCapacity)
        {
            if (requiredCapacity <= _denseCapacity) return;

            int newCapacity = _denseCapacity * 2;
            var newDenseHandle = _arena.Allocate(sizeof(int) * newCapacity, _priority, _hints);
            var newValuesHandle = _arena.Allocate(Unsafe.SizeOf<T>() * newCapacity, _priority, _hints);

            var oldDense = _arena.GetSpan<int>(_denseHandle, _denseCapacity);
            var newDense = _arena.GetSpan<int>(newDenseHandle, newCapacity);
            oldDense.CopyTo(newDense);

            var oldValues = _arena.GetSpan<T>(_valuesHandle, _denseCapacity);
            var newValues = _arena.GetSpan<T>(newValuesHandle, newCapacity);
            oldValues.CopyTo(newValues);

            _arena.Free(_denseHandle);
            _arena.Free(_valuesHandle);

            _denseHandle = newDenseHandle;
            _valuesHandle = newValuesHandle;
            _denseCapacity = newCapacity;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (!_sparseHandle.IsInvalid) _arena.Free(_sparseHandle);
            if (!_denseHandle.IsInvalid) _arena.Free(_denseHandle);
            if (!_valuesHandle.IsInvalid) _arena.Free(_valuesHandle);

            _sparseHandle = default;
            _denseHandle = default;
            _valuesHandle = default;
            _count = 0;
        }
    }
}