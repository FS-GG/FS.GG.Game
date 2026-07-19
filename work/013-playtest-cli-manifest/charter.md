---
schemaVersion: 1
workId: 013-playtest-cli-manifest
title: Playtest CLI manifest scaffolding and coverage lint
stage: charter
changeTier: tier1
status: chartered
policyPointers:
  - .fsgg/sdd.yml
  - .fsgg/agents.yml
  - .fsgg/policy.yml
  - .fsgg/capabilities.yml
  - .fsgg/tooling.yml
---

# Playtest CLI manifest scaffolding and coverage lint Charter

## Identity
Phase 5 of the headless-playtest-authoring-machinery roadmap
(`docs/reports/2026-07-19-headless-playtest-authoring-machinery-design.md`, §4.4, §5, §6). The first
piece that lives in a **separate tool**, not the pure package: a new `fsgg-playtest` CLI
(`FS.GG.Playtest.Cli`) with two subcommands — `scaffold-manifest`, which reads a TestSpec's §14
acceptance criteria and emits one `GP-###` gameplay-FR stub per AC (the exact shape WI-7 hand-authored:
`Id`, `gameplay` facet, `CoversAc`, `Summary`), and `coverage-lint`, the completeness critic that,
given a manifest and a proof report, reports which cited ACs have a backing `InputDriven` proof and
which are still stubs.

## Principles
- **Separate tool, not the package.** The corpus/evidence tooling reads files, so it belongs in an
  `fsgg-playtest` CLI, keeping `FS.GG.Game.Harness` a pure Core+BCL leaf (the design's capability-vs-gate
  split). The CLI does no game simulation.
- **Fail closed (#266).** A `coverage-lint` that cannot see a proof reports the AC as **uncovered**,
  not covered; a `synthetic` proof does not cover; an unreadable manifest/report is an error, never a
  confident pass. A cited AC with no `InputDriven` proof fails the lint with a non-zero exit.
- **Deterministic, git-friendly artifacts.** The manifest is a stable, line-based, round-trippable
  format so a diff is meaningful and a scaffold is idempotent.

## Scope Boundaries
- **In:** a new `FS.GG.Playtest.Cli` project (Exe) with `scaffold-manifest` (TestSpec §14 → `GP-###`
  manifest) and `coverage-lint` (manifest + proof report → covered/uncovered ACs, fail-closed exit
  code, plus an advisory completeness gap vs the spec's full AC set); a `Playtest.Cli.Tests` project;
  both wired into the solution. Dogfood on `snake.md`/`pong.md` and the WI-7 Pong manifest.
- **Out (later phase):** `emit-evidence` (Phase 6 — the evidence bridge to `fsgg-sdd`); any change to
  `Game.Core`/`Game.Harness`; running or reflecting over the test assembly (the proof report is the
  input, kept deterministic).

## Policy Pointers
- Honors constitution I (specify-before-implement), II (structured artifacts are the machine contract —
  the manifest), VI (test evidence — dogfooded on the corpus), VIII (safe failure — fail-closed lint).
- **Tier 1:** introduces a new tool + command surface (the CLI's subcommands, flags, exit codes), so
  the command contract, tests, and docs land together. The tool is an app (not a packaged library), so
  its contract is its CLI, verified by tests, not an `.fsi` surface baseline.

## Lifecycle Notes
- Builds on the `GameplayFr` shape WI-7 established (`tests/Game.Harness.Tests/ReferenceProof.fs`).
- Next lifecycle action: `fsgg-sdd specify --work 013-playtest-cli-manifest`.
