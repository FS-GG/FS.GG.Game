---
schemaVersion: 1
workId: 020-tile-edges-vertices
title: Tile Edges and Vertices
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Tile Edges and Vertices Specification

Prose status: specified

## User Value
Game-logic code can address the **edges** and **corners** between tiles — not just the tiles — with
canonical, deduped keys; query the tile-part relationships (a cell's edges/corners, an edge's cells/
endpoints, a vertex's cells/edges) as pure functions; and path with **edge-aware** search so a thin
wall on a shared edge blocks movement between two otherwise-open cells (doors, fences, rivers,
fortified borders). This is the boundary model tactics and roguelike features need and the tile-only
grid cannot express.

## Scope
- SB-001: A new `Edges` module in `FS.GG.Game.Core` — a `Dir` type, a canonical `Edge` value
  (`edgeBetween`/`edgeOf`/`edgeCells`), a canonical `Vertex` value, and the relationships
  `neighbours`/`borders`/`corners`/`edgeEndpoints`/`vertexCells`/`vertexEdges`.
- SB-002: `isEdgePassable walls a b` and edge-aware `Edges.bfs`/`Edges.astar` that never cross a walled
  edge, plus a tactics-style wall scenario test and a full property suite.

## Non-Goals
- SB-003: No hex edges (a separate hex-boundary model), no diagonal edges, no change to the square
  `Pathfinding`/`Grids` surface, and none of the already-shipped M2 item (hex grids 2.1).

## User Stories
- US-001 (P1): As game-logic code, I can store a wall once on the canonical shared edge between two
  cells and have it block movement across that edge in both directions.
- US-002 (P1): As game-logic code, I can query the tile-part relationships as pure, mutually-consistent
  functions to reason about boundaries.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given two orthogonally adjacent cells, when `edgeBetween a b` and `edgeBetween b a` are called, then they return the same canonical `Edge`; non-adjacent cells yield `None`.
- AC-002 [US-002] [FR-002]: Given a cell, when `borders` and `corners` are called, then they return its 4 edges and 4 vertices, each canonical and in a fixed order.
- AC-003 [US-002] [FR-003]: Given an edge, when `edgeCells` is called, then it returns the two cells the edge separates, and `edge` is in `borders c` exactly when `c` is one of `edgeCells edge` (mutual consistency); likewise `vertexCells`/`vertexEdges`/`corners` are mutually consistent.
- AC-004 [US-001] [FR-004]: Given a wall set containing the canonical edge between `a` and `b`, when `isEdgePassable walls a b` and `isEdgePassable walls b a` are called, then both return false; with no such wall, both return true.
- AC-005 [US-001] [FR-005]: Given two adjacent open cells separated by a walled edge, when `Edges.bfs`/`Edges.astar` route between them, then the path goes around the wall (never crossing it) and equals plain `bfs` when the wall set is empty.
- AC-006 [US-002] [FR-006]: Given identical inputs, when any `Edges` function runs repeatedly, then its result is byte-identical; all outputs are integer and total.

## Functional Requirements
- FR-001: `Edges.edgeBetween` MUST be order-independent (`edgeBetween a b = edgeBetween b a`), returning `Some` canonical `Edge` exactly when the cells are orthogonally adjacent and `None` otherwise; `edgeOf cell dir` MUST return that same canonical edge. (covers AC-001)
- FR-002: `Edges.borders cell` MUST return the cell's 4 canonical edges and `Edges.corners cell` its 4 canonical vertices, each in a fixed documented order. (covers AC-002)
- FR-003: The relationships MUST be mutually consistent — `edge` is in `borders c` iff `c` is in `edgeCells edge`, and `vertex` is in `corners c` iff `c` is in `vertexCells vertex` — and `edgeEndpoints`/`vertexEdges` MUST agree with them. (covers AC-003)
- FR-004: `Edges.isEdgePassable walls a b` MUST be symmetric and return false exactly when the canonical edge between `a` and `b` is in `walls`, so a wall stored once blocks both directions. (covers AC-004)
- FR-005: `Edges.bfs`/`Edges.astar` MUST never cross a walled edge — a wall between two adjacent open cells blocks direct movement, forcing a detour — and MUST equal plain `bfs` reachability/hop-count when `walls` is empty. (covers AC-005)
- FR-006: Every `Edges` function MUST be pure, integer, total, and byte-deterministic (fixed enumeration orders, total-order search frontier — no float or hash-iteration leakage). (covers AC-006)

## Ambiguities
- AMB-001: Edge representation — a canonical `(loCell, hiCell)` pair (auto-deduped), or `(Cell, Dir)` canonicalized to N/W?
- AMB-002: Direction convention — which axis is North (Row−1 vs Row+1)? (Must match the module's neighbour offsets.)
- AMB-003: Vertex representation — a lattice-point coordinate, or `(Cell, Corner)` canonicalized to one representative?

## Public Or Tool-Facing Impact
- Tier 1 (contracted). Adds a new public `Edges` module (types + functions) to `FS.GG.Game.Core`, a
  new `Edges.fs`/`.fsi` in the compile order — so the `.fsi`, both surface baselines, and tests land
  together, and the surface-baseline-drift gate must stay green. No existing signature changes.

## Lifecycle Notes
- Determinism/property tests MUST carry a stable filterable name for the `gate.yml` guard.
- Next lifecycle action: `fsgg-sdd clarify --work 020-tile-edges-vertices`.
