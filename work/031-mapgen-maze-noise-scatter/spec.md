---
schemaVersion: 1
workId: 031-mapgen-maze-noise-scatter
title: MapGen Maze, Noise Heightmap and Scatter
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# MapGen Maze, Noise Heightmap and Scatter Specification

Prose status: specified

## User Value
A generated game can produce recursive-backtracker mazes, fractal value-noise heightmaps classified into
biome/tile bands, and Poisson-disk scattered props/spawns — all byte-identically from a seed. Milestone M5,
which completes the "comprehensive general" family set on the M1 substrate.

## Scope
- SB-001: `MapGen.maze` — a recursive-backtracker perfect maze as a `TileMap`.
- SB-002: `NoiseParams`, `MapGen.heightField` (fractal value noise → `Grid<int>` heights), and
  `MapGen.classify` (ascending threshold table → `Grid<'T>`).
- SB-003: `MapGen.poissonScatter` — Bridson sampling over a `Grid<bool>` mask.
- SB-004: A determinism + traversability + shape property suite and the surface baseline.

## Non-Goals
- SB-005: No full teaching SKILL.md (M6), no tile texturing/autotiling of the height field, no render-tier work.

## User Stories
- US-001 (P1): As a game author, I can generate a perfect maze as a `TileMap` from a seed.
- US-002 (P1): As a worldgen author, I can generate a value-noise height field and classify it into tiles/biomes.
- US-003 (P1): As a game author, I can scatter props/spawns at a minimum spacing over a mask.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given a size, when `MapGen.maze` runs, then it returns a `TileMap` that is a perfect maze — a single traversable floor region with a wall border — threading the `Rng`.
- AC-002 [US-002] [FR-002]: Given a `NoiseParams` and a size, when `MapGen.heightField` runs, then it returns a `Grid<int>` of heights in `[0, 255]` computed by fractal value noise over a hashed lattice, using a salt drawn from the `Rng` (a stream separate from the caller's).
- AC-003 [US-002] [FR-003]: Given an ascending `(threshold, value)` table and a `Grid<int>`, when `MapGen.classify` runs, then each cell maps to the value of the highest threshold `<= its height` (the lowest band when below all), producing a `Grid<'T>` of the same dimensions.
- AC-004 [US-003] [FR-004]: Given a `Grid<bool>` mask and a `minDist`, when `MapGen.poissonScatter` runs, then it returns cells that are all masked-eligible and pairwise at least `minDist` apart, in a deterministic order, threading the `Rng`.
- AC-005 [US-001] [FR-005]: Given one seed, when `maze`/`heightField`/`poissonScatter` each run twice, then their outputs are byte-identical; a different seed differs.
- AC-006 [US-002] [FR-006]: Given degenerate size/params or an empty classify table, when any of the four runs, then it returns a documented empty/clamped value and never throws.

## Functional Requirements
- FR-001: `MapGen.maze` MUST return a `TileMap` that is a recursive-backtracker perfect maze — a single `bfs`-traversable floor region with a `Wall` border — threading the `Rng`. (covers AC-001)
- FR-002: `MapGen.heightField` MUST return a `Grid<int>` of heights in `[0, 255]` from fractal value noise over a hashed lattice, with its noise salt drawn from the `Rng` as a stream separate from the caller's. (covers AC-002)
- FR-003: `MapGen.classify` MUST map each `Grid<int>` cell to the value of the highest `threshold <= height` in an ascending `(threshold, value)` table (the lowest band when below all), yielding a same-dimension `Grid<'T>`. (covers AC-003)
- FR-004: `MapGen.poissonScatter` MUST return masked-eligible cells that are pairwise at least `minDist` apart, in a deterministic order, threading the `Rng`. (covers AC-004)
- FR-005: `maze`, `heightField`, and `poissonScatter` MUST be byte-identical for a seed and differ for a different seed. (covers AC-005)
- FR-006: All four MUST be total — degenerate size/params and an empty classify table yield a documented empty/clamped value; none throws. (covers AC-006)

## Ambiguities
- AMB-001: `maze` dimensions — `width`/`height` as the `TileMap` tile dimensions (maze cells on the odd lattice), or as maze-cell counts?
- AMB-002: `heightField` output — fixed `[0, 255]` integer range, and is the noise salt a `Rng.split` stream separate from the caller's `Rng`?
- AMB-003: `classify` empty-table behaviour — an empty grid, or require a non-empty table?
- AMB-004: `poissonScatter` candidate generation — trig-based annulus sampling, or integer rejection sampling (no `sin`/`cos`)?

## Public Or Tool-Facing Impact
- Tier 1 (contracted). Adds `NoiseParams` and four `MapGen` values to `FS.GG.Game.Core`; updates the surface
  baseline. Additive.

## Lifecycle Notes
- Tests reuse the M1 traversability harness and carry stable filterable names.
- Next lifecycle action: `fsgg-sdd clarify --work 031-mapgen-maze-noise-scatter`.
