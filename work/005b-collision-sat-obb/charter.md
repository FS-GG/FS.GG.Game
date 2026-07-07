---
schemaVersion: 1
workId: 005b-collision-sat-obb
title: SAT/OBB convex collision manifolds
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

# SAT/OBB convex collision manifolds Charter

## Identity
- Work id: `005b-collision-sat-obb`
- Fourth collision-capsule slice: add **convex-polygon narrow-phase manifolds** to
  `FS.GG.Game.Core.Geometry` via the **Separating Axis Theorem (SAT)**. Introduces a new
  `ConvexPolygon` primitive (a convex, CCW-wound vertex ring) with an `obbPolygon` constructor for
  the oriented-bounding-box case, and a `polygonContact` function returning the existing penetration
  `Contact` (minimum-translation-vector `Normal` + `Depth`).
- Roadmap Phase A, split out of the original 005 (segment-cast shipped as the sibling slice #14).
  This completes the convex-manifold half the corpus lists as "SAT/OBB convex, MTV, parallel-axis
  dedup".

## Principles
- **Detection, not response** — return the minimum translation vector as a `Contact` value; no
  resolution (that is 006). Reuses `Contact` so the resolution layer consumes one manifold shape.
- **Contract parity with the other manifolds** — `polygonContact a b |> Option.isSome` is true
  exactly on positive-area overlap; touching (zero-overlap on a separating axis) is `None`, matching
  the strict-edge convention of `aabbContact`/`circleContact`.
- **Pure, total, NaN-safe** — a degenerate polygon (< 3 vertices, zero area, or a NaN operand)
  yields `None`, never throws. `ConvexPolygon` is a plain vertex ring; convexity/winding is a
  documented input convention, not a runtime-enforced invariant.
- **Determinism is a tested property** — SAT projections and the MTV comparison are IEEE
  deterministic; the candidate-axis generation order and the equal-minimum-overlap tie-break are
  fixed and documented, proven by filter-named goldens.
- **Parallel-axis dedup** — antiparallel and duplicate candidate axes (opposite OBB faces, shared
  orientations) are collapsed to one before projection, so the axis set is minimal and the tie-break
  is stable.

## Scope Boundaries
- **In:** a public `ConvexPolygon = { Vertices: Point[] }` type; an `obbPolygon: center → halfExtents
  → rotation → ConvexPolygon` constructor; `polygonContact: a: ConvexPolygon → b: ConvexPolygon →
  Contact option` via SAT (deduped axes, MTV, `Normal` oriented a→b); the constraint-face invariants
  (isSome ⇔ overlap, unit normal, positive depth, MTV separates, NaN/degenerate totality) + a
  filter-named determinism golden set; `.fsi` surface + refreshed baseline; goldens under the
  existing gate guard.
- **Out:** general (non-convex) polygons; polygon–circle and polygon–AABB cross manifolds (a later
  slice if the corpus calls for it — this slice is convex↔convex, with AABB/OBB expressible as
  polygons); GJK/EPA (SAT only here); resolution/response (→ 006); broad-phase (→ 007); any
  architecture shell or sample; `capabilities.yml` domain wiring (→ 008).

## Policy Pointers
- Constitution I (specify-before-implement), III (public surface declared — `.fsi` + baseline),
  V (MUE boundary — pure core), VI (test evidence mandatory).
- Design authority: the capsule corpus `docs/reports/2026-07-07-*` and the collision design
  `docs/reports/2026-07-05-game-logic-collision-detection-design.md` (SAT, MTV, parallel-axis dedup).
- Light governance profile (advisory).

## Lifecycle Notes
- **Tier 1**: adds public `Geometry` signatures + a `ConvexPolygon` type + `obbPolygon` constructor;
  `.fsi`, baseline, and tests land together. Reuses the `Contact` type and the a→b normal / MTV
  convention already established by `aabbContact`/`circleContact`.
- Determinism goldens carry a `determinism golden` name substring for the `gate.yml` zero-match guard.
- Next lifecycle action: `fsgg-sdd specify --work 005b-collision-sat-obb`.
