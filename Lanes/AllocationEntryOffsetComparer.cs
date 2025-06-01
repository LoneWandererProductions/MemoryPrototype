/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     Lanes
 * FILE:        AllocationEntryOffsetComparer.cs
 * PURPOSE:     Your file purpose here
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

using System.Collections.Generic;
using Core.MemoryArenaPrototype.Core;

namespace Lanes
{
    // Helper comparer to sort entries by Offset ascending
    internal sealed class AllocationEntryOffsetComparer : IComparer<AllocationEntry>
    {
        public int Compare(AllocationEntry x, AllocationEntry y)
        {
            return x.Offset.CompareTo(y.Offset);
        }
    }
}