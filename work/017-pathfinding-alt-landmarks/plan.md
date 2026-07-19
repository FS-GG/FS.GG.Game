---
schemaVersion: 1
workId: 017-pathfinding-alt-landmarks
title: ALT Landmark Heuristic
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/017-pathfinding-alt-landmarks/spec.md
sourceClarifications: work/017-pathfinding-alt-landmarks/clarifications.md
sourceChecklist: work/017-pathfinding-alt-landmarks/checklist.md
publicOrToolFacingImpact: true
---

# ALT Landmark Heuristic Plan

Prose status: planned

## Source Snapshot
- spec: work/017-pathfinding-alt-landmarks/spec.md sha256:6a55cf761bf2fc4df670657159d09eb6a41c43f120a22523121fcfbadf095158 schemaVersion:1
- clarifications: work/017-pathfinding-alt-landmarks/clarifications.md sha256:18eb779c9f4baa2ecedf904088ddc062ba78eb721d10753a248db448bb768483 schemaVersion:1
- checklist: work/017-pathfinding-alt-landmarks/checklist.md sha256:bac99b3d1d41116f7c1f6ed782856122ca517934e8194a85e872767dead2b350 schemaVersion:1

## Technical Context
F# / `FS.GG.Game.Core.Pathfinding`. The existing `astar` is refactored onto a private
heuristic-parameterised core `astarWith (h: Cell -> int64) ...`; `astar` delegates with the current
octile heuristic (byte-identical output, guarded by the existing determinism/differential tests). A
new opaque `Landmarks` value holds one `distanceField` per landmark; `Landmarks.astar` runs the core
with `max(octile, ALT)`.

## Constitution Check
- III (public surface): declare `Landmarks` + `Landmarks.build`/`heuristic`/`astar` in the `.fsi`
  before the `.fs`; update both surface baselines. `astar`'s signature is unchanged.
- V (MUE / pure): `build`, `heuristic`, and `astar` are pure functions.
- VI (test evidence): differential (equal cost) + admissibility fail before, pass after.

## Plan Scope
- Refactor `astar` onto `astarWith`; add `type Landmarks` and a `Landmarks` module (`build`,
  `heuristic`, `astar`). Requirement count: 6. Clarification decision count: 3. Checklist result count: 6.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: Extract private `astarWith (h: Cell -> int64) neighbourhood maxVisited isWalkable start goal` from the current `astar` body; `astar` becomes `astarWith (heuristic neighbourhood goal) ...`, byte-identical (the existing 500-case determinism/differential tests are the guard). `Landmarks.astar` calls `astarWith` with the combined heuristic — optimal because that heuristic is admissible.
- PD-002 [AC-004] [FR-004] complete: `Landmarks.build neighbourhood isWalkable count bounds` selects landmarks by deterministic farthest-point (max-min) sampling from the bounds min-corner walkable seed, `(Col,Row)` tie-break (DEC-001), and stores a `distanceField` (cost `fun c -> if isWalkable c then 1 else 0`) per landmark within bounds.
- PD-003 [AC-005] [FR-005] complete: `Landmarks.heuristic landmarks goal cell` = `max over landmarks of |d(L,goal) - d(L,cell)|` (skipping landmarks that cannot reach both), all integer `baseStep`-scaled. Admissible by the triangle inequality over exact shortest-path distances.
- PD-004 [AC-006] [FR-006] complete: `Landmarks.astar` uses `fun c -> max (octile neighbourhood goal c) (Landmarks.heuristic landmarks goal c)`. Pointwise-max of two admissible heuristics is admissible and >= octile, so it expands no more nodes than `astar` (DEC-002) and strictly fewer where ALT is tighter; a benchmark proves the strict case.
- PD-005 [AC-002] [FR-002] complete: `Landmarks.astar` reuses the `astarWith` reconstruction, so every path is valid (start/goal-anchored, adjacency-legal, no corner-cut, walkable) — asserted by the differential test's validity check.
- PD-006 [AC-003] [FR-003] complete: because the heuristic is admissible and the search is the same A* loop bounded by `maxVisited` pops, `Landmarks.astar` agrees with `astar` on reachability within a budget that bounds out neither.

## Contract Impact
- PC-001 [PD-001] [PD-002] command report: Tier-1 public surface additions — `Pathfinding.Landmarks` (type) and `Landmarks.build`/`heuristic`/`astar` (members). Both surface baselines update with the `.fsi` and tests; the drift gate stays green. `astar`'s baseline line is unchanged. The `.fsi` is the contract (no `contracts/` dir).

## Verification Obligations
- VO-001 [PD-001] [PD-003] [PC-001] semanticTest: FsCheck differential over random bounded grids — `Landmarks.astar` equals `astar` in cost, returns a valid path, and agrees on reachability; plus an admissibility property (`heuristic <= true astar distance`). Fails before, passes after; build clean under warnings-as-errors; both baselines regenerated and committed.
- VO-002 [PD-002] [PD-004] semanticTest: a named `determinism` golden (byte-identical landmarks/path across runs) and a fewer-expansions proxy — a detour map + budget where `Landmarks.astar` returns `Some` while plain `astar` returns `None`.

## Migration Posture
- PM-001 [PC-001] diagnoseOnly: Plan schemaVersion 1 accepted; the `astar` refactor keeps its signature and output, and the additions remove/rename nothing, so no consumer migration.

## Generated View Impact
- GV-001 [PD-001] workModel: `readiness/017-pathfinding-alt-landmarks/work-model.json` refreshes from current plan sources or reports `staleGeneratedView`.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 017-pathfinding-alt-landmarks`.
