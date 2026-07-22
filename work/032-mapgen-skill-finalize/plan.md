---
schemaVersion: 1
workId: 032-mapgen-skill-finalize
title: MapGen Skill Finalize
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/032-mapgen-skill-finalize/spec.md
sourceClarifications: work/032-mapgen-skill-finalize/clarifications.md
sourceChecklist: work/032-mapgen-skill-finalize/checklist.md
publicOrToolFacingImpact: true
---

# MapGen Skill Finalize Plan

Prose status: planned

## Source Snapshot
- spec: work/032-mapgen-skill-finalize/spec.md sha256:dd73171da9e1eae3c9889e1dcddd1622155f15c72e0df89e989d24e67c50bc7a schemaVersion:1
- clarifications: work/032-mapgen-skill-finalize/clarifications.md sha256:741a70fa880445c245899a62f494d73015579ebab9a5699fe6529c6ad27a5cd4 schemaVersion:1
- checklist: work/032-mapgen-skill-finalize/checklist.md sha256:ab85efef3c94d98191a2f3a69bf38ad7f5c7ba81276bdbcf83e4232bf80c7feb schemaVersion:1

## Technical Context
Documentation + manifest only; no F# source change. Rewrite `template/product-skills/fs-gg-mapgen/SKILL.md`
to the full teaching body, regenerate `template/skill-manifest/skill-manifest.json`, and record the
cross-repo reconcile deferral. The gates are `generate-skill-manifest.fsx --check` and `check-skill-refs.sh`.

## Constitution Check
- II Structured/skill contract: the manifest is the record; it is regenerated from the authored bytes.
- IV Idiomatic simplicity: the SKILL.md matches the shipped house style.
- "Derived, not restated" (ADR-0058): the manifest sha256 derives from the SKILL.md; no hand-edited digest.

## Design
- Rewrite the SKILL.md: keep the front-matter `name`/`description`; expand the body with the determinism
  contract and one worked example per family (`caves`, `bspDungeon`/`RoomGraph`, `floorLayout`/`floorSeed`,
  `maze`/`heightField`+`classify`/`poissonScatter`), the fill/router-agreement rule, and the pitfalls, in the
  `fs-gg-grids`/`fs-gg-ai` style (DEC-001).
- Regenerate the manifest (`dotnet fsi scripts/generate-skill-manifest.fsx`); the `fs-gg-mapgen` sha256
  updates to the finalized bytes; `--check` passes.
- Record the cross-repo `.github` registry reconcile as a deferral (DEC-002/DEC-003), pointing at the
  cross-repo-coordination protocol.

## Plan Scope
- Finalizes the `fs-gg-mapgen` skill and manifest from the covered spec. Requirement count: 4. Clarification
  decision count: 3. Checklist result count: 4. Tier 1 (tool-facing skill/manifest change; no code surface).

## Plan Decisions
- PD-001 [AC-001] [FR-001] [DEC-001] complete: full teaching SKILL.md — determinism contract + a worked
  example per family.
- PD-002 [AC-002] [FR-002] complete: regenerate the manifest; `--check` exits 0; `fs-gg-mapgen` sha256 matches
  the finalized bytes.
- PD-003 [AC-003] [FR-003] complete: every `[[ref]]` resolves under `check-skill-refs.sh`.
- PD-004 [AC-004] [FR-004] [DEC-002] [DEC-003] complete: record the cross-repo `.github` registry reconcile
  follow-up as a deferral.
- PD-005 [CR-005] acceptedDeferral: The cross-repo `.github` registry reconcile (the new `owner: fs-gg-game`
  `fs-gg-mapgen` row, registry = manifest = bytes) stays visible to task and evidence generation as an
  accepted deferral owned by the maintainer — it is another repo's change, filed via the
  cross-repo-coordination protocol, not made here.

## Contract Impact
- PC-001 [PD-002] skillManifest: `skill-manifest.json` `fs-gg-mapgen` sha256 changes to the finalized SKILL.md
  digest; the row identity/materializes-when are unchanged.

## Verification Obligations
- VO-001 [PD-001] [PD-002] [PD-003] semanticTest: `generate-skill-manifest.fsx --check` and
  `check-skill-refs.sh` both exit 0 against the finalized SKILL.md.
- VO-002 [PD-004] deferralRecorded: the cross-repo reconcile deferral is recorded with owner/scope/rationale.

## Migration Posture
- PM-001 [PC-001] diagnoseOnly: the skill row identity is unchanged; only its bytes/digest change; no consumer
  migration.

## Generated View Impact
- GV-001 [PD-001] workModel: `readiness/032-mapgen-skill-finalize/work-model.json` refreshes or reports
  `staleGeneratedView`.

## Accepted Deferrals
- AD-001 [DEC-003] [FR-004]: the `.github` registry `owner: fs-gg-game` `fs-gg-mapgen` row is a cross-repo
  follow-up owned by the maintainer — recorded, not dropped.
- CR-005 acceptedDeferral: The `.github` registry reconcile deferral (AD-001) remains visible to tasks and
  evidence — a cross-repo follow-up the maintainer files, recorded here so it is not silently dropped.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 032-mapgen-skill-finalize`.
