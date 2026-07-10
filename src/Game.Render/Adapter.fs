namespace FS.GG.Game.Render

open FS.GG.UI.Scene

/// See Adapter.fsi for the contract. Pure, total, deterministic projection of FS.GG.Game.Core sim
/// primitives onto FS.GG.UI.Scene drawables — the ADR-0022 §2 render edge. `Scene`/`Path`/`Colors`
/// are the FS.GG.UI.Scene constructors; sim inputs are fully qualified to keep the two `Point`/`Rect`
/// vocabularies distinct.
[<RequireQualifiedAccess>]
module Adapter =

    let point (p: FS.GG.Game.Core.Point) : Point = { X = p.X; Y = p.Y }

    let rect (r: FS.GG.Game.Core.Rect) : Rect =
        { X = r.X
          Y = r.Y
          Width = r.Width
          Height = r.Height }

    /// Hoisted so `specOf` allocates nothing: `GridSpec` is a struct, but `Point` is a reference
    /// record, and `cellRect`/`cellCentre` run once per tile per frame under `drawCells`/`drawPath`.
    let private simOrigin: FS.GG.Game.Core.Point = { X = 0.0; Y = 0.0 }

    /// The sim-side grid policy this adapter projects: an origin-anchored grid of square cells. The
    /// render edge exposes `cellSize` alone rather than a whole `GridSpec` because a Scene is drawn in
    /// its own coordinate space — a non-zero origin is a Scene transform, not a grid property.
    let private specOf (cellSize: float) : FS.GG.Game.Core.Grids.GridSpec =
        { CellSize = cellSize; Origin = simOrigin }

    let cellRect (cellSize: float) (cell: FS.GG.Game.Core.Cell) : Rect =
        rect (FS.GG.Game.Core.Grids.cellRect (specOf cellSize) cell)

    let cellCentre (cellSize: float) (cell: FS.GG.Game.Core.Cell) : Point =
        point (FS.GG.Game.Core.Grids.cellCenter (specOf cellSize) cell)

    let drawRect (fill: Color) (r: FS.GG.Game.Core.Rect) : Scene =
        Scene.filledRectangle (rect r) fill

    let drawCell (cellSize: float) (fill: Color) (cell: FS.GG.Game.Core.Cell) : Scene =
        Scene.filledRectangle (cellRect cellSize cell) fill

    let drawCells (cellSize: float) (fill: Color) (cells: FS.GG.Game.Core.Cell seq) : Scene =
        cells
        |> Seq.map (drawCell cellSize fill)
        |> List.ofSeq
        |> Scene.group

    let drawPath (cellSize: float) (paint: Paint) (route: FS.GG.Game.Core.Cell list) : Scene =
        match route with
        | []
        | [ _ ] -> Scene.empty
        | head :: tail ->
            let start = cellCentre cellSize head
            let commands =
                Path.moveTo start.X start.Y
                :: (tail
                    |> List.map (fun cell ->
                        let c = cellCentre cellSize cell
                        Path.lineTo c.X c.Y))
            Scene.path (Path.create PathFillType.Winding commands) paint

    let drawPoints (paint: Paint) (points: FS.GG.Game.Core.Point seq) : Scene =
        points |> Seq.map point |> List.ofSeq |> fun ps -> Scene.points ps paint
