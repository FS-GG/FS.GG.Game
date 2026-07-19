---
schemaVersion: 1
workId: 013-playtest-cli-manifest
title: Playtest CLI Manifest And Coverage Lint
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Playtest CLI Manifest And Coverage Lint Specification

Prose status: specified

## User Value
A game author scaffolds a gameplay-FR manifest from a TestSpec's §14 acceptance criteria with `fsgg-playtest scaffold-manifest`, and lints which cited ACs have a backing non-synthetic `InputDriven` proof with `fsgg-playtest coverage-lint`, instead of hand-authoring the manifest and eyeballing coverage. The lint is the completeness critic that stops a proof from silently covering 6 of 19 ACs.

## Scope
- SB-001: A new `fsgg-playtest` CLI (`FS.GG.Playtest.Cli`, an Exe) — a separate tool, not the pure package.
- SB-002: `scaffold-manifest --spec <testspec.md>` parses the §14 numbered ACs and emits one `GP-###` gameplay-FR stub per AC (`Id`, `gameplay` facet, `CoversAc`, `Summary`).
- SB-003: `coverage-lint --manifest <m> --proofs <p> [--spec <s>]` reports which cited ACs have an `InputDriven` proof, fails closed, and (with `--spec`) reports the completeness gap against the spec's full AC set.
- SB-004: A `Playtest.Cli.Tests` project; both wired into the solution; dogfood on the corpus and the WI-7 Pong manifest.

## Non-Goals
- SB-010: `emit-evidence` — the evidence bridge to `fsgg-sdd` (Phase 6).
- SB-011: Running or reflecting over the test assembly — the proof report is a deterministic file input.
- SB-012: Any change to `FS.GG.Game.Core` or `FS.GG.Game.Harness`; any game simulation in the CLI.

## User Stories
- US-001 (P1): As a game author, I turn a TestSpec's §14 AC list into a checklist of `GP-###` proofs-to-write with one command.
- US-002 (P1): As a game author, I lint a manifest against a proof report and get a non-zero exit when any cited AC lacks an `InputDriven` proof.
- US-003 (P2): As a game author, I see which of the spec's ACs the manifest does not cite at all, so I know the completeness gap.
- US-004 (P2): As a maintainer, I get a clear error (never a false pass) when a manifest or proof report is unreadable or malformed.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given a TestSpec with N numbered §14 acceptance criteria, when `scaffold-manifest --spec` runs, then it emits exactly N `GP-###` stubs, one per AC, each with the `gameplay` facet, `CoversAc` = that AC's number, and a `Summary` taken from the AC's title.
- AC-002 [US-001] [FR-002]: Given a scaffolded manifest, when it is written and re-parsed, then it round-trips to the same gameplay-FR records (a stable, line-based format).
- AC-003 [US-002] [FR-003]: Given a manifest and a proof report marking each `GP-###` `inputDriven`, `synthetic`, or absent, when `coverage-lint` runs, then an AC is covered iff some GP that cites it is `inputDriven`, and the tool reports covered vs uncovered cited ACs.
- AC-004 [US-002] [FR-004]: Given a cited AC whose only covering GP is `synthetic`, absent from the report, or unproven, when `coverage-lint` runs, then that AC is reported uncovered and the tool exits non-zero — it never treats an unseen or synthetic proof as covering.
- AC-005 [US-003] [FR-005]: Given `coverage-lint --spec`, when it runs, then it additionally reports the spec's §14 AC numbers that no GP in the manifest cites (the completeness gap), as advisory.
- AC-006 [US-004] [FR-006]: Given an unreadable or malformed manifest, proof report, or spec, when a subcommand runs, then it reports an error and exits non-zero — never a confident empty success.
- AC-007 [US-001] [FR-007]: Given the corpus, when `scaffold-manifest` runs on `snake.md` (17 §14 ACs) it emits 17 stubs, and on `pong.md` (19 §14 ACs) it emits 19; and `coverage-lint` reports the WI-7 Pong manifest as fully covered when its GPs are `inputDriven`, and a deliberately-removed proof as an uncovered AC with a non-zero exit.
- AC-008 [US-001] [FR-008]: Given the repository, when the CLI is added, then it is a separate `FS.GG.Playtest.Cli` Exe (not the pure package), references no game-simulation code for these subcommands, and is wired into the solution alongside its test project.

## Functional Requirements
- FR-001: `scaffold-manifest --spec <file>` MUST parse the file's §14 numbered acceptance criteria and emit exactly one `GP-###` gameplay-FR stub per AC, each carrying the `gameplay` facet, `CoversAc` = the AC number, and a `Summary` from the AC title. (covers AC-001)
- FR-002: The manifest MUST be a stable, line-based format that round-trips: writing then re-parsing MUST yield the same gameplay-FR records. (covers AC-002)
- FR-003: `coverage-lint --manifest <m> --proofs <p>` MUST report a cited AC as covered iff some GP citing it is marked `inputDriven` in the proof report, and MUST list covered and uncovered cited ACs. (covers AC-003)
- FR-004: `coverage-lint` MUST fail closed: a cited AC whose covering GPs are all `synthetic`, absent, or unproven MUST be reported uncovered and MUST cause a non-zero exit; an unseen or `synthetic` proof MUST NOT be treated as covering. (covers AC-004)
- FR-005: With `--spec`, `coverage-lint` MUST additionally report, as advisory, the spec's §14 AC numbers not cited by any GP in the manifest (the completeness gap). (covers AC-005)
- FR-006: Every subcommand MUST report an error and exit non-zero on an unreadable or malformed manifest, proof report, or spec — never a confident empty success. (covers AC-006)
- FR-007: `scaffold-manifest` MUST emit 17 stubs for `snake.md` and 19 for `pong.md`, and `coverage-lint` MUST report the WI-7 Pong manifest fully covered under `inputDriven` proofs and a removed proof as an uncovered AC with a non-zero exit. (covers AC-007)
- FR-008: The CLI MUST be a separate `FS.GG.Playtest.Cli` Exe (not the pure package), MUST NOT run game simulation for these subcommands, and MUST be wired into the solution with its test project. (covers AC-008)

## Ambiguities
- AMB-001: The manifest and proof-report file formats (a line-based `GP-### | gameplay | covers=… | summary` manifest and a `GP-### <provenance>` proof report vs a richer serialization).
- AMB-002: Whether "covered" is judged over the ACs the manifest cites, or over the spec's full §14 AC set — bearing on what "fully covered" means for the pass/fail vs the advisory gap.

## Public Or Tool-Facing Impact
- Introduces a new tool and command surface: the `fsgg-playtest` CLI with `scaffold-manifest` and `coverage-lint`. Tier-1 — the command contract, tests, and docs land together; the tool is an app, so its contract is its CLI (verified by tests), not an `.fsi` baseline.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 013-playtest-cli-manifest`.
