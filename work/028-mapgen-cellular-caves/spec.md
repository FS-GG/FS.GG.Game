---
schemaVersion: 1
workId: 028-mapgen-cellular-caves
title: MapGen Cellular-Automata Caves
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# MapGen Cellular-Automata Caves Specification

Prose status: specified

## User Value
A generated FS.GG.UI game can produce organic cave levels — random-filled, cellular-automata-smoothed, and
guaranteed a single traversable cavern — byte-identically from a seed. Milestone M2, the first family
generator on the M1 substrate.

## Scope
- SB-001: `CaveParams` and `MapGen.caves` added to the `MapGen` module in `FS.GG.Game.Core`, over the M1
  `Grid`/`Tile`/`TileMap`/`connect` substrate.
- SB-002: The 4-5 cellular-automata smoothing rule and a single-cavern connectivity guarantee via
  `MapGen.connect`.
- SB-003: A determinism + traversability property suite reusing the M1 `determinismHarness`/
  `traversabilityHarness`, and the updated public-surface baseline.

## Non-Goals
- SB-004: No BSP dungeons (M3), room-graph floors (M4), maze/noise/scatter (M5), full teaching SKILL.md
  (M6), or render-tier work.

## User Stories
- US-001 (P1): As a game author, I can call `MapGen.caves` with a size, wall density, and smoothing count
  and get an organic, fully-traversable cavern from a seed.
- US-002 (P1): As a product consumer, I can rely on `caves` being byte-identical for a seed so a replay
  reproduces the same cavern.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given a `CaveParams` and a size, when `MapGen.caves` runs, then it returns a `TileMap` whose border is `Wall` and whose interior is random-filled at the params' wall chance then smoothed.
- AC-002 [US-001] [FR-002]: Given the smoothing rule, when a pass runs, then a cell becomes `Wall` when at least 5 of its 8 neighbours (out-of-bounds counted as wall) are wall, and `Floor` otherwise — the classic 4-5 rule.
- AC-003 [US-001] [FR-003]: Given a generated cave with any floor, when `regions` runs on it, then there is exactly one floor region (connectivity guaranteed by `connect`), and `bfs` reaches every floor cell.
- AC-004 [US-002] [FR-004]: Given one seed, when `caves` runs twice, then the two `TileMap`s are byte-identical; an incremented seed differs.
- AC-005 [US-001] [FR-005]: Given zero/negative size or impossible params (negative passes, out-of-[0,1] wall chance), when `caves` runs, then it returns a documented empty/clamped `TileMap` and never throws.

## Functional Requirements
- FR-001: `MapGen.caves` MUST return a `TileMap` with a `Wall` border and an interior random-filled at `CaveParams.WallChance` then smoothed `CaveParams.SmoothingPasses` times, threading the `Rng`. (covers AC-001)
- FR-002: The smoothing pass MUST apply the 4-5 rule — a cell is `Wall` iff at least 5 of its 8 neighbours (out-of-bounds treated as wall) are wall. (covers AC-002)
- FR-003: A generated cave with any floor MUST have exactly one floor region and be fully `bfs`-traversable (single cavern via `connect`). (covers AC-003)
- FR-004: `caves` MUST be byte-identical for a seed and differ for an incremented seed. (covers AC-004)
- FR-005: `caves` MUST be total — degenerate size and impossible params yield a documented empty/clamped `TileMap`; it never throws. (covers AC-005)

## Ambiguities
- AMB-001: Does `CaveParams` carry its own `Neighbourhood`, or is smoothing fixed at the 8-neighbour Moore neighbourhood while `connect` uses a separate one?
- AMB-002: Is the wall border enforced by forcing border cells to `Wall`, or only by treating out-of-bounds as wall during smoothing?

## Public Or Tool-Facing Impact
- Tier 1 (contracted). Adds `CaveParams` and `MapGen.caves` to `FS.GG.Game.Core`; updates the surface
  baseline. Additive.

## Lifecycle Notes
- Tests reuse the M1 harness and carry stable filterable names.
- Next lifecycle action: `fsgg-sdd clarify --work 028-mapgen-cellular-caves`.
