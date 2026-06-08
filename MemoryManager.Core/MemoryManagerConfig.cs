/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     MemoryArenaPrototype.Core
 * FILE:        MemoryManagerConfig.cs
 * PURPOSE:     Configuration class for the MemoryManager, encapsulating all tunable parameters and thresholds for memory management policies.
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

namespace MemoryManager.Core
{
    /// <summary>
    ///      The config for the mm
    /// </summary>
    public sealed class MemoryManagerConfig
    {
        /// <summary>
        ///      Parameter-less constructor with defaults
        /// </summary>
        public MemoryManagerConfig()
        {
        }

        /// <summary>
        ///      Constructs config based on a given slow lane size.
        ///      Other parameters are set with "rule of thumb" proportions.
        /// </summary>
        /// <param name="slowLaneSize">Total size of the slow lane (bytes)</param>
        public MemoryManagerConfig(int slowLaneSize)
        {
            SlowLaneSize = slowLaneSize;

            // Rule of thumb: Fast lane size is ~1/8 of slow lane size, but capped to a max (e.g. 16MB)
            FastLaneSize = Math.Min(slowLaneSize / 8, 16 * 1024 * 1024);

            FastLaneSafetyMargin = 0.10;
            CompactionThreshold = 0.80;
            PolicyCheckInterval = TimeSpan.FromSeconds(1);
            EnableAutoCompaction = true;

            // Threshold for generic operations set as 1/4 of fast lane size
            Threshold = FastLaneSize / 4;

            FastLaneUsageThreshold = 0.9;
            FastLaneLargeEntryThreshold = Math.Min(4096, FastLaneSize / 256);

            SlowLaneUsageThreshold = 0.85;
            SlowLaneSafetyMargin = 0.10;

            // Default hybrid tuning
            SlowLaneBlobCapacityFraction = 0.20;
            SlowLaneBlobThreshold = 256;

            // Default performance-tuned layout choices
            FastLaneFreeListStrategy = AllocationStrategy.FirstFit;
            SlowLaneFreeListStrategy = AllocationStrategy.BestFit;
        }

        /// <summary>
        /// Gets the free-list block search strategy used by the FastLane when running the FreeList strategy wrapper.
        /// </summary>
        public AllocationStrategy FastLaneFreeListStrategy { get; init; } = AllocationStrategy.FirstFit;

        /// <summary>
        /// Gets the free-list block search strategy used by the SlowLane to manage gap allocations.
        /// </summary>
        public AllocationStrategy SlowLaneFreeListStrategy { get; init; } = AllocationStrategy.BestFit;

        /// <summary>
        /// Gets the fast lane strategy.
        /// Your pick old FastLane or the more speedy BumbLane.
        /// </summary>
        /// <value>
        /// The fast lane strategy.
        /// </value>
        public AllocatorStrategy FastLaneStrategy { get; init; } = AllocatorStrategy.FreeList;

        /// <summary>
        /// How many frames an allocation can sit in the FastLane before being evicted
        /// Gets the maximum fast lane age frames.
        /// </summary>
        /// <value>
        /// The maximum fast lane age frames.
        /// </value>
        public int MaxFastLaneAgeFrames { get; init; } = 600;

        /// <summary>
        /// Gets the size of the fast lane.
        /// Size of the fast memory lane (high-speed, limited capacity)
        /// Rule of thumb: balance between fast allocations and available RAM,
        /// 1MB is small enough for fast cache-friendly allocations.
        /// </summary>
        /// <value>
        /// The size of the fast lane.
        /// </value>
        public int FastLaneSize { get; init; } = 1024 * 1024; // 1MB

        /// <summary>
        /// Gets the size of the slow lane.
        /// Size of the slow memory lane (larger but slower memory pool)
        /// Typically several times larger than fast lane for bulk storage
        /// </summary>
        /// <value>
        /// The size of the slow lane.
        /// </value>
        public int SlowLaneSize { get; init; } = 8 * 1024 * 1024; // 8MB

        /// <summary>
        /// Gets or sets the fast lane safety margin.
        /// Safety margin before triggering compaction or other maintenance
        /// E.g., 10% means compact before 90% usage to avoid sudden out-of-memory
        /// </summary>
        /// <value>
        /// The fast lane safety margin.
        /// </value>
        public double FastLaneSafetyMargin { get; set; } = 0.10;

