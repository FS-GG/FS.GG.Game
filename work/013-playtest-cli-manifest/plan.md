---
schemaVersion: 1
workId: 013-playtest-cli-manifest
title: Playtest CLI Manifest And Coverage Lint
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/013-playtest-cli-manifest/spec.md
sourceClarifications: work/013-playtest-cli-manifest/clarifications.md
sourceChecklist: work/013-playtest-cli-manifest/checklist.md
publicOrToolFacingImpact: true
---

# Playtest CLI Manifest And Coverage Lint Plan

Prose status: planned

## Source Snapshot
- spec: work/013-playtest-cli-manifest/spec.md sha256:bcf5c4ace87fefc6d61ace447b3eb5980d4dd7d31de9cf17c56c54dd49863dc7 schemaVersion:1
- clarifications: work/013-playtest-cli-manifest/clarifications.md sha256:42980a6663d537ed9826741615c36cfc19dcf5b9c2d002c7f128d231dab92e7f schemaVersion:1
- checklist: work/013-playtest-cli-manifest/checklist.md sha256:567b06f9b10b97273d2130280f0eaca92838d7dc8d60dcf3418f6797f62e7850 schemaVersion:1

## Plan Scope
- Add a new Exe project `src/Playtest.Cli/FS.GG.Playtest.Cli.fsproj` (assembly/tool name
  `fsgg-playtest`) and a test project `tests/Playtest.Cli.Tests/`. Wire both into `FS.GG.Game.slnx`.
- Requirement count: 8. Clarification decision count: 2. Checklist result count: 8.

## Technical Context
F# 10 / .NET 10, BCL only (no game-simulation reference; no external serialization library). Pure
string/file processing with a small hand-rolled parser (DEC-001). The tool follows the same
Model-Update-Effect posture: parsing/coverage are pure functions; I/O (read files, write manifest,
`stdout`, exit code) is at the `Program` edge.

## Design
### Modules (`src/Playtest.Cli/`)
- `Manifest.fs` — `type GameplayFr = { Id; Facet; Summary; CoversAc: int list }`; `render`/`parse` for
  the line format `GP-### | gameplay | covers=<csv> | <summary>` (comment/blank tolerant), round-tripping
  (FR-002, DEC-001).
- `TestSpec.fs` — `parseSection14 : lines -> (int * string) list`: find the `## 14. Acceptance` heading,
  read to the next `## ` heading, and extract each `^\s*(\d+)\.\s+\*\*(title)\.\*\*` as `(number, title)`.
  `scaffold : acs -> GameplayFr list` emits `GP-00n` per AC with `CoversAc = [n]` and `Summary = title`
  (FR-001).
- `Proofs.fs` — `type Provenance = InputDriven | Synthetic | Missing`; `parse : lines -> Map<string,
  Provenance>` for the `GP-### <provenance>` report (DEC-001).
- `Coverage.fs` — `lint : manifest -> proofs -> specAcs option -> LintReport`. An AC is covered iff some
  citing GP is `InputDriven`; `citedUncovered` = cited ACs not so covered (fail-closed: absent/`Synthetic`
  → not covered) (FR-003, FR-004); `specGap` (with `--spec`) = spec §14 ACs no GP cites, advisory (FR-005).
- `Program.fs` — arg parse and dispatch `scaffold-manifest` / `coverage-lint`; a malformed/missing input
  prints an error and returns a non-zero exit (FR-006); `coverage-lint` returns non-zero iff
  `citedUncovered` is non-empty (FR-004); `--spec` gap is printed but does not by itself fail (DEC-002).

### Test project (`tests/Playtest.Cli.Tests/`)
- Expecto over the pure modules: §14 parsing/scaffolding on the real `snake.md` (17) and `pong.md` (19);
  manifest round-trip; coverage over a WI-7-style Pong manifest with all-`inputDriven` proofs → fully
  covered; a removed proof → the AC uncovered and a failing lint; a `synthetic` proof → not covering;
  the `--spec` completeness gap; malformed-input errors.

