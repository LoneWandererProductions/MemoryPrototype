/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     MemoryManager
 * FILE:        MemoryArenaExtensions.cs
 * PURPOSE:     Syntactic sugar for the MemoryArena.
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

using MemoryManager.Core;
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

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
        public static MemoryHandle Store<T>(this MemoryArena arena, T value) where T : unmanaged
        {
            var handle = arena.Allocate(Unsafe.SizeOf<T>());
            arena.BulkSet(handle, MemoryMarshal.CreateReadOnlySpan(ref value, 1));
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
        /// Encodes and stores a string as a raw UTF8 payload array inside the arena workspace.
        /// </summary>
        /// <param name="arena">The arena.</param>
        /// <param name="text">The text.</param>
        /// <returns>Handle to stored data.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static MemoryHandle StoreString(this MemoryArena arena, string text)
        {
            // Convert to bytes
            var bytes = System.Text.Encoding.UTF8.GetBytes(text);

            // Allocate space
            var handle = arena.AllocateArray<byte>(bytes.Length);

            // FIX: Explicitly provide the <byte> type token to satisfy the instance method
            arena.BulkSet<byte>(handle, bytes);

            return handle;
        }

        /// <summary>
        /// Safely passes a mutable Span payload directly to the atomic native arena handler 
        /// by automatically resolving compiler type inference constraints.
        /// </summary>
        /// <typeparam name="T">The unmanaged type.</typeparam>
        /// <param name="arena">The arena.</param>
        /// <param name="handle">The handle.</param>
        /// <param name="source">The source.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void BulkSet<T>(this MemoryArena arena, MemoryHandle handle, Span<T> source) where T : unmanaged
        {
            // Bypasses instance type inference by passing T explicitly
            arena.BulkSet<T>(handle, source);
        }
    }
}