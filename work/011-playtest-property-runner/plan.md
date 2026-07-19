---
schemaVersion: 1
workId: 011-playtest-property-runner
title: Playtest Property Runner
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/011-playtest-property-runner/spec.md
sourceClarifications: work/011-playtest-property-runner/clarifications.md
sourceChecklist: work/011-playtest-property-runner/checklist.md
publicOrToolFacingImpact: true
---

# Playtest Property Runner Plan

Prose status: planned

## Source Snapshot
- spec: work/011-playtest-property-runner/spec.md sha256:7120963144ed83016483b4e0f03df8894ad98f404ba1211c4807d0d4b3f6f3ff schemaVersion:1
- clarifications: work/011-playtest-property-runner/clarifications.md sha256:e719ea969f7f8f18f79471a13f6c504f6d7f81be2459085e75251423d251a293 schemaVersion:1
- checklist: work/011-playtest-property-runner/checklist.md sha256:8d56d442d33ad4df98a664491264df767124bb0b5d22d5235c63de2010e2ac76 schemaVersion:1

## Plan Scope
- Add a new pure `Properties` module (`Properties.fsi`/`Properties.fs`) to `FS.GG.Game.Harness`,
  compiled after `Explore`. Add `PongSim` dogfood proofs (incl. one FsCheck-driven pairing test) and
  regenerate the surface baseline.
- Requirement count: 8. Clarification decision count: 2. Checklist result count: 8.

