namespace FS.GG.Game.Core

/// Public contract module exposed by the FS.GG.Game.Core package.
/// The **parts of a square grid** and the pixel↔cell map between them.
///
/// A square grid is made of three kinds of part (Red Blob Games, "Parts of a grid"):
///   * FACES    — the cells/tiles. Reuses the shared `Cell` (`{ Col; Row }`), NOT a look-alike type.
///   * EDGES    — the shared boundary between two adjacent faces, named canonically by `Edge`.
///   * VERTICES — the corners where edges meet, named by `Vertex`.
///
/// `Pathfinding` walks faces and `Fov`/`Los` see across them, but neither can name the boundary
/// *between* two faces — which is where a wall lives. `Edge` is that missing name, and `edgeSegment`
/// is what turns a tile grid into the `Visibility.Segment` list a continuous line-of-sight cast
/// consumes. `cellAt` is the inverse of `cellRect`/`cellCenter`: the projection from a continuous
/// position back to the tile containing it, without which nothing can answer "which tile did this
/// land in".
///
/// Promoted from the frozen `grids` starter fragment in FS.GG.Rendering (`template/fragments/grids`),
/// which every game that wanted grid parts had to copy and diverge from.
///
/// Canonical coordinates (the whole scheme in three lines):
///   * `Vertex { Col; Row }`          — the top-left corner of cell `(Col, Row)`.
///   * `Edge Vertical   { Col; Row }` — boundary of cells `(Col-1, Row)` / `(Col, Row)`; runs from
///                                      vertex `(Col, Row)` down to `(Col, Row+1)`.
///   * `Edge Horizontal { Col; Row }` — boundary of cells `(Col, Row-1)` / `(Col, Row)`; runs from
///                                      vertex `(Col, Row)` right to `(Col+1, Row)`.
///
/// Each boundary therefore has exactly ONE name, so two references to the same wall are equal. The
/// adjacency conversions are mutually consistent: every edge/corner a cell reports reports that cell
/// back.
///
/// Everything here is pure, total, and deterministic. The adjacency conversions are integer
/// arithmetic returning lists in a fixed, documented order (no floating-point tie-break, no hash
/// iteration), and the pixel map is straight-line float arithmetic guarded against non-finite input —
/// so identical input yields byte-identical output across runs and platforms, safe to call from a
/// replayed simulation step.
///
/// Square grids only. Hex and triangle grids are a deliberate non-goal: their part vocabulary is a
/// different shape, not a parameter of this one.
///
/// References: https://www.redblobgames.com/grids/parts/ and https://www.redblobgames.com/grids/edges/
[<RequireQualifiedAccess>]
module Grids =

    /// Public contract type exposed by the FS.GG.Game.Core package.
    /// Whether an `Edge` is a horizontal boundary (the top/bottom of a cell) or a vertical one
    /// (the left/right of a cell).
    type EdgeOrientation =
        | Horizontal
        | Vertical

    /// Public contract type exposed by the FS.GG.Game.Core package.
    /// A grid EDGE — the shared boundary between two adjacent faces. `Col`/`Row` plus `Orientation`
    /// give each boundary exactly one canonical name (a `Vertical` edge is named from the cell on its
    /// right; a `Horizontal` edge from the cell below it), so two references to the same boundary are
    /// structurally equal and usable as a set/map key for "which walls still stand".
    ///
    /// It is scoped to `Grids` rather than promoted alongside `Point`/`Rect`/`Cell` because `Grids` is
    /// currently its only consumer — the same rule `Visibility.Segment` follows. A type moved *into*
    /// `Primitives` later is additive; one moved out of it is breaking.
    [<Struct>]
    type Edge =
        { Col: int
          Row: int
          Orientation: EdgeOrientation }

    /// Public contract type exposed by the FS.GG.Game.Core package.
    /// A grid VERTEX — a corner where edges meet. `(Col, Row)` is the top-left corner of cell
    /// `(Col, Row)`, so the corner lattice is offset by half a cell from the faces. Distinct from
    /// `Cell` despite the identical shape: a `Cell` indexes a face, a `Vertex` indexes a corner, and
    /// conflating them is the bug this type exists to prevent.
    [<Struct>]
    type Vertex = { Col: int; Row: int }

    /// Public contract type exposed by the FS.GG.Game.Core package.
    /// The pixel-mapping policy. `CellSize` is the side length of a cell in continuous units;
    /// `Origin` is the position of vertex `(0, 0)` (the top-left corner of cell `(0, 0)`).
    ///
    /// Degenerate values do not throw and never emit a NaN: a non-finite or non-positive `CellSize`
    /// falls back to `1.0`, and a non-finite `Origin` component falls back to `0.0`.
    ///
    /// A struct, like `Cell`/`Edge`/`Vertex`: the pixel map is called once per tile per frame by the
    /// render edge, and a heap-allocated policy record would put one gen0 allocation behind every
    /// tile drawn.
    [<Struct>]
    type GridSpec = { CellSize: float; Origin: Point }

    /// Public contract function exposed by the FS.GG.Game.Core package.
    /// A cell's four corners, in top-left, top-right, bottom-right, bottom-left order.
    val cellCorners: c: Cell -> Vertex list

    /// Public contract function exposed by the FS.GG.Game.Core package.
    /// A cell's four edges, in top, right, bottom, left order. Each returned edge reports this cell
    /// back from `edgeCells`.
    val cellEdges: c: Cell -> Edge list

    /// Public contract function exposed by the FS.GG.Game.Core package.
    /// The two faces an edge separates, in ascending order: left-then-right for a `Vertical` edge,
    /// above-then-below for a `Horizontal` one. This is the "this wall fell — which two tiles now
    /// connect?" conversion.
    val edgeCells: e: Edge -> Cell list

    /// Public contract function exposed by the FS.GG.Game.Core package.
    /// The two vertices at an edge's ends, start then end along the edge's natural direction (down for
    /// `Vertical`, right for `Horizontal`).
    val edgeVertices: e: Edge -> Vertex list

    /// Public contract function exposed by the FS.GG.Game.Core package.
    /// The four faces meeting at a vertex, in top-left, top-right, bottom-right, bottom-left order.
    val vertexCells: v: Vertex -> Cell list

    /// Public contract function exposed by the FS.GG.Game.Core package.
    /// The four edges meeting at a vertex, in up, right, down, left order.
    val vertexEdges: v: Vertex -> Edge list

    /// Public contract function exposed by the FS.GG.Game.Core package.
    /// The axis-aligned bounding box a cell occupies in continuous space. Total: a degenerate `spec`
    /// degrades per `GridSpec` rather than throwing, so no returned coordinate is ever NaN.
    val cellRect: spec: GridSpec -> c: Cell -> Rect

    /// Public contract function exposed by the FS.GG.Game.Core package.
    /// The continuous centre of a cell. Total; never NaN.
    val cellCenter: spec: GridSpec -> c: Cell -> Point

    /// Public contract function exposed by the FS.GG.Game.Core package.
    /// The continuous position of a vertex. Total; never NaN.
    val vertexPoint: spec: GridSpec -> v: Vertex -> Point

    /// Public contract function exposed by the FS.GG.Game.Core package.
    /// An edge as its two endpoint positions — stroke this to draw a fence/border, or feed it to
    /// `Visibility.Segment` to make a tile wall occlude a continuous line-of-sight cast. Endpoints are
    /// in `edgeVertices` order. Total; never NaN.
    val edgeSegment: spec: GridSpec -> e: Edge -> Point * Point

    /// Public contract function exposed by the FS.GG.Game.Core package.
    /// The continuous midpoint of an edge. Total; never NaN.
    val edgeMidpoint: spec: GridSpec -> e: Edge -> Point

    /// Public contract function exposed by the FS.GG.Game.Core package.
    /// Inverse of `cellRect`/`cellCenter`: the cell containing a continuous position. This is the
    /// pointer-picking and impact-to-tile projection — "which tile did this shell land in".
    ///
    /// Floor-based, so a position exactly on a boundary belongs to the cell to its right/below, and
    /// `cellAt spec` composed with the top-left corner of `cellRect spec` is the identity on `Cell`.
    ///
    /// Total: a non-finite coordinate maps to `0` on that axis rather than emitting a NaN cell, and a
    /// degenerate `spec` degrades per `GridSpec`. A position beyond the range of `int` on an axis
    /// saturates there (unchecked float→int conversion); the grid is not a bounded structure, so this
    /// is a coordinate far outside any playable field rather than a condition to raise on.
    val cellAt: spec: GridSpec -> p: Point -> Cell
