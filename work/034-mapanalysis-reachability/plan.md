---
schemaVersion: 1
workId: 034-mapanalysis-reachability
title: MapAnalysis Reachability & Connectivity
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/034-mapanalysis-reachability/spec.md
sourceClarifications: work/034-mapanalysis-reachability/clarifications.md
sourceChecklist: work/034-mapanalysis-reachability/checklist.md
publicOrToolFacingImpact: true
---

# MapAnalysis Reachability & Connectivity Plan

Prose status: planned

## Source Snapshot
- spec: work/034-mapanalysis-reachability/spec.md sha256:218f2cfba8ddf0d8fdeff39b3ff1a6ce0b27d6c3c68ee99c1448315bf710ae72 schemaVersion:1
- clarifications: work/034-mapanalysis-reachability/clarifications.md sha256:fa366bf409697fa8cfcb5164e2d4928b28454bac4052510b86f6f8908912f312 schemaVersion:1
- checklist: work/034-mapanalysis-reachability/checklist.md sha256:9d72838be0bc48441e00506c03c87ac1506bfd46bec5a260ab44f192713a3a86 schemaVersion:1

## Technical Context
F#, `net10.0`, BCL-only. A new `MapAnalysis` module (`MapAnalysis.fs`/`.fsi`) in `FS.GG.Game.Core`, compiled
after `MapGen` and `Pathfinding` (it consumes both). Tier 1: new public surface, both Core baselines refresh.
Tests in `Game.Core.Tests`; a `MapAnalysis` section added to the `fs-gg-mapcraft` SKILL.md.

## Constitution Check
- III Public Surface: `MapAnalysis.fsi` authored first; baselines refreshed additively.
- IV Idiomatic simplicity: reuse `Pathfinding.distanceField` and `MapGen.regions`; no second flood-fill.
- V MUE: pure functions; no ambient state.
- VI Test evidence: router-consistency + determinism + totality properties.

## Design
- `reachable neighbourhood maxVisited isWalkable start : Set<Cell>` (DEC-002) — guard `isWalkable start`
  (else `Set.empty`); `cost c = if isWalkable c then 1 else 0` (0 = impassable, `distanceField`'s convention);
  `Pathfinding.distanceField neighbourhood maxVisited cost [start] |> Map.keys |> Set.ofSeq`. This reuses the
  router's exact neighbour + no-corner-cut logic, so FR-002 holds by construction.
- `stranded neighbourhood from map : Cell list` — `isFloor c = MapGen.get map c = ValueSome Floor`; if `from`
  not floor ⇒ all floor cells; else `floorCells − reachable neighbourhood (w*h+1) isFloor from`, row-major.
- `isConnected neighbourhood map : bool` — no floor ⇒ true; else `stranded` from the first floor cell is empty.
- `componentCount neighbourhood map : int` — `MapGen.regions neighbourhood map |> List.length` (DEC-001).
- Determinism: `floorCells` row-major; `Set<Cell>` iteration is sorted (F# Set), used only for membership;
  results in fixed order.

## Public Surface
- `MapAnalysis.fsi` declaring the four values. Baselines gain `MapAnalysis` + members.

## Plan Scope
- Adds the `MapAnalysis` reachability layer from the covered spec. Requirement count: 6. Clarification decision
  count: 2. Checklist result count: 6. Tier 1 (new public surface + baseline).

## Plan Decisions
- PD-001 [AC-001] [FR-001] [DEC-002] complete: `reachable` over `distanceField`; non-walkable start ⇒ empty.
- PD-002 [AC-002] [FR-002] complete: router-consistency — `reachable` set == `bfs`-reachable cells (property).
- PD-003 [AC-003] [FR-003] complete: `stranded` — floor cells not reachable from the reference, row-major.
- PD-004 [AC-004] [FR-004] complete: `isConnected` — single floor component (empty floor ⇒ true).
- PD-005 [AC-005] [FR-005] [DEC-001] complete: `componentCount` over `MapGen.regions`; no duplicate `components`.
- PD-006 [AC-006] [FR-006] complete: totality — degenerate input ⇒ documented empty/clamped; never throws.

## Contract Impact
- PC-001 [PD-001] surfaceBaseline: `FS.GG.Game.Core` surface grows by the `MapAnalysis` module; both baselines
  update in lockstep; drift shows exactly the additions.

## Verification Obligations
- VO-001 [PD-002] [PD-003] [PD-004] [PD-005] semanticTest: router-consistency, stranded/isConnected on known
  fixtures, and componentCount-matches-regions tests pass; build clean under warnings-as-errors.
- VO-002 [PD-001] [PD-006] semanticTest: reachable correctness + totality property suites pass.
- VO-003 [PC-001] semanticTest: both surface baselines byte-match the built surface (drift gate clean).

## Migration Posture
- PM-001 [PC-001] diagnoseOnly: purely additive public surface; nothing removed or renamed.

## Generated View Impact
- GV-001 [PD-001] workModel: `readiness/034-mapanalysis-reachability/work-model.json` refreshes or reports
  `staleGeneratedView`.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 034-mapanalysis-reachability`.
