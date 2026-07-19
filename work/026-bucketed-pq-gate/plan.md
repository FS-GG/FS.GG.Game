---
schemaVersion: 1
workId: 026-bucketed-pq-gate
title: Bucketed Priority Queue — Profiling Gate Decision
stage: plan
changeTier: tier2
status: planned
sourceSpec: work/026-bucketed-pq-gate/spec.md
sourceClarifications: work/026-bucketed-pq-gate/clarifications.md
sourceChecklist: work/026-bucketed-pq-gate/checklist.md
publicOrToolFacingImpact: true
---

# Bucketed Priority Queue — Profiling Gate Decision Plan

Prose status: planned

## Source Snapshot
- spec: work/026-bucketed-pq-gate/spec.md sha256:14e9a82c8534eaa1979e1935caffcbdb3b6ff13d8c5cfbadd2c42e143ac474ba schemaVersion:1
- clarifications: work/026-bucketed-pq-gate/clarifications.md sha256:d4391473954e3b3690f72c9ac977fbb58935567df93ebd6594962b5a39100495 schemaVersion:1
- checklist: work/026-bucketed-pq-gate/checklist.md sha256:694b8fe2d91eb1c3a45e046325c14e03277070dd0e68de9188a10c3272ea453a schemaVersion:1

## Technical Context
No production code change. A benchmark smoke test in `Game.Core.Tests` and a decision note under
`docs/reference/`. The `Pathfinding` surface stays byte-identical.

## Constitution Check
- II (structured decision recorded): the note is the durable record.
- VI (test evidence): the benchmark exercises `astar` on a large search.
- No silent output changes: `Pathfinding` surface/baselines unchanged.

## Plan Scope
- Add a benchmark test + a docs/reference note. Requirement count: 3. Clarification decision count: 2.
  Checklist result count: 3. Tier 2 (no public surface change).

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: a benchmark test runs `astar` on a large open grid (e.g. 120x120, EightWay) and asserts an optimal-cost path within `maxVisited`, repeated enough to show the immutable-Set frontier is adequate — no timing assertion (flaky), a throughput/correctness smoke.
- PD-002 [AC-002] [FR-002] complete: a note `docs/reference/bucketed-pq-gate.md` records the profiling-gated deferral: the bucketed PQ needs a bounded small f-range, but astar's int64 f over an unbounded coordinate space is not bounded, so O(1) buckets do not apply and would not preserve determinism/O(1); the frontier is not the profiled bottleneck (neighbour generation + isWalkable dominate a maxVisited-bounded search). Follow-on: revisit only if a bounded-range workload profile flags the frontier.
- PD-003 [AC-003] [FR-003] complete: no `.fsi` or baseline change; a surface-baseline check confirms byte-identity.

## Contract Impact
- PC-001 [PD-003] command report: No public surface change — only a test and a docs/reference note are added; `Pathfinding`'s `.fsi` and both surface baselines are byte-identical. The drift gate shows no change.

## Verification Obligations
- VO-001 [PD-001] [PD-002] semanticTest: the benchmark test passes (astar solves the large search optimally within budget); the decision note exists and records the gated deferral + rationale + follow-on. Build clean under warnings-as-errors.
- VO-002 [PD-003] semanticTest: baselineUnchanged — the surface baselines and every published `.fsi` are byte-identical (no such paths in git status for Pathfinding surface).

## Migration Posture
- PM-001 [PC-001] diagnoseOnly: nothing removed or renamed; no consumer migration.

## Generated View Impact
- GV-001 [PD-001] workModel: `readiness/026-bucketed-pq-gate/work-model.json` refreshes or reports `staleGeneratedView`.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 026-bucketed-pq-gate`.
