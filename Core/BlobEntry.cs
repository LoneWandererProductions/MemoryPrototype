/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     Core.MemoryArenaPrototype.Core
 * FILE:        BlobEntry.cs
 * PURPOSE:     Your file purpose here
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

namespace Core
{
    // A separate struct for tracking Blob metadata
    public struct BlobEntry
    {
        public int Id;
        public int Offset;
        public int Size;
        public int AllocationFrame;
    }
}