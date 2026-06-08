/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     MemoryManager.Tests
 * FILE:        MemoryArenaTests.cs
 * PURPOSE:     Exhaustive integration and stress verification for the MemoryArena wrapper.
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

using MemoryManager.Core;

namespace MemoryManager.Tests
{
    [TestClass]
    public class MemoryArenaCoreIntegrationTests
    {
        private MemoryManagerConfig _config;

        [TestInitialize]
        public void Setup()
        {
            _config = new MemoryManagerConfig
            {
                FastLaneSize = 256 * 1024,      // 256 KB
                SlowLaneSize = 1024 * 1024,     // 1 MB
                Threshold = 4 * 1024,           // 4 KB Lane Boundary
                FastLaneUsageThreshold = 0.85f,
                SlowLaneUsageThreshold = 0.80f,
                CompactionThreshold = 0.85f,
                SlowLaneSafetyMargin = 0.10f,
                EnableAutoCompaction = false,   // Controlled manually within integration steps
                PolicyCheckInterval = TimeSpan.Zero
            };
        }

        #region --- SECTION 1: LATEST ROUTING & WORKSPACE BOUNDARY RULES ---

        [TestMethod]
        public void Allocate_SizeBelowThreshold_RoutesToFastLane()
        {
            var arena = new MemoryArena(_config);
            int size = 2 * 1024; // 2 KB (< 4 KB Threshold)

            var handle = arena.Allocate(size);

            Assert.IsTrue(handle.Id > 0, "Allocations under the size threshold must receive a positive FastLane ID.");
            Assert.IsFalse(handle.IsInvalid, "Returned handle must declare proof of life.");
        }

        [TestMethod]
        public void Allocate_SizeAboveThreshold_RoutesToSlowLane()
        {
            var arena = new MemoryArena(_config);
            int size = 8 * 1024; // 8 KB (> 4 KB Threshold)

            var handle = arena.Allocate(size);

            Assert.IsTrue(handle.Id < 0, "Allocations over the size threshold must receive a negative SlowLane ID.");
        }

        [TestMethod]
        public void Allocate_SizeSpansEntireFastLane_FallbackRoutesToSlowLane()
        {
            var arena = new MemoryArena(_config);
            // Request size safely underneath the threshold boundary routing rule, 
            // but configure it to request more space than the entire FastLane block contains.
            var tightConfig = new MemoryManagerConfig
            {
                FastLaneSize = 1024,
                SlowLaneSize = 64 * 1024,
                Threshold = 2048 // Rules technically point to FastLane
            };
            var smallArena = new MemoryArena(tightConfig);

            var handle = smallArena.Allocate(1500);

            Assert.IsTrue(handle.Id < 0, "If the FastLane cannot fit the requested block size, the orchestrator must fallback to the SlowLane.");
        }

        [TestMethod]
        public void Allocate_BothLanesExhausted_ThrowsOutOfMemoryException()
        {
            var arena = new MemoryArena(_config);
            int impossibleSize = 2 * 1024 * 1024; // 2 MB (Exceeds total system capacity allocation bounds)

            Assert.ThrowsException<OutOfMemoryException>(() =>
            {
                arena.Allocate(impossibleSize);
            }, "Requesting more memory than total workspace partitions allow must throw an explicit OutOfMemoryException.");
        }

        #endregion

        #region --- SECTION 2: SYNTACTIC SUGAR & EXTENSION STRUCT VALIDATION ---

        [TestMethod]
        public void Extensions_StoreAndGetPrimitive_MaintainsValueFlawlessly()
        {
            var arena = new MemoryArena(_config);
            double diagnosticValue = 12345.67890;

            var handle = arena.Store(diagnosticValue);
            double retrievedValue = arena.Get<double>(handle);

            Assert.AreEqual(diagnosticValue, retrievedValue, "Primitive type serialization extensions must guarantee bitwise alignment reading back data values.");
        }

