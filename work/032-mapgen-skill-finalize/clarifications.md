---
schemaVersion: 1
workId: 032-mapgen-skill-finalize
title: MapGen Skill Finalize
stage: clarify
changeTier: tier1
status: needsAnswers
sourceSpec: work/032-mapgen-skill-finalize/spec.md
publicOrToolFacingImpact: true
---

# MapGen Skill Finalize Clarifications

## Source Specification
- work/032-mapgen-skill-finalize/spec.md

## Clarification Questions
- **CQ-001** (AMB-001): Full worked example per family, or a concise API summary?
- **CQ-002** (AMB-002): File the cross-repo GitHub issue now, or record an in-repo documented follow-up?

## Answers
- CQ-001 → A **full worked example per family**, matching the `fs-gg-grids`/`fs-gg-ai` house style the other
  product skills set. A concise API summary would under-serve a scaffolded product author who has only the
  skill to learn from (the `.fsi` is a contract, not a tutorial); the shipped skills deliberately teach the
  *rules* (determinism, fill/router agreement, seed threading) plus a copyable example per capability, and
  `fs-gg-mapgen` should match so it reads as one system with its siblings (resolves AMB-001).
- CQ-002 → Record an **in-repo documented follow-up** the maintainer files, not an auto-filed GitHub issue.
  The `.github` registry row is another repo's change (registry = manifest = bytes), and filing a cross-repo
  issue is a maintainer action with side effects outside this repo — the SDD item records the reconcile as a
  first-class deferral (owner/scope/rationale) and points at the cross-repo-coordination protocol, rather
  than reaching into another repo unprompted. This mirrors how `fs-gg-effects`/`fs-gg-physics` handled the
  same reconcile (resolves AMB-002).

## Decisions
- **DEC-001** [CQ-001] [AMB:AMB-001] [FR-001]: The SKILL.md carries a full worked example per family in the
  `fs-gg-grids`/`fs-gg-ai` house style.
- **DEC-002** [CQ-002] [AMB:AMB-002] [FR-004]: The cross-repo `.github` registry reconcile is a recorded
  in-repo deferral pointing at the cross-repo-coordination protocol; it is not auto-filed.

## Accepted Deferrals
- **DEC-003** [AMB:AMB-002] [FR-004]: The `.github` registry `owner: fs-gg-game` `fs-gg-mapgen` row is
  deferred to a cross-repo follow-up owned by the maintainer — recorded, not dropped.

## Remaining Ambiguity
- None. AMB-001 and AMB-002 are resolved by DEC-001 through DEC-003.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 032-mapgen-skill-finalize`.
