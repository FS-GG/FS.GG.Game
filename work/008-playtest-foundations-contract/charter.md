---
schemaVersion: 1
workId: 008-playtest-foundations-contract
title: Playtest tooling foundations contract note
stage: charter
changeTier: tier2
status: chartered
policyPointers:
  - .fsgg/sdd.yml
  - .fsgg/agents.yml
  - .fsgg/policy.yml
  - .fsgg/capabilities.yml
  - .fsgg/tooling.yml
---

# Playtest tooling foundations contract note Charter

## Identity
Phase 0 of the headless-playtest-authoring-machinery roadmap
(`docs/reports/2026-07-19-headless-playtest-authoring-machinery-design.md`, §6). A **read-only audit**
that pins the four `FS.GG.Game.Harness` primitives every downstream playtest tool (laws library,
witness-finder, property runner, manifest tooling) is about to build on, and records them as a
load-bearing contract note so a future change to `Playable`/`Trace` knows the tooling depends on them.
This work item authors a documentation note only; it adds no code and changes no public surface.

## Principles
- **Audit, do not build.** The deliverable is a note that *observes* already-shipped surface; it
  introduces no new module, type, or signature. Baselines stay byte-identical.
- **Pin what is load-bearing, and why.** Each pinned primitive names the exact surface relied upon and
  the downstream feature that would break if it changed, so the note is actionable, not decorative.
- **The `.fsi` files remain the authority.** The note is a pointer to relied-upon contract, not a
  second source of truth for it; where it quotes surface, the `.fsi` wins.

## Scope Boundaries
- **In:** a one-page contract note under `docs/reference/` pinning four primitives as relied-upon by
  the playtest tooling — (1) `Playable.Keymap` as the command alphabet, (2) the fingerprint
  `'world -> 'f` as the canonical visited-set key, (3) ambient-free determinism (no wall clock, no
  ambient RNG; enforced by `DependencyTests`), (4) FsCheck already referenced by the test project.
- **Out:** any change to `FS.GG.Game.Harness` surface or `Game.Core`; the laws library, witness-finder,
  property runner, bot combinators, and the `fsgg-playtest` CLI (Phases 1–6, separate work items).

## Policy Pointers
- Honors constitution I (specify-before-implement) and II (structured artifacts are the machine
  contract): the note records relied-upon contract so downstream phases specify against a fixed base.
- Tier 2 (internal/docs): needs spec + a presence/consistency check; public surface baselines unchanged.
- Governance files are optional compatibility pointers and are not evaluated by this command.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd specify --work 008-playtest-foundations-contract`.
