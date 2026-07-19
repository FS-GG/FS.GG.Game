---
schemaVersion: 1
workId: 026-bucketed-pq-gate
stage: clarify
sourceSpec: work/026-bucketed-pq-gate/spec.md
---

# Clarifications

## Source Specification
- work/026-bucketed-pq-gate/spec.md

## Clarification Questions
- **CQ-001** (AMB-001): Implement the IntBucketQueue, or record the gated decision + benchmark?
- **CQ-002** (AMB-002): Where is the decision recorded?

## Answers
- CQ-001 → Record the gated decision, backed by a benchmark. The roadmap makes 3.4 conditional ("taken only if measurement justifies it") and the decisive fact is structural: the bucketed PQ needs a bounded small f-range, but the library's astar runs over an unbounded integer coordinate space with int64 f, so O(1) integer buckets do not cleanly apply and would not preserve the O(1) claim. Implementing a speculative swap the profile does not justify would violate the roadmap's own gate (resolves AMB-001).
- CQ-002 → A note under docs/reference/ (a durable, discoverable record), mirroring the repo's existing reference-note pattern; the work item cross-links it (resolves AMB-002).

## Decisions
- **DEC-001** [CQ-001] [AMB:AMB-001] [FR-001] [FR-002]: The bucketed-PQ swap is DEFERRED per its profiling gate; this item delivers a benchmark smoke test and a recorded decision note, not an IntBucketQueue implementation.
- **DEC-002** [CQ-002] [AMB:AMB-002] [FR-002]: The decision is recorded in a docs/reference/ note.

## Accepted Deferrals
- None — the ambiguities are resolved above. (The bucketed-PQ implementation itself is the subject of the recorded decision, not a lifecycle deferral obligation of this item.)

## Remaining Ambiguity
- None. AMB-001 and AMB-002 are resolved by DEC-001 and DEC-002.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 026-bucketed-pq-gate`.
