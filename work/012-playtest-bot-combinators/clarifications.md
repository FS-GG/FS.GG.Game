---
schemaVersion: 1
workId: 012-playtest-bot-combinators
title: Playtest Bot Combinators
stage: clarify
changeTier: tier1
status: clarified
sourceSpec: work/012-playtest-bot-combinators/spec.md
publicOrToolFacingImpact: true
---

# Playtest Bot Combinators Clarifications

## Source Specification
- work/012-playtest-bot-combinators/spec.md

## Clarification Questions
- CQ-001 [AMB:AMB-001] blocking: How does a stateless `Bot<'view>` support `scripted` positional playback?
- CQ-002 [AMB:AMB-002] blocking: Does `Bots.chase` draw from the generator on a tie, or idle without drawing?

## Answers
- CQ-001 → Via a captured playback index — `scripted` is the one deliberately stateful combinator. A `Bot<'view>.Decide` receives only `'view` and `Rng`, neither of which carries a script position, so `Bots.scripted` closes over a mutable index that advances each decision (emitting the next frame, then idling when the script is exhausted). It is documented as **single-use**: a fresh `Bots.scripted script` must be constructed per run, because a shared instance would carry playback state across runs (and would therefore not satisfy the `(view, seed)` determinism law). The other four combinators are pure and do satisfy it.
- CQ-002 → It idles without drawing. `Bots.chase` issues `up`/`down`/idle purely from the two view axes and never touches the generator, so it is deterministic in `(view, seed)` with no draw. This reproduces `PongSim.chaseBot`'s directional (non-tie) behaviour; the tie-break-with-random-draw that `chaseBot` adds is a *composition* an author builds from `Bots.chase` and `Bots.random`, not a hidden behaviour of `chase`.

## Decisions
- **DEC-001** [CQ-001] [AMB:AMB-001] [FR-002]: `Bots.scripted` closes over a mutable playback index and is documented single-use (fresh per run); it is the sole stateful combinator and the deliberate exception to `(view, seed)` determinism. `sitter`/`random`/`chase`/`greedyToward` are pure.
- **DEC-002** [CQ-002] [AMB:AMB-002] [FR-004]: `Bots.chase` idles on a tie without drawing from the generator, so it is deterministic in `(view, seed)`; the random tie-break is a caller composition of `Bots.chase` and `Bots.random`, not built in.

## Accepted Deferrals
- None.

## Remaining Ambiguity
- None. AMB-001 and AMB-002 are resolved by DEC-001 and DEC-002 above.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 012-playtest-bot-combinators`.
