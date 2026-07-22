---
schemaVersion: 1
workId: 031-mapgen-maze-noise-scatter
title: MapGen Maze, Noise Heightmap and Scatter
stage: clarify
changeTier: tier1
status: needsAnswers
sourceSpec: work/031-mapgen-maze-noise-scatter/spec.md
publicOrToolFacingImpact: true
---

# MapGen Maze, Noise Heightmap and Scatter Clarifications

## Source Specification
- work/031-mapgen-maze-noise-scatter/spec.md

## Clarification Questions
- **CQ-001** (AMB-001): `maze` dims as `TileMap` tile dimensions, or maze-cell counts?
- **CQ-002** (AMB-002): `heightField` output `[0,255]` and a split noise salt?
- **CQ-003** (AMB-003): `classify` empty-table behaviour?
- **CQ-004** (AMB-004): `poissonScatter` candidate generation — trig or integer rejection?

## Answers
- CQ-001 → `width`/`height` are the **`TileMap` tile dimensions**. Maze cells sit on the odd lattice
  (`(2x+1, 2y+1)`) with wall tiles between, so the returned map is directly usable as terrain and its border
  is naturally `Wall`. A maze-cell-count signature would force every caller to compute `2n+1` and would not
  compose with the other `TileMap` generators, which all speak tile dimensions. A dimension `< 3` (no room
  for a single cell + border) yields an all-wall map (resolves AMB-001).
- CQ-002 → Heights are integers in `[0, 255]` (a byte height — enough resolution for banding, cheap to store
  and golden-test), and the noise **salt is drawn from the passed `Rng`** so the caller threads one stream;
  a worldgen caller that must not desync its sim RNG passes a `Rng.split` child (the design's separate-stream
  rule is the caller's `split`, not a hidden one here). The only float is the hashed-lattice unit-interval
  value (built like `Rng.nextFloat`) and the fixed smootherstep interpolation — no `atan2`/`sqrt`
  (resolves AMB-002).
- CQ-003 → An empty table yields an **empty grid** (`Width=0`, `Height=0`, empty `Cells`). `classify` cannot
  synthesize a `'T` with no table entry to draw from, so returning empty is the only total option that
  invents no value; a non-empty table (the real pipeline always has one) classifies normally. Documented, so
  a caller that passes `[]` gets a defined result rather than an exception (resolves AMB-003).
- CQ-004 → **Integer rejection sampling**: candidates are integer `(dx, dy)` offsets drawn uniformly in
  `[-2·minDist, 2·minDist]²` and kept only when `minDist² <= dx²+dy² <= (2·minDist)²`. This keeps the whole
  generator on integer arithmetic (no `sin`/`cos`), matching the module's "minimal float" rule, and is
  trivially cross-platform deterministic. Candidate acceptance also checks the mask and the min-distance to
  existing samples via an integer squared-distance scan (resolves AMB-004).

## Decisions
- **DEC-001** [CQ-001] [AMB:AMB-001] [FR-001]: `maze` `width`/`height` are `TileMap` tile dimensions; cells on
  the odd lattice; `< 3` yields an all-wall map.
- **DEC-002** [CQ-002] [AMB:AMB-002] [FR-002]: `heightField` heights are `[0,255]` integers; the salt is
  drawn from the passed `Rng`; the caller `split`s for a separate stream.
- **DEC-003** [CQ-003] [AMB:AMB-003] [FR-003]: an empty `classify` table yields an empty grid.
- **DEC-004** [CQ-004] [AMB:AMB-004] [FR-004]: `poissonScatter` uses integer rejection sampling in the
  annulus with an integer squared-distance min-spacing check.

## Accepted Deferrals
- None — all four ambiguities are resolved above.

## Remaining Ambiguity
- None. AMB-001 through AMB-004 are resolved by DEC-001 through DEC-004.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 031-mapgen-maze-noise-scatter`.
