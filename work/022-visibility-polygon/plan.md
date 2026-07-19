---
schemaVersion: 1
workId: 022-visibility-polygon
title: 2D Visibility Polygon
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/022-visibility-polygon/spec.md
sourceClarifications: work/022-visibility-polygon/clarifications.md
sourceChecklist: work/022-visibility-polygon/checklist.md
publicOrToolFacingImpact: true
---

# 2D Visibility Polygon Plan

Prose status: planned

## Source Snapshot
- spec: work/022-visibility-polygon/spec.md sha256:b82c7a77c4163df8f716939c8df7a8f87987a86f81f98d306b2cfb9433676db9 schemaVersion:1
- clarifications: work/022-visibility-polygon/clarifications.md sha256:7a7553006a4c1b4509d80bb0988068e43f8b03020a223abf48d640c7723884c7 schemaVersion:1
- checklist: work/022-visibility-polygon/checklist.md sha256:138ae0374b2321072ebe65b5c5eeefb6bcce1f00873e43600feb6514192800f4 schemaVersion:1

## Technical Context
F# / new `FS.GG.Game.Harness.VisibilityPolygon` module (`VisibilityPolygon.fs`/`.fsi`), added to the
Harness compile order. Pure geometry over Core's `Point`/`Rect`; no ambient clock/RNG/IO (the harness
DependencyTests still pass). Float coordinates are the reason it lives in the harness, not Core.

## Constitution Check
- III (public surface): declare `VisibilityPolygon` in the `.fsi`; update the Harness surface baseline.
- V (pure): the polygon is a pure function of (origin, bounds, segments).
- VI (test evidence): the property suite fails before, passes after.

## Plan Scope
- Add `VisibilityPolygon.fs`/`.fsi`; register in the Harness `.fsproj`. Requirement count: 6.
  Clarification decision count: 3 (+1 accepted deferral). Checklist result count: 6.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: `polygon (origin: Point) (bounds: Rect) (segments: (Point*Point) list) : Point list`. Add the four `bounds` edges to the segments so every ray hits (DEC-003). For each endpoint of every segment, cast a ray from origin toward it at its pseudo-angle and at ±epsilon offsets (to see just past corners), take the nearest segment intersection by SQUARED distance, and collect the hit points (DEC-002).
- PD-002 [AC-005] [FR-005] complete: order the hit points by an integer-style **pseudo-angle** (quadrant + slope, computed from dx,dy without atan2), tie-break by squared distance — a deterministic total order, so the polygon is reproducible. No atan2, no float angle sort.
- PD-003 [AC-002] [FR-002] complete: because rays fan out over the full 2π and every ray hits a bounding edge, the ordered hit ring encloses the origin; a point-in-polygon test confirms the origin is inside.
- PD-004 [AC-001] [AC-004] [FR-001] [FR-004] complete: each hit is the NEAREST intersection along its ray, so the open segment origin->hit crosses no wall — every vertex is visible (star-shaped). A wall nearer than the bounds shortens the rays behind it, carving the shadow: a point behind the wall is outside the polygon.
- PD-005 [AC-003] [FR-003] complete: with no interior segments only the bounds edges remain, so the rays reach the bounds and the polygon area is within tolerance of the bounds area (a shoelace-area test).
- PD-006 [AC-006] [FR-006] complete: totality — a zero-length segment contributes no valid intersection (skipped), an empty interior segment list leaves the bounds, and an origin on/outside bounds still returns the (possibly degenerate) polygon without throwing (guarded denominators; parallel/near-parallel rays skipped).
- PD-007 [FR-005] complete: Promotion to Game.Core, exact-rational intersection arithmetic, and the full endpoint sweep are intentionally out of scope here — roadmap 2.4 stages this in the harness until the numeric contract is proven (advisory, not a lifecycle obligation).

## Contract Impact
- PC-001 [PD-001] command report: Tier-1 public surface addition — the `VisibilityPolygon` module in `FS.GG.Game.Harness`. The Harness surface baselines gain the type/members with the `.fsi` and tests; the new file joins the compile order; the drift gate stays green. No change to `Game.Core`.

## Verification Obligations
- VO-001 [PD-001] [PD-004] [PC-001] semanticTest: a property suite — origin inside (point-in-polygon), star-shaped (every vertex visible), empty-room area ≈ bounds, a wall casts a shadow (a behind point outside, an open point inside), and reproducibility (identical output). Fails before, passes after; build clean under warnings-as-errors; the Harness baseline regenerated and committed.
- VO-002 [PD-002] [PD-006] semanticTest: a named `determinism` golden (byte-identical polygon across runs) and totality cases (origin outside, empty/zero-length segments do not throw).

## Migration Posture
- PM-001 [PC-001] diagnoseOnly: Plan schemaVersion 1 accepted; a purely additive new harness module, so no consumer migration.

## Generated View Impact
- GV-001 [PD-001] workModel: `readiness/022-visibility-polygon/work-model.json` refreshes from current plan sources or reports `staleGeneratedView`.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.
- Promotion to `Game.Core`, exact-rational intersection arithmetic, and the full endpoint sweep are intentionally out of scope here — roadmap 2.4 stages this in the harness until the numeric contract is proven; a future work item owns the promotion. This is an advisory note, not a lifecycle obligation.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 022-visibility-polygon`.
