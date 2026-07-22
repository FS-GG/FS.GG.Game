---
schemaVersion: 1
workId: 030-mapgen-room-graph
title: MapGen Room-Graph Branching-Walk Floors
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

# MapGen Room-Graph Branching-Walk Floors Charter

## Identity
Milestone **M4** of the procedural map generation design
(`docs/reports/2026-07-22-procedural-map-generation-design.md`, §3.3) — the roguelike-dungeon-crawler spec
§4.8 generator. Adds an Isaac-style **graph of rooms on a grid**: a branching random walk from a Start room,
special-room assignment (Boss/Treasure/Shop/Secret) by the documented farthest-dead-end rule, a per-room
template id, and a `floorSeed` deriving each floor's independent-yet-reproducible seed from the run seed and
floor index. Depends on M1 (`027-mapgen-substrate`); extends `src/Game.Core/MapGen.fs`/`.fsi`.

## Principles
- **A graph, not a tile grid.** This family is a graph of rooms (`FloorRoom` on `Cell` coordinates plus
  adjacency); a game rasterizes it into a `TileMap` with its own room dimensions.
- **Deterministic, per-floor independent.** The walk threads an explicit `Rng`; `floorSeed run i` makes each
  floor of a run reproducible yet independent — the roguelike §14.1 byte-identity-for-a-seed requirement.
- **Documented special-room rule.** Boss at the farthest dead-end from Start, then the rest by descending
  distance — a rule, recorded, not an accident of iteration order.
- **Total.** Non-positive room count yields a single Start room; never throws.

## Scope Boundaries
- **In:** `RoomKind`, `FloorRoom`, `FloorLayout`, `FloorParams`, `MapGen.floorLayout`, `MapGen.floorSeed`,
  the branching walk, special-room assignment, and the surface-baseline update.
- **Out:** maze/noise/scatter (M5), the full teaching SKILL.md (M6), tile rasterization of the layout, and
  render-tier work.

## Policy Pointers
- Honors constitution I, III (surface + baseline), IV, V, VI (determinism + graph-shape tests), and
  "no silent output changes" (additive surface).
- Tier 1: adds public `MapGen` surface, so signatures, baseline, and tests land together.

## Lifecycle Notes
- Determinism tests assert byte-identity for a seed and independence across `floorSeed`s; stable filterable
  names.
- Next lifecycle action: `fsgg-sdd specify --work 030-mapgen-room-graph`.
