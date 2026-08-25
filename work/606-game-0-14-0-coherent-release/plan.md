---
schemaVersion: 1
workId: 606-game-0-14-0-coherent-release
title: Game 0.14.0 coherent runtime release
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/606-game-0-14-0-coherent-release/spec.md
sourceClarifications: work/606-game-0-14-0-coherent-release/clarifications.md
sourceChecklist: work/606-game-0-14-0-coherent-release/checklist.md
publicOrToolFacingImpact: true
---

# Game 0.14.0 coherent runtime release Plan

Prose status: planned

## Source Snapshot
- spec: work/606-game-0-14-0-coherent-release/spec.md sha256:a6ba355ab7517e1dc1036e50f401cfeeb29c2f3f0aadf600c10256335ecd270a schemaVersion:1
- clarifications: work/606-game-0-14-0-coherent-release/clarifications.md sha256:5fba1293e8518a6563d18b20383c4fe73c4eec34fe99c4a9d0d90fcb7708504f schemaVersion:1
- checklist: work/606-game-0-14-0-coherent-release/checklist.md sha256:0cd948719aa957045732f8563216c35bdebd160ea2d8819a5a8403d034211521 schemaVersion:1

## Plan Scope
- Work item 606-game-0-14-0-coherent-release is planned from the current specification, clarification, and checklist facts.
- Requirement count: 4.
- Clarification decision count: 3.
- Checklist result count: 4.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: Build a clean temporary consumer against the locally prepared package directory and compile the three named Journey members so source presence cannot substitute for package usability.
- PD-002 [AC-002] [FR-002] complete: Change only the repository-wide version scalar to 0.14.0, then run locked restore, solution build/test, public-surface comparison against downloaded 0.13.0 baselines, and one coherent pack producing exactly three package identities.
- PD-003 [AC-003] [FR-003] complete: Reuse the existing producer-owned release workflow; statically and executably verify that its verify job gates one pack and ordered dual-feed pushes of the same artifact directory. The candidate PR declares, but does not execute, tag and publication obligations.
- PD-004 [AC-004] [FR-004] complete: Record exact package hashes, nuspec repository commits, and clean-consumer evidence in tracked readiness artifacts; require post-merge operators to verify both feeds before registry reconciliation and downstream dispatch.

## Contract Impact
- PC-001 [PD-001] [PD-002] package: The public Core/Render/Harness package set advances from 0.13.0 to additive-compatible 0.14.0 while preserving one version scalar and the existing package identities.
- PC-002 [PD-003] release: `v0.14.0` will identify the accepted merged source; one workflow-prepared artifact set is the authority for both feeds.

## Verification Obligations
- VO-001 [PD-001] [PC-001] packageConsumer: A clean temporary project restores local `FS.GG.Game.Harness` 0.14.0 (and its coherent Core dependency) and compiles the three named Journey APIs.
- VO-002 [PD-002] [PC-001] releaseGate: Locked restore, Release build, full tests, public API compatibility versus 0.13.0, exact package inventory, and nuspec version/repository metadata all pass.
- VO-003 [PD-003] [PC-002] workflowTopology: The release workflow has one verification dependency, one coherent pack, org-feed-first ordering, public-feed second ordering, and no re-pack between pushes.
- VO-004 [PD-004] [PC-002] postMerge: The delivery declaration binds tag, dual-feed byte verification, registry reconciliation, and consumer dispatch obligations to the exact candidate head.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] additive: Existing 0.13.0 consumers remain valid; 0.14.0 opt-in is additive and requires no source migration.

## Generated View Impact
- GV-001 [PD-001] workModel: Refresh the SDD work model, analysis, evidence, verification, ship, and generated agent guidance after implementation evidence is final.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 606-game-0-14-0-coherent-release`.
