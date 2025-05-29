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

            public string? DebugName { get; set; }
            public int AllocationFrame { get; set; }
            public int LastAccessFrame { get; set; }

            public AllocationPriority Priority { get; set; }
            public AllocationHints Hints { get; set; }
        }
    }
}