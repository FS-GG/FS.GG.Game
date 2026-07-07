---
schemaVersion: 1
workId: 005-collision-raycast
title: Collision raycast (segment vs AABB and circle)
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

# Collision raycast (segment vs AABB and circle) Charter

## Identity
- Work id: `005-collision-raycast`
- Third collision-capsule slice: add **segment-cast queries** to
  `FS.GG.Game.Core.Geometry` — segment vs AABB (slab method) and segment vs circle (quadratic) —
  returning a new `RayHit` value (parameter `t`, hit `Point`, surface `Normal`). This is the
  detection query the corpus lists as "ray/segment–AABB (slab), ray/segment–circle (quadratic)",
  distinct from the penetration `Contact` manifolds of 004.
- Roadmap Phase A (re-scoped from the original 005; SAT/OBB split to 005b).

## Principles
- **Query, not response** — return the first forward crossing as a value; no resolution.
- **Pure, total, NaN-safe** — a degenerate (zero-length) segment, a NaN operand, or a non-positive
  radius yields `None`, never throws.
- **Determinism is a tested property** — slab divisions and the one quadratic `sqrt` are IEEE
  deterministic; corner/tie cases get a documented tie-break, proven by filter-named goldens.
- **Entry semantics are explicit** — report the first crossing INTO the shape from outside within
  the segment parameter `t ∈ [0, 1]`; a segment originating inside the shape reports no entry.

## Scope Boundaries
- **In:** a public `RayHit = { T: float; Point: Point; Normal: Point }` type; `segmentAabbHit` and
  `segmentCircleHit` returning `RayHit option`; the constraint-face invariants (t ∈ [0,1], hit point
  on surface, unit normal, NaN/degenerate totality) + a filter-named determinism golden set; `.fsi`
  surface + refreshed baseline; goldens covered by the existing gate guard.
- **Out:** SAT/OBB and convex manifolds (→ 005b); infinite rays with distance parameterization
  (segments only here); resolution/response (→ 006); broad-phase (→ 007); any architecture shell or
  sample; `capabilities.yml` domain wiring (→ 008).

## Policy Pointers
- Constitution I (specify-before-implement), III (public surface declared — `.fsi` + baseline),
  V (MUE boundary — pure core), VI (test evidence mandatory).
- Design authority: the capsule corpus `docs/reports/2026-07-07-*` and the collision design
  `docs/reports/2026-07-05-game-logic-collision-detection-design.md` (slab method, Amanatides–Woo).
- Light governance profile (advisory).

## Lifecycle Notes
- **Tier 1**: adds public `Geometry` signatures + a `RayHit` type; `.fsi`, baseline, and tests land
  together. Reuses the slab clip idiom already in `sweptIntersects`.
- Determinism goldens carry a `determinism golden` name substring for the `gate.yml` zero-match guard.
- Next lifecycle action: `fsgg-sdd specify --work 005-collision-raycast`.
