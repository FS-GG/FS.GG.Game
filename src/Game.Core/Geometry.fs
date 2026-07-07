namespace FS.GG.Game.Core

[<RequireQualifiedAccess>]
module Geometry =

    // Strict edges: edge- and corner-touching rectangles have zero-area overlap ⇒ not intersecting.
    // NaN-safe: any NaN operand makes a comparison false, so the whole conjunction is false (no throw).
    let intersects (a: Rect) (b: Rect) : bool =
        a.X < b.X + b.Width
        && a.X + a.Width > b.X
        && a.Y < b.Y + b.Height
        && a.Y + a.Height > b.Y

    // Inclusive edges: `inner` flush against `outer`'s edge is contained.
    let contains (outer: Rect) (inner: Rect) : bool =
        inner.X >= outer.X
        && inner.Y >= outer.Y
        && inner.X + inner.Width <= outer.X + outer.Width
        && inner.Y + inner.Height <= outer.Y + outer.Height

    // Inclusive edges: a point on the low or high edge is contained.
    let containsPoint (rect: Rect) (point: Point) : bool =
        point.X >= rect.X
        && point.X <= rect.X + rect.Width
        && point.Y >= rect.Y
        && point.Y <= rect.Y + rect.Height

    let center (rect: Rect) : Point =
        { X = rect.X + rect.Width / 2.0
          Y = rect.Y + rect.Height / 2.0 }

    let ofCenter (center: Point) (width: float) (height: float) : Rect =
        { X = center.X - width / 2.0
          Y = center.Y - height / 2.0
          Width = width
          Height = height }

    // Narrow-phase AABB contact (SAT specialised to two axes). Centre/half-extent form yields the
    // minimum-translation vector directly: overlap depths px,py = (haX+hbX)-|dx|, (haY+hbY)-|dy| over
    // the centre delta a→b. A non-positive depth on either axis means no positive-area overlap, so
    // `isSome` agrees with `intersects` (strict edges). The smaller positive depth is the MTV. Tie-
    // breaks are fixed for byte-determinism (the corpus's ordering-nondeterminism enemy): px = py ⇒
    // X axis; a zero centre-delta on the chosen axis ⇒ +direction. NaN-safe: a NaN depth fails the
    // `> 0.0` guards, giving None without throwing.
    let aabbContact (a: Rect) (b: Rect) : Contact option =
        let haX, haY = a.Width / 2.0, a.Height / 2.0
        let hbX, hbY = b.Width / 2.0, b.Height / 2.0
        let dx = (b.X + hbX) - (a.X + haX)
        let dy = (b.Y + hbY) - (a.Y + haY)
        let px = (haX + hbX) - abs dx
        let py = (haY + hbY) - abs dy
        if px > 0.0 && py > 0.0 then
            let sign v = if v < 0.0 then -1.0 else 1.0 // +bias on exact zero — the documented tie-break
            if px <= py then Some { Normal = { X = sign dx; Y = 0.0 }; Depth = px }
            else Some { Normal = { X = 0.0; Y = sign dy }; Depth = py }
        else
            None

    // Swept AABB via Minkowski expansion: grow `target` by `moving`'s extents so `moving` collapses to
    // its min-corner point, then clip the motion segment (point → point+velocity) against the expanded
    // box with the Liang–Barsky slab method. A start/end that overlaps `target` puts the corresponding
    // segment endpoint inside the expanded box, so this is a superset of the static `intersects` at both
    // endpoints while also catching fast projectiles that tunnel through a thin target in one step.
    let sweptIntersects (moving: Rect) (velocity: Point) (target: Rect) : bool =
        let minX = target.X - moving.Width
        let maxX = target.X + target.Width
        let minY = target.Y - moving.Height
        let maxY = target.Y + target.Height
        let px, py = moving.X, moving.Y
        let dx, dy = velocity.X, velocity.Y

        // Clip parameter t ∈ [0,1] against one axis' slab; returns the narrowed (tEnter, tExit) or None.
        let clip p d lo hi (tEnter: float) (tExit: float) : (float * float) option =
            if d = 0.0 then
                if p < lo || p > hi then None else Some(tEnter, tExit)
            else
                let t1 = (lo - p) / d
                let t2 = (hi - p) / d
                let tNear = min t1 t2
                let tFar = max t1 t2
                let e = max tEnter tNear
                let x = min tExit tFar
                if e > x then None else Some(e, x)

        match clip px dx minX maxX 0.0 1.0 with
        | None -> false
        | Some(e, x) ->
            match clip py dy minY maxY e x with
            | None -> false
            | Some(e2, x2) -> e2 <= x2
