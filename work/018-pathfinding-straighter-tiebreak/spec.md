---
schemaVersion: 1
workId: 018-pathfinding-straighter-tiebreak
title: Straighter-Path Tie-Break
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Straighter-Path Tie-Break Specification

Prose status: specified

## User Value
Game-logic code can opt into `Pathfinding.astarStraight` — an A* variant whose frontier tie-break
prefers, among equal-`f` nodes, those nearer the straight line to the goal — to get an **optimal-cost**
path that looks straighter (fewer needless zig-zags) and is often found with fewer expansions. The
shipped `astar` is unchanged and byte-identical, so existing consumers are unaffected and migrate to
the bias only if they want it.

## Scope
- SB-001: A `Pathfinding.astarStraight` entry point, signature-parallel to `astar`, whose ordering key
  is `(f, h, cross, Col, Row)` with `cross` an integer cross-product deviation from the straight
  `start -> goal` line.
- SB-002: A differential (equal-cost + valid + reachability) oracle vs `astar`, a byte-identity check
  that `astar`'s own output is unchanged, a straightness-improvement test, and a determinism golden.

## Non-Goals
- SB-003: No change to `astar`'s signature or output, no applying the bias to `bfs`/`reachable`/`jps`/
  the Dijkstra fields, no any-angle/Theta* smoothing (roadmap 2.2), and none of the other M1 items
  (JPS 1.1, Regions 1.2, ALT 1.3).

## User Stories
- US-001 (P1): As game-logic code, I can call `astarStraight` for an optimal path that hugs the
  straight line to the goal, so unit movement looks less jagged without costing extra path length.
- US-002 (P1): As game-logic code, I can rely on the shipped `astar` staying byte-identical — the
  bias is opt-in — and on `astarStraight` being pure and byte-deterministic.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given any grid and a solvable query, when `astarStraight` is called, then it returns `Some p` with the identical total cost to `astar`'s path.
- AC-002 [US-001] [FR-002]: Given `astarStraight` returns `Some p`, then `p` is a valid path — start/goal-anchored, every step `Neighbourhood`-legal with no corner-cutting, every cell walkable.
- AC-003 [US-002] [FR-003]: Given the same inputs, when `astar` is called with and without this work item present, then its output is byte-identical (the bias never changes plain `astar`).
- AC-004 [US-001] [FR-004]: Given a case with several equal-cost paths (e.g. an open grid, off-axis goal), when both run, then `astarStraight`'s path has a smaller total deviation from the straight `start -> goal` line than `astar`'s.
- AC-005 [US-002] [FR-005]: Given identical inputs across repeated or parallel runs, when `astarStraight` is called, then the returned path is byte-identical (integer cross-product, strict total order).

## Functional Requirements
- FR-001: `astarStraight` MUST return a path whose total cost equals `astar`'s path cost for the same inputs whenever a path exists on a search not bounded out by `maxVisited`. (covers AC-001)
- FR-002: Every path `astarStraight` returns MUST be valid — start-prefixed, goal-suffixed, each step a `Neighbourhood`-legal adjacent move with no corner-cutting, every cell walkable. (covers AC-002)
- FR-003: The shipped `astar` MUST remain byte-identical — the cross-product tie-break is opt-in via `astarStraight` and never alters `astar`'s output (the bias term is a constant 0 in `astar`'s key). (covers AC-003)
- FR-004: On a query with several equal-cost paths, `astarStraight`'s path MUST have a total straight-line deviation no greater than `astar`'s, and strictly smaller on at least one such query. (covers AC-004)
- FR-005: `astarStraight` MUST be byte-deterministic across runs and platforms — the cross term is an integer `|dx1*dy2 - dx2*dy1|` and the key is a strict total order, so no float or hash iteration influences output. (covers AC-005)

## Ambiguities
- AMB-001: The cross-product term — deviation of the current cell from the straight `start -> goal` line via `|dx1*dy2 - dx2*dy1|`, or a different straightness measure (e.g. checkerboard)?
- AMB-002: Where the cross term sits in the key — after `h` (as `(f, h, cross, Col, Row)`) or elsewhere? (It must stay a strict total order and must not perturb `astar`.)
- AMB-003: Surface shape — a separate `astarStraight` function, or a flag on `astar`? (A flag would change `astar`'s signature; the roadmap wants `astar` untouched.)

## Public Or Tool-Facing Impact
- Tier 1 (contracted). Adds a public `Pathfinding.astarStraight` signature to `FS.GG.Game.Core` and
  extends the internal frontier key with a tie term (constant 0 for `astar`, so its output is
  byte-identical). `.fsi`, both surface baselines, and tests land together; the drift gate stays green.
  No existing signature changes.

## Lifecycle Notes
- Differential/straightness/determinism tests MUST carry a stable filterable name for the `gate.yml`
  determinism guard.
- Next lifecycle action: `fsgg-sdd clarify --work 018-pathfinding-straighter-tiebreak`.
