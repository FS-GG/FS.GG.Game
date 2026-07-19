---
schemaVersion: 1
workId: 021-any-angle-smoothing
stage: clarify
sourceSpec: work/021-any-angle-smoothing/spec.md
---

# Clarifications

## Source Specification
- work/021-any-angle-smoothing/spec.md

## Clarification Questions
- **CQ-001** (AMB-001): Result form — Cell list or Point list?
- **CQ-002** (AMB-002): Smoothing strategy?
- **CQ-003** (AMB-003): LOS source?

## Answers
- CQ-001 → A `Cell list` subsequence of the original waypoints. The kept cells are integer grid cells; straight-line any-angle movement between them is the caller's render concern. Returning cells keeps `smooth` byte-deterministic and out of the float boundary, consistent with the module (resolves AMB-001).
- CQ-002 → A greedy keep-farthest-visible string-pull: from the current anchor advance while LOS to the next waypoint is clear, and commit the last visible waypoint when it breaks. Simple, deterministic, and adjacent waypoints are always mutually visible (a valid step), so it always progresses and terminates. An optimal funnel is deferred (needs a navmesh) (resolves AMB-002).
- CQ-003 → A caller-supplied `losClear : Cell -> Cell -> bool`, so the caller picks the `Los` mode and transparency; the canonical choice is `Los.lineOfSight isTransparent` (resolves AMB-003).

## Decisions
- **DEC-001** [CQ-001] [AMB:AMB-001] [FR-001]: `smooth` returns a `Cell list` subsequence of the input waypoints; no float `Point` output.
- **DEC-002** [CQ-002] [AMB:AMB-002] [FR-002] [FR-003]: A greedy keep-farthest-visible string-pull; adjacency guarantees progress and termination, and every committed segment is LOS-clear.
- **DEC-003** [CQ-003] [AMB:AMB-003] [FR-005]: `losClear` is a caller-supplied predicate (canonically `Los.lineOfSight isTransparent`), so the caller owns the LOS mode.

## Accepted Deferrals
- None — all three ambiguities are resolved above.

## Remaining Ambiguity
- None. AMB-001, AMB-002, and AMB-003 are resolved by DEC-001..DEC-003.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 021-any-angle-smoothing`.
