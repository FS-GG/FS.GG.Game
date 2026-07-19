---
schemaVersion: 1
workId: 024-line-circle-templates
stage: clarify
sourceSpec: work/024-line-circle-templates/spec.md
---

# Clarifications

## Source Specification
- work/024-line-circle-templates/spec.md

## Clarification Questions
- **CQ-001** (AMB-001): ring = midpoint outline or annulus?
- **CQ-002** (AMB-002): return order?
- **CQ-003** (AMB-003): disc distance metric?

## Answers
- CQ-001 → The midpoint-circle outline (a thin, ~1-cell-thick rasterized circle), the standard range-ring template; the annulus is trivially `disc r` minus `disc (r-1)` and not worth a second primitive (resolves AMB-001).
- CQ-002 → `disc` in row-major scan order (rows outer, columns inner); `ring` deduplicated and sorted by `(Col, Row)` (the 8-way symmetric midpoint points are gathered into a set then sorted). Both are fixed deterministic orders (resolves AMB-002).
- CQ-003 → Euclidean, via the integer **squared** distance `dc^2 + dr^2 <= radius^2` (no float sqrt); computed in int64 to stay overflow-safe (resolves AMB-003).

## Decisions
- **DEC-001** [CQ-001] [AMB:AMB-001] [FR-002]: `ring` is the integer midpoint-circle outline, deduplicated and sorted by `(Col, Row)`.
- **DEC-002** [CQ-002] [AMB:AMB-002] [FR-001] [FR-002]: `disc` in row-major scan order; `ring` sorted by `(Col, Row)`.
- **DEC-003** [CQ-003] [AMB:AMB-003] [FR-001] [FR-003]: `disc` uses the integer squared Euclidean distance in int64 (overflow-safe, no sqrt).

## Accepted Deferrals
- None — all three ambiguities are resolved above.

## Remaining Ambiguity
- None. AMB-001, AMB-002, and AMB-003 are resolved by DEC-001..DEC-003.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 024-line-circle-templates`.
