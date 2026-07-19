---
schemaVersion: 1
workId: 007-game-headless-harness
title: Game Headless Harness
stage: clarify
changeTier: tier1
status: needsAnswers
sourceSpec: work/007-game-headless-harness/spec.md
publicOrToolFacingImpact: true
---

# Game Headless Harness Clarifications

## Source Specification
- work/007-game-headless-harness/spec.md

## Clarification Questions
- CQ-001 [AMB:AMB-001]: Does a trace compare the full `'World` value, or a caller-supplied projection of it?
- CQ-002 [AMB:AMB-002]: Does the scripted driver always fold through the game's default keymap, or may a test supply its own?
- CQ-003 [AMB:AMB-003]: What does a `runMatrix` outcome carry so the runner stays game-agnostic?

## Answers
- CQ-001 → Both, with a default. A trace compares the full `'World` value by default — what `Game.Core`'s structural equality and fixed-step determinism were built for (`Loop.fsi`: "a headless replay that only fingerprints `Current`"). A caller may opt into a projection when the world is large or carries irrelevant fields; under-specifying that projection is the caller's risk, not the harness default.
- CQ-002 → Both, with a default. The driver folds through the game's default keymap by default, so a test hits the exact route a player does and the evidence stays non-synthetic (FR-002); a test may substitute its own keymap to cover rebinding.
- CQ-003 → A caller-supplied `outcome: 'World -> 'O` projection. `runMatrix` never inspects the world; the caller extracts what a match means (winner, score, survival), which keeps the runner and the win-rate-band helper game-agnostic.

## Decisions
- **DEC-001** [CQ-001] [AMB:AMB-001] [FR-001]: A trace compares the full `'World` value by default, with an opt-in caller projection `fingerprint: 'World -> 'F` for large worlds. FR-001's byte-identity guarantee is stated over whichever comparison surface is in force.
- **DEC-002** [CQ-002] [AMB:AMB-002] [FR-002]: The scripted driver folds through the game's default keymap by default; a test may substitute its own via `Playable.Keymap`. FR-002's raw-key→keymap→`Command` route holds for either.
- **DEC-003** [CQ-003] [AMB:AMB-003] [FR-005]: `runMatrix` takes a caller-supplied `outcome: 'World -> 'O` and returns one `'O` per match; the world type never leaks into the runner's or the band helper's vocabulary.

## Accepted Deferrals
- None.

## Remaining Ambiguity
- None. AMB-001, AMB-002, and AMB-003 are resolved by DEC-001 through DEC-003 above.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 007-game-headless-harness`.
