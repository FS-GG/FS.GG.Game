namespace FS.GG.Game.Core

/// Public contract type exposed by the FS.GG.Game.Core package.
/// A cardinal edge direction from a cell. `North` is `Row − 1` (matching the module's orthogonal
/// offset order), `East` `Col + 1`, `South` `Row + 1`, `West` `Col − 1`.
type Dir =
    | North
    | East
    | South
    | West

/// Public contract type exposed by the FS.GG.Game.Core package.
/// A canonical grid **edge** — the boundary between two orthogonally adjacent cells, stored as the pair
/// sorted so `Lo <= Hi`. Sorting IS the dedupe: the edge shared by two cells has ONE representative
/// regardless of which side names it, so a wall set stores it once and blocks both directions. Built
/// only via `Edges.edgeBetween` / `Edges.edgeOf`. `[<Struct>]`, structural equality — a stable set/map
/// key.
[<Struct>]
type Edge = { Lo: Cell; Hi: Cell }

/// Public contract type exposed by the FS.GG.Game.Core package.
/// A canonical grid **vertex** — a lattice point at cell corners. Vertex `(VCol, VRow)` is the NW
/// corner of cell `(VCol, VRow)` and is shared by up to four cells; the lattice point is inherently
/// deduped. `[<Struct>]`, structural equality.
[<Struct>]
type Vertex = { VCol: int; VRow: int }

/// Public contract module exposed by the FS.GG.Game.Core package.
/// Tile **edges and vertices** (roadmap 2.3): address the boundaries between tiles, not just the tiles,
/// so features can live on them — thin walls, doors, fences, rivers. Canonical addressing dedupes the
/// shared edge/vertex; the tile-part relationships are pure, mutually-consistent functions; and
/// edge-aware `bfs`/`astar` let a wall on a shared edge block movement between two otherwise-open
/// cells. Everything is integer, total, and byte-deterministic. Complements — does not replace — the
/// tile model; the square `Pathfinding`/`Grids` are unchanged.
[<RequireQualifiedAccess>]
module Edges =

    /// The cell one step from `c` in direction `d`.
    val step: c: Cell -> d: Dir -> Cell

    /// The canonical edge between `a` and `b` — `Some` exactly when they are orthogonally adjacent,
    /// `None` otherwise. Order-independent: `edgeBetween a b = edgeBetween b a`.
    val edgeBetween: a: Cell -> b: Cell -> Edge option

    /// The canonical edge on the `d` side of `c` — the `(Cell, Dir)` addressing mapped onto the same
    /// `Edge` value `edgeBetween` produces.
    val edgeOf: c: Cell -> d: Dir -> Edge

    /// The two cells a canonical edge separates.
    val edgeCells: e: Edge -> Cell * Cell

    /// A cell's four canonical edges, in N, E, S, W order.
    val borders: c: Cell -> Edge list

    /// A cell's four canonical corner vertices, in NW, NE, SW, SE order.
    val corners: c: Cell -> Vertex list

    /// The two endpoint vertices of a canonical edge.
    val edgeEndpoints: e: Edge -> Vertex * Vertex

    /// The up-to-four cells that touch a vertex, in NW, NE, SW, SE order (spatially around the point).
    val vertexCells: v: Vertex -> Cell list

    /// The four canonical edges meeting at a vertex, in N, E, S, W (segment) order.
    val vertexEdges: v: Vertex -> Edge list

    /// The four orthogonally adjacent cells of `c`, in N, E, S, W order.
    val neighbours: c: Cell -> Cell list

    /// **True when movement from `a` to `b` is not blocked by a wall on their shared edge.** Symmetric
    /// (because `edgeBetween` is): a wall stored once on the canonical edge blocks both directions. A
    /// non-adjacent pair has no shared edge and is treated as passable (the caller supplies adjacency).
    val isEdgePassable: walls: Set<Edge> -> a: Cell -> b: Cell -> bool

    /// Public contract function exposed by the FS.GG.Game.Core package.
    /// Breadth-first shortest hop path that respects thin walls: it never crosses an edge in `walls`, so
    /// a wall between two open cells forces a detour. `Some path` (endpoints included) or `None` when
    /// unreachable; `maxVisited`-bounded and byte-deterministic (FIFO over the fixed N,E,S,W order).
    /// With an empty `walls` set it matches plain `Pathfinding.bfs` (`FourWay`) reachability and hop
    /// count. Total on degenerate input: a non-walkable `start`/`goal` or `maxVisited <= 0` yields
    /// `None`; a walkable `start = goal` yields `Some [start]`.
    val bfs: walls: Set<Edge> -> maxVisited: int -> isWalkable: (Cell -> bool) -> start: Cell -> goal: Cell -> Cell list option

    /// Public contract function exposed by the FS.GG.Game.Core package.
    /// A* shortest hop path that respects thin walls, using the Manhattan distance as the admissible
    /// heuristic — same contract, endpoint-inclusion, `maxVisited` bound, wall-blocking, and degenerate
    /// behaviour as `Edges.bfs`, and the same shortest-hop result. The frontier is keyed by the total
    /// integer order `(f, h, Col, Row)`, so the path is byte-identical across runs and platforms.
    val astar: walls: Set<Edge> -> maxVisited: int -> isWalkable: (Cell -> bool) -> start: Cell -> goal: Cell -> Cell list option
