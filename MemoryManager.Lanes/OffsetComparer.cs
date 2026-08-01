/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     MemoryManager.Lanes
 * FILE:        OffsetComparer.cs
 * PURPOSE:     A zero-allocation comparison struct for sorting allocations by offset.
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

using MemoryManager.Core;

namespace MemoryManager.Lanes
{
    /// <summary>
    /// A zero-allocation comparison struct for sorting allocations by offset.
    /// </summary>
    /// <seealso cref="System.Collections.Generic.IComparer&lt;MemoryManager.Core.AllocationEntry&gt;" />
    public struct OffsetComparer : IComparer<AllocationEntry>
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
        public readonly int Compare(AllocationEntry x, AllocationEntry y)
        {
            return x.Offset.CompareTo(y.Offset);
        }
    }
}