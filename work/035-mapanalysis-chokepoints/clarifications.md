---
schemaVersion: 1
workId: 035-mapanalysis-chokepoints
title: MapAnalysis Entrances, Exits & Chokepoints
stage: clarify
changeTier: tier1
status: needsAnswers
sourceSpec: work/035-mapanalysis-chokepoints/spec.md
publicOrToolFacingImpact: true
---

# MapAnalysis Entrances, Exits & Chokepoints Clarifications

## Source Specification
- work/035-mapanalysis-chokepoints/spec.md

## Clarification Questions
- **CQ-001** (AMB-001): Are `borderOpenings` neighbourhood-independent border floor cells, or filtered to
  cells actually open to outside movement?
- **CQ-002** (AMB-002): Verify `articulationPoints` against the O(V²) remove-and-recount oracle, or spot-check?

## Answers
- CQ-001 → Neighbourhood-independent: a `borderOpenings` cell is a `Floor` cell on the map's outer ring (col
  0 / col W-1 / row 0 / row H-1), full stop. A border floor cell *is* where the map meets the outside — the
  "entrance/exit" — regardless of whether a 4- or 8-connected step would carry a unit off-grid (there is no
  off-grid cell to step to). Filtering by an outside-movement notion would invent an off-map neighbourhood the
  rest of the module does not model. So `borderOpenings : TileMap -> Cell list`, no `Neighbourhood` parameter
  (resolves AMB-001).
- CQ-002 → Verify against the **remove-and-recount oracle** in a property test: for a random map, a cell is in
  `articulationPoints` iff `componentCount` rises when that cell is set to `Wall`. This is the FR-003
  definition made executable, and it independently checks the iterative-Tarjan implementation (the two agree
  or the test fails). The oracle is O(V²) so property maps stay small; the shipped `articulationPoints` is the
  O(V+E) Tarjan used at build time. Plus fixture tests (a dumbbell's corridor cells are articulation points;
  an open room has none) (resolves AMB-002).

## Decisions
- **DEC-001** [CQ-001] [AMB:AMB-001] [FR-001]: `borderOpenings : TileMap -> Cell list` — border `Floor` cells,
  neighbourhood-independent.
- **DEC-002** [CQ-002] [AMB:AMB-002] [FR-003]: `articulationPoints` is verified by the remove-and-recount
  oracle (a cell is articulation iff walling it raises `componentCount`) plus fixtures; the shipped impl is
  iterative Tarjan.

## Accepted Deferrals
- None — both ambiguities are resolved above.

## Remaining Ambiguity
- None. AMB-001 and AMB-002 are resolved by DEC-001 and DEC-002.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 035-mapanalysis-chokepoints`.
