---
schemaVersion: 1
workId: 019-hex-grid
stage: clarify
sourceSpec: work/019-hex-grid/spec.md
---

# Clarifications

## Source Specification
- work/019-hex-grid/spec.md

## Clarification Questions
- **CQ-001** (AMB-001): Cube record with derived S, or axial?
- **CQ-002** (AMB-002): Which offset layout convention?
- **CQ-003** (AMB-003): round tie-break on equal largest error?

## Answers
- CQ-001 → A cube record `Hex = { Q: int; R: int; S: int }` built only via `Hex.create q r` (which sets `S = -q - r`), so the invariant `q + r + s = 0` holds by construction; structural equality gives a stable key. Cube is the algorithm-friendly form the roadmap recommends; axial is recoverable as `(Q, R)` (resolves AMB-001).
- CQ-002 → Odd-r offset (`odd rows shoved right`), the Red Blob "odd-r" horizontal layout, documented on the converters. One fixed convention keeps `toOffset`/`ofOffset` exact inverses; doubled uses the doubled-height/doubled-width scheme (resolves AMB-002).
- CQ-003 → Reset the component with the largest absolute rounding delta; on an exact tie, prefer to reset `S` first, then `Q`, then `R` — a fixed documented order so `round` is byte-deterministic (resolves AMB-003).

## Decisions
- **DEC-001** [CQ-001] [AMB:AMB-001] [FR-001]: `Hex = { Q: int; R: int; S: int }`, constructed via `Hex.create q r` with `S = -q - r`; the invariant holds by construction and the record has structural equality.
- **DEC-002** [CQ-002] [AMB:AMB-002] [FR-007]: Offset converters use the odd-r horizontal layout; doubled converters use the doubled coordinate scheme; both are documented and are exact inverses.
- **DEC-003** [CQ-003] [AMB:AMB-003] [FR-006]: `round` resets the largest-absolute-error component; ties resolve in the fixed order S, then Q, then R, so rounding is byte-deterministic.

## Accepted Deferrals
- None — all three ambiguities are resolved above.

## Remaining Ambiguity
- None. AMB-001, AMB-002, and AMB-003 are resolved by DEC-001..DEC-003.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 019-hex-grid`.
