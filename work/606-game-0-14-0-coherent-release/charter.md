---
schemaVersion: 1
workId: 606-game-0-14-0-coherent-release
title: Game 0.14.0 coherent runtime release
stage: charter
changeTier: tier1
status: chartered
policyPointers:
  - .fsgg/sdd.yml
  - .fsgg/agents.yml
  - .fsgg/policy.yml
  - .fsgg/capabilities.yml
  - .fsgg/tooling.yml
---

# Game 0.14.0 coherent runtime release Charter

## Identity
- Work id: `606-game-0-14-0-coherent-release`
- Lifecycle stage: charter
- Status: chartered

## Principles
- Release `FS.GG.Game.Core`, `FS.GG.Game.Render`, and `FS.GG.Game.Harness` as one coherent set from one immutable commit and one version scalar.
- Treat the existing additive Journey API and landed defect repairs as the release payload; this item does not redesign runtime behavior.
- Prepare artifacts once, gate them before publication, and require byte identity across the authenticated GitHub feed and public nuget.org.
- Keep publication, tag creation, registry reconciliation, and consumer dispatch as explicit post-merge obligations; this implementation PR only makes the release source ready.

## Scope Boundaries
- In scope: version policy, release notes/evidence, API compatibility against 0.13.0, full build/test/pack gates, package metadata, clean-consumer Journey surface proof, and release workflow coherence.
- Out of scope: new Journey features, consumer pin changes before publication, publishing from an unmerged feature branch, and independent package versions for Core/Render/Harness.
- Keep SDD lifecycle ownership separate from optional Governance enforcement.

## Policy Pointers
- SDD policy comes from `.fsgg/sdd.yml` and `.fsgg/agents.yml`.
- Governance files are optional compatibility pointers and are not evaluated by this command.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd specify --work 606-game-0-14-0-coherent-release`.
