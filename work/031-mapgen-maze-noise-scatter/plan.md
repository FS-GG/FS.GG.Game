---
schemaVersion: 1
workId: 031-mapgen-maze-noise-scatter
title: MapGen Maze, Noise Heightmap and Scatter
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/031-mapgen-maze-noise-scatter/spec.md
sourceClarifications: work/031-mapgen-maze-noise-scatter/clarifications.md
sourceChecklist: work/031-mapgen-maze-noise-scatter/checklist.md
publicOrToolFacingImpact: true
---

# MapGen Maze, Noise Heightmap and Scatter Plan

Prose status: planned

## Source Snapshot
- spec: work/031-mapgen-maze-noise-scatter/spec.md sha256:b36c7c2863156e4db783db593779649cdd15525c191501e79d70b8a7a8bcd8e4 schemaVersion:1
- clarifications: work/031-mapgen-maze-noise-scatter/clarifications.md sha256:8903974bc3a1eb0d32832fab53323c19ee98a5866e2a34b70dbd91140c69e86a schemaVersion:1
- checklist: work/031-mapgen-maze-noise-scatter/checklist.md sha256:ce8bef6e7ee463cf762f0a81b812b3c5499b1fd29b4f7d74c611257c08ae9874 schemaVersion:1

## Technical Context
F#, `net10.0`, BCL-only. Extends `MapGen` (`MapGen.fs`/`.fsi`) with `maze`, `NoiseParams`, `heightField`,
`classify`, and `poissonScatter`. Tier 1: additive public surface, baselines refresh. Maze reuses the M1
traversability harness.

## Constitution Check
- III Public Surface: `NoiseParams` + the four values declared in `MapGen.fsi` first; baselines additive.
- IV Idiomatic simplicity: integer arithmetic throughout (DEC-004); reuse `Grid<'T>`/`TileMap`; the only
  float is the hashed unit-interval noise value + fixed smootherstep (DEC-002).
- V MUE: `Rng` threaded in a fixed order; no ambient state.
- VI Test evidence: determinism (all three), traversability (maze, M1 harness), min-spacing (scatter).

## Design
- **maze** `w h rng` (DEC-001): all-`Wall`, cells on the odd lattice `(2x+1,2y+1)`; recursive-backtracker
  (explicit stack), unvisited neighbours in fixed N,E,S,W order, next chosen by `Rng`; carve the between-wall
  and the neighbour cell. `w<3` or `h<3` ⇒ all-wall map. Perfect maze ⇒ single traversable floor, wall border.
- **heightField** `w h p rng` (DEC-002): `NoiseParams = { Octaves; Frequency; Persistence }`; salt drawn from
  `rng`; per cell sum `Octaves` of hashed-lattice value noise (bilinear + smootherstep) at doubling frequency
  and `Persistence` amplitude; normalise and scale to `[0,255]` `int`. `Grid<int>`.
- **classify** `table field` (DEC-003): ascending `(threshold,value)`; each cell → value of the highest
  `threshold <= height`, lowest band below all; empty table ⇒ empty grid. `Grid<'T>`.
- **poissonScatter** `mask minDist rng` (DEC-004): Bridson over the `Grid<bool>` mask; seed = first eligible
  cell row-major; active list indexed by `Rng`; integer rejection-sampled annulus candidates; accept when
  masked, unoccupied, and `>= minDist` from every sample (integer squared-distance scan). Samples in insertion
  order; threads `Rng`.
- Determinism: fixed orders, `Rng` threaded, integer arithmetic (noise's float ops are fixed and
  cross-platform); all outputs byte-identical for a seed.

## Public Surface
- `MapGen.fsi` gains `NoiseParams` and `val maze`, `val heightField`, `val classify`, `val poissonScatter`.
- Baselines gain `NoiseParams` (+ members) and the four `MapGen` values.

## Plan Scope
- Extends `MapGen` with the maze/noise/scatter primitives from the covered spec. Requirement count: 6.
  Clarification decision count: 4. Checklist result count: 6. Tier 1 (additive public surface + baseline).

## Plan Decisions
- PD-001 [AC-001] [FR-001] [DEC-001] complete: recursive-backtracker maze on the odd lattice; single
  traversable floor + wall border.
- PD-002 [AC-002] [FR-002] [DEC-002] complete: fractal value-noise `heightField` → `Grid<int>` in `[0,255]`,
  salt from `Rng`.
- PD-003 [AC-003] [FR-003] [DEC-003] complete: ascending-threshold `classify` → `Grid<'T>`; empty table ⇒
  empty grid.
- PD-004 [AC-004] [FR-004] [DEC-004] complete: Bridson `poissonScatter` over a mask; integer rejection
  candidates; min-spacing by squared distance; deterministic order.
- PD-005 [AC-005] [FR-005] complete: determinism property — maze/heightField/poissonScatter byte-identical for
  a seed (M1 harness for the maze map); differs for another.
- PD-006 [AC-006] [FR-006] complete: totality — degenerate size/params and empty table yield documented
  empty/clamped values; never throws.

## Contract Impact
- PC-001 [PD-001] surfaceBaseline: `FS.GG.Game.Core` surface grows by `NoiseParams` and the four `MapGen`
  values; both baselines update in lockstep; drift shows exactly those additions.

## Verification Obligations
- VO-001 [PD-001] [PD-002] [PD-003] [PD-004] semanticTest: unit tests for maze traversability, height range,
  classify banding, and scatter min-spacing pass; build clean under warnings-as-errors.
- VO-002 [PD-005] [PD-006] semanticTest: determinism and totality suites pass.
- VO-003 [PC-001] semanticTest: both surface baselines byte-match the built surface (drift gate clean).

## Migration Posture
- PM-001 [PC-001] diagnoseOnly: purely additive public surface; nothing removed or renamed.

## Generated View Impact
- GV-001 [PD-001] workModel: `readiness/031-mapgen-maze-noise-scatter/work-model.json` refreshes or reports
  `staleGeneratedView`.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 031-mapgen-maze-noise-scatter`.
