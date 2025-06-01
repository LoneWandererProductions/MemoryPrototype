/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     Core
 * FILE:        AllocationEntry.cs
 * PURPOSE:     Your file purpose here
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

namespace Core
{
    namespace MemoryArenaPrototype.Core
    {
        public struct AllocationEntry
        {
            public int Offset { get; set; }
            public int Size { get; set; }
            public int HandleId { get; set; }

            public bool IsStub { get; set; }
            public MemoryHandle? RedirectTo { get; set; }

            /// <summary>
            /// Gets or sets the name of the debug.
            /// </summary>
            /// <value>
            /// The name of the debug.
            /// </value>
            public string? DebugName { get; set; }
            public int AllocationFrame { get; set; }
            public int LastAccessFrame { get; set; }

            public AllocationPriority Priority { get; set; }
            public AllocationHints Hints { get; set; }
        }
    }
}