/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     MemoryManager.Types
 * FILE:        ArenaPriorityQueueNode.cs
 * PURPOSE:     Internal node struct for ArenaPriorityQueue min-heap storage.
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

namespace MemoryManager.Types.Structs
{
    /// <summary>
    /// Internal node struct for <see cref="ArenaPriorityQueue{TElement, TPriority}"/>.
    /// </summary>
    /// <typeparam name="TElement">The payload element type.</typeparam>
    /// <typeparam name="TPriority">The priority type.</typeparam>
    internal struct ArenaPriorityQueueNode<TElement, TPriority>
        where TElement : unmanaged
        where TPriority : unmanaged, IComparable<TPriority>
    {
        /// <summary>
        /// The element
        /// </summary>
        public TElement Element;

        /// <summary>
        /// The priority
        /// </summary>
        public TPriority Priority;
    }
}