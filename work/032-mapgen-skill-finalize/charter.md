---
schemaVersion: 1
workId: 032-mapgen-skill-finalize
title: MapGen Skill Finalize
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

# MapGen Skill Finalize Charter

## Identity
Milestone **M6** (terminal) of the procedural map generation design
(`docs/reports/2026-07-22-procedural-map-generation-design.md`, §3.5). Turns the M1 `fs-gg-mapgen` SKILL.md
*skeleton* into the authoritative teaching body — the determinism rules and every shipped family (caves,
BSP dungeons, room-graph floors, maze/noise/scatter) — regenerates the skill manifest so its sha256 matches
the finalized bytes, and records the cross-repo `.github` registry reconcile follow-up. No F# source change.

## Principles
- **Teach the rules, not just the API.** The body follows the `fs-gg-grids`/`fs-gg-ai` style: the
  determinism contract, the fill/router agreement, and worked examples per family.
- **Derived, not restated.** The manifest is regenerated from the authored SKILL.md bytes; `--check` and
  the skill-ref gate must pass.
- **Cross-repo honesty.** The `.github` registry needs a new `owner: fs-gg-game` row (registry = manifest =
  bytes); that lives in another repo, so this item records the follow-up rather than pretending to make it.

## Scope Boundaries
- **In:** the full `template/product-skills/fs-gg-mapgen/SKILL.md` body, the regenerated
  `skill-manifest.json`, passing `--check`/`check-skill-refs`, and the recorded cross-repo reconcile
  follow-up.
- **Out:** any `MapGen` source or `.fsi` change (M1–M5 own the surface); render-tier work.

## Policy Pointers
- Honors constitution II (structured/skill contract), IV, and "derived, not restated" (ADR-0058).
- Tier 1 (tool-facing): changes the product-skill bytes and manifest sha256.

## Lifecycle Notes
- The cross-repo registry reconcile is a first-class **deferral** (another repo owns it), recorded with
  owner/scope/rationale.
- Next lifecycle action: `fsgg-sdd specify --work 032-mapgen-skill-finalize`.
