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
        FrameCritical = 1 << 0,
        Cold = 1 << 1,
        Old = 1 << 2,
        Evictable = Cold | Old
    }
}