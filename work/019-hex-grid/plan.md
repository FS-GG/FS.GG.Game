---
schemaVersion: 1
workId: 019-hex-grid
title: Hexagonal Grid Module
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/019-hex-grid/spec.md
sourceClarifications: work/019-hex-grid/clarifications.md
sourceChecklist: work/019-hex-grid/checklist.md
publicOrToolFacingImpact: true
---

# Hexagonal Grid Module Plan

Prose status: planned

## Source Snapshot
- spec: work/019-hex-grid/spec.md sha256:c7eb96c6ac4da1fc7cbf627ed6ababd62210e0dcc2f63f7ec0eccd8c6a534a59 schemaVersion:1
- clarifications: work/019-hex-grid/clarifications.md sha256:2b31d6c2282aa6539c0bcb5f713d534ec761bfd30cc08a640b6800d09b2c92c9 schemaVersion:1
- checklist: work/019-hex-grid/checklist.md sha256:d0debff92dbf060edb647f278930875ce38c94c5729aeb0de44286d009f4f8f5 schemaVersion:1

## Technical Context
F# / new `FS.GG.Game.Core.Hex` module (`Hex.fs`/`.fsi`), added to the `FS.GG.Game.Core` compile order
after `Primitives`. Self-contained: integer cube coordinates and a compact hex A*/BFS reusing the same
total-order frontier discipline as the square `Pathfinding`. No change to existing modules.

## Constitution Check
- III (public surface): declare the whole `Hex` surface in `Hex.fsi` before `.fs`; add to both surface
  baselines. New `.fsi` file.
- IV (idiomatic simplicity), V (pure): all functions pure; cube arithmetic is plain integer.
- VI (test evidence): the property suite fails before (no module) and passes after.

## Plan Scope
- Add `Hex.fs`/`.fsi`; register in `FS.GG.Game.Core.fsproj`. Requirement count: 8. Clarification
  decision count: 3. Checklist result count: 8.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: `type Hex = { Q: int; R: int; S: int }`; `Hex.create q r = { Q = q; R = r; S = -q - r }` (DEC-001), so `q + r + s = 0` by construction; `Hex.origin`.
- PD-002 [AC-002] [FR-002] complete: `distance a b = (abs(dq) + abs(dr) + abs(ds)) / 2` in int64 (wide-span safe), = `max(abs dq, abs dr, abs ds)`. Integer, total.
- PD-003 [AC-003] [FR-003] complete: fixed `directions` list of 6 cube unit vectors; `neighbours h` maps `add h` over them in order; `add`/`subtract`/`scale` are componentwise integer.
- PD-004 [AC-004] [FR-004] complete: `rotateRight { Q; R; S } = { Q = -S; R = -Q; S = -R }` and `rotateLeft = { Q = -R; R = -S; S = -Q }` (DEC-001's cube form); six applications are the identity, one preserves origin distance.
- PD-005 [AC-005] [FR-005] complete: `range n` enumerates `{ q in -n..n, r in max(-n,-q-n)..min(n,-q+n) }` (cardinality 3n(n+1)+1); `ring n` walks the 6 edges from a fixed corner (`6n`, or `[origin]` at 0); `spiral n` = origin then ring 1..n — all fixed order.
- PD-006 [AC-006] [FR-006] complete: `round (fq, fr, fs)` rounds each, fixes the largest-|delta| component to keep the invariant, ties in the order S,Q,R (DEC-003); `lineDraw a b` = `distance+1` samples via cube lerp + `round` (deterministic IEEE lerp with the documented round tie-break), each consecutive pair adjacent.
- PD-007 [AC-007] [FR-007] complete: `toOffset`/`ofOffset` use odd-r layout, `toDoubled`/`ofDoubled` the doubled scheme (DEC-002); each pair is an exact integer inverse.
- PD-008 [AC-008] [FR-008] complete: `Hex.bfs`/`Hex.astar` search over `neighbours` filtered by `isWalkable`, endpoint-inclusive, `maxVisited`-bounded, frontier ordered by a total integer key (`astar` uses `distance` as the heuristic; `bfs` is unweighted), so paths are shortest-hop and byte-deterministic and the two agree on reachability.

## Contract Impact
- PC-001 [PD-001] command report: Tier-1 public surface addition — the entire `Hex` module (type + ~18 functions). Both surface baselines gain the new type and members with the `.fsi` and tests; the new file joins the compile order; the drift gate stays green. The `.fsi` is the contract (no `contracts/` dir).

## Verification Obligations
- VO-001 [PD-001] [PD-008] [PC-001] semanticTest: an Expecto/FsCheck property suite — the cube invariant, distance/neighbour laws, rotation 6-cycles, range/ring/spiral cardinalities, line contiguity + length, converter round-trips, and hex-pathfinding optimality (`astar`/`bfs` length = distance + 1 on an open hex grid) + reachability agreement + determinism. Fails before, passes after; build clean under warnings-as-errors; both baselines regenerated and committed.
- VO-002 [PD-006] [PD-008] semanticTest: named `determinism` goldens for `round`/`lineDraw` and for hex pathfinding byte-identity across runs.

## Migration Posture
- PM-001 [PC-001] diagnoseOnly: Plan schemaVersion 1 accepted; a purely additive new module, so no consumer migration.

## Generated View Impact
- GV-001 [PD-001] workModel: `readiness/019-hex-grid/work-model.json` refreshes from current plan sources or reports `staleGeneratedView`.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 019-hex-grid`.
