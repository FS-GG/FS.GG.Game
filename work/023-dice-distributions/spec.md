---
schemaVersion: 1
workId: 023-dice-distributions
title: Dice and Damage Distributions
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Dice and Damage Distributions Specification

Prose status: specified

## User Value
Game-logic code can build integer dice **Distributions** (`die`, `uniform`, `constant`), combine them
(`convolve` for the sum of independent rolls, `repeat`, `advantage`/`disadvantage` for max/min of two
rolls), read their **exact moments** (`mean`, `variance`), and draw **seeded samples** through the
existing `Rng` — deterministic combat math as pure values, without hand-rolling the convolution.

## Scope
- SB-001: A `Dice` module in `FS.GG.Game.Core` with a `Distribution` type (outcome -> positive integer
  weight) and `constant`/`uniform`/`die`/`convolve`/`repeat`/`advantage`/`disadvantage`/`mean`/
  `variance`/`outcomes`/`totalWeight`/`sample`.
- SB-002: A property suite: weight-map correctness, convolution = sum distribution, advantage/
  disadvantage = max/min, exact moments (d6 mean 3.5, variance 35/12), and sample determinism +
  empirical convergence.

## Non-Goals
- SB-003: No exploding/critical dice or drop-lowest (a later extension), no continuous distributions,
  no full damage/`Effects` pipeline, and none of the other M4 items (templates 3.3, influence 3.2,
  bucketed PQ 3.4).

## User Stories
- US-001 (P1): As game-logic code, I can build and combine dice distributions and read their exact
  moments, for deterministic combat resolution.
- US-002 (P1): As game-logic code, I can draw a seeded sample from a distribution through `Rng`,
  reproducibly.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given `die 6`, then its weights are `{1..6 -> 1}`; `constant v` is `{v -> 1}`; `uniform lo hi` is `{lo..hi -> 1}`.
- AC-002 [US-001] [FR-002]: Given two distributions, when `convolve a b` is called, then it is the distribution of `X + Y` for independent `X~a`, `Y~b` — each outcome `va+vb` gets weight `wa*wb` (summed over collisions) — and `repeat n d` is `d` convolved with itself `n` times (`repeat 0 d = constant 0`).
- AC-003 [US-001] [FR-003]: Given two distributions, when `advantage a b` / `disadvantage a b` is called, then it is the distribution of `max(X,Y)` / `min(X,Y)`; `mean (advantage d d) >= mean d >= mean (disadvantage d d)`.
- AC-004 [US-001] [FR-004]: Given `die 6`, then `mean` is `3.5` and `variance` is `35/12`; `mean` and `variance` are the exact weighted moments of any distribution.
- AC-005 [US-002] [FR-005]: Given a distribution and an `Rng`, when `sample` is called, then it returns an outcome in the distribution's support and a threaded `Rng`; the same seed yields the same sample sequence, and the empirical mean over many samples converges to `mean`.
- AC-006 [US-002] [FR-006]: Given any operation, when called repeatedly on identical inputs, then the result is byte-identical, and degenerate inputs (empty support, `repeat 0`, `uniform hi lo` with `hi<lo`) are total.

## Functional Requirements
- FR-001: `Dice.die sides` MUST be the uniform distribution over `1..sides`; `constant v` is `{v -> 1}`; `uniform lo hi` is the uniform distribution over `lo..hi` (empty when `hi < lo`). (covers AC-001)
- FR-002: `Dice.convolve a b` MUST be the sum distribution of independent `a` and `b` (outcome `va+vb`, weight `wa*wb` summed over collisions); `repeat n d` MUST be `d` convolved `n` times with `repeat 0 d = constant 0`. (covers AC-002)
- FR-003: `Dice.advantage a b` / `disadvantage a b` MUST be the distribution of `max` / `min` of independent `a`, `b`, and `mean` MUST be monotone accordingly. (covers AC-003)
- FR-004: `Dice.mean` and `Dice.variance` MUST be the exact weighted moments of a distribution (`mean = Sum(v*w)/Sum(w)`, `variance = Sum((v-mean)^2 * w)/Sum(w)`). (covers AC-004)
- FR-005: `Dice.sample d rng` MUST draw an outcome from `d`'s support with probability proportional to its weight, threading and returning the `Rng`, so identical seeds reproduce the sample sequence and the empirical mean converges to `mean`. (covers AC-005)
- FR-006: Every `Dice` operation MUST be pure, total, and byte-deterministic — integer weights, no ambient RNG, degenerate inputs handled without throwing. (covers AC-006)

## Ambiguities
- AMB-001: `Distribution` representation — an opaque type over `Map<int,int>`, or a bare `Map<int,int>`?
- AMB-002: `mean`/`variance` return type — exact rational, or `float`?
- AMB-003: `sample` on an empty distribution — throw, or a documented total fallback?

## Public Or Tool-Facing Impact
- Tier 1 (contracted). Adds a new public `Dice` module (type + functions) to `FS.GG.Game.Core`, a new
  `Dice.fs`/`.fsi` in the compile order — so the `.fsi`, both surface baselines, and tests land
  together, and the drift gate must stay green. No existing signature changes.

## Lifecycle Notes
- Determinism/property tests MUST carry a stable filterable name for the `gate.yml` guard.
- Next lifecycle action: `fsgg-sdd clarify --work 023-dice-distributions`.
