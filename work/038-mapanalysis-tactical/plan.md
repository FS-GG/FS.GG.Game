---
schemaVersion: 1
workId: 038-mapanalysis-tactical
title: MapAnalysis Tactical Shape (Exposure, Cover, Killzones)
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/038-mapanalysis-tactical/spec.md
sourceClarifications: work/038-mapanalysis-tactical/clarifications.md
sourceChecklist: work/038-mapanalysis-tactical/checklist.md
publicOrToolFacingImpact: true
---

# MapAnalysis Tactical Shape Plan

Prose status: planned

## Source Snapshot
- spec: work/038-mapanalysis-tactical/spec.md sha256:743a5fb7ce64e7929f9824dac88d844363a71b6c55808b95be84c077deb49898 schemaVersion:1
- clarifications: work/038-mapanalysis-tactical/clarifications.md sha256:71228eb2d70d63620a54f9d56e2d2fe516e83e0135bf0a16f31cb00023e1e2d8 schemaVersion:1
- checklist: work/038-mapanalysis-tactical/checklist.md sha256:2ae3466b8e66c92785e7865e4d16bd3dc78775eb224497ec710a27cb830d4cae schemaVersion:1

## Technical Context
F#, `net10.0`, BCL-only. Extends `MapAnalysis` (`MapAnalysis.fs`/`.fsi`) with `exposureMap`/`coverMap`/
`killzones`. Tier 1: additive public surface, baselines refresh. `hasLos` is a caller-supplied oracle (no
`Los` dependency added). This is the terminal Part II milestone.

## Constitution Check
- III Public Surface: new signatures in `MapAnalysis.fsi` first; baselines additive.
- IV Idiomatic simplicity: `exposureMap`/`killzones` are straight O(V²) passes over floor-cell pairs; `hasLos`
  is a parameter, exactly as `Ai` takes its LOS oracle.
- V MUE: pure functions; results are `Map`/sorted lists.
- VI Test evidence: the exposure-symmetry property + cover-count fixtures + totality.

## Design
- `exposureMap hasLos map : Map<Cell,int>` (DEC-002) — `floorCells` row-major; each `c` mapped to the count of
  other floor cells `d` with `hasLos c d`; keyed `Map`, so enumeration order does not reach the result.
- `coverMap map : Map<Cell,int>` — each floor `c` mapped to the count of its 8 neighbouring positions that are
  `Wall` or off-map (both count as cover). Fixed offset order; keyed `Map`.
- `killzones hasLos minLength map : (Cell*Cell) list` (DEC-001) — floor-cell pairs `(a,b)` with `a < b`,
  `hasLos a b`, and Chebyshev distance `>= minLength`; emitted in `(a,b)` order (the canonical `Cell` total
  order), which is already sorted since the outer loop is row-major and `a < b`.
- Static-shape-vs-`Ai` boundary documented (geometry priors here; dynamic enemy-keyed threat in `Ai`).
- Determinism: row-major floor enumeration; `Map` keys; `killzones` in `(a,b)` order; assumes a deterministic
  `hasLos`.

## Public Surface
- `MapAnalysis.fsi` gains `exposureMap`, `coverMap`, `killzones`. Baselines gain the three members.

## Plan Scope
- Extends `MapAnalysis` with the tactical-shape layer from the covered spec. Requirement count: 5.
  Clarification decision count: 2. Checklist result count: 5. Tier 1 (additive public surface + baseline).

## Plan Decisions
- PD-001 [AC-001] [FR-001] [DEC-002] complete: `exposureMap` — per floor cell, count of other visible floor
  cells (no self).
- PD-002 [AC-002] [FR-002] complete: symmetry under a symmetric `hasLos` (property).
- PD-003 [AC-003] [FR-003] complete: `coverMap` — count of adjacent wall/off-map positions (0..8).
- PD-004 [AC-004] [FR-004] [DEC-001] complete: `killzones` — canonical `a < b` pairs, `hasLos`, Chebyshev
  `>= minLength`, sorted.
- PD-005 [AC-005] [FR-005] complete: totality — degenerate input ⇒ empty/documented; never throws.

## Contract Impact
- PC-001 [PD-001] surfaceBaseline: `FS.GG.Game.Core` surface grows by three `MapAnalysis` members; both
  baselines update in lockstep; drift shows exactly the additions.

## Verification Obligations
- VO-001 [PD-001] [PD-002] [PD-003] [PD-004] semanticTest: the exposure-symmetry property, cover-count
  fixtures, and killzone fixtures pass; build clean under warnings-as-errors.
- VO-002 [PD-005] semanticTest: totality tests pass.
- VO-003 [PC-001] semanticTest: both surface baselines byte-match the built surface (drift gate clean).

## Migration Posture
- PM-001 [PC-001] diagnoseOnly: purely additive public surface; nothing removed or renamed.

## Generated View Impact
- GV-001 [PD-001] workModel: `readiness/038-mapanalysis-tactical/work-model.json` refreshes or reports
  `staleGeneratedView`.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 038-mapanalysis-tactical`.
