using System;

namespace Core
{
    public interface IMemoryLane
    {
        MemoryHandle Allocate(int size);
        IntPtr Resolve(MemoryHandle handle);
        void Free(MemoryHandle handle);
        void Compact();
        bool CanAllocate(int size);
        string DebugDump();
    }
}