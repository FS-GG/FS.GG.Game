---
schemaVersion: 1
workId: 014-playtest-evidence-bridge
title: Playtest Evidence Bridge
stage: clarify
changeTier: tier1
status: clarified
sourceSpec: work/014-playtest-evidence-bridge/spec.md
publicOrToolFacingImpact: true
---

# Playtest Evidence Bridge Clarifications

## Source Specification
- work/014-playtest-evidence-bridge/spec.md

## Clarification Questions
- CQ-001 [AMB:AMB-001] blocking: How does a GP map to its TRX test outcome — by the GP id in the test name, or an explicit mapping?
- CQ-002 [AMB:AMB-002] blocking: What `result` does a not-satisfying GP get — `missing` or `fail`?

## Answers
- CQ-001 → By the GP id appearing in a passed TRX test's name (convention), and it is fail-closed. `emit-evidence` collects the set of TRX test names whose outcome is `Passed`; a GP is "tested-green" iff some passed test name contains its id (e.g. a test named `... GP-006 ...`). This needs no extra mapping file and matches how the reference proofs are written; and because a GP with no matching passed test is treated as not-tested (not-satisfying), an author cannot get a satisfying row without a real passing proof.
- CQ-002 → `missing` when there is no proof entry or no passing test for the GP; `fail` when a matching test exists but its TRX outcome is not `Passed`. Both are non-satisfying under the SDD rule (only `pass ∧ ¬synthetic` satisfies), but distinguishing them is more honest — `missing` says "no proof yet", `fail` says "the proof ran and failed". A `synthetic` proof is a third, distinct disposition: `result: pass` with `synthetic: true` (disclosed, non-satisfying).

## Decisions
- **DEC-001** [CQ-001] [AMB:AMB-001] [FR-003] [FR-004]: A GP is "tested-green" iff some `Passed` TRX test name contains the GP id; a GP with no such passed test is not-satisfying (fail-closed). No extra GP→test mapping file is required.
- **DEC-002** [CQ-002] [AMB:AMB-002] [FR-004]: A not-satisfying GP is emitted `result: missing` when it has no proof entry or no passing test, and `result: fail` when a matching test exists but did not pass; a `synthetic` proof is emitted `result: pass` with `synthetic: true`.

## Accepted Deferrals
- None.

## Remaining Ambiguity
- None. AMB-001 and AMB-002 are resolved by DEC-001 and DEC-002 above.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 014-playtest-evidence-bridge`.
