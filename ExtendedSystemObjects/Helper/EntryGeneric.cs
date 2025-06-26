using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace ExtendedSystemObjects.Helper
{
    [StructLayout(LayoutKind.Sequential)]
    public struct EntryGeneric<TValue>
    {
        public int Key;
        public TValue Value;
        public byte Used;
    }
}
