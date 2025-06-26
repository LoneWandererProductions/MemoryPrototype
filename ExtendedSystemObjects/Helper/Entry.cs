/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     ExtendedSystemObjects.Helper
 * FILE:        Entry.cs
 * PURPOSE:     Your file purpose here
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

using System.Runtime.InteropServices;

namespace ExtendedSystemObjects.Helper
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Entry
    {
        public int Key;
        public int Value;
        public byte Used;
    }
}
