/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     MemoryArenaPrototype.Core
 * FILE:        BlobManager.cs
 * PURPOSE:     Internal linear/bump allocator for unpredictable or large blob data.
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

namespace MemoryManager.Core
{
    /// <inheritdoc />
    /// <summary>
    /// A Linear/Bump allocator designed for large or unpredictable blobs of memory.
    /// Allocations are extremely fast, leveraging a dense flat array for metadata tracking.
    /// Memory is physically reclaimed and consolidated during explicit compaction.
    /// </summary>
    /// <seealso cref="IMemoryLane" />
    public sealed class BlobManager : IMemoryLane
    {
        /// <summary>
        /// The starting identifier
        /// </summary>
        public const int StartingId = -1000000;

        /// <summary>
        /// The buffer
        /// </summary>
        private readonly nint _buffer;

        /// <summary>
        /// The capacity
        /// </summary>
        private readonly int _capacity;

        /// <summary>
        /// Negative IDs are standard convention for SlowLane/Blob allocations
        /// </summary>
        private int _nextId = StartingId;

        /// <summary>
        /// The next free offset
        /// </summary>
        private int _nextFreeOffset;

        /// <summary>
        /// Dense flat array mapping handles directly to their metadata via (StartingId - Id)
        /// </summary>
        private BlobEntry[] _entries = new BlobEntry[128];

#if DEBUG
        /// <summary>
        /// The debug names
        /// </summary>
        private readonly Dictionary<int, string> _debugNames = new();
#endif

        /// <inheritdoc />
        public int EntryCount { get; private set; }

        /// <inheritdoc />
        public event Action<string>? OnCompaction;

        /// <inheritdoc />
        public event Action<string, int, int>? OnAllocationExtension;

        /// <summary>
        ///     Initializes a new instance of the BlobManager.
        /// </summary>
        public BlobManager(nint buffer, int capacity)
        {
            _buffer = buffer;
            _capacity = capacity;
        }

        /// <inheritdoc />
        public bool CanAllocate(int size)
        {
            return _nextFreeOffset + size <= _capacity;
        }

        /// <inheritdoc />
        public MemoryHandle Allocate(
            int size,
            AllocationPriority priority = AllocationPriority.Normal,
            AllocationHints hints = AllocationHints.None,
            string? debugName = null,
            int currentFrame = 0)
        {
            if (!CanAllocate(size))
                throw new OutOfMemoryException(
                    $"BlobManager: Out of memory. Requested {size} bytes, but only {FreeSpace()} available.");

            if (_nextId == int.MinValue)
                throw new OutOfMemoryException("BlobManager: ID exhaustion. Cannot allocate more IDs.");

            var id = _nextId--;
            int index = StartingId - id; // Maps sequential negative IDs cleanly to 0, 1, 2...

            EnsureCapacity(index);

            byte version = 1;

            _entries[index] = new BlobEntry
            {
                Id = id,
                Offset = _nextFreeOffset,
                Size = size,
                AllocationFrame = currentFrame,
                Version = version
            };

#if DEBUG
            if (!string.IsNullOrEmpty(debugName))
            {
                _debugNames[id] = debugName;
            }
#endif

            // Bump the allocator forward
            _nextFreeOffset += size;
            EntryCount++;

            return new MemoryHandle(id, version, this);
        }

        /// <inheritdoc />
        public void Free(MemoryHandle handle)
        {
            int index = StartingId - handle.Id;

            if (index < 0 || index >= _entries.Length || _entries[index].Size == 0)
                throw new InvalidOperationException($"BlobManager: Invalid or double-freed handle {handle.Id}");

            _entries[index] = default;
            EntryCount--;

#if DEBUG
            _debugNames.Remove(handle.Id);
#endif
        }

        /// <summary>
        /// Frees multiple handles sequentially.
        /// </summary>
        public void FreeMany(IEnumerable<MemoryHandle> handles)
        {
            foreach (var handle in handles)
            {
                Free(handle);
            }
        }

        /// <inheritdoc />
        public nint Resolve(MemoryHandle handle)
        {
            int index = StartingId - handle.Id;

            if (index < 0 || index >= _entries.Length || _entries[index].Size == 0)
                throw new InvalidOperationException($"BlobManager: Invalid handle {handle.Id}");

            ref readonly var entry = ref _entries[index];

            if (entry.Version != handle.Version)
                throw new AccessViolationException($"Blob Zombie: ID {handle.Id} version mismatch.");

            return _buffer + entry.Offset;
        }

        /// <inheritdoc />
        public bool HasHandle(MemoryHandle handle)
        {
            int index = StartingId - handle.Id;
            return index >= 0 && index < _entries.Length && _entries[index].Size > 0;
        }

        /// <inheritdoc />
        public AllocationEntry GetEntry(MemoryHandle handle)
        {
            int index = StartingId - handle.Id;

            if (index < 0 || index >= _entries.Length || _entries[index].Size == 0)
                throw new InvalidOperationException($"BlobManager: Invalid handle {handle.Id}");

            ref readonly var blob = ref _entries[index];

            return new AllocationEntry
            {
                HandleId = blob.Id,
                Version = blob.Version,
                Offset = blob.Offset,
                Size = blob.Size,
                AllocationFrame = blob.AllocationFrame,
                IsStub = false,
                RedirectToId = 0,
                Priority = AllocationPriority.Normal,
                Hints = AllocationHints.None,
                LastAccessFrame = blob.AllocationFrame
            };
        }

