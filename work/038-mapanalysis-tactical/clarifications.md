---
schemaVersion: 1
workId: 038-mapanalysis-tactical
title: MapAnalysis Tactical Shape (Exposure, Cover, Killzones)
stage: clarify
changeTier: tier1
status: needsAnswers
sourceSpec: work/038-mapanalysis-tactical/spec.md
publicOrToolFacingImpact: true
---

# MapAnalysis Tactical Shape Clarifications

## Source Specification
- work/038-mapanalysis-tactical/spec.md

## Clarification Questions
- **CQ-001** (AMB-001): `killzones` length threshold metric — Chebyshev, Manhattan, or Euclidean?
- **CQ-002** (AMB-002): Does `exposureMap` count a cell seeing itself?

## Answers
- CQ-001 → **Chebyshev** (max of the axis deltas), the integer king-move distance. A killzone is a long *open
  sightline*, and on a grid a sightline's "length" is how many king-steps it spans — Chebyshev is the natural
  integer sightline metric, deterministic and √2/`sqrt`-free. Manhattan overcounts a diagonal line's length,
  and Euclidean needs a float; Chebyshev is exactly "how far apart along the line of sight" (resolves AMB-001).
- CQ-002 → **Only other cells**; a cell does not count itself. Exposure measures *incoming* sightlines from
  elsewhere — how many positions an enemy could shoot you from — so self-sight is not exposure (it is always
  true and would just add a constant 1 to every cell, obscuring the signal). This also makes the symmetry
  clean: `c` counts `d` iff `d` counts `c`, with no self term to special-case (resolves AMB-002).

## Decisions
- **DEC-001** [CQ-001] [AMB:AMB-001] [FR-004]: `killzones` uses Chebyshev distance for the `minLength`
  threshold.
- **DEC-002** [CQ-002] [AMB:AMB-002] [FR-001] [FR-002]: `exposureMap` counts only *other* floor cells (no
  self), which also makes the symmetry term-free.

## Accepted Deferrals
- None — both ambiguities are resolved above.

## Remaining Ambiguity
- None. AMB-001 and AMB-002 are resolved by DEC-001 and DEC-002.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 038-mapanalysis-tactical`.
