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

// What the path ACTUALLY costs the unit: the same 10/14 baseStep, times the cost of the cell being
// entered. `pathCost` above is this with the terrain thrown away — which is exactly what `astar`
// prices, and exactly why an `astar` path can overrun a budget `reachableWithin` costed (#212). The
// start is not charged: you are already standing on it.
let private terrainPathCost (cost: Cell -> int) (path: Cell list) =
    path
    |> List.pairwise
    |> List.sumBy (fun (a, b) -> (if a.Col <> b.Col && a.Row <> b.Row then 14 else 10) * cost b)

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

        test "astar heuristic does not overflow on a far-apart goal (int64 deltas)" {
            // Regression (docs/reports/2026-07-15-code-and-architecture-review.md §3). `heuristic`
            // computed `abs (c.Col - goal.Col)` in `int`: for a goal near Int32.MinValue the `int`
            // subtraction WRAPS and `abs Int32.MinValue` THROWS, so astar's very first `h start` blew up
            // on a legal cell pair in the "unbounded integer cell space" the module advertises — a
            // totality violation. Widened to int64 (the hardening Los/Ai already carry), the estimate is
            // finite and admissible. The point of this test is that the call RETURNS rather than throws.
            let walk = fun _ -> true
            let start = { Col = 0; Row = 0 }
            let goal = { Col = System.Int32.MinValue; Row = 0 }
            // The goal is astronomically far, so a small budget exhausts `maxVisited` and reports
            // unreachable — which is the correct total answer, and the one the old code never reached.
            let path = Pathfinding.astar FourWay 50 walk start goal
            Expect.isNone path "far goal is unreachable within the budget — and reached without throwing"
        }

        test "astar finds an optimal path at extreme coordinates (int64 widening is sound)" {
            // The complement of the test above: the widening must fix the throw WITHOUT perturbing
            // normal operation at large magnitude. Both cells sit near Int32.MinValue but three apart on
            // an all-walkable grid, so the int64 g-score/heuristic must still yield the shortest route.
            let walk = fun _ -> true
            let b = System.Int32.MinValue + 1000 // extreme magnitude, clear of the exact MinValue corner
            let start = { Col = b; Row = b }
            let goal = { Col = b + 3; Row = b }
            match Pathfinding.astar FourWay 1000 walk start goal with
            | Some p ->
                Expect.isTrue (validPath walk start goal p) "a valid start..goal path"
                Expect.equal (pathCost p) 30 "3 orthogonal steps × baseStep 10 — the shortest route"
            | None -> failtest "expected a path between two near cells at extreme coordinates"
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

        test "reachableWithin costs the reachable set, start inclusive and free" {
            let cost = gridCost 5 1 Set.empty Set.empty
            let range = Pathfinding.reachableWithin FourWay 5000 cost 20 { Col = 0; Row = 0 }
            Expect.equal
                range
                (Map.ofList [ { Col = 0; Row = 0 }, 0; { Col = 1; Row = 0 }, 10; { Col = 2; Row = 0 }, 20 ])
                "start costs nothing; two orthogonal steps fit a budget of 20"
        }

        test "reachableWithin charges the cell stepped onto (the forward direction)" {
            // Mud at (1,0): entering it costs 3×10. distanceField, walking goal-ward, prices it 10.
            let cost = gridCost 3 1 Set.empty (Set.ofList [ (1, 0) ])
            let range = Pathfinding.reachableWithin FourWay 5000 cost 100 { Col = 0; Row = 0 }
            Expect.equal (Map.tryFind { Col = 1; Row = 0 } range) (Some 30) "stepping onto mud costs 3×10"
            Expect.equal (Map.tryFind { Col = 2; Row = 0 } range) (Some 40) "then 10 onto clear ground"
            let field = Pathfinding.distanceField FourWay 1000 cost [ { Col = 0; Row = 0 } ]
            Expect.notEqual (Map.tryFind { Col = 1; Row = 0 } range) (Map.tryFind { Col = 1; Row = 0 } field) "the two directions disagree on value under non-uniform cost"
            Expect.equal (range |> Map.toList |> List.map fst) (field |> Map.toList |> List.map fst) "...but agree on which cells are reachable"
        }

        test "reachableWithin totality: a negative budget, an impassable start, or maxVisited <= 0" {
            let cost = gridCost 4 4 (Set.ofList [ (0, 0) ]) Set.empty
            Expect.equal (Pathfinding.reachableWithin FourWay 5000 cost -1 { Col = 1; Row = 1 }) Map.empty "negative budget"
            Expect.equal (Pathfinding.reachableWithin FourWay 5000 cost 100 { Col = 0; Row = 0 }) Map.empty "impassable start"
            Expect.equal (Pathfinding.reachableWithin FourWay 0 cost 100 { Col = 1; Row = 1 }) Map.empty "zero maxVisited"
            Expect.equal (Pathfinding.reachableWithin FourWay -1 cost 100 { Col = 1; Row = 1 }) Map.empty "negative maxVisited"
            Expect.equal (Pathfinding.reachableWithin FourWay 5000 cost 0 { Col = 1; Row = 1 }) (Map.ofList [ { Col = 1; Row = 1 }, 0 ]) "zero budget ⇒ stand still"
        }

        test "reachableWithin terminates on an unbounded cost predicate (maxVisited, not budget, bounds it)" {
            // The framework holds no map, so `fun _ -> 1` is a legal terrain: every cell is walkable.
            // A budget of 100000 admits ~2e8 cells; only `maxVisited` keeps this from exhausting memory.
            let range = Pathfinding.reachableWithin FourWay 200 (fun _ -> 1) 100_000 { Col = 0; Row = 0 }
            Expect.equal (Map.count range) 200 "exactly maxVisited cells are settled"
            Expect.equal (Map.tryFind { Col = 0; Row = 0 } range) (Some 0) "the start is settled first, and costs nothing"
            Expect.all (range |> Map.toList |> List.map snd) (fun v -> v >= 0 && v <= 100_000) "every settled value is within budget"
        }

        // -----------------------------------------------------------------------------------------
        // reachable / pathTo — the budgeted, predecessor-keeping move range (#212, and §4.4 of
        // docs/TestSpecs/Games/turn-based-tactics.md, whose `Reach = {Steps; Endable}` this is).

        test "#212: the highlight and the path agree, where reachableWithin + astar did not" {
            // The trap, exactly as filed. Rough terrain at (1,0) costs 5; everything else costs 1.
            //
            //          col 0    col 1     col 2
            //   row 0  START    ROUGH(5)  DEST
            //   row 1  .        .         .
            //
            // Budget 40 = four 1-cost steps (baseStep 10 × cost 1). The cheapest route to DEST goes
            // the long way round the rough cell and costs exactly 40 — so DEST is in budget, and any
            // honest move range highlights it.
            let cost (c: Cell) =
                if c.Col < 0 || c.Col > 2 || c.Row < 0 || c.Row > 1 then 0
                elif c = { Col = 1; Row = 0 } then 5
                else 1

            let start = { Col = 0; Row = 0 }
            let dest = { Col = 2; Row = 0 }
            let budget = 40
            let reach = Pathfinding.reachable FourWay 5000 cost (fun _ -> true) budget start

            Expect.isTrue (Set.contains dest reach.Endable) "DEST is in budget, so it is highlighted"
            Expect.equal (reach.Steps.[dest].Cost) 40 "...at a cost of exactly the budget"

            // THE BUG: `astar` takes no cost function, so it minimises STEPS. The two-step route
            // straight through the rough cell is the fewest steps, and it costs 5×10 + 1×10 = 60 —
            // 50% over a budget the highlight already promised was affordable.
            let viaAstar = Pathfinding.astar FourWay 5000 (fun c -> cost c > 0) start dest
            Expect.equal viaAstar (Some [ start; { Col = 1; Row = 0 }; dest ]) "astar takes the rough shortcut"
            Expect.equal (terrainPathCost cost viaAstar.Value) 60 "which the unit cannot afford"
            Expect.isTrue (terrainPathCost cost viaAstar.Value > budget) "the old composition overruns the budget"

            // THE FIX: the path comes out of the same search that costed the highlight.
            let path = (Pathfinding.pathTo reach dest).Value

            Expect.equal
                path
                [ start; { Col = 0; Row = 1 }; { Col = 1; Row = 1 }; { Col = 2; Row = 1 }; dest ]
                "pathTo walks the cheapest route: around the rough cell"

            Expect.equal (terrainPathCost cost path) 40 "and it costs what the highlight said it would"
            Expect.isTrue (terrainPathCost cost path <= budget) "in budget, by construction"
        }

        test "you may path THROUGH an ally but not end ON them (§4.4's v1 rule)" {
            // The split one predicate cannot express: the ally's cell is traversable AND not endable.
            let ally = { Col = 1; Row = 0 }
            let cost = gridCost 3 1 Set.empty Set.empty
            let reach = Pathfinding.reachable FourWay 5000 cost (fun c -> c <> ally) 100 { Col = 0; Row = 0 }

            Expect.isTrue (Map.containsKey ally reach.Steps) "the ally's cell IS traversable, so it is in Steps"
            Expect.isFalse (Set.contains ally reach.Endable) "...and is NOT a legal destination"

            let dest = { Col = 2; Row = 0 }
            Expect.isTrue (Set.contains dest reach.Endable) "the cell beyond the ally is reachable"

            // Reconstruct from Steps, not Endable. Filter the thing you reconstruct from and this
            // path — the one the rule exists to allow — dead-ends on the ally.
            Expect.equal
                (Pathfinding.pathTo reach dest)
                (Some [ { Col = 0; Row = 0 }; ally; dest ])
                "and the route to it passes straight through the ally"
        }

        test "canEndOn narrows destinations only — including the unit's own cell, with no exception" {
            let cost = gridCost 3 1 Set.empty Set.empty
            let start = { Col = 0; Row = 0 }
            // A `canEndOn` meaning "unoccupied" excludes `start`: the unit is standing on it. Whether
            // standing still is a legal move is the game's call, so the framework does not guess.
            let occupied c = c = start
            let reach = Pathfinding.reachable FourWay 5000 cost (fun c -> not (occupied c)) 100 start

            Expect.isTrue (Map.containsKey start reach.Steps) "start is always settled"
            Expect.isFalse (Set.contains start reach.Endable) "but canEndOn is applied to it like any other cell"

            // The documented idiom for making standing still legal.
            let reach2 = Pathfinding.reachable FourWay 5000 cost (fun c -> c = start || not (occupied c)) 100 start
            Expect.isTrue (Set.contains start reach2.Endable) "`c = start || ...` puts it back"
        }

        test "pathTo totality: the start, an unsettled cell, and the CameFrom root" {
            let cost = gridCost 3 3 Set.empty Set.empty
            let start = { Col = 0; Row = 0 }
            let reach = Pathfinding.reachable FourWay 5000 cost (fun _ -> true) 20 start

            Expect.equal (Pathfinding.pathTo reach start) (Some [ start ]) "dest = start ⇒ a single-element path"
            Expect.equal (reach.Steps.[start].CameFrom) None "start is the one cell with no predecessor"

            Expect.equal (Pathfinding.pathTo reach { Col = 2; Row = 2 }) None "out of budget ⇒ never settled ⇒ None"
            Expect.equal (Pathfinding.pathTo reach { Col = 9; Row = 9 }) None "off the map ⇒ None"

            let settledButOne =
                reach.Steps |> Map.toList |> List.filter (fun (_, s) -> s.CameFrom.IsNone) |> List.length

            Expect.equal settledButOne 1 "the CameFrom tree has exactly one root, and it is the start"
        }

        test "reachable totality: a negative budget, an impassable start, or maxVisited <= 0" {
            let cost = gridCost 4 4 (Set.ofList [ (0, 0) ]) Set.empty
            let empty = { Pathfinding.Steps = Map.empty; Pathfinding.Endable = Set.empty }
            let go maxVisited budget start = Pathfinding.reachable FourWay maxVisited cost (fun _ -> true) budget start

            Expect.equal (go 5000 -1 { Col = 1; Row = 1 }) empty "negative budget ⇒ empty Steps AND empty Endable"
            Expect.equal (go 5000 100 { Col = 0; Row = 0 }) empty "impassable start"
            Expect.equal (go 0 100 { Col = 1; Row = 1 }) empty "zero maxVisited"
            Expect.equal (go -1 100 { Col = 1; Row = 1 }) empty "negative maxVisited"

            let standStill = go 5000 0 { Col = 1; Row = 1 }
            Expect.equal (standStill.Steps |> Map.toList |> List.map fst) [ { Col = 1; Row = 1 } ] "zero budget ⇒ stand still"
            Expect.equal standStill.Endable (Set.singleton { Col = 1; Row = 1 }) "...and it is endable"
        }

        test "EightWay + non-uniform cost: a settled cell's CameFrom must track the cost that WON" {
            // The one configuration in which a cell is improved AFTER it is first queued — and so the
            // one place a predecessor can go stale while its cost moves on. It needs BOTH a diagonal
            // (which makes the edge weight depend on the edge, not just the cell entered) AND a
            // non-uniform cost. Under FourWay, or under a uniform cost, a cell's first relaxation is
            // already its cheapest and this can never fire, which is exactly why it is easy to miss.
            //
            //   row 0:  START(1)  1
            //   row 1:  1         MUD(3)
            //
            // (1,1) is first reached DIAGONALLY from START: 14 × 3 = 42.
            // It is then improved ORTHOGONALLY via (1,0) or (0,1): 10 + (10 × 3) = 40.
            // Keep the cost but not the predecessor and the highlight says 40 while the path costs 42.
            let cost (c: Cell) =
                if c.Col < 0 || c.Col > 1 || c.Row < 0 || c.Row > 1 then 0
                elif c = { Col = 1; Row = 1 } then 3
                else 1

            let start = { Col = 0; Row = 0 }
            let mud = { Col = 1; Row = 1 }
            let reach = Pathfinding.reachable EightWay 5000 cost (fun _ -> true) 100 start

            Expect.equal (reach.Steps.[mud].Cost) 40 "the orthogonal route is cheaper than the diagonal one (40 < 42)"
            Expect.notEqual (reach.Steps.[mud].CameFrom) (Some start) "so CameFrom must NOT still point at the diagonal predecessor"

            let path = (Pathfinding.pathTo reach mud).Value
            Expect.equal (List.length path) 3 "the winning route is two orthogonal steps, not one diagonal"
            Expect.equal (terrainPathCost cost path) 40 "and the path costs exactly what the highlight promised"
        }

        testCase "every settled path costs exactly what the highlight promised, and fits the budget (FsCheck)" <| fun () ->
            // The property the whole primitive exists for, and the one #212's composition broke:
            // cost-consistency between the highlight and the path, BY CONSTRUCTION.
            //
            // Terrain comes from dense BITMASKS, not from FsCheck's default `(int * int) list` — that
            // generator yields the empty list far too often, so `mud` was usually empty, the cost was
            // usually uniform, and the EightWay-with-mud case (the only one where a cell is improved
            // after it is queued — see the test above) was reached only by luck. A stale-predecessor
            // mutant survived 300 runs of the list-generator version of this property.
            let prop (mudRaw: int) (blockedRaw: int) (b: int) (eightWay: bool) =
                let bitAt (mask: int) (c: Cell) = (mask >>> (c.Row * 5 + c.Col)) &&& 1 = 1
                // Mask, never `abs`: `abs Int32.MinValue` throws, and FsCheck may hand us one.
                let mudMask = (mudRaw &&& 0x1FFFFFF) ||| 2 // never 0: (1,0) is mud at minimum
                let blockedMask = blockedRaw &&& 0x1FFFFFF
                let start = { Col = 0; Row = 0 }

                let cost (c: Cell) =
                    if c.Col < 0 || c.Col > 4 || c.Row < 0 || c.Row > 4 then 0
                    elif bitAt blockedMask c then 0
                    elif bitAt mudMask c then 3
                    else 1

                let nb = if eightWay then EightWay else FourWay
                let budget = 30 + (b &&& 0x7F)

                if cost start <= 0 then
                    true
                else
                    // An arbitrary endable rule, to prove `canEndOn` never leaks into costs or paths.
                    let canEndOn (c: Cell) = (c.Col + c.Row) % 3 <> 1
                    let reach = Pathfinding.reachable nb 5000 cost canEndOn budget start

                    // Checked over EVERY settled cell, not just the endable ones: a path routes THROUGH
                    // pass-through cells, so their predecessors must be sound too.
                    let consistent =
                        reach.Steps
                        |> Map.forall (fun dest step ->
                            match Pathfinding.pathTo reach dest with
                            | None -> false // settled ⇒ pathTo MUST reconstruct
                            | Some path ->
                                // The route the unit walks costs exactly what the highlight was costed
                                // at, and that is within budget. astar cannot give you this.
                                terrainPathCost cost path = step.Cost
                                && step.Cost <= budget
                                && List.head path = start
                                && List.last path = dest
                                && path |> List.forall (fun c -> cost c > 0))

                    // Endable is exactly the canEndOn subset of Steps — it never adds or reprices.
                    let endableIsASubset =
                        reach.Endable = (reach.Steps |> Map.toList |> List.map fst |> List.filter canEndOn |> Set.ofList)

                    consistent && endableIsASubset

            Check.One(Config.QuickThrowOnFailure.WithMaxTest 500, prop)

        testCase "reachableWithin is exactly reachable's Steps with the predecessors dropped (FsCheck)" <| fun () ->
            // One engine, so the cost map and the move range can never drift apart about what is in
            // budget — which is the failure #212 is a special case of.
            let prop (mudRaw: int) (blockedRaw: int) (b: int) (eightWay: bool) =
                let bitAt (mask: int) (c: Cell) = (mask >>> (c.Row * 5 + c.Col)) &&& 1 = 1
                // Mask, never `abs`: `abs Int32.MinValue` throws, and FsCheck may hand us one.
                let mudMask = (mudRaw &&& 0x1FFFFFF) ||| 2
                let blockedMask = blockedRaw &&& 0x1FFFFFF

                let cost (c: Cell) =
                    if c.Col < 0 || c.Col > 4 || c.Row < 0 || c.Row > 4 then 0
                    elif bitAt blockedMask c then 0
                    elif bitAt mudMask c then 3
                    else 1

                let nb = if eightWay then EightWay else FourWay
                let budget = 30 + (b &&& 0x7F)
                let start = { Col = 0; Row = 0 }
                let range = Pathfinding.reachableWithin nb 5000 cost budget start
                // `canEndOn` is irrelevant here: it must not touch Steps.
                let reach = Pathfinding.reachable nb 5000 cost (fun c -> c.Col % 2 = 0) budget start
                range = (reach.Steps |> Map.map (fun _ s -> s.Cost))

            Check.One(Config.QuickThrowOnFailure.WithMaxTest 500, prop)

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
                    let range = Pathfinding.reachableWithin EightWay 5000 cost budget start
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
                    let range = Pathfinding.reachableWithin EightWay 5000 cost budget start
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

        // ------------------------------------------------------------------------------------------
        // The `baseStep` scale, exported (#229). It was load-bearing and unexported, so every caller
        // hardcoded `10`. Getting it wrong fails SILENTLY and TOTALLY — nothing throws, `int` goes in
        // and `int` comes out — which is what these tests pin, from both ends.

        test "baseStep IS the scale the search charges: orthogonal costs it, diagonal 14/10 of it" {
            let cost = gridCost 3 3 Set.empty Set.empty
            let start = { Col = 0; Row = 0 }
            let reach = Pathfinding.reachable EightWay 5000 cost (fun _ -> true) (Pathfinding.budgetFor 3) start

            Expect.equal
                (Map.find { Col = 1; Row = 0 } reach.Steps).Cost
                Pathfinding.baseStep
                "an orthogonal move costs exactly baseStep"

            // Derived from baseStep in the implementation, not written as a second `14` — so the two
            // cannot drift apart if the scale ever changes.
            Expect.equal
                (Map.find { Col = 1; Row = 1 } reach.Steps).Cost
                (Pathfinding.baseStep * 14 / 10)
                "a diagonal costs baseStep * 14/10 — the integer √2, cheaper than the two orthogonals it replaces"
        }

        test "budgetFor scales a move range into baseStep units — and a RAW move range freezes the unit" {
            let cost = gridCost 9 9 Set.empty Set.empty
            let start = { Col = 4; Row = 4 }
            let moveRange = 4

            Expect.equal
                (Pathfinding.budgetFor moveRange)
                (Pathfinding.baseStep * moveRange)
                "budgetFor is baseStep * moveRange"

            // The #229 bug, pinned. Passing `moveRange` RAW is the obvious thing — the parameter is
            // called `budget`, and the unit's budget IS 4 — and every step already costs baseStep (10),
            // which exceeds 4. So the search settles the start cell and stops.
            let raw = Pathfinding.reachable FourWay 5000 cost (fun _ -> true) moveRange start
            Expect.equal (Map.count raw.Steps) 1 "a raw moveRange settles ONLY start — the silent, total freeze"

            Expect.equal
                (Set.toList raw.Endable)
                [ start ]
                "...and the highlight is the single cell the unit is standing on"

            // Scaled, the same unit gets its real range: the Manhattan diamond of radius 4 = 41 cells.
            let scaled =
                Pathfinding.reachable FourWay 5000 cost (fun _ -> true) (Pathfinding.budgetFor moveRange) start

            Expect.equal (Map.count scaled.Steps) 41 "budgetFor gives the unit the move range it actually has"
        }

        test "budgetFor saturates rather than wrapping — an overflow is the same freeze from the other end" {
            let cost = gridCost 5 5 Set.empty Set.empty
            let start = { Col = 2; Row = 2 }

            // `baseStep * Int32.MaxValue` overflows `int`. Wrapped, it lands NEGATIVE — and a negative
            // budget yields an EMPTY reach, indistinguishable from the frozen unit above.
            let huge = Pathfinding.budgetFor System.Int32.MaxValue
            Expect.equal huge System.Int32.MaxValue "clamped to Int32.MaxValue, not wrapped to a negative"

            let reach = Pathfinding.reachable FourWay 5000 cost (fun _ -> true) huge start
            Expect.equal (Map.count reach.Steps) 25 "a saturated budget reaches the whole grid, not nothing"
        }

        test "budgetFor preserves reachable's totality: negative stays empty, zero settles start alone" {
            let cost = gridCost 5 5 Set.empty Set.empty
            let start = { Col = 2; Row = 2 }

            Expect.isTrue (Pathfinding.budgetFor -1 < 0) "a negative moveRange is passed through negative"
            let negative = Pathfinding.reachable FourWay 5000 cost (fun _ -> true) (Pathfinding.budgetFor -1) start
            Expect.equal (Map.count negative.Steps) 0 "a negative budget yields an empty Steps"
            Expect.equal (Set.count negative.Endable) 0 "...and an empty Endable"

            // 0 and negative are NOT the same reach, which is exactly why budgetFor must not clamp a
            // negative moveRange UP to zero: that would turn "no move range" into "may stand still".
            Expect.equal (Pathfinding.budgetFor 0) 0 "budgetFor 0 = 0"
            let zero = Pathfinding.reachable FourWay 5000 cost (fun _ -> true) (Pathfinding.budgetFor 0) start
            Expect.equal (Set.toList zero.Endable) [ start ] "a zero budget settles start alone — standing still"
        }
    ]

