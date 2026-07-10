namespace FS.GG.Game.Core

[<RequireQualifiedAccess>]
module Fov =

    // An exact rational slope, `Num/Den`, with `Den > 0` always. Symmetric shadowcasting's whole
    // determinism argument rests on never evaluating this as a float: every comparison below is a
    // cross-multiplication, so a slope window is compared with integer arithmetic only. The
    // intermediate products are taken in int64 because the row scan multiplies depth by a numerator
    // that is itself O(depth) — int32 would overflow at radii long before the O(radius^2) scan does.
    [<Struct>]
    type private Slope = { Num: int; Den: int }

    // Integer division rounding toward -inf / +inf. `den` is always > 0 here, so the sign of the
    // quotient is the sign of `num`, and `/` truncating toward zero is only wrong on a non-zero
    // remainder.
    let private floorDiv (num: int64) (den: int64) : int64 =
        let q = num / den
        if num % den <> 0L && num < 0L then q - 1L else q

    let private ceilDiv (num: int64) (den: int64) : int64 =
        let q = num / den
        if num % den <> 0L && num > 0L then q + 1L else q

    // The slope through the NEAR edge of tile `col` on row `depth`: Fraction(2*col - 1, 2*depth).
    // `depth >= 1` on every call, so `Den > 0` holds.
    let private slopeOf (depth: int) (col: int) : Slope = { Num = 2 * col - 1; Den = 2 * depth }

    // round_ties_up(depth * slope)  = floor(depth*Num/Den + 1/2)
    let private minColOf (depth: int) (s: Slope) : int =
        int (floorDiv (2L * int64 depth * int64 s.Num + int64 s.Den) (2L * int64 s.Den))

    // round_ties_down(depth * slope) = ceil(depth*Num/Den - 1/2)
    let private maxColOf (depth: int) (s: Slope) : int =
        int (ceilDiv (2L * int64 depth * int64 s.Num - int64 s.Den) (2L * int64 s.Den))

    // A floor is revealed only when its CENTRE lies within the slope window — `col` between
    // `depth*start` and `depth*end`. This is the test that makes floor-to-floor visibility symmetric;
    // walls bypass it (they are revealed on sight, the "expansive walls" rule).
    let private isSymmetric (depth: int) (start: Slope) (end_: Slope) (col: int) : bool =
        int64 col * int64 start.Den >= int64 depth * int64 start.Num
        && int64 col * int64 end_.Den <= int64 depth * int64 end_.Num

    // (depth, col) in quadrant-local space -> absolute cell. `depth` grows away from the origin, `col`
    // runs across the row from negative to positive. The |col| = depth diagonals are scanned by two
    // adjacent quadrants; the result is a Set, so a doubly-scanned seam cell lands in it exactly once
    // rather than being double-counted or dropped.
    let private transform (origin: Cell) (quadrant: int) (depth: int) (col: int) : Cell =
        match quadrant with
        | 0 -> { Col = origin.Col + col; Row = origin.Row - depth } // north
        | 1 -> { Col = origin.Col + col; Row = origin.Row + depth } // south
        | 2 -> { Col = origin.Col + depth; Row = origin.Row + col } // east
        | _ -> { Col = origin.Col - depth; Row = origin.Row + col } // west

    let fov (isTransparent: Cell -> bool) (origin: Cell) (radius: int) : Set<Cell> =
        if radius < 0 then
            Set.empty
        else
            let r2 = int64 radius * int64 radius

            // Circular clipping is orthogonal to the scan: it gates what is REVEALED, never what casts
            // a shadow. Keeping it out of the blocking logic is what stops a smaller radius from
            // revealing a cell a larger one hid, and keeps the pairwise radius test symmetric.
            let withinRadius (c: Cell) =
                let dCol = int64 (c.Col - origin.Col)
                let dRow = int64 (c.Row - origin.Row)
                dCol * dCol + dRow * dRow <= r2

            // Scan one row of one quadrant, nearest-to-farthest, carrying the [start, end] slope window.
            // On a floor->wall transition recurse one row deeper with the window narrowed to the wall's
            // near edge; on a wall->floor transition reopen the window past the wall's near edge and
            // keep walking the row. A row that ends on a floor continues into the next row.
            let rec scanRow (quadrant: int) (depth: int) (start: Slope) (end_: Slope) (acc: Set<Cell>) : Set<Cell> =
                if depth > radius then
                    acc
                else
                    let maxCol = maxColOf depth end_

                    // `start` is threaded through the column walk because a wall->floor transition widens
                    // it mid-row; `prev` is the previous tile's opacity (ValueNone before the first tile,
                    // so neither transition can fire on it).
                    let rec walk (col: int) (start: Slope) (prev: bool voption) (acc: Set<Cell>) : Set<Cell> =
                        if col > maxCol then
                            // The row ran to its end in the open: the next row inherits the whole window.
                            match prev with
                            | ValueSome false -> scanRow quadrant (depth + 1) start end_ acc
                            | _ -> acc
                        else
                            let cell = transform origin quadrant depth col
                            let isWall = not (isTransparent cell)

                            // Reveal BEFORE `start` is widened by this tile: the symmetry test must read
                            // the window this tile was actually scanned under.
                            let acc =
                                if (isWall || isSymmetric depth start end_ col) && withinRadius cell then
                                    Set.add cell acc
                                else
                                    acc

                            // wall -> floor: the shadow ends; reopen the window at this tile's near edge.
                            let start =
                                match prev with
                                | ValueSome true when not isWall -> slopeOf depth col
                                | _ -> start

                            // floor -> wall: a new shadow begins; the cells beyond it live in the next row
                            // clipped to this tile's near edge. (Mutually exclusive with the case above.)
                            let acc =
                                match prev with
                                | ValueSome false when isWall -> scanRow quadrant (depth + 1) start (slopeOf depth col) acc
                                | _ -> acc

                            walk (col + 1) start (ValueSome isWall) acc

                    walk (minColOf depth start) start ValueNone acc

            // The origin is visible unconditionally — you occupy the cell you stand in, opaque or not.
            [ 0..3 ]
            |> List.fold
                (fun acc quadrant -> scanRow quadrant 1 { Num = -1; Den = 1 } { Num = 1; Den = 1 } acc)
                (Set.singleton origin)
