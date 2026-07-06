---
schemaVersion: 1
workId: 002-game-render-scene-adapter
title: Game Render Scene Adapter
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Game Render Scene Adapter Specification

Prose status: specified

## User Value
Rendering-side consumers of a headless Game.Core sim need to draw simulation state without hand-mapping every sim primitive to a Scene drawable, so a sim can be visualised through the FS.GG.UI render edge while Game.Core stays BCL-only and reaches up to nothing.

## Scope
- SB-001: Add a new packable FS.GG.Game.Render library that depends on FS.GG.Game.Core and FS.GG.UI.Scene and purely projects Game.Core Point/Rect/Cell and pathfinding Cell-list results onto FS.GG.UI.Scene drawables; deterministic, no Skia, no mutation. The Game.Core -> FS.GG.UI.Scene dependency edge (adapter reaches UP to Rendering) is established here.

## Non-Goals
- SB-002: Do not implement later lifecycle commands or Governance enforcement in this specification.

## User Stories
- US-001 (P1): As a maintainer, I can specify Game Render Scene Adapter after chartering the work item.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given a chartered work item, when specify runs with intent, then spec.md is created with stable ids.

## Functional Requirements
- FR-001: Adapter projection functions map Game.Core.Point/Rect to Scene.Point/Rect by identity, project a discrete Game.Core.Cell to a Scene.Rect via a cellSize scale (integer-logic to float-presentation), and project an astar/bfs Cell-list route to a Scene polyline through cell centres; an Expecto suite asserts each projection is deterministic (fixed input yields a byte-identical Scene) and geometrically correct. (Stories: US-001; Acceptance: AC-001)

## Ambiguities
No material ambiguities recorded.

## Public Or Tool-Facing Impact
- This specification is an SDD lifecycle artifact and command-report contract input.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 002-game-render-scene-adapter`.
