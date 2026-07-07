---
schemaVersion: 1
workId: 004-collision-circle-manifolds
title: Collision circle manifolds (circle-circle, circle-AABB)
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

# Collision circle manifolds (circle-circle, circle-AABB) Charter

## Identity
- Work id: `004-collision-circle-manifolds`
- The first roadmap slice after the shipped AABB manifold: extend
  `FS.GG.Game.Core.Geometry` with **circle–circle** and **circle–AABB** contact detection,
  returning the same `Contact` manifold (unit-axis/direction normal + positive penetration depth)
  that `aabbContact` produces. Detection only — pure, total, byte-deterministic narrow-phase.
- First work item of the collision capsule (Phase A of the capability-capsule roadmap); mirrors the
  merged `aabbContact` pattern (PR #12).

## Principles
- **Detection is separate from response** — return manifolds as values; resolution/response is a
  later work item (006), never authored here.
- **Pure, total, NaN-safe** — no I/O, no wall-clock; degenerate inputs (coincident centers, a
  circle center inside the box, zero radius) return a documented deterministic result, never throw.
- **Determinism is a tested property** — byte-identical output across runs/platforms, proven by
  FsCheck invariants + a filter-nameable golden set (so the gate's zero-match guard binds it).
- **Integer/squared math in the logic core** — compare **squared** distances; push any `sqrt`/
  irrational to the presentation edge, per the corpus's "integer logic, float presentation" rule.
- **Documented tie-breaks** — the corpus's ordering-nondeterminism enemy: coincident centers and
  the center-inside-box case each get one stated, tested tie-break.

## Scope Boundaries
- **In:** `circleContact` (circle–circle) and `circleAabbContact` (circle–AABB) returning
  `Contact option`; a `Circle` primitive type if the spec needs one; the constraint-face invariants
  (isSome≡overlap, positive depth, unit normal, MTV-separates, symmetry) + a determinism golden set;
  the `.fsi` surface + refreshed baseline; binding the goldens into the existing determinism gate.
- **Out:** response/resolution (→006); ray/segment & SAT/OBB manifolds (→005); broad-phase /
  `SpatialGrid` integration (→007); any architecture shell, adapter, or sample; `capabilities.yml`
  domain wiring (→008).

## Policy Pointers
- Constitution **I** (specify-before-implement), **III** (public surface declared — `.fsi` +
  surface baseline), **V** (MUE boundary — pure core, I/O at edges), **VI** (test evidence
  mandatory: tests fail-before/pass-after, real fixtures).
- Design authority: the capsule corpus `docs/reports/2026-07-07-*` (three faces; determinism-as-
  tested-property; the collision design `docs/reports/2026-07-05-game-logic-collision-detection-design.md`).
- Light governance profile (advisory) — this work reports readiness; it does not enforce.

## Lifecycle Notes
- **Tier 1** (contracted): adds public `Geometry` signatures, so `.fsi` + baseline + tests land
  together and the surface-baseline-drift gate must stay green.
- Determinism golden tests must carry a stable, filterable name (e.g. a `determinism golden`
  substring) so the `gate.yml` "Determinism & property invariants" job's zero-match guard covers
  them, exactly as the `aabbContact` goldens do.
- Next lifecycle action: `fsgg-sdd specify --work 004-collision-circle-manifolds`.
