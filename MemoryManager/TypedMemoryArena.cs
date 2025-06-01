/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     MemoryManager
 * FILE:        TypedMemoryArena.cs
 * PURPOSE:     Your file purpose here
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Core;

namespace MemoryManager
{
    public sealed class TypedMemoryArena
    {
        private static TypedMemoryArena? _instance;

        private static readonly object _lock = new();

        public static void Initialize(MemoryArena arena)
        {
            lock (_lock)
            {
                if (_instance != null)
                    throw new InvalidOperationException("TypedMemoryArena is already initialized.");

                _instance = new TypedMemoryArena(arena);
            }
        }

        public static TypedMemoryArena Instance =>
            _instance ?? throw new InvalidOperationException("TypedMemoryArena is not initialized. Call Initialize() first.");

        private readonly MemoryArena _arena;

        private TypedMemoryArena(MemoryArena arena)
        {
            _arena = arena;
        }

        public MemoryHandle Allocate<T>() where T : unmanaged
        {
            var size = Marshal.SizeOf<T>();
            return _arena.Allocate(size);
        }

        public ref T Get<T>(MemoryHandle handle) where T : unmanaged
        {
            unsafe
            {
                return ref Unsafe.AsRef<T>((void*)_arena.Resolve(handle));
            }
        }

        public void Set<T>(MemoryHandle handle, in T value) where T : unmanaged
        {
            unsafe
            {
                var ptr = (T*)_arena.Resolve(handle);
                *ptr = value;
            }
        }

        public void Free(MemoryHandle handle)
        {
            _arena.Free(handle);
        }
    }
}