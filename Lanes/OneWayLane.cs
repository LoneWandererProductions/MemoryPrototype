/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     Lanes
 * FILE:        OneWayLane.cs
 * PURPOSE:     Reserved Memory area that only ports references from FastLane to SlowLane
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

// ReSharper disable UnusedMember.Global

using System;
using Core;

namespace Lanes
{
    /// <summary>
    ///     One way Memory Lane
    /// </summary>
    public sealed class OneWayLane
    {
        private readonly FastLane _fastLane;
        private readonly SlowLane _slowLane;

        /// <summary>
        ///     Initializes a new instance of the <see cref="OneWayLane" /> class.
        /// </summary>
        // Notice we changed IMemoryLane to FastLane/SlowLane so we have access to ReplaceWithStub
        public OneWayLane(FastLane fastLane, SlowLane slowLane)
        {
            _fastLane = fastLane ?? throw new ArgumentNullException(nameof(fastLane));
            _slowLane = slowLane ?? throw new ArgumentNullException(nameof(slowLane));
        }

        /// <summary>
        ///     Moves data from FastLane to SlowLane and sets up a redirection stub.
        /// </summary>
        /// <param name="fastHandle">The fast handle.</param>
        /// <returns>True if the move was successful, false otherwise.</returns>
        public unsafe bool MoveFromFastToSlow(MemoryHandle fastHandle)
        {
            // Fix 1: FastLane IDs are positive! If it's negative, it's invalid here.
            if (fastHandle.IsInvalid || fastHandle.Id < 0) return false;

            var fastPtr = _fastLane.Resolve(fastHandle);
            var size = _fastLane.GetAllocationSize(fastHandle);

            // Allocate on slow lane
            var slowHandle = _slowLane.Allocate(size);

            // SlowLane IDs are negative. If >= 0, something went wrong.
            if (slowHandle.IsInvalid || slowHandle.Id >= 0) return false;

            var slowPtr = _slowLane.Resolve(slowHandle);

            // Fix 2: Direct unmanaged copy (No phantom byte[] needed)
            System.Buffer.MemoryCopy(
                (void*)fastPtr,
                (void*)slowPtr,
                size,
                size);

            // Fix 3: Set up the Stub instead of Freeing!
            // This ensures that anyone holding the old fastHandle will now 
            // seamlessly get routed to the new slowHandle.
            _fastLane.ReplaceWithStub(fastHandle, slowHandle);

            return true;
        }
    }
}