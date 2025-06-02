/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     Core
 * FILE:        AllocationEntry.cs
 * PURPOSE:     Represents a single allocation record within the memory arena, 
 *              tracking offset, size, handle identity, and metadata for debugging and management.
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

#nullable enable
namespace Core
{
    namespace MemoryArenaPrototype.Core
    {
        /// <summary>
        /// Represents an entry for an allocated memory block inside the MemoryArena.
        /// Tracks location, size, identity, and additional metadata for lifetime and debugging purposes.
        /// </summary>
        public struct AllocationEntry
        {
            /// <summary>
            /// The offset from the start of the arena's memory block where this allocation begins.
            /// </summary>
            public int Offset { get; set; }

            /// <summary>
            /// The size in bytes of this allocated block.
            /// </summary>
            public int Size { get; set; }

            /// <summary>
            /// A unique identifier handle for this allocation.
            /// </summary>
            public int HandleId { get; init; }

            /// <summary>
            /// Indicates whether this allocation entry is a stub placeholder (e.g., for deferred allocation or redirection).
            /// </summary>
            public bool IsStub { get; set; }

            /// <summary>
            /// If this allocation is redirected, references the handle to which it redirects.
            /// </summary>
            public MemoryHandle? RedirectTo { get; set; }

            /// <summary>
            /// Optional debug name or label for easier identification of this allocation during debugging.
            /// </summary>
            public string? DebugName { get; init; }

            /// <summary>
            /// The frame or timestamp when this allocation was initially made.
            /// </summary>
            public int AllocationFrame { get; set; }

            /// <summary>
            /// The frame or timestamp when this allocation was last accessed.
            /// Useful for tracking usage patterns and implementing eviction or caching policies.
            /// </summary>
            public int LastAccessFrame { get; set; }

            /// <summary>
            /// Priority level of this allocation which can influence eviction or memory management policies.
            /// </summary>
            public AllocationPriority Priority { get; init; }

            /// <summary>
            /// Additional hints or flags for this allocation that can affect behavior in the memory arena.
            /// </summary>
            public AllocationHints Hints { get; init; }
        }
    }
}
