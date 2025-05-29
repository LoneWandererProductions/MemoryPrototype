using System;

namespace Core
{
    public interface IMemoryLane
    {
        MemoryHandle Allocate(
            int size,
            AllocationPriority priority = AllocationPriority.Normal,
            AllocationHints hints = AllocationHints.None,
            string? debugName = null,
            int currentFrame = 0);

        IntPtr Resolve(MemoryHandle handle);
        void Free(MemoryHandle handle);
        void Compact();
        bool CanAllocate(int size);
        string DebugDump();
    }
}