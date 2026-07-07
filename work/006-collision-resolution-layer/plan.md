---
schemaVersion: 1
workId: 006-collision-resolution-layer
title: Collision Resolution Layer
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/006-collision-resolution-layer/spec.md
sourceClarifications: work/006-collision-resolution-layer/clarifications.md
sourceChecklist: work/006-collision-resolution-layer/checklist.md
publicOrToolFacingImpact: true
---

# Collision Resolution Layer Plan

Prose status: planned

## Source Snapshot
- spec: work/006-collision-resolution-layer/spec.md sha256:7d8b37f72039fe325e4b89c2b2ea358e9336b8f3f9868287c28f0f0fc7794e28 schemaVersion:1
- clarifications: work/006-collision-resolution-layer/clarifications.md sha256:6005e816e6db9900e7330b01815ea57564f4b2a6812edc00cac18c79cb654bdd schemaVersion:1
- checklist: work/006-collision-resolution-layer/checklist.md sha256:fa7b52fd98ac4dd8dfff29000536ce536aaafce7804f57a7b1a03f03c546b55e schemaVersion:1

## Plan Scope
- Add a new `FS.GG.Game.Core.Resolution` module (`Resolution.fs`/`.fsi`) — the arcade/kinematic
  response layer consuming the detection `Contact` and the grid `Cell`, producing transforms. Pure,
  total, deterministic; response is a layer separate from detection (`Resolution` never calls
  `Geometry`). Reuses `Contact`/`Point`/`Cell` — no new types.
- Requirement count: 5. Clarification decision count: 4. Checklist result count: 5.

## Design
- New module `Resolution` in `src/Game.Core/Resolution.fs` + `.fsi`, added to the `.fsproj` compile
  order after `Geometry` (it consumes `Contact`) and after `Pathfinding` (it consumes `Cell`).
- `pushOut (position: Point) (contact: Contact) : Point` — `position − Normal × Depth` (DEC-003);
  a zero-`Depth` contact returns `position` unchanged. No slop (caller concern).
- `slide (velocity: Point) (normal: Point) : Point` — `velocity − (velocity · normal) × normal`
  (DEC-002), `normal` assumed unit (DEC-001). Kills the normal component (`result · normal = 0`),
  preserves the tangential. No internal `sqrt`.
- `knockback (start: Cell) (step: Cell) (distance: int) (blocked: Cell → bool) : Cell` — advance from
  `start` by `step` up to `distance` times; before each step test the *next* cell against `blocked`,
  stopping (returning the current cell) on the first blocked next-cell (DEC-004). `distance ≤ 0`
  returns `start`; `start` is assumed free. A tail-recursive fixed-order walk — deterministic.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: `Resolution.pushOut position contact = { X = position.X − Normal.X × Depth; Y = position.Y − Normal.Y × Depth }` — separate the body along the MTV (DEC-003); zero `Depth` ⇒ identity.
- PD-002 [AC-002] [FR-002] complete: `Resolution.slide velocity normal = velocity − (velocity · normal) × normal` with `normal` assumed unit (DEC-001) and the projection unconditional (DEC-002), so `result · normal = 0` and the tangential component is unchanged. No normalization / `sqrt`.
- PD-003 [AC-003] [FR-003] complete: `Resolution.knockback start step distance blocked` walks up to `distance` cells along `step`, testing each next cell against `blocked` and stopping in the last free cell (DEC-004); a tail-recursive fixed-order loop.
- PD-004 [AC-004] [FR-004] complete: Totality — `pushOut`/`slide` are pure arithmetic (NaN flows through, no throw); `knockback` returns `start` on `distance ≤ 0` or an immediately-blocked first next-cell (DEC-004), and the `blocked` predicate is the only effect surface (assumed total by the caller).
- PD-005 [AC-005] [FR-005] complete: Determinism — IEEE arithmetic for `pushOut`/`slide` and a fixed-order integer walk for `knockback`; identical inputs yield byte-identical output. Golden tests carry a `determinism golden` name substring for the `gate.yml` guard.

## Contract Impact
- PC-001 [PD-001] [PD-002] [PD-003] command report: Tier-1 public surface addition to `FS.GG.Game.Core` — a new `Resolution` module (`Resolution.fs`/`.fsi`) with `pushOut`/`slide`/`knockback`. The surface baseline gains `FS.GG.Game.Core.Resolution`; `.fsi`, baseline, and tests land together. The `.fsi` is the contract.
- PC-002 [PD-005] command report: The determinism golden naming convention keeps the `gate.yml` determinism filter covering the new goldens — no workflow change.

## Verification Obligations
- VO-001 [PD-001] [PC-001] semanticTest: Expecto/FsCheck invariants (`pushOut` separates — pushed position no longer overlaps per an AABB oracle; `slide` result · normal = 0 and tangential preserved; `knockback` never lands on a blocked cell and never exceeds `distance`; NaN/`distance ≤ 0`/immediately-blocked totality) that fail before and pass after; a named determinism golden set; `dotnet build` clean under warnings-as-errors; surface baseline regenerated and committed; the full and filtered suites green.

## Migration Posture
- PM-001 [PC-001] diagnoseOnly: Plan schemaVersion 1 is accepted; unsupported plan schemas diagnose before write.

## Generated View Impact
- GV-001 [PD-001] workModel: readiness/006-collision-resolution-layer/work-model.json refreshes from current plan sources or reports staleGeneratedView.
- GV-002 [PC-001] surfaceBaseline: `readiness/surface-baselines/FS.GG.Game.Core.txt` regenerates to include `Resolution`; the surface-baseline-drift gate must stay green.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 006-collision-resolution-layer`.
