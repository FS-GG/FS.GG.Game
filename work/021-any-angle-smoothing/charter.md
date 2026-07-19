---
schemaVersion: 1
workId: 021-any-angle-smoothing
title: Any-Angle Path Smoothing
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

# Any-Angle Path Smoothing Charter

## Identity
Milestone M3 item 2.2 of the Red Blob Games algorithm roadmap
(`docs/reports/2026-07-19-redblobgames-algorithm-incorporation-roadmap.md`, §3.2). Adds
`Pathfinding.smooth` — a post-hoc string-pull over an `astar` path that drops intermediate waypoints
whenever there is a clear line of sight past them, so units move on straighter any-angle segments
instead of zig-zagging cell-to-cell. It layers on the existing search untouched and uses the integer
`Los` line-of-sight check, so the result stays byte-deterministic.

## Principles
- **Integer LOS, byte-identical.** The visibility test is the caller's `losClear` (the integer
  `Los.lineOfSight`, Bresenham/supercover), never a float ray — the smoothed path is byte-identical.
- **A subsequence, never longer.** `smooth` keeps a subset of the original waypoints; it never adds a
  cell and never lengthens the path.
- **Layered, not intrusive.** It takes a finished path; `astar`/`reachable` are unchanged.
- **Determinism.** The greedy keep-farthest-visible scan is a pure, deterministic function.

## Scope Boundaries
- **In:** `Pathfinding.smooth losClear path : Cell list` — a greedy string-pull keeping the farthest
  LOS-visible waypoint; every kept consecutive pair has clear LOS; a property suite (subsequence,
  LOS-clear segments, never-longer, straight-path collapse, determinism).
- **Out:** first-class Theta* (any-angle DURING search), continuous `Point` output / float rays,
  funnel smoothing over navmesh polygons, and the other M3 item (visibility polygon 2.4).

## Policy Pointers
- Honors constitution I, III (public surface), V (pure), VI (test evidence).
- Tier 1 (contracted): `.fsi`, both surface baselines, and tests land together; drift gate stays green.
- Governance pointers are optional compatibility facts, not evaluated by this command.

## Lifecycle Notes
- Determinism/property tests carry a stable filterable name for the `gate.yml` guard.
- Next lifecycle action: `fsgg-sdd specify --work 021-any-angle-smoothing`.
