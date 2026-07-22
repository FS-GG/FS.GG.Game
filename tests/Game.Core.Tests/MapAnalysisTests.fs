module Game.Core.Tests.MapAnalysisTests

// MapAnalysis (M8+): producer-agnostic analysis over any map. The headline property is router-consistency —
// MapAnalysis.reachable must return exactly the cells Pathfinding.bfs can reach — so the analysis layer and
// the routing layer can never disagree about what is reachable.

open Expecto
open FsCheck
open FS.GG.Game.Core

let private cell c r : Cell = { Col = c; Row = r }

let private tileMapOf (rows: int list list) : TileMap =
    let h = List.length rows
    let w = if h = 0 then 0 else List.length (List.head rows)
    let cells =
        [| for row in rows do
               for v in row -> if v = 0 then Wall else Floor |]
    { Width = w; Height = h; Cells = cells }

/// A random TileMap for property tests (interior floor at ~60%, border wall), deterministic in the seed.
let private randomMap (w: int) (h: int) (seed: uint64) : TileMap =
    let mutable rng = Rng.ofSeed seed
    let cells = Array.create (w * h) Wall
    for row in 0 .. h - 1 do
        for col in 0 .. w - 1 do
            let struct (f, next) = Rng.nextFloat rng
            rng <- next
            if row > 0 && col > 0 && row < h - 1 && col < w - 1 && f > 0.4 then
                cells.[row * w + col] <- Floor
    { Width = w; Height = h; Cells = cells }

