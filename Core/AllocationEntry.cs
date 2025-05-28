namespace Core
{
    namespace MemoryArenaPrototype.Core
    {
        public sealed class AllocationEntry
        {
            public int Offset { get; set; }
            public int Size { get; set; }
            public int HandleId { get; set; }
            public bool IsStub { get; set; } = false;
            public MemoryHandle? RedirectTo { get; set; } = null;
        }
    }
}