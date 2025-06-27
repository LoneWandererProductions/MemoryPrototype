using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Core
{
    internal class BlobEntry
    {
        public int Id;
        public int Offset;
        public int Size;
        public string? DebugName;
        public int AllocationFrame;
    }
}
