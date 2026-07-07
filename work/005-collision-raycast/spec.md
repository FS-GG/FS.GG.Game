---
schemaVersion: 1
workId: 005-collision-raycast
title: Collision Raycast
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Collision Raycast Specification

Prose status: specified

## User Value
Game code can cast a line **segment** against an AABB or a circle and get the first forward surface
crossing as a pure `RayHit` value — the parameter `t` along the segment, the hit `Point`, and the
surface `Normal`. This powers line-of-fire, projectile, and pick/selection queries deterministically,
reusing the slab clip already proven in `sweptIntersects`.

## Scope
- SB-001: Segment-cast **detection queries** in `FS.GG.Game.Core.Geometry` — `segmentAabbHit`
  (slab method) and `segmentCircleHit` (quadratic) — returning a new `RayHit` value on the existing
  `Point`/`Rect`/`Circle`.
- SB-002: Reuse the constraint-face harness (FsCheck + filter-named determinism goldens bound to the
  `gate.yml` guard).

## Non-Goals
- SB-003: No SAT/OBB or convex manifolds (→ 005b), no infinite-ray distance parameterization
  (segments, `t ∈ [0,1]`, only), no resolution (→ 006), no broad-phase (→ 007), no architecture
  shell/sample, and no `capabilities.yml` domain wiring (→ 008).

## User Stories
- US-001 (P1): As game-logic code, I can cast a segment at an AABB and learn where and with what
  surface normal it first enters, so I can resolve line-of-fire against rectangular bodies.
- US-002 (P1): As game-logic code, I can cast a segment at a circle and learn the entry point and
  normal, so I can resolve line-of-fire against circular bodies.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given a segment that crosses into a box from outside, when `segmentAabbHit` is called, then it returns `Some` with `T` the entry parameter; given a segment that misses or starts inside, then it returns `None`.
- AC-002 [US-001] [FR-002]: Given a segment entering a box, when `segmentAabbHit` returns a hit, then `Point = p0 + (p1 − p0) × T` lies on the box boundary and `Normal` is the outward unit axis of the entered face.
- AC-003 [US-002] [FR-003]: Given a segment whose near quadratic root against a circle lies in `[0,1]`, when `segmentCircleHit` is called, then it returns `Some`; given a miss or an origin inside the circle, then it returns `None`.
- AC-004 [US-002] [FR-004]: Given a segment entering a circle, when `segmentCircleHit` returns a hit, then `Point` lies on the circle and `Normal = (Point − Center) / Radius` is unit length.
- AC-005 [US-001] [FR-005]: Given a zero-length segment, a NaN operand, or a non-positive radius, when either function is called, then it returns `None` and never throws.
- AC-006 [US-001] [FR-006]: Given a segment that enters an AABB exactly at a corner (equal per-axis entry parameter), when `segmentAabbHit` is called, then the X-axis face is chosen deterministically, and identical inputs yield a byte-identical `RayHit` across runs and platforms.

## Functional Requirements
- FR-001: `segmentAabbHit` MUST return `Some RayHit` exactly when the segment enters the box from outside within `t ∈ [0,1]` (slab `tEnter ≤ tExit`, `tEnter ∈ [0,1]`) and `None` on miss or an origin inside the box. (covers AC-001)
- FR-002: `segmentAabbHit`'s hit MUST carry `T` = the entry parameter, `Point = p0 + (p1 − p0) × T` on the box boundary, and `Normal` = the outward unit axis normal of the entered face. (covers AC-002)
- FR-003: `segmentCircleHit` MUST return `Some RayHit` exactly when the near root of the ray–circle quadratic lies in `[0,1]` (the segment enters the circle from outside) and `None` on a negative discriminant, a degenerate segment, or an origin inside the circle. (covers AC-003)
- FR-004: `segmentCircleHit`'s hit MUST carry `T` = the near root, `Point` on the circle, and `Normal = (Point − Center) / Radius` (unit length). (covers AC-004)
- FR-005: Both functions MUST be pure and total: a zero-length segment, a NaN operand, or a non-positive radius yields `None` without throwing. (covers AC-005)
- FR-006: Both functions MUST be byte-deterministic, with a documented tie-break when a segment enters an AABB exactly at a corner (equal per-axis entry parameter) — the X-axis face wins. (covers AC-006)

## Ambiguities
- AMB-001: `RayHit` representation — a public `RayHit = { T: float; Point: Point; Normal: Point }`
  record, or a tuple? (Affects the surface baseline.)
- AMB-002: Origin-inside behavior — a segment starting inside the shape returns `None` (no external
  entry) versus `T = 0`. (Proposed: `None`, matching the entry-from-outside semantics.)
- AMB-003: AABB corner entry tie-break — when the X and Y entry parameters are equal, which face
  normal wins? (Proposed: X axis, consistent with the 004 manifold tie-breaks.)

## Public Or Tool-Facing Impact
- Tier 1 (contracted). Adds a public `RayHit` type and `segmentAabbHit` / `segmentCircleHit`
  signatures, so `.fsi`, the surface baseline (`readiness/surface-baselines/FS.GG.Game.Core.txt`),
  and tests land together; the surface-baseline-drift gate must stay green.

## Lifecycle Notes
- Determinism golden tests MUST carry a `determinism golden` name substring for the `gate.yml`
  zero-match guard.
- Next lifecycle action: `fsgg-sdd clarify --work 005-collision-raycast`.
