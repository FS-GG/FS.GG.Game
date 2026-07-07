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

    // Narrow-phase circle–circle contact. Squared distance keeps the boolean test sqrt-free; the
    // single sqrt (correctly-rounded IEEE, cross-platform deterministic) builds the manifold only on
    // a hit. Coincident centres (d = 0, DEC-002) default the normal to (1, 0). A NaN or non-positive
    // radius fails the strict `<` (NaN comparisons are false), yielding None without throwing.
    let circleContact (a: Circle) (b: Circle) : Contact option =
        let dx = b.Center.X - a.Center.X
        let dy = b.Center.Y - a.Center.Y
        let r = a.Radius + b.Radius
        let d2 = dx * dx + dy * dy
        if a.Radius > 0.0 && b.Radius > 0.0 && d2 < r * r then
            let d = sqrt d2
            if d > 0.0 then Some { Normal = { X = dx / d; Y = dy / d }; Depth = r - d }
            else Some { Normal = { X = 1.0; Y = 0.0 }; Depth = r }
        else
            None

    // Narrow-phase circle–AABB contact. Clamp the centre to the box; squared distance from the centre
    // to that clamp vs radius² is the sqrt-free overlap test (a zero distance means the centre is
    // inside). Centre-outside: the normal points from the circle toward the box (so −Normal×Depth
    // separates, as in aabbContact). Centre-inside: fall back to the least-penetration face, tie-
    // breaking equal penetration toward X with a +bias (DEC-003, identical to aabbContact). A NaN or
    // non-positive radius yields None: the radius guard catches non-positive/NaN radius, and a NaN
    // centre makes d2 NaN so the strict `<` fails.
    let circleAabbContact (c: Circle) (box: Rect) : Contact option =
        if not (c.Radius > 0.0) then
            None
        else
            let minX, minY = box.X, box.Y
            let maxX, maxY = box.X + box.Width, box.Y + box.Height
            let cx, cy = c.Center.X, c.Center.Y
            let clampedX = max minX (min maxX cx)
            let clampedY = max minY (min maxY cy)
            let dx = cx - clampedX
            let dy = cy - clampedY
            let d2 = dx * dx + dy * dy
            if d2 < c.Radius * c.Radius then
                if d2 > 0.0 then
                    // Centre outside the box: normal points circle → box (opposite the clamp offset).
                    let d = sqrt d2
                    Some { Normal = { X = -dx / d; Y = -dy / d }; Depth = c.Radius - d }
                else
                    // Centre inside the box: least-penetration face. `esc` is the outward escape sign
                    // per axis (strict `<` gives the +bias on a tie); the normal is its opposite.
                    let pl, pr = cx - minX, maxX - cx
                    let pb, pt = cy - minY, maxY - cy
                    let penX, escX = if pl < pr then pl, -1.0 else pr, 1.0
                    let penY, escY = if pb < pt then pb, -1.0 else pt, 1.0
                    if penX <= penY then Some { Normal = { X = -escX; Y = 0.0 }; Depth = penX + c.Radius }
                    else Some { Normal = { X = 0.0; Y = -escY }; Depth = penY + c.Radius }
            else
                None

    // Segment-vs-AABB cast (slab method). Returns the first entry crossing INTO the box from outside
    // with t in [0,1] (DEC-002: an origin inside, or the box behind the segment, gives None). The
    // entered face is the axis whose near-t was the max; its normal is the outward unit axis (opposite
    // the segment direction on that axis). A corner entry (equal per-axis near-t) resolves to the X
    // face (DEC-003). NaN or zero-length segment: the comparisons fail, yielding None.
    let segmentAabbHit (p0: Point) (p1: Point) (box: Rect) : RayHit option =
        let dx, dy = p1.X - p0.X, p1.Y - p0.Y
        let minX, maxX = box.X, box.X + box.Width
        let minY, maxY = box.Y, box.Y + box.Height
        // Per-axis slab -> (tNear, tFar) option; None when parallel and outside the slab.
        let slab p d lo hi : (float * float) option =
            if d = 0.0 then
                if p < lo || p > hi then None
                else Some(System.Double.NegativeInfinity, System.Double.PositiveInfinity)
            else
                let t1 = (lo - p) / d
                let t2 = (hi - p) / d
                Some(min t1 t2, max t1 t2)
        match slab p0.X dx minX maxX, slab p0.Y dy minY maxY with
        | Some(nx, fx), Some(ny, fy) ->
            let tEnter = max nx ny
            let tExit = min fx fy
            if tEnter <= tExit && tEnter >= 0.0 && tEnter <= 1.0 then
                // The entering axis is the one with the larger near-t; a tie resolves to X (DEC-003).
                let normal =
                    if nx >= ny then { X = (if dx > 0.0 then -1.0 else 1.0); Y = 0.0 }
                    else { X = 0.0; Y = (if dy > 0.0 then -1.0 else 1.0) }
                Some { T = tEnter; Point = { X = p0.X + dx * tEnter; Y = p0.Y + dy * tEnter }; Normal = normal }
            else
                None
        | _ -> None

    // Segment-vs-circle cast (ray–circle quadratic on d = p1 - p0, f = p0 - center). The near root
    // t = (-b - sqrt disc) / 2a is the entry; returned only when t in [0,1] (DEC-002: origin inside
    // gives a negative near root => None). Guards a > 0 (non-degenerate segment), radius > 0, and
    // disc >= 0; any NaN fails these, yielding None. The single sqrt is a correctly-rounded IEEE op.
    let segmentCircleHit (p0: Point) (p1: Point) (c: Circle) : RayHit option =
        let dx, dy = p1.X - p0.X, p1.Y - p0.Y
        let fx, fy = p0.X - c.Center.X, p0.Y - c.Center.Y
        let a = dx * dx + dy * dy
        let b = 2.0 * (fx * dx + fy * dy)
        let cc = fx * fx + fy * fy - c.Radius * c.Radius
        let disc = b * b - 4.0 * a * cc
        if a > 0.0 && c.Radius > 0.0 && disc >= 0.0 then
            let t = (-b - sqrt disc) / (2.0 * a)
            if t >= 0.0 && t <= 1.0 then
                let px, py = p0.X + dx * t, p0.Y + dy * t
                Some { T = t; Point = { X = px; Y = py }; Normal = { X = (px - c.Center.X) / c.Radius; Y = (py - c.Center.Y) / c.Radius } }
            else
                None
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
