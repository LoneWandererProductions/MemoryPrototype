# MemoryLane

**MemoryLane** is a prototype memory allocation and handle system built to experiment with custom memory management in C#. Inspired by techniques found in game engines and real-time systems, it explores handle indirection, dual-tier memory lanes, and optional compaction.

> ⚠️ **Note:** This is a **prototype for learning and experimentation**. It is **not production-ready** and remains **untested for real-world use cases**. Use at your own risk.

---

## ⚠️ Known Limitations

- Does not manage C# objects directly — only unmanaged memory blocks.
- No garbage collection integration.
- No automatic bounds checking — `MemoryHandle` is mostly safe *if* used correctly via the API.
- Designed for low-level experimentation, not high-level safety.

---

## ✨ Features

- 🧠 **Dual Memory Lanes**  
  - `FastLane`: low-latency, short-lived allocations (e.g., frame-local).  
  - `SlowLane`: large, persistent allocations (e.g., background assets).

- 🔁 **Stable Handle System**  
  - `MemoryHandle` provides opaque, safe references to internal memory entries.  
  - Redirection via lightweight stubs for safe relocation.

- 🧹 **Optional Live Compaction**  
  - Reduces fragmentation and reclaims space dynamically.

- 🔄 **Stub-Based Indirection**  
  - Moves memory without invalidating existing handles.

- 🧪 **Safety Checks**  
  - Includes `TryGet<T>`, `IsValid`, and `GetHandleState`.

- 🛤️ **One-Way Lane Transfer**  
  - `OneWayLane` shifts data from `FastLane` to `SlowLane`.  
  - Uses internal buffer and `Marshal.Copy` to avoid breaking references.  
  - Can be plugged into compaction or run manually.

- ✅ **Robust Unit Tests** *(planned)*  
  - Will cover correctness, safety, and performance.

---

## 🎯 Learning Objectives

This project was created to:

- Understand manual memory management strategies.
- Explore handle indirection and pointer safety.
- Learn arena-style memory management, paging, and pooling.
- Gain insights into low-level system design using C#.

---

## ⚙️ Requirements

- **.NET 5.0** (or compatible runtime)

---

## 🧭 Future Work / Ideas

These are conceptual features or areas for future exploration:

- [ ] **Paging Support** – Evict memory to disk or secondary storage.  
- [ ] **SlowLane Compaction Enhancements** – Reserve ~10% as scratch space.  
- [ ] **Improved OneWayLane**  
  - Use shared buffers or memory pools.  
  - Add bidirectional transfer.  
  - Expose migration cost/heuristics to callers.  
- [ ] **Advanced Memory Tracking**  
  - Allocation lifetime, usage frequency, promotion history.  
  - Debug tags for diagnostics.  
- [ ] **Allocation Groups** – Group handles for batch operations or diagnostics.  
- [ ] **Alignment Support** – `AlignTo(int)` for cache-friendly access.  
- [ ] **Failover Policies** – Automatically fall back to `SlowLane` when `FastLane` fails.  
- [ ] **Multithreaded Allocator** – Concurrency support via locks or lock-free front-end.  
- [ ] **Object Lifecycle Management** – Auto-eviction based on access time.  
- [ ] **Dynamic Buffer Growth** – Allow runtime memory expansion.  
- [ ] **Memory Compression** – Compress cold data in `SlowLane`.  
- [ ] **Block-Based Allocation & Reuse**  
  - Uniform block sizes, freelists, and tombstones.  
  - Improves reuse, reduces fragmentation, simplifies compaction.

---

## 📐 Architecture Overview

`MemoryLane` is split into two tiers — **FastLane** and **SlowLane** — optimized for different lifetimes and access patterns. This enables efficient management of both transient and long-lived memory, with built-in support for migration and indirection.

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
