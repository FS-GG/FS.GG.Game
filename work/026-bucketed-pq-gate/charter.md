---
schemaVersion: 1
workId: 026-bucketed-pq-gate
title: Bucketed Priority Queue — Profiling Gate Decision
stage: charter
changeTier: tier2
status: chartered
policyPointers:
  - .fsgg/sdd.yml
  - .fsgg/agents.yml
  - .fsgg/policy.yml
  - .fsgg/capabilities.yml
  - .fsgg/tooling.yml
---

# Bucketed Priority Queue — Profiling Gate Decision Charter

## Identity
Milestone M4 item 3.4 of the Red Blob Games algorithm roadmap
(`docs/reports/2026-07-19-redblobgames-algorithm-incorporation-roadmap.md`, §4.4). This item is
**explicitly gated by profiling** — the roadmap says the bucketed priority queue is "worth it only if
profiling shows the immutable-`Set` frontier is the bottleneck" and it is "taken only if measurement
justifies it," with "no public surface change." This work item makes that measured decision and
records it, backed by a benchmark smoke test. It changes no public surface.

## Principles
- **Measure, then decide.** The swap is optional and internal; this item produces the measurement and
  the reasoned decision, not a speculative implementation.
- **A bucketed PQ needs a bounded small `f`-range.** The library's `astar` runs over an *unbounded*
  integer coordinate space with int64 `f` — the f-range is not bounded, so O(1) integer buckets do
  not cleanly apply; a `Map`/`Set` frontier is the appropriate general structure.
- **No public surface change.** `Pathfinding`'s surface and byte-identical outputs are untouched.

## Scope Boundaries
- **In:** a benchmark smoke test exercising `astar` on a large search (frontier throughput is
  adequate), and a recorded decision (`docs/reference/`) that the bucketed-PQ swap is deferred per its
  own profiling gate — the f-range is unbounded and the frontier is not the profiled bottleneck.
- **Out:** implementing/wiring the `IntBucketQueue` (would be a follow-on IF a future profile of a
  bounded-range workload justifies it), any public surface change, and the other M4 items (dice 3.1,
  templates 3.3, influence 3.2).

## Policy Pointers
- Honors constitution I (specify-before-implement), II (structured decision recorded), VI (evidence —
  the benchmark), and "no silent output changes" (surface unchanged).
- Tier 2 (internal/decision): needs spec + a benchmark/consistency check; public baselines unchanged.
- Governance pointers are optional compatibility facts, not evaluated by this command.

## Lifecycle Notes
- The benchmark test carries a stable filterable name.
- Next lifecycle action: `fsgg-sdd specify --work 026-bucketed-pq-gate`.
