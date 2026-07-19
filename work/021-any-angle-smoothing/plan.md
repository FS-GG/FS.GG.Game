---
schemaVersion: 1
workId: 021-any-angle-smoothing
title: Any-Angle Path Smoothing
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/021-any-angle-smoothing/spec.md
sourceClarifications: work/021-any-angle-smoothing/clarifications.md
sourceChecklist: work/021-any-angle-smoothing/checklist.md
publicOrToolFacingImpact: true
---

# Any-Angle Path Smoothing Plan

Prose status: planned

## Source Snapshot
- spec: work/021-any-angle-smoothing/spec.md sha256:c331a9faaaeb26478c13f06aaae00c9eaf231a3246eddb62138578063e849c34 schemaVersion:1
- clarifications: work/021-any-angle-smoothing/clarifications.md sha256:6a8bf28401ce614005ea2074ddf265335da638f340bc32c917f32b17341a287d schemaVersion:1
- checklist: work/021-any-angle-smoothing/checklist.md sha256:8915bf4c28e245493a78e468d769bb0a93bd4dc4109671324899586b80d1d0f3 schemaVersion:1

## Technical Context
F# / `FS.GG.Game.Core.Pathfinding`. `smooth` is a pure post-hoc function over a finished path; it
takes a caller-supplied `losClear` (canonically `Los.lineOfSight isTransparent`) and never touches
the search. No new module.

## Constitution Check
- III (public surface): declare `Pathfinding.smooth` in the `.fsi`; update both baselines.
- V (MUE / pure): `smooth` is a pure function.
- VI (test evidence): the property suite fails before, passes after.

## Plan Scope
- Add one public function `Pathfinding.smooth`. Requirement count: 5. Clarification decision count: 3.
  Checklist result count: 5.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: `smooth: losClear:(Cell -> Cell -> bool) -> path:Cell list -> Cell list`. Greedy keep-farthest-visible string-pull over an array of the waypoints: keep index 0; advance `next` while `losClear anchor path.[next]`; when it breaks, commit `path.[next-1]` as the new anchor; always append the final cell. Returns a subsequence keeping first and last (DEC-001/DEC-002).
- PD-002 [AC-002] [FR-002] complete: Every committed segment is `(anchor, path.[next-1])` with `losClear anchor path.[next-1]` proven true just before the break (or the adjacency of the last step), so every consecutive pair in the result is LOS-clear.
- PD-003 [AC-003] [FR-003] complete: Only original waypoints are kept, so `|result| <= |path|`; when `losClear start c` holds for every `c` (e.g. a straight clear path) the scan advances to the end and the result is `[start; goal]`.
- PD-004 [AC-004] [FR-004] complete: Totality — `[]` -> `[]`, `[x]` -> `[x]`. Termination — `next` strictly increases and adjacent waypoints are always mutually visible (a valid path step), so a break at `next` commits `path.[next-1]` with the new anchor `< next` and progress continues. Deterministic: a pure scan over the integer predicate.
- PD-005 [AC-005] [FR-005] complete: With `losClear = Los.lineOfSight isTransparent`, each kept segment is LOS-clear over that transparency, so straight movement between consecutive kept cells never crosses a non-transparent cell — the smoothing is safe. A test uses the real `Los.lineOfSight` against a wall.

## Contract Impact
- PC-001 [PD-001] command report: Tier-1 public surface addition — `Pathfinding.smooth`. Both surface baselines gain the one member with the `.fsi` and tests; the drift gate stays green. The `.fsi` is the contract (no `contracts/` dir). No existing signature changes.

## Verification Obligations
- VO-001 [PD-001] [PD-002] [PC-001] semanticTest: a property suite over random paths/obstacles — subsequence + endpoint preservation, every consecutive pair LOS-clear, never-longer, straight-path collapse, and (with real `Los.lineOfSight`) safe segments around a wall. Fails before, passes after; build clean under warnings-as-errors; both baselines regenerated and committed.
- VO-002 [PD-004] semanticTest: a named `determinism` golden asserting `smooth` is byte-identical across runs, plus the totality (empty/single) cases.

## Migration Posture
- PM-001 [PC-001] diagnoseOnly: Plan schemaVersion 1 accepted; purely additive, so no consumer migration.

## Generated View Impact
- GV-001 [PD-001] workModel: `readiness/021-any-angle-smoothing/work-model.json` refreshes from current plan sources or reports `staleGeneratedView`.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 021-any-angle-smoothing`.
