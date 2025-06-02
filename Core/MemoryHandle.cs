/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     Core
 * FILE:        MemoryHandle.cs
 * PURPOSE:     Represents a handle to a memory allocation managed by an IMemoryLane.
 *              Encapsulates an identifier and provides access to the allocated memory.
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

using System;

namespace Core
{
    /// <summary>
    ///     Represents a lightweight, immutable handle for a memory allocation within a memory lane.
    ///     Provides access to the underlying memory pointer and validity checks.
    /// </summary>
    public readonly struct MemoryHandle
    {
        /// <summary>
        ///     Gets the unique identifier for this handle.
        ///     Must be a signed integer.
        /// </summary>
        public int Id { get; }

        /// <summary>
        ///     The lane
        /// </summary>
        private readonly IMemoryLane _lane;

        /// <summary>
        ///     Initializes a new instance of the <see cref="MemoryHandle" /> struct with the specified ID and memory lane.
        /// </summary>
        /// <param name="id">The allocation identifier.</param>
        /// <param name="lane">The memory lane managing the allocation.</param>
        public MemoryHandle(int id, IMemoryLane lane)
        {
            Id = id;
            _lane = lane;
        }

        /// <summary>
        ///     Resolves this handle to a raw memory pointer.
        /// </summary>
        /// <returns>An <see cref="IntPtr" /> pointing to the allocated memory block.</returns>
        public IntPtr GetPointer()
        {
            return _lane.Resolve(this);
        }

        /// <summary>
        ///     Gets a value indicating whether this handle is invalid.
        ///     An invalid handle is one with a non-positive ID or no associated memory lane.
        /// </summary>
        public bool IsInvalid => _lane == null;
    }
}