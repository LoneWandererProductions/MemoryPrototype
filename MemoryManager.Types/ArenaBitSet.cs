/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     MemoryManager.Types
 * FILE:        ArenaBitSet.cs
 * PURPOSE:     A high-performance, zero-allocation bitset using 64-bit word chunks.
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

using System.Runtime.CompilerServices;
using MemoryManager.Core;

namespace MemoryManager.Types
{
    /// <summary>
    /// A high-performance, zero-allocation bitset using 64-bit word chunks backed by an <see cref="IMemoryAllocator"/>.
    /// </summary>
    public sealed class ArenaBitSet : IDisposable
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
        /// The ulong capacity
        /// </summary>
        private int _ulongCapacity;

        /// <summary>
        /// The bit capacity
        /// </summary>
        private int _bitCapacity;

        /// <summary>
        /// Gets the bit capacity.
        /// </summary>
        /// <value>
        /// The bit capacity.
        /// </value>
        public int BitCapacity => _bitCapacity;

        /// <summary>
        /// Initializes a new instance of the <see cref="ArenaBitSet" /> class.
        /// </summary>
        /// <param name="arena">The arena.</param>
        /// <param name="initialBitCapacity">The initial bit capacity.</param>
        /// <param name="priority">The priority.</param>
        /// <param name="hints">The hints.</param>
        /// <exception cref="System.ArgumentNullException">arena - Cannot instantiate ArenaBitSet without a valid allocator.</exception>
        public ArenaBitSet(IMemoryAllocator? arena, int initialBitCapacity = 64,
            AllocationPriority priority = AllocationPriority.Normal, AllocationHints hints = AllocationHints.None)
        {
            _arena = arena ?? throw new ArgumentNullException(nameof(arena), $"Cannot instantiate {GetType().Name} without a valid allocator.");

            if (initialBitCapacity <= 0) initialBitCapacity = 64;

            _arena = arena;
            _priority = priority;
            _hints = hints;
            _bitCapacity = initialBitCapacity;
            _ulongCapacity = (_bitCapacity + 63) / 64;
            _handle = _arena.Allocate(sizeof(ulong) * _ulongCapacity, _priority, _hints);
            Clear();
        }

        /// <summary>
        /// Sets the specified index.
        /// </summary>
        /// <param name="index">The index.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Set(int index)
        {
            EnsureCapacity(index + 1);
            var span = _arena.GetSpan<ulong>(_handle, _ulongCapacity);
            span[index >> 6] |= (1UL << (index & 63));
        }

        /// <summary>
        /// Unsets the specified index.
        /// </summary>
        /// <param name="index">The index.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Unset(int index)
        {
            if (index >= _bitCapacity) return;
            var span = _arena.GetSpan<ulong>(_handle, _ulongCapacity);
            span[index >> 6] &= ~(1UL << (index & 63));
        }

        /// <summary>
        /// Gets the specified index.
        /// </summary>
        /// <param name="index">The index.</param>
        /// <returns>Get Data at specified index and true, if it is set, false, if not.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Get(int index)
        {
            if (index >= _bitCapacity) return false;
            var span = _arena.GetSpan<ulong>(_handle, _ulongCapacity);
            return (span[index >> 6] & (1UL << (index & 63))) != 0;
        }

        /// <summary>
        /// Clears this instance.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear()
        {
            var span = _arena.GetSpan<ulong>(_handle, _ulongCapacity);
            span.Clear();
        }

        /// <summary>
        /// Contents as span.
        /// </summary>
        /// <returns>Data as Span.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span<ulong> AsSpan() => _arena.GetSpan<ulong>(_handle, _ulongCapacity);

        /// <summary>
        /// Ensures the capacity.
        /// </summary>
        /// <param name="requiredBits">The required bits.</param>
        private void EnsureCapacity(int requiredBits)
        {
            if (requiredBits <= _bitCapacity) return;

            int newBitCapacity = Math.Max(_bitCapacity * 2, requiredBits);
            int newUlongCapacity = (newBitCapacity + 63) / 64;
            var newHandle = _arena.Allocate(sizeof(ulong) * newUlongCapacity, _priority, _hints);

            var oldSpan = _arena.GetSpan<ulong>(_handle, _ulongCapacity);
            var newSpan = _arena.GetSpan<ulong>(newHandle, newUlongCapacity);

            oldSpan.CopyTo(newSpan);
            newSpan.Slice(_ulongCapacity).Clear();

            _arena.Free(_handle);
            _handle = newHandle;
            _bitCapacity = newBitCapacity;
            _ulongCapacity = newUlongCapacity;
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