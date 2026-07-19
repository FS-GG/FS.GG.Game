---
schemaVersion: 1
workId: 011-playtest-property-runner
title: Playtest Property Runner
stage: clarify
changeTier: tier1
status: clarified
sourceSpec: work/011-playtest-property-runner/spec.md
publicOrToolFacingImpact: true
---

# Playtest Property Runner Clarifications

## Source Specification
- work/011-playtest-property-runner/spec.md

## Clarification Questions
- CQ-001 [AMB:AMB-001] blocking: Are generation and shrinking pure (over `Game.Core`'s `Rng`, in the package), or FsCheck-based?
- CQ-002 [AMB:AMB-002] blocking: Does "every step" include the `Init` world, and how does the counterexample index the violating step?

## Answers
- CQ-001 → Pure, over `Game.Core`'s seeded `Rng`, inside the package. FsCheck is a *test* dependency (the Phase 0 pin), and a `Properties` module that referenced it would pull FsCheck into the harness assembly and break the leaf-dependency invariant that `DependencyTests` enforces (WI-007 FR-007). So `Properties.check` generates scripts with `Rng.nextInt` over the keymap alphabet and shrinks deterministically; FsCheck remains test-only, used in `Game.Harness.Tests` to further stress the same invariants (the design's "pairing"). This keeps generation reproducible-by-seed for free.
- CQ-002 → Yes, `Init` is checked (before any input), then the invariant is checked after every fixed step. The counterexample's `Step` indexes the post-step frame at which the invariant first fails (0-based); a violation at `Init` itself is reported as `Step = -1` with an empty script, since no input is needed to reach it.

## Decisions
- **DEC-001** [CQ-001] [AMB:AMB-001] [FR-005] [FR-007]: `Properties` generation and shrinking are pure over `Game.Core.Rng` and live in the package; the harness references no FsCheck. FsCheck stays a test-only dependency exercising the same invariants. Determinism-by-seed follows from `Rng` being a pure value.
- **DEC-002** [CQ-002] [AMB:AMB-002] [FR-002] [FR-003]: The invariant is checked at `Init` and after every fixed step. `Counterexample.Step` is the 0-based post-step frame index of the first violation; a violation at `Init` is `Step = -1` with an empty `Script`.

## Accepted Deferrals
- None.

## Remaining Ambiguity
- None. AMB-001 and AMB-002 are resolved by DEC-001 and DEC-002 above.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 011-playtest-property-runner`.