// ------------------------------------------------------------------------------------------------
// Jump Point Search (work item 015, roadmap 1.1). A grid-specialised A* for uniform-cost grids that
// pops far fewer frontier nodes and returns a path of the SAME least cost as `astar`. The primary
// correctness net is a DIFFERENTIAL ORACLE against the shipped `astar`: equal cost, a valid path,
// and agreement on reachability (DEC-001). It does not compare cell sequences — several least-cost
// paths tie, and JPS's canonical jumps pick a different equal-cost one than astar's tie-break.

// Stricter than `validPath`: also asserts NO CORNER CUTTING — a diagonal step is taken only when
// both shared orthogonal neighbours are walkable (FR-002).
let private validPathNoCut (isWalkable: Cell -> bool) (start: Cell) (goal: Cell) (path: Cell list) =
    validPath isWalkable start goal path
    && path
       |> List.pairwise
       |> List.forall (fun (a, b) ->
           if a.Col <> b.Col && a.Row <> b.Row then
               isWalkable { Col = b.Col; Row = a.Row } && isWalkable { Col = a.Col; Row = b.Row }
           else
               true)

[<Tests>]
let jpsTests =
    testList "Game.Core Pathfinding JPS (015, FR-001..FR-006)" [

        test "jps FourWay jumps a straight corridor to the goal" {
            let walk = gridWalkable 6 1 Set.empty
            let path = Pathfinding.jps FourWay 1000 walk { Col = 0; Row = 0 } { Col = 5; Row = 0 }
            Expect.equal
                path
                (Some [ for c in 0..5 -> { Col = c; Row = 0 } ])
                "start..goal inclusive straight path, cell-by-cell"
        }

        test "jps EightWay takes the strictly-cheaper diagonal on an open grid" {
            let walk = gridWalkable 4 4 Set.empty
            let path = Pathfinding.jps EightWay 1000 walk { Col = 0; Row = 0 } { Col = 3; Row = 3 }
            let cost = path |> Option.map pathCost
            Expect.equal cost (Some 42) "three diagonals (14 each) — the least cost, same as astar"
            Expect.isTrue (validPathNoCut walk { Col = 0; Row = 0 } { Col = 3; Row = 3 } (Option.get path)) "valid"
        }

        test "jps EightWay refuses to cut the corner past a blocked orthogonal (D5)" {
            let walk = gridWalkable 3 3 (Set.ofList [ (1, 0) ])
            let path = Pathfinding.jps EightWay 1000 walk { Col = 0; Row = 0 } { Col = 1; Row = 1 }
            let astar = Pathfinding.astar EightWay 1000 walk { Col = 0; Row = 0 } { Col = 1; Row = 1 }
            Expect.equal (path |> Option.map pathCost) (astar |> Option.map pathCost) "same cost as astar (no corner-cut)"
            Expect.isTrue (validPathNoCut walk { Col = 0; Row = 0 } { Col = 1; Row = 1 } (Option.get path)) "no corner-cut ⇒ routes around"
        }

        test "jps routes around a wall with a single gap (FR-002 validity)" {
            let blocked = Set.ofList [ for r in 0..3 -> (2, r) ]
            let walk = gridWalkable 5 5 blocked
            let start = { Col = 0; Row = 0 }
            let goal = { Col = 4; Row = 0 }
            let path = Pathfinding.jps EightWay 5000 walk start goal
            Expect.isSome path "a path through the gap exists"
            Expect.isTrue (validPathNoCut walk start goal (Option.get path)) "valid walkable adjacent no-corner-cut path"
            Expect.isFalse (Option.get path |> List.exists (fun c -> blocked.Contains(c.Col, c.Row))) "never enters the wall"
        }

        test "jps shares astar's degenerate-input contract (FR-004 totality)" {
            let walk = gridWalkable 3 3 (Set.ofList [ (0, 0); (2, 2) ])
            let open3 = gridWalkable 3 3 Set.empty
            Expect.isNone (Pathfinding.jps FourWay 100 walk { Col = 0; Row = 0 } { Col = 1; Row = 1 }) "blocked start ⇒ None"
            Expect.isNone (Pathfinding.jps FourWay 100 walk { Col = 1; Row = 1 } { Col = 2; Row = 2 }) "blocked goal ⇒ None"
            Expect.isNone (Pathfinding.jps FourWay 0 open3 { Col = 0; Row = 0 } { Col = 2; Row = 2 }) "maxVisited 0 ⇒ None"
            Expect.isNone (Pathfinding.jps FourWay -1 open3 { Col = 0; Row = 0 } { Col = 2; Row = 2 }) "negative maxVisited ⇒ None"
            Expect.equal (Pathfinding.jps EightWay 100 open3 { Col = 1; Row = 1 } { Col = 1; Row = 1 }) (Some [ { Col = 1; Row = 1 } ]) "start = goal ⇒ [start]"
        }

        test "jps agrees with astar that a walled-off goal is unreachable (FR-003)" {
            let ring = Set.ofList [ (3, 4); (4, 4); (5, 4); (3, 5); (5, 5); (3, 6); (4, 6); (5, 6) ]
            let walk = gridWalkable 8 8 ring
            Expect.isNone (Pathfinding.jps EightWay 5000 walk { Col = 0; Row = 0 } { Col = 4; Row = 5 }) "goal enclosed ⇒ None"
        }

        test "determinism: jps is byte-identical across repeat runs (golden)" {
            // FourWay (0,0)->(1,1): two equal-cost routes; the result must still be one stable path.
            let walk = gridWalkable 2 2 Set.empty
            let a = Pathfinding.jps FourWay 100 walk { Col = 0; Row = 0 } { Col = 1; Row = 1 }
            let b = Pathfinding.jps FourWay 100 walk { Col = 0; Row = 0 } { Col = 1; Row = 1 }
            Expect.equal a b "identical inputs ⇒ byte-identical path despite the tie"
            Expect.isTrue (a |> Option.get |> validPathNoCut walk { Col = 0; Row = 0 } { Col = 1; Row = 1 }) "and it is a valid path"
        }

        test "FR-006: jps reaches the goal on a budget that starves astar (fewer frontier pops)" {
            // A 20×20 OPEN grid, corner to corner, FourWay. astar's Manhattan heuristic is exact here,
            // so every cell on the triangle of equal-cost staircases shares one f-value and astar pops
            // hundreds of them before the goal's tie-break turn comes up. jps collapses each open run
            // into one jump and reaches the corner in a handful of pops. A `maxVisited` between the two
            // (30) is Some for jps and None for astar — an observable proxy, over the public API, for
            // "jps pops strictly fewer frontier nodes" (DEC-002 / FR-006).
            let walk = gridWalkable 20 20 Set.empty
            let start = { Col = 0; Row = 0 }
            let goal = { Col = 19; Row = 19 }
            let budget = 30
            Expect.isSome (Pathfinding.jps FourWay budget walk start goal) "jps jumps there in a handful of pops"
            Expect.isNone (Pathfinding.astar FourWay budget walk start goal) "astar exhausts the same budget mid-triangle"
            // With a generous budget both find equal-cost paths.
            let j = Pathfinding.jps FourWay 5000 walk start goal
            let a = Pathfinding.astar FourWay 5000 walk start goal
            Expect.equal (j |> Option.map pathCost) (a |> Option.map pathCost) "equal cost under a generous budget"
        }

        testCase "differential: jps equals astar in cost + validity + reachability over random grids (FsCheck)" <| fun () ->
            // The primary correctness oracle (DEC-001). Generous maxVisited so NEITHER is bounded out,
            // which is the regime the equivalence is asserted over.
            let prop (blockedRaw: (int * int) list) (sc: int) (sr: int) (gc: int) (gr: int) (eightWay: bool) =
                let blocked = blockedRaw |> List.map (fun (c, r) -> (((abs c) % 8), ((abs r) % 8))) |> Set.ofList
                let walk = gridWalkable 8 8 blocked
                let nb = if eightWay then EightWay else FourWay
                let start = { Col = (abs sc) % 8; Row = (abs sr) % 8 }
                let goal = { Col = (abs gc) % 8; Row = (abs gr) % 8 }
                let j = Pathfinding.jps nb 5000 walk start goal
                let a = Pathfinding.astar nb 5000 walk start goal

                match j, a with
                | None, None -> true // agree on unreachable
                | Some jp, Some ap -> pathCost jp = pathCost ap && validPathNoCut walk start goal jp // equal cost + valid
                | _ -> false // reachability disagreement is a failure

            Check.One(Config.QuickThrowOnFailure.WithMaxTest 1000, prop)

        testCase "determinism: jps repeat calls are byte-identical over random grids (FsCheck)" <| fun () ->
            let prop (blockedRaw: (int * int) list) (sc: int) (sr: int) (gc: int) (gr: int) =
                let blocked = blockedRaw |> List.map (fun (c, r) -> (((abs c) % 8), ((abs r) % 8))) |> Set.ofList
                let walk = gridWalkable 8 8 blocked
                let start = { Col = (abs sc) % 8; Row = (abs sr) % 8 }
                let goal = { Col = (abs gc) % 8; Row = (abs gr) % 8 }
                Pathfinding.jps EightWay 2000 walk start goal = Pathfinding.jps EightWay 2000 walk start goal

            Check.One(Config.QuickThrowOnFailure.WithMaxTest 500, prop)
    ]

