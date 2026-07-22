---
schemaVersion: 1
workId: 036-mapanalysis-path-metrics
title: MapAnalysis Path & Flow Metrics
stage: clarify
changeTier: tier1
status: needsAnswers
sourceSpec: work/036-mapanalysis-path-metrics/spec.md
publicOrToolFacingImpact: true
---

# MapAnalysis Path & Flow Metrics Clarifications

## Source Specification
- work/036-mapanalysis-path-metrics/spec.md

## Clarification Questions
- **CQ-001** (AMB-001): Unweighted hop counts, or `Pathfinding.distanceField` movement cost?
- **CQ-002** (AMB-002): Add a `MapAnalysis.distanceField`, or defer to `Pathfinding`'s?

## Answers
- CQ-001 → **Unweighted BFS hop counts.** "How many steps across is this map" is the designer's mental model
  of size — a diameter of 40 means 40 tiles corner-to-corner, an integer a human reasons about and a `Rule`
  can bound. `Pathfinding.distanceField`'s `baseStep`/√2-weighted cost answers a *different* question (how
  expensive is the route to a mover), and folding the 14/10 diagonal weight into "diameter" would make the
  number an opaque cost rather than a hop count. So the metrics are plain BFS levels over the corner-cut-aware
  adjacency; a caller who wants movement cost reads `Pathfinding.distanceField` directly (resolves AMB-001).
- CQ-002 → **Defer the field to `Pathfinding.distanceField`; add only the derived metrics.** A second
  `distanceField` would duplicate the router's primitive (the module's standing "reuse, don't re-roll" rule,
  as M8 did for `components`). `MapAnalysis` adds `isolation`/`diameter`, which `Pathfinding` does not, and
  the SKILL.md points at `Pathfinding.distanceField` for the field itself (resolves AMB-002).

## Decisions
- **DEC-001** [CQ-001] [AMB:AMB-001] [FR-001] [FR-002]: `isolation`/`diameter` are unweighted BFS hop counts
  over the corner-cut-aware floor adjacency.
- **DEC-002** [CQ-002] [AMB:AMB-002] [FR-002]: `MapAnalysis` adds only `isolation`/`diameter`; the distance
  field itself is `Pathfinding.distanceField`.

## Accepted Deferrals
- None — both ambiguities are resolved above.

## Remaining Ambiguity
- None. AMB-001 and AMB-002 are resolved by DEC-001 and DEC-002.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 036-mapanalysis-path-metrics`.
