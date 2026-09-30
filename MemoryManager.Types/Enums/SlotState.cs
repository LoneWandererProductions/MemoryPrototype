/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     MemoryManager.Types.Enums
 * FILE:        SlotState.cs
 * PURPOSE:     Slot Type for MemoryArena
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

namespace MemoryManager.Types.Enums
{
    /// <summary>
    /// Enum type for MemoryArena
    /// </summary>
    internal enum SlotState : byte
    {
        Free = 0,
        Occupied = 1,
        Tombstone = 2
    }
}