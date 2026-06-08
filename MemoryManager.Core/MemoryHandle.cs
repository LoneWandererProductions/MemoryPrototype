/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     MemoryArenaPrototype.Core
 * FILE:        MemoryHandle.cs
 * PURPOSE:     Represents a handle to a memory allocation managed by an IMemoryLane.
 *              Encapsulates an identifier and provides access to the allocated memory.
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

namespace MemoryManager.Core
{
    /// <summary>
    ///     Represents a lightweight, immutable handle for a memory allocation within a memory lane.
    ///     Provides access to the underlying memory pointer and validity checks.
    /// </summary>
    public readonly struct MemoryHandle : IEquatable<MemoryHandle>
    {
        /// <summary>
        ///     Gets the unique identifier for this handle.
        ///     Must be a signed integer.
        /// </summary>
        public int Id { get; }

        /// <summary>
        /// Gets the current memory lane.
        /// </summary>
        /// <value>
        /// The lane controlling this memory handle.
        /// </value>
        public IMemoryLane? Lane => _lane;

        /// <summary>
        /// The version
        /// </summary>
        public readonly uint Version;

        /// <summary>
        ///     The lane
        /// </summary>
        private readonly IMemoryLane? _lane;

        /// <summary>
        /// Initializes a new instance of the <see cref="MemoryHandle" /> struct with the specified ID and memory lane.
        /// </summary>
        /// <param name="id">The allocation identifier.</param>
        /// <param name="version">The version.</param>
        /// <param name="lane">The memory lane managing the allocation.</param>
        public MemoryHandle(int id, uint version, IMemoryLane? lane)
        {
            Id = id;
            Version = version;
            _lane = lane;
        }

        /// <summary>
        /// Resolves this handle to a raw memory pointer.
        /// </summary>
        /// <returns>
        /// An <see cref="nint" /> pointing to the allocated memory block.
        /// </returns>
        public nint GetPointer()
        {
            return _lane.Resolve(this);
        }

        /// <summary>
        ///     Gets a value indicating whether this handle is invalid.
        ///     An invalid handle is one with a non-positive ID or no associated memory lane.
        /// </summary>
        public bool IsInvalid => _lane == null || Id == 0;

        /// <summary>
        /// Indicates whether the current object is equal to another object of the same type.
        /// </summary>
        /// <param name="other">An object to compare with this object.</param>
        /// <returns>
        ///   <see langword="true" /> if the current object is equal to the <paramref name="other" /> parameter; otherwise, <see langword="false" />.
        /// </returns>
        public bool Equals(MemoryHandle other) => Id == other.Id && Version == other.Version;

        /// <summary>
        /// Indicates whether this instance and a specified object are equal.
        /// </summary>
        /// <param name="obj">The object to compare with the current instance.</param>
        /// <returns>
        ///   <see langword="true" /> if <paramref name="obj" /> and this instance are the same type and represent the same value; otherwise, <see langword="false" />.
        /// </returns>
        public override bool Equals(object? obj) => obj is MemoryHandle other && Equals(other);

        /// <summary>
        /// Returns a hash code for this instance.
        /// </summary>
        /// <returns>
        /// A hash code for this instance, suitable for use in hashing algorithms and data structures like a hash table.
        /// </returns>
        public override int GetHashCode() => HashCode.Combine(Id, Version);
    }
}