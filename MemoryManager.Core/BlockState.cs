/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     MemoryArenaPrototype.Core
 * FILE:        BlockState.cs
 * PURPOSE:     Status of the memory block
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

namespace MemoryManager.Core
{
    /// <summary>
    /// States of the memory blocks.
    /// </summary>
    public enum BlockState
    {
        Free,
        Allocated,
        Deleted,
        Cold,
        Hot,
        Aging,
        Protected
    }
}