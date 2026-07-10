module Game.Core.Tests.PathfindingTests

// Ported from FS.GG.Rendering tests/Canvas.Tests/PathfindingTests.fs (ADR-0022 / P2). Only the
// namespace open changes (FS.GG.UI.Canvas → FS.GG.Game.Core). Deterministic grid pathfinding
// (A* / BFS) over a walkability predicate: 4/8-neighbour, integer 10/14 costs, total (f,h,Col,Row)
// tie-break, no corner-cutting, maxVisited bound, endpoint-inclusive.

open Expecto
open FsCheck
open FS.GG.Game.Core

// Walkability over a bounded w×h grid minus a blocked set (the predicate IS the map).
let private gridWalkable (w: int) (h: int) (blocked: Set<int * int>) (c: Cell) =
    c.Col >= 0
    && c.Col < w
    && c.Row >= 0
    && c.Row < h
    && not (blocked.Contains(c.Col, c.Row))

// Cost over a bounded w×h grid: 0 (impassable) off-grid or blocked, 3 on "mud", else 1. `cost c > 0`
// IS the walkability predicate, so one terrain function drives astar/bfs and the Dijkstra fields.
let private gridCost (w: int) (h: int) (blocked: Set<int * int>) (mud: Set<int * int>) (c: Cell) =
    if not (gridWalkable w h blocked c) then 0
    elif mud.Contains(c.Col, c.Row) then 3
    else 1

// The integer cost astar itself accumulates over a returned path: 10 orthogonal, 14 diagonal.
let private pathCost (path: Cell list) =
    path
    |> List.pairwise
    |> List.sumBy (fun (a, b) -> if a.Col <> b.Col && a.Row <> b.Row then 14 else 10)

// Follow the flow field from `c`, at most `fuel` steps, and report where it comes to rest.
let private rollDownhill (flow: Map<Cell, Cell>) (fuel: int) (c: Cell) =
    let rec walk n c =
        if n <= 0 then None
        else
            match Map.tryFind c flow with
            | Some next -> walk (n - 1) next
            | None -> Some c // a sink: the goal (or a local minimum)

    walk fuel c

// A returned path must: start at `start`, end at `goal`, be all-walkable, and step only between
// adjacent (≤1 in each axis) cells.
let private validPath (isWalkable: Cell -> bool) (start: Cell) (goal: Cell) (path: Cell list) =
    match path with
    | [] -> false
    | _ ->
        List.head path = start
        && List.last path = goal
        && List.forall isWalkable path
        && path
           |> List.pairwise
           |> List.forall (fun (a, b) ->
               abs (a.Col - b.Col) <= 1 && abs (a.Row - b.Row) <= 1 && (a.Col <> b.Col || a.Row <> b.Row))

