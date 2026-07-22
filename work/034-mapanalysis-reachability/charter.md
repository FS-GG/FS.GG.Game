---
schemaVersion: 1
workId: 034-mapanalysis-reachability
title: MapAnalysis Reachability & Connectivity
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

# MapAnalysis Reachability & Connectivity Charter

## Identity
Milestone **M8** of the map construction & analysis design
(`docs/reports/2026-07-22-procedural-map-generation-design.md`, Part II §9/§11) — the first of the
producer-agnostic `MapAnalysis` machinery. Lands the new `MapAnalysis` module in `FS.GG.Game.Core` with the
reachability/connectivity primitives every map-builder needs: `reachable` (from a start over a walkability
predicate), `stranded` (floor cells unreachable from a point), `isConnected`, and `componentCount`. Depends
on the shipped `MapGen` substrate + `Pathfinding`; extends nothing — a new module file.

## Principles
- **Producer-agnostic.** `reachable` takes a `Cell -> bool` predicate (the map *is* the predicate, as in
  `Pathfinding`), so it serves authored and agent-built maps, not only `MapGen` output.
- **Agree with the router.** Built on `Pathfinding.distanceField`/`regions`, so reachability and
  connectivity use the router's exact neighbour logic (no-corner-cutting under `EightWay`) — a `reachable`
  set is exactly what `bfs` can reach.
- **No duplication.** `MapGen.regions` already computes components; `MapAnalysis` reuses it (`componentCount`)
  and adds the reachable/stranded/connectivity vocabulary, not a second flood-fill.
- **Deterministic & total.** Row-major cell enumeration, `Set<Cell>`/`Cell list` results in fixed order; a
  degenerate map / non-floor start returns a documented value; never throws.

## Scope Boundaries
- **In:** `MapAnalysis.fs`/`.fsi` with `reachable`/`stranded`/`isConnected`/`componentCount`; a correctness +
  determinism + router-consistency property suite; the surface-baseline update; a `MapAnalysis` section added
  to the `fs-gg-mapcraft` SKILL.md.
- **Out:** chokepoints (M9), path metrics (M10), fairness/validate (M11), tactical (M12); render-tier work.

## Policy Pointers
- Honors constitution I, III (surface + baseline), IV, V, VI (property tests), and "no silent output changes".
- Tier 1: introduces a public `MapAnalysis` surface, so signatures, baseline, tests, and the skill section
  land together.

## Lifecycle Notes
- A router-consistency property (`reachable` set == `bfs`-reachable cells) is the headline test.
- Next lifecycle action: `fsgg-sdd specify --work 034-mapanalysis-reachability`.
