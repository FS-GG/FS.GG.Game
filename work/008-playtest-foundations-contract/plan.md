---
schemaVersion: 1
workId: 008-playtest-foundations-contract
title: Playtest Foundations Contract
stage: plan
changeTier: tier2
status: planned
sourceSpec: work/008-playtest-foundations-contract/spec.md
sourceClarifications: work/008-playtest-foundations-contract/clarifications.md
sourceChecklist: work/008-playtest-foundations-contract/checklist.md
publicOrToolFacingImpact: false
---

# Playtest Foundations Contract Plan

Prose status: planned

## Source Snapshot
- spec: work/008-playtest-foundations-contract/spec.md sha256:865fdcbc34db2fdbcb188a653a2e9fd90dae666efb4a4416504be8f09f1ad422 schemaVersion:1
- clarifications: work/008-playtest-foundations-contract/clarifications.md sha256:d89e1a828374daf1ed2808794ec1d73660b4577c1e139e1b7be8f11adf55368b schemaVersion:1
- checklist: work/008-playtest-foundations-contract/checklist.md sha256:58445bd465654db60bcac3497839ba791dbc7468890c07fae6b7f32232b6a367 schemaVersion:1

## Plan Scope
- Author one read-only reference note, `docs/reference/playtest-tooling-foundations.md`, pinning four
  already-shipped `FS.GG.Game.Harness` primitives as relied-upon by the playtest tooling roadmap
  (Phases 1–6). No source code, no `.fsi`, no build target, no baseline change.
- Requirement count: 5. Clarification decision count: 0. Checklist result count: 5.

## Technical Context
This is a documentation-only, tier-2 audit. It touches no compiled project. The note's content is
verified against the *shipped* surface it cites: `Playable.Keymap` (`src/Game.Harness/Playable.fsi`),
`Driver.identityFingerprint` and the fingerprint projection (`src/Game.Harness/Driver.fsi`),
ambient-free determinism as enforced by `tests/Game.Harness.Tests/DependencyTests.fs`, and the FsCheck
`PackageReference` in `tests/Game.Harness.Tests/Game.Harness.Tests.fsproj`. Because nothing compiled
changes, the load-bearing guarantee (FR-005) is that the surface baselines under
`readiness/surface-baselines/` and the published `.fsi` set are byte-identical before and after.

## Design
- The note has one short section per pinned primitive. Each section states: the exact surface relied
  upon (type/member + `.fsi` file), the invariant the tooling assumes, and the downstream Phase that
  breaks if the invariant is dropped.
  1. **Keymap-as-command-alphabet** → `Playable.Keymap : Map<'key,Command>`; its value-set is the move
     alphabet a search/generator enumerates. Dependents: witness-finder (Phase 2), property runner
     (Phase 3). (FR-001)
  2. **Fingerprint-as-visited-key** → `'world -> 'f` (`Driver.identityFingerprint` or a caller
     projection); the canonical dedup key for BFS/reachability. Dependent: witness-finder /
     `reachable` (Phase 2). (FR-002)
  3. **Ambient-free determinism** → no wall clock, no ambient RNG; enforced by `DependencyTests`. A
     search/replay/property run over a `Playable` is reproducible only because of this. Dependents:
     laws library (Phase 1), property runner (Phase 3). (FR-003)
  4. **FsCheck availability** → already a `PackageReference` of `Game.Harness.Tests`; Phase 3 needs no
     new package. (FR-004)
- The note carries a "load-bearing — do not change without updating the playtest tooling" banner and
  links back to the design report §3.

## Constitution Check
- **I Specify-before-implement:** the note is authored against a written spec; it introduces no code.
- **II Structured artifacts are the machine contract:** the `.fsi` files remain authoritative; the note
  is a relied-upon pointer, not a second source of truth.
- **III Public surface:** no surface change; baselines unchanged (FR-005) — the tier-2 invariant.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: The note pins `Playable.Keymap`'s value-set as the command alphabet, citing `Playable.fsi` and naming the Phase 2/Phase 3 dependents.
- PD-002 [AC-002] [FR-002] complete: The note pins the fingerprint `'world -> 'f` as the canonical visited-set key, citing `Driver.identityFingerprint`/projection and naming the Phase 2 dependent.
- PD-003 [AC-003] [FR-003] complete: The note pins ambient-free determinism as enforced by `DependencyTests`, naming the Phase 1/Phase 3 dependents.
- PD-004 [AC-004] [FR-004] complete: The note pins FsCheck as an already-referenced test dependency, establishing Phase 3 needs no new package.
- PD-005 [AC-005] [FR-005] complete: The work item changes no surface; the surface baselines and published `.fsi` set are byte-identical before and after (audit-only).

## Contract Impact
- PC-001 [PD-001] [PD-002] [PD-003] [PD-004] [PD-005] command report: No public surface, schema, command, or artifact-layout change. A new reference doc is added under `docs/reference/`; no `.fsi`, no baseline, no `.fsproj` changes.

## Verification Obligations
- VO-001 [PD-001] [PD-002] [PD-003] [PD-004] [PC-001] docConsistency: The note `docs/reference/playtest-tooling-foundations.md` exists and pins all four primitives, each naming a real shipped surface (`Playable.Keymap`, `Driver.identityFingerprint`, `DependencyTests`, the FsCheck `PackageReference`) and its downstream Phase dependent — checked by reading the note against the cited `.fsi`/test/`.fsproj`.
- VO-002 [PD-005] [PC-001] baselineUnchanged: `git diff` over `readiness/surface-baselines/` and every published `.fsi` shows no change introduced by this work item, and the harness test suite stays green (40/40) — the audit-only guarantee for FR-005.

## Migration Posture
- PM-001 [PC-001] diagnoseOnly: Plan schemaVersion 1 is accepted. Nothing is removed or renamed; no consumer migration is required and no baseline is migrated.

## Generated View Impact
- GV-001 [PD-001] [PD-005] workModel: because this work item authors only a documentation note and touches no `.fsi`, the sole generated view affected is `readiness/008-playtest-foundations-contract/work-model.json` — it must reflect all five FR→PD→VO chains once verify/ship build it, and no surface-baseline view changes (FR-005). A later edit that adds or drops a PD-###/VO-### restales the work model until `fsgg-sdd refresh` re-runs; the surface-baseline views under `readiness/surface-baselines/` stay untouched.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.
- This is Phase 0 of the roadmap in `docs/reports/2026-07-19-headless-playtest-authoring-machinery-design.md`; it deliberately builds nothing so Phases 1–6 specify against a fixed, documented base.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 008-playtest-foundations-contract`.
