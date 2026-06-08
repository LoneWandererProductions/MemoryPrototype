/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     MemoryArenaPrototype.Core
 * FILE:        AllocationPriority.cs
 * PURPOSE:     Priority of the Allocation
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

namespace MemoryManager.Core
{
    /// <summary>
    /// Priority of the Allocation, this can be used to determine the order in which allocations are made when multiple allocations are requested at the same time. This can be useful for optimizing performance and ensuring that critical allocations are made first.
    /// </summary>
    public enum AllocationPriority
    {
        /// <summary>
        /// The critical Priority, these allocations are the most important and should be allocated first. They are likely to be used for critical game systems or performance-sensitive code.
        /// </summary>
        Critical = 0,

        /// <summary>
        /// The normal, Priority, these allocations are important but not as critical as the critical ones. They may be used for general game logic or less performance-sensitive code.
        /// </summary>
        Normal = 1,

        /// <summary>
        /// The low, Priority, these allocations are the least important and should be allocated last. They may be used for non-essential game features or background tasks that can tolerate delays in allocation.
        /// </summary>
        Low = 2
    }
}