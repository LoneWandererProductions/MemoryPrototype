/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     MemoryArenaPrototype.Lane
 * FILE:        MemoryLaneUtils.cs
 * PURPOSE:     Most shared methods of both lanes and most important all the debug stuff for both.
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

using ExtendedSystemObjects;
using MemoryManager.Core;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text;
using MemoryHandle = MemoryManager.Core.MemoryHandle;

namespace MemoryManager.Lanes
{
    /// <summary>
    /// Static utility class for shared methods between FastLane and SlowLane, as well as common debug functionality.
    /// </summary>
    internal static class MemoryLaneUtils
    {
        /// <summary>
        ///     Calculates the true physical free space by summing available tracking blocks.
        /// </summary>
        /// <param name="freeBlocks">The free blocks tracker array.</param>
        /// <param name="freeBlockCount">The active free block count.</param>
        /// <returns>Free space of Lane in bytes.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int CalculateFreeSpace(FreeBlock[] freeBlocks, int freeBlockCount)
        {
            // Direct readout from the physical free blocks array provides 100% accuracy and accommodates canary padding
            var totalFree = 0;
            for (var i = 0; i < freeBlockCount; i++)
            {
                totalFree += freeBlocks[i].Size;
            }
            return totalFree;
        }

        /// <summary>
        ///     Estimates fragmentation using memory dispersion metrics against available free blocks.
        /// </summary>
        /// <param name="freeBlocks">The free blocks tracker array.</param>
        /// <param name="freeBlockCount">The active free block count.</param>
        /// <returns>Estimated fragmentation percentage (0-100).</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int EstimateFragmentation(FreeBlock[] freeBlocks, int freeBlockCount)
        {
            if (freeBlockCount <= 1) return 0;

            var totalFree = 0;
            var largestBlock = 0;

            // FIX: Evaluates real fragment scattering instead of guessing boundaries based on structural array position offsets
            for (var i = 0; i < freeBlockCount; i++)
            {
                var size = freeBlocks[i].Size;
                totalFree += size;
                if (size > largestBlock)
                {
                    largestBlock = size;
                }
            }

            if (totalFree == 0) return 0;

            // Dispersion formula: measures how fragmented your available free memory chunks are
            return (int)((1.0 - ((double)largestBlock / totalFree)) * 100);
        }

        /// <summary>
        /// Stubs the count.
        /// </summary>
        /// <param name="entries">The entries.</param>
        /// <returns>
        /// Number of stubs, aka references from fast to slow Lane.
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int StubCount(ReadOnlySpan<AllocationEntry> entries)
        {
            var count = 0;
            foreach (ref readonly var entry in entries)
            {
                if (entry.IsStub) count++;
            }

            return count;
        }

        /// <summary>
        /// Finds a free spot using a configurable Free-List search approach (Zero Allocation).
        /// </summary>
        /// <param name="size">The physical size needed (including canaries/alignment padding).</param>
        /// <param name="freeBlocks">The free blocks tracker array.</param>
        /// <param name="freeBlockCount">The active free block count.</param>
        /// <param name="strategy">The allocation strategy layout constraint to use.</param>
        /// <returns>First free next offset for the allocator, or -1 if Out of Memory.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int FindFreeSpot(int size, ref FreeBlock[] freeBlocks, ref int freeBlockCount, AllocationStrategy strategy)
        {
            var targetIndex = -1;

            switch (strategy)
            {
                case AllocationStrategy.FirstFit:
                    // --- FIRST FIT STRATEGY (High-Velocity Homogeneous Path) ---
                    for (var i = 0; i < freeBlockCount; i++)
                    {
                        if (freeBlocks[i].Size >= size)
                        {
                            targetIndex = i;
                            break; // Exit instantly on the first validation match
                        }
                    }
                    break;

                case AllocationStrategy.BestFit:
                    // --- BEST FIT STRATEGY (Anti-Fragmentation Heterogeneous Path) ---
                    var minRemainder = int.MaxValue;

                    for (var i = 0; i < freeBlockCount; i++)
                    {
                        if (freeBlocks[i].Size >= size)
                        {
                            int remainder = freeBlocks[i].Size - size;

                            // Track the hole that leaves behind the smallest possible wasted remnant
                            if (remainder < minRemainder)
                            {
                                minRemainder = remainder;
                                targetIndex = i;

                                // OPTIMIZATION: If we hit a perfect structural match, stop scanning immediately
                                if (remainder == 0) break;
                            }
                        }
                    }
                    break;
            }

            // If no suitable memory block hole was found across the free-list table
            if (targetIndex == -1) return -1;

            // --- CONSUME AND UPDATE THE TRACKED HOLE ---
            var assignedOffset = freeBlocks[targetIndex].Offset;

            // Shrink the hole footprint boundaries
            freeBlocks[targetIndex].Offset += size;
            freeBlocks[targetIndex].Size -= size;

            // If the hole is fully consumed down to 0 bytes, remove it by swapping with the last item
            if (freeBlocks[targetIndex].Size == 0)
            {
                freeBlockCount--;
                freeBlocks[targetIndex] = freeBlocks[freeBlockCount];
            }

            return assignedOffset;
        }

