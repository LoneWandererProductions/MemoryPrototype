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
        private readonly UnmanagedMap<BlockAllocation> _allocations = new();
        private readonly Queue<int> _freeIds = new();

        private int _nextId = 1;
        private bool _disposed;

        public int GetUsedBlockCount()
        {
            lock (_lock)
            {
                int count = 0;
                for (int i = 0; i < Capacity; i++)
                    if (_states[i] == BlockState.Allocated)
                        count++;
                return count;
            }
        }

        public int GetFreeBlockCount()
        {
            lock (_lock)
            {
                int count = 0;
                for (int i = 0; i < Capacity; i++)
                    if (_states[i] == BlockState.Free)
                        count++;
                return count;
            }
        }

        public int UsedBytes => GetUsedBlockCount() * BlockSize;

        public float UsageRatio => UsedBytes / (float)(Capacity * BlockSize);

        /// <summary>
        /// Initializes a new instance of the <see cref="BlockMemoryManager"/> class.
        /// </summary>
        /// <param name="buffer">The buffer.</param>
        /// <param name="totalSize">The total size.</param>
        /// <param name="blockSize">Size of the block. Optional, 16 is on the safe side.</param>
        /// <exception cref="System.ArgumentException">Block size must evenly divide total size</exception>
        public BlockMemoryManager(IntPtr buffer, int totalSize, int blockSize = 16)
        {
            if(blockSize < 16 || blockSize % 16 != 0)
                throw new ArgumentException("Block size must be >= 16 and a multiple of 16 for alignment safety.");

            Buffer = buffer;
            BlockSize = blockSize;
            Capacity = totalSize / blockSize;
            _states = new UnmanagedArray<BlockState>(Capacity);

            for (int i = 0; i < Capacity; i++)
                _states[i] = BlockState.Free;
        }

        public bool TryAllocate(int sizeInBytes, out int allocationId)
        {
            lock (_lock)
            {
                allocationId = -1;
                int blocksNeeded = (sizeInBytes + BlockSize - 1) / BlockSize;
                if (!TryAllocateContiguous(blocksNeeded, out int startBlockIndex))
                    return false;

                allocationId = GetNextAvailableId();
                _allocations[allocationId] = new BlockAllocation { StartIndex = startBlockIndex, BlockCount = blocksNeeded };
                return true;
            }
        }

        public bool TryAllocate(int sizeInBytes, out int allocationId, out IntPtr ptr)
        {
            if (TryAllocate(sizeInBytes, out allocationId))
            {
                ptr = GetPointer(allocationId);
                return true;
            }
            ptr = IntPtr.Zero;
            return false;
        }

        public bool TryAllocateArray<T>(int count, out int allocationId) where T : unmanaged
        {
            int totalBytes = count * Unsafe.SizeOf<T>();
            return TryAllocate(totalBytes, out allocationId);
        }

        public unsafe ref T Resolve<T>(int allocationId) where T : unmanaged
        {
            var ptr = (T*)GetPointer(allocationId);
            return ref *ptr;
        }

        public unsafe Span<T> ResolveArray<T>(int allocationId, int count) where T : unmanaged
        {
            var ptr = (T*)GetPointer(allocationId);
            return new Span<T>(ptr, count);
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
                }

                _allocations.TryRemove(allocationId);
                _freeIds.Enqueue(allocationId);
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
                        }

                        _allocations[id] = new BlockAllocation
                        {
                            StartIndex = writeIndex,
                            BlockCount = alloc.BlockCount
                        };
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
                return _allocations.TryGetValue(allocationId, out var alloc) ? alloc : null;
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
                return _allocations.ContainsKey(allocationId);
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed) return;
                _disposed = true;

                _states.Dispose();
                _allocations.Dispose();
                _freeIds.Clear();
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
                        i += j; // skip to after the last checked
                        break;
                    }
                }

                if (allFree)
                {
                    for (int j = 0; j < count; j++)
                        _states[i + j] = BlockState.Allocated;

                    startIndex = i;
                    return true;
                }
            }

            startIndex = -1;
            return false;
        }

        private int GetNextAvailableId()
        {
            if (_freeIds.Count > 0)
                return _freeIds.Dequeue();

            while (_allocations.ContainsKey(_nextId))
            {
                _nextId++;
                if (_nextId == int.MaxValue)
                    _nextId = 1;
            }

            return _nextId++;
        }
    }
}
