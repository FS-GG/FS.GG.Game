---
schemaVersion: 1
workId: 003-game-product-skill-home
title: Game Product Skill Home
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Game Product Skill Home Specification

Prose status: specified

## User Value
The four game product skills (fs-gg-game-core, fs-gg-audio, fs-gg-persistence, fs-gg-model-swap) belong to the extracted game component, not the render framework; giving FS.GG.Game a producer skill-manifest lets the org registry reconcile their ownership fs-gg-rendering -> fs-gg-game while their bodies stay byte-identical to Rendering's frozen copies.

## Scope
- SB-001: Add template/product-skills/<id>/SKILL.md byte-identical copies of the four game skills, a producer template/skill-manifest/skill-manifest.json declaring them in the ADR-0017 canonical grammar, a generate-skill-manifest.fsx generator that recomputes each SKILL.md digest, and a CI drift gate. No Rendering change (its copies stay frozen); no dotnet-new template package or fragments (deferred to the P6 provider epic).

## Non-Goals
- SB-002: Do not implement later lifecycle commands or Governance enforcement in this specification.

## User Stories
- US-001 (P1): As a maintainer, I can specify Game Product Skill Home after chartering the work item.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given a chartered work item, when specify runs with intent, then spec.md is created with stable ids.

## Functional Requirements
- FR-001: The generated manifest lists exactly the four game skills with sha256 byte-equal to sha256sum of each SKILL.md and materializes-when "profile in [game, sample-pack]"; the generator --check exits 0 on the committed manifest and non-zero on drift, wired as a gate.yml job. (Stories: US-001; Acceptance: AC-001)

## Ambiguities
No material ambiguities recorded.

## Public Or Tool-Facing Impact
- This specification is an SDD lifecycle artifact and command-report contract input.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 003-game-product-skill-home`.
