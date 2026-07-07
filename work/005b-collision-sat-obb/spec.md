---
schemaVersion: 1
workId: 005b-collision-sat-obb
title: SAT/OBB Convex Collision Manifolds
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# SAT/OBB Convex Collision Manifolds Specification

Prose status: specified

## User Value
Game code can test two convex polygons — including oriented bounding boxes built via a constructor —
for overlap and get the penetration manifold as a pure `Contact` value: the minimum-translation-vector
(MTV) `Normal` (unit, oriented from `a` toward `b`) and `Depth` (positive penetration). This powers
rotated-body and arbitrary-convex collision, completing the convex half of the narrow phase and
reusing the same `Contact` shape the 004 manifolds already produce so the 006 resolution layer sees
one manifold contract.

## Scope
- SB-001: Convex-vs-convex **narrow-phase manifolds** in `FS.GG.Game.Core.Geometry` via SAT — a new
  `ConvexPolygon` primitive, an `obbPolygon` constructor (center, half-extents, rotation), and
  `polygonContact` returning the existing `Contact` (MTV `Normal` + `Depth`).
- SB-002: Reuse the constraint-face harness (FsCheck invariants + filter-named determinism goldens
  bound to the `gate.yml` zero-match guard) and the surface-baseline-drift gate.

## Non-Goals
- SB-003: No non-convex/concave polygons; no polygon–circle or polygon–AABB cross manifolds (this
  slice is convex↔convex, with AABB/OBB expressible as polygons); no GJK/EPA (SAT only); no
  resolution/response (→ 006); no broad-phase (→ 007); no architecture shell or sample; no
  `capabilities.yml` domain wiring (→ 008).

## User Stories
- US-001 (P1): As game-logic code, I can test two convex polygons for overlap and, on a hit, receive
  the minimum-translation vector as a `Contact`, so I can detect and later resolve collisions between
  arbitrary convex bodies.
- US-002 (P1): As game-logic code, I can build a `ConvexPolygon` for an oriented bounding box from a
  center, half-extents, and rotation, so I can collide rotated rectangular bodies without hand-rolling
  vertex math.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given two convex polygons overlapping on positive area, when `polygonContact` is called, then it returns `Some`; given polygons that only touch (a separating axis with zero overlap) or are disjoint, then it returns `None`.
- AC-002 [US-001] [FR-002]: Given two overlapping convex polygons, when `polygonContact` returns a hit, then `Normal` is a unit vector along the least-penetration axis oriented toward the nearer exit (the a→b least-penetration direction) and `Depth` is that penetration — the true exit distance, so that even when one polygon nests inside the other (full containment) translating `a` by `−Normal × Depth` removes the overlap (separates the polygons).
- AC-003 [US-002] [FR-003]: Given a center, half-extents, and a rotation angle, when `obbPolygon` is called, then it returns a 4-vertex CCW `ConvexPolygon` whose vertices are the rotated box corners, and colliding two such OBBs through `polygonContact` yields a `Contact` consistent with the axis-aligned case when rotation is zero.
- AC-004 [US-001] [FR-004]: Given a degenerate input (a polygon with fewer than 3 vertices, zero area, or any NaN coordinate), when `polygonContact` is called, then it returns `None` and never throws.
- AC-005 [US-001] [FR-005]: Given candidate SAT axes that include antiparallel or duplicate directions (opposite OBB faces, shared orientations), when `polygonContact` computes the MTV, then those axes are deduplicated to a minimal set so the minimum-overlap selection is over distinct axes.
- AC-006 [US-001] [FR-006]: Given two convex polygons whose minimum overlap is equal on more than one candidate axis, when `polygonContact` is called, then the tie is broken by the fixed candidate-axis generation order (a-edges before b-edges, in vertex order), and identical inputs yield a byte-identical `Contact` across runs and platforms.

## Functional Requirements
- FR-001: `polygonContact` MUST return `Some Contact` exactly when the two convex polygons overlap on positive area (SAT finds no separating axis with non-positive overlap) and `None` when any candidate axis separates them (a gap, or a touch with zero overlap). (covers AC-001)
- FR-002: On a hit, `polygonContact`'s `Contact` MUST carry `Normal` = the unit least-penetration axis oriented along the nearer exit (the a→b least-penetration direction) and `Depth` = that penetration computed with the full-containment correction (the true exit distance, not the naive interval overlap), such that translating `a` by `−Normal × Depth` separates the polygons — including when one nests inside the other (the MTV property). (covers AC-002)
- FR-003: `obbPolygon center halfExtents rotation` MUST return a 4-vertex CCW-wound `ConvexPolygon` of the rotated box corners, equal (up to vertex ordering) to the axis-aligned box when `rotation = 0`. (covers AC-003)
- FR-004: `polygonContact` MUST be pure and total: a polygon with fewer than 3 vertices, a zero-area polygon, or any NaN coordinate yields `None` without throwing. (covers AC-004)
- FR-005: `polygonContact` MUST deduplicate candidate SAT axes — collapsing antiparallel and duplicate edge-normal directions to a single canonical axis before projection — so the minimum-overlap selection ranges over distinct axes. (covers AC-005)
- FR-006: `polygonContact` MUST be byte-deterministic, with a documented tie-break when the minimum overlap is equal on more than one candidate axis: the first axis in the fixed generation order (a's edge normals in vertex order, then b's) wins. (covers AC-006)

## Ambiguities
- AMB-001: `ConvexPolygon` representation — a public `ConvexPolygon = { Vertices: Point[] }` record
  (documented convex, CCW convention) versus a validated/smart-constructor type. (Affects the surface
  baseline. Proposed: a plain record with a documented input convention, matching `Circle`/`Rect`.)
- AMB-002: `Normal` orientation + depth under containment — orient by a **centroid delta** with the
  naive interval overlap as depth, versus by the **nearer-exit** direction with the full-containment
  correction as depth. (Proposed: nearer-exit + containment correction, so `−Normal × Depth` truly
  separates even when one polygon nests inside the other; this is the a→b least-penetration direction
  for the winning axis and is what the MTV property in AC-002 actually requires.)
- AMB-003: Equal-minimum-overlap tie-break — which axis wins when two candidate axes give the same
  overlap? (Proposed: the first in the fixed generation order — a's edge normals in vertex order, then
  b's — consistent with the deterministic tie-breaks of 004.)
- AMB-004: `obbPolygon` winding and rotation sign — CCW vertices, rotation as a counter-clockwise
  angle in radians. (Proposed: CCW winding, CCW-positive radians, so `polygonContact`'s CCW convention
  is satisfied by construction.)

## Public Or Tool-Facing Impact
- Tier 1 (contracted). Adds a public `ConvexPolygon` type plus `obbPolygon` and `polygonContact`
  signatures, so `.fsi`, the surface baseline (`readiness/surface-baselines/FS.GG.Game.Core.txt`),
  and tests land together; the surface-baseline-drift gate must stay green.

## Lifecycle Notes
- Determinism golden tests MUST carry a `determinism golden` name substring for the `gate.yml`
  zero-match guard.
- Reuses the existing `Contact` type — no new manifold shape; the 006 resolution layer consumes it
  unchanged.
- Next lifecycle action: `fsgg-sdd clarify --work 005b-collision-sat-obb`.
