---
schemaVersion: 1
workId: 025-influence-map
title: Influence-Map Recipe
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Influence-Map Recipe Specification

Prose status: specified

## User Value
Game AI code can build an integer **influence map** from strengthed sources via `Ai.influenceMap` — a
thin wrapper over `Pathfinding.distanceField` with linear distance falloff — and combine friendly vs
enemy maps into a **tension** map by subtraction (a documented recipe). This is the standard AI
spatial-reasoning primitive (control, threat, desirability), reusing the shipped distance field rather
than a new engine.

## Scope
- SB-001: `Ai.influenceMap neighbourhood maxVisited cost sources : Map<Cell,int>` where `sources` are
  `(Cell * int)` (cell, strength); influence at a cell is `max` over sources of `max(0, strength -
  distance)`, distance from `distanceField`; zero-influence cells are absent.
- SB-002: A property suite (single-source falloff, multi-source max combine, distanceField
  consistency, tension-recipe, totality, determinism) and the documented tension recipe.

## Non-Goals
- SB-003: No float LOS-based influence (that is the existing `Ai.threatField`), no temporal
  decay/accumulation, and none of the other M4 items (dice 3.1, templates 3.3, bucketed PQ 3.4).

## User Stories
- US-001 (P1): As game AI code, I can build an integer influence map from strengthed sources with
  linear falloff, for control/threat/desirability reasoning.
- US-002 (P1): As game AI code, I can subtract a friendly and an enemy influence map into a tension
  map (documented recipe).

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given a single source `(s, strength)`, when `influenceMap` is called, then the source cell has influence `strength`, influence decreases by 1 per `baseStep` of distance, and cells at distance `>= strength` (influence `<= 0`) are absent.
- AC-002 [US-001] [FR-002]: Given multiple sources, when `influenceMap` is called, then each cell's influence is the `max` over sources of `max(0, strength - distance-to-that-source)`.
- AC-003 [US-001] [FR-003]: Given the same `neighbourhood`/`cost`/`maxVisited`, `influenceMap` MUST be consistent with `distanceField` (a source's contribution uses `distanceField [source]`), inherit its determinism and cost convention, and yield an empty map for empty sources.
- AC-004 [US-002] [FR-004]: Given a friendly and an enemy influence map, when the caller forms `tension = friendly - enemy` per cell, then positive tension marks friendly-controlled cells and negative enemy-controlled — the documented recipe, verified by a test.

## Functional Requirements
- FR-001: `Ai.influenceMap` MUST give a single source `(s, strength)` an influence of `strength` at `s` that falls off by 1 per `baseStep` of `distanceField` distance, with cells of influence `<= 0` absent. (covers AC-001)
- FR-002: For multiple sources, `Ai.influenceMap` MUST assign each cell the `max` over sources of `max(0, strength - distance-to-that-source)`. (covers AC-002)
- FR-003: `Ai.influenceMap` MUST be built on `Pathfinding.distanceField` (one field per source), inheriting its `cost` convention, `maxVisited` bound, and determinism; empty sources yield an empty map. (covers AC-003)
- FR-004: The friendly-vs-enemy **tension** recipe (`friendly - enemy` per cell) MUST be documented and demonstrated by a test. (covers AC-004)

## Ambiguities
- AMB-001: Falloff shape — linear (`strength - distance`), or another (e.g. inverse-square)?
- AMB-002: Source-strength units — `baseStep`-scaled distance units (so `strength - distance` is meaningful), or tile counts?
- AMB-003: Multi-source combine — `max` (strongest source dominates) or sum (additive influence)?

## Public Or Tool-Facing Impact
- Tier 1 (contracted). Adds `Ai.influenceMap` to `FS.GG.Game.Core` — so the `.fsi`, both surface
  baselines, and tests land together, and the drift gate must stay green. No existing signature changes.

## Lifecycle Notes
- Determinism/property tests MUST carry a stable filterable name for the `gate.yml` guard.
- Next lifecycle action: `fsgg-sdd clarify --work 025-influence-map`.
