---
schemaVersion: 1
workId: 021-any-angle-smoothing
title: Any-Angle Path Smoothing
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Any-Angle Path Smoothing Specification

Prose status: specified

## User Value
Game-logic code can call `Pathfinding.smooth` on a finished `astar` path to drop the intermediate
waypoints wherever there is a clear line of sight past them — turning a jagged cell-by-cell staircase
into a shorter run of straight any-angle segments — so units move naturally. It reuses the integer
`Los` line-of-sight check (Bresenham/supercover), so the smoothed path is byte-deterministic, and it
layers on top of the existing search without changing it.

## Scope
- SB-001: A `Pathfinding.smooth losClear path : Cell list` — a greedy string-pull that keeps the
  farthest LOS-visible waypoint, taking a caller-supplied integer `losClear : Cell -> Cell -> bool`.
- SB-002: A property suite: subsequence, LOS-clear consecutive segments, never-longer, straight-path
  collapse to `[start; goal]`, totality, and determinism.

## Non-Goals
- SB-003: No first-class Theta* (any-angle *during* search), no continuous `Point`/float-ray output,
  no navmesh funnel smoothing, and none of the other M3 item (visibility polygon 2.4).

## User Stories
- US-001 (P1): As game-logic code, I can smooth an `astar` path into straighter any-angle segments by
  dropping waypoints that a clear line of sight lets me skip.
- US-002 (P1): As game-logic code, I can rely on `smooth` being pure, total, and byte-deterministic
  (integer LOS), and on every kept segment being genuinely LOS-clear.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given a path and a `losClear` predicate, when `smooth` is called, then it returns a subsequence of the path that begins at the original start and ends at the original goal.
- AC-002 [US-002] [FR-002]: Given the smoothed path, then every consecutive pair `(a, b)` in it satisfies `losClear a b` (each any-angle segment is genuinely clear).
- AC-003 [US-001] [FR-003]: Given any path, when `smooth` is called, then the result is never longer than the input, and a path all of whose cells are mutually LOS-clear from the start collapses to `[start; goal]`.
- AC-004 [US-002] [FR-004]: Given a degenerate path (empty or single cell), when `smooth` is called, then it returns the path unchanged; identical inputs always yield a byte-identical result.
- AC-005 [US-002] [FR-005]: Given `losClear = Los.lineOfSight isTransparent`, when `smooth` runs on a valid A* path over the same transparency, then the smoothed path's segments never require crossing a non-transparent cell (the smoothing is safe).

## Functional Requirements
- FR-001: `smooth losClear path` MUST return a subsequence of `path` (order preserved, no cell added) that keeps the original first and last cells. (covers AC-001)
- FR-002: Every consecutive pair `(a, b)` in the smoothed path MUST satisfy `losClear a b`. (covers AC-002)
- FR-003: The smoothed path MUST never be longer than the input, and MUST collapse to `[start; goal]` when `losClear start goal` (and more generally whenever the whole path is visible from the start). (covers AC-003)
- FR-004: `smooth` MUST be pure and total — an empty path yields empty, a single-cell path yields itself — and byte-deterministic (the greedy keep-farthest-visible scan over the integer predicate). (covers AC-004)
- FR-005: With `losClear = Los.lineOfSight isTransparent`, every kept segment MUST be LOS-clear over that transparency, so straight-line movement between consecutive kept cells never crosses a non-transparent cell. (covers AC-005)

## Ambiguities
- AMB-001: Result form — a `Cell list` subsequence of waypoints (integer, deterministic), or a continuous `Point list`?
- AMB-002: Smoothing strategy — greedy keep-farthest-visible string-pull, or an optimal funnel?
- AMB-003: LOS source — a caller-supplied `losClear` predicate (any `Los` mode / transparency), or fixed to a specific `Los` function?

## Public Or Tool-Facing Impact
- Tier 1 (contracted). Adds a public `Pathfinding.smooth` signature to `FS.GG.Game.Core` — so the
  `.fsi`, both surface baselines, and tests land together, and the drift gate must stay green. No
  existing signature changes.

## Lifecycle Notes
- Determinism/property tests MUST carry a stable filterable name for the `gate.yml` guard.
- Next lifecycle action: `fsgg-sdd clarify --work 021-any-angle-smoothing`.
