/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     Lanes
 * FILE:        OneWayLane.cs
 * PURPOSE:     Your file purpose here
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

using Core;
using System;

namespace Lanes
{
    public sealed class OneWayLane
    {
        private readonly byte[] _buffer;
        private readonly IMemoryLane _fastLane;
        private readonly IMemoryLane _slowLane;

        internal int BufferCapacity => _buffer.Length;

        public OneWayLane(int bufferSize, IMemoryLane fastLane, IMemoryLane slowLane)
        {
            _buffer = new byte[bufferSize];
            _fastLane = fastLane ?? throw new ArgumentNullException(nameof(fastLane));
            _slowLane = slowLane ?? throw new ArgumentNullException(nameof(slowLane));
        }

        /// <summary>
        /// Moves data from FastLane to SlowLane using the buffer (one-way)
        /// </summary>
        /// <param name="fastHandle">The fast handle.</param>
        /// <returns>True if the move was successful, false otherwise.</returns>
        internal unsafe bool MoveFromFastToSlow(MemoryHandle fastHandle)
        {
            if (fastHandle.IsInvalid) return false;

            var fastPtr = _fastLane.Resolve(fastHandle);
            var size = _fastLane.GetAllocationSize(fastHandle);

            // Allocate on slow lane first to get destination pointer
            var slowHandle = _slowLane.Allocate(size);
            if (slowHandle.IsInvalid) return false;

            var slowPtr = _slowLane.Resolve(slowHandle);

            // Copy memory block directly from fastPtr to slowPtr
            Buffer.MemoryCopy(
                source: (void*)fastPtr,
                destination: (void*)slowPtr,
                destinationSizeInBytes: size,
                sourceBytesToCopy: size);

            _fastLane.Free(fastHandle);

            return true;
        }
    }
}
