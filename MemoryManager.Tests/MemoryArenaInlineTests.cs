/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     MemoryManager.Tests
 * FILE:        MemoryArenaInlineTests.cs
 * PURPOSE:     Test my Arena and use of it
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

using MemoryManager.Core;
using MemoryManager.Lanes;

namespace MemoryManager.Tests
{
    [TestClass]
    public class MemoryArenaInlineTests
    {
        /// <summary>
        ///     Memories the arena can allocate and resolve primitive and structs.
        /// </summary>
        [TestMethod]
        public void MemoryArena_GenerationalRedirection_WorksCorrectly()
        {
            var config = new MemoryManagerConfig
            {
                FastLaneSize = 64 * 1024,
                SlowLaneSize = 128 * 1024,
                Threshold = 256,
                PolicyCheckInterval = TimeSpan.Zero
            };

            var arena = new MemoryArena(config);

            // SUGAR: Allocate and set a struct
            // The handle returned here has a Version (e.g., ID: 1, Version: 1)
            var structHandle = arena.Store(new MyStruct { X = 123, Y = 3.14f });

            // Force move to slow lane
            // This creates a STUB in FastLane that contains:
            // RedirectToId = -1, RedirectVersion = 1 (or whatever the SlowLane assigned)
            arena.MoveFastToSlow(structHandle);

            // 1. Verify FastLane metadata
            var entry = arena.FastLane.GetEntry(structHandle);
            Assert.IsTrue(entry.IsStub);
            Assert.AreNotEqual(0, entry.RedirectToId);

            // NEW: Verify the versioning handshake
            Assert.AreNotEqual(0, entry.RedirectVersion, "Stub must carry the version assigned by the SlowLane.");

            // 2. Verify SlowLane has the redirected entry
            // We must pass the correct Version here, or SlowLane.Resolve will throw a Zombie exception!
            var redirectHandle = new MemoryHandle(entry.RedirectToId, entry.RedirectVersion, arena.SlowLane);
            Assert.IsTrue(arena.SlowLane.HasHandle(redirectHandle));

            // 3. Confirm data transparency
            // arena.Get internally calls Resolve, which sees the Stub,
            // grabs the RedirectVersion, and hops to the SlowLane automatically.
            var result = arena.Get<MyStruct>(structHandle);
            Assert.AreEqual(123, result.X);
            Assert.AreEqual(3.14f, result.Y, 0.001f);
        }