// ------------------------------------------------------------------------------------------------
// Connected-component early-out (work item 016, roadmap 1.2). `Regions.sameComponent` must agree
// with `astar` reachability over the same bounded walkability — the differential oracle — while
// answering in O(1) rather than exhausting `maxVisited`.

[<Tests>]
let regionsTests =
    testList "Game.Core Pathfinding Regions (016, FR-001..FR-005)" [

        test "sameComponent separates two islands and joins within each (FR-001)" {
            // 5×3 grid, a full wall at Col=2 splits it into a left and a right island.
            let blocked = Set.ofList [ for r in 0..2 -> (2, r) ]
            let walk = gridWalkable 5 3 blocked
            let regions = Pathfinding.Regions.build FourWay ({ Col = 0; Row = 0 }, { Col = 4; Row = 2 }) walk
            Expect.isTrue (Pathfinding.Regions.sameComponent regions { Col = 0; Row = 0 } { Col = 1; Row = 2 }) "same (left) island"
            Expect.isTrue (Pathfinding.Regions.sameComponent regions { Col = 3; Row = 0 } { Col = 4; Row = 2 }) "same (right) island"
            Expect.isFalse (Pathfinding.Regions.sameComponent regions { Col = 0; Row = 0 } { Col = 4; Row = 0 }) "across the wall ⇒ different components"
        }

        test "an out-of-bounds or unwalkable cell is in no component — even against itself (FR-002)" {
            let blocked = Set.ofList [ (1, 1) ]
            let walk = gridWalkable 3 3 blocked
            let regions = Pathfinding.Regions.build FourWay ({ Col = 0; Row = 0 }, { Col = 2; Row = 2 }) walk
            Expect.isFalse (Pathfinding.Regions.sameComponent regions { Col = 1; Row = 1 } { Col = 1; Row = 1 }) "unwalkable cell: false even against itself"
            Expect.isFalse (Pathfinding.Regions.sameComponent regions { Col = 9; Row = 9 } { Col = 0; Row = 0 }) "out-of-bounds ⇒ false"
            Expect.isTrue (Pathfinding.Regions.sameComponent regions { Col = 0; Row = 0 } { Col = 0; Row = 0 }) "a walkable cell IS in its own component"
        }

        test "EightWay connectivity honours no corner-cutting, matching astar (FR-003)" {
            // (1,0) and (0,1) blocked pen the (0,0) cell diagonally from (1,1): no legal step joins them.
            let blocked = Set.ofList [ (1, 0); (0, 1) ]
            let walk = gridWalkable 2 2 blocked
            let regions = Pathfinding.Regions.build EightWay ({ Col = 0; Row = 0 }, { Col = 1; Row = 1 }) walk
            let astarJoins = (Pathfinding.astar EightWay 1000 walk { Col = 0; Row = 0 } { Col = 1; Row = 1 }).IsSome
            Expect.equal (Pathfinding.Regions.sameComponent regions { Col = 0; Row = 0 } { Col = 1; Row = 1 }) astarJoins "no corner-cut ⇒ not connected, exactly as astar"
            Expect.isFalse astarJoins "and astar agrees they are not joined"
        }

        test "determinism: Regions yields byte-identical answers across repeat builds (golden)" {
            let blocked = Set.ofList [ (1, 1); (2, 2) ]
            let walk = gridWalkable 4 4 blocked
            let bounds = ({ Col = 0; Row = 0 }, { Col = 3; Row = 3 })
            let r1 = Pathfinding.Regions.build EightWay bounds walk
            let r2 = Pathfinding.Regions.build EightWay bounds walk
            let pairs = [ for a in 0..3 do for b in 0..3 -> ({ Col = a; Row = b }, { Col = b; Row = a }) ]
            Expect.isTrue
                (pairs |> List.forall (fun (a, b) -> Pathfinding.Regions.sameComponent r1 a b = Pathfinding.Regions.sameComponent r2 a b))
                "identical inputs ⇒ identical sameComponent answers"
        }

        test "sameComponent needs no search bound to reject an unreachable pair (FR-005)" {
            // No maxVisited is passed to sameComponent at all — the guard is O(1), not a bounded search.
            let blocked = Set.ofList [ for r in 0..9 -> (5, r) ]
            let walk = gridWalkable 10 10 blocked
            let regions = Pathfinding.Regions.build FourWay ({ Col = 0; Row = 0 }, { Col = 9; Row = 9 }) walk
            Expect.isFalse (Pathfinding.Regions.sameComponent regions { Col = 0; Row = 0 } { Col = 9; Row = 9 }) "walled apart ⇒ rejected with no exploration"
        }

        testCase "differential: sameComponent equals astar reachability over random bounded grids (FsCheck)" <| fun () ->
            let prop (blockedRaw: (int * int) list) (eightWay: bool) =
                let blocked = blockedRaw |> List.map (fun (c, r) -> (((abs c) % 6), ((abs r) % 6))) |> Set.ofList
                let walk = gridWalkable 6 6 blocked
                let nb = if eightWay then EightWay else FourWay
                let regions = Pathfinding.Regions.build nb ({ Col = 0; Row = 0 }, { Col = 5; Row = 5 }) walk
                let cells = [ for c in 0..5 do for r in 0..5 -> { Col = c; Row = r } ]
                cells
                |> List.forall (fun a ->
                    cells
                    |> List.forall (fun b ->
                        let reaches = (Pathfinding.astar nb 5000 walk a b).IsSome
                        Pathfinding.Regions.sameComponent regions a b = reaches))

            Check.One(Config.QuickThrowOnFailure.WithMaxTest 500, prop)

        testCase "determinism: Regions is a pure function of its inputs over random terrain (FsCheck)" <| fun () ->
            let prop (blockedRaw: (int * int) list) =
                let blocked = blockedRaw |> List.map (fun (c, r) -> (((abs c) % 7), ((abs r) % 7))) |> Set.ofList
                let walk = gridWalkable 7 7 blocked
                let bounds = ({ Col = 0; Row = 0 }, { Col = 6; Row = 6 })
                let r1 = Pathfinding.Regions.build EightWay bounds walk
                let r2 = Pathfinding.Regions.build EightWay bounds walk
                let cells = [ for c in 0..6 do for r in 0..6 -> { Col = c; Row = r } ]
                cells
                |> List.forall (fun a -> cells |> List.forall (fun b -> Pathfinding.Regions.sameComponent r1 a b = Pathfinding.Regions.sameComponent r2 a b))

            Check.One(Config.QuickThrowOnFailure.WithMaxTest 300, prop)
    ]

