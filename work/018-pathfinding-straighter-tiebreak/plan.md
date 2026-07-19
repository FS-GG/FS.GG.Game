---
schemaVersion: 1
workId: 018-pathfinding-straighter-tiebreak
title: Straighter-Path Tie-Break
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/018-pathfinding-straighter-tiebreak/spec.md
sourceClarifications: work/018-pathfinding-straighter-tiebreak/clarifications.md
sourceChecklist: work/018-pathfinding-straighter-tiebreak/checklist.md
publicOrToolFacingImpact: true
---

# Straighter-Path Tie-Break Plan

Prose status: planned

## Source Snapshot
- spec: work/018-pathfinding-straighter-tiebreak/spec.md sha256:bceb626e029d621f9dd27140b4c2b56ff8f3ea78401c0b28cf8bfa87463a6b8d schemaVersion:1
- clarifications: work/018-pathfinding-straighter-tiebreak/clarifications.md sha256:d1029cfe3f67dc62f08f535626039cb08bf2dbb1786a6d4154f071509a58106d schemaVersion:1
- checklist: work/018-pathfinding-straighter-tiebreak/checklist.md sha256:d2dfb7d9daa3f66dcf8595723c240364b31f968548b933e9e2d2d1f539f641ee schemaVersion:1

## Technical Context
F# / `FS.GG.Game.Core.Pathfinding`. The shared `astarWith` engine gains a `tie: Cell -> int64`
parameter folded into the frontier key as `(f, h, tie c, Col, Row)`. `astar` and `Landmarks.astar`
pass `fun _ -> 0L` (a constant, so the order is identical to the old 4-tuple key — byte-identical
output). `astarStraight` passes the cross-product deviation term.

## Constitution Check
- III (public surface): declare `Pathfinding.astarStraight` in the `.fsi`; update both surface
  baselines. `astar`'s signature and baseline line are unchanged.
- V (MUE / pure): `astarStraight` is a pure function.
- VI (test evidence): the differential (equal cost) and the astar-byte-identity check fail before /
  pass after.
- "No silent output changes": the bias is a separate opt-in entry point; `astar` is untouched.

## Plan Scope
- Thread `tie` through `astarWith`; add `astarStraight`. Requirement count: 5. Clarification
  decision count: 3. Checklist result count: 5.

## Plan Decisions
- PD-001 [AC-003] [FR-003] complete: Add a `tie: Cell -> int64` parameter to the private `astarWith`, folded into the frontier key as `(f, h, tie c, Col, Row)`; `astar` and `Landmarks.astar` pass `fun _ -> 0L`. A constant 0 orders identically to the old `(f, h, Col, Row)` key, so `astar`'s output is byte-identical (DEC-002); the existing astar determinism/differential tests are the guard.
- PD-002 [AC-001] [FR-001] complete: `Pathfinding.astarStraight neighbourhood maxVisited isWalkable start goal` calls `astarWith` with `tie c = |dx1*dy2 - dx2*dy1|` (int64), where (dx1,dy1)=c-start and (dx2,dy2)=goal-start (DEC-001). Because `tie` sits after `f` and `h`, cost-optimality is unchanged, so the path cost equals `astar`'s.
- PD-003 [AC-004] [FR-004] complete: The cross term is zero on the straight line and grows with perpendicular deviation, so among equal-`(f,h)` frontier nodes the one nearest the line pops first — a straighter path. A test measures total deviation and asserts `astarStraight <= astar`, strictly less on an open off-axis query.
- PD-004 [AC-002] [FR-002] complete: `astarStraight` reuses the `astarWith` reconstruction, so every path is valid (start/goal-anchored, adjacency-legal, no corner-cut, walkable) — asserted by the differential validity check.
- PD-005 [AC-005] [FR-005] complete: The cross term is an integer `|dx1*dy2 - dx2*dy1|` in int64 (no overflow over the coordinate span, no float), and `(f, h, cross, Col, Row)` is a strict total order, so `astarStraight` is byte-deterministic; a golden pins run-to-run byte-identity.

## Contract Impact
- PC-001 [PD-002] command report: Tier-1 public surface addition — `Pathfinding.astarStraight`. Both surface baselines gain the one member with the `.fsi` and tests; `astar`'s baseline line is unchanged; the drift gate stays green. The `.fsi` is the contract (no `contracts/` dir).

## Verification Obligations
- VO-001 [PD-001] [PD-002] [PC-001] semanticTest: FsCheck differential over random grids — `astarStraight` equals `astar` in cost, is valid, and agrees on reachability; a byte-identity check that a captured `astar` golden is unchanged. Fails before, passes after; build clean under warnings-as-errors; both baselines regenerated and committed.
- VO-002 [PD-003] [PD-005] semanticTest: a straightness test (`astarStraight` total line-deviation <= `astar`, strictly less on an open off-axis query) and a named `determinism` golden for run-to-run byte-identity.

## Migration Posture
- PM-001 [PC-001] diagnoseOnly: Plan schemaVersion 1 accepted; `astar` is unchanged and the addition removes/renames nothing, so no consumer migration.

## Generated View Impact
- GV-001 [PD-002] workModel: `readiness/018-pathfinding-straighter-tiebreak/work-model.json` refreshes from current plan sources or reports `staleGeneratedView`.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 018-pathfinding-straighter-tiebreak`.
