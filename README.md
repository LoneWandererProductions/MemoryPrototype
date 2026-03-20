# MemoryPrototype

**MemoryPrototype** is a prototype memory allocation and handle system built to experiment with custom memory management in C#. Inspired by techniques found in game engines and real-time systems, it explores handle indirection, dual-tier memory lanes, and optional compaction.

> ⚠️ **Note:** This is a **prototype for learning and experimentation**. Use at your own risk.
> Validated via high-stress memory pressure tests and performance benchmarks against the .NET Garbage Collector.

---

### 🚀 The Killer Features

1. **Stable Handle Indirection (Relocatable Memory)**
   Instead of handing you a raw pointer that permanently pins memory, MemoryLane gives you an O(1) `MemoryHandle`. This means the engine can physically move your data in the background to defragment the heap, and your references will never break.
2. **The "Janitor" (Automated Lifecycle Management)**
   You don't have to decide where data lives. Hot, short-lived data stays in the zero-allocation `FastLane`. If data survives too long or goes "Cold," the automated Janitor seamlessly migrates it to the persistent `SlowLane`, leaving a lightweight redirection stub behind.
3. **Live Compaction (Zero External Fragmentation)**
   Standard freelist allocators suffer from "Swiss Cheese" memory over time, leaving gaps you can't use for large objects. MemoryLane features Live Compaction, physically sliding memory blocks together to eliminate gaps and reclaim 100% of your wasted space without pausing the application.
4. **Pluggable Allocation Strategies**
   Not all workloads are the same. MemoryLane allows you to hot-swap the internal allocation engine of the `FastLane` via a simple config toggle. Choose between a safe, hole-reusing **Free-List Allocator** or a lightning-fast, O(1) **Linear Bump Allocator**.

---

## ⚠️ Known Limitations

- Does not manage C# objects directly — only unmanaged memory blocks.
- No garbage collection integration.
- No automatic bounds checking — `MemoryHandle` is mostly safe *if* used correctly via the API.
- Designed for low-level experimentation, not high-level safety.

---

## ✨ Features

- 🧠 **Dual Memory Lanes** - `FastLane`: low-latency, short-lived allocations (e.g., frame-local).  
  - `SlowLane`: large, persistent allocations (e.g., background assets).

- 🔁 **Stable Handle System** - `MemoryHandle` provides opaque, safe references to internal memory entries.  
  - Redirection via lightweight stubs for safe relocation.

- 🔌 **Modular Strategy Pattern** - Toggle the core allocation logic (`AllocatorStrategy.FreeList` vs `AllocatorStrategy.LinearBump`) to perfectly match your application's allocation tempo.

- 🧹 **Optional Live Compaction** - Reduces fragmentation and reclaims space dynamically.

- 🔄 **Stub-Based Indirection** - Moves memory without invalidating existing handles.

- 🧪 **Safety Checks** - Includes `TryGet<T>`, `IsValid`, and `GetHandleState`.

- 🛤️ **One-Way Lane Transfer** - `OneWayLane` shifts data from `FastLane` to `SlowLane`.  
  - Uses `Span<T>` and `Buffer.MemoryCopy` to avoid breaking references.  
  - Can be plugged into compaction or run manually.

- 📦 High-Level Managed Types - Includes ArenaList<T>, a resizable collection that lives entirely in unmanaged memory.
  - Automatically handles Growth and Migration through the handle system.
  - Integrates with the Janitor for automatic defragmentation when lists are resized or freed.
 
- 🔤 **Unmanaged String Support** - Store strings as UTF-8 byte arrays to save 50% memory over C# UTF-16 strings and bypass the GC.

- ✅ **Robust Unit Tests** - Includes structural validation, performance benchmarks, and multi-lane stress testing.

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

- [ ] **Thread Safety** — Replace global lock with `ReaderWriterLockSlim` for parallel Resolve operations.
- [ ] **Handle ID Pooling** — Implement a stack-based pool for `MemoryHandle` IDs to ensure O(1) allocation without metadata overhead.
- [ ] **SIMD Alignment** — Add `AlignTo(int boundary)` to the allocation logic for cache-line and SIMD-friendly memory offsets.
- [ ] **Visual Profiler** — Export internal memory maps to a heatmap (JSON/HTML) for real-time fragmentation monitoring.
- [ ] **Bidirectional Transfer** — (Advanced) Allow the Janitor to "pull" frequently accessed data back into the `FastLane`.
- [ ] **The "Lounge" (Pool Allocator)** — A dedicated memory tier for uniform, homogeneous data blocks (e.g., arrays of identical structs). Eliminates fragmentation entirely using an O(1) index stack. Includes a strict Janitor policy to evict "liars" to the `SlowLane` if their temporary frame data overstays its welcome.

