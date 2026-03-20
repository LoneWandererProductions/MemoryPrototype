/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     Lanes
 * FILE:        IFastLane.cs
 * PURPOSE:     Interface specification especially for FastLane.
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */


using Core;

namespace Lanes
{
    /// <summary>
    /// Interface Definition for our FastLane.
    /// </summary>
    /// <seealso cref="Core.IMemoryLane" />
    public interface IFastLane : IMemoryLane
    {
        /// <summary>
        /// Gets or sets the one way lane.
        /// </summary>
        /// <value>
        /// The one way lane.
        /// </value>
        OneWayLane? OneWayLane { get; set; }

        /// <summary>
        /// Compacts the specified current frame.
        /// </summary>
        /// <param name="currentFrame">The current frame.</param>
        /// <param name="config">The configuration.</param>
        void Compact(int currentFrame, MemoryManagerConfig config);

        /// <summary>
        /// Replaces the with stub.
        /// </summary>
        /// <param name="fastHandle">The fast handle.</param>
        /// <param name="slowHandle">The slow handle.</param>
        void ReplaceWithStub(MemoryHandle fastHandle, MemoryHandle slowHandle);
    }
}