## Constitution Check
- **II Structured artifacts:** the manifest is the machine contract; its format is stable and tested.
- **IV Idiomatic simplicity:** a thin BCL-only CLI, hand-rolled parser, no external deps.
- **V Model-Update-Effect:** parsing/coverage pure; I/O and exit codes at the `Program` edge.
- **VIII Safe failure:** fail-closed lint and explicit error exits (never a confident empty pass).

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: `TestSpec.parseSection14` extracts the numbered §14 ACs and `scaffold` emits one `GP-00n` per AC with the `gameplay` facet, `CoversAc = [n]`, and the AC title as `Summary`.
- PD-002 [AC-002] [FR-002] [DEC-001] complete: `Manifest.render`/`parse` implement the line format `GP-### | gameplay | covers=<csv> | <summary>` and round-trip to identical records.
- PD-003 [AC-003] [FR-003] complete: `Coverage.lint` marks a cited AC covered iff a citing GP is `InputDriven` in the proof report, and lists covered/uncovered cited ACs.
- PD-004 [AC-004] [FR-004] [DEC-002] complete: a cited AC whose covering GPs are all `Synthetic`/absent/unproven is uncovered; `Program` returns non-zero when any cited AC is uncovered; an unseen or synthetic proof never covers.
- PD-005 [AC-005] [FR-005] [DEC-002] complete: with `--spec`, `lint` also reports the spec §14 ACs no GP cites (the completeness gap) as advisory, without by itself failing.
- PD-006 [AC-006] [FR-006] complete: a missing/unreadable/malformed manifest, proof report, or spec makes the subcommand print an error and exit non-zero.
- PD-007 [AC-007] [FR-007] complete: dogfood proves 17 stubs for `snake.md`, 19 for `pong.md`, the WI-7 Pong manifest fully covered under `inputDriven`, and a removed proof as an uncovered AC failing the lint.
- PD-008 [AC-008] [FR-008] complete: the CLI is a separate `FS.GG.Playtest.Cli` Exe wired into `FS.GG.Game.slnx` with its test project; it references no game-simulation code for these subcommands.

## Contract Impact
- PC-001 [PD-001] [PD-003] [PD-004] [PD-005] [PD-006] command report: New tool + command surface — `fsgg-playtest scaffold-manifest` and `coverage-lint` with their flags and exit codes. The contract is the CLI, verified by tests; the tool is an app (`IsPackable=false`), so no `.fsi` surface baseline is added.
- PC-002 [PD-008] command report: `FS.GG.Playtest.Cli` and `Playtest.Cli.Tests` are added to `FS.GG.Game.slnx`; the CLI references no `Game.Core`/`Game.Harness` simulation code for these subcommands (BCL only).

## Verification Obligations
- VO-001 [PD-001] [PD-002] [PD-003] [PC-001] semanticTest: tests prove §14 parse/scaffold (one stub per AC), manifest round-trip, and coverage marking (`inputDriven` covers, cited-AC accounting).
- VO-002 [PD-004] [PD-005] [PD-006] [PD-007] semanticTest: tests prove fail-closed behaviour (removed/synthetic/absent proof → uncovered + non-zero exit), the `--spec` advisory gap, malformed-input errors, and the corpus dogfood (17/19 stubs; WI-7 manifest fully covered).
- VO-003 [PD-008] [PC-002] semanticTest: `dotnet build` of the new projects clean under warnings-as-errors; both wired into the solution; the CLI's referenced assemblies are BCL only (no simulation); full new suite green.

## Migration Posture
- PM-001 [PC-001] diagnoseOnly: Plan schemaVersion 1 accepted. New tool; nothing removed or renamed; no consumer migration and no baseline migration (the tool is an app, not a packaged library).

## Generated View Impact
- GV-001 [PD-001] [PD-007] workModel: the work model (readiness/013-.../work-model.json) must reflect the eight FR→PD→VO chains once verify/ship build it; a plan edit that adds or drops a PD/VO restales it until `fsgg-sdd refresh` re-runs.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.
- This is Phase 5 of `docs/reports/2026-07-19-headless-playtest-authoring-machinery-design.md`; Phase 6
  (`emit-evidence`) extends this same CLI to close the loop to `fsgg-sdd verify`.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 013-playtest-cli-manifest`.
