---
schemaVersion: 1
workId: 024-line-circle-templates
title: Line and Circle Templates
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

# Line and Circle Templates Charter

## Identity
Milestone M4 item 3.3 of the Red Blob Games algorithm roadmap
(`docs/reports/2026-07-19-redblobgames-algorithm-incorporation-roadmap.md`, §4.3). Adds AoE/range
templates: a filled `Grids.disc` and an outline `Grids.ring` (integer midpoint circle) for blast
radii and range rings, and confirms/reuses the shipped `Los.line` as the line-template primitive
rather than duplicating it.

## Principles
- **Reuse, don't duplicate.** `Los.line` is already the grid line primitive; line templates use it.
  Only the circle rasterization is genuinely new.
- **Integer and total.** Squared-distance tests in int64 (overflow-safe); midpoint circle is pure
  integer; fixed emission order; deterministic.

## Scope Boundaries
- **In:** `Grids.disc center radius` (filled) and `Grids.ring center radius` (midpoint-circle outline);
  a property suite; a test confirming `Los.line` is the line-template primitive.
- **Out:** cones/arcs, hex disc/ring (depends on 2.1's hex model beyond scope), thick lines, and the
  other M4 items (dice 3.1, influence 3.2, bucketed PQ 3.4).

## Policy Pointers
- Honors constitution I, III (public surface), IV (idiomatic simplicity), V (pure), VI (test evidence).
- Tier 1 (contracted): `.fsi`, both surface baselines, and tests land together; drift gate green.
- Governance pointers are optional compatibility facts, not evaluated by this command.

## Lifecycle Notes
- Determinism/property tests carry a stable filterable name for the `gate.yml` guard.
- Next lifecycle action: `fsgg-sdd specify --work 024-line-circle-templates`.
