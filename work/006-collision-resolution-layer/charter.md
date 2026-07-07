---
schemaVersion: 1
workId: 006-collision-resolution-layer
title: Collision resolution layer (kinematic slide/push-out + grid knockback)
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

# Collision resolution layer (kinematic slide/push-out + grid knockback) Charter

## Identity
- Work id: `006-collision-resolution-layer`
- Fifth collision-capsule slice, and the first **response** slice: add a new
  `FS.GG.Game.Core.Resolution` module — the arcade/kinematic response layer that *consumes* the
  detection `Contact` (and the discrete grid `Cell`) and produces transforms. Three orthogonal pure
  primitives: `pushOut` (separate a body along `−Normal × Depth`), `slide` (kill the normal component
  of a velocity, keep the tangential — the "stop"), and `knockback` (discrete cell displacement along
  a step, stopped by a blocker). This is the corpus's §6.1 arcade default.
- Roadmap Phase A item 006. Detection (`Geometry`) stays a separate layer from response
  (`Resolution`) — the corpus's load-bearing "detection separate from response" rule.

## Principles
- **Response is a separate layer from detection** — `Resolution` consumes the `Contact`/`Cell`
  values `Geometry` and the grid produce; it never re-detects. Pure transforms, no shared state.
- **Arcade/kinematic first** — detect → push out along the MTV → zero the velocity along the contact
  normal. Impulse-based physics (mass/restitution/friction) is explicitly out of scope (a later
  optional heavy layer).
- **Pure, total, deterministic** — every function is a pure transform; NaN operands flow through
  without throwing; a non-positive knockback distance or an immediately-blocked step returns the
  start unchanged; grid stepping is order-deterministic.
- **Reuse the established vocabulary** — `Contact` (Normal + Depth), `Point`, and the grid `Cell`
  `{ Col; Row }`; no new look-alike types.

## Scope Boundaries
- **In:** a new `Resolution` module with `pushOut: position → contact → Point` (separate along
  `−Normal × Depth`), `slide: velocity → normal → Point` (`v − (v·n)·n`, normal assumed unit), and
  `knockback: start → step → distance → blocked → Cell` (discrete displacement stopped before a
  blocked cell); the constraint-face invariants (push-out separates, slide zeroes the normal
  component and preserves the tangential, knockback never enters a blocked cell nor exceeds
  `distance`, NaN/degenerate totality) + a filter-named determinism golden set; `.fsi` surface +
  refreshed baseline; goldens under the existing gate guard.
- **Out:** impulse-based resolution (mass/restitution/friction/Baumgarte — the optional heavy layer);
  swept/CCD response and multi-contact stacking solvers; slopes/one-way platforms; a `Body`/world
  step loop (that is a shell/sample concern, → Phase C/D); broad-phase (→ 007); any architecture
  shell or sample; `capabilities.yml` domain wiring (→ 008).

## Policy Pointers
- Constitution I (specify-before-implement), III (public surface declared — `.fsi` + baseline),
  V (MUE boundary — pure core), VI (test evidence mandatory).
- Design authority: the collision design `docs/reports/2026-07-05-game-logic-collision-detection-design.md`
  §6.1 (arcade/kinematic: push-out, slide, knockback) and the capsule corpus `docs/reports/2026-07-07-*`.
- Light governance profile (advisory).

## Lifecycle Notes
- **Tier 1**: adds a public `Resolution` module + signatures; `.fsi`, baseline, and tests land
  together. Reuses `Contact`/`Point`/`Cell` — no new types. First response slice; establishes the
  detection↔response layer split in code.
- Determinism goldens carry a `determinism golden` name substring for the `gate.yml` zero-match guard.
- Next lifecycle action: `fsgg-sdd specify --work 006-collision-resolution-layer`.
