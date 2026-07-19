---
schemaVersion: 1
workId: 020-tile-edges-vertices
title: Tile Edges and Vertices
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

# Tile Edges and Vertices Charter

## Identity
Milestone M2 item 2.3 of the Red Blob Games algorithm roadmap
(`docs/reports/2026-07-19-redblobgames-algorithm-incorporation-roadmap.md`, §3.3). Adds a new `Edges`
module that addresses the *edges* and *corners* between tiles, not just the tiles — so features can
live on boundaries (thin walls, doors, fences, rivers). Canonical edge/vertex addressing dedupes the
shared boundary; the tile-part relationships are pure functions; and edge-aware pathfinding lets a
wall on the shared edge block movement between two otherwise-open cells.

## Principles
- **Canonical, deduped addressing.** An edge shared by two cells has ONE representative, so a wall set
  stores it once and blocks both directions; likewise a vertex shared by up to four cells.
- **Relationships are pure `input -> output` functions**, mutually consistent (an edge is in a cell's
  borders iff that cell is one of the edge's cells).
- **Integer, total, deterministic.** Fixed enumeration orders; the edge-aware search inherits the
  same total-order frontier discipline as the square `Pathfinding`.
- **Complements, doesn't replace, the tile model.** No change to `Grids`/`Pathfinding`; the square
  `astar` is untouched — edge-aware search is a new entry point.

## Scope Boundaries
- **In:** a canonical `Edge` and `Vertex` addressing (`edgeBetween`/`edgeOf`/`edgeCells`,
  `borders`/`corners`, `edgeEndpoints`/`vertexCells`/`vertexEdges`, `neighbours`); `isEdgePassable`
  and edge-aware `Edges.bfs`/`Edges.astar` that block walled edges; a tactics-style wall scenario test.
- **Out:** hex edges (depends on 2.1's hex model), diagonal edges, changing the square
  `Pathfinding`/`Grids` surface, and the other M2 item (hex grids 2.1, already shipped).

## Policy Pointers
- Honors constitution I, III (public surface), IV (idiomatic simplicity), V (pure), VI (test evidence).
- Tier 1 (contracted): new `.fsi`, surface baselines, and tests land together; drift gate stays green;
  the new file joins the `FS.GG.Game.Core` compile order.
- Governance pointers are optional compatibility facts, not evaluated by this command.

## Lifecycle Notes
- Determinism/property tests carry a stable filterable name for the `gate.yml` guard.
- Next lifecycle action: `fsgg-sdd specify --work 020-tile-edges-vertices`.
