---
schemaVersion: 1
workId: 018-pathfinding-straighter-tiebreak
stage: clarify
sourceSpec: work/018-pathfinding-straighter-tiebreak/spec.md
---

# Clarifications

## Source Specification
- work/018-pathfinding-straighter-tiebreak/spec.md

## Clarification Questions
- **CQ-001** (AMB-001): What is the cross-product straightness term?
- **CQ-002** (AMB-002): Where does the cross term sit in the ordering key?
- **CQ-003** (AMB-003): Separate function or a flag on astar?

## Answers
- CQ-001 → The integer cross-product `|dx1*dy2 - dx2*dy1|` where (dx1,dy1) = cell-start and (dx2,dy2) = goal-start, computed in int64. It is twice the triangle area (start, goal, cell) — proportional to the cell's perpendicular distance from the straight line — and is exactly zero on that line (resolves AMB-001).
- CQ-002 → After `h`, as `(f, h, cross, Col, Row)`. It only breaks ties among equal-`(f, h)` nodes, so optimality is untouched; the key stays a strict total order (resolves AMB-002).
- CQ-003 → A separate `astarStraight` function. A flag would change `astar`'s signature; a separate entry point keeps `astar` byte-identical and lets the bias be opt-in and versioned (resolves AMB-003).

## Decisions
- **DEC-001** [CQ-001] [AMB:AMB-001] [FR-004] [FR-005]: The straightness term is the int64 cross-product `|dx1*dy2 - dx2*dy1|` (cell-start × goal-start), zero on the straight line, larger with perpendicular deviation.
- **DEC-002** [CQ-002] [AMB:AMB-002] [FR-001] [FR-003]: The frontier key is `(f, h, cross, Col, Row)`; `cross` is a pure tie-break after `f` and `h`, so cost-optimality is unchanged, and for plain `astar` the term is a constant 0 (byte-identical order).
- **DEC-003** [CQ-003] [AMB:AMB-003] [FR-003]: Expose `Pathfinding.astarStraight` as a separate function; `astar`'s signature and output are unchanged.

## Accepted Deferrals
- None — all three ambiguities are resolved above.

## Remaining Ambiguity
- None. AMB-001, AMB-002, and AMB-003 are resolved by DEC-001..DEC-003.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 018-pathfinding-straighter-tiebreak`.
