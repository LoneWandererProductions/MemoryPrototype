using ExtendedSystemObjects;
using ExtendedSystemObjects.Interfaces;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Core
{
    public sealed partial class BlockMemoryManager
    {
        private readonly IntPtr _buffer;
        private readonly int _blockSize;
        private readonly int _blockCount;
        private readonly UnmanagedArray<BlockState> _states;

        private readonly SortedSet<int> _freeBlocks = new();
        private readonly UnmanagedMap<BlockAllocation> _allocations = new(); // id -> allocation
        private int _nextId = 1;

        public int Capacity => _blockCount;
        public int BlockSize => _blockSize;
        public IntPtr Buffer => _buffer;

        public BlockMemoryManager(IntPtr buffer, int totalSize, int blockSize)
        {
            if (blockSize <= 0 || totalSize % blockSize != 0)
                throw new ArgumentException("Block size must evenly divide total size");

            _buffer = buffer;
            _blockSize = blockSize;
            _blockCount = totalSize / blockSize;
            _states = new UnmanagedArray<BlockState>(_blockCount);

            for (int i = 0; i < _blockCount; i++)
            {
                _states[i] = BlockState.Free;
                _freeBlocks.Add(i);
            }
        }

        public bool TryAllocateBySize(int sizeInBytes, out int allocationId)
        {
            allocationId = -1;
            int blocksNeeded = (sizeInBytes + _blockSize - 1) / _blockSize;
            if (!TryAllocateContiguous(blocksNeeded, out int startBlockIndex)) return false;

            allocationId = _nextId++;
            _allocations[allocationId] = new BlockAllocation { StartIndex = startBlockIndex, BlockCount = blocksNeeded };
            return true;
        }

        public void Free(int allocationId)
        {
            if (!_allocations.TryGetValue(allocationId, out var alloc))
                throw new ArgumentException("Invalid allocation ID");

            for (int i = 0; i < alloc.BlockCount; i++)
            {
                int block = alloc.StartIndex + i;
                _states[block] = BlockState.Free;
                _freeBlocks.Add(block);
            }

            _allocations.TryRemove(allocationId);
        }

        public IntPtr GetPointer(int allocationId)
        {
            if (!_allocations.TryGetValue(allocationId, out var alloc))
                throw new ArgumentException("Invalid allocation ID");

            return _buffer + alloc.StartIndex * _blockSize;
        }

        public void Compact()
        {
            int writeIndex = 0;

            foreach (var (id, alloc) in _allocations)
            {
                if (alloc.StartIndex != writeIndex)
                {
                    for (int i = 0; i < alloc.BlockCount; i++)
                    {
                        IntPtr src = _buffer + (alloc.StartIndex + i) * _blockSize;
                        IntPtr dst = _buffer + (writeIndex + i) * _blockSize;

                        unsafe
                        {
                            Unsafe.CopyBlockUnaligned((void*)dst, (void*)src, (uint)_blockSize);
                        }

                        _states[alloc.StartIndex + i] = BlockState.Free;
                        _states[writeIndex + i] = BlockState.Allocated;
                        _freeBlocks.Add(alloc.StartIndex + i);
                        _freeBlocks.Remove(writeIndex + i);
                    }

                    _allocations[id] = new BlockAllocation { StartIndex = writeIndex, BlockCount = alloc.BlockCount };
                }

                writeIndex += alloc.BlockCount;
            }
        }

        public int GetUsedBlockCount() => _blockCount - _freeBlocks.Count;

        public int GetFreeBlockCount() => _freeBlocks.Count;

        public void Dispose()
        {
            _states.Dispose();
            _allocations.Dispose();
        }

        private bool TryAllocateContiguous(int count, out int startIndex)
        {
            startIndex = -1;
            for (int i = 0; i <= _blockCount - count; i++)
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

            return false;
        }
    }
}
