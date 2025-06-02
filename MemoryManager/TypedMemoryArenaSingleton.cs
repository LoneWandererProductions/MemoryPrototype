/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     MemoryManager
 * FILE:        TypedMemoryArenaSingleton.cs
 * PURPOSE:     A wrapper to simplify access to the MemoryArena. Provides typed allocation, 
 *              retrieval, and deallocation using a singleton-style interface for scoped use cases.
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

#nullable enable
using System;

namespace MemoryManager
{
    /// <summary>
    ///     Provides a thread-safe singleton access pattern for a <see cref="TypedMemoryArena" /> instance.
    ///     This class simplifies global access to a single shared memory arena for unmanaged memory operations.
    /// </summary>
    public static class TypedMemoryArenaSingleton
    {
        /// <summary>
        ///     Holds the singleton instance of <see cref="TypedMemoryArena" />.
        /// </summary>
        private static TypedMemoryArena? _instance;

        /// <summary>
        ///     Synchronization object for thread-safe initialization.
        /// </summary>
        private static readonly object Lock = new();

        /// <summary>
        ///     Gets the singleton instance of <see cref="TypedMemoryArena" />.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        ///     Thrown if the singleton has not been initialized using <see cref="Initialize" />.
        /// </exception>
        public static TypedMemoryArena Instance =>
            _instance ?? throw new InvalidOperationException("TypedMemoryArena not initialized.");

        /// <summary>
        ///     Initializes the singleton instance with the given <see cref="MemoryArena" />.
        ///     This method must be called before accessing the <see cref="Instance" />.
        /// </summary>
        /// <param name="arena">The <see cref="MemoryArena" /> to wrap with <see cref="TypedMemoryArena" />.</param>
        /// <exception cref="InvalidOperationException">
        ///     Thrown if initialization has already occurred.
        /// </exception>
        public static void Initialize(MemoryArena arena)
        {
            lock (Lock)
            {
                if (_instance != null)
                    throw new InvalidOperationException("TypedMemoryArena already initialized.");

                _instance = new TypedMemoryArena(arena);
            }
        }
    }
}