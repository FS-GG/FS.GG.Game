---
schemaVersion: 1
workId: 606-game-0-14-0-coherent-release
title: Game 0.14.0 coherent runtime release
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Game 0.14.0 coherent runtime release Specification

Prose status: specified

## User Value
Stable-feed consumers can restore the landed additive Journey APIs and defect repairs from supported Game packages.

## Scope
- SB-001: Advance FS.GG.Game.Core, FS.GG.Game.Render, and FS.GG.Game.Harness together from 0.13.0 to stable 0.14.0; prepare one artifact set and explicit post-merge release obligations.

## Non-Goals
- SB-002: Add no new runtime behavior and do not split the coherent version scalar.

## User Stories
- US-001 (P1): As a Game package consumer, I can restore a stable coherent release that contains the Journey coverage surface already present on `main`.
- US-002 (P1): As a release operator, I can prove that Core, Render, and Harness were built once from one source revision and are safe to publish together.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given the locally prepared 0.14.0 packages, when a clean consumer restores them, then it compiles code using `JourneyReceipt.definitionDigest`, `ActionCoverageReport`, and `Journey.checkActionCoverage`.
- AC-002 [US-002] [FR-002]: Given the candidate release revision, when the full release gates run, then build, tests, additive API comparison, pack inventory, and repository metadata checks pass for all three packages at exactly 0.14.0.
- AC-003 [US-002] [FR-003]: Given an accepted merged release-preparation PR, when the release is later cut, then one immutable tag identifies the source and the exact prepared package payloads are pushed to GitHub Packages before the byte-identical payloads are pushed to nuget.org.
- AC-004 [US-002] [FR-004]: Given publication succeeds, when release reconciliation runs, then feed metadata and package bytes are verified before registry state changes or downstream consumer dispatches are accepted.

## Functional Requirements
- FR-001: A clean consumer MUST restore the prepared `FS.GG.Game.Harness` 0.14.0 package (and its coherent Core dependency) and compile `JourneyReceipt.definitionDigest`, `ActionCoverageReport`, and `Journey.checkActionCoverage`. (Stories: US-001; Acceptance: AC-001)
- FR-002: `FS.GG.Game.Core`, `FS.GG.Game.Render`, and `FS.GG.Game.Harness` MUST pack at exactly 0.14.0 from one candidate SHA, with that SHA in each nuspec repository metadata field; the full build/test suite and an additive public-surface comparison against 0.13.0 MUST pass. (Stories: US-002; Acceptance: AC-002)
- FR-003: The producer release workflow MUST gate publication on verification, publish one prepared coherent artifact set to GitHub Packages first and then publish byte-identical payloads to nuget.org; no tag or publication may occur before merge, independent critic pass, and host acceptance. (Stories: US-002; Acceptance: AC-003)
- FR-004: Publication completion MUST require the tag, both feed versions, repository commit metadata, and package byte identity to be verified before registry reconciliation or consumer dispatch is considered complete. (Stories: US-002; Acceptance: AC-004)

## Ambiguities
No material ambiguities recorded.

## Public Or Tool-Facing Impact
- This specification is an SDD lifecycle artifact and command-report contract input.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 606-game-0-14-0-coherent-release`.
