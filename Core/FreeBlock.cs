/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     Core
 * FILE:        FreeBlock.cs
 * PURPOSE:     Struct that holds free space.
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

namespace Core
{
    /// <summary>
    /// Info about free spaces
    /// </summary>
    public struct FreeBlock
    {
        /// <summary>
        /// The offset
        /// </summary>
        public int Offset;

        /// <summary>
        /// The size
        /// </summary>
        public int Size;
    }
}