[<Tests>]
let tests =
    testList
        "MapAnalysis"
        [ testCase "reachable — from a start covers its pocket, empty from a wall"
          <| fun () ->
              // two floor pockets separated by a wall column
              let m =
                  tileMapOf
                      [ [ 1; 1; 0; 1; 1 ]
                        [ 1; 1; 0; 1; 1 ] ]
              let isFloor c = MapGen.get m c = ValueSome Floor
              let left = MapAnalysis.reachable FourWay 100 isFloor (cell 0 0)
              Expect.equal (Set.count left) 4 "left pocket is 4 cells"
              Expect.isFalse (Set.contains (cell 3 0) left) "right pocket not reachable"
              Expect.equal (MapAnalysis.reachable FourWay 100 isFloor (cell 2 0)) Set.empty "start on a wall => empty"

          testCase "reachable — equals exactly the cells bfs can reach (FsCheck, router-consistency)"
          <| fun () ->
              let prop (s: uint64) (sc: int) (sr: int) =
                  let w, h = 18, 14
                  let m = randomMap w h (s % 100000UL)
                  let isFloor c = MapGen.get m c = ValueSome Floor
                  let start = cell (1 + (abs sc) % (w - 2)) (1 + (abs sr) % (h - 2))
                  if not (isFloor start) then
                      true // vacuous; the reachable/empty case is covered elsewhere
                  else
                      let reached = MapAnalysis.reachable FourWay (w * h + 1) isFloor start
                      // brute force: every floor cell, is it bfs-reachable from start?
                      let bfsReaches goal =
                          (Pathfinding.bfs FourWay (w * h + 1) isFloor start goal).IsSome
                      let allFloor =
                          [ for row in 0 .. h - 1 do
                                for col in 0 .. w - 1 do
                                    if isFloor (cell col row) then cell col row ]
                      allFloor |> List.forall (fun c -> Set.contains c reached = bfsReaches c)
              Check.One(Config.QuickThrowOnFailure.WithMaxTest 200, prop)

          testCase "stranded / isConnected — a two-pocket map is disconnected, its far pocket stranded"
          <| fun () ->
              let m =
                  tileMapOf
                      [ [ 1; 1; 0; 1 ]
                        [ 1; 1; 0; 1 ] ]
              Expect.isFalse (MapAnalysis.isConnected FourWay m) "two pockets => not connected"
              let stranded = MapAnalysis.stranded FourWay (cell 0 0) m
              Expect.equal stranded [ cell 3 0; cell 3 1 ] "the right pocket is stranded, row-major"
              // a single-pocket map is connected and strands nothing
              let m2 = tileMapOf [ [ 1; 1; 1 ]; [ 1; 1; 1 ] ]
              Expect.isTrue (MapAnalysis.isConnected FourWay m2) "one pocket => connected"
              Expect.equal (MapAnalysis.stranded FourWay (cell 0 0) m2) [] "nothing stranded"

          testCase "stranded — a non-floor reference strands every floor cell"
          <| fun () ->
              let m = tileMapOf [ [ 1; 0 ]; [ 0; 1 ] ]
              Expect.equal (MapAnalysis.stranded FourWay (cell 1 0) m) [ cell 0 0; cell 1 1 ] "wall reference => all floor stranded"

          testCase "isConnected — agrees with componentCount <= 1 (FsCheck)"
          <| fun () ->
              let prop (s: uint64) =
                  let m = randomMap 16 12 (s % 100000UL)
                  MapAnalysis.isConnected FourWay m = (MapAnalysis.componentCount FourWay m <= 1)
              Check.One(Config.QuickThrowOnFailure.WithMaxTest 200, prop)

          testCase "componentCount — matches MapGen.regions length"
          <| fun () ->
              let m =
                  tileMapOf
                      [ [ 1; 0; 1 ]
                        [ 0; 0; 0 ]
                        [ 1; 0; 1 ] ]
              Expect.equal (MapAnalysis.componentCount FourWay m) 4 "four isolated floor cells"
              Expect.equal (MapAnalysis.componentCount FourWay (MapGen.filled 4 4 Wall)) 0 "no floor => 0 components"

          testCase "MapAnalysis M8 totality — degenerate maps and starts never throw (FsCheck)"
          <| fun () ->
              let prop (w: int) (h: int) (sc: int) (sr: int) =
                  let m = randomMap (max 0 ((w % 10))) (max 0 ((h % 10))) (uint64 (abs w))
                  let c = cell (sc % 14 - 7) (sr % 14 - 7)
                  let isFloor cc = MapGen.get m cc = ValueSome Floor
                  MapAnalysis.reachable FourWay 50 isFloor c |> ignore
                  MapAnalysis.stranded EightWay c m |> ignore
                  MapAnalysis.isConnected FourWay m |> ignore
                  MapAnalysis.componentCount EightWay m |> ignore
                  true
              Check.One(Config.QuickThrowOnFailure.WithMaxTest 300, prop)

          // ---- M9: entrances/exits & chokepoints ----

          testCase "borderOpenings / deadEnds — border floor and single-neighbour cells, row-major"
          <| fun () ->
              // a plus-shaped floor with an opening on the top border
              let m =
                  tileMapOf
                      [ [ 0; 1; 0 ]
                        [ 1; 1; 1 ]
                        [ 0; 1; 0 ] ]
              Expect.equal (MapAnalysis.borderOpenings m) [ cell 1 0; cell 0 1; cell 2 1; cell 1 2 ] "border floor cells row-major"
              // the four arm tips are dead-ends (one floor neighbour each), the centre is not
              Expect.equal (MapAnalysis.deadEnds FourWay m) [ cell 1 0; cell 0 1; cell 2 1; cell 1 2 ] "arm tips are dead-ends"

          testCase "articulationPoints — a dumbbell's corridor cells are chokepoints; an open room has none"
          <| fun () ->
              // two 2x2 rooms joined by a 1-wide, 2-long corridor
              let dumbbell =
                  tileMapOf
                      [ [ 1; 1; 0; 0; 1; 1 ]
                        [ 1; 1; 1; 1; 1; 1 ]
                        [ 1; 1; 0; 0; 1; 1 ] ]
              let aps = MapAnalysis.articulationPoints FourWay dumbbell |> Set.ofList
              Expect.isTrue (Set.contains (cell 2 1) aps) "corridor cell (2,1) is a chokepoint"
              Expect.isTrue (Set.contains (cell 3 1) aps) "corridor cell (3,1) is a chokepoint"
              Expect.isFalse (Set.contains (cell 0 0) aps) "a room-corner is not a chokepoint"
              // a fully open room has no articulation points
              let room = tileMapOf [ [ 1; 1; 1 ]; [ 1; 1; 1 ]; [ 1; 1; 1 ] ]
              Expect.equal (MapAnalysis.articulationPoints FourWay room) [] "an open room has no chokepoints"

          testCase "articulationPoints — equals the remove-and-recount oracle (FsCheck)"
          <| fun () ->
              let prop (s: uint64) =
                  let w, h = 14, 11
                  let m = randomMap w h (s % 100000UL)
                  let aps = MapAnalysis.articulationPoints FourWay m |> Set.ofList
                  // oracle: a floor cell is an articulation point iff walling it raises the component count
                  let baseCount = MapAnalysis.componentCount FourWay m
                  let allFloor =
                      [ for row in 0 .. h - 1 do
                            for col in 0 .. w - 1 do
                                if m.Cells.[row * w + col] = Floor then cell col row ]
                  allFloor
                  |> List.forall (fun c ->
                      let walled = MapGen.set m c Wall
                      let rises = MapAnalysis.componentCount FourWay walled > baseCount
                      Set.contains c aps = rises)
              Check.One(Config.QuickThrowOnFailure.WithMaxTest 150, prop)

          testCase "articulationPoints — a many-thousand-cell corridor completes (iterative, no stack overflow)"
          <| fun () ->
              // a single-row corridor 5000 cells long: recursion would overflow; iterative DFS must not
              let len = 5000
              let cells = Array.create len Floor
              let corridor: TileMap = { Width = len; Height = 1; Cells = cells }
              let aps = MapAnalysis.articulationPoints FourWay corridor
              // every interior cell of a path is an articulation point; the two ends are not
              Expect.equal (List.length aps) (len - 2) "all interior corridor cells are chokepoints"

          testCase "MapAnalysis M9 totality — degenerate maps never throw (FsCheck)"
          <| fun () ->
              let prop (w: int) (h: int) (s: uint64) =
                  let m = randomMap (max 0 (w % 9)) (max 0 (h % 9)) (s % 100000UL)
                  MapAnalysis.borderOpenings m |> ignore
                  MapAnalysis.deadEnds EightWay m |> ignore
                  MapAnalysis.articulationPoints FourWay m |> ignore
                  MapAnalysis.articulationPoints EightWay m |> ignore
                  true
              Check.One(Config.QuickThrowOnFailure.WithMaxTest 300, prop)

          // ---- M10: path & flow metrics ----

          testCase "isolation / diameter — a straight corridor of k cells has diameter k-1"
          <| fun () ->
              let corridor (k: int) : TileMap = { Width = k; Height = 1; Cells = Array.create k Floor }
              Expect.equal (MapAnalysis.diameter FourWay (corridor 10)) 9 "10-cell corridor diameter is 9"
              Expect.equal (MapAnalysis.isolation FourWay (corridor 10) (cell 0 0)) 9 "isolation of an end is 9"
              Expect.equal (MapAnalysis.isolation FourWay (corridor 10) (cell 5 0)) 5 "isolation of the middle is 5"
              // a non-floor cell and an empty map both measure 0
              Expect.equal (MapAnalysis.isolation FourWay (corridor 10) (cell 0 (-1))) 0 "off-map cell => 0"
              Expect.equal (MapAnalysis.diameter FourWay (MapGen.filled 4 4 Wall)) 0 "no floor => 0"

          testCase "diameter — an L-bend measures the path around the corner"
          <| fun () ->
              // an L: 3 across the top, then 2 down the right — 4 hops end to end
              let m =
                  tileMapOf
                      [ [ 1; 1; 1 ]
                        [ 0; 0; 1 ]
                        [ 0; 0; 1 ] ]
              Expect.equal (MapAnalysis.diameter FourWay m) 4 "L-shape diameter follows the bend"

          testCase "diameter equals the max over floor cells of isolation (FsCheck)"
          <| fun () ->
              let prop (s: uint64) =
                  let w, h = 14, 10
                  let m = randomMap w h (s % 100000UL)
                  let byIsolation =
                      [ for row in 0 .. h - 1 do
                            for col in 0 .. w - 1 do
                                if m.Cells.[row * w + col] = Floor then MapAnalysis.isolation FourWay m (cell col row) ]
                      |> function
                          | [] -> 0
                          | xs -> List.max xs
                  MapAnalysis.diameter FourWay m = byIsolation
              Check.One(Config.QuickThrowOnFailure.WithMaxTest 200, prop)

          testCase "MapAnalysis M10 totality — degenerate maps never throw (FsCheck)"
          <| fun () ->
              let prop (w: int) (h: int) (s: uint64) (cc: int) (cr: int) =
                  let m = randomMap (max 0 (w % 9)) (max 0 (h % 9)) (s % 100000UL)
                  MapAnalysis.isolation EightWay m (cell (cc % 12 - 6) (cr % 12 - 6)) |> ignore
                  MapAnalysis.diameter FourWay m |> ignore
                  MapAnalysis.diameter EightWay m |> ignore
                  true
              Check.One(Config.QuickThrowOnFailure.WithMaxTest 300, prop) ]
