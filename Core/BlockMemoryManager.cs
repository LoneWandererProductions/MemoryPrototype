/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     Core
 * FILE:        BlockMemoryManager.cs
 * PURPOSE:     Your file purpose here
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

using ExtendedSystemObjects;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Core
{
    public sealed class BlockMemoryManager
    {
        public int GetUsedBlockCount() => Capacity - _freeBlocks.Count;

        public int GetFreeBlockCount() => _freeBlocks.Count;

        public int Capacity { get; }

        public int BlockSize { get; }

        public IntPtr Buffer { get; }

        private readonly UnmanagedArray<BlockState> _states;

        private readonly SortedSet<int> _freeBlocks = new();

        private readonly UnmanagedMap<BlockAllocation> _allocations = new(); // id -> allocation

        private int _nextId = 1;

        /// <summary>
        /// Initializes a new instance of the <see cref="BlockMemoryManager"/> class.
        /// </summary>
        /// <param name="buffer">The buffer.</param>
        /// <param name="totalSize">The total size.</param>
        /// <param name="blockSize">Size of the block.</param>
        /// <exception cref="System.ArgumentException">Block size must evenly divide total size</exception>
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

        public bool TryAllocateBySize(int sizeInBytes, out int allocationId)
        {
            allocationId = -1;
            int blocksNeeded = (sizeInBytes + BlockSize - 1) / BlockSize;
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

        public void Compact()
        {
            int writeIndex = 0;

            foreach (var (id, alloc) in _allocations)
            {
                if (alloc.StartIndex != writeIndex)
                {
                    for (int i = 0; i < alloc.BlockCount; i++)
                    {
                        IntPtr src = Buffer + (alloc.StartIndex + i) * BlockSize;
                        IntPtr dst = Buffer + (writeIndex + i) * BlockSize;

                        unsafe
                        {
                            Unsafe.CopyBlockUnaligned((void*)dst, (void*)src, (uint)BlockSize);
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

        public IntPtr GetPointer(int allocationId)
        {
            if (!_allocations.TryGetValue(allocationId, out var alloc))
                throw new ArgumentException("Invalid allocation ID");

            return Buffer + (alloc.StartIndex * BlockSize);
        }

        public void Dispose()
        {
            _states.Dispose();
            _allocations.Dispose();
        }

        private bool TryAllocateContiguous(int count, out int startIndex)
        {
            startIndex = -1;

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

            return false;
        }
    }
}
