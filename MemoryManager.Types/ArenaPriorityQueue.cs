/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     MemoryManager.Types
 * FILE:        ArenaPriorityQueue.cs
 * PURPOSE:     An unmanaged binary min-heap for zero-GC priority queue operations (A* pathfinding, timers).
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

using System.Runtime.CompilerServices;
using MemoryManager.Core;
using MemoryManager.Types.Structs;

namespace MemoryManager.Types
{
    /// <inheritdoc />s
    /// <summary>
    /// A high-performance unmanaged Binary Min-Heap Priority Queue backed by an <see cref="IMemoryAllocator"/>.
    /// </summary>
    /// <typeparam name="TElement">The payload element type.</typeparam>
    /// <typeparam name="TPriority">The priority type (must implement IComparable).</typeparam>
    public sealed class ArenaPriorityQueue<TElement, TPriority> : IDisposable
        where TElement : unmanaged
        where TPriority : unmanaged, IComparable<TPriority>
    {
        /// <summary>
        /// The arena
        /// </summary>
        private readonly IMemoryAllocator _arena;

        /// <summary>
        /// The priority
        /// </summary>
        private readonly AllocationPriority _priority;

        /// <summary>
        /// The hints
        /// </summary>
        private readonly AllocationHints _hints;

        /// <summary>
        /// The nodes handle
        /// </summary>
        private MemoryHandle _nodesHandle;

        /// <summary>
        /// The capacity
        /// </summary>
        private int _capacity;

        /// <summary>
        /// The count
        /// </summary>
        private int _count;

        /// <summary>
        /// Gets the count.
        /// </summary>
        /// <value>
        /// The count.
        /// </value>
        public int Count => _count;

        /// <summary>
        /// Gets the capacity.
        /// </summary>
        /// <value>
        /// The capacity.
        /// </value>
        public int Capacity => _capacity;

        /// <summary>
        /// Initializes a new instance of the <see cref="ArenaPriorityQueue{TElement, TPriority}"/> class.
        /// </summary>
        /// <param name="arena">The arena.</param>
        /// <param name="initialCapacity">The initial capacity.</param>
        /// <param name="priority">The priority.</param>
        /// <param name="hints">The hints.</param>
        /// <exception cref="System.ArgumentNullException">arena - Cannot instantiate {GetType().Name} without a valid allocator.</exception>
        public ArenaPriorityQueue(IMemoryAllocator arena, int initialCapacity = 16,
            AllocationPriority priority = AllocationPriority.Normal, AllocationHints hints = AllocationHints.None)
        {
            _arena = arena ?? throw new ArgumentNullException(nameof(arena), $"Cannot instantiate {GetType().Name} without a valid allocator.");

            if (initialCapacity <= 0) initialCapacity = 16;

            _capacity = initialCapacity;
            _priority = priority;
            _hints = hints;

            _nodesHandle = _arena.Allocate(Unsafe.SizeOf<ArenaPriorityQueueNode<TElement, TPriority>>() * _capacity, _priority, _hints);
        }

        /// <summary>
        /// Enqueues the specified element.
        /// </summary>
        /// <param name="element">The element.</param>
        /// <param name="priority">The priority.</param>
        public void Enqueue(in TElement element, in TPriority priority)
        {
            if (_count == _capacity) Grow();

            var nodes = _arena.GetSpan<ArenaPriorityQueueNode<TElement, TPriority>>(_nodesHandle, _capacity);
            int nodeIndex = _count++;

            nodes[nodeIndex] = new ArenaPriorityQueueNode<TElement, TPriority> { Element = element, Priority = priority };
            SiftUp(nodes, nodeIndex);
        }

        /// <summary>
        /// Dequeues this instance.
        /// </summary>
        /// <returns>The root Element</returns>
        /// <exception cref="System.InvalidOperationException">The Priority Queue is empty.</exception>
        public TElement Dequeue()
        {
            if (_count == 0)
                throw new InvalidOperationException("The Priority Queue is empty.");

            var nodes = _arena.GetSpan<ArenaPriorityQueueNode<TElement, TPriority>>(_nodesHandle, _capacity);
            TElement rootElement = nodes[0].Element;

            int lastIndex = --_count;
            if (lastIndex > 0)
            {
                nodes[0] = nodes[lastIndex]; // Move last leaf to root position
                SiftDown(nodes, 0);          // Restore min-heap ordering
            }

            return rootElement;
        }

        /// <summary>
        /// Peeks the specified priority.
        /// </summary>
        /// <param name="priority">The priority.</param>
        /// <returns>Referenced Element with specified priority.</returns>
        /// <exception cref="System.InvalidOperationException">The Priority Queue is empty.</exception>
        public ref TElement Peek(out TPriority priority)
        {
            if (_count == 0)
                throw new InvalidOperationException("The Priority Queue is empty.");

            var nodes = _arena.GetSpan<ArenaPriorityQueueNode<TElement, TPriority>>(_nodesHandle, _capacity);
            priority = nodes[0].Priority;
            return ref nodes[0].Element;
        }

        /// <summary>
        /// Clears this instance.
        /// </summary>
        public void Clear() => _count = 0;

        /// <summary>
        /// Sifts up.
        /// </summary>
        /// <param name="nodes">The nodes.</param>
        /// <param name="index">The index.</param>
        private static void SiftUp(Span<ArenaPriorityQueueNode<TElement, TPriority>> nodes, int index)
        {
            while (index > 0)
            {
                int parentIndex = (index - 1) >> 1;

                // If current priority >= parent priority, min-heap property holds
                if (nodes[index].Priority.CompareTo(nodes[parentIndex].Priority) >= 0)
                    break;

                // Swap parent and child
                (nodes[index], nodes[parentIndex]) = (nodes[parentIndex], nodes[index]);
                index = parentIndex;
            }
        }

        /// <summary>
        /// Sifts down.
        /// </summary>
        /// <param name="nodes">The nodes.</param>
        /// <param name="index">The index.</param>
        private void SiftDown(Span<ArenaPriorityQueueNode<TElement, TPriority>> nodes, int index)
        {
            int half = _count >> 1;

            while (index < half)
            {
                int leftChild = (index << 1) + 1;
                int rightChild = leftChild + 1;
                int smallestChild = leftChild;

                if (rightChild < _count && nodes[rightChild].Priority.CompareTo(nodes[leftChild].Priority) < 0)
                {
                    smallestChild = rightChild;
                }

                if (nodes[index].Priority.CompareTo(nodes[smallestChild].Priority) <= 0)
                    break;

                // Swap with smaller child
                (nodes[index], nodes[smallestChild]) = (nodes[smallestChild], nodes[index]);
                index = smallestChild;
            }
        }

        /// <summary>
        /// Grows this instance.
        /// </summary>
        private void Grow()
        {
            var newCapacity = _capacity * 2;
            var newHandle = _arena.Allocate(Unsafe.SizeOf<ArenaPriorityQueueNode<TElement, TPriority>>() * newCapacity, _priority, _hints);

            var oldNodes = _arena.GetSpan<ArenaPriorityQueueNode<TElement, TPriority>>(_nodesHandle, _capacity);
            var newNodes = _arena.GetSpan<ArenaPriorityQueueNode<TElement, TPriority>>(newHandle, newCapacity);

            oldNodes.CopyTo(newNodes);
            _arena.Free(_nodesHandle);

            _nodesHandle = newHandle;
            _capacity = newCapacity;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (!_nodesHandle.IsInvalid) _arena.Free(_nodesHandle);
            _nodesHandle = default;
            _count = 0;
        }
    }
}