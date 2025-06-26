/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     Core
 * FILE:        BlockState.cs
 * PURPOSE:     Your file purpose here
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

namespace Core
{
    public enum BlockState 
    { 
        Free, 
        Allocated, 
        Deleted, 
        Cold, 
        Hot, 
        Aging, 
        Protected 
    }

}
