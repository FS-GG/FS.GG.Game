namespace FS.GG.Game.Core

[<Struct>]
type Cell = { Col: int; Row: int }

type Neighbourhood =
    | FourWay
    | EightWay

[<RequireQualifiedAccess>]
module Pathfinding =

    // Fixed step offsets. The enumeration order does not affect A* output (the total (f,h,Col,Row)
    // frontier order decides), and for BFS it is a deterministic, documented order.
    let private orthoOffsets = [ (0, -1); (0, 1); (-1, 0); (1, 0) ]
    let private diagOffsets = [ (-1, -1); (1, -1); (-1, 1); (1, 1) ]

    // Walkable neighbours of `c` with their integer step cost. Orthogonal = 10, diagonal = 14.
    // Under EightWay a diagonal is refused unless BOTH shared orthogonal neighbours are walkable
    // (no corner-cutting).
    let private neighbours (nb: Neighbourhood) (isWalkable: Cell -> bool) (c: Cell) : struct (Cell * int) list =
        let ortho =
            orthoOffsets
            |> List.choose (fun (dx, dy) ->
                let n = { Col = c.Col + dx; Row = c.Row + dy }
                if isWalkable n then Some(struct (n, 10)) else None)

        match nb with
        | FourWay -> ortho
        | EightWay ->
            let diag =
                diagOffsets
                |> List.choose (fun (dx, dy) ->
                    let n = { Col = c.Col + dx; Row = c.Row + dy }
                    let side1 = { Col = c.Col + dx; Row = c.Row }
                    let side2 = { Col = c.Col; Row = c.Row + dy }

                    if isWalkable n && isWalkable side1 && isWalkable side2 then
                        Some(struct (n, 14))
                    else
                        None)

            ortho @ diag

    // Admissible integer heuristic toward `goal`: Manhattan*10 (4-way) / octile (8-way with 10/14).
    let private heuristic (nb: Neighbourhood) (goal: Cell) (c: Cell) : int =
        let dx = abs (c.Col - goal.Col)
        let dy = abs (c.Row - goal.Row)

        match nb with
        | FourWay -> 10 * (dx + dy)
        | EightWay ->
            let lo = min dx dy
            let hi = max dx dy
            14 * lo + 10 * (hi - lo)

    // Walk the cameFrom chain from `goal` back to the start, yielding start..goal inclusive.
    let private reconstruct (cameFrom: Map<Cell, Cell>) (goal: Cell) : Cell list =
        let rec walk acc c =
            match Map.tryFind c cameFrom with
            | Some prev -> walk (c :: acc) prev
            | None -> c :: acc

        walk [] goal

    // Shared guards for the trivial/degenerate cases before a real search runs.
    let private trivial (maxVisited: int) (isWalkable: Cell -> bool) (start: Cell) (goal: Cell) : Cell list option option =
        if maxVisited <= 0 || not (isWalkable start) || not (isWalkable goal) then Some None
        elif start = goal then Some(Some [ start ])
        else None

    let astar
        (neighbourhood: Neighbourhood)
        (maxVisited: int)
        (isWalkable: Cell -> bool)
        (start: Cell)
        (goal: Cell)
        : Cell list option =
        match trivial maxVisited isWalkable start goal with
        | Some result -> result
        | None ->
            let h = heuristic neighbourhood goal
            // Frontier is a Set keyed by the TOTAL integer order (f, h, Col, Row) — Set.minElement is
            // deterministic, so the pop order (hence the path) is bit-identical across runs/platforms.
            let h0 = h start
            let openSet = Set.singleton (h0, h0, start.Col, start.Row)
            let gScore = Map.ofList [ start, 0 ]

            let rec loop
                (openSet: Set<int * int * int * int>)
                (gScore: Map<Cell, int>)
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
                            neighbours neighbourhood isWalkable current
                            |> List.fold
                                (fun (os, gs, cf) (struct (n, cost)) ->
                                    let tentative = g + cost

                                    match Map.tryFind n gs with
                                    | Some existing when existing <= tentative -> (os, gs, cf)
                                    | prior ->
                                        let hn = h n
                                        // Drop any stale open entry for n (same h, old g) before re-adding.
                                        let os =
                                            match prior with
                                            | Some oldG -> Set.remove (oldG + hn, hn, n.Col, n.Row) os
                                            | None -> os

                                        let os = Set.add (tentative + hn, hn, n.Col, n.Row) os
                                        (os, Map.add n tentative gs, Map.add n current cf))
                                (openSet, gScore, cameFrom)

                        loop openSet gScore cameFrom (expansions + 1)

            loop openSet gScore Map.empty 0

    let bfs
        (neighbourhood: Neighbourhood)
        (maxVisited: int)
        (isWalkable: Cell -> bool)
        (start: Cell)
        (goal: Cell)
        : Cell list option =
        match trivial maxVisited isWalkable start goal with
        | Some result -> result
        | None ->
            // Amortized-O(1) immutable FIFO queue (front list + reversed back list). Deterministic:
            // enqueue order follows the fixed neighbour offsets, dequeue is FIFO — no hashing on order.
            let rec loop
                (front: Cell list)
                (back: Cell list)
                (cameFrom: Map<Cell, Cell>)
                (visited: Set<Cell>)
                (expansions: int)
                : Cell list option =
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
                            neighbours neighbourhood isWalkable current
                            |> List.fold
                                (fun (bk, cf, vis) (struct (n, _cost)) ->
                                    if Set.contains n vis then
                                        (bk, cf, vis)
                                    else
                                        (n :: bk, Map.add n current cf, Set.add n vis))
                                (back, cameFrom, visited)

                        loop rest back cameFrom visited (expansions + 1)

            loop [ start ] [] Map.empty (Set.singleton start) 0

    // ---------------------------------------------------------------------------------------------
    // Many-to-one navigation: Dijkstra maps / flow fields.
    // (docs/reports/2026-07-05-game-logic-pathfinding-navigation-design.md §6, §7)
    //
    // `cost c` is the cost to ENTER cell `c`; `cost c <= 0` means impassable. So the walkability
    // predicate `astar`/`bfs` take is exactly `fun c -> cost c > 0`, and one terrain function drives
    // both. A move onto `c` costs `baseStep * cost c` with `baseStep` the same integer 10 (orthogonal)
    // / 14 (diagonal) `neighbours` already yields — so with `cost = fun _ -> 1` these fields reproduce
    // `astar`'s g-score exactly (the optimality cross-check).
    //
    // Every edge weight is >= 10 (strictly positive), so a settled cell is final: plain Dijkstra.
    // Accumulation is int64 so a large `cost` cannot silently wrap; a relaxation that would exceed
    // Int32.MaxValue is dropped (treated as unreachable) rather than overflowing.

    // The frontier is a Set keyed by the TOTAL integer order (dist, Col, Row) — `Set.minElement` is
    // deterministic, so the settle order (hence the field) is bit-identical across runs/platforms.
    // Exactly one open entry exists per cell: a relaxation removes the stale entry before re-adding.
    //
    // `stepWeight current n baseStep` decides the direction convention, which is the ONE thing that
    // differs between the reverse (`distanceField`) and forward (`reachableWithin`) walks.
    let private dijkstra
        (neighbourhood: Neighbourhood)
        (cost: Cell -> int)
        (stepWeight: Cell -> Cell -> int -> int64)
        (admit: int64 -> bool)
        (maxVisited: int)
        (seeds: Cell list)
        : Map<Cell, int> =
        let isWalkable c = cost c > 0

        let rec loop
            (openSet: Set<int64 * int * int>)
            (tentative: Map<Cell, int64>)
            (settled: Map<Cell, int>)
            (expansions: int)
            : Map<Cell, int> =
            if Set.isEmpty openSet || expansions >= maxVisited then
                settled
            else
                let (d, col, row) = Set.minElement openSet
                let current = { Col = col; Row = row }
                let openSet = Set.remove (d, col, row) openSet
                // Popped with the minimum key and all weights > 0 => `d` is final for `current`.
                let settled = Map.add current (int d) settled

                let (openSet, tentative) =
                    neighbours neighbourhood isWalkable current
                    |> List.fold
                        (fun (os, tent) (struct (n, baseStep)) ->
                            let candidate = d + stepWeight current n baseStep

                            if candidate > int64 System.Int32.MaxValue || not (admit candidate) then
                                (os, tent)
                            else
                                match Map.tryFind n tent with
                                | Some existing when existing <= candidate -> (os, tent)
                                | prior ->
                                    // Drop any stale open entry for n (old distance) before re-adding.
                                    let os =
                                        match prior with
                                        | Some oldD -> Set.remove (oldD, n.Col, n.Row) os
                                        | None -> os

                                    (Set.add (candidate, n.Col, n.Row) os, Map.add n candidate tent))
                        (openSet, tentative)

                loop openSet tentative settled (expansions + 1)

        // Only cells that are actually settled are returned, so a `maxVisited` cut-off yields a
        // partial-but-correct field rather than a field with unfinalised tentative values in it.
        let seeds = seeds |> List.filter isWalkable |> List.distinct

        if maxVisited <= 0 || List.isEmpty seeds then
            Map.empty
        else
            let openSet = seeds |> List.map (fun g -> (0L, g.Col, g.Row)) |> Set.ofList
            let tentative = seeds |> List.map (fun g -> g, 0L) |> Map.ofList
            loop openSet tentative Map.empty 0

    let distanceField
        (neighbourhood: Neighbourhood)
        (maxVisited: int)
        (cost: Cell -> int)
        (goals: Cell list)
        : Map<Cell, int> =
        // Reverse walk: expanding goal-ward cell `current` to `n` means the AGENT steps n -> current,
        // i.e. it enters `current`. So the weight is `baseStep * cost current`.
        let stepWeight (current: Cell) (_n: Cell) (baseStep: int) = int64 baseStep * int64 (cost current)
        dijkstra neighbourhood cost stepWeight (fun _ -> true) maxVisited goals

    let reachableWithin
        (neighbourhood: Neighbourhood)
        (cost: Cell -> int)
        (budget: int)
        (start: Cell)
        : Map<Cell, int> =
        if budget < 0 then
            Map.empty
        else
            // Forward walk: the agent steps current -> n, entering `n`. Weight is `baseStep * cost n`.
            let stepWeight (_current: Cell) (n: Cell) (baseStep: int) = int64 baseStep * int64 (cost n)
            // Every edge weight is positive, so the budget alone bounds the search: no `maxVisited`.
            let admit (candidate: int64) = candidate <= int64 budget
            dijkstra neighbourhood cost stepWeight admit System.Int32.MaxValue [ start ]

    let flowField (neighbourhood: Neighbourhood) (field: Map<Cell, int>) : Map<Cell, Cell> =
        // Membership in the field IS walkability here, so the no-corner-cutting rule still applies:
        // a diagonal step is offered only when both shared orthogonals are also in the field.
        let inField c = Map.containsKey c field

        // `Map.fold` walks keys in ascending order, and ties among equally-low neighbours break on
        // (value, Col, Row) — so the flow field is a pure deterministic function of `field`.
        field
        |> Map.fold
            (fun acc c v ->
                let downhill =
                    neighbours neighbourhood inField c
                    |> List.choose (fun (struct (n, _baseStep)) ->
                        match Map.tryFind n field with
                        | Some nv when nv < v -> Some(nv, n.Col, n.Row)
                        | _ -> None)

                match downhill with
                | [] -> acc // A sink (a goal, or a local minimum): no strictly-lower neighbour, so no arrow.
                | xs ->
                    let (_, col, row) = List.min xs
                    Map.add c { Col = col; Row = row } acc)
            Map.empty
