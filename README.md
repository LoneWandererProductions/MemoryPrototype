# MemoryPrototype

**MemoryPrototype** is a high-performance, polymorphic unmanaged memory arena and handle framework built to bypass the .NET Garbage Collector for latency-critical runtime environments. Inspired by data-oriented, sharded architectures in modern low-latency game engines and production-grade runtimes, it implements stable handle indirection, generational version validation, zero-allocation custom structures, and drop-in memory lane strategies for both single-threaded and concurrent parallel execution profiles.

> ⚠️ **Engineering Status:** This is a polished **systems-engineering sandbox and architecture laboratory**. While fully verified by automated parallel chaos test suites to achieve exceptional throughput and zero GC allocation on hot paths, it is meant for specialized standalone subsystems. See the [Production Finish-Line Roadmap](#-the-production-finish-line-roadmap) for remaining considerations.

---

### 🚀 Key Features

1. **Stable Handle Indirection (Relocatable Memory)**
   Instead of handing out raw native pointers that permanently pin layout blocks in memory, the system yields opaque, lightweight $O(1)$ `MemoryHandle` tokens. This layer of indirection permits the background lanes to safely relocate data during defragmentation sweeps without ever breaking or invalidating external user references.

2. **Generational Version Validation (Zombie Protection)**
   To completely eliminate the dangling pointer and double-free vulnerabilities common in unmanaged environments, each `MemoryHandle` carries an index `Id` and an atomic generation `Version`. When handles are recycled, their internal slots increment their generation counts. Legacy handles attempting to access stale memory addresses instantly trigger safe access exceptions instead of silently corrupting recycled data blocks.

3. **Sharded Thread Isolation (`ConcurrentMemoryArena`)**
   Achieves true scaling across high-core CPUs by providing an isolated, lock-free allocation path for every independent worker thread via `ThreadLocal<IFastLane>` storage. Workers allocate and recycle space at raw hardware speeds without fighting over a global execution lock. Cross-thread deallocations (Thread B freeing Thread A's memory) are offloaded asynchronously via atomic lock-free concurrent queues.

4. **Dynamic Segregated Size Classes (`AllocatorStrategy.Slab`)**
   Introduces an ultra-fast, zero-compaction fast-lane implementation. Slabs dynamically generate power-of-two size tracks (e.g., 16B, 32B, 64B... up to the configured boundary) matching your hardware layout. Memory allocation and deallocation within a slab are reduced to instantaneous $O(1)$ LIFO index array stack operations—completely eliminating external fragmentation.

5. **The "Janitor" (Automated Tiered Eviction)**
   Data lifespans are managed policy-heuristically. Hot data lives in your choice of fast-lane architecture. If an allocation outstays its welcome (frame age limits), grows past entry thresholds, or is explicitly initialized via `AllocationHints.Cold`, the automated Janitor moves it downstream to a bulk `SlowLane` during maintenance frames, instantly deploying a seamless routing redirection stub in its wake.

6. **Unified Polymorphic Framework (`IMemoryAllocator`)**
   Both `MemoryArena` (optimized for single-threaded isolated pipelines) and `ConcurrentMemoryArena` (built for massive multi-threaded scaling) implement the `IMemoryAllocator` interface contract. This allows all syntactic sugar, array helpers, type initializers, and string engines to be shared seamlessly across both execution environments.

---

## 📐 Architecture Overview

The system enforces a strict hierarchical topology over specialized unmanaged memory tiers.

* **⚡ FastLane**: Optimized for high-frequency, short-lived "hot" transient structures. Uses sequential **positive integers** (`1, 2, 3...`) for allocation IDs. Version tracking is backed by an ultra-fast, growable native pointer array (`uint*`) that maps handle IDs directly to generation counts via an $O(1)$ array offset calculation.
* **🐌 SlowLane**: Optimized for large, persistent "cold" allocations or long-lived structural blocks. Uses sequential **negative integers** (`-1, -2, -3...`) for allocation IDs. Maps negative handles cleanly to positive array coordinates using an absolute value index converter (`-id`), retaining raw pointer lookup performance.
* **🪐 BlobManager (Slow-Lane Sub-Allocator)**: Optimized for isolating tiny, chaotic allocations (e.g., $\le$ 256 bytes) away from the primary free block table to prevent memory fragmentation.
* **走 OneWayLane (The Bridge)**: Orchestrates seamless data migration. It safely reads the original entry metadata to pull telemetry (preserving original frame age, priorities, and usage hints) and executes a vector-speed `System.Buffer.MemoryCopy` across the lane boundaries before converting the old site into a routing redirection stub.
* **🧱 SlabLane (Segregated Bins)**: An alternative drop-in fast-lane strategy optimized for ultra-fast, uniform object pooling (e.g., ECS components, entities). It partitions unmanaged memory into distinct power-of-two size classes (16B, 32B, 64B, 128B, etc.). Both allocations and frees execute as instant $O(1)$ LIFO slot recycling operations, completely immunizing the hot path against external fragmentation and bypassing compaction stutters entirely.
---

## 🛑 The Production Finish-Line Roadmap

To move this system out of the prototyping phase and into a hardened, production-grade systems engine, the following structural limitations must be addressed:

### 1. "Stop-the-World" Compaction Latency
* **Current State:** When compaction triggers, the compactor engine allocates an entirely new unmanaged heap block via `Marshal.AllocHGlobal` and slides all survivors over via memory block copying. For massive heaps (e.g., 512MB+), this causes noticeable block stutters (latency spikes) and temporarily forces a **double memory footprint**.
* **Production Fix:** Implement an **Incremental/Phased Compactor** that only relocates a small chunk of fragmented blocks per frame tick, or enforce strict zero-compaction rules where arenas are completely cleared and cycled out at deterministic boundaries (e.g., scene changes).

### 2. Add bool IsFragmented() to trigger Compact() on threshold
* ** Current State:** The compactor is only triggered when the arena is completely full. This allows fragmentation to grow unchecked until the last possible moment, causing more severe stutters and higher memory usage.
* **Production Fix:** Introduce an `IsFragmented()` method that evaluates the current fragmentation level against a predefined threshold. If the fragmentation exceeds the threshold, trigger the `Compact()` method proactively to maintain optimal memory usage and reduce latency spikes.

- 
---

## 🧩 Config Presets & Advanced Usage

Configurations can be customized manually or generated via optimized static factories targeted at distinct architecture types:

```csharp
using MemoryManager;
using MemoryManager.Core;

// --- PROFILE 1: Ultra-Fast Real-Time Game Loop (Single Threaded Pipeline) ---
// Employs a blazing fast O(1) Linear Bump allocator for transient per-frame arrays.
var gameConfig = MemoryManagerConfig.CreateForGameLoop(totalBudget: 32 * 1024 * 1024);
var physicsArena = new MemoryArena(gameConfig);

// --- PROFILE 2: High-Velocity Concurrent Object Pooling (Multi-Threaded Scaling) ---
// Provisions lock-free sharded Slab Lanes across all cores utilizing uniform size class bins.
var poolConfig = MemoryManagerConfig.CreateForObjectPooling(totalBudget: 64 * 1024 * 1024);
var parallelArena = new ConcurrentMemoryArena(poolConfig);

## Basic CRUD Operations & Extension Methods

```csharp
using MemoryManager;
using MemoryManager.Core;

// Syntactic sugar extensions run seamlessly on any IMemoryAllocator instance
IMemoryAllocator allocator = new ConcurrentMemoryArena(MemoryManagerConfig.CreateForObjectPooling());

// 1. Structural Primitive Allocation & Direct Value Writing
var healthHandle = allocator.Store(100);

// 2. High-Performance Fixed-Size Arrays
var arrayHandle = allocator.AllocateArray<int>(count: 5);
int[] sourceData = { 10, 20, 30, 40, 50 };

// Executes atomic, size-validated bitwise memory copy
allocator.BulkSet(arrayHandle, sourceData);

// 3. Ultra-Fast Zero-Allocation Spans (Hot Path Loops)
// Extract the span ONCE outside loops to execute modifications at raw hardware cache speed
Span<int> activeSpan = allocator.GetSpan<int>(arrayHandle, sourceData.Length);
for (int i = 0; i < activeSpan.Length; i++)
{
    activeSpan[i] *= 2; 
}

// 4. Symmetric Unmanaged UTF-8 String Storage (Bypasses GC Allocation 완전히)
var stringHandle = allocator.StoreString("Hello from the native unmanaged Arena!");
string decodedText = allocator.GetString(stringHandle);

// 5. Clean Resource Reclamation
allocator.Free(healthHandle);
allocator.Free(stringHandle);
```