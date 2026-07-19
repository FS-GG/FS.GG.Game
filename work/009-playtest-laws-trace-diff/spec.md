---
schemaVersion: 1
workId: 009-playtest-laws-trace-diff
title: Playtest Laws And Trace Diff
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Playtest Laws And Trace Diff Specification

Prose status: specified

## User Value
A game author proves the metamorphic laws every deterministic `Playable` must satisfy — determinism, replay, fixed-step discipline, provenance (non-synthetic), and matrix order-independence — in a few lines via one `Laws` runner, instead of re-deriving them per game (as `DriverTests`/`MatrixTests`/`BotTests` each do today). When a law fails, the author reads it as "diverged at step N" via `Trace.firstDivergence`, and can look at a trace directly via `Trace.render`, with no throwaway `fsi` probe.

## Scope
- SB-001: A new pure `Laws` module in `FS.GG.Game.Harness` exposing a `LawReport` type and a runner over a `Playable` and sample scripts.
- SB-002: Two new pure functions on the existing `Trace` module — `firstDivergence` and `render`.
- SB-003: A golden record/update helper in the `Game.Harness.Tests` project (not the package), built on `Trace.render`, so a golden is captured from a green run rather than hand-transcribed.
- SB-004: `PongSim` dogfood proofs and an updated `FS.GG.Game.Harness` surface baseline.

## Non-Goals
- SB-010: The witness-finder (Phase 2), FsCheck property runner (Phase 3), bot combinators (Phase 4), and the `fsgg-playtest` CLI (Phases 5–6).
- SB-011: Any change to `FS.GG.Game.Core`, or to the existing behavior of `Trace`/`Driver`/`Matrix` — this only *adds* surface.
- SB-012: Golden *file* read/write inside the pure package — the harness performs no I/O (WI-007 FR-007), so file capture lives only in the test project.

## User Stories
- US-001 (P1): As a game author, I run one `Laws` runner over my `Playable` and a few sample scripts and get a per-law pass/fail report, instead of hand-writing determinism/replay/fixed-step/provenance tests.
- US-002 (P1): As a game author, when a determinism or replay law fails, I see the first step at which the traces diverge and the two differing frames, not just "not equal".
- US-003 (P2): As a game author, I check that permuting a matrix's matches permutes the outcomes identically, in one law call.
- US-004 (P2): As a game author, I dump a trace to a stable, human-readable, step-ordered string to inspect its dynamics without a throwaway probe.
- US-005 (P2): As a game author, my trace golden is captured from a green run and rewritten on an intended change via a test-project helper, never hand-transcribed.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given a `Playable` and sample key scripts, when the determinism law runs, then two runs of each script produce equal frames and the law passes; and given a `Step` poisoned to depend on wall-clock/ambient state, when it runs, then the determinism law fails.
- AC-002 [US-001] [FR-002]: Given sample key scripts, when the replay law runs, then driving the resolved commands back through `Driver.runCommands` reproduces byte-identical frames to `Driver.runScript`, and a captured `Run.Captured` from `runBot` replays identically.
- AC-003 [US-001] [FR-003]: Given a `Playable` and a script of length n, when the fixed-step law runs, then the recorded trace has exactly n frames (one whole step per input frame at the constant `Dt`).
- AC-004 [US-001] [FR-004]: Given any trace the laws build, when the provenance law runs, then `Trace.isSynthetic` is false for every driven trace.
- AC-005 [US-003] [FR-005]: Given a `MatchSetup`, an outcome projection, and a set of matches, when the matrix order-independence law runs, then permuting the matches permutes the outcomes identically and changes no match's outcome.
- AC-006 [US-002] [FR-006]: Given two traces, when `Trace.firstDivergence a b` is called, then it returns `Some (i, fa, fb)` for the first index i at which the frame lists differ (including a length mismatch) and `None` when the frame lists are equal.
- AC-007 [US-004] [FR-007]: Given a trace and a `show` projection, when `Trace.render show trace` is called, then it returns a stable, step-ordered, deterministic string of the frames, identical across runs.
- AC-008 [US-002] [FR-008]: Given a failing determinism or replay law, when the `LawReport` is produced, then the failing entry names the law and carries the `Trace.firstDivergence` step index, so the failure is actionable rather than "two lists were not equal".
- AC-009 [US-005] [FR-009]: Given a golden trace file and a green run, when the golden helper runs without update mode, then it compares the rendered trace to the golden and fails on drift with a divergence step; and when run in update mode, then it rewrites the golden from the current run — all inside the test project, with the package performing no file I/O.
- AC-010 [US-001] [FR-010]: Given the harness assembly after this change, when its dependencies are inspected, then it still references only `FS.GG.Game.Core` and the BCL and performs no I/O and no wall-clock read (WI-007 FR-007 preserved), and the surface baseline is regenerated to include the new `Laws` and `Trace` surface.

## Functional Requirements
- FR-001: A determinism law MUST assert that two runs of the same `Playable` over the same script produce equal frames, and MUST fail when the `Step` is non-deterministic (clock/ambient-poisoned). (covers AC-001)
- FR-002: A replay law MUST assert that the resolved commands of a script replayed through `Driver.runCommands` reproduce byte-identical frames to `Driver.runScript`, and that a `Run.Captured` from `runBot` replays identically. (covers AC-002)
- FR-003: A fixed-step-discipline law MUST assert that a script of length n yields exactly n recorded frames — one whole fixed step per input frame at the constant `Dt`. (covers AC-003)
- FR-004: A provenance law MUST assert that every trace the laws build is `Origin.InputDriven` (`Trace.isSynthetic` is false). (covers AC-004)
- FR-005: A matrix order-independence law MUST assert that permuting a set of matches permutes the outcomes identically and changes no match's outcome. (covers AC-005)
- FR-006: `Trace.firstDivergence` MUST return the first index and differing frames at which two traces' frame lists diverge (including a length mismatch), or `None` when they are equal. (covers AC-006)
- FR-007: `Trace.render` MUST produce a stable, step-ordered, deterministic string of a trace's frames under a caller `show` projection. (covers AC-007)
- FR-008: The `LawReport` MUST be actionable: each result names its law and pass/fail, and a failing determinism/replay result MUST carry the `Trace.firstDivergence` step index. (covers AC-008)
- FR-009: A golden record/update helper in the test project MUST compare a rendered trace to a golden file and fail on drift, and MUST rewrite the golden from the current run in an explicit update mode — with all file I/O confined to the test project. (covers AC-009)
- FR-010: The change MUST remain additive and pure: the harness assembly MUST still reference only `FS.GG.Game.Core` and the BCL with no I/O or wall-clock read (WI-007 FR-007), and the `FS.GG.Game.Harness` surface baseline MUST be regenerated to include the new surface. (covers AC-010)

## Ambiguities
- AMB-001: Whether the single `laws` runner also checks matrix order-independence (which needs match inputs, not scripts) or whether that law is a separate entry point taking a `MatchSetup` and matches.
- AMB-002: What `Trace.render` emits for the empty trace and how it delimits frames (one frame per line vs a single joined string) so goldens are stable and diff-friendly.

## Public Or Tool-Facing Impact
- Adds public package surface: a new `Laws` module (`LawReport` + runners) and two new `Trace` functions. Tier-1 change — `.fsi`, surface baseline, tests, and docs land together; existing surface is unchanged (purely additive).

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 009-playtest-laws-trace-diff`.