        /// <inheritdoc />
        public int GetAllocationSize(MemoryHandle handle)
        {
            int index = StartingId - handle.Id;

            if (index < 0 || index >= _entries.Length || _entries[index].Size == 0)
                throw new InvalidOperationException($"BlobManager: Invalid handle {handle.Id}");

            return _entries[index].Size;
        }

        /// <inheritdoc />
        public void Compact()
        {
            if (EntryCount == 0) return;

            var pool = System.Buffers.ArrayPool<BlobEntry>.Shared;
            var scratch = pool.Rent(EntryCount);

            try
            {
                var validCount = 0;
                for (var i = 0; i < _entries.Length; i++)
                {
                    if (_entries[i].Size > 0)
                    {
                        scratch[validCount++] = _entries[i];
                    }
                }

                var validSpan = scratch.AsSpan(0, validCount);
                validSpan.Sort(new BlobOffsetComparer());

                var currentOffset = 0;

                for (var i = 0; i < validSpan.Length; i++)
                {
                    ref readonly var entry = ref validSpan[i];

                    if (entry.Offset > currentOffset)
                    {
                        unsafe
                        {
                            System.Buffer.MemoryCopy(
                                (void*)(_buffer + entry.Offset),
                                (void*)(_buffer + currentOffset),
                                entry.Size,
                                entry.Size);
                        }
                    }

                    // Update the offset inside our primary dense track tracking array slot directly
                    int index = StartingId - entry.Id;
                    _entries[index].Offset = currentOffset;

                    currentOffset += entry.Size;
                }

                _nextFreeOffset = currentOffset;
            }
            finally
            {
                pool.Return(scratch);
            }

            OnCompaction?.Invoke(nameof(BlobManager));
        }

        /// <inheritdoc />
        public double UsagePercentage()
        {
            // Normalizes return behavior with FastLane/SlowLane metrics (returns scale 0.0 - 1.0 instead of 0.0 - 100.0)
            return _capacity == 0 ? 0.0 : _nextFreeOffset / (double)_capacity;
        }

        /// <inheritdoc />
        public int FreeSpace() => _capacity - _nextFreeOffset;

        /// <inheritdoc />
        public int EstimateFragmentation()
        {
            var allocatedBytes = _nextFreeOffset;
            if (allocatedBytes == 0) return 0;

            var livingBytes = 0;
            for (var i = 0; i < _entries.Length; i++)
            {
                if (_entries[i].Size > 0)
                {
                    livingBytes += _entries[i].Size;
                }
            }

            var wastedBytes = allocatedBytes - livingBytes;
            return (int)((double)wastedBytes / allocatedBytes * 100);
        }

        /// <inheritdoc />
        public int StubCount() => 0;

        /// <inheritdoc />
        public IEnumerable<MemoryHandle> GetHandles()
        {
            for (var i = 0; i < _entries.Length; i++)
            {
                if (_entries[i].Size > 0)
                {
                    yield return new MemoryHandle(_entries[i].Id, _entries[i].Version, this);
                }
            }
        }

        /// <inheritdoc />
        public string DebugDump()
        {
            return $"BlobManager Dump\nAllocations: {EntryCount}\nUsed: {_nextFreeOffset}/{_capacity} bytes";
        }

        /// <inheritdoc />
        public string DebugVisualMap()
        {
            if (_capacity == 0) return "[]";

            const int mapResolution = 80;
            var map = new char[mapResolution];
            var bytesPerChar = (double)_capacity / mapResolution;

            for (var i = 0; i < mapResolution; i++)
            {
                var startByte = i * bytesPerChar;
                var endByte = (i + 1) * bytesPerChar;

                if (startByte >= _nextFreeOffset)
                {
                    map[i] = '░';
                    continue;
                }

                var isLiving = false;
                for (var j = 0; j < _entries.Length; j++)
                {
                    if (_entries[j].Size > 0 && _entries[j].Offset < endByte && _entries[j].Offset + _entries[j].Size > startByte)
                    {
                        isLiving = true;
                        break;
                    }
                }

                map[i] = isLiving ? '█' : '-';
            }

            return $"Blob Map: [{new string(map)}]\nLegend: █=Used, -=Gap, ░=Free";
        }

        /// <inheritdoc />
        public string DebugRedirections() => "[BlobRedirects not applicable]";

        /// <summary>
        /// Resizes the tracking entry metadata array geometric pool size.
        /// </summary>
        private void EnsureCapacity(int requiredIndex)
        {
            if (requiredIndex >= _entries.Length)
            {
                var oldSize = _entries.Length;
                var newSize = oldSize * 2;
                if (newSize <= requiredIndex) newSize = requiredIndex + 1;

                Array.Resize(ref _entries, newSize);
                OnAllocationExtension?.Invoke(nameof(BlobManager), oldSize, newSize);
            }
        }

        /// <summary>
        /// Zero-allocation comparer for sorting blobs by offset.
        /// </summary>
        private struct BlobOffsetComparer : IComparer<BlobEntry>
        {
            public int Compare(BlobEntry x, BlobEntry y)
            {
                return x.Offset.CompareTo(y.Offset);
            }
        }
    }
}