        [TestMethod]
        public void MemoryArena_ZombieHandle_ThrowsException()
        {
            var arena = new MemoryArena(new MemoryManagerConfig { FastLaneSize = 1024 });

            // 1. Allocate something
            var originalHandle = arena.Store(100);
            var firstVersion = originalHandle.Version;

            // 2. Free it
            arena.Free(originalHandle);

            // 3. Force a new allocation into the SAME slot (if your ID logic allows recycling)
            // Even if the ID is the same, the Version will increment to firstVersion + 1
            var newHandle = arena.Store(200);

            // 4. Try to use the OLD handle (The Zombie)
            // This should now trigger our new safety guard!
            Assert.ThrowsException<AccessViolationException>(() =>
            {
                var data = arena.Get<int>(originalHandle);
            });
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

            // SUGAR 1: Allocate and set an int.
            // This handle now carries a Version (e.g., ID: 1, Version: 1)
            var intHandle = arena.Store(777);

            // SUGAR 2: Allocate and set a struct.
            var structHandle = arena.Store(new MyStruct { X = 123, Y = 3.14f });

            // Assert initial values
            Assert.AreEqual(777, arena.Get<int>(intHandle));
            var readStruct = arena.Get<MyStruct>(structHandle);
            Assert.AreEqual(123, readStruct.X);
            Assert.AreEqual(3.14f, readStruct.Y, 0.001f);

            // --- THE BIG MOVE ---
            // FastLane creates a Stub.
            // Stub.RedirectToId = SlowLane ID
            // Stub.RedirectVersion = SlowLane Version
            arena.MoveFastToSlow(structHandle);

            // 1. Verify FastLane still has the stub
            Assert.IsTrue(arena.FastLane.HasHandle(structHandle));
            var entry = arena.FastLane.GetEntry(structHandle);

            Assert.IsTrue(entry.IsStub);
            Assert.AreNotEqual(0, entry.RedirectToId, "Stub must point to a valid ID.");

            // 2. Verify SlowLane has the redirected entry
            // FIX: We must use the RedirectVersion from the metadata!
            // If we pass 0 or guess, the SlowLane will throw a Zombie Exception.
            var redirectHandle = new MemoryHandle(entry.RedirectToId, entry.RedirectVersion, arena.SlowLane);
            Assert.IsTrue(arena.SlowLane.HasHandle(redirectHandle));

            // 3. Confirm data transparency
            // This is the magic part: the user still uses 'structHandle' (the FastLane one).
            // Internally, Resolve sees the stub, sees the RedirectVersion, and
            // fetches the data from the SlowLane automatically.
            var result = arena.Get<MyStruct>(structHandle);
            Assert.AreEqual(123, result.X);
            Assert.AreEqual(3.14f, result.Y, 0.001f);
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
        /// Memories the arena canary catches buffer overrun throws access violation exception.
        /// </summary>
        [TestMethod]
        [TestCategory("Safety")]
        public unsafe void MemoryArena_CanaryCatchesBufferOverrun_ThrowsAccessViolationException()
        {
#if DEBUG
            // Arrange
            var config = new MemoryManagerConfig { FastLaneSize = 1024 * 1024 };
            var arena = new MemoryArena(config);

            // Allocate a small block of 16 bytes
            var handle = arena.Allocate(16);
            var entry = arena.GetEntry(handle);

            // Resolve the raw byte pointer to our user data space
            byte* userDataPtr = (byte*)arena.Resolve(handle);

            // 1. INTENTIONAL CORRUPTION: 
            // The post-canary sits exactly at (userDataPtr + entry.Size)
            uint* postCanaryPtr = (uint*)(userDataPtr + entry.Size);

            // Overwrite the original 0xDEADBEEF signature with poison data
            *postCanaryPtr = 0x00000000;

            // Act & Assert
            Assert.ThrowsException<AccessViolationException>(() =>
            {
                // Freeing the handle triggers the MemoryCanary.Validate phase!
                arena.Free(handle);
            }, "The arena compactor/free engine must catch a post-canary breach and halt.");
#else
    // In Release mode, pass automatically since canaries aren't compiled
    Assert.Inconclusive("Canary tests are ignored in Release builds.");
#endif
        }

        /// <summary>
        /// Memories the arena canary catches buffer underrun throws access violation exception.
        /// </summary>
        [TestMethod]
        [TestCategory("Safety")]
        public unsafe void MemoryArena_CanaryCatchesBufferUnderrun_ThrowsAccessViolationException()
        {
#if DEBUG
            // Arrange
            var config = new MemoryManagerConfig { FastLaneSize = 1024 * 1024 };
            var arena = new MemoryArena(config);

            var handle = arena.Allocate(16);
            byte* userDataPtr = (byte*)arena.Resolve(handle);

            // 2. INTENTIONAL CORRUPTION:
            // The pre-canary sits exactly 4 bytes behind the user data pointer offset
            uint* preCanaryPtr = (uint*)(userDataPtr - MemoryCanary.Size);

            // Poison the pre-canary layout signature
            *preCanaryPtr = 0xBADF00D;

            // Act & Assert
            Assert.ThrowsException<AccessViolationException>(() =>
            {
                arena.Free(handle);
            }, "The arena compactor/free engine must catch a pre-canary breach and halt.");
#else
    Assert.Inconclusive("Canary tests are ignored in Release builds.");
#endif
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