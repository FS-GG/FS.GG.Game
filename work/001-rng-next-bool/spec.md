---
schemaVersion: 1
workId: 001-rng-next-bool
title: Rng Next Bool
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Rng Next Bool Specification

Prose status: specified

## User Value
Game.Core consumers need a deterministic single-bit random draw without hand-rolling a modulus over nextUInt64, so coin-flip and branch decisions stay byte-identical across runs and platforms.

## Scope
- SB-001: Add Rng.nextBool to FS.GG.Game.Core returning bool and the advanced Rng, threading state like the existing draws; no change to the SplitMix64 core or the existing draw functions.

## Non-Goals
- SB-002: Do not implement later lifecycle commands or Governance enforcement in this specification.

## User Stories
- US-001 (P1): As a maintainer, I can specify Rng Next Bool after chartering the work item.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given a chartered work item, when specify runs with intent, then spec.md is created with stable ids.

## Functional Requirements
- FR-001: nextBool is deterministic (a fixed seed yields a fixed boolean sequence) and threads state (returns the advanced Rng); an Expecto test asserts the first eight draws from a fixed seed match a committed boolean sequence. (Stories: US-001; Acceptance: AC-001)

## Ambiguities
No material ambiguities recorded.

## Public Or Tool-Facing Impact
- This specification is an SDD lifecycle artifact and command-report contract input.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 001-rng-next-bool`.
