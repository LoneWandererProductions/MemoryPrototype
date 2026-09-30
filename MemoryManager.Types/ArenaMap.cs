/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     MemoryManager.Types
 * FILE:        ArenaMap.cs
 * PURPOSE:     A zero-GC open-addressing hash table backed by an IMemoryAllocator.
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

using MemoryManager.Core;
using MemoryManager.Types.Enums;
using MemoryManager.Types.Structs;
using System.Runtime.CompilerServices;

namespace MemoryManager.Types
{
    /// <inheritdoc />
    /// <summary>
    /// A zero-GC open-addressing hash table for unmanaged keys and values using linear probing.
    /// </summary>
    /// <typeparam name="TKey">The unmanaged key type.</typeparam>
    /// <typeparam name="TValue">The unmanaged value type.</typeparam>
    public sealed class ArenaMap<TKey, TValue> : IDisposable
        where TKey : unmanaged, IEquatable<TKey>
        where TValue : unmanaged
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
        /// The entries handle
        /// </summary>
        private MemoryHandle _entriesHandle;

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
        /// Initializes a new instance of the <see cref="ArenaMap{TKey, TValue}"/> class.
        /// </summary>
        /// <param name="arena">The arena.</param>
        /// <param name="initialCapacity">The initial capacity.</param>
        /// <param name="priority">The priority.</param>
        /// <param name="hints">The hints.</param>
        /// <exception cref="System.ArgumentNullException">arena - Cannot instantiate {GetType().Name} without a valid allocator.</exception>
        public ArenaMap(IMemoryAllocator arena, int initialCapacity = 16,
            AllocationPriority priority = AllocationPriority.Normal, AllocationHints hints = AllocationHints.None)
        {
            _arena = arena ?? throw new ArgumentNullException(nameof(arena), $"Cannot instantiate {GetType().Name} without a valid allocator.");

            // Capacity must be a power of two for fast bitwise modulo operations (hash & (_capacity - 1))
            _capacity = GetNextPowerOfTwo(initialCapacity < 8 ? 8 : initialCapacity);
            _priority = priority;
            _hints = hints;

            _entriesHandle = _arena.Allocate(Unsafe.SizeOf<ArenaMapEntry<TKey, TValue>>() * _capacity, _priority, _hints);
            Clear();
        }

        /// <summary>
        /// Adds the or update.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="value">The value.</param>
        public void AddOrUpdate(in TKey key, in TValue value)
        {
            // Maintain a max load factor of ~70% to prevent linear probing cluster slowdowns
            if (_count >= _capacity * 0.7)
            {
                Grow();
            }

            var entries = _arena.GetSpan<ArenaMapEntry<TKey, TValue>>(_entriesHandle, _capacity);
            int mask = _capacity - 1;
            int index = (int)((uint)key.GetHashCode() & mask);
            int firstTombstone = -1;

            while (entries[index].State != SlotState.Free)
            {
                if (entries[index].State == SlotState.Occupied && entries[index].Key.Equals(key))
                {
                    entries[index].Value = value; // Key exists, update value
                    return;
                }

                if (entries[index].State == SlotState.Tombstone && firstTombstone == -1)
                {
                    firstTombstone = index; // Track first reusable deleted slot
                }

                index = (index + 1) & mask; // Linear probe next slot
            }

            // Reuse tombstone if available, otherwise use free slot
            int insertIndex = firstTombstone != -1 ? firstTombstone : index;
            entries[insertIndex] = new ArenaMapEntry<TKey, TValue>
            {
                Key = key,
                Value = value,
                State = SlotState.Occupied
            };

            _count++;
        }

        /// <summary>
        /// Tries the get value.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="value">The value.</param>
        /// <returns>Refernce the value <true> if value is available, <false> if nothing was referenced.</returns>
        public bool TryGetValue(in TKey key, out TValue value)
        {
            var entries = _arena.GetSpan<ArenaMapEntry<TKey, TValue>>(_entriesHandle, _capacity);
            int mask = _capacity - 1;
            int index = (int)((uint)key.GetHashCode() & mask);

            while (entries[index].State != SlotState.Free)
            {
                if (entries[index].State == SlotState.Occupied && entries[index].Key.Equals(key))
                {
                    value = entries[index].Value;
                    return true;
                }

                index = (index + 1) & mask;
            }

            value = default;
            return false;
        }

        /// <summary>
        /// Removes the specified key.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <returns>Removes item <true> if successful, else <false> if no key was removed.</returns>
        public bool Remove(in TKey key)
        {
            var entries = _arena.GetSpan<ArenaMapEntry<TKey, TValue>>(_entriesHandle, _capacity);
            int mask = _capacity - 1;
            int index = (int)((uint)key.GetHashCode() & mask);

            while (entries[index].State != SlotState.Free)
            {
                if (entries[index].State == SlotState.Occupied && entries[index].Key.Equals(key))
                {
                    // Mark as Tombstone to avoid breaking probe chains for keys inserted past this index
                    entries[index].State = SlotState.Tombstone;
                    entries[index].Key = default;
                    entries[index].Value = default;
                    _count--;
                    return true;
                }

                index = (index + 1) & mask;
            }

            return false;
        }

        /// <summary>
        /// Clears this instance.
        /// </summary>
        public void Clear()
        {
            var entries = _arena.GetSpan<ArenaMapEntry<TKey, TValue>>(_entriesHandle, _capacity);
            entries.Clear();
            _count = 0;
        }

        /// <summary>
        /// Grows this instance.
        /// </summary>
        private void Grow()
        {
            int oldCapacity = _capacity;
            var oldEntries = _arena.GetSpan<ArenaMapEntry<TKey, TValue>>(_entriesHandle, oldCapacity);

            _capacity *= 2;
            var newHandle = _arena.Allocate(Unsafe.SizeOf<ArenaMapEntry<TKey, TValue>>() * _capacity, _priority, _hints);
            var newEntries = _arena.GetSpan<ArenaMapEntry<TKey, TValue>>(newHandle, _capacity);
            newEntries.Clear();

            int mask = _capacity - 1;

            // Rehash all existing active items into the enlarged table
            for (int i = 0; i < oldCapacity; i++)
            {
                if (oldEntries[i].State == SlotState.Occupied)
                {
                    int index = (int)((uint)oldEntries[i].Key.GetHashCode() & mask);
                    while (newEntries[index].State != SlotState.Free)
                    {
                        index = (index + 1) & mask;
                    }

                    newEntries[index] = oldEntries[i];
                }
            }

            _arena.Free(_entriesHandle);
            _entriesHandle = newHandle;
        }

        /// <summary>
        /// Gets the next power of two.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <returns>Next power of two int.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetNextPowerOfTwo(int value)
        {
            value--;
            value |= value >> 1;
            value |= value >> 2;
            value |= value >> 4;
            value |= value >> 8;
            value |= value >> 16;
            return value + 1;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (!_entriesHandle.IsInvalid) _arena.Free(_entriesHandle);
            _entriesHandle = default;
            _count = 0;
        }
    }
}