/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     MemoryArenaPrototype.Core
 * FILE:        AllocationPriority.cs
 * PURPOSE:     Priority of the Allocation
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

namespace MemoryManager.Core
{
    public enum AllocationPriority
    {
        Critical = 0,
        Normal = 1,
        Low = 2
    }
}