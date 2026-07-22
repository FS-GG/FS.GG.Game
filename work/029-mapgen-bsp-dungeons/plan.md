---
schemaVersion: 1
workId: 029-mapgen-bsp-dungeons
title: MapGen BSP Room-and-Corridor Dungeons
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/029-mapgen-bsp-dungeons/spec.md
sourceClarifications: work/029-mapgen-bsp-dungeons/clarifications.md
sourceChecklist: work/029-mapgen-bsp-dungeons/checklist.md
publicOrToolFacingImpact: true
---

# MapGen BSP Room-and-Corridor Dungeons Plan

Prose status: planned

## Source Snapshot
- spec: work/029-mapgen-bsp-dungeons/spec.md sha256:dddba9ac8f0c0230e659d13d2f89882316673ac01df9be52336c927af8c6ec99 schemaVersion:1
- clarifications: work/029-mapgen-bsp-dungeons/clarifications.md sha256:e4a74a125936a3ca17390a006b46027493aef6218aec76d323d6518533ca6814 schemaVersion:1
- checklist: work/029-mapgen-bsp-dungeons/checklist.md sha256:74d8c3eead127dd3db2a7a8197717c5a764dd2395671af7acad3c51ff01e94fc schemaVersion:1

## Technical Context
F#, `net10.0`, BCL-only. Extends `MapGen` (`MapGen.fs`/`.fsi`) with `BspParams`, `Room`, `RoomGraph`, and
`bspDungeon`. Tier 1: additive public surface, baselines refresh. Tests reuse the M1 harness.

## Constitution Check
- III Public Surface: types + `bspDungeon` declared in `MapGen.fsi` first; baselines refreshed additively.
- IV Idiomatic simplicity: reuse `Rect` for `Room.Bounds` (DEC-003) and the nearest-pair L-carve; no new
  rect type or corridor primitive.
- V MUE: `Rng` threaded through a fixed recursion order; no ambient state.
- VI Test evidence: determinism (TileMap + RoomGraph) + traversability reuse the M1 harness.

## Design
- `BspParams = { MinLeaf: int; MaxLeaf: int; RoomPadding: int }`.
- `Room = { Id: int; Bounds: Rect }` (integer cell values in `Rect`); `RoomGraph = { Rooms: Room[]; Corridors: (int * int)[] }`.
- `bspDungeon w h p rng`: (1) clamp `w,h >= 0`, `MinLeaf >= 1`, `MaxLeaf >= MinLeaf`, `RoomPadding >= 0`;
  empty ⇒ empty map + empty graph. (2) Recurse over `(x,y,w,h)` nodes: split the **longer** dimension when it
  exceeds `MaxLeaf` and is `>= 2*MinLeaf`, split position drawn from `Rng` in `[MinLeaf, size-MinLeaf]`; else
  the node is a leaf. Left-before-right, so leaf order is fixed. (3) Place one room per leaf: a `Rect` inside
  the leaf inset by `RoomPadding`, size/offset drawn from `Rng`; assign `Id` in leaf traversal order; carve
  floor. (4) On the way up, join each node's two subtrees by a nearest-pair L-corridor between their
  representative (first) rooms; record the room-id pair in `Corridors`. (5) `connect FourWay` as the
  connectivity safety net (DEC-002). Return `struct (map, graph, rng')`.
- Determinism: recursion is left-before-right, every draw threads the `Rng`, room ids follow leaf order, and
  `Corridors`/`Rooms` are arrays in that fixed order — so both the `TileMap` and the `RoomGraph` are
  byte-identical for a seed.

## Public Surface
- `MapGen.fsi` gains `BspParams`, `Room`, `RoomGraph`, and
  `val bspDungeon: width -> height -> BspParams -> Rng -> struct (TileMap * RoomGraph * Rng)`.
- Baselines gain those types (+ members) and `MapGen.bspDungeon`.

## Plan Scope
- Extends `MapGen` with the BSP-dungeon family from the covered spec. Requirement count: 6. Clarification
  decision count: 3. Checklist result count: 6. Tier 1 (additive public surface + baseline).

## Plan Decisions
- PD-001 [AC-001] [FR-001] [DEC-001] complete: recursive longer-dimension partition (split position from
  `Rng`), one padded room per leaf, carve floor, return `TileMap` + `RoomGraph` + `Rng`.
- PD-002 [AC-002] [FR-002] [DEC-003] complete: each room `Rect` lies within its leaf inset by `RoomPadding`;
  disjoint leaves ⇒ non-overlapping rooms.
- PD-003 [AC-003] [FR-003] complete: `RoomGraph.Rooms` one per room, integer `Bounds`, `Id` ascending by leaf
  order; `Corridors` the joined room-id pairs.
- PD-004 [AC-004] [FR-004] [DEC-002] complete: BSP subtree joins + `connect FourWay` safety net ⇒ a single
  `bfs`-traversable floor.
- PD-005 [AC-005] [FR-005] complete: determinism property — both `TileMap` and `RoomGraph` byte-identical for
  a seed (M1 harness for the map; structural equality for the graph); seed+1 differs.
- PD-006 [AC-006] [FR-006] complete: totality — clamp size/params; degenerate input ⇒ empty map + empty
  graph; never throws.

## Contract Impact
- PC-001 [PD-001] surfaceBaseline: `FS.GG.Game.Core` surface grows by `BspParams`, `Room`, `RoomGraph`, and
  `MapGen.bspDungeon`; both baselines update in lockstep; drift shows exactly those additions.

## Verification Obligations
- VO-001 [PD-001] [PD-002] [PD-003] semanticTest: unit tests for room-within-leaf placement, non-overlap, and
  the RoomGraph shape pass; build clean under warnings-as-errors.
- VO-002 [PD-004] [PD-005] [PD-006] semanticTest: traversability (M1 harness), determinism (map + graph), and
  totality suites pass.
- VO-003 [PC-001] semanticTest: both surface baselines byte-match the built surface (drift gate clean).

## Migration Posture
- PM-001 [PC-001] diagnoseOnly: purely additive public surface; nothing removed or renamed.

## Generated View Impact
- GV-001 [PD-001] workModel: `readiness/029-mapgen-bsp-dungeons/work-model.json` refreshes or reports
  `staleGeneratedView`.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 029-mapgen-bsp-dungeons`.
