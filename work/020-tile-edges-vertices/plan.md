---
schemaVersion: 1
workId: 020-tile-edges-vertices
title: Tile Edges and Vertices
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/020-tile-edges-vertices/spec.md
sourceClarifications: work/020-tile-edges-vertices/clarifications.md
sourceChecklist: work/020-tile-edges-vertices/checklist.md
publicOrToolFacingImpact: true
---

# Tile Edges and Vertices Plan

Prose status: planned

## Source Snapshot
- spec: work/020-tile-edges-vertices/spec.md sha256:b3fcf9753b6186f643ec149bace1235a092cce8554d8dad49e9d38e40cda897d schemaVersion:1
- clarifications: work/020-tile-edges-vertices/clarifications.md sha256:8f67e417e34338ce92c03e426db6eb2c8a08e6b71d2101f5c2ba029b9a5d6baf schemaVersion:1
- checklist: work/020-tile-edges-vertices/checklist.md sha256:5886ceee2dc318c3ab487d7629b6623ab21f978edd0918730bebd4d32db014f6 schemaVersion:1

## Technical Context
F# / new `FS.GG.Game.Core.Edges` module (`Edges.fs`/`.fsi`), added to the compile order after
`Pathfinding`. Self-contained integer addressing + a compact edge-aware BFS/A* reusing the total-order
frontier discipline. No change to existing modules.

## Constitution Check
- III (public surface): declare the `Edges` surface in `Edges.fsi` before `.fs`; add to both baselines.
- IV/V (idiomatic, pure): plain integer formulas; searches pure.
- VI (test evidence): the wall-scenario + property suite fail before, pass after.

## Plan Scope
- Add `Edges.fs`/`.fsi`; register in `FS.GG.Game.Core.fsproj`. Requirement count: 6. Clarification
  decision count: 3. Checklist result count: 6.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: `type Dir = North | East | South | West` with offsets N=(0,-1), E=(1,0), S=(0,1), W=(-1,0) (DEC-002). `type Edge = { Lo: Cell; Hi: Cell }` sorted (DEC-001); `edgeBetween a b` = `Some { min; max }` iff orthogonally adjacent (order-independent), else `None`; `edgeOf cell dir = edgeBetween cell (step cell dir)`; `edgeCells edge = (edge.Lo, edge.Hi)`.
- PD-002 [AC-002] [FR-002] complete: `type Vertex = { VCol: int; VRow: int }`; vertex (c,r) is cell (c,r)'s NW corner (DEC-003). `borders cell` = the 4 `edgeOf cell dir` in N,E,S,W order; `corners cell` = the 4 vertices (c,r),(c+1,r),(c,r+1),(c+1,r+1) in NW,NE,SW,SE order.
- PD-003 [AC-003] [FR-003] complete: `edgeEndpoints edge` = the two shared vertices of the edge; `vertexCells v` = the <=4 cells (v-(1,1)),(v-(0,1)... i.e. (VCol-1,VRow-1),(VCol,VRow-1),(VCol-1,VRow),(VCol,VRow); `vertexEdges v` = the 4 edges around v. Mutual consistency (edge in borders c iff c in edgeCells edge; vertex in corners c iff c in vertexCells v) is a property test.
- PD-004 [AC-004] [FR-004] complete: `isEdgePassable (walls: Set<Edge>) a b` = `match edgeBetween a b with Some e -> not (Set.contains e walls) | None -> true`. Symmetric because `edgeBetween` is; a wall stored once (canonical) blocks both directions.
- PD-005 [AC-005] [FR-005] complete: `Edges.bfs`/`Edges.astar (walls) maxVisited isWalkable start goal` search over the 4 orthogonal neighbours filtered by `isWalkable` AND `isEdgePassable walls current n`, endpoint-inclusive, `maxVisited`-bounded, total-order frontier. With empty `walls` they match plain `Pathfinding.bfs` reachability/hop-count; a walled edge forces a detour.
- PD-006 [AC-006] [FR-006] complete: every function is a pure integer formula or a deterministic search over a fixed neighbour order; a determinism golden pins byte-identity.

## Contract Impact
- PC-001 [PD-001] command report: Tier-1 public surface addition — the `Edges` module (Dir/Edge/Vertex types + ~11 functions). Both surface baselines gain the new types and members with the `.fsi` and tests; the new file joins the compile order; the drift gate stays green. The `.fsi` is the contract (no `contracts/` dir).

## Verification Obligations
- VO-001 [PD-001] [PD-005] [PC-001] semanticTest: a property suite — edge canonicalisation/order-independence, borders/corners cardinality + order, relationship mutual consistency, isEdgePassable symmetry, and the edge-aware wall scenario (a wall forces a detour; empty walls match plain bfs). Fails before, passes after; build clean under warnings-as-errors; both baselines regenerated and committed.
- VO-002 [PD-006] semanticTest: a named `determinism` golden for the relationships and the edge-aware search byte-identity across runs.

## Migration Posture
- PM-001 [PC-001] diagnoseOnly: Plan schemaVersion 1 accepted; a purely additive new module, so no consumer migration.

## Generated View Impact
- GV-001 [PD-001] workModel: `readiness/020-tile-edges-vertices/work-model.json` refreshes from current plan sources or reports `staleGeneratedView`.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 020-tile-edges-vertices`.