[<Tests>]
let tests =
    testList "Game.Core Pathfinding (US1, FR-001..FR-005)" [

        test "astar FourWay finds a straight corridor path, endpoints included" {
            let walk = gridWalkable 4 1 Set.empty
            let path = Pathfinding.astar FourWay 1000 walk { Col = 0; Row = 0 } { Col = 3; Row = 0 }
            Expect.equal
                path
                (Some [ { Col = 0; Row = 0 }; { Col = 1; Row = 0 }; { Col = 2; Row = 0 }; { Col = 3; Row = 0 } ])
                "start..goal inclusive straight path"
        }

        test "astar EightWay takes the strictly-cheaper diagonal on an open grid" {
            let walk = gridWalkable 4 4 Set.empty
            let path = Pathfinding.astar EightWay 1000 walk { Col = 0; Row = 0 } { Col = 3; Row = 3 }
            Expect.equal
                path
                (Some [ { Col = 0; Row = 0 }; { Col = 1; Row = 1 }; { Col = 2; Row = 2 }; { Col = 3; Row = 3 } ])
                "diagonal (cost 14) beats stair-stepping (cost 20 per corner)"
        }

        test "astar FourWay cannot use diagonals (longer Manhattan path)" {
            let walk = gridWalkable 4 4 Set.empty
            let path = Pathfinding.astar FourWay 1000 walk { Col = 0; Row = 0 } { Col = 3; Row = 3 }
            Expect.equal (path |> Option.map List.length) (Some 7) "6 orthogonal moves ⇒ 7 cells"
            Expect.isTrue (path |> Option.get |> validPath walk { Col = 0; Row = 0 } { Col = 3; Row = 3 }) "valid"
        }

        test "astar EightWay refuses to cut the corner past a blocked orthogonal (D5)" {
            // (1,0) blocked ⇒ the (0,0)->(1,1) diagonal is refused; must go via (0,1).
            let walk = gridWalkable 3 3 (Set.ofList [ (1, 0) ])
            let path = Pathfinding.astar EightWay 1000 walk { Col = 0; Row = 0 } { Col = 1; Row = 1 }
            Expect.equal
                path
                (Some [ { Col = 0; Row = 0 }; { Col = 0; Row = 1 }; { Col = 1; Row = 1 } ])
                "no corner-cut ⇒ routes around, length 3 not the direct diagonal length 2"
        }

        test "astar routes around a wall with a single gap" {
            // 5×5, wall at Col=2 rows 0..3, gap at (2,4).
            let blocked = Set.ofList [ for r in 0..3 -> (2, r) ]
            let walk = gridWalkable 5 5 blocked
            let start = { Col = 0; Row = 0 }
            let goal = { Col = 4; Row = 0 }
            let path = Pathfinding.astar EightWay 5000 walk start goal
            Expect.isSome path "a path through the gap exists"
            Expect.isTrue (validPath walk start goal (Option.get path)) "valid walkable adjacent path"
            Expect.isFalse (Option.get path |> List.exists (fun c -> blocked.Contains(c.Col, c.Row))) "never enters the wall"
        }

        test "start = goal (walkable) yields the single-cell path" {
            let walk = gridWalkable 3 3 Set.empty
            Expect.equal (Pathfinding.astar FourWay 100 walk { Col = 1; Row = 1 } { Col = 1; Row = 1 }) (Some [ { Col = 1; Row = 1 } ]) "[start]"
            Expect.equal (Pathfinding.bfs EightWay 100 walk { Col = 1; Row = 1 } { Col = 1; Row = 1 }) (Some [ { Col = 1; Row = 1 } ]) "[start]"
        }

        test "a blocked start or goal yields None" {
            let walk = gridWalkable 3 3 (Set.ofList [ (0, 0); (2, 2) ])
            Expect.isNone (Pathfinding.astar FourWay 100 walk { Col = 0; Row = 0 } { Col = 1; Row = 1 }) "blocked start"
            Expect.isNone (Pathfinding.astar FourWay 100 walk { Col = 1; Row = 1 } { Col = 2; Row = 2 }) "blocked goal"
        }

        test "a walled-off goal yields None (bounded search terminates)" {
            // goal (4,5) fully enclosed by a ring of blocked cells (8-connected).
            let ring = Set.ofList [ (3, 4); (4, 4); (5, 4); (3, 5); (5, 5); (3, 6); (4, 6); (5, 6) ]
            let walk = gridWalkable 8 8 ring
            let path = Pathfinding.astar EightWay 5000 walk { Col = 0; Row = 0 } { Col = 4; Row = 5 }
            Expect.isNone path "goal enclosed by a wall ring ⇒ None"
        }

        test "maxVisited <= 0 yields None" {
            let walk = gridWalkable 4 4 Set.empty
            Expect.isNone (Pathfinding.astar FourWay 0 walk { Col = 0; Row = 0 } { Col = 3; Row = 3 }) "zero budget"
            Expect.isNone (Pathfinding.bfs FourWay -1 walk { Col = 0; Row = 0 } { Col = 3; Row = 3 }) "negative budget"
        }

        test "an unreachable-within-budget goal terminates at the cap with None" {
            // large open grid, tiny budget → cannot reach a far goal.
            let walk = gridWalkable 100 100 Set.empty
            Expect.isNone (Pathfinding.astar FourWay 3 walk { Col = 0; Row = 0 } { Col = 99; Row = 99 }) "budget exhausted ⇒ None"
        }

        test "bfs returns a hop-minimal path" {
            let walk = gridWalkable 5 1 Set.empty
            let path = Pathfinding.bfs FourWay 1000 walk { Col = 0; Row = 0 } { Col = 4; Row = 0 }
            Expect.equal (path |> Option.map List.length) (Some 5) "4 hops ⇒ 5 cells"
            Expect.isTrue (path |> Option.get |> validPath walk { Col = 0; Row = 0 } { Col = 4; Row = 0 }) "valid"
        }

        test "two equal-cost routes still yield one stable path (repeat-run byte-identity)" {
            // FourWay (0,0)->(1,1): right-then-up and up-then-right both cost 20.
            let walk = gridWalkable 2 2 Set.empty
            let a = Pathfinding.astar FourWay 100 walk { Col = 0; Row = 0 } { Col = 1; Row = 1 }
            let b = Pathfinding.astar FourWay 100 walk { Col = 0; Row = 0 } { Col = 1; Row = 1 }
            Expect.equal a b "identical inputs ⇒ byte-identical path despite the tie"
            Expect.isTrue (a |> Option.get |> validPath walk { Col = 0; Row = 0 } { Col = 1; Row = 1 }) "and it is a valid path"
        }

        testCase "determinism: repeat calls are byte-identical over random grids (FsCheck)" <| fun () ->
            let prop (blockedRaw: (int * int) list) (sc: int) (sr: int) (gc: int) (gr: int) =
                let blocked = blockedRaw |> List.map (fun (c, r) -> (((abs c) % 8), ((abs r) % 8))) |> Set.ofList
                let walk = gridWalkable 8 8 blocked
                let start = { Col = (abs sc) % 8; Row = (abs sr) % 8 }
                let goal = { Col = (abs gc) % 8; Row = (abs gr) % 8 }
                let p1 = Pathfinding.astar EightWay 2000 walk start goal
                let p2 = Pathfinding.astar EightWay 2000 walk start goal
                // deterministic, and any Some result is a genuinely valid path
                p1 = p2 && (match p1 with Some path -> validPath walk start goal path | None -> true)
            Check.One(Config.QuickThrowOnFailure.WithMaxTest 500, prop)

        testCase "bfs determinism over random grids (FsCheck)" <| fun () ->
            let prop (blockedRaw: (int * int) list) (sc: int) (gc: int) =
                let blocked = blockedRaw |> List.map (fun (c, r) -> (((abs c) % 6), ((abs r) % 6))) |> Set.ofList
                let walk = gridWalkable 6 6 blocked
                let start = { Col = (abs sc) % 6; Row = 0 }
                let goal = { Col = (abs gc) % 6; Row = 5 }
                Pathfinding.bfs FourWay 2000 walk start goal = Pathfinding.bfs FourWay 2000 walk start goal
            Check.One(Config.QuickThrowOnFailure.WithMaxTest 500, prop)

        // -----------------------------------------------------------------------------------------
        // distanceField / flowField / reachableWithin (§6, §7 of the navigation design)

        test "distanceField is a golden integration field over a corridor" {
            let cost = gridCost 4 1 Set.empty Set.empty
            let field = Pathfinding.distanceField FourWay 1000 cost [ { Col = 0; Row = 0 } ]
            Expect.equal
                field
                (Map.ofList [ { Col = 0; Row = 0 }, 0; { Col = 1; Row = 0 }, 10; { Col = 2; Row = 0 }, 20; { Col = 3; Row = 0 }, 30 ])
                "goal = 0, then 10 per orthogonal step away"
        }

        test "distanceField charges the entered cell's cost (mud is dear to cross, not to stand on)" {
            // Corridor (0,0)..(2,0), goal (0,0), mud at (1,0). An agent at (1,0) steps onto (0,0): 10.
            // An agent at (2,0) steps onto mud (3×10) then onto (0,0) (10): 40.
            let cost = gridCost 3 1 Set.empty (Set.ofList [ (1, 0) ])
            let field = Pathfinding.distanceField FourWay 1000 cost [ { Col = 0; Row = 0 } ]
            Expect.equal (Map.tryFind { Col = 1; Row = 0 } field) (Some 10) "standing on mud is free; leaving it onto clear ground costs 10"
            Expect.equal (Map.tryFind { Col = 2; Row = 0 } field) (Some 40) "crossing mud costs 3×10, then 10 onto the goal"
        }

        test "distanceField takes the nearest of several goals" {
            let cost = gridCost 5 1 Set.empty Set.empty
            let field = Pathfinding.distanceField FourWay 1000 cost [ { Col = 0; Row = 0 }; { Col = 4; Row = 0 } ]
            Expect.equal (Map.tryFind { Col = 1; Row = 0 } field) (Some 10) "nearest goal is (0,0)"
            Expect.equal (Map.tryFind { Col = 3; Row = 0 } field) (Some 10) "nearest goal is (4,0)"
            Expect.equal (Map.tryFind { Col = 2; Row = 0 } field) (Some 20) "equidistant midpoint"
        }

        test "distanceField excludes cells walled off from every goal" {
            // (1,0) blocked splits the 3×1 corridor; (2,0) can never reach the goal (0,0).
            let cost = gridCost 3 1 (Set.ofList [ (1, 0) ]) Set.empty
            let field = Pathfinding.distanceField FourWay 1000 cost [ { Col = 0; Row = 0 } ]
            Expect.equal field (Map.ofList [ { Col = 0; Row = 0 }, 0 ]) "only the goal itself is reachable"
        }

        test "distanceField totality: empty goals, all-impassable goals, and maxVisited <= 0" {
            let cost = gridCost 4 4 Set.empty Set.empty
            Expect.equal (Pathfinding.distanceField FourWay 1000 cost []) Map.empty "no goals ⇒ empty field"
            Expect.equal (Pathfinding.distanceField FourWay 0 cost [ { Col = 0; Row = 0 } ]) Map.empty "zero budget"
            Expect.equal (Pathfinding.distanceField FourWay -1 cost [ { Col = 0; Row = 0 } ]) Map.empty "negative budget"
            // An impassable goal is skipped, not thrown — and a passable sibling still seeds the field.
            let blocked = gridCost 4 4 (Set.ofList [ (0, 0) ]) Set.empty
            Expect.equal (Pathfinding.distanceField FourWay 1000 blocked [ { Col = 0; Row = 0 } ]) Map.empty "every goal impassable ⇒ empty"
            let mixed = Pathfinding.distanceField FourWay 1000 blocked [ { Col = 0; Row = 0 }; { Col = 3; Row = 3 } ]
            Expect.equal (Map.tryFind { Col = 3; Row = 3 } mixed) (Some 0) "the passable goal still seeds"
            Expect.isFalse (Map.containsKey { Col = 0; Row = 0 } mixed) "the impassable goal is skipped"
        }

        test "distanceField honours maxVisited and returns only settled (final) values" {
            // Unbounded cell space: `cost` is 1 everywhere, so only maxVisited stops the walk.
            let field = Pathfinding.distanceField FourWay 5 (fun _ -> 1) [ { Col = 0; Row = 0 } ]
            Expect.equal (Map.count field) 5 "exactly maxVisited cells are settled"
            Expect.equal (Map.tryFind { Col = 0; Row = 0 } field) (Some 0) "the goal is settled first"
            // Every settled value is final: it equals the true unbounded-field value for that cell.
            let full = Pathfinding.distanceField FourWay 1000 (fun _ -> 1) [ { Col = 0; Row = 0 } ]
            for KeyValue(c, v) in field do
                Expect.equal (Map.tryFind c full) (Some v) "a settled value is never a tentative one"
        }

        test "distanceField cost accumulation cannot overflow into a negative distance" {
            // A huge per-cell cost would wrap int32 if accumulated naively; such cells are unreachable.
            let cost = gridCost 40 1 Set.empty Set.empty
            let huge c = if c = { Col = 0; Row = 0 } then 1 else 300_000_000 * (cost c)
            let field = Pathfinding.distanceField FourWay 1000 huge [ { Col = 0; Row = 0 } ]
            Expect.all (field |> Map.toList |> List.map snd) (fun v -> v >= 0) "no negative (wrapped) distances"
            Expect.equal (Map.tryFind { Col = 1; Row = 0 } field) (Some 10) "one step onto the cheap goal is fine"
            Expect.isFalse (Map.containsKey { Col = 2; Row = 0 } field) "a step that would exceed Int32.MaxValue is unreachable"
        }

        test "flowField points every cell at its lower neighbour, and omits the sink" {
            let cost = gridCost 4 1 Set.empty Set.empty
            let field = Pathfinding.distanceField FourWay 1000 cost [ { Col = 0; Row = 0 } ]
            let flow = Pathfinding.flowField FourWay field
            Expect.equal
                flow
                (Map.ofList [ { Col = 1; Row = 0 }, { Col = 0; Row = 0 }; { Col = 2; Row = 0 }, { Col = 1; Row = 0 }; { Col = 3; Row = 0 }, { Col = 2; Row = 0 } ])
                "each cell steps downhill; the goal is a sink and carries no arrow"
            Expect.isFalse (Map.containsKey { Col = 0; Row = 0 } flow) "the goal has no strictly-lower neighbour"
        }

        test "flowField totality: an empty field yields an empty flow" {
            Expect.equal (Pathfinding.flowField EightWay Map.empty) Map.empty "empty ⇒ empty"
        }

        test "flowField gives no arrow to an equal-valued neighbour (adjacent goals are both sinks)" {
            // Two adjacent goals both hold 0. A `<=` downhill rule would point them at each other:
            // an agent standing on a goal would be told to step off it, and rolling downhill would
            // cycle forever instead of reporting arrival. Only a STRICTLY lower neighbour is downhill.
            let cost = gridCost 4 1 Set.empty Set.empty
            let field = Pathfinding.distanceField FourWay 1000 cost [ { Col = 0; Row = 0 }; { Col = 1; Row = 0 } ]
            Expect.equal (Map.tryFind { Col = 0; Row = 0 } field) (Some 0) "both goals hold 0"
            Expect.equal (Map.tryFind { Col = 1; Row = 0 } field) (Some 0) "both goals hold 0"

            let flow = Pathfinding.flowField FourWay field
            Expect.isFalse (Map.containsKey { Col = 0; Row = 0 } flow) "a goal with an equal-valued neighbour is still a sink"
            Expect.isFalse (Map.containsKey { Col = 1; Row = 0 } flow) "a goal with an equal-valued neighbour is still a sink"
            Expect.equal (Map.tryFind { Col = 2; Row = 0 } flow) (Some { Col = 1; Row = 0 }) "non-goals still step downhill"
            Expect.equal (rollDownhill flow 8 { Col = 3; Row = 0 }) (Some { Col = 1; Row = 0 }) "rolling downhill arrives, it does not cycle"
        }

        test "flowField gives no arrow across a plateau of equal values" {
            // A hand-built field (not a distanceField): (0,0) and (1,0) tie at 5, with no lower cell.
            let field = Map.ofList [ { Col = 0; Row = 0 }, 5; { Col = 1; Row = 0 }, 5 ]
            Expect.equal (Pathfinding.flowField FourWay field) Map.empty "a plateau is all sinks — never a cycle"
        }

        test "flowField refuses to cut a corner past a cell absent from the field" {
            // (1,0) blocked ⇒ absent from the field ⇒ the (0,0)->(1,1) diagonal is not offered.
            let cost = gridCost 2 2 (Set.ofList [ (1, 0) ]) Set.empty
            let field = Pathfinding.distanceField EightWay 1000 cost [ { Col = 1; Row = 1 } ]
            let flow = Pathfinding.flowField EightWay field
            Expect.isFalse (Map.containsKey { Col = 1; Row = 0 } field) "the blocked cell is not in the field"
            Expect.equal (Map.tryFind { Col = 0; Row = 0 } flow) (Some { Col = 0; Row = 1 }) "routes around, never diagonally past the missing corner"
        }

        test "reachableWithin is the move range, start inclusive and free" {
            let cost = gridCost 5 1 Set.empty Set.empty
            let range = Pathfinding.reachableWithin FourWay cost 20 { Col = 0; Row = 0 }
            Expect.equal
                range
                (Map.ofList [ { Col = 0; Row = 0 }, 0; { Col = 1; Row = 0 }, 10; { Col = 2; Row = 0 }, 20 ])
                "start costs nothing; two orthogonal steps fit a budget of 20"
        }

        test "reachableWithin charges the cell stepped onto (the forward direction)" {
            // Mud at (1,0): entering it costs 3×10. distanceField, walking goal-ward, prices it 10.
            let cost = gridCost 3 1 Set.empty (Set.ofList [ (1, 0) ])
            let range = Pathfinding.reachableWithin FourWay cost 100 { Col = 0; Row = 0 }
            Expect.equal (Map.tryFind { Col = 1; Row = 0 } range) (Some 30) "stepping onto mud costs 3×10"
            Expect.equal (Map.tryFind { Col = 2; Row = 0 } range) (Some 40) "then 10 onto clear ground"
            let field = Pathfinding.distanceField FourWay 1000 cost [ { Col = 0; Row = 0 } ]
            Expect.notEqual (Map.tryFind { Col = 1; Row = 0 } range) (Map.tryFind { Col = 1; Row = 0 } field) "the two directions disagree on value under non-uniform cost"
            Expect.equal (range |> Map.toList |> List.map fst) (field |> Map.toList |> List.map fst) "...but agree on which cells are reachable"
        }

        test "reachableWithin totality: a negative budget or an impassable start yields an empty map" {
            let cost = gridCost 4 4 (Set.ofList [ (0, 0) ]) Set.empty
            Expect.equal (Pathfinding.reachableWithin FourWay cost -1 { Col = 1; Row = 1 }) Map.empty "negative budget"
            Expect.equal (Pathfinding.reachableWithin FourWay cost 100 { Col = 0; Row = 0 }) Map.empty "impassable start"
            Expect.equal (Pathfinding.reachableWithin FourWay cost 0 { Col = 1; Row = 1 }) (Map.ofList [ { Col = 1; Row = 1 }, 0 ]) "zero budget ⇒ stand still"
        }

        testCase "distanceField agrees with astar's path cost for every reachable cell (FsCheck)" <| fun () ->
            // The optimality cross-check: with a uniform cost of 1, the field value at `c` is exactly
            // the cost of the astar path from `c` to the goal.
            let prop (blockedRaw: (int * int) list) (gc: int) (gr: int) =
                let blocked = blockedRaw |> List.map (fun (c, r) -> (((abs c) % 6), ((abs r) % 6))) |> Set.ofList
                let goal = { Col = (abs gc) % 6; Row = (abs gr) % 6 }
                let cost = gridCost 6 6 blocked Set.empty
                let walk = gridWalkable 6 6 blocked

                if not (walk goal) then
                    true
                else
                    let field = Pathfinding.distanceField EightWay 5000 cost [ goal ]

                    field
                    |> Map.forall (fun c v ->
                        match Pathfinding.astar EightWay 5000 walk c goal with
                        | Some path -> pathCost path = v
                        | None -> false) // in the field ⇒ astar must find a path

            Check.One(Config.QuickThrowOnFailure.WithMaxTest 300, prop)

        testCase "a cell is in the distanceField exactly when astar can reach the goal (FsCheck)" <| fun () ->
            let prop (blockedRaw: (int * int) list) (gc: int) (gr: int) =
                let blocked = blockedRaw |> List.map (fun (c, r) -> (((abs c) % 5), ((abs r) % 5))) |> Set.ofList
                let goal = { Col = (abs gc) % 5; Row = (abs gr) % 5 }
                let cost = gridCost 5 5 blocked Set.empty
                let walk = gridWalkable 5 5 blocked

                if not (walk goal) then
                    true
                else
                    let field = Pathfinding.distanceField FourWay 5000 cost [ goal ]

                    [ for col in 0..4 do
                          for row in 0..4 -> { Col = col; Row = row } ]
                    |> List.forall (fun c ->
                        let inField = Map.containsKey c field
                        let reaches = (Pathfinding.astar FourWay 5000 walk c goal).IsSome
                        inField = reaches)

            Check.One(Config.QuickThrowOnFailure.WithMaxTest 300, prop)

        testCase "reachableWithin equals distanceField filtered by budget, under uniform cost (FsCheck)" <| fun () ->
            // Uniform cost makes the graph symmetric, so the forward and goal-ward walks coincide.
            let prop (blockedRaw: (int * int) list) (sc: int) (sr: int) (b: int) =
                let blocked = blockedRaw |> List.map (fun (c, r) -> (((abs c) % 6), ((abs r) % 6))) |> Set.ofList
                let start = { Col = (abs sc) % 6; Row = (abs sr) % 6 }
                let budget = (abs b) % 80
                let cost = gridCost 6 6 blocked Set.empty

                if cost start <= 0 then
                    true
                else
                    let range = Pathfinding.reachableWithin EightWay cost budget start
                    let field = Pathfinding.distanceField EightWay 5000 cost [ start ]
                    range = (field |> Map.filter (fun _ v -> v <= budget))

            Check.One(Config.QuickThrowOnFailure.WithMaxTest 300, prop)

        testCase "reachableWithin is a subset of distanceField, under any cost (FsCheck)" <| fun () ->
            let prop (blockedRaw: (int * int) list) (mudRaw: (int * int) list) (sc: int) (sr: int) (b: int) =
                let blocked = blockedRaw |> List.map (fun (c, r) -> (((abs c) % 6), ((abs r) % 6))) |> Set.ofList
                let mud = mudRaw |> List.map (fun (c, r) -> (((abs c) % 6), ((abs r) % 6))) |> Set.ofList
                let start = { Col = (abs sc) % 6; Row = (abs sr) % 6 }
                let budget = (abs b) % 120
                let cost = gridCost 6 6 blocked mud

                if cost start <= 0 then
                    true
                else
                    let range = Pathfinding.reachableWithin EightWay cost budget start
                    let field = Pathfinding.distanceField EightWay 5000 cost [ start ]
                    range |> Map.forall (fun c _ -> Map.containsKey c field)

            Check.One(Config.QuickThrowOnFailure.WithMaxTest 300, prop)

        testCase "rolling downhill on the flow field always arrives at a goal (FsCheck)" <| fun () ->
            let prop (blockedRaw: (int * int) list) (gc: int) (gr: int) =
                let blocked = blockedRaw |> List.map (fun (c, r) -> (((abs c) % 6), ((abs r) % 6))) |> Set.ofList
                let goal = { Col = (abs gc) % 6; Row = (abs gr) % 6 }
                let cost = gridCost 6 6 blocked Set.empty

                if cost goal <= 0 then
                    true
                else
                    let field = Pathfinding.distanceField EightWay 5000 cost [ goal ]
                    let flow = Pathfinding.flowField EightWay field
                    // Every cell in the field rolls downhill to the single goal within 36 steps
                    // (the field has at most 36 cells, and each step strictly lowers the value).
                    field |> Map.forall (fun c _ -> rollDownhill flow 36 c = Some goal)

            Check.One(Config.QuickThrowOnFailure.WithMaxTest 300, prop)

        testCase "the fields are byte-identical across repeat runs over random terrain (FsCheck)" <| fun () ->
            // No Map/Set iteration-order or float tie-break leakage: multi-source, no early exit.
            let prop (blockedRaw: (int * int) list) (mudRaw: (int * int) list) (goalsRaw: (int * int) list) =
                let blocked = blockedRaw |> List.map (fun (c, r) -> (((abs c) % 7), ((abs r) % 7))) |> Set.ofList
                let mud = mudRaw |> List.map (fun (c, r) -> (((abs c) % 7), ((abs r) % 7))) |> Set.ofList
                let goals = goalsRaw |> List.map (fun (c, r) -> { Col = (abs c) % 7; Row = (abs r) % 7 })
                let cost = gridCost 7 7 blocked mud

                let f1 = Pathfinding.distanceField EightWay 5000 cost goals
                let f2 = Pathfinding.distanceField EightWay 5000 cost goals
                f1 = f2 && Pathfinding.flowField EightWay f1 = Pathfinding.flowField EightWay f2

            Check.One(Config.QuickThrowOnFailure.WithMaxTest 300, prop)
    ]
