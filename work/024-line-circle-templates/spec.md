---
schemaVersion: 1
workId: 024-line-circle-templates
title: Line and Circle Templates
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Line and Circle Templates Specification

Prose status: specified

## User Value
Game-logic code gets AoE/range **templates**: a filled `Grids.disc` (blast radius) and an outline
`Grids.ring` (range ring) rasterized with integer arithmetic, plus a confirmation that the shipped
`Los.line` is the line-template primitive (reused, not duplicated). Deterministic cell sets for
telegraphing spells, blasts, and movement ranges.

## Scope
- SB-001: `Grids.disc center radius : Cell list` (every cell within the Euclidean radius) and
  `Grids.ring center radius : Cell list` (the integer midpoint-circle outline), both deterministic.
- SB-002: A property suite (disc membership, ring-on-boundary, determinism, totality, overflow-safe
  squared distance) and a test confirming `Los.line` is the line-template primitive.

## Non-Goals
- SB-003: No cones/arcs, no hex disc/ring, no thick/width lines, and none of the other M4 items (dice
  3.1, influence 3.2, bucketed PQ 3.4).

## User Stories
- US-001 (P1): As game-logic code, I can get the filled disc and the outline ring around a cell for
  AoE and range templates.
- US-002 (P1): As game-logic code, I can reuse `Los.line` for line templates.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given a center and radius, when `Grids.disc` is called, then it returns exactly the cells whose squared distance to the center is `<= radius^2`; `disc c 0 = [c]`; a negative radius yields `[]`.
- AC-002 [US-001] [FR-002]: Given a center and radius, when `Grids.ring` is called, then it returns the midpoint-circle outline — deduplicated, in a fixed order, every cell at rounded distance `radius` from the center; `ring c 0 = [c]`, negative yields `[]`.
- AC-003 [US-001] [FR-003]: Given identical inputs, when `disc`/`ring` are called repeatedly, then the results are byte-identical and integer/total (a large radius does not overflow the squared-distance test — computed in int64).
- AC-004 [US-002] [FR-004]: Given two cells, when `Los.line` is used as the line template, then it returns a contiguous grid line including both endpoints (confirming the reused primitive).

## Functional Requirements
- FR-001: `Grids.disc center radius` MUST return exactly the cells whose integer squared distance to `center` is `<= radius^2` (radius 0 yields `[center]`, negative yields `[]`), in a fixed scan order. (covers AC-001)
- FR-002: `Grids.ring center radius` MUST return the integer midpoint-circle outline — deduplicated and in a fixed sorted order, every cell at rounded Euclidean distance `radius` from `center` (radius 0 yields `[center]`, negative yields `[]`). (covers AC-002)
- FR-003: `disc`/`ring` MUST be pure, total, byte-deterministic, and overflow-safe — the squared-distance comparison is computed in int64 so a large radius does not wrap. (covers AC-003)
- FR-004: Line templates MUST reuse the shipped `Los.line` (a contiguous endpoint-inclusive grid line), confirmed by a test; no duplicate line primitive is added. (covers AC-004)

## Ambiguities
- AMB-001: `ring` definition — the midpoint-circle outline (thin, 1-cell), or the annulus `disc r \ disc (r-1)`?
- AMB-002: `disc`/`ring` return order — scan order (disc) and sorted `(Col,Row)` (ring), or another?
- AMB-003: distance metric for the disc — Euclidean (squared) or Chebyshev/Manhattan?

## Public Or Tool-Facing Impact
- Tier 1 (contracted). Adds `Grids.disc` and `Grids.ring` to `FS.GG.Game.Core` — so the `.fsi`, both
  surface baselines, and tests land together, and the drift gate must stay green. No existing
  signature changes; `Los.line` is unchanged and merely confirmed.

## Lifecycle Notes
- Determinism/property tests MUST carry a stable filterable name for the `gate.yml` guard.
- Next lifecycle action: `fsgg-sdd clarify --work 024-line-circle-templates`.
