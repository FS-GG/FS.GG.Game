---
schemaVersion: 1
workId: 010-playtest-witness-finder
title: Playtest Witness Finder
stage: clarify
changeTier: tier1
status: clarified
sourceSpec: work/010-playtest-witness-finder/spec.md
publicOrToolFacingImpact: true
---

# Playtest Witness Finder Clarifications

## Source Specification
- work/010-playtest-witness-finder/spec.md

## Clarification Questions
- CQ-001 [AMB:AMB-001] blocking: Does `findScript` return a bare `Command list list option`, or a richer three-case result so truncation is distinct from not-found?
- CQ-002 [AMB:AMB-002] blocking: Does `findScript` take an explicit fingerprint for its visited key, or dedup by whole-world comparison?

## Answers
- CQ-001 → A richer three-case result. The whole point of the fail-closed principle (#266) is that "the goal is not reachable within N" and "the search gave up early" must not both collapse to `None`. So `findScript` returns `FindResult = Found of Witness | NotFound | Truncated of visited:int`. `Found` carries the shortest script and its depth; `NotFound` means the frontier was fully explored within the bound and no state satisfied the goal; `Truncated` means the depth/visited cap was hit first and carries the visited count. `reachable` mirrors this with a `Truncated: bool` flag on its result.
- CQ-002 → An explicit fingerprint for the visited key. Deduping by whole-world comparison would force `'world: comparison`, which a rich world (carrying an `Rng` or a `Map`) may not satisfy, and would prevent the caller from collapsing render-irrelevant fields out of the key. So `findScript` takes `fingerprint: 'world -> 'f when 'f: comparison` — exactly the visited-set key the Phase 0 contract pins — while the goal predicate still reads the whole `'world`.

## Decisions
- **DEC-001** [CQ-001] [AMB:AMB-001] [FR-003] [FR-004]: `Explore.findScript` returns `FindResult = Found of Witness | NotFound | Truncated of visited:int`; `Witness = { Script: Command list list; Depth: int }`. `reachable` returns `{ States: Set<'f>; Truncated: bool }`. Truncation is always a distinct, non-empty signal, never a silent `NotFound`.
- **DEC-002** [CQ-002] [AMB:AMB-002] [FR-006]: `Explore.findScript` and `Explore.reachable` take an explicit `fingerprint: 'world -> 'f when 'f: comparison` as the visited-set key; the goal predicate reads the whole `'world`. The world type need not be comparable.

## Accepted Deferrals
- None.

## Remaining Ambiguity
- None. AMB-001 and AMB-002 are resolved by DEC-001 and DEC-002 above.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 010-playtest-witness-finder`.
