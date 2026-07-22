---
schemaVersion: 1
workId: 038-mapanalysis-tactical
title: MapAnalysis Tactical Shape (Exposure, Cover, Killzones)
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

# MapAnalysis Tactical Shape Charter

## Identity
Milestone **M12** (final) of the map construction & analysis design (Part II §9/§11). Extends `MapAnalysis`
with the **static tactical shape** of a map: `exposureMap` (how many cells can see each cell), `coverMap`
(adjacent-wall cover per cell), and `killzones` (long mutual sightlines). Depends on M8; extends
`src/Game.Core/MapAnalysis.fs`/`.fsi`.

## Principles
- **Static shape, not live enemies.** These are functions of *geometry alone*, computable at build time with
  no units present — the tactical *priors* of a map. They are deliberately distinct from `Ai.threatField`/
  `influenceMap`, which are the *dynamic*, per-tick answer keyed on actual enemy sources. `MapAnalysis` is
  substrate; `Ai` is policy — the same split the `Ai` skill draws.
- **Caller supplies line-of-sight.** `exposureMap`/`killzones` take a `hasLos: Cell -> Cell -> bool` oracle
  (as `Ai` does), so which cast decides visibility — and whether a bush blocks it — stays the caller's
  policy; `MapAnalysis` adds no `Los` dependency.
- **Deterministic & total.** Row-major enumeration; `Map`/sorted-list results; a degenerate map yields an
  empty result; never throws (given a total `hasLos`).

## Scope Boundaries
- **In:** `exposureMap`/`coverMap`/`killzones`; a correctness + determinism + totality suite; the surface
  baseline; a SKILL.md section drawing the tactical-shape-vs-`Ai` boundary; the `fs-gg-mapcraft` finalize note.
- **Out:** any dynamic/enemy-keyed threat (that is `Ai`); render-tier work.

## Policy Pointers
- Honors constitution I, III (surface + baseline), IV, V, VI (property tests), "no silent output changes".
- Tier 1: extends the public `MapAnalysis` surface.

## Lifecycle Notes
- The exposure symmetry (`a` sees `b` ⇒ `b` counts `a`) and the cover-count fixtures are the headline tests.
- This is the terminal Part II milestone — the `fs-gg-mapcraft` analysis phase is complete after it.
- Next lifecycle action: `fsgg-sdd specify --work 038-mapanalysis-tactical`.
