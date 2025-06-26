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
        private int _count;
        private int _power;

        private const int MinPower = 4;
        private const int MaxPower = 20;

        public UnmanagedMap(int? capacityPowerOf2 = null)
        {
            _power = Math.Clamp(capacityPowerOf2 ?? 8, MinPower, MaxPower);
            _capacity = 1 << _power;

            _entries = (EntryGeneric<TValue>*)Marshal.AllocHGlobal(sizeof(EntryGeneric<TValue>) * _capacity);
            Unsafe.InitBlock(_entries, 0, (uint)(sizeof(EntryGeneric<TValue>) * _capacity));
        }

        public int Count => _count;
        public int Capacity => _capacity;

        public TValue this[int key]
        {
            get
            {
                if (!TryGetValue(key, out var value))
                    throw new KeyNotFoundException($"Key {key} not found.");
                return value;
            }
            set => Set(key, value);
        }

        public void Set(int key, TValue value)
        {
            var mask = _capacity - 1;
            int firstTombstone = -1;

            for (int i = 0; i < _capacity; i++)
            {
                int idx = (key + i) & mask;
                ref var slot = ref _entries[idx];

                if (slot.Used == SharedResources.Empty)
                {
                    ref var target = ref (firstTombstone != -1 ? ref _entries[firstTombstone] : ref slot);
                    target.Key = key;
                    target.Value = value;
                    target.Used = SharedResources.Occupied;
                    _count++;
                    return;
                }

                if (slot.Used == SharedResources.Tombstone && firstTombstone == -1)
                {
                    firstTombstone = idx;
                }
                else if (slot.Used == SharedResources.Occupied && slot.Key == key)
                {
                    slot.Value = value;
                    return;
                }
            }

            throw new InvalidOperationException("UnmanagedMap is full. Consider resizing.");
        }

        public bool TryGetValue(int key, out TValue value)
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
                    value = slot.Value;
                    return true;
                }
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
                    _count--;
                    return true;
                }
            }

            return false;
        }

        public EntryGenericEnumerator<TValue> GetEnumerator() => new EntryGenericEnumerator<TValue>(_entries, _capacity);
        IEnumerator<(int, TValue)> IEnumerable<(int, TValue)>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public void Clear()
        {
            if (_entries != null)
            {
                Unsafe.InitBlock(_entries, 0, (uint)(sizeof(EntryGeneric<TValue>) * _capacity));
                _count = 0;
            }
        }

        public void Dispose()
        {
            if (_entries != null)
            {
                Marshal.FreeHGlobal((IntPtr)_entries);
                _entries = null;
            }
        }
    }
}
