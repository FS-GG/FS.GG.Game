---
schemaVersion: 1
workId: 033-mapcraft-rename
title: Rename fs-gg-mapgen to fs-gg-mapcraft (map construction)
stage: charter
changeTier: tier1
status: chartered
policyPointers:
  - .fsgg/sdd.yml
  - .fsgg/agents.yml
  - .fsgg/policy.yml
  - .fsgg/capabilities.yml
  - .fsgg/tooling.yml
---

# Rename fs-gg-mapgen to fs-gg-mapcraft Charter

## Identity
Milestone **M7** of the map construction & analysis design
(`docs/reports/2026-07-22-procedural-map-generation-design.md`, Part II §11) — the gate for the
producer-agnostic analysis machinery (M8–M12). Retires the generation-framed `fs-gg-mapgen` product-skill
id and introduces `fs-gg-mapcraft` (map **construction**: produce → analyze → validate, with procedural
generation as *one producer*). Reframes the SKILL.md scope, moves it and its typecheck fixture to the new
id, regenerates the manifest, and updates the cross-repo registry request (FS-GG/.github#1355). No `MapGen`
F# source change.

## Principles
- **The name is the framing.** `fs-gg-mapgen` implied generation-only; the capability is construction, and
  the analysis layer (M8–M12) is producer-agnostic — shared by procedural, authored, and agent-built maps.
- **Cheap now, dear later.** The skill shipped only in FS.GG.Game#474; renaming before it is widely consumed
  costs one manifest regen + one cross-repo comment, versus an ADR-0003 identity-permanence problem later.
- **Module names unchanged.** `MapGen` stays the *generation* module; the new `MapAnalysis` (M8–M12) is the
  *analysis* module. `fs-gg-mapcraft` is the umbrella skill over both — no churny F# rename.
- **Derived, not restated.** The manifest is regenerated from the new bytes; `--check`/`check-skill-refs`
  stay green.

## Scope Boundaries
- **In:** move `template/product-skills/fs-gg-mapgen/` → `fs-gg-mapcraft/`; front-matter `name`/description
  reframed to construction+analysis; the SKILL.md scope reframed to the produce→analyze→validate pipeline
  (generation is one section; analysis lands per M8–M12); rename `scripts/skill-block-context/fs-gg-mapgen.fs`
  → `fs-gg-mapcraft.fs`; update the `generate-skill-manifest.fsx` catalog id; regenerate the manifest;
  update the cross-repo registry request FS-GG/.github#1355.
- **Out:** the `MapAnalysis` module and any analysis code (M8–M12); any `MapGen` F# rename or surface change;
  rewriting the shipped `work/027`/`032` SDD history (immutable — named `fs-gg-mapgen` correctly at the time).

## Policy Pointers
- Honors constitution II (skill contract), IV, and "derived, not restated" (ADR-0058); ADR-0003 identity
  (retire-and-introduce, done before wide adoption).
- Tier 1 (tool-facing): retires a skill id and introduces a new one; the manifest changes.

## Lifecycle Notes
- The cross-repo registry request update is a first-class deferral (another repo owns #1355).
- Next lifecycle action: `fsgg-sdd specify --work 033-mapcraft-rename`.
