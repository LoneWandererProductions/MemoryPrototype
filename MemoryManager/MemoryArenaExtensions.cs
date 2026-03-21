/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     MemoryManager
 * FILE:        MemoryArenaExtensions.cs
 * PURPOSE:     Syntactic sugar for the MemoryArena.
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

#nullable enable
using MemoryManager.Core;
using System;
using System.Runtime.CompilerServices;

namespace MemoryManager
{
    public static class MemoryArenaExtensions
    {
        /// <summary>
        /// Allocates space for a specific unmanaged type.
        /// </summary>
        /// <typeparam name="T">Generic Type we have to define.</typeparam>
        /// <param name="arena">The arena.</param>
        /// <returns>Handle to stored data.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static MemoryHandle Allocate<T>(this MemoryArena arena) where T : unmanaged
        {
            return arena.Allocate(Unsafe.SizeOf<T>());
        }

        /// <summary>
        /// Allocates space for a specific unmanaged type and immediately sets its value.
        /// </summary>
        /// <typeparam name="T">Generic Type we have to define.</typeparam>
        /// <param name="arena">The arena.</param>
        /// <param name="value">The value.</param>
        /// <returns>Handle to stored data.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe MemoryHandle Store<T>(this MemoryArena arena, T value) where T : unmanaged
        {
            var handle = arena.Allocate(Unsafe.SizeOf<T>());
            Unsafe.Write(arena.Resolve(handle).ToPointer(), value);
            return handle;
        }

        /// <summary>
        /// Allocates an array of unmanaged types.
        /// </summary>
        /// <typeparam name="T">Generic Type we have to define.</typeparam>
        /// <param name="arena">The arena.</param>
        /// <param name="count">The count.</param>
        /// <returns>An allocated Array and handle to said array.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static MemoryHandle AllocateArray<T>(this MemoryArena arena, int count) where T : unmanaged
        {
            return arena.Allocate(Unsafe.SizeOf<T>() * count);
        }

        /// <summary>
        ///     Copies a source span of data into the unmanaged memory referenced by the handle.
        /// </summary>
        /// <typeparam name="T">The unmanaged type.</typeparam>
        /// <param name="arena">The target arena.</param>
        /// <param name="handle">Handle to the unmanaged destination.</param>
        /// <param name="source">The source data to copy from.</param>
        /// <exception cref="ArgumentException">Thrown if the destination allocation is too small.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void BulkSet<T>(this MemoryArena arena, MemoryHandle handle, ReadOnlySpan<T> source)
            where T : unmanaged
        {
            var entry = arena.GetEntry(handle);
            var requiredSize = Unsafe.SizeOf<T>() * source.Length;

            if (entry.Size < requiredSize)
                throw new ArgumentException(
                    $"Destination allocation ({entry.Size} bytes) is too small for source data ({requiredSize} bytes).");

            var dest = arena.Resolve(handle).ToPointer();

            // Fixed: Use the Span's internal reference to copy directly to the pointer
            fixed (T* srcPtr = source)
            {
                Buffer.MemoryCopy(srcPtr, dest, entry.Size, requiredSize);
            }
        }

        /// <summary>
        /// Stores the string.
        /// </summary>
        /// <param name="arena">The arena.</param>
        /// <param name="text">The text.</param>
        /// <returns>Handle to stored data.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static MemoryHandle StoreString(this MemoryArena arena, string text)
        {
            // Convert to bytes
            var bytes = System.Text.Encoding.UTF8.GetBytes(text);

            // Allocate space (including a null-terminator if you want to be C-compatible)
            var handle = arena.AllocateArray<byte>(bytes.Length);

            // Bulk copy into the arena
            arena.BulkSet(handle, bytes);

            return handle;
        }

        /// <summary>
        /// Bulks the set.
        /// </summary>
        /// <typeparam name="T">The unmanaged type.</typeparam>
        /// <param name="arena">The arena.</param>
        /// <param name="handle">The handle.</param>
        /// <param name="source">The source.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void BulkSet<T>(this MemoryArena arena, MemoryHandle handle, T[] source) where T : unmanaged
        {
            // Just wrap the existing Span version
            arena.BulkSet<T>(handle, source.AsSpan());
        }
    }
}