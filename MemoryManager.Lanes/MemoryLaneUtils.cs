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
        ///     Calculates the free space.
        /// </summary>
        /// <param name="entries">The entries.</param>
        /// <param name="entryCount">The entry count.</param>
        /// <param name="capacity">The capacity.</param>
        /// <returns>Free space of Lane.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int CalculateFreeSpace(AllocationEntry[] entries, int entryCount, int capacity)
        {
            if (entryCount == 0) return capacity;

            var usedSpace = 0;
            for (var i = 0; i < entryCount; i++)
            {
                // If your array contains freed/invalid entries that shouldn't be counted,
                // add a check here (e.g., if (!entries[i].IsStub))
                usedSpace += entries[i].Size;
            }

            return capacity - usedSpace;
        }

        /// <summary>
        ///     Estimates the fragmentation.
        /// </summary>
        /// <param name="entries">The entries.</param>
        /// <param name="entryCount">The entry count.</param>
        /// <returns>Estimated fragmentation.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int EstimateFragmentation(AllocationEntry[] entries, int entryCount)
        {
            if (entryCount == 0) return 0;

            var lastEnd = 0;
            var usedSpace = 0;

            for (var i = 0; i < entryCount; i++)
            {
                // Access by ref to avoid copying the struct
                ref var entry = ref entries[i];

                usedSpace += entry.Size;

                var end = entry.Offset + entry.Size;
                if (end > lastEnd)
                {
                    lastEnd = end;
                }
            }

            var gapSum = lastEnd - usedSpace;
            return lastEnd == 0 ? 0 : (int)((double)gapSum / lastEnd * 100);
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
            // Using 'ref readonly' prevents the struct from being copied into a local variable
            foreach (ref readonly var entry in entries)
            {
                if (entry.IsStub) count++;
            }

            return count;
        }

        /// <summary>
        /// Finds a free spot using a Free-List approach (Zero Allocation, O(N) over holes, not allocations)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int FindFreeSpot(int size, ref FreeBlock[] freeBlocks, ref int freeBlockCount)
        {
            for (var i = 0; i < freeBlockCount; i++)
            {
                if (freeBlocks[i].Size >= size)
                {
                    var assignedOffset = freeBlocks[i].Offset;

                    // Shrink the hole
                    freeBlocks[i].Offset += size;
                    freeBlocks[i].Size -= size;

                    // If hole is fully consumed, remove it by swapping with the last item
                    if (freeBlocks[i].Size == 0)
                    {
                        freeBlockCount--;
                        freeBlocks[i] = freeBlocks[freeBlockCount];
                    }

                    return assignedOffset;
                }
            }

            return -1; // -1 indicates Out of Memory
        }

        /// <summary>
        /// Returns memory to the free list and merges adjacent blocks to prevent fragmentation.
        /// </summary>
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
        ///     Debugs the dump.
        /// </summary>
        /// <param name="entries">The entries.</param>
        /// <param name="entryCount">The entry count.</param>
        /// <returns>Generic debug Dump</returns>
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
            // The span's length represents the current active entry count
            if (entries.Length == 0)
                return "Memory is empty";

            var sb = new StringBuilder();

            // 1. Rent a temporary scratch buffer from the shared pool to avoid heap allocations
            var pool = ArrayPool<AllocationEntry>.Shared;
            var scratch = pool.Rent(entries.Length);

            var validCount = 0;
            try
            {
                // 2. Filter out stubs and invalid IDs manually (Replaces .Take().Where())
                for (var i = 0; i < entries.Length; i++)
                {
                    ref readonly var e = ref entries[i];
                    if (!e.IsStub && e.HandleId != -1)
                    {
                        scratch[validCount++] = e;
                    }
                }

                // 3. Slice our scratch array to exactly how many valid entries we found and sort it
                var validSpan = scratch.AsSpan(0, validCount);

                // Passing a custom struct comparison instead of a lambda avoids delegate allocation
                validSpan.Sort(new OffsetComparer());

                sb.AppendLine("--- Dump Start ---");

                const int barWidth = 80;
                var visual = new char[barWidth];
                var allocationCoverage = new double[barWidth];
                var scale = barWidth / (double)capacity;

                // 4. Process the sorted entries
                for (var i = 0; i < validSpan.Length; i++)
                {
                    ref readonly var e = ref validSpan[i];
                    double startByte = e.Offset;
                    double endByte = e.Offset + e.Size;
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

                // Define partial blocks (8 levels + dot)
                char[] blocks = { '░', '▏', '▎', '▍', '▌', '▋', '▊', '▉', '█' };

                for (var i = 0; i < barWidth; i++)
                {
                    var coverage = allocationCoverage[i];
                    if (coverage <= 0.0)
                    {
                        visual[i] = '░'; // free
                    }
                    else if (coverage < 0.125)
                    {
                        visual[i] = '.'; // very small allocation
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
                sb.Append(visual); // Directly appends char span without allocation in modern .NET
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
                // Always return the rented array back to the pool, even if something crashed
                pool.Return(scratch);
            }

            return sb.ToString();
        }

        /// <summary>
        ///     Gets the next identifier.
        ///     SlowLane always counts up in a negative from -1
        ///     FastLane always counts up in a positive from +1
        /// </summary>
        /// <param name="freeIds">The free ids.</param>
        /// <param name="nextHandleId">The next handle identifier.</param>
        /// <returns>First free next Id for Handler.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int GetNextId(UnmanagedIntList freeIds, ref int nextHandleId)
        {
            if (freeIds.Length > 0)
                return freeIds.Pop();

            return nextHandleId >= 0 ? nextHandleId++ : nextHandleId--;
        }

        /// <summary>
        ///     Debugs the redirections.
        /// </summary>
        /// <param name="entries">The entries.</param>
        /// <param name="entryCount">The entry count.</param>
        /// <param name="debugNames">Optional dictionary mapping HandleId to DebugName.</param>
        /// <returns>Display all information about stubs and forwarding.</returns>
        internal static string DebugRedirections(AllocationEntry[] entries, int entryCount,
            IReadOnlyDictionary<int, string>? debugNames = null)
        {
            var sb = new StringBuilder(entryCount * 64);
            for (var i = 0; i < entryCount; i++)
            {
                var e = entries[i];

                // Changed [FastLane] to [Lane] since SlowLane uses this utility too!
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

                // Look up the name in the dictionary instead of the struct
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
        ///     Usages the percentage.
        /// </summary>
        /// <param name="entryCount">The entry count.</param>
        /// <param name="entries">The entries.</param>
        /// <param name="capacity">The capacity.</param>
        /// <returns>Used space in percentage</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static double UsagePercentage(int entryCount, AllocationEntry[]? entries, int capacity)
        {
            if (entries == null || capacity == 0) return 0.0;

            var used = 0;
            for (var i = 0; i < entryCount; i++)
                if (!entries[i].IsStub)
                    used += entries[i].Size;

            return (double)used / capacity;
        }

        /// <summary>
        ///     Determines whether the specified handle has handle.
        /// </summary>
        /// <param name="handle">The handle.</param>
        /// <param name="handleIndex">Index of the handle.</param>
        /// <returns>
        ///     <c>true</c> if the specified handle has handle; otherwise, <c>false</c>.
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool HasHandle(MemoryHandle handle, UnmanagedMap<int> handleIndex)
        {
            return handleIndex.ContainsKey(handle.Id);
        }

        /// <summary>
        ///     Gets the entry.
        /// </summary>
        /// <param name="handle">The handle.</param>
        /// <param name="handleIndex">Index of the handle.</param>
        /// <param name="entries">The entries.</param>
        /// <param name="lane">The lane.</param>
        /// <returns>Get the entry by handle.</returns>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="InvalidOperationException"></exception>
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
        ///     Gets the size of the allocation.
        /// </summary>
        /// <param name="handle">The handle.</param>
        /// <param name="handleIndex">Index of the handle.</param>
        /// <param name="entries">The entries.</param>
        /// <param name="lane">The lane.</param>
        /// <returns>Calculate size for the allocation.</returns>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="InvalidOperationException"></exception>
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
        ///     Ensures the entry capacity.
        /// </summary>
        /// <param name="entries">The entries.</param>
        /// <param name="requiredSlotIndex">Index of the required slot.</param>
        /// <returns>New size of Allocation array.</returns>
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