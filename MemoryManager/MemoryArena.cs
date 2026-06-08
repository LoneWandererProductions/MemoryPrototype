/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     MemoryManager
 * FILE:        MemoryArena.cs
 * PURPOSE:     The wrapper around the different Memory Components.
 *              Mostly consists of FastLane, SlowLane, OneWayLane
 *              All configurable via Config and all internal actions are policy based.
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

using MemoryManager.Core;
using MemoryManager.Lanes;
using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;

namespace MemoryManager
{
    /// <inheritdoc />
    /// <summary>
    /// The thread-safe wrapper around the different Memory Arena Components.
    /// </summary>
    public sealed class MemoryArena : IMemoryAllocator
    {
        /// <summary>
        /// The configuration
        /// </summary>
        private readonly MemoryManagerConfig _config;

        /// <summary>
        /// The lock
        /// </summary>
        private readonly object _lock;

        /// <summary>
        /// The policy timer
        /// </summary>
        private readonly Timer? _policyTimer;

        /// <summary>
        /// Gets the current frame clock.
        /// </summary>
        public int CurrentFrame { get; private set; }

        /// <summary>
        /// Gets the active fast lane engine instance.
        /// </summary>
        public IFastLane FastLane { get; set; }

        /// <summary>
        /// Gets the active slow lane engine instance.
        /// </summary>
        public SlowLane SlowLane { get; set; }