        [TestMethod]
        public void Extensions_AllocateArrayAndBulkSetSpan_ValidatesDataBoundaries()
        {
            var arena = new MemoryArena(_config);
            int[] sourceData = { 10, 20, 30, 40, 50, 60, 70, 80, 90, 100 };

            var handle = arena.AllocateArray<int>(sourceData.Length);
            arena.BulkSet(handle, sourceData.AsSpan());

            var outputSpan = arena.GetSpan<int>(handle, sourceData.Length);

            CollectionAssert.AreEqual(sourceData, outputSpan.ToArray(), "Bulk span transfers onto flat allocated structural block memory coordinates failed data parity checks.");
        }

        [TestMethod]
        public void Extensions_BulkSetOutOfBoundsPayload_ThrowsArgumentException()
        {
            var arena = new MemoryArena(_config);
            var handle = arena.AllocateArray<short>(5); // Space reserved for 10 bytes
            short[] overflowingPayload = { 1, 2, 3, 4, 5, 6, 7, 8 }; // Payload size requirements = 16 bytes

            Assert.ThrowsException<ArgumentException>(() =>
            {
                arena.BulkSet<short>(handle, overflowingPayload);
            }, "Writing an array payload that exceeds the structural entry allocation size boundaries must throw an explicit ArgumentException.");
        }

        [TestMethod]
        public void Extensions_StoreStringUTF8_DecodesAccurately()
        {
            var arena = new MemoryArena(_config);
            string initialText = "Wayfarer Memory Manager Test String Token #2026!";

            var handle = arena.StoreString(initialText);
            var entry = arena.GetEntry(handle);

            var payloadSpan = arena.GetSpan<byte>(handle, entry.Size);
            string reconstitutedText = System.Text.Encoding.UTF8.GetString(payloadSpan);

            Assert.AreEqual(initialText, reconstitutedText, "String serialization extensions must accurately marshal strings to UTF8 workspace tracks.");
        }

        #endregion

        #region --- SECTION 3: ADVANCED MIGRATION & REVERSAL STRESS ---

        [TestMethod]
        public void MoveSlowToFast_ValidMigration_TransfersDataAndFreesOldSlot()
        {
            var arena = new MemoryArena(_config);
            var baselineStruct = new SampleMetric { IdField = 99, Velocity = 451.2f };

            var slowHandle = arena.Allocate(8 * 1024, AllocationPriority.Critical, AllocationHints.None);
            arena.Get<SampleMetric>(slowHandle) = baselineStruct;

            Assert.IsTrue(slowHandle.Id < 0, "Pre-condition failure: test value must start inside SlowLane territory.");

            // 2. Migrate the layout structural record upward into the FastLane
            var fastHandle = arena.MoveSlowToFast(slowHandle);

            // 3. Assert structural position flipping properties
            Assert.IsTrue(fastHandle.Id > 0, "The newly assigned structural reference handle must point to the FastLane namespace.");

            var migratedStruct = arena.Get<SampleMetric>(fastHandle);
            Assert.AreEqual(baselineStruct.IdField, migratedStruct.IdField, "Data properties dropped or tracking corruption noted during lane promotion.");
            Assert.AreEqual(baselineStruct.Velocity, migratedStruct.Velocity, "Floating point alignment errors inside structure allocation layout shifts.");

            // 4. Assert the old SlowLane handles are cleanly destroyed 
            Assert.ThrowsException<InvalidOperationException>(() =>
            {
                arena.Resolve(slowHandle);
            }, "Resolving the old slow lane index handle after execution transfers must trigger invalid token failures.");
        }

        [TestMethod]
        public void MoveSlowToFast_PassingPositiveIdHandle_ThrowsArgumentException()
        {
            var arena = new MemoryArena(_config);
            var fastHandle = arena.Store(42);

            Assert.ThrowsException<ArgumentException>(() =>
            {
                arena.MoveSlowToFast(fastHandle);
            }, "Invoking a slow-to-fast migration using a fast handle identifier must crash validation steps immediately.");
        }

