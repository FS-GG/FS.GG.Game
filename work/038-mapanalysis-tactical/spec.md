---
schemaVersion: 1
workId: 038-mapanalysis-tactical
title: MapAnalysis Tactical Shape (Exposure, Cover, Killzones)
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# MapAnalysis Tactical Shape Specification

Prose status: specified

## User Value
A map-builder can measure the static tactical shape of a map — how exposed each cell is (how many cells can
see it), how much cover each cell has from adjacent walls, and where the killzones are (long mutual
sightlines) — as geometry-only priors, distinct from `Ai`'s dynamic enemy-keyed threat. Milestone M12, the
terminal `MapAnalysis` machinery.

## Scope
- SB-001: `MapAnalysis.exposureMap` (per floor cell, how many other floor cells can see it) and
  `MapAnalysis.killzones` (pairs of mutually-visible floor cells at least a minimum distance apart), both over
  a caller-supplied `hasLos: Cell -> Cell -> bool` oracle.
- SB-002: `MapAnalysis.coverMap` (per floor cell, how many of its 8 neighbouring positions are wall / off-map
  — cover directions), needing no oracle.
- SB-003: A correctness + determinism + totality suite; the surface baseline; a SKILL.md section drawing the
  static-shape-vs-`Ai` boundary.

## Non-Goals
- SB-004: No dynamic or enemy-keyed threat/influence (that is `Ai.threatField`/`influenceMap`); no render-tier
  work.

## User Stories
- US-001 (P1): As a map-builder, I can compute an exposure map and a cover map to see which cells are open
  killing ground and which are protected.
- US-002 (P1): As a map-builder, I can list the killzones — the long open sightlines a designer wants to break
  up or defend.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given a `hasLos` oracle and a `TileMap`, when `MapAnalysis.exposureMap` runs, then it maps each `Floor` cell to the number of other `Floor` cells `d` with `hasLos c d`.
- AC-002 [US-001] [FR-002]: Given a symmetric `hasLos`, when `exposureMap` runs, then `c`'s count includes `d` iff `d`'s count includes `c` (exposure is symmetric).
- AC-003 [US-001] [FR-003]: Given a `TileMap`, when `MapAnalysis.coverMap` runs, then it maps each `Floor` cell to the number of its 8 neighbouring positions that are `Wall` or off-map (0..8).
- AC-004 [US-002] [FR-004]: Given a `hasLos`, a `minLength`, and a `TileMap`, when `MapAnalysis.killzones` runs, then it returns the canonical `(a, b)` pairs (`a < b`) of `Floor` cells with `hasLos a b` and Chebyshev distance at least `minLength`, sorted deterministically.
- AC-005 [US-001] [FR-005]: Given degenerate input (empty map, no floor, a `hasLos` that is always false), when any function runs, then it returns an empty/documented value and never throws.

## Functional Requirements
- FR-001: `MapAnalysis.exposureMap` MUST map each `Floor` cell `c` to the count of other `Floor` cells `d` with `hasLos c d`. (covers AC-001)
- FR-002: Under a symmetric `hasLos`, `exposureMap` MUST be symmetric — `d` is counted in `c` iff `c` is counted in `d`. (covers AC-002)
- FR-003: `MapAnalysis.coverMap` MUST map each `Floor` cell to the number of its 8 neighbouring positions that are `Wall` or off-map (0..8). (covers AC-003)
- FR-004: `MapAnalysis.killzones` MUST return the canonical `(a, b)` pairs (`a < b`) of `Floor` cells with `hasLos a b` and Chebyshev distance `>= minLength`, in a fixed deterministic order. (covers AC-004)
- FR-005: Every function MUST be total — degenerate input yields an empty/documented value and never throws (given a total `hasLos`). (covers AC-005)

## Ambiguities
- AMB-001: Distance metric for the `killzones` length threshold — Chebyshev, Manhattan, or Euclidean?
- AMB-002: Should `exposureMap` include a cell seeing *itself* (count self), or only other cells?

## Public Or Tool-Facing Impact
- Tier 1 (contracted). Extends the public `MapAnalysis` surface; updates the surface baseline. Additive.

## Lifecycle Notes
- The exposure-symmetry property and cover-count fixtures are the headline tests; this is the terminal Part II
  milestone.
- Next lifecycle action: `fsgg-sdd clarify --work 038-mapanalysis-tactical`.
