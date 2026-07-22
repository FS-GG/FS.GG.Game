---
schemaVersion: 1
workId: 030-mapgen-room-graph
title: MapGen Room-Graph Branching-Walk Floors
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/030-mapgen-room-graph/spec.md
sourceClarifications: work/030-mapgen-room-graph/clarifications.md
sourceChecklist: work/030-mapgen-room-graph/checklist.md
publicOrToolFacingImpact: true
---

# MapGen Room-Graph Branching-Walk Floors Plan

Prose status: planned

## Source Snapshot
- spec: work/030-mapgen-room-graph/spec.md sha256:a0216893c9b2a3654f9d79a292b8a1fc7a8a097abbd8b41028538236584669bd schemaVersion:1
- clarifications: work/030-mapgen-room-graph/clarifications.md sha256:1840ceed1ce4190fefe915d9ed4566763bd671f04542310f2a62b943c4105661 schemaVersion:1
- checklist: work/030-mapgen-room-graph/checklist.md sha256:d1af754cbe0ab7072e1b115400b94c185e7d87631d0ad5ea6b2f5e35355b1e5a schemaVersion:1

## Technical Context
F#, `net10.0`, BCL-only. Extends `MapGen` (`MapGen.fs`/`.fsi`) with `RoomKind`, `FloorRoom`, `FloorLayout`,
`FloorParams`, `floorLayout`, and `floorSeed`. This family is a graph of rooms on `Cell` coordinates, not a
`TileMap`. Tier 1: additive public surface, baselines refresh.

## Constitution Check
- III Public Surface: types + `floorLayout`/`floorSeed` declared in `MapGen.fsi` first; baselines additive.
- IV Idiomatic simplicity: doors implied by `Adjacency` (DEC-001), no second field; reuse the `Rng` mixer for
  `floorSeed` (DEC-003).
- V MUE: `Rng` threaded through a fixed walk order; no ambient state.
- VI Test evidence: determinism (byte-identity + per-floor independence) + graph-shape properties.

## Design
- `RoomKind = Normal | Start | Boss | Treasure | Shop | Secret`.
- `FloorRoom = { Cell: Cell; Kind: RoomKind; TemplateId: int }`; doors implied by adjacency.
- `FloorLayout = { Rooms: FloorRoom[]; Adjacency: (Cell * Cell)[] }`.
- `FloorParams = { RoomCount: int; MaxRooms: int; SpecialRooms: RoomKind list }`.
- `floorLayout p rng`: (1) target = clamp `RoomCount` to `[1, max 1 MaxRooms]`. (2) Branching walk from Start
  at `(0,0)`: a FIFO queue seeded with the start; while placed < target and queue non-empty, dequeue `c` and
  for each of the 4 directions (fixed N,E,S,W order) consider `n = c + dir` — skip if occupied, skip if `n`
  already touches ≥2 placed rooms, else a 50% `Rng.nextBool` gate places `n` and enqueues it (stop at
  target). (3) `TemplateId` per room drawn from `Rng` in placement order. (4) BFS distances from Start over
  4-adjacency; dead-ends = placed cells with exactly one placed neighbour (Start excluded); assign
  `SpecialRooms` in list order to dead-ends by descending distance (tie → `Cell`), extras omitted. (5)
  `Rooms` in placement order, `Adjacency` the 4-adjacent placed-cell pairs sorted by `(a, b)`. Return
  `struct (layout, rng')`.
- `floorSeed run i`: seed an `Rng` from `run`, mix `i` in via the splitmix finalizer, return the state.
- Determinism: fixed direction order, `Rng` threaded, rooms in placement order, adjacency sorted — the whole
  `FloorLayout` is byte-identical for a seed.

## Public Surface
- `MapGen.fsi` gains `RoomKind`, `FloorRoom`, `FloorLayout`, `FloorParams`,
  `val floorLayout: FloorParams -> Rng -> struct (FloorLayout * Rng)`, and
  `val floorSeed: runSeed: uint64 -> floorIndex: int -> uint64`.
- Baselines gain those types (+ members) and the two `MapGen` values.

## Plan Scope
- Extends `MapGen` with the room-graph floor family from the covered spec. Requirement count: 7. Clarification
  decision count: 3. Checklist result count: 7. Tier 1 (additive public surface + baseline).

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: branching walk from a Start room places up to `RoomCount` rooms,
  threading `Rng`; returns `FloorLayout` + `Rng`.
- PD-002 [AC-002] [FR-002] complete: reject occupied cells and candidates touching ≥2 placed rooms ⇒ a
  connected adjacency graph.
- PD-003 [AC-003] [FR-003] [DEC-002] complete: special rooms assigned to farthest dead-ends by descending
  distance; extras omitted.
- PD-004 [AC-004] [FR-004] [DEC-001] complete: `Rooms` carry `Cell`/`Kind`/`TemplateId`; `Adjacency` the
  sorted 4-adjacent pairs; doors implied.
- PD-005 [AC-005] [FR-005] [DEC-003] complete: `floorSeed run i` mixes `i` into an `Rng` from `run`; distinct
  per index.
- PD-006 [AC-006] [FR-006] complete: determinism property — byte-identical for a seed; differs for another.
- PD-007 [AC-007] [FR-007] complete: totality — `RoomCount <= 0` ⇒ single Start room; params clamp; never
  throws.

## Contract Impact
- PC-001 [PD-001] surfaceBaseline: `FS.GG.Game.Core` surface grows by the four types and the two `MapGen`
  values; both baselines update in lockstep; drift shows exactly those additions.

## Verification Obligations
- VO-001 [PD-001] [PD-002] [PD-003] [PD-004] semanticTest: unit tests for the connected graph, special-room
  placement on dead-ends, and the room/adjacency shape pass; build clean under warnings-as-errors.
- VO-002 [PD-005] [PD-006] [PD-007] semanticTest: `floorSeed` distinctness, determinism, and totality suites
  pass.
- VO-003 [PC-001] semanticTest: both surface baselines byte-match the built surface (drift gate clean).

## Migration Posture
- PM-001 [PC-001] diagnoseOnly: purely additive public surface; nothing removed or renamed.

## Generated View Impact
- GV-001 [PD-001] workModel: `readiness/030-mapgen-room-graph/work-model.json` refreshes or reports
  `staleGeneratedView`.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 030-mapgen-room-graph`.
