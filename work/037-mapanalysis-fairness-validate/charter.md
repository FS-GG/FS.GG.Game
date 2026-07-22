---
schemaVersion: 1
workId: 037-mapanalysis-fairness-validate
title: MapAnalysis Distribution, Fairness & Validation
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

# MapAnalysis Distribution, Fairness & Validation Charter

## Identity
Milestone **M11** of the map construction & analysis design (Part II §8/§11) — the **keystone** of the
pipeline. Extends `MapAnalysis` with distribution/fairness measures (`spacing`, `fairness`, `coverage`) and
the `validate` battery (`Rule`/`Report`) that runs the analyses against a rule set and returns accept/reject
*with reasons* — the loop every map-building agent hand-rolls: produce → validate → re-produce. Depends on
M8–M10; extends `src/Game.Core/MapAnalysis.fs`/`.fsi`.

## Principles
- **The report is the loop.** `validate` returns a `Report` (Passed + Failures with reasons + the measured
  facts) so an agent can regenerate until it passes — the whole reason the analysis layer exists.
- **Fairness is a spread of distances.** `fairness` maps each spawn to its nearest-resource hop distance; a
  designer checks the spread. `spacing` measures how separated points are; `coverage` the fraction of floor
  within a radius of some point.
- **Deterministic & total.** Rules evaluated in list order; failures in that order; degenerate input yields a
  documented value; never throws.

## Scope Boundaries
- **In:** `spacing`/`fairness`/`coverage`; `Rule`/`Report`/`validate`; a correctness + determinism + totality
  suite; a worked `generate → validate` example over a `MapGen` cave; the surface baseline; a SKILL.md section.
- **Out:** tactical analysis (M12); render-tier work.

## Policy Pointers
- Honors constitution I, III (surface + baseline), IV, V, VI (property tests), "no silent output changes".
- Tier 1: extends the public `MapAnalysis` surface with new types (`Rule`/`Report`) + values.

## Lifecycle Notes
- The `validate` accept/reject-with-reasons behaviour on a known-good vs known-bad map is the headline test.
- Next lifecycle action: `fsgg-sdd specify --work 037-mapanalysis-fairness-validate`.
