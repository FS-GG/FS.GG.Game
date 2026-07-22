---
schemaVersion: 1
workId: 035-mapanalysis-chokepoints
title: MapAnalysis Entrances, Exits & Chokepoints
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# MapAnalysis Entrances, Exits & Chokepoints Specification

Prose status: specified

## User Value
A map-builder can locate a map's entrances/exits (floor on the border), its dead-ends, and its chokepoints
(the bottleneck cells whose removal splits the map) — over any `TileMap`, consistently with the router.
Milestone M9 of the `MapAnalysis` machinery.

## Scope
- SB-001: `MapAnalysis.borderOpenings`, `MapAnalysis.deadEnds`, and `MapAnalysis.articulationPoints`, extending
  the existing module.
- SB-002: `articulationPoints` uses iterative Tarjan (no recursion), so it is total on any shape.
- SB-003: A correctness + determinism + totality property suite; the surface baseline; a SKILL.md sentence.

## Non-Goals
- SB-004: No path metrics (M10), fairness/validate (M11), or tactical analysis (M12); no render-tier work.

## User Stories
- US-001 (P1): As a map-builder, I can list a map's border openings (entrances/exits) and its dead-ends.
- US-002 (P1): As a map-builder, I can list the map's chokepoints — the cells whose removal disconnects it.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given a `TileMap`, when `MapAnalysis.borderOpenings` runs, then it returns the `Floor` cells on the map border (col 0, col W-1, row 0, or row H-1), in row-major order.
- AC-002 [US-001] [FR-002]: Given a `TileMap` and a `Neighbourhood`, when `MapAnalysis.deadEnds` runs, then it returns the `Floor` cells with exactly one `Floor` neighbour (respecting no-corner-cutting under `EightWay`), in row-major order.
- AC-003 [US-002] [FR-003]: Given a `TileMap` and a `Neighbourhood`, when `MapAnalysis.articulationPoints` runs, then it returns exactly the `Floor` cells whose removal increases the floor component count under that neighbourhood, in row-major order.
- AC-004 [US-002] [FR-004]: Given a long single-cell-wide corridor (thousands of cells), when `articulationPoints` runs, then it completes without a stack overflow (iterative DFS) and never throws.
- AC-005 [US-001] [FR-005]: Given degenerate input (empty map, all-wall, all-floor), when any function runs, then it returns a documented value and never throws.

## Functional Requirements
- FR-001: `MapAnalysis.borderOpenings` MUST return the `Floor` cells on the map border, in row-major order. (covers AC-001)
- FR-002: `MapAnalysis.deadEnds` MUST return the `Floor` cells with exactly one `Floor` neighbour under the `Neighbourhood` (no-corner-cutting under `EightWay`), in row-major order. (covers AC-002)
- FR-003: `MapAnalysis.articulationPoints` MUST return exactly the `Floor` cells whose removal increases the floor component count under the `Neighbourhood`, in row-major order. (covers AC-003)
- FR-004: `articulationPoints` MUST be implemented with iterative DFS so it is total on any shape, including a many-thousand-cell corridor, without a stack overflow. (covers AC-004)
- FR-005: Every function MUST be total — degenerate input yields a documented value and never throws. (covers AC-005)

## Ambiguities
- AMB-001: Are `borderOpenings` neighbourhood-independent (a border cell is a border cell regardless of 4/8), or should they be filtered to those actually open to outside movement?
- AMB-002: For `articulationPoints`, verify against the O(V²) remove-and-recount oracle in tests (proving the Tarjan implementation), or trust Tarjan and only spot-check fixtures?

## Public Or Tool-Facing Impact
- Tier 1 (contracted). Extends the public `MapAnalysis` surface; updates the surface baseline. Additive.

## Lifecycle Notes
- The remove-and-recount cross-check is the headline correctness test; tests carry stable filterable names.
- Next lifecycle action: `fsgg-sdd clarify --work 035-mapanalysis-chokepoints`.
