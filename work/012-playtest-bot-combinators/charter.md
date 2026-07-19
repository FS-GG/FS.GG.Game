---
schemaVersion: 1
workId: 012-playtest-bot-combinators
title: Playtest bot combinators
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

# Playtest bot combinators Charter

## Identity
Phase 4 of the headless-playtest-authoring-machinery roadmap
(`docs/reports/2026-07-19-headless-playtest-authoring-machinery-design.md`, §4.6, §6). A small pure
**`Bots`** combinator library over the existing `Bot<'view>`, so a policy is a one-liner instead of a
re-invented `chaseBot`/`sitterBot` per game: `sitter`, `scripted`, `random`, `chase`, `greedyToward`,
plus a view-projection helper that keeps a policy honestly blind (the `Ai.TeamView` fog boundary).
Low risk, independent of Phases 2–3, and it shrinks every future matrix/balance proof.

## Principles
- **Pure combinators over `Bot<'view>`.** Each is a thin wrapper; the package stays Core + BCL, so
  `DependencyTests` (WI-007 FR-007) keeps passing.
- **Determinism in (view, seed) is preserved** for the stateless policies (`sitter`, `random`,
  `chase`, `greedyToward`) — the `Bot<'view>` contract (WI-007 FR-004).
- **`scripted` is the one stateful playback bot.** A stateless `Decide` cannot track a script position,
  so `scripted` carries a captured playback index; it is documented as single-use (a fresh one per
  run) and is the deliberate exception to (view, seed) determinism.
- **Honest blindness.** The view-projection helper adapts a bot to a wider view by projecting to the
  narrower one it reads, so a policy cannot consult anything outside its projection.

## Scope Boundaries
- **In:** a `Bots` module (`Bots.fsi`/`Bots.fs`) with `sitter`/`scripted`/`random`/`chase`/
  `greedyToward` and a view-projection helper; `PongSim` dogfood (re-express `sitterBot` with identical
  outcomes and `chaseBot`'s chase behaviour) plus a second toy game reusing them with zero bespoke bot
  code; an updated surface baseline.
- **Out (later phases):** the `fsgg-playtest` CLI (Phases 5–6); any change to `Game.Core` or existing
  harness behaviour; a competitive game-playing AI (these are test-input policies).

## Policy Pointers
- Honors constitution I (specify-before-implement), III (public surface + baseline with tests), IV
  (idiomatic simplicity), V (pure transitions), VI (test evidence — dogfooded on `PongSim`).
- **Tier 1:** new public package surface (`Bots` module).

## Lifecycle Notes
- Independent of Phases 2–3; depends only on the shipped `Bot`/`Matrix` surface.
- Next lifecycle action: `fsgg-sdd specify --work 012-playtest-bot-combinators`.
