# MemoryPrototype

**MemoryPrototype** is a high-performance, dual-tier unmanaged memory arena and handle system built to bypass the .NET Garbage Collector for performance-critical subsystems. Inspired by data-oriented memory management architecture in low-latency runtime engines, it implements stable handle indirection, generational version validation, automated lifecycle eviction policies, and low-level compacting memory lanes.

> ⚠️ **Engineering Status:** This is an **experimental sandbox prototype for learning and systems engineering**. While optimized to achieve exceptional throughput and zero GC allocation on hot paths, it is currently undergoing architectural hardening. See the [Production Finish-Line Roadmap](#-the-production-finish-line-roadmap) below for critical hurdles to address before running this engine in production environments.

---

### 🚀 Key Features

1. **Stable Handle Indirection (Relocatable Memory)**
   Instead of handing out raw pointers that permanently pin your layout blocks in memory, `MemoryArena` yields opaque, lightweight $O(1)$ `MemoryHandle` tokens. This layer of indirection permits the background engine to safely move your data to defragment the unmanaged heap without ever breaking user references.

2. **Generational Version Validation (Zombie Protection)**
   To defeat the dangling pointer problem common in custom allocators, each `MemoryHandle` carries a tracking `Id` and a generation `Version`. When handles are recycled, their index version increments. Any legacy handle trying to access an old address will instantly trigger a safe access violation instead of silently corrupting recycled memory.

3. **The "Janitor" (Automated Tiered Eviction)**
   You do not need to micro-manage object lifespans. Hot, short-lived data is allocated inside a lean, high-speed `FastLane`. If an allocation outstays its welcome (based on frame age), grows too large, or is explicitly tagged with `AllocationHints.Cold`, the automated Janitor moves it to a persistent `SlowLane` during quiet frames, replacing the original site with a seamless redirection stub.

4. **Pluggable Fast-Lane Strategies**
   Not all workloads share the same structural tempo. The arena config file lets you hot-swap the allocation engine backing the `FastLane`:
   * **`AllocatorStrategy.FreeList`**: Tracks fragmented allocations with a block recycling manager; ideal for chaotic, highly variable lifespans.
   * **`AllocatorStrategy.Linear`**: A blazing-fast, true $O(1)$ sequential bump allocator; ideal for transient per-frame scratch pads.

5. **Atomic Safe Copying**
   Features a native, thread-safe `BulkSet<T>` utility that safely accepts `ReadOnlySpan<T>` payloads, guaranteeing that block data transfers, size validation, and pointer resolution occur in a single atomic phase safely isolated from compaction interrupts.

---

## 📐 Architecture Overview

MemoryPrototype establishes a strict separation of concerns across multiple specialized unmanaged memory tiers.

* **⚡ FastLane**: Optimized for high-frequency, short-lived "hot" transient structures. Uses sequential **positive integers** (`1, 2, 3...`) for allocation IDs. Version tracking is backed by an ultra-fast, growable native pointer array (`uint*`) that maps handle IDs directly to generation counts via an $O(1)$ array offset calculation.
* **🐌 SlowLane**: Optimized for large, persistent "cold" allocations or long-lived structural blocks. Uses sequential **negative integers** (`-1, -2, -3...`) for allocation IDs. Maps negative handles cleanly to positive array coordinates using an absolute value index converter (`-id`), retaining raw pointer lookup performance.
* **🪐 BlobManager (Slow-Lane Sub-Allocator)**: Optimized for isolating tiny, chaotic allocations (e.g., $\le$ 256 bytes) away from the primary free block table to prevent memory fragmentation.
* **走 OneWayLane (The Bridge)**: Orchestrates seamless data migration. It safely reads the original entry metadata to pull telemetry (preserving original frame age, priorities, and usage hints) and executes a vector-speed `System.Buffer.MemoryCopy` across the lane boundaries before converting the old site into a routing redirection stub.

---

## 🛑 The Production Finish-Line Roadmap

To move this system out of the prototyping phase and into a hardened, production-grade systems engine, the following structural limitations must be addressed:

### 1. Multi-Threaded Cache-Line Contention (The Global Lock Bottleneck)
* **Current State:** The system achieves thread safety by acquiring a heavy global `lock (_lock)` on the wrapper for nearly every execution entry point (`Allocate`, `Resolve`, `Free`, `BulkSet`). Under high thread contention, parallel worker threads will stall waiting for this single lock, destroying core scaling.
* **Production Fix:** Transition to a **Thread-Local Arena** architecture. Each thread should allocate out of its own dedicated lock-free local scratch ring, only accessing the synchronized global arena when local pages are completely exhausted.

### 2. "Stop-the-World" Compaction Latency
* **Current State:** When compaction triggers, the compactor engine allocates an entirely new unmanaged heap block via `Marshal.AllocHGlobal` and slides all survivors over via memory block copying. For massive heaps (e.g., 512MB+), this causes noticeable block stutters (latency spikes) and temporarily forces a **double memory footprint**.
* **Production Fix:** Implement an **Incremental/Phased Compactor** that only relocates a small chunk of fragmented blocks per frame tick, or enforce strict zero-compaction rules where arenas are completely cleared and cycled out at deterministic boundaries (e.g., scene changes).

### 3. Hardware Memory Alignment Blindness
* **Current State:** The bump allocators currently increment layouts directly by byte size (`_nextFreeOffset += size`). If a user allocates an odd structural layout (e.g., a 7-byte struct), subsequent items are placed on unaligned memory addresses, causing the CPU to fetch multiple cache-lines for single read instructions, tanking cache line efficiency.
* **Production Fix:** Force all lane offsets to snap cleanly to hardware alignment boundaries (e.g., 16-byte, 32-byte, or 64-byte boundaries for SIMD alignment) using power-of-two bitwise masking layout constraints:
  ```csharp
  alignedOffset = (offset + (alignment - 1)) & ~(alignment - 1);

### 4. Zero Defensive Buffer-Overrun Safety Guardrails
* **Current State:** Resolving a handle exposes a raw native `nint` address space. If code reads or writes outside the structural bounds of that specific entry allocation, it will silently overwrite adjacent allocations or metadata arrays without an error, resulting in impossible-to-trace heap corruption.
* **Production Fix:** In `#if DEBUG` configurations, implement **Canary Guard Bands**. Inject a known sequence of invariant diagnostic bytes (e.g., `0xDEADBEEF`) directly before and after every physical allocation chunk. During `Free()` or compaction loops, validate these bands to instantly catch memory overruns.

---

## 🧩 Config Presets & Advanced Usage

MemoryPrototype includes optimized configuration presets out of the box to instantly tune execution properties for specific workloads:

```csharp
using MemoryManager;
using MemoryManager.Core;

// --- PROFILE 1: Ultra-Fast Real-Time Game Loop ---
// Initializes a high-frequency layout budget utilizing the 
// ultra-fast O(1) Linear Bump strategy with aggressive frame aging.
var gameConfig = MemoryManagerConfig.CreateForGameLoop(totalBudget: 32 * 1024 * 1024);
var gameArena = new MemoryArena(gameConfig);

// --- PROFILE 2: Heavy I/O Stream / Asset Chunking ---
// Instantiates a robust Free-List strategy engine capable of 
// managing out-of-order data transfers with larger block thresholds.
var streamConfig = MemoryManagerConfig.CreateForBulkProcessing(totalBudget: 128 * 1024 * 1024);
var streamArena = new MemoryArena(streamConfig);
```

## Basic CRUD Operations & Extension Methods

```csharp
using MemoryManager;
using MemoryManager.Core;

var arena = new MemoryArena(new MemoryManagerConfig(8 * 1024 * 1024));

// 1. Core Allocation & Value Storage
var healthHandle = arena.AllocateAndStore(100);

// 2. Multi-Element Unmanaged Arrays
int[] sourceData = { 10, 20, 30, 40, 50 };
var arrayHandle = arena.AllocateArray<int>(sourceData.Length);

// Atomic, thread-safe bulk block transfer
arena.BulkSet(arrayHandle, sourceData);

// 3. High-Speed Zero-Allocation Spans (Hot Paths)
var span = arena.GetSpan<int>(arrayHandle, sourceData.Length);
foreach (ref var value in span)
{
    value *= 2; // Direct address modifications at hardware cache line speeds
}

// 4. Compact UTF-8 String Storage (Bypasses GC & 50% RAM reduction)
var stringHandle = arena.StoreString("Hello from the native unmanaged Arena!");

// 5. Automated Janitor Lifecycle Advancement (Once Per Tick/Frame)
arena.TickFrame();
arena.RunMaintenanceCycle();

// 6. Explicit Deallocation
arena.Free(healthHandle);
arena.Free(arrayHandle);
```