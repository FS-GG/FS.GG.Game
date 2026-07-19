---
schemaVersion: 1
workId: 015-pathfinding-jps
title: Jump Point Search
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

# Jump Point Search Charter

## Identity
Milestone M1 item 1.1 of the Red Blob Games algorithm roadmap
(`docs/reports/2026-07-19-redblobgames-algorithm-incorporation-roadmap.md`, §2.1). Adds a
grid-specialized A* — Jump Point Search (JPS) — to `FS.GG.Game.Core.Pathfinding` that "jumps" over
runs of symmetric intermediate nodes on uniform-cost grids, expanding far fewer nodes than plain A*
while returning a **byte-identical** path. It is a drop-in acceleration beside `astar`, sharing the
same walkability predicate, neighbourhood, `maxVisited` bound, and total tie-break order.

## Principles
- **Byte-identical to `astar`.** JPS is an optimization, not a new answer: on the same input it must
  return exactly what `astar` returns. The differential oracle against the existing A* is the primary
  correctness test.
- **Determinism first.** Jump order and forced-neighbour ordering are a documented total order; no
  floats, no hash-set iteration leakage — the same guarantee the rest of `Pathfinding` carries.
- **Uniform cost only.** JPS assumes uniform-cost grids; it takes a binary `isWalkable`, never a
  `cost` function. Weighted terrain stays with `reachable`/`distanceField`.
- **Surface-first.** The `.fsi` signature is the contract; it lands with the `.fs`, the surface
  baseline, and the tests together.

## Scope Boundaries
- **In:** a new `Pathfinding.jps` entry point (FourWay/EightWay, no corner-cutting) returning
  `Cell list option`, byte-identical to `astar`; a differential property test against `astar`; an
  expansion-count reduction benchmark on open maps; `.fsi` + surface baseline update.
- **Out:** weighted-terrain JPS, JPS+ / preprocessing, any change to `astar`/`bfs`/`distanceField`
  behaviour, hex JPS (depends on 2.1), and the other M1 items (Regions 1.2, ALT 1.3, tie-break 1.4).

## Policy Pointers
- Honors constitution I (specify-before-implement), III (public surface declared, not incidental),
  V (pure transitions), and VI (test evidence mandatory — the differential oracle fails before and
  passes after).
- Tier 1 (contracted): `.fsi` signatures, `readiness/surface-baselines/FS.GG.Game.Core.txt`, and
  tests land together; the surface-baseline-drift gate stays green.
- Governance pointers are optional compatibility facts, not evaluated by this command.

## Lifecycle Notes
- Determinism/differential tests carry a stable filterable name so the `gate.yml` determinism guard
  covers them, as the existing Pathfinding goldens do.
- Next lifecycle action: `fsgg-sdd specify --work 015-pathfinding-jps`.
