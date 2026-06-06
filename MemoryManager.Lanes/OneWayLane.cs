/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     MemoryArenaPrototype.Lane
 * FILE:        OneWayLane.cs
 * PURPOSE:     Reserved Memory area that only ports references from FastLane to SlowLane
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

// ReSharper disable UnusedMember.Global

using System.Diagnostics;
using MemoryManager.Core;

namespace MemoryManager.Lanes
{
    /// <summary>
    ///     One way Memory Lane
    /// </summary>
    public sealed class OneWayLane
    {
        /// <summary>
        /// The fast lane
        /// </summary>
        private readonly IFastLane _fastLane;

        /// <summary>
        /// The slow lane
        /// </summary>
        private readonly IMemoryLane _slowLane;

        /// <summary>
        /// Initializes a new instance of the <see cref="OneWayLane" /> class.
        /// </summary>
        /// <param name="fastLane">The fast lane.</param>
        /// <param name="slowLane">The slow lane.</param>
        /// <exception cref="ArgumentNullException">
        /// fastLane
        /// or
        /// slowLane
        /// </exception>
        public OneWayLane(IFastLane fastLane, IMemoryLane slowLane)
        {
            _fastLane = fastLane ?? throw new ArgumentNullException(nameof(fastLane));
            _slowLane = slowLane ?? throw new ArgumentNullException(nameof(slowLane));
        }

        /// <summary>
        /// Moves data from FastLane to SlowLane and sets up a redirection stub.
        /// </summary>
        /// <param name="fastHandle">The fast handle.</param>
        /// <returns>
        /// True if the move was successful, false otherwise.
        /// </returns>
        public unsafe bool MoveFromFastToSlow(MemoryHandle fastHandle)
        {
            // 1. Pre-validation
            if (fastHandle.IsInvalid || fastHandle.Id < 0) return false;

            nint fastPtr;
            int size;
            try
            {
                fastPtr = _fastLane.Resolve(fastHandle);
                size = _fastLane.GetAllocationSize(fastHandle);
            }
            catch
            {
                // Fail-safe if the fast handle was already corrupted or freed
                return false;
            }

            // 2. Allocate on slow lane
            MemoryHandle slowHandle;
            try
            {
                slowHandle = _slowLane.Allocate(size);
            }
            catch (OutOfMemoryException)
            {
                return false; // Slow lane is full, abort migration but keep fast data intact
            }

            // Validate slow lane handle characteristics safely before proceeding
            if (slowHandle.IsInvalid || slowHandle.Id >= 0)
            {
                // If the handle is invalid but somehow reserved memory, free it if possible
                if (!slowHandle.IsInvalid) _slowLane.Free(slowHandle);
                return false;
            }

            // 3. Execution Phase
            try
            {
                var slowPtr = _slowLane.Resolve(slowHandle);

                // Direct unmanaged copy
                System.Buffer.MemoryCopy(
                    (void*)fastPtr,
                    (void*)slowPtr,
                    size, // Destination capacity limit
                    size // Row bytes to copy
                );

                // SUCCESS: Only swap to stub if the copy operation completely succeeded
                _fastLane.ReplaceWithStub(fastHandle, slowHandle);
                return true;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error during MoveFromFastToSlow: {ex}");
                // CLEANUP ON FAILURE: If copying or resolving blew up,
                // free the allocated slow lane block so we don't leak unmanaged memory.
                _slowLane.Free(slowHandle);
                return false;
            }
        }
    }
}