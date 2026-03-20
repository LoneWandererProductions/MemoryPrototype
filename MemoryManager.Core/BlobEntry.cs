/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     MemoryManager.Core
 * FILE:        BlobEntry.cs
 * PURPOSE:     Your file purpose here
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

namespace MemoryManager.Core
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