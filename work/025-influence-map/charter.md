---
schemaVersion: 1
workId: 025-influence-map
title: Influence-Map Recipe
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

# Influence-Map Recipe Charter

## Identity
Milestone M4 item 3.2 of the Red Blob Games algorithm roadmap
(`docs/reports/2026-07-19-redblobgames-algorithm-incorporation-roadmap.md`, §4.2). Largely subsumed by
the shipped `Pathfinding.distanceField`; the incremental value is a thin `Ai.influenceMap` wrapper —
an integer linear-falloff influence map over per-source distance fields — and a documented
friendly-vs-enemy tension recipe.

## Principles
- **Thin wrapper over `distanceField`.** No new search engine; `influenceMap` composes per-source
  `distanceField` calls and combines them, inheriting determinism and the cost convention.
- **Integer, deterministic.** Influence is integer (linear falloff in `baseStep` units); the combine
  is a deterministic max.

## Scope Boundaries
- **In:** `Ai.influenceMap neighbourhood maxVisited cost sources` (strengthed sources → integer
  linear-falloff influence, combined by max) and a documented tension recipe (caller subtracts two
  maps); a property suite.
- **Out:** float LOS-based influence (that is the existing `threatField`), decay/temporal influence,
  and the other M4 items (dice 3.1, templates 3.3, bucketed PQ 3.4).

## Policy Pointers
- Honors constitution I, III (public surface), V (pure), VI (test evidence).
- Tier 1 (contracted): `.fsi`, both surface baselines, and tests land together; drift gate green.
- Governance pointers are optional compatibility facts, not evaluated by this command.

## Lifecycle Notes
- Determinism/property tests carry a stable filterable name for the `gate.yml` guard.
- Next lifecycle action: `fsgg-sdd specify --work 025-influence-map`.
