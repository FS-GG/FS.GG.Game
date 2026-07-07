---
schemaVersion: 1
workId: 005b-collision-sat-obb
title: SAT/OBB Convex Collision Manifolds
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/005b-collision-sat-obb/spec.md
sourceClarifications: work/005b-collision-sat-obb/clarifications.md
sourceChecklist: work/005b-collision-sat-obb/checklist.md
publicOrToolFacingImpact: true
---

# SAT/OBB Convex Collision Manifolds Plan

Prose status: planned

## Source Snapshot
- spec: work/005b-collision-sat-obb/spec.md sha256:5a16d00b732ceb7eb578cf98183c18095ee2cc4ee3f0f56666f1facee4ac8c43 schemaVersion:1
- clarifications: work/005b-collision-sat-obb/clarifications.md sha256:c046a498cda29b72035cab2adeef68d29b6181be112537472cea9a71634e573c schemaVersion:1
- checklist: work/005b-collision-sat-obb/checklist.md sha256:6fc7fc61cffb8d76e5902daa503fcf163327ffc28de3d90322818b18dde0d236 schemaVersion:1

## Plan Scope
- Add convex-vs-convex SAT manifolds to `FS.GG.Game.Core.Geometry` on a new `ConvexPolygon` value
  (DEC-001), reusing the existing `Contact` type and the a→b / MTV convention proven by
  `aabbContact`/`circleContact`. Pure/total/deterministic; detection only.
- Requirement count: 6. Clarification decision count: 4. Checklist result count: 6.

## Design
- `ConvexPolygon = { Vertices: Point[] }` — a plain public record in `Primitives.fs`/`.fsi` (DEC-001).
  Documented convention: convex, CCW-wound; convexity/winding are input assumptions, not enforced.
- Outward edge normals: for CCW winding, edge `e = v[i+1] − v[i]` has outward normal `(e.Y, −e.X)`
  (verified against `obbPolygon`'s bottom edge → −Y). Each is normalized; zero-length or NaN edges
  yield no axis and are skipped.
- SAT projection: for a unit axis `n`, each polygon projects to `[min, max]` of `v · n`. The signed
  exit distances are `d1 = maxA − minB` (push a toward −n) and `d2 = maxB − minA` (push a toward +n);
  the axis overlaps iff both are `> 0.0`. Any axis with `not (d1 > 0 && d2 > 0)` (a gap, a touch, or
  a NaN interval) is separating ⇒ `None`. The axis penetration is `min(d1, d2)` and the least
  penetration over all axes is the MTV; `Depth` = that penetration.
- Normal orientation + containment correction (DEC-002): the normal is oriented toward the nearer
  exit (`n` when `d1 ≤ d2`, else `−n`), so `−Normal × Depth` separates. Using `min(d1, d2)` rather
  than the naive `min(maxA,maxB) − max(minA,minB)` is the design's **full-containment correction**:
  when one interval nests inside the other the naive form understates the depth, but `min(d1, d2)` is
  the true distance to push the nested shape out. No centroid heuristic (it fails under containment).
- Candidate-axis order + dedup (DEC-003, FR-005): generate `a`'s edge normals in vertex order, then
  `b`'s; canonicalize each to a sign-stable direction and drop near-duplicates so the axis set is
  minimal; the min-overlap scan keeps the first axis in this order on an exact tie (byte-stable).

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: `Geometry.polygonContact (a: ConvexPolygon) (b: ConvexPolygon) : Contact option`. SAT over the deduped candidate axes: project both polygons onto each unit axis; `Some` iff every axis has `overlap > 0.0` (positive-area overlap); any non-positive/NaN overlap (gap or touch) ⇒ `None`. Strict-edge convention matches `intersects`/`aabbContact`.
- PD-002 [AC-002] [FR-002] complete: On a hit, the `Contact` carries `Depth` = the minimum over axes of `min(d1, d2)` (the full-containment correction) and `Normal` = the unit axis oriented toward the nearer exit (the a→b least-penetration direction, DEC-002), so translating `a` by `−Normal × Depth` separates the polygons even under full containment (MTV property).
- PD-003 [AC-003] [FR-003] complete: `Geometry.obbPolygon (center: Point) (halfExtents: Point) (rotation: float) : ConvexPolygon` builds 4 CCW corners `center + R(rotation)·(±hx, ±hy)` in CCW order `(−hx,−hy),(hx,−hy),(hx,hy),(−hx,hy)` (DEC-004); at `rotation = 0` this is the axis-aligned box, so two such OBBs agree with the AABB case.
- PD-004 [AC-004] [FR-004] complete: Totality — `< 3` vertices short-circuits to `None`; a zero-area polygon yields no valid axis (all edge normals degenerate) ⇒ `None`; any NaN coordinate makes projections NaN so `overlap > 0.0` is false ⇒ `None`. Same NaN-safety idiom as `aabbContact` (comparisons, not exceptions).
- PD-005 [AC-005] [FR-005] complete: Candidate SAT axes are deduplicated — antiparallel/duplicate edge-normal directions (opposite OBB faces, shared orientations) collapse to one canonical axis before projection — so min-overlap ranges over distinct axes. Correctness does not depend on dedup (duplicate axes give equal overlap and the tie-break selects the same axis), but the set is kept minimal.
- PD-006 [AC-006] [FR-006] complete: Determinism — SAT dot products and the per-axis `sqrt` normalization are correctly-rounded IEEE ops; the only choice (equal minimum overlap on two axes) is fixed to the first axis in the a-then-b vertex-order generation order (DEC-003). Golden tests carry a `determinism golden` name substring for the `gate.yml` guard.

## Contract Impact
- PC-001 [PD-001] [PD-003] command report: Tier-1 public surface additions to `FS.GG.Game.Core` — the `ConvexPolygon` type (`Primitives.fs`/`.fsi`) and `obbPolygon`/`polygonContact` (`Geometry.fs`/`.fsi`). The surface baseline gains `FS.GG.Game.Core.ConvexPolygon`; `.fsi`, baseline, and tests land together. The `.fsi` is the contract.
- PC-002 [PD-006] command report: The determinism golden naming convention keeps the `gate.yml` determinism filter covering the new goldens — no workflow change.

## Verification Obligations
- VO-001 [PD-001] [PC-001] semanticTest: Expecto/FsCheck invariants (Some⟺positive-area overlap agreeing with an independent oracle for AABB-as-polygon vs `aabbContact`; unit normal; positive depth; MTV separates; a-then-b tie order; `< 3`-vertex / zero-area / NaN totality) that fail before and pass after; a named determinism golden set; `dotnet build` clean under warnings-as-errors; surface baseline regenerated and committed; the full and filtered suites green.

## Migration Posture
- PM-001 [PC-001] diagnoseOnly: Plan schemaVersion 1 is accepted; unsupported plan schemas diagnose before write.

## Generated View Impact
- GV-001 [PD-001] workModel: readiness/005b-collision-sat-obb/work-model.json refreshes from current plan sources or reports staleGeneratedView.
- GV-002 [PC-001] surfaceBaseline: `readiness/surface-baselines/FS.GG.Game.Core.txt` regenerates to include `ConvexPolygon`, `obbPolygon`, and `polygonContact`; the surface-baseline-drift gate must stay green.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 005b-collision-sat-obb`.
