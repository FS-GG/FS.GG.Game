---
schemaVersion: 1
workId: 023-dice-distributions
title: Dice and Damage Distributions
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

# Dice and Damage Distributions Charter

## Identity
Milestone M4 item 3.1 of the Red Blob Games algorithm roadmap
(`docs/reports/2026-07-19-redblobgames-algorithm-incorporation-roadmap.md`, §4.1). Adds a `Dice`
module: a pure integer `Distribution` (outcome -> weight) with `convolve`/`mean`/`variance`, dice
constructors (`die`/`uniform`/`constant`), advantage/disadvantage (max/min of two rolls), and a
seeded `sample` that threads the existing `Rng`. Combat math — dice distributions — as deterministic
values.

## Principles
- **Integer weights, exact moments.** A `Distribution` is a `Map<int, int>` of outcome to positive
  weight; `convolve` multiplies weights and adds outcomes; `mean`/`variance` are the exact moments.
- **Determinism.** Distribution math is a pure function; `sample` is deterministic given the `Rng`
  seed, threading `Rng` in and out (no ambient RNG).
- **Sits on `Rng`.** `sample` reuses the shipped `Rng`; no new RNG.

## Scope Boundaries
- **In:** a `Distribution` type; `constant`/`uniform`/`die`; `convolve`/`repeat`; `advantage`/
  `disadvantage`; `mean`/`variance`/`outcomes`/`totalWeight`; seeded `sample`; a property suite.
- **Out:** exploding/critical dice and drop-lowest (a later extension), continuous distributions,
  a full damage/`Effects` pipeline, and the other M4 items (templates 3.3, influence 3.2, PQ 3.4).

## Policy Pointers
- Honors constitution I, III (public surface), V (pure), VI (test evidence).
- Tier 1 (contracted): new `.fsi`, both surface baselines, and tests land together; drift gate green.
- Governance pointers are optional compatibility facts, not evaluated by this command.

## Lifecycle Notes
- Determinism/property tests carry a stable filterable name for the `gate.yml` guard.
- Next lifecycle action: `fsgg-sdd specify --work 023-dice-distributions`.
