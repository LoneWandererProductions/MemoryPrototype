/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     ExtendedSystemObjects.Helper
 * FILE:        EntryEnumerator.cs
 * PURPOSE:     Your file purpose here
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

using System.Collections;
using System.Collections.Generic;

namespace ExtendedSystemObjects.Helper
{
    public unsafe struct EntryEnumerator : IEnumerable<Entry>, IEnumerator<Entry>
    {
        private readonly Entry* _entries;
        private readonly int _capacity;
        private int _index;

        /// <summary>
        /// Initializes a new instance of the <see cref="EntryEnumerator"/> struct.
        /// </summary>
        /// <param name="entries">The entries.</param>
        /// <param name="capacity">The capacity.</param>
        public EntryEnumerator(Entry* entries, int capacity)
        {
            _entries = entries;
            _capacity = capacity;
            _index = -1;
        }

        public bool MoveNext()
        {
            while (++_index < _capacity)
            {
                if (_entries[_index].Used == SharedResources.Occupied)
                    return true;
            }

            return false;
        }

        public Entry Current => _entries[_index];

        object IEnumerator.Current => Current;

        public void Reset() => _index = -1;

        public void Dispose() { }

        public EntryEnumerator GetEnumerator() => this;

        IEnumerator<Entry> IEnumerable<Entry>.GetEnumerator() => this;
        IEnumerator IEnumerable.GetEnumerator() => this;
    }

}
