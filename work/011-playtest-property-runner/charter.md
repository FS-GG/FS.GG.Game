---
schemaVersion: 1
workId: 011-playtest-property-runner
title: Playtest FsCheck property runner
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

# Playtest FsCheck property runner Charter

## Identity
Phase 3 of the headless-playtest-authoring-machinery roadmap
(`docs/reports/2026-07-19-headless-playtest-authoring-machinery-design.md`, §4.2, §6). A new pure
**`Properties`** module on `FS.GG.Game.Harness` that generates random valid scripts over the
`Playable.Keymap` alphabet, drives each through the game, and asserts an author-supplied invariant
(`'world -> bool`) holds at **every step of every run** — turning WI-7's "bounds over one 16-step run"
into "bounds over thousands of runs". On a violation it shrinks to a minimal counter-script. Builds
directly on Phase 1's alphabet/laws.

## Principles
- **Pure package, FsCheck stays test-side.** FsCheck is a *test* dependency (the Phase 0 pin); a
  `Properties` module that referenced it would pull FsCheck into the package and break the
  leaf-dependency invariant (WI-007 FR-007, enforced by `DependencyTests`). So generation and
  shrinking are pure over `Game.Core`'s seeded `Rng`; FsCheck is used only in the test project to
  further stress the same invariants (the "pairing").
- **Every step, every run.** The invariant is checked at `Init` and after every fixed step of every
  generated run — not just the final frame.
- **Deterministic and reproducible.** Same seed → same generated runs → same result, because `Rng` is
  a pure value; a counterexample is replayable.
- **Shrink to a minimal counter-script.** A failing run is reduced to the shortest script that still
  violates the invariant, so the author debugs the essence, not the noise.

## Scope Boundaries
- **In:** a `Properties` module (`Properties.fsi`/`Properties.fs`) with a config, a `PropertyResult`
  (`Held`/`Falsified`), a `Counterexample`, seeded generation over the keymap alphabet, per-step
  invariant checking, and a deterministic shrink; `PongSim` dogfood (invariants over ≥1000 runs; a
  broken invariant shrunk to a minimal counter-script; an FsCheck-driven pairing test); an updated
  surface baseline.
- **Out (later phases):** bot combinators (Phase 4), the `fsgg-playtest` CLI (Phases 5–6); any change
  to `Game.Core` or existing harness behavior; putting FsCheck into the pure package.

## Policy Pointers
- Honors constitution I (specify-before-implement), III (public surface + baseline with tests), V
  (pure transitions; the seeded `Rng` is the only randomness), VI (test evidence — dogfooded on
  `PongSim`).
- **Tier 1:** new public package surface (`Properties` module + result/config types).

## Lifecycle Notes
- Depends on Phase 1 (the alphabet/laws) and Phase 0 (FsCheck availability, ambient-free determinism).
- Next lifecycle action: `fsgg-sdd specify --work 011-playtest-property-runner`.
