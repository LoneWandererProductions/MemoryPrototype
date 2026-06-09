/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     MemoryManager.Types
 * FILE:        ArenaQueue.cs
 * PURPOSE:     A high-performance, resizable unmanaged Ring Buffer (Circular Queue).
 *              Manages wrap-around index alignment and unmanaged data unwrapping.
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

using MemoryManager.Core;
using System.Runtime.CompilerServices;

namespace MemoryManager.Types
{
    /// <summary>
    /// A high-performance, zero-allocation circular queue (Ring Buffer) for unmanaged types,
    /// backed by an <see cref="IMemoryAllocator"/>.
    /// </summary>
    /// <typeparam name="T">The unmanaged type to store.</typeparam>
    public sealed class ArenaQueue<T> : IDisposable where T : unmanaged
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
        /// The handle
        /// </summary>
        private MemoryHandle _handle;

        /// <summary>
        /// The capacity
        /// </summary>
        private int _capacity;

        /// <summary>
        /// The head
        /// </summary>
        private int _head;

        /// <summary>
        /// The tail
        /// </summary>
        private int _tail;

        /// <summary>
        /// The count
        /// </summary>
        private int _count;

        /// <summary>
        /// Gets the number of elements currently waiting in the queue.
        /// </summary>
        public int Count => _count;

        /// <summary>
        /// Gets the total size of the internal native ring loop before a resize triggers.
        /// </summary>
        public int Capacity => _capacity;

        /// <summary>
        /// Initializes a new instance of the <see cref="ArenaQueue{T}" /> class.
        /// </summary>
        /// <param name="arena">The arena.</param>
        /// <param name="initialCapacity">The initial capacity.</param>
        /// <param name="priority">The priority.</param>
        /// <param name="hints">The hints.</param>
        public ArenaQueue(IMemoryAllocator arena, int initialCapacity = 8, AllocationPriority priority = AllocationPriority.Normal, AllocationHints hints = AllocationHints.None)
        {
            if (initialCapacity <= 0) initialCapacity = 8;

            _arena = arena;
            _capacity = initialCapacity;
            _priority = priority;
            _hints = hints;

            // Allocate the circular block segment tracking structural alignment properties
            _handle = _arena.Allocate(Unsafe.SizeOf<T>() * _capacity, _priority, _hints);
            _head = 0;
            _tail = 0;
            _count = 0;
        }

        /// <summary>
        /// Pushes an item to the back of the circular queue, automatically growing the array if full.
        /// </summary>
        /// <param name="item">The item.</param>
        public void Enqueue(T item)
        {
            if (_count == _capacity) Grow();

            var span = _arena.GetSpan<T>(_handle, _capacity);
            span[_tail] = item;

            // The Clockwork Magic: Wrap index back around to 0 if it overshoots the hardware boundary
            _tail = (_tail + 1) % _capacity;
            _count++;
        }

        /// <summary>
        /// Pops and returns the oldest item from the front of the circular queue.
        /// </summary>
        /// <returns></returns>
        /// <exception cref="System.InvalidOperationException">The ArenaQueue is empty.</exception>
        public T Dequeue()
        {
            if (_count == 0)
                throw new InvalidOperationException("The ArenaQueue is empty.");

            var span = _arena.GetSpan<T>(_handle, _capacity);
            T item = span[_head];

            // Advance the front coordinate tracking pointer around the ring loop
            _head = (_head + 1) % _capacity;
            _count--;

            return item;
        }

        /// <summary>
        /// Evaluates the front-most item without popping it from the data lane buffer.
        /// </summary>
        /// <returns></returns>
        /// <exception cref="System.InvalidOperationException">The ArenaQueue is empty.</exception>
        public ref T Peek()
        {
            if (_count == 0)
                throw new InvalidOperationException("The ArenaQueue is empty.");

            var span = _arena.GetSpan<T>(_handle, _capacity);
            return ref span[_head];
        }

        /// <summary>
        /// Resets structural coordinates to zero. Memory retains previous properties until reclamation sweeps.
        /// </summary>
        public void Clear()
        {
            _head = 0;
            _tail = 0;
            _count = 0;
        }

        /// <summary>
        /// The Unwrapping Engine: Allocates a double-sized ring loop and cleanly flattens
        /// segmented wrap-around data fragments into a unified linear coordinate system.
        /// </summary>
        private void Grow()
        {
            var newCapacity = _capacity * 2;
            var newHandle = _arena.Allocate(Unsafe.SizeOf<T>() * newCapacity, _priority, _hints);

            var oldSpan = _arena.GetSpan<T>(_handle, _capacity);
            var newSpan = _arena.GetSpan<T>(newHandle, newCapacity);

            if (_count > 0)
            {
                // Case 1: Contiguous layout block (Head is positioned behind Tail)
                if (_head < _tail)
                {
                    oldSpan.Slice(_head, _count).CopyTo(newSpan);
                }
                // Case 2: Fractured layout block (Tail wrapped around and is positioned ahead of Head)
                else
                {
                    // Step A: Copy from Head to the physical end of the old unmanaged buffer array block
                    var headSegmentSize = _capacity - _head;
                    oldSpan.Slice(_head, headSegmentSize).CopyTo(newSpan);

                    // Step B: Copy remaining wrapped trailing elements from index 0 up to the Tail marker position
                    oldSpan.Slice(0, _tail).CopyTo(newSpan.Slice(headSegmentSize));
                }
            }

            // Recycle old positions and update spatial properties
            _arena.Free(_handle);
            _handle = newHandle;
            _head = 0;
            _tail = _count; // Tail is now cleanly positioned right after our linear block data cluster
            _capacity = newCapacity;
        }

        /// <summary>
        /// Cleans up unmanaged boundaries back to native runtime engines safely.
        /// </summary>
        public void Dispose()
        {
            if (!_handle.IsInvalid)
            {
                _arena.Free(_handle);
                _handle = default;
            }
        }
    }
}