        /// <summary>
        /// Gets the compaction threshold.
        /// Fraction of usage at which compaction kicks in
        /// 80% usage triggers compaction to reduce fragmentation
        /// </summary>
        /// <value>
        /// The compaction threshold.
        /// </value>
        public double CompactionThreshold { get; init; } = 0.80;

        /// <summary>
        /// Gets the policy check interval.
        /// How often to check allocation policies like compaction and reclamation
        /// 1 second is a reasonable balance between responsiveness and overhead
        /// </summary>
        /// <value>
        /// The policy check interval.
        /// </value>
        public TimeSpan PolicyCheckInterval { get; init; } = TimeSpan.FromSeconds(1);

        /// <summary>
        /// Gets a value indicating whether [enable automatic compaction].
        /// Useful to turn off during profiling or debugging
        /// </summary>
        /// <value>
        ///   <c>true</c> if [enable automatic compaction]; otherwise, <c>false</c>.
        /// </value>
        public bool EnableAutoCompaction { get; init; } = true;

        /// <summary>
        /// Gets the threshold.
        /// Generic threshold for some operation (adjust as needed)
        /// Example: 256KB, might represent chunk size or fragmentation tolerance
        /// </summary>
        /// <value>
        /// The threshold.
        /// </value>
        public int Threshold { get; init; } = 1024 * 1024 / 4; //256 KB

        /// <summary>
        /// Gets the fast lane usage threshold.
        /// Usage fraction of fast lane to trigger compaction (e.g., 90%)
        /// Higher means less frequent compactions but higher risk of fragmentation
        /// </summary>
        /// <value>
        /// The fast lane usage threshold.
        /// </value>
        public double FastLaneUsageThreshold { get; init; } = 0.9;

        /// <summary>
        /// Gets or sets the fast lane large entry threshold.
        /// Size threshold in bytes for entries considered "large"
        /// Entries larger than this are candidates for moving to slow lane
        /// Rule of thumb: 4KB aligns roughly with typical OS page size and cache line multiples
        /// </summary>
        /// <value>
        /// The fast lane large entry threshold.
        /// </value>
        public int FastLaneLargeEntryThreshold { get; init; } = 4096;

        /// <summary>
        /// Gets the slow lane usage threshold.
        /// Usage fraction of slow lane to trigger compaction (e.g., 85%)
        ///  Should be less aggressive than fast lane compaction to avoid overhead
        /// </summary>
        /// <value>
        /// The slow lane usage threshold.
        /// </value>
        public double SlowLaneUsageThreshold { get; init; } = 0.85;

        /// <summary>
        /// Gets the slow lane safety margin.
        /// Safety margin for slow lane compaction decisions
        /// Similar to fast lane safety margin, but can be tuned independently
        /// </summary>
        /// <value>
        /// The slow lane safety margin.
        /// </value>
        public double SlowLaneSafetyMargin { get; init; } = 0.10;

        /// <summary>
        /// The maximum number of distinct allocations the FastLane can track at once.
        /// </summary>
        public int MaxEntries { get; init; } = 1024;

        /// <summary>
        /// Fraction of the SlowLane capacity dedicated to the BlobManager for small, unpredictable data.
        /// Example: 0.20 reserves 20% of the SlowLane for tiny blobs.
        /// </summary>
        public double SlowLaneBlobCapacityFraction { get; init; } = 0.20;

        /// <summary>
        /// Allocations in the SlowLane smaller than or equal to this size (in bytes)
        /// will be routed to the BlobManager instead of the main BlockManager.
        /// </summary>
        public int SlowLaneBlobThreshold { get; init; } = 256;

        /// <summary>
        ///      Estimates the total reserved unmanaged memory (in bytes) this configuration will request,
        ///      not including minor overhead for handles and management structures.
        /// </summary>
        public double GetEstimatedReservedMegabytes()
        {
            return (FastLaneSize + SlowLaneSize) / (1024.0 * 1024.0);
        }

        /*
         * Preset factory methods for common scenarios. These provide convenient starting points for typical use cases,
         */

        /// <summary>
        /// Creates a configuration tuned for real-time game loops.
        /// Employs an ultra-fast Bump/Linear allocator for short-lived transient frames.
        /// </summary>
        /// <param name="totalBudget">The total budget.</param>
        /// <returns>MemoryManagerConfig instance configured for real-time game loops.</returns>
        public static MemoryManagerConfig CreateForGameLoop(int totalBudget = 16 * 1024 * 1024)
        {
            return new MemoryManagerConfig
            {
                SlowLaneSize = (int)(totalBudget * 0.75),
                FastLaneSize = (int)(totalBudget * 0.25),
                FastLaneStrategy = AllocatorStrategy.LinearBump, // Maximum speed
                MaxFastLaneAgeFrames = 300, // Evict to SlowLane faster
                FastLaneUsageThreshold = 0.85,
                CompactionThreshold = 0.75,
                EnableAutoCompaction = true,
                PolicyCheckInterval = TimeSpan.FromMilliseconds(16), // Check roughly every frame at 60fps

                // Homogeneous loop presets: SlowLane aggregates long-lived chunks neatly via BestFit
                SlowLaneFreeListStrategy = AllocationStrategy.BestFit
            };
        }

