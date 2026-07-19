---
schemaVersion: 1
workId: 016-pathfinding-regions
title: Connected-Component Early-Out
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

# Connected-Component Early-Out Charter

## Identity
Milestone M1 item 1.2 of the Red Blob Games algorithm roadmap
(`docs/reports/2026-07-19-redblobgames-algorithm-incorporation-roadmap.md`, §2.2). Adds
`Pathfinding.Regions` — a value that labels each maximal walkable region of a bounded grid once by
deterministic flood fill — plus `Regions.sameComponent`, an O(1) test that rejects an unreachable
`start -> goal` before any search runs, instead of exhausting `maxVisited`.

## Principles
- **Boolean only, ids never leak.** Only `sameComponent : bool` is exposed; region label ids stay
  internal, so relabeling can never change an observable result.
- **Connectivity matches the search.** The flood fill uses the module's own neighbour rule (including
  the no-corner-cutting rule under `EightWay`), so `sameComponent a b` agrees with whether `astar`/
  `bfs` can find a path — the differential oracle against `astar`.
- **Deterministic.** A pure function of `(bounds, isWalkable, neighbourhood)` over a documented
  scan order.
- **Surface-first.** The `.fsi` is the contract; it lands with the `.fs`, the surface baseline, and
  the tests together.

## Scope Boundaries
- **In:** `Pathfinding.Regions` (opaque value) with `Regions.build` (flood fill over inclusive
  bounds) and `Regions.sameComponent` (O(1) boolean); a differential property test that
  `sameComponent` agrees with `astar` reachability; `.fsi` + surface baseline update.
- **Out:** exposing region ids/sizes, incremental/dynamic re-labelling, an unbounded region (bounds
  are required — the framework holds no map), auto-wiring the guard into `astar`/`bfs` signatures,
  and the other M1 items (JPS 1.1, ALT 1.3, tie-break 1.4).

## Policy Pointers
- Honors constitution I, III (public surface), V (pure), VI (test evidence — the differential oracle
  fails before and passes after).
- Tier 1 (contracted): `.fsi`, `readiness/surface-baselines/FS.GG.Game.Core.txt` (+ members), and
  tests land together; the surface-baseline-drift gate stays green.
- Governance pointers are optional compatibility facts, not evaluated by this command.

## Lifecycle Notes
- Determinism/differential tests carry a stable filterable name so the `gate.yml` determinism guard
  covers them.
- Next lifecycle action: `fsgg-sdd specify --work 016-pathfinding-regions`.
