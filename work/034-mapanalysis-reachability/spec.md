---
schemaVersion: 1
workId: 034-mapanalysis-reachability
title: MapAnalysis Reachability & Connectivity
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# MapAnalysis Reachability & Connectivity Specification

Prose status: specified

## User Value
A map-building agent can ask reachability and connectivity questions of *any* map — what is reachable from a
start, which floor cells are stranded, is the map fully connected, how many components — over a walkability
predicate or a `TileMap`, consistently with the router. Milestone M8, the first `MapAnalysis` machinery.

## Scope
- SB-001: A new `MapAnalysis` module in `FS.GG.Game.Core` with `reachable` (predicate-general),
  `stranded`, `isConnected`, and `componentCount`.
- SB-002: Built on `Pathfinding.distanceField` (reachable set) and `MapGen.regions` (components), so it agrees
  with the router's neighbour logic; no second flood-fill.
- SB-003: A correctness + determinism + router-consistency property suite; the surface baseline; and a
  `MapAnalysis` section in the `fs-gg-mapcraft` SKILL.md.

## Non-Goals
- SB-004: No chokepoints (M9), path metrics (M10), fairness/validate (M11), or tactical analysis (M12); no
  render-tier work.

## User Stories
- US-001 (P1): As a map-builder, I can compute the reachable set from a start over a walkability predicate.
- US-002 (P1): As a map-builder, I can ask whether a map is fully connected and, if not, which floor cells are
  stranded from a reference point.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given a walkability predicate, a `Neighbourhood`, and a walkable `start`, when `MapAnalysis.reachable` runs, then it returns the set of all cells reachable from `start` bounded by `maxVisited`; a non-walkable `start` returns the empty set.
- AC-002 [US-001] [FR-002]: Given the same map, start, and neighbourhood, when both run, then `MapAnalysis.reachable`'s set equals exactly the set of cells `Pathfinding.bfs` can reach from `start`.
- AC-003 [US-002] [FR-003]: Given a `TileMap` and a reference cell, when `MapAnalysis.stranded` runs, then it returns the `Floor` cells not reachable from the reference (all `Floor` cells when the reference is not `Floor`), in row-major order.
- AC-004 [US-002] [FR-004]: Given a `TileMap`, when `MapAnalysis.isConnected` runs, then it returns true iff all `Floor` cells form a single component under the neighbourhood (an empty-floor map is connected, vacuously).
- AC-005 [US-002] [FR-005]: Given a `TileMap`, when `MapAnalysis.componentCount` runs, then it returns the number of `Floor` components (matching `MapGen.regions`).
- AC-006 [US-001] [FR-006]: Given degenerate input (empty map, non-floor start, zero `maxVisited`), when any function runs, then it returns a documented empty/clamped value and never throws.

## Functional Requirements
- FR-001: `MapAnalysis.reachable` MUST return the set of cells reachable from a walkable `start` over the `isWalkable` predicate and `Neighbourhood`, bounded by `maxVisited`; a non-walkable `start` yields the empty set. (covers AC-001)
- FR-002: `MapAnalysis.reachable`'s returned set MUST equal exactly the cells `Pathfinding.bfs` can reach from the same `start` under the same `Neighbourhood`. (covers AC-002)
- FR-003: `MapAnalysis.stranded` MUST return the `Floor` cells of a `TileMap` not reachable from a reference cell (all `Floor` cells when the reference is not `Floor`), in row-major order. (covers AC-003)
- FR-004: `MapAnalysis.isConnected` MUST return true iff all `Floor` cells form a single component under the `Neighbourhood` (an empty-floor map is connected). (covers AC-004)
- FR-005: `MapAnalysis.componentCount` MUST return the number of `Floor` components, consistent with `MapGen.regions`. (covers AC-005)
- FR-006: Every function MUST be total — degenerate input yields a documented empty/clamped value and never throws. (covers AC-006)

## Ambiguities
- AMB-001: The design-doc sketch listed `components: Neighbourhood -> TileMap -> Region list`. Since `MapGen.regions` already provides exactly that, should `MapAnalysis` expose `componentCount` (a thin count over `regions`) instead of re-exporting/duplicating `components`?
- AMB-002: Argument order for `reachable` — match `Pathfinding.bfs` (`neighbourhood -> maxVisited -> isWalkable -> start`), or the sketch's `(neighbourhood -> isWalkable -> start -> maxVisited)`?

## Public Or Tool-Facing Impact
- Tier 1 (contracted). Adds a public `MapAnalysis` module + `.fsi` to `FS.GG.Game.Core`; updates the surface
  baseline; adds a section to the `fs-gg-mapcraft` SKILL.md. Additive.

## Lifecycle Notes
- The router-consistency property is the headline test; tests carry stable filterable names.
- Next lifecycle action: `fsgg-sdd clarify --work 034-mapanalysis-reachability`.