        /// <summary>
        /// Returns memory to the free list and merges adjacent blocks to prevent fragmentation.
        /// </summary>
        /// <param name="offset">The offset.</param>
        /// <param name="size">The size.</param>
        /// <param name="freeBlocks">The free blocks.</param>
        /// <param name="freeBlockCount">The free block count.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void ReturnFreeSpace(int offset, int size, ref FreeBlock[] freeBlocks, ref int freeBlockCount)
        {
            if (freeBlockCount >= freeBlocks.Length)
            {
                Array.Resize(ref freeBlocks, freeBlocks.Length * 2);
            }

            var newHole = new FreeBlock { Offset = offset, Size = size };

            // Insert sorted by offset
            var insertIndex = freeBlockCount;
            for (var i = 0; i < freeBlockCount; i++)
            {
                if (freeBlocks[i].Offset > offset)
                {
                    insertIndex = i;
                    break;
                }
            }

            Array.Copy(freeBlocks, insertIndex, freeBlocks, insertIndex + 1, freeBlockCount - insertIndex);
            freeBlocks[insertIndex] = newHole;
            freeBlockCount++;

            // Merge adjacent holes
            for (var i = 0; i < freeBlockCount - 1; i++)
            {
                if (freeBlocks[i].Offset + freeBlocks[i].Size == freeBlocks[i + 1].Offset)
                {
                    freeBlocks[i].Size += freeBlocks[i + 1].Size;
                    Array.Copy(freeBlocks, i + 2, freeBlocks, i + 1, freeBlockCount - i - 2);
                    freeBlockCount--;
                    i--; // Re-check the merged block against the next one
                }
            }
        }

        /// <summary>
        /// Debugs the dump.
        /// </summary>
        /// <param name="entries">The entries.</param>
        /// <param name="entryCount">The entry count.</param>
        /// <returns>
        /// Generic debug Dump
        /// </returns>
        internal static string DebugDump(AllocationEntry[] entries, int entryCount)
        {
            var sb = new StringBuilder(entryCount * 48);
            for (var i = 0; i < entryCount; i++)
            {
                var e = entries[i];
                sb.Append("[Lane] ID ").Append(e.HandleId)
                    .Append(" Offset ").Append(e.Offset)
                    .Append(" Size ").Append(e.Size)
                    .AppendLine();
            }

            return sb.ToString();
        }

