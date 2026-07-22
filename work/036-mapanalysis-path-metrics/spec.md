---
schemaVersion: 1
workId: 036-mapanalysis-path-metrics
title: MapAnalysis Path & Flow Metrics
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# MapAnalysis Path & Flow Metrics Specification

Prose status: specified

## User Value
A map-builder can measure a map's size and shape — how isolated a cell is (its farthest reachable cell) and
the map's critical-path length (diameter, the longest shortest-path) — as clean integer hop counts, to
validate a map is neither too small nor too sprawling. Milestone M10 of the `MapAnalysis` machinery.

## Scope
- SB-001: `MapAnalysis.isolation` (a floor cell's eccentricity) and `MapAnalysis.diameter` (the map's longest
  shortest-path), by unweighted BFS over the corner-cut-aware floor adjacency.
- SB-002: A correctness + determinism + totality property suite; the surface baseline; a SKILL.md sentence.

## Non-Goals
- SB-003: No `MapAnalysis.distanceField` (use `Pathfinding.distanceField`); no fairness/validate (M11) or
  tactical analysis (M12); no render-tier work.

## User Stories
- US-001 (P1): As a map-builder, I can measure how isolated a cell is (the hop distance to its farthest
  reachable floor cell).
- US-002 (P1): As a map-builder, I can measure a map's diameter (critical-path length) to validate its size.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given a `TileMap`, a `Neighbourhood`, and a `Floor` cell, when `MapAnalysis.isolation` runs, then it returns the maximum hop distance (BFS, each neighbour step = 1) from the cell to any cell reachable from it; a non-`Floor` or isolated cell returns 0.
- AC-002 [US-002] [FR-002]: Given a `TileMap` and a `Neighbourhood`, when `MapAnalysis.diameter` runs, then it returns the maximum shortest-path hop distance between any two `Floor` cells in the same component (0 for an empty-floor or single-cell map).
- AC-003 [US-002] [FR-003]: Given the same map and neighbourhood, when both run, then `MapAnalysis.diameter` equals the maximum over all `Floor` cells of `MapAnalysis.isolation`.
- AC-004 [US-001] [FR-004]: Given a straight corridor of `k` floor cells, when `diameter` runs, then it returns `k - 1` (the two ends are `k-1` hops apart).
- AC-005 [US-001] [FR-005]: Given degenerate input (empty map, all-wall, non-floor cell), when any function runs, then it returns 0 and never throws.

## Functional Requirements
- FR-001: `MapAnalysis.isolation` MUST return the maximum BFS hop distance from a `Floor` cell to any cell reachable from it (0 for a non-`Floor` or isolated cell). (covers AC-001)
- FR-002: `MapAnalysis.diameter` MUST return the maximum shortest-path hop distance between any two `Floor` cells in the same component (0 for empty-floor or single-cell). (covers AC-002)
- FR-003: `MapAnalysis.diameter` MUST equal the maximum over all `Floor` cells of `MapAnalysis.isolation` for the same map and neighbourhood. (covers AC-003)
- FR-004: For a straight corridor of `k` `Floor` cells, `MapAnalysis.diameter` MUST return `k - 1`. (covers AC-004)
- FR-005: Every function MUST be total — degenerate input returns 0 and never throws. (covers AC-005)

## Ambiguities
- AMB-001: Should the metrics be unweighted hop counts, or `Pathfinding.distanceField`'s `baseStep`/√2-weighted movement cost?
- AMB-002: Should `MapAnalysis` add a `distanceField`, or defer the field to `Pathfinding.distanceField` and add only the derived metrics?

## Public Or Tool-Facing Impact
- Tier 1 (contracted). Extends the public `MapAnalysis` surface; updates the surface baseline. Additive.

## Lifecycle Notes
- The `diameter = max isolation` consistency property is the headline test; tests carry stable filterable names.
- Next lifecycle action: `fsgg-sdd clarify --work 036-mapanalysis-path-metrics`.
