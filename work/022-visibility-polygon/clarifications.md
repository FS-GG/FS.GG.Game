---
schemaVersion: 1
workId: 022-visibility-polygon
stage: clarify
sourceSpec: work/022-visibility-polygon/spec.md
---

# Clarifications

## Source Specification
- work/022-visibility-polygon/spec.md

## Clarification Questions
- **CQ-001** (AMB-001): Output form — float Point or exact rational?
- **CQ-002** (AMB-002): Algorithm — ray-at-endpoints or full sweep?
- **CQ-003** (AMB-003): Require a bounds Rect?

## Answers
- CQ-001 → A float `Point list` polygon. This is the render/authoring boundary the roadmap keeps OUT of Core, which is why the module lives in `Game.Harness`; intersection points are IEEE-reproducible floats, and exact-rational arithmetic is deferred with the Core promotion (resolves AMB-001).
- CQ-002 → Cast rays from the origin at each wall endpoint plus tiny ±pseudo-angle offsets (to see just past corners), take the nearest segment hit per ray by squared distance, then order the hits by pseudo-angle to form the polygon. Simpler and correct for a visualization; the full endpoint sweep is deferred with the Core promotion (resolves AMB-002).
- CQ-003 → Yes: a `bounds` Rect whose four edges are added to the segment set, so every ray hits something and the polygon is always closed. An origin outside/degenerate still returns a polygon (resolves AMB-003).

## Decisions
- **DEC-001** [CQ-001] [AMB:AMB-001] [FR-001] [FR-005]: The polygon is a float `Point list`, in `Game.Harness`; exact-rational vertices and the Core promotion are deferred (see the recorded deferral).
- **DEC-002** [CQ-002] [AMB:AMB-002] [FR-001] [FR-004]: Ray-at-endpoints (with ±offset) + nearest-by-squared-distance + pseudo-angle ordering; the full sweep is deferred.
- **DEC-003** [CQ-003] [AMB:AMB-003] [FR-003] [FR-006]: A required `bounds` Rect closes the polygon; degenerate origins/segments are total.

## Accepted Deferrals
- None — the three ambiguities are resolved above.

## Remaining Ambiguity
- None. AMB-001, AMB-002, and AMB-003 are resolved by DEC-001..DEC-003.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 022-visibility-polygon`.
