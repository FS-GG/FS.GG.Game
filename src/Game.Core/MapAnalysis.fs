namespace FS.GG.Game.Core

open System.Collections.Generic

[<RequireQualifiedAccess>]
module MapAnalysis =

    // Reachability is delegated to Pathfinding.distanceField: its settled keys ARE the reachable set, and it
    // uses the router's own neighbour + no-corner-cutting logic — so `reachable` agrees with `bfs` by
    // construction rather than by a parallel flood-fill that could drift from it.
    let reachable
        (neighbourhood: Neighbourhood)
        (maxVisited: int)
        (isWalkable: Cell -> bool)
        (start: Cell)
        : Set<Cell> =
        if not (isWalkable start) then
            Set.empty
        else
            // distanceField treats `cost c <= 0` as impassable; 1/0 is the binary walkability lift.
            let cost c = if isWalkable c then 1 else 0

            Pathfinding.distanceField neighbourhood maxVisited cost [ start ]
            |> Map.toSeq
            |> Seq.map fst
            |> Set.ofSeq

    /// The `Floor` cells of a map in row-major order — the deterministic enumeration the results follow.
    let private floorCells (map: TileMap) : Cell list =
        [ for row in 0 .. map.Height - 1 do
              for col in 0 .. map.Width - 1 do
                  if map.Cells.[row * map.Width + col] = Floor then
                      { Col = col; Row = row } ]

    let stranded (neighbourhood: Neighbourhood) (from: Cell) (map: TileMap) : Cell list =
        let isFloor c = MapGen.get map c = ValueSome Floor
        let floors = floorCells map

        if not (isFloor from) then
            floors // nothing is reachable from a non-floor reference
        else
            // maxVisited large enough to settle every floor cell.
            let budget = map.Width * map.Height + 1
            let reached = reachable neighbourhood budget isFloor from
            floors |> List.filter (fun c -> not (Set.contains c reached))

    let isConnected (neighbourhood: Neighbourhood) (map: TileMap) : bool =
        match floorCells map with
        | [] -> true // no floor is vacuously connected
        | first :: _ -> stranded neighbourhood first map |> List.isEmpty

    let componentCount (neighbourhood: Neighbourhood) (map: TileMap) : int =
        MapGen.regions neighbourhood map |> List.length

    // ---------------------------------------------------------------------------------------------
    // M9 — entrances/exits & chokepoints.
    // ---------------------------------------------------------------------------------------------

    /// The in-bounds `Floor` neighbours of `c` under `neighbourhood`, rejecting corner-cut diagonals under
    /// `EightWay` — the same adjacency `MapGen.regions` and the router use, in a fixed offset order.
    let private floorNeighbours (neighbourhood: Neighbourhood) (map: TileMap) (c: Cell) : Cell list =
        let w = map.Width
        let h = map.Height
        let isFloor col row = col >= 0 && row >= 0 && col < w && row < h && map.Cells.[row * w + col] = Floor

        let offsets =
            match neighbourhood with
            | FourWay -> [| struct (0, -1); struct (-1, 0); struct (1, 0); struct (0, 1) |]
            | EightWay ->
                [| struct (-1, -1)
                   struct (0, -1)
                   struct (1, -1)
                   struct (-1, 0)
                   struct (1, 0)
                   struct (-1, 1)
                   struct (0, 1)
                   struct (1, 1) |]

        [ for struct (dc, dr) in offsets do
              let nc = c.Col + dc
              let nr = c.Row + dr

              let cornerCut =
                  neighbourhood = EightWay
                  && dc <> 0
                  && dr <> 0
                  && not (isFloor (c.Col + dc) c.Row && isFloor c.Col (c.Row + dr))

              if isFloor nc nr && not cornerCut then
                  { Col = nc; Row = nr } ]

    let borderOpenings (map: TileMap) : Cell list =
        let w = map.Width
        let h = map.Height

        if w <= 0 || h <= 0 then
            []
        else
            [ for row in 0 .. h - 1 do
                  for col in 0 .. w - 1 do
                      if
                          (col = 0 || row = 0 || col = w - 1 || row = h - 1)
                          && map.Cells.[row * w + col] = Floor
                      then
                          { Col = col; Row = row } ]

    let deadEnds (neighbourhood: Neighbourhood) (map: TileMap) : Cell list =
        floorCells map
        |> List.filter (fun c -> List.length (floorNeighbours neighbourhood map c) = 1)

    let articulationPoints (neighbourhood: Neighbourhood) (map: TileMap) : Cell list =
        let cells = floorCells map |> List.toArray // row-major
        let n = cells.Length

        if n = 0 then
            []
        else
            let indexOf = Dictionary<Cell, int>()
            cells |> Array.iteri (fun i c -> indexOf.[c] <- i)
            let adj = cells |> Array.map (fun c -> floorNeighbours neighbourhood map c |> List.map (fun nc -> indexOf.[nc]) |> List.toArray)

            let disc = Array.create n -1
            let low = Array.zeroCreate<int> n
            let isAp = Array.create n false
            let mutable timer = 0

            // Iterative Tarjan: an explicit frame stack (node, parent, cursor into adj[node]) replaces the
            // call stack, so a corridor of any length cannot overflow. Row-major roots keep it deterministic.
            for root in 0 .. n - 1 do
                if disc.[root] < 0 then
                    let mutable rootChildren = 0
                    let stack = Stack<struct (int * int * int ref)>()
                    disc.[root] <- timer
                    low.[root] <- timer
                    timer <- timer + 1
                    stack.Push(struct (root, -1, ref 0))

                    while stack.Count > 0 do
                        let struct (u, parent, cursor) = stack.Peek()

                        if cursor.Value < adj.[u].Length then
                            let v = adj.[u].[cursor.Value]
                            cursor.Value <- cursor.Value + 1

                            if disc.[v] < 0 then
                                // tree edge
                                if u = root then
                                    rootChildren <- rootChildren + 1

                                disc.[v] <- timer
                                low.[v] <- timer
                                timer <- timer + 1
                                stack.Push(struct (v, u, ref 0))
                            elif v <> parent then
                                // back edge
                                low.[u] <- min low.[u] disc.[v]
                        else
                            // finished u: fold its low into its parent and apply the non-root AP rule
                            stack.Pop() |> ignore

                            if parent >= 0 then
                                low.[parent] <- min low.[parent] low.[u]

                                if parent <> root && low.[u] >= disc.[parent] then
                                    isAp.[parent] <- true

                    if rootChildren > 1 then
                        isAp.[root] <- true

            [ for i in 0 .. n - 1 do
                  if isAp.[i] then
                      cells.[i] ]

    // ---------------------------------------------------------------------------------------------
    // M10 — path & flow metrics. Unweighted BFS hop distances — the topological "how many steps across",
    // distinct from Pathfinding.distanceField's baseStep/√2 movement cost.
    // ---------------------------------------------------------------------------------------------

    /// Level BFS hop distances from a `Floor` `start` over `floorNeighbours`; empty when `start` is not floor.
    let private bfsHops (neighbourhood: Neighbourhood) (map: TileMap) (start: Cell) : Dictionary<Cell, int> =
        let dist = Dictionary<Cell, int>()

        if MapGen.get map start = ValueSome Floor then
            dist.[start] <- 0
            let queue = Queue<Cell>()
            queue.Enqueue start

            while queue.Count > 0 do
                let c = queue.Dequeue()

                for n in floorNeighbours neighbourhood map c do
                    if not (dist.ContainsKey n) then
                        dist.[n] <- dist.[c] + 1
                        queue.Enqueue n

        dist

    let isolation (neighbourhood: Neighbourhood) (map: TileMap) (cell: Cell) : int =
        let dist = bfsHops neighbourhood map cell
        // `max` over the BFS levels is commutative, so Dictionary enumeration order does not reach the result.
        if dist.Count = 0 then 0 else Seq.max dist.Values

    let diameter (neighbourhood: Neighbourhood) (map: TileMap) : int =
        floorCells map
        |> List.fold (fun acc c -> max acc (isolation neighbourhood map c)) 0
