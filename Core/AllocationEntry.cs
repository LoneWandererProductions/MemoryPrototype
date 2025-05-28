namespace Core
{
    namespace MemoryArenaPrototype.Core
    {
        public sealed class AllocationEntry
        {
            public int Offset { get; set; }
            public int Size { get; init; }
            public int HandleId { get; init; }
            public bool IsStub { get; set; }
            public MemoryHandle? RedirectTo { get; set; }
        }
    }
}