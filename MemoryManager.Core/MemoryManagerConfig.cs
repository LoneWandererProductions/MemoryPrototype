/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     MemoryArenaPrototype.Core
 * FILE:        MemoryManagerConfig.cs
 * PURPOSE:     Your file purpose here
 * PROGRAMMER:  Peter Geinitz (Wayfarer)
 */

namespace MemoryManager.Core
{
    /// <summary>
    ///     The config for the mm
    /// </summary>
    public sealed class MemoryManagerConfig
    {
        // Add more knobs here as you identify other tuning parameters

        /// <summary>
        ///     Parameter-less constructor with defaults
        /// </summary>
        public MemoryManagerConfig()
        {
        }

        /// <summary>
        ///     Constructs config based on a given slow lane size.
        ///     Other parameters are set with "rule of thumb" proportions.
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
        }

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

        // Whether automatic compaction is enabled
        // Useful to turn off during profiling or debugging
        public bool EnableAutoCompaction { get; init; } = true;

        // Generic threshold for some operation (adjust as needed)
        // Example: 256KB, might represent chunk size or fragmentation tolerance
        public int Threshold { get; init; } = 1024 * 1024 / 4; //256 KB

        // Usage fraction of fast lane to trigger compaction (e.g., 90%)
        // Higher means less frequent compactions but higher risk of fragmentation
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
        public int FastLaneLargeEntryThreshold { get; set; } = 4096;

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
        ///     Estimates the total reserved unmanaged memory (in bytes) this configuration will request,
        ///     not including minor overhead for handles and management structures.
        /// </summary>
        public double GetEstimatedReservedMegabytes()
        {
            // BufferSize is gone! Just the raw unmanaged lane allocations.
            return (FastLaneSize + SlowLaneSize) / (1024.0 * 1024.0);
        }
    }
}