---
schemaVersion: 1
workId: 030-mapgen-room-graph
title: MapGen Room-Graph Branching-Walk Floors
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# MapGen Room-Graph Branching-Walk Floors Specification

Prose status: specified

## User Value
A generated roguelike can produce an Isaac-style floor as a graph of rooms — a branching walk from a Start
room, special rooms assigned by the farthest-dead-end rule, per-room template ids — byte-identically from a
seed, with a `floorSeed` giving each floor of a run its own reproducible-yet-independent seed. Milestone M4,
the roguelike spec §4.8 generator on the M1 substrate.

## Scope
- SB-001: `RoomKind`, `FloorRoom`, `FloorLayout`, `FloorParams`, `MapGen.floorLayout`, and
  `MapGen.floorSeed` added to the `MapGen` module in `FS.GG.Game.Core`.
- SB-002: A branching random walk (occupied-cell and loop-limit rules), special-room assignment by
  descending dead-end distance, per-room template ids, and per-floor seed derivation.
- SB-003: A determinism + graph-shape property suite, and the surface baseline.

## Non-Goals
- SB-004: No maze/noise/scatter (M5), full teaching SKILL.md (M6), tile rasterization of the layout, or
  render-tier work.

## User Stories
- US-001 (P1): As a roguelike author, I can call `MapGen.floorLayout` and get a connected graph of rooms with
  a Start, special rooms, and template ids, from a seed.
- US-002 (P1): As a roguelike author, I can derive each floor's seed with `MapGen.floorSeed runSeed floorIndex`
  so floors are independent yet reproducible.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given a `FloorParams`, when `MapGen.floorLayout` runs, then it places up to `RoomCount` rooms (at least the Start at the origin) by a branching walk, threading the `Rng`, and returns a `FloorLayout` and the `Rng`.
- AC-002 [US-001] [FR-002]: Given the walk, when a candidate room cell is considered, then it is never an already-placed cell and is rejected when it already touches two or more placed rooms, so the resulting adjacency graph is connected.
- AC-003 [US-001] [FR-003]: Given the placed rooms, when special rooms from `SpecialRooms` are assigned, then they go to the farthest dead-end rooms (by descending graph distance from Start, Start excluded), and any that cannot be placed (too few dead-ends) are omitted.
- AC-004 [US-001] [FR-004]: Given the `FloorLayout`, when it is read, then `Rooms` carries each room's `Cell`, `Kind`, and a `TemplateId` drawn from the `Rng`, and `Adjacency` lists the 4-adjacent room-cell pairs, both in a fixed deterministic order.
- AC-005 [US-002] [FR-005]: Given `MapGen.floorSeed runSeed floorIndex`, when it is called, then it returns a seed that is deterministic for the pair and distinct across floor indices.
- AC-006 [US-002] [FR-006]: Given one seed, when `floorLayout` runs twice, then the two `FloorLayout`s are byte-identical; a different seed differs.
- AC-007 [US-001] [FR-007]: Given `RoomCount <= 0` or other degenerate params, when `floorLayout` runs, then it returns a single Start room and never throws.

## Functional Requirements
- FR-001: `MapGen.floorLayout` MUST place up to `FloorParams.RoomCount` rooms (at least a Start at the origin) by a branching random walk, threading the `Rng`, and return `struct (FloorLayout * Rng)`. (covers AC-001)
- FR-002: The walk MUST never place a room on an occupied cell and MUST reject a candidate that already touches two or more placed rooms, keeping the adjacency graph connected. (covers AC-002)
- FR-003: Special rooms from `FloorParams.SpecialRooms` MUST be assigned to the farthest dead-end rooms by descending graph distance from Start (Start excluded), with any that cannot be placed omitted. (covers AC-003)
- FR-004: `FloorLayout.Rooms` MUST carry each room's `Cell`, `Kind`, and an `Rng`-drawn `TemplateId`, and `FloorLayout.Adjacency` MUST list the 4-adjacent room-cell pairs, both in a fixed deterministic order. (covers AC-004)
- FR-005: `MapGen.floorSeed runSeed floorIndex` MUST return a deterministic seed for the pair that is distinct across floor indices. (covers AC-005)
- FR-006: `floorLayout` MUST be byte-identical for a seed and differ for a different seed. (covers AC-006)
- FR-007: `floorLayout` MUST be total — `RoomCount <= 0` yields a single Start room, params clamp, and it never throws. (covers AC-007)

## Ambiguities
- AMB-001: `FloorRoom` shape — carry an explicit doors field, or imply doors from `Adjacency`?
- AMB-002: When dead-ends are too few for all `SpecialRooms`, omit the extras, or relax to any leaf/any non-Start room?
- AMB-003: `floorSeed` derivation — reuse the `Rng` splitmix mixing, or a bespoke hash of `(runSeed, floorIndex)`?

## Public Or Tool-Facing Impact
- Tier 1 (contracted). Adds `RoomKind`, `FloorRoom`, `FloorLayout`, `FloorParams`, `MapGen.floorLayout`, and
  `MapGen.floorSeed` to `FS.GG.Game.Core`; updates the surface baseline. Additive.

## Lifecycle Notes
- Tests carry stable filterable names.
- Next lifecycle action: `fsgg-sdd clarify --work 030-mapgen-room-graph`.
