/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     MemoryArenaPrototype.Core
 * FILE:        AllocationStrategy.cs
 * PURPOSE:     Defines the search algorithm used to locate available unmanaged space within the free-lists.
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

namespace MemoryManager.Core
{
    /// <summary>
    /// Defines the search algorithm used to locate available unmanaged space within the free-lists.
    /// </summary>
    public enum AllocationStrategy : byte
    {
        /// <summary>
        /// Blazing fast execution. Grabs the very first block that fits. Ideal for uniform/homogeneous lifetimes.
        /// </summary>
        FirstFit,

        /// <summary>
        /// Minimizes fragmentation. Scans the free-list to find the absolute smallest hole that fits the payload.
        /// </summary>
        BestFit
    }
}