        /// <summary>
        /// Gets the block size threshold separating lanes.
        /// </summary>
        private int Threshold { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="MemoryArena" /> class.
        /// </summary>
        /// <param name="config">The memory configuration blueprint.</param>
        public MemoryArena(MemoryManagerConfig config)
        {
            _lock = new object();
            _config = config;
            Threshold = config.Threshold;

            // PASS 1: Initialize the anti-fragmentation persistent tier
            SlowLane = new SlowLane(
                config.SlowLaneSize,
                config.SlowLaneBlobCapacityFraction,
                config.SlowLaneBlobThreshold,
                config.MaxEntries,
                config.SlowLaneFreeListStrategy);

            // PASS 2: Factory Selector for the FastLane layout blueprint
            switch (config.FastLaneStrategy)
            {
                case AllocatorStrategy.FreeList:
                    FastLane = new FastLane(
                        config.FastLaneSize,
                        SlowLane,
                        config.MaxEntries,
                        config.FastLaneFreeListStrategy);
                    break;

                case AllocatorStrategy.Slab:
                    // Drop-in our new dynamic segregated storage bucket allocator
                    FastLane = new SlabLane(
                        config.FastLaneSize,
                        SlowLane,
                        config.MaxEntries,
                        config);
                    break;

                case AllocatorStrategy.LinearBump:
                default:
                    // Fall back to our ultra-high speed frame-based linear bump allocator
                    FastLane = new LinearLane(
                        config.FastLaneSize,
                        SlowLane,
                        config.MaxEntries);
                    break;
            }

            FastLane.OneWayLane = new OneWayLane(FastLane, SlowLane);

            if (config.PolicyCheckInterval > TimeSpan.Zero)
                _policyTimer = new Timer(_ => CheckPolicies(), null, config.PolicyCheckInterval, config.PolicyCheckInterval);
        }

        /// <summary>
        /// Gets a Span over the memory region for high-speed bulk access.
        /// </summary>
        /// <typeparam name="T">The type of elements in the span.</typeparam>
        /// <param name="handle">The handle.</param>
        /// <param name="count">The count.</param>
        /// <returns>A span containing the elements of type T.</returns>
        public unsafe Span<T> GetSpan<T>(MemoryHandle handle, int count) where T : unmanaged
        {
            lock (_lock)
            {
                var ptr = ResolveInternal(handle).ToPointer();
                return new Span<T>(ptr, count);
            }
        }

        /// <inheritdoc />
        /// <summary>
        /// Retrieves the full allocation entry metadata for a given handle by routing to the correct lane.
        /// </summary>
        /// <param name="handle">The handle.</param>
        /// <returns>The allocation entry metadata.</returns>
        /// <exception cref="System.InvalidOperationException">Invalid handle</exception>
        public AllocationEntry GetEntry(MemoryHandle handle)
        {
            if (handle.IsInvalid) throw new InvalidOperationException("Invalid handle");

            lock (_lock)
            {
                return handle.Id > 0
                    ? FastLane.GetEntry(handle)
                    : SlowLane.GetEntry(handle);
            }
        }

        /// <summary>
        /// Increments the arena's internal clock and optionally runs maintenance.
        /// </summary>
        public void TickFrame()
        {
            lock (_lock)
            {
                CurrentFrame++;
            }

            if (_policyTimer == null)
            {
                CheckPolicies();
            }
        }

        /// <summary>
        /// Moves data from FastLane to SlowLane, leaving a Redirection Stub behind.
        /// </summary>
        public void MoveFastToSlow(MemoryHandle fastHandle)
        {
            lock (_lock)
            {
                FastLane.OneWayLane?.MoveFromFastToSlow(fastHandle);
            }
        }

        /// <summary>
        /// Moves an Entry from slow to fast based on policy heuristics.
        /// </summary>
        /// <param name="slowHandle">The slow handle.</param>
        /// <returns></returns>
        /// <exception cref="System.ArgumentException">Handle must be a valid SlowLane handle (negative ID).</exception>
        /// <exception cref="System.OutOfMemoryException">Not enough space in FastLane to move from SlowLane.</exception>
        public unsafe MemoryHandle MoveSlowToFast(MemoryHandle slowHandle)
        {
            lock (_lock)
            {
                if (slowHandle.IsInvalid || slowHandle.Id >= 0)
                    throw new ArgumentException("Handle must be a valid SlowLane handle (negative ID).");

                var slowEntry = SlowLane.GetEntry(slowHandle);
                var size = slowEntry.Size;

                if (!FastLane.CanAllocate(size))
                    throw new OutOfMemoryException("Not enough space in FastLane to move from SlowLane.");

                var fastHandle = FastLane.Allocate(size, slowEntry.Priority, slowEntry.Hints, null, CurrentFrame);

                Buffer.MemoryCopy(
                    (void*)(SlowLane.Buffer + slowEntry.Offset),
                    (void*)FastLane.Resolve(fastHandle),
                    size,
                    size);

                SlowLane.Free(slowHandle);
                return fastHandle;
            }
        }

        /// <summary>
        /// Retrieves a reference directly to a value of type T inside the unmanaged arena.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="handle">The handle.</param>
        /// <returns>A reference to the value of type T.</returns>
        public unsafe ref T Get<T>(MemoryHandle handle) where T : unmanaged
        {
            lock (_lock)
            {
                var ptr = ResolveInternal(handle);
                return ref Unsafe.AsRef<T>(ptr.ToPointer());
            }
        }

        /// <summary>
        /// Forces an immediate execution of the background consolidation routine.
        /// </summary>
        public void RunMaintenanceCycle()
        {
            lock (_lock)
            {
                TryCompactFastLane();
                TryCompactSlowLane();
            }
        }

        /// <summary>
        /// Allocates space for type T and stores the provided value immediately.
        /// </summary>
        public unsafe MemoryHandle AllocateAndStore<T>(
            T value,
            AllocationPriority priority = AllocationPriority.Normal,
            AllocationHints hints = AllocationHints.None,
            string? debugName = null) where T : unmanaged
        {
            lock (_lock)
            {
                var handle = AllocateInternal(Unsafe.SizeOf<T>(), priority, hints, debugName, CurrentFrame);
                var dest = ResolveInternal(handle).ToPointer();
                Unsafe.Write(dest, value);
                return handle;
            }
        }

        /// <inheritdoc />
        /// <summary>
        /// Copies a source span of data atomically into the unmanaged memory referenced by the handle.
        /// Guarantees thread-safety against background compaction sweeps during bulk memory transfers.
        /// </summary>
        /// <typeparam name="T">The unmanaged type of elements to copy.</typeparam>
        /// <param name="handle">Handle pointing to the destination unmanaged memory block.</param>
        /// <param name="source">The read-only data span to copy from.</param>
        /// <exception cref="InvalidOperationException">Thrown if the handle is invalid.</exception>
        /// <exception cref="ArgumentException">Thrown if the destination allocation partition is too small.</exception>
        public unsafe void BulkSet<T>(MemoryHandle handle, ReadOnlySpan<T> source) where T : unmanaged
        {
            if (handle.IsInvalid)
                throw new InvalidOperationException("Invalid handle");

            lock (_lock) // ATOMIC BOUNDARY: No background compaction can sneak in while this block executes
            {
                // 1. Fetch metadata safely
                var entry = handle.Id > 0 ? FastLane.GetEntry(handle) : SlowLane.GetEntry(handle);
                var requiredSize = Unsafe.SizeOf<T>() * source.Length;

                if (entry.Size < requiredSize)
                    throw new ArgumentException(
                        $"Destination allocation ({entry.Size} bytes) is too small for source data ({requiredSize} bytes).");

                // 2. Resolve pointer within the same synchronized frame
                var dest = (handle.Id > 0 ? FastLane.Resolve(handle) : SlowLane.Resolve(handle)).ToPointer();

                // 3. Execute bitwise copy while holding the system lockout lock
                fixed (T* srcPtr = source)
                {
                    Buffer.MemoryCopy(srcPtr, dest, entry.Size, requiredSize);
                }
            }
        }

        /// <inheritdoc />
        /// <summary>
        /// Allocates a contiguous block of memory from the appropriate lane based on size threshold policies.
        /// </summary>
        /// <param name="size">The size.</param>
        /// <param name="priority">The priority.</param>
        /// <param name="hints">The hints.</param>
        /// <param name="debugName">Name of the debug.</param>
        /// <param name="currentFrame">The current frame.</param>
        /// <returns>The allocated memory handle.</returns>
        public MemoryHandle Allocate(
            int size,
            AllocationPriority priority = AllocationPriority.Normal,
            AllocationHints hints = AllocationHints.None,
            string? debugName = null,
            int currentFrame = 0)
        {
            lock (_lock)
            {
                return AllocateInternal(size, priority, hints, debugName, currentFrame == 0 ? CurrentFrame : currentFrame);
            }
        }

        /// <inheritdoc />
        /// <summary>
        /// Resolves the memory handle to a reliable native address space pointer.
        /// </summary>
        /// <param name="handle">The handle.</param>
        /// <returns>The resolved pointer.</returns>
        public IntPtr Resolve(MemoryHandle handle)
        {
            lock (_lock)
            {
                return ResolveInternal(handle);
            }
        }

        /// <inheritdoc />
        /// <summary>
        /// Reclaims unmanaged workspace tied to a specific allocation tracking registration.
        /// </summary>
        /// <param name="handle">The handle.</param>
        /// <exception cref="System.InvalidOperationException">Invalid handle</exception>
        public void Free(MemoryHandle handle)
        {
            if (handle.IsInvalid)
                throw new InvalidOperationException("Invalid handle");

            lock (_lock) // FIX: Lock synchronization prevents array manipulation races during background compaction
            {
                if (handle.Id > 0)
                    FastLane.Free(handle);
                else
                    SlowLane.Free(handle);
            }
        }

        /// <inheritdoc />
        /// <summary>
        /// Forces physical compaction and fragment collection across all tiers.
        /// </summary>
        public void CompactAll()
        {
            lock (_lock)
            {
                FastLane.Compact(CurrentFrame, _config);
                SlowLane.Compact();
            }
        }

        /// <inheritdoc />
        /// <summary>
        /// Debugdump.
        /// </summary>
        public string DebugDump()
        {
            lock (_lock)
            {
                var sb = new System.Text.StringBuilder();

                sb.AppendLine("===== MemoryArena Dump =====");

                // Hot Path Telemetry
                sb.AppendLine($"Fast Lane Usage: {FastLane.UsagePercentage():P2}, Free: {FastLane.FreeSpace()} bytes, Entries: {FastLane.EntryCount}, Stubs: {FastLane.StubCount()}");
                sb.AppendLine($"Estimated Fragmentation: {FastLane.EstimateFragmentation()}%");
                sb.AppendLine(FastLane.DebugDump());
                sb.AppendLine(FastLane.DebugVisualMap());
                sb.AppendLine(FastLane.DebugRedirections());

                sb.AppendLine(); // Clean spacing break

                // Cold Path Telemetry
                sb.AppendLine($"Slow Lane Usage: {SlowLane.UsagePercentage():P2}, Free: {SlowLane.FreeSpace()} bytes, Entries: {SlowLane.EntryCount}, Stubs: {SlowLane.StubCount()}");
                sb.AppendLine($"Estimated Fragmentation: {SlowLane.EstimateFragmentation()}%");
                sb.AppendLine(SlowLane.DebugDump());
                sb.AppendLine(SlowLane.DebugVisualMap());
                sb.AppendLine(SlowLane.DebugRedirections());

                sb.AppendLine("============================");

                return sb.ToString();
            }
        }

        /// <inheritdoc />
        public void LogDump() => Trace.WriteLine(DebugDump());

        // --- UNLOCKED INTERNAL PATHS ---

        /// <summary>
        /// Allocates the internal.
        /// </summary>
        /// <param name="size">The size.</param>
        /// <param name="priority">The priority.</param>
        /// <param name="hints">The hints.</param>
        /// <param name="debugName">Name of the debug.</param>
        /// <param name="frame">The frame.</param>
        /// <returns>The allocated memory handle.</returns>
        /// <exception cref="System.OutOfMemoryException">Neither lane could allocate memory. Requested size: {size}, " +
        ///                 $"FastLane free: {FastLane.FreeSpace()}, SlowLane free: {SlowLane.FreeSpace()}</exception>
        private MemoryHandle AllocateInternal(int size, AllocationPriority priority, AllocationHints hints, string? debugName, int frame)
        {
            if (size <= Threshold && FastLane.CanAllocate(size))
                return FastLane.Allocate(size, priority, hints, debugName, frame);
            if (SlowLane.CanAllocate(size))
                return SlowLane.Allocate(size, priority, hints, debugName, frame);

            throw new OutOfMemoryException(
                $"Neither lane could allocate memory. Requested size: {size}, " +
                $"FastLane free: {FastLane.FreeSpace()}, SlowLane free: {SlowLane.FreeSpace()}");
        }

        /// <summary>
        /// Resolves the internal.
        /// </summary>
        /// <param name="handle">The handle.</param>
        /// <returns>The resolved pointer.</returns>
        /// <exception cref="System.InvalidOperationException">Invalid handle {handle.Id}</exception>
        private IntPtr ResolveInternal(MemoryHandle handle)
        {
            if (handle.IsInvalid)
                throw new InvalidOperationException($"Invalid handle {handle.Id}");

            return handle.Id > 0 ? FastLane.Resolve(handle) : SlowLane.Resolve(handle);
        }

        /// <summary>
        /// Tries the compact fast lane.
        /// </summary>
        private void TryCompactFastLane()
        {
            if (FastLane.UsagePercentage() > _config.FastLaneUsageThreshold)
            {
                FastLane.Compact(CurrentFrame, _config);
            }
        }

        /// <summary>
        /// Tries the compact slow lane.
        /// </summary>
        private void TryCompactSlowLane()
        {
            if (SlowLane.UsagePercentage() <= _config.SlowLaneUsageThreshold)
                return;

            double fragmentationFraction = SlowLane.EstimateFragmentation() / 100.0;
            double currentFreeSpace = SlowLane.FreeSpace();
            double totalSize = SlowLane.Capacity;
            double predictedFreeAfterCompaction = currentFreeSpace + (fragmentationFraction * totalSize);

            if (predictedFreeAfterCompaction / totalSize >= _config.SlowLaneSafetyMargin)
            {
                SlowLane.Compact();
            }
        }

        /// <summary>
        /// Checks the policies.
        /// </summary>
        private void CheckPolicies()
        {
            lock (_lock)
            {
                if (!_config.EnableAutoCompaction) return;

                //  Consolidated and streamlined dual-threshold checks into unified sequential evaluation pass
                var usage = FastLane.UsagePercentage();
                if (usage >= _config.CompactionThreshold || usage > _config.FastLaneUsageThreshold)
                {
                    FastLane.Compact(CurrentFrame, _config);
                }

                TryCompactSlowLane();
            }
        }
    }
}