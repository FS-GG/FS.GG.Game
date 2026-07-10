namespace FS.GG.Game.Core

/// Parts of a square grid — faces (`Cell`), edges, vertices — and the pixel↔cell map.
/// Promoted from the FS.GG.Rendering `grids` starter fragment (template/fragments/grids).
/// See Grids.fsi for the contract; the reference is https://www.redblobgames.com/grids/parts/
[<RequireQualifiedAccess>]
module Grids =

    type EdgeOrientation =
        | Horizontal
        | Vertical

    [<Struct>]
    type Edge =
        { Col: int
          Row: int
          Orientation: EdgeOrientation }

    [<Struct>]
    type Vertex = { Col: int; Row: int }

    [<Struct>]
    type GridSpec = { CellSize: float; Origin: Point }

    // -----------------------------------------------------------------------------------------
    // Adjacency — the parts relationship table. Pure integer arithmetic; every list is in a fixed,
    // documented order, which is what makes the output byte-deterministic. The conversions are
    // mutually consistent: every edge/corner a cell reports reports that cell back.
    // -----------------------------------------------------------------------------------------

    let cellCorners (c: Cell) : Vertex list =
        [ { Col = c.Col; Row = c.Row } // TL
          { Col = c.Col + 1; Row = c.Row } // TR
          { Col = c.Col + 1; Row = c.Row + 1 } // BR
          { Col = c.Col; Row = c.Row + 1 } ] // BL

    let cellEdges (c: Cell) : Edge list =
        [ { Col = c.Col; Row = c.Row; Orientation = Horizontal } // top
          { Col = c.Col + 1; Row = c.Row; Orientation = Vertical } // right
          { Col = c.Col; Row = c.Row + 1; Orientation = Horizontal } // bottom
          { Col = c.Col; Row = c.Row; Orientation = Vertical } ] // left

    let edgeCells (e: Edge) : Cell list =
        match e.Orientation with
        | Vertical -> [ { Col = e.Col - 1; Row = e.Row }; { Col = e.Col; Row = e.Row } ]
        | Horizontal -> [ { Col = e.Col; Row = e.Row - 1 }; { Col = e.Col; Row = e.Row } ]

    /// The single source of truth for an edge's two ends, shared by `edgeVertices` (which lists them)
    /// and `edgeSegment` (which maps them to pixels). Returning the pair rather than a list keeps the
    /// arity in the type, so neither caller needs an unreachable fallback case.
    let private edgeEnds (e: Edge) : Vertex * Vertex =
        match e.Orientation with
        | Vertical -> { Col = e.Col; Row = e.Row }, { Col = e.Col; Row = e.Row + 1 }
        | Horizontal -> { Col = e.Col; Row = e.Row }, { Col = e.Col + 1; Row = e.Row }

    let edgeVertices (e: Edge) : Vertex list =
        let a, b = edgeEnds e
        [ a; b ]

    let vertexCells (v: Vertex) : Cell list =
        [ { Col = v.Col - 1; Row = v.Row - 1 } // TL
          { Col = v.Col; Row = v.Row - 1 } // TR
          { Col = v.Col; Row = v.Row } // BR
          { Col = v.Col - 1; Row = v.Row } ] // BL

    let vertexEdges (v: Vertex) : Edge list =
        [ { Col = v.Col; Row = v.Row - 1; Orientation = Vertical } // up
          { Col = v.Col; Row = v.Row; Orientation = Horizontal } // right
          { Col = v.Col; Row = v.Row; Orientation = Vertical } // down
          { Col = v.Col - 1; Row = v.Row; Orientation = Horizontal } ] // left

    // -----------------------------------------------------------------------------------------
    // Pixel map. Total: a non-finite/non-positive CellSize falls back to 1.0 and a non-finite origin
    // component to 0.0, so no NaN ever escapes into a Rect/Point the render edge will draw.
    // -----------------------------------------------------------------------------------------

    let private safeCellSize (spec: GridSpec) =
        if System.Double.IsFinite spec.CellSize && spec.CellSize > 0.0 then
            spec.CellSize
        else
            1.0

    let private safeOriginX (spec: GridSpec) =
        if System.Double.IsFinite spec.Origin.X then spec.Origin.X else 0.0

    let private safeOriginY (spec: GridSpec) =
        if System.Double.IsFinite spec.Origin.Y then spec.Origin.Y else 0.0

    let cellRect (spec: GridSpec) (c: Cell) : Rect =
        let s = safeCellSize spec

        { X = safeOriginX spec + float c.Col * s
          Y = safeOriginY spec + float c.Row * s
          Width = s
          Height = s }

    let cellCenter (spec: GridSpec) (c: Cell) : Point =
        let s = safeCellSize spec

        { X = safeOriginX spec + (float c.Col + 0.5) * s
          Y = safeOriginY spec + (float c.Row + 0.5) * s }

    let vertexPoint (spec: GridSpec) (v: Vertex) : Point =
        let s = safeCellSize spec

        { X = safeOriginX spec + float v.Col * s
          Y = safeOriginY spec + float v.Row * s }

    let edgeSegment (spec: GridSpec) (e: Edge) : Point * Point =
        let a, b = edgeEnds e
        vertexPoint spec a, vertexPoint spec b

    let edgeMidpoint (spec: GridSpec) (e: Edge) : Point =
        let a, b = edgeSegment spec e
        { X = (a.X + b.X) * 0.5; Y = (a.Y + b.Y) * 0.5 }

    let cellAt (spec: GridSpec) (p: Point) : Cell =
        let s = safeCellSize spec

        let col =
            if System.Double.IsFinite p.X then
                int (floor ((p.X - safeOriginX spec) / s))
            else
                0

        let row =
            if System.Double.IsFinite p.Y then
                int (floor ((p.Y - safeOriginY spec) / s))
            else
                0

        { Col = col; Row = row }
