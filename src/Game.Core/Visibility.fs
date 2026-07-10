namespace FS.GG.Game.Core

[<RequireQualifiedAccess>]
module Visibility =

    type Segment = { A: Point; B: Point }

    type Settings = { Radius: float }

    type VisibilityPolygon = { Source: Point; Vertices: Point list }

    // A tiny angular nudge (a linearised rotation) used to shoot rays just past each corner so the sweep
    // captures the walls that begin/end there. Pure arithmetic — no transcendental, so it stays
    // deterministic. Small enough not to skip a real corner, large enough to clear float noise.
    let private nudge = 1e-5

    let private isFinitePoint (p: Point) =
        System.Double.IsFinite p.X && System.Double.IsFinite p.Y

    let private isFiniteSeg (s: Segment) = isFinitePoint s.A && isFinitePoint s.B

    let private sqLen (v: Point) = v.X * v.X + v.Y * v.Y

    // The portion of the segment `a`→`b` lying inside `rect`, or `None` when it misses entirely. A
    // Liang–Barsky slab clip of the parametric segment against the four half-planes — sqrt-free and
    // transcendental-free, so it is exact and deterministic. Edge contact counts as a touch (inclusive,
    // matching `Geometry.containsPoint`), which can clip to a zero-length graze; the caller drops those.
    //
    // This both culls and trims, and it must never report a false negative: a dropped occluder is a wall
    // the viewpoint sees straight through. Trimming matters as much as culling — the sweep aims its rays
    // at occluder endpoints, so an occluder has to END where it leaves the sight bound. A wall left at
    // full length whose endpoints lie far outside the bound contributes no aim point where it crosses
    // the bound, and the ring then cuts the corner between the wall and the bound edge.
    let private clipSegmentToRect (rect: Rect) (a: Point) (b: Point) : (Point * Point) option =
        let dx = b.X - a.X
        let dy = b.Y - a.Y

        // Narrow the surviving parameter window `[t0, t1]` by one half-plane `p * t <= q`. A `p` of zero
        // means the segment runs parallel to that slab, so it survives only where it already lies inside
        // it (`q >= 0`); an empty window `(1, 0)` is absorbing, so a rejected segment stays rejected.
        // Struct tuples, so clipping a wall list allocates nothing.
        let inline clip (p: float) (q: float) (struct (t0, t1)) =
            if p = 0.0 then
                if q < 0.0 then struct (1.0, 0.0) else struct (t0, t1)
            else
                let r = q / p
                if p < 0.0 then struct (max t0 r, t1) else struct (t0, min t1 r)

        let window =
            struct (0.0, 1.0)
            |> clip (-dx) (a.X - rect.X)
            |> clip dx (rect.X + rect.Width - a.X)
            |> clip (-dy) (a.Y - rect.Y)
            |> clip dy (rect.Y + rect.Height - a.Y)

        let struct (t0, t1) = window

        if t0 > t1 then
            None
        else
            Some({ X = a.X + t0 * dx; Y = a.Y + t0 * dy }, { X = a.X + t1 * dx; Y = a.Y + t1 * dy })

    // Half the difference `p - q`, computed as `p/2 - q/2` so that two opposite-signed extremes cannot
    // overflow on the way (`MaxValue - (-MaxValue)` is infinite; `MaxValue/2 - (-MaxValue/2)` is not).
    // Halving by `ScaleB` is exact for every normal operand, so this loses nothing the solve depends on.
    let inline private halfDiff (p: float) (q: float) =
        System.Math.ScaleB(p, -1) - System.Math.ScaleB(q, -1)

    // Split a finite vector into a mantissa whose larger component lies in `[1, 2)` and the power-of-two
    // exponent that restores it: `v = 2^k * v'`. `ScaleB` shifts the exponent field without touching the
    // mantissa, so the split is EXACT — no rounding is introduced, which is what keeps the byte-determinism
    // contract intact. A zero (or, defensively, a non-finite) vector is passed through with `k = 0`.
    let inline private normalise (vx: float) (vy: float) =
        let m = max (abs vx) (abs vy)

        if m = 0.0 || not (System.Double.IsFinite m) then
            struct (vx, vy, 0)
        else
            let k = System.Math.ILogB m
            struct (System.Math.ScaleB(vx, -k), System.Math.ScaleB(vy, -k), k)

    // The overflow-free re-solve, reached only when the direct parametrisation saturated (§ `raySegment`).
    //
    // Both parameters are ratios of cross products over the same denominator:
    //     t = (W × E) / (dir × E)        u = (W × dir) / (dir × E)
    // Every operand appears exactly once in a numerator and once in a denominator, so scaling any vector by
    // a power of two rescales both and cancels out of the quotient. Normalising all three into `[1, 2)`
    // bounds each cross product by 8 — it cannot overflow — and the exponents are then reapplied to the
    // quotients with `ScaleB`, again exactly. Nothing here rounds, so a replay stays byte-identical.
    //
    // `W` is anchored at whichever endpoint is NEARER `origin`, with `E` running from it to the far one.
    // Normalising alone would make the solve finite; anchoring is what makes it *accurate*, because a far
    // anchor forces `W`'s short component down into the subnormals when the long one is scaled to `[1, 2)`.
    // `u` is measured from that anchor, and `[0, 1]` describes the same point set from either end, so no
    // `u' = 1 - u` remapping is needed — and `u` is internal to the on-segment test, never returned.
    let private raySegmentRescaled (origin: Point) (dir: Point) (seg: Segment) : (Point * float) option =
        let nearer, farther =
            let reach (p: Point) =
                max (abs (halfDiff p.X origin.X)) (abs (halfDiff p.Y origin.Y))

            if reach seg.A <= reach seg.B then seg.A, seg.B else seg.B, seg.A

        let struct (wx, wy, a) = normalise (halfDiff nearer.X origin.X) (halfDiff nearer.Y origin.Y)
        let struct (dx, dy, b) = normalise dir.X dir.Y
        let struct (ex, ey, c) = normalise (halfDiff farther.X nearer.X) (halfDiff farther.Y nearer.Y)

        let denom = dx * ey - dy * ex

        if denom = 0.0 then
            None // parallel, or a zero-length segment — the same verdict the direct solve reaches
        else
            // `W` and `E` were each built from a HALVED difference, so restoring them costs one extra
            // power of two apiece: `W = 2^(a+1) * w`, `E = 2^(c+1) * e`, `dir = 2^b * d`. Substituting into
            // the two ratios, `E`'s exponent cancels out of `t` entirely and `dir`'s out of `u`.
            let t = System.Math.ScaleB((wx * ey - wy * ex) / denom, (a + 1) - b)
            let u = System.Math.ScaleB((wx * dy - wy * dx) / denom, (a + 1) - (c + 1))

            // A true `t` can still be unrepresentable (an origin genuinely `MaxValue` away from the hit),
            // and `origin + t * dir` can overflow even for finite `t`. Both degrade to `None`, as before.
            if not (System.Double.IsFinite t && System.Double.IsFinite u) then
                None
            elif t >= 0.0 && u >= 0.0 && u <= 1.0 then
                let hit = { X = origin.X + t * dir.X; Y = origin.Y + t * dir.Y }
                if isFinitePoint hit then Some(hit, t) else None
            else
                None

    let raySegment (origin: Point) (dir: Point) (seg: Segment) : (Point * float) option =
        if not (isFinitePoint origin && isFinitePoint dir && isFiniteSeg seg) then
            None
        else
            let ex = seg.B.X - seg.A.X
            let ey = seg.B.Y - seg.A.Y
            // denom = dir × segDir. Zero ⇒ parallel (also covers a zero-length segment: ex = ey = 0).
            let denom = dir.X * ey - dir.Y * ex

            if denom = 0.0 then
                None
            else
                let wx = seg.A.X - origin.X
                let wy = seg.A.Y - origin.Y
                let t = (wx * ey - wy * ex) / denom // (W × segDir) / (dir × segDir)
                let u = (wx * dir.Y - wy * dir.X) / denom // (W × dir) / (dir × segDir)

                // Finite operands do not imply a finite result. A cross product of two finite vectors
                // overflows once the magnitudes multiply past `Double.MaxValue`, and what overflows is the
                // *parametrisation*, not the answer: `W` is anchored at `seg.A`, so a far `seg.A` inflates
                // `wx`/`wy` even when the crossing itself is nearby. Two distinct saturations follow, and
                // neither is detectable from `t` and `u` alone:
                //
                //   * The NUMERATOR overflows while `denom` stays finite ⇒ `t` is ±infinity. #56 caught
                //     this one, because the constructed point `origin + infinity * dir` is infinite (and
                //     NaN wherever `dir` is zero, since `infinity * 0.0 = NaN`) — a NaN vertex in the ring.
                //   * The DENOMINATOR overflows ⇒ `t` and `u` come back as ORDINARY numbers near zero,
                //     because `finite / infinity = 0`. Nothing downstream looks wrong: `raySegment` reports
                //     a hit at the origin with `t = 0`, silently, for a crossing that is elsewhere.
                //
                // So finiteness of `denom` is part of the precondition, not a consequence of it. When any of
                // the three saturates, re-solve with the operands rescaled by exact powers of two, which
                // recovers the true `t` for every intersection that is itself representable (#59).
                //
                // The direct solve below is left bit-for-bit as it was, and is still what every ordinary
                // input takes: the rescaled path is a fallback, not a replacement, so no currently-computable
                // result moves by even an ulp and the golden replays stay byte-identical.
                if
                    not (System.Double.IsFinite denom && System.Double.IsFinite t && System.Double.IsFinite u)
                then
                    raySegmentRescaled origin dir seg
                elif t >= 0.0 && u >= 0.0 && u <= 1.0 then
                    // A finite `t` and a finite `dir` can still overflow their product.
                    let hit = { X = origin.X + t * dir.X; Y = origin.Y + t * dir.Y }
                    if isFinitePoint hit then Some(hit, t) else None
                else
                    None

    let isVisible (source: Point) (target: Point) (segments: Segment list) : bool =
        let dir = { X = target.X - source.X; Y = target.Y - source.Y }

        if not (isFinitePoint source && isFinitePoint target) then
            false
        elif sqLen dir = 0.0 then
            true // the source can always see itself
        else
            // `target` sits at t = 1 along `dir`; a blocker lies strictly between (0 < t < 1).
            segments
            |> List.exists (fun s ->
                match raySegment source dir s with
                | Some(_, t) -> t > 1e-9 && t < 1.0 - 1e-9
                | None -> false)
            |> not

    // The four edges of the axis-aligned bound box `source ± radius`, as synthetic walls, so a ray that
    // hits no real occluder still terminates on the bound and the polygon is always closed.
    let private boundEdges (source: Point) (radius: float) : Segment list * Point list =
        let x0, y0 = source.X - radius, source.Y - radius
        let x1, y1 = source.X + radius, source.Y + radius
        let tl = { X = x0; Y = y0 }
        let tr = { X = x1; Y = y0 }
        let br = { X = x1; Y = y1 }
        let bl = { X = x0; Y = y1 }
        [ { A = tl; B = tr }; { A = tr; B = br }; { A = br; B = bl }; { A = bl; B = tl } ], [ tl; tr; br; bl ]

    // Nearest hit of a single ray against every candidate segment (deterministic: `List.fold` keeps the
    // first minimum in list order on a `t` tie). `None` only if the ray hits nothing (guarded away by
    // the ever-present bound edges).
    let private nearestHit (source: Point) (dir: Point) (segs: Segment list) : Point option =
        (None, segs)
        ||> List.fold (fun best s ->
            match raySegment source dir s with
            | None -> best
            | Some(p, t) ->
                match best with
                | Some(_, bt) when bt <= t -> best
                | _ -> Some(p, t))
        |> Option.map fst

    // Total rotational order of points around `source`, computed from cross products only (no `atan2`):
    // half-plane first, then cross-product sign, then squared distance, then the supplied integer index.
    let private angleCompare (source: Point) (a: Point * int) (b: Point * int) : int =
        let pa, ia = a
        let pb, ib = b
        let va = { X = pa.X - source.X; Y = pa.Y - source.Y }
        let vb = { X = pb.X - source.X; Y = pb.Y - source.Y }
        // half = 0 for angles in [0, π) (upper, incl. +x axis), 1 for [π, 2π): a consistent CCW start.
        let half (v: Point) =
            if v.Y < 0.0 || (v.Y = 0.0 && v.X < 0.0) then 1 else 0

        let ha, hb = half va, half vb

        if ha <> hb then
            compare ha hb
        else
            let cross = va.X * vb.Y - va.Y * vb.X

            if cross > 0.0 then -1
            elif cross < 0.0 then 1
            else
                let c = compare (sqLen va) (sqLen vb)
                if c <> 0 then c else compare ia ib

    let polygon (settings: Settings) (source: Point) (segments: Segment list) : VisibilityPolygon =
        // Total on a bad radius: fall back to a minimal positive bound rather than throwing.
        let radius =
            if System.Double.IsFinite settings.Radius && settings.Radius > 0.0 then
                settings.Radius
            else
                1.0

        if not (isFinitePoint source) then
            { Source = source; Vertices = [] }
        else

        // Drop non-finite and zero-length occluders up front (total; they can never occlude).
        let real =
            segments
            |> List.filter (fun s -> isFiniteSeg s && sqLen { X = s.B.X - s.A.X; Y = s.B.Y - s.A.Y } > 0.0)

        // Clip the occluders to the sight bound box, by an exact segment-vs-box test. NOT a `SpatialGrid`
        // bucket: the grid indexes *points*, so bucketing a segment by its endpoints drops a wall that
        // spans the box with both ends outside it — the viewpoint then sees straight through it. Long
        // walls are the common case, not the corner case, so this test is exact.
        //
        // Clipping, rather than merely keeping, is what puts an aim point where a spanning wall crosses
        // the bound. Rays never travel beyond the bound anyway (they terminate on its edges), so trimming
        // an occluder to it cannot change which rays it blocks. A wall that only grazes a corner clips to
        // zero length and is dropped — it occludes nothing.
        let boundRect =
            { X = source.X - radius
              Y = source.Y - radius
              Width = 2.0 * radius
              Height = 2.0 * radius }

        let culled =
            real
            |> List.choose (fun s ->
                clipSegmentToRect boundRect s.A s.B
                |> Option.map (fun (a, b) -> { A = a; B = b })
                |> Option.filter (fun s -> sqLen { X = s.B.X - s.A.X; Y = s.B.Y - s.A.Y } > 0.0))

        let bEdges, bCorners = boundEdges source radius
        let allSegs = culled @ bEdges

        // Aim points: every culled-occluder endpoint plus the bound corners.
        let aimPoints =
            [ for s in culled do
                  yield s.A
                  yield s.B
              yield! bCorners ]

        // For each aim point cast three rays (at it, and nudged either side) to slip past corners.
        let rays =
            [ for p in aimPoints do
                  let d = { X = p.X - source.X; Y = p.Y - source.Y }

                  if sqLen d > 0.0 then
                      yield d
                      yield { X = d.X - nudge * d.Y; Y = d.Y + nudge * d.X }
                      yield { X = d.X + nudge * d.Y; Y = d.Y - nudge * d.X } ]

        let hits =
            rays
            |> List.choose (fun d -> nearestHit source d allSegs)
            |> List.indexed
            |> List.map (fun (i, p) -> p, i)

        let ordered = hits |> List.sortWith (angleCompare source) |> List.map fst

        { Source = source; Vertices = ordered }
