---
schemaVersion: 1
workId: 023-dice-distributions
stage: clarify
sourceSpec: work/023-dice-distributions/spec.md
---

# Clarifications

## Source Specification
- work/023-dice-distributions/spec.md

## Clarification Questions
- **CQ-001** (AMB-001): Distribution representation?
- **CQ-002** (AMB-002): mean/variance return type?
- **CQ-003** (AMB-003): sample on empty distribution?

## Answers
- CQ-001 → An opaque `Distribution` type wrapping a normalized `Map<int, int>` (outcome -> positive weight, zero-weight entries dropped). Opaqueness lets `convolve`/`sample` rely on the invariant (all weights > 0, non-empty for a well-formed die); `outcomes`/`totalWeight` expose it read-only (resolves AMB-001).
- CQ-002 → `float`. The exact weighted moment is a rational, but a deterministic IEEE `float` is the ergonomic return for combat tuning; the computation uses integer sums internally so it is reproducible (resolves AMB-002).
- CQ-003 → A documented total fallback: `sample` on an empty distribution returns `0` and the unchanged `Rng` (never throws). Well-formed constructors never produce an empty distribution; only a degenerate `uniform hi lo` (hi<lo) can, and it is handled (resolves AMB-003).

## Decisions
- **DEC-001** [CQ-001] [AMB:AMB-001] [FR-001]: `Distribution` is an opaque type over `Map<int, int>` with all weights > 0; `outcomes`/`totalWeight` are the read-only accessors.
- **DEC-002** [CQ-002] [AMB:AMB-002] [FR-004]: `mean`/`variance` return `float`, computed from integer sums (reproducible).
- **DEC-003** [CQ-003] [AMB:AMB-003] [FR-005] [FR-006]: `sample` on an empty distribution returns `(0, rng)` — a documented total fallback; constructors never yield empty except a degenerate `uniform`.

## Accepted Deferrals
- None — all three ambiguities are resolved above.

## Remaining Ambiguity
- None. AMB-001, AMB-002, and AMB-003 are resolved by DEC-001..DEC-003.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 023-dice-distributions`.
