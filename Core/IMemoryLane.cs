/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     Core
 * FILE:        IMemoryLane.cs
 * PURPOSE:     Your file purpose here
 * PROGRAMMER:  Your name here
 */

using Core.MemoryArenaPrototype.Core;
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
        int GetAllocationSize(MemoryHandle fastHandle);
        AllocationEntry GetEntry(MemoryHandle handle);
        bool HasHandle(MemoryHandle handle);
        void Free(MemoryHandle handle);
        void Compact();
        bool CanAllocate(int size);
        string DebugDump();
    }
}