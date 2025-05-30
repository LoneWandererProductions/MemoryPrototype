using Core;
using Core.MemoryArenaPrototype.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Lanes
{
    internal static class MemoryLaneUtils
    {
        internal static int CalculateFreeSpace(AllocationEntry[] entries, int entryCount, int capacity)
        {
            if (entryCount == 0) return capacity;

            var sortedEntries = entries.Take(entryCount).OrderBy(e => e.Offset).ToArray();

            var freeSpace = sortedEntries[0].Offset;

            for (var i = 1; i < sortedEntries.Length; i++)
            {
                var gap = sortedEntries[i].Offset - (sortedEntries[i - 1].Offset + sortedEntries[i - 1].Size);
                if (gap > 0)
                    freeSpace += gap;
            }

            freeSpace += capacity - (sortedEntries[^1].Offset + sortedEntries[^1].Size);

            return freeSpace;
        }

        internal static int EstimateFragmentation(AllocationEntry[] entries, int entryCount, int capacity)
        {
            if (entryCount == 0) return 0;

            var sortedEntries = entries.Take(entryCount).OrderBy(e => e.Offset).ToArray();

            var totalGap = sortedEntries[0].Offset;

            for (var i = 1; i < sortedEntries.Length; i++)
            {
                var gap = sortedEntries[i].Offset - (sortedEntries[i - 1].Offset + sortedEntries[i - 1].Size);
                if (gap > 0)
                    totalGap += gap;
            }

            totalGap += capacity - (sortedEntries[^1].Offset + sortedEntries[^1].Size);

            return (int)((double)totalGap / capacity * 100);
        }

        // Add other helpers as needed...
        internal static int StubCount(int entryCount, AllocationEntry[]? entries)
        {
            var count = 0;
            for (var i = 0; i < entryCount; i++)
                if (entries?[i].IsStub == true)
                    count++;
            return count;
        }

        internal static int FindFreeSpot(int size, AllocationEntry[] entries, int entryCount)
        {
            var offset = 0;
            for (var i = 0; i < entryCount; i++)
            {
                var entry = entries[i];
                if (offset + size <= entry.Offset)
                    return offset;

                offset = entry.Offset + entry.Size;
            }

            return offset;
        }

        /// <summary>
        /// Debugs the dump.
        /// </summary>
        /// <param name="entries">The entries.</param>
        /// <param name="entryCount">The entry count.</param>
        /// <returns>Debug Dump</returns>
        internal static string DebugDump(AllocationEntry[] entries, int entryCount)
        {
            var sb = new System.Text.StringBuilder(entryCount * 48); // Rough estimate per line
            for (var i = 0; i < entryCount; i++)
            {
                var entry = entries[i];
                sb.Append("[FastLane] ID ").Append(entry.HandleId)
                    .Append(" Offset ").Append(entry.Offset)
                    .Append(" Size ").Append(entry.Size)
                    .AppendLine();
            }

            return sb.ToString();
        }

        /// <summary>
        /// Usages the percentage.
        /// </summary>
        /// <param name="entryCount">The entry count.</param>
        /// <param name="entries">The entries.</param>
        /// <param name="capacity">The capacity.</param>
        /// <returns>Used space Percentage</returns>
        internal static double UsagePercentage(int entryCount, AllocationEntry[]? entries, int capacity)
        {
            var used = 0;
            for (var i = 0; i < entryCount; i++)
                if (entries?[i].IsStub == false) used += entries[i].Size;

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
        internal static bool HasHandle(MemoryHandle handle, Dictionary<int, int> handleIndex)
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
        /// <returns>Data from allocated space</returns>
        /// <exception cref="System.InvalidOperationException"></exception>
        internal static AllocationEntry GetEntry(MemoryHandle handle, Dictionary<int, int> handleIndex, AllocationEntry[] entries, string lane)
        {
            if (entries == null) new ArgumentException($"{lane}: Invalid handle");

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
        /// <returns>Size of the allocated data.</returns>
        /// <exception cref="System.InvalidOperationException"></exception>
        internal static int GetAllocationSize(MemoryHandle handle, Dictionary<int, int> handleIndex, AllocationEntry[] entries, string lane)
        {
            if (entries == null) new ArgumentException($"{lane}: Invalid handle");

            if (handleIndex.TryGetValue(handle.Id, out int index))
            {
                return entries[index].Size;
            }
            throw new InvalidOperationException($"{lane}: Invalid handle");
        }
    }
}