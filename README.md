
---

### 🔧 Tier Responsibilities

#### ✅ FastLane
- Short-lived, high-speed allocations.
- Backed by `BlockMemoryManager`.
- Positive allocation IDs.
- Promotes stale/oversized entries to `SlowLane` via `OneWayLane`.

#### ✅ SlowLane
- Long-lived or oversized allocations.
- Negative allocation IDs.
- Internally split between:  
  - `BlockMgr` for mid-sized data.  
  - `BlobMgr` for large/unpredictable blobs.  
- Dynamic repartitioning between block/blob regions.

#### ✅ OneWayLane
- Migrates memory from `FastLane` to `SlowLane`.
- Triggered by allocation failure, compaction, or manual flush.
- Uses stub-based redirection to maintain handle validity.

---

### 🧠 Planned Architecture Enhancements

- Adaptive block/blob partitioning in `SlowLane`.
- Freelist and tombstone support for `BlobMgr`.
- Thread-aware or asynchronous migration.
- Telemetry for usage and pressure monitoring.

---

## 🧩 Example Usage

```csharp
var config = new MemoryManagerConfig
{
    FastLaneSize = 1024 * 1024,       // 1 MB
    SlowLaneSize = 10 * 1024 * 1024,  // 10 MB
    Threshold = 4096,                 // Switch threshold between lanes
    EnableAutoCompaction = true,
    CompactionThreshold = 0.90,
    SlowLaneUsageThreshold = 0.85,
    SlowLaneSafetyMargin = 0.10,
    PolicyCheckInterval = TimeSpan.FromSeconds(10)
};

var arena = new MemoryArena(config);

// --- Raw MemoryArena usage (more control) ---
var size = Marshal.SizeOf<MyStruct>();
var handleRaw = arena.Allocate(size);
ref var dataRaw = ref arena.Get<MyStruct>(handleRaw);
dataRaw.Value = 123;
Console.WriteLine($"Raw arena value: {dataRaw.Value}");
arena.Free(handleRaw);

// --- TypedMemoryArena usage (simplified) ---
var typedArena = new TypedMemoryArena(arena);
var handleTyped = typedArena.Allocate<MyStruct>();
typedArena.Set(handleTyped, new MyStruct { Value = 456, PositionX = 1.1f, PositionY = 2.2f });
ref var dataTyped = ref typedArena.Get<MyStruct>(handleTyped);
Console.WriteLine($"Typed arena value: {dataTyped.Value}");
typedArena.Free(handleTyped);

// Optionally run manual compaction
arena.RunMaintenanceCycle();
