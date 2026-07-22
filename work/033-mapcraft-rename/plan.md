---
schemaVersion: 1
workId: 033-mapcraft-rename
title: Rename fs-gg-mapgen to fs-gg-mapcraft (map construction)
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/033-mapcraft-rename/spec.md
sourceClarifications: work/033-mapcraft-rename/clarifications.md
sourceChecklist: work/033-mapcraft-rename/checklist.md
publicOrToolFacingImpact: true
---

# Rename fs-gg-mapgen to fs-gg-mapcraft Plan

Prose status: planned

## Source Snapshot
- spec: work/033-mapcraft-rename/spec.md sha256:90c1e7bd758cfc506d16ba09c7841e87bccfb0da2131103868d543a278f25af4 schemaVersion:1
- clarifications: work/033-mapcraft-rename/clarifications.md sha256:dc6bd8fcc5ce8fc3ac27e29923166fa251fb2c9aefb3f3379ffdc4a89d092455 schemaVersion:1
- checklist: work/033-mapcraft-rename/checklist.md sha256:6baad55f76377366ef6ebc4784d9a5fcb0a78c6a8f2244fbb6c76559978023e8 schemaVersion:1

## Technical Context
Documentation + manifest + fixture rename only; no F# source change. `git mv` the SKILL.md and fixture,
reframe the SKILL.md, update the generator catalog id, regenerate the manifest, and comment the cross-repo
issue. Gates: `generate-skill-manifest.fsx --check`, `check-skill-refs.sh`, `typecheck-md-blocks.fsx`.

## Constitution Check
- II Skill contract: the manifest is the record; regenerated from the new bytes.
- IV Idiomatic simplicity: skill renamed, F# module names unchanged (DEC-001).
- ADR-0003 identity: retire-and-introduce before wide adoption; ADR-0058 derived-not-restated (manifest sha).

## Design
- `git mv template/product-skills/fs-gg-mapgen template/product-skills/fs-gg-mapcraft`.
- Reframe the SKILL.md: front-matter `name: fs-gg-mapcraft` + a construction-framed description; Scope
  section rewritten to produce → analyze → validate (generation = one producer; `MapAnalysis` machinery noted
  as forthcoming M8–M12). Worked examples unchanged (still over the shipped `MapGen` surface, DEC-002).
- `git mv scripts/skill-block-context/fs-gg-mapgen.fs scripts/skill-block-context/fs-gg-mapcraft.fs` (the
  fixture is keyed by skill id / doc stem).
- Update the `scripts/generate-skill-manifest.fsx` catalog row id `fs-gg-mapgen` → `fs-gg-mapcraft` and its
  source path; regenerate the manifest.
- Comment FS-GG/.github#1355 that the id is now `fs-gg-mapcraft` (DEC-003 deferral).

## Plan Scope
- Renames the skill and regenerates the manifest from the covered spec. Requirement count: 5. Clarification
  decision count: 3. Checklist result count: 5. Tier 1 (tool-facing skill-id retire+introduce; no code
  surface).

## Plan Decisions
- PD-001 [AC-001] [FR-001] [DEC-001] complete: `git mv` the SKILL.md to `fs-gg-mapcraft/`; front-matter
  `name: fs-gg-mapcraft`; `fs-gg-mapgen/` removed.
- PD-002 [AC-002] [FR-002] [DEC-002] complete: reframe the Scope to produce → analyze → validate; generation
  one producer; analysis noted forthcoming M8–M12.
- PD-003 [AC-003] [FR-003] complete: update the generator catalog id + path; regenerate the manifest;
  `--check` green with a `fs-gg-mapcraft` row and no `fs-gg-mapgen` row.
- PD-004 [AC-004] [FR-004] complete: rename the fixture to `fs-gg-mapcraft.fs`; `typecheck-md-blocks.fsx` and
  `check-skill-refs.sh` green.
- PD-005 [AC-005] [FR-005] [DEC-003] complete: comment FS-GG/.github#1355 with the `fs-gg-mapcraft` id.
- PD-006 [CR-006] acceptedDeferral: The FS-GG/.github#1355 registry-row rename (fs-gg-mapgen → fs-gg-mapcraft,
  registry = manifest = bytes) stays visible to task and evidence generation as an accepted deferral — this
  repo posts the update on the issue, but the row itself is owned and reconciled in FS-GG/.github.

## Contract Impact
- PC-001 [PD-003] skillManifest: `skill-manifest.json` retires the `fs-gg-mapgen` row and adds a
  `fs-gg-mapcraft` row (moved-SKILL.md sha256, `supplied-by` the new path); the row's scope/mirrored/
  materializes-when are otherwise unchanged.

## Verification Obligations
- VO-001 [PD-001] [PD-002] [PD-003] [PD-004] semanticTest: `generate-skill-manifest.fsx --check`,
  `check-skill-refs.sh`, and `typecheck-md-blocks.fsx` all exit 0 with the `fs-gg-mapcraft` id.
- VO-002 [PD-005] deferralRecorded: the FS-GG/.github#1355 update is recorded/posted.

## Migration Posture
- PM-001 [PC-001] diagnoseOnly: a skill-id retire+introduce; no consumer in this repo pins the id (no other
  `[[fs-gg-mapgen]]` ref). The cross-repo registry row is reconciled in FS-GG/.github (#1355).

## Generated View Impact
- GV-001 [PD-001] workModel: `readiness/033-mapcraft-rename/work-model.json` refreshes or reports
  `staleGeneratedView`.

## Accepted Deferrals
- AD-001 [DEC-003] [FR-005]: the registry row rename lands in FS-GG/.github (#1355), owned there.
- CR-006 acceptedDeferral: The FS-GG/.github#1355 registry-row rename deferral (AD-001) remains visible to
  tasks and evidence — posted on the cross-repo issue, resolved in that repo.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.
- Shipped `work/027`/`032` SDD history keeps the `fs-gg-mapgen` id — immutable record, correct at ship time.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 033-mapcraft-rename`.
