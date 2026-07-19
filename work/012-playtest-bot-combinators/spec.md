---
schemaVersion: 1
workId: 012-playtest-bot-combinators
title: Playtest Bot Combinators
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Playtest Bot Combinators Specification

Prose status: specified

## User Value
A game author writes a bot policy as a one-liner over the existing `Bot<'view>` — `Bots.sitter`, `Bots.scripted`, `Bots.random`, `Bots.chase`, `Bots.greedyToward` — instead of re-inventing `chaseBot`/`sitterBot` per game, and adapts a policy to a wider view with a projection helper that keeps it blind to everything outside its projection (the `Ai.TeamView` fog boundary). Every future matrix/balance proof gets shorter.

## Scope
- SB-001: A new pure `Bots` module in `FS.GG.Game.Harness` with combinators over `Bot<'view>`.
- SB-002: `sitter`, `random`, `chase`, `greedyToward` are stateless and preserve `(view, seed)` determinism; `scripted` is a documented single-use stateful playback bot.
- SB-003: A view-projection helper that adapts a `Bot<'inner>` to a `Bot<'outer>` via a projection.
- SB-004: `PongSim` dogfood proofs, a second toy game reusing the combinators, and an updated surface baseline.

## Non-Goals
- SB-010: The `fsgg-playtest` CLI (Phases 5–6).
- SB-011: A competitive or general game-playing AI — these produce test-input policies, not winning agents (`greedyToward` is a heuristic for reaching states).
- SB-012: Any change to `FS.GG.Game.Core` or to the existing behavior of `Bot`/`Matrix`/`Driver`.

## User Stories
- US-001 (P1): As a game author, I use `Bots.sitter`/`Bots.chase` instead of hand-writing a sitter/chaser per game.
- US-002 (P1): As a game author, I express a fixed input sequence as a `Bots.scripted` bot and a random policy as `Bots.random`.
- US-003 (P2): As a game author, I pick the locally-best move with `Bots.greedyToward` given a scoring function.
- US-004 (P2): As a game author, I adapt a bot written against a narrow view to a wider one with a projection, keeping it blind to the rest.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given `Bots.sitter`, when it decides on any view and seed, then it issues no commands and does not consume the generator — identical to `PongSim.sitterBot`, with identical match outcomes.
- AC-002 [US-002] [FR-002]: Given `Bots.scripted script`, when it is driven frame by frame, then it emits the script's frames in order and then idles; it is documented as single-use stateful playback.
- AC-003 [US-002] [FR-003]: Given `Bots.random draw`, when it decides, then it returns `draw rng` — the commands and advanced generator the caller's draw produces — and is deterministic in the seed.
- AC-004 [US-001] [FR-004]: Given `Bots.chase (target, self) up down`, when the target axis is less than the self axis it issues `up`, greater it issues `down`, equal it idles; and it is deterministic in `(view, seed)`. It reproduces `PongSim.chaseBot`'s directional (non-tie) behavior.
- AC-005 [US-003] [FR-005]: Given `Bots.greedyToward score moves`, when it decides, then it issues the single move maximizing `score view move` (the first on a tie), or no command when `moves` is empty.
- AC-006 [US-004] [FR-006]: Given `Bots.on project bot`, when it decides on an outer view, then it decides exactly as `bot` does on `project outer`, so the policy sees only the projection.
- AC-007 [US-001] [FR-007]: Given a two-seat matrix, when `Bots.sitter` replaces `PongSim.sitterBot`, then every match outcome is identical; and a second toy game reuses `Bots.chase`/`Bots.sitter` with zero bespoke bot code.
- AC-008 [US-001] [FR-008]: Given the harness assembly after this change, when its dependencies are inspected, then it still references only `FS.GG.Game.Core` and the BCL and performs no I/O or wall-clock read, and the surface baseline is regenerated to include the new `Bots` surface.

## Functional Requirements
- FR-001: `Bots.sitter` MUST issue no commands and leave the generator unchanged on every decision. (covers AC-001)
- FR-002: `Bots.scripted` MUST emit a given command script frame by frame in order and then idle; it MUST be documented as a single-use stateful playback bot (its playback position advances per decision). (covers AC-002)
- FR-003: `Bots.random` MUST decide via a caller-supplied draw function `Rng -> struct (Command list * Rng)`, threading the generator, and MUST be deterministic in the seed. (covers AC-003)
- FR-004: `Bots.chase` MUST issue the `up` command when the target axis is below the self axis, `down` when above, and idle on equality, reading both axes from the view; it MUST be deterministic in `(view, seed)`. (covers AC-004)
- FR-005: `Bots.greedyToward` MUST issue the single move maximizing a caller-supplied `score: 'view -> Command -> float` (the first on a tie), or no command when the move set is empty. (covers AC-005)
- FR-006: `Bots.on` MUST adapt a `Bot<'inner>` to a `Bot<'outer>` via a projection `'outer -> 'inner`, so the wrapped policy reads only the projection. (covers AC-006)
- FR-007: The combinators MUST reproduce `PongSim.sitterBot` with identical match outcomes and `chaseBot`'s directional behavior, and MUST be reusable on a second toy game with zero bespoke bot code. (covers AC-007)
- FR-008: The change MUST remain additive and pure: the harness assembly MUST still reference only `FS.GG.Game.Core` and the BCL with no I/O or wall-clock read, and the surface baseline MUST be regenerated to include the new `Bots` surface. (covers AC-008)

## Ambiguities
- AMB-001: How a stateless `Bot<'view>` supports `scripted` positional playback — via a captured playback index (stateful, single-use) or some stateless encoding.
- AMB-002: Whether `Bots.chase` draws from the generator on a tie (like `PongSim.chaseBot`) or idles without drawing.

## Public Or Tool-Facing Impact
- Adds public package surface: a new `Bots` module. Tier-1 change — `.fsi`, surface baseline, tests, and docs land together; existing surface unchanged (purely additive).

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 012-playtest-bot-combinators`.
