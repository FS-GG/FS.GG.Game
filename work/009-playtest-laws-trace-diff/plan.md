---
schemaVersion: 1
workId: 009-playtest-laws-trace-diff
title: Playtest Laws And Trace Diff
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/009-playtest-laws-trace-diff/spec.md
sourceClarifications: work/009-playtest-laws-trace-diff/clarifications.md
sourceChecklist: work/009-playtest-laws-trace-diff/checklist.md
publicOrToolFacingImpact: true
---

# Playtest Laws And Trace Diff Plan

Prose status: planned

## Source Snapshot
- spec: work/009-playtest-laws-trace-diff/spec.md sha256:707727aedf61415f7d42f20fab5e42218c30f893772efb76afb4bf2cc213b6c1 schemaVersion:1
- clarifications: work/009-playtest-laws-trace-diff/clarifications.md sha256:3b386ba0838e1782aa0b4b2680e518e8476363e12ace029c80bfe7695b130e6e schemaVersion:1
- checklist: work/009-playtest-laws-trace-diff/checklist.md sha256:450254ddce184d7e01356f887c1c05d5e10a409e76689ed369ec77a16766fd90 schemaVersion:1

## Plan Scope
- Add two `Trace` functions (`firstDivergence`, `render`) to the existing `Trace.fsi`/`Trace.fs`, and a
  new pure `Laws` module (`Laws.fsi`/`Laws.fs`) to `FS.GG.Game.Harness`, compiled after `Matrix`.
- Add a golden record/update helper in the `Game.Harness.Tests` project (I/O confined there), and
  `PongSim` dogfood proofs. Regenerate `readiness/surface-baselines/FS.GG.Game.Harness.txt`.
- Requirement count: 10. Clarification decision count: 2. Checklist result count: 10.

## Technical Context
F# 10 / .NET 10, MVU discipline. Every new package function is pure and reaches only `Game.Core` +
BCL, so `DependencyTests` (WI-007 FR-007) keeps passing (FR-010). The laws are thin metamorphic
wrappers over the shipped `Driver`/`Matrix`/`Trace`. `Laws.check` builds its traces with
`Driver.identityFingerprint` (so frames are the whole world, requiring `'world: equality`) — the
default full-world comparison surface DEC-001 of WI-007 established.

## Design
### Trace additions (FR-006, FR-007; DEC-002)
- `Trace.firstDivergence (a: Trace<'f>) (b: Trace<'f>) : (int * 'f * 'f) option when 'f: equality` —
  walks the two frame lists in step order and returns `Some (i, a_i, b_i)` at the first index where
  both have a frame and they differ; `None` when neither trace diverges within its common prefix
  (equal traces, or one a strict prefix of the other). Determinism/replay always compare equal-length
  traces (same script), so content divergence is the reported case; the length dimension is owned by
  the fixed-step law (FR-003). Documented as such in the `.fsi`.
- `Trace.render (show: 'f -> string) (trace: Trace<'f>) : string` — one line per frame, `"<i>: " +
  show frame`, step order, joined by `\n`; empty trace → `""` (DEC-002). Pure; no I/O.

### Laws module (FR-001..FR-005, FR-008; DEC-001)
- `type LawResult = { Law: string; Passed: bool; DivergenceStep: int option; Detail: string }`.
- `type LawReport = { Results: LawResult list }`, with `LawReport.allPassed` and `LawReport.failures`.
- `Laws.check (playable: Playable<'world,'key>) (sampleScripts: 'key list list list) : LawReport when
  'world: equality` runs, per sample script, four laws:
  - **determinism** (FR-001): `Driver.runScript` twice; compare via `Trace.firstDivergence`. A failing
    entry carries the divergence step (FR-008).
  - **replay** (FR-002): `Driver.runScript playable id script` vs `Driver.runCommands playable id
    (resolved commands)` — resolve-then-run equals run-script; divergence step on failure.
  - **fixed-step** (FR-003): recorded frame count equals script length (one whole step per frame).
  - **provenance** (FR-004): `not (Trace.isSynthetic trace)` for every built trace.
