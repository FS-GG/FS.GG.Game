---
schemaVersion: 1
workId: 007-game-headless-harness
title: Game Headless Harness
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/007-game-headless-harness/spec.md
sourceClarifications: work/007-game-headless-harness/clarifications.md
sourceChecklist: work/007-game-headless-harness/checklist.md
publicOrToolFacingImpact: true
---

# Game Headless Harness Plan

Prose status: planned

## Source Snapshot
- spec: work/007-game-headless-harness/spec.md sha256:9e69fa3abed046d2df6e3cdcef3ea6345de91b49dbb0597a269de5202b7772eb schemaVersion:1
- clarifications: work/007-game-headless-harness/clarifications.md sha256:b859f16d05c21f3c441e6155e60556c3bc53458e9bbaf4bac2e239ea2eadb56d schemaVersion:1
- checklist: work/007-game-headless-harness/checklist.md sha256:12c9d240fa17367ff6a7ecdc3f3b4f1de5de87a24905eac48c541cc52404c40f schemaVersion:1

## Plan Scope
- Add a new package `FS.GG.Game.Harness` (`src/Game.Harness/FS.GG.Game.Harness.fsproj`,
  namespace `FS.GG.Game.Harness`) on top of `FS.GG.Game.Core`, reaching up to nothing
  above the BCL. Five paired `.fsi`/`.fs` modules: `Trace`, `Playable`, `Driver`,
  `Matrix`, `Synthetic`. A minimal integer-grid Pong reference sim lives in the test
  project, not the package.
- Requirement count: 8. Clarification decision count: 3. Checklist result count: 8.

