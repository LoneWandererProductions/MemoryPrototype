/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     ExtendedSystemObjects
 * FILE:        UnmanagedMap.cs
 * PURPOSE:     Your file purpose here
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

using ExtendedSystemObjects.Helper;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ExtendedSystemObjects
{
    public unsafe class UnmanagedMap<TValue> : IEnumerable<(int, TValue)>, IDisposable where TValue : unmanaged
    {
        private EntryGeneric<TValue>* _entries;
        private int _capacity;
        public int Count { get; private set; }
        private int _power;

        private int _capacityPowerOf2;

        private const int MinPowerOf2 = 4;  // 16 entries minimum
        private const int MaxPowerOf2 = 20; // ~1 million entries max


        public UnmanagedMap(int? capacityPowerOf2 = null)
        {
            var power = capacityPowerOf2 ?? 8;
            if (power < MinPowerOf2)
                power = MinPowerOf2;

            _capacity = 1 << power;
            _capacityPowerOf2 = power;

            _entries = (EntryGeneric<TValue>*)Marshal.AllocHGlobal(sizeof(EntryGeneric<TValue>) * _capacity);
            Unsafe.InitBlock(_entries, 0, (uint)(sizeof(EntryGeneric<TValue>) * _capacity));
        }

        public int Capacity => _capacity;

        public TValue this[int key]
        {
            get => Get(key);
            set => Set(key, value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Set(int key, TValue value)
        {
            var compactTried = false;
            var resizeTried = false;

            while (true)
            {
                if (Count >= _capacity * 0.7f)
                {
                    if (!compactTried)
                    {
                        Compact();
                        compactTried = true;

                        // Retry after compaction only if it helped
                        if (Count < _capacity * 0.7f)
                            continue;
                    }

                    if (!resizeTried)
                    {
                        Resize();
                        resizeTried = true;
                        compactTried = false; // allow compact again later
                        continue;
                    }

                    throw new InvalidOperationException("UnmanagedIntMap full after compact and resize");
                }

                var mask = _capacity - 1;
                var index = key & mask;
                var firstTombstone = -1;

                for (var i = 0; i < _capacity; i++)
                {
                    ref var slot = ref _entries[(index + i) & mask];

                    if (slot.Used == SharedResources.Empty)
                    {
                        if (firstTombstone != -1)
                        {
                            ref var tomb = ref _entries[firstTombstone];
                            tomb.Key = key;
                            tomb.Value = value;
                            tomb.Used = SharedResources.Occupied;
                        }
                        else
                        {
                            slot.Key = key;
                            slot.Value = value;
                            slot.Used = SharedResources.Occupied;
                        }

                        Count++;
                        return;
                    }

                    if (slot.Used == SharedResources.Tombstone && firstTombstone == -1)
                        firstTombstone = (index + i) & mask;
                    else if (slot.Key == key)
                    {
                        slot.Value = value;
                        return;
                    }
                }

                // Should not be reached, retry
                continue;
            }
        }

        public TValue Get(int key)
        {
            var mask = _capacity - 1;
            var index = key & mask;

            for (var i = 0; i < _capacity; i++)
            {
                ref var slot = ref _entries[(index + i) & mask];

                if (slot.Used == SharedResources.Empty)
                    break;

                if (slot.Used == SharedResources.Occupied && slot.Key == key)
                    return slot.Value;
            }

            throw new KeyNotFoundException($"Key {key} not found.");
        }

        public bool ContainsKey(int key) => FindIndex(key) >= 0;

        public bool TryGetValue(int key, out TValue value)
        {
            int idx = FindIndex(key);
            if (idx >= 0)
            {
                value = _entries[idx].Value;
                return true;
            }
            value = default;
            return false;
        }

        public bool TryRemove(int key)
        {
            var mask = _capacity - 1;
            for (int i = 0; i < _capacity; i++)
            {
                int idx = (key + i) & mask;
                ref var slot = ref _entries[idx];

                if (slot.Used == SharedResources.Empty)
                    break;

                if (slot.Used == SharedResources.Occupied && slot.Key == key)
                {
                    slot.Used = SharedResources.Tombstone;
                    Count--;
                    return true;
                }
            }

            return false;
        }

        public EntryGenericEnumerator<TValue> GetEnumerator() => new EntryGenericEnumerator<TValue>(_entries, _capacity);
        
        IEnumerator<(int, TValue)> IEnumerable<(int, TValue)>.GetEnumerator() => GetEnumerator();
        
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public IEnumerable<int> Keys
        {
            get
            {
                foreach (var key in GetKeysSnapshot())
                    yield return key;
            }
        }

        public void Resize()
        {
            if (_capacityPowerOf2 >= MaxPowerOf2)
                return;

            _capacityPowerOf2++;
            var newMap = new UnmanagedMap<TValue>(_capacityPowerOf2);

            for (var i = 0; i < _capacity; i++)
            {
                ref var entry = ref _entries[i];
                if (entry.Used == SharedResources.Occupied)
                    newMap.Set(entry.Key, entry.Value);
            }

            Free();

            _entries = newMap._entries;
            _capacity = newMap._capacity;
            _capacityPowerOf2 = newMap._capacityPowerOf2;
            Count = newMap.Count;

            // Prevent double free
            newMap._entries = null;
        }

        public void Compact()
        {
            var loadFactor = Count / (float)_capacity;

            if (loadFactor >= 0.25f || _capacity <= (1 << MinPowerOf2))
                return;

            int targetPowerOf2 = Math.Max(MinPowerOf2, _capacityPowerOf2 - 1);
            var newMap = new UnmanagedMap<TValue>(targetPowerOf2);

            for (var i = 0; i < _capacity; i++)
            {
                ref var entry = ref _entries[i];
                if (entry.Used == SharedResources.Occupied)
                    newMap.Set(entry.Key, entry.Value);
            }

            Free();
            _entries = newMap._entries;
            _capacity = newMap._capacity;
            _capacityPowerOf2 = targetPowerOf2;
            Count = newMap.Count;

            newMap._entries = null; // prevent double free
        }

        public void Clear()
        {
            if (_entries != null)
            {
                Unsafe.InitBlock(_entries, 0, (uint)(sizeof(EntryGeneric<TValue>) * _capacity));
                Count = 0;
            }
        }

        public void Free()
        {
            if (_entries != null)
            {
                Marshal.FreeHGlobal((IntPtr)_entries);
                _entries = null;
            }

            _capacity = 0;
            Count = 0;
        }

        public void Dispose()
        {
            Free();
            GC.SuppressFinalize(this);
        }

        ~UnmanagedMap()
        {
            if (_entries != null)
                Free();
        }

        private int FindIndex(int key)
        {
            var mask = _capacity - 1;
            var index = key & mask;

            for (var i = 0; i < _capacity; i++)
            {
                var idx = (index + i) & mask;
                ref var slot = ref _entries[idx];

                if (slot.Used == SharedResources.Empty)
                    break;
                if (slot.Used == SharedResources.Occupied && slot.Key == key)
                    return idx;
            }

            return -1;
        }


        private List<int> GetKeysSnapshot()
        {
            var keys = new List<int>(Count);
            for (var i = 0; i < _capacity; i++)
            {
                var entry = _entries[i];
                if (entry.Used == SharedResources.Occupied)
                    keys.Add(entry.Key);
            }

            return keys;
        }
    }
}
