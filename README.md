# MemoryLane

**MemoryLane** is a prototype memory allocation and handle system built to experiment with custom memory management in C#. Inspired by techniques found in game engines and real-time systems, it explores handle indirection, dual-tier memory lanes, and optional compaction.


> ⚠️ **Note:** This is a **prototype for learning and experimentation**. Use at your own risk.
Validated via high-stress memory pressure tests and performance benchmarks against the .NET Garbage Collector.

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
  - Uses `Span<T>` and `Buffer.MemoryCopy` to avoid breaking references.  
  - Can be plugged into compaction or run manually.

- 🔤 Unmanaged String Support 
   — Store strings as UTF-8 byte arrays to save 50% memory over C# UTF-16 strings and bypass the GC.

- ✅ **Robust Unit Tests**  
  — Includes structural validation, performance benchmarks, and multi-lane stress testing.

---

## 🎯 Learning Objectives

This project was created to:

- Understand manual memory management strategies.
- Explore handle indirection and pointer safety.
- Learn arena-style memory management, paging, and pooling.
- Gain insights into low-level system design using C#.

---

## ⚙️ Requirements

- **.NET 9.0** (or compatible runtime)

---

## 🧭 Future Work / Ideas

These are conceptual features or areas for future exploration:

[ ] Thread Safety — Replace global lock with ReaderWriterLockSlim for parallel Resolve operations.
[ ] Handle ID Pooling — Implement a stack-based pool for MemoryHandle IDs to ensure $O(1)$ allocation without metadata overhead.
[ ] SIMD Alignment — Add AlignTo(int boundary) to the allocation logic for cache-line and SIMD-friendly memory offsets.
[ ] Visual Profiler — Export internal memory maps to a heatmap (JSON/HTML) for real-time fragmentation monitoring.
[ ] Bidirectional Transfer — (Advanced) Allow the Janitor to "pull" frequently accessed data back into the `FastLane`.

---

## 📐 Planned Architecture Overview

MemoryLane utilizes a dual-tier strategy to bypass the .NET Garbage Collector for high-frequency or large-scale unmanaged data. By using Handle Indirection, the system can physically move memory (defragmentation) without breaking user-held references.

### 🔧 Tier Responsibilities

#### ✅ FastLane
Optimized for: High-frequency, short-lived "hot" data.

Backend: High-speed Arena with a zero-allocation Free-List.

Convention: Uses Positive Allocation IDs.

The Janitor: Automatically promotes stale, oversized, or "Cold-tagged" entries to the SlowLane during maintenance cycles to keep the FastLane lean.

#### ✅ SlowLane
Optimized for: Large, persistent "cold" data or background assets.

Backend: Large-scale unmanaged buffer with defragmentation support.

Convention: Uses Negative Allocation IDs.

Maintenance: Performs deep compaction when fragmentation exceeds configured thresholds.

#### ✅ OneWayLane (The Bridge)
Responsibility: Seamlessly migrates memory from FastLane to SlowLane.

Mechanism: Direct pointer-to-pointer Buffer.MemoryCopy.

Stub System: Replaces the FastLane entry with a Stub that redirects all future Resolve calls to the new SlowLane address.

---

## 🧩 Example Usage

```csharp
// Setup Configuration
var config = new MemoryManagerConfig(slowLaneSize: 10 * 1024 * 1024) 
{
    EnableAutoCompaction = true,
    FastLaneUsageThreshold = 0.90,
    MaxFastLaneAgeFrames = 600 // Janitor evicts after 10 seconds at 60fps
};

var arena = new MemoryArena(config);

// --- 1. Syntactic Sugar: Allocation & Storage ---
// Stores an int directly. The handle is stable even if the memory moves!
var intHandle = arena.AllocateAndStore(777);

// --- 2. High-Performance Arrays & Bulk Operations ---
int[] sourceData = { 10, 20, 30, 40, 50 };
var arrayHandle = arena.AllocateArray<int>(sourceData.Length);

// "Slam" managed data into unmanaged memory instantly
arena.BulkSet(arrayHandle, sourceData);

// --- 3. Pointer-Speed Access via Spans ---
// Get a Span for zero-allocation, high-speed iteration
var span = arena.GetSpan<int>(arrayHandle, sourceData.Length);
foreach(ref var val in span) 
{
    val *= 2; // Direct memory manipulation
}

// --- 4. Policy-Driven Maintenance ---
// Increments internal clock and triggers Janitor/Compaction if needed
arena.TickFrame(); 
arena.RunMaintenanceCycle(); 

// --- 5. Manual Cleanup ---
arena.Free(intHandle);
arena.Free(arrayHandle);