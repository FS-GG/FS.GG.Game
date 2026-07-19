---
schemaVersion: 1
workId: 007-game-headless-harness
title: Game Headless Harness
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Game Headless Harness Specification

Prose status: specified

## User Value
A game author can prove a gameplay requirement by driving the real game world headlessly through the standard `Command` input frontier — script or in-process bot — with replay reduced to a value comparison rather than a tolerance check, and with any shortcut (a hand-built world state) marked honestly as synthetic.

## Scope
- SB-001: A deterministic, headless harness package `FS.GG.Game.Harness` on top of `FS.GG.Game.Core`.
- SB-002: Four ways in — a scripted raw-key→keymap→`Command` driver; an in-process `Bot` policy; a multi-seed matrix runner; and a typed synthetic-state entry point.
- SB-003: A trace/fingerprint the caller asserts on as a value.

## Non-Goals
- SB-010: The SDD FR-classifier grammar and the per-FR evidence gate (FS-GG/.github ADR-0048) — not built here.
- SB-011: Governance profile-gate inheritance (ADR-0049), the block-on-ship flip, and the reference-game proof run — separate work items.
- SB-012: The LLM/agent-playthrough tier — separate tooling; this package only defines the `Command`-trace capture/replay bridge it plugs into (FR-008).
- SB-013: The `fs-gg-playtest` product skill and its Rendering `game`-profile wiring — later work items.

## User Stories
- US-001 (P1): As a game author, I can drive my game's world through a scripted sequence of real inputs headlessly and get a deterministic trace I can assert on.
- US-002 (P1): As a game author, I can have an in-process bot play my game so I generate gameplay states without hand-scripting every input.
- US-003 (P2): As a game author, I can run a matchup across many seeds and assert an aggregate property, such as a win-rate band, headlessly.
- US-004 (P2): As a game author, when driving from real input is too expensive, I can start a test from a hand-built world state that is clearly and unavoidably marked synthetic.
- US-005 (P2): As a game author, I can capture a bot's or an agent's playthrough as a script and replay it deterministically as a regression golden.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given a `Playable` and a fixed scripted key sequence, when the script is folded through the harness on two separate runs, then the two world traces are byte-identical.
- AC-002 [US-001] [FR-002]: Given a scripted raw key token bound by the game's keymap, when the harness processes the script, then it resolves the token through the keymap to a `Command` before applying it, and an unbound token maps to no command rather than being applied raw.
- AC-003 [US-001] [FR-003]: Given a frame-time sequence, when the harness advances the world, then it advances only by whole fixed steps via the game's step function, never with a variable `dt` and never feeding an interpolation `alpha` back into the world.
- AC-004 [US-002] [FR-004]: Given a `Bot` decision, when it runs, then it is passed only a caller-supplied view and a seeded generator, and the same `(view, seed)` yields the same `Command`s, with no access to the full world model in its signature.
- AC-005 [US-003] [FR-005]: Given a set of `(bot, bot, seed)` matches, when `runMatrix` runs them, then it returns one deterministic outcome per match, and permuting the input order does not change any match's outcome.
- AC-006 [US-004] [FR-006]: Given a test started from a hand-built world via the synthetic entry point, when its trace or evidence is produced, then that output is identifiable as synthetic and is distinct in the API from an input-driven run.
- AC-007 [US-001] [FR-007]: Given the harness assembly, when its dependencies are inspected, then it references only `FS.GG.Game.Core` and the BCL, and it performs no I/O and no wall-clock read.
- AC-008 [US-005] [FR-008]: Given a bot or agent playthrough, when its produced `Command` sequence is captured as a script and replayed through the scripted driver, then the replay yields a byte-identical trace to the original run.

## Functional Requirements
- FR-001: The harness MUST produce a byte-identical world trace for a fixed scripted key sequence across repeated runs and platforms. (covers AC-001)
- FR-002: The scripted driver MUST resolve each raw key token through the game's keymap into a `Command` before applying it, and MUST treat an unbound token as producing no command. (covers AC-002)
- FR-003: The harness MUST advance the world only by whole fixed steps via the game's step function, with no variable `dt` and no interpolation `alpha` fed back into the simulation. (covers AC-003)
- FR-004: A `Bot` policy MUST decide from a caller-supplied view and a seeded generator only — never the full world model — and MUST be deterministic in `(view, seed)`. (covers AC-004)
- FR-005: `runMatrix` MUST run a set of `(bot, bot, seed)` matches, return one outcome per match, and be independent of the order in which matches or sources are supplied. (covers AC-005)
- FR-006: The synthetic-state entry point MUST be a distinct, typed API surface, and any trace or evidence derived from it MUST be identifiable as synthetic. (covers AC-006)
- FR-007: The harness MUST depend only on `FS.GG.Game.Core` and the BCL, and MUST perform no I/O and no wall-clock read. (covers AC-007)
- FR-008: A captured `Command` sequence from a bot or agent playthrough MUST, when replayed through the scripted driver, reproduce a byte-identical trace to the original run. (covers AC-008)

## Ambiguities
- AMB-001: Whether a trace fingerprints the full `'World` value or a caller-supplied projection of it — a memory/fidelity trade-off to resolve in `clarify`.
- AMB-002: Whether the keymap the scripted driver folds through is always the game's default keymap or a test may supply its own — bears on how FR-002 is exercised.
- AMB-003: What a `runMatrix` "outcome" carries at the vocabulary level (raw final world vs a caller-supplied `outcome: 'World -> 'O` projection) so the band assertion stays game-agnostic.

## Public Or Tool-Facing Impact
- Introduces a new public package surface (`FS.GG.Game.Harness` with `.fsi` contracts) consumed by every game's test project — a tier-1 change, so signatures and tests land together.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 007-game-headless-harness`.
