/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     MemoryManager.Tests
 * FILE:        MemoryArenaInlineTests.cs
 * PURPOSE:     Test my Arena and use of it
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

using MemoryManager.Core;
using MemoryManager.Lanes;
using System.Runtime.InteropServices;

namespace MemoryManager.Tests
{
    [TestClass]
    public class MemoryArenaInlineTests
    {
        /// <summary>
        ///     Memories the arena can allocate and resolve primitive and structs.
        /// </summary>
        [TestMethod]
        public void MemoryArenaCanAllocateAndResolvePrimitiveAndStructs()
        {
            var config = new MemoryManagerConfig
            {
                FastLaneSize = 64 * 1024,
                SlowLaneSize = 128 * 1024,
                Threshold = 256,
                PolicyCheckInterval = TimeSpan.Zero,
                FastLaneUsageThreshold = 0.85,
                CompactionThreshold = 0.90,
                SlowLaneUsageThreshold = 0.80,
                SlowLaneSafetyMargin = 0.10
            };

            var arena = new MemoryArena(config);

            // Allocate and write an int
            var intHandle = arena.Allocate(sizeof(int));
            arena.Get<int>(intHandle) = 777;

            // Allocate and write a struct
            var structHandle = arena.Allocate(Marshal.SizeOf<MyStruct>());
            arena.Get<MyStruct>(structHandle) = new MyStruct { X = 123, Y = 3.14f };

            //Debug
            arena.DebugDump();

            // Assert values
            Assert.AreEqual(777, arena.Get<int>(intHandle));
            var readStruct = arena.Get<MyStruct>(structHandle);
            Assert.AreEqual(123, readStruct.X);
            Assert.AreEqual(3.14f, readStruct.Y, 0.001f);

            // Force move to slow lane
            arena.MoveFastToSlow(structHandle);

            //Debug
            arena.DebugDump();

            // Verify FastLane still has the stub
            Assert.IsTrue(arena.FastLane.HasHandle(structHandle));
            var entry = arena.FastLane.GetEntry(structHandle);
            Assert.IsTrue(entry.IsStub);

            // Use the new integer ID logic
            Assert.AreNotEqual(0, entry.RedirectToId, "Stub must point to a valid ID (not 0).");

            // Verify SlowLane has the redirected entry
            // Reconstruct the memory handle using the integer ID
            var redirectHandle = new MemoryHandle(entry.RedirectToId, arena.SlowLane);
            Assert.IsTrue(arena.SlowLane.HasHandle(redirectHandle));

            // Confirm data still accessible
            var result = arena.Get<MyStruct>(structHandle);
            Assert.AreEqual(123, result.X);
            Assert.AreEqual(3.14f, result.Y, 0.001f);

            //Debug
            arena.DebugDump();
        }


        /// <summary>
        /// Memories the arena can allocate and resolve primitive and structs with extensions.
        /// </summary>
        [TestMethod]
        public void MemoryArenaCanAllocateAndResolvePrimitiveAndStructsWithExtensions()
        {
            var config = new MemoryManagerConfig
            {
                FastLaneSize = 64 * 1024,
                SlowLaneSize = 128 * 1024,
                Threshold = 256,
                PolicyCheckInterval = TimeSpan.Zero
            };

            var arena = new MemoryArena(config);

            // SUGAR 1: Allocate and set an int in one clean line
            var intHandle = arena.Store(777);

            // SUGAR 2: Allocate and set a struct in one clean line
            var structHandle = arena.Store(new MyStruct { X = 123, Y = 3.14f });

            // Assert values
            Assert.AreEqual(777, arena.Get<int>(intHandle));
            var readStruct = arena.Get<MyStruct>(structHandle);
            Assert.AreEqual(123, readStruct.X);
            Assert.AreEqual(3.14f, readStruct.Y, 0.001f);

            // Force move to slow lane
            arena.MoveFastToSlow(structHandle);

            // Verify FastLane still has the stub
            Assert.IsTrue(arena.FastLane.HasHandle(structHandle));
            var entry = arena.FastLane.GetEntry(structHandle);
            Assert.IsTrue(entry.IsStub);
            Assert.AreNotEqual(0, entry.RedirectToId, "Stub must point to a valid ID (not 0).");

            // Verify SlowLane has the redirected entry
            var redirectHandle = new MemoryHandle(entry.RedirectToId, arena.SlowLane);
            Assert.IsTrue(arena.SlowLane.HasHandle(redirectHandle));

            // Confirm data still accessible
            var result = arena.Get<MyStruct>(structHandle);
            Assert.AreEqual(123, result.X);
            Assert.AreEqual(3.14f, result.Y, 0.001f);
        }

