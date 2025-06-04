using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Core
{
    using System;
    using System.Runtime.CompilerServices;
    using System.Runtime.InteropServices;

    public unsafe struct NativeArray<T> where T : unmanaged
    {
        private readonly T* _pointer;
        public readonly int Length;

        public NativeArray(void* pointer, int length)
        {
            _pointer = (T*)pointer;
            Length = length;
        }

        public ref T this[int index]
        {
            get
            {
                if ((uint)index >= (uint)Length)
                    throw new IndexOutOfRangeException();
                return ref _pointer[index];
            }
        }

        public Span<T> AsSpan()
        {
            return new Span<T>(_pointer, Length);
        }

        public ReadOnlySpan<T> AsReadOnlySpan()
        {
            return new ReadOnlySpan<T>(_pointer, Length);
        }

        public void Fill(T value)
        {
            var span = AsSpan();
            for (int i = 0; i < Length; i++)
                span[i] = value;
        }
    }

}
