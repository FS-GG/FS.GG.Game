---
schemaVersion: 1
workId: 031-mapgen-maze-noise-scatter
title: MapGen Maze, Noise Heightmap and Scatter
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

# MapGen Maze, Noise Heightmap and Scatter Charter

## Identity
Milestone **M5** of the procedural map generation design
(`docs/reports/2026-07-22-procedural-map-generation-design.md`, §3.4). Completes "comprehensive general" with
three primitives on the `MapGen` module: a recursive-backtracker **maze**, a fractal value-noise
**heightField** plus a threshold **classify** (the sandbox-survival worldgen first stage), and a Bridson
**poissonScatter** for props/spawns. Depends on M1 (`027-mapgen-substrate`); extends
`src/Game.Core/MapGen.fs`/`.fsi`.

## Principles
- **Deterministic, one seed in.** Every generator threads an explicit `Rng` in a fixed order and is
  byte-identical for a seed. Value noise draws its salt as a split stream so worldgen cannot desync a sim.
- **Integer output, minimal float.** `heightField` returns `Grid<int>` heights; the only float is the
  hashed-lattice unit-interval value and the fixed interpolation — no `atan2`/`sqrt` on the path.
- **Reuse the substrate.** Maze produces a `TileMap` validated by the M1 traversability harness; `classify`
  maps `Grid<int> -> Grid<'T>` over the generic `Grid`.
- **Total.** Degenerate size/params and an empty classify table yield a documented value; never throws.

## Scope Boundaries
- **In:** `MapGen.maze`; `NoiseParams`, `MapGen.heightField`, `MapGen.classify`; `MapGen.poissonScatter`;
  determinism + traversability + shape property suites; the surface-baseline update.
- **Out:** the full teaching SKILL.md (M6), tile texturing/autotiling of the height field, and render-tier work.

## Policy Pointers
- Honors constitution I, III (surface + baseline), IV, V, VI (determinism + property tests), and
  "no silent output changes" (additive surface).
- Tier 1: adds public `MapGen` surface, so signatures, baseline, and tests land together.

## Lifecycle Notes
- Maze reuses the M1 `traversabilityHarness`; noise/scatter assert byte-identity for a seed; stable filterable
  names.
- Next lifecycle action: `fsgg-sdd specify --work 031-mapgen-maze-noise-scatter`.
