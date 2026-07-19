---
schemaVersion: 1
workId: 016-pathfinding-regions
stage: clarify
sourceSpec: work/016-pathfinding-regions/spec.md
---

# Clarifications

## Source Specification
- work/016-pathfinding-regions/spec.md

## Clarification Questions
- **CQ-001** (AMB-001): Does `Regions` expose region ids/sizes, or only the boolean `sameComponent`?
- **CQ-002** (AMB-002): How are bounds represented?
- **CQ-003** (AMB-003): What scan/flood order labels the components?

## Answers
- CQ-001 → Only the boolean `sameComponent`. Region label ids stay internal, so relabeling can never change an observable result — the determinism property the roadmap calls for (resolves AMB-001).
- CQ-002 → An inclusive `Cell * Cell` corner pair, normalized (min/max per axis) so the caller's corner order is irrelevant. It reuses the existing `Cell` primitive rather than adding a bounds type (resolves AMB-002).
- CQ-003 → A row-major scan (rows outer, columns inner) with a BFS flood from each unlabeled walkable cell, using the module's own neighbour rule. The order only assigns internal ids, which do not leak (resolves AMB-003).

## Decisions
- **DEC-001** [CQ-001] [AMB:AMB-001] [FR-004]: `Regions` exposes only `sameComponent : bool`; label ids are internal to the opaque value and never appear in a result.
- **DEC-002** [CQ-002] [AMB:AMB-002] [FR-001] [FR-002]: Bounds are an inclusive `Cell * Cell` corner pair, normalized per axis; out-of-bounds cells are in no component.
- **DEC-003** [CQ-003] [AMB:AMB-003] [FR-003] [FR-004]: `build` labels via a row-major scan and BFS flood using the module's neighbour rule (no corner-cutting under `EightWay`), so connectivity matches `astar`/`bfs`.

## Accepted Deferrals
- None — all three ambiguities are resolved above.

## Remaining Ambiguity
- None. AMB-001, AMB-002, and AMB-003 are resolved by DEC-001..DEC-003.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 016-pathfinding-regions`.
