---
schemaVersion: 1
workId: 012-playtest-bot-combinators
title: Playtest Bot Combinators
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/012-playtest-bot-combinators/spec.md
sourceClarifications: work/012-playtest-bot-combinators/clarifications.md
sourceChecklist: work/012-playtest-bot-combinators/checklist.md
publicOrToolFacingImpact: true
---

# Playtest Bot Combinators Plan

Prose status: planned

## Source Snapshot
- spec: work/012-playtest-bot-combinators/spec.md sha256:bfd60383eea7da614cd6b1beb80386a770181f9f1fc20f960807e582db291483 schemaVersion:1
- clarifications: work/012-playtest-bot-combinators/clarifications.md sha256:314a779aebb2063eda2dbf26abf743479ff5e3accbd46af4caea08906ba5994f schemaVersion:1
- checklist: work/012-playtest-bot-combinators/checklist.md sha256:18f5d5c33fbc0c95c91fe5e7f7ad48ba69acf4756811e0ed47a4291a07632910 schemaVersion:1

## Plan Scope
- Add a new pure `Bots` module (`Bots.fsi`/`Bots.fs`) to `FS.GG.Game.Harness`, compiled after
  `Playable` (which defines `Bot<'view>`). Add `PongSim` + toy-game dogfood proofs and regenerate the
  surface baseline.
- Requirement count: 8. Clarification decision count: 2. Checklist result count: 8.

## Technical Context
F# 10 / .NET 10. Each combinator constructs a `Bot<'view> = { Decide: 'view -> Rng -> struct (Command
list * Rng) }`. Four are pure; `scripted` closes over a mutable playback index (DEC-001). Pure over
Core + BCL — no I/O, no wall clock — so `DependencyTests` (WI-007 FR-007) stays green (FR-008).

## Design
### `Bots` module
- `sitter : Bot<'view>` — `Decide = fun _ rng -> struct ([], rng)` (FR-001).
- `scripted : Command list list -> Bot<'view>` — closes over a `ref` index; each `Decide` returns the
  frame at the current index (or `[]` past the end) and advances the index, leaving `rng` untouched.
  Documented single-use (DEC-001, FR-002).
- `random : (Rng -> struct (Command list * Rng)) -> Bot<'view>` — `Decide = fun _ rng -> draw rng`
  (FR-003).
- `chase : (('view -> int) * ('view -> int)) -> Command -> Command -> Bot<'view>` — reads `target` and
  `self` axes; `up` when `target < self`, `down` when `target > self`, `[]` on tie; never draws
  (DEC-002, FR-004).
- `greedyToward : ('view -> Command -> float) -> Command list -> Bot<'view>` — `[]` when `moves` is
  empty; else `[ moves |> List.maxBy (score view) ]` (first on tie — `List.maxBy` keeps the earliest
  maximum) (FR-005).
- `on : ('outer -> 'inner) -> Bot<'inner> -> Bot<'outer>` — `Decide = fun outer rng -> bot.Decide
  (project outer) rng`, so the wrapped policy reads only the projection (FR-006).

### Dogfood (FR-007, FR-008)
- `PongSim`: `Bots.sitter` replaces `sitterBot` in a two-seat matrix and every match outcome is
  identical; `Bots.chase ((fst,snd)) MoveNorth MoveSouth` reproduces `chaseBot`'s directional
  behaviour on non-tie views; `greedyToward`/`random`/`on` are unit-proven. A second toy game (a
  1-D `Playable`) reuses `Bots.chase`/`Bots.sitter` with zero bespoke bot code. `DependencyTests`
  re-run green; the surface baseline is regenerated additively.

## Constitution Check
- **III Public Surface:** `Bots.fsi` before `Bots.fs`; baseline regenerated with tests.
- **IV Idiomatic simplicity:** each combinator is one small `Decide`.
- **V Model-Update-Effect:** four combinators pure; `scripted`'s mutation is a documented, contained
  playback index at the test edge of use.
- **VI Test evidence:** every FR covered on `PongSim` and a second toy game.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: `Bots.sitter` returns `struct ([], rng)` on every decision — no command, generator unchanged.
- PD-002 [AC-002] [FR-002] [DEC-001] complete: `Bots.scripted` closes over a mutable index, emitting frames in order then idling; documented single-use.
- PD-003 [AC-003] [FR-003] complete: `Bots.random draw` returns `draw rng`, threading the generator; deterministic in the seed.
- PD-004 [AC-004] [FR-004] [DEC-002] complete: `Bots.chase` issues up/down/idle from the two view axes without drawing; deterministic in `(view, seed)`; reproduces `chaseBot`'s non-tie behaviour.
- PD-005 [AC-005] [FR-005] complete: `Bots.greedyToward score moves` issues the earliest max-scoring move (`List.maxBy`), or `[]` when `moves` is empty.
- PD-006 [AC-006] [FR-006] complete: `Bots.on project bot` decides as `bot` on `project outer`, so the policy reads only the projection (the fog boundary).
- PD-007 [AC-007] [FR-007] complete: `Bots.sitter` reproduces `sitterBot` with identical match outcomes; a second toy game reuses `Bots.chase`/`Bots.sitter` with zero bespoke bot code.
- PD-008 [AC-008] [FR-008] complete: the module is pure Core+BCL; `DependencyTests` stays green and the surface baseline is regenerated.

## Contract Impact
- PC-001 [PD-001] [PD-002] [PD-003] [PD-004] [PD-005] [PD-006] command report: Tier-1 additive surface — a new `Bots` module. `.fsi`, baseline, tests, and docs land together; existing surface byte-unchanged.
- PC-002 [PD-008] command report: the harness's referenced-assembly set stays `{FSharp.Core, FS.GG.Game.Core, System.*}`; `DependencyTests` continues to enforce the leaf-dependency + no-I/O invariant.

## Verification Obligations
- VO-001 [PD-001] [PD-002] [PD-003] [PD-004] [PD-005] [PD-006] [PC-001] semanticTest: unit tests for each combinator — `sitter` idles, `scripted` plays then idles, `random` threads the draw, `chase` up/down/idle and `(view, seed)` determinism, `greedyToward` earliest-max/empty, `on` projects the view.
- VO-002 [PD-007] semanticTest: `Bots.sitter` yields match outcomes identical to `sitterBot` on a `PongSim` matrix; `Bots.chase` reproduces `chaseBot`'s non-tie directional decisions; a second toy game reuses `Bots.chase`/`Bots.sitter`.
- VO-003 [PD-008] [PC-002] semanticTest: `DependencyTests` re-run green; `dotnet build` clean under warnings-as-errors; the surface baseline regenerated and committed; full harness suite green.

## Migration Posture
- PM-001 [PC-001] diagnoseOnly: Plan schemaVersion 1 accepted. Purely additive; nothing removed or renamed; no consumer migration. The surface baseline is extended, not migrated.

## Generated View Impact
- GV-001 [PD-001] [PD-007] workModel: the work model (readiness/012-.../work-model.json) must reflect the eight FR→PD→VO chains once verify/ship build it; a plan edit that adds or drops a PD/VO restales it until `fsgg-sdd refresh` re-runs.
- GV-002 [PC-001] surfaceBaseline: `readiness/surface-baselines/FS.GG.Game.Harness.txt` is regenerated from the extended `.fsi` set; the surface-baseline-drift gate must stay green.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.
- This is Phase 4 of `docs/reports/2026-07-19-headless-playtest-authoring-machinery-design.md`;
  independent of Phases 2–3, it shrinks every future matrix/balance proof.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 012-playtest-bot-combinators`.
