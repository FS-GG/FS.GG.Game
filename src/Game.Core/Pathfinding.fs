namespace FS.GG.Game.Core

type Neighbourhood =
    | FourWay
    | EightWay

[<RequireQualifiedAccess>]
module Pathfinding =

    type Step =
        { Cost: int
          CameFrom: Cell option }

    type Reach =
        { Steps: Map<Cell, Step>
          Endable: Set<Cell> }

    // The integer step scale, and the ONE place it is written. Every 10 and 14 below derives from it
    // (#229): the literals used to be copied into `neighbours`, `heuristic` and the docstrings, so a
    // change to the scale had to be caught by prose.
    let baseStep = 10

    // The diagonal: `baseStep * √2`, integer-scaled — 14 when `baseStep` is 10. The 14/10 ratio IS the
    // √2 approximation, which is why the module never needs a float and equal-cost ties cannot leak
    // through floating-point equality. Derived, so it cannot drift from `baseStep`.
    let private diagStep = baseStep * 14 / 10

    // Saturating, because the failure it prevents is the one #229 is about: `int` multiplication would
    // WRAP a large `moveRange` to a negative budget, and a negative budget yields an EMPTY reach — the
    // same silent total freeze as passing `moveRange` raw. Clamping high leaves the walk bounded by
    // `maxVisited` (which is what bounds it anyway); a genuinely negative `moveRange` stays negative,
    // preserving `reachable`'s documented "negative budget ⇒ empty" totality.
    let budgetFor (moveRange: int) : int =
        let scaled = int64 baseStep * int64 moveRange

        if scaled > int64 System.Int32.MaxValue then System.Int32.MaxValue
        elif scaled < int64 System.Int32.MinValue then System.Int32.MinValue
        else int scaled

    // Fixed step offsets. The enumeration order does not affect A* output (the total (f,h,Col,Row)
    // frontier order decides), and for BFS it is a deterministic, documented order.
    let private orthoOffsets = [ (0, -1); (0, 1); (-1, 0); (1, 0) ]
    let private diagOffsets = [ (-1, -1); (1, -1); (-1, 1); (1, 1) ]

    // Walkable neighbours of `c` with their integer step cost: `baseStep` orthogonal, `diagStep`
    // diagonal. Under EightWay a diagonal is refused unless BOTH shared orthogonal neighbours are
    // walkable (no corner-cutting).
    let private neighbours (nb: Neighbourhood) (isWalkable: Cell -> bool) (c: Cell) : struct (Cell * int) list =
        let ortho =
            orthoOffsets
            |> List.choose (fun (dx, dy) ->
                let n = { Col = c.Col + dx; Row = c.Row + dy }
                if isWalkable n then Some(struct (n, baseStep)) else None)

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
                        Some(struct (n, diagStep))
                    else
                        None)

            ortho @ diag

    // Admissible integer heuristic toward `goal`: Manhattan (4-way) / octile (8-way), both in `baseStep`
    // units. Admissible because `diagStep <= 2 * baseStep` — a diagonal never costs more than the two
    // orthogonals it replaces — so the estimate can never exceed the true remaining cost.
    let private heuristic (nb: Neighbourhood) (goal: Cell) (c: Cell) : int =
        let dx = abs (c.Col - goal.Col)
        let dy = abs (c.Row - goal.Row)

        match nb with
        | FourWay -> baseStep * (dx + dy)
        | EightWay ->
            let lo = min dx dy
            let hi = max dx dy
            diagStep * lo + baseStep * (hi - lo)

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
    // Every edge weight is >= `baseStep` (strictly positive), so a settled cell is final: plain Dijkstra.
    // Accumulation is int64 so a large `cost` cannot silently wrap; a relaxation that would exceed
    // Int32.MaxValue is dropped (treated as unreachable) rather than overflowing.

    // The frontier is a Set keyed by the TOTAL integer order (dist, Col, Row) — `Set.minElement` is
    // deterministic, so the settle order (hence the field) is bit-identical across runs/platforms.
    // Exactly one open entry exists per cell: a relaxation removes the stale entry before re-adding.
    //
    // `stepWeight currentCost n baseStep` decides the direction convention, which is the ONE thing
    // that differs between the reverse (`distanceField`) and forward (`reachableWithin`) walks.
    // `currentCost` is the popped cell's cost, evaluated once per pop rather than once per neighbour.
    //
    // The walk SETTLES a `Step`, keeping the predecessor that produced each cell's final cost — the
    // relaxation already knows it, and discarding it is what left callers unable to reconstruct a path
    // from a budgeted search and reaching for `astar`, which prices a different question (#212). The
    // cost-only fields project it back out; `reachable` returns the tree.
    //
    // The `CameFrom` tree is deterministic for the same reason the costs are: cells settle in the total
    // `(dist, Col, Row)` order, and a relaxation improves a cell only on a STRICTLY lower candidate
    // (`existing <= candidate` skips), so among equal-cost predecessors the earliest-settled one wins
    // and no later tie can displace it.
    let private dijkstra
        (neighbourhood: Neighbourhood)
        (cost: Cell -> int)
        (stepWeight: int -> Cell -> int -> int64)
        (admit: int64 -> bool)
        (maxVisited: int)
        (seeds: Cell list)
        : Map<Cell, Step> =
        let isWalkable c = cost c > 0

        let rec loop
            (openSet: Set<int64 * int * int>)
            (tentative: Map<Cell, int64>)
            (cameFrom: Map<Cell, Cell>)
            (settled: Map<Cell, Step>)
            (expansions: int)
            : Map<Cell, Step> =
            if Set.isEmpty openSet || expansions >= maxVisited then
                settled
            else
                let (d, col, row) = Set.minElement openSet
                let current = { Col = col; Row = row }
                let openSet = Set.remove (d, col, row) openSet
                // Popped with the minimum key and all weights > 0 => `d` is final for `current`.
                // A seed has no predecessor, so `CameFrom` is None for exactly the seeds.
                let settled =
                    Map.add
                        current
                        { Cost = int d
                          CameFrom = Map.tryFind current cameFrom }
                        settled
                // `cost` is arbitrary caller code; evaluate it once per pop, not once per neighbour.
                let currentCost = cost current

                let (openSet, tentative, cameFrom) =
                    neighbours neighbourhood isWalkable current
                    |> List.fold
                        (fun (os, tent, cf) (struct (n, baseStep)) ->
                            let candidate = d + stepWeight currentCost n baseStep

                            if candidate > int64 System.Int32.MaxValue || not (admit candidate) then
                                (os, tent, cf)
                            else
                                match Map.tryFind n tent with
                                | Some existing when existing <= candidate -> (os, tent, cf)
                                | prior ->
                                    // Drop any stale open entry for n (old distance) before re-adding.
                                    let os =
                                        match prior with
                                        | Some oldD -> Set.remove (oldD, n.Col, n.Row) os
                                        | None -> os

                                    (Set.add (candidate, n.Col, n.Row) os,
                                     Map.add n candidate tent,
                                     Map.add n current cf))
                        (openSet, tentative, cameFrom)

                loop openSet tentative cameFrom settled (expansions + 1)

        // Only cells that are actually settled are returned, so a `maxVisited` cut-off yields a
        // partial-but-correct field rather than a field with unfinalised tentative values in it.
        let seeds = seeds |> List.filter isWalkable |> List.distinct

        if maxVisited <= 0 || List.isEmpty seeds then
            Map.empty
        else
            let openSet = seeds |> List.map (fun g -> (0L, g.Col, g.Row)) |> Set.ofList
            let tentative = seeds |> List.map (fun g -> g, 0L) |> Map.ofList
            loop openSet tentative Map.empty Map.empty 0

    // The cost-only projection of a settled field. `distanceField`/`reachableWithin` predate the
    // `Step` tree and are defined in terms of it, so they cannot drift from `reachable`.
    let private costsOf (settled: Map<Cell, Step>) : Map<Cell, int> =
        settled |> Map.map (fun _ s -> s.Cost)

    let distanceField
        (neighbourhood: Neighbourhood)
        (maxVisited: int)
        (cost: Cell -> int)
        (goals: Cell list)
        : Map<Cell, int> =
        // Reverse walk: expanding goal-ward cell `current` to `n` means the AGENT steps n -> current,
        // i.e. it enters `current`. So the weight is `baseStep * cost current`.
        // The `CameFrom` tree of a reverse, multi-source walk points goal-ward and means something quite
        // different from `reachable`'s, so it is not exposed — only the costs are.
        //
        // KNOWN COST, deliberately paid: this walk therefore builds a predecessor map it throws away (a
        // `Map.add` per relaxation, a `Step` per settle). The alternative is a second Dijkstra, or a
        // flag threading "don't track" through this one — and a second engine is exactly how the cost
        // map and the move range drift apart, which IS #212. One engine, one determinism guarantee,
        // one place to be wrong. If a profile ever shows this on a hot path, add the flag then, with a
        // number to justify it — not before.
        let stepWeight (currentCost: int) (_n: Cell) (baseStep: int) = int64 baseStep * int64 currentCost
        dijkstra neighbourhood cost stepWeight (fun _ -> true) maxVisited goals |> costsOf

    // The forward, budgeted walk that BOTH `reachable` and `reachableWithin` are views of — so the move
    // range and the cost map are one search and cannot disagree about what is in budget, which is the
    // whole of #212. `reachable` adds the `Endable` split on top; `reachableWithin` projects the costs
    // out. Neither pays for the other's work.
    let private settle
        (neighbourhood: Neighbourhood)
        (maxVisited: int)
        (cost: Cell -> int)
        (budget: int)
        (start: Cell)
        : Map<Cell, Step> =
        if budget < 0 then
            Map.empty
        else
            // Forward walk: the agent steps current -> n, entering `n`. Weight is `baseStep * cost n`.
            let stepWeight (_currentCost: int) (n: Cell) (baseStep: int) = int64 baseStep * int64 (cost n)
            // `budget` prunes, but it does NOT bound: over an unbounded `cost` predicate the reachable
            // set grows quadratically in `budget`, so `maxVisited` is what makes the walk terminate —
            // the same bound, for the same reason, as `astar`/`bfs`/`distanceField`.
            let admit (candidate: int64) = candidate <= int64 budget
            dijkstra neighbourhood cost stepWeight admit maxVisited [ start ]

    let reachable
        (neighbourhood: Neighbourhood)
        (maxVisited: int)
        (cost: Cell -> int)
        (canEndOn: Cell -> bool)
        (budget: int)
        (start: Cell)
        : Reach =
        let steps = settle neighbourhood maxVisited cost budget start

        // `canEndOn` narrows the DESTINATIONS; it never narrows `Steps`. That split is the point:
        // a cell held by an ally is traversable and not endable, and one predicate cannot say so.
        // Applied to `start` too, with no exception — a `canEndOn` meaning "unoccupied" excludes the
        // unit's own cell, and whether standing still is legal is the game's call, not ours.
        let endable =
            steps |> Map.fold (fun acc c _ -> if canEndOn c then Set.add c acc else acc) Set.empty

        { Steps = steps; Endable = endable }

    let reachableWithin
        (neighbourhood: Neighbourhood)
        (maxVisited: int)
        (cost: Cell -> int)
        (budget: int)
        (start: Cell)
        : Map<Cell, int> =
        // The same settled field `reachable` returns, with the predecessors dropped. Deliberately NOT
        // routed through `reachable`: that would fold an `Endable` set over every settled cell (with
        // `canEndOn` constantly true) and then discard it — and this is the primitive the docstring
        // sends AI threat overlays and candidate-cell scoring to.
        settle neighbourhood maxVisited cost budget start |> costsOf

    let pathTo (reach: Reach) (dest: Cell) : Cell list option =
        // Walk the CameFrom chain back to the seed (the one cell whose CameFrom is None). Reads `Steps`,
        // NOT `Endable`: a legal route may pass THROUGH a cell it may not stop on, so reconstructing
        // from the filtered set would dead-end on exactly the routes `canEndOn` exists to allow.
        let rec walk acc c =
            match Map.tryFind c reach.Steps with
            | Some { CameFrom = Some prev } -> walk (c :: acc) prev
            | _ -> c :: acc // the seed (CameFrom = None); the tree has no other roots.

        if Map.containsKey dest reach.Steps then
            Some(walk [] dest)
        else
            None

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
