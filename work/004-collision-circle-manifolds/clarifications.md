---
schemaVersion: 1
workId: 004-collision-circle-manifolds
stage: clarify
sourceSpec: work/004-collision-circle-manifolds/spec.md
---

# Clarifications

## Source Specification
- work/004-collision-circle-manifolds/spec.md

## Clarification Questions
- **CQ-001** (AMB-001): Should the circle be a public `Circle` primitive type, or should the functions take bare `center: Point -> radius: float` arguments?
- **CQ-002** (AMB-002): When two circle centers coincide (distance 0), the contact normal direction is undefined — what fixed direction do we return?
- **CQ-003** (AMB-003): For `circleAabbContact` with the center inside the box, when two faces have equal penetration, which axis wins the tie-break?

## Answers
- CQ-001 → Introduce a public `Circle = { Center: Point; Radius: float }` record. It is consistent with the existing `Point`/`Rect` primitives, makes circle–circle call sites read cleanly, and is the natural argument the future resolution layer (006) and samples consume (resolves AMB-001).
- CQ-002 → Return `Normal = (1, 0)` with `Depth = rA + rB`. A fixed, documented constant keeps the degenerate case byte-deterministic (resolves AMB-002).
- CQ-003 → Mirror `aabbContact`: the X axis wins, with the +direction bias, so the two collision primitives share one tie-break rule (resolves AMB-003).

## Decisions
- **DEC-001** [CQ-001] [AMB:AMB-001] [FR-001] [FR-002] [FR-003]: Add a public `Circle = { Center: Point; Radius: float }` type in `FS.GG.Game.Core`; `circleContact` takes two `Circle`s and `circleAabbContact` takes a `Circle` and a `Rect`. The new type is a Tier-1 surface addition (baseline + `.fsi` updated together).
- **DEC-002** [CQ-002] [AMB:AMB-002] [FR-002] [FR-006] [AC-006]: Coincident circle centers yield `Contact { Normal = (1, 0); Depth = rA + rB }` — a fixed, documented, tested constant.
- **DEC-003** [CQ-003] [AMB:AMB-003] [FR-004] [FR-006] [AC-004]: `circleAabbContact`'s center-inside fallback breaks equal face penetration toward the X axis with a +direction bias, identical to `aabbContact`'s rule — one cross-primitive tie-break.

## Accepted Deferrals
- None — all three ambiguities are resolved above; none deferred.

## Remaining Ambiguity
- None. AMB-001, AMB-002, and AMB-003 are resolved by DEC-001..DEC-003 above.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 004-collision-circle-manifolds`.
