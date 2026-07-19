---
schemaVersion: 1
workId: 010-playtest-witness-finder
title: Playtest witness-finder and reachability explorer
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

# Playtest witness-finder and reachability explorer Charter

## Identity
Phase 2 (the keystone) of the headless-playtest-authoring-machinery roadmap
(`docs/reports/2026-07-19-headless-playtest-authoring-machinery-design.md`, §4.1, §6). A new pure
**`Explore`** module on `FS.GG.Game.Harness` that does a bounded breadth-first search over a `Playable`
and returns the **shortest command script from `Init`** reaching a caller's goal predicate
(`findScript`), plus the reachable-fingerprint frontier within a bound (`reachable`). Because a witness
is a real path from `Init` driven through the game's own step, the evidence it produces is
`InputDriven` **by construction** — dissolving the reachable-vs-synthetic tension that made the WI-7
proof hard.

## Principles
- **Additive and pure.** A thin BFS over already-shipped surface (`Playable`, `Driver`, the
  fingerprint). Core + BCL only; `DependencyTests` stays green.
- **A witness finder, not a prover.** A negative result within a bound is "not found within N", never
  "impossible". The API name and docs must say so.
- **Fail closed (#266).** State explosion is real. When the search hits its depth bound or visited-set
  cap before exhausting the frontier, it returns a distinct **`Truncated`** signal reporting the
  frontier size — never a silent `NotFound`/empty that reads as a confident "impossible".
- **The fingerprint is the visited key** (the Phase 0 contract): the search dedups by
  `fingerprint world`, and a caller may search over a projected fingerprint to control granularity, or
  restrict the move alphabet to the keys that matter.

## Scope Boundaries
- **In:** an `Explore` module (`Explore.fsi`/`Explore.fs`) with `findScript`/`reachable`, a fail-closed
  result type, a config for restricted move alphabet + visited cap; `PongSim` dogfood proofs (a
  non-synthetic left-paddle-deflection witness; a bounded reachable frontier; a fail-closed truncation
  on an out-of-reach goal); an updated `FS.GG.Game.Harness` surface baseline.
- **Out (later phases):** the FsCheck property runner (Phase 3), bot combinators (Phase 4), the
  `fsgg-playtest` CLI (Phases 5–6); any change to `Game.Core` or existing harness behavior; a general
  game-playing AI (the witness finder produces *test inputs*, not a competitive agent).

## Policy Pointers
- Honors constitution I (specify-before-implement), III (public surface + baseline with tests), IV
  (idiomatic simplicity — a small BFS), V (pure transitions), VIII (safe failure — the fail-closed
  truncation signal).
- **Tier 1:** new public package surface (`Explore` module + result types), so signatures, baseline,
  tests, and docs land together.

## Lifecycle Notes
- Depends on Phase 0 foundations (fingerprint-as-visited-key) and the shipped `Driver`.
- Next lifecycle action: `fsgg-sdd specify --work 010-playtest-witness-finder`.
