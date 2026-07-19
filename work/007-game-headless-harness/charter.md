---
schemaVersion: 1
workId: 007-game-headless-harness
title: Headless gameplay test harness (FS.GG.Game.Harness)
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

# Headless gameplay test harness (FS.GG.Game.Harness) Charter

## Identity
A new `FS.GG.Game.Harness` package on top of `FS.GG.Game.Core`: a deterministic, headless harness
that drives a game's world through the **standard input frontier** — raw key → keymap → `Command` →
world step — folds fixed steps, and fingerprints/compares the resulting world trace as a value.
It provides the scripted driver, an in-process `Bot` policy interface, a multi-seed matrix runner
(the mini-tanks balance shape), and a **typed** synthetic-state escape hatch. This is WI-1 of the
headless-gameplay initiative and the capability the per-FR gameplay gate presumes.

## Principles
- **Determinism is the contract.** Reuse `Game.Core`'s seeded `Rng`, `FixedStep`, and `Loop`; a
  replay is a value comparison, not a tolerance check. No wall clock, no ambient RNG, no
  `Map`/`HashSet` iteration order escaping into a result.
- **Drive the real input route.** Gameplay is exercised through `Command` (a device-free intent),
  by default via the game's own keymap (raw key → keymap → `Command`), so the evidence a test
  produces is genuinely *non-synthetic*.
- **Synthetic is the labeled fallback.** The synthetic-state entry point is a distinct, typed
  surface so its cost is visible — under the SDD satisfaction rule and Governance's non-relaxable
  synthetic gate, `synthetic:true` can never satisfy an obligation.
- **`Game.Core` stays a pure vocabulary.** The harness depends on `Game.Core` and reaches up to
  nothing beyond the BCL; it does not live inside `Game.Core`.

## Scope Boundaries
- **In:** the `FS.GG.Game.Harness` package — `Playable` driver; `runScript` (raw-key→keymap→`Command`
  fold → trace); the `Bot` interface (`Observe`→`TeamView`, `Decide`→`Command`s); `runMatrix`
  multi-seed runner; the typed synthetic-state hatch; trace/fingerprint. Tests that dogfood a
  reference sim (Snake/Pong from the TestSpec corpus).
- **Out (other work items, sequenced on the Coordination board):** the SDD FR-classifier grammar +
  per-FR gate (FS-GG/.github ADR-0048); Governance profile-gate inheritance (ADR-0049); the
  `fs-gg-playtest` product skill (WI-2); wiring it into Rendering's `game` profile skill union
  (WI-6); the LLM/agent-playthrough tier (separate tooling); the reference-game proof run (WI-7);
  flipping the block-on-ship gate (WI-8).

## Policy Pointers
- Honors the SDD constitution: specify-before-implement, and test evidence mandatory.
- **Executes, does not decide:** the cross-repo boundaries are set in FS-GG/.github **ADR-0048**
  (per-FR non-synthetic evidence obligation) and **ADR-0049** (template-profile gate inheritance).
  This harness is the capability those gates presume; it changes no SDD or Governance surface itself.
- Governance files are compatibility pointers and are not evaluated by the lifecycle here.

## Lifecycle Notes
- **Tier 1:** introduces a new public package surface (`FS.GG.Game.Harness` with `.fsi` contracts),
  so signatures and tests land together.
- Depends on nothing already built (`Game.Core` exists); runs in **parallel** with the SDD →
  Governance critical path and feeds the WI-7 proof run.
- Next lifecycle action: `fsgg-sdd specify --work 007-game-headless-harness`.
