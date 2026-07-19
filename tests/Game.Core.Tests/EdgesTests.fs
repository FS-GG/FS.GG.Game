module Game.Core.Tests.EdgesTests

// Tile edges & vertices (work item 020, roadmap 2.3). Canonical edge/vertex addressing, the tile-part
// relationships (mutually consistent), edge-aware pathfinding that blocks a wall on a shared edge, and
// determinism throughout.

open Expecto
open FsCheck
open FS.GG.Game.Core

let private gridWalkable (w: int) (h: int) (blocked: Set<int * int>) (c: Cell) =
    c.Col >= 0 && c.Col < w && c.Row >= 0 && c.Row < h && not (blocked.Contains(c.Col, c.Row))

let private cellOf (col: int) (row: int) = { Col = ((col % 8) + 8) % 8; Row = ((row % 8) + 8) % 8 }

[<Tests>]
let tests =
    testList "Game.Core Edges (020, FR-001..FR-006)" [

        test "edgeBetween is canonical and order-independent (FR-001)" {
            let a = { Col = 2; Row = 3 }
            let b = { Col = 3; Row = 3 }
            Expect.equal (Edges.edgeBetween a b) (Edges.edgeBetween b a) "same edge from either side"
            Expect.isSome (Edges.edgeBetween a b) "adjacent ⇒ Some"
            Expect.isNone (Edges.edgeBetween a { Col = 5; Row = 5 }) "non-adjacent ⇒ None"
            Expect.isNone (Edges.edgeBetween a a) "same cell ⇒ None"
            Expect.equal (Edges.edgeOf a East) (Option.get (Edges.edgeBetween a b)) "edgeOf matches edgeBetween"
        }

        testCase "edgeBetween order-independence + edgeOf round-trip over random adjacent pairs (FsCheck)" <| fun () ->
            let prop (col: int) (row: int) (d: int) =
                let c = cellOf col row
                let dir = [| North; East; South; West |].[((abs d) % 4)]
                let n = Edges.step c dir
                Edges.edgeBetween c n = Edges.edgeBetween n c
                && Edges.edgeOf c dir = Option.get (Edges.edgeBetween c n)

            Check.One(Config.QuickThrowOnFailure.WithMaxTest 1000, prop)

        test "borders and corners have the right cardinality and are canonical (FR-002)" {
            let c = { Col = 1; Row = 1 }
            Expect.equal (List.length (Edges.borders c)) 4 "four edges"
            Expect.equal (List.length (Edges.corners c)) 4 "four corners"
            Expect.equal (Edges.borders c |> List.distinct |> List.length) 4 "edges distinct"
            Expect.equal (Edges.corners c |> List.distinct |> List.length) 4 "corners distinct"
        }

        testCase "relationships are mutually consistent (FR-003, FsCheck)" <| fun () ->
            let prop (col: int) (row: int) =
                let c = cellOf col row
                // edge in borders c  <=>  c in edgeCells edge
                let edgesOk =
                    Edges.borders c
                    |> List.forall (fun e ->
                        let (l, h) = Edges.edgeCells e
                        (l = c || h = c))
                // vertex in corners c  <=>  c in vertexCells vertex
                let cornersOk =
                    Edges.corners c
                    |> List.forall (fun v -> Edges.vertexCells v |> List.contains c)
                // every edge of c shares its two endpoint vertices with c's corners
                let endpointsOk =
                    Edges.borders c
                    |> List.forall (fun e ->
                        let (v1, v2) = Edges.edgeEndpoints e
                        let cs = Edges.corners c
                        List.contains v1 cs && List.contains v2 cs)
                // every edge around a corner vertex is a real edge whose cells include that vertex's cells
                let vertexEdgesOk =
                    Edges.corners c
                    |> List.forall (fun v ->
                        Edges.vertexEdges v
                        |> List.forall (fun e ->
                            let (l, h) = Edges.edgeCells e
                            let vc = Edges.vertexCells v
                            List.contains l vc && List.contains h vc))

                edgesOk && cornersOk && endpointsOk && vertexEdgesOk

            Check.One(Config.QuickThrowOnFailure.WithMaxTest 1000, prop)

        test "isEdgePassable is symmetric and a wall blocks both directions (FR-004)" {
            let a = { Col = 0; Row = 0 }
            let b = { Col = 1; Row = 0 }
            let wall = Option.get (Edges.edgeBetween a b)
            let walls = Set.singleton wall
            Expect.isFalse (Edges.isEdgePassable walls a b) "blocked a->b"
            Expect.isFalse (Edges.isEdgePassable walls b a) "blocked b->a (one canonical wall)"
            Expect.isTrue (Edges.isEdgePassable Set.empty a b) "no wall ⇒ passable"
            Expect.isTrue (Edges.isEdgePassable walls a { Col = 0; Row = 1 }) "a different edge is unaffected"
        }

        test "FR-005: a wall between two open cells forces a detour" {
            // (0,0) and (1,0) are both open; a wall on their shared edge forces the path around.
            let walk = gridWalkable 3 3 Set.empty
            let start = { Col = 0; Row = 0 }
            let goal = { Col = 1; Row = 0 }
            let wall = Set.singleton (Option.get (Edges.edgeBetween start goal))

            let direct = Edges.bfs Set.empty 1000 walk start goal
            Expect.equal direct (Some [ start; goal ]) "no wall ⇒ direct one-hop path"

            let detour = Edges.astar wall 1000 walk start goal
            Expect.isSome detour "still reachable around the wall"
            let p = Option.get detour
            Expect.equal (List.head p) start "starts at start"
            Expect.equal (List.last p) goal "ends at goal"
            Expect.isTrue (List.length p > 2) "the detour is longer than the blocked direct hop"
            // the path never crosses the walled edge
            Expect.isTrue
                (p |> List.pairwise |> List.forall (fun (x, y) -> Edges.isEdgePassable wall x y))
                "the path never crosses the wall"
        }

        test "a fully-walled cell is unreachable (FR-005 totality)" {
            let walk = gridWalkable 3 3 Set.empty
            let goal = { Col = 1; Row = 1 }
            // wall off all four edges of the centre cell
            let walls = Edges.borders goal |> Set.ofList
            Expect.isNone (Edges.astar walls 5000 walk { Col = 0; Row = 0 } goal) "walled-in goal ⇒ None"
        }

        testCase "empty walls match plain Pathfinding.bfs reachability + hop count (FR-005, FsCheck)" <| fun () ->
            let prop (blockedRaw: (int * int) list) (sc: int) (sr: int) (gc: int) (gr: int) =
                let blocked = blockedRaw |> List.map (fun (c, r) -> (((abs c) % 6), ((abs r) % 6))) |> Set.ofList
                let walk = gridWalkable 6 6 blocked
                let start = { Col = (abs sc) % 6; Row = (abs sr) % 6 }
                let goal = { Col = (abs gc) % 6; Row = (abs gr) % 6 }
                let e = Edges.bfs Set.empty 5000 walk start goal
                let p = Pathfinding.bfs FourWay 5000 walk start goal
                // same reachability and same hop count as the tile-only FourWay bfs
                (e.IsSome = p.IsSome)
                && (match e, p with
                    | Some ep, Some pp -> List.length ep = List.length pp
                    | _ -> true)

            Check.One(Config.QuickThrowOnFailure.WithMaxTest 500, prop)

        testCase "determinism: relationships and edge-aware search are byte-identical (FR-006, FsCheck)" <| fun () ->
            let prop (blockedRaw: (int * int) list) (wallsRaw: (int * int) list) (sc: int) (sr: int) (gc: int) (gr: int) =
                let blocked = blockedRaw |> List.map (fun (c, r) -> (((abs c) % 6), ((abs r) % 6))) |> Set.ofList
                let walk = gridWalkable 6 6 blocked
                // build a wall set from random adjacent-ish edges
                let walls =
                    wallsRaw
                    |> List.choose (fun (c, r) ->
                        let a = { Col = (abs c) % 6; Row = (abs r) % 6 }
                        Edges.edgeBetween a (Edges.step a East))
                    |> Set.ofList
                let start = { Col = (abs sc) % 6; Row = (abs sr) % 6 }
                let goal = { Col = (abs gc) % 6; Row = (abs gr) % 6 }
                Edges.astar walls 5000 walk start goal = Edges.astar walls 5000 walk start goal
                && Edges.borders start = Edges.borders start

            Check.One(Config.QuickThrowOnFailure.WithMaxTest 500, prop)
    ]
