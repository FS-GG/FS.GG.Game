---
schemaVersion: 1
workId: 028-mapgen-cellular-caves
title: MapGen Cellular-Automata Caves
stage: clarify
changeTier: tier1
status: needsAnswers
sourceSpec: work/028-mapgen-cellular-caves/spec.md
publicOrToolFacingImpact: true
---

# MapGen Cellular-Automata Caves Clarifications

## Source Specification
- work/028-mapgen-cellular-caves/spec.md

## Clarification Questions
- **CQ-001** (AMB-001): Does `CaveParams` carry its own `Neighbourhood`, or is smoothing fixed at Moore-8?
- **CQ-002** (AMB-002): Is the wall border force-set, or only implied by out-of-bounds-as-wall smoothing?

## Answers
- CQ-001 → `CaveParams` carries a single `Neighbourhood` used for BOTH the connectivity of the final cavern
  and the region check. The **smoothing** neighbourhood is always the 8-cell Moore neighbourhood — that is
  what the 4-5 rule is defined over (5 of 8), and making it 4-connected would change the rule, not a knob.
  So `CaveParams.Neighbourhood` governs `connect`/`regions`; the CA rule is Moore-8 by definition. Keeping
  one explicit `Neighbourhood` on the params (no hidden default) matches M1 DEC-001 (resolves AMB-001).
- CQ-002 → Force-set the border to `Wall` as the final step, AND treat out-of-bounds as wall during
  smoothing. Out-of-bounds-as-wall biases the edges toward wall but does not guarantee a solid border (a
  large open interior can leave an edge cell floor); a game needs a hard wall boundary so the cavern is
  enclosed. So smoothing counts OOB as wall (FR-002) and a final pass forces every border cell to `Wall`
  (FR-001) — the two together give a guaranteed enclosure (resolves AMB-002).

## Decisions
- **DEC-001** [CQ-001] [AMB:AMB-001] [FR-001] [FR-003]: `CaveParams` carries one explicit `Neighbourhood`
  for `connect`/`regions`; CA smoothing is always Moore-8.
- **DEC-002** [CQ-002] [AMB:AMB-002] [FR-001] [FR-002]: Smoothing treats out-of-bounds as wall; a final step
  force-sets the border to `Wall`, guaranteeing an enclosed cavern.

## Accepted Deferrals
- None — both ambiguities are resolved above.

## Remaining Ambiguity
- None. AMB-001 and AMB-002 are resolved by DEC-001 and DEC-002.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 028-mapgen-cellular-caves`.
