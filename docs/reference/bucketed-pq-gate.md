# Decision: bucketed priority queue (roadmap 3.4) — deferred per profiling gate

- **Work item:** `026-bucketed-pq-gate`
- **Roadmap item:** M4 §4.4 — "Bucketed / binned priority queue"
- **Decision:** **Deferred.** The bucketed-PQ swap is not taken; the immutable-`Set` frontier is
  retained. `Pathfinding`'s public surface and its byte-identical outputs are unchanged.

## Why the roadmap gates this

The roadmap states 3.4 is "worth it **only if** profiling shows the immutable-`Set` frontier is the
bottleneck," is "taken only if measurement justifies it," and requires "**no public surface change**."
An O(1) integer bucket queue applies "when the `f`-range is small."

## Why the gate is not met

1. **The `f`-range is not bounded.** `Pathfinding.astar` runs over an **unbounded integer coordinate
   space** (the walkability predicate *is* the map) with an **int64** ordering key `(f, h, tie, Col,
   Row)`. An O(1) bucket array is indexed by integer `f`; with an unbounded int64 `f`-range there is no
   small, dense bucket index to allocate, so the structure degrades to a sparse map keyed by `f` —
   which is what the `Set` frontier already is. The O(1) claim does not hold for the general search.

2. **The frontier is not the profiled bottleneck.** Each expansion does an immutable-`Set` insert/pop
   (O(log n) over a frontier bounded by `maxVisited`) *plus* neighbour generation and one or more
   caller `isWalkable`/`cost` calls per neighbour. For the maxVisited-bounded searches the library
   targets (game maps of hundreds to low-thousands of cells), the caller predicate and neighbour
   allocation dominate; the frontier's log-factor is not the hot path. The benchmark smoke test
   (`Game.Core.Tests`, "bucketed-PQ gate: astar solves a large search…") confirms `astar` solves a
   large open search optimally within budget with the current frontier.

3. **Determinism must be preserved exactly.** Any swap must reproduce the exact `(f, h, tie, Col,
   Row)` total order byte-for-byte (a differential test). A bucket queue buys nothing here that the
   totally-ordered `Set` does not already provide.

## Follow-on condition

Revisit **only** if a concrete profile of a *bounded-range* workload (e.g. a fixed small grid with a
tight, dense integer `f`-range and a hot pathfinding loop) shows the `Set` frontier as the measured
bottleneck. At that point an internal `IntBucketQueue` can be introduced **behind the existing
frontier abstraction**, gated by that benchmark and a differential test proving byte-identical output
— with no public surface change, exactly as the roadmap sketches.
