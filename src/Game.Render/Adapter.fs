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

    let cellRect (cellSize: float) (cell: FS.GG.Game.Core.Cell) : Rect =
        { X = float cell.Col * cellSize
          Y = float cell.Row * cellSize
          Width = cellSize
          Height = cellSize }

    let cellCentre (cellSize: float) (cell: FS.GG.Game.Core.Cell) : Point =
        { X = (float cell.Col + 0.5) * cellSize
          Y = (float cell.Row + 0.5) * cellSize }

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
