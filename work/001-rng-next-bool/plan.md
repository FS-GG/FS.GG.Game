---
schemaVersion: 1
workId: 001-rng-next-bool
title: Rng Next Bool
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/001-rng-next-bool/spec.md
sourceClarifications: work/001-rng-next-bool/clarifications.md
sourceChecklist: work/001-rng-next-bool/checklist.md
publicOrToolFacingImpact: true
---

# Rng Next Bool Plan

Prose status: planned

## Source Snapshot
- spec: work/001-rng-next-bool/spec.md sha256:31a85157cc0057b4d97b9317752e3e6562ff949d787447bbaf33ae909f3db8e8 schemaVersion:1
- clarifications: work/001-rng-next-bool/clarifications.md sha256:44031751d5148bac796bba0ea747ab0f849e2fdc151d421428152f723304d7e8 schemaVersion:1
- checklist: work/001-rng-next-bool/checklist.md sha256:4e1621b12245f0bd89ce548482c14e8fe7e5be2990586af0aadb85b514a54d8a schemaVersion:1

## Plan Scope
- Work item 001-rng-next-bool is planned from the current specification, clarification, and checklist facts.
- Requirement count: 1.
- Clarification decision count: 0.
- Checklist result count: 1.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: Plan requirement FR-001 through the plan command contract.

## Contract Impact
- PC-001 [PD-001] command report: fsgg-sdd plan, work/001-rng-next-bool/plan.md, and command-report JSON are tool-facing and compatibility-preserving.

## Verification Obligations
- VO-001 [PD-001] [PC-001] semanticTest: Run focused command tests, FSI/prelude evidence, and CLI smoke evidence before task generation.

## Migration Posture
- PM-001 [PC-001] diagnoseOnly: Plan schemaVersion 1 is accepted; unsupported plan schemas diagnose before write.

## Generated View Impact
- GV-001 [PD-001] workModel: readiness/001-rng-next-bool/work-model.json refreshes from current plan sources or reports staleGeneratedView.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 001-rng-next-bool`.
