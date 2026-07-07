---
schemaVersion: 1
workId: 005b-collision-sat-obb
stage: clarify
sourceSpec: work/005b-collision-sat-obb/spec.md
---

# Clarifications

## Source Specification
- work/005b-collision-sat-obb/spec.md

## Clarification Questions
- **CQ-001** (AMB-001): Should `ConvexPolygon` be a plain public record or a validated smart-constructor type?
- **CQ-002** (AMB-002): How are the `Contact.Normal` orientation and `Depth` chosen so `−Normal × Depth` separates even under full containment?
- **CQ-003** (AMB-003): When two candidate axes give an equal minimum overlap, which axis wins?
- **CQ-004** (AMB-004): What winding and rotation-sign convention does `obbPolygon` produce?

## Answers
- CQ-001 → A plain public `ConvexPolygon = { Vertices: Point[] }` record, matching the `Point`/`Rect`/`Circle` primitives: convexity and CCW winding are a documented input convention, not runtime-enforced, and a degenerate ring (fewer than 3 vertices, zero area, or NaN) is a no-contact input (`polygonContact` returns `None`) rather than a construction-time error (resolves AMB-001).
- CQ-002 → Per axis, from the two signed exit distances `d1 = aMax − bMin` (push a toward −axis) and `d2 = bMax − aMin` (push a toward +axis): the axis penetration is `min(d1, d2)` and the normal is oriented toward that nearer exit. `min(d1, d2)` is the full-containment correction — when one interval nests inside the other, the naive `min(aMax,bMax) − max(aMin,bMin)` understates the depth, but `min(d1, d2)` is the true distance to push the nested shape out. This is the a→b least-penetration direction for the winning axis and is exactly what makes `−Normal × Depth` separate the pair (a centroid-sign heuristic fails this under containment) (resolves AMB-002).
- CQ-003 → The first axis in the fixed candidate-axis generation order — `a`'s edge normals in vertex order, then `b`'s — wins. This is a single, documented, cross-run-stable rule, consistent with the deterministic tie-breaks of 004 (resolves AMB-003).
- CQ-004 → CCW winding, rotation as a CCW-positive angle in radians. Building the OBB CCW by construction satisfies `polygonContact`'s CCW input convention, so OBBs never need re-winding (resolves AMB-004).

## Decisions
- **DEC-001** [CQ-001] [AMB:AMB-001] [FR-004]: `ConvexPolygon = { Vertices: Point[] }` is a plain public record; convex + CCW is a documented convention, and degenerate rings return `None` from `polygonContact` rather than throwing. Tier-1 surface addition (baseline + `.fsi` together).
- **DEC-002** [CQ-002] [AMB:AMB-002] [FR-002]: `Contact.Depth` is the minimum over axes of `min(d1, d2)` (the full-containment correction) and `Contact.Normal` is the unit axis oriented toward that nearer exit — the a→b least-penetration direction — so `−Normal × Depth` separates the pair even under full containment. No centroid heuristic.
- **DEC-003** [CQ-003] [AMB:AMB-003] [FR-005] [FR-006]: Candidate axes are generated in a fixed order (a's edge normals in vertex order, then b's) and deduplicated; an equal-minimum-overlap tie resolves to the first axis in that order.
- **DEC-004** [CQ-004] [AMB:AMB-004] [FR-003]: `obbPolygon` emits 4 CCW vertices for a CCW-positive rotation angle in radians, satisfying the CCW input convention by construction.

## Accepted Deferrals
- None — all four ambiguities are resolved by decisions above; none deferred.

## Remaining Ambiguity
- None. AMB-001, AMB-002, AMB-003, and AMB-004 are resolved by DEC-001..DEC-004 above.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 005b-collision-sat-obb`.
