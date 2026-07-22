---
schemaVersion: 1
workId: 037-mapanalysis-fairness-validate
title: MapAnalysis Distribution, Fairness & Validation
stage: clarify
changeTier: tier1
status: needsAnswers
sourceSpec: work/037-mapanalysis-fairness-validate/spec.md
publicOrToolFacingImpact: true
---

# MapAnalysis Distribution, Fairness & Validation Clarifications

## Source Specification
- work/037-mapanalysis-fairness-validate/spec.md

## Clarification Questions
- **CQ-001** (AMB-001): `spacing`/`fairness` distance metric — Manhattan, Euclidean, or BFS hop?
- **CQ-002** (AMB-002): `Rule` as a closed DU or an open predicate?

## Answers
- CQ-001 → **`spacing` is Manhattan** (a straight-line grid metric on `Cell`, integer and deterministic, no
  float/`sqrt`), because it measures raw separation of a *point set* with no map context. **`fairness` is BFS
  hop over the map** (the same corner-cut-aware adjacency M10 uses), because "how far is a spawn from a
  resource" must respect walls — a resource across a wall is not close, however near in straight-line terms.
  So the two metrics differ by design: `spacing` is geometric, `fairness`/`coverage` are traversal (resolves
  AMB-001).
- CQ-002 → A **closed `Rule` DU** of the common constraints — `Connected`, `MinDiameter of int`,
  `MaxDiameter of int`, `MinBorderOpenings of int`, `MaxComponents of int`. A closed set keeps `validate`
  total and its `Report`/reason strings deterministic and inspectable, and it is what a rule *config* wants
  (serializable, enumerable). An open `TileMap -> string option` predicate would let a caller inject arbitrary
  (possibly non-deterministic or throwing) logic into the keystone — the opposite of what a validation battery
  should guarantee. A caller needing a bespoke check runs it alongside `validate` and combines the results
  (resolves AMB-002).

## Decisions
- **DEC-001** [CQ-001] [AMB:AMB-001] [FR-001] [FR-002]: `spacing` uses Manhattan distance on the point set;
  `fairness`/`coverage` use BFS hop distance over the map's corner-cut-aware adjacency.
- **DEC-002** [CQ-002] [AMB:AMB-002] [FR-004]: `Rule` is a closed DU — `Connected`, `MinDiameter`,
  `MaxDiameter`, `MinBorderOpenings`, `MaxComponents`; `validate` evaluates them in list order.

## Accepted Deferrals
- None — both ambiguities are resolved above.

## Remaining Ambiguity
- None. AMB-001 and AMB-002 are resolved by DEC-001 and DEC-002.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 037-mapanalysis-fairness-validate`.
