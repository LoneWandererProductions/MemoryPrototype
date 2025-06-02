/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     Lanes
 * FILE:        OneWayLane.cs
 * PURPOSE:     Reserved Memory area that only ports references from FastLane to SlowLane
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

using System;
using Core;

namespace Lanes
{
    /// <summary>
    ///     One way Memory Lane
    /// </summary>
    public sealed class OneWayLane
    {
        /// <summary>
        ///     The buffer
        /// </summary>
        private readonly byte[] _buffer;

        /// <summary>
        ///     The FastLane
        /// </summary>
        private readonly IMemoryLane _fastLane;

        /// <summary>
        ///     The SlowLane
        /// </summary>
        private readonly IMemoryLane _slowLane;

        /// <summary>
        ///     Initializes a new instance of the <see cref="OneWayLane" /> class.
        /// </summary>
        /// <param name="bufferSize">Size of the buffer.</param>
        /// <param name="fastLane">The fast lane.</param>
        /// <param name="slowLane">The slow lane.</param>
        /// <exception cref="System.ArgumentNullException">
        ///     fastLane
        ///     or
        ///     slowLane
        /// </exception>
        public OneWayLane(int bufferSize, IMemoryLane fastLane, IMemoryLane slowLane)
        {
            _buffer = new byte[bufferSize];
            _fastLane = fastLane ?? throw new ArgumentNullException(nameof(fastLane));
            _slowLane = slowLane ?? throw new ArgumentNullException(nameof(slowLane));
        }

        /// <summary>
        ///     Gets the buffer capacity.
        /// </summary>
        /// <value>
        ///     The buffer capacity.
        /// </value>
        internal int BufferCapacity => _buffer.Length;

        /// <summary>
        ///     Moves data from FastLane to SlowLane using the buffer (one-way)
        /// </summary>
        /// <param name="fastHandle">The fast handle.</param>
        /// <returns>True if the move was successful, false otherwise.</returns>
        internal unsafe bool MoveFromFastToSlow(MemoryHandle fastHandle)
        {
            if (fastHandle.IsInvalid || fastHandle.Id >= 0) return false;

            var fastPtr = _fastLane.Resolve(fastHandle);
            var size = _fastLane.GetAllocationSize(fastHandle);

            // Allocate on slow lane first to get destination pointer
            var slowHandle = _slowLane.Allocate(size);
            if (slowHandle.IsInvalid || slowHandle.Id >= 0) return false;

            var slowPtr = _slowLane.Resolve(slowHandle);

            // Copy memory block directly from fastPtr to slowPtr
            Buffer.MemoryCopy(
                (void*)fastPtr,
                (void*)slowPtr,
                size,
                size);

            _fastLane.Free(fastHandle);

            return true;
        }
    }
}