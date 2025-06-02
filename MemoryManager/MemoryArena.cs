/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     MemoryManager
 * FILE:        MemoryArena.cs
 * PURPOSE:     The wrapper around the different Memory Components.
 *              Mostly consists of FastLane, SlowLane, OnwayLane
 *              All configurable via Config and all internal actions are policy based.
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 * REMARKS:     FastLane Policy
 *              When to Compact FastLane:
 *              Usage > 90% of FastLane and enough memory is wasted by stubs.
 *              Large allocation request cannot be fulfilled despite stubs present.
 *              Manual trigger (e.g. during maintenance cycle or scene change).
 *              What to Move to SlowLane:
 *              Entries not accessed recently (age tagging or LRU heuristic).
 *              Entries larger than a threshold (e.g., 4 KB+).
 *              Entries marked as cold or long-lived (by system tagging).
 *              Entries not tagged as frame-critical.
 *              On Move:
 *              Allocate in SlowLane
 *              Copy data
 *              Replace original with stub
 *              Flag for FastLane.Compact() if space was freed
 *              SlowLane Policy
 *              When to Compact SlowLane:
 *              Usage > 85% of SlowLane
 *              Fragmentation is high (e.g., >20% space in gaps)
 *              SlowLane allocation fails even though enough space exists
 *              Manual trigger, e.g. MemoryArena.CompactAll()
 *              Compacting Constraints:
 *              Only compact if free space after compaction > 10%
 *              Ensures space for future moves from FastLane, at least that is planned, reserved 10% of slow lane for janitor work
 *              Do not compact aggressively; it’s slower and costlier
 *              Prefer compaction during low activity (non-frame time)
 */

// ReSharper disable NotAccessedField.Local

#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using Core;
using Lanes;

namespace MemoryManager
{
    /// <summary>
    ///     The wrapper around the different Memory Components.
    /// </summary>
    public sealed class MemoryArena
    {
        /// <summary>
        ///     The configuration
        /// </summary>
        private readonly MemoryManagerConfig _config;

        /// <summary>
        ///     The lock
        /// </summary>
        private readonly object _lock;

        // Optionally: background thread for policies
        private Timer? _policyTimer;

        /// <summary>
        ///     Initializes a new instance of the <see cref="MemoryArena" /> class.
        /// </summary>
        /// <param name="config">The configuration.</param>
        public MemoryArena(MemoryManagerConfig config)
        {
            _lock = new object();

            _config = config;
            Threshold = config.Threshold;
            SlowLane = new SlowLane(config.SlowLaneSize);
            FastLane = new FastLane(config.FastLaneSize, SlowLane);

            // Now safe to construct OneWayLane
            FastLane.OneWayLane = new OneWayLane(config.BufferSize, FastLane, SlowLane);

            if (config.PolicyCheckInterval > TimeSpan.Zero)
                _policyTimer = new Timer(_ => CheckPolicies(), null, config.PolicyCheckInterval,
                    config.PolicyCheckInterval);
        }

        /// <summary>
        ///     Gets or sets the fast lane.
        /// </summary>
        /// <value>
        ///     The fast lane.
        /// </value>
        public FastLane FastLane { get; set; }

        /// <summary>
        ///     Gets or sets the slow lane.
        /// </summary>
        /// <value>
        ///     The slow lane.
        /// </value>
        public SlowLane SlowLane { get; set; }

        /// <summary>
        ///     Gets or sets the threshold.
        /// </summary>
        /// <value>
        ///     The threshold.
        /// </value>
        private int Threshold { get; }

        /// <summary>
        ///     Moves the fast to slow.
        ///     Leaves a Stub to the new location.
        /// </summary>
        /// <param name="fastHandle">The fast handle.</param>
        public unsafe void MoveFastToSlow(MemoryHandle fastHandle)
        {
            var fastEntry = FastLane.GetEntry(fastHandle);
            var size = fastEntry.Size;

            var slowHandle = SlowLane.Allocate(size);

            Buffer.MemoryCopy(
                (void*)(FastLane.Buffer + fastEntry.Offset),
                (void*)SlowLane.Resolve(slowHandle),
                size,
                size);

            FastLane.ReplaceWithStub(fastHandle, slowHandle);
        }