## Technical Context
F# 10 / .NET 10, MVU discipline (Principle V). The harness reuses Game.Core's
`Command` (device-free intent), `Rng` (seeded, value-typed SplitMix64), and the
fixed-step contract. It is the **headless replay** case `Loop.fsi` sanctions ("a
headless replay that only fingerprints `Current` may carry no `Previous` at all"):
it drives the game's own step function at a fixed `Dt`, folds the steps, and
fingerprints the resulting world as a value — replay is a value comparison, not a
tolerance check. Because Game.Core has no concrete world, no command-driven step,
and the keymap type lives *outside* Game.Core (in `FS.GG.UI.KeyboardInput`, which
FR-007 forbids the harness from referencing), the harness is generic over the world
`'world` and the raw-key token `'key` and carries the game's `Apply`, `Step`, `Dt`,
and a harness-owned `Map<'key, Command>` keymap.

## Constitution Check
- **III Public Surface:** every module ships a `.fsi` declared before its `.fs`;
  signatures land before implementation; a surface baseline
  (`readiness/surface-baselines/FS.GG.Game.Harness.txt`) is added and kept current.
- **V Model-Update-Effect:** `Apply`/`Step` are pure transitions; no I/O, no
  wall-clock read (FR-007); randomness is a threaded value `Rng`; no `Map`/`HashSet`
  order escapes a result (matrix outcomes returned in input order; bots applied in a
  fixed seat order).
- **VI Test evidence:** each FR is covered by a real-fixture test driving the grid
  Pong sim through the real `Command` frontier; the synthetic hatch is the only
  synthetic surface and no test relies on it to satisfy an FR.

## Design
Five modules under `src/Game.Harness/` (compile order):

1. **`Trace`** — provenance-carrying comparison surface.
   `[<RequireQualifiedAccess>] type Origin = InputDriven | Synthetic`; `Trace<'f>` is
   **sealed/opaque** (no public ctor/fields), mirroring `Ai.TeamView`. Constructed
   only by the driver (`InputDriven`) and the synthetic hatch (`Synthetic`), so a
   synthetic trace can never be forged input-driven (FR-006). `Trace.frames`
   exposes the `'f list` compared for byte-identity (FR-001, FR-008); `Trace.origin`
   / `Trace.isSynthetic` read provenance; `Trace.equalFrames` compares frame
   sequences.

2. **`Playable`** — the driven game plus the bot policy.
   `Playable<'world,'key when 'key : comparison>` = `{ Init; Keymap: Map<'key,
   Command>; Apply: Command -> 'world -> 'world; Step: 'world -> float -> 'world;
   Dt: float }`. `Playable.resolve` does raw key -> keymap -> `Command`, `None` for
   an unbound token (FR-002). `Bot<'view>` = `{ Decide: 'view -> Rng -> struct
   (Command list * Rng) }` — sees only `'view` and `Rng`; `'world` never in its
   signature (FR-004).

3. **`Driver`** — scripted / bot single-seat drivers.
   `identityFingerprint` is the default full-`'world` fingerprint (DEC-001).
   `runCommands` is the shared core: per frame, fold `Apply` over the frame's
   commands, then advance exactly one fixed step `Step world Dt` (constant `Dt`, no
   `alpha` — FR-003), recording `fingerprint world`. `runScript` resolves each
   frame's raw keys through the keymap (dropping unbound) then delegates
   (FR-002; a test may pass a `Playable` with its own keymap — DEC-002). `runBot`
   observes -> decides -> applies -> steps and captures each frame's emitted
   commands into `Run.Captured`, so replay through `runCommands` is byte-identical
   (FR-008).

4. **`Matrix`** — multi-seed bot-vs-bot runner (FR-005).
   `[<RequireQualifiedAccess>] type Seat = A | B`; `MatchSetup<'world,'view>` carries
   `Dt`, a seeded `Init: Rng -> 'world`, per-seat `Observe`/`Apply`, `Step`,
   `IsOver`, `MaxSteps`; `Match<'view>` = `{ Seed; A; B }`. `runMatch` seeds from
   `Rng.ofSeed Seed`, steps in fixed seat order (A then B) to `IsOver`/`MaxSteps`,
   returns `outcome world` — the world type never leaks (DEC-003). `runMatrix`
   returns one `'o` per input match in input order, each independent (permuting
   `matches` permutes the result identically). `winRate` is the band helper.

5. **`Synthetic`** — labeled fallback (FR-006).
   `Synthetic.trace` builds a `Trace` with `Origin.Synthetic` from hand-built worlds.
   It is the only route to a `Synthetic`-tagged trace and cannot yield an
   `InputDriven` one, so `Trace.isSynthetic` is an unforgeable provenance bit.

Dependency posture (FR-007): the `.fsproj` carries one `ProjectReference` (Game.Core)
and FSharp.Core — no `FS.GG.UI.KeyboardInput`, no `FS.GG.Game.Render`, no I/O.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: `Trace<'f>` is an opaque value whose `frames : 'f list` is the byte-identity comparison surface; `Driver.runCommands`/`runScript` produce identical `frames` for identical `(Playable, script)` across runs — determinism inherited from `Rng` value-threading and structural world equality (DEC-001).
- PD-002 [AC-002] [FR-002] complete: `Playable.resolve p key = Map.tryFind key p.Keymap`; `runScript` maps each raw token through the keymap before `Apply`, and an unbound token contributes no command (`None` dropped), while a test may substitute `Playable.Keymap` (DEC-002).
- PD-003 [AC-003] [FR-003] complete: `runCommands`/`runBot` advance the world by exactly one `Step world Dt` per frame at the constant `Playable.Dt`; no variable `dt`, and `Loop.alpha` is never consulted — the sim only ever sees whole fixed steps.
- PD-004 [AC-004] [FR-004] complete: `Bot<'view>.Decide : 'view -> Rng -> struct (Command list * Rng)` — the signature admits no `'world`; identical `(view, seed)` yields identical commands because `Rng` is a pure value and `Decide` a function.
- PD-005 [AC-005] [FR-005] complete: `Matrix.runMatrix setup outcome matches` returns `(Match * 'o) list`, one per input match, in input order; each `runMatch` seeds only from its `Match.Seed` and reads no shared state, so permuting `matches` permutes the output identically and changes no match's `'o` (DEC-003). `winRate` folds outcomes to a fraction.
- PD-006 [AC-006] [FR-006] complete: `Origin.Synthetic` is reachable only via `Synthetic.trace` and `Origin.InputDriven` only via the driver; `Trace.isSynthetic` distinguishes them, so any trace/evidence from the synthetic hatch is self-identifying and `synthetic:true` can never satisfy an obligation.
- PD-007 [AC-007] [FR-007] complete: `FS.GG.Game.Harness.fsproj` references only `FS.GG.Game.Core` + FSharp.Core; the modules perform no I/O and read no wall clock (all time is the caller's fixed `Dt`).
- PD-008 [AC-008] [FR-008] complete: `runBot` captures the per-frame `Command list` it applied into `Run.Captured`; replaying `Run.Captured` through `runCommands` on the same `Playable` reproduces byte-identical `Trace.frames` — bot/agent capture-replay is the same value comparison as scripted replay.

## Contract Impact
- PC-001 [PD-001] [PD-002] [PD-003] [PD-004] [PD-006] command report: Tier-1 new public package `FS.GG.Game.Harness` with `.fsi` contracts for `Trace`, `Playable`, `Driver`, `Matrix`, `Synthetic`. A new surface baseline `readiness/surface-baselines/FS.GG.Game.Harness.txt` is added; `.fsi`, baseline, tests, and docs land together. The `.fsi` files are the authoritative contract; `contracts/harness-surface.md` records it in prose.
- PC-002 [PD-007] command report: The new package is wired into `FS.GG.Game.slnx` and any build/surface gates; its referenced-assembly set (Game.Core + FSharp.Core + System.*) is a contract asserted by an AC-007 reflection test, keeping the leaf-dependency invariant enforced rather than documented.

## Verification Obligations
- VO-001 [PD-001] [PD-003] [PD-008] [PC-001] semanticTest: Expecto tests over the grid Pong sim proving byte-identical `Trace.frames` on repeat runs (FR-001), whole-fixed-step advance counts (FR-003), and capture-then-replay identity (FR-008); named `determinism golden` where a literal golden applies, so the determinism gate covers them.
- VO-002 [PD-002] [PD-004] [PD-005] [PD-006] semanticTest: Tests proving keymap resolution incl. unbound-token drop (FR-002), bot determinism in `(view, seed)` with no `'world` in signature (FR-004), `runMatrix` order-independence and one-outcome-per-match plus `winRate` stability (FR-005), and synthetic-vs-input provenance distinction (FR-006).
- VO-003 [PD-007] [PC-002] semanticTest: A reflection test asserting the harness assembly's referenced assemblies are within `{FSharp.Core, FS.GG.Game.Core, System.*}` and that no wall-clock/I/O type is referenced (FR-007); `dotnet build` clean under warnings-as-errors; the surface baseline for `FS.GG.Game.Harness` regenerated and committed; full and determinism-filtered suites green.

## Migration Posture
- PM-001 [PC-001] diagnoseOnly: Plan schemaVersion 1 is accepted. The new package adds surface; it removes and renames nothing, so no consumer migration is required. The surface baseline is created, not migrated.

## Generated View Impact
- GV-001 [PD-001] [PD-008] workModel: the normalized work model (readiness/007-game-headless-harness/work-model.json) must reflect all eight FR→PD→VO chains once verify/ship build it; a plan edit that adds or drops a PD-###/VO-### restales it until `fsgg-sdd refresh` re-runs, so the eight determinism/provenance obligations stay traceable from requirement to evidence.
- GV-002 [PC-001] surfaceBaseline: `readiness/surface-baselines/FS.GG.Game.Harness.txt` is generated from the new `.fsi` files; the surface-baseline-drift gate must stay green.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only. This package is the
  capability the ADR-0048/ADR-0049 gates presume; it changes no SDD or Governance
  surface.
- The reference sim here is a minimal grid Pong sufficient to exercise every FR; the
  full Snake/Pong obligation-chain proof is WI-7 (FS-GG/FS.GG.Game#422), a separate
  item blocked on the cross-repo critical path.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 007-game-headless-harness`.
