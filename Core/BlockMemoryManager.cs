/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     Core
 * FILE:        BlockMemoryManager.cs
 * PURPOSE:     Manages fixed-size block allocations in an unmanaged buffer.
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

using ExtendedSystemObjects;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Core
{
    public sealed class BlockMemoryManager : IDisposable
    {
        private readonly object _lock = new();

        public int Capacity { get; }

        public int BlockSize { get; }

        public IntPtr Buffer { get; }

        private readonly UnmanagedArray<BlockState> _states;
        private readonly SortedSet<int> _freeBlocks = new();
        private readonly UnmanagedMap<BlockAllocation> _allocations = new();

        private int _nextId = 1;

        public int GetUsedBlockCount()
        {
            lock (_lock) return Capacity - _freeBlocks.Count;
        }

        public int GetFreeBlockCount()
        {
            lock (_lock) return _freeBlocks.Count;
        }

        public int UsedBytes
        {
            get
            {
                lock (_lock) return (Capacity - _freeBlocks.Count) * BlockSize;
            }
        }

        public float UsageRatio
        {
            get
            {
                lock (_lock) return UsedBytes / (float)(Capacity * BlockSize);
            }
        }

        public BlockMemoryManager(IntPtr buffer, int totalSize, int blockSize)
        {
            if (blockSize <= 0 || totalSize % blockSize != 0)
                throw new ArgumentException("Block size must evenly divide total size");

            Buffer = buffer;
            BlockSize = blockSize;
            Capacity = totalSize / blockSize;
            _states = new UnmanagedArray<BlockState>(Capacity);

            for (int i = 0; i < Capacity; i++)
            {
                _states[i] = BlockState.Free;
                _freeBlocks.Add(i);
            }
        }

        public bool TryAllocate(int sizeInBytes, out int allocationId)
        {
            lock (_lock)
            {
                allocationId = -1;
                int blocksNeeded = (sizeInBytes + BlockSize - 1) / BlockSize;
                if (!TryAllocateContiguous(blocksNeeded, out int startBlockIndex)) return false;

                allocationId = GetNextAvailableId();
                _allocations[allocationId] = new BlockAllocation { StartIndex = startBlockIndex, BlockCount = blocksNeeded };
                return true;
            }
        }

        public void Free(int allocationId)
        {
            lock (_lock)
            {
                if (!_allocations.TryGetValue(allocationId, out var alloc))
                    throw new ArgumentException("Invalid allocation ID");

                for (int i = 0; i < alloc.BlockCount; i++)
                {
                    int block = alloc.StartIndex + i;

                    if (_states[block] != BlockState.Allocated)
                        throw new InvalidOperationException($"Block {block} is not allocated");

                    _states[block] = BlockState.Free;
                    ZeroBlock(block);
                    _freeBlocks.Add(block);
                }

                _allocations.TryRemove(allocationId);
            }
        }

        public void Compact()
        {
            lock (_lock)
            {
                var keys = new List<int>(_allocations.Keys);
                int writeIndex = 0;

                foreach (var id in keys)
                {
                    var alloc = _allocations[id];

                    if (alloc.StartIndex != writeIndex)
                    {
                        for (int i = 0; i < alloc.BlockCount; i++)
                        {
                            int readIndex = alloc.StartIndex + i;
                            int writeBlock = writeIndex + i;

                            IntPtr src = Buffer + readIndex * BlockSize;
                            IntPtr dst = Buffer + writeBlock * BlockSize;

                            unsafe
                            {
                                Unsafe.CopyBlockUnaligned((void*)dst, (void*)src, (uint)BlockSize);
                                Unsafe.InitBlockUnaligned((void*)src, 0, (uint)BlockSize); // zero source
                            }

                            _states[readIndex] = BlockState.Free;
                            _states[writeBlock] = BlockState.Allocated;

                            _freeBlocks.Add(readIndex);
                            _freeBlocks.Remove(writeBlock);
                        }

                        _allocations[id] = new BlockAllocation { StartIndex = writeIndex, BlockCount = alloc.BlockCount };
                    }

                    writeIndex += alloc.BlockCount;
                }
            }
        }

        public IntPtr GetPointer(int allocationId)
        {
            lock (_lock)
            {
                if (!_allocations.TryGetValue(allocationId, out var alloc))
                    throw new ArgumentException("Invalid allocation ID");

                return Buffer + alloc.StartIndex * BlockSize;
            }
        }

        public BlockAllocation? GetAllocation(int allocationId)
        {
            lock (_lock)
            {
                return _allocations.TryGetValue(allocationId, out var alloc) ? alloc : null;
            }
        }

        public bool IsAllocated(int blockIndex)
        {
            lock (_lock)
            {
                if (blockIndex < 0 || blockIndex >= Capacity)
                    return false;
                return _states[blockIndex] == BlockState.Allocated;
            }
        }

        public bool IsValidId(int allocationId)
        {
            lock (_lock)
            {
                return _allocations.ContainsKey(allocationId);
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                _states.Dispose();
                _allocations.Dispose();
            }
        }

        private bool TryAllocateContiguous(int count, out int startIndex)
        {
            for (int i = 0; i <= Capacity - count; i++)
            {
                bool allFree = true;

                for (int j = 0; j < count; j++)
                {
                    if (_states[i + j] != BlockState.Free)
                    {
                        allFree = false;
                        i += j;
                        break;
                    }
                }

                if (allFree)
                {
                    for (int j = 0; j < count; j++)
                    {
                        _states[i + j] = BlockState.Allocated;
                        _freeBlocks.Remove(i + j);
                    }

                    startIndex = i;
                    return true;
                }
            }

            startIndex = -1;
            return false;
        }

        private int GetNextAvailableId()
        {
            while (_allocations.ContainsKey(_nextId))
            {
                _nextId++;
                if (_nextId == int.MaxValue)
                    _nextId = 1;
            }

            return _nextId++;
        }

        private void ZeroBlock(int blockIndex)
        {
            unsafe
            {
                var ptr = Buffer + blockIndex * BlockSize;
                Unsafe.InitBlockUnaligned((void*)ptr, 0, (uint)BlockSize);
            }
        }
    }
}
