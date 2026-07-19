---
schemaVersion: 1
workId: 015-pathfinding-jps
title: Jump Point Search
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/015-pathfinding-jps/spec.md
sourceClarifications: work/015-pathfinding-jps/clarifications.md
sourceChecklist: work/015-pathfinding-jps/checklist.md
publicOrToolFacingImpact: true
---

# Jump Point Search Plan

Prose status: planned

## Source Snapshot
- spec: work/015-pathfinding-jps/spec.md sha256:67520d4c0efa0c76840a55b994fc9ce55933d4134895708ff344d78c4bae988e schemaVersion:1
- clarifications: work/015-pathfinding-jps/clarifications.md sha256:ad34163cdb20468762ba10ef68e7ffe362305d45fe6495aceb18b6c90d400a90 schemaVersion:1
- checklist: work/015-pathfinding-jps/checklist.md sha256:33488122e8e3cb861095bc93819db32b3aca290755084c2872992b659c020d79 schemaVersion:1

## Technical Context
F# / `FS.GG.Game.Core.Pathfinding`. JPS is a grid-specialized A*: same open-set, same total order,
same heuristic and int64 g-accumulation as the shipped `astar`, but its successor generation is a
jump/prune step instead of the raw neighbour set. Uniform cost only (binary `isWalkable`, no `cost`).

## Constitution Check
- III (public surface declared): declare `Pathfinding.jps` in `Pathfinding.fsi` before `.fs`; update
  the surface baseline `readiness/surface-baselines/FS.GG.Game.Core.txt` in the same change.
- V (MUE / pure transitions): `jps` is a pure function, safe inside a deterministic-replay `update`.
- VI (test evidence): the differential property test fails before (no `jps`) and passes after.

## Plan Scope
- Add one public function `Pathfinding.jps` and its jump machinery (all `private`) to `Pathfinding.fs`
  /`.fsi`. No change to `astar`/`bfs`/`distanceField`/`reachable`. Requirement count: 6. Clarification
  decision count: 2. Checklist result count: 6.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: Add `jps: neighbourhood:Neighbourhood -> maxVisited:int -> isWalkable:(Cell -> bool) -> start:Cell -> goal:Cell -> Cell list option` to `.fsi`/`.fs`, signature-parallel to `astar`. It reuses the module `trivial` guard, `heuristic`, and the `(f,h,Col,Row)` int64 frontier so its cost-optimality is inherited from A* over the pruned successor graph. JPS is provably optimal on uniform-cost grids, so its path cost equals `astar`'s (DEC-001).
- PD-002 [AC-002] [FR-002] complete: The open set stores jump points keyed like `astar`. On popping a jump point `c` with recorded arrival direction, `jps` computes its successor jump points by pruning to natural+forced neighbour directions and calling `jump` along each. `cameFrom` records the predecessor jump point; the g-score is the true `baseStep`-scaled path cost to the jump point (orthogonal run length × `baseStep` + diagonal count × `diagStep`).
- PD-003 [AC-002] [FR-002] complete: `jump dir node` scans from `node` in `dir`: returns `None` if the stepped cell is unwalkable or (diagonal) violates no-corner-cutting; returns `Some cell` if the cell is `goal` or has a forced neighbour; for a diagonal `dir`, first returns `Some cell` if a recursive straight `jump` along either component finds a jump point; otherwise recurses along `dir`. Direction order and the forced-neighbour test are a fixed documented total order (FR-005).
- PD-004 [AC-002] [FR-002] complete: Path reconstruction walks the jump-point `cameFrom` chain, then **interpolates the intermediate cells** of each straight/diagonal run between consecutive jump points, so the returned `Cell list` is contiguous cell-by-cell (start..goal inclusive, each step `Neighbourhood`-legal) — the validity FR-002 asserts, not a sparse jump-point list.
- PD-005 [AC-002] [FR-002] complete: No corner cutting — a diagonal step (in `jump` and in successor expansion) is taken only when both shared orthogonal neighbours are walkable, identical to the shipped `neighbours`. `FourWay` admits only orthogonal directions; its jumps are straight runs whose forced neighbours are turns opened by an adjacent wall.
- PD-006 [AC-004] [FR-004] complete: Totality — `jps` calls the shared `trivial maxVisited isWalkable start goal` guard first, so a non-walkable `start`/`goal`, `maxVisited <= 0`, and `start = goal` yield exactly `astar`'s degenerate results (`None`, `None`, `Some [start]`).
- PD-007 [AC-005] [FR-005] complete: Determinism — costs are integers, the frontier is the same total `(f,h,Col,Row)` order as `astar` (deterministic `Set.minElement`), and jump/forced-neighbour direction enumeration is a fixed list; no float or hash-iteration influences output. A self-determinism golden asserts run-to-run byte-identity.
- PD-008 [AC-003] [AC-006] [FR-003] [FR-006] complete: `maxVisited` bounds **frontier pops** (DEC-002), the same unit `astar` counts, so `jps` and `astar` agree on reachability (FR-003) on searches not bounded out. A benchmark counts frontier pops for both and asserts `jps < astar` on an open map (FR-006).

## Contract Impact
- PC-001 [PD-001] command report: Tier-1 public surface addition — `Pathfinding.jps` in `Pathfinding.fsi`/`.fs`. The surface baseline `readiness/surface-baselines/FS.GG.Game.Core.txt` gains the `jps` entry; `.fsi`, baseline, and tests land together and the surface-baseline-drift gate stays green. The `.fsi` is the contract (no separate `contracts/` dir). No existing signature changes.

## Verification Obligations
- VO-001 [PD-001] [PD-002] [PC-001] semanticTest: FsCheck differential property over random grids/queries — for each, `jps` and `astar` agree on `None`/`Some` (within budget), and when `Some`, the `jps` path has equal total cost to `astar`'s and is valid (start/goal-anchored, adjacency-legal, no corner-cutting, walkable). Fails before (no `jps`), passes after; `dotnet build` clean under warnings-as-errors; surface baseline regenerated and committed.
- VO-002 [PD-007] [PD-008] semanticTest: a named `determinism`/`differential` golden asserting `jps` run-to-run byte-identity, plus a benchmark-style test counting frontier pops that asserts `jps` pops strictly fewer nodes than `astar` on an open map (FR-006).

## Migration Posture
- PM-001 [PC-001] diagnoseOnly: Plan schemaVersion 1 is accepted; the addition removes/renames nothing, so no consumer migration is required.

## Generated View Impact
- GV-001 [PD-001] workModel: `readiness/015-pathfinding-jps/work-model.json` refreshes from current plan sources or reports `staleGeneratedView`.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 015-pathfinding-jps`.
