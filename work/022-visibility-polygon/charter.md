---
schemaVersion: 1
workId: 022-visibility-polygon
title: 2D Visibility Polygon
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

# 2D Visibility Polygon Charter

## Identity
Milestone M3 item 2.4 of the Red Blob Games algorithm roadmap
(`docs/reports/2026-07-19-redblobgames-algorithm-incorporation-roadmap.md`, §3.4). Adds a 2D
**visibility polygon** — the continuous region visible from a light/eye point over a set of wall
segments — to the `FS.GG.Game.Harness` authoring layer, for light sources, vision cones, and
fog-of-war over segment geometry (complementing the grid shadowcasting in `Fov`/`Los`). Per the
roadmap this is the highest determinism risk, so it is **deliberately staged in the harness** (the
non-deterministic authoring/visualization layer) and the promotion-to-Core decision is recorded, not
taken, in this work item.

## Principles
- **Deterministic ordering, no `atan2`.** Endpoints sort by an integer **pseudo-angle** (quadrant +
  slope) and the nearest wall is chosen by **squared** distance — so the sweep order is reproducible.
- **Float coordinates are the boundary, and stay in the harness.** Ray/segment intersection points
  are `Point` (float, IEEE-reproducible); the module lives in `Game.Harness`, not Core, exactly
  because of this. Promotion to Core is deferred until the numeric contract is proven.
- **Star-shaped correctness.** The polygon is star-shaped about the origin (every vertex visible) and
  contains the origin; a wall casts a shadow.

## Scope Boundaries
- **In:** `VisibilityPolygon.polygon origin bounds segments : Point list` in `Game.Harness`; a
  pseudo-angle order + squared-distance nearest test; property tests (origin inside, star-shaped,
  empty-room ≈ bounds, a wall casts a shadow, reproducibility, totality); a recorded
  promotion-to-Core decision.
- **Out:** promoting the module to `Game.Core`, exact-rational intersection arithmetic, dynamic/soft
  shadows or lighting attenuation, and the other M3 item (any-angle smoothing 2.2, already shipped).

## Policy Pointers
- Honors constitution I, III (public surface), V (pure — no ambient clock/RNG/IO; the harness
  DependencyTests still pass), VI (test evidence).
- Tier 1 (contracted): new `Game.Harness` `.fsi`, its surface baseline, and tests land together.
- Governance pointers are optional compatibility facts, not evaluated by this command.

## Lifecycle Notes
- Reproducibility/property tests carry a stable filterable name for the `gate.yml` guard.
- Next lifecycle action: `fsgg-sdd specify --work 022-visibility-polygon`.
