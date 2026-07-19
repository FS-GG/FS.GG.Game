---
schemaVersion: 1
workId: 009-playtest-laws-trace-diff
title: Playtest laws library and trace diff
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

# Playtest laws library and trace diff Charter

## Identity
Phase 1 of the headless-playtest-authoring-machinery roadmap
(`docs/reports/2026-07-19-headless-playtest-authoring-machinery-design.md`, §4.2, §4.3, §6). Two
additive, pure surfaces on `FS.GG.Game.Harness`: (1) a **`Laws`** module of the metamorphic laws every
deterministic `Playable` must satisfy — determinism, replay, fixed-step discipline, provenance
(non-synthetic), and matrix order-independence — returning a per-law `LawReport`; and (2)
**`Trace.firstDivergence`** and **`Trace.render`**, which turn a failing law from "two lists were not
equal" into "diverged at step 7" and give a probe-free look at a trace. It is the cheapest, highest
immediate-value win in the roadmap and depends only on the Phase 0 foundations.

## Principles
- **Additive and pure.** Everything here is a thin layer over already-shipped harness surface
  (`Driver`, `Matrix`, `Trace`). The package stays `FS.GG.Game.Core` + BCL only — no I/O, no wall
  clock — so `DependencyTests` (WI-007 FR-007) keeps passing unchanged.
- **The laws *check* determinism, they do not assume it.** A clock- or RNG-poisoned `Step` must be
  *caught* by the determinism law with an actionable divergence step index — that is the whole point.
- **Provenance is a law, not an assumption.** Every trace the laws build is `Origin.InputDriven`, so
  `not (Trace.isSynthetic t)` is something the report asserts.
- **Golden *file* I/O stays out of the pure package.** `Trace.render` (pure string) lives in the
  package; the golden record/update helper that reads and writes golden files lives in the **test
  project**, because the harness may perform no I/O (WI-007 FR-007).

## Scope Boundaries
- **In:** a `Laws` module (`Laws.fsi`/`Laws.fs`) with a `LawReport` type and law runners; two new
  `Trace` functions (`firstDivergence`, `render`); a golden record/update test helper in
  `Game.Harness.Tests`; `PongSim` dogfood proofs; an updated `FS.GG.Game.Harness` surface baseline.
- **Out (later phases):** the witness-finder (Phase 2); the FsCheck property runner (Phase 3, though
  it builds on this alphabet/laws); bot combinators (Phase 4); the `fsgg-playtest` CLI (Phases 5–6);
  any change to `Game.Core` or to the existing `Trace`/`Driver`/`Matrix` behavior.

## Policy Pointers
- Honors constitution I (specify-before-implement), III (public surface declared — `.fsi` + baseline
  land with tests), V (Model-Update-Effect — pure transitions, I/O at the test edge only), and VI
  (test evidence mandatory — dogfooded on `PongSim`).
- **Tier 1:** adds public package surface (`Laws` module + two `Trace` functions), so signatures,
  surface baseline, tests, and docs land together.

## Lifecycle Notes
- Depends on Phase 0 (`008-playtest-foundations-contract`) foundations being pinned.
- Next lifecycle action: `fsgg-sdd specify --work 009-playtest-laws-trace-diff`.
