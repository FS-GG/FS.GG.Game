namespace FS.GG.Game.Core

[<RequireQualifiedAccess>]
module Physics =

    type BodyKind =
        | Static
        | Kinematic
        | Dynamic

    type Shape =
        | SCircle of radius: float
        | SBox of halfExtents: Point
        | SPoly of polygon: ConvexPolygon

    type Material = { Restitution: float; Friction: float }

    type Config =
        { Gravity: Point
          VelocityIterations: int
          PositionIterations: int
          Slop: float
          Correction: float
          BounceThreshold: float
          SleepLinearSq: float
          SleepAngular: float
          SleepTicks: int
          BroadPhaseCellSize: float }

    // Representation hidden by the .fsi (opaque `World`, as `SpatialGrid<'T>` is). Struct-of-arrays, not
    // an array of records: `Loop.advance` copies the world once per step (`previous <- current`), and the
    // copy of an SoA world is one `Array.blit` per field — a memcpy of contiguous memory — where an array
    // of records is N heap allocations and N cache misses. The layout IS the performance strategy, so it
    // is chosen now, before there is anything to optimise.
    //
    // Constitution §IV sanctions a mutable interior "where it is clearer or measurably necessary; say so
    // in a short comment": this is that case, and the arrays never escape, because `World` is opaque and
    // every function here returns values.
    //
    // `step` adds `Vel`/`Rot`/`AngVel` — the integrated state — plus `InvMass`/`InvInertia`, derived once
    // in `addBody` rather than recomputed per step. Zero inverse mass IS the encoding of "infinite mass",
    // so `Static` and `Kinematic` need no branch in the solver: they simply absorb every impulse.
    //
    // #76 adds the two pieces of CROSS-TICK state the performance slice needs, and nothing else:
    //
    //   * `Asleep`/`SleepCounter` — the sleeping lever. The counter is an `int` count of consecutive ticks
    //     under threshold, never an accumulation of elapsed float seconds (R6): a float accumulator decides
    //     differently on two machines, and the whole point of the counter is that it does not.
    //
    //   * `Cache*` — the warm-start lever: last tick's accumulated normal and tangent impulse per contact
    //     point, keyed by `(A, B, FeatureId, point)`. Held as six parallel arrays, kept STRICTLY ASCENDING
    //     by that key, because the solver stages its contact points in exactly that order — so seeding this
    //     tick from last tick's cache is a linear two-cursor merge, with no dictionary to iterate (R6: a
    //     hash order is not a determinism guarantee) and no sort to pay for.
    //
    // Neither reaches `checksum`, which hashes body state only (R3). That is a property of `checksum`, not
    // an accident of the layout — see its comment.
    type World =
        { Config: Config
          Kinds: BodyKind[]
          Shapes: Shape[]
          Materials: Material[]
          Pos: Point[]
          Vel: Point[]
          Rot: float[]
          AngVel: float[]
          InvMass: float[]
          InvInertia: float[]
          Asleep: bool[]
          SleepCounter: int[]
          CacheA: int[]
          CacheB: int[]
          CacheFeature: int[]
          CachePoint: int[]
          CacheN: float[]
          CacheT: float[] }

    let private finite (v: float) =
        not (System.Double.IsNaN v) && not (System.Double.IsInfinity v)

    let private finitePoint (p: Point) = finite p.X && finite p.Y

    // Shoelace area magnitude — zero for a collinear ring. Mirrors `Geometry`'s private `ringArea`, so a
    // polygon this module calls degenerate is exactly one `Geometry.polygonManifold` would refuse.
    let private ringArea (v: Point[]) =
        let mutable acc = 0.0

        for i in 0 .. v.Length - 1 do
            let p = v.[i]
            let q = v.[(i + 1) % v.Length]
            acc <- acc + (p.X * q.Y - q.X * p.Y)

        abs acc / 2.0

    let private vadd (a: Point) (b: Point) : Point = { X = a.X + b.X; Y = a.Y + b.Y }
    let private vsub (a: Point) (b: Point) : Point = { X = a.X - b.X; Y = a.Y - b.Y }
    let private vscale (s: float) (v: Point) : Point = { X = s * v.X; Y = s * v.Y }
    let private vdot (a: Point) (b: Point) = a.X * b.X + a.Y * b.Y

    // The 2D scalar cross `a × b`. In 2D the cross product of two vectors is a scalar (the z component of
    // the 3D result), and the cross of a scalar with a vector is a vector — the two shapes the angular
    // terms of an impulse need, and the reason they are separate functions rather than one.
    let private vcross (a: Point) (b: Point) = a.X * b.Y - a.Y * b.X

    // `w × r` for a scalar angular velocity `w`: the linear velocity that rotation contributes at `r`.
    let private scrossv (s: float) (v: Point) : Point = { X = -s * v.Y; Y = s * v.X }

    // Rotate about the origin. `rotation = 0.0` returns the input *bit for bit* rather than multiplying by
    // `cos 0 = 1.0` and `sin 0 = 0.0` — which would also be exact, except that `-0.0 * 1.0 - 0.0 * y` is
    // not `-0.0` for every `y`. Every body starts at `Rot = 0.0`, so this fast path is the common one, and
    // it is what keeps the broad phase byte-identical to the pre-rotation `pairs` (#74/#84) it inherited.
    let private vrot (rotation: float) (v: Point) : Point =
        if rotation = 0.0 then
            v
        else
            let c = cos rotation
            let s = sin rotation
            { X = c * v.X - s * v.Y
              Y = s * v.X + c * v.Y }

    // A shape's extent about its own origin, as `struct(loX, loY, hiX, hiY)`; `ValueNone` for a degenerate
    // shape — the no-collision input of the `Shape` contract. NaN fails every `>` test, so the guards are
    // NaN-safe without a separate check.
    let private localBounds (shape: Shape) : struct (float * float * float * float) voption =
        match shape with
        | SCircle radius ->
            if finite radius && radius > 0.0 then
                ValueSome(struct (-radius, -radius, radius, radius))
            else
                ValueNone
        | SBox halfExtents ->
            if finitePoint halfExtents && halfExtents.X > 0.0 && halfExtents.Y > 0.0 then
                ValueSome(struct (-halfExtents.X, -halfExtents.Y, halfExtents.X, halfExtents.Y))
            else
                ValueNone
        | SPoly polygon ->
            let v = polygon.Vertices

            // `not (area > 0.0)` rather than `area <= 0.0`: a ring whose shoelace terms overflow and
            // cancel has a NaN area, and NaN fails BOTH comparisons. Only the negated form rejects it —
            // which is what `Geometry.wellFormed` does, and the parity claimed above is the point.
            if v.Length < 3 || not (v |> Array.forall finitePoint) || not (ringArea v > 0.0) then
                ValueNone
            else
                let mutable loX, loY = v.[0].X, v.[0].Y
                let mutable hiX, hiY = v.[0].X, v.[0].Y

                for p in v do
                    loX <- min loX p.X
                    loY <- min loY p.Y
                    hiX <- max hiX p.X
                    hiY <- max hiY p.Y

                ValueSome(struct (loX, loY, hiX, hiY))

    // Bounds of a *rotated* shape about its own origin. Once `step` can spin a body, the unrotated extent
    // stops bounding it, and a broad phase that keeps using it drops real contacts — so rotation has to
    // reach `pairs`, not just the narrow phase.
    //
    // A non-finite rotation makes the body collide with nothing, exactly as a non-finite position does:
    // there is no box to bound, and returning one would leak NaN into `Geometry.intersects`.
    //
    // At `rotation = 0.0` this returns `localBounds` unchanged, so the pair set — and every golden
    // checksum keyed on it — is bit-identical to the pre-rotation broad phase for any world of unrotated
    // bodies, which is every world `addBody` can build.
    let private rotatedBounds (shape: Shape) (rotation: float) : struct (float * float * float * float) voption =
        match localBounds shape with
        | ValueNone -> ValueNone
        | ValueSome unrotated when rotation = 0.0 -> ValueSome unrotated
        | ValueSome(struct (loX, loY, hiX, hiY)) ->
            if not (finite rotation) then
                ValueNone
            else
                match shape with
                // A circle is rotationally symmetric: its box is its box.
                | SCircle _ -> ValueSome(struct (loX, loY, hiX, hiY))
                | SBox _
                | SPoly _ ->
                    // The rotated hull of a convex shape is bounded by the rotated hull of its box's
                    // corners for `SBox`, and by its rotated vertices for `SPoly`. Both are the extremes
                    // of a convex set, so min/max over them is exact, not conservative.
                    let corners =
                        match shape with
                        | SPoly polygon -> polygon.Vertices
                        | _ ->
                            [| { X = loX; Y = loY }
                               { X = hiX; Y = loY }
                               { X = hiX; Y = hiY }
                               { X = loX; Y = hiY } |]

                    let mutable rloX, rloY = infinity, infinity
                    let mutable rhiX, rhiY = -infinity, -infinity

                    for v in corners do
                        let r = vrot rotation v
                        rloX <- min rloX r.X
                        rloY <- min rloY r.Y
                        rhiX <- max rhiX r.X
                        rhiY <- max rhiY r.Y

                    ValueSome(struct (rloX, rloY, rhiX, rhiY))

    // World-space AABB of body `i`, or `ValueNone` when it collides with nothing.
    let private aabbOf (world: World) (i: int) : Rect voption =
        let p = world.Pos.[i]

        if not (finitePoint p) then
            ValueNone
        else
            match rotatedBounds world.Shapes.[i] world.Rot.[i] with
            | ValueNone -> ValueNone
            | ValueSome(struct (loX, loY, hiX, hiY)) ->
                ValueSome
                    { X = p.X + loX
                      Y = p.Y + loY
                      Width = hiX - loX
                      Height = hiY - loY }

    // Only a `Dynamic` body has finite mass, so a pair without one can never resolve.
    let private solvable (a: BodyKind) (b: BodyKind) = a = Dynamic || b = Dynamic

    // Mass and rotational inertia of a shape, as `struct(mass, inertia)`; `ValueNone` for a degenerate
    // shape, which has neither.
    //
    // **Density is 1.0, and the body's origin is its centre of mass.** Neither `Material` nor `addBody`
    // carries a mass, a density or a centroid, so this slice must fix a convention, and `ρ = 1` is the one
    // that keeps `mass` a pure function of `Shape` — no new field, no new parameter, nothing to keep in
    // sync. A later `Material.Density` multiplies both quantities and is a purely additive change; a later
    // centroid shift is not, which is why `SPoly`'s inertia is taken about the ORIGIN rather than about
    // the ring's centroid. A polygon whose centroid is not its origin therefore spins about its origin.
    // That is a choice, not an oversight: the origin is what `Pos` means, and what `interpolate` (#78)
    // will lerp.
    //
    // Inertia is about the axis through that origin. Zero-inertia shapes cannot exist here (`localBounds`
    // has already rejected them), but the caller still guards, because `1.0 / 0.0` is not an error in
    // floating point — it is `infinity`, and an infinite inverse inertia is a body that spins up to NaN
    // on the first impulse.
    let private massProps (shape: Shape) : struct (float * float) voption =
        match localBounds shape with
        | ValueNone -> ValueNone
        | ValueSome _ ->
            match shape with
            | SCircle radius ->
                let m = System.Math.PI * radius * radius
                ValueSome(struct (m, 0.5 * m * radius * radius))
            | SBox halfExtents ->
                let w = 2.0 * halfExtents.X
                let h = 2.0 * halfExtents.Y
                let m = w * h
                ValueSome(struct (m, m * (w * w + h * h) / 12.0))
            | SPoly polygon ->
                // Unit-density mass is the ring's area; the polar second moment about the origin is the
                // standard triangle-fan sum over the same fan.
                //
                // Each fan term is SIGNED, and the sign is taken off the total — never off the term. A fan
                // triangle `(origin, p, q)` whose winding opposes the ring's contributes a NEGATIVE area,
                // and that cancellation is the whole mechanism by which the fan measures a polygon the
                // origin lies outside of. Taking `abs` per term instead adds those triangles rather than
                // subtracting them: the triangle `[(10,0); (11,0); (10,1)]` would weigh 21x its true mass.
                // `ringArea` above already gets this right, and `localBounds` used it to accept this very
                // polygon — so a per-term `abs` here would disagree with the gate that let the shape in.
                //
                // `abs` on the totals (rather than assuming a CCW ring) is what keeps both quantities
                // winding-independent, which `ringArea` also promises.
                let v = polygon.Vertices
                let mutable twiceArea = 0.0
                let mutable moment = 0.0

                for i in 0 .. v.Length - 1 do
                    let p = v.[i]
                    let q = v.[(i + 1) % v.Length]
                    let c = vcross p q
                    twiceArea <- twiceArea + c
                    moment <- moment + c * (vdot p p + vdot p q + vdot q q)

                ValueSome(struct (abs twiceArea / 2.0, abs moment / 12.0))

    // The inverse mass and inverse inertia of a body, which is what every impulse actually divides by.
    // Zero means infinite: `Static` and `Kinematic` bodies are immovable by construction, and a degenerate
    // shape has no mass to move. Guarding on `> 0.0 && finite` rejects NaN too, since NaN fails every
    // comparison — so a body built from an overflowing polygon absorbs impulses rather than spreading NaN.
    let private inverseProps (kind: BodyKind) (shape: Shape) : struct (float * float) =
        match kind with
        | Static
        | Kinematic -> struct (0.0, 0.0)
        | Dynamic ->
            match massProps shape with
            | ValueNone -> struct (0.0, 0.0)
            | ValueSome(struct (m, i)) ->
                let invM = if finite m && m > 0.0 then 1.0 / m else 0.0
                let invI = if finite i && i > 0.0 then 1.0 / i else 0.0
                struct (invM, invI)

    let empty (config: Config) : World =
        { Config = config
          Kinds = [||]
          Shapes = [||]
          Materials = [||]
          Pos = [||]
          Vel = [||]
          Rot = [||]
          AngVel = [||]
          InvMass = [||]
          InvInertia = [||]
          Asleep = [||]
          SleepCounter = [||]
          CacheA = [||]
          CacheB = [||]
          CacheFeature = [||]
          CachePoint = [||]
          CacheN = [||]
          CacheT = [||] }

    let addBody
        (kind: BodyKind)
        (shape: Shape)
        (material: Material)
        (position: Point)
        (world: World)
        : struct (int * World) =
        let index = world.Pos.Length
        let struct (invMass, invInertia) = inverseProps kind shape

        let grown =
            { world with
                Kinds = Array.append world.Kinds [| kind |]
                Shapes = Array.append world.Shapes [| shape |]
                Materials = Array.append world.Materials [| material |]
                Pos = Array.append world.Pos [| position |]
                // A body enters at rest and unrotated. Nothing on this surface can impart an initial
                // velocity or orientation — `step` is the only thing that moves a body — so `Rot = 0.0`
                // for every world `addBody` can build, and `vrot`'s fast path is always taken.
                Vel = Array.append world.Vel [| { X = 0.0; Y = 0.0 } |]
                Rot = Array.append world.Rot [| 0.0 |]
                AngVel = Array.append world.AngVel [| 0.0 |]
                InvMass = Array.append world.InvMass [| invMass |]
                InvInertia = Array.append world.InvInertia [| invInertia |]
                // A body enters AWAKE, however still the world around it. It has never been under the sleep
                // threshold for a single tick, so its counter starts at zero, and a body dropped into a
                // settled scene falls rather than joining the freeze.
                Asleep = Array.append world.Asleep [| false |]
                SleepCounter = Array.append world.SleepCounter [| 0 |] }

        // The warm-start cache is carried across UNCHANGED. Its keys are body indices, and `addBody` only
        // ever appends — every existing index still names the body it named — so no entry is invalidated by
        // a body arriving. Clearing it here would cold-start every contact in the world on the tick a
        // bullet spawns, which is exactly the tick that can least afford it.
        struct (index, grown)

    let pairs (world: World) : struct (int * int)[] =
        let n = world.Pos.Length
        let boxes = Array.init n (aabbOf world)

        // The grid buckets each body under every cell its AABB touches (`SpatialGrid.buildBounds`), so a
        // body's own box is a sufficient query region: two boxes that overlap share a point, that point
        // lies in some cell, and both bodies are filed under it. No dilation, and therefore no global
        // constant — one 500-unit floor no longer widens every other body's query to the whole world.
        //
        // Non-collidable bodies never enter the grid, so they can never be a candidate. Ascending index
        // order is insertion order, which `SpatialGrid.queryBounds` preserves — that is what makes the
        // result sorted below without a sort.
        let grid =
            SpatialGrid.buildBounds
                world.Config.BroadPhaseCellSize
                (seq {
                    for i in 0 .. n - 1 do
                        match boxes.[i] with
                        | ValueSome b -> yield b, i
                        | ValueNone -> ()
                })

        let acc = ResizeArray<struct (int * int)>()

        for i in 0 .. n - 1 do
            match boxes.[i] with
            | ValueNone -> ()
            | ValueSome bi ->
                // `queryBounds` filters with `Geometry.intersects` — the same strict-edge test the pair
                // contract names — so a returned `j` already overlaps `i` and needs no re-test here.
                //
                // `j > i` emits each unordered pair exactly once, in the canonical `a < b` order, so no
                // dedup pass is needed; and because i ascends in the outer loop while the candidates come
                // back ascending, `acc` comes out sorted lexicographically by `(a, b)`.
                for j in SpatialGrid.queryBounds bi grid do
                    if j > i && solvable world.Kinds.[i] world.Kinds.[j] then
                        acc.Add(struct (i, j))

        acc.ToArray()

    // ---------------------------------------------------------------------------------------------
    // Narrow phase
    // ---------------------------------------------------------------------------------------------

    // `FeatureId` is opaque by `Geometry`'s contract — "compare it, do not decode it" — and it packs a
    // face pair into a NON-NEGATIVE int. The circle cases have no face pair on the circle's side, so they
    // mint ids from the negative half, which `polygonManifold` can never produce. Distinctness is all the
    // contract asks, and it is what #76's warm-start cache will key on.
    let private featureCircleCircle = -1
    let private featureCirclePoly (face: int) = -2 - face

    // Body `i` as a world-space polygon: its shape rotated by `Rot` about its origin, then translated to
    // `Pos`. Only ever called for a body whose shape is `SBox`/`SPoly` and which `collidable` accepted.
    let private worldPolygon (world: World) (i: int) : ConvexPolygon =
        let p = world.Pos.[i]
        let r = world.Rot.[i]

        match world.Shapes.[i] with
        | SBox halfExtents -> Geometry.obbPolygon p halfExtents r
        | SPoly polygon ->
            { Vertices = polygon.Vertices |> Array.map (fun v -> vadd p (vrot r v)) }
        | SCircle _ -> { Vertices = [||] }

    let private worldCircle (world: World) (i: int) (radius: float) : Circle =
        { Center = world.Pos.[i]; Radius = radius }

    // A body collides with nothing when its position is non-finite, its shape is degenerate, or its
    // rotation is non-finite — exactly the three `ValueNone`s the broad phase already honours.
    let private collidable (world: World) (i: int) =
        finitePoint world.Pos.[i]
        && (rotatedBounds world.Shapes.[i] world.Rot.[i]).IsSome

    // Two circles. `Normal` runs a → b, as `polygonManifold`'s does, and the strict-edge convention is
    // shared with it too: `d < ra + rb`, so a touch is not a contact.
    //
    // Coincident centres yield `ValueNone` rather than an arbitrary normal. There is no direction to
    // separate along, and inventing one (`(1, 0)`, say) would be a silent, unphysical bias — and would
    // divide by zero to get there.
    let private circleCircleManifold (a: int) (b: int) (ca: Circle) (cb: Circle) : Manifold voption =
        let d = vsub cb.Center ca.Center
        let d2 = vdot d d
        let r = ca.Radius + cb.Radius

        if not (d2 < r * r) then
            ValueNone
        else
            let dist = sqrt d2

            if not (dist > 0.0) then
                ValueNone
            else
                let n = vscale (1.0 / dist) d

                ValueSome
                    { A = a
                      B = b
                      Normal = n
                      Depth = r - dist
                      Points = [| vadd ca.Center (vscale ca.Radius n) |]
                      PointCount = 1
                      FeatureId = featureCircleCircle }

    // A circle against a convex polygon, returning `struct(normal, depth, point, featureId)` with the
    // normal directed CIRCLE → POLYGON. The caller orients it to a → b.
    //
    // The scan is the standard directed face query: the face of maximum signed separation is the only
    // candidate, because a convex polygon lies entirely behind each of its faces. Outward normals are
    // taken with the ring's winding — `localBounds` accepted either winding via `ringArea`'s `abs`, so
    // this must too, or a clockwise polygon would push bodies *into* itself.
    let private circlePolygonParts
        (c: Circle)
        (poly: ConvexPolygon)
        : struct (Point * float * Point * int) voption =
        let v = poly.Vertices

        if v.Length < 3 then
            ValueNone
        else
            let mutable signedTwiceArea = 0.0

            for i in 0 .. v.Length - 1 do
                signedTwiceArea <- signedTwiceArea + vcross v.[i] v.[(i + 1) % v.Length]

            // +1 for a counter-clockwise ring, -1 for a clockwise one: the sign that turns the edge's
            // right-hand perpendicular into the OUTWARD normal.
            let winding = if signedTwiceArea >= 0.0 then 1.0 else -1.0

            let mutable bestFace = -1
            let mutable bestSep = -infinity
            let mutable bestNormal = { X = 0.0; Y = 0.0 }

            for i in 0 .. v.Length - 1 do
                let v0 = v.[i]
                let v1 = v.[(i + 1) % v.Length]
                let e = vsub v1 v0
                let len2 = vdot e e

                // A zero-length edge has no normal. Skipping it is safe: a convex ring with a repeated
                // vertex still has every other face, and `localBounds` has already rejected the ring whose
                // every edge is degenerate (zero area).
                if len2 > 0.0 then
                    let len = sqrt len2
                    let nOut = vscale (winding / len) { X = e.Y; Y = -e.X }
                    let sep = vdot nOut (vsub c.Center v0)

                    // Strict `>` keeps the first face on an exact tie — the same first-wins rule
                    // `polygonContact` applies to its axis scan.
                    if sep > bestSep then
                        bestSep <- sep
                        bestFace <- i
                        bestNormal <- nOut

            if bestFace < 0 || not (bestSep < c.Radius) then
                // No face separated it by less than the radius: either every edge was degenerate, or the
                // circle clears the polygon (`sep >= r` — and equality is a touch, not a contact).
                ValueNone
            elif bestSep < 0.0 then
                // The centre is INSIDE the polygon. Every face reports a negative separation, and the
                // least-negative one is the shallowest exit — the direction that leaves soonest.
                let cp = vsub c.Center (vscale bestSep bestNormal)
                ValueSome(struct (vscale -1.0 bestNormal, c.Radius - bestSep, cp, featureCirclePoly bestFace))
            else
                // The centre is outside. The closest point on the polygon is the closest point on that
                // face's SEGMENT — clamping `t` is what handles the vertex region, where the nearest
                // feature is a corner rather than a face and the normal is not the face's.
                let v0 = v.[bestFace]
                let v1 = v.[(bestFace + 1) % v.Length]
                let e = vsub v1 v0
                let len2 = vdot e e
                let t = max 0.0 (min 1.0 (vdot (vsub c.Center v0) e / len2))
                let cp = vadd v0 (vscale t e)
                let d = vsub c.Center cp
                let d2 = vdot d d

                if not (d2 < c.Radius * c.Radius) then
                    ValueNone
                else
                    let dist = sqrt d2

                    // `dist = 0` means the centre sits exactly on the boundary: the face normal is the
                    // only defined direction, and `d / dist` would be `0 / 0`.
                    let nOut = if dist > 0.0 then vscale (1.0 / dist) d else bestNormal

                    ValueSome(struct (vscale -1.0 nOut, c.Radius - dist, cp, featureCirclePoly bestFace))

    let manifold (world: World) (a: int) (b: int) : Manifold voption =
        let n = world.Pos.Length

        if a < 0 || b < 0 || a >= n || b >= n || a = b then
            ValueNone
        elif not (collidable world a) || not (collidable world b) then
            ValueNone
        else
            match world.Shapes.[a], world.Shapes.[b] with
            | SCircle ra, SCircle rb ->
                circleCircleManifold a b (worldCircle world a ra) (worldCircle world b rb)

            | SCircle ra, _ ->
                match circlePolygonParts (worldCircle world a ra) (worldPolygon world b) with
                | ValueNone -> ValueNone
                | ValueSome(struct (normal, depth, point, feature)) ->
                    // `circlePolygonParts` directs the normal circle → polygon, which here is a → b.
                    ValueSome
                        { A = a
                          B = b
                          Normal = normal
                          Depth = depth
                          Points = [| point |]
                          PointCount = 1
                          FeatureId = feature }

            | _, SCircle rb ->
                match circlePolygonParts (worldCircle world b rb) (worldPolygon world a) with
                | ValueNone -> ValueNone
                | ValueSome(struct (normal, depth, point, feature)) ->
                    // Here the normal runs circle → polygon, i.e. b → a. Flip it to a → b.
                    ValueSome
                        { A = a
                          B = b
                          Normal = vscale -1.0 normal
                          Depth = depth
                          Points = [| point |]
                          PointCount = 1
                          FeatureId = feature }

            | _, _ ->
                // `polygonManifold` labels its arguments `0` and `1`; re-label them with the body indices
                // so `A`/`B` index the world, and `Normal` (already a → b) needs no flip.
                match Geometry.polygonManifold (worldPolygon world a) (worldPolygon world b) with
                | ValueNone -> ValueNone
                | ValueSome m -> ValueSome { m with A = a; B = b }

    // ---------------------------------------------------------------------------------------------
    // The step
    // ---------------------------------------------------------------------------------------------

    // A coefficient a caller may have handed us as NaN, negative, or greater than one. NaN fails both
    // comparisons, so the `not (x > lo)` form — rather than `x < lo` — is what maps it to `lo`.
    let private clamp01 (x: float) = if not (x > 0.0) then 0.0 elif x > 1.0 then 1.0 else x

    let private clampLow (x: float) = if not (x > 0.0) then 0.0 else x

    // Strict lexicographic `<` on a warm-start cache key. The key is `(A, B, FeatureId, point)`: the pair,
    // the contacting feature pair, and which of the manifold's (up to two) points this is.
    let inline private keyLess
        (a1: int)
        (b1: int)
        (f1: int)
        (p1: int)
        (a2: int)
        (b2: int)
        (f2: int)
        (p2: int)
        =
        if a1 <> a2 then a1 < a2
        elif b1 <> b2 then b1 < b2
        elif f1 <> f2 then f1 < f2
        else p1 < p2

    let step (world: World) (dt: float) : World =
        let n = world.Pos.Length
        let cfg = world.Config

        // A non-finite or non-positive `dt` is a no-op, not an error: `step` is `Loop.advance`'s
        // `integrate`, and `Loop` is total for every `dt` it can be handed.
        if n = 0 || not (finite dt) || dt <= 0.0 then
            world
        else
            // Copy once, then mutate in place. `working` aliases these very arrays, so `pairs` and
            // `manifold` read the CURRENT state through it at every point below — no rebuild, no restage.
            let pos = Array.copy world.Pos
            let vel = Array.copy world.Vel
            let rot = Array.copy world.Rot
            let angVel = Array.copy world.AngVel

            let working =
                { world with
                    Pos = pos
                    Vel = vel
                    Rot = rot
                    AngVel = angVel }

            let invMass = world.InvMass
            let invInertia = world.InvInertia

            let asleep = Array.copy world.Asleep
            let sleepCounter = Array.copy world.SleepCounter

            // 1. Broad phase, then the wake pass — BEFORE gravity, so a body woken this tick also falls
            //    this tick rather than hanging weightless for one frame. The reorder is free and it is
            //    sound: `pairs` and `manifold` read `Pos`/`Rot`/`Shapes`/`Kinds` and never a velocity, so
            //    integrating velocity first or last cannot move a single contact.
            let pairList = pairs working
            let pairCount = pairList.Length

            // "At rest" — the SAME predicate the sleep counter increments on, read here off the velocities
            // this step opens with, which are the ones that drove that counter last step. Defining it once
            // and using it twice is what keeps "moving enough to wake a neighbour" and "moving enough to
            // stay awake" from drifting apart. NaN fails both `<`, so a body whose velocity has gone
            // non-finite is never at rest — it moves, chaotically, and wakes what it touches.
            let inline atRest (i: int) =
                vdot vel.[i] vel.[i] < cfg.SleepLinearSq && abs angVel.[i] < cfg.SleepAngular

            // `mover` is a snapshot taken BEFORE anything wakes: a body that is awake, not static, and
            // ACTUALLY MOVING this tick.
            //
            // The last clause is load-bearing, and "awake" alone is the bug it exists to prevent. Two
            // dynamic bodies resting on one another are each awake while their sleep counters climb, and
            // counters that reach `SleepTicks` a tick apart would then have each body wake the other the
            // instant it dozed off — forever. A stack of two might sleep, by the luck of both counters
            // filling on the same tick; a stack of three never would. Box2D escapes this by sleeping a
            // solver ISLAND as a unit, and §4 puts islands out of scope, so the escape here is to ask the
            // stronger and more honest question: is the neighbour *moving*, not merely *awake*?
            //
            // The price is that a wake propagates only through bodies that visibly move. A sleeper that
            // takes a load without shifting does not pass the disturbance on to the one beneath it — which
            // is the right reading of "at rest", and cheap, because it is already supported.
            //
            // Freezing the snapshot is what makes waking order-INDEPENDENT: otherwise a body woken by an
            // early pair would wake its own neighbour later in the same sweep, and how far a wake travelled
            // through a stack would depend on the order `pairs` happened to emit. Instead a wake advances
            // at most one body per tick, through any pair order, on any machine.
            //
            // Only a `Dynamic` body ever sleeps, so `asleep.[i]` already implies `Kinds.[i] = Dynamic`.
            let mover =
                Array.init n (fun i -> not asleep.[i] && world.Kinds.[i] <> Static && not (atRest i))

            // The narrow phase is computed ONCE per pair here, and reused by the solver below.
            //
            // A pair with no mover is skipped outright — neither body can have moved since last tick, so
            // its contact cannot have changed and nothing is left for the solver to do. That skip is the
            // whole O(n) → O(0) claim of sleeping: a settled scene narrow-phases nothing. (The broad phase
            // still runs; culling that needs solver islands, which §4 puts out of scope.)
            let staged = Array.create pairCount ValueNone
            let computed = Array.zeroCreate<bool> pairCount

            for k in 0 .. pairCount - 1 do
                let struct (a, b) = pairList.[k]

                if mover.[a] || mover.[b] then
                    let m = manifold working a b
                    staged.[k] <- m
                    computed.[k] <- true

                    // A sleeper in contact with something that can move must wake: it is about to be leant
                    // on. A `Static` body is not a mover and so never wakes anything — a floor does not
                    // disturb the box that settled on it, which is the entire point of the lever.
                    match m with
                    | ValueSome _ ->
                        if asleep.[a] && mover.[b] then
                            asleep.[a] <- false
                            sleepCounter.[a] <- 0

                        if asleep.[b] && mover.[a] then
                            asleep.[b] <- false
                            sleepCounter.[b] <- 0
                    | ValueNone -> ()

            // 2. Integrate velocity. Semi-implicit Euler: velocity first, position last, so the solve in
            //    between sees the velocity the contact must actually cancel. A zero inverse mass is a
            //    `Static` or `Kinematic` body, which gravity does not touch; a sleeping body is not
            //    integrated at all, which is the other half of what "stops integrating" means.
            let g = cfg.Gravity

            if finitePoint g then
                for i in 0 .. n - 1 do
                    if not asleep.[i] && invMass.[i] > 0.0 then
                        vel.[i] <- vadd vel.[i] (vscale dt g)

            // A sleeping body is IMMOVABLE for the duration of this step — infinite mass, exactly as a
            // `Static` body has. It must be: `step` does not integrate it, so an impulse that changed its
            // velocity would be silently discarded, and the pair would resolve as though one side had
            // absorbed momentum that never went anywhere. Zero inverse mass is already this module's
            // encoding of "unmoved by any impulse", so sleeping needs no branch anywhere in the solver —
            // only these two arrays, which are what the solver divides by from here on.
            let effInvMass = Array.init n (fun i -> if asleep.[i] then 0.0 else invMass.[i])
            let effInvInertia = Array.init n (fun i -> if asleep.[i] then 0.0 else invInertia.[i])

            // 3. Gather the manifolds the solver must see. A pair needs solving exactly when it has an
            //    awake `Dynamic` member — anything else has no finite mass to move. A pair skipped above
            //    can qualify here, because one of its bodies may have just been woken through a different
            //    pair; that one is narrow-phased now.
            let inline solverRelevant (a: int) (b: int) =
                (not asleep.[a] && world.Kinds.[a] = Dynamic)
                || (not asleep.[b] && world.Kinds.[b] = Dynamic)

            let found = ResizeArray<Manifold>()
            let solvePairs = ResizeArray<struct (int * int)>()

            for k in 0 .. pairCount - 1 do
                let struct (a, b) = pairList.[k]

                if solverRelevant a b then
                    solvePairs.Add(struct (a, b))

                    match (if computed.[k] then staged.[k] else manifold working a b) with
                    | ValueSome m -> found.Add m
                    | ValueNone -> ()

            let manifolds = found.ToArray()

            // Sort by `(A, B, FeatureId)` before solving (R6). The broad phase already emits pairs
            // ascending and deduped, and there is one manifold per pair, so this sort is a no-op today —
            // which is exactly why it is here rather than assumed: it is the contract that survives a
            // later broad phase that stops being sorted, and the key is unique so the sort is total.
            Array.sortInPlaceBy (fun (m: Manifold) -> struct (m.A, m.B, m.FeatureId)) manifolds

            // 3. Stage the contact points. Flattened across manifolds: the solver iterates points, not
            //    pairs, and the per-point constants are hoisted out of the iteration loop.
            let mutable count = 0

            for m in manifolds do
                count <- count + m.PointCount

            let cA = Array.zeroCreate<int> count
            let cB = Array.zeroCreate<int> count
            // The other two thirds of the warm-start key. Staged alongside the contact rather than
            // recovered later, because these six arrays ARE next tick's cache — see the write-back.
            let cF = Array.zeroCreate<int> count
            let cP = Array.zeroCreate<int> count
            let cRA = Array.zeroCreate<Point> count
            let cRB = Array.zeroCreate<Point> count
            let cN = Array.zeroCreate<Point> count
            let cT = Array.zeroCreate<Point> count
            let cMassN = Array.zeroCreate<float> count
            let cMassT = Array.zeroCreate<float> count
            let cBias = Array.zeroCreate<float> count
            let cMu = Array.zeroCreate<float> count
            let cAccN = Array.zeroCreate<float> count
            let cAccT = Array.zeroCreate<float> count

            // Velocity of body `i` at the world point offset `r` from its origin: linear plus `ω × r`.
            let inline pointVel (i: int) (r: Point) = vadd vel.[i] (scrossv angVel.[i] r)

            // `BounceThreshold` gates restitution: below it `e = 0`, or a resting box trades a tiny
            // approach velocity for a tiny bounce forever and never comes to rest (R6). A non-finite or
            // negative threshold degrades to zero — every approach then bounces, which is the old,
            // jittery behaviour, but never a NaN.
            let bounceThreshold = clampLow cfg.BounceThreshold

            // The warm-start cursor. `oldA/oldB/...` are last tick's cache, strictly ascending by
            // `(A, B, FeatureId, point)`; the contacts staged below are generated in that same order, since
            // `manifolds` was just sorted by `(A, B, FeatureId)` and a manifold's points ascend. Two sorted
            // sequences and one forward-only cursor is therefore a linear merge — the cursor never rewinds,
            // so seeding the whole world costs O(contacts + cache) and not one hash lookup.
            let oldA = world.CacheA
            let oldB = world.CacheB
            let oldF = world.CacheFeature
            let oldP = world.CachePoint
            let mutable cursor = 0

            let mutable k = 0

            for m in manifolds do
                let a = m.A
                let b = m.B

                // Restitution combines as the MAXIMUM of the two coefficients, not the minimum. A floor is
                // almost always `Restitution = 0`, and `min` would make every ball dead on every floor —
                // restitution would be unreachable in the one scene that motivates it. `max` says instead
                // that a bouncy thing bounces off anything, which is both the useful reading and the one
                // Box2D takes.
                let e = clamp01 (max world.Materials.[a].Restitution world.Materials.[b].Restitution)

                // Coulomb friction combines as the geometric mean of the two coefficients — the usual
                // choice, and the one that makes a frictionless body (`μ = 0`) frictionless against
                // everything, however rough the other surface. Note the asymmetry with restitution above:
                // `max` there lets one bouncy body bounce, `sqrt(μa·μb)` here lets one slippery body slide.
                // Both say "the more surprising surface wins", which is what a game wants.
                let mu = sqrt (clampLow world.Materials.[a].Friction * clampLow world.Materials.[b].Friction)

                for pi in 0 .. m.PointCount - 1 do
                    let p = m.Points.[pi]
                    let rA = vsub p pos.[a]
                    let rB = vsub p pos.[b]
                    let nrm = m.Normal
                    let tan = { X = -nrm.Y; Y = nrm.X }

                    let rnA = vcross rA nrm
                    let rnB = vcross rB nrm

                    let kN =
                        effInvMass.[a]
                        + effInvMass.[b]
                        + effInvInertia.[a] * rnA * rnA
                        + effInvInertia.[b] * rnB * rnB

                    let rtA = vcross rA tan
                    let rtB = vcross rB tan

                    let kT =
                        effInvMass.[a]
                        + effInvMass.[b]
                        + effInvInertia.[a] * rtA * rtA
                        + effInvInertia.[b] * rtB * rtB

                    // Two static bodies never reach here (`solvable`), but a contact between a dynamic body
                    // and itself along the normal CAN have zero effective mass at a degenerate lever arm.
                    // Zero effective mass ⇒ zero impulse, which is the physically right answer and the one
                    // that does not divide by zero.
                    let massN = if kN > 0.0 then 1.0 / kN else 0.0
                    let massT = if kT > 0.0 then 1.0 / kT else 0.0

                    // Approach speed along the normal, measured ONCE, before any impulse. `Normal` runs
                    // a → b, so an approaching pair has `vn < 0`.
                    // Approach speed along the normal, measured ONCE, before any impulse — and in
                    // particular before the warm-start seed below, which is applied in a separate pass for
                    // exactly this reason. Restitution must answer "how fast were they closing when they
                    // met?", and last tick's impulse is not part of that question.
                    let vrel = vsub (pointVel b rB) (pointVel a rA)
                    let vn = vdot vrel nrm
                    let bias = if -vn > bounceThreshold then -e * vn else 0.0

                    // Warm start. Seed this contact with the impulse the SAME contact accumulated last
                    // tick, found by advancing the cursor to its key. A contact that is new this tick — a
                    // fresh pair, or the same pair meeting on a different feature — finds no entry and
                    // starts cold at zero, which is precisely the pre-#76 behaviour.
                    while cursor < oldA.Length
                          && keyLess oldA.[cursor] oldB.[cursor] oldF.[cursor] oldP.[cursor] a b m.FeatureId pi do
                        cursor <- cursor + 1

                    if
                        cursor < oldA.Length
                        && oldA.[cursor] = a
                        && oldB.[cursor] = b
                        && oldF.[cursor] = m.FeatureId
                        && oldP.[cursor] = pi
                    then
                        cAccN.[k] <- world.CacheN.[cursor]
                        cAccT.[k] <- world.CacheT.[cursor]
                        cursor <- cursor + 1

                    cA.[k] <- a
                    cB.[k] <- b
                    cF.[k] <- m.FeatureId
                    cP.[k] <- pi
                    cRA.[k] <- rA
                    cRB.[k] <- rB
                    cN.[k] <- nrm
                    cT.[k] <- tan
                    cMassN.[k] <- massN
                    cMassT.[k] <- massT
                    cBias.[k] <- bias
                    cMu.[k] <- mu
                    k <- k + 1

            let inline applyImpulse (i: int) (j: int) (rI: Point) (rJ: Point) (imp: Point) =
                vel.[i] <- vsub vel.[i] (vscale effInvMass.[i] imp)
                angVel.[i] <- angVel.[i] - effInvInertia.[i] * vcross rI imp
                vel.[j] <- vadd vel.[j] (vscale effInvMass.[j] imp)
                angVel.[j] <- angVel.[j] + effInvInertia.[j] * vcross rJ imp

            // 4. Apply the seeded impulses, as a pass of its OWN. Two reasons it cannot be folded into the
            //    staging loop above: every contact's `bias` must be read off the pre-seed velocities (a
            //    contact staged late would otherwise see the seeds of contacts staged early), and the
            //    solver's first iteration must open on a world where every seed is already in place — that
            //    is what "warm" means.
            //
            //    Seeding an all-zero contact is skipped rather than applied. It is not merely an
            //    optimisation: `vscale 0.0 n` can carry a signed zero into `vel`, and the guard is what
            //    makes a first-touch contact BIT-identical to the cold solver rather than merely equal.
            for c in 0 .. count - 1 do
                if cAccN.[c] <> 0.0 || cAccT.[c] <> 0.0 then
                    let imp = vadd (vscale cAccN.[c] cN.[c]) (vscale cAccT.[c] cT.[c])
                    applyImpulse cA.[c] cB.[c] cRA.[c] cRB.[c] imp

            // 5. Velocity solve. FIXED iterations — never "until converged", because a float convergence
            //    tolerance decides differently on two machines and that is a replay divergence (R6).
            //
            //    The accumulators start at the seeded values, not at zero, so the clamps below (`accN >= 0`
            //    and the friction cone) act on the impulse the contact has applied ACROSS ticks. That is
            //    the whole trick: iteration 1 already stands where last tick's iteration 8 finished.
            for _ in 1 .. max 0 cfg.VelocityIterations do
                for c in 0 .. count - 1 do
                    let a = cA.[c]
                    let b = cB.[c]
                    let rA = cRA.[c]
                    let rB = cRB.[c]

                    // Normal impulse. The ACCUMULATED impulse is clamped non-negative, not the increment:
                    // a contact may pull back an earlier over-correction within a step, but the total it
                    // has applied can never be negative — contacts push, they do not glue.
                    let vn = vdot (vsub (pointVel b rB) (pointVel a rA)) cN.[c]
                    let jn = cMassN.[c] * (cBias.[c] - vn)
                    let accN = max (cAccN.[c] + jn) 0.0
                    let djn = accN - cAccN.[c]
                    cAccN.[c] <- accN
                    applyImpulse a b rA rB (vscale djn cN.[c])

                    // Coulomb friction, clamped to the friction cone `|jt| <= μ·jn` using the normal
                    // impulse accumulated so far. Solved after the normal so the cone it clamps to is the
                    // one this iteration actually established.
                    let vt = vdot (vsub (pointVel b rB) (pointVel a rA)) cT.[c]
                    let jt = cMassT.[c] * -vt
                    let maxF = cMu.[c] * cAccN.[c]
                    let accT = max -maxF (min maxF (cAccT.[c] + jt))
                    let djt = accT - cAccT.[c]
                    cAccT.[c] <- accT
                    applyImpulse a b rA rB (vscale djt cT.[c])

            // 6. Integrate position. A `Static` body never moves; a `Kinematic` one carries its own
            //    velocity through contacts unchanged, which is what makes it kinematic; a sleeping one is
            //    not integrated, which is what makes it free.
            for i in 0 .. n - 1 do
                if world.Kinds.[i] <> Static && not asleep.[i] then
                    pos.[i] <- vadd pos.[i] (vscale dt vel.[i])
                    rot.[i] <- rot.[i] + dt * angVel.[i]

            // 7. Positional correction. The velocity solve removes approach velocity but not the overlap
            //    already present, and left alone that overlap accumulates into a sinking stack.
            //
            //    `Slop` leaves a sliver of penetration uncorrected — correcting to exactly zero makes
            //    resting contacts flicker between touching and separated — and `Correction` removes only a
            //    fraction of the remainder per iteration, which is what keeps the push stable.
            //
            //    The manifold is RECOMPUTED each iteration rather than reusing the staged depth: reusing
            //    it applies the same correction `PositionIterations` times and overshoots by that factor.
            //
            //    Only `solvePairs` are corrected, and against the EFFECTIVE inverse masses: a sleeping body
            //    must not be nudged out of a penetration it is not awake to feel, and its awake partner
            //    must therefore take the whole correction, exactly as it would against a static floor.
            let slop = clampLow cfg.Slop
            let percent = clamp01 cfg.Correction

            if percent > 0.0 then
                for _ in 1 .. max 0 cfg.PositionIterations do
                    for struct (a, b) in solvePairs do
                        match manifold working a b with
                        | ValueNone -> ()
                        | ValueSome m ->
                            let sum = effInvMass.[a] + effInvMass.[b]

                            if sum > 0.0 then
                                let corr = max (m.Depth - slop) 0.0 / sum * percent

                                if corr > 0.0 then
                                    pos.[a] <- vsub pos.[a] (vscale (corr * effInvMass.[a]) m.Normal)
                                    pos.[b] <- vadd pos.[b] (vscale (corr * effInvMass.[b]) m.Normal)

            // 8. Sleep. A `Dynamic` body that has held BOTH speeds under threshold for `SleepTicks`
            //    CONSECUTIVE ticks stops. The counter is an `int` and the comparison exact (R6); one tick
            //    above threshold resets it to zero, so "consecutive" is meant literally.
            //
            //    A non-positive `SleepTicks` disables the lever outright — a body cannot be at rest for
            //    zero consecutive ticks, and reading it as "sleep immediately" would freeze every scene on
            //    its first slow tick. A non-positive `SleepLinearSq` or `SleepAngular` disables it too, but
            //    needs no code: `atRest` is false for every body, because no squared speed is below zero.
            //    NaN fails every `<` for the same reason, so a body whose velocity has gone non-finite
            //    never sleeps — it stays awake, visibly wrong, rather than freezing its own bug in place.
            //
            //    Velocity is ZEROED on falling asleep. A sleeping body is not integrated, so whatever
            //    sliver of velocity it held would be neither spent nor visible — and would spring back into
            //    the world on the tick it woke, seconds later, from nowhere.
            if cfg.SleepTicks > 0 then
                for i in 0 .. n - 1 do
                    if world.Kinds.[i] = Dynamic && not asleep.[i] then
                        if atRest i then
                            sleepCounter.[i] <- sleepCounter.[i] + 1

                            if sleepCounter.[i] >= cfg.SleepTicks then
                                asleep.[i] <- true
                                vel.[i] <- { X = 0.0; Y = 0.0 }
                                angVel.[i] <- 0.0
                        else
                            sleepCounter.[i] <- 0

            // 9. Publish the cache. The staged arrays ARE the cache — same keys, same order, and `cAccN`
            //    and `cAccT` now hold what the solver finished with. They are handed over rather than
            //    copied because nothing else holds a reference to them: they were allocated inside this
            //    step, and `World` is opaque, so they cannot be aliased by a caller.
            //
            //    A pair the solver never saw (both asleep, or asleep against static) contributes no entry,
            //    so its impulses are forgotten and it cold-starts on the tick it wakes. That is the right
            //    trade: it was, by construction, at rest.
            { working with
                Asleep = asleep
                SleepCounter = sleepCounter
                CacheA = cA
                CacheB = cB
                CacheFeature = cF
                CachePoint = cP
                CacheN = cAccN
                CacheT = cAccT }

    // ---------------------------------------------------------------------------------------------
    // The desync tripwire
    // ---------------------------------------------------------------------------------------------

    // The exact bits of a float, canonicalised so that values which COMPARE equal also HASH equal.
    // Two of them do not, bitwise:
    //
    //   * `-0.0` and `0.0` are `=` but differ in the sign bit, and a body drifting to `-0.0` on one
    //     machine and `0.0` on another is not a desync;
    //   * NaN has 2^52 - 1 bit patterns, and which one an operation produces is not portable.
    //
    // Collapsing both is what makes the checksum a tripwire for real divergence rather than for the
    // representation of zero. Note that NaN mapping to one value means two *different* NaN worlds hash
    // alike — acceptable, because a NaN body state is already a bug the checksum did not need to catch.
    let private canonicalBits (v: float) : uint64 =
        if System.Double.IsNaN v then 0x7FF8000000000000UL
        elif v = 0.0 then 0UL
        else uint64 (System.BitConverter.DoubleToInt64Bits v)

    let checksum (world: World) : uint64 =
        // FNV-1a, 64-bit. Chosen over `.GetHashCode()` because the latter is explicitly not stable across
        // runtimes or runs, which is the one property a replay tripwire needs.
        let mutable h = 14695981039346656037UL

        let inline feed (v: float) =
            let b = canonicalBits v
            let mutable s = 0

            while s < 64 do
                h <- (h ^^^ ((b >>> s) &&& 0xFFUL)) * 1099511628211UL
                s <- s + 8

        // The body count is folded in first so that a world of N bodies cannot hash like a world of N+1
        // whose extra body happens to be all zeros.
        feed (float world.Pos.Length)

        for i in 0 .. world.Pos.Length - 1 do
            feed world.Pos.[i].X
            feed world.Pos.[i].Y
            feed world.Vel.[i].X
            feed world.Vel.[i].Y
            feed world.Rot.[i]
            feed world.AngVel.[i]

        h
