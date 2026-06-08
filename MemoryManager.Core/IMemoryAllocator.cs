/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     MemoryArenaPrototype.Core
 * FILE:        IMemoryAllocator.cs
 * PURPOSE:     Defines the interface for memory allocators managing allocations within the MemoryArena.
 *              Provides methods for allocation, deallocation, access, compaction, and diagnostics.
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

using System.Diagnostics;

namespace MemoryManager.Core
{
    /// <summary>
    /// Interface representing a memory allocator, responsible for managing memory allocations, deallocations, and access within a memory arena.
    /// </summary>
    public interface IMemoryAllocator
    {
        /// <summary>
        /// Allocates the specified size.
        /// </summary>
        /// <param name="size">The size.</param>
        /// <param name="priority">The priority.</param>
        /// <param name="hints">The hints.</param>
        /// <param name="debugName">Name of the debug.</param>
        /// <param name="currentFrame">The current frame.</param>
        /// <returns>A <see cref="MemoryHandle"/> representing the allocated memory.</returns>
        MemoryHandle Allocate(int size, AllocationPriority priority = AllocationPriority.Normal, AllocationHints hints = AllocationHints.None, string? debugName = null, int currentFrame = 0);

        /// <summary>
        /// Frees the specified handle.
        /// </summary>
        /// <param name="handle">The handle.</param>
        void Free(MemoryHandle handle);

        /// <summary>
        /// Resolves the specified handle.
        /// </summary>
        /// <param name="handle">The handle.</param>
        /// <returns>A <see cref="nint"/> pointing to the allocated memory block.</returns>
        nint Resolve(MemoryHandle handle);

        /// <summary>
        /// Bulks the set.
        /// </summary>
        /// <typeparam name="T">The type of the elements.</typeparam>
        /// <param name="handle">The handle.</param>
        /// <param name="source">The source.</param>
        void BulkSet<T>(MemoryHandle handle, ReadOnlySpan<T> source) where T : unmanaged;

        /// <summary>
        /// Gets the entry.
        /// </summary>
        /// <param name="handle">The handle.</param>
        /// <returns>An <see cref="AllocationEntry"/> representing the allocation entry.</returns>
        AllocationEntry GetEntry(MemoryHandle handle);

        /// <summary>
        /// Generates a highly detailed string snapshot of the internal clockwork and moving parts.
        /// </summary>
        /// <returns>A string representing the internal state of the allocator.</returns>
        string DebugDump();

        /// <summary>
        /// Default Interface Method: Automatically pushes the DebugDump straight to the system Trace.
        /// </summary>
        public void LogDump();
    }
}