        /// <summary>
        /// Creates a configuration tuned for heavy I/O, networking, or asset streaming.
        /// Uses a FreeList allocator to handle random out-of-order allocations.
        /// </summary>
        /// <param name="totalBudget">The total budget.</param>
        /// <returns>MemoryManagerConfig instance configured for bulk processing.</returns>
        public static MemoryManagerConfig CreateForBulkProcessing(int totalBudget = 64 * 1024 * 1024)
        {
            return new MemoryManagerConfig
            {
                SlowLaneSize = (int)(totalBudget * 0.85),
                FastLaneSize = (int)(totalBudget * 0.15),
                FastLaneStrategy = AllocatorStrategy.FreeList, // Out-of-order safety
                FastLaneLargeEntryThreshold = 16384, // Allow up to 16KB in the hot lane
                MaxFastLaneAgeFrames = 1200, // Let data sit longer before moving
                SlowLaneUsageThreshold = 0.80,
                SlowLaneBlobThreshold = 512, // Route larger fragments to blobs
                PolicyCheckInterval = TimeSpan.FromSeconds(2), // Low maintenance overhead

                // Heterogeneous presets: prioritize high allocation velocity on FastLane, anti-fragmentation on SlowLane
                FastLaneFreeListStrategy = AllocationStrategy.FirstFit,
                SlowLaneFreeListStrategy = AllocationStrategy.BestFit
            };
        }

        /// <summary>
        /// Creates a configuration tuned for tightly constrained or embedded footprints.
        /// Minimizes unmanaged allocation limits and compacts aggressively.
        /// </summary>
        /// <returns>MemoryManagerConfig instance configured for low memory scenarios.</returns>
        public static MemoryManagerConfig CreateForLowMemory()
        {
            return new MemoryManagerConfig
            {
                FastLaneSize = 256 * 1024, // 256 KB
                SlowLaneSize = 1024 * 1024, // 1 MB
                FastLaneStrategy = AllocatorStrategy.FreeList,
                EnableAutoCompaction = true,
                CompactionThreshold = 0.60, // Compact very early
                FastLaneUsageThreshold = 0.70,
                SlowLaneUsageThreshold = 0.70,
                SlowLaneBlobCapacityFraction = 0.35, // Allocate more space for tiny fragments
                PolicyCheckInterval = TimeSpan.FromMilliseconds(500),

                // Low-Footprint presets: Use BestFit everywhere to squeeze the max data out of limited space limits
                FastLaneFreeListStrategy = AllocationStrategy.BestFit,
                SlowLaneFreeListStrategy = AllocationStrategy.BestFit
            };
        }

        /// <summary>
        /// NEW PRESET: Creates a configuration tuned for ultra-fast, predictable object pooling.
        /// Employs the zero-compaction, fixed-bin segregated SlabLane strategy.
        /// </summary>
        /// <param name="totalBudget">The total budget.</param>
        /// <returns>MemoryManagerConfig instance configured for object pooling.</returns>
        public static MemoryManagerConfig CreateForObjectPooling(int totalBudget = 32 * 1024 * 1024)
        {
            return new MemoryManagerConfig
            {
                SlowLaneSize = (int)(totalBudget * 0.70),
                FastLaneSize = (int)(totalBudget * 0.30),
                FastLaneStrategy = AllocatorStrategy.Slab, // Deploy the new Slab architecture!
                Threshold = 512, // Bins up to 512 bytes (perfect for game components/entities)
                MaxFastLaneAgeFrames = 2400, // Let objects sit longer in hot bins
                FastLaneLargeEntryThreshold = 512, // Keep the slots compact and matching our max bin size
                EnableAutoCompaction = true,
                PolicyCheckInterval = TimeSpan.FromMilliseconds(500),
                SlowLaneFreeListStrategy =
                    AllocationStrategy.BestFit // SlowLane catches larger spills via clean best-fit
            };
        }
    }
}