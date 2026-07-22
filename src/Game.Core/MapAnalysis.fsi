namespace FS.GG.Game.Core

/// Public contract module exposed by the FS.GG.Game.Core package.
/// Producer-agnostic analysis over a map — a `TileMap`, or (like `Pathfinding`) a `Cell -> bool`
/// walkability predicate. The analysis half of the `fs-gg-mapcraft` construction pipeline
/// (produce → analyze → validate): a map-builder asks these questions of a map however it was made —
/// procedurally (`MapGen`), hand-authored, or agent-drawn. Everything is pure, total, and deterministic,
/// and built on `Pathfinding`/`MapGen` so its answers agree with the router's neighbour logic.
///
/// M8 covers reachability & connectivity. Later milestones add chokepoints, path metrics, fairness &
/// validation, and tactical shape.
[<RequireQualifiedAccess>]
module MapAnalysis =

    /// The set of cells reachable from `start` over the `isWalkable` predicate and `neighbourhood`, bounded
    /// by `maxVisited`. Built on `Pathfinding.distanceField`, so it uses the router's exact neighbour logic
    /// (no corner-cutting under `EightWay`) — the returned set is exactly what `Pathfinding.bfs` can reach.
    /// A non-walkable `start` yields the empty set. Argument order matches `Pathfinding.bfs`.
    val reachable:
        neighbourhood: Neighbourhood -> maxVisited: int -> isWalkable: (Cell -> bool) -> start: Cell -> Set<Cell>

    /// The `Floor` cells of `map` **not** reachable from `from` under `neighbourhood`, in row-major order —
    /// the "did I strand a region?" answer. When `from` is not a `Floor` cell, every `Floor` cell is
    /// stranded (nothing is reachable from a wall). Total.
    val stranded: neighbourhood: Neighbourhood -> from: Cell -> map: TileMap -> Cell list

    /// Whether every `Floor` cell of `map` forms a single connected component under `neighbourhood`. A map
    /// with no `Floor` is connected (vacuously). Total.
    val isConnected: neighbourhood: Neighbourhood -> map: TileMap -> bool

    /// The number of connected `Floor` components of `map` under `neighbourhood` — the count of
    /// `MapGen.regions` (which is the component list itself). Total; an empty-floor map has 0 components.
    val componentCount: neighbourhood: Neighbourhood -> map: TileMap -> int
