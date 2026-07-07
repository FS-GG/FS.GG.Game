---
schemaVersion: 1
workId: 004-collision-circle-manifolds
title: Collision Circle Manifolds
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Collision Circle Manifolds Specification

Prose status: specified

## User Value
Game-logic code can obtain an exact **circle–circle** or **circle–AABB** collision manifold — a
contact normal plus penetration depth — as a pure value, the same `Contact` shape the shipped AABB
narrow-phase (`Geometry.aabbContact`) returns. This lets a game detect circular-body overlaps
deterministically and hand the manifold to a later resolution layer, without hand-rolling the
degenerate cases (coincident centers, a circle center inside the box).

## Scope
- SB-001: Circle collision **detection** in `FS.GG.Game.Core.Geometry` — `circle–circle` and
  `circle–AABB` manifolds returning `Contact option`, on the existing `Point`/`Rect` plus a circle
  representation.
- SB-002: Reuse the shipped `Contact` manifold shape and the existing constraint-face invariant
  harness (FsCheck + filter-named determinism goldens bound to the `gate.yml` guard).

## Non-Goals
- SB-003: No response/resolution (→ 006), no ray/segment or SAT/OBB manifolds (→ 005), no
  broad-phase/`SpatialGrid` integration (→ 007), no architecture shell/adapter/sample, and no
  `capabilities.yml` domain wiring (→ 008).

## User Stories
- US-001 (P1): As game-logic code, I can test two circles for contact and receive the separation
  manifold, so overlaps are detected and later resolvable deterministically.
- US-002 (P1): As game-logic code, I can test a circle against an axis-aligned box and receive the
  contact manifold, including when the circle's center lies inside the box.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given two circles whose center distance is strictly less than the sum of their radii, when `circleContact` is called, then it returns `Some`; given touching or disjoint circles, then it returns `None`.
- AC-002 [US-001] [FR-002]: Given two overlapping circles, when `circleContact` returns a manifold, then `Normal` is the unit vector from the first center toward the second and `Depth` equals `(rA + rB) − centerDistance`.
- AC-003 [US-002] [FR-003]: Given a circle overlapping a box, when `circleAabbContact` is called, then it returns `Some` with a normal derived from the circle center clamped to the box; given a gap, then it returns `None`.
- AC-004 [US-002] [FR-004]: Given a circle whose center lies inside the box, when `circleAabbContact` is called, then it returns a manifold whose normal is the least-penetration box face (the documented center-inside fallback).
- AC-005 [US-001] [FR-005]: Given a NaN coordinate/radius or a non-positive radius, when either function is called, then it returns `None` and never throws.
- AC-006 [US-001] [FR-006]: Given identical inputs across repeated or parallel runs, when either function is called, then the returned `Contact` is byte-identical, with the coincident-center and equal-face tie-breaks fixed.
- AC-007 [US-002] [FR-007]: Given the manifold of an overlapping pair, when the first shape is translated by `−Normal × Depth`, then the pair no longer overlaps on positive area.

## Functional Requirements
- FR-001: `circleContact` MUST return `Some Contact` exactly when two circles overlap on positive area (center distance strictly less than the radius sum) and `None` on touching or disjoint circles. (covers AC-001)
- FR-002: `circleContact`'s manifold MUST carry a unit `Normal` pointing from the first circle's center toward the second and `Depth = (rA + rB) − centerDistance`. (covers AC-002)
- FR-003: `circleAabbContact` MUST return `Some Contact` exactly when the circle overlaps the box on positive area, computed by clamping the circle center to the box and comparing squared distance to the squared radius, and `None` otherwise. (covers AC-003)
- FR-004: When the circle center lies inside the box, `circleAabbContact` MUST return a manifold whose `Normal` is the box face of least penetration (the center-inside fallback), with a documented tie-break on equal penetration. (covers AC-004)
- FR-005: Both functions MUST be pure and total: a NaN coordinate/radius or a non-positive radius yields `None` without throwing. (covers AC-005)
- FR-006: Both functions MUST be byte-deterministic — identical inputs yield an identical `Contact` across runs and platforms — using squared-distance comparisons for the overlap test and documented tie-breaks (coincident centers → a fixed normal; equal box-face penetration → a fixed axis). (covers AC-006)
- FR-007: For any contact, translating the first shape by `−Normal × Depth` MUST remove the positive-area overlap, so the manifold is a valid minimum translation. (covers AC-007)

## Ambiguities
- AMB-001: Circle representation — introduce a public `Circle = { Center: Point; Radius: float }`
  primitive, or take bare `center: Point -> radius: float` arguments? (Affects the surface baseline.)
- AMB-002: Coincident-center circle–circle normal — when the two centers are equal (distance 0),
  the direction is undefined; fix it to a documented constant (proposed `(1, 0)`, depth `rA + rB`).
- AMB-003: Circle–AABB center-inside tie-break — on equal penetration across faces, which axis
  wins? (Proposed: mirror `aabbContact` — X axis, +direction bias, for cross-primitive consistency.)

## Public Or Tool-Facing Impact
- Tier 1 (contracted). Adds public `Geometry` signatures (`circleContact`, `circleAabbContact`) and,
  pending AMB-001, a public `Circle` type — so `.fsi` signatures, the surface baseline
  (`readiness/surface-baselines/FS.GG.Game.Core.txt`), and tests land together, and the
  surface-baseline-drift gate must stay green.

## Lifecycle Notes
- Determinism golden tests MUST carry a stable, filterable name (a `determinism golden` substring)
  so the `gate.yml` "Determinism & property invariants" zero-match guard covers them, exactly as the
  `aabbContact` goldens do.
- Next lifecycle action: `fsgg-sdd clarify --work 004-collision-circle-manifolds`.
