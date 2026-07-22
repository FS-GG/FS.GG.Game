---
schemaVersion: 1
workId: 036-mapanalysis-path-metrics
title: MapAnalysis Path & Flow Metrics
stage: charter
changeTier: tier1
status: chartered
policyPointers:
  - .fsgg/sdd.yml
  - .fsgg/agents.yml
  - .fsgg/policy.yml
  - .fsgg/capabilities.yml
  - .fsgg/tooling.yml
---

# MapAnalysis Path & Flow Metrics Charter

## Identity
Milestone **M10** of the map construction & analysis design (Part II §9/§11). Extends `MapAnalysis` with the
size/shape metrics a map-builder validates against: `isolation` (a cell's eccentricity — how far its farthest
reachable cell is) and `diameter` (the map's critical-path length — the longest shortest-path between any two
floor cells). Depends on M8/M9; extends `src/Game.Core/MapAnalysis.fs`/`.fsi`.

## Principles
- **Topological hop distance.** The metrics are unweighted BFS hop counts over the same corner-cut-aware
  adjacency the module uses — a clean integer "how many steps across" that a designer reasons about, distinct
  from `Pathfinding.distanceField`'s `baseStep`/√2-weighted movement cost (which stays the routing layer's).
- **No duplication.** `MapAnalysis` does not re-add a `distanceField` — `Pathfinding.distanceField` is the
  field primitive; M10 adds the derived metrics `Pathfinding` does not.
- **Deterministic & total.** BFS levels are order-independent; `max` over them is commutative; a degenerate or
  empty-floor map yields 0.

## Scope Boundaries
- **In:** `MapAnalysis.isolation` and `MapAnalysis.diameter`; a correctness + determinism + totality suite;
  the surface baseline; a SKILL.md sentence.
- **Out:** fairness/validate (M11), tactical (M12); a `MapAnalysis.distanceField` (use `Pathfinding`'s);
  render-tier work.

## Policy Pointers
- Honors constitution I, III (surface + baseline), IV, V, VI (property tests), "no silent output changes".
- Tier 1: extends the public `MapAnalysis` surface.

## Lifecycle Notes
- `diameter = max over floor cells of isolation` is the headline consistency test.
- Next lifecycle action: `fsgg-sdd specify --work 036-mapanalysis-path-metrics`.
