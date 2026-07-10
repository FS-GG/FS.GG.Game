namespace FS.GG.Game.Core

// Representation is hidden by the .fsi (opaque SpatialGrid<'T>). `Items` holds every (position, item) in
// insertion order; `Buckets` maps a cell key to the ascending item indices in that cell — the index
// indirection is what preserves insertion order in query results.
//
// `Bounds.[i]` is item i's extent. `build` files a point as a degenerate (zero-size) rect at its
// position, so a point grid is the special case of an extent grid where every extent is a point, and
// `build`/`buildBounds` share one bucketing rule rather than two that must be kept in step.
//
// `Oversized` holds the indices of items whose extent spans more cells than the grid is willing to file
// them in (or whose bounds are not finite). They are a candidate for EVERY query, and are then rejected
// by the same exact filter as any other candidate.
type SpatialGrid<'T> =
    { CellSize: float
      Items: (Point * 'T)[]
      Bounds: Rect[]
      Buckets: Map<struct (int * int), int list>
      Oversized: int list }

[<RequireQualifiedAccess>]
module SpatialGrid =

    // A non-positive or non-finite cell size collapses to one bucket, keeping every operation total.
    let private cellSizeValid (cellSize: float) : bool =
        cellSize > 0.0
        && not (System.Double.IsNaN cellSize)
        && not (System.Double.IsInfinity cellSize)

    let private finite (v: float) : bool =
        not (System.Double.IsNaN v) && not (System.Double.IsInfinity v)

    // Cell index of a coordinate. Matches `keyOf` exactly so build/query bucket the same way.
    let private cellIndex (cellSize: float) (v: float) : int = int (floor (v / cellSize))

    let private keyOf (cellSize: float) (p: Point) : struct (int * int) =
        if cellSizeValid cellSize then
            struct (cellIndex cellSize p.X, cellIndex cellSize p.Y)
        else
            struct (0, 0)

    // The inclusive cell span of the interval between `a` and `b`, as floats (never overflow: huge ⇒
    // infinity, which the caller's magnitude guard catches). Normalised with min/max because
    // `Geometry.intersects` can report an overlap for a rect of NEGATIVE extent, and reading such a rect's
    // span as `floor a .. floor b` would enumerate nothing — filing the item under no cell at all, which
    // is a false negative rather than merely a slow query.
    let private span (cellSize: float) (a: float) (b: float) : struct (float * float) =
        struct (floor (min a b / cellSize), floor (max a b / cellSize))

    // Most cells one item may be filed under before it is deferred to `Oversized` instead. This bounds
    // what a single extreme extent can cost the grid: uncapped, a body 1e9 units wide at a 0.001 cell size
    // would demand 1e12 bucket entries. An oversized item loses ITS OWN acceleration (it is a candidate
    // for every query) and nothing else's — which is the whole point of bucketing by extent.
    let private maxCellsPerItem = 1024.0

    // Cell indices `i`'s bounds occupy, or `ValueNone` when it must be filed as oversized.
    let private cellsOf (cellSize: float) (r: Rect) : struct (int * int * int * int) voption =
        if not (finite r.X && finite r.Y && finite (r.X + r.Width) && finite (r.Y + r.Height)) then
            ValueNone
        else
            let struct (minCf, maxCf) = span cellSize r.X (r.X + r.Width)
            let struct (minRf, maxRf) = span cellSize r.Y (r.Y + r.Height)

            // Cell indices must stay inside int range: past 2^31 `int (floor …)` wraps and breaks the
            // monotonicity that guarantees no false negative. Cap well below it, as `candidateIndices`
            // does for a query box.
            let safe =
                abs minCf < 1.0e9 && abs maxCf < 1.0e9 && abs minRf < 1.0e9 && abs maxRf < 1.0e9

            if not safe || (maxCf - minCf + 1.0) * (maxRf - minRf + 1.0) > maxCellsPerItem then
                ValueNone
            else
                ValueSome(struct (int minCf, int minRf, int maxCf, int maxRf))

    // Candidate item indices (ascending → insertion order) for the axis-aligned box [minX,maxX]×[minY,maxY].
    // Common case (small query relative to the populated grid): enumerate only the box's cells. Fallbacks
    // that keep the function total and bounded: a single bucket for an invalid cell size, and a full O(n)
    // exact scan whenever the box would span more cells than there are buckets — which also absorbs
    // non-finite bounds (a `float` cell estimate of `infinity` trips the same branch, so no billion-cell
    // enumeration and no throw). The exact per-item filter downstream is authoritative either way.
    //
    // `Oversized` is unioned in unconditionally: those items are filed under no cell, so enumerating cells
    // can never reach them.
    let private candidateIndices
        (minX: float)
        (minY: float)
        (maxX: float)
        (maxY: float)
        (grid: SpatialGrid<'T>)
        : int list =
        let cs = grid.CellSize

        let idxs =
            if not (cellSizeValid cs) then
                Map.tryFind (struct (0, 0)) grid.Buckets |> Option.defaultValue []
            elif not (finite minX && finite minY && finite maxX && finite maxY) then
                [ 0 .. grid.Items.Length - 1 ]
            else
                // float cell bounds — never overflow (huge ⇒ infinity), so the decision is safe.
                let minCf, maxCf = floor (minX / cs), floor (maxX / cs)
                let minRf, maxRf = floor (minY / cs), floor (maxY / cs)
                let boxCells = (maxCf - minCf + 1.0) * (maxRf - minRf + 1.0)
                // Enumerating cells is only correct while cell indices stay inside int range: past 2^31
                // `int (floor …)` wraps and breaks the monotonicity that guarantees no false negative.
                // Cap well below 2^31; anything larger (or a box wider than the grid) uses the exact
                // O(n) scan, which never consults bucket keys and so is correct at any magnitude.
                let safe = abs minCf < 1.0e9 && abs maxCf < 1.0e9 && abs minRf < 1.0e9 && abs maxRf < 1.0e9

                if not safe || boxCells > float (Map.count grid.Buckets) then
                    [ 0 .. grid.Items.Length - 1 ]
                else
                    let minC, maxC = int minCf, int maxCf
                    let minR, maxR = int minRf, int maxRf

                    [ for c in minC..maxC do
                          for r in minR..maxR do
                              match Map.tryFind (struct (c, r)) grid.Buckets with
                              | Some v -> yield! v
                              | None -> () ]

        // `List.sort |> List.distinct` restores ascending (insertion) order and collapses an item reached
        // through several of its cells — the dedup that bucketing by extent makes necessary.
        match grid.Oversized with
        | [] -> idxs |> List.sort |> List.distinct
        | over -> idxs @ over |> List.sort |> List.distinct

    // Shared tail of both constructors: bucket item `i` under every cell `cellsFor i` yields, or defer it.
    let private bucketAll (n: int) (cellsFor: int -> struct (int * int * int * int) voption) =
        let mutable buckets = Map.empty
        let oversized = ResizeArray<int>()

        for i in 0 .. n - 1 do
            match cellsFor i with
            | ValueNone -> oversized.Add i
            | ValueSome(struct (minC, minR, maxC, maxR)) ->
                for c in minC..maxC do
                    for r in minR..maxR do
                        let k = struct (c, r)
                        let existing = Map.tryFind k buckets |> Option.defaultValue []
                        buckets <- Map.add k (i :: existing) buckets

        // indices were prepended, so reverse each bucket back to ascending (insertion) order
        struct (buckets |> Map.map (fun _ idxs -> List.rev idxs), List.ofSeq oversized)

    let build (cellSize: float) (items: seq<Point * 'T>) : SpatialGrid<'T> =
        let arr = Seq.toArray items

        let cellsFor i =
            let (p, _) = arr.[i]
            let struct (c, r) = keyOf cellSize p
            ValueSome(struct (c, r, c, r))

        let struct (buckets, oversized) = bucketAll arr.Length cellsFor

        { CellSize = cellSize
          Items = arr
          // A position is a zero-size extent at itself, so `queryBounds` on a point grid agrees with
          // `query` (a degenerate rect intersects exactly what its point is strictly inside of).
          Bounds = arr |> Array.map (fun (p, _) -> { X = p.X; Y = p.Y; Width = 0.0; Height = 0.0 })
          Buckets = buckets
          Oversized = oversized }

    let buildBounds (cellSize: float) (items: seq<Rect * 'T>) : SpatialGrid<'T> =
        let arr = Seq.toArray items
        let bounds = arr |> Array.map fst

        let cellsFor i =
            if not (cellSizeValid cellSize) then
                // One bucket, exactly as `build` degrades: every item is filed under (0,0), so the
                // exact filter alone decides. Not `Oversized` — that would be the same answer by a
                // slower route, and `candidateIndices` already reads bucket (0,0) in this mode.
                ValueSome(struct (0, 0, 0, 0))
            else
                cellsOf cellSize bounds.[i]

        let struct (buckets, oversized) = bucketAll arr.Length cellsFor

        { CellSize = cellSize
          // The item's `position` is its bounds' minimum corner; `query`/`queryRadius` read it.
          Items = arr |> Array.map (fun (r, v) -> ({ X = r.X; Y = r.Y }: Point), v)
          Bounds = bounds
          Buckets = buckets
          Oversized = oversized }

    let query (region: Rect) (grid: SpatialGrid<'T>) : 'T list =
        candidateIndices region.X region.Y (region.X + region.Width) (region.Y + region.Height) grid
        |> List.choose (fun i ->
            let (p, item) = grid.Items.[i]
            if Geometry.containsPoint region p then Some item else None)

    let queryBounds (region: Rect) (grid: SpatialGrid<'T>) : 'T list =
        // Normalised for the same reason `span` is: a negative-extent `region` can still intersect, and
        // reading its span backwards would enumerate no cells and lose the hit.
        let loX, hiX = min region.X (region.X + region.Width), max region.X (region.X + region.Width)
        let loY, hiY = min region.Y (region.Y + region.Height), max region.Y (region.Y + region.Height)

        candidateIndices loX loY hiX hiY grid
        |> List.choose (fun i ->
            let (_, item) = grid.Items.[i]
            if Geometry.intersects region grid.Bounds.[i] then Some item else None)

    let queryRadius (center: Point) (radius: float) (grid: SpatialGrid<'T>) : 'T list =
        let r =
            if not (finite radius) then 0.0 else max 0.0 radius

        let r2 = r * r

        candidateIndices (center.X - r) (center.Y - r) (center.X + r) (center.Y + r) grid
        |> List.choose (fun i ->
            let (p, item) = grid.Items.[i]
            let dx = p.X - center.X
            let dy = p.Y - center.Y
            if dx * dx + dy * dy <= r2 then Some item else None)
