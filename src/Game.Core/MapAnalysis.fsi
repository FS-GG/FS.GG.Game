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

    /// The `Floor` cells on the map's outer ring (`col = 0`, `col = Width-1`, `row = 0`, or `row = Height-1`),
    /// in row-major order — a map's entrances/exits, where it meets the outside. Neighbourhood-independent.
    /// Total.
    val borderOpenings: map: TileMap -> Cell list

    /// The `Floor` cells with exactly one `Floor` neighbour under `neighbourhood` (no corner-cutting under
    /// `EightWay`), in row-major order — the map's dead-ends. Total.
    val deadEnds: neighbourhood: Neighbourhood -> map: TileMap -> Cell list

    /// The chokepoints of `map` under `neighbourhood` — exactly the `Floor` cells whose removal increases the
    /// floor component count (graph articulation points / cut vertices), in row-major order. Implemented with
    /// **iterative** DFS (Tarjan), so a long single-cell-wide corridor cannot overflow the stack — total on
    /// any shape.
    val articulationPoints: neighbourhood: Neighbourhood -> map: TileMap -> Cell list

    /// The eccentricity of `cell` — the maximum **unweighted hop distance** (BFS, each `neighbourhood` step =
    /// 1) from `cell` to any `Floor` cell reachable from it. 0 when `cell` is not `Floor` or has no reachable
    /// neighbour. This is a topological hop count, distinct from `Pathfinding.distanceField`'s `baseStep`/√2
    /// movement cost. Total.
    val isolation: neighbourhood: Neighbourhood -> map: TileMap -> cell: Cell -> int

    /// The diameter of `map` under `neighbourhood` — the longest shortest-path (in unweighted hops) between
    /// any two `Floor` cells in the same component, i.e. the map's critical-path length. Equals the maximum
    /// over all `Floor` cells of `isolation`. 0 for an empty-floor or single-cell map. O(V²); a build/validate
    /// -time metric, not a per-tick one. Total.
    val diameter: neighbourhood: Neighbourhood -> map: TileMap -> int
