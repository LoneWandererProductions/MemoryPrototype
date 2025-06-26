﻿using System.Collections;
using System.Collections.Generic;

namespace ExtendedSystemObjects.Helper
{
    public unsafe struct EntryGenericEnumerator<TValue> : IEnumerator<(int, TValue)>
    {
        private readonly EntryGeneric<TValue>* _entries;
        private readonly int _capacity;
        private int _index;

        public EntryGenericEnumerator(EntryGeneric<TValue>* entries, int capacity)
        {
            _entries = entries;
            _capacity = capacity;
            _index = -1;
            Current = default;
        }

        public (int, TValue) Current { get; private set; }
        object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            while (++_index < _capacity)
            {
                var entry = _entries[_index];
                if (entry.Used == SharedResources.Occupied)
                {
                    Current = (entry.Key, entry.Value);
                    return true;
                }
            }

            return false;
        }

        public void Reset() => _index = -1;

        public void Dispose() { }
    }
}
