---
schemaVersion: 1
workId: 033-mapcraft-rename
title: Rename fs-gg-mapgen to fs-gg-mapcraft (map construction)
stage: clarify
changeTier: tier1
status: needsAnswers
sourceSpec: work/033-mapcraft-rename/spec.md
publicOrToolFacingImpact: true
---

# Rename fs-gg-mapgen to fs-gg-mapcraft Clarifications

## Source Specification
- work/033-mapcraft-rename/spec.md

## Clarification Questions
- **CQ-001** (AMB-001): Rename the F# `MapGen` module too, or keep it under the `fs-gg-mapcraft` umbrella?
- **CQ-002** (AMB-002): Add analysis sections to the SKILL.md now, or only reframe the scope?

## Answers
- CQ-001 → Keep the F# module named `MapGen`. The rename is about the **skill** (the agent-facing capability
  name), not the code. `MapGen` is genuinely the *generation* module and its name is accurate; the analysis
  code lands in a *separate* `MapAnalysis` module (M8–M12). `fs-gg-mapcraft` is the umbrella skill over both.
  Renaming `MapGen` → e.g. `MapCraft` would churn every call site, the `.fsi`, and both surface baselines for
  no semantic gain, and would wrongly imply the module does analysis. So: skill renamed, modules unchanged
  (resolves AMB-001).
- CQ-002 → Only **reframe the scope** now; analysis sections land per M8–M12 as each ships. Adding empty
  analysis stubs to the SKILL.md in M7 would document a surface that does not exist yet — a reader could copy
  a `MapAnalysis.reachable` example that will not compile against Core until M8. The honest M7 SKILL.md frames
  the produce → analyze → validate pipeline and names the analysis machinery as *forthcoming* (M8–M12), while
  its worked examples stay over the shipped `MapGen` surface (which still typecheck). Each analysis milestone
  adds its own section + example as it lands (resolves AMB-002).

## Decisions
- **DEC-001** [CQ-001] [AMB:AMB-001] [FR-001]: The F# `MapGen` module keeps its name; only the *skill* id is
  renamed. Analysis code lands in a separate `MapAnalysis` module (M8–M12).
- **DEC-002** [CQ-002] [AMB:AMB-002] [FR-002]: M7 reframes the SKILL.md scope to the produce → analyze →
  validate pipeline (generation is one producer; analysis noted as forthcoming M8–M12); analysis sections and
  examples land per milestone. M7's worked examples stay over the shipped `MapGen` surface.

## Accepted Deferrals
- **DEC-003** [AMB:AMB-002] [FR-005]: Updating the cross-repo registry request FS-GG/.github#1355 to the
  `fs-gg-mapcraft` id is a comment on another repo's issue — recorded here and posted, but its *resolution*
  (the registry row) is owned in FS-GG/.github.

## Remaining Ambiguity
- None. AMB-001 and AMB-002 are resolved by DEC-001 through DEC-003.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 033-mapcraft-rename`.
