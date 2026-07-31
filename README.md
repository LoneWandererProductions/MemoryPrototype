# ⚡ MemoryManager: High-Performance Unmanaged Arena Allocator for .NET

[![License](https://img.shields.io/badge/License-Apache_2.0-blue.svg)](License.txt)
[![.NET 9.0+](https://img.shields.io/badge/.NET-9.0%2B-purple.svg)](https://dotnet.microsoft.com/)
[![Build Status](https://img.shields.io/badge/Tests-Passing-brightgreen.svg)]()

**MemoryManager** is a low-level, production-grade memory arena architecture engineered for high-throughput, low-latency .NET applications such as game engine cores, real-time telemetry systems, database engines, and networking pipelines.

By managing native heap memory directly through platform allocators, **MemoryManager completely eliminates .NET Garbage Collector (GC) pressure**, guarantees CPU cache-line locality, and provides safe, generational handle tracking with in-place adaptive compaction.

---

## 🏛️ System Architecture

The framework relies on a **Tiered Dual-Lane Memory Architecture** backed by an indirection handle tracking system.

```
                  ┌─────────────────────────────────────────┐
                  │       MemoryArena / ConcurrentArena     │
                  └────────────────────┬────────────────────┘
                                       │
                     ┌─────────────────┴─────────────────┐
                     ▼                                   ▼
          ┌─────────────────────┐             ┌─────────────────────┐
          │      FastLane       │             │      SlowLane       │
          │  (Hot / Short-Lived)│             │  (Cold / Long-Lived)│
          └──────────┬──────────┘             └──────────┬──────────┘
                     │                                   │
      ┌──────────────┼──────────────┐                    ├─────────────────────┐
      ▼              ▼              ▼                    ▼                     ▼
┌───────────┐  ┌───────────┐  ┌───────────┐      ┌───────────────┐     ┌──────────────┐
│ SlabLane  │  │LinearLane │  │ FastLane  │      │  BlobManager  │     │ FreeList     │
│  (Slabs)  │  │  (Bump)   │  │(FreeList) │      │ (Size <=256B) │     │ (Size >256B) │
└───────────┘  └───────────┘  └───────────┘      └───────────────┘     └──────────────┘
```

### Key Architectural Pillars

* **Generational Handles (`MemoryHandle`):** Memory references are abstract versioned handles (`Id` + `Version`). Handles prevent Use-After-Free bugs and allow memory compaction without invalidating caller references.
* **Pluggable Allocation Strategies:** Choose between **Slab Bins**, **Linear Bump**, or **Variable Free-Lists** depending on workload characteristics.
* **Lock-Free Concurrency:** `ConcurrentMemoryArena` shards allocations across thread-local fast lanes with atomic cross-thread deallocation staging queues.
* **In-Place Adaptive Compaction:** Compaction slides active memory blocks *in-place* using binary memory copies without doubling the RAM footprint or forcing full-heap defragmentation stalls.

---

## ⚡ Key Features

* 🚀 **Zero GC Overhead:** Allocations reside entirely on the unmanaged heap (`Marshal.AllocHGlobal` / `NativeMemory`).
* 🔒 **Zombie Handle Protection:** Version-stamped handles instantly detect and throw on access to deallocated memory blocks.
* 🛡️ **Canary Guard Bands:** Debug builds feature pre- and post-guard band validation (`0xDEADBEEF`) to catch buffer underruns and overruns immediately.
* 📦 **Zero-Allocation Collections:** Includes `ArenaList<T>`, `ArenaQueue<T>`, `ArenaBuffer<T>`, and `ArenaStack<T>` equipped with struct `Span<T>.Enumerator` support for zero-allocation `foreach` loops.
* 🎯 **Policy-Based Maintenance:** Background Janitor maintenance automatically promotes aging or cold data out of fast lanes into the slow lane.

---

## 🚀 Getting Started

### 1. Basic Allocation & Typed Storage

```csharp
using MemoryManager.Core;
using MemoryManager.Lanes;

// 1. Initialize configuration
var config = new MemoryManagerConfig
{
    FastLaneSize = 1024 * 1024,     // 1 MB Hot Path
    SlowLaneSize = 4 * 1024 * 1024,   // 4 MB Cold Path
    FastLaneStrategy = AllocatorStrategy.FreeList
};

// 2. Instantiate Arena
using var arena = new MemoryArena(config);

// 3. Store a struct directly in unmanaged memory
var handle = arena.Store(new Vector3 { X = 10.0f, Y = 20.0f, Z = 30.0f });

// 4. Read back the struct
var position = arena.Get<Vector3>(handle);

// 5. Free memory when done
arena.Free(handle);
```

### 2. High-Performance Zero-GC `ArenaList<T>`

```csharp
using MemoryManager.Types;

// Create an unmanaged resizable list backed by the arena allocator
using var list = new ArenaList<int>(arena, initialCapacity: 16);

list.Add(100);
list.Add(200);
list.Add(300);

// Fast, allocation-free iteration using Span<T>.Enumerator
foreach (ref var value in list)
{
    Console.WriteLine(value);
}
```

### 3. Circular Ring Buffer `ArenaQueue<T>`

```csharp
using MemoryManager.Types;

using var queue = new ArenaQueue<Point>(arena, initialCapacity: 32);

queue.Enqueue(new Point { X = 1, Y = 2 });
queue.Enqueue(new Point { X = 3, Y = 4 });

var point = queue.Dequeue();
```

---

## 📐 Allocation Strategies Comparison

| Strategy | Allocation Time | Deallocation Time | Fragmentation | Best Use Case |
| :--- | :---: | :---: | :---: | :--- |
| **`Slab`** | $O(1)$ | $O(1)$ | Zero (Bin-local) | Fixed-size uniform struct pools |
| **`LinearBump`** | $O(1)$ | $O(1)$ (Bulk) | N/A | High-speed per-frame scratch buffers |
| **`FreeList`** | $O(N)$ Best/First-Fit | $O(1)$ Coalescing | Variable | Dynamic, unpredictable payload sizes |

---

## 🧹 Adaptive In-Place Compaction

The `SlowLane` provides two defragmentation policies via `CompactionStyle`:

```csharp
// Full Compaction: Complete defragmentation (Ideal for loading boundaries)
slowLane.Compact(CompactionStyle.Full);

// GoodEnough Compaction: Stops immediately once a gap of 4KB is opened (Ideal for frame-time budget preservation)
slowLane.Compact(CompactionStyle.GoodEnough, requiredSize: 4096);
```

* **In-Place Memory Sliding:** Compaction slides active memory blocks down using `System.Buffer.MemoryCopy` without allocating temporary secondary buffers.
* **Automatic Healing:** If `Allocate()` fails due to fragmented space, `SlowLane` automatically triggers an in-place compaction pass before retrying.

---

## 🧪 Testing & Verification

The solution contains comprehensive unit, correctness, stress, and performance test suites covering:

* **Correctness:** Handle generation, stub redirection, and canary overrun/underrun validation.
* **Compaction:** In-place data sliding, `GoodEnough` early termination, and `FreeMany` batch recovery.
* **Stability:** Multi-threaded stress tests under concurrent alloc/free workloads.

To execute the full test suite:
```bash
dotnet test --configuration Release
```

---

## 📜 License

This project is licensed under the Apache 2.0 License - see the [License.txt](License.txt) file for details.
