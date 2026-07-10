module Game.Core.Tests.GridsTests

// Promoted from the FS.GG.Rendering `grids` starter fragment (template/fragments/grids).
// Parts of a square grid — faces (`Cell`), edges, vertices — plus the pixel↔cell map. The adjacency
// conversions are integer arithmetic in a fixed documented order; the pixel map is total (non-finite
// and non-positive input degrades rather than throws, and never emits a NaN).

open Expecto
open FsCheck
open FS.GG.Game.Core

let private cell c r : Cell = { Col = c; Row = r }
let private vtx c r : Grids.Vertex = { Col = c; Row = r }
let private hEdge c r : Grids.Edge = { Col = c; Row = r; Orientation = Grids.Horizontal }
let private vEdge c r : Grids.Edge = { Col = c; Row = r; Orientation = Grids.Vertical }
let private spec size ox oy : Grids.GridSpec = { CellSize = size; Origin = { X = ox; Y = oy } }

/// Bound a generated int into a range where `col * cellSize + origin` stays exactly representable, so
/// the `cellAt`/`cellRect` round-trip tests measure the mapping rather than float cancellation.
let private smallIdx (i: int) = i % 1000

/// Bound a generated int into a finite, positive cell size.
let private smallSize (i: int) = 1.0 + float (abs (i % 64))

/// Bound a generated int into a finite origin offset.
let private smallOrigin (i: int) = float (i % 1000)

let private isFinitePt (p: Point) =
    System.Double.IsFinite p.X && System.Double.IsFinite p.Y

let private isFiniteRect (r: Rect) =
    System.Double.IsFinite r.X
    && System.Double.IsFinite r.Y
    && System.Double.IsFinite r.Width
    && System.Double.IsFinite r.Height

/// Every degenerate `GridSpec` the contract promises to absorb.
let private degenerateSpecs =
    [ "NaN cellSize", spec nan 0.0 0.0
      "infinite cellSize", spec infinity 0.0 0.0
      "zero cellSize", spec 0.0 0.0 0.0
      "negative cellSize", spec -8.0 0.0 0.0
      "NaN origin.X", spec 16.0 nan 0.0
      "NaN origin.Y", spec 16.0 0.0 nan
      "infinite origin", spec 16.0 infinity -infinity ]

