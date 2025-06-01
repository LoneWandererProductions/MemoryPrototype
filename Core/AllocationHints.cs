/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     Core
 * FILE:        AllocationHints.cs
 * PURPOSE:     Your file purpose here
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

using System;

namespace Core
{
    [Flags]
    public enum AllocationHints
    {
        None = 0,
        FrameCritical = 1 << 0,
        Cold = 1 << 1,
        Old = 1 << 2,
        Evictable = Cold | Old
    }
}