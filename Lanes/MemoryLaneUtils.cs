/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     Lanes
 * FILE:        MemoryLaneUtils.cs
 * PURPOSE:     Most shared methods of both lanes and most important all the debug stuff for both.
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Core;
using Core.MemoryArenaPrototype.Core;

namespace Lanes
{
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

            var sorted = entries.Take(entryCount).ToArray();
            Array.Sort(sorted, (a, b) => a.Offset.CompareTo(b.Offset));

            var free = sorted[0].Offset;

            for (var i = 1; i < entryCount; i++)
            {
                var endPrev = sorted[i - 1].Offset + sorted[i - 1].Size;
                var gap = sorted[i].Offset - endPrev;
                if (gap > 0) free += gap;
            }

            var lastEnd = sorted[entryCount - 1].Offset + sorted[entryCount - 1].Size;
            free += capacity - lastEnd;

            return free;
        }

        /// <summary>
        ///     Estimates the fragmentation.
        /// </summary>
        /// <param name="entries">The entries.</param>
        /// <param name="entryCount">The entry count.</param>
        /// <param name="capacity">The capacity.</param>
        /// <returns>Estimated fragmentation.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int EstimateFragmentation(IEnumerable<AllocationEntry> entries, int entryCount, int capacity)
        {
            if (entryCount == 0) return 0;

            var sorted = entries.Take(entryCount).ToArray();
            Array.Sort(sorted, (a, b) => a.Offset.CompareTo(b.Offset));

            var gapSum = sorted[0].Offset;

            for (var i = 1; i < entryCount; i++)
            {
                var endPrev = sorted[i - 1].Offset + sorted[i - 1].Size;
                var gap = sorted[i].Offset - endPrev;
                if (gap > 0) gapSum += gap;
            }

            var lastEnd = sorted[entryCount - 1].Offset + sorted[entryCount - 1].Size;
            gapSum += capacity - lastEnd;

            return (int)((double)gapSum / capacity * 100);
        }

        /// <summary>
        ///     Stubs the count.
        /// </summary>
        /// <param name="entryCount">The entry count.</param>
        /// <param name="entries">The entries.</param>
        /// <returns>Number of stubs, aka references from fast to slow Lane.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int StubCount(int entryCount, AllocationEntry[] entries)
        {
            var count = 0;
            for (var i = 0; i < entryCount; i++)
                if (entries[i].IsStub)
                    count++;

            return count;
        }

        /// <summary>
        ///     Finds the free spot.
        /// </summary>
        /// <param name="size">The size.</param>
        /// <param name="entries">The entries.</param>
        /// <param name="entryCount">The entry count.</param>
        /// <returns>Calculate free space.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int FindFreeSpot(int size, IEnumerable<AllocationEntry> entries, int entryCount)
        {
            var sorted = entries.Take(entryCount).ToArray();
            Array.Sort(sorted, (a, b) => a.Offset.CompareTo(b.Offset));

            var offset = 0;

            for (var i = 0; i < entryCount; i++)
            {
                var entry = sorted[i];
                if (offset + size <= entry.Offset)
                    return offset;

                offset = entry.Offset + entry.Size;
            }

            return offset;
        }

        /// <summary>
        ///     Debugs the dump.
        /// </summary>
        /// <param name="entries">The entries.</param>
        /// <param name="entryCount">The entry count.</param>
        /// <returns>Generic debug Dump</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static string DebugDump(AllocationEntry[] entries, int entryCount)
        {
            var sb = new StringBuilder(entryCount * 48);
            for (var i = 0; i < entryCount; i++)
            {
                var e = entries[i];
                sb.Append("[FastLane] ID ").Append(e.HandleId)
                    .Append(" Offset ").Append(e.Offset)
                    .Append(" Size ").Append(e.Size)
                    .AppendLine();
            }

            return sb.ToString();
        }

        /// <summary>
        ///     Debugs the visual map.
        /// </summary>
        /// <param name="entries">The entries.</param>
        /// <param name="entryCount">The entry count.</param>
        /// <param name="capacity">The capacity.</param>
        /// <returns>Visual map of fragmentation.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static string DebugVisualMap(IEnumerable<AllocationEntry> entries, int entryCount, int capacity)
        {
            if (entryCount == 0)
                return "Memory is empty";

            var sb = new StringBuilder();

            var validEntries = entries
                .Take(entryCount)
                .Where(e => !e.IsStub)
                .ToArray();

            sb.AppendLine("--- Dump Start ---");

            // Dump all allocations (for reference)
            foreach (var e in validEntries.OrderBy(e => e.HandleId))
            {
                sb.AppendLine($"[Lane] ID {e.HandleId} Offset {e.Offset} Size {e.Size}");
            }
            sb.AppendLine();

            // Logical order by HandleId
            sb.AppendLine("Logical Handle Order:");
            foreach (var e in validEntries.OrderBy(e => e.HandleId))
            {
                sb.AppendLine($"[ID {e.HandleId,3}] Offset {e.Offset:D6}-{e.Offset + e.Size:D6} ({e.Size} bytes)"
                              + (e.DebugName != null ? $" - {e.DebugName}" : ""));
            }
            sb.AppendLine();

            // Memory Layout order by Offset
            sb.AppendLine("Memory Layout (by Offset):");
            var sortedByOffset = validEntries.OrderBy(e => e.Offset).ToArray();

            // Debug info for entries
            foreach (var e in sortedByOffset)
            {
                sb.AppendLine($"[Check] Entry: Offset={e.Offset}, Size={e.Size}");
            }

            int lastEnd = 0;
            foreach (var e in sortedByOffset)
            {
                if (e.Offset > lastEnd)
                {
                    int gap = e.Offset - lastEnd;
                    sb.AppendLine($"[Gap ] {lastEnd:D6}-{e.Offset:D6} ({gap} bytes)");
                }

                sb.AppendLine($"[Used] {e.Offset:D6}-{e.Offset + e.Size:D6} (ID {e.HandleId}, {e.Size} bytes)");
                lastEnd = e.Offset + e.Size;
            }
            if (lastEnd < capacity)
                sb.AppendLine($"[Gap ] {lastEnd:D6}-{capacity:D6} ({capacity - lastEnd} bytes)");

            // Visual map with shading
            const int barWidth = 80;
            var visual = new char[barWidth];
            Array.Fill(visual, '░');  // Light shade for gaps

            foreach (var e in sortedByOffset)
            {
                double scale = barWidth / (double)capacity;
                int start = (int)(e.Offset * scale);
                int end = (int)((e.Offset + e.Size) * scale);
                end = Math.Max(start + 1, end);
                end = Math.Min(barWidth, end);

                for (int i = start; i < end; i++)
                {
                    visual[i] = '▓'; // Dark shade for allocations
                }
            }

            sb.AppendLine();
            sb.AppendLine("Visual Map (▓ = allocated, ░ = gap):");
            sb.AppendLine(new string(visual));
            sb.AppendLine($"Capacity: {capacity} bytes");

            sb.AppendLine();

            // Check for duplicate HandleIds
            var duplicates = validEntries
                .GroupBy(e => e.HandleId)
                .Where(g => g.Count() > 1)
                .ToList();

            if (duplicates.Any())
            {
                sb.AppendLine("⚠️  WARNING: Duplicate HandleIds detected!");
                foreach (var dup in duplicates)
                    sb.AppendLine($" - HandleId {dup.Key} appears {dup.Count()} times");
            }

            sb.AppendLine("--- Dump End ---");

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
        internal static int GetNextId(Stack<int> freeIds, ref int nextHandleId)
        {
            if (freeIds.Count > 0)
                return freeIds.Pop();

            return nextHandleId >= 0 ? nextHandleId++ : nextHandleId--;
        }

        /// <summary>
        ///     Debugs the redirections.
        /// </summary>
        /// <param name="entries">The entries.</param>
        /// <param name="entryCount">The entry count.</param>
        /// <returns>Display all information about stubs and forwarding.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static string DebugRedirections(AllocationEntry[] entries, int entryCount)
        {
            var sb = new StringBuilder(entryCount * 64);
            for (var i = 0; i < entryCount; i++)
            {
                var e = entries[i];
                sb.Append("[FastLane] ID ").Append(e.HandleId)
                    .Append(" Offset ").Append(e.Offset)
                    .Append(" Size ").Append(e.Size);

                if (e.IsStub)
                    sb.Append(" [STUB -> ID ")
                        .Append(e.RedirectTo?.Id.ToString() ?? "null")
                        .Append("]");

                if (!string.IsNullOrWhiteSpace(e.DebugName))
                    sb.Append(" Name=").Append(e.DebugName);

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
        internal static bool HasHandle(MemoryHandle handle, Dictionary<int, int> handleIndex)
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
        internal static AllocationEntry GetEntry(MemoryHandle handle, Dictionary<int, int> handleIndex,
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
        internal static int GetAllocationSize(MemoryHandle handle, Dictionary<int, int> handleIndex,
            AllocationEntry[] entries, string lane)
        {
            if (entries == null)
                throw new ArgumentException($"{lane}: Invalid handle");

            if (handleIndex.TryGetValue(handle.Id, out var index))
                return entries[index].Size;

            throw new InvalidOperationException($"{lane}: Invalid handle");
        }
    }
}