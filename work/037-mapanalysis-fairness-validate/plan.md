---
schemaVersion: 1
workId: 037-mapanalysis-fairness-validate
title: MapAnalysis Distribution, Fairness & Validation
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/037-mapanalysis-fairness-validate/spec.md
sourceClarifications: work/037-mapanalysis-fairness-validate/clarifications.md
sourceChecklist: work/037-mapanalysis-fairness-validate/checklist.md
publicOrToolFacingImpact: true
---

# MapAnalysis Distribution, Fairness & Validation Plan

Prose status: planned

## Source Snapshot
- spec: work/037-mapanalysis-fairness-validate/spec.md sha256:f8c92d9e8988c1d83575b1a153782fe00e778442294a95e4e0fa80a262bbbd3f schemaVersion:1
- clarifications: work/037-mapanalysis-fairness-validate/clarifications.md sha256:8b5d92d895fa45a3a7833baaf00c883412e9e21b59b89c461131e4c6904db9b8 schemaVersion:1
- checklist: work/037-mapanalysis-fairness-validate/checklist.md sha256:d50697254765100a16862d0faf753673550a732c4ac50f9fb12a31338c46aeff schemaVersion:1

## Technical Context
F#, `net10.0`, BCL-only. Extends `MapAnalysis` (`MapAnalysis.fs`/`.fsi`) with `spacing`/`fairness`/`coverage`,
the `Rule`/`Report` types, and `validate`. Tier 1: additive public surface (+ two types), baselines refresh.
Reuses M9 `floorNeighbours` and M8–M10 analyses; adds a private multi-source BFS hop.

## Constitution Check
- III Public Surface: `Rule`/`Report` + the four values in `MapAnalysis.fsi` first; baselines additive.
- IV Idiomatic simplicity: `validate` composes the shipped M8–M10 analyses; a closed `Rule` DU.
- V MUE: pure functions; BFS local state only.
- VI Test evidence: good-vs-bad `validate`, reason-per-rule order, and the metrics on fixtures.

## Design
- Private `bfsHopsMulti neighbourhood map (starts: Cell list) : Dictionary<Cell,int>` — level BFS seeded with
  every floor `start` at 0 (the multi-source hop field).
- `spacing points : struct (int * float)` (DEC-001) — for each point, its nearest-other-point **Manhattan**
  distance; return `struct (min, mean)`; fewer than two points ⇒ `struct (0, 0.0)`.
- `fairness spawns resources neighbourhood map : Map<Cell,int>` (DEC-001) — `bfsHopsMulti` from the floor
  `resources`; each spawn present in the field maps to its distance; unreachable spawns omitted.
- `coverage neighbourhood map points radius : float` — `bfsHopsMulti` from the floor `points`; fraction of
  floor cells with distance ≤ `radius`; no floor / no point ⇒ 0.0.
- `type Rule = Connected | MinDiameter of int | MaxDiameter of int | MinBorderOpenings of int | MaxComponents of int` (DEC-002).
- `type Report = { Passed; Failures: string list; Connected; ComponentCount; Diameter; BorderOpenings }`.
- `validate rules neighbourhood map : Report` — compute the facts once (isConnected, componentCount, diameter,
  borderOpenings count); fold `rules` in order, appending a deterministic reason string per violation; `Passed
  = List.isEmpty Failures`.
- Determinism: points enumerated as given; BFS levels order-independent under `max`/`min`/count; rules in list
  order.

## Public Surface
- `MapAnalysis.fsi` gains `Rule`, `Report`, `spacing`, `fairness`, `coverage`, `validate`. Baselines gain the
  types + members.

## Plan Scope
- Extends `MapAnalysis` with the fairness/validation keystone from the covered spec. Requirement count: 6.
  Clarification decision count: 2. Checklist result count: 6. Tier 1 (additive public surface + baseline).

## Plan Decisions
- PD-001 [AC-001] [FR-001] [DEC-001] complete: `spacing` — min & mean nearest-neighbour Manhattan; `< 2` ⇒
  `struct (0, 0.0)`.
- PD-002 [AC-002] [FR-002] [DEC-001] complete: `fairness` — multi-source BFS from resources; spawn → nearest
  hop; unreachable omitted.
- PD-003 [AC-003] [FR-003] complete: `coverage` — fraction of floor within `radius` hops of a point; empty ⇒ 0.0.
- PD-004 [AC-004] [FR-004] [DEC-002] complete: `Rule`/`Report` + `validate`; failures in rule-list order;
  facts populated.
- PD-005 [AC-005] [FR-005] complete: good map passes, disconnected map fails with the connectivity reason.
- PD-006 [AC-006] [FR-006] complete: totality — degenerate input ⇒ documented value; never throws.

## Contract Impact
- PC-001 [PD-004] surfaceBaseline: `FS.GG.Game.Core` surface grows by `Rule`/`Report` and four `MapAnalysis`
  members; both baselines update in lockstep; drift shows exactly the additions.

## Verification Obligations
- VO-001 [PD-001] [PD-002] [PD-003] semanticTest: `spacing`/`fairness`/`coverage` on known fixtures pass;
  build clean under warnings-as-errors.
- VO-002 [PD-004] [PD-005] [PD-006] semanticTest: good-vs-bad `validate`, reason-order, and totality tests pass.
- VO-003 [PC-001] semanticTest: both surface baselines byte-match the built surface (drift gate clean).

## Migration Posture
- PM-001 [PC-001] diagnoseOnly: purely additive public surface; nothing removed or renamed.

## Generated View Impact
- GV-001 [PD-004] workModel: `readiness/037-mapanalysis-fairness-validate/work-model.json` refreshes or reports
  `staleGeneratedView`.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 037-mapanalysis-fairness-validate`.
