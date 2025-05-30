using System;

namespace Core
{
    public readonly struct MemoryHandle
    {
        public int Id { get; }

        private readonly IMemoryLane _lane;

        public MemoryHandle(int id, IMemoryLane lane)
        {
            Id = id;
            _lane = lane;
        }

        public IntPtr GetPointer()
        {
            return _lane.Resolve(this);
        }

        /// <summary>
        /// Gets a value indicating whether this instance is invalid.
        /// </summary>
        /// <value>
        ///   <c>true</c> if this instance is invalid; otherwise, <c>false</c>.
        /// </value>
        public bool IsInvalid => Id <= 0 || _lane == null;
    }
}