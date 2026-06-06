/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     MemoryArenaPrototype.Core
 * FILE:        AllocationHints.cs
 * PURPOSE:     Hints for the Memory Manager about the allocated data.
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

namespace MemoryManager.Core
{
    /// <summary>
    /// Enum flag of the Data.
    /// </summary>
    [Flags]
    public enum AllocationHints
    {
        None = 0,

        /// <summary>
        /// The frame critical flag indicates that the allocated data is critical for the current frame and should be prioritized for allocation in the fast lane.
        /// </summary>
        FrameCritical = 1 << 0,
        Cold = 1 << 1,
        Old = 1 << 2,

        /// <summary>
        /// The evictable flag indicates that the allocated data can be evicted from the fast lane when memory pressure occurs.
        /// </summary>
        Evictable = Cold | Old
    }
}