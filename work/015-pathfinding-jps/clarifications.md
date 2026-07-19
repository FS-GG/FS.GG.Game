---
schemaVersion: 1
workId: 015-pathfinding-jps
stage: clarify
sourceSpec: work/015-pathfinding-jps/spec.md
---

# Clarifications

## Source Specification
- work/015-pathfinding-jps/spec.md

## Clarification Questions
- **CQ-001** (AMB-001): Is the differential contract against `astar` literal cell-by-cell identity of
  the returned path, or equal cost + path validity + reachability agreement + `jps` self-determinism?
- **CQ-002** (AMB-002): What unit does `jps`' `maxVisited` bound — frontier pops (as `astar`), or
  cells scanned inside jumps?

## Answers
- CQ-001 → Equal cost + validity + reachability agreement + `jps` self-determinism. Literal
  cell-identity cannot hold in general: on a uniform-cost grid many least-cost paths are equal-cost,
  and JPS's canonical-jump pruning selects a different equal-cost sequence than A*'s
  `(f, h, Col, Row)` tie-break. Demanding identical *cells* would force discarding the pruning that
  is JPS's entire value. The honest, testable contract is that `jps` returns a path of the *same
  cost* A* would, that the path is *valid*, that the two *agree on reachability*, and that `jps` is
  itself *byte-deterministic* run-to-run — which is exactly what a deterministic-replay `update`
  needs (resolves AMB-001).
- CQ-002 → Frontier pops, the same unit `astar` bounds on, so `maxVisited` means the same thing to a
  caller who swaps `jps` for `astar`. Because a jump-point search pops far fewer nodes, the two reach
  the `None`-on-exhaustion boundary at different query sizes; the differential equivalence (FR-001/
  FR-003) is therefore asserted on searches that *complete within budget* (not bounded out), which is
  the regime a caller sizes `maxVisited` for anyway (resolves AMB-002).

## Decisions
- **DEC-001** [CQ-001] [AMB:AMB-001] [FR-001] [FR-002] [FR-003] [AC-001] [AC-002] [AC-003]: The
  `jps`↔`astar` differential contract is **equal path cost, path validity, reachability agreement,
  and `jps` run-to-run byte-identity** — not literal cell-by-cell identity. The property test asserts
  those four facts; it does not compare cell sequences to `astar`.
- **DEC-002** [CQ-002] [AMB:AMB-002] [FR-004] [FR-006]: `jps`' `maxVisited` bounds **frontier pops**
  (jump points popped from the open set), the same unit `astar` counts. Differential equivalence is
  asserted only where the search is not bounded out by `maxVisited`; the benchmark (FR-006) counts
  frontier pops for both.

## Accepted Deferrals
- None — both ambiguities are resolved above.

## Remaining Ambiguity
- None. AMB-001 and AMB-002 are resolved by DEC-001 and DEC-002.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 015-pathfinding-jps`.