        /// <summary>
        /// Debugs the visual map.
        /// </summary>
        /// <param name="entries">The entries.</param>
        /// <param name="capacity">The capacity.</param>
        /// <returns>
        /// Visual map of fragmentation.
        /// </returns>
        internal static string DebugVisualMap(ReadOnlySpan<AllocationEntry> entries, int capacity)
        {
            if (entries.Length == 0)
                return "Memory is empty";

            var sb = new StringBuilder();

            var pool = ArrayPool<AllocationEntry>.Shared;
            var scratch = pool.Rent(entries.Length);

            var validCount = 0;
            try
            {
                for (var i = 0; i < entries.Length; i++)
                {
                    ref readonly var e = ref entries[i];
                    if (!e.IsStub && e.HandleId != -1)
                    {
                        scratch[validCount++] = e;
                    }
                }

                var validSpan = scratch.AsSpan(0, validCount);
                validSpan.Sort(new OffsetComparer());

                sb.AppendLine("--- Dump Start ---");

                const int barWidth = 80;
                var visual = new char[barWidth];
                var allocationCoverage = new double[barWidth];
                var scale = barWidth / (double)capacity;

                for (var i = 0; i < validSpan.Length; i++)
                {
                    ref readonly var e = ref validSpan[i];

                    // Visual mapping handles canary alignment offsets cleanly
                    int physicalOffset = MemoryCanary.GetPhysicalOffset(e.Offset);
                    int physicalSize = MemoryCanary.GetPhysicalSize(e.Size);

                    double startByte = physicalOffset;
                    double endByte = physicalOffset + physicalSize;
                    var startIdx = (int)(startByte * scale);
                    var endIdx = (int)(endByte * scale);
                    endIdx = Math.Max(startIdx + 1, endIdx);
                    endIdx = Math.Min(barWidth, endIdx);

                    for (var j = startIdx; j < endIdx; j++)
                    {
                        var cellStart = j / scale;
                        var cellEnd = (j + 1) / scale;

                        var overlap = Math.Min(endByte, cellEnd) - Math.Max(startByte, cellStart);
                        var cellSize = cellEnd - cellStart;

                        if (overlap > 0) allocationCoverage[j] += overlap / cellSize;
                    }
                }

                char[] blocks = { '░', '▏', '▎', '▍', '▌', '▋', '▊', '▉', '█' };

                for (var i = 0; i < barWidth; i++)
                {
                    var coverage = allocationCoverage[i];
                    if (coverage <= 0.0)
                    {
                        visual[i] = '░';
                    }
                    else if (coverage < 0.125)
                    {
                        visual[i] = '.';
                    }
                    else
                    {
                        var blockIndex = (int)Math.Round(coverage * 8);
                        blockIndex = Math.Min(8, blockIndex);
                        visual[i] = blocks[blockIndex];
                    }
                }

                sb.AppendLine("Visual Map (░ = gap, . = very small allocation, ▏ to █ = partial/full allocation):");
#if NETCOREAPP
                sb.Append(visual);
                sb.AppendLine();
#else
                sb.AppendLine(new string(visual));
#endif
                sb.AppendLine($"Capacity: {capacity} bytes");
                sb.AppendLine();
                sb.AppendLine("--- Dump End ---");
            }
            finally
            {
                pool.Return(scratch);
            }

            return sb.ToString();
        }

        /// <summary>
        /// Gets the next identifier.
        /// SlowLane always counts up in a negative from -1
        /// FastLane always counts up in a positive from +1
        /// </summary>
        /// <param name="freeIds">The free ids.</param>
        /// <param name="nextHandleId">The next handle identifier.</param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int GetNextId(UnmanagedIntList freeIds, ref int nextHandleId)
        {
            if (freeIds.Length > 0)
                return freeIds.Pop();

            return nextHandleId >= 0 ? nextHandleId++ : nextHandleId--;
        }

        /// <summary>
        /// Debugs the redirections.
        /// </summary>
        /// <param name="entries">The entries.</param>
        /// <param name="entryCount">The entry count.</param>
        /// <param name="debugNames">The debug names.</param>
        /// <returns>Debug string representing the redirections.</returns>
        internal static string DebugRedirections(AllocationEntry[] entries, int entryCount,
            IReadOnlyDictionary<int, string>? debugNames = null)
        {
            var sb = new StringBuilder(entryCount * 64);
            for (var i = 0; i < entryCount; i++)
            {
                var e = entries[i];

                sb.Append("[Lane] ID ").Append(e.HandleId)
                    .Append(" Offset ").Append(e.Offset)
                    .Append(" Size ").Append(e.Size);

                if (e.IsStub)
                {
                    var redirectStr = e.RedirectToId != 0 ? e.RedirectToId.ToString() : "null";

                    sb.Append(" [STUB -> ID ")
                        .Append(redirectStr)
                        .Append("]");
                }

                if (debugNames != null && debugNames.TryGetValue(e.HandleId, out var name))
                {
                    if (!string.IsNullOrWhiteSpace(name))
                        sb.Append(" Name=").Append(name);
                }

                sb.AppendLine();
            }

            return sb.ToString();
        }

