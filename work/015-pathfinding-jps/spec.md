---
schemaVersion: 1
workId: 015-pathfinding-jps
title: Jump Point Search
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Jump Point Search Specification

Prose status: specified

## User Value
Game-logic code can call `Pathfinding.jps` — a grid-specialized A* that "jumps" over runs of
symmetric intermediate cells on uniform-cost grids — and get a **least-cost path of the same cost as
`astar` returns**, computed while popping far fewer frontier nodes. It is a drop-in acceleration
beside `astar`: same `Neighbourhood`, same binary `isWalkable`, same `maxVisited` bound, same
endpoint-inclusive `Cell list option` result, and the same cross-run/cross-platform determinism.

## Scope
- SB-001: A `Pathfinding.jps` entry point over the existing uniform-cost grid model — `FourWay`/
  `EightWay` with the module's no-corner-cutting rule, a binary `isWalkable`, a `maxVisited` bound,
  and an endpoint-inclusive `Cell list option` result.
- SB-002: A differential property test against the shipped `astar` (equal cost, valid path,
  identical `None`/`Some` decision on a complete search) plus a self-determinism golden, and a
  benchmark asserting `jps` pops strictly fewer frontier nodes than `astar` on open maps.

## Non-Goals
- SB-003: No weighted-terrain JPS (it takes no `cost` function; weighted terrain stays with
  `reachable`/`distanceField`), no JPS+/preprocessing, no change to `astar`/`bfs`/`distanceField`
  behaviour or surface, no hex JPS (depends on 2.1), and none of the other M1 items (Regions 1.2,
  ALT 1.3, tie-break 1.4).

## User Stories
- US-001 (P1): As game-logic code, I can call `jps` in place of `astar` on a uniform-cost grid and
  get a least-cost path of the same cost, so pathing is faster with no change to the answer's cost or
  validity.
- US-002 (P1): As game-logic code, I can rely on `jps` being pure, total, and byte-deterministic —
  the same result across runs and platforms and the same degenerate-input behaviour as `astar` — so
  it is safe inside a deterministic-replay `update`.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given any uniform-cost grid and a `start`/`goal` for which `astar`
  returns `Some path` on a search not bounded out by `maxVisited`, when `jps` is called with the same
  arguments, then `jps` returns `Some p` where `p` has the identical total cost to `astar`'s path.
- AC-002 [US-001] [FR-002]: Given `jps` returns `Some p`, then `p` is a valid path — it begins at
  `start`, ends at `goal`, every consecutive pair is an adjacent step legal under the `Neighbourhood`
  (diagonals only when both shared orthogonals are walkable), and every cell is walkable.
- AC-003 [US-001] [FR-003]: Given the same grid, when both are run on a search not bounded out by
  `maxVisited`, then `jps` returns `None` exactly when `astar` returns `None` (they agree on
  reachability).
- AC-004 [US-002] [FR-004]: Given a non-walkable `start` or `goal`, or `maxVisited <= 0`, or
  `start = goal` with a walkable `start`, when `jps` is called, then it returns exactly what `astar`
  returns for that degenerate input (`None`, `None`, and `Some [start]` respectively).
- AC-005 [US-002] [FR-005]: Given identical inputs across repeated or parallel runs, when `jps` is
  called, then the returned `Cell list option` is byte-identical every time (no heap-order,
  hash-iteration, or float tie-break leakage).
- AC-006 [US-001] [FR-006]: Given a large open (few-obstacle) map, when `jps` and `astar` both solve
  the same query, then `jps` pops strictly fewer frontier nodes than `astar`.

## Functional Requirements
- FR-001: `jps` MUST return a path whose total cost (`baseStep` per orthogonal step, `baseStep * 14 / 10` per diagonal) equals `astar`'s path cost for the same inputs whenever `astar` finds a path on a search not bounded out by `maxVisited`. (covers AC-001)
- FR-002: Every path `jps` returns MUST be valid — `start`-prefixed, `goal`-suffixed, each consecutive pair a `Neighbourhood`-legal adjacent move honouring the no-corner-cutting rule, and every cell walkable. (covers AC-002)
- FR-003: `jps` MUST agree with `astar` on reachability — it returns `None` exactly when `astar` returns `None` on a search not bounded out by `maxVisited`. (covers AC-003)
- FR-004: `jps` MUST be total and share `astar`'s degenerate-input contract: a non-walkable `start` or `goal`, or `maxVisited <= 0`, yields `None`; a walkable `start = goal` yields `Some [start]`. (covers AC-004)
- FR-005: `jps` MUST be byte-deterministic across runs and platforms — jump order and forced-neighbour ordering are a documented total order and no cost or key is a float. (covers AC-005)
- FR-006: `jps` MUST pop strictly fewer frontier nodes than `astar` on open maps, demonstrated by a benchmark that counts frontier pops for both on a shared open-map query. (covers AC-006)

## Ambiguities
- AMB-001: **Byte-identical *cells* vs identical *cost*.** The roadmap's exit criterion reads
  "byte-identical paths to `astar`." On grids where more than one least-cost path exists (e.g. an
  open `FourWay` grid, where every monotone staircase is equal-cost), JPS's canonical-jump pruning
  and A*'s `(f, h, Col, Row)` frontier tie-break select *different* equal-cost cell sequences, so
  literal cell-identity cannot hold in general without discarding JPS's pruning (which is its entire
  value). Is the differential contract literal cell-identity, or equal-cost + validity + agreement on
  reachability + `jps` self-determinism?
- AMB-002: **`maxVisited` accounting unit.** `astar` bounds on frontier pops (open-set expansions).
  A jump-point search pops far fewer nodes but scans many cells inside each jump. Does `jps`'
  `maxVisited` bound the same unit `astar` uses (frontier pops), so the parameter means the same
  thing to a caller — accepting that the `None`-on-exhaustion boundary then differs between the two?

## Public Or Tool-Facing Impact
- Tier 1 (contracted). Adds a public `Pathfinding.jps` signature to `FS.GG.Game.Core` — so the
  `.fsi`, the surface baseline (`readiness/surface-baselines/FS.GG.Game.Core.txt`), and the tests
  land together, and the surface-baseline-drift gate must stay green. No existing signature changes.

## Lifecycle Notes
- The differential/determinism tests MUST carry a stable filterable name (a `determinism` /
  `differential` substring) so the `gate.yml` determinism-and-property-invariants guard covers them,
  as the existing Pathfinding goldens do.
- Next lifecycle action: `fsgg-sdd clarify --work 015-pathfinding-jps`.
