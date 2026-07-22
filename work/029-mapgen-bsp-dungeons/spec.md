---
schemaVersion: 1
workId: 029-mapgen-bsp-dungeons
title: MapGen BSP Room-and-Corridor Dungeons
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# MapGen BSP Room-and-Corridor Dungeons Specification

Prose status: specified

## User Value
A generated FS.GG.UI game can produce structured room-and-corridor dungeons — rectangular rooms joined by
corridors — byte-identically from a seed, and receives a `RoomGraph` of integer-addressed rooms and the
corridors between them to place a start/exit/loot on. Milestone M3 on the M1 substrate.

## Scope
- SB-001: `BspParams`, `Room`, `RoomGraph`, and `MapGen.bspDungeon` added to the `MapGen` module in
  `FS.GG.Game.Core`, over the M1 substrate.
- SB-002: A recursive BSP partition, one padded room per leaf, L-corridors joining sibling subtrees, and a
  single-dungeon connectivity guarantee via `MapGen.connect`.
- SB-003: A determinism + traversability property suite reusing the M1 harness, and the surface baseline.

## Non-Goals
- SB-004: No room-graph floors (M4), maze/noise/scatter (M5), full teaching SKILL.md (M6), or render-tier work.

## User Stories
- US-001 (P1): As a game author, I can call `MapGen.bspDungeon` and get a rectangular room-and-corridor
  dungeon plus a `RoomGraph` I can place gameplay on, from a seed.
- US-002 (P1): As a product consumer, I can rely on `bspDungeon` being byte-identical for a seed in both its
  `TileMap` and its `RoomGraph`.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given a `BspParams` and a size, when `MapGen.bspDungeon` runs, then it partitions the rectangle into leaves bounded by the params, places one padded room per leaf, carves floor for rooms and corridors, and returns a `TileMap`, a `RoomGraph`, and the threaded `Rng`.
- AC-002 [US-001] [FR-002]: Given a placed room, when its `Bounds` are checked, then the room lies inside its leaf inset by `RoomPadding`, and rooms in distinct leaves do not overlap.
- AC-003 [US-001] [FR-003]: Given the `RoomGraph`, when it is read, then `Rooms` has one `Room` per placed room with integer `Bounds` and `Id` ascending in a fixed traversal order, and `Corridors` lists the room-id pairs a corridor joins.
- AC-004 [US-001] [FR-004]: Given a generated dungeon with any floor, when `regions`/`bfs` run, then the floor is a single traversable region (connectivity via BSP joins plus `connect`).
- AC-005 [US-002] [FR-005]: Given one seed, when `bspDungeon` runs twice, then both the `TileMap` and the `RoomGraph` are byte-identical; an incremented seed differs.
- AC-006 [US-001] [FR-006]: Given zero/negative size or impossible params (min > max, non-positive leaf), when `bspDungeon` runs, then it returns an empty `TileMap` and empty `RoomGraph` and never throws.

## Functional Requirements
- FR-001: `MapGen.bspDungeon` MUST partition the `width * height` rectangle into leaves bounded by `BspParams`, place one padded room per leaf, carve room and corridor floor, and return `struct (TileMap * RoomGraph * Rng)` threading the `Rng`. (covers AC-001)
- FR-002: Each room's `Bounds` MUST lie within its leaf inset by `RoomPadding`, and rooms in distinct leaves MUST NOT overlap. (covers AC-002)
- FR-003: `RoomGraph.Rooms` MUST carry one `Room` per placed room with integer `Bounds` and `Id` ascending in a fixed traversal order, and `RoomGraph.Corridors` MUST list the room-id pairs a corridor joins. (covers AC-003)
- FR-004: A generated dungeon with any floor MUST be a single `bfs`-traversable region. (covers AC-004)
- FR-005: `bspDungeon` MUST be byte-identical for a seed in both `TileMap` and `RoomGraph`, and differ for an incremented seed. (covers AC-005)
- FR-006: `bspDungeon` MUST be total — degenerate size and impossible params yield an empty `TileMap` and empty `RoomGraph`; it never throws. (covers AC-006)

## Ambiguities
- AMB-001: Split-axis choice — always split the longer dimension, or pick the axis from the `Rng`?
- AMB-002: Which rooms do sibling subtrees connect — a representative (e.g. first) room from each, or the geometrically nearest pair — and is a final `MapGen.connect` applied as a connectivity safety net?
- AMB-003: `Room.Bounds` type — reuse the existing `Rect` (float fields holding integer values) or introduce a new integer rect?

## Public Or Tool-Facing Impact
- Tier 1 (contracted). Adds `BspParams`, `Room`, `RoomGraph`, and `MapGen.bspDungeon` to `FS.GG.Game.Core`;
  updates the surface baseline. Additive.

## Lifecycle Notes
- Tests reuse the M1 harness and carry stable filterable names.
- Next lifecycle action: `fsgg-sdd clarify --work 029-mapgen-bsp-dungeons`.
