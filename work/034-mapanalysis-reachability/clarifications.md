---
schemaVersion: 1
workId: 034-mapanalysis-reachability
title: MapAnalysis Reachability & Connectivity
stage: clarify
changeTier: tier1
status: needsAnswers
sourceSpec: work/034-mapanalysis-reachability/spec.md
publicOrToolFacingImpact: true
---

# MapAnalysis Reachability & Connectivity Clarifications

## Source Specification
- work/034-mapanalysis-reachability/spec.md

## Clarification Questions
- **CQ-001** (AMB-001): Expose `componentCount` instead of duplicating `MapGen.regions` as `components`?
- **CQ-002** (AMB-002): `reachable` argument order — match `Pathfinding.bfs`, or the sketch order?

## Answers
- CQ-001 → Expose `componentCount : Neighbourhood -> TileMap -> int` (a thin count over `MapGen.regions`) and
  do **not** add a `components` function. `MapGen.regions` already returns the `Region list` the sketch's
  `components` described; re-exporting it under `MapAnalysis` would be a second name for one function — the
  "reuse, don't re-roll" rule the whole module follows. The SKILL.md points readers at `MapGen.regions` for
  the component list and `MapAnalysis.componentCount` for the count. `MapAnalysis` adds only what `MapGen`
  lacks: the predicate-general `reachable`, and `stranded`/`isConnected` built on it (resolves AMB-001).
- CQ-002 → Match `Pathfinding.bfs`: `reachable neighbourhood maxVisited isWalkable start`. Every routing
  entry point in the module family orders args `neighbourhood -> maxVisited -> (predicate/cost) -> start(…)`,
  so a reader who knows `bfs`/`astar` reads `reachable` without re-learning the shape, and partial
  application (`reachable EightWay budget isWalkable`) yields a reusable start→set function. The sketch order
  was illustrative; consistency with the shipped router wins (resolves AMB-002).

## Decisions
- **DEC-001** [CQ-001] [AMB:AMB-001] [FR-005]: `MapAnalysis` exposes `componentCount` (over `MapGen.regions`);
  no duplicate `components`.
- **DEC-002** [CQ-002] [AMB:AMB-002] [FR-001]: `reachable`'s signature is
  `neighbourhood -> maxVisited -> isWalkable -> start`, matching `Pathfinding.bfs`.

## Accepted Deferrals
- None — both ambiguities are resolved above.

## Remaining Ambiguity
- None. AMB-001 and AMB-002 are resolved by DEC-001 and DEC-002.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 034-mapanalysis-reachability`.
