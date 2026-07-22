module Game.Core.Tests.MapGenTests

// MapGen substrate (M1): the dense Grid<'T> container, Tile/TileMap/Region, total addressing, and the
// flood-fill connectivity toolkit. The determinism and traversability HARNESSES below are the reusable
// properties every family generator (M2–M5) instantiates with its own generator — a generator is
// "correct" here when it is byte-identical for a seed and its connected floor is fully traversable.

open Expecto
open FsCheck
open FS.GG.Game.Core

let private cell c r : Cell = { Col = c; Row = r }

/// Build a TileMap from a row-major array of 0/1 (1 = Floor), for compact fixtures.
let private tileMapOf (rows: int list list) : TileMap =
    let h = List.length rows
    let w = if h = 0 then 0 else List.length (List.head rows)

    let cells =
        [| for row in rows do
               for v in row -> if v = 0 then Wall else Floor |]

    { Width = w; Height = h; Cells = cells }

// -------------------------------------------------------------------------------------------------
// Reusable harnesses — M2–M5 call these with their real generators.
// -------------------------------------------------------------------------------------------------

/// Byte-identity: `gen seed` twice is structurally equal; an incremented seed differs. A generator that
/// leaked hash-set order or ambient state would fail the equality; one that ignored the seed the difference.
let determinismHarness (gen: uint64 -> TileMap) (seed: uint64) : bool =
    let a = gen seed
    let b = gen seed
    let bumped = gen (seed + 1UL)
    a = b && a <> bumped

/// After a generator that promises a single connected floor, every Floor cell is reachable from the first
/// under `Pathfinding.bfs` on the same neighbourhood.
let traversabilityHarness (neighbourhood: Neighbourhood) (map: TileMap) : bool =
    let floors =
        [ for row in 0 .. map.Height - 1 do
              for col in 0 .. map.Width - 1 do
                  if MapGen.get map (cell col row) = ValueSome Floor then
                      cell col row ]

    match floors with
    | [] -> true
    | start :: rest ->
        let isWalkable c = MapGen.get map c = ValueSome Floor
        let budget = map.Width * map.Height + 1
        rest
        |> List.forall (fun goal -> (Pathfinding.bfs neighbourhood budget isWalkable start goal).IsSome)

// -------------------------------------------------------------------------------------------------
// A sample generator built ONLY from the M1 substrate (random fill threaded row-major, then connect).
// It stands in for a family generator so the harnesses have something to exercise before M2 lands.
// -------------------------------------------------------------------------------------------------

let private sampleGen (neighbourhood: Neighbourhood) (w: int) (h: int) (seed: uint64) : TileMap =
    let mutable rng = Rng.ofSeed seed
    let cells = Array.create (w * h) Wall

    for row in 0 .. h - 1 do
        for col in 0 .. w - 1 do
            let struct (f, next) = Rng.nextFloat rng
            rng <- next
            // interior cells floor with ~55% chance; the border stays wall
            if row > 0 && col > 0 && row < h - 1 && col < w - 1 && f > 0.45 then
                cells.[row * w + col] <- Floor

    let filledMap: TileMap = { Width = w; Height = h; Cells = cells }
    let struct (connected, _) = MapGen.connect neighbourhood rng filledMap
    connected

