---
schemaVersion: 1
workId: 016-pathfinding-regions
title: Connected-Component Early-Out
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/016-pathfinding-regions/spec.md
sourceClarifications: work/016-pathfinding-regions/clarifications.md
sourceChecklist: work/016-pathfinding-regions/checklist.md
publicOrToolFacingImpact: true
---

# Connected-Component Early-Out Plan

Prose status: planned

## Source Snapshot
- spec: work/016-pathfinding-regions/spec.md sha256:5fb8135936ea0e20cdbf10da81f4a7f159324b94491558519f7e7b5a0bfd3177 schemaVersion:1
- clarifications: work/016-pathfinding-regions/clarifications.md sha256:52f2d4d9e56b9b1de3c5dde2cb14f09c2ff10dfacb1c16d474bcb388a687fc38 schemaVersion:1
- checklist: work/016-pathfinding-regions/checklist.md sha256:e9c7bd8ff3433de1fa412d66710084aa3c49f91e63b37f600c7cbc6a7c7209c7 schemaVersion:1

## Technical Context
F# / `FS.GG.Game.Core.Pathfinding`. A new opaque `Regions` value plus a companion `Regions` module.
Flood fill reuses the module's private `neighbours` (so connectivity — including no-corner-cutting —
matches `astar`/`bfs` exactly). Bounded, because the framework holds no map.

## Constitution Check
- III (public surface): declare `Regions` (opaque) + `Regions.build`/`sameComponent` in the `.fsi`
  before the `.fs`; update the type and member surface baselines together.
- V (MUE / pure): `build` is a pure function; `sameComponent` is a pure lookup.
- VI (test evidence): the differential property (sameComponent == astar reachability) fails before,
  passes after.

## Plan Scope
- Add a nested `type Regions` (opaque; internal `Map<Cell,int>` labels) and a `[<RequireQualifiedAccess>]
  module Regions` with `build` and `sameComponent`. No change to existing functions. Requirement
  count: 5. Clarification decision count: 3. Checklist result count: 5.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: Add opaque `type Regions` and `Regions.build: Neighbourhood -> Cell * Cell -> (Cell -> bool) -> Regions`. Flood fill labels each maximal walkable region; `sameComponent` agrees with `astar` reachability by construction (same neighbour rule) (DEC-002).
- PD-002 [AC-002] [FR-002] complete: `Regions.sameComponent: Regions -> Cell -> Cell -> bool` = both cells present in the internal label map AND equal labels. An out-of-bounds/unwalkable cell is absent from the map, so `sameComponent` is false — with no `a = b` exception.
- PD-003 [AC-003] [FR-003] complete: connectivity uses the private `neighbours neighbourhood walk` (walk = in-bounds AND isWalkable), so the no-corner-cutting rule under `EightWay` is inherited and matches `astar`/`bfs`.
- PD-004 [AC-004] [FR-004] complete: `build` is pure — a row-major scan (rows outer, cols inner) with a BFS flood assigning ascending label ids (DEC-003). `sameComponent` exposes only equality, so relabeling cannot change any result; a determinism golden pins byte-identity.
- PD-005 [AC-005] [FR-005] complete: `sameComponent` is two `Map.tryFind` lookups — O(log n) map access, no search, no `maxVisited`; the disconnected-map test answers without a search bound.

## Contract Impact
- PC-001 [PD-001] [PD-002] command report: Tier-1 public surface additions — `Pathfinding.Regions` (type) and `Regions.build`/`sameComponent` (members). Both surface baselines (`readiness/surface-baselines/FS.GG.Game.Core.txt` + `members/`) update with the `.fsi` and tests; the drift gate stays green. The `.fsi` is the contract (no `contracts/` dir). No existing signature changes.

## Verification Obligations
- VO-001 [PD-001] [PD-002] [PC-001] semanticTest: FsCheck differential over random bounded grids — for every cell pair, `sameComponent a b` == `(astar ... a b).IsSome`; plus concrete out-of-bounds/unwalkable and EightWay-corner cases. Fails before (no `Regions`), passes after; build clean under warnings-as-errors; both surface baselines regenerated and committed.
- VO-002 [PD-004] semanticTest: a named `determinism` golden asserting `build` yields byte-identical `sameComponent` answers across repeat runs over random terrain.

## Migration Posture
- PM-001 [PC-001] diagnoseOnly: Plan schemaVersion 1 accepted; purely additive, so no consumer migration.

## Generated View Impact
- GV-001 [PD-001] workModel: `readiness/016-pathfinding-regions/work-model.json` refreshes from current plan sources or reports `staleGeneratedView`.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 016-pathfinding-regions`.
