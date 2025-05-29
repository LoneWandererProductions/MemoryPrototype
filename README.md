# MemoryLane

**MemoryLane** is a prototype memory allocation and handle system created as an experiment in custom memory management using C#. It's designed to explore low-level memory control strategies typically found in game engines or real-time systems, such as handle indirection, dual-lane memory tiers, and optional compaction.

> ⚠️ **Note:** This is a **prototype for learning and experimentation**. It is **not production-ready** and remains **untested for real-world use cases**. Use at your own risk.

---

## ✨ Features

- 🧠 **Dual Memory Lanes**
  - `FastLane` for low-latency, short-lived allocations (e.g., frame-local data).
  - `SlowLane` for high-capacity, persistent allocations (e.g., assets or background data).

- 🔁 **Stable Handle System**
  - `MemoryHandle` provides safe, opaque references to internal memory entries.
  - Supports internal redirection through lightweight stubs.

- 🧹 **Optional Live Compaction**
  - Memory compaction to reduce fragmentation, with stub-based relocation handling.

- 🔄 **Stub-Based Indirection**
  - Allows redirection of handles to new memory locations without breaking references.

- 🧪 **Safety Checks**
  - Methods like `TryGet<T>`, `IsValid`, and `GetHandleState` validate access.

---

## 🎯 Learning Objectives

This project was created to:
- Gain a general understanding of memory management concepts.
- Experiment with custom memory allocation strategies.
- Explore techniques for handle indirection and pointer safety.
- Understand concepts behind arena allocators, memory paging, and pooling.
- Practice designing low-level systems architecture in C#.

---

## ⚙️ Requirements

- **.NET 5.0** (or compatible runtime)

---

## 🧭 Future Work / Ideas and Todos

The following are planned features or experimental directions for further exploration, nothing concrete though:

- [ ] **Paging Support**  
  Evict memory to disk or swap lanes when memory is full, with lazy reloads.

- [ ] **Compaction Improvements**  
  Reserve ~10% of the slow lane as scratch space for non-blocking lane compaction.

- [ ] **Memory Usage Tracking**  
  Collect statistics per category or system (e.g., physics, AI, UI).

- [ ] **Allocation Tags / Groups**  
  Group handles by type for bulk free or memory diagnostics.

- [ ] **Alignment Support**  
  Add `AlignTo(int boundary)` in `FindFreeSpot()` for SIMD/cache safety.

- [ ] **Failover Policies**  
  If `FastLane` fails, automatically fall back to `SlowLane` + stub.
  (In general: allow plug-and-play memory strategies.)

- [ ] **Multithreaded Allocator**  
  Add concurrency support via spinlocks or lock-free front-end.

- [ ] **Object Lifecycle Management**  
  Auto-clear or evict stale/old objects based on access time.

- [ ] **Dynamic Memory Expansion**  
  Support runtime expansion of internal memory buffers.

- [ ] **Memory Compression**  
  Compress rarely accessed data in the SlowLane to reclaim space (with a performance tradeoff).

- [ ] **Robust unit tests**  
  Add automatic tests for correctness, safety, and basic performance characteristics.
---

## 🧩 Example Usage

```csharp
var config = new MemoryManagerConfig
{
    FastLaneSize = 1024 * 1024, // 1 MB
    SlowLaneSize = 10 * 1024 * 1024, // 10 MB
    Threshold = 4096, // Switch threshold between lanes
    EnableAutoCompaction = true,
    CompactionThreshold = 0.90,
    SlowLaneUsageThreshold = 0.85,
    SlowLaneSafetyMargin = 0.10,
    PolicyCheckInterval = TimeSpan.FromSeconds(10)
};

var arena = new MemoryArena(config);

// Allocate a small struct
var handle = arena.Allocate(sizeof(MyStruct), hints: AllocationHints.None);

// Resolve the pointer and work with the data
unsafe
{
    var ptr = (MyStruct*)arena.Resolve(handle);
    ptr->Value = 123;
}

// Free when done
arena.Free(handle);

// Optionally run manual compaction
arena.RunMaintenanceCycle();
