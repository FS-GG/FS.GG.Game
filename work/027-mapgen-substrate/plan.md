---
schemaVersion: 1
workId: 027-mapgen-substrate
title: MapGen Substrate
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/027-mapgen-substrate/spec.md
sourceClarifications: work/027-mapgen-substrate/clarifications.md
sourceChecklist: work/027-mapgen-substrate/checklist.md
publicOrToolFacingImpact: true
---

# MapGen Substrate Plan

Prose status: planned

## Source Snapshot
- spec: work/027-mapgen-substrate/spec.md sha256:075255b7dea91dd0fa270064b388b17bbb7c7deb22f50159f74c7c8675579e4b schemaVersion:1
- clarifications: work/027-mapgen-substrate/clarifications.md sha256:bd102502b7350d0a6d35481d99f552d67821ce45e750a6d2f5748a57b19b49c2 schemaVersion:1
- checklist: work/027-mapgen-substrate/checklist.md sha256:94c173bbb42f8ffe164b538ab0c60ea362faf1f25f56853b483f5dc342222003 schemaVersion:1

## Technical Context
F#, `net10.0`, functional-first, BCL-only. A new `MapGen` module in `FS.GG.Game.Core`, compiled after
`Pathfinding` and `Grids` (it consumes `Neighbourhood`, `Cell`, `Rect`, `Rng`). Tier 1: it declares a public
surface, so `MapGen.fsi` is authored before `MapGen.fs` and both surface baselines
(`readiness/surface-baselines/FS.GG.Game.Core.txt` and `members/…`) are refreshed via
`scripts/refresh-surface-baselines.fsx`. A skeleton `fs-gg-mapgen` product skill and its manifest row land
in the same item so the capability is materializable (DEC-003).

## Constitution Check
- III Public Surface: `MapGen.fsi` is authored first and both baselines refreshed; the drift gate must be
  clean (FR-008).
- IV Idiomatic simplicity: one generic container `Grid<'T>` (DEC-004), reused `Neighbourhood`/`Cell`/`Rect`,
  no new spatial vocabulary.
- V MUE: every function is a pure value transition; `Rng` is threaded explicitly, never ambient.
- VI Test evidence: determinism + traversability property tests, fail-before/pass-after, real fixtures.
- No silent output changes: the new surface is additive; no existing signature or baseline entry changes
  except the additions.

## Design
- **Container.** `Grid<'T> = { Width; Height; Cells: 'T[] }`, row-major, index `Row*Width+Col`. `filled`,
  `inBounds`, `get : Grid<'T> -> Cell -> 'T voption`, `set : Grid<'T> -> Cell -> 'T -> Grid<'T>` (copy-on-
  write; out-of-bounds `set` returns the grid unchanged). `Tile = Wall | Floor`; `TileMap = Grid<Tile>`.
- **Regions.** `regions : Neighbourhood -> TileMap -> Region list` — a row-major scan seeds an unvisited
  Floor cell, flood-fills over the given `Neighbourhood`, and emits `Region { Id; Cells }` with `Cells`
  row-major and `Id` ascending by first-cell scan order (seed-independent). `largestRegion` folds by
  `(Cells.Length desc, Id asc)`.
- **Connect.** `connect : Neighbourhood -> Rng -> TileMap -> struct (TileMap * Rng)` — compute regions;
  target = largest; for each other region in ascending `Id`, find the nearest cell pair (min squared `Cell`
  distance, ties → smallest `(from,to)`), carve an axis-then-axis L of Floor. Threads `Rng` (reserved for
  future jitter; deterministic today). Result is one connected Floor component (FR-006).
- **Determinism.** No `Date`/hash-set iteration on the output path; the only float is none here (noise is
  M5). Byte-identity is structural equality of the returned `Grid`.