// ------------------------------------------------------------------------------------------------
// ALT landmark heuristic (work item 017, roadmap 1.3). Landmarks.astar must return an OPTIMAL-cost
// path (equal cost to astar — the differential oracle), its heuristic must be admissible, and it
// must expand fewer nodes than plain astar on a detour map. Determinism throughout.

[<Tests>]
let landmarksTests =
    testList "Game.Core Pathfinding Landmarks/ALT (017, FR-001..FR-006)" [

        test "Landmarks.astar returns an optimal-cost, valid path (FR-001/FR-002)" {
            let blocked = Set.ofList [ for r in 0..3 -> (2, r) ]
            let walk = gridWalkable 5 5 blocked
            let lm = Pathfinding.Landmarks.build EightWay walk 4 ({ Col = 0; Row = 0 }, { Col = 4; Row = 4 })
            let start = { Col = 0; Row = 0 }
            let goal = { Col = 4; Row = 0 }
            let j = Pathfinding.Landmarks.astar lm EightWay 5000 walk start goal
            let a = Pathfinding.astar EightWay 5000 walk start goal
            Expect.equal (j |> Option.map pathCost) (a |> Option.map pathCost) "same least cost as astar"
            Expect.isTrue (validPathNoCut walk start goal (Option.get j)) "and a valid no-corner-cut path"
        }

        test "the ALT heuristic is admissible — never exceeds the true distance (FR-005)" {
            let blocked = Set.ofList [ (2, 1); (2, 2); (2, 3) ]
            let walk = gridWalkable 6 6 blocked
            let bounds = ({ Col = 0; Row = 0 }, { Col = 5; Row = 5 })
            let lm = Pathfinding.Landmarks.build FourWay walk 4 bounds
            let goal = { Col = 5; Row = 5 }
            let cells = [ for c in 0..5 do for r in 0..5 -> { Col = c; Row = r } ]
            for cell in cells do
                match Pathfinding.astar FourWay 5000 walk cell goal with
                | Some p -> Expect.isTrue (Pathfinding.Landmarks.heuristic lm goal cell <= pathCost p) (sprintf "admissible at %A" cell)
                | None -> () // unreachable: no true distance to bound
        }

        test "determinism: build + Landmarks.astar are byte-identical across runs (golden)" {
            let blocked = Set.ofList [ (3, 1); (3, 2); (1, 4) ]
            let walk = gridWalkable 6 6 blocked
            let bounds = ({ Col = 0; Row = 0 }, { Col = 5; Row = 5 })
            let lm1 = Pathfinding.Landmarks.build EightWay walk 5 bounds
            let lm2 = Pathfinding.Landmarks.build EightWay walk 5 bounds
            let start = { Col = 0; Row = 5 }
            let goal = { Col = 5; Row = 0 }
            Expect.equal (Pathfinding.Landmarks.astar lm1 EightWay 5000 walk start goal) (Pathfinding.Landmarks.astar lm2 EightWay 5000 walk start goal) "identical inputs ⇒ identical path"
            let cells = [ for c in 0..5 do for r in 0..5 -> { Col = c; Row = r } ]
            Expect.isTrue (cells |> List.forall (fun c -> Pathfinding.Landmarks.heuristic lm1 goal c = Pathfinding.Landmarks.heuristic lm2 goal c)) "and identical heuristics"
        }

        test "FR-006: Landmarks.astar expands strictly fewer nodes than astar on a detour map" {
            // A vertical wall at col 7 (rows 0..13) with a gap at the bottom (row 14) forces a long
            // detour. octile underestimates the detour badly, so astar fans out on the near side; ALT's
            // landmark distances price the detour, so it expands fewer nodes. The smallest maxVisited
            // for which a search returns Some IS its expansion count — an observable pop-count proxy.
            let wall = Set.ofList [ for r in 0..13 -> (7, r) ]
            let walk = gridWalkable 15 15 wall
            let start = { Col = 0; Row = 0 }
            let goal = { Col = 14; Row = 0 }
            let bounds = ({ Col = 0; Row = 0 }, { Col = 14; Row = 14 })
            let lm = Pathfinding.Landmarks.build FourWay walk 6 bounds

            // Smallest budget for which f returns Some (monotone in budget ⇒ binary search).
            let firstSolve (f: int -> Cell list option) =
                let rec go lo hi =
                    if lo >= hi then lo
                    else
                        let mid = (lo + hi) / 2
                        if (f mid).IsSome then go lo mid else go (mid + 1) hi
                go 1 4000

            let astarPops = firstSolve (fun b -> Pathfinding.astar FourWay b walk start goal)
            let altPops = firstSolve (fun b -> Pathfinding.Landmarks.astar lm FourWay b walk start goal)
            Expect.isLessThan altPops astarPops (sprintf "ALT expands fewer nodes (ALT=%d, astar=%d)" altPops astarPops)
        }

        testCase "differential: Landmarks.astar equals astar in cost + validity + reachability (FsCheck)" <| fun () ->
            let prop (blockedRaw: (int * int) list) (sc: int) (sr: int) (gc: int) (gr: int) (eightWay: bool) =
                let blocked = blockedRaw |> List.map (fun (c, r) -> (((abs c) % 7), ((abs r) % 7))) |> Set.ofList
                let walk = gridWalkable 7 7 blocked
                let nb = if eightWay then EightWay else FourWay
                let lm = Pathfinding.Landmarks.build nb walk 4 ({ Col = 0; Row = 0 }, { Col = 6; Row = 6 })
                let start = { Col = (abs sc) % 7; Row = (abs sr) % 7 }
                let goal = { Col = (abs gc) % 7; Row = (abs gr) % 7 }
                let j = Pathfinding.Landmarks.astar lm nb 5000 walk start goal
                let a = Pathfinding.astar nb 5000 walk start goal
                match j, a with
                | None, None -> true
                | Some jp, Some ap -> pathCost jp = pathCost ap && validPathNoCut walk start goal jp
                | _ -> false

            Check.One(Config.QuickThrowOnFailure.WithMaxTest 1000, prop)

        testCase "admissibility over random grids: heuristic never overestimates (FsCheck)" <| fun () ->
            let prop (blockedRaw: (int * int) list) (gc: int) (gr: int) =
                let blocked = blockedRaw |> List.map (fun (c, r) -> (((abs c) % 6), ((abs r) % 6))) |> Set.ofList
                let walk = gridWalkable 6 6 blocked
                let goal = { Col = (abs gc) % 6; Row = (abs gr) % 6 }
                let lm = Pathfinding.Landmarks.build EightWay walk 4 ({ Col = 0; Row = 0 }, { Col = 5; Row = 5 })
                let cells = [ for c in 0..5 do for r in 0..5 -> { Col = c; Row = r } ]
                cells
                |> List.forall (fun cell ->
                    match Pathfinding.astar EightWay 5000 walk cell goal with
                    | Some p -> Pathfinding.Landmarks.heuristic lm goal cell <= pathCost p
                    | None -> true)

            Check.One(Config.QuickThrowOnFailure.WithMaxTest 500, prop)
    ]

