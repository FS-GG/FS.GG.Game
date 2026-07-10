module Game.Core.Tests.SpatialGridTests

// Ported from FS.GG.Rendering tests/Canvas.Tests/SpatialGridTests.fs (ADR-0022 / P2). The two Scene
// opens collapse into one: FS.GG.UI.Canvas + FS.GG.UI.Scene → FS.GG.Game.Core (Point/Rect/Geometry
// and SpatialGrid now share one namespace). Uniform spatial grid for range/splash queries: exact
// results (no false negatives/positives), deterministic insertion order, total on degenerate
// cellSize/radius.

open Expecto
open FsCheck
open FS.GG.Game.Core

let private pt x y : Point = { X = x; Y = y }

// Keep generated coordinates/cell size in a realistic range: astronomically large coords are a
// documented out-of-range case exercised by a dedicated example, not the acceleration contract.
let private clamp (v: float) =
    if System.Double.IsNaN v || System.Double.IsInfinity v then 0.0 else max -1.0e6 (min 1.0e6 v)

let private goodCell (cs: float) =
    if System.Double.IsNaN cs || System.Double.IsInfinity cs || cs <= 0.0 then 1.0 else min 1.0e4 (abs cs + 0.5)

// Brute-force oracles the grid must match exactly.
let private bruteRect (region: Rect) (items: (Point * 'T) list) =
    items |> List.filter (fun (p, _) -> Geometry.containsPoint region p) |> List.map snd

let private bruteRadius (center: Point) (r: float) (items: (Point * 'T) list) =
    let r = if System.Double.IsNaN r || System.Double.IsInfinity r then 0.0 else max 0.0 r
    items
    |> List.filter (fun (p, _) ->
        let dx = p.X - center.X
        let dy = p.Y - center.Y
        dx * dx + dy * dy <= r * r)
    |> List.map snd

let private bruteBounds (region: Rect) (items: (Rect * 'T) list) =
    items |> List.filter (fun (r, _) -> Geometry.intersects region r) |> List.map snd

let private rect x y w h : Rect = { X = x; Y = y; Width = w; Height = h }

/// Enough distinct occupied cells that `candidateIndices` takes its cell-enumeration path rather than
/// the "query box spans more cells than the grid has buckets" O(n) exact scan — which would mask a
/// bucketing bug by finding the item anyway.
let private fillers =
    [ for i in 0..9 -> rect (100.0 + float i) 100.0 0.0 0.0, sprintf "filler%d" i ]

[<Tests>]
let tests =
    testList "Game.Core SpatialGrid (US2, FR-006..FR-009)" [

        test "rectangle query returns exactly the contained items, in insertion order" {
            let items = [ pt 1.0 1.0, "a"; pt 50.0 50.0, "b"; pt 2.0 3.0, "c"; pt 4.0 4.0, "d" ]
            let grid = SpatialGrid.build 8.0 items
            let got = SpatialGrid.query { X = 0.0; Y = 0.0; Width = 5.0; Height = 5.0 } grid
            Expect.equal got [ "a"; "c"; "d" ] "exact contents in insertion order (b is far away)"
        }

        test "radius query returns items within distance, squared-distance boundary inclusive" {
            let items = [ pt 10.0 11.0, "a"; pt 20.0 20.0, "b"; pt 12.0 9.0, "c"; pt 13.0 10.0, "d" ]
            let grid = SpatialGrid.build 4.0 items
            let got = SpatialGrid.queryRadius (pt 10.0 10.0) 3.0 grid
            // a: dist 1, c: dist √8≈2.83, d: dist 3 (== radius, inclusive). b far.
            Expect.equal got [ "a"; "c"; "d" ] "within radius incl. the exact-boundary item, insertion order"
        }

        test "empty grid returns empty results" {
            let grid = SpatialGrid.build 4.0 ([]: (Point * string) list)
            Expect.equal (SpatialGrid.query { X = 0.0; Y = 0.0; Width = 100.0; Height = 100.0 } grid) [] "rect"
            Expect.equal (SpatialGrid.queryRadius (pt 0.0 0.0) 10.0 grid) [] "radius"
        }

        test "non-positive cellSize falls back to a single bucket but stays exact" {
            let items = [ pt 1.0 1.0, "a"; pt 90.0 90.0, "b"; pt 2.0 2.0, "c" ]
            for cs in [ 0.0; -4.0 ] do
                let grid = SpatialGrid.build cs items
                Expect.equal
                    (SpatialGrid.query { X = 0.0; Y = 0.0; Width = 5.0; Height = 5.0 } grid)
                    [ "a"; "c" ]
                    (sprintf "cellSize %f still returns exact contents" cs)
        }

        test "radius <= 0 returns only center-coincident items" {
            let items = [ pt 5.0 5.0, "a"; pt 5.0 5.0, "b"; pt 6.0 5.0, "c" ]
            let grid = SpatialGrid.build 2.0 items
            Expect.equal (SpatialGrid.queryRadius (pt 5.0 5.0) 0.0 grid) [ "a"; "b" ] "radius 0 ⇒ coincident only"
            Expect.equal (SpatialGrid.queryRadius (pt 5.0 5.0) -3.0 grid) [ "a"; "b" ] "negative radius ⇒ radius 0"
        }

        test "zero-area rect returns items exactly on that point" {
            let items = [ pt 3.0 3.0, "a"; pt 3.0 4.0, "b" ]
            let grid = SpatialGrid.build 2.0 items
            Expect.equal (SpatialGrid.query { X = 3.0; Y = 3.0; Width = 0.0; Height = 0.0 } grid) [ "a" ] "point-rect hits (3,3)"
        }

        test "repeat builds and queries are byte-identical (determinism)" {
            let items = [ for i in 0..40 -> pt (float (i % 7)) (float (i % 5)), i ]
            let g1 = SpatialGrid.build 3.0 items
            let g2 = SpatialGrid.build 3.0 items
            let region = { X = 0.0; Y = 0.0; Width = 4.0; Height = 4.0 }
            Expect.equal (SpatialGrid.query region g1) (SpatialGrid.query region g2) "same rect result"
            Expect.equal (SpatialGrid.queryRadius (pt 2.0 2.0) 2.5 g1) (SpatialGrid.queryRadius (pt 2.0 2.0) 2.5 g2) "same radius result"
        }

        test "huge (out-of-range) coordinates stay exact via the O(n) fallback" {
            // A cell index past 2^31 would wrap; the impl must fall back to an exact scan there.
            let items = [ pt 1.0e15 1.0e15, "far"; pt 2.0 2.0, "near"; pt 1.0e15 1.0e15, "far2" ]
            let grid = SpatialGrid.build 1.0 items
            let region = { X = 1.0e15 - 1.0; Y = 1.0e15 - 1.0; Width = 2.0; Height = 2.0 }
            Expect.equal (SpatialGrid.query region grid) [ "far"; "far2" ] "exact hit on huge coords, insertion order"
            Expect.equal (SpatialGrid.queryRadius (pt 1.0e15 1.0e15) 1.0 grid) [ "far"; "far2" ] "radius exact on huge coords"
        }

        testCase "rect query equals brute-force filter, in insertion order (FsCheck)" <| fun () ->
            let prop (coords: (float * float) list) (cs: float) (rx: float) (ry: float) (rw: float) (rh: float) =
                let items = coords |> List.mapi (fun i (x, y) -> pt (clamp x) (clamp y), i)
                let grid = SpatialGrid.build (goodCell cs) items
                let region = { X = clamp rx; Y = clamp ry; Width = abs (clamp rw); Height = abs (clamp rh) }
                SpatialGrid.query region grid = bruteRect region items
            Check.One(Config.QuickThrowOnFailure.WithMaxTest 500, prop)

        testCase "radius query equals brute-force filter, in insertion order (FsCheck)" <| fun () ->
            let prop (coords: (float * float) list) (cs: float) (cx: float) (cy: float) (r: float) =
                let items = coords |> List.mapi (fun i (x, y) -> pt (clamp x) (clamp y), i)
                let grid = SpatialGrid.build (goodCell cs) items
                let center = pt (clamp cx) (clamp cy)
                let radius = clamp r
                SpatialGrid.queryRadius center radius grid = bruteRadius center radius items
            Check.One(Config.QuickThrowOnFailure.WithMaxTest 500, prop)

        // --- buildBounds / queryBounds: bucketing by extent (#84) --------------------------------------

        test "a large item is found from a query that its ORIGIN sits far outside" {
            // The whole point of `buildBounds`. Item "big" spans [-100,100]²; its minimum corner — the
            // only cell `build` would have filed it under — is nowhere near the query. A position-bucketed
            // grid answers this query with `[]` unless the caller dilates it by the largest extent in the
            // world, which is the global constant `buildBounds` exists to delete.
            let items = fillers @ [ rect -100.0 -100.0 200.0 200.0, "big"; rect 89.0 89.0 2.0 2.0, "small" ]
            let grid = SpatialGrid.buildBounds 8.0 items
            Expect.equal (SpatialGrid.queryBounds (rect 89.5 89.5 1.0 1.0) grid) [ "big"; "small" ] "insertion order"
        }

        test "an item spanning many cells is returned exactly once" {
            let items = fillers @ [ rect 0.0 0.0 10.0 10.0, "wide" ]
            let grid = SpatialGrid.buildBounds 1.0 items
            Expect.equal (SpatialGrid.queryBounds (rect 1.0 1.0 8.0 8.0) grid) [ "wide" ] "dedup across its cells"
        }

        test "queryBounds inherits Geometry.intersects' strict edges — touching is not overlap" {
            let items = fillers @ [ rect 0.0 0.0 2.0 2.0, "a" ]
            let grid = SpatialGrid.buildBounds 1.0 items
            Expect.equal (SpatialGrid.queryBounds (rect 2.0 0.0 2.0 2.0) grid) [] "flush against a's right edge"
            Expect.equal (SpatialGrid.queryBounds (rect 1.9 0.0 2.0 2.0) grid) [ "a" ] "overlapping by 0.1"
        }

        test "an item whose extent is read backwards is still bucketed — negative width" {
            // `Geometry.intersects` reports an overlap for a rect of NEGATIVE extent (it compares
            // `X < b.X + b.Width` and `X + Width > b.X` without normalising), so "x" below genuinely
            // overlaps the region. Reading its cell span as `floor 5.0 .. floor 4.0` enumerates nothing
            // and files it under NO cell, which is a false negative rather than a slow query.
            let x = rect 5.0 0.0 -1.0 1.0
            let region = rect 3.9 0.2 1.2 0.5
            Expect.isTrue (Geometry.intersects region x) "precondition: the oracle says these overlap"

            let grid = SpatialGrid.buildBounds 1.0 (fillers @ [ x, "x" ])
            Expect.equal (SpatialGrid.queryBounds region grid) [ "x" ] "found despite the reversed extent"
        }

        test "an item too large to bucket stays exact, and costs only itself" {
            // 1e6 units wide at a 0.5 cell size is ~4 million cells: past the per-item cap, so the grid
            // defers it instead of filing it. It must still answer queries exactly.
            let items = fillers @ [ rect -500000.0 -1.0 1000000.0 2.0, "floor"; rect 0.0 0.5 1.0 1.0, "crate" ]
            let grid = SpatialGrid.buildBounds 0.5 items
            Expect.equal (SpatialGrid.queryBounds (rect 0.1 0.6 0.5 0.5) grid) [ "floor"; "crate" ] "deferred item still found"
            Expect.equal (SpatialGrid.queryBounds (rect 0.1 50.0 0.5 0.5) grid) [] "and not found where it does not reach"
        }

        test "non-finite bounds and a non-finite region stay total and exact" {
            let items = fillers @ [ rect 0.0 0.0 infinity infinity, "inf"; rect 1.0 1.0 1.0 1.0, "unit" ]
            let grid = SpatialGrid.buildBounds 1.0 items
            Expect.equal (SpatialGrid.queryBounds (rect 1.2 1.2 0.5 0.5) grid) [ "inf"; "unit" ] "unbounded item overlaps"
            Expect.equal (SpatialGrid.queryBounds (rect nan nan 1.0 1.0) grid) [] "a NaN region overlaps nothing"
        }

        test "non-positive cellSize falls back to a single bucket but stays exact (bounds grid)" {
            let items = [ rect 0.0 0.0 2.0 2.0, "a"; rect 90.0 90.0 2.0 2.0, "b"; rect 1.0 1.0 2.0 2.0, "c" ]
            for cs in [ 0.0; -4.0; nan ] do
                let grid = SpatialGrid.buildBounds cs items
                Expect.equal
                    (SpatialGrid.queryBounds (rect 0.5 0.5 1.0 1.0) grid)
                    [ "a"; "c" ]
                    (sprintf "cellSize %f still returns exact contents" cs)
        }

        test "on a point-built grid, queryBounds is query's strict-edge counterpart" {
            // `build` files a position as a zero-size extent at itself, so the two constructors share one
            // bucketing rule. `query` is edge-INCLUSIVE, `queryBounds` is strict: (2,2) is on the region's
            // corner, so `query` returns it and `queryBounds` does not.
            let items = [ pt 1.0 1.0, "in"; pt 2.0 2.0, "corner" ]
            let grid = SpatialGrid.build 1.0 items
            let region = { X = 0.0; Y = 0.0; Width = 2.0; Height = 2.0 }
            Expect.equal (SpatialGrid.query region grid) [ "in"; "corner" ] "inclusive edges"
            Expect.equal (SpatialGrid.queryBounds region grid) [ "in" ] "strict edges"
        }

        test "buildBounds preserves the point contract of query/queryRadius via the minimum corner" {
            let items = [ rect 3.0 3.0 4.0 4.0, "a"; rect 50.0 50.0 1.0 1.0, "b" ]
            let grid = SpatialGrid.buildBounds 2.0 items
            Expect.equal (SpatialGrid.query { X = 0.0; Y = 0.0; Width = 5.0; Height = 5.0 } grid) [ "a" ] "min corner (3,3)"
            Expect.equal (SpatialGrid.queryRadius (pt 3.0 3.0) 0.0 grid) [ "a" ] "radius 0 at the min corner"
        }

        testCase "bounds query equals brute-force intersects, in insertion order (FsCheck)" <| fun () ->
            // Extents are NOT abs'd: negative widths are a legal input to `Geometry.intersects` and so
            // must be a legal input here. Cell sizes reach 0.5 against 1e6 coordinates, so the generator
            // also walks the deferred-item path.
            let prop (boxes: (float * float * float * float) list) (cs: float) (r: float * float * float * float) =
                let items = boxes |> List.mapi (fun i (x, y, w, h) -> rect (clamp x) (clamp y) (clamp w) (clamp h), i)
                let grid = SpatialGrid.buildBounds (goodCell cs) items
                let (rx, ry, rw, rh) = r
                let region = rect (clamp rx) (clamp ry) (clamp rw) (clamp rh)
                SpatialGrid.queryBounds region grid = bruteBounds region items
            Check.One(Config.QuickThrowOnFailure.WithMaxTest 500, prop)
    ]
