/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     MemoryManager.Core
 * FILE:        MemoryCanary.cs
 * PURPOSE:     Centralized unmanaged guard band validation with zero RELEASE overhead.
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

using System;
using System.Runtime.CompilerServices;

namespace MemoryManager.Core
{
    /// <summary>
    /// Canary class provides a simple mechanism for detecting buffer overruns and underruns in debug builds by placing known "canary" values before and after user allocations. 
    /// In release builds, the canary logic is completely stripped out to ensure zero overhead, 
    /// making it an effective tool for catching memory corruption issues during development without impacting performance in production.
    /// It additionally acts as an alignment boundary engine, snapping unmanaged pointers cleanly to hardware memory channels.
    /// </summary>
    public static class MemoryCanary
    {
        /// <summary>
        /// The byte alignment requirement boundary (Must be a power of two, e.g., 16, 32, 64)
        /// </summary>
        private const int Alignment = 16;

        /// <summary>
        /// The size
        /// </summary>
        public const int Size = 4; // 4 bytes for a uint marker

        /// <summary>
        /// The magic
        /// </summary>
        private const uint Magic = 0xDEADBEEF;

        /// <summary>
        /// Pre-calculated front window padding size required to fit the pre-canary and maintain power-of-two alignment
        /// </summary>
#if DEBUG
        private static readonly int FrontPadding = (Size + (Alignment - 1)) & ~(Alignment - 1);
#else
        private static readonly int FrontPadding = 0;
#endif

        /// <summary>
        /// Calculates the total physical footprint needed in the allocator block pool.
        /// </summary>
        /// <param name="userSize">Size of the user.</param>
        /// <returns>Total block allocation size required, rounded up to the nearest alignment multiple.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetPhysicalSize(int userSize)
        {
#if DEBUG
            // Raw physical size needs space for front tracking layout padding, user data payload, and post-canary trail bytes
            int rawSize = FrontPadding + userSize + Size;
            return (rawSize + (Alignment - 1)) & ~(Alignment - 1);
#else
            return (userSize + (Alignment - 1)) & ~(Alignment - 1);
#endif
        }

        /// <summary>
        /// Maps a raw physical block offset to a user-facing offset past the pre-canary.
        /// </summary>
        /// <param name="physicalOffset">The physical offset.</param>
        /// <returns>Aligned offset position where user data begins.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetUserOffset(int physicalOffset)
        {
            return physicalOffset + FrontPadding;
        }

        /// <summary>
        /// Maps a user data offset back to the absolute physical block start coordinate.
        /// </summary>
        /// <param name="userOffset">The user offset.</param>
        /// <returns>Absolute start address index of the complete tracking memory block pool chunk.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetPhysicalOffset(int userOffset)
        {
            return userOffset - FrontPadding;
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
        ///                      $"Expected Canary: 0x{Magic:X}, Pre-Site: 0x{preCanary:X}, Post-Site: 0x{postCanary:X}</exception>
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