/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     Core
 * FILE:        IMemoryLane.cs
 * PURPOSE:     Defines the interface for memory lanes managing allocations within the MemoryArena.
 *              Provides methods for allocation, deallocation, access, compaction, and diagnostics.
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

#nullable enable
using System;
using Core.MemoryArenaPrototype.Core;

namespace Core
{
    /// <summary>
    /// Interface representing a memory lane, a segment or strategy for managing memory allocations.
    /// Memory lanes handle allocation, freeing, and access to memory blocks with different policies.
    /// </summary>
    public interface IMemoryLane
    {
        /// <summary>
        /// Allocates a memory block of the specified size with optional priority, hints, and debugging info.
        /// </summary>
        /// <param name="size">The size in bytes to allocate.</param>
        /// <param name="priority">Priority influencing allocation or eviction behavior.</param>
        /// <param name="hints">Additional hints that modify allocation behavior.</param>
        /// <param name="debugName">Optional debug name for identifying the allocation.</param>
        /// <param name="currentFrame">Current frame or timestamp for tracking allocation timing.</param>
        /// <returns>A handle representing the allocated memory block.</returns>
        MemoryHandle Allocate(
            int size,
            AllocationPriority priority = AllocationPriority.Normal,
            AllocationHints hints = AllocationHints.None,
            string? debugName = null,
            int currentFrame = 0);

        /// <summary>
        /// Resolves a memory handle to a raw pointer (IntPtr) to access the allocated memory.
        /// </summary>
        /// <param name="handle">The handle identifying the allocated memory block.</param>
        /// <returns>A pointer to the start of the allocated memory.</returns>
        IntPtr Resolve(MemoryHandle handle);

        /// <summary>
        /// Gets the size of the allocation identified by the specified handle.
        /// </summary>
        /// <param name="fastHandle">The handle referencing the allocation.</param>
        /// <returns>The size in bytes of the allocation.</returns>
        int GetAllocationSize(MemoryHandle fastHandle);

        /// <summary>
        /// Retrieves the full allocation entry metadata for a given handle.
        /// </summary>
        /// <param name="handle">The handle identifying the allocation.</param>
        /// <returns>The allocation entry associated with the handle.</returns>
        AllocationEntry GetEntry(MemoryHandle handle);

        /// <summary>
        /// Determines whether this memory lane contains the specified handle.
        /// </summary>
        /// <param name="handle">The handle to check.</param>
        /// <returns><c>true</c> if the handle belongs to this lane; otherwise, <c>false</c>.</returns>
        bool HasHandle(MemoryHandle handle);

        /// <summary>
        /// Frees the allocation associated with the given handle.
        /// </summary>
        /// <param name="handle">The handle referencing the allocation to free.</param>
        void Free(MemoryHandle handle);

        /// <summary>
        /// Compacts the memory lane by consolidating free space and possibly rearranging allocations.
        /// Useful to reduce fragmentation and improve allocation efficiency.
        /// </summary>
        void Compact();

        /// <summary>
        /// Checks if the memory lane can allocate a block of the specified size currently.
        /// </summary>
        /// <param name="size">The size in bytes to check for allocation feasibility.</param>
        /// <returns><c>true</c> if allocation is possible; otherwise, <c>false</c>.</returns>
        bool CanAllocate(int size);

        /// <summary>
        /// Provides a debug string dump describing the internal state of the memory lane.
        /// Useful for diagnostics and debugging allocation behavior.
        /// </summary>
        /// <returns>A string representation of the current memory lane state.</returns>
        string DebugDump();
    }
}
