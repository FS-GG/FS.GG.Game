---
schemaVersion: 1
workId: 035-mapanalysis-chokepoints
title: MapAnalysis Entrances, Exits & Chokepoints
stage: charter
changeTier: tier1
status: chartered
policyPointers:
  - .fsgg/sdd.yml
  - .fsgg/agents.yml
  - .fsgg/policy.yml
  - .fsgg/capabilities.yml
  - .fsgg/tooling.yml
---

# MapAnalysis Entrances, Exits & Chokepoints Charter

## Identity
Milestone **M9** of the map construction & analysis design (Part II §9/§11). Extends the `MapAnalysis` module
with the structural-topology questions a map-builder asks: `borderOpenings` (floor on the map border — the
entrances/exits), `deadEnds` (floor cells with one floor neighbour), and `articulationPoints` (the bottleneck
cells whose removal splits the map). Depends on M8; extends `src/Game.Core/MapAnalysis.fs`/`.fsi`.

## Principles
- **Agree with the router.** Neighbour adjacency (and its no-corner-cutting under `EightWay`) matches
  `Pathfinding`, so "chokepoint" means a bottleneck for the same movement the game routes with.
- **Total, including on adversarial shapes.** `articulationPoints` uses **iterative** DFS (Tarjan) rather
  than recursion, so a long snaking corridor cannot overflow the stack — the function is total for any map.
- **Deterministic.** Row-major cell enumeration and fixed neighbour order; all results in a fixed order.

## Scope Boundaries
- **In:** `borderOpenings`/`deadEnds`/`articulationPoints` on `MapAnalysis`; a correctness (dumbbell/known
  fixtures) + determinism + totality suite; the surface-baseline update; a SKILL.md sentence.
- **Out:** path metrics (M10), fairness/validate (M11), tactical (M12); render-tier work.

## Policy Pointers
- Honors constitution I, III (surface + baseline), IV, V, VI (property tests), "no silent output changes".
- Tier 1: extends the public `MapAnalysis` surface.

## Lifecycle Notes
- The articulation-point cross-check (removal increases `componentCount`) is the headline correctness test.
- Next lifecycle action: `fsgg-sdd specify --work 035-mapanalysis-chokepoints`.
