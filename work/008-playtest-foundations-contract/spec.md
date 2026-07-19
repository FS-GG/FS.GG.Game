---
schemaVersion: 1
workId: 008-playtest-foundations-contract
title: Playtest Foundations Contract
stage: specify
changeTier: tier2
status: specified
publicOrToolFacingImpact: false
---

# Playtest Foundations Contract Specification

Prose status: specified

## User Value
A future author of the playtest tooling — and anyone changing `Playable`/`Trace`/`Matrix` in `FS.GG.Game.Harness` — can see, in one pinned note, exactly which harness primitives the downstream tooling (laws library, witness-finder, property runner, manifest tooling) relies on and why, so a load-bearing change is not made blind.

## Scope
- SB-001: A single read-only documentation contract note under `docs/reference/` that pins four already-shipped `FS.GG.Game.Harness` primitives as relied-upon by the playtest tooling roadmap.
- SB-002: For each pinned primitive, the exact surface relied upon and the downstream Phase (1–6) feature that would break if it changed.

## Non-Goals
- SB-010: Any change to `FS.GG.Game.Harness` or `FS.GG.Game.Core` public surface, or to any surface baseline — this is audit-only.
- SB-011: The laws library, witness-finder, property runner, bot combinators, and the `fsgg-playtest` CLI (Phases 1–6, separate work items).
- SB-012: Any new test asserting sim behavior — the only check here is that the note exists, is internally consistent with the shipped `.fsi`, and that no baseline changed.

## User Stories
- US-001 (P1): As an author about to build a downstream playtest tool, I can read one note that tells me which harness primitives the tooling leans on and why, so I build against a fixed base.
- US-002 (P2): As a maintainer changing `Playable`/`Trace`/`Matrix`, I can see from the note that a primitive is load-bearing for the tooling before I change it, so I do not silently break Phases 1–6.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given the note, when it describes the command alphabet, then it pins `Playable.Keymap`'s value-set as the command alphabet a search/generator reads, and names the witness-finder (Phase 2) and property runner (Phase 3) as the dependents.
- AC-002 [US-001] [FR-002]: Given the note, when it describes graph-search deduplication, then it pins the fingerprint `'world -> 'f` (`Driver.identityFingerprint` / a caller projection) as the canonical visited-set key, and names the witness-finder/`reachable` (Phase 2) as the dependent.
- AC-003 [US-002] [FR-003]: Given the note, when it describes reproducibility, then it pins ambient-free determinism (no wall clock, no ambient RNG) as enforced by `DependencyTests`, and names the laws library (Phase 1) and property runner (Phase 3) as the dependents.
- AC-004 [US-001] [FR-004]: Given the note, when it describes property generation, then it pins FsCheck as already a referenced dependency of `Game.Harness.Tests`, so Phase 3 needs no new package.
- AC-005 [US-002] [FR-005]: Given the repository after this work item, when the surface baselines and public `.fsi` set are compared to before, then they are byte-identical — the note added contract documentation only, no surface.

## Functional Requirements
- FR-001: The note MUST pin `Playable.Keymap`'s value-set as the command alphabet the tooling reads, naming the exact surface and the Phase 2/Phase 3 dependents. (covers AC-001)
- FR-002: The note MUST pin the fingerprint `'world -> 'f` as the canonical visited-set key for graph search, naming the exact surface and the Phase 2 dependent. (covers AC-002)
- FR-003: The note MUST pin ambient-free determinism (no wall clock, no ambient RNG) as enforced by `DependencyTests`, naming the Phase 1/Phase 3 dependents. (covers AC-003)
- FR-004: The note MUST pin FsCheck as an already-referenced test dependency, establishing that Phase 3 needs no new package. (covers AC-004)
- FR-005: The work item MUST introduce no public-surface change: every surface baseline and every published `.fsi` MUST be byte-identical before and after. (covers AC-005)

## Ambiguities
No material ambiguities recorded.

## Public Or Tool-Facing Impact
- None. This is a documentation-only, tier-2 work item; it adds a reference note and changes no public surface, schema, command, or baseline (FR-005).

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 008-playtest-foundations-contract`.
