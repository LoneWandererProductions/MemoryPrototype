# MemoryLane

**MemoryLane** is a prototype memory allocation and handle system created as an experiment in custom memory management using C#. It's designed to explore low-level memory control strategies typically found in game engines or real-time systems, such as handle indirection, dual-lane memory tiers, and optional compaction.

> ⚠️ **Note:** This is a **prototype for learning and experimentation**. It is **not production-ready** and remains **untested for real-world use cases**. Use at your own risk.

**Known Limitations**
- Does not manage C# objects directly — only unmanaged memory blocks.
- No garbage collection integration.
- No automatic bounds checking — accessing memory via MemoryHandle is safe in most cases (untested) only if used through the provided API correctly.
- Designed for low-level experimentation, not high-level safety.

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

- 🛤️ **One-Way Lane Transfer**
  - `OneWayLane` component moves memory entries from `FastLane` to `SlowLane` using an internal buffer and `Marshal.Copy`.
  - Useful for offloading memory that is better suited for long-term storage.
  - Plugged into the compaction cycle or invoked manually.

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

- [ ] **OneWayLane Improvements**  
  - Use memory pool or shared scratch buffers.
  - Support bidirectional memory migration.
  - Expose migration cost/heuristics to caller or policies.

- [ ] **Lane Improvements**  
  - Change the way the Lanes allocate the Memory
  - Improve performance in generell

- [ ] **Memory Usage Tracking (SQL Server-inspired)**  
  Collect per-user, per-program statistics:
  - Track access frequency, allocation lifetime, and promotion history.
  - Use this data to recommend compaction avoidance, slow-lane promotion, or preallocation.
  - Useful for long-running applications and plug-and-play optimization.
  - Developer can assign debug tags to allocations for diagnostics.

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
                FastLaneSize = 1024 * 1024,       // 1 MB
                SlowLaneSize = 10 * 1024 * 1024, // 10 MB
                Threshold = 4096,                 // Switch threshold between lanes
                EnableAutoCompaction = true,
                CompactionThreshold = 0.90,
                SlowLaneUsageThreshold = 0.85,
                SlowLaneSafetyMargin = 0.10,
                PolicyCheckInterval = TimeSpan.FromSeconds(10)
            };

            var arena = new MemoryArena(config);

            // --- Raw MemoryArena usage (more control, more verbose) ---
            var size = Marshal.SizeOf<MyStruct>();
            var handleRaw = arena.Allocate(size);
            ref var dataRaw = ref arena.Get<MyStruct>(handleRaw);
            dataRaw.Value = 123;
            Console.WriteLine($"Raw arena Value: {dataRaw.Value}");
            arena.Free(handleRaw);

            // --- TypedMemoryArena usage (simpler, more abstract) ---
            var typedArena = new TypedMemoryArena(arena);
            var handleTyped = typedArena.Allocate<MyStruct>();
            typedArena.Set(handleTyped, new MyStruct { Value = 456, PositionX = 1.1f, PositionY = 2.2f });
            ref var dataTyped = ref typedArena.Get<MyStruct>(handleTyped);
            Console.WriteLine($"Typed arena Value: {dataTyped.Value}");
            typedArena.Free(handleTyped);

            // Optionally run manual compaction
            arena.RunMaintenanceCycle();
