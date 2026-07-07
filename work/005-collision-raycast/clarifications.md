---
schemaVersion: 1
workId: 005-collision-raycast
stage: clarify
sourceSpec: work/005-collision-raycast/spec.md
---

# Clarifications

## Source Specification
- work/005-collision-raycast/spec.md

## Clarification Questions
- **CQ-001** (AMB-001): Should the hit result be a public `RayHit` record, or a tuple?
- **CQ-002** (AMB-002): What does a segment originating inside the shape return?
- **CQ-003** (AMB-003): When a segment enters an AABB exactly at a corner (equal per-axis entry parameter), which face normal wins?

## Answers
- CQ-001 → A public `RayHit = { T: float; Point: Point; Normal: Point }` record — consistent with the `Point`/`Rect`/`Circle`/`Contact` primitives, self-documenting, and structurally comparable for golden tests (resolves AMB-001).
- CQ-002 → `None`. These are entry-from-outside queries; an origin inside the shape has no external crossing within `[0,1]` (the near root / slab entry is negative). Origin-inside handling belongs to a later containment/response concern (resolves AMB-002).
- CQ-003 → The X-axis face wins, matching the 004 manifold tie-breaks — one cross-primitive rule (resolves AMB-003).

## Decisions
- **DEC-001** [CQ-001] [AMB:AMB-001] [FR-002] [FR-004]: Add a public `RayHit = { T: float; Point: Point; Normal: Point }` type in `FS.GG.Game.Core`; both cast functions return `RayHit option`. Tier-1 surface addition (baseline + `.fsi` together).
- **DEC-002** [CQ-002] [AMB:AMB-002] [FR-001] [FR-003]: A segment whose entry parameter is negative (origin inside, or the shape behind the segment) returns `None` — entry-from-outside semantics.
- **DEC-003** [CQ-003] [AMB:AMB-003] [FR-006]: An AABB corner entry (equal per-axis entry parameter) resolves to the X-axis face normal, identical to the 004 tie-break rule.

## Accepted Deferrals
- None — all three ambiguities are resolved above; none deferred.

## Remaining Ambiguity
- None. AMB-001, AMB-002, and AMB-003 are resolved by DEC-001..DEC-003 above.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 005-collision-raycast`.
