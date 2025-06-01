/*
 * COPYRIGHT:   See COPYING in the top level directory
 * PROJECT:     MemoryManager
 * FILE:        MemoryManagerConfig.cs
 * PURPOSE:     Your file purpose here
 * PROGRAMMER:  Your name here
 */

using System;

namespace MemoryManager
{
    /// <summary>
    /// The config for the mm
    /// </summary>
    public sealed class MemoryManagerConfig
    {
        /// <summary>
        /// Estimates the total reserved unmanaged memory (in bytes) this configuration will request,
        /// not including minor overhead for handles and management structures.
        /// </summary>
        public double GetEstimatedReservedMegabytes()
        {
            return (FastLaneSize + SlowLaneSize + BufferSize) / (1024.0 * 1024.0);
        }

        // Size of the fast memory lane (high-speed, limited capacity)
        // Rule of thumb: balance between fast allocations and available RAM,
        // 1MB is small enough for fast cache-friendly allocations.
        public int FastLaneSize { get; set; } = 1024 * 1024; // 1MB

        // Size of the slow memory lane (larger but slower memory pool)
        // Typically several times larger than fast lane for bulk storage
        public int SlowLaneSize { get; set; } = 8 * 1024 * 1024; // 8MB

        // Safety margin before triggering compaction or other maintenance
        // E.g., 10% means compact before 90% usage to avoid sudden out-of-memory
        public double FastLaneSafetyMargin { get; set; } = 0.10;

        // Fraction of usage at which compaction kicks in
        // 80% usage triggers compaction to reduce fragmentation
        public double CompactionThreshold { get; set; } = 0.80;

        // How often to check allocation policies like compaction and reclamation
        // 1 second is a reasonable balance between responsiveness and overhead
        public TimeSpan PolicyCheckInterval { get; set; } = TimeSpan.FromSeconds(1);

        // Whether automatic compaction is enabled
        // Useful to turn off during profiling or debugging
        public bool EnableAutoCompaction { get; set; } = true;

        // Generic threshold for some operation (adjust as needed)
        // Example: 256KB, might represent chunk size or fragmentation tolerance
        public int Threshold { get; set; } = (1024 * 1024) / 4; //256 KB

        // Usage fraction of fast lane to trigger compaction (e.g., 90%)
        // Higher means less frequent compactions but higher risk of fragmentation
        public double FastLaneUsageThreshold { get; set; } = 0.9;

        // Size threshold in bytes for entries considered "large"
        // Entries larger than this are candidates for moving to slow lane 
        // Rule of thumb: 4KB aligns roughly with typical OS page size and cache line multiples
        public int FastLaneLargeEntryThreshold { get; set; } = 4096;

        // Usage fraction of slow lane to trigger compaction (e.g., 85%)
        // Should be less aggressive than fast lane compaction to avoid overhead
        public double SlowLaneUsageThreshold { get; set; } = 0.85;

        // Safety margin for slow lane compaction decisions
        // Similar to fast lane safety margin, but can be tuned independently
        public double SlowLaneSafetyMargin { get; set; } = 0.10;

        /// <summary>
        /// Gets or sets the size of the buffer.
        /// </summary>
        /// <value>
        /// The size of the buffer.
        /// </value>
        public int BufferSize { get; set; } = (1024 * 1024) / 4; // 256 KB

        // Add more knobs here as you identify other tuning parameters

        /// <summary>
        /// Parameterless constructor with defaults
        /// </summary>
        public MemoryManagerConfig() { }

        /// <summary>
        /// Constructs config based on a given slow lane size.
        /// Other parameters are set with "rule of thumb" proportions.
        /// </summary>
        /// <param name="slowLaneSize">Total size of the slow lane (bytes)</param>
        public MemoryManagerConfig(int slowLaneSize)
        {
            SlowLaneSize = slowLaneSize;

            // Rule of thumb: Fast lane size is ~1/8 of slow lane size, but capped to a max (e.g. 16MB)
            FastLaneSize = Math.Min(slowLaneSize / 8, 16 * 1024 * 1024);

            // Safety margin stays default 10%
            FastLaneSafetyMargin = 0.10;

            // Compaction threshold set to 80% usage of fast lane
            CompactionThreshold = 0.80;

            // Check policy every second (tune as needed)
            PolicyCheckInterval = TimeSpan.FromSeconds(1);

            EnableAutoCompaction = true;

            // Threshold for generic operations set as 1/4 of fast lane size
            Threshold = FastLaneSize / 4;

            // Trigger compaction if usage exceeds 90%
            FastLaneUsageThreshold = 0.9;

            // Large entries threshold set to typical page size or 1/256 of fast lane size, whichever is smaller
            FastLaneLargeEntryThreshold = Math.Min(4096, FastLaneSize / 256);

            // Set defaults for slow lane thresholds
            SlowLaneUsageThreshold = 0.85;
            SlowLaneSafetyMargin = 0.10;

            //set Transfer Buffer
            BufferSize = Math.Max(FastLaneSize / 4, 64 * 1024);
        }
    }
}
