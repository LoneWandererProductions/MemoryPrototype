using System;
using System.Runtime.InteropServices;
using MemoryManager;

namespace MemoryPrototype
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            Console.WriteLine("Hello World!");

            var config = new MemoryManagerConfig
            {
                FastLaneSize = 1024 * 1024, // 1 MB
                SlowLaneSize = 10 * 1024 * 1024, // 10 MB
                Threshold = 4096, // Switch threshold between lanes
                EnableAutoCompaction = true,
                CompactionThreshold = 0.90,
                SlowLaneUsageThreshold = 0.85,
                SlowLaneSafetyMargin = 0.10,
                PolicyCheckInterval = TimeSpan.FromSeconds(10)
            };

            var arena = new MemoryArena(config);

            // Resolve the pointer and work with the data
            var size = Marshal.SizeOf<MyStruct>();
            var handle = arena.Allocate(size);
            ref var data = ref arena.Get<MyStruct>(handle);
            data.Value = 123;

            // Free when done
            arena.Free(handle);

            // Optionally run manual compaction
            arena.RunMaintenanceCycle();
        }
    }
}