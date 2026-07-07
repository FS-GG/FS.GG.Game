---
schemaVersion: 1
workId: 006-collision-resolution-layer
stage: clarify
sourceSpec: work/006-collision-resolution-layer/spec.md
---

# Clarifications

## Source Specification
- work/006-collision-resolution-layer/spec.md

## Clarification Questions
- **CQ-001** (AMB-001): Does `slide` assume a unit `normal` or normalize defensively?
- **CQ-002** (AMB-002): Is `slide`'s projection unconditional or approach-only?
- **CQ-003** (AMB-003): Does `pushOut` subtract a slop, or separate exactly by `Depth`?
- **CQ-004** (AMB-004): What are `knockback`'s `step`/`distance` semantics, and is the `start` cell tested?

## Answers
- CQ-001 → Assume a unit `normal`. Every `Contact.Normal` is unit by construction (the `Geometry` manifolds guarantee it), so `slide` skips an internal `sqrt`/normalization; a non-unit normal is a documented caller error (resolves AMB-001).
- CQ-002 → Unconditional `v − (v·n)·n`. This is the design §6.1 slide ("keep tangential, kill normal") and gives the clean invariant `result · normal = 0` regardless of sign; approach-only (removing just the inward component) is a caller concern layered on top (resolves AMB-002).
- CQ-003 → Exact push-out by `Depth`. The primitive stays a clean, testable separation (`position − Normal × Depth`); slop to prevent jitter is a caller concern — pass a `Contact` with reduced `Depth` — kept out of the core so the "separates exactly" invariant holds (resolves AMB-003).
- CQ-004 → `step` is the per-cell delta (e.g. `{Col=1;Row=0}`) applied up to `distance` times; `distance ≤ 0` returns `start`. `start` is assumed free (the mover is already there), so only each *next* cell is tested against `blocked`; on the first blocked next-cell the mover stays put. This matches the tactics "displacement stopped by a wall/occupant" rule (resolves AMB-004).

## Decisions
- **DEC-001** [CQ-001] [AMB:AMB-001] [FR-002]: `slide` assumes `normal` is unit (the `Contact.Normal` contract); no internal normalization.
- **DEC-002** [CQ-002] [AMB:AMB-002] [FR-002]: `slide` is the unconditional projection `v − (v·n)·n`; the invariant is `result · normal = 0`.
- **DEC-003** [CQ-003] [AMB:AMB-003] [FR-001]: `pushOut` separates exactly by `Depth` (`position − Normal × Depth`); slop is a caller concern, not in the primitive.
- **DEC-004** [CQ-004] [AMB:AMB-004] [FR-003] [FR-004]: `knockback`'s `step` is the per-cell delta applied up to `distance` times; `distance ≤ 0` or an immediately-blocked next cell returns `start`; only next cells are tested, `start` is assumed free.

## Accepted Deferrals
- None — all four ambiguities are resolved by decisions above; none deferred.

## Remaining Ambiguity
- None. AMB-001, AMB-002, AMB-003, and AMB-004 are resolved by DEC-001..DEC-004 above.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 006-collision-resolution-layer`.
