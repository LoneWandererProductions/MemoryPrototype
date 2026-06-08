/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     MemoryManager.Core
 * FILE:        BlobEntry.cs
 * PURPOSE:     A struct representing metadata for a blob allocation within the memory arena, tracking its identifier, offset, size, allocation frame, and version for management and debugging purposes.
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

namespace MemoryManager.Core
{
    // A separate struct for tracking Blob metadata
    public struct BlobEntry
    {
        /// <summary>
        /// The identifier
        /// </summary>
        public int Id;

        /// <summary>
        /// The offset
        /// </summary>
        public int Offset;

        /// <summary>
        /// The size
        /// </summary>
        public int Size;

        /// <summary>
        /// The allocation frame
        /// </summary>
        public int AllocationFrame;

        /// <summary>
        /// The version
        /// </summary>
        public byte Version;
    }
}