using System;

namespace Core
{
    public readonly struct MemoryHandle
    {
        public int Id { get; }
        private readonly IMemoryLane _lane;

        public MemoryHandle(int id, IMemoryLane lane)
        {
            Id = id;
            _lane = lane;
        }

        public IntPtr GetPointer()
        {
            return _lane.Resolve(this);
        }
    }
}