// ------------------------------------------------------------------------------------------------
// Straighter-path tie-break (work item 018, roadmap 1.4). astarStraight returns an OPTIMAL-cost path
// (equal cost to astar) that hugs the straight start→goal line, while astar itself stays
// byte-identical (the bias is opt-in).

// Total deviation of a path from the straight start→goal line: sum of the integer cross-products.
let private lineDeviation (start: Cell) (goal: Cell) (path: Cell list) =
    let dgx = int64 goal.Col - int64 start.Col
    let dgy = int64 goal.Row - int64 start.Row
    path
    |> List.sumBy (fun c -> abs ((int64 c.Col - int64 start.Col) * dgy - dgx * (int64 c.Row - int64 start.Row)))

[<Tests>]
let straightTests =
    testList "Game.Core Pathfinding astarStraight (018, FR-001..FR-005)" [

        test "astar output is UNCHANGED by this work item (FR-003 byte-identity golden)" {
            // These are the exact goldens the shipped astar tests pin; astarStraight must not perturb them.
            let walk = gridWalkable 4 4 Set.empty
            Expect.equal
                (Pathfinding.astar EightWay 1000 walk { Col = 0; Row = 0 } { Col = 3; Row = 3 })
                (Some [ { Col = 0; Row = 0 }; { Col = 1; Row = 1 }; { Col = 2; Row = 2 }; { Col = 3; Row = 3 } ])
                "astar's diagonal golden is unchanged"
            let corridor = gridWalkable 4 1 Set.empty
            Expect.equal
                (Pathfinding.astar FourWay 1000 corridor { Col = 0; Row = 0 } { Col = 3; Row = 0 })
                (Some [ { Col = 0; Row = 0 }; { Col = 1; Row = 0 }; { Col = 2; Row = 0 }; { Col = 3; Row = 0 } ])
                "astar's corridor golden is unchanged"
        }

        test "astarStraight returns an optimal-cost, valid path (FR-001/FR-002)" {
            let walk = gridWalkable 6 5 Set.empty
            let start = { Col = 0; Row = 0 }
            let goal = { Col = 5; Row = 3 }
            let s = Pathfinding.astarStraight FourWay 5000 walk start goal
            let a = Pathfinding.astar FourWay 5000 walk start goal
            Expect.equal (s |> Option.map pathCost) (a |> Option.map pathCost) "same least cost as astar"
            Expect.isTrue (validPathNoCut walk start goal (Option.get s)) "valid no-corner-cut path"
        }

        test "FR-004: astarStraight hugs the line — smaller deviation than astar on an open off-axis query" {
            let walk = gridWalkable 8 6 Set.empty
            let start = { Col = 0; Row = 0 }
            let goal = { Col = 7; Row = 4 }
            let s = (Pathfinding.astarStraight FourWay 5000 walk start goal).Value
            let a = (Pathfinding.astar FourWay 5000 walk start goal).Value
            Expect.isLessThan (lineDeviation start goal s) (lineDeviation start goal a) "astarStraight deviates less from the straight line"
            Expect.equal (pathCost s) (pathCost a) "...at the same (optimal) cost"
        }

        test "determinism: astarStraight is byte-identical across repeat runs (golden)" {
            let walk = gridWalkable 6 6 (Set.ofList [ (3, 2); (3, 3) ])
            let start = { Col = 0; Row = 0 }
            let goal = { Col = 5; Row = 5 }
            Expect.equal
                (Pathfinding.astarStraight EightWay 5000 walk start goal)
                (Pathfinding.astarStraight EightWay 5000 walk start goal)
                "identical inputs ⇒ byte-identical path"
        }

        testCase "differential: astarStraight equals astar in cost + validity + reachability (FsCheck)" <| fun () ->
            let prop (blockedRaw: (int * int) list) (sc: int) (sr: int) (gc: int) (gr: int) (eightWay: bool) =
                let blocked = blockedRaw |> List.map (fun (c, r) -> (((abs c) % 8), ((abs r) % 8))) |> Set.ofList
                let walk = gridWalkable 8 8 blocked
                let nb = if eightWay then EightWay else FourWay
                let start = { Col = (abs sc) % 8; Row = (abs sr) % 8 }
                let goal = { Col = (abs gc) % 8; Row = (abs gr) % 8 }
                let s = Pathfinding.astarStraight nb 5000 walk start goal
                let a = Pathfinding.astar nb 5000 walk start goal
                match s, a with
                | None, None -> true
                | Some sp, Some ap -> pathCost sp = pathCost ap && validPathNoCut walk start goal sp
                | _ -> false

            Check.One(Config.QuickThrowOnFailure.WithMaxTest 1000, prop)

        testCase "astarStraight deviation is never worse than astar's over random open grids (FsCheck)" <| fun () ->
            // The bias is monotone-good on straightness: on open grids (unique-cost-free ties abound)
            // astarStraight never deviates more than astar for the same optimal cost.
            let prop (sc: int) (sr: int) (gc: int) (gr: int) =
                let walk = gridWalkable 8 8 Set.empty
                let start = { Col = (abs sc) % 8; Row = (abs sr) % 8 }
                let goal = { Col = (abs gc) % 8; Row = (abs gr) % 8 }
                match Pathfinding.astarStraight FourWay 5000 walk start goal, Pathfinding.astar FourWay 5000 walk start goal with
                | Some s, Some a -> pathCost s = pathCost a && lineDeviation start goal s <= lineDeviation start goal a
                | None, None -> true
                | _ -> false

            Check.One(Config.QuickThrowOnFailure.WithMaxTest 500, prop)
    ]
