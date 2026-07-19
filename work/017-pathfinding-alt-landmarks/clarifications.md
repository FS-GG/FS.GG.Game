---
schemaVersion: 1
workId: 017-pathfinding-alt-landmarks
stage: clarify
sourceSpec: work/017-pathfinding-alt-landmarks/spec.md
---

# Clarifications

## Source Specification
- work/017-pathfinding-alt-landmarks/spec.md

## Clarification Questions
- **CQ-001** (AMB-001): What is the landmark selection strategy?
- **CQ-002** (AMB-002): How is the heuristic combined with octile?
- **CQ-003** (AMB-003): How many landmarks by default, and are bounds required?

## Answers
- CQ-001 → Deterministic farthest-point (max-min) sampling: seed at the bounds' min-corner walkable cell, then repeatedly pick the reachable cell maximising the minimum distance to the already-chosen landmarks, ties broken by the total `(Col, Row)` order. A pure function of the map (resolves AMB-001).
- CQ-002 → `max(octile, ALT)` — the pointwise maximum of two admissible heuristics is admissible and never looser than either, so it dominates plain octile and never loses optimality (resolves AMB-002).
- CQ-003 → `count` is an explicit caller argument (a small handful, e.g. 4–8, is typical); bounds are a required inclusive `Cell * Cell` pair because the framework holds no map (resolves AMB-003).

## Decisions
- **DEC-001** [CQ-001] [AMB:AMB-001] [FR-004]: Landmarks are chosen by deterministic farthest-point sampling from a fixed seed (bounds min-corner), max-min distance with `(Col, Row)` tie-break; a pure function of `(neighbourhood, isWalkable, count, bounds)`.
- **DEC-002** [CQ-002] [AMB:AMB-002] [FR-005] [FR-001]: The search heuristic is `max(octile, ALT)`; both are admissible, so the maximum is admissible and optimality holds while expanding no more nodes than octile.
- **DEC-003** [CQ-003] [AMB:AMB-003] [FR-004]: `Landmarks.build` takes an explicit `count` and an inclusive `Cell * Cell` bounds; landmark tables are `distanceField`s within those bounds.

## Accepted Deferrals
- None — all three ambiguities are resolved above.

## Remaining Ambiguity
- None. AMB-001, AMB-002, and AMB-003 are resolved by DEC-001..DEC-003.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 017-pathfinding-alt-landmarks`.