[<Tests>]
let tests =
    testList
        "MapGen"
        [ testCase "get/set/inBounds are total — OOB get is ValueNone, OOB set is a no-op"
          <| fun () ->
              let g = MapGen.filled 3 2 Wall
              Expect.isTrue (MapGen.inBounds g (cell 2 1)) "in bounds"
              Expect.isFalse (MapGen.inBounds g (cell 3 0)) "col past width is out"
              Expect.isFalse (MapGen.inBounds g (cell 0 2)) "row past height is out"
              Expect.equal (MapGen.get g (cell 1 1)) (ValueSome Wall) "in-bounds get"
              Expect.equal (MapGen.get g (cell -1 0)) ValueNone "OOB get is ValueNone"
              let g2 = MapGen.set g (cell 1 1) Floor
              Expect.equal (MapGen.get g2 (cell 1 1)) (ValueSome Floor) "set writes"
              Expect.equal (MapGen.get g (cell 1 1)) (ValueSome Wall) "set is copy-on-write (input unchanged)"
              Expect.equal (MapGen.set g (cell 9 9) Floor) g "OOB set returns the grid unchanged"

          testCase "filled clamps a non-positive dimension to an empty grid"
          <| fun () ->
              Expect.equal (MapGen.filled 0 5 Wall).Cells.Length 0 "zero width is empty"
              Expect.equal (MapGen.filled 5 -3 Wall).Cells.Length 0 "negative height clamps to empty"

          testCase "regions labels components with Cells row-major and Id ascending by first cell"
          <| fun () ->
              // two disjoint floor blobs: top-left 2x1, and a single cell at bottom-right
              let m =
                  tileMapOf
                      [ [ 1; 1; 0 ]
                        [ 0; 0; 0 ]
                        [ 0; 0; 1 ] ]

              let rs = MapGen.regions FourWay m
              Expect.equal (List.length rs) 2 "two components"
              Expect.equal rs.[0].Id 0 "first-scanned region is Id 0"
              Expect.equal rs.[0].Cells [| cell 0 0; cell 1 0 |] "region 0 cells row-major"
              Expect.equal rs.[1].Id 1 "later region is Id 1"
              Expect.equal rs.[1].Cells [| cell 2 2 |] "region 1 is the lone cell"

          testCase "largestRegion returns the biggest, ties to lowest Id, ValueNone on no floor"
          <| fun () ->
              let m =
                  tileMapOf
                      [ [ 1; 0; 1 ]
                        [ 1; 0; 0 ] ]
              // region 0 = {(0,0),(0,1)} size 2 ; region 1 = {(2,0)} size 1
              match MapGen.largestRegion FourWay m with
              | ValueSome r -> Expect.equal r.Id 0 "largest is region 0"
              | ValueNone -> failtest "expected a region"

              let empty = MapGen.filled 3 3 Wall
              Expect.equal (MapGen.largestRegion FourWay empty) ValueNone "no floor => ValueNone"

          testCase "connect joins every region into a single connected floor"
          <| fun () ->
              // three separated single floor cells
              let m =
                  tileMapOf
                      [ [ 1; 0; 1 ]
                        [ 0; 0; 0 ]
                        [ 1; 0; 0 ] ]

              let struct (joined, _) = MapGen.connect FourWay (Rng.ofSeed 1UL) m
              Expect.equal (List.length (MapGen.regions FourWay joined)) 1 "one region after connect"
              Expect.isTrue (traversabilityHarness FourWay joined) "every floor cell reachable"

          testCase "connect is a no-op on a map that is already one region"
          <| fun () ->
              let m = tileMapOf [ [ 1; 1 ]; [ 1; 1 ] ]
              let struct (joined, _) = MapGen.connect FourWay (Rng.ofSeed 7UL) m
              Expect.equal joined m "already-connected map returned unchanged"

          testCase "MapGen determinism harness — build+connect twice from a seed is byte-identical (FsCheck)"
          <| fun () ->
              let prop (s: uint64) =
                  determinismHarness (sampleGen FourWay 24 18) (s % 100000UL)

              Check.One(Config.QuickThrowOnFailure.WithMaxTest 200, prop)

          testCase "MapGen traversability harness — after connect every floor cell is reachable (FsCheck)"
          <| fun () ->
              let prop (s: uint64) =
                  let m = sampleGen EightWay 24 18 (s % 100000UL)
                  traversabilityHarness EightWay m

              Check.One(Config.QuickThrowOnFailure.WithMaxTest 100, prop)

          testCase "MapGen totality — degenerate dimensions and OOB never throw (FsCheck)"
          <| fun () ->
              let prop (w: int) (h: int) (cc: int) (cr: int) =
                  let g = MapGen.filled ((w % 12) - 4) ((h % 12) - 4) Wall
                  let c = cell (cc % 20 - 10) (cr % 20 - 10)
                  // none of these throw on any (possibly empty / OOB) input
                  MapGen.get g c |> ignore
                  MapGen.set g c Floor |> ignore
                  MapGen.regions FourWay g |> ignore
                  MapGen.largestRegion EightWay g |> ignore
                  let struct (_, _) = MapGen.connect FourWay (Rng.ofSeed 0UL) g
                  true

              Check.One(Config.QuickThrowOnFailure.WithMaxTest 300, prop)

          // ---- M2: cellular-automata caves ----

          testCase "MapGen.caves — border is solid wall and a reasonable fill leaves floor"
          <| fun () ->
              let p: CaveParams = { WallChance = 0.45; SmoothingPasses = 4; Neighbourhood = FourWay }
              let struct (m, _) = MapGen.caves 40 30 p (Rng.ofSeed 3UL)
              Expect.equal m.Width 40 "width"
              Expect.equal m.Height 30 "height"
              let borderAllWall =
                  [ for c in 0 .. m.Width - 1 -> cell c 0 ]
                  @ [ for c in 0 .. m.Width - 1 -> cell c (m.Height - 1) ]
                  @ [ for r in 0 .. m.Height - 1 -> cell 0 r ]
                  @ [ for r in 0 .. m.Height - 1 -> cell (m.Width - 1) r ]
                  |> List.forall (fun c -> MapGen.get m c = ValueSome Wall)
              Expect.isTrue borderAllWall "every border cell is Wall"
              Expect.isTrue (Array.contains Floor m.Cells) "the cave has floor"

          testCase "MapGen.caves — the generated cavern is a single traversable region"
          <| fun () ->
              let p: CaveParams = { WallChance = 0.45; SmoothingPasses = 5; Neighbourhood = EightWay }
              let struct (m, _) = MapGen.caves 48 32 p (Rng.ofSeed 11UL)
              Expect.equal (List.length (MapGen.regions EightWay m)) 1 "exactly one floor region"
              Expect.isTrue (traversabilityHarness EightWay m) "every floor cell reachable"

          testCase "MapGen.caves determinism — byte-identical for a seed via the M1 harness (FsCheck)"
          <| fun () ->
              let p: CaveParams = { WallChance = 0.45; SmoothingPasses = 4; Neighbourhood = FourWay }
              let gen (s: uint64) = let struct (m, _) = MapGen.caves 32 24 p (Rng.ofSeed s) in m
              let prop (s: uint64) = determinismHarness gen (s % 100000UL)
              Check.One(Config.QuickThrowOnFailure.WithMaxTest 150, prop)

          testCase "MapGen.caves totality — degenerate size and params never throw (FsCheck)"
          <| fun () ->
              let prop (w: int) (h: int) (wc: int) (sp: int) =
                  let p: CaveParams =
                      { WallChance = float (wc % 300) / 100.0 - 1.0 // spans out-of-[0,1]
                        SmoothingPasses = (sp % 12) - 3 // spans negative
                        Neighbourhood = if wc % 2 = 0 then FourWay else EightWay }
                  let struct (_, _) = MapGen.caves ((w % 20) - 4) ((h % 20) - 4) p (Rng.ofSeed (uint64 (abs wc)))
                  true
              Check.One(Config.QuickThrowOnFailure.WithMaxTest 300, prop)

          // ---- M3: BSP room-and-corridor dungeons ----

          testCase "MapGen.bspDungeon — rooms are non-overlapping and the floor is one traversable region"
          <| fun () ->
              let p: BspParams = { MinLeaf = 8; MaxLeaf = 16; RoomPadding = 1 }
              let struct (m, g, _) = MapGen.bspDungeon 64 48 p (Rng.ofSeed 5UL)
              Expect.isTrue (g.Rooms.Length >= 2) "several rooms placed"
              // every room's bounds sit inside the map
              let inMap (rm: Room) =
                  int rm.Bounds.X >= 0 && int rm.Bounds.Y >= 0
                  && int rm.Bounds.X + int rm.Bounds.Width <= m.Width
                  && int rm.Bounds.Y + int rm.Bounds.Height <= m.Height
              Expect.isTrue (Array.forall inMap g.Rooms) "rooms lie within the map"
              // pairwise non-overlap
              let overlaps (a: Room) (b: Room) =
                  let ax, ay, aw, ah = int a.Bounds.X, int a.Bounds.Y, int a.Bounds.Width, int a.Bounds.Height
                  let bx, by, bw, bh = int b.Bounds.X, int b.Bounds.Y, int b.Bounds.Width, int b.Bounds.Height
                  ax < bx + bw && bx < ax + aw && ay < by + bh && by < ay + ah
              let anyOverlap =
                  [ for i in 0 .. g.Rooms.Length - 1 do
                        for j in i + 1 .. g.Rooms.Length - 1 -> overlaps g.Rooms.[i] g.Rooms.[j] ]
                  |> List.exists id
              Expect.isFalse anyOverlap "no two rooms overlap"
              Expect.equal (List.length (MapGen.regions FourWay m)) 1 "one floor region"
              Expect.isTrue (traversabilityHarness FourWay m) "every floor cell reachable"

          testCase "MapGen.bspDungeon — Room ids are contiguous ascending and corridors reference real rooms"
          <| fun () ->
              let p: BspParams = { MinLeaf = 7; MaxLeaf = 14; RoomPadding = 1 }
              let struct (_, g, _) = MapGen.bspDungeon 60 40 p (Rng.ofSeed 9UL)
              Expect.equal (g.Rooms |> Array.mapi (fun i r -> r.Id = i) |> Array.forall id) true "ids ascend 0..n-1"
              let valid (a, b) = a >= 0 && b >= 0 && a < g.Rooms.Length && b < g.Rooms.Length
              Expect.isTrue (Array.forall valid g.Corridors) "corridors reference real room ids"

          testCase "MapGen.bspDungeon determinism — TileMap and RoomGraph byte-identical for a seed (FsCheck)"
          <| fun () ->
              let p: BspParams = { MinLeaf = 8; MaxLeaf = 16; RoomPadding = 1 }
              let prop (s: uint64) =
                  let seed = s % 100000UL
                  let struct (m1, g1, _) = MapGen.bspDungeon 56 40 p (Rng.ofSeed seed)
                  let struct (m2, g2, _) = MapGen.bspDungeon 56 40 p (Rng.ofSeed seed)
                  let struct (mb, gb, _) = MapGen.bspDungeon 56 40 p (Rng.ofSeed (seed + 1UL))
                  m1 = m2 && g1 = g2 && (m1 <> mb || g1 <> gb)
              Check.One(Config.QuickThrowOnFailure.WithMaxTest 120, prop)

          testCase "MapGen.bspDungeon totality — degenerate size and params never throw (FsCheck)"
          <| fun () ->
              let prop (w: int) (h: int) (mn: int) (mx: int) =
                  let p: BspParams =
                      { MinLeaf = (mn % 20) - 4 // spans <= 0
                        MaxLeaf = (mx % 20) - 8 // spans < MinLeaf
                        RoomPadding = (mn % 6) - 2 } // spans negative
                  let struct (_, _, _) = MapGen.bspDungeon ((w % 30) - 4) ((h % 30) - 4) p (Rng.ofSeed (uint64 (abs mx)))
                  true
              Check.One(Config.QuickThrowOnFailure.WithMaxTest 300, prop)

          // ---- M4: room-graph branching-walk floors ----

          testCase "MapGen.floorLayout — Start at origin, connected graph, special rooms on dead-ends"
          <| fun () ->
              let p: FloorParams = { RoomCount = 15; MaxRooms = 20; SpecialRooms = [ Boss; Treasure; Shop ] }
              let struct (fl, _) = MapGen.floorLayout p (Rng.ofSeed 4UL)
              Expect.isTrue (fl.Rooms.Length >= 1) "at least one room"
              Expect.equal fl.Rooms.[0].Kind Start "first room is Start"
              Expect.equal fl.Rooms.[0].Cell (cell 0 0) "Start at origin"
              // connectivity: BFS over adjacency reaches every room
              let adj = System.Collections.Generic.Dictionary<Cell, ResizeArray<Cell>>()
              let add a b =
                  if not (adj.ContainsKey a) then adj.[a] <- ResizeArray()
                  adj.[a].Add b
              for (a, b) in fl.Adjacency do add a b; add b a
              let seen = System.Collections.Generic.HashSet<Cell>()
              let q = System.Collections.Generic.Queue<Cell>()
              q.Enqueue(cell 0 0)
              seen.Add(cell 0 0) |> ignore
              while q.Count > 0 do
                  let c = q.Dequeue()
                  if adj.ContainsKey c then
                      for n in adj.[c] do
                          if seen.Add n then q.Enqueue n
              Expect.equal seen.Count fl.Rooms.Length "every room reachable from Start (connected graph)"
              // special rooms are dead-ends (one adjacency)
              let degree (c: Cell) = fl.Adjacency |> Array.sumBy (fun (a, b) -> (if a = c then 1 else 0) + (if b = c then 1 else 0))
              let specials = fl.Rooms |> Array.filter (fun rm -> rm.Kind <> Normal && rm.Kind <> Start)
              Expect.isTrue (specials |> Array.forall (fun rm -> degree rm.Cell = 1)) "special rooms are dead-ends"

          testCase "MapGen.floorSeed — deterministic per (run, index) and distinct across indices"
          <| fun () ->
              Expect.equal (MapGen.floorSeed 42UL 3) (MapGen.floorSeed 42UL 3) "deterministic"
              let seeds = [ for i in 0 .. 20 -> MapGen.floorSeed 42UL i ]
              Expect.equal (List.distinct seeds |> List.length) 21 "all 21 floor seeds distinct"

          testCase "MapGen.floorLayout determinism — byte-identical for a seed (FsCheck)"
          <| fun () ->
              let p: FloorParams = { RoomCount = 14; MaxRooms = 18; SpecialRooms = [ Boss; Treasure ] }
              let prop (s: uint64) =
                  let seed = s % 100000UL
                  let struct (a, _) = MapGen.floorLayout p (Rng.ofSeed seed)
                  let struct (b, _) = MapGen.floorLayout p (Rng.ofSeed seed)
                  let struct (c, _) = MapGen.floorLayout p (Rng.ofSeed (seed + 1UL))
                  a = b && a <> c
              Check.One(Config.QuickThrowOnFailure.WithMaxTest 150, prop)

          testCase "MapGen.floorLayout totality — degenerate params yield a single Start room, never throw"
          <| fun () ->
              let prop (rc: int) (mr: int) =
                  let p: FloorParams =
                      { RoomCount = (rc % 30) - 10 // spans <= 0
                        MaxRooms = (mr % 30) - 10
                        SpecialRooms = [ Boss; Treasure; Shop; Secret ] }
                  let struct (fl, _) = MapGen.floorLayout p (Rng.ofSeed (uint64 (abs rc)))
                  fl.Rooms.Length >= 1 && fl.Rooms.[0].Kind = Start
              Check.One(Config.QuickThrowOnFailure.WithMaxTest 300, prop)

          // ---- M5: maze, noise heightmap, scatter ----

          testCase "MapGen.maze — a perfect maze is a single traversable floor with a wall border"
          <| fun () ->
              let struct (m, _) = MapGen.maze 31 21 (Rng.ofSeed 6UL)
              Expect.equal (List.length (MapGen.regions FourWay m)) 1 "one connected floor (perfect maze)"
              Expect.isTrue (traversabilityHarness FourWay m) "every floor cell reachable"
              let borderWall =
                  [ for c in 0 .. m.Width - 1 -> cell c 0 ] @ [ for r in 0 .. m.Height - 1 -> cell 0 r ]
                  |> List.forall (fun c -> MapGen.get m c = ValueSome Wall)
              Expect.isTrue borderWall "border is wall"

          testCase "MapGen.maze determinism — byte-identical for a seed via the M1 harness (FsCheck)"
          <| fun () ->
              let gen (s: uint64) = let struct (m, _) = MapGen.maze 25 17 (Rng.ofSeed s) in m
              let prop (s: uint64) = determinismHarness gen (s % 100000UL)
              Check.One(Config.QuickThrowOnFailure.WithMaxTest 150, prop)

          testCase "MapGen.heightField — heights in [0,255], byte-identical for a seed"
          <| fun () ->
              let p: NoiseParams = { Octaves = 4; Frequency = 0.08; Persistence = 0.5 }
              let a = MapGen.heightField 40 30 p (Rng.ofSeed 8UL)
              let b = MapGen.heightField 40 30 p (Rng.ofSeed 8UL)
              let c = MapGen.heightField 40 30 p (Rng.ofSeed 9UL)
              Expect.isTrue (a.Cells |> Array.forall (fun v -> v >= 0 && v <= 255)) "heights in [0,255]"
              Expect.equal a b "byte-identical for a seed"
              Expect.notEqual a c "differs for another seed"

          testCase "MapGen.classify — each cell maps to the highest threshold <= its height; empty table => empty"
          <| fun () ->
              let field: Grid<int> = { Width = 2; Height = 2; Cells = [| 0; 50; 150; 255 |] }
              let table = [ (0, "water"); (40, "sand"); (120, "grass"); (200, "rock") ]
              let g = MapGen.classify table field
              Expect.equal g.Cells [| "water"; "sand"; "grass"; "rock" |] "banded by threshold"
              // below-all falls to the lowest band
              let field2: Grid<int> = { Width = 1; Height = 1; Cells = [| 5 |] }
              let g2 = MapGen.classify [ (10, "hi") ] field2
              Expect.equal g2.Cells [| "hi" |] "below all uses the lowest band"
              let empty = MapGen.classify ([]: (int * string) list) field
              Expect.equal empty.Cells.Length 0 "empty table => empty grid"

          testCase "MapGen.poissonScatter — samples are mask-eligible and pairwise >= minDist apart"
          <| fun () ->
              let w, h = 40, 30
              let mask: Grid<bool> = { Width = w; Height = h; Cells = Array.create (w * h) true }
              let minDist = 4
              let struct (pts, _) = MapGen.poissonScatter mask minDist (Rng.ofSeed 2UL)
              Expect.isTrue (pts.Length > 5) "several points scattered"
              Expect.isTrue (pts |> List.forall (fun c -> mask.Cells.[c.Row * w + c.Col])) "all on eligible cells"
              let arr = List.toArray pts
              let minOk =
                  [ for i in 0 .. arr.Length - 1 do
                        for j in i + 1 .. arr.Length - 1 ->
                            let dc = arr.[i].Col - arr.[j].Col
                            let dr = arr.[i].Row - arr.[j].Row
                            dc * dc + dr * dr >= minDist * minDist ]
                  |> List.forall id
              Expect.isTrue minOk "all pairs at least minDist apart"

          testCase "MapGen.poissonScatter determinism — byte-identical for a seed; empty mask => empty"
          <| fun () ->
              let w, h = 30, 20
              let mask: Grid<bool> = { Width = w; Height = h; Cells = Array.create (w * h) true }
              let struct (a, _) = MapGen.poissonScatter mask 3 (Rng.ofSeed 1UL)
              let struct (b, _) = MapGen.poissonScatter mask 3 (Rng.ofSeed 1UL)
              Expect.equal a b "deterministic for a seed"
              let allFalse: Grid<bool> = { Width = w; Height = h; Cells = Array.create (w * h) false }
              let struct (none, _) = MapGen.poissonScatter allFalse 3 (Rng.ofSeed 1UL)
              Expect.equal none [] "all-false mask => no points"

          testCase "MapGen M5 totality — degenerate size/params never throw (FsCheck)"
          <| fun () ->
              let prop (w: int) (h: int) (oc: int) (md: int) =
                  let ww = (w % 12) - 4
                  let hh = (h % 12) - 4
                  let struct (_, _) = MapGen.maze ww hh (Rng.ofSeed (uint64 (abs w)))
                  let np: NoiseParams = { Octaves = (oc % 8) - 2; Frequency = 0.1; Persistence = 0.5 }
                  MapGen.heightField ww hh np (Rng.ofSeed (uint64 (abs h))) |> ignore
                  let mask: Grid<bool> = { Width = max 0 ww; Height = max 0 hh; Cells = Array.create (max 0 ww * max 0 hh) true }
                  let struct (_, _) = MapGen.poissonScatter mask ((md % 8) - 3) (Rng.ofSeed 0UL)
                  true
              Check.One(Config.QuickThrowOnFailure.WithMaxTest 200, prop) ]
