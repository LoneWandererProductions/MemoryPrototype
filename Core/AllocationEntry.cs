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
        }
    }
}