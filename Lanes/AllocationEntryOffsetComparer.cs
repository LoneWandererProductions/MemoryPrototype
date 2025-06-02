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
    /// <summary>
    /// Helper comparer to sort entries by Offset ascending
    /// </summary>
    /// <seealso cref="System.Collections.Generic.IComparer&lt;Core.MemoryArenaPrototype.Core.AllocationEntry&gt;" />
    internal sealed class AllocationEntryOffsetComparer : IComparer<AllocationEntry>
    {
        /// <summary>
        /// Compares two objects and returns a value indicating whether one is less than, equal to, or greater than the other.
        /// </summary>
        /// <param name="x">The first object to compare.</param>
        /// <param name="y">The second object to compare.</param>
        /// <returns>
        /// A signed integer that indicates the relative values of <paramref name="x" /> and <paramref name="y" />, as shown in the following table.
        /// <list type="table"><listheader><term> Value</term><description> Meaning</description></listheader><item><term> Less than zero</term><description><paramref name="x" /> is less than <paramref name="y" />.</description></item><item><term> Zero</term><description><paramref name="x" /> equals <paramref name="y" />.</description></item><item><term> Greater than zero</term><description><paramref name="x" /> is greater than <paramref name="y" />.</description></item></list>
        /// </returns>
        public int Compare(AllocationEntry x, AllocationEntry y)
        {
            return x.Offset.CompareTo(y.Offset);
        }
    }
}