---
schemaVersion: 1
workId: 016-pathfinding-regions
title: Connected-Component Early-Out
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Connected-Component Early-Out Specification

Prose status: specified

## User Value
Game-logic code can build a `Pathfinding.Regions` value once for a bounded grid and then ask
`Regions.sameComponent a b` — an O(1) test — to reject an unreachable `start -> goal` before running
any search. Today `astar`/`bfs`/`reachable` fully explore before reporting "no path", which on maps
with walls or islands is the worst case on every failed query; the component guard turns that into
two map lookups.

## Scope
- SB-001: A `Pathfinding.Regions` opaque value with `Regions.build neighbourhood bounds isWalkable`
  (deterministic flood fill over the inclusive `Cell * Cell` bounds, using the module's own neighbour
  rule) and `Regions.sameComponent regions a b : bool` (O(1)).
- SB-002: A differential property test that `sameComponent` agrees with `astar` reachability over the
  same bounded walkability, plus a determinism golden.

## Non-Goals
- SB-003: No exposed region ids/sizes/counts, no incremental or dynamic re-labelling, no unbounded
  region (the framework holds no map, so bounds are required), no auto-wiring the guard into the
  `astar`/`bfs`/`reachable` signatures, and none of the other M1 items (JPS 1.1, ALT 1.3, tie-break
  1.4).

## User Stories
- US-001 (P1): As game-logic code, I can reject an unreachable `start -> goal` in O(1) with
  `Regions.sameComponent`, so a failed query on a walled map does not exhaust `maxVisited`.
- US-002 (P1): As game-logic code, I can rely on `Regions` being pure and deterministic and on its
  connectivity matching what `astar`/`bfs` can actually traverse (including no corner-cutting).

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given a bounded grid and two in-bounds walkable cells, when `sameComponent a b` is called, then it returns true exactly when `astar` finds a path from a to b under the same neighbourhood.
- AC-002 [US-001] [FR-002]: Given a cell that is out of bounds or not walkable, when `sameComponent` is called with it, then it returns false (an out-of-bounds/blocked cell is in no component — even against itself).
- AC-003 [US-002] [FR-003]: Given `EightWay`, when two walkable cells are diagonally adjacent but their shared orthogonals are blocked, then `sameComponent` reports them connected only if a legal no-corner-cutting path joins them — matching `astar`.
- AC-004 [US-002] [FR-004]: Given identical `(bounds, isWalkable, neighbourhood)`, when `build` runs repeatedly, then the resulting `Regions` yields byte-identical `sameComponent` answers, and internal region label ids never appear in any result.
- AC-005 [US-001] [FR-005]: Given a built `Regions`, when `sameComponent` is called, then it performs O(1) work (two label lookups) with no search — demonstrated by answering across a disconnected map without a `maxVisited` bound.

## Functional Requirements
- FR-001: `Regions.sameComponent a b` MUST return true exactly when a and b are both in-bounds walkable cells joined by a path under the region's `neighbourhood` — i.e. it agrees with `astar` reachability over the same bounded walkability. (covers AC-001)
- FR-002: `Regions.sameComponent` MUST return false when either cell is out of bounds or not walkable, with no exception for `a = b` (an unwalkable cell belongs to no component). (covers AC-002)
- FR-003: `Regions.build`'s connectivity MUST use the module's neighbour rule, honouring the no-corner-cutting rule under `EightWay`, so `sameComponent` agrees with `astar`/`bfs` traversability. (covers AC-003)
- FR-004: `Regions.build` MUST be a pure, deterministic function of `(neighbourhood, bounds, isWalkable)` over a documented scan order, and `sameComponent` MUST expose only a boolean so internal label ids never leak into results. (covers AC-004)
- FR-005: `Regions.sameComponent` MUST be O(1) (two label lookups, no search), so it rejects an unreachable `start -> goal` without exploring. (covers AC-005)

## Ambiguities
- AMB-001: Does `Regions` expose region ids/sizes, or only the boolean `sameComponent`? (Roadmap: only the boolean, so relabeling cannot leak.)
- AMB-002: Bounds representation — an inclusive `Cell * Cell` corner pair (normalized so caller order is irrelevant), or a separate bounds type?
- AMB-003: Scan/flood order for labelling — row-major scan with a BFS flood, or another documented order? (Only affects internal ids, which do not leak.)

## Public Or Tool-Facing Impact
- Tier 1 (contracted). Adds a public `Pathfinding.Regions` type and `Regions.build`/`sameComponent`
  functions to `FS.GG.Game.Core` — so the `.fsi`, the surface baseline (types + members), and tests
  land together, and the surface-baseline-drift gate must stay green. No existing signature changes.

## Lifecycle Notes
- Differential/determinism tests MUST carry a stable filterable name so the `gate.yml` determinism
  guard covers them.
- Next lifecycle action: `fsgg-sdd clarify --work 016-pathfinding-regions`.
