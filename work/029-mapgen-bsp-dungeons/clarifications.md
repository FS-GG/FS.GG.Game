---
schemaVersion: 1
workId: 029-mapgen-bsp-dungeons
title: MapGen BSP Room-and-Corridor Dungeons
stage: clarify
changeTier: tier1
status: needsAnswers
sourceSpec: work/029-mapgen-bsp-dungeons/spec.md
publicOrToolFacingImpact: true
---

# MapGen BSP Room-and-Corridor Dungeons Clarifications

## Source Specification
- work/029-mapgen-bsp-dungeons/spec.md

## Clarification Questions
- **CQ-001** (AMB-001): Split the longer dimension, or pick the split axis from the `Rng`?
- **CQ-002** (AMB-002): Connect a representative room per subtree or the nearest pair, and apply a final
  `connect` safety net?
- **CQ-003** (AMB-003): `Room.Bounds` as the existing `Rect`, or a new integer rect type?

## Answers
- CQ-001 → Split the **longer** dimension, and only draw the split *position* from the `Rng` (within
  `[MinLeaf, size - MinLeaf]`). Splitting the longer side keeps leaves from degenerating into thin slivers a
  room can't fit — a random-axis split routinely does. When both sides are within `MaxLeaf` the node is a
  leaf; when a side exceeds `MaxLeaf` but is too short to split (`< 2 * MinLeaf`) the other axis is used.
  Deterministic: only the position draws from the threaded `Rng` (resolves AMB-001).
- CQ-002 → Join the **nearest cell pair** between a representative room of each sibling subtree (the left
  subtree's and right subtree's first room in traversal order), carved as an axis-then-axis L — reusing the
  same nearest-pair + L-carve primitive M1 `connect` uses, so the corridor style is consistent. Then apply a
  final `MapGen.connect FourWay` as a **safety net**: BSP joins already connect every subtree, so `connect`
  is a no-op on a well-formed dungeon, but it deterministically repairs the rare case where a leaf was too
  small to hold a room (that subtree contributes no room to join). The `RoomGraph.Corridors` record the BSP
  joins; the safety-net corridors are a `TileMap` connectivity guarantee, not room adjacency (resolves
  AMB-002).
- CQ-003 → Reuse the existing `Rect`. Its float fields hold integer cell values here (`X`=col, `Y`=row,
  `Width`/`Height`=cell extents), exactly as `Grids.cellRect` already produces `Rect` in cell space. A new
  integer-rect type would duplicate the vocabulary the grid family already maps rooms onto, against the
  "reuse, don't re-roll" rule; callers who want integer corners read `int room.Bounds.X` etc. (resolves
  AMB-003).

## Decisions
- **DEC-001** [CQ-001] [AMB:AMB-001] [FR-001]: Split the longer dimension; only the split position draws from
  the `Rng`; a too-short longer side falls back to the other axis.
- **DEC-002** [CQ-002] [AMB:AMB-002] [FR-003] [FR-004]: Sibling subtrees join their representative rooms by a
  nearest-pair L-corridor recorded in `Corridors`; a final `MapGen.connect FourWay` guarantees `TileMap`
  connectivity.
- **DEC-003** [CQ-003] [AMB:AMB-003] [FR-002] [FR-003]: `Room.Bounds` is the existing `Rect` holding integer
  cell values; no new rect type.

## Accepted Deferrals
- None — all three ambiguities are resolved above.

## Remaining Ambiguity
- None. AMB-001 through AMB-003 are resolved by DEC-001 through DEC-003.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 029-mapgen-bsp-dungeons`.
