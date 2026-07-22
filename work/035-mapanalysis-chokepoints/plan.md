---
schemaVersion: 1
workId: 035-mapanalysis-chokepoints
title: MapAnalysis Entrances, Exits & Chokepoints
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/035-mapanalysis-chokepoints/spec.md
sourceClarifications: work/035-mapanalysis-chokepoints/clarifications.md
sourceChecklist: work/035-mapanalysis-chokepoints/checklist.md
publicOrToolFacingImpact: true
---

# MapAnalysis Entrances, Exits & Chokepoints Plan

Prose status: planned

## Source Snapshot
- spec: work/035-mapanalysis-chokepoints/spec.md sha256:b7f254c253b82bf9d8383a986d800f2b9b016f1e34eecfd8e7e7656394b14f1d schemaVersion:1
- clarifications: work/035-mapanalysis-chokepoints/clarifications.md sha256:980a3dcb6d0758357d7804c2d5ae183b701d2c20a86a5deda2235a1ddcf69239 schemaVersion:1
- checklist: work/035-mapanalysis-chokepoints/checklist.md sha256:4750703199677807ce6a6f1086bcf6cf2db0274ea606798f3b64d011861f41bd schemaVersion:1

## Technical Context
F#, `net10.0`, BCL-only. Extends `MapAnalysis` (`MapAnalysis.fs`/`.fsi`). Tier 1: additive public surface,
baselines refresh. Tests in `Game.Core.Tests`.

## Constitution Check
- III Public Surface: new signatures in `MapAnalysis.fsi` first; baselines additive.
- IV Idiomatic simplicity: a shared private floor-neighbour helper (corner-cut-aware) feeds `deadEnds` and
  `articulationPoints`.
- V MUE: pure functions; iterative DFS uses local mutable arrays, no ambient state.
- VI Test evidence: the remove-and-recount oracle + fixtures + totality (a long corridor).

## Design
- Private `floorNeighbours neighbourhood map c : Cell list` — the in-bounds `Floor` neighbours of `c` under
  the neighbourhood, rejecting corner-cut diagonals under `EightWay` (the same rule `MapGen.regions`/the
  router use).
- `borderOpenings map : Cell list` (DEC-001) — row-major scan of `Floor` cells with `col∈{0,W-1}` or
  `row∈{0,H-1}`. No `Neighbourhood`.
- `deadEnds neighbourhood map : Cell list` — row-major `Floor` cells whose `floorNeighbours` count is 1.
- `articulationPoints neighbourhood map : Cell list` — index `Floor` cells row-major; build an int-index
  adjacency from `floorNeighbours`; run **iterative** Tarjan (explicit stack of `(node, parent, cursor)`
  frames, `disc`/`low` arrays, root-with-≥2-children rule, non-root `low[child] >= disc[u]` rule) so a long
  corridor cannot overflow the stack (FR-004). Collect flagged cells row-major (indices are row-major, so the
  output is too).
- Determinism: row-major indexing, fixed neighbour offset order, DFS visits neighbours in adjacency order.

## Public Surface
- `MapAnalysis.fsi` gains `borderOpenings`, `deadEnds`, `articulationPoints`. Baselines gain the three members.

## Plan Scope
- Extends `MapAnalysis` with the chokepoint layer from the covered spec. Requirement count: 5. Clarification
  decision count: 2. Checklist result count: 5. Tier 1 (additive public surface + baseline).

## Plan Decisions
- PD-001 [AC-001] [FR-001] [DEC-001] complete: `borderOpenings` — border `Floor` cells, row-major, no
  neighbourhood.
- PD-002 [AC-002] [FR-002] complete: `deadEnds` — `Floor` cells with one `Floor` neighbour (corner-cut-aware),
  row-major.
- PD-003 [AC-003] [FR-003] [DEC-002] complete: `articulationPoints` — iterative Tarjan; output row-major.
- PD-004 [AC-004] [FR-004] complete: iterative DFS ⇒ total on a many-thousand-cell corridor (no stack
  overflow).
- PD-005 [AC-005] [FR-005] complete: totality — degenerate maps yield documented values; never throws.

## Contract Impact
- PC-001 [PD-001] surfaceBaseline: `FS.GG.Game.Core` surface grows by three `MapAnalysis` members; both
  baselines update in lockstep; drift shows exactly the additions.

## Verification Obligations
- VO-001 [PD-001] [PD-002] [PD-003] semanticTest: fixtures (dumbbell corridor = articulation; open room = none)
  and the remove-and-recount oracle property pass; build clean under warnings-as-errors.
- VO-002 [PD-004] [PD-005] semanticTest: the long-corridor totality test and degenerate-input tests pass.
- VO-003 [PC-001] semanticTest: both surface baselines byte-match the built surface (drift gate clean).

## Migration Posture
- PM-001 [PC-001] diagnoseOnly: purely additive public surface; nothing removed or renamed.

## Generated View Impact
- GV-001 [PD-001] workModel: `readiness/035-mapanalysis-chokepoints/work-model.json` refreshes or reports
  `staleGeneratedView`.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 035-mapanalysis-chokepoints`.
