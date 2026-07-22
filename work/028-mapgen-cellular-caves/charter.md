---
schemaVersion: 1
workId: 028-mapgen-cellular-caves
title: MapGen Cellular-Automata Caves
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

# MapGen Cellular-Automata Caves Charter

## Identity
Milestone **M2** of the procedural map generation design
(`docs/reports/2026-07-22-procedural-map-generation-design.md`, §3.1). Adds the first family generator to
the `MapGen` module: organic caverns by random-fill at a wall density, N smoothing passes of the classic
4-5 cellular-automata rule, then the M1 `connect` guarantee of a single traversable cavern. Depends on M1
(`027-mapgen-substrate`); extends `src/Game.Core/MapGen.fs`/`.fsi`.

## Principles
- **Deterministic, one seed in.** `caves` threads an explicit `Rng` row-major and returns the connected
  `TileMap`; the same seed is byte-identical.
- **Reuse the substrate.** Fill/smooth over `Grid<Tile>`; guarantee connectivity via M1 `connect`; validate
  by routing with `Pathfinding.bfs` (the M1 traversability harness).
- **Total.** Degenerate size or impossible params (0 passes, out-of-range wall chance) return a documented
  value; never throws.

## Scope Boundaries
- **In:** `CaveParams`, `MapGen.caves`, the 4-5 smoothing rule, connectivity via `connect`, a determinism +
  traversability property suite reusing the M1 harness, and the surface-baseline update.
- **Out:** BSP dungeons (M3), room-graph floors (M4), maze/noise/scatter (M5), the full teaching SKILL.md
  (M6). No render-tier work.

## Policy Pointers
- Honors constitution I, III (surface + baseline), IV, V, VI (determinism + traversability tests), and
  "no silent output changes" (additive surface).
- Tier 1: adds public `MapGen` surface, so signatures, baseline, and tests land together.

## Lifecycle Notes
- Tests carry stable filterable names and reuse the M1 `determinismHarness`/`traversabilityHarness`.
- Next lifecycle action: `fsgg-sdd specify --work 028-mapgen-cellular-caves`.
