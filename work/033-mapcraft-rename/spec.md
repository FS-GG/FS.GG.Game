---
schemaVersion: 1
workId: 033-mapcraft-rename
title: Rename fs-gg-mapgen to fs-gg-mapcraft (map construction)
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Rename fs-gg-mapgen to fs-gg-mapcraft Specification

Prose status: specified

## User Value
Map-building agents get a product skill named for the real capability — **map construction** (produce →
analyze → validate) — instead of generation-only, with procedural generation positioned as *one producer*
under it. Milestone M7, the gate for the producer-agnostic analysis machinery (M8–M12).

## Scope
- SB-001: Retire the `fs-gg-mapgen` product-skill id; introduce `fs-gg-mapcraft` — move
  `template/product-skills/fs-gg-mapgen/SKILL.md` to `template/product-skills/fs-gg-mapcraft/SKILL.md`.
- SB-002: Reframe the SKILL.md front-matter (`name: fs-gg-mapcraft`, description) and its Scope section to the
  produce → analyze → validate pipeline; generation is one section; analysis machinery is noted as delivered
  across M8–M12.
- SB-003: Rename the typecheck fixture `scripts/skill-block-context/fs-gg-mapgen.fs` →
  `fs-gg-mapcraft.fs`; update the `scripts/generate-skill-manifest.fsx` catalog id; regenerate
  `template/skill-manifest/skill-manifest.json`.
- SB-004: Update the cross-repo registry request FS-GG/.github#1355 to the `fs-gg-mapcraft` id.

## Non-Goals
- SB-005: No `MapAnalysis` module or analysis code (M8–M12); no `MapGen` F# rename or surface change; no
  rewriting of the shipped `work/027`/`032` SDD history.

## User Stories
- US-001 (P1): As a map-building agent, I find a skill named `fs-gg-mapcraft` that frames the full
  construction pipeline, so I understand generation is one part of it.
- US-002 (P1): As the build, I can regenerate the manifest and have it verify with the `fs-gg-mapcraft` row
  and no stale `fs-gg-mapgen` row.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given the move, when the skill tree is inspected, then `template/product-skills/fs-gg-mapcraft/SKILL.md` exists, `template/product-skills/fs-gg-mapgen/` does not, and the SKILL.md front-matter `name` is `fs-gg-mapcraft`.
- AC-002 [US-001] [FR-002]: Given the SKILL.md, when its Scope is read, then it frames map construction as produce → analyze → validate with procedural generation as one producer, and notes the analysis machinery (M8–M12).
- AC-003 [US-002] [FR-003]: Given the regenerated manifest, when `generate-skill-manifest.fsx --check` runs, then it exits 0 with a `fs-gg-mapcraft` row (sha256 of the moved SKILL.md) and no `fs-gg-mapgen` row.
- AC-004 [US-001] [FR-004]: Given the renamed fixture, when `typecheck-md-blocks.fsx` and `check-skill-refs.sh` run, then every `fs-gg-mapcraft` block typechecks and every `[[ref]]` resolves.
- AC-005 [US-002] [FR-005]: Given the rename, when the cross-repo registry request is checked, then FS-GG/.github#1355 reflects the `fs-gg-mapcraft` id.

## Functional Requirements
- FR-001: The work item MUST move the SKILL.md to `template/product-skills/fs-gg-mapcraft/SKILL.md` (removing the `fs-gg-mapgen` directory) with front-matter `name: fs-gg-mapcraft`. (covers AC-001)
- FR-002: The SKILL.md Scope MUST frame map construction as produce → analyze → validate with procedural generation as one producer, noting the analysis machinery delivered across M8–M12. (covers AC-002)
- FR-003: The regenerated manifest MUST pass `generate-skill-manifest.fsx --check` with a `fs-gg-mapcraft` row and no `fs-gg-mapgen` row. (covers AC-003)
- FR-004: The renamed typecheck fixture MUST let every `fs-gg-mapcraft` `fsharp` block typecheck, and `check-skill-refs.sh` MUST resolve every `[[ref]]`. (covers AC-004)
- FR-005: The work item MUST update the cross-repo registry request FS-GG/.github#1355 to the `fs-gg-mapcraft` id. (covers AC-005)

## Ambiguities
- AMB-001: Does the F# `MapGen` module rename too, or stay `MapGen` under the `fs-gg-mapcraft` umbrella?
- AMB-002: Does M7 add analysis sections to the SKILL.md now (stubs), or only reframe the scope, with analysis sections landing per M8–M12?

## Public Or Tool-Facing Impact
- Tier 1 (tool-facing). Retires the `fs-gg-mapgen` skill id and introduces `fs-gg-mapcraft`; the manifest
  changes. No code surface change.

## Lifecycle Notes
- The cross-repo registry request update is a first-class deferral (FS-GG/.github#1355).
- Next lifecycle action: `fsgg-sdd clarify --work 033-mapcraft-rename`.