        /// <summary>
        ///     Moves the slow to fast.
        ///     Moves an Entry from slow to fast, it should be policy based and not directly done by the user.
        /// </summary>
        /// <param name="slowHandle">The slow handle.</param>
        /// <returns>New handle to the fastlane</returns>
        /// <exception cref="System.ArgumentException">Handle must be a valid SlowLane handle (negative ID).</exception>
        /// <exception cref="System.OutOfMemoryException">Not enough space in FastLane to move from SlowLane.</exception>
        public unsafe MemoryHandle MoveSlowToFast(MemoryHandle slowHandle)
        {
            if (slowHandle.IsInvalid || slowHandle.Id >= 0)
                throw new ArgumentException("Handle must be a valid SlowLane handle (negative ID).");

            var slowEntry = SlowLane.GetEntry(slowHandle);
            var size = slowEntry.Size;

            if (!FastLane.CanAllocate(size))
                throw new OutOfMemoryException("Not enough space in FastLane to move from SlowLane.");

            // Allocate in FastLane
            var fastHandle = FastLane.Allocate(size);

            // Copy data from SlowLane buffer to FastLane buffer
            Buffer.MemoryCopy(
                (void*)(SlowLane.Buffer + slowEntry.Offset),
                (void*)FastLane.Resolve(fastHandle),
                size,
                size);

            // Free the old slow lane allocation
            SlowLane.Free(slowHandle);

            return fastHandle;
        }

        /// <summary>
        ///     Gets the specified handle.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="handle">The handle.</param>
        /// <returns>Get handle to the type.</returns>
        public unsafe ref T Get<T>(MemoryHandle handle) where T : unmanaged
        {
            var ptr = Resolve(handle);
            return ref Unsafe.AsRef<T>(ptr.ToPointer());
        }

        /// <summary>
        ///     Runs the maintenance cycle.
        ///     Will be policy managed in the future
        /// </summary>
        public void RunMaintenanceCycle()
        {
            TryCompactFastLane();
            TryCompactSlowLane();
        }

        /// <summary>
        ///     Allocates the specified size.
        /// </summary>
        /// <param name="size">The size.</param>
        /// <param name="priority">The priority.</param>
        /// <param name="hints">The hints.</param>
        /// <param name="debugName">Name of the debug.</param>
        /// <param name="currentFrame">The current frame.</param>
        /// <returns>Handle to the memory area.</returns>
        /// <exception cref="System.OutOfMemoryException">
        ///     Neither lane could allocate memory. Requested size: {size}, " +
        ///     $"FastLane free: {FastLane.FreeSpace()}, SlowLane free: {SlowLane.FreeSpace()}
        /// </exception>
        public MemoryHandle Allocate(
            int size,
            AllocationPriority priority = AllocationPriority.Normal,
            AllocationHints hints = AllocationHints.None,
            string? debugName = null,
            int currentFrame = 0)
        {
            lock (_lock)
            {
                if (size <= Threshold && FastLane.CanAllocate(size))
                    return FastLane.Allocate(size, priority, hints, debugName, currentFrame);
                if (SlowLane.CanAllocate(size))
                    return SlowLane.Allocate(size, priority, hints, debugName, currentFrame);

                throw new OutOfMemoryException(
                    $"Neither lane could allocate memory. Requested size: {size}, " +
                    $"FastLane free: {FastLane.FreeSpace()}, SlowLane free: {SlowLane.FreeSpace()}");
            }
        }

        /// <summary>
        ///     Checks the policies.
        ///     Convention: Positive handles go to FastLane, negative to SlowLane.
        ///     Zero is reserved/invalid.
        /// </summary>
        /// <param name="handle">The handle.</param>
        /// <returns>Get the pointer to the data.</returns>
        public IntPtr Resolve(MemoryHandle handle)
        {
            if (handle.IsInvalid)
                throw new InvalidOperationException($"Invalid handle {handle.Id}");

            if (handle.Id > 0)
                return FastLane.Resolve(handle);

            return SlowLane.Resolve(handle);
        }

        /// <summary>
        ///     Compacts all lanes.
        /// </summary>
        public void CompactAll()
        {
            FastLane.Compact();
            SlowLane.Compact();
        }

