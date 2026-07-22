---
schemaVersion: 1
workId: 036-mapanalysis-path-metrics
title: MapAnalysis Path & Flow Metrics
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/036-mapanalysis-path-metrics/spec.md
sourceClarifications: work/036-mapanalysis-path-metrics/clarifications.md
sourceChecklist: work/036-mapanalysis-path-metrics/checklist.md
publicOrToolFacingImpact: true
---

# MapAnalysis Path & Flow Metrics Plan

Prose status: planned

## Source Snapshot
- spec: work/036-mapanalysis-path-metrics/spec.md sha256:4e9428d85422ea74d7d52dba654aa295a47db8f05d56db598ed1fb20bb0f0295 schemaVersion:1
- clarifications: work/036-mapanalysis-path-metrics/clarifications.md sha256:0a000b80969fd98b59fe4d13eed07b9bf8b033afcff82bebcb205de8c200bbe2 schemaVersion:1
- checklist: work/036-mapanalysis-path-metrics/checklist.md sha256:9d849d67f4b58ab06feb7c306cf6e1e2c391f47e7d94ed9cabd659d80ca44e33 schemaVersion:1

## Technical Context
F#, `net10.0`, BCL-only. Extends `MapAnalysis` (`MapAnalysis.fs`/`.fsi`) with `isolation`/`diameter`. Tier 1:
additive public surface, baselines refresh. Reuses the private `floorNeighbours` from M9.

## Constitution Check
- III Public Surface: new signatures in `MapAnalysis.fsi` first; baselines additive.
- IV Idiomatic simplicity: one private `bfsHops` powers both metrics; no second distance field.
- V MUE: pure functions; local BFS state only.
- VI Test evidence: the `diameter = max isolation` property + corridor exact value + totality.

## Design
- Private `bfsHops neighbourhood map start : Dictionary<Cell,int>` — a level BFS over `floorNeighbours` from a
  `Floor` `start` (empty when `start` is not `Floor`); `dist.[c]` is the hop count.
- `isolation neighbourhood map cell : int` — `bfsHops` from `cell`; `max` of its values, or 0 when the map is
  empty/`cell` non-floor (a `max` over BFS levels is commutative, so the `Dictionary` iteration order does not
  affect the result — no determinism leak).
- `diameter neighbourhood map : int` — `floorCells map |> List.fold (fun acc c -> max acc (isolation … c)) 0`.
  O(V²) BFS-from-each; run at build/validate time, not per tick (property maps stay small). Row-major fold,
  deterministic.

## Public Surface
- `MapAnalysis.fsi` gains `isolation` and `diameter`. Baselines gain the two members.

## Plan Scope
- Extends `MapAnalysis` with the path-metric layer from the covered spec. Requirement count: 5. Clarification
  decision count: 2. Checklist result count: 5. Tier 1 (additive public surface + baseline).

## Plan Decisions
- PD-001 [AC-001] [FR-001] [DEC-001] complete: `isolation` — BFS eccentricity; non-floor/isolated ⇒ 0.
- PD-002 [AC-002] [FR-002] [DEC-001] [DEC-002] complete: `diameter` — max shortest-path hop; empty/single ⇒ 0.
- PD-003 [AC-003] [FR-003] complete: `diameter = max over floor cells of isolation` (property).
- PD-004 [AC-004] [FR-004] complete: a straight `k`-cell corridor has `diameter = k - 1` (exact fixture).
- PD-005 [AC-005] [FR-005] complete: totality — degenerate input ⇒ 0; never throws.

## Contract Impact
- PC-001 [PD-001] surfaceBaseline: `FS.GG.Game.Core` surface grows by two `MapAnalysis` members; both baselines
  update in lockstep; drift shows exactly the additions.

## Verification Obligations
- VO-001 [PD-001] [PD-002] [PD-003] [PD-004] semanticTest: the `diameter = max isolation` property, the
  corridor exact value, and fixtures pass; build clean under warnings-as-errors.
- VO-002 [PD-005] semanticTest: totality tests pass.
- VO-003 [PC-001] semanticTest: both surface baselines byte-match the built surface (drift gate clean).

## Migration Posture
- PM-001 [PC-001] diagnoseOnly: purely additive public surface; nothing removed or renamed.

## Generated View Impact
- GV-001 [PD-001] workModel: `readiness/036-mapanalysis-path-metrics/work-model.json` refreshes or reports
  `staleGeneratedView`.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 036-mapanalysis-path-metrics`.
