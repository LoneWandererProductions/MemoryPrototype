/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     MemoryManagerTests
 * FILE:        MemoryArenaInlineTests.cs
 * PURPOSE:     Your file purpose here
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

using System;
using System.Runtime.InteropServices;
using MemoryManager;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MemoryManagerTests
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
            Assert.IsTrue(entry.RedirectTo.HasValue);

            // Verify SlowLane has the redirected entry
            var redirectHandle = entry.RedirectTo.Value;
            Assert.IsTrue(arena.SlowLane.HasHandle(redirectHandle));

            // Confirm data still accessible
            var result = arena.Get<MyStruct>(structHandle);
            Assert.AreEqual(123, result.X);
            Assert.AreEqual(3.14f, result.Y, 0.001f);

            //Debug
            arena.DebugDump();
        }

        /// <summary>
        ///     Test Struct.
        /// </summary>
        private struct MyStruct
        {
            public int X;
            public float Y;
        }
    }
}