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

    // Oriented bounding box as a convex polygon: the four box corners rotated CCW about `center`.
    // Local corners in CCW order (-hx,-hy),(hx,-hy),(hx,hy),(-hx,hy); the rotation R(θ) = [[c,-s],[s,c]]
    // is applied to each then translated by `center`. At θ = 0 (cos 0 = 1, sin 0 = 0 exactly) these are
    // the axis-aligned box corners, so two obbPolygons agree with aabbContact (DEC-004). CCW winding
    // satisfies polygonContact's outward-normal convention by construction.
    let obbPolygon (center: Point) (halfExtents: Point) (rotation: float) : ConvexPolygon =
        let c = cos rotation
        let s = sin rotation
        let hx, hy = halfExtents.X, halfExtents.Y
        let corner lx ly : Point =
            { X = center.X + lx * c - ly * s
              Y = center.Y + lx * s + ly * c }
        { Vertices = [| corner -hx -hy; corner hx -hy; corner hx hy; corner -hx hy |] }

    // ---- shared convex-polygon plumbing (implementation-only; the .fsi hides it) ----

    let private isFiniteF (x: float) = not (System.Double.IsNaN x) && not (System.Double.IsInfinity x)

    // Shoelace area magnitude; zero for a collinear/degenerate ring.
    let private ringArea (v: Point[]) =
        let mutable acc = 0.0
        for i in 0 .. v.Length - 1 do
            let p = v.[i]
            let q = v.[(i + 1) % v.Length]
            acc <- acc + (p.X * q.Y - q.X * p.Y)
        abs acc / 2.0

    // The totality guard every convex-polygon function shares: `< 3` vertices, any non-finite
    // coordinate, or a zero area is a no-contact input. Non-finiteness is explicit rather than left to
    // the projection loop's comparison NaN-poisoning, which would silently skip a NaN vertex. A NaN
    // area also fails `> 0.0`, so the whole predicate is NaN-safe.
    let private wellFormed (v: Point[]) =
        v.Length >= 3
        && v |> Array.forall (fun p -> isFiniteF p.X && isFiniteF p.Y)
        && ringArea v > 0.0

    // Unit outward normal of edge i (v[i] → v[i+1]) on a CCW ring; ValueNone for a zero-length edge.
    let private edgeNormalAt (v: Point[]) (i: int) : Point voption =
        let p = v.[i]
        let q = v.[(i + 1) % v.Length]
        let ex, ey = q.X - p.X, q.Y - p.Y
        let len = sqrt (ex * ex + ey * ey)
        if len > 0.0 then ValueSome { X = ey / len; Y = -ex / len } else ValueNone

    // The Separating Axis Theorem scan shared by `polygonContact` and `polygonManifold` — the sole
    // producer of (depth, a→b normal), so the two can never disagree on them. Candidate axes are the
    // outward edge normals of both polygons — a's in vertex order, then b's — with zero-length edges
    // skipped and antiparallel/duplicate directions deduped to a canonical half-plane (FR-005),
    // preserving generation order. On each axis both polygons project to intervals [aMn,aMx],[bMn,bMx].
    // The signed exit distances are d1 = aMx - bMn (push a toward -n) and d2 = bMx - aMn (push a toward
    // +n); the pair overlaps on this axis iff both are > 0. Any axis with `not (d1 > 0 && d2 > 0)` (a
    // gap, a touch, or a NaN interval) separates ⇒ None (strict edges, matching aabbContact). The axis
    // penetration is min(d1, d2) — this carries the full-containment correction: when one interval
    // nests inside the other the naive min(aMx,bMx)-max(aMn,bMn) would understate the depth, but
    // min(d1,d2) is the true distance to push the nested shape out either end. The least penetration
    // over all axes is the MTV; its normal is oriented along the nearer exit so `-normal*depth`
    // separates (the a→b least-penetration direction). An equal minimum on two axes keeps the first in
    // generation order (DEC-003), for byte-determinism. Callers must have checked `wellFormed` first.
    let private satScan (va: Point[]) (vb: Point[]) : (float * Point) option =
        let edgeNormals (v: Point[]) : Point list =
            [ for i in 0 .. v.Length - 1 do
                  match edgeNormalAt v i with
                  | ValueSome n -> yield n
                  | ValueNone -> () ]
        // Fold antiparallel/duplicate axes onto one representative, keeping first-seen (generation
        // order) so the tie-break is stable. The stored orientation is irrelevant to the overlap
        // magnitude (projection is symmetric under axis negation); a→b orientation is set below.
        let axes =
            (edgeNormals va @ edgeNormals vb)
            |> List.fold (fun (acc: Point list) (n: Point) ->
                let dup =
                    acc
                    |> List.exists (fun (m: Point) ->
                        (abs (m.X - n.X) < 1e-9 && abs (m.Y - n.Y) < 1e-9)
                        || (abs (m.X + n.X) < 1e-9 && abs (m.Y + n.Y) < 1e-9))
                if dup then acc else acc @ [ n ])
                []
        let project (v: Point[]) (n: Point) =
            let mutable mn = System.Double.PositiveInfinity
            let mutable mx = System.Double.NegativeInfinity
            for pt in v do
                let d = pt.X * n.X + pt.Y * n.Y
                if d < mn then mn <- d
                if d > mx then mx <- d
            mn, mx
        // Scan for the least penetration; a non-overlapping axis short-circuits to separated. Each
        // axis contributes (pen, normal) where `normal` is oriented so -normal*pen separates (the
        // nearer exit). `>= best` keeps the FIRST axis on an exact tie (generation order, DEC-003).
        let mutable best: (float * Point) option = None
        let mutable separated = false
        for n in axes do
            if not separated then
                let aMn, aMx = project va n
                let bMn, bMx = project vb n
                let d1 = aMx - bMn // push a toward -n to exit
                let d2 = bMx - aMn // push a toward +n to exit
                if not (d1 > 0.0 && d2 > 0.0) then
                    separated <- true
                else
                    let pen, normal =
                        if d1 <= d2 then d1, n
                        else d2, { X = -n.X; Y = -n.Y }
                    match best with
                    | Some(bp, _) when pen >= bp -> ()
                    | _ -> best <- Some(pen, normal)
        if separated then None else best

    // Narrow-phase convex-polygon contact via the Separating Axis Theorem — the MTV, as a `Contact`.
    let polygonContact (a: ConvexPolygon) (b: ConvexPolygon) : Contact option =
        if not (wellFormed a.Vertices && wellFormed b.Vertices) then
            None
        else
            satScan a.Vertices b.Vertices
            |> Option.map (fun (depth, normal) -> { Normal = normal; Depth = depth })

    // Narrow-phase convex-polygon contact POINTS — reference-face selection plus Sutherland–Hodgman
    // clipping of the incident face, the chunk of narrow phase SAT alone does not give you. `Normal`
    // and `Depth` come from the very same `satScan` `polygonContact` calls, so the two agree bit for
    // bit and `isSome` agrees for all inputs; this function only adds the points, the pair ids, and
    // the feature id.
    //
    // Reference face, by a DIRECTED face query. SAT hands back the least-penetration axis `n` (a→b)
    // but not which polygon owns the face on it, and it cannot: the axis may be the NEGATION of a face
    // normal rather than a face normal (the vertex-contact case, where one polygon's support along the
    // axis is a lone vertex, not a face). Picking the face most parallel to `n` therefore picks a face
    // whose plane is not the contact plane, and clipping against it produces points nowhere near the
    // contact. So ask the question the contact plane actually answers: for each face of `a`, how far
    // does the nearest vertex of `b` sit above its plane, and vice versa. The face with the GREATEST
    // separation — the least negative, since an overlapping pair puts every vertex below some plane —
    // is the reference face, and its penetration is exactly `Depth`. Both queries are over outward
    // face normals only, so the reference face always exists and its plane is the contact plane.
    //
    // An exact tie between the two polygons (parallel faces, e.g. two axis-aligned boxes) resolves to
    // A; within one polygon an exact tie resolves to the FIRST face in vertex order. Both are the same
    // deterministic first-wins rule `polygonContact` uses for its axis tie (DEC-003).
    //
    // Incident face. The face of the OTHER polygon most anti-parallel to the reference face's normal
    // (minimum dot product) — the face pointing back at it. First in vertex order on a tie. On a convex
    // ring the edge normals sweep monotonically, so this face is always one of the two adjacent to the
    // incident polygon's deepest vertex: the contact is on it.
    //
    // Clipping. The incident face's segment is clipped to the reference face's two SIDE planes (the
    // half-planes through the reference face's endpoints with normals along ±the face direction),
    // which is Sutherland–Hodgman on a two-vertex subject polygon. The survivors at or below the
    // reference face plane are the contact points: 2 for a face-on-face contact, 1 when a vertex pokes
    // in. Each lies exactly on the boundary of the INCIDENT polygon (it is a point of its face) and at
    // signed distance in `[-Depth, 0]` below the reference face — the lower bound because the deepest
    // incident vertex sits exactly `Depth` below it, by the face query above — so each lies within
    // `Depth` of the reference polygon's boundary too. Coincident survivors (a clip that grazes an
    // endpoint) collapse to one point, so `PointCount` counts distinct points.
    //
    // The deepest incident vertex is an endpoint of the incident face, and for a CONVEX reference
    // polygon it always lies between that face's side planes — so it always survives the clip, always
    // has `sep = -Depth <= 0`, and `PointCount >= 1` for every `ValueSome`, as the agreement with
    // `polygonContact` requires. (Checked over 226k random strictly-convex pairs, 3–7 vertices: it
    // never once fell outside.) The two branches that handle its absence are therefore dead for valid
    // input. They exist because `ConvexPolygon`'s convexity is an INPUT ASSUMPTION, not an enforced
    // invariant, and a concave ring must still yield a bounded point rather than a vertex from the far
    // side of the polygon: fall back to the clip survivor nearest the reference face, and — if the
    // face missed the slab entirely — to its own nearer endpoint.
    //
    // FeatureId packs (whether the reference face is b's, reference edge index, incident edge index)
    // into one int. It is stable across ticks for an unmoving pair because every input to it is —
    // vertex order and the two argmax scans are pure functions of the geometry. Opaque by contract:
    // compare it, do not decode it.
    //
    // Totality: `wellFormed` rejects `< 3` vertices, a non-finite coordinate, and a zero-area ring,
    // exactly as `polygonContact` does. Nothing below can throw or divide by zero — `wellFormed`
    // guarantees a positive-length edge exists, and the clip's `d0 - d1` divisor is non-zero on every
    // branch that reaches it (`d0 <= 0 < d1` or `d1 <= 0 < d0`).
    let polygonManifold (a: ConvexPolygon) (b: ConvexPolygon) : Manifold voption =
        let va, vb = a.Vertices, b.Vertices
        if not (wellFormed va && wellFormed vb) then
            ValueNone
        else
            match satScan va vb with
            | None -> ValueNone
            | Some(depth, normal) ->
                // The face of `pv` whose plane `qv` penetrates least — argmax over faces of the
                // minimum signed distance from that face's plane to `qv`'s vertices. First wins on a
                // tie. A positive ring area guarantees a non-degenerate edge, so the index is real.
                let faceQuery (pv: Point[]) (qv: Point[]) =
                    let mutable bi = 0
                    let mutable bs = System.Double.NegativeInfinity
                    for i in 0 .. pv.Length - 1 do
                        match edgeNormalAt pv i with
                        | ValueSome n ->
                            let p0 = pv.[i]
                            let mutable s = System.Double.PositiveInfinity
                            for v in qv do
                                let d = (v.X - p0.X) * n.X + (v.Y - p0.Y) * n.Y
                                if d < s then s <- d
                            if s > bs then
                                bs <- s
                                bi <- i
                        | ValueNone -> ()
                    bi, bs

                let aFace, aSep = faceQuery va vb
                let bFace, bSep = faceQuery vb va
                let flip = bSep > aSep // an exact tie keeps A as the reference
                let refV, refEdge, incV = if flip then vb, bFace, va else va, aFace, vb

                let r0 = refV.[refEdge]
                let r1 = refV.[(refEdge + 1) % refV.Length]
                let rn = (edgeNormalAt refV refEdge).Value
                let ex, ey = r1.X - r0.X, r1.Y - r0.Y
                let elen = sqrt (ex * ex + ey * ey)
                let dirX, dirY = ex / elen, ey / elen

                // The incident face: most anti-parallel to the reference normal; first wins on a tie.
                let mutable incEdge = 0
                let mutable incAlign = System.Double.PositiveInfinity
                for i in 0 .. incV.Length - 1 do
                    match edgeNormalAt incV i with
                    | ValueSome n ->
                        let d = n.X * rn.X + n.Y * rn.Y
                        if d < incAlign then
                            incAlign <- d
                            incEdge <- i
                    | ValueNone -> ()

                // Clip a 2-point segment to the half-plane { x | (x − p)·n ≤ 0 }, preserving order.
                // A segment clipped to a half-plane yields 2 points or none, so the shape is stable.
                let clip (px, py) (nx, ny) (seg: (Point * Point) voption) =
                    match seg with
                    | ValueNone -> ValueNone
                    | ValueSome(q0, q1) ->
                        let d0 = (q0.X - px) * nx + (q0.Y - py) * ny
                        let d1 = (q1.X - px) * nx + (q1.Y - py) * ny
                        let cross () =
                            let t = d0 / (d0 - d1)
                            { X = q0.X + (q1.X - q0.X) * t; Y = q0.Y + (q1.Y - q0.Y) * t }
                        if d0 <= 0.0 && d1 <= 0.0 then ValueSome(q0, q1)
                        elif d0 <= 0.0 then ValueSome(q0, cross ())
                        elif d1 <= 0.0 then ValueSome(cross (), q1)
                        else ValueNone

                let incFace = incV.[incEdge], incV.[(incEdge + 1) % incV.Length]

                // The candidate contacts: the incident face clipped to the reference face's lateral
                // slab, or — when it lies wholly outside the slab — the raw face, whose nearer endpoint
                // is then the contact. Either way both candidates lie on the incident boundary.
                let c0, c1 =
                    match clip (r0.X, r0.Y) (-dirX, -dirY) (ValueSome incFace) |> clip (r1.X, r1.Y) (dirX, dirY) with
                    | ValueSome pair -> pair
                    | ValueNone -> incFace

                // Signed distance above the reference face plane; ≤ 0 means penetrating.
                let sep (q: Point) = (q.X - r0.X) * rn.X + (q.Y - r0.Y) * rn.Y
                // Two survivors within this of each other are one contact, not two.
                let coincident (u: Point) (w: Point) = abs (u.X - w.X) <= 1e-12 && abs (u.Y - w.Y) <= 1e-12

                let points =
                    if sep c0 <= 0.0 && sep c1 <= 0.0 && not (coincident c0 c1) then [| c0; c1 |]
                    elif sep c0 <= 0.0 then [| c0 |]
                    elif sep c1 <= 0.0 then [| c1 |]
                    // Neither is below the plane: the contact wraps a corner of the reference face, so
                    // the nearer candidate is it. `<=` keeps the first, as every tie-break here does.
                    elif sep c0 <= sep c1 then [| c0 |]
                    else [| c1 |]

                // Opaque packing: flip in bit 30, reference edge in bits 15..29, incident edge in 0..14.
                let featureId =
                    ((if flip then 1 else 0) <<< 30)
                    ||| ((refEdge &&& 0x7FFF) <<< 15)
                    ||| (incEdge &&& 0x7FFF)

                ValueSome
                    { A = 0
                      B = 1
                      Normal = normal
                      Depth = depth
                      Points = points
                      PointCount = points.Length
                      FeatureId = featureId }

    // Segment-vs-convex-polygon cast: the slab method generalised from two axis slabs to one half-plane
    // per edge. For a CCW ring the edge v[i]→v[i+1] has outward unit normal (ey, -ex)/|e| — the same
    // normal polygonContact projects on — and a point x is inside iff (x - v[i])·n <= 0. Along the
    // segment, f_i(t) = dist_i + t·denom_i with dist_i = (p0 - v[i])·n and denom_i = (p1 - p0)·n: a
    // negative denom crosses INTO the half-plane (an entry at t = -dist/denom; keep the max), a positive
    // one leaves it (keep the min), and a zero one is parallel — separating outright when p0 lies outside
    // (dist > 0). The greatest entry parameter is the crossing into the polygon and the edge that
    // produced it is the ENTERED edge, whose outward normal is the armour zone the caller reads.
    //
    // Strict edges, as in aabbContact/polygonContact — where a zero-AREA overlap is not a contact, here a
    // zero-LENGTH chord is not a hit: tEnter < tExit is required. The chord degenerates to a point only
    // where two edge lines meet, so what this rejects is precisely a segment clipping a vertex. It does
    // NOT reject a segment collinear with an edge: that edge is parallel (it constrains nothing) and the
    // chord along it has positive length, so the cast reports the edge it crossed to reach the boundary —
    // which is what segmentAabbHit does too, and the two agree there. The clipped vertex is the only place
    // the RULE is stricter than segmentAabbHit's, whose `<=` slab test calls that graze a hit. (Separately,
    // a segment endpoint lying exactly on the boundary can flip either way between the two, because a Rect
    // edge `X + Width` and the corresponding obbPolygon corner `centre + halfExtent` are not the same
    // double: 4.2 + 1.6 <> 5.0 + 0.8. That is the two shapes disagreeing, not the two rules.)
    //
    // An origin inside gives every entry a negative parameter (tEnter < 0 ⇒ None, DEC-002), as does a
    // polygon behind the segment. A corner ENTRY — several edges sharing the maximal entry parameter —
    // keeps the FIRST in vertex order, as polygonContact keeps the first axis in generation order
    // (DEC-003); segmentAabbHit's corner tie-break is the X face, so the two agree on T and Point there
    // but not on Normal, exactly as their contact counterparts agree on isSome but not on the tie normal.
    // Zero-length edges contribute no half-plane and are skipped.
    //
    // Totality: fewer than 3 vertices, a zero-area ring, or any non-finite coordinate on the segment or
    // the ring yields None (an explicit guard, as in polygonContact, rather than relying on the
    // comparisons' NaN-poisoning). A zero-length segment yields None wherever it sits.
    let segmentPolygonHit (p0: Point) (p1: Point) (poly: ConvexPolygon) : RayHit option =
        let v = poly.Vertices
        // Entry parameters within this of the maximum are one corner entry, not a later edge. `t` is the
        // dimensionless segment parameter, so the bound is scale-free; 1e-9 matches the tolerance
        // polygonContact folds duplicate axes with.
        let cornerTie = 1e-9
        if not (wellFormed v) then None
        elif not (isFiniteF p0.X && isFiniteF p0.Y && isFiniteF p1.X && isFiniteF p1.Y) then None
        else
            let dx, dy = p1.X - p0.X, p1.Y - p0.Y
            let mutable tEnter = System.Double.NegativeInfinity
            let mutable tExit = System.Double.PositiveInfinity
            let mutable normal = { X = 0.0; Y = 0.0 }
            let mutable separated = false
            for i in 0 .. v.Length - 1 do
                if not separated then
                    let a = v.[i]
                    let b = v.[(i + 1) % v.Length]
                    let ex, ey = b.X - a.X, b.Y - a.Y
                    let len = sqrt (ex * ex + ey * ey)
                    if len > 0.0 then
                        let n: Point = { X = ey / len; Y = -ex / len }
                        let dist = (p0.X - a.X) * n.X + (p0.Y - a.Y) * n.Y
                        let denom = dx * n.X + dy * n.Y
                        if denom = 0.0 then
                            // Parallel to this edge: outside its half-plane ⇒ no crossing exists at all.
                            if dist > 0.0 then separated <- true
                        else
                            let t = -dist / denom
                            if denom < 0.0 then
                                // Entering. `tEnter` always takes the true max, so the reported point is
                                // never short of the boundary. The struck face, though, is the FIRST edge
                                // in vertex order among those entered at that parameter — and at a corner
                                // entry the two adjacent edges' parameters, equal in exact arithmetic, are
                                // each computed through their own normalisation and so differ by an ULP or
                                // two. A bare `>` would let that rounding noise pick the armour zone (it
                                // does, for ~10% of corner entries on a rotated ring), so the normal moves
                                // only on an increase that clears `cornerTie`. On an axis-aligned ring the
                                // normals are exactly 0/±1, the parameters tie exactly, and this is a no-op.
                                if t > tEnter then
                                    if t - tEnter > cornerTie then normal <- n
                                    tEnter <- t
                            elif t < tExit then
                                tExit <- t
            if separated || not (tEnter < tExit) || tEnter < 0.0 || tEnter > 1.0 then
                None
            else
                Some { T = tEnter; Point = { X = p0.X + dx * tEnter; Y = p0.Y + dy * tEnter }; Normal = normal }

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
