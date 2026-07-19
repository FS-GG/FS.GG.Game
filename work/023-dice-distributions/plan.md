---
schemaVersion: 1
workId: 023-dice-distributions
title: Dice and Damage Distributions
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/023-dice-distributions/spec.md
sourceClarifications: work/023-dice-distributions/clarifications.md
sourceChecklist: work/023-dice-distributions/checklist.md
publicOrToolFacingImpact: true
---

# Dice and Damage Distributions Plan

Prose status: planned

## Source Snapshot
- spec: work/023-dice-distributions/spec.md sha256:e108ffe08d37c60e801eab533de5f91af0bc54d9f4ca4f81f792a901ce6e2c8c schemaVersion:1
- clarifications: work/023-dice-distributions/clarifications.md sha256:a3a1422294c298a39fc265e4d1e84f61857a09f7c8736ac394423e98808c1ad8 schemaVersion:1
- checklist: work/023-dice-distributions/checklist.md sha256:6b1e26abf0144f015ebe16aea3cd97ed0901dbc4cd91b6071a6c5eb93774cb76 schemaVersion:1

## Technical Context
F# / new `FS.GG.Game.Core.Dice` module (`Dice.fs`/`.fsi`), added to the compile order after `Rng`.
Pure integer distribution math; `sample` threads the shipped `Rng`. No new module dependencies beyond `Rng`.

## Constitution Check
- III (public surface): declare the `Dice` surface in `.fsi` before `.fs`; add to both baselines.
- V (pure): all ops pure; `sample` threads `Rng` explicitly (no ambient RNG).
- VI (test evidence): the property suite fails before, passes after.

## Plan Scope
- Add `Dice.fs`/`.fsi`; register in `FS.GG.Game.Core.fsproj`. Requirement count: 6. Clarification
  decision count: 3. Checklist result count: 6.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: `type Distribution = private { Weights: Map<int,int> }` (all weights > 0, DEC-001). `constant v`, `uniform lo hi` (empty when hi<lo), `die sides = uniform 1 sides`. `outcomes`/`totalWeight` accessors.
- PD-002 [AC-002] [FR-002] complete: `convolve a b` folds over the outcome cross-product accumulating weight `wa*wb` at `va+vb` (int64 accumulation guarded); `repeat n d` folds `convolve` n times from `constant 0` (DEC — repeat 0 = constant 0).
- PD-003 [AC-003] [FR-003] complete: `advantage a b`/`disadvantage a b` fold over the cross-product accumulating weight at `max va vb`/`min va vb`.
- PD-004 [AC-004] [FR-004] complete: `mean = float(Sum v*w)/float(Sum w)`, `variance = float(Sum (v-mean)^2 * w)/float(Sum w)` using int64 numerator sums (DEC-002, reproducible float).
- PD-005 [AC-005] [FR-005] complete: `sample d rng` draws `k in [0, totalWeight)` via `Rng.nextInt 0 (totalWeight-1)`, walks outcomes in ascending key order accumulating weight until `k` falls in a bucket, returns `(outcome, rng')`; an empty distribution returns `(0, rng)` (DEC-003).
- PD-006 [AC-006] [FR-006] complete: purity/totality — integer weights, Map ascending-key iteration is deterministic, no ambient RNG; degenerate `uniform hi lo`, `repeat 0`, and empty `sample` are handled.

## Contract Impact
- PC-001 [PD-001] command report: Tier-1 public surface addition — the `Dice` module (`Distribution` type + ~11 functions). Both surface baselines gain the type/members with the `.fsi` and tests; the new file joins the compile order; the drift gate stays green.

## Verification Obligations
- VO-001 [PD-001] [PD-004] [PC-001] semanticTest: a property suite — constructor weight maps, convolution = sum distribution (mean adds; against a brute-force enumeration), advantage/disadvantage = max/min, exact moments (d6 3.5 and 35/12), and monotone means. Fails before, passes after; build clean under warnings-as-errors; both baselines regenerated.
- VO-002 [PD-005] [PD-006] semanticTest: sample determinism (same seed ⇒ same sequence), empirical-mean convergence over many samples, and totality (empty/degenerate) — a named `determinism` golden.

## Migration Posture
- PM-001 [PC-001] diagnoseOnly: Plan schemaVersion 1 accepted; a purely additive new module, so no consumer migration.

## Generated View Impact
- GV-001 [PD-001] workModel: `readiness/023-dice-distributions/work-model.json` refreshes or reports `staleGeneratedView`.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 023-dice-distributions`.
