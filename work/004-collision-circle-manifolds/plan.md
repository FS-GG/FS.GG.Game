---
schemaVersion: 1
workId: 004-collision-circle-manifolds
title: Collision Circle Manifolds
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/004-collision-circle-manifolds/spec.md
sourceClarifications: work/004-collision-circle-manifolds/clarifications.md
sourceChecklist: work/004-collision-circle-manifolds/checklist.md
publicOrToolFacingImpact: true
---

# Collision Circle Manifolds Plan

Prose status: planned

## Source Snapshot
- spec: work/004-collision-circle-manifolds/spec.md sha256:19b0c711e26401b431eb1812b90f8c3baba2a8fc01f4caa48c6ff4bd90781e8f schemaVersion:1
- clarifications: work/004-collision-circle-manifolds/clarifications.md sha256:3a99ff55817aff3904f864a8ea830fb24d377d80661725008c0c8a37ccfdbf18 schemaVersion:1
- checklist: work/004-collision-circle-manifolds/checklist.md sha256:b50f59bf4e605692d52696b727c000a4a65b0bbe70894f61b6f2db44998cf9f8 schemaVersion:1

## Plan Scope
- Deepen `FS.GG.Game.Core.Geometry` with two circle narrow-phase manifolds, on a new `Circle`
  primitive (DEC-001), returning the shipped `Contact` value. Pure/total/deterministic; detection
  only. Mirrors the `aabbContact` implementation and its invariant harness (PR #12).
- Requirement count: 7. Clarification decision count: 3. Checklist result count: 7.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: Add `type Circle = { Center: Point; Radius: float }` to `Primitives` (DEC-001) and `Geometry.circleContact (a: Circle) (b: Circle) : Contact option`. Overlap test uses the **squared** distance: `Some` iff `dx*dx+dy*dy < (rA+rB)²` (strict; touch/gap ⇒ `None`).
- PD-002 [AC-002] [FR-002] complete: On overlap, distance `d = sqrt(dx²+dy²)` (a single correctly-rounded IEEE sqrt — deterministic across platforms). `Normal = (dx/d, dy/d)`, `Depth = (rA+rB) − d`. Coincident centers (`d = 0`, DEC-002) ⇒ `Normal = (1,0)`, `Depth = rA+rB`.
- PD-003 [AC-003] [FR-003] complete: `Geometry.circleAabbContact (c: Circle) (r: Rect) : Contact option`. Clamp `c.Center` to `r`; `Some` iff squared distance from center to the clamp `< radius²` (strict).
- PD-004 [AC-004] [FR-004] complete: Center-inside-box (clamp == center, squared distance 0) fallback: choose the least-penetration box face as the normal axis; equal-penetration tie-break ⇒ X axis, +bias (DEC-003, identical to `aabbContact`). `Depth` = that face penetration + radius.
- PD-005 [AC-005] [FR-005] complete: Totality guards — a non-positive radius or any NaN operand makes the strict `<` comparison false, yielding `None` without throwing (same NaN-safety idiom as `intersects`/`aabbContact`).
- PD-006 [AC-006] [FR-006] complete: Determinism — squared comparisons for the boolean test; the only sqrt is the correctly-rounded manifold distance; tie-breaks fixed per DEC-002/DEC-003. Golden tests carry a `determinism golden` name substring so the `gate.yml` zero-match guard covers them.
- PD-007 [AC-007] [FR-007] complete: MTV validity — a property test translates the first shape by `−Normal×Depth` and asserts the positive-area overlap is removed, matching the `aabbContact` "MTV separates" property.

## Contract Impact
- PC-001 [PD-001] [PD-003] command report: Tier-1 public surface additions to `FS.GG.Game.Core` — the `Circle` type (`Primitives.fs`/`.fsi`) and `circleContact`/`circleAabbContact` (`Geometry.fs`/`.fsi`). The surface baseline `readiness/surface-baselines/FS.GG.Game.Core.txt` gains `FS.GG.Game.Core.Circle`; `.fsi`, baseline, and tests land together. The `.fsi` is the contract (no separate `contracts/` dir).
- PC-002 [PD-006] command report: The determinism golden naming convention keeps the `gate.yml` "Determinism & property invariants" filter covering the new goldens — no workflow change required.

## Verification Obligations
- VO-001 [PD-001] [PC-001] semanticTest: Expecto/FsCheck invariants (isSome≡overlap, unit normal + correct depth, center-inside fallback, NaN/zero-radius totality, MTV-separates) that fail before and pass after; a named determinism golden set; `dotnet build` clean under warnings-as-errors; surface baseline regenerated and committed; the full and filtered (`--filter FullyQualifiedName~circle`) suites green.

## Migration Posture
- PM-001 [PC-001] diagnoseOnly: Plan schemaVersion 1 is accepted; unsupported plan schemas diagnose before write.

## Generated View Impact
- GV-001 [PD-001] workModel: readiness/004-collision-circle-manifolds/work-model.json refreshes from current plan sources or reports staleGeneratedView.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 004-collision-circle-manifolds`.
