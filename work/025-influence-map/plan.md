---
schemaVersion: 1
workId: 025-influence-map
title: Influence-Map Recipe
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/025-influence-map/spec.md
sourceClarifications: work/025-influence-map/clarifications.md
sourceChecklist: work/025-influence-map/checklist.md
publicOrToolFacingImpact: true
---

# Influence-Map Recipe Plan

Prose status: planned

## Source Snapshot
- spec: work/025-influence-map/spec.md sha256:c029297d6248e966727a57b7b11f05deda1b35cb2c8664ac5ce5cf1c3ffd3cde schemaVersion:1
- clarifications: work/025-influence-map/clarifications.md sha256:83f0074ebd5ec6d56bf3779d636bcb6dd2c0d15a4beec945066e91d2083c753f schemaVersion:1
- checklist: work/025-influence-map/checklist.md sha256:8f2e34020b5a5f38640e3f11afc7e68ab49853a5d254ef9f644e7eec89c8fad7 schemaVersion:1

## Technical Context
F# / extend `FS.GG.Game.Core.Ai` with `influenceMap`, a thin wrapper composing per-source
`Pathfinding.distanceField` calls. Integer, deterministic. No new engine.

## Constitution Check
- III (public surface): declare `Ai.influenceMap` in the `.fsi`; update both baselines.
- V (pure): a pure composition over distanceField.
- VI (test evidence): the property suite fails before, passes after.

## Plan Scope
- Add `Ai.influenceMap`. Requirement count: 4. Clarification decision count: 3. Checklist result count: 4.

## Plan Decisions
- PD-001 [AC-001] [AC-003] [FR-001] [FR-003] complete: `influenceMap neighbourhood maxVisited cost (sources:(Cell*int) list)` folds over sources; for each `(s, strength)` computes `Pathfinding.distanceField neighbourhood maxVisited cost [s]` and adds `max(0, strength - dist)` where positive (DEC-001/DEC-002). Empty sources => empty map. Inherits distanceField's cost/maxVisited/determinism.
- PD-002 [AC-002] [FR-002] complete: contributions combine by `max` (DEC-003) — a cell keeps the largest source contribution; zero-influence cells stay absent.
- PD-003 [AC-004] [FR-004] complete: the tension recipe (`friendly - enemy` per cell over the two influence maps, defaulting absent to 0) is documented on `influenceMap` and demonstrated by a test.
- PD-004 [FR-003] complete: totality/determinism — integer arithmetic, deterministic Map folds in ascending key order, inherits distanceField totality (empty/degenerate sources, maxVisited bound).

## Contract Impact
- PC-001 [PD-001] command report: Tier-1 public surface addition — `Ai.influenceMap`. Both surface baselines gain the member with the `.fsi` and tests; the drift gate stays green. No existing signature changes.

## Verification Obligations
- VO-001 [PD-001] [PD-002] [PC-001] semanticTest: a property suite — single-source linear falloff (source = strength, falls 1 per baseStep, absent <=0), multi-source max combine (against a brute-force per-cell max), distanceField consistency, tension recipe, and totality. Fails before, passes after; build clean; both baselines regenerated.
- VO-002 [PD-004] semanticTest: a named `determinism` golden asserting influenceMap is byte-identical across runs over random source sets.

## Migration Posture
- PM-001 [PC-001] diagnoseOnly: Plan schemaVersion 1 accepted; purely additive, so no consumer migration.

## Generated View Impact
- GV-001 [PD-001] workModel: `readiness/025-influence-map/work-model.json` refreshes or reports `staleGeneratedView`.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 025-influence-map`.
