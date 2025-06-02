/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     MemoryManager
 * FILE:        TypedMemoryArena.cs
 * PURPOSE:     A wrapper to simplify access to the MemoryArena. Provides typed allocation, 
 *              retrieval, and deallocation.
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

#nullable enable
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Core;

namespace MemoryManager
{
    /// <summary>
    ///     A typed, singleton-style wrapper around <see cref="MemoryArena" /> that simplifies
    ///     allocation, access, and deallocation of unmanaged memory.
    /// </summary>
    public sealed class TypedMemoryArena
    {
        /// <summary>
        ///     Internal reference to the wrapped <see cref="MemoryArena" />.
        /// </summary>
        private readonly MemoryArena _arena;

        /// <summary>
        ///     Private constructor to enforce singleton usage.
        /// </summary>
        /// <param name="arena">The memory arena to wrap.</param>
        public TypedMemoryArena(MemoryArena arena)
        {
            _arena = arena;
        }

        /// <summary>
        ///     Allocates memory for a single instance of type <typeparamref name="T" />.
        /// </summary>
        /// <typeparam name="T">The unmanaged type to allocate.</typeparam>
        /// <returns>A <see cref="MemoryHandle" /> referencing the allocated memory.</returns>
        public MemoryHandle Allocate<T>() where T : unmanaged
        {
            var size = Marshal.SizeOf<T>();
            return _arena.Allocate(size);
        }

        /// <summary>
        ///     Allocates memory for an array of <paramref name="count" /> elements of type <typeparamref name="T" />.
        /// </summary>
        /// <typeparam name="T">The unmanaged type to allocate.</typeparam>
        /// <param name="count">Number of elements to allocate.</param>
        /// <returns>A <see cref="MemoryHandle" /> referencing the allocated memory.</returns>
        public MemoryHandle Allocate<T>(int count) where T : unmanaged
        {
            var size = Marshal.SizeOf<T>() * count;
            return _arena.Allocate(size);
        }

        /// <summary>
        ///     Gets a typed reference to the memory at the specified <paramref name="handle" />.
        /// </summary>
        /// <typeparam name="T">The unmanaged type stored at the memory location.</typeparam>
        /// <param name="handle">The handle referencing the memory.</param>
        /// <returns>A reference to the value at the specified memory location.</returns>
        public ref T Get<T>(MemoryHandle handle) where T : unmanaged
        {
            unsafe
            {
                return ref Unsafe.AsRef<T>((void*)_arena.Resolve(handle));
            }
        }

        /// <summary>
        ///     Gets a <see cref="Span{T}" /> over the memory region referenced by <paramref name="handle" />
        ///     for <paramref name="count" /> elements of type <typeparamref name="T" />.
        /// </summary>
        /// <typeparam name="T">The unmanaged type stored in the array.</typeparam>
        /// <param name="handle">The handle referencing the memory block.</param>
        /// <param name="count">Number of elements in the memory block.</param>
        /// <returns>A <see cref="Span{T}" /> to access the allocated memory safely.</returns>
        public unsafe Span<T> GetSpan<T>(MemoryHandle handle, int count) where T : unmanaged
        {
            var ptr = (T*)_arena.Resolve(handle);
            return new Span<T>(ptr, count);
        }

        /// <summary>
        ///     Writes a value of type <typeparamref name="T" /> into the memory referenced by <paramref name="handle" />.
        ///     Mostly used for Structs.
        /// </summary>
        /// <typeparam name="T">The unmanaged type to write.</typeparam>
        /// <param name="handle">The handle referencing the target memory.</param>
        /// <param name="value">The value to write.</param>
        public void Set<T>(MemoryHandle handle, in T value) where T : unmanaged
        {
            unsafe
            {
                var ptr = (T*)_arena.Resolve(handle);
                *ptr = value;
            }
        }

        /// <summary>
        ///     Allocates memory for an unmanaged type <typeparamref name="T" /> and writes the provided <paramref name="value" />
        ///     to it.
        /// </summary>
        /// <typeparam name="T">The unmanaged type to allocate and write.</typeparam>
        /// <param name="value">The value to store in allocated memory.</param>
        /// <returns>A <see cref="MemoryHandle" /> referencing the allocated memory.</returns>
        public MemoryHandle AllocateAndSet<T>(in T value) where T : unmanaged
        {
            var handle = Allocate<T>();
            Set(handle, value);
            return handle;
        }

        /// <summary>
        ///     Allocates memory for an unmanaged type <typeparamref name="T" /> and provides a handle to it.
        /// </summary>
        /// <typeparam name="T">The unmanaged type to allocate and write.</typeparam>
        /// <param name="value">The value to store in allocated memory.</param>
        /// <returns></returns>
        public ref T AllocateAndGet<T>(in T value) where T : unmanaged
        {
            var handle = Allocate<T>();
            Set(handle, value);
            return ref Get<T>(handle);
        }

        /// <summary>
        ///     Frees the memory associated with the specified <paramref name="handle" />.
        /// </summary>
        /// <param name="handle">The handle referencing the memory to free.</param>
        public void Free(MemoryHandle handle)
        {
            _arena.Free(handle);
        }
    }
}