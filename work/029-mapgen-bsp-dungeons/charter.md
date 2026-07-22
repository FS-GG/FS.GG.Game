---
schemaVersion: 1
workId: 029-mapgen-bsp-dungeons
title: MapGen BSP Room-and-Corridor Dungeons
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

# MapGen BSP Room-and-Corridor Dungeons Charter

## Identity
Milestone **M3** of the procedural map generation design
(`docs/reports/2026-07-22-procedural-map-generation-design.md`, §3.2). Adds the structured-dungeon family to
the `MapGen` module: recursively partition the rectangle (BSP), place one room per leaf, join siblings with
L-corridors, and return both the `TileMap` and a `RoomGraph` of integer-addressed rooms and the corridors
between them. Depends on M1 (`027-mapgen-substrate`); extends `src/Game.Core/MapGen.fs`/`.fsi`.

## Principles
- **Deterministic, one seed in.** The partition, room placement, and corridors thread an explicit `Rng` in a
  fixed traversal order; the same seed is byte-identical for both the `TileMap` and the `RoomGraph`.
- **Structural metadata is first-class.** The `RoomGraph` (integer room ids + corridor pairs) is what a game
  places a start/exit/loot on; it is a value, deterministically ordered.
- **Reuse the substrate.** Carve over `Grid<Tile>`; guarantee connectivity via M1 `connect`; validate with
  `Pathfinding.bfs`.
- **Total.** Degenerate size or impossible params yield an empty map + empty `RoomGraph`; never throws.

## Scope Boundaries
- **In:** `BspParams`, `Room`, `RoomGraph`, `MapGen.bspDungeon`, the recursive partition + room placement +
  L-corridor join, connectivity via `connect`, a determinism + traversability property suite, and the
  surface-baseline update.
- **Out:** room-graph floors (M4), maze/noise/scatter (M5), the full teaching SKILL.md (M6), render-tier work.

## Policy Pointers
- Honors constitution I, III (surface + baseline), IV, V, VI (determinism + traversability tests), and
  "no silent output changes" (additive surface).
- Tier 1: adds public `MapGen` surface, so signatures, baseline, and tests land together.

## Lifecycle Notes
- Tests reuse the M1 `determinismHarness`/`traversabilityHarness` and carry stable filterable names.
- Next lifecycle action: `fsgg-sdd specify --work 029-mapgen-bsp-dungeons`.
