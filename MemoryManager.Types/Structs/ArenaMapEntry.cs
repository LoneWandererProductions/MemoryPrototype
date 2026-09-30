/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     emoryManager.Types.Structs
 * FILE:        ArenaMapEntry.cs
 * PURPOSE:     Internal slot entry struct for ArenaMap.
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

using MemoryManager.Types.Enums;

namespace MemoryManager.Types.Structs
{
    /// <summary>
    /// Internal entry struct for <see cref="ArenaMap{TKey, TValue}"/>.
    /// </summary>
    /// <typeparam name="TKey">The unmanaged key type.</typeparam>
    /// <typeparam name="TValue">The unmanaged value type.</typeparam>
    internal struct ArenaMapEntry<TKey, TValue>
        where TKey : unmanaged
        where TValue : unmanaged
    {
        /// <summary>
        /// The key
        /// </summary>
        public TKey Key;

        /// <summary>
        /// The value
        /// </summary>
        public TValue Value;

        /// <summary>
        /// The state
        /// </summary>
        public SlotState State;
    }
}