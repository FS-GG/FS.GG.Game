---
schemaVersion: 1
workId: 017-pathfinding-alt-landmarks
title: ALT Landmark Heuristic
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# ALT Landmark Heuristic Specification

Prose status: specified

## User Value
Game-logic code can build a `Pathfinding.Landmarks` value once for a bounded grid and then call
`Landmarks.astar` to get an **optimal-cost** path — the same least cost plain `astar` returns — that
A* finds by expanding **fewer nodes** on large or open maps, because the landmark (ALT) heuristic is
admissible and far tighter than the octile/Manhattan estimate. `astar` itself is unchanged.

## Scope
- SB-001: A `Pathfinding.Landmarks` opaque value with `Landmarks.build` (deterministic farthest-point
  landmark selection over inclusive bounds, one `distanceField` per landmark), `Landmarks.heuristic`
  (admissible integer estimate to a goal), and `Landmarks.astar` (A* over `max(octile, ALT)`).
- SB-002: A differential property test (equal cost + valid path + reachability agreement vs `astar`),
  an admissibility test (heuristic never exceeds the true remaining distance), a determinism golden,
  and a fewer-expansions proxy.

## Non-Goals
- SB-003: No change to `astar`'s public signature or output, no ALT for weighted terrain (the
  heuristic serves the binary-`isWalkable` `astar`), no dynamic/incremental landmark updates, no
  unbounded landmark region, and none of the other M1 items (JPS 1.1, Regions 1.2, tie-break 1.4).

## User Stories
- US-001 (P1): As game-logic code, I can call `Landmarks.astar` for a path of the same least cost as
  `astar` but found with fewer expansions on large/open maps, so long-range pathing is cheaper.
- US-002 (P1): As game-logic code, I can rely on `Landmarks` being pure and deterministic — landmark
  selection is a fixed function of the map — and on the heuristic being admissible so optimality holds.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given a bounded grid and a solvable query, when `Landmarks.astar` is called, then it returns `Some p` with the identical total cost to `astar`'s path.
- AC-002 [US-001] [FR-002]: Given `Landmarks.astar` returns `Some p`, then `p` is a valid path — start/goal-anchored, every step `Neighbourhood`-legal with no corner-cutting, every cell walkable.
- AC-003 [US-001] [FR-003]: Given the same grid within a generous budget, when both run, then `Landmarks.astar` returns `None` exactly when `astar` returns `None` (reachability agreement).
- AC-004 [US-002] [FR-004]: Given identical inputs, when `build` and `Landmarks.astar` run repeatedly, then landmark selection and the returned path are byte-identical across runs and platforms.
- AC-005 [US-002] [FR-005]: Given a built `Landmarks`, a goal, and any cell, when `Landmarks.heuristic` is called, then the estimate never exceeds the true shortest-path distance from that cell to the goal (admissible).
- AC-006 [US-001] [FR-006]: Given a map with a detour where octile badly underestimates, when `Landmarks.astar` and `astar` solve it, then `Landmarks.astar` expands strictly fewer frontier nodes.

## Functional Requirements
- FR-001: `Landmarks.astar` MUST return a path whose total cost equals `astar`'s path cost for the same inputs whenever a path exists within a budget that bounds out neither search. (covers AC-001)
- FR-002: Every path `Landmarks.astar` returns MUST be valid — start-prefixed, goal-suffixed, each step a `Neighbourhood`-legal adjacent move with no corner-cutting, every cell walkable. (covers AC-002)
- FR-003: `Landmarks.astar` MUST agree with `astar` on reachability — `None` exactly when `astar` returns `None` on a search not bounded out by `maxVisited`. (covers AC-003)
- FR-004: `Landmarks.build` and `Landmarks.astar` MUST be pure and byte-deterministic across runs and platforms — landmark selection is farthest-point sampling from a fixed seed with integer distances and total tie-breaks. (covers AC-004)
- FR-005: `Landmarks.heuristic landmarks goal cell` MUST be admissible — it never exceeds the true shortest-path distance from `cell` to `goal` — so `Landmarks.astar` stays optimal. (covers AC-005)
- FR-006: `Landmarks.astar` MUST expand no more frontier nodes than plain `astar` and strictly fewer on a map where the landmark heuristic is tighter than octile, demonstrated by a benchmark. (covers AC-006)

## Ambiguities
- AMB-001: Landmark selection strategy — farthest-point (max-min) sampling from a fixed seed corner, or random/other? (Determinism requires a fixed rule.)
- AMB-002: Heuristic combination — use `max(octile, ALT)` (tighter, still admissible) or ALT alone?
- AMB-003: How many landmarks by default, and are bounds required? (The framework holds no map, so a bounded region is required to enumerate landmarks and distance tables.)

## Public Or Tool-Facing Impact
- Tier 1 (contracted). Adds a public `Pathfinding.Landmarks` type and `Landmarks.build`/`heuristic`/
  `astar` to `FS.GG.Game.Core`; refactors `astar` internally onto a shared heuristic-parameterised
  core with byte-identical output. `.fsi`, both surface baselines, and tests land together; the
  drift gate stays green. No existing signature changes.

## Lifecycle Notes
- Differential/admissibility/determinism tests MUST carry a stable filterable name for the `gate.yml`
  determinism guard.
- Next lifecycle action: `fsgg-sdd clarify --work 017-pathfinding-alt-landmarks`.
