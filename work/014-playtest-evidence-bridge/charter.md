---
schemaVersion: 1
workId: 014-playtest-evidence-bridge
title: Playtest CLI evidence bridge (emit-evidence)
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

# Playtest CLI evidence bridge (emit-evidence) Charter

## Identity
Phase 6 (the final phase) of the headless-playtest-authoring-machinery roadmap
(`docs/reports/2026-07-19-headless-playtest-authoring-machinery-design.md`, §4.5, §6). Extends the
`fsgg-playtest` CLI with **`emit-evidence`**: given the gameplay-FR manifest, a proof report, and a
TRX from the playtest run, it generates the per-FR `evidence.yml` rows — `result: pass`,
**`synthetic: false`**, `requirementRefs`, and the TRX `observedRun` receipt — that make the ADR-0048
gate green. Crucially, a GP whose proof trace is `Origin.Synthetic` is emitted `synthetic: true`
(disclosed, non-satisfying), so the **tool**, not the author, enforces the satisfaction rule. This is
the piece that makes "the obligation chain is green" a *generated* fact.

## Principles
- **The tool enforces the satisfaction rule.** Only an `InputDriven` proof whose test passed is
  emitted `result: pass ∧ synthetic: false`; a `Synthetic` proof is emitted `synthetic: true` and can
  never satisfy — the author cannot launder a synthetic stand-in through this bridge.
- **Fail closed (#266).** A GP with a missing proof, no passing test, or a malformed/unreadable TRX is
  emitted as not-satisfying (`missing`/`fail`), never a silent pass.
- **Faithful `Origin` → `synthetic:` mapping.** The care of the phase is reading provenance and the
  TRX receipt exactly, so the emitted `evidence.yml` is what `fsgg-sdd verify` reads.
- **Still a separate tool.** `emit-evidence` writes an authored artifact the SDD lifecycle then owns;
  it does not evaluate the gate itself.

## Scope Boundaries
- **In:** an `emit-evidence` subcommand on `FS.GG.Playtest.Cli` (manifest + proof report + TRX →
  `evidence.yml` rows with the observed-run receipt and the faithful `Origin` → `synthetic:` mapping);
  a TRX reader; tests over the emitted grammar and the satisfaction-rule mapping. Dogfood on a
  reference manifest.
- **Out:** running `fsgg-sdd verify` itself (the CLI emits the artifact; SDD owns the decision); any
  change to `Game.Core`/`Game.Harness`; reflecting over a live assembly (the proof report + TRX are the
  deterministic inputs).

## Policy Pointers
- Honors constitution I (specify-before-implement), II (structured artifacts — the emitted evidence is
  the machine contract), VI (test evidence), VIII (safe failure — fail-closed, synthetic-refusing).
- **Tier 1:** extends the tool's command surface with `emit-evidence`; the command contract, tests, and
  docs land together (the tool is an app, so no `.fsi` baseline).

## Lifecycle Notes
- Depends on Phase 5 (the manifest + proof report) and the shipped `fsgg-sdd evidence` grammar.
- Next lifecycle action: `fsgg-sdd specify --work 014-playtest-evidence-bridge`.
