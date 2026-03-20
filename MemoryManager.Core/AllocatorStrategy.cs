/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     MemoryArenaPrototype.Core
 * FILE:        AllocatorStrategy.cs
 * PURPOSE:     We can Swap the Fastlane with BumpLane. BumPlane is faster but has issues with longer living data and has a need for more frequent cleanups.
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

namespace MemoryManager.Core
{
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
    }
}
