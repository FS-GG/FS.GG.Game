---
schemaVersion: 1
workId: 014-playtest-evidence-bridge
title: Playtest Evidence Bridge
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Playtest Evidence Bridge Specification

Prose status: specified

## User Value
A game author generates the per-FR `evidence.yml` rows for a gameplay proof suite from the gameplay-FR manifest, a proof report, and a TRX with `fsgg-playtest emit-evidence`, instead of hand-maintaining evidence — and cannot launder a synthetic proof through it, because the tool maps `Origin.Synthetic` to `synthetic: true`. "The obligation chain is green" becomes a generated fact.

## Scope
- SB-001: An `emit-evidence` subcommand on the existing `fsgg-playtest` CLI.
- SB-002: It reads a manifest, a proof report, and a TRX, and emits SDD `evidence.yml` rows (one per GP) with `requirementRefs`, `result`, `synthetic`, and a TRX `observedRun` receipt.
- SB-003: The tool enforces the satisfaction rule — `result: pass ∧ synthetic: false` only for an `InputDriven` proof whose test passed; a `Synthetic` proof is emitted `synthetic: true`; a missing proof/test is emitted not-satisfying.
- SB-004: Tests over the emitted grammar and the mapping; dogfood on a reference manifest.

## Non-Goals
- SB-010: Running `fsgg-sdd verify` itself — the CLI emits the artifact; SDD owns the decision.
- SB-011: Reflecting over a live test assembly — the proof report and TRX are the deterministic inputs.
- SB-012: Any change to `Game.Core`/`Game.Harness` or to the Phase 5 subcommands.

## User Stories
- US-001 (P1): As a game author, I generate the evidence.yml rows for my gameplay proofs from the manifest + proof report + TRX in one command.
- US-002 (P1): As a reviewer, I trust that a synthetic proof cannot be emitted as satisfying — the tool discloses it as `synthetic: true`.
- US-003 (P2): As a maintainer, I get a fail-closed, not-satisfying row (never a silent pass) for a GP with a missing proof or a failing/absent test.
- US-004 (P2): As a maintainer, I get an error on a malformed or unreadable TRX/manifest/proof report.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given a TRX, when `emit-evidence` reads it, then it extracts the run's pass/fail/skip counts and each test's outcome, and computes the TRX file digest for the `observedRun` receipt.
- AC-002 [US-001] [FR-002]: Given the manifest and proof report, when `emit-evidence` runs, then it emits one `evidence.yml` row per GP with `subject.type: requirement`, `requirementRefs: [GP-###]`, the covered ACs, `kind: verification`, and the TRX `observedRun` receipt.
- AC-003 [US-002] [FR-003]: Given a GP whose proof is `inputDriven` and whose test passed, when `emit-evidence` runs, then its row is `result: pass` and `synthetic: false`; given a GP whose proof is `synthetic`, then its row is `synthetic: true` (disclosed, non-satisfying).
- AC-004 [US-003] [FR-004]: Given a GP with a `missing`/absent proof or no passing test, when `emit-evidence` runs, then its row is `result: missing` (or `fail`) and never `result: pass ∧ synthetic: false` — fail closed.
- AC-005 [US-001] [FR-005]: Given the emitted file, when it is read, then it is valid SDD `evidence.yml` grammar (a `schemaVersion` and an `evidence:` list with the required per-row fields) that `fsgg-sdd verify` can consume.
- AC-006 [US-004] [FR-006]: Given a malformed or unreadable TRX, manifest, or proof report, when `emit-evidence` runs, then it reports an error and exits non-zero — never a confident empty success.
- AC-007 [US-001] [FR-007]: Given a reference manifest with all-`inputDriven` proofs and a passing TRX, when `emit-evidence` runs, then every emitted row satisfies (`pass ∧ ¬synthetic`); and when one GP's proof is forced `synthetic`, then exactly that row is `synthetic: true` and does not satisfy.
- AC-008 [US-001] [FR-008]: Given the CLI, when `emit-evidence` is added, then it is a subcommand of the existing `FS.GG.Playtest.Cli` (still BCL-only, no game simulation) and the Phase 5 subcommands are unchanged.

## Functional Requirements
- FR-001: `emit-evidence` MUST parse a TRX to extract the run's pass/fail/skip counts, each test's outcome, and MUST compute the TRX file digest for the `observedRun` receipt. (covers AC-001)
- FR-002: `emit-evidence` MUST emit one `evidence.yml` row per GP with `subject.type: requirement`, `requirementRefs: [GP-###]`, the GP's covered ACs, `kind: verification`, and the TRX `observedRun` receipt. (covers AC-002)
- FR-003: A GP whose proof is `inputDriven` and whose test passed MUST be emitted `result: pass` and `synthetic: false`; a GP whose proof is `synthetic` MUST be emitted `synthetic: true` (disclosed, non-satisfying). (covers AC-003)
- FR-004: A GP with a `missing`/absent proof or no passing test MUST be emitted not-satisfying (`result: missing` or `fail`) and MUST NOT be emitted `result: pass ∧ synthetic: false` — fail closed. (covers AC-004)
- FR-005: The emitted file MUST be valid SDD `evidence.yml` grammar — a `schemaVersion` and an `evidence:` list whose rows carry the fields `fsgg-sdd verify` reads. (covers AC-005)
- FR-006: `emit-evidence` MUST report an error and exit non-zero on a malformed or unreadable TRX, manifest, or proof report. (covers AC-006)
- FR-007: On a reference manifest with all-`inputDriven` proofs and a passing TRX, every emitted row MUST satisfy; forcing one GP's proof `synthetic` MUST make exactly that row `synthetic: true` and non-satisfying. (covers AC-007)
- FR-008: `emit-evidence` MUST be a subcommand of the existing `FS.GG.Playtest.Cli` (BCL-only, no game simulation), leaving the Phase 5 subcommands unchanged. (covers AC-008)

## Ambiguities
- AMB-001: How a GP maps to its TRX test outcome — by the GP id appearing in the test name (convention) or by an explicit mapping in the proof report.
- AMB-002: The `result` value for a not-satisfying GP — `missing` (no proof/test) vs `fail` (test present but failed).

## Public Or Tool-Facing Impact
- Extends the `fsgg-playtest` command surface with `emit-evidence`. Tier-1 — the command contract, tests, and docs land together; the tool is an app, so its contract is its CLI (verified by tests), not an `.fsi` baseline.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 014-playtest-evidence-bridge`.
