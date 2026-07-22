namespace FS.GG.Game.Core

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
