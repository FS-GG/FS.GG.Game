---
schemaVersion: 1
workId: 027-mapgen-substrate
title: "MapGen Substrate — Grid, Tiles, Regions & Connectivity"
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

# MapGen Substrate — Grid, Tiles, Regions & Connectivity Charter

## Identity
Milestone **M1** of the procedural map generation design
(`docs/reports/2026-07-22-procedural-map-generation-design.md`, §2 and §6). This item lands the shared
substrate every map generator builds on: a new `MapGen` module in `FS.GG.Game.Core` with the dense
`Grid<'T>` container, the `Tile`/`TileMap`/`Region` vocabulary, total grid primitives, the flood-fill
connectivity toolkit (`regions`/`largestRegion`/`connect`), a reusable determinism property, and the
`fs-gg-mapgen` product-skill *home* (a SKILL.md skeleton + a skill-manifest row). It is the gate the four
family work items (M2–M5) depend on; it ships no family generator itself.

## Principles
- **Byte-identical determinism is the headline contract.** Everything threads an explicit `Rng`, iterates
  row-major, and labels regions by fixed row-major first-cell position — never by draw order or hash-set
  enumeration. The reusable determinism property is delivered here so every later family instantiates it.
- **Reuse the Core vocabulary, do not re-roll it.** `Cell`/`Rect` from `Primitives`, `Neighbourhood` from
  `Pathfinding`. `Grid<'T>` is the only new container; connectivity is validated by routing with
  `Pathfinding.bfs`.
- **Total, not defensive.** Degenerate inputs (zero/negative size, out-of-bounds `get`/`set`) return a
  documented value; no generator throws.
- **Core-resident, per the placement decision.** A seeded integer generator meets Core's pure/total/
  deterministic/BCL-only bar (design §1), refining the algorithm roadmap's §3.5 "flagged, not planned".

## Scope Boundaries
- **In:** `MapGen.fs` + `MapGen.fsi` in `FS.GG.Game.Core` with `Grid<'T>`, `Tile`, `TileMap`, `Region`;
  `filled`/`inBounds`/`get`/`set`; `regions`/`largestRegion`/`connect`; the reusable determinism +
  traversability test harness; the updated public-surface baseline; and the `fs-gg-mapgen` SKILL.md
  skeleton at `template/product-skills/fs-gg-mapgen/` plus its `scripts/generate-skill-manifest.fsx`
  catalog row and regenerated `skill-manifest.json`.
- **Out:** every family generator — cellular caves (M2), BSP dungeons (M3), room-graph floors (M4), maze/
  noise/scatter (M5); the full teaching SKILL.md body and cross-repo registry reconcile (M6); any render-
  tier texturing/autotiling/smoothing (stays out of Core, design §1/§5).

## Policy Pointers
- Honors constitution I (specify-before-implement), III (public surface declared — new `.fsi` + baseline),
  IV (idiomatic simplicity), V (pure transitions), VI (test evidence — determinism + traversability), and
  "no silent output changes".
- Tier 1 (contracted): introduces a public `MapGen` surface, so signatures, baseline, tests, and the skill
  skeleton land together.
- Governance pointers are optional compatibility facts, not evaluated by this command.

## Lifecycle Notes
- The determinism/traversability tests carry stable filterable names so M2–M5 can extend the same harness.
- Next lifecycle action: `fsgg-sdd specify --work 027-mapgen-substrate`.
