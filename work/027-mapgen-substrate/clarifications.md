---
schemaVersion: 1
workId: 027-mapgen-substrate
title: MapGen Substrate
stage: clarify
changeTier: tier1
status: needsAnswers
sourceSpec: work/027-mapgen-substrate/spec.md
publicOrToolFacingImpact: true
---

# MapGen Substrate Clarifications

## Source Specification
- work/027-mapgen-substrate/spec.md

## Clarification Questions
- **CQ-001** (AMB-001): Do `regions`/`connect` default a `Neighbourhood`, or require the caller to pass one?
- **CQ-002** (AMB-002): What is `connect`'s corridor strategy — nearest-cell-pair straight carve, or
  centroid-to-centroid L-corridor?
- **CQ-003** (AMB-003): Does the `fs-gg-mapgen` manifest row materialize now, or stay withheld until M6?
- **CQ-004** (AMB-004): Is `Grid<'T>` generic, or specialized to `Tile`?

## Answers
- CQ-001 → Require the caller to pass a `Neighbourhood`. This matches the established Core convention:
  `Pathfinding.bfs`/`astar` and `Edges` all take an explicit `Neighbourhood`, and connectivity semantics
  genuinely differ between 4- and 8-connected fills (a diagonal-only touch is connected under Eight, not
  Four). A hidden default would silently pick one and desync a generator's fill from the router that
  validates it (resolves AMB-001).
- CQ-002 → Straight carve between the **nearest cell pair** of the two regions (min squared `Cell` distance,
  ties broken by the lexicographically smallest `(fromCell, toCell)` pair), carved as an axis-then-axis
  L-segment. Nearest-pair keeps the corridor short and local — a centroid-to-centroid line can start or end
  outside either region (in a concavity) and cross a third region, which is both longer and harder to make
  deterministic. The `(Col, Row)` tie-break is the same total order `Cell` already uses for path determinism
  (resolves AMB-002).
- CQ-003 → Materialize now. The row is `scope: product`, `mirrored: false`,
  `materializes-when: profile in [game, sample-pack]`, live from M1. A skeleton SKILL.md that names the
  capability, its scope, and its determinism contract is honest, materializable guidance; M6 deepens the
  teaching body without changing the row's identity. Withholding it would make M2–M5 build against a
  capability the scaffold cannot see (resolves AMB-003).
- CQ-004 → Generic `Grid<'T>`. The design requires it: M5's `heightField` returns `Grid<int>` and `classify`
  maps `Grid<int> -> Grid<'T>`, so a `Tile`-specialized container could not carry the substrate. `TileMap`
  is the `Grid<Tile>` alias for the wall/floor families; the `.fsi` surface cost of one generic type is
  accepted and baselined here (resolves AMB-004).

## Decisions
- **DEC-001** [CQ-001] [AMB:AMB-001] [FR-002] [FR-004]: `regions` and `connect` take an explicit
  `Neighbourhood`; no default.
- **DEC-002** [CQ-002] [AMB:AMB-002] [FR-004] [FR-006]: `connect` carves an L-corridor between the nearest
  cell pair of each region and the largest, ties broken by smallest `(fromCell, toCell)`.
- **DEC-003** [CQ-003] [AMB:AMB-003] [FR-009]: The `fs-gg-mapgen` row is live from M1
  (`profile in [game, sample-pack]`, `mirrored: false`); M6 deepens the body only.
- **DEC-004** [CQ-004] [AMB:AMB-004] [FR-001] [FR-008]: `Grid<'T>` is generic; `TileMap = Grid<Tile>`; the
  generic type is baselined in `MapGen.fsi`.

## Accepted Deferrals
- None — all four ambiguities are resolved above.

## Remaining Ambiguity
- None. AMB-001 through AMB-004 are resolved by DEC-001 through DEC-004.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 027-mapgen-substrate`.
