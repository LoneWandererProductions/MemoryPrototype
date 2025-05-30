using System;
using System.Runtime.InteropServices;
using Core;

namespace Lanes
{
    public sealed class OneWayLane
    {
        private readonly byte[] _buffer;
        private readonly IMemoryLane _fastLane;
        private readonly IMemoryLane _slowLane;

        public OneWayLane(int bufferSize, IMemoryLane fastLane, IMemoryLane slowLane)
        {
            _buffer = new byte[bufferSize];
            _fastLane = fastLane ?? throw new ArgumentNullException(nameof(fastLane));
            _slowLane = slowLane ?? throw new ArgumentNullException(nameof(slowLane));
        }

        internal int BufferCapacity => _buffer.Length;

        /// <summary>
        ///     Moves data from FastLane to SlowLane using the buffer (one-way)
        /// </summary>
        /// <param name="fastHandle">The fast handle.</param>
        /// <returns>True if the move was successful, false otherwise.</returns>
        internal bool MoveFromFastToSlow(MemoryHandle fastHandle)
        {
            if (fastHandle.IsInvalid) return false;

            var fastPtr = _fastLane.Resolve(fastHandle);
            var size = _fastLane.GetAllocationSize(fastHandle);

            if (size > _buffer.Length)
                throw new InvalidOperationException(
                    $"Buffer size {_buffer.Length} too small for allocation size {size}.");

            Marshal.Copy(fastPtr, _buffer, 0, size);

            var slowHandle = _slowLane.Allocate(size);
            if (slowHandle.IsInvalid) return false;

            var slowPtr = _slowLane.Resolve(slowHandle);

            Marshal.Copy(_buffer, 0, slowPtr, size);

            _fastLane.Free(fastHandle);

            return true;
        }
    }
}