        /// <summary>
        /// Memories the arena bulk set and span validation.
        /// </summary>
        [TestMethod]
        public void MemoryArenaBulkSetAndSpanValidation()
        {
            var config = new MemoryManagerConfig { FastLaneSize = 1024 * 1024 };
            var arena = new MemoryArena(config);

            // 1. Create source data in managed C#
            int[] sourceData = { 10, 20, 30, 40, 50 };

            // 2. Allocate an array in the Arena
            var handle = arena.AllocateArray<int>(sourceData.Length);

            // 3. Use BulkSet to upload the data
            arena.BulkSet<int>(handle, sourceData.AsSpan());

            // 4. Validate the upload worked
            unsafe
            {
                var ptr = (int*)arena.Resolve(handle);
                Assert.AreEqual(10, ptr[0]);
                Assert.AreEqual(50, ptr[4]);
            }

            // 5. Use a Span to modify the unmanaged data in-place
            // (This simulates a high-speed systems task like a physics update)
            var unmanagedSpan = arena.GetSpan<int>(handle, sourceData.Length);
            for (var i = 0; i < unmanagedSpan.Length; i++)
            {
                unmanagedSpan[i] *= 2;
            }

            // 6. Final Validation
            Assert.AreEqual(20, unmanagedSpan[0]);
            Assert.AreEqual(100, unmanagedSpan[4]);

            // Ensure the original managed array was NOT modified (it was a copy)
            Assert.AreEqual(10, sourceData[0]);
        }

        /// <summary>
        /// Memories the arena janitor and compaction stress test.
        /// </summary>
        [TestMethod]
        [TestCategory("Stress")]
        public void MemoryArenaJanitorAndCompactionStressTest()
        {
            var config = new MemoryManagerConfig
            {
                FastLaneSize = 512 * 1024, // 512KB
                SlowLaneSize = 2 * 1024 * 1024, // 2MB
                MaxFastLaneAgeFrames = 10,
                FastLaneLargeEntryThreshold = 1024
            };

            var arena = new MemoryArena(config);
            var handles = new List<MemoryHandle>();
            const int objectCount = 1000;

            // 1. Fill the FastLane with a mix of Hot and Cold data
            for (var i = 0; i < objectCount; i++)
            {
                // Every 5th object is "Cold" (Janitor bait)
                var hints = i % 5 == 0 ? AllocationHints.Cold : AllocationHints.None;

                // Store a unique ID in each block to verify later
                var handle = arena.AllocateAndStore(i, hints: hints);
                handles.Add(handle);
            }

            // 2. Simulate "Time passing" to age the allocations
            for (var frame = 0; frame < 15; frame++)
            {
                arena.RunMaintenanceCycle(); // Janitor starts looking at Age and Hints
            }

            // 3. Force a full compaction of both lanes
            arena.CompactAll();

            // 4. Verification: All handles must still resolve to their original unique ID
            for (var i = 0; i < objectCount; i++)
            {
                var handle = handles[i];
                var val = arena.Get<int>(handle);

                Assert.AreEqual(i, val, $"Data corruption at index {i}. Handle ID: {handle.Id}");

                // Verify that "Cold" objects actually moved to the SlowLane (Negative IDs in Redirect)
                if (i % 5 == 0)
                {
                    var entry = arena.GetEntry(handle);
                    Assert.IsTrue(entry.IsStub, $"Object {i} should have been moved by the Janitor.");
                    Assert.AreNotEqual(0, entry.RedirectToId);
                }
            }

            // 5. Check final health
            arena.DebugDump();
            Assert.AreEqual(0, arena.FastLane.EstimateFragmentation());
        }

