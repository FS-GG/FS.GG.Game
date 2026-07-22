---
schemaVersion: 1
workId: 027-mapgen-substrate
title: MapGen Substrate
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# MapGen Substrate Specification

Prose status: specified

## User Value
A generated FS.GG.UI game gets the shared, deterministic substrate every map generator reuses: a dense
tile grid it can build and address, a flood-fill toolkit that finds and connects regions, and a
seed-reproducibility guarantee — plus the `fs-gg-mapgen` product-skill home so the capability is
materializable into a scaffold. Milestone M1 of the procedural map generation design; it ships the
substrate, not the family generators (M2–M5).

## Scope
- SB-001: A new `MapGen` module in `FS.GG.Game.Core` with the dense `Grid<'T>` container, the
  `Tile`/`TileMap`/`Region` vocabulary, total grid primitives (`filled`/`inBounds`/`get`/`set`), and the
  connectivity toolkit (`regions`/`largestRegion`/`connect`), all pure/total/BCL-only over `Rng`, `Cell`,
  `Rect`, and `Pathfinding.Neighbourhood`.
- SB-002: A reusable determinism + traversability test harness under `tests/Game.Core.Tests`, named so
  M2–M5 extend it.
- SB-003: The public-surface contract — `MapGen.fsi` and the updated surface baseline.
- SB-004: The `fs-gg-mapgen` product-skill home — a SKILL.md skeleton at
  `template/product-skills/fs-gg-mapgen/`, a `scripts/generate-skill-manifest.fsx` catalog row, and the
  regenerated `template/skill-manifest/skill-manifest.json`.

## Non-Goals
- SB-005: No family generator (cellular caves, BSP dungeons, room-graph floors, maze/noise/scatter) — those
  are M2–M5. No full teaching SKILL.md body or cross-repo registry reconcile (M6). No render-tier
  texturing/autotiling/smoothing (stays out of Core).

## User Stories
- US-001 (P1): As a map-generator author (M2–M5), I can build and address a dense tile grid and find/connect
  its regions from a seed, so every family reuses one substrate instead of re-rolling containers and
  connectivity.
- US-002 (P1): As a scaffolded-product consumer, I can rely on generation being byte-identical for a seed,
  so a replayed or shared run reproduces the same map.
- US-003 (P2): As a scaffold, I can materialize the `fs-gg-mapgen` skill because it has a manifest row and
  SKILL.md, so the capability is discoverable before its teaching body is finalized.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given `filled w h v`, when a caller reads an in-bounds cell with `get` and an out-of-bounds cell, then in-bounds returns `ValueSome v` and out-of-bounds returns `ValueNone`, and `set` on an out-of-bounds cell returns the grid unchanged.
- AC-002 [US-001] [FR-002]: Given a `TileMap` with several disjoint Floor components, when `regions` runs, then it returns one `Region` per component with `Cells` in row-major order and `Id`s ascending by each region's row-major first cell — the same labelling regardless of the seed that produced the map.
- AC-003 [US-001] [FR-003]: Given a `TileMap` with regions of different sizes, when `largestRegion` runs, then it returns the region with the most cells, ties broken by lowest `Id`, and `ValueNone` when there is no Floor.
- AC-004 [US-001] [FR-004]: Given a `TileMap` with multiple Floor regions, when `connect` runs, then the result has every former region joined to the largest by carved Floor corridors and threads the `Rng` through.
- AC-005 [US-002] [FR-005]: Given one seed, when the substrate builds and connects a map twice, then the two `Grid` values are byte-identical; when the seed is incremented, the result differs.
- AC-006 [US-002] [FR-006]: Given a `connect`ed `TileMap`, when `Pathfinding.bfs` searches from any Floor cell, then every other Floor cell is reachable (a single connected component).
- AC-007 [US-001] [FR-007]: Given zero or negative dimensions or an impossible input, when any substrate function runs, then it returns a documented empty/clamped value and never throws.
- AC-008 [US-001] [FR-008]: Given the build, when the public-surface baseline is checked, then `MapGen.fsi` is present and the surface baseline reflects it exactly (the drift gate is clean).
- AC-009 [US-003] [FR-009]: Given the skill pipeline, when `generate-skill-manifest.fsx --check` runs, then the `fs-gg-mapgen` row is present with its SKILL.md sha256 and the manifest is up to date.

## Functional Requirements
- FR-001: `MapGen` MUST provide `filled`/`inBounds`/`get`/`set` over a `Grid<'T>` where `get` returns `voption`, `set` on an out-of-bounds cell is a no-op, and all four are total. (covers AC-001)
- FR-002: `MapGen.regions` MUST flood-fill the Floor cells into connected components (over a caller-supplied `Neighbourhood`) with `Cells` in row-major order and `Id`s ascending by each region's row-major first-cell position, independent of the generating seed. (covers AC-002)
- FR-003: `MapGen.largestRegion` MUST return the largest region (ties broken by lowest `Id`) or `ValueNone` when there is no Floor. (covers AC-003)
- FR-004: `MapGen.connect` MUST carve Floor corridors joining every region to the largest so the result is a single connected floor, threading the `Rng`, in a deterministic region and tie-break order. (covers AC-004)
- FR-005: The substrate MUST be byte-identical when run twice from one seed, and MUST produce a different result for an incremented seed. (covers AC-005)
- FR-006: After `connect`, every Floor cell MUST be reachable from every other Floor cell under `Pathfinding.bfs` on the same `Neighbourhood`. (covers AC-006)
- FR-007: Every substrate function MUST be total — zero/negative dimensions yield an empty grid, out-of-bounds and impossible inputs return documented values, and none throws. (covers AC-007)
- FR-008: The work item MUST declare the public surface — add `MapGen.fsi` and update the surface baseline so the drift gate is clean. (covers AC-008)
- FR-009: The work item MUST add the `fs-gg-mapgen` skill home — a SKILL.md skeleton, a manifest catalog row, and a regenerated `skill-manifest.json` — such that `generate-skill-manifest.fsx --check` passes. (covers AC-009)

## Ambiguities
- AMB-001: Should `regions`/`connect` default a `Neighbourhood`, or require the caller to pass one (matching `Pathfinding`/`Edges`)?
- AMB-002: What is `connect`'s corridor strategy — a straight carve between the nearest cell pair of the two regions, or a centroid-to-centroid L-corridor?
- AMB-003: Does the `fs-gg-mapgen` manifest row materialize now (live from M1), or stay listed-but-withheld until the teaching body lands in M6?
- AMB-004: Is `Grid<'T>` generic (any tile payload) or specialized to `Tile`, given the F# `.fsi` surface-baseline cost of a generic type?

## Public Or Tool-Facing Impact
- Tier 1 (contracted). Adds a public `MapGen` module + `.fsi` to `FS.GG.Game.Core`, updates the surface
  baseline, and adds a `fs-gg-mapgen` product-skill row to the skill manifest. Signatures, baseline, tests,
  and the skill skeleton land together.

## Lifecycle Notes
- The determinism/traversability tests MUST carry stable filterable names so M2–M5 extend the same harness.
- Next lifecycle action: `fsgg-sdd clarify --work 027-mapgen-substrate`.