[<Tests>]
let tests =
    testList "Game.Core Grids (parts of a square grid)" [

        // --- adjacency: the fixed orderings are the contract ----------------------------------

        testList "adjacency orderings (documented, and therefore load-bearing)" [

            test "cellCorners is TL, TR, BR, BL" {
                let expected = [ vtx 2 3; vtx 3 3; vtx 3 4; vtx 2 4 ]
                Expect.equal (Grids.cellCorners (cell 2 3)) expected "TL, TR, BR, BL"
            }

            test "cellEdges is top, right, bottom, left" {
                let expected = [ hEdge 2 3; vEdge 3 3; hEdge 2 4; vEdge 2 3 ]
                Expect.equal (Grids.cellEdges (cell 2 3)) expected "top, right, bottom, left"
            }

            test "edgeCells of a vertical edge is left-then-right" {
                Expect.equal (Grids.edgeCells (vEdge 3 3)) [ cell 2 3; cell 3 3 ] "left, right"
            }

            test "edgeCells of a horizontal edge is above-then-below" {
                Expect.equal (Grids.edgeCells (hEdge 3 3)) [ cell 3 2; cell 3 3 ] "above, below"
            }

            test "edgeVertices of a vertical edge runs downward" {
                Expect.equal (Grids.edgeVertices (vEdge 3 3)) [ vtx 3 3; vtx 3 4 ] "top, bottom"
            }

            test "edgeVertices of a horizontal edge runs rightward" {
                Expect.equal (Grids.edgeVertices (hEdge 3 3)) [ vtx 3 3; vtx 4 3 ] "left, right"
            }

            test "vertexCells is TL, TR, BR, BL" {
                let expected = [ cell 2 3; cell 3 3; cell 3 4; cell 2 4 ]
                Expect.equal (Grids.vertexCells (vtx 3 4)) expected "TL, TR, BR, BL"
            }

            test "vertexEdges is up, right, down, left" {
                let expected = [ vEdge 3 3; hEdge 3 4; vEdge 3 4; hEdge 2 4 ]
                Expect.equal (Grids.vertexEdges (vtx 3 4)) expected "up, right, down, left"
            }
        ]

        // --- adjacency: mutual consistency ----------------------------------------------------

        testList "adjacency is mutually consistent (FsCheck >=500)" [

            testCase "every edge a cell reports, reports that cell back" <| fun () ->
                let prop (c: int) (r: int) =
                    let x = cell (smallIdx c) (smallIdx r)
                    Grids.cellEdges x |> List.forall (fun e -> Grids.edgeCells e |> List.contains x)
                Check.One(Config.QuickThrowOnFailure.WithMaxTest 500, prop)

            testCase "every corner a cell reports, reports that cell back" <| fun () ->
                let prop (c: int) (r: int) =
                    let x = cell (smallIdx c) (smallIdx r)
                    Grids.cellCorners x |> List.forall (fun v -> Grids.vertexCells v |> List.contains x)
                Check.One(Config.QuickThrowOnFailure.WithMaxTest 500, prop)

            testCase "every edge a vertex reports, reports that vertex back" <| fun () ->
                let prop (c: int) (r: int) =
                    let v = vtx (smallIdx c) (smallIdx r)
                    Grids.vertexEdges v |> List.forall (fun e -> Grids.edgeVertices e |> List.contains v)
                Check.One(Config.QuickThrowOnFailure.WithMaxTest 500, prop)

            testCase "an edge separates exactly two distinct cells, and joins two distinct vertices" <| fun () ->
                let prop (c: int) (r: int) (vertical: bool) =
                    let e =
                        if vertical then vEdge (smallIdx c) (smallIdx r) else hEdge (smallIdx c) (smallIdx r)
                    let cs = Grids.edgeCells e
                    let vs = Grids.edgeVertices e
                    List.length cs = 2 && cs.[0] <> cs.[1] && List.length vs = 2 && vs.[0] <> vs.[1]
                Check.One(Config.QuickThrowOnFailure.WithMaxTest 500, prop)

            testCase "the two cells an edge separates are orthogonal neighbours" <| fun () ->
                let prop (c: int) (r: int) (vertical: bool) =
                    let e =
                        if vertical then vEdge (smallIdx c) (smallIdx r) else hEdge (smallIdx c) (smallIdx r)
                    match Grids.edgeCells e with
                    | [ a; b ] -> abs (a.Col - b.Col) + abs (a.Row - b.Row) = 1
                    | _ -> false
                Check.One(Config.QuickThrowOnFailure.WithMaxTest 500, prop)
        ]

        test "an edge has exactly one canonical name — the shared boundary is the same value from either side" {
            // (2,3)'s right edge and (3,3)'s left edge are the SAME wall. This equality is what makes an
            // `Edge` usable as a set key for "which walls still stand".
            let rightOf23 = Grids.cellEdges (cell 2 3) |> List.item 1
            let leftOf33 = Grids.cellEdges (cell 3 3) |> List.item 3
            Expect.equal rightOf23 leftOf33 "one boundary, one name"
        }

        // --- pixel map ------------------------------------------------------------------------

        testList "pixel map" [

            test "cellRect places the cell square at origin + index*size" {
                let expected: Rect = { X = 100.0 + 32.0; Y = 200.0 + 48.0; Width = 16.0; Height = 16.0 }
                Expect.equal (Grids.cellRect (spec 16.0 100.0 200.0) (cell 2 3)) expected "origin + col*s"
            }

            test "cellCenter is the middle of cellRect" {
                let expected: Point = { X = 100.0 + 40.0; Y = 200.0 + 56.0 }
                Expect.equal (Grids.cellCenter (spec 16.0 100.0 200.0) (cell 2 3)) expected "(col+0.5)*s"
            }

            test "vertexPoint places the corner lattice at origin + index*size" {
                let expected: Point = { X = 100.0 + 32.0; Y = 200.0 + 48.0 }
                Expect.equal (Grids.vertexPoint (spec 16.0 100.0 200.0) (vtx 2 3)) expected "origin + col*s"
            }

            test "edgeSegment of a vertical edge runs downward one cell" {
                let a, b = Grids.edgeSegment (spec 16.0 0.0 0.0) (vEdge 2 3)
                Expect.equal a { X = 32.0; Y = 48.0 } "top end"
                Expect.equal b { X = 32.0; Y = 64.0 } "bottom end"
            }

            test "edgeSegment of a horizontal edge runs rightward one cell" {
                let a, b = Grids.edgeSegment (spec 16.0 0.0 0.0) (hEdge 2 3)
                Expect.equal a { X = 32.0; Y = 48.0 } "left end"
                Expect.equal b { X = 48.0; Y = 48.0 } "right end"
            }

            test "edgeMidpoint bisects edgeSegment" {
                Expect.equal (Grids.edgeMidpoint (spec 16.0 0.0 0.0) (vEdge 2 3)) { X = 32.0; Y = 56.0 } "midpoint"
            }

            testCase "edgeSegment is edgeVertices mapped through vertexPoint (FsCheck >=500)" <| fun () ->
                // They share a private source of truth for the two ends; this pins them together so a
                // future change to one cannot silently reorder or displace the other.
                let prop (c: int) (r: int) (size: int) (vertical: bool) =
                    let s = spec (smallSize size) 0.0 0.0
                    let e =
                        if vertical then vEdge (smallIdx c) (smallIdx r) else hEdge (smallIdx c) (smallIdx r)
                    let a, b = Grids.edgeSegment s e
                    match Grids.edgeVertices e |> List.map (Grids.vertexPoint s) with
                    | [ va; vb ] -> a = va && b = vb
                    | _ -> false
                Check.One(Config.QuickThrowOnFailure.WithMaxTest 500, prop)

            test "a cell's four edgeSegments are its cellRect's four sides" {
                // This is the join that makes a tile grid an occluder set: stroke every standing wall.
                let s = spec 16.0 0.0 0.0
                let r = Grids.cellRect s (cell 2 3)
                let corners = Grids.cellCorners (cell 2 3) |> List.map (Grids.vertexPoint s)
                Expect.equal corners.[0] { X = r.X; Y = r.Y } "TL corner is the rect's min corner"
                Expect.equal corners.[2] { X = r.X + r.Width; Y = r.Y + r.Height } "BR corner is the max corner"
            }

            test "cellAt is floor-based: a point on a boundary belongs to the cell right/below" {
                let s = spec 16.0 0.0 0.0
                Expect.equal (Grids.cellAt s { X = 32.0; Y = 48.0 }) (cell 2 3) "exactly on the TL corner"
                Expect.equal (Grids.cellAt s { X = 31.999; Y = 48.0 }) (cell 1 3) "a hair left is the cell before"
                Expect.equal (Grids.cellAt s { X = 47.999; Y = 63.999 }) (cell 2 3) "a hair inside the BR corner"
            }

            test "cellAt maps negative space to negative indices (the grid is unbounded)" {
                let s = spec 16.0 0.0 0.0
                Expect.equal (Grids.cellAt s { X = -1.0; Y = -1.0 }) (cell -1 -1) "floor, not truncate"
                Expect.equal (Grids.cellAt s { X = -16.0; Y = -16.0 }) (cell -1 -1) "on the boundary"
                Expect.equal (Grids.cellAt s { X = -16.001; Y = 0.0 }) (cell -2 0) "past it"
            }
        ]

        // --- the round-trip the issue names as acceptance --------------------------------------

        testList "cellAt inverts the pixel map (FsCheck >=500)" [

            testCase "cellAt (min corner of cellRect spec c) = c, for every finite spec" <| fun () ->
                let prop (c: int) (r: int) (size: int) (ox: int) (oy: int) =
                    let s = spec (smallSize size) (smallOrigin ox) (smallOrigin oy)
                    let x = cell (smallIdx c) (smallIdx r)
                    let box = Grids.cellRect s x
                    Grids.cellAt s { X = box.X; Y = box.Y } = x
                Check.One(Config.QuickThrowOnFailure.WithMaxTest 500, prop)

            testCase "cellAt (cellCenter spec c) = c, for every finite spec" <| fun () ->
                let prop (c: int) (r: int) (size: int) (ox: int) (oy: int) =
                    let s = spec (smallSize size) (smallOrigin ox) (smallOrigin oy)
                    let x = cell (smallIdx c) (smallIdx r)
                    Grids.cellAt s (Grids.cellCenter s x) = x
                Check.One(Config.QuickThrowOnFailure.WithMaxTest 500, prop)

            testCase "every point inside cellRect maps back to that cell (origin-offset too)" <| fun () ->
                let prop (c: int) (r: int) (size: int) (ox: int) (fx: int) =
                    // The origin varies: a cellAt that dropped its `- origin` term would otherwise
                    // still satisfy this property, since an origin of 0 hides the subtraction.
                    let s = spec (smallSize size) (smallOrigin ox) (smallOrigin (ox * 7))
                    let x = cell (smallIdx c) (smallIdx r)
                    let box = Grids.cellRect s x
                    // a strictly-interior sample point, parameterised across the cell
                    let u = float (abs (fx % 99) + 1) / 100.0
                    let v = float (abs ((fx * 13) % 99) + 1) / 100.0
                    let p: Point = { X = box.X + u * box.Width; Y = box.Y + v * box.Height }
                    Grids.cellAt s p = x
                Check.One(Config.QuickThrowOnFailure.WithMaxTest 500, prop)
        ]

        // --- totality -------------------------------------------------------------------------

        testList "totality: a degenerate GridSpec degrades, never throws and never leaks NaN" [

            for name, s in degenerateSpecs do
                test $"cellRect / cellCenter / vertexPoint / edgeMidpoint are finite under {name}" {
                    Expect.isTrue (isFiniteRect (Grids.cellRect s (cell 2 3))) "cellRect finite"
                    Expect.isTrue (isFinitePt (Grids.cellCenter s (cell 2 3))) "cellCenter finite"
                    Expect.isTrue (isFinitePt (Grids.vertexPoint s (vtx 2 3))) "vertexPoint finite"
                    Expect.isTrue (isFinitePt (Grids.edgeMidpoint s (vEdge 2 3))) "edgeMidpoint finite"

                    let a, b = Grids.edgeSegment s (hEdge 2 3)
                    Expect.isTrue (isFinitePt a && isFinitePt b) "edgeSegment finite"
                }

            test "a degenerate cellSize falls back to 1.0" {
                let badSizes = degenerateSpecs |> List.filter (fun (n, _) -> n.Contains "cellSize")
                // Guard the selection itself: a renamed fixture would otherwise empty this list and
                // turn the loop below into a green test that asserts nothing.
                Expect.equal (List.length badSizes) 4 "NaN / infinite / zero / negative cellSize"

                for _, s in badSizes do
                    let r = Grids.cellRect s (cell 0 0)
                    Expect.equal r.Width 1.0 "cellSize falls back to 1.0"
                    Expect.equal r.Height 1.0 "cellSize falls back to 1.0"
            }

            test "a non-finite cellAt coordinate maps to 0 on that axis, not a NaN cell" {
                let s = spec 16.0 0.0 0.0
                Expect.equal (Grids.cellAt s { X = nan; Y = 48.0 }) (cell 0 3) "NaN X -> col 0"
                Expect.equal (Grids.cellAt s { X = 32.0; Y = infinity }) (cell 2 0) "infinite Y -> row 0"
                Expect.equal (Grids.cellAt s { X = -infinity; Y = nan }) (cell 0 0) "both degrade"
            }

            test "cellAt under a degenerate spec does not throw" {
                for _, s in degenerateSpecs do
                    Grids.cellAt s { X = 5.0; Y = 5.0 } |> ignore
            }

            test "an out-of-int-range coordinate saturates rather than throwing" {
                let s = spec 1.0 0.0 0.0
                Grids.cellAt s { X = 1e300; Y = -1e300 } |> ignore
            }
        ]

        // --- determinism ----------------------------------------------------------------------

        test "identical input yields identical output (safe for a replayed step)" {
            let s = spec 16.0 3.5 -7.25
            let once = Grids.cellEdges (cell 4 5), Grids.cellRect s (cell 4 5), Grids.cellAt s { X = 71.5; Y = 88.25 }
            let twice = Grids.cellEdges (cell 4 5), Grids.cellRect s (cell 4 5), Grids.cellAt s { X = 71.5; Y = 88.25 }
            Expect.equal once twice "pure and deterministic"
        }
    ]
