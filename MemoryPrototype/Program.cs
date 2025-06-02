/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     MemoryPrototype
 * FILE:        Program.cs
 * PURPOSE:     Sample Program to showcase the Syntax.
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

using System;
using System.Runtime.InteropServices;
using MemoryManager;

namespace MemoryPrototype
{
    internal static class Program
    {
        /// <summary>
        ///     Defines the entry point of the application.
        ///     Here we just showcase some basic syntax.
        /// </summary>
        /// <param name="args">The arguments.</param>
        private static void Main(string[] args)
        {
            Console.WriteLine("Hello Memory.");

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

            Console.WriteLine($"Expected used memory: {config.GetEstimatedReservedMegabytes()} as mb.");

            var arena = new MemoryArena(config);

            // --- Raw MemoryArena usage (more control, more verbose) ---
            var size = Marshal.SizeOf<MyStruct>();
            var handleRaw = arena.Allocate(size);
            ref var dataRaw = ref arena.Get<MyStruct>(handleRaw);
            dataRaw.Value = 123;
            Console.WriteLine($"Raw arena Value: {dataRaw.Value}");
            arena.Free(handleRaw);

            // --- TypedMemoryArena usage (simpler, more abstract) ---
            TypedMemoryArenaSingleton.Initialize(arena);
            var instance = TypedMemoryArenaSingleton.Instance;

            var handleTyped = instance.Allocate<MyStruct>();
            instance.Set(handleTyped, new MyStruct { Value = 456, PositionX = 1.1f, PositionY = 2.2f });
            ref var dataTyped = ref instance.Get<MyStruct>(handleTyped);
            Console.WriteLine($"Typed arena Value: {dataTyped.Value}");
            instance.Free(handleTyped);

            // Optionally run manual compaction
            arena.RunMaintenanceCycle();

            // --- TypedMemoryArena usage, handle arrays ---
            // Allocate array of 10 ints
            const int count = 10;
            var handle = instance.Allocate<int>(count);

            // Get a Span<int> to safely access the array memory
            var intArray = instance.GetSpan<int>(handle, count);

            // Initialize array elements
            for (var i = 0; i < count; i++) intArray[i] = i * 2;

            // Read elements
            for (var i = 0; i < count; i++) Console.WriteLine($"Element {i} = {intArray[i]}");

            // When done, free the allocated memory
            instance.Free(handle);

            //a bit more compressed.
            handle = instance.AllocateAndSet(new MyStruct { Value = 456, PositionX = 3.1f, PositionY = 2.2f });
            ref var data = ref instance.Get<MyStruct>(handle);
            Console.WriteLine(data.PositionX);

            //and with direct access
            ref var data2 = ref instance.AllocateAndGet(new MyStruct { Value = 789 });
            data2.PositionX += 10f;
            Console.WriteLine(data2.PositionX);

            arena.DebugDump();

            Console.ReadLine();
        }

        /// <summary>
        ///     Test struct
        /// </summary>
        internal struct MyStruct
        {
            public int Value;
            public float PositionX;
            public float PositionY;
        }
    }
}