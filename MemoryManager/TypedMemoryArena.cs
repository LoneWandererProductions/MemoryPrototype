using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Core;

namespace MemoryManager
{
    public sealed class TypedMemoryArena
    {
        private readonly MemoryArena _arena;

        public TypedMemoryArena(MemoryArena arena)
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