        /// <summary>
        /// Memories the arena configuration switch uses correct fast lane strategy.
        /// </summary>
        [TestMethod]
        [TestCategory("Architecture")]
        public void MemoryArena_ConfigSwitch_UsesCorrectFastLaneStrategy()
        {
            // --- 1. Test FreeList Strategy (The Classic Engine) ---
            var configFreeList = new MemoryManagerConfig
            {
                FastLaneSize = 64 * 1024,
                SlowLaneSize = 128 * 1024,
                FastLaneStrategy = AllocatorStrategy.FreeList // Explicitly request the Free-List
            };

            var arenaFreeList = new MemoryArena(configFreeList);

            // Assert the internal type is correct
            Assert.IsInstanceOfType(arenaFreeList.FastLane, typeof(FastLane),
                "Arena should use the original FastLane (Free-List) when configured.");

            // Prove the interface works seamlessly
            var handle1 = arenaFreeList.AllocateAndStore(42);
            Assert.AreEqual(42, arenaFreeList.Get<int>(handle1), "FreeList allocation failed.");


            // --- 2. Test LinearBump Strategy (The Speed Demon) ---
            var configBump = new MemoryManagerConfig
            {
                FastLaneSize = 64 * 1024,
                SlowLaneSize = 128 * 1024,
                FastLaneStrategy = AllocatorStrategy.LinearBump // Explicitly request the Bump Allocator
            };

            var arenaBump = new MemoryArena(configBump);

            // Assert the internal type is correct
            Assert.IsInstanceOfType(arenaBump.FastLane, typeof(LinearLane),
                "Arena should use the new LinearLane (Bump Allocator) when configured.");

            // Prove the interface works seamlessly
            var handle2 = arenaBump.AllocateAndStore(99);
            Assert.AreEqual(99, arenaBump.Get<int>(handle2), "LinearBump allocation failed.");
        }

        /// <summary>
        /// Memories the arena default configuration works out of the box.
        /// </summary>
        [TestMethod]
        [TestCategory("Architecture")]
        public void MemoryArena_DefaultConfig_WorksOutOfTheBox()
        {
            // --- THE FRESH USER SCENARIO ---
            // Absolutely zero custom configuration. Just the raw defaults.
            var defaultConfig = new MemoryManagerConfig();

            // 1. Initialization should not throw an exception
            var arena = new MemoryArena(defaultConfig);

            // 2. Verify the default strategy (You set it to FreeList in the config!)
            Assert.IsInstanceOfType(arena.FastLane, typeof(FastLane),
                "The default config should initialize the standard FastLane (FreeList).");

            // 3. Test a tiny allocation (Should safely route to FastLane or BlobManager)
            var smallHandle = arena.AllocateAndStore(777);
            Assert.AreEqual(777, arena.Get<int>(smallHandle),
                "Failed to read tiny allocation using default config.");

            // 4. Test a massive allocation
            // Default Threshold is 256KB. Let's allocate an array larger than that
            // to force it directly into the SlowLane to ensure the default boundaries hold.
            var largeSize = 300 * 1024; // 300 KB
            var largeHandle = arena.Allocate(largeSize);

            // The ID should be negative, proving the default Threshold safely pushed it to the SlowLane
            Assert.IsTrue(largeHandle.Id < 0,
                "Large allocation did not route to the SlowLane under default thresholds.");

            // 5. Clean up
            arena.Free(smallHandle);
            arena.Free(largeHandle);
        }

        /// <summary>
        /// Test Struct.
        /// </summary>
        private struct MyStruct
        {
            public int X;
            public float Y;
        }
    }
}