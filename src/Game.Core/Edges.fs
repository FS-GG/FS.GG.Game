namespace FS.GG.Game.Core

/// A cardinal edge direction from a cell. North is Row − 1 (matching the module's orthogonal offset
/// order); East Col + 1; South Row + 1; West Col − 1.
type Dir =
    | North
    | East
    | South
    | West

/// A canonical grid EDGE — the boundary between two orthogonally adjacent cells, stored as the pair
/// sorted so `Lo <= Hi`. Sorting IS the dedupe: the edge shared by two cells has ONE representative
/// regardless of which side names it, so a wall set stores it once. Built only via `Edges.edgeBetween`
/// / `Edges.edgeOf`.
[<Struct>]
type Edge = { Lo: Cell; Hi: Cell }

/// A canonical grid VERTEX — a lattice point at cell corners. Vertex `(VCol, VRow)` is the NW corner of
/// cell `(VCol, VRow)`, and is shared by up to four cells; the lattice point is inherently deduped.
[<Struct>]
type Vertex = { VCol: int; VRow: int }

[<RequireQualifiedAccess>]
module Edges =

    let private offset (d: Dir) : int * int =
        match d with
        | North -> (0, -1)
        | East -> (1, 0)
        | South -> (0, 1)
        | West -> (-1, 0)

    /// The cell one step from `c` in direction `d`.
    let step (c: Cell) (d: Dir) : Cell =
        let (dx, dy) = offset d
        { Col = c.Col + dx; Row = c.Row + dy }

    // Orthogonal adjacency, in int64 so a far-apart non-adjacent pair cannot overflow the difference.
    let private adjacent (a: Cell) (b: Cell) : bool =
        abs (int64 a.Col - int64 b.Col) + abs (int64 a.Row - int64 b.Row) = 1L

    /// The canonical edge between `a` and `b` — `Some` exactly when they are orthogonally adjacent,
    /// `None` otherwise. Order-independent: `edgeBetween a b = edgeBetween b a`.
    let edgeBetween (a: Cell) (b: Cell) : Edge option =
        if adjacent a b then
            Some { Lo = min a b; Hi = max a b }
        else
            None

    /// The canonical edge on the `d` side of `c` (`(Cell, Dir)` addressing → the same `Edge` value).
    let edgeOf (c: Cell) (d: Dir) : Edge =
        let n = step c d
        { Lo = min c n; Hi = max c n }

    /// The two cells a canonical edge separates.
    let edgeCells (e: Edge) : Cell * Cell = (e.Lo, e.Hi)

    /// A cell's four canonical edges, in N, E, S, W order.
    let borders (c: Cell) : Edge list =
        [ edgeOf c North; edgeOf c East; edgeOf c South; edgeOf c West ]

    /// A cell's four canonical corner vertices, in NW, NE, SW, SE order.
    let corners (c: Cell) : Vertex list =
        [ { VCol = c.Col; VRow = c.Row }
          { VCol = c.Col + 1; VRow = c.Row }
          { VCol = c.Col; VRow = c.Row + 1 }
          { VCol = c.Col + 1; VRow = c.Row + 1 } ]

    /// The two endpoint vertices of a canonical edge.
    let edgeEndpoints (e: Edge) : Vertex * Vertex =
        if e.Hi.Col > e.Lo.Col then
            // vertical edge (East neighbour): the shared segment at Col = Hi.Col
            ({ VCol = e.Hi.Col; VRow = e.Lo.Row }, { VCol = e.Hi.Col; VRow = e.Lo.Row + 1 })
        else
            // horizontal edge (South neighbour): the shared segment at Row = Hi.Row
            ({ VCol = e.Lo.Col; VRow = e.Hi.Row }, { VCol = e.Lo.Col + 1; VRow = e.Hi.Row })

    /// The up-to-four cells that touch a vertex, in NW, NE, SW, SE order (spatially around the point).
    let vertexCells (v: Vertex) : Cell list =
        [ { Col = v.VCol - 1; Row = v.VRow - 1 }
          { Col = v.VCol; Row = v.VRow - 1 }
          { Col = v.VCol - 1; Row = v.VRow }
          { Col = v.VCol; Row = v.VRow } ]

    /// The four canonical edges meeting at a vertex, in N, E, S, W (segment) order.
    let vertexEdges (v: Vertex) : Edge list =
        let nw = { Col = v.VCol - 1; Row = v.VRow - 1 }
        let ne = { Col = v.VCol; Row = v.VRow - 1 }
        let sw = { Col = v.VCol - 1; Row = v.VRow }
        let se = { Col = v.VCol; Row = v.VRow }

        [ { Lo = min nw ne; Hi = max nw ne } // north segment (between the two upper cells)
          { Lo = min ne se; Hi = max ne se } // east segment
          { Lo = min sw se; Hi = max sw se } // south segment
          { Lo = min nw sw; Hi = max nw sw } ] // west segment

    /// The four orthogonally adjacent cells of `c`, in N, E, S, W order.
    let neighbours (c: Cell) : Cell list =
        [ step c North; step c East; step c South; step c West ]

    /// **True when movement from `a` to `b` is not blocked by a wall on their shared edge.** Symmetric
    /// (because `edgeBetween` is): a wall stored once on the canonical edge blocks both directions. A
    /// non-adjacent pair has no shared edge, so it is treated as passable (the caller supplies
    /// adjacency).
    let isEdgePassable (walls: Set<Edge>) (a: Cell) (b: Cell) : bool =
        match edgeBetween a b with
        | Some e -> not (Set.contains e walls)
        | None -> true

    // Walk the cameFrom chain from `goal` back to the start, yielding start..goal inclusive.
    let private reconstruct (cameFrom: Map<Cell, Cell>) (goal: Cell) : Cell list =
        let rec walk acc c =
            match Map.tryFind c cameFrom with
            | Some prev -> walk (c :: acc) prev
            | None -> c :: acc

        walk [] goal

    // The orthogonal neighbours of `c` that are both walkable and reachable across a non-walled edge.
    let private openNeighbours (walls: Set<Edge>) (isWalkable: Cell -> bool) (c: Cell) : Cell list =
        neighbours c
        |> List.filter (fun n -> isWalkable n && isEdgePassable walls c n)

    /// Public contract function exposed by the FS.GG.Game.Core package.
    /// Breadth-first shortest hop path that respects thin walls: it never crosses an edge in `walls`,
    /// so a wall between two open cells forces a detour. Endpoint-inclusive, `maxVisited`-bounded,
    /// `None` when unreachable; byte-deterministic (FIFO over the fixed N,E,S,W order). With an empty
    /// `walls` set it matches plain `Pathfinding.bfs` (FourWay) reachability and hop count.
    let bfs
        (walls: Set<Edge>)
        (maxVisited: int)
        (isWalkable: Cell -> bool)
        (start: Cell)
        (goal: Cell)
        : Cell list option =
        if maxVisited <= 0 || not (isWalkable start) || not (isWalkable goal) then
            None
        elif start = goal then
            Some [ start ]
        else
            let rec loop front back cameFrom visited expansions =
                match front with
                | [] ->
                    match List.rev back with
                    | [] -> None
                    | front -> loop front [] cameFrom visited expansions
                | current :: rest ->
                    if expansions >= maxVisited then
                        None
                    elif current = goal then
                        Some(reconstruct cameFrom goal)
                    else
                        let (back, cameFrom, visited) =
                            openNeighbours walls isWalkable current
                            |> List.fold
                                (fun (bk, cf, vis) n ->
                                    if Set.contains n vis then
                                        (bk, cf, vis)
                                    else
                                        (n :: bk, Map.add n current cf, Set.add n vis))
                                (back, cameFrom, visited)

                        loop rest back cameFrom visited (expansions + 1)

            loop [ start ] [] Map.empty (Set.singleton start) 0

    /// Public contract function exposed by the FS.GG.Game.Core package.
    /// A* shortest hop path that respects thin walls, using the Manhattan distance as the admissible
    /// heuristic — same contract, endpoint-inclusion, `maxVisited` bound, wall-blocking, and degenerate
    /// behaviour as `Edges.bfs`, and the same shortest-hop result (every hop costs 1). The frontier is
    /// keyed by the total integer order `(f, h, Col, Row)`, so the path is byte-identical across runs.
    let astar
        (walls: Set<Edge>)
        (maxVisited: int)
        (isWalkable: Cell -> bool)
        (start: Cell)
        (goal: Cell)
        : Cell list option =
        if maxVisited <= 0 || not (isWalkable start) || not (isWalkable goal) then
            None
        elif start = goal then
            Some [ start ]
        else
            let h (c: Cell) : int64 =
                abs (int64 c.Col - int64 goal.Col) + abs (int64 c.Row - int64 goal.Row)

            let h0 = h start
            let openSet = Set.singleton (h0, h0, start.Col, start.Row)
            let gScore = Map.ofList [ start, 0L ]

            let rec loop
                (openSet: Set<int64 * int64 * int * int>)
                (gScore: Map<Cell, int64>)
                (cameFrom: Map<Cell, Cell>)
                (expansions: int)
                : Cell list option =
                if Set.isEmpty openSet || expansions >= maxVisited then
                    None
                else
                    let (f, hCur, col, row) = Set.minElement openSet
                    let current = { Col = col; Row = row }
                    let openSet = Set.remove (f, hCur, col, row) openSet

                    if current = goal then
                        Some(reconstruct cameFrom current)
                    else
                        let g = gScore.[current]

                        let (openSet, gScore, cameFrom) =
                            openNeighbours walls isWalkable current
                            |> List.fold
                                (fun (os, gs, cf) n ->
                                    let tentative = g + 1L

                                    match Map.tryFind n gs with
                                    | Some existing when existing <= tentative -> (os, gs, cf)
                                    | prior ->
                                        let hn = h n

                                        let os =
                                            match prior with
                                            | Some oldG -> Set.remove (oldG + hn, hn, n.Col, n.Row) os
                                            | None -> os

                                        let os = Set.add (tentative + hn, hn, n.Col, n.Row) os
                                        (os, Map.add n tentative gs, Map.add n current cf))
                                (openSet, gScore, cameFrom)

                        loop openSet gScore cameFrom (expansions + 1)

            loop openSet gScore Map.empty 0
