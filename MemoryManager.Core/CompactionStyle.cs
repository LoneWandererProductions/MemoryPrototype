/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     MemoryArenaPrototype.Core
 * FILE:        CompactionStyle.cs
 * PURPOSE:     Defines the compaction styles for memory management strategies.
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

namespace MemoryManager.Core
{
    public enum CompactionStyle
    {
        Full,        // The current "Nuclear Reset" (Default for SlowLane)
        GoodEnough,  // Stops the moment a target gap is opened
        None         // Defragments strictly via free-list coalescing
    }
}