- `Laws.matrixOrderIndependent (setup) (outcome) (matches) : LawResult when 'o: equality` (FR-005):
  runs `Matrix.runMatrix` on `matches` and on a permutation, and asserts the permuted outcome list is
  the same permutation of results (no match's outcome changed). Separate entry point because it needs
  match inputs, not scripts (DEC-001).

### Golden helper (FR-009) — test project only
- `Game.Harness.Tests.GoldenFile.check (path) (rendered)`: reads the golden file and compares to the
  rendered trace; on drift returns the first differing line index (via a line-level `firstDivergence`
  analogue). When `PLAYTEST_UPDATE_GOLDENS` is set, it (re)writes the golden from the current run.
  Uses `System.IO`/`Environment` — legal in the test project, illegal in the package (FR-010).

### Dogfood + baseline (FR-010)
- `PongSim` proves `Laws.check` passes with ≤5 lines; a mutable-counter-poisoned `Step` is caught by
  the determinism law with a `DivergenceStep`. `matrixOrderIndependent` proven on a Pong two-seat
  setup. Regenerate the surface baseline to include the new surface; `DependencyTests` re-run green.

## Constitution Check
- **III Public Surface:** `Laws.fsi` declared before `Laws.fs`; the two `Trace` functions added to
  `Trace.fsi` first; surface baseline regenerated and committed with tests.
- **V Model-Update-Effect:** all package additions pure; the only I/O (golden files) lives at the test
  edge.
- **VI Test evidence:** every FR covered by a real `PongSim` proof; the determinism law's catch is
  proven by a deliberately non-deterministic `Step` fixture.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: `Laws.check` runs `Driver.runScript` twice per script and compares with `Trace.firstDivergence`; a non-deterministic `Step` yields a failing determinism `LawResult` carrying the divergence step.
- PD-002 [AC-002] [FR-002] complete: the replay law compares `runScript` against `runCommands` over the resolved commands; a `runBot` capture replayed through `runCommands` is proven identical in a dedicated `PongSim` test.
- PD-003 [AC-003] [FR-003] complete: the fixed-step law asserts `Trace.frames` length equals script length — one whole `Step world Dt` per input frame.
- PD-004 [AC-004] [FR-004] complete: the provenance law asserts `not (Trace.isSynthetic t)` for every trace the runner builds (all `Origin.InputDriven`).
- PD-005 [AC-005] [FR-005] [DEC-001] complete: `Laws.matrixOrderIndependent` is a separate runner over `MatchSetup`/`outcome`/`matches`; it runs `runMatrix` on `matches` and a permutation and asserts identical outcomes under the permutation.
- PD-006 [AC-006] [FR-006] complete: `Trace.firstDivergence` returns the first content-divergence `(i, a_i, b_i)` in the common prefix or `None`; the fixed-step law owns length, and the `.fsi` documents the prefix semantics.
- PD-007 [AC-007] [FR-007] [DEC-002] complete: `Trace.render` emits `"<i>: " + show frame` per line in step order joined by `\n`, empty trace `""` — stable and diff-friendly.
- PD-008 [AC-008] [FR-008] complete: `LawResult` names its law and pass/fail, and a failing determinism/replay result carries `DivergenceStep` from `Trace.firstDivergence`.
- PD-009 [AC-009] [FR-009] complete: `GoldenFile.check` in the test project compares a rendered trace to a golden and fails on drift with a divergence line, and rewrites on `PLAYTEST_UPDATE_GOLDENS`; all file I/O is in the test project.
- PD-010 [AC-010] [FR-010] complete: additions are pure Core+BCL; `DependencyTests` stays green and the `FS.GG.Game.Harness` surface baseline is regenerated to include the new surface.

## Contract Impact
- PC-001 [PD-001] [PD-005] [PD-006] [PD-007] [PD-008] command report: Tier-1 additive surface — a new `Laws` module (`LawResult`, `LawReport`, `check`, `matrixOrderIndependent`) and two new `Trace` functions. `.fsi`, baseline, tests, and docs land together; existing surface is byte-unchanged.
- PC-002 [PD-010] command report: the harness's referenced-assembly set stays `{FSharp.Core, FS.GG.Game.Core, System.*}`; the AC-010/`DependencyTests` reflection test continues to enforce the leaf-dependency + no-I/O invariant after the additions.

## Verification Obligations
- VO-001 [PD-001] [PD-002] [PD-003] [PD-004] [PD-008] [PC-001] semanticTest: `PongSim` proves `Laws.check` reports all four script-laws pass in ≤5 lines; a non-deterministic `Step` fixture makes the determinism law fail with a `DivergenceStep`; a `runBot` capture replays identically.
- VO-002 [PD-005] [PD-006] [PD-007] [PD-009] semanticTest: tests for `matrixOrderIndependent` (permutation-invariant), `Trace.firstDivergence` (equal→None, content-diff→Some index), `Trace.render` (stable step-ordered string, empty→""), and `GoldenFile.check` (drift fails, update rewrites).
- VO-003 [PD-010] [PC-002] semanticTest: `DependencyTests` re-run green (no I/O/clock type in the harness); `dotnet build` clean under warnings-as-errors; the `FS.GG.Game.Harness` surface baseline regenerated and committed; full harness suite green.

## Migration Posture
- PM-001 [PC-001] diagnoseOnly: Plan schemaVersion 1 accepted. Purely additive surface; nothing is removed or renamed, so no consumer migration is required. The surface baseline is extended, not migrated.

## Generated View Impact
- GV-001 [PD-001] [PD-008] workModel: the work model (readiness/009-.../work-model.json) must reflect the ten FR→PD→VO chains once verify/ship build it; a plan edit that adds or drops a PD/VO restales it until `fsgg-sdd refresh` re-runs.
- GV-002 [PC-001] surfaceBaseline: `readiness/surface-baselines/FS.GG.Game.Harness.txt` is regenerated from the extended `.fsi` set; the surface-baseline-drift gate must stay green.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.
- This is Phase 1 of `docs/reports/2026-07-19-headless-playtest-authoring-machinery-design.md`; the
  FsCheck property runner (Phase 3) will build on this alphabet/laws.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 009-playtest-laws-trace-diff`.
