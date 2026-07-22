---
schemaVersion: 1
workId: 032-mapgen-skill-finalize
title: MapGen Skill Finalize
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# MapGen Skill Finalize Specification

Prose status: specified

## User Value
A scaffolded game gets the authoritative `fs-gg-mapgen` product skill — a full teaching body covering the
determinism rules and every shipped family (caves, BSP dungeons, room-graph floors, maze/noise/scatter) —
with a regenerated, verified skill manifest. Milestone M6 (terminal), which finalizes the capability the
M1–M5 code delivered.

## Scope
- SB-001: The full `template/product-skills/fs-gg-mapgen/SKILL.md` teaching body (determinism contract +
  every family, `fs-gg-grids`/`fs-gg-ai` style), replacing the M1 skeleton.
- SB-002: The regenerated `template/skill-manifest/skill-manifest.json` whose `fs-gg-mapgen` sha256 matches
  the finalized bytes, passing `--check` and the skill-ref gate.
- SB-003: The recorded cross-repo `.github` registry reconcile follow-up (a new `owner: fs-gg-game` row).

## Non-Goals
- SB-004: No `MapGen` source or `.fsi` change (M1–M5 own the surface); no render-tier work.

## User Stories
- US-001 (P1): As a scaffolded-product author, I can read the `fs-gg-mapgen` skill and learn the determinism
  rules and how to use every map generator.
- US-002 (P1): As the build, I can regenerate the skill manifest and have it verify against the finalized
  SKILL.md bytes.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given the finalized `SKILL.md`, when it is read, then it teaches the determinism contract and each family (caves, BSP dungeons, room-graph floors, maze/noise/scatter) with usage examples over the `MapGen` surface.
- AC-002 [US-002] [FR-002]: Given the regenerated manifest, when `generate-skill-manifest.fsx --check` runs, then it exits 0 and the `fs-gg-mapgen` sha256 matches the finalized `SKILL.md` bytes.
- AC-003 [US-001] [FR-003]: Given the finalized `SKILL.md`, when `check-skill-refs.sh` runs, then every `[[ref]]` in it resolves.
- AC-004 [US-001] [FR-004]: Given the epic is complete, when the cross-repo follow-up is checked, then a recorded reconcile item exists for the `.github` registry `owner: fs-gg-game` `fs-gg-mapgen` row.

## Functional Requirements
- FR-001: The `fs-gg-mapgen` `SKILL.md` MUST be a full teaching body covering the determinism contract and each shipped family (caves, BSP dungeons, room-graph floors, maze/noise/scatter) with usage examples over the `MapGen` surface. (covers AC-001)
- FR-002: The `skill-manifest.json` MUST be regenerated so `generate-skill-manifest.fsx --check` exits 0 and the `fs-gg-mapgen` sha256 matches the finalized `SKILL.md`. (covers AC-002)
- FR-003: Every `[[ref]]` in the finalized `SKILL.md` MUST resolve under `check-skill-refs.sh`. (covers AC-003)
- FR-004: The work item MUST record the cross-repo `.github` registry reconcile follow-up for the new `owner: fs-gg-game` `fs-gg-mapgen` row. (covers AC-004)

## Ambiguities
- AMB-001: Teaching depth — a full worked example per family (matching `fs-gg-grids`/`fs-gg-ai`), or a concise API summary?
- AMB-002: The cross-repo reconcile — file a GitHub issue via the cross-repo-coordination protocol now, or record an in-repo documented follow-up the maintainer files?

## Public Or Tool-Facing Impact
- Tier 1 (tool-facing). Changes the `fs-gg-mapgen` product-skill bytes and the manifest sha256. Additive to
  the skill catalog; no code surface change.

## Lifecycle Notes
- The cross-repo reconcile is a first-class deferral (another repo owns it).
- Next lifecycle action: `fsgg-sdd clarify --work 032-mapgen-skill-finalize`.
