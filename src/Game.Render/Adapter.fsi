namespace FS.GG.Game.Render

/// Public contract module exposed by the FS.GG.Game.Render package.
///
/// Pure projection of `FS.GG.Game.Core` simulation primitives onto `FS.GG.UI.Scene` drawables —
/// the render edge of ADR-0022 §2. `FS.GG.Game.Core` stays BCL-only and reaches up to nothing;
/// THIS adapter is the one place that reaches UP to `FS.GG.UI.Scene`. Every function is a total,
/// deterministic, side-effect-free projection: identical sim input yields a structurally identical
/// (byte-identical) `Scene`, so a headless sim can be drawn without the sim core taking any
/// dependency on the render stack.
///
/// Types are fully qualified because both `FS.GG.Game.Core` and `FS.GG.UI.Scene` expose `Point`
/// and `Rect`: the sim (`FS.GG.Game.Core.*`) is the source vocabulary, the render (`FS.GG.UI.Scene.*`)
/// the target. The `Cell`/`cellSize` functions are the integer-logic → float-presentation seam
/// (ADR-0022 §2.1): the sim reasons in whole tiles, the render edge is the sole place tiles become
/// continuous coordinates.
[<RequireQualifiedAccess>]
module Adapter =

    /// Project a continuous simulation point onto its Scene counterpart. Identity on coordinates
    /// (both carry the same float `X`/`Y`); it only re-labels sim vocabulary as render vocabulary.
    val point: p: FS.GG.Game.Core.Point -> FS.GG.UI.Scene.Point

    /// Project a continuous simulation rectangle onto its Scene counterpart. Identity on fields.
    val rect: r: FS.GG.Game.Core.Rect -> FS.GG.UI.Scene.Rect

    /// The continuous Scene rectangle a discrete tile `Cell` occupies at `cellSize` —
    /// `(Col*cellSize, Row*cellSize, cellSize, cellSize)`. The integer-logic → float-presentation
    /// seam: whole-tile sim indices become continuous render coordinates here and only here.
    ///
    /// Delegates to `FS.GG.Game.Core.Grids.cellRect` at origin `(0, 0)` rather than reimplementing the
    /// arithmetic, so the sim's grid map and the render edge's cannot drift apart. It therefore
    /// inherits `Grids`' totality: a non-finite or non-positive `cellSize` falls back to `1.0` instead
    /// of emitting a NaN or inverted `Rect` for the Scene to draw.
    val cellRect: cellSize: float -> cell: FS.GG.Game.Core.Cell -> FS.GG.UI.Scene.Rect

    /// The continuous centre of a tile `Cell` at `cellSize` — `((Col+0.5)*cellSize,
    /// (Row+0.5)*cellSize)`. Used to route a path through tile centres.
    ///
    /// Delegates to `FS.GG.Game.Core.Grids.cellCenter` (same function, sim spelling) at origin
    /// `(0, 0)`; same drift-proofing and same `cellSize` fallback as `cellRect`. The name keeps the
    /// render stack's `centre` spelling — renaming a published contract would be breaking, and the
    /// seam is exactly where the two spellings are allowed to meet.
    val cellCentre: cellSize: float -> cell: FS.GG.Game.Core.Cell -> FS.GG.UI.Scene.Point

    /// Draw a simulation rectangle as a filled Scene rectangle.
    val drawRect: fill: FS.GG.UI.Scene.Color -> r: FS.GG.Game.Core.Rect -> FS.GG.UI.Scene.Scene

    /// Draw a single tile `Cell` as a filled Scene square at `cellSize`.
    val drawCell:
        cellSize: float -> fill: FS.GG.UI.Scene.Color -> cell: FS.GG.Game.Core.Cell -> FS.GG.UI.Scene.Scene

    /// Draw a set of tiles (a walkable/occupied set) as a grouped Scene of filled squares, in the
    /// order supplied — no reordering, so the result is deterministic in enumeration order.
    val drawCells:
        cellSize: float ->
        fill: FS.GG.UI.Scene.Color ->
        cells: FS.GG.Game.Core.Cell seq ->
            FS.GG.UI.Scene.Scene

    /// Draw a route (a `Cell list`, e.g. from `FS.GG.Game.Core.Pathfinding.astar`/`bfs`) as a Scene
    /// polyline through the tile centres, painted with `paint`. A route of fewer than two cells has
    /// nothing to connect and yields `Scene.empty`. Deterministic in route order.
    val drawPath:
        cellSize: float ->
        paint: FS.GG.UI.Scene.Paint ->
        route: FS.GG.Game.Core.Cell list ->
            FS.GG.UI.Scene.Scene

    /// Draw simulation points as a Scene points node painted with `paint`, in supplied order.
    val drawPoints:
        paint: FS.GG.UI.Scene.Paint -> points: FS.GG.Game.Core.Point seq -> FS.GG.UI.Scene.Scene
