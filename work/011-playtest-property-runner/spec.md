---
schemaVersion: 1
workId: 011-playtest-property-runner
title: Playtest Property Runner
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Playtest Property Runner Specification

Prose status: specified

## User Value
A game author asserts an invariant (`'world -> bool`, e.g. "the ball is always on the playfield") holds at every step across thousands of randomly generated valid input scripts over the game's keymap alphabet, and — when it does not — gets a minimal shrunk counter-script that still violates it, instead of bounding a single hand-written run. Generation is a pure function of a seed, so a failure is deterministic and replayable.

## Scope
- SB-001: A new pure `Properties` module in `FS.GG.Game.Harness` that generates random valid scripts over the `Playable.Keymap` alphabet from a seed and drives each through the game.
- SB-002: The invariant is checked at `Init` and after every fixed step of every generated run; a config controls run count, max length, and the move alphabet.
- SB-003: On the first violation, a deterministic shrink reduces the failing run to a minimal counter-script that still violates the invariant.
- SB-004: FsCheck stays a test-only dependency (out of the pure package); `PongSim` dogfood proofs and an updated surface baseline.

## Non-Goals
- SB-010: Bot combinators (Phase 4) and the `fsgg-playtest` CLI (Phases 5–6).
- SB-011: Putting FsCheck (or any generator library) into the pure package — that would break the leaf-dependency invariant (WI-007 FR-007).
- SB-012: Any change to `FS.GG.Game.Core` or to the existing behavior of `Trace`/`Driver`/`Matrix`/`Laws`/`Explore`.

## User Stories
- US-001 (P1): As a game author, I assert an invariant holds at every step across thousands of generated runs in one call, instead of writing per-run bounds tests.
- US-002 (P1): As a game author, when an invariant is violated, I get a minimal counter-script that still violates it, so I debug the essence rather than a long noisy run.
- US-003 (P2): As a game author, my property run is deterministic in a seed, so a failure reproduces exactly.
- US-004 (P2): As a game author, I restrict the generated move alphabet and tune run count/length via a config.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given a `Playable` and a seed, when `Properties.check` runs, then it generates random valid scripts over the `Playable.Keymap` alphabet and drives each through the game.
- AC-002 [US-001] [FR-002]: Given a generated run, when the runner evaluates the invariant, then it checks it at `Init` and after every fixed step, not only the final frame.
- AC-003 [US-001] [FR-003]: Given an invariant that holds, when `Properties.check` runs the configured number of runs, then it returns `Held` with the run count; given one that is violated, it returns `Falsified` with a counterexample.
- AC-004 [US-002] [FR-004]: Given a violated invariant, when the runner returns a counterexample, then its script is shrunk so that removing any further frame no longer violates the invariant — a minimal counter-script.
- AC-005 [US-003] [FR-005]: Given the same seed and config, when `Properties.check` runs twice, then it returns the identical result (generation is a pure function of the seed).
- AC-006 [US-004] [FR-006]: Given a config with a restricted move alphabet and run/length bounds, when the runner generates, then it draws only from that alphabet and respects the bounds; the default alphabet is the empty frame plus each single command in `Playable.Keymap` and the default run count is at least 1000.
- AC-007 [US-001] [FR-007]: Given the harness assembly after this change, when its dependencies are inspected, then it still references only `FS.GG.Game.Core` and the BCL (no FsCheck) and performs no I/O or wall-clock read, and the surface baseline is regenerated to include the new `Properties` surface.
- AC-008 [US-001] [FR-008]: Given `PongSim`, when the "ball always on the playfield" and "velocity magnitude is unit" invariants run across at least 1000 generated runs, then they hold; and when a deliberately false invariant runs, then a minimal counter-script is produced — including via an FsCheck-driven pairing test in the test project.

## Functional Requirements
- FR-001: `Properties.check` MUST generate random valid scripts over the `Playable.Keymap` alphabet from a seed and drive each through the game. (covers AC-001)
- FR-002: The runner MUST check the invariant at `Init` and after every fixed step of every generated run. (covers AC-002)
- FR-003: `Properties.check` MUST return `Held` with the run count when the invariant holds across all runs, and `Falsified` with a counterexample (seed, script, violating step) on the first violation. (covers AC-003)
- FR-004: On a violation, the runner MUST shrink the failing run to a minimal counter-script — one where removing any further frame no longer violates the invariant. (covers AC-004)
- FR-005: Generation MUST be a pure, deterministic function of the seed and config: the same inputs MUST yield the identical result. (covers AC-005)
- FR-006: A config MUST control the run count, max script length, and move alphabet; the default alphabet MUST be the empty frame plus each single command in `Playable.Keymap`, and the default run count MUST be at least 1000. (covers AC-006)
- FR-007: The runner MUST be pure: the harness assembly MUST reference only `FS.GG.Game.Core` and the BCL (no FsCheck) with no I/O or wall-clock read, and the surface baseline MUST be regenerated to include the new `Properties` surface. (covers AC-007)
- FR-008: `PongSim` MUST demonstrate the runner: the playfield and unit-velocity invariants hold across at least 1000 generated runs, a deliberately false invariant yields a minimal counter-script, and an FsCheck-driven test in the test project exercises the pairing. (covers AC-008)

## Ambiguities
- AMB-001: Whether generation and shrinking are pure (over `Game.Core`'s `Rng`, in the package) or FsCheck-based (which would pull FsCheck into the package and break FR-007).
- AMB-002: Whether "every step" includes the `Init` world and how the counterexample indexes the violating step.

## Public Or Tool-Facing Impact
- Adds public package surface: a new `Properties` module plus its `PropertyResult`/`Counterexample`/`PropertyConfig` types. Tier-1 change — `.fsi`, surface baseline, tests, and docs land together; existing surface unchanged (purely additive).

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 011-playtest-property-runner`.
