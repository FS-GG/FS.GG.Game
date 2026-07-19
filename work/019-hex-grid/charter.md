---
schemaVersion: 1
workId: 019-hex-grid
title: Hexagonal Grid Module
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

# Hexagonal Grid Module Charter

## Identity
Milestone M2 item 2.1 of the Red Blob Games algorithm roadmap
(`docs/reports/2026-07-19-redblobgames-algorithm-incorporation-roadmap.md`, §3.1) — the single biggest
capability hole, since the library is square-grid only today. Adds a new `Hex` module: cube/axial
integer coordinates with distance, neighbours, arithmetic, rotation, range/ring/spiral, line drawing,
rounding, and offset/doubled converters, plus hex pathfinding (`Hex.astar`/`Hex.bfs`) that proves the
search works over a hex neighbour set.

## Principles
- **Integer and total.** Cube coordinates are integers with the invariant `q + r + s = 0`; every
  operation is integer and total. The one float touch-point (`round`, used by `lineDraw`) carries a
  documented tie-break so it is byte-deterministic; pixel↔hex conversion stays OUT of Core (render
  adapter), the float boundary the roadmap flags.
- **Cube for algorithms, offset/doubled for storage.** Algorithms run in cube; converters let a
  rectangular map store hexes in offset or doubled form and convert at the boundary.
- **Determinism.** Enumeration orders (neighbours, ring, spiral, line) are fixed and documented; hex
  pathfinding inherits the same total-order frontier discipline as the square `Pathfinding`.
- **Self-contained.** A new `Hex.fs`/`.fsi`; no change to the shipped square `Pathfinding`/`Grids`.

## Scope Boundaries
- **In:** a `Hex` cube type + constructor; `add`/`subtract`/`scale`/`neighbours`/`distance`/`rotateLeft`
  /`rotateRight`/`range`/`ring`/`spiral`/`lineDraw`/`round`; offset (`toOffset`/`ofOffset`) and doubled
  (`toDoubled`/`ofDoubled`) converters; `Hex.astar`/`Hex.bfs` over hex adjacency; a full property suite.
- **Out:** pixel↔hex / hex↔pixel conversion (render adapter float boundary), hex JPS/ALT/edges
  (later), changing the square `Pathfinding`/`Grids` surface, and the other M2 item (edges/vertices 2.3).

## Policy Pointers
- Honors constitution I, III (public surface), IV (idiomatic simplicity), V (pure), VI (test evidence).
- Tier 1 (contracted): new `.fsi`, surface baselines, and tests land together; drift gate stays green;
  the new file is added to the `FS.GG.Game.Core` compile order.
- Governance pointers are optional compatibility facts, not evaluated by this command.

## Lifecycle Notes
- Determinism/property tests carry a stable filterable name for the `gate.yml` guard.
- Next lifecycle action: `fsgg-sdd specify --work 019-hex-grid`.