## Public Surface
- `MapGen.fsi` declaring `Grid<'T>`, `Tile`, `TileMap`, `Region`, and the `MapGen` module values above.
- Baselines: `FS.GG.Game.Core.txt` gains `Grid`1`, `Tile`(+`Tags`), `TileMap`, `Region`, `MapGen`;
  `members/FS.GG.Game.Core.txt` gains their signatures.

## Plan Scope
- Work item 027-mapgen-substrate is planned from the current specification, clarification, and checklist
  facts. Requirement count: 9. Clarification decision count: 4. Checklist result count: 9. Tier 1 (adds a
  public `MapGen` surface + `.fsi` + baselines + a skill-manifest row).

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: implement `Grid<'T>` + `filled`/`inBounds`/`get`(voption)/`set`(no-op
  OOB), all total.
- PD-002 [AC-002] [FR-002] [DEC-001] complete: `regions Neighbourhood TileMap` — row-major flood-fill,
  ascending-by-first-cell `Id`, row-major `Cells`, seed-independent labelling.
- PD-003 [AC-003] [FR-003] complete: `largestRegion` — max cells, tie → lowest `Id`, `ValueNone` when no
  Floor.
- PD-004 [AC-004] [FR-004] [DEC-001] [DEC-002] complete: `connect Neighbourhood Rng TileMap` — nearest-pair
  L-corridors joining every region to the largest, ascending `Id` order, `Rng` threaded.
- PD-005 [AC-005] [FR-005] complete: determinism property — build+connect twice per seed is byte-identical;
  seed+1 differs. Reusable, stably-named harness for M2–M5.
- PD-006 [AC-006] [FR-006] complete: traversability property — after `connect`, `Pathfinding.bfs` reaches
  every Floor cell from any Floor cell.
- PD-007 [AC-007] [FR-007] complete: totality — zero/negative dims ⇒ empty `Grid`; OOB/impossible inputs ⇒
  documented values; no throws. Property over random degenerate inputs.
- PD-008 [AC-008] [FR-008] complete: author `MapGen.fsi`; refresh both surface baselines; drift gate clean.
- PD-009 [AC-009] [FR-009] [DEC-003] complete: add `template/product-skills/fs-gg-mapgen/SKILL.md` skeleton,
  a `generate-skill-manifest.fsx` catalog row (`profile in [game, sample-pack]`, `NotMirrored`), and
  regenerate `skill-manifest.json`; `--check` passes.

## Contract Impact
- PC-001 [PD-008] surfaceBaseline: `FS.GG.Game.Core` public surface grows by the `MapGen` module and its
  types; both baselines are updated in lockstep with the `.fsi`, and the drift gate shows exactly those
  additions and nothing else.
- PC-002 [PD-009] skillManifest: `skill-manifest.json` gains the `fs-gg-mapgen` row with its SKILL.md
  sha256; the `.github` registry reconcile (`registry = manifest = bytes`) is an M6 cross-repo follow-up.

## Verification Obligations
- VO-001 [PD-001] [PD-002] [PD-003] [PD-004] semanticTest: unit tests for grid primitives, region labelling
  order, largest-region tie-break, and connect corridor carving pass; build clean under warnings-as-errors.
- VO-002 [PD-005] [PD-006] [PD-007] semanticTest: the determinism, traversability, and totality property
  suites pass (fail-before on a deliberately non-deterministic stub, pass-after).
- VO-003 [PD-008] semanticTest: `MapGen.fsi` present; both surface baselines byte-match the built surface
  (drift gate clean).
- VO-004 [PD-009] semanticTest: `dotnet fsi scripts/generate-skill-manifest.fsx --check` exits 0 with the
  `fs-gg-mapgen` row present.

## Migration Posture
- PM-001 [PC-001] diagnoseOnly: purely additive public surface; nothing removed or renamed; no consumer
  migration.

## Generated View Impact
- GV-001 [PD-001] workModel: `readiness/027-mapgen-substrate/work-model.json` refreshes from the plan sources
  or reports `staleGeneratedView`.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.
- The `Rng` parameter on `connect` is threaded but deterministic-without-jitter today; M2–M5 may draw from
  it. Keeping it in the M1 signature avoids a Tier-1 surface change later.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 027-mapgen-substrate`.
