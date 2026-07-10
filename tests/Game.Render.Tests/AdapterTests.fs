module Game.Render.Tests.AdapterTests

// Feature 002 (ADR-0022 §2 / P4): the render edge. FS.GG.Game.Render.Adapter projects BCL-only
// FS.GG.Game.Core sim primitives onto FS.GG.UI.Scene drawables. Headless (Skia-free): the adapter is
// a pure sim->Scene projection, so these run without the render stack. Convention under test:
// point/rect are identity; cellRect/cellCentre are the integer-logic -> float-presentation seam;
// drawPath routes a Cell list through tile centres; every projection is deterministic (fixed input
// yields a structurally identical Scene).

open Expecto
open FS.GG.UI.Scene
open FS.GG.Game.Core

let private cell c r : Cell = { Col = c; Row = r }
let private simPoint x y : FS.GG.Game.Core.Point = { X = x; Y = y }
let private simRect x y w h : FS.GG.Game.Core.Rect = { X = x; Y = y; Width = w; Height = h }

let private fill = Colors.rgb 10uy 20uy 30uy
let private paint = Paint.stroke (Colors.rgb 1uy 2uy 3uy) 2.0

[<Tests>]
let tests =
    testList "FS.GG.Game.Render Adapter (US1, FR-001)" [

        test "point projects sim coordinates by identity" {
            let expected: FS.GG.UI.Scene.Point = { X = 3.5; Y = -1.25 }
            Expect.equal (FS.GG.Game.Render.Adapter.point (simPoint 3.5 -1.25)) expected "point is identity on X/Y"
        }

        test "rect projects sim fields by identity" {
            let expected: FS.GG.UI.Scene.Rect = { X = 1.0; Y = 2.0; Width = 3.0; Height = 4.0 }
            Expect.equal (FS.GG.Game.Render.Adapter.rect (simRect 1.0 2.0 3.0 4.0)) expected "rect is identity on fields"
        }

        test "cellRect scales a discrete tile to its continuous square (integer->float seam)" {
            let expected: FS.GG.UI.Scene.Rect = { X = 32.0; Y = 48.0; Width = 16.0; Height = 16.0 }
            Expect.equal (FS.GG.Game.Render.Adapter.cellRect 16.0 (cell 2 3)) expected "(Col*size, Row*size, size, size)"
        }

        test "cellCentre is the tile centre at cellSize" {
            let expected: FS.GG.UI.Scene.Point = { X = 40.0; Y = 56.0 }
            Expect.equal (FS.GG.Game.Render.Adapter.cellCentre 16.0 (cell 2 3)) expected "((Col+0.5)*size, (Row+0.5)*size)"
        }

        // The seam delegates to FS.GG.Game.Core.Grids rather than reimplementing the arithmetic, so the
        // sim's grid map and the render edge's cannot drift. These pin the delegation, not merely the
        // result: they are the tests that would go red if someone re-inlined the formula here.

        test "cellRect agrees with Grids.cellRect at origin (0,0) — the duplication is collapsed" {
            let spec: Grids.GridSpec = { CellSize = 16.0; Origin = simPoint 0.0 0.0 }
            let expected = FS.GG.Game.Render.Adapter.rect (Grids.cellRect spec (cell 2 3))
            Expect.equal (FS.GG.Game.Render.Adapter.cellRect 16.0 (cell 2 3)) expected "delegates to Grids"
        }

        test "cellCentre agrees with Grids.cellCenter at origin (0,0) — one function, two spellings" {
            let spec: Grids.GridSpec = { CellSize = 16.0; Origin = simPoint 0.0 0.0 }
            let expected = FS.GG.Game.Render.Adapter.point (Grids.cellCenter spec (cell 2 3))
            Expect.equal (FS.GG.Game.Render.Adapter.cellCentre 16.0 (cell 2 3)) expected "delegates to Grids"
        }

        test "a degenerate cellSize degrades to 1.0 rather than drawing a NaN or inverted Rect" {
            // Inherited from Grids' totality guard. Before delegation these emitted NaN / negative
            // extents straight into the Scene.
            for bad in [ nan; infinity; -infinity; 0.0; -8.0 ] do
                let r = FS.GG.Game.Render.Adapter.cellRect bad (cell 2 3)
                Expect.equal r.Width 1.0 $"cellSize {bad} falls back to 1.0"
                Expect.equal r.Height 1.0 $"cellSize {bad} falls back to 1.0"
                Expect.isTrue (System.Double.IsFinite r.X && System.Double.IsFinite r.Y) "no NaN reaches the Scene"

                let c = FS.GG.Game.Render.Adapter.cellCentre bad (cell 2 3)
                Expect.isTrue (System.Double.IsFinite c.X && System.Double.IsFinite c.Y) "no NaN reaches the Scene"
        }

        test "drawRect emits the same filled rectangle as the Scene constructor" {
            let r = simRect 5.0 6.0 7.0 8.0
            let expected = Scene.filledRectangle (FS.GG.Game.Render.Adapter.rect r) fill
            Expect.equal (FS.GG.Game.Render.Adapter.drawRect fill r) expected "drawRect = filledRectangle over projected rect"
        }

        test "drawCell emits the filled square for the tile" {
            let expected = Scene.filledRectangle (FS.GG.Game.Render.Adapter.cellRect 16.0 (cell 1 1)) fill
            Expect.equal (FS.GG.Game.Render.Adapter.drawCell 16.0 fill (cell 1 1)) expected "drawCell = filledRectangle over cellRect"
        }

        test "drawCells groups the tiles in supplied order (deterministic)" {
            let cells = [ cell 0 0; cell 1 0; cell 0 1 ]
            let expected = Scene.group (cells |> List.map (FS.GG.Game.Render.Adapter.drawCell 8.0 fill))
            Expect.equal (FS.GG.Game.Render.Adapter.drawCells 8.0 fill cells) expected "order-preserving group"
        }

        test "drawPath with fewer than two cells is empty (nothing to connect)" {
            Expect.equal (FS.GG.Game.Render.Adapter.drawPath 16.0 paint []) Scene.empty "empty route -> Scene.empty"
            Expect.equal (FS.GG.Game.Render.Adapter.drawPath 16.0 paint [ cell 2 2 ]) Scene.empty "single cell -> Scene.empty"
        }

        test "drawPath routes a Cell list through tile centres as a polyline" {
            let route = [ cell 0 0; cell 1 0; cell 1 1 ]
            let centres = route |> List.map (FS.GG.Game.Render.Adapter.cellCentre 16.0)
            let commands =
                match centres with
                | head :: tail ->
                    Path.moveTo head.X head.Y :: (tail |> List.map (fun c -> Path.lineTo c.X c.Y))
                | [] -> []
            let expected = Scene.path (Path.create PathFillType.Winding commands) paint
            Expect.equal (FS.GG.Game.Render.Adapter.drawPath 16.0 paint route) expected "polyline through centres"
        }

        test "drawPath projects a real A* route (astar result -> Scene)" {
            // All-walkable 4x4 grid; A* returns a Cell-list route the adapter draws.
            let route =
                Pathfinding.astar FourWay 1000 (fun _ -> true) (cell 0 0) (cell 3 3)
            match route with
            | Some cells ->
                Expect.isGreaterThan (List.length cells) 1 "A* found a multi-cell route"
                let drawn = FS.GG.Game.Render.Adapter.drawPath 20.0 paint cells
                Expect.notEqual drawn Scene.empty "a real route draws a non-empty polyline"
            | None -> failtest "A* should find a route on an all-walkable grid"
        }

        test "drawPoints projects sim points in order" {
            let pts = [ simPoint 0.0 0.0; simPoint 1.5 2.5 ]
            let expected = Scene.points (pts |> List.map FS.GG.Game.Render.Adapter.point) paint
            Expect.equal (FS.GG.Game.Render.Adapter.drawPoints paint pts) expected "order-preserving points node"
        }

        test "projection is deterministic — identical input yields a structurally identical Scene" {
            let cells = [ cell 0 0; cell 3 1; cell 2 4 ]
            let route = [ cell 0 0; cell 1 0; cell 2 0; cell 2 1 ]
            let build () =
                Scene.group
                    [ FS.GG.Game.Render.Adapter.drawCells 12.0 fill cells
                      FS.GG.Game.Render.Adapter.drawPath 12.0 paint route
                      FS.GG.Game.Render.Adapter.drawRect fill (simRect 1.0 2.0 3.0 4.0) ]
            Expect.equal (build ()) (build ()) "two projections of the same input are equal"
        }
    ]
