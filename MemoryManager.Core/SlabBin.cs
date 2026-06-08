/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     MemoryManager.Core
 * FILE:        SlabBin.cs
 * PURPOSE:     Contains the SlabBin struct, which manages fixed-size buckets for the SlabLane. It tracks free slots and their offsets for efficient allocation and deallocation.
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

using System.Runtime.CompilerServices;

namespace MemoryManager.Core
{
    /// <summary>
    /// The SlabBin struct manages fixed-size buckets for the SlabLane. It tracks free slots and their offsets for efficient allocation and deallocation.
    /// </summary>
    public struct SlabBin
    {
        /// <summary>
        /// The size class
        /// </summary>
        public readonly int SizeClass;

        /// <summary>
        /// The physical slot size
        /// </summary>
        public readonly int PhysicalSlotSize;

        /// <summary>
        /// The free offsets
        /// </summary>
        private readonly int[] _freeOffsets;

        /// <summary>
        /// The top
        /// </summary>
        private int _top;

        /// <summary>
        /// Initializes a new instance of the <see cref="SlabBin"/> struct.
        /// </summary>
        /// <param name="sizeClass">The size class.</param>
        /// <param name="physicalSlotSize">Size of the physical slot.</param>
        /// <param name="baseOffset">The base offset.</param>
        /// <param name="totalSlots">The total slots.</param>
        public SlabBin(int sizeClass, int physicalSlotSize, int baseOffset, int totalSlots)
        {
            SizeClass = sizeClass;
            PhysicalSlotSize = physicalSlotSize;
            _freeOffsets = new int[totalSlots];
            _top = totalSlots;

            // Populate lookup index coordinates backwards to create high-speed LIFO cache hits
            for (int i = 0; i < totalSlots; i++)
            {
                _freeOffsets[i] = baseOffset + i * physicalSlotSize;
            }
        }

        /// <summary>
        /// Gets the free count.
        /// </summary>
        /// <value>
        /// The free count.
        /// </value>
        public int FreeCount => _top;

        /// <summary>
        /// Pops the slot offset.
        /// </summary>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int PopSlotOffset() => _freeOffsets[--_top];

        /// <summary>
        /// Pushes the slot offset.
        /// </summary>
        /// <param name="offset">The offset.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void PushSlotOffset(int offset) => _freeOffsets[_top++] = offset;
    }
}