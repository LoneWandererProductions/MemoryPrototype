/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     MemoryManager.Core
 * FILE:        MemoryCanary.cs
 * PURPOSE:     Centralized unmanaged guard band validation with zero RELEASE overhead.
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

using System.Runtime.CompilerServices;

namespace MemoryManager.Core
{
    /// <summary>
    /// Canary class provides a simple mechanism for detecting buffer overruns and underruns in debug builds by placing known "canary" values before and after user allocations. 
    /// In release builds, the canary logic is completely stripped out to ensure zero overhead, 
    /// making it an effective tool for catching memory corruption issues during development without impacting performance in production.
    /// </summary>
    public static class MemoryCanary
    {
        /// <summary>
        /// The size
        /// </summary>
        public const int Size = 4; // 4 bytes for a uint marker

        /// <summary>
        /// The magic
        /// </summary>
        private const uint Magic = 0xDEADBEEF;

        /// <summary>
        /// Calculates the total physical footprint needed in the allocator block pool.
        /// </summary>
        /// <param name="userSize">Size of the user.</param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetPhysicalSize(int userSize)
        {
#if DEBUG
            return userSize + Size * 2;
#else
            return userSize;
#endif
        }

        /// <summary>
        /// Maps a raw physical block offset to a user-facing offset past the pre-canary.
        /// </summary>
        /// <param name="physicalOffset">The physical offset.</param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetUserOffset(int physicalOffset)
        {
#if DEBUG
            return physicalOffset + Size;
#else
            return physicalOffset;
#endif
        }

        /// <summary>
        /// Maps a user data offset back to the absolute physical block start coordinate.
        /// </summary>
        /// <param name="userOffset">The user offset.</param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetPhysicalOffset(int userOffset)
        {
#if DEBUG
            return userOffset - Size;
#else
            return userOffset;
#endif
        }

        /// <summary>
        /// Writes the invariant magic signatures around the user space allocation boundaries.
        /// </summary>
        /// <param name="buffer">The buffer.</param>
        /// <param name="userOffset">The user offset.</param>
        /// <param name="userSize">Size of the user.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void WriteGuardBands(nint buffer, int userOffset, int userSize)
        {
#if DEBUG
            // Write Pre-Canary directly before the user offset space
            *(uint*)(buffer + userOffset - Size) = Magic;
            // Write Post-Canary directly at the tail index end of user size space
            *(uint*)(buffer + userOffset + userSize) = Magic;
#endif
        }

        /// <summary>
        /// Inspects the boundaries to verify that no adjacent buffer underruns/overruns occurred.
        /// </summary>
        /// <param name="buffer">The buffer.</param>
        /// <param name="userOffset">The user offset.</param>
        /// <param name="userSize">Size of the user.</param>
        /// <param name="handleId">The handle identifier.</param>
        /// <exception cref="AccessViolationException">CRITICAL HEAP CORRUPTION DETECTED: Buffer boundary breach on Handle ID {handleId}. " +
        ///                     $"Expected Canary: 0x{Magic:X}, Pre-Site: 0x{preCanary:X}, Post-Site: 0x{postCanary:X}</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void Validate(nint buffer, int userOffset, int userSize, int handleId)
        {
#if DEBUG
            uint preCanary = *(uint*)(buffer + userOffset - Size);
            uint postCanary = *(uint*)(buffer + userOffset + userSize);

            if (preCanary != Magic || postCanary != Magic)
            {
                throw new AccessViolationException(
                    $"CRITICAL HEAP CORRUPTION DETECTED: Buffer boundary breach on Handle ID {handleId}. " +
                    $"Expected Canary: 0x{Magic:X}, Pre-Site: 0x{preCanary:X}, Post-Site: 0x{postCanary:X}");
            }
#endif
        }
    }
}