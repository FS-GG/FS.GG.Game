namespace FS.GG.Game.Core

type LineMode =
    | Thin
    | Supercover

[<RequireQualifiedAccess>]
module Los =

    let line (a: Cell) (b: Cell) : Cell list =
        // Deltas and the error term are `int64` so the arithmetic stays total across the WHOLE `int`
        // coordinate domain: an `int` subtraction could wrap, `abs Int32.MinValue` throws, and `2 * err`
        // overflows to negative once the dominant delta reaches ~2^30 (which would stall the axis and
        // loop forever). Promoting to `int64` removes all three — still pure integer arithmetic, still
        // byte-identical, and the walk always terminates. (x/y stay `int`: they only ever step by ±1.)
        let dx = abs (int64 b.Col - int64 a.Col)
        let dy = abs (int64 b.Row - int64 a.Row)
        let sx = if a.Col < b.Col then 1 else -1
        let sy = if a.Row < b.Row then 1 else -1
        let acc = ResizeArray<Cell>()
        let mutable x = a.Col
        let mutable y = a.Row
        // err is the Bresenham decision variable (dx - dy). All arithmetic is integer, so there is no
        // rounding-mode drift — the same endpoints always produce the same steps.
        let mutable err = dx - dy
        let mutable go = true

        while go do
            acc.Add { Col = x; Row = y }

            if x = b.Col && y = b.Row then
                go <- false
            else
                let e2 = 2L * err

                if e2 > -dy then
                    err <- err - dy
                    x <- x + sx

                if e2 < dx then
                    err <- err + dx
                    y <- y + sy

        List.ofSeq acc

    let supercover (a: Cell) (b: Cell) : Cell list =
        // Deltas and the tiebreak cross-product are `int64` for the same full-`int`-domain totality as
        // `line` (wrap-free subtraction, `abs` that can't throw). Still pure integer arithmetic; x/y
        // only step by ±1.
        let dx = int64 b.Col - int64 a.Col
        let dy = int64 b.Row - int64 a.Row
        let nx = abs dx
        let ny = abs dy
        let sx = if dx > 0L then 1 else -1
        let sy = if dy > 0L then 1 else -1
        let acc = ResizeArray<Cell>()
        let mutable x = a.Col
        let mutable y = a.Row
        acc.Add { Col = x; Row = y }
        // ix / iy count the orthogonal steps already taken toward b. At each step choose the axis whose
        // next half-cell boundary the line crosses first: compare (0.5 + ix)/nx with (0.5 + iy)/ny,
        // cross-multiplied to stay in integers ⇒ (1 + 2*ix)*ny vs (1 + 2*iy)*nx. An exact tie (the line
        // passes through a lattice corner) steps in x first — a deterministic choice that keeps the walk
        // 4-connected rather than cutting the corner diagonally.
        let mutable ix = 0L
        let mutable iy = 0L

        while ix < nx || iy < ny do
            let cmp = (1L + 2L * ix) * ny - (1L + 2L * iy) * nx

            if iy >= ny || (ix < nx && cmp <= 0L) then
                x <- x + sx
                ix <- ix + 1L
            else
                y <- y + sy
                iy <- iy + 1L

            acc.Add { Col = x; Row = y }

        List.ofSeq acc

    let trace (mode: LineMode) (a: Cell) (b: Cell) : Cell list =
        match mode with
        | Thin -> line a b
        | Supercover -> supercover a b

    let lineOfSightBy (mode: LineMode) (isTransparent: Cell -> bool) (a: Cell) (b: Cell) : bool =
        // Trace the canonical ordered pair, never the caller's argument order: `Thin`'s error-tie break
        // is resolved in a fixed direction, so `trace m a b` and `trace m b a` can visit different
        // intermediate cells, and a wall in a sometimes-visited cell would block one direction only.
        // `Cell` is a struct record over `(Col, Row)`, so its structural comparison IS the total cell
        // order the design calls for. Endpoints are excluded from the test either way, so canonicalizing
        // changes only which tiles between them are consulted — identically for both argument orders.
        let lo, hi = if a <= b then a, b else b, a

        trace mode lo hi
        |> List.forall (fun c -> c = lo || c = hi || isTransparent c)

    let lineOfSight (isTransparent: Cell -> bool) (a: Cell) (b: Cell) : bool =
        lineOfSightBy Supercover isTransparent a b