        #endregion

        #region --- SECTION 4: CONCURRENCY LOCKOUT & MULTITHREAD STRESS ---

        [TestMethod]
        public void Concurrency_ParallelAllocationsAndBackgroundCompaction_MaintainsStateSanity()
        {
            var threadedConfig = new MemoryManagerConfig
            {
                FastLaneSize = 512 * 1024,
                SlowLaneSize = 2 * 1024 * 1024,
                Threshold = 512,
                EnableAutoCompaction = false
            };
            var arena = new MemoryArena(threadedConfig);

            int taskCount = 8;
            int allocationsPerTask = 100;
            var barrier = new Barrier(taskCount + 1); // Blocks synched tracks + 1 compaction thread

            var allocatedHandles = new System.Collections.Concurrent.ConcurrentBag<MemoryHandle>();

            // Thread Loop A: Concurrent allocations executing against shared arena tracks
            var allocationTasks = Enumerable.Range(0, taskCount).Select(t => Task.Run(() =>
            {
                barrier.SignalAndWait(); // Synchronize all workers for high contention
                for (int i = 0; i < allocationsPerTask; i++)
                {
                    var handle = arena.AllocateAndStore(i + (t * 1000));
                    allocatedHandles.Add(handle);

                    // Simulate processing cycles by resolving and updating values immediately
                    ref int currentVal = ref arena.Get<int>(handle);
                    currentVal += 5;
                }
            })).ToList();

            // Thread Loop B: Trigger active maintenance cycles concurrently during operations
            var compactionTask = Task.Run(() =>
            {
                barrier.SignalAndWait();
                for (int i = 0; i < 5; i++)
                {
                    arena.RunMaintenanceCycle();
                    arena.CompactAll();
                    Thread.Sleep(5); // Force brief scheduling gaps to hit race conditions
                }
            });

            // Wait for full work execution completion
            Task.WaitAll(allocationTasks.Concat(new[] { compactionTask }).ToArray());

            // Post-execution data verification block
            Assert.AreEqual(taskCount * allocationsPerTask, allocatedHandles.Count, "Total completed allocation counts do not match configured expectations.");

            foreach (var handle in allocatedHandles)
            {
                // Every resolved int value should remain accessible and valid
                int storedVal = arena.Get<int>(handle);
                Assert.IsTrue(storedVal >= 5, "Unsynchronized background data sweeps corrupted active memory data footprints.");
            }
        }

        #endregion

        #region --- SECTION 5: SAFETY BOUNDARIES & EXPIRED TOKENS ---

        [TestMethod]
        public void Security_InvalidHandleIdentifiers_ThrowException()
        {
            var arena = new MemoryArena(_config);
            var invalidHandle = new MemoryHandle(0, 1, arena.FastLane); // 0 token boundary rule

            Assert.ThrowsException<InvalidOperationException>(() => arena.Resolve(invalidHandle));
            Assert.ThrowsException<InvalidOperationException>(() => arena.Free(invalidHandle));
            Assert.ThrowsException<InvalidOperationException>(() => arena.GetEntry(invalidHandle));
        }

        [TestMethod]
        public void Security_TamperedVersionHandle_ThrowsAccessViolationException()
        {
            var arena = new MemoryArena(_config);
            var trueHandle = arena.Store(999);

            // Construct a fake handle targeting the exact same ID slot tracking address, 
            // but inject a spoofed generational token sequence signature.
            var forgedHandle = new MemoryHandle(trueHandle.Id, (uint)(trueHandle.Version + 5), arena.FastLane);

            Assert.ThrowsException<AccessViolationException>(() =>
            {
                arena.Get<int>(forgedHandle);
            }, "Submitting expired, stale, or tampered handles must trigger access validation crashes.");
        }

        #endregion

        #region --- HELPER STRUCTURES ---

        private struct SampleMetric
        {
            public long IdField;
            public float Velocity;
        }

        #endregion
    }
}