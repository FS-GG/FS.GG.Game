---
schemaVersion: 1
workId: 025-influence-map
stage: clarify
sourceSpec: work/025-influence-map/spec.md
---

# Clarifications

## Source Specification
- work/025-influence-map/spec.md

## Clarification Questions
- **CQ-001** (AMB-001): Falloff shape?
- **CQ-002** (AMB-002): Strength units?
- **CQ-003** (AMB-003): Combine — max or sum?

## Answers
- CQ-001 → Linear falloff `strength - distance`. It is the simplest, most predictable, and integer (no float sqrt/inverse); inverse-square falloff is a caller variation over the same per-source distance fields (resolves AMB-001).
- CQ-002 → `baseStep`-scaled distance units — the same units `distanceField` returns — so `strength - distance` is a meaningful integer and a strength of `50` reaches 5 tiles at the default `baseStep` 10 (resolves AMB-002).
- CQ-003 → `max` (the strongest nearby source dominates), the standard control/threat semantics; additive influence is a caller variation. `max` keeps a single strong source from being diluted (resolves AMB-003).

## Decisions
- **DEC-001** [CQ-001] [AMB:AMB-001] [FR-001]: Linear falloff — influence contribution of a source is `max(0, strength - distance)`, distance from `distanceField`.
- **DEC-002** [CQ-002] [AMB:AMB-002] [FR-001]: Strength is in `baseStep`-scaled distance units (matching `distanceField`).
- **DEC-003** [CQ-003] [AMB:AMB-003] [FR-002]: Sources combine by `max`; zero-influence cells are absent.

## Accepted Deferrals
- None — all three ambiguities are resolved above.

## Remaining Ambiguity
- None. AMB-001, AMB-002, and AMB-003 are resolved by DEC-001..DEC-003.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 025-influence-map`.