---

## 📐 Planned Architecture Overview

MemoryLane utilizes a dual-tier strategy to bypass the .NET Garbage Collector for high-frequency or large-scale unmanaged data. By using Handle Indirection, the system can physically move memory (defragmentation) without breaking user-held references.

### 🔧 Tier Responsibilities

#### ✅ FastLane
**Optimized for:** High-frequency, short-lived "hot" data.  
**Backend:** Pluggable Strategy (`LinearBump` for maximum O(1) speed, or `FreeList` for highly-variable, chaotic lifespans).  
**Convention:** Uses Positive Allocation IDs.  
**The Janitor:** Automatically promotes stale, oversized, or "Cold-tagged" entries to the SlowLane during maintenance cycles to keep the FastLane lean.

#### ✅ SlowLane
**Optimized for:** Large, persistent "cold" data or background assets.  
**Backend:** Hybrid Segregated Allocator (Free-Block Manager for large assets, Bump Allocator for tiny blobs).  
**Convention:** Uses Negative Allocation IDs.  
**Maintenance:** Performs deep compaction when fragmentation exceeds configured thresholds.

#### ✅ OneWayLane (The Bridge)
**Responsibility:** Seamlessly migrates memory from FastLane to SlowLane.  
**Mechanism:** Direct pointer-to-pointer `Buffer.MemoryCopy`.  
**Stub System:** Replaces the FastLane entry with a Stub that redirects all future Resolve calls to the new SlowLane address.

---

## 🧩 Example Usage

```csharp
using MemoryManager;
using MemoryManager.Types;

// --- 1. Setup Configuration ---
// Define your sandbox boundaries. 10MB SlowLane, 1MB FastLane.
var config = new MemoryManagerConfig(slowLaneSize: 10 * 1024 * 1024) 
{
    EnableAutoCompaction = true,
    FastLaneUsageThreshold = 0.90, // Trigger maintenance at 90% full
    MaxFastLaneAgeFrames = 600,    // Janitor evicts "stale" data after 10 seconds (60fps)
    FastLaneStrategy = AllocatorStrategy.LinearBump // O(1) lightning speed
};

var arena = new MemoryArena(config);

// --- 2. High-Level Collections (The Lounge) ---
// Use ArenaList for resizable, unmanaged collections.
// It feels like a standard List<T>, but lives in your FastLane.
var list = new ArenaList<int>(arena, initialCapacity: 16);
for(int i = 0; i < 20; i++) list.Add(i); 

// --- 3. Stable Handles & Syntactic Sugar ---
// Store a single value. The handle remains valid even if the Janitor 
// moves this integer to the SlowLane later!
var healthHandle = arena.AllocateAndStore(100);

// --- 4. Bulk Data & Memory "Slamming" ---
int[] sourceData = { 10, 20, 30, 40, 50 };
var arrayHandle = arena.AllocateArray<int>(sourceData.Length);

// Vectorized copy from managed to unmanaged memory
arena.BulkSet(arrayHandle, sourceData);

// --- 5. Unmanaged Strings (Save 50% RAM) ---
// Store text as UTF-8 in the Arena, bypassing C# UTF-16 overhead and the GC.
var stringHandle = arena.AllocateString("Hello from the Arena!");

// --- 6. Pointer-Speed Access via Spans ---
// Get a Span for zero-allocation, high-speed iteration.
// This is the "hot path" for game loops.
var span = arena.GetSpan<int>(arrayHandle, sourceData.Length);
foreach(ref var val in span) 
{
    val *= 2; // Direct memory manipulation at CPU cache speeds
}

// --- 7. Policy-Driven Maintenance ---
// Call this once per frame. It tracks the age of allocations.
arena.TickFrame(); 

// Periodically runs the Janitor (eviction) and Compaction (defragmentation)
// to ensure your FastLane never turns into "Swiss Cheese."
arena.RunMaintenanceCycle(); 

// --- 8. Manual Cleanup ---
arena.Free(healthHandle);
arena.Free(arrayHandle);
// Note: ArenaList and String handles are cleaned up similarly.