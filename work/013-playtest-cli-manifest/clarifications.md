---
schemaVersion: 1
workId: 013-playtest-cli-manifest
title: Playtest CLI Manifest And Coverage Lint
stage: clarify
changeTier: tier1
status: clarified
sourceSpec: work/013-playtest-cli-manifest/spec.md
publicOrToolFacingImpact: true
---

# Playtest CLI Manifest And Coverage Lint Clarifications

## Source Specification
- work/013-playtest-cli-manifest/spec.md

## Clarification Questions
- CQ-001 [AMB:AMB-001] blocking: What are the manifest and proof-report file formats?
- CQ-002 [AMB:AMB-002] blocking: Is "covered" judged over the ACs the manifest cites, or the spec's full §14 set?

## Answers
- CQ-001 → A stable, line-based, comment-tolerant format, hand-parsed with no external dependency. The **manifest** is one record per line: `GP-### | gameplay | covers=<n[,n...]> | <summary>`; lines starting with `#` and blank lines are ignored. The **proof report** is one line per GP: `GP-### <provenance>`, where provenance is `inputDriven`, `synthetic`, or `missing`. Both round-trip and diff cleanly, and neither needs a YAML/JSON library (which keeps the tool a thin BCL-only Exe).
- CQ-002 → Both, with distinct severities. The pass/fail is judged over the ACs the **manifest cites**: every cited AC must have an `inputDriven`-proven covering GP, or the lint fails closed with a non-zero exit (FR-004). Separately, with `--spec`, the tool reports the **spec's** §14 ACs that no GP cites — the completeness gap — as **advisory** (does not, by itself, change the exit code), because a spec legitimately has ACs out of scope for a given sim (e.g. Pong's float-physics ACs for an integer fixture).

## Decisions
- **DEC-001** [CQ-001] [AMB:AMB-001] [FR-002] [FR-003]: The manifest is line-based `GP-### | gameplay | covers=<csv> | <summary>` (comment/blank tolerant) and the proof report is `GP-### <inputDriven|synthetic|missing>`; both are hand-parsed, no external serialization dependency.
- **DEC-002** [CQ-002] [AMB:AMB-002] [FR-004] [FR-005]: Pass/fail coverage is over the manifest's **cited** ACs (fail closed on any un-`inputDriven` cited AC); the spec-wide completeness gap (uncited §14 ACs) is reported as **advisory** only when `--spec` is supplied and does not by itself fail the lint.

## Accepted Deferrals
- None.

## Remaining Ambiguity
- None. AMB-001 and AMB-002 are resolved by DEC-001 and DEC-002 above.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 013-playtest-cli-manifest`.