        /// <summary>
        ///     Free the memory by pointer
        ///     Convention: Positive handles go to FastLane, negative to SlowLane.
        ///     Zero is reserved/invalid.
        /// </summary>
        /// <param name="handle">The handle.</param>
        public void Free(MemoryHandle handle)
        {
            if (handle.IsInvalid)
                throw new InvalidOperationException("Invalid handle");

            if (handle.Id > 0)
                FastLane.Free(handle);
            else
                SlowLane.Free(handle);
        }

        /// <summary>
        ///     Dumps all debug logs, use with care cpu heavy..
        /// </summary>
        public void DebugDump()
        {
            Trace.WriteLine("===== MemoryArena Dump =====");
            Trace.WriteLine(
                $"Fast Lane Usage: {FastLane.UsagePercentage():P2}, Free: {FastLane.FreeSpace()} bytes, Entries: {FastLane.EntryCount}, Stubs: {FastLane.StubCount()}");

            Trace.WriteLine($"Estimated Fragmentation: {FastLane.EstimateFragmentation():P2}");
            Trace.WriteLine(FastLane.DebugDump());
            Trace.WriteLine(FastLane.DebugVisualMap());
            Trace.WriteLine(FastLane.DebugRedirections());

            Trace.WriteLine(
                $"Slow Lane Usage: {SlowLane.UsagePercentage():P2}, Free: {SlowLane.FreeSpace()} bytes, Entries: {SlowLane.EntryCount}, Stubs: {SlowLane.StubCount()}");
            Trace.WriteLine($"Estimated Fragmentation: {SlowLane.EstimateFragmentation():P2}");
            Trace.WriteLine(SlowLane.DebugDump());
            Trace.WriteLine(SlowLane.DebugVisualMap());
            Trace.WriteLine(SlowLane.DebugRedirections());
            Trace.WriteLine("============================");
        }

        /// <summary>
        ///     Tries to compact the FastLane.
        /// </summary>
        private void TryCompactFastLane()
        {
            var fastLaneUsageThreshold = _config.FastLaneUsageThreshold;

            if (FastLane.UsagePercentage() <= fastLaneUsageThreshold)
                return;

            // Cache handles that should be moved first to avoid modifying collection during enumeration
            var handlesToMove = new List<MemoryHandle>();

            foreach (var handle in FastLane.GetHandles())
            {
                var entry = FastLane.GetEntry(handle);
                if (entry.Size > _config.FastLaneUsageThreshold ||
                    entry.Hints.HasFlag(AllocationHints.Cold) ||
                    entry.Hints.HasFlag(AllocationHints.Old))
                    handlesToMove.Add(handle);
            }

            foreach (var handle in handlesToMove) MoveFastToSlow(handle);

            FastLane.Compact();
        }

        /// <summary>
        ///     Tries to compact the SlowLane.
        /// </summary>
        private void TryCompactSlowLane()
        {
            // Usage threshold before considering compaction (e.g., 85%)
            var slowLaneUsageThreshold = _config.SlowLaneUsageThreshold;

            if (!(SlowLane.UsagePercentage() > slowLaneUsageThreshold))
                return;

            // Estimated fragmentation fraction (0.0 - 1.0)
            double fragmentation = SlowLane.EstimateFragmentation();

            // Current free space in bytes
            double currentFreeSpace = SlowLane.FreeSpace();

            // Total capacity of the slow lane in bytes
            double totalSize = SlowLane.Capacity;

            // Predicted free space after compaction (current free + fragmented gaps)
            var predictedFreeAfterCompaction = currentFreeSpace + fragmentation * totalSize;

            // Use slow lane specific safety margin to decide if compaction is worthwhile
            var safetyMargin = _config.SlowLaneSafetyMargin;

            // If predicted free space ratio meets or exceeds safety margin, perform compaction
            if (predictedFreeAfterCompaction / totalSize >= safetyMargin) SlowLane.Compact();
        }

        /// <summary>
        ///     Checks the policies.
        /// </summary>
        private void CheckPolicies()
        {
            var usage = FastLane.UsagePercentage();
            if (!_config.EnableAutoCompaction || !(usage >= _config.CompactionThreshold)) return;

            TryCompactFastLane();
            TryCompactSlowLane();
            // Optional: log compaction stats or notify observers
        }
    }
}