---
schemaVersion: 1
workId: 037-mapanalysis-fairness-validate
title: MapAnalysis Distribution, Fairness & Validation
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# MapAnalysis Distribution, Fairness & Validation Specification

Prose status: specified

## User Value
A map-building agent can measure point distribution (`spacing`), spawn fairness (each spawn's nearest-resource
distance), and coverage, and run a `validate` battery that accepts or rejects a map against rules *with
reasons* — so it can loop produce → validate → re-produce until the map is good. Milestone M11, the keystone.

## Scope
- SB-001: `MapAnalysis.spacing` (min & mean nearest-neighbour spacing of points), `MapAnalysis.fairness` (each
  spawn's nearest-resource hop distance), and `MapAnalysis.coverage` (fraction of floor within a radius of some
  point).
- SB-002: A `Rule` type (common constraints), a `Report` type (Passed + Failures + measured facts), and
  `MapAnalysis.validate : Rule list -> Neighbourhood -> TileMap -> Report`.
- SB-003: A correctness + determinism + totality suite, a worked `generate → validate` example over a `MapGen`
  cave, the surface baseline, and a SKILL.md section.

## Non-Goals
- SB-004: No tactical analysis (M12); no render-tier work.

## User Stories
- US-001 (P1): As a map-builder, I can measure spacing, fairness, and coverage of points on a map.
- US-002 (P1): As a map-builder, I can validate a map against a rule set and get accept/reject with reasons.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given a list of cells, when `MapAnalysis.spacing` runs, then it returns the minimum and mean nearest-neighbour Manhattan distance among the points (fewer than two points ⇒ `struct (0, 0.0)`).
- AC-002 [US-001] [FR-002]: Given spawns, resources, a `Neighbourhood`, and a `TileMap`, when `MapAnalysis.fairness` runs, then it maps each spawn that can reach a resource to its nearest-resource hop distance (a spawn that reaches none is absent from the map).
- AC-003 [US-001] [FR-003]: Given points, a radius, a `Neighbourhood`, and a `TileMap`, when `MapAnalysis.coverage` runs, then it returns the fraction of `Floor` cells within `radius` hops of some point (0.0 when there is no floor or no point).
- AC-004 [US-002] [FR-004]: Given a `Rule list`, a `Neighbourhood`, and a `TileMap`, when `MapAnalysis.validate` runs, then it returns a `Report` whose `Passed` is true with an empty `Failures` iff every `Rule` holds, and otherwise carries one reason string per violated rule (in rule-list order), plus the measured facts (`Connected`/`ComponentCount`/`Diameter`/`BorderOpenings`).
- AC-005 [US-002] [FR-005]: Given a known-good map (connected, adequate diameter) and a known-bad one (disconnected), when `validate` runs the same rules, then the good map passes and the bad map fails with the connectivity reason.
- AC-006 [US-001] [FR-006]: Given degenerate input (empty points/map, zero radius), when any function runs, then it returns a documented value and never throws.

## Functional Requirements
- FR-001: `MapAnalysis.spacing` MUST return the minimum and mean nearest-neighbour Manhattan distance of the points as `struct (int * float)` (`struct (0, 0.0)` for fewer than two points). (covers AC-001)
- FR-002: `MapAnalysis.fairness` MUST map each spawn that can reach a resource to its nearest-resource hop distance under the `Neighbourhood`, omitting spawns that reach no resource. (covers AC-002)
- FR-003: `MapAnalysis.coverage` MUST return the fraction of `Floor` cells within `radius` hops of some point (0.0 when there is no floor or no point). (covers AC-003)
- FR-004: `MapAnalysis.validate` MUST return a `Report` whose `Passed` is true with empty `Failures` iff every `Rule` holds, else one reason string per violated rule in rule-list order, with the measured facts populated. (covers AC-004)
- FR-005: `validate` MUST pass a known-good map and fail a disconnected map (with the connectivity reason) under a rule set requiring connectivity. (covers AC-005)
- FR-006: Every function MUST be total — degenerate input yields a documented value and never throws. (covers AC-006)

## Ambiguities
- AMB-001: `spacing`/`fairness` distance metric — Manhattan hop, Euclidean, or BFS hop over the map?
- AMB-002: `Rule` shape — a closed DU of common constraints (`Connected`/`MinDiameter`/…), or an open predicate `TileMap -> string option`?

## Public Or Tool-Facing Impact
- Tier 1 (contracted). Adds `Rule`/`Report` types and four `MapAnalysis` values; updates the surface baseline.
  Additive.

## Lifecycle Notes
- The good-vs-bad `validate` behaviour and the reason-per-rule order are the headline tests.
- Next lifecycle action: `fsgg-sdd clarify --work 037-mapanalysis-fairness-validate`.