        /// <summary>
        /// Usages the percentage.
        /// </summary>
        /// <param name="entryCount">The entry count.</param>
        /// <param name="entries">The entries.</param>
        /// <param name="capacity">The capacity.</param>
        /// <returns>Usage percentage of the allocated entries.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static double UsagePercentage(int entryCount, AllocationEntry[]? entries, int capacity)
        {
            if (entries == null || capacity == 0) return 0.0;

            var used = 0;
            for (var i = 0; i < entryCount; i++)
                if (!entries[i].IsStub)
                {
                    // FIX: Tracks exact physical memory footprint constraints during density assessments
                    used += MemoryCanary.GetPhysicalSize(entries[i].Size);
                }

            return (double)used / capacity;
        }

        /// <summary>
        /// Determines whether the specified handle has handle.
        /// </summary>
        /// <param name="handle">The handle.</param>
        /// <param name="handleIndex">Index of the handle.</param>
        /// <returns>
        ///   <c>true</c> if the specified handle has handle; otherwise, <c>false</c>.
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool HasHandle(MemoryHandle handle, UnmanagedMap<int> handleIndex)
        {
            return handleIndex.ContainsKey(handle.Id);
        }

        /// <summary>
        /// Gets the entry.
        /// </summary>
        /// <param name="handle">The handle.</param>
        /// <param name="handleIndex">Index of the handle.</param>
        /// <param name="entries">The entries.</param>
        /// <param name="lane">The lane.</param>
        /// <returns>The allocation entry corresponding to the specified handle.</returns>
        /// <exception cref="ArgumentException">$"{lane}: Invalid handle</exception>
        /// <exception cref="InvalidOperationException">$"{lane}: Invalid handle</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static AllocationEntry GetEntry(MemoryHandle handle, UnmanagedMap<int> handleIndex,
            AllocationEntry[] entries, string lane)
        {
            if (entries == null)
                throw new ArgumentException($"{lane}: Invalid handle");

            if (!handleIndex.TryGetValue(handle.Id, out var index))
                throw new InvalidOperationException($"{lane}: Invalid handle");

            return entries[index];
        }

        /// <summary>
        /// Gets the size of the allocation.
        /// </summary>
        /// <param name="handle">The handle.</param>
        /// <param name="handleIndex">Index of the handle.</param>
        /// <param name="entries">The entries.</param>
        /// <param name="lane">The lane.</param>
        /// <returns>The size of the allocation corresponding to the specified handle.</returns>
        /// <exception cref="ArgumentException">$"{lane}: Invalid handle</exception>
        /// <exception cref="InvalidOperationException">$"{lane}: Invalid handle</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int GetAllocationSize(MemoryHandle handle, UnmanagedMap<int> handleIndex,
            AllocationEntry[] entries, string lane)
        {
            if (entries == null)
                throw new ArgumentException($"{lane}: Invalid handle");

            if (handleIndex.TryGetValue(handle.Id, out var index))
                return entries[index].Size;

            throw new InvalidOperationException($"{lane}: Invalid handle");
        }

        /// <summary>
        /// Ensures the entry capacity.
        /// </summary>
        /// <param name="entries">The entries.</param>
        /// <param name="requiredSlotIndex">Index of the required slot.</param>
        /// <returns>The new size of the entries array.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int EnsureEntryCapacity(ref AllocationEntry[] entries, int requiredSlotIndex)
        {
            if (requiredSlotIndex < entries.Length)
                return entries.Length;

            var newSize = Math.Max(entries.Length * 2, requiredSlotIndex + 1);
            Array.Resize(ref entries, newSize);

            return newSize;
        }

        /// <summary>
        /// A zero-allocation comparison struct for sorting allocations by offset.
        /// </summary>
        /// <seealso cref="System.Collections.Generic.IComparer&lt;MemoryManager.Core.AllocationEntry&gt;" />
        private struct OffsetComparer : IComparer<AllocationEntry>
        {
            public int Compare(AllocationEntry x, AllocationEntry y)
            {
                return x.Offset.CompareTo(y.Offset);
            }
        }
    }
}