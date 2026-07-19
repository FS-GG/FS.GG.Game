---
schemaVersion: 1
workId: 009-playtest-laws-trace-diff
title: Playtest Laws And Trace Diff
stage: clarify
changeTier: tier1
status: clarified
sourceSpec: work/009-playtest-laws-trace-diff/spec.md
publicOrToolFacingImpact: true
---

# Playtest Laws And Trace Diff Clarifications

## Source Specification
- work/009-playtest-laws-trace-diff/spec.md

## Clarification Questions
- CQ-001 [AMB:AMB-001] blocking: Does the single `Laws` script runner also check matrix order-independence, or is that law a separate entry point that takes a `MatchSetup` and matches?
- CQ-002 [AMB:AMB-002] blocking: What does `Trace.render` emit for the empty trace, and how does it delimit frames, so goldens are stable and diff-friendly?

## Answers
- CQ-001 → A separate entry point. The four script-drivable laws (determinism, replay, fixed-step, provenance) are checked by one `Laws.check : Playable -> sampleScripts -> LawReport` runner, because they need only a `Playable` and key scripts. Matrix order-independence fundamentally needs match inputs — a `MatchSetup`, an `outcome` projection, and a set of `Match` values — which a script runner does not carry, so it is a distinct runner `Laws.matrixOrderIndependent`. Forcing it into the script runner would either take an unused-most-of-the-time parameter or hide a second contract behind a scripts-only signature; keeping it separate is honest.
- CQ-002 → One frame per line, each line the caller's `show frame` with no added decoration beyond a stable step index prefix (`0: <show>`), joined by `\n`; the empty trace renders as the empty string. This is line-diff-friendly (a golden diff points at the exact step) and deterministic (frames are already in step order). The step prefix makes `Trace.render` output and `Trace.firstDivergence`'s index line up.

## Decisions
- **DEC-001** [CQ-001] [AMB:AMB-001] [FR-005] [FR-008]: `Laws.check : Playable<'world,'key> -> 'key list list list -> LawReport` checks determinism/replay/fixed-step/provenance over sample key scripts; matrix order-independence (FR-005) is a separate `Laws.matrixOrderIndependent : MatchSetup<'world,'view> -> ('world -> 'o) -> Match<'view> list -> bool` (or a `LawResult`), so each runner's signature carries exactly the inputs its laws need.
- **DEC-002** [CQ-002] [AMB:AMB-002] [FR-007]: `Trace.render show trace` emits one line per frame, `"<i>: " + show frame`, in step order, joined by `\n`; the empty trace renders as `""`. The step-index prefix aligns rendered output with `Trace.firstDivergence` indices.

## Accepted Deferrals
- None.

## Remaining Ambiguity
- None. AMB-001 and AMB-002 are resolved by DEC-001 and DEC-002 above.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 009-playtest-laws-trace-diff`.
