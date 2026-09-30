/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     MemoryManager.Tests
 * FILE:        ArenaTypesPreferredConfigTests.cs
 * PURPOSE:     MS Unit sample tests demonstrating each Arena container running on its preferred MemoryManagerConfig profile.
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

using MemoryManager.Core;
using MemoryManager.Types;

namespace MemoryManager.Tests
{
    /// <summary>
    /// Integration and sample unit tests for all <see cref="MemoryManager.Types"/> containers[cite: 1, 2, 3, 4, 5, 6, 7, 8, 9],
    /// executed against their workload-matched <see cref="MemoryManagerConfig"/> presets[cite: 1].
    /// </summary>
    [TestClass]
    public sealed class ArenaTypesPreferredConfigTests
    {
        /// <summary>
        /// Coordinate Struct.
        /// </summary>
        private struct Vector3
        {
            public float X, Y, Z;
        }

        /// <summary>
        /// Sample Material configuration
        /// </summary>
        private struct MaterialConfig
        {
            public float Roughness;
            public float Metallic;
            public int TextureId;
        }

        // --- PRESET 1: BULK PROCESSING (FreeList for Dynamic Resizing Collections)[cite: 1] ---

        /// <summary>
        /// Arenas the list sample bulk processing configuration.
        /// </summary>
        [TestMethod]
        [TestCategory("Samples")]
        public void ArenaList_Sample_BulkProcessingConfig()
        {
            // FreeList handles out-of-order frees cleanly when buffers double during growth[cite: 1]
            using var arena = new MemoryArena(MemoryManagerConfig.CreateForBulkProcessing(2 * 1024 * 1024));
            using var list = new ArenaList<int>(arena, initialCapacity: 4);

            for (int i = 1; i <= 10; i++)
            {
                list.Add(i * 10);
            }

            Assert.AreEqual(10, list.Count);
            Assert.AreEqual(10, list[0]);
            Assert.AreEqual(100, list[9]);
        }

        /// <summary>
        /// Arenas the queue sample bulk processing configuration.
        /// </summary>
        [TestMethod]
        [TestCategory("Samples")]
        public void ArenaQueue_Sample_BulkProcessingConfig()
        {
            using var arena = new MemoryArena(MemoryManagerConfig.CreateForBulkProcessing(2 * 1024 * 1024));
            using var queue = new ArenaQueue<Vector3>(arena, initialCapacity: 4);

            queue.Enqueue(new Vector3 { X = 1, Y = 0, Z = 0 });
            queue.Enqueue(new Vector3 { X = 0, Y = 1, Z = 0 });

            Vector3 first = queue.Dequeue();
            Assert.AreEqual(1.0f, first.X);
            Assert.AreEqual(1, queue.Count);
        }

        /// <summary>
        /// Arenas the stack sample bulk processing configuration.
        /// </summary>
        [TestMethod]
        [TestCategory("Samples")]
        public void ArenaStack_Sample_BulkProcessingConfig()
        {
            using var arena = new MemoryArena(MemoryManagerConfig.CreateForBulkProcessing(2 * 1024 * 1024));
            using var stack = new ArenaStack<int>(arena, initialCapacity: 4);

            stack.Push(100);
            stack.Push(200);

            Assert.AreEqual(200, stack.Peek());
            Assert.AreEqual(200, stack.Pop());
            Assert.AreEqual(100, stack.Pop());
        }

        /// <summary>
        /// Arenas the map sample bulk processing configuration.
        /// </summary>
        [TestMethod]
        [TestCategory("Samples")]
        public void ArenaMap_Sample_BulkProcessingConfig()
        {
            using var arena = new MemoryArena(MemoryManagerConfig.CreateForBulkProcessing(2 * 1024 * 1024));
            using var map = new ArenaMap<int, float>(arena, initialCapacity: 8);

            map.AddOrUpdate(42, 3.14159f);
            map.AddOrUpdate(100, 2.71828f);

            Assert.IsTrue(map.TryGetValue(42, out float val));
            Assert.AreEqual(3.14159f, val);

            map.Remove(42);
            Assert.IsFalse(map.TryGetValue(42, out _));
        }

        /// <summary>
        /// Arenas the sparse set sample bulk processing configuration.
        /// </summary>
        [TestMethod]
        [TestCategory("Samples")]
        public void ArenaSparseSet_Sample_BulkProcessingConfig()
        {
            using var arena = new MemoryArena(MemoryManagerConfig.CreateForBulkProcessing(2 * 1024 * 1024));
            using var set = new ArenaSparseSet<Vector3>(arena, initialMaxEntityId: 100, initialDenseCapacity: 8);

            // Entity IDs 12 and 500 mapped to contiguous components
            set.Add(12, new Vector3 { X = 5, Y = 5, Z = 5 });
            set.Add(500, new Vector3 { X = 1, Y = 2, Z = 3 });

            Assert.IsTrue(set.Contains(500));
            Assert.AreEqual(2, set.Count);

            // Contiguous slice for zero-GC iteration
            Span<Vector3> activeComponents = set.AsSpan();
            Assert.AreEqual(2, activeComponents.Length);
        }

