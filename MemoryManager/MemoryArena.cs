using System;
using System.Diagnostics;
using Core;
using Lanes;

namespace MemoryManager
{
    public sealed class MemoryArena
    {
        private readonly FastLane _fastLane;
        private readonly SlowLane _slowLane;
        private readonly int _threshold;

        private bool IsFastLaneHandle(MemoryHandle handle) => _fastLane.HasHandle(handle);
        private bool IsSlowLaneHandle(MemoryHandle handle) => _slowLane.HasHandle(handle);


        public MemoryArena(int fastLaneSize, int slowLaneSize, int fastLaneThreshold)
        {
            _slowLane = new SlowLane(slowLaneSize);
            _fastLane = new FastLane(fastLaneSize, _slowLane);
            _threshold = fastLaneThreshold;
        }

        public unsafe void MoveFastToSlow(MemoryHandle fastHandle)
        {
            var fastEntry = _fastLane.GetEntry(fastHandle);
            var size = fastEntry.Size;

            var slowHandle = _slowLane.Allocate(size);

            Buffer.MemoryCopy(
                (void*)(_fastLane.Buffer + fastEntry.Offset),
                (void*)_slowLane.Resolve(slowHandle),
                size,
                size);

            _fastLane.ReplaceWithStub(fastHandle, slowHandle);
        }

        public void AutoCompactFastLane()
        {
            // If FastLane usage above threshold (e.g. 90%)
            if (_fastLane.UsagePercentage() > 0.9)
            {
                // Find candidates to move
                foreach (var handle in _fastLane.GetHandles())
                {
                    // Logic to select candidates can be based on size, age, etc.
                    MoveFastToSlow(handle);
                }

                _fastLane.Compact();
            }
        }

        public MemoryHandle Allocate(int size)
        {
            if (size <= _threshold && _fastLane.CanAllocate(size))
                return _fastLane.Allocate(size);
            if (_slowLane.CanAllocate(size))
                return _slowLane.Allocate(size);

            throw new OutOfMemoryException("Neither lane could allocate memory.");
        }

        public void CompactAll()
        {
            _fastLane.Compact();
            _slowLane.Compact();
        }

        public void Free(MemoryHandle handle)
        {
            if (IsFastLaneHandle(handle))
                _fastLane.Free(handle);
            else if(IsSlowLaneHandle(handle))
                _slowLane.Free(handle);
        }

        public void DebugDump()
        {
            Trace.WriteLine("Fast Lane:");
            Trace.WriteLine(_fastLane.DebugDump());
            Trace.WriteLine("Slow Lane:");
            Trace.WriteLine(_slowLane.DebugDump());
        }
    }
}