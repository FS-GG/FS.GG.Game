---
schemaVersion: 1
workId: 028-mapgen-cellular-caves
title: MapGen Cellular-Automata Caves
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/028-mapgen-cellular-caves/spec.md
sourceClarifications: work/028-mapgen-cellular-caves/clarifications.md
sourceChecklist: work/028-mapgen-cellular-caves/checklist.md
publicOrToolFacingImpact: true
---

# MapGen Cellular-Automata Caves Plan

Prose status: planned

## Source Snapshot
- spec: work/028-mapgen-cellular-caves/spec.md sha256:454fce707a843711cae96b35820fc0335562a689cd378078fa581b0a48a41ecf schemaVersion:1
- clarifications: work/028-mapgen-cellular-caves/clarifications.md sha256:5869d8d44acb1afda669027fd5e67f30b7cf162537a4f8ce668e5f6a9b7b3b83 schemaVersion:1
- checklist: work/028-mapgen-cellular-caves/checklist.md sha256:3c4aba75cb8eb58d552204478a56168174fc4b34e5d4d8ff8f529cbdb10688c3 schemaVersion:1

## Technical Context
F#, `net10.0`, BCL-only. Extends the existing `MapGen` module in `FS.GG.Game.Core` (one module, one file —
`MapGen.fs`/`.fsi`) with `CaveParams` and `caves`. Tier 1: additive public surface, so the `.fsi` grows and
both Core surface baselines refresh. Tests reuse the M1 harness in `MapGenTests.fs`.

## Constitution Check
- III Public Surface: `CaveParams` + `caves` declared in `MapGen.fsi` first; baselines refreshed additively.
- IV Idiomatic simplicity: fill/smooth are pure `Grid`→`Grid` folds; connectivity reuses M1 `connect`.
- V MUE: `Rng` threaded explicitly; no ambient state.
- VI Test evidence: determinism + traversability properties reuse the M1 harness.

## Design
- `CaveParams = { WallChance: float; SmoothingPasses: int; Neighbourhood: Neighbourhood }`.
- `caves w h p rng`: (1) clamp `w,h` to ≥0 and `WallChance` to `[0,1]`, `SmoothingPasses` to ≥0; (2)
  random-fill row-major — each cell `Wall` when `Rng.nextFloat < WallChance`, threading `rng`; (3) run the
  4-5 Moore-8 smoothing `SmoothingPasses` times (a cell is `Wall` iff ≥5 of its 8 neighbours, OOB counted as
  wall, are wall) as a pure grid→grid fold; (4) force the border to `Wall` (DEC-002); (5)
  `MapGen.connect p.Neighbourhood rng' filled` for the single-cavern guarantee; return `struct (map, rng'')`.
- Determinism: fill draws row-major from the threaded `Rng`; smoothing is a deterministic fold; `connect` is
  the M1 deterministic carve. Byte-identity is structural equality of the returned `TileMap`.

## Public Surface
- `MapGen.fsi` gains `CaveParams` and `val caves: width -> height -> CaveParams -> Rng -> struct (TileMap * Rng)`.
- Baselines gain `CaveParams` (+ members) and `MapGen.caves`.

## Plan Scope
- Extends `MapGen` with the caves family from the covered spec. Requirement count: 5. Clarification decision
  count: 2. Checklist result count: 5. Tier 1 (additive public surface + baseline).

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: `caves` random-fills at `WallChance`, smooths, forces a wall border,
  then `connect`s; threads `Rng`.
- PD-002 [AC-002] [FR-002] complete: the smoothing pass is the 4-5 Moore-8 rule, OOB counted as wall, as a
  pure grid→grid fold.
- PD-003 [AC-003] [FR-003] [DEC-001] complete: exactly one floor region and full `bfs`-traversability via
  `connect p.Neighbourhood`.
- PD-004 [AC-004] [FR-004] complete: determinism property reusing the M1 `determinismHarness` (twice-per-seed
  byte-identical; seed+1 differs).
- PD-005 [AC-005] [FR-005] complete: totality — clamp size and params; degenerate input yields an empty/
  clamped `TileMap`; never throws.

## Contract Impact
- PC-001 [PD-001] surfaceBaseline: `FS.GG.Game.Core` surface grows by `CaveParams` and `MapGen.caves`; both
  baselines update in lockstep with the `.fsi`; drift shows exactly those additions.

## Verification Obligations
- VO-001 [PD-001] [PD-002] [PD-003] semanticTest: unit tests for the border, the 4-5 rule on a known
  fixture, and single-region connectivity pass; build clean under warnings-as-errors.
- VO-002 [PD-004] [PD-005] semanticTest: the determinism (M1 harness), traversability (M1 harness), and
  totality property suites pass.
- VO-003 [PC-001] semanticTest: both surface baselines byte-match the built surface (drift gate clean).

## Migration Posture
- PM-001 [PC-001] diagnoseOnly: purely additive public surface; nothing removed or renamed.

## Generated View Impact
- GV-001 [PD-001] workModel: `readiness/028-mapgen-cellular-caves/work-model.json` refreshes or reports
  `staleGeneratedView`.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 028-mapgen-cellular-caves`.
