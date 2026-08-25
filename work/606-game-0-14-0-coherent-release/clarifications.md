---
schemaVersion: 1
workId: 606-game-0-14-0-coherent-release
title: Game 0.14.0 coherent runtime release
stage: clarify
changeTier: tier1
status: clarified
sourceSpec: work/606-game-0-14-0-coherent-release/spec.md
publicOrToolFacingImpact: true
---

# Game 0.14.0 coherent runtime release Clarifications

## Source Specification
- work/606-game-0-14-0-coherent-release/spec.md

## Clarification Questions
- **CQ-001**: Is the release a patch or minor cut?
- **CQ-002**: May the implementation PR create the tag or publish packages?
- **CQ-003**: Must existing consumers immediately change their exact 0.13.0 pins?

## Answers
- CQ-001 → Minor 0.14.0 because the Journey public surface is additive relative to the published 0.13.0 baseline.
- CQ-002 → No. The PR prepares merged release source, evidence, and explicit delivery obligations; tagging and publication happen only after independent review and host acceptance.
- CQ-003 → No. Existing exact pins remain valid; downstream updates are dispatched only after 0.14.0 is verified on both feeds.

## Decisions
- **DEC-001** [CQ-001] [FR-001] [FR-002]: Advance the single repository version scalar to stable 0.14.0 and pack Core, Render, and Harness together.
- **DEC-002** [CQ-002] [FR-003] [FR-004]: Treat tag creation, dual-feed publication, feed verification, registry reconciliation, and consumer dispatch as post-merge obligations; never perform them from the candidate branch.
- **DEC-003** [CQ-003] [FR-004]: Do not modify consumer pins in this work item; dispatch only evidence-backed updates after publication succeeds.

## Accepted Deferrals
No accepted deferrals recorded.

## Remaining Ambiguity
No blocking ambiguity remains.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 606-game-0-14-0-coherent-release`.
