---
schemaVersion: 1
workId: 026-bucketed-pq-gate
title: Bucketed Priority Queue — Profiling Gate Decision
stage: specify
changeTier: tier2
status: specified
publicOrToolFacingImpact: true
---

# Bucketed Priority Queue — Profiling Gate Decision Specification

Prose status: specified

## User Value
The team gets a **recorded, measured decision** on the roadmap's profiling-gated bucketed priority
queue (item 3.4), backed by an `astar` frontier benchmark — instead of a speculative implementation.
The roadmap says the swap is "taken only if measurement justifies it" with "no public surface change";
this item measures, decides, and records, keeping the `Pathfinding` surface and its byte-identical
outputs unchanged.

## Scope
- SB-001: A benchmark smoke test exercising `Pathfinding.astar` on a large search (a big open grid),
  confirming the immutable-`Set` frontier is adequate — the search completes optimally within its
  `maxVisited` bound.
- SB-002: A decision note under `docs/reference/` recording the profiling-gated **deferral** of the
  bucketed-PQ swap: the library's `astar` runs over an unbounded integer coordinate space with int64
  `f`, so the f-range is not bounded and O(1) integer buckets do not cleanly apply; the frontier is
  not the profiled bottleneck. No `IntBucketQueue` is implemented; the public surface is unchanged.

## Non-Goals
- SB-003: No `IntBucketQueue` implementation or frontier swap (a follow-on IF a future profile of a
  bounded-range workload justifies it), no public surface change, and none of the other M4 items.

## User Stories
- US-001 (P1): As the team, I can read a recorded, measured decision on the bucketed-PQ gate, so the
  optional optimization is dispositioned honestly rather than left ambiguous.
- US-002 (P1): As a `Pathfinding` consumer, I can rely on the surface and outputs being unchanged by
  this decision.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given a large open grid, when the benchmark runs `astar`, then it returns an optimal path within its `maxVisited` bound (the Set frontier is adequate in practice).
- AC-002 [US-001] [FR-002]: Given the decision note, then it records the profiling-gated deferral with its rationale (unbounded f-range; frontier not the bottleneck) and names the follow-on condition (a bounded-range workload profile).
- AC-003 [US-002] [FR-003]: Given this work item, when the public surface baselines are checked, then they are byte-identical — no `Pathfinding` signature or output changed.

## Functional Requirements
- FR-001: A benchmark test MUST exercise `Pathfinding.astar` on a large search and confirm it returns an optimal-cost path within its `maxVisited` bound. (covers AC-001)
- FR-002: A decision note under `docs/reference/` MUST record the bucketed-PQ swap as deferred per its profiling gate, with the rationale (unbounded int64 f-range so O(1) buckets do not apply; frontier not the profiled bottleneck) and the follow-on condition. (covers AC-002)
- FR-003: This work item MUST make no public surface change — the `Pathfinding` `.fsi` and both surface baselines stay byte-identical. (covers AC-003)

## Ambiguities
- AMB-001: Deliverable form — implement and benchmark the IntBucketQueue, or record the gated decision with a supporting benchmark?
- AMB-002: Where the decision is recorded — a `docs/reference/` note, or only in the work item?

## Public Or Tool-Facing Impact
- Tier 2 (internal/decision). Adds a benchmark test and a `docs/reference/` note; the public
  `Pathfinding` surface and baselines are unchanged (the drift gate must show no change).

## Lifecycle Notes
- The benchmark test MUST carry a stable filterable name.
- Next lifecycle action: `fsgg-sdd clarify --work 026-bucketed-pq-gate`.
