/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     MemoryManager
 * FILE:        ConcurrentMemoryArena.cs
 * PURPOSE:     A thread-isolated, high-scaling wrapper around Memory Lanes.
 * Uses Thread-Local Storage (TLS) to eliminate hot-path allocation locks.
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

using MemoryManager.Core;
using MemoryManager.Lanes;
using System;
using System.Collections.Concurrent;
using System.Threading;

namespace MemoryManager
{
    /// <summary>
    /// A thread-safe, highly concurrent memory arena. 
    /// Provisions lock-free, independent fast lanes for every individual worker thread.
    /// </summary>
    public sealed class ConcurrentMemoryArena : IMemoryAllocator, IDisposable
    {
        private readonly MemoryManagerConfig _config;
        private readonly SlowLane _globalSlowLane;
        private readonly object _globalLock;

        // The Magic Engine: Provisions a distinct, completely isolated fast lane instance per thread
        private readonly ThreadLocal<IFastLane> _threadLocalFastLane;

        // The Cross-Thread Safety Net: Collects handles freed by foreign threads
        private readonly ConcurrentQueue<MemoryHandle> _remoteFreeQueue;

        /// <summary>
        /// Gets the block size threshold separating lanes.
        /// </summary>
        public int Threshold { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ConcurrentMemoryArena"/> class.
        /// </summary>
        public ConcurrentMemoryArena(MemoryManagerConfig config)
        {
            _config = config;
            _globalLock = new object();
            _remoteFreeQueue = new ConcurrentQueue<MemoryHandle>();
            Threshold = config.Threshold;

            // Shared tier is thread-synchronized via explicit gate boundary locking
            _globalSlowLane = new SlowLane(
                config.SlowLaneSize,
                config.SlowLaneBlobCapacityFraction,
                config.SlowLaneBlobThreshold,
                config.MaxEntries,
                config.SlowLaneFreeListStrategy);

            // Thread-local factory allocation assignment profile
            _threadLocalFastLane = new ThreadLocal<IFastLane>(() =>
            {
                IFastLane localLane;

                // Instantiate the configured allocation strategy completely isolated on this thread
                switch (_config.FastLaneStrategy)
                {
                    case AllocatorStrategy.FreeList:
                        localLane = new FastLane(_config.FastLaneSize, _globalSlowLane, _config.MaxEntries, _config.FastLaneFreeListStrategy);
                        break;
                    case AllocatorStrategy.Slab:
                        localLane = new SlabLane(_config.FastLaneSize, _globalSlowLane, _config.MaxEntries, _config);
                        break;
                    case AllocatorStrategy.LinearBump:
                    default:
                        localLane = new LinearLane(_config.FastLaneSize, _globalSlowLane, _config.MaxEntries);
                        break;
                }

                // Cross-tier eviction bridge registration mapping
                localLane.OneWayLane = new OneWayLane(localLane, _globalSlowLane);
                return localLane;
            }, trackAllValues: true); // Track values so we can clean up properly on Dispose
        }

        /// <summary>
        /// Allocates memory from the calling thread's isolated fast lane (lock-free) 
        /// or routes to the shared slow lane.
        /// </summary>
        public MemoryHandle Allocate(int size, AllocationPriority priority = AllocationPriority.Normal, AllocationHints hints = AllocationHints.None, string? debugName = null, int currentFrame = 0)
        {
            // Housekeeping: Clean up any work other threads sent back to us before allocating fresh slots
            DrainRemoteFreesForCurrentThread();

            if (size <= Threshold)
            {
                var localLane = _threadLocalFastLane.Value!;

                // Pure Lock-Free Hot Path! Bypasses global contention lines entirely.
                if (localLane.CanAllocate(size))
                {
                    return localLane.Allocate(size, priority, hints, debugName, currentFrame);
                }
            }

            // Fallback Tier: Acquire lock only when entering global shared space
            lock (_globalLock)
            {
                if (_globalSlowLane.CanAllocate(size))
                {
                    return _globalSlowLane.Allocate(size, priority, hints, debugName, currentFrame);
                }
            }

            throw new OutOfMemoryException($"ConcurrentMemoryArena pool exhausted. Requested footprint: {size} bytes.");
        }

        /// <summary>
        /// Resolves a memory handle safely, checking local thread coordinates or crossing thread lines.
        /// </summary>
        public nint Resolve(MemoryHandle handle)
        {
            if (handle.IsInvalid) throw new InvalidOperationException("Cannot resolve an invalid handle.");

            if (handle.Id > 0)
            {
                // Accessing underlying native addresses inside active slots remains thread-safe
                return handle.Lane.Resolve(handle);
            }

            // Fallback to shared global storage tracking coordinates
            lock (_globalLock)
            {
                return _globalSlowLane.Resolve(handle);
            }
        }

        /// <summary>
        /// Safely reclaims memory, instantly assessing if this is a local or foreign cross-thread free operation.
        /// </summary>
        public void Free(MemoryHandle handle)
        {
            if (handle.IsInvalid) throw new InvalidOperationException("Cannot free an invalid handle.");

            // Global SlowLane handles maintain negative identity signatures
            if (handle.Id < 0)
            {
                lock (_globalLock)
                {
                    _globalSlowLane.Free(handle);
                }
                return;
            }

            // THE CROSS-THREAD CHECK: Does this handle belong to the calling thread's private lane?
            if (handle.Lane == _threadLocalFastLane.Value)
            {
                // Happy Path: Calling thread owns this memory. Instant, lock-free reclamation cycle.
                handle.Lane.Free(handle);
            }
            else
            {
                // Chaos Path: A foreign thread is deleting our memory. 
                // Hand it off via lock-free concurrent staging array to avoid racing internal indices.
                _remoteFreeQueue.Enqueue(handle);
            }
        }

        /// <summary>
        /// Copies a source span of data atomically into the unmanaged memory referenced by the handle.
        /// Guaranteed thread-safe based on data-lane isolation layout constraints.
        /// </summary>
        public unsafe void BulkSet<T>(MemoryHandle handle, ReadOnlySpan<T> source) where T : unmanaged
        {
            if (handle.IsInvalid) throw new InvalidOperationException("Invalid handle");

            if (handle.Id > 0)
            {
                // Hot lock-free path: Isolated thread lanes manage their blocks autonomously
                var entry = handle.Lane.GetEntry(handle);
                var requiredSize = System.Runtime.CompilerServices.Unsafe.SizeOf<T>() * source.Length;

                if (entry.Size < requiredSize)
                    throw new ArgumentException($"Destination allocation ({entry.Size} bytes) is too small for source data ({requiredSize} bytes).");

                var dest = handle.Lane.Resolve(handle).ToPointer();
                fixed (T* srcPtr = source)
                {
                    Buffer.MemoryCopy(srcPtr, dest, entry.Size, requiredSize);
                }
            }
            else
            {
                // Fallback shared tier: Protected by the global synchronization lockout barrier
                lock (_globalLock)
                {
                    var entry = _globalSlowLane.GetEntry(handle);
                    var requiredSize = System.Runtime.CompilerServices.Unsafe.SizeOf<T>() * source.Length;

                    if (entry.Size < requiredSize)
                        throw new ArgumentException($"Destination allocation ({entry.Size} bytes) is too small for source data ({requiredSize} bytes).");

                    var dest = _globalSlowLane.Resolve(handle).ToPointer();
                    fixed (T* srcPtr = source)
                    {
                        Buffer.MemoryCopy(srcPtr, dest, entry.Size, requiredSize);
                    }
                }
            }
        }

        /// <summary>
        /// Retrieves the full allocation entry metadata for a given handle.
        /// </summary>
        public AllocationEntry GetEntry(MemoryHandle handle)
        {
            if (handle.IsInvalid) throw new InvalidOperationException("Invalid handle");

            if (handle.Id > 0)
            {
                return handle.Lane.GetEntry(handle);
            }

            lock (_globalLock)
            {
                return _globalSlowLane.GetEntry(handle);
            }
        }

        /// <summary>
        /// Drains remote free requests intended for the calling thread's private lane structure.
        /// </summary>
        private void DrainRemoteFreesForCurrentThread()
        {
            if (_remoteFreeQueue.IsEmpty) return;

            int initialCount = _remoteFreeQueue.Count;
            var localLane = _threadLocalFastLane.Value!;

            // Process outstanding cross-thread requests bounded by current snapshot depths
            for (int i = 0; i < initialCount; i++)
            {
                if (_remoteFreeQueue.TryPeek(out var handle))
                {
                    if (handle.Lane == localLane)
                    {
                        // The item belongs to us! Dequeue it and recycle it safely on the home thread.
                        if (_remoteFreeQueue.TryDequeue(out var matchingHandle))
                        {
                            matchingHandle.Lane.Free(matchingHandle);
                        }
                    }
                    else
                    {
                        // Rotation fallback: Item belongs to a different worker thread. 
                        // Cycle it back to the tail of the line so its respective owner can catch it.
                        if (_remoteFreeQueue.TryDequeue(out var foreignHandle))
                        {
                            _remoteFreeQueue.Enqueue(foreignHandle);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Flushes memory structures cleanly across all thread execution tracks.
        /// </summary>
        public void Dispose()
        {
            lock (_globalLock)
            {
                _globalSlowLane.Dispose();
            }

            // Iterate over every isolated lane instance provisioned by active worker paths
            foreach (var lane in _threadLocalFastLane.Values)
            {
                if (lane is IDisposable disposableLane)
                {
                    disposableLane.Dispose();
                }
            }

            _threadLocalFastLane.Dispose();
        }
    }
}