---
schemaVersion: 1
workId: 024-line-circle-templates
title: Line and Circle Templates
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/024-line-circle-templates/spec.md
sourceClarifications: work/024-line-circle-templates/clarifications.md
sourceChecklist: work/024-line-circle-templates/checklist.md
publicOrToolFacingImpact: true
---

# Line and Circle Templates Plan

Prose status: planned

## Source Snapshot
- spec: work/024-line-circle-templates/spec.md sha256:5d9c51aa299be907ce51b66b3a8a109859259716bb64d4001eff972c01da7d93 schemaVersion:1
- clarifications: work/024-line-circle-templates/clarifications.md sha256:ad40af7ab6b614e13c7daf4290c84c826cd7837d6102873514f424fb28fc6b59 schemaVersion:1
- checklist: work/024-line-circle-templates/checklist.md sha256:18efe47200b6eb8405929dc7f131c64911a6d6adb969d5f8998851cca238b060 schemaVersion:1

## Technical Context
F# / extend `FS.GG.Game.Core.Grids` with `disc` and `ring`. Integer arithmetic; `ring` is a midpoint
circle; line templates reuse the shipped `Los.line`. No new module.

## Constitution Check
- III (public surface): declare `Grids.disc`/`ring` in the `.fsi`; update both baselines.
- IV/V (idiomatic, pure): plain integer set construction.
- VI (test evidence): the property suite fails before, passes after.

## Plan Scope
- Add `Grids.disc`/`ring`. Requirement count: 4. Clarification decision count: 3. Checklist result count: 4.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: `disc center radius` = row-major scan over the bounding box, keeping cells with `int64 dc*dc + int64 dr*dr <= int64 radius*radius` (DEC-003, overflow-safe). radius 0 => [center]; negative => [].
- PD-002 [AC-002] [FR-002] complete: `ring center radius` = integer midpoint-circle algorithm emitting the 8-way symmetric octant points into a set, then sorted by (Col,Row) (DEC-001/DEC-002). radius 0 => [center]; negative => [].
- PD-003 [AC-003] [FR-003] complete: both are pure integer functions with a fixed emission order; the squared-distance test is int64 (overflow-safe); determinism goldens pin byte-identity.
- PD-004 [AC-004] [FR-004] complete: line templates reuse `Los.line` unchanged; a test exercises `Los.line` contiguity to confirm the primitive. No duplicate line function is added.

## Contract Impact
- PC-001 [PD-001] command report: Tier-1 public surface additions — `Grids.disc` and `Grids.ring`. Both surface baselines gain the two members with the `.fsi` and tests; the drift gate stays green. No existing signature changes.

## Verification Obligations
- VO-001 [PD-001] [PD-002] [PC-001] semanticTest: a property suite — disc membership (exactly the squared-distance set; matches a brute-force enumeration), disc/ring totality (radius 0, negative), ring-on-boundary (every ring cell at rounded distance = radius), determinism, and Los.line contiguity. Fails before, passes after; build clean; both baselines regenerated.
- VO-002 [PD-003] semanticTest: named `determinism` goldens for disc/ring and the overflow-safe large-radius case.

## Migration Posture
- PM-001 [PC-001] diagnoseOnly: Plan schemaVersion 1 accepted; purely additive, so no consumer migration.

## Generated View Impact
- GV-001 [PD-001] workModel: `readiness/024-line-circle-templates/work-model.json` refreshes or reports `staleGeneratedView`.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 024-line-circle-templates`.
