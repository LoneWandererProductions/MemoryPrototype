/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     MemoryManager.Types
 * FILE:        ArenaRent.cs
 * PURPOSE:     A lightweight RAII scope wrapper for renting transient unmanaged spans from IMemoryAllocator.
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

using System.Runtime.CompilerServices;
using MemoryManager.Core;

namespace MemoryManager.Types
{
    /// <summary>
    /// A stack-scoped RAII wrapper for renting a temporary <see cref="Span{T}"/> 
    /// from an <see cref="IMemoryAllocator"/> and returning it automatically upon disposal.
    /// </summary>
    /// <typeparam name="T">The unmanaged element type.</typeparam>
    public ref struct ArenaRent<T> where T : unmanaged
    {
        /// <summary>
        /// The arena
        /// </summary>
        private readonly IMemoryAllocator? _arena;

        /// <summary>
        /// The handle
        /// </summary>
        private MemoryHandle _handle;

        /// <summary>
        /// Gets the rented memory slice as a <see cref="Span{T}" />.
        /// </summary>
        /// <value>
        /// The span.
        /// </value>
        public Span<T> Span { get; }

        /// <summary>
        /// Gets the total number of rented elements.
        /// </summary>
        public int Length => Span.Length;

        /// <summary>
        /// Rents a block of unmanaged memory for temporary use.
        /// </summary>
        /// <param name="arena">The arena.</param>
        /// <param name="count">The count.</param>
        /// <param name="priority">The priority.</param>
        /// <param name="hints">The hints.</param>
        /// <exception cref="System.ArgumentNullException">arena</exception>
        /// <exception cref="System.ArgumentOutOfRangeException">count - Allocation count cannot be negative.</exception>
        public ArenaRent(
            IMemoryAllocator arena,
            int count,
            AllocationPriority priority = AllocationPriority.Critical,
            AllocationHints hints = AllocationHints.FrameCritical | AllocationHints.NoSpill)
        {
            _arena = arena ?? throw new ArgumentNullException(nameof(arena));

            if (count < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count), "Allocation count cannot be negative.");
            }

            if (count == 0)
            {
                _handle = default;
                Span = Span<T>.Empty;
                return;
            }

            _handle = _arena.Allocate(Unsafe.SizeOf<T>() * count, priority, hints);
            Span = _arena.GetSpan<T>(_handle, count);
        }

        /// <summary>
        /// Gets a reference to the element at the specified index.
        /// </summary>
        /// <value>
        /// The <see cref="T"/>.
        /// </value>
        /// <param name="index">The index.</param>
        /// <returns>Data at index.</returns>
        public ref T this[int index] => ref Span[index];

        /// <summary>
        /// Releases the rented memory back to the backing allocator.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Dispose()
        {
            if (_handle.IsInvalid) return;

            _arena.Free(_handle);
            _handle = default;
        }
    }
}