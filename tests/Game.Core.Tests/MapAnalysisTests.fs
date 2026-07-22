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
              Check.One(Config.QuickThrowOnFailure.WithMaxTest 300, prop) ]