## Technical Context
F# 10 / .NET 10. Generation draws per-frame moves from the keymap alphabet using `Rng.nextInt`
(Game.Core's seeded SplitMix64), so the runner is pure and deterministic-by-seed (DEC-001), and the
harness references no FsCheck — `DependencyTests` (WI-007 FR-007) stays green (FR-007). Each run is
driven with `Driver.runCommands` under `Driver.identityFingerprint`, so the invariant sees the whole
world at `Init` and after each fixed step (DEC-002).

## Design
### Types (DEC-002)
- `type Counterexample = { Seed: uint64; Script: Command list list; Step: int }` — `Step` is the
  0-based post-step frame index of the first violation; `-1` with an empty `Script` means `Init` itself.
- `type PropertyResult = Held of runs: int | Falsified of Counterexample`.
- `type PropertyConfig = { Runs: int; MaxLength: int; Moves: Command list list option }`;
  `Properties.defaultConfig = { Runs = 1000; MaxLength = 32; Moves = None }` (FR-006).

### Runner (`Properties` module)
- `moves = config.Moves |> Option.defaultValue (Explore.defaultMoves playable)` — empty frame plus each
  single keymap command (reuses Phase 2's alphabet helper).
- `firstViolation invariant script`: run `Driver.runCommands playable identityFingerprint script`, then
  find the first frame index where `not (invariant frame)`; `None` if all frames hold.
- `check playable invariant seed = checkWith defaultConfig playable invariant seed`.
- `checkWith config playable invariant seed`: first check `invariant Init`; if false →
  `Falsified { Seed = seed; Script = []; Step = -1 }`. Otherwise thread an `Rng` from `seed` across
  `config.Runs` runs; each run draws a random length in `[0, MaxLength]` then that many random moves
  from the alphabet. For the first run whose `firstViolation` is `Some k`, shrink and return
  `Falsified`. If all runs hold → `Held config.Runs`.
- **Shrink** (FR-004): truncate the failing script to its first-violation prefix, then greedily remove
  each frame left-to-right, keeping a removal only if the reduced script still violates. Terminates at a
  minimal counter-script (no single further frame can be removed). Deterministic.

### Dogfood (FR-008)
- `PongSim`: "ball on the playfield" (`0 ≤ BallX < W ∧ 0 ≤ BallY < H`) and "unit velocity"
  (`|BallDX| = 1 ∧ |BallDY| = 1`) both `Held` across ≥1000 runs. A deliberately false invariant
  (`LeftY <> 0`) is `Falsified` with a minimal counter-script of four `MoveNorth` frames. A test-project
  FsCheck property (`Check.One(... WithMaxTest 1000 ...)`) drives the pairing over the playfield
  invariant. `DependencyTests` re-run green (no FsCheck in the package); baseline regenerated additively.

## Constitution Check
- **III Public Surface:** `Properties.fsi` before `Properties.fs`; baseline regenerated with tests.
- **V Model-Update-Effect:** pure; the only randomness is the threaded value `Rng`.
- **VI Test evidence:** every FR covered on `PongSim`; a deliberately-broken invariant proves the shrink.
- **Leaf dependency:** FsCheck stays test-only; the package remains Core+BCL (FR-007).

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: `Properties.check` draws per-frame moves from the keymap alphabet with `Rng.nextInt` and drives each generated script through `Driver.runCommands`.
- PD-002 [AC-002] [FR-002] [DEC-002] complete: the invariant is evaluated at `Init` and on each recorded post-step frame via `firstViolation`.
- PD-003 [AC-003] [FR-003] complete: `check` returns `Held runs` when every run holds, else `Falsified { Seed; Script; Step }` on the first violating run.
- PD-004 [AC-004] [FR-004] complete: the shrink truncates to the first-violation prefix then greedily drops removable frames, yielding a minimal counter-script.
- PD-005 [AC-005] [FR-005] [DEC-001] complete: generation threads a seeded `Rng` value, so identical `(seed, config)` produce the identical result across runs.
- PD-006 [AC-006] [FR-006] complete: `PropertyConfig` controls runs/length/alphabet; `defaultConfig` is `Runs = 1000`, `MaxLength = 32`, alphabet = empty + single keymap commands.
- PD-007 [AC-007] [FR-007] [DEC-001] complete: the module is pure Core+BCL with no FsCheck reference; `DependencyTests` stays green and the surface baseline is regenerated.
- PD-008 [AC-008] [FR-008] complete: `PongSim` proves the playfield/unit-velocity invariants over ≥1000 runs, a broken invariant shrunk to four `MoveNorth`, and an FsCheck pairing test.

## Contract Impact
- PC-001 [PD-001] [PD-003] [PD-004] [PD-006] command report: Tier-1 additive surface — a new `Properties` module and its `PropertyResult`/`Counterexample`/`PropertyConfig` types. `.fsi`, baseline, tests, and docs land together; existing surface byte-unchanged.
- PC-002 [PD-007] command report: the harness's referenced-assembly set stays `{FSharp.Core, FS.GG.Game.Core, System.*}` — explicitly NOT FsCheck; `DependencyTests` continues to enforce it.

## Verification Obligations
- VO-001 [PD-001] [PD-002] [PD-003] [PD-005] [PC-001] semanticTest: `PongSim` proves generation-and-per-step checking (`Held` over ≥1000 runs for the playfield/unit-velocity invariants) and determinism-by-seed (same seed → identical result).
- VO-002 [PD-004] [PD-006] [PD-008] semanticTest: a deliberately false invariant yields a minimal counter-script (four `MoveNorth`); the config's restricted alphabet/bounds are honoured; an FsCheck-driven pairing test runs ≥1000 cases.
- VO-003 [PD-007] [PC-002] semanticTest: `DependencyTests` re-run green (no FsCheck in the package); `dotnet build` clean under warnings-as-errors; the surface baseline regenerated and committed; full harness suite green.

## Migration Posture
- PM-001 [PC-001] diagnoseOnly: Plan schemaVersion 1 accepted. Purely additive; nothing removed or renamed; no consumer migration. The surface baseline is extended, not migrated.

## Generated View Impact
- GV-001 [PD-001] [PD-004] workModel: the work model (readiness/011-.../work-model.json) must reflect the eight FR→PD→VO chains once verify/ship build it; a plan edit that adds or drops a PD/VO restales it until `fsgg-sdd refresh` re-runs.
- GV-002 [PC-001] surfaceBaseline: `readiness/surface-baselines/FS.GG.Game.Harness.txt` is regenerated from the extended `.fsi` set; the surface-baseline-drift gate must stay green.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.
- This is Phase 3 of `docs/reports/2026-07-19-headless-playtest-authoring-machinery-design.md`; it builds
  on Phase 1's alphabet/laws and reuses Phase 2's `Explore.defaultMoves`.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 011-playtest-property-runner`.
