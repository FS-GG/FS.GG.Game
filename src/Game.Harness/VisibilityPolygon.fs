namespace FS.GG.Game.Harness

open FS.GG.Game.Core

[<RequireQualifiedAccess>]
module VisibilityPolygon =

    // A "diamond angle" pseudo-angle in [0, 4) that is monotonic in the true atan2(dy, dx) but uses NO
    // trig — quadrant + slope only — so the sweep order is reproducible and free of atan2's platform
    // variance. Undefined only at the origin (dx = dy = 0), which is never an endpoint direction here.
    let private pseudoAngle (dx: float) (dy: float) : float =
        if dy >= 0.0 then
            if dx >= 0.0 then dy / (dx + dy) // quadrant I  -> [0,1)
            else 1.0 - dx / (-dx + dy) // quadrant II -> [1,2)
        elif dx < 0.0 then
            2.0 - dy / (-dx - dy) // quadrant III -> [2,3)
        else
            3.0 + dx / (dx - dy) // quadrant IV -> [3,4)

    // Ray from `o` in direction `d` vs segment a->b. Returns the ray parameter `t` (>= 0, and the hit
    // is `o + t*d`) at the intersection when it lands on the segment, else None. Parallel/degenerate
    // (near-zero cross product) is skipped, so it never divides by ~0.
    let private rayHitT (ox, oy) (dx, dy) (ax, ay) (bx, by) : float option =
        let sx = bx - ax
        let sy = by - ay
        let denom = dx * sy - dy * sx

        if abs denom < 1e-12 then
            None
        else
            let wx = ax - ox
            let wy = ay - oy
            let t = (wx * sy - wy * sx) / denom
            let u = (wx * dy - wy * dx) / denom

            if t >= 0.0 && u >= -1e-9 && u <= 1.0 + 1e-9 then Some t else None

    /// The 2D visibility polygon from `origin` over `segments`, clipped to `bounds`.
    let polygon (origin: Point) (bounds: Rect) (segments: (Point * Point) list) : Point list =
        let ox = origin.X
        let oy = origin.Y

        // The four bounds edges close the scene, so every ray hits something.
        let corners =
            [ { X = bounds.X; Y = bounds.Y }
              { X = bounds.X + bounds.Width; Y = bounds.Y }
              { X = bounds.X + bounds.Width; Y = bounds.Y + bounds.Height }
              { X = bounds.X; Y = bounds.Y + bounds.Height } ]

        let boundsEdges =
            [ (corners.[0], corners.[1])
              (corners.[1], corners.[2])
              (corners.[2], corners.[3])
              (corners.[3], corners.[0]) ]

        let allSegments = boundsEdges @ segments

        // Endpoints to aim rays at (both ends of every segment), skipping any that coincide with the
        // origin (a zero-length direction). A zero-length segment simply contributes two coincident
        // endpoints and no valid intersection.
        let endpoints =
            allSegments
            |> List.collect (fun (a, b) -> [ a; b ])
            |> List.filter (fun p -> p.X <> ox || p.Y <> oy)

        // For an endpoint direction, also aim just to either side (a tiny rotation) so a ray slips past
        // the corner to whatever wall lies behind it — the classic "cast at ±epsilon" trick.
        let eps = 1e-4

        let rayDirs =
            endpoints
            |> List.collect (fun p ->
                let dx = p.X - ox
                let dy = p.Y - oy
                let c = cos eps
                let s = sin eps
                // rotate (dx,dy) by ±eps
                [ (dx, dy)
                  (dx * c - dy * s, dx * s + dy * c)
                  (dx * c + dy * s, -dx * s + dy * c) ])

        // Cast each ray; keep the NEAREST hit (smallest t) over all segments.
        let hits =
            rayDirs
            |> List.choose (fun (dx, dy) ->
                if dx = 0.0 && dy = 0.0 then
                    None
                else
                    let nearest =
                        allSegments
                        |> List.choose (fun (a, b) -> rayHitT (ox, oy) (dx, dy) (a.X, a.Y) (b.X, b.Y))
                        |> function
                            | [] -> None
                            | ts -> Some(List.min ts)

                    nearest
                    |> Option.map (fun t ->
                        let hx = ox + t * dx
                        let hy = oy + t * dy
                        // key on the direction's pseudo-angle (the polygon is ordered by it), tie-break
                        // by t so a nearer hit at the same angle sorts first — a deterministic total order.
                        (pseudoAngle dx dy, t, { X = hx; Y = hy })))

        // Order the hit points by pseudo-angle (then distance) to trace the polygon boundary.
        hits
        |> List.sortBy (fun (ang, t, _) -> (ang, t))
        |> List.map (fun (_, _, p) -> p)
