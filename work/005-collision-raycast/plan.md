---
schemaVersion: 1
workId: 005-collision-raycast
title: Collision Raycast
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/005-collision-raycast/spec.md
sourceClarifications: work/005-collision-raycast/clarifications.md
sourceChecklist: work/005-collision-raycast/checklist.md
publicOrToolFacingImpact: true
---

# Collision Raycast Plan

Prose status: planned

## Source Snapshot
- spec: work/005-collision-raycast/spec.md sha256:96179c7938e9a15906f3b12ca43b4aa3aa4603c84d4bfe480f4f937ba77e1e5a schemaVersion:1
- clarifications: work/005-collision-raycast/clarifications.md sha256:c3fcada85c40eb0413eab3d3c9b2a9b299ef460f3526bb78b86f4ee1b5e5a25e schemaVersion:1
- checklist: work/005-collision-raycast/checklist.md sha256:b44a0ffe398f354537e9bd98ce57692e49eab8a3ac2a6e75a061b133d9624601 schemaVersion:1

## Plan Scope
- Add segment-cast queries to `FS.GG.Game.Core.Geometry` on a new `RayHit` value (DEC-001), reusing
  the slab clip idiom already in `sweptIntersects`. Pure/total/deterministic; queries only.
- Requirement count: 6. Clarification decision count: 3. Checklist result count: 6.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: `Geometry.segmentAabbHit (p0: Point) (p1: Point) (box: Rect) : RayHit option`. Slab method over `d = p1 − p0`: per-axis `tNear/tFar` (parallel-axis branch when a component is 0), `tEnter = max tNear`, `tExit = min tFar`. `Some` iff `tEnter ≤ tExit` and `tEnter ∈ [0,1]` (entry from outside, DEC-002); else `None`.
- PD-002 [AC-002] [FR-002] complete: The hit carries `T = tEnter`, `Point = p0 + d × tEnter`, and `Normal` = the outward unit axis of the entered face (the axis whose `tNear` was the max; sign opposite `d` on that axis). Corner tie (equal per-axis `tNear`) resolves to the X face (DEC-003).
- PD-003 [AC-003] [FR-003] complete: `Geometry.segmentCircleHit (p0: Point) (p1: Point) (c: Circle) : RayHit option`. Quadratic on `d = p1 − p0`, `f = p0 − c.Center`: `a = d·d`, `b = 2 f·d`, `cc = f·f − r²`, `disc = b² − 4a·cc`. `Some` iff `a > 0`, `r > 0`, `disc ≥ 0`, and the near root `t = (−b − sqrt disc) / (2a) ∈ [0,1]` (DEC-002); else `None`.
- PD-004 [AC-004] [FR-004] complete: The hit carries `T = t`, `Point = p0 + d × t`, and `Normal = (Point − c.Center) / r` (unit; `r > 0` guaranteed by the guard).
- PD-005 [AC-005] [FR-005] complete: Totality — a zero-length segment (`a = 0` / `d = 0`), a NaN operand, or a non-positive radius fails the guards/strict comparisons and yields `None` (same NaN-safety idiom as `sweptIntersects`/`aabbContact`).
- PD-006 [AC-006] [FR-006] complete: Determinism — the slab divisions and the single quadratic `sqrt` are correctly-rounded IEEE ops; the only ambiguity (AABB corner entry) is fixed to the X face (DEC-003). Golden tests carry a `determinism golden` name substring for the `gate.yml` guard.

## Contract Impact
- PC-001 [PD-001] [PD-003] command report: Tier-1 public surface additions to `FS.GG.Game.Core` — the `RayHit` type (`Primitives.fs`/`.fsi`) and `segmentAabbHit`/`segmentCircleHit` (`Geometry.fs`/`.fsi`). The surface baseline gains `FS.GG.Game.Core.RayHit`; `.fsi`, baseline, and tests land together. The `.fsi` is the contract.
- PC-002 [PD-006] command report: The determinism golden naming convention keeps the `gate.yml` determinism filter covering the new goldens — no workflow change.

## Verification Obligations
- VO-001 [PD-001] [PC-001] semanticTest: Expecto/FsCheck invariants (Some⟺entry-from-outside, `T ∈ [0,1]`, hit point on surface, unit normal, zero-length/NaN/non-positive-radius totality) that fail before and pass after; a named determinism golden set; `dotnet build` clean under warnings-as-errors; surface baseline regenerated and committed; the full and filtered suites green.

## Migration Posture
- PM-001 [PC-001] diagnoseOnly: Plan schemaVersion 1 is accepted; unsupported plan schemas diagnose before write.

## Generated View Impact
- GV-001 [PD-001] workModel: readiness/005-collision-raycast/work-model.json refreshes from current plan sources or reports staleGeneratedView.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 005-collision-raycast`.
