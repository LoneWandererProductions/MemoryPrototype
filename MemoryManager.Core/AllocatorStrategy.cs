/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     MemoryArenaPrototype.Core
 * FILE:        AllocatorStrategy.cs
 * PURPOSE:     We can Swap the Fastlane with BumpLane. BumPlane is faster but has issues with longer living data and has a need for more frequent cleanups.
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

namespace MemoryManager.Core
{
    /// <summary>
    /// Enum for possible allocation strategies. This is used to determine which lane to use for a given allocation request, and can be configured globally or per-allocation.
    /// </summary>
    public enum AllocatorStrategy
    {
        /// <summary>
        /// The free list
        ///  Safe, reuses holes, handles chaotic lifespans
        /// </summary>
        FreeList = 0,

        /// <summary>
        /// The linear bump
        /// Absolute maximum speed, requires frequent compactions
        /// </summary>
        LinearBump = 1,

        /// <summary>
        /// The slab lane,
        /// Uses fixed-size bins for small allocations, excellent for uniform patterns, but can lead to fragmentation if misused.
        /// </summary>
        Slab = 2
    }
}