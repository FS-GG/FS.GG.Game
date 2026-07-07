---
schemaVersion: 1
workId: 006-collision-resolution-layer
title: Collision Resolution Layer
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Collision Resolution Layer Specification

Prose status: specified

## User Value
Game code can turn a detection `Contact` into a **response** — separate an overlapping body along the
minimum-translation vector (`pushOut`), stop it by killing the velocity component along the contact
normal while keeping the tangential slide (`slide`), and apply discrete grid **knockback** that stops
before a blocked cell — as pure, deterministic transforms in a new `FS.GG.Game.Core.Resolution`
module kept separate from detection. This is the arcade/kinematic response the corpus ships
first-class, reusing the `Contact`/`Point`/`Cell` values `Geometry` and the grid already produce.

## Scope
- SB-001: A new `FS.GG.Game.Core.Resolution` module with `pushOut`, `slide`, and `knockback`
  consuming the existing `Contact`/`Point`/`Cell`; **arcade/kinematic response only**. Response is a
  layer separate from detection (`Resolution` never re-detects).
- SB-002: Reuse the constraint-face harness (FsCheck invariants + filter-named determinism goldens
  bound to the `gate.yml` zero-match guard) and the surface-baseline-drift gate.

## Non-Goals
- SB-003: No impulse-based resolution (mass/restitution/friction/Baumgarte — the optional heavy
  layer); no swept/CCD response; no multi-contact stacking solver; no slopes or one-way platforms;
  no `Body`/world step loop (a shell/sample concern); no broad-phase (→ 007); no architecture shell
  or sample; no `capabilities.yml` domain wiring (→ 008).

## User Stories
- US-001 (P1): As game-logic code, I can push an overlapping body out of penetration along a
  `Contact`'s minimum-translation vector and stop its inward motion by sliding its velocity along the
  contact surface, so a moving body resolves cleanly against a wall without tunnelling or jitter.
- US-002 (P1): As turn-based-tactics code, I can knock a unit back a number of grid cells along a
  direction and have it stop in the last free cell before a wall or occupant, so knockback respects
  the board without overshooting into blocked space.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given a body at `position` overlapping per a `Contact { Normal; Depth }`, when `pushOut position contact` is called, then it returns `position − Normal × Depth` (the body translated out along the MTV), and a zero-`Depth` contact returns `position` unchanged.
- AC-002 [US-001] [FR-002]: Given a velocity and a unit contact `normal`, when `slide velocity normal` is called, then the result's component along `normal` is zero (`result · normal = 0`) and its tangential component equals the input's (`result = velocity − (velocity · normal) × normal`).
- AC-003 [US-002] [FR-003]: Given a `start` cell, a per-cell `step`, a `distance`, and a `blocked` predicate, when `knockback start step distance blocked` is called, then it returns the cell reached by advancing up to `distance` steps along `step`, stopping in the last cell before the first `blocked` cell; the returned cell is never `blocked`, and the number of steps taken never exceeds `distance`.
- AC-004 [US-001] [FR-004]: Given a NaN operand (`pushOut`/`slide`), a non-positive `distance`, or a first step that is immediately `blocked` (`knockback`), when the function is called, then it returns without throwing — NaN flows through arithmetically, and a non-positive distance or immediately-blocked step returns `start` unchanged.
- AC-005 [US-001] [FR-005]: Given identical inputs, when any `Resolution` function is called, then it yields a byte-identical result across runs and platforms — arithmetic is IEEE-deterministic and grid stepping advances in a fixed order.

## Functional Requirements
- FR-001: `pushOut position contact` MUST return `position − contact.Normal × contact.Depth` (separate the body along the MTV); a zero-`Depth` contact leaves `position` unchanged. (covers AC-001)
- FR-002: `slide velocity normal` MUST return `velocity − (velocity · normal) × normal` for a unit `normal`, so the result's normal component is zero and its tangential component is unchanged. (covers AC-002)
- FR-003: `knockback start step distance blocked` MUST advance from `start` by `step` up to `distance` times, stopping in the last cell before the first `blocked` cell; the returned cell MUST NOT be `blocked` and the steps taken MUST NOT exceed `distance`. (covers AC-003)
- FR-004: All `Resolution` functions MUST be pure and total: a NaN operand flows through without throwing; a non-positive `distance` or an immediately-blocked first step returns `start` unchanged. (covers AC-004)
- FR-005: All `Resolution` functions MUST be byte-deterministic — IEEE arithmetic and fixed-order grid stepping — so identical inputs yield identical output across runs and platforms. (covers AC-005)

## Ambiguities
- AMB-001: `slide`'s `normal` — assume a **unit** vector (as every `Contact.Normal` is) and skip an
  internal normalization, versus normalize defensively. (Proposed: assume unit — no internal `sqrt`,
  matching the manifold-normal contract; a non-unit normal is a caller error.)
- AMB-002: `slide` projection — **unconditional** `v − (v·n)·n` (kills the normal component whatever
  its sign) versus approach-only (remove only the inward component). (Proposed: unconditional, per
  the design §6.1 "slide = keep tangential, kill normal"; approach-only is a caller concern.)
- AMB-003: `pushOut` slop — exact push-out by `Depth`, versus subtract a small slop so
  exactly-touching does not jitter. (Proposed: exact `Depth`; slop is a caller concern — pass a
  `Contact` with reduced `Depth` — so the primitive stays a clean, testable separation.)
- AMB-004: `knockback`'s `step`/`distance` — `step` is the **per-cell delta** (e.g. `{Col=1;Row=0}`)
  applied up to `distance` times, versus `distance` being an absolute cell count with `step` a sign.
  Also whether the `start` cell itself is tested. (Proposed: `step` is the per-cell delta applied up
  to `distance` times; `start` is assumed free — only each *next* cell is tested against `blocked`.)

## Public Or Tool-Facing Impact
- Tier 1 (contracted). Adds a public `Resolution` module (`pushOut`/`slide`/`knockback`), so `.fsi`,
  the surface baseline (`readiness/surface-baselines/FS.GG.Game.Core.txt`), and tests land together;
  the surface-baseline-drift gate must stay green.

## Lifecycle Notes
- Determinism golden tests MUST carry a `determinism golden` name substring for the `gate.yml`
  zero-match guard.
- Reuses `Contact`/`Point`/`Cell` — no new types. Response (`Resolution`) is a separate layer from
  detection (`Geometry`).
- Next lifecycle action: `fsgg-sdd clarify --work 006-collision-resolution-layer`.
