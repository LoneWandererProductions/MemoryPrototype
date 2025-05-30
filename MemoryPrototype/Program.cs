using System;
using System.Runtime.InteropServices;
using MemoryManager;

namespace MemoryPrototype
{
    internal static class Program
    {
        private static void Main(string[] args)
        {
            Console.WriteLine("Hello World!");

            var config = new MemoryManagerConfig
            {
                FastLaneSize = 1024 * 1024,       // 1 MB
                SlowLaneSize = 10 * 1024 * 1024, // 10 MB
                Threshold = 4096,                 // Switch threshold between lanes
                EnableAutoCompaction = true,
                CompactionThreshold = 0.90,
                SlowLaneUsageThreshold = 0.85,
                SlowLaneSafetyMargin = 0.10,
                PolicyCheckInterval = TimeSpan.FromSeconds(10)
            };

            var arena = new MemoryArena(config);

            // --- Raw MemoryArena usage (more control, more verbose) ---
            var size = Marshal.SizeOf<MyStruct>();
            var handleRaw = arena.Allocate(size);
            ref var dataRaw = ref arena.Get<MyStruct>(handleRaw);
            dataRaw.Value = 123;
            Console.WriteLine($"Raw arena Value: {dataRaw.Value}");
            arena.Free(handleRaw);

            // --- TypedMemoryArena usage (simpler, more abstract) ---
            TypedMemoryArena.Initialize(arena);
            var instance = TypedMemoryArena.Instance;

            var handleTyped = instance.Allocate<MyStruct>();
            instance.Set(handleTyped, new MyStruct { Value = 456, PositionX = 1.1f, PositionY = 2.2f });
            ref var dataTyped = ref instance.Get<MyStruct>(handleTyped);
            Console.WriteLine($"Typed arena Value: {dataTyped.Value}");
            instance.Free(handleTyped);

            // Optionally run manual compaction
            arena.RunMaintenanceCycle();

            arena.DebugDump();

            Console.ReadLine();
        }
    }
}