        /// <summary>
        /// Arenas the priority queue sample bulk processing configuration.
        /// </summary>
        [TestMethod]
        [TestCategory("Samples")]
        public void ArenaPriorityQueue_Sample_BulkProcessingConfig()
        {
            using var arena = new MemoryArena(MemoryManagerConfig.CreateForBulkProcessing(2 * 1024 * 1024));
            using var queue = new ArenaPriorityQueue<int, int>(arena, initialCapacity: 8);

            // Payload Entity IDs with Priority (lower number = higher priority)
            queue.Enqueue(1001, 50); // Low priority
            queue.Enqueue(2002, 1);  // Critical priority
            queue.Enqueue(3003, 10); // Medium priority

            Assert.AreEqual(2002, queue.Dequeue()); // Priority 1
            Assert.AreEqual(3003, queue.Dequeue()); // Priority 10
            Assert.AreEqual(1001, queue.Dequeue()); // Priority 50
        }

        // --- PRESET 2: OBJECT POOLING (Slab / Fixed Bins for Slot Recycling)[cite: 1] ---

        /// <summary>
        /// Arenas the pool sample object pooling configuration.
        /// </summary>
        [TestMethod]
        [TestCategory("Samples")]
        public void ArenaPool_Sample_ObjectPoolingConfig()
        {
            // Slab allocation eliminates compaction stalls and zeroes fragmentation for uniform slots[cite: 1]
            using var arena = new MemoryArena(MemoryManagerConfig.CreateForObjectPooling(4 * 1024 * 1024));
            using var pool = new ArenaPool<Vector3>(arena, initialCapacity: 16);

            ref var particle = ref pool.Rent(out int index);
            particle.X = 10.0f;
            particle.Y = 20.0f;

            Assert.AreEqual(10.0f, pool.Get(index).X);

            pool.Return(index);
            Assert.AreEqual(0, pool.ActiveCount);
        }

        /// <summary>
        /// Arenas the intern pool sample object pooling configuration.
        /// </summary>
        [TestMethod]
        [TestCategory("Samples")]
        public void ArenaInternPool_Sample_ObjectPoolingConfig()
        {
            using var arena = new MemoryArena(MemoryManagerConfig.CreateForObjectPooling(4 * 1024 * 1024));
            using var internPool = new ArenaInternPool<MaterialConfig>(arena, initialCapacity: 16);

            var matA = new MaterialConfig { Roughness = 0.5f, Metallic = 0.8f, TextureId = 12 };
            var matB = new MaterialConfig { Roughness = 0.5f, Metallic = 0.8f, TextureId = 12 };

            int idxA = internPool.Add(matA);
            int idxB = internPool.Add(matB);

            // Deduplication verification: identical structs share the exact same slot index
            Assert.AreEqual(idxA, idxB);
            Assert.AreEqual(2, internPool.GetRefCount(idxA));
            Assert.AreEqual(1, internPool.UniqueCount);

            internPool.Release(idxA);
            Assert.AreEqual(1, internPool.GetRefCount(idxA));
        }

        // --- PRESET 3: FRAME SCRATCH / GAME LOOP (Linear Bump for Ultra-Fast Transient Buffers)[cite: 1] ---

        /// <summary>
        /// Arenas the buffer sample frame scratch configuration.
        /// </summary>
        [TestMethod]
        [TestCategory("Samples")]
        public void ArenaBuffer_Sample_FrameScratchConfig()
        {
            // LinearBump provides max allocation speed for transient inner-loop scratchpads[cite: 1]
            using var arena = new MemoryArena(MemoryManagerConfig.CreateForFrameScratch(2 * 1024 * 1024));
            using var buffer = new ArenaBuffer<int>(arena, capacity: 16);

            buffer.Add(5);
            buffer.Add(10);

            Assert.AreEqual(2, buffer.Count);
            
            // Fast reset between frames without deallocating unmanaged memory[cite: 3]
            buffer.Clear();
            Assert.AreEqual(0, buffer.Count);
        }

        /// <summary>
        /// Arenas the rent sample frame scratch configuration.
        /// </summary>
        [TestMethod]
        [TestCategory("Samples")]
        public void ArenaRent_Sample_FrameScratchConfig()
        {
            using var arena = new MemoryArena(MemoryManagerConfig.CreateForFrameScratch(2 * 1024 * 1024));

            // RAII stack scope renting[cite: 7]
            using (var rent = new ArenaRent<byte>(arena, count: 256))
            {
                Span<byte> bytes = rent.Span;
                bytes.Fill(0xAB);
                Assert.AreEqual(256, rent.Length);
                Assert.AreEqual(0xAB, bytes[0]);
            }
        }

        /// <summary>
        /// Arenas the bit set sample frame scratch configuration.
        /// </summary>
        [TestMethod]
        [TestCategory("Samples")]
        public void ArenaBitSet_Sample_FrameScratchConfig()
        {
            using var arena = new MemoryArena(MemoryManagerConfig.CreateForFrameScratch(2 * 1024 * 1024));
            using var bitSet = new ArenaBitSet(arena, initialBitCapacity: 128);

            bitSet.Set(42);
            bitSet.Set(100);

            Assert.IsTrue(bitSet.Get(42));
            Assert.IsTrue(bitSet.Get(100));
            Assert.IsFalse(bitSet.Get(7));

            bitSet.Unset(42);
            Assert.IsFalse(bitSet.Get(42));
        }
    }
}