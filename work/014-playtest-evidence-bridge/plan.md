---
schemaVersion: 1
workId: 014-playtest-evidence-bridge
title: Playtest Evidence Bridge
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/014-playtest-evidence-bridge/spec.md
sourceClarifications: work/014-playtest-evidence-bridge/clarifications.md
sourceChecklist: work/014-playtest-evidence-bridge/checklist.md
publicOrToolFacingImpact: true
---

# Playtest Evidence Bridge Plan

Prose status: planned

## Source Snapshot
- spec: work/014-playtest-evidence-bridge/spec.md sha256:26fd8f7990a8ee5c73c6006fb6359ea2a1d78a744d9ea5d0f3fbd62dda923814 schemaVersion:1
- clarifications: work/014-playtest-evidence-bridge/clarifications.md sha256:cb6f62cc1a7129dcd94f7d8318aaf65be41ac09ea0e596092e0299fb36984079 schemaVersion:1
- checklist: work/014-playtest-evidence-bridge/checklist.md sha256:377c0ad5f9ca8142317076edb7fad772824cedd564ee74f35fa05bfd4469fd8d schemaVersion:1

## Plan Scope
- Extend `FS.GG.Playtest.Cli` with a TRX reader (`Trx.fs`), an evidence emitter (`Evidence.fs`), and an
  `emit-evidence` dispatch in `Program.fs`. Add tests to `Playtest.Cli.Tests`.
- Requirement count: 8. Clarification decision count: 2. Checklist result count: 8.

## Technical Context
F# 10 / .NET 10, BCL only (`System.Xml.Linq` for the TRX, `System.Security.Cryptography` for the file
digest). Parsing/emitting are pure; file I/O and exit codes stay at the `Program` edge. Reuses Phase 5's
`Manifest`/`Proofs` modules unchanged.

## Design
### `Trx.fs` (FR-001)
- `type TrxRun = { Passed: int; Failed: int; Skipped: int; PassedTestNames: string list; Digest: string }`.
- `parse (xml: string) : Result<TrxRun,string>`: read `<Counters passed failed .../>` and each
  `<UnitTestResult testName outcome/>` under the TeamTest namespace; collect the names whose outcome is
  `Passed`. `digest (bytes) : string` is the lowercase sha256 hex of the TRX file (for `observedRun`).

### `Evidence.fs` (FR-002, FR-003, FR-004; DEC-001, DEC-002)
- `type Row = { Id; RequirementRefs: string list; CoversAc: int list; Result: string; Synthetic: bool }`.
- `rowFor (run) (proofs) (fr) : Row`: a GP is *tested-green* iff some `run.PassedTestNames` contains
  `fr.Id`. Then, by provenance:
  - `Synthetic` → `result: pass`, `synthetic: true` (disclosed, non-satisfying).
  - `InputDriven` and tested-green → `result: pass`, `synthetic: false` (satisfies).
  - `InputDriven` but not tested-green, or `Missing`/absent → `result: missing` (no proof/test) or
    `fail` (a matching test exists but did not pass) — never `pass ∧ ¬synthetic` (fail closed).
- `render (trxPath) (run) (rows) : string`: a valid SDD `evidence.yml` — `schemaVersion: 1` and an
  `evidence:` list; each row carries `id`, `kind: verification`, `subject { type: requirement, id }`,
  `requirementRefs`, `acceptanceScenarioRefs`, `result`, `synthetic`, and an `observedRun` receipt
  (`source`, `digest`, `outcome`, `passed`/`failed`/`skipped`) (FR-005).

### `Program.fs` — `emit-evidence` (FR-006, FR-008)
- Dispatch `emit-evidence --manifest <m> --proofs <p> --trx <t> [--out <o>]`. Read all inputs (a
  missing/unreadable/malformed one → error + non-zero exit); parse; emit rows; write or print.

### Tests (`Playtest.Cli.Tests`)
- `Trx.parse` on the committed sample TRX; the satisfaction-rule mapping (`inputDriven`+green → pass/¬synthetic;
  `synthetic` → synthetic:true; `missing`/absent/`fail` → not satisfying); the emitted grammar parses and
  encodes `pass ∧ ¬synthetic` only where it should; the reference dogfood (all-satisfying, then one GP
  forced synthetic); malformed-input errors.

## Constitution Check
- **II Structured artifacts:** the emitted `evidence.yml` is the machine contract read by `fsgg-sdd`.
- **V Model-Update-Effect:** parse/map/render pure; I/O and exit codes at the `Program` edge.
- **VIII Safe failure:** fail-closed mapping and error exits; a synthetic proof can never be laundered.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: `Trx.parse` extracts pass/fail/skip counts and each test's outcome under the TeamTest namespace; `Trx.digest` is the sha256 of the TRX file for the receipt.
- PD-002 [AC-002] [FR-002] complete: `Evidence.render` emits one row per GP with `subject.type: requirement`, `requirementRefs: [GP-###]`, the covered ACs, `kind: verification`, and the TRX `observedRun` receipt.
- PD-003 [AC-003] [FR-003] [DEC-001] complete: `Evidence.rowFor` emits `pass ∧ ¬synthetic` only for an `InputDriven` proof whose GP id appears in a passed TRX test name; a `Synthetic` proof is `pass ∧ synthetic: true`.
- PD-004 [AC-004] [FR-004] [DEC-002] complete: a GP with no proof/passing test is `result: missing`; a matching-but-failing test is `result: fail`; neither is `pass ∧ ¬synthetic` (fail closed).
- PD-005 [AC-005] [FR-005] complete: the emitted file is valid SDD `evidence.yml` grammar — `schemaVersion` + an `evidence:` list with per-row `id`/`kind`/`subject`/`requirementRefs`/`result`/`synthetic`/`observedRun`.
- PD-006 [AC-006] [FR-006] complete: a malformed/unreadable TRX, manifest, or proof report makes `emit-evidence` print an error and exit non-zero.
- PD-007 [AC-007] [FR-007] complete: dogfood proves an all-`inputDriven`+passing reference emits all-satisfying rows, and forcing one GP `synthetic` makes exactly that row `synthetic: true` and non-satisfying.
- PD-008 [AC-008] [FR-008] complete: `emit-evidence` is a subcommand of `FS.GG.Playtest.Cli` (BCL-only, no simulation); the Phase 5 subcommands are unchanged.

## Contract Impact
- PC-001 [PD-001] [PD-002] [PD-003] [PD-004] [PD-005] command report: New command surface — `fsgg-playtest emit-evidence` with its flags, exit codes, and the emitted `evidence.yml` grammar. The contract is the CLI + the emitted grammar, verified by tests; the tool is an app, so no `.fsi` baseline.
- PC-002 [PD-008] command report: the CLI's referenced assemblies stay BCL only (`System.Xml.Linq`, `System.Security.Cryptography`); no `Game.Core`/`Game.Harness` reference is added.

## Verification Obligations
- VO-001 [PD-001] [PD-002] [PD-005] [PC-001] semanticTest: tests prove TRX parsing (counts, passed names, digest) and the emitted `evidence.yml` grammar (schemaVersion + evidence rows with the required fields).
- VO-002 [PD-003] [PD-004] [PD-007] semanticTest: tests prove the satisfaction-rule mapping — `inputDriven`+green → `pass ∧ ¬synthetic`; `synthetic` → `synthetic: true`; `missing`/absent → `missing`; failing test → `fail`; and the reference dogfood.
- VO-003 [PD-006] [PD-008] [PC-002] semanticTest: malformed-input errors + non-zero exit; the CLI builds clean under warnings-as-errors, stays BCL-only, and the Phase 5 subcommands are unchanged; full CLI suite green.

## Migration Posture
- PM-001 [PC-001] diagnoseOnly: Plan schemaVersion 1 accepted. Additive subcommand; nothing removed or renamed; no consumer migration and no baseline (the tool is an app).

## Generated View Impact
- GV-001 [PD-002] [PD-003] workModel: the work model (readiness/014-.../work-model.json) must reflect the eight FR→PD→VO chains once verify/ship build it; a plan edit that adds or drops a PD/VO restales it until `fsgg-sdd refresh` re-runs.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.
- This is Phase 6 (final) of `docs/reports/2026-07-19-headless-playtest-authoring-machinery-design.md`;
  it closes the loop from a driven trace to a green SDD gate, with the synthetic shortcut refused end to end.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 014-playtest-evidence-bridge`.
