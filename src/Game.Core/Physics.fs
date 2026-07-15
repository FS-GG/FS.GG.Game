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
    // #94 adds a third piece: `Boxes`/`Grid`, the BROAD-PHASE INDEX. Each body's world-space AABB and the
    // grid that buckets the collidable ones — a pure function of body pose and shape, kept current with the
    // bodies by `empty`/`addBody`/`addBodies` and rebuilt by `step` from the poses it integrates. Carrying it
    // is what lets `pairs` READ the grid instead of rebuilding one per call, and lets a single `step` share
    // one grid between its discrete broad phase and its speculative CCD sweep, rather than build it twice.
    //
    // None of the three reaches `checksum`, which hashes body state only (R3). That is a property of
    // `checksum`, not an accident of the layout — see its comment.
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
          CacheT: float[]
          // The broad-phase index (#94). Derived cross-tick state, like the cache: never presentation, never
          // hashed, and — because a body ADDED and one that MOVED can reach the same pose by different arrays
          // — never compared. `Boxes.[i]` is `ValueNone` exactly where body `i` collides with nothing.
          Boxes: Rect voption[]
          Grid: SpatialGrid<int> }

    // The presentation projection of a body — `Pos` and `Rot`, nothing else. Public via the `.fsi`.
    type Transform = { Position: Point; Rotation: float }

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

    // World-space AABB of body `i`, or `ValueNone` when it collides with nothing. Reads the SoA arrays
    // directly rather than a whole `World`, so `broadPhase` can bound every body while the `World` that will
    // carry them — and its `Boxes`/`Grid` — is still being built.
    let private aabbAt (pos: Point[]) (shapes: Shape[]) (rot: float[]) (i: int) : Rect voption =
        let p = pos.[i]

        if not (finitePoint p) then
            ValueNone
        else
            match rotatedBounds shapes.[i] rot.[i] with
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

    // The smallest half-extent of a shape's LOCAL box — the thinnest cross-section it presents, and so the
    // furthest it may move in one step before it could skip clean past a contact between this step's start
    // and end. Rotation-invariant for the thinness that matters: a box is `min(hx, hy)` thick however it is
    // turned. `ValueNone` for a degenerate shape, which cannot tunnel because it collides with nothing.
    let private minHalfExtent (shape: Shape) : float voption =
        match localBounds shape with
        | ValueNone -> ValueNone
        | ValueSome(struct (loX, loY, hiX, hiY)) -> ValueSome(min ((hiX - loX) * 0.5) ((hiY - loY) * 0.5))

    // The broad-phase index for a set of bodies: each body's world-space AABB (`ValueNone` where the body
    // collides with nothing), and the `SpatialGrid` that buckets the collidable ones under every cell their
    // box touches. Both are a pure function of pose and shape — `Pos`, `Rot`, `Shapes`, and the cell size —
    // which is what lets a `World` carry them as cross-tick state and refresh them only when a body is added
    // or moves.
    //
    // The grid is seeded in ASCENDING body index, an order `SpatialGrid.queryBounds` preserves — the reason
    // `pairs` comes out sorted without a sort. Non-collidable bodies never enter it. Because the box query
    // needs no dilation (a body's own box is a sufficient region: two boxes that overlap share a point, that
    // point lies in a cell, and both bodies are filed under it), one huge floor no longer widens every other
    // body's query to the whole world.
    //
    // Building the index HERE, from the same inputs and in the same order the per-call build used, keeps it
    // byte-identical to the grid `pairs` and the CCD sweep used to build for themselves — so every pair set,
    // every speculative contact, and every golden checksum is unchanged.
    let private broadPhase
        (config: Config)
        (pos: Point[])
        (shapes: Shape[])
        (rot: float[])
        : Rect voption[] * SpatialGrid<int> =
        let n = pos.Length
        let boxes = Array.init n (aabbAt pos shapes rot)

        let grid =
            SpatialGrid.buildBounds
                config.BroadPhaseCellSize
                (seq {
                    for i in 0 .. n - 1 do
                        match boxes.[i] with
                        | ValueSome b -> yield b, i
                        | ValueNone -> ()
                })

        boxes, grid

    let empty (config: Config) : World =
        let boxes, grid = broadPhase config [||] [||] [||]

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
          CacheT = [||]
          Boxes = boxes
          Grid = grid }

    // Append a BATCH of bodies in one pass, returning `struct(indices, world')` where `indices.[k]` is the
    // identity of `bodies.[k]` — dense and ascending from the world's previous body count, exactly the
    // indices the same bodies added one at a time would receive. This is the build path a scene loader
    // wants: it copies each SoA array and rebuilds the broad-phase index ONCE for the whole batch, so
    // building an N-body world is O(N), where N separate `addBody` calls are O(N²) — a copy per body.
    //
    // Every guarantee `addBody` makes is preserved, because this is still an append that returns a new value
    // and never mutates the input world: purity, persistence (the input world is untouched and safe to keep),
    // and stable ascending indices. A body enters at rest, unrotated and awake, however still the scene it
    // joins — so a body dropped into a settled world falls rather than joining the freeze.
    let addBodies (bodies: seq<BodyKind * Shape * Material * Point>) (world: World) : struct (int[] * World) =
        let batch = Array.ofSeq bodies

        if batch.Length = 0 then
            // Adding nothing is the identity, broad-phase index included: no pose changed, so the world's
            // grid is already current for its bodies and is carried through untouched.
            struct ([||], world)
        else
            let baseIndex = world.Pos.Length
            let k = batch.Length
            let kinds = batch |> Array.map (fun (kind, _, _, _) -> kind)
            let shapes = batch |> Array.map (fun (_, shape, _, _) -> shape)
            let materials = batch |> Array.map (fun (_, _, material, _) -> material)
            let positions = batch |> Array.map (fun (_, _, _, position) -> position)

            let invMass = Array.zeroCreate k
            let invInertia = Array.zeroCreate k

            for i in 0 .. k - 1 do
                let struct (im, ii) = inverseProps kinds.[i] shapes.[i]
                invMass.[i] <- im
                invInertia.[i] <- ii

            let kinds' = Array.append world.Kinds kinds
            let shapes' = Array.append world.Shapes shapes
            // Rot is `0.0` for every world these functions can build — `step` is the only thing that turns a
            // body — so `vrot`'s fast path is always taken. `pos'`/`shapes'`/`rot'` are named because the
            // broad-phase rebuild below reads exactly them.
            let rot' = Array.append world.Rot (Array.zeroCreate k)
            let pos' = Array.append world.Pos positions

            // Rebuild the broad-phase index ONCE, for the whole grown set. This single O(N) is what a batch
            // buys over N `addBody` calls, each of which rebuilt it — and is why `addBody` build was O(N²).
            let boxes, grid = broadPhase world.Config pos' shapes' rot'

            let grown =
                { world with
                    Kinds = kinds'
                    Shapes = shapes'
                    Materials = Array.append world.Materials materials
                    Pos = pos'
                    Vel = Array.append world.Vel (Array.create k { X = 0.0; Y = 0.0 })
                    Rot = rot'
                    AngVel = Array.append world.AngVel (Array.zeroCreate k)
                    InvMass = Array.append world.InvMass invMass
                    InvInertia = Array.append world.InvInertia invInertia
                    Asleep = Array.append world.Asleep (Array.zeroCreate k)
                    SleepCounter = Array.append world.SleepCounter (Array.zeroCreate k)
                    // The warm-start cache is carried across UNCHANGED. Its keys are body indices, and this
                    // only ever appends — every existing index still names the body it named — so no entry is
                    // invalidated by a body arriving. Clearing it would cold-start every contact in the world
                    // on the tick a body spawns, which is exactly the tick that can least afford it.
                    Boxes = boxes
                    Grid = grid }

            struct (Array.init k (fun i -> baseIndex + i), grown)

    let addBody
        (kind: BodyKind)
        (shape: Shape)
        (material: Material)
        (position: Point)
        (world: World)
        : struct (int * World) =
        // The single-body build path, defined through the batch one so the two can never drift. Its cost is
        // O(body count) — one array copy — exactly as the `.fsi` documents; reach for `addBodies` when many
        // bodies arrive at once and that per-body copy would square.
        let struct (indices, grown) = addBodies [ (kind, shape, material, position) ] world
        struct (indices.[0], grown)

    let pairs (world: World) : struct (int * int)[] =
        let n = world.Pos.Length

        // Read the broad-phase index off the `World` rather than rebuild a grid per call: `Boxes`/`Grid` are
        // cross-tick state kept current by `empty`/`addBody`/`addBodies` and refreshed by `step`, and they
        // are current for THIS world's poses by construction — nothing mutates a `World` in place, so the
        // grid a world carries always matches the bodies it carries.
        let boxes = world.Boxes
        let grid = world.Grid

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

    // The speculative-contact sentinel. A speculative contact (fixed-cost CCD, generated inside `step` for a
    // fast mover about to tunnel) has no touching FEATURE — the pair has not met yet — so it keys the
    // warm-start cache under one reserved id, disjoint from every discrete id: `polygonManifold`'s are
    // non-negative, the circle cases' are `-1` and `<= -2`, and `Int32.MinValue` is none of these. A pair
    // holds at most one speculative point, so the pair key `(A, B)` already separates one pair's from another's.
    let private featureSpeculative = System.Int32.MinValue

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

            // 3a. Speculative contacts — fixed-cost CCD. A body that moves more than its own thinnest
            //     cross-section in one step can pass clean through a thin wall between this step's position
            //     and the next, and the discrete broad phase — which reads only the two endpoint positions
            //     — never sees the crossing. Rather than SUBSTEP (which multiplies cost and, worse, changes
            //     `dt`, so a recorded input stream re-simulated at a different step count diverges — R6),
            //     generate a contact for the impending impact and let the solver refuse to let the pair
            //     close through it. `dt` never changes; the cost is one segment cast per fast mover per
            //     candidate, paid ONLY when a fast mover exists.
            //
            //     Gated wholly on `hasFast`, and INERT when no body is fast: a scene with no fast-movers
            //     stages exactly the discrete manifolds and steps BYTE-IDENTICALLY to one built before this
            //     slice, so #75/#76's golden checksums do not move. A fast mover with nothing in its path
            //     adds nothing either — free flight is untouched.
            //
            //     Scope: the mover is a CIRCLE — the projectile shape (a bullet, a ball). A fast *polygon*
            //     mover is not swept: linear polygon CCD is a heavier follow-up, and continuous *rotational*
            //     CCD is fenced out by #46's out-list. The target may be any collidable shape.
            //
            //     Corner caveat: against a polygon target the mover's CENTRE is cast at the BARE face (no
            //     Minkowski inflation of the polygon's corners), so a fast circle that clips a corner WITHOUT
            //     its centre reaching the target registers no speculative hit and can tunnel that corner. The
            //     discrete phase recovers next tick once real penetration exists; documented as a scope limit
            //     on `step` in the `.fsi`.

            // A speculative contact between fast circle mover `m` (radius `rm`) and target `t`, or
            // `ValueNone` when the mover's swept centre does not reach the target this step, or the two
            // already touch. `Depth` is carried NEGATIVE — the magnitude of the gap the pair has yet to
            // close — which the staging loop reads to bias the solver toward closing exactly that gap and no
            // more, so the mover stops AT the surface rather than through it. The public `manifold` never
            // produces this; the negative-`Depth` encoding lives only inside `step`.
            let speculativeContact (m: int) (t: int) (rm: float) : Manifold voption =
                let c0 = pos.[m]
                // Cast along the RELATIVE motion, so a target moving out of the way is not struck and one
                // moving in is. For a static target (the wall the acceptance names) this is the mover's own
                // displacement.
                let c1 = vadd c0 (vscale dt (vsub vel.[m] vel.[t]))

                let hit =
                    match world.Shapes.[t] with
                    // The circle target is inflated by the mover's radius (a Minkowski sum), so the cast of
                    // the mover's CENTRE reports where the two SURFACES meet.
                    | SCircle rt -> Geometry.segmentCircleHit c0 c1 { Center = pos.[t]; Radius = rm + rt }
                    | _ -> Geometry.segmentPolygonHit c0 c1 (worldPolygon working t)

                match hit with
                | None -> ValueNone
                | Some h ->
                    // `h.Normal` is the target's outward unit normal at the impact face — it runs t -> m.
                    let nOut = h.Normal
                    // Perpendicular distance from the mover's centre to the impact plane, NOW. For a polygon
                    // the cast was against the bare face, so the mover's own radius is still to subtract; for
                    // the circle the cast already inflated the target by `rm`, so this is the surface gap.
                    let faceGap = vdot nOut (vsub c0 h.Point)

                    let s =
                        match world.Shapes.[t] with
                        | SCircle _ -> faceGap
                        | _ -> faceGap - rm

                    // A NaN gap is no contact. Otherwise the gap clamps to zero at or past the surface: a
                    // ZERO-gap speculative contact biases the solver to hold the pair AT the surface (vn ->
                    // 0) rather than let it drive through. That is what stops a mover fast enough to reach
                    // the wall THIS tick from punching through on the NEXT — the tick where it has just
                    // touched, so the discrete phase still reads no contact (a touch is not a contact) and
                    // would otherwise leave the approach unclamped. The discrete phase takes over the moment
                    // real penetration exists.
                    if System.Double.IsNaN s then
                        ValueNone
                    else
                        let gap = max s 0.0
                        // Contact point on the mover's surface facing the target, in the CURRENT frame, so
                        // its lever arm on the mover is the true `rm` rather than the swept distance.
                        let point = vsub c0 (vscale rm nOut)
                        let a = min m t
                        let b = max m t
                        // Orient the normal a -> b, the convention the solver shares with every discrete
                        // manifold. `nOut` runs t -> m, so it already IS a -> b when the mover is the higher
                        // index, and flips when it is the lower.
                        let normal = if m > t then nOut else vscale -1.0 nOut

                        ValueSome
                            { A = a
                              B = b
                              Normal = normal
                              Depth = -gap
                              Points = [| point |]
                              PointCount = 1
                              FeatureId = featureSpeculative }

            let fast = Array.zeroCreate<bool> n
            let mutable hasFast = false

            for i in 0 .. n - 1 do
                // A sleeping body does not move, and a `Static` one never does; only a mover can tunnel.
                if world.Kinds.[i] <> Static && not asleep.[i] then
                    match minHalfExtent world.Shapes.[i] with
                    | ValueSome ext ->
                        let dx = dt * vel.[i].X
                        let dy = dt * vel.[i].Y
                        // Squared magnitudes, and `>` not `>=`: a body moving EXACTLY its own thinness is
                        // still caught by the discrete phase next tick, and NaN fails `>`, so a body whose
                        // velocity has gone non-finite is never treated as fast. A body at rest never is,
                        // which is what keeps a settled scene inert.
                        if dx * dx + dy * dy > ext * ext then
                            fast.[i] <- true
                            hasFast <- true
                    | ValueNone -> ()

            let speculative =
                if not hasFast then
                    [||]
                else
                    // Share the broad-phase index rather than rebuild it (#94). The CCD sweep runs on the
                    // same OPENING poses the discrete broad phase did — position is not integrated until step
                    // 6, below — so `working.Boxes`/`working.Grid`, carried from the incoming world whose poses
                    // `working` opens with, are precisely the boxes and grid a rebuild here would produce. Only
                    // the QUERY region differs — a swept box, not a body's own — and the bucketed grid is one.
                    let boxes = working.Boxes
                    let grid = working.Grid

                    let acc = ResizeArray<Manifold>()
                    let seen = System.Collections.Generic.HashSet<struct (int * int)>()

                    for m in 0 .. n - 1 do
                        if fast.[m] then
                            match world.Shapes.[m], boxes.[m] with
                            | SCircle rm, ValueSome bm ->
                                // The query region is the mover's box swept along its OWN displacement this
                                // step. Targets sit at their current positions, so growing the box by the
                                // mover's motion covers everything it could pass through.
                                let d = vscale dt vel.[m]

                                let swept =
                                    { X = min bm.X (bm.X + d.X)
                                      Y = min bm.Y (bm.Y + d.Y)
                                      Width = bm.Width + abs d.X
                                      Height = bm.Height + abs d.Y }

                                for t in SpatialGrid.queryBounds swept grid do
                                    // One speculative contact per unordered pair — both bodies may be fast
                                    // and query each other — and only where `collidable` and the discrete
                                    // narrow phase found NO contact: an overlapping pair is the discrete
                                    // solver's, and a pair can never be both at once.
                                    if t <> m then
                                        let a = min m t
                                        let b = max m t

                                        if
                                            seen.Add(struct (a, b))
                                            && collidable working t
                                            && (manifold working a b).IsNone
                                        then
                                            match speculativeContact m t rm with
                                            | ValueSome sm -> acc.Add sm
                                            | ValueNone -> ()
                            | _ -> ()

                    acc.ToArray()

            // Discrete manifolds first, then the speculative ones; the sort below canonicalises the union by
            // `(A, B, FeatureId)`, so their append order does not matter. When nothing is speculative this is
            // exactly `found.ToArray()` — same array of values the pre-CCD step produced.
            let manifolds =
                if speculative.Length = 0 then
                    found.ToArray()
                else
                    Array.append (found.ToArray()) speculative

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

                    // A speculative contact carries a NEGATIVE `Depth`: the gap `-Depth` the pair has yet to
                    // close. Its bias is `Depth/dt`, a negative target normal velocity — the fastest the pair
                    // may approach and still only just touch this step. The accumulated-impulse clamp below
                    // then does nothing while they separate or close slowly (a negative `jn` on a zero
                    // accumulator), and pushes back only when they would overshoot the surface, which is what
                    // stops the tunnel. Restitution is DEFERRED to the tick they actually meet, where the
                    // discrete contact applies it: a bounce off a surface not yet reached is unphysical. A
                    // discrete contact (`Depth >= 0`) keeps the restitution bias unchanged.
                    let bias =
                        if m.Depth < 0.0 then m.Depth / dt
                        elif -vn > bounceThreshold then -e * vn
                        else 0.0

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
            //
            // 10. Refresh the broad-phase index (#94) from the poses this step integrated and corrected, so
            //     the world handed back carries a grid current for its OWN positions — the invariant every
            //     `World` holds, and what lets the next `pairs`/`step` read the grid instead of rebuilding it.
            //     `pos`/`rot` are `working`'s arrays, now final; `Shapes` never changed. Built from the same
            //     inputs the next step's own broad phase would have used, so the pair set it feeds is
            //     bit-identical — the grid simply moved from the start of that step to the end of this one.
            let boxes, grid = broadPhase cfg pos world.Shapes rot

            { working with
                Asleep = asleep
                SleepCounter = sleepCounter
                CacheA = cA
                CacheB = cB
                CacheFeature = cF
                CachePoint = cP
                CacheN = cAccN
                CacheT = cAccT
                Boxes = boxes
                Grid = grid }

    // ---------------------------------------------------------------------------------------------
    // Presentation interpolation
    // ---------------------------------------------------------------------------------------------

    // Shortest-arc angular blend, factored out so the one rule everyone forgets can be asserted directly
    // (it is `internal`, reached through InternalsVisibleTo, not part of the public surface). `Math.Round`
    // (nearest, ties-to-even) folds the raw delta into (-pi, pi], so blending previous = +3.13 to
    // current = -3.13 crosses +pi rather than unwinding the long way through 0. Endpoints are exact: `t = 0`
    // returns `a0`, and `t = 1` is special-cased to `a1` so a wrapped pair still lands bit-for-bit on
    // `current`'s angle rather than a full turn off it.
    let internal lerpAngleShortest (a0: float) (a1: float) (t: float) : float =
        if t <= 0.0 then a0
        elif t >= 1.0 then a1
        else
            let twoPi = 2.0 * System.Math.PI
            let delta = a1 - a0
            let shortest = delta - twoPi * System.Math.Round(delta / twoPi)
            a0 + shortest * t

    let interpolate (alpha: float) (previous: World) (current: World) : Transform[] =
        // `alpha` is Loop.alpha's blend factor and is read HERE, at the presentation boundary, and nowhere
        // near `step`. Clamp to [0, 1]: a non-finite or out-of-range alpha must neither throw nor
        // extrapolate past an endpoint. (NaN loses every comparison, so guard it explicitly, else it would
        // fall through to the raw value.)
        let a =
            if System.Double.IsNaN alpha then 0.0
            else max 0.0 (min 1.0 alpha)

        // Linear blend with exact endpoints, so `interpolate 0 = previous` and `interpolate 1 = current`
        // hold with no float drift from `v0 + (v1 - v0) * 1.0`.
        let lerp (v0: float) (v1: float) =
            if a <= 0.0 then v0
            elif a >= 1.0 then v1
            else v0 + (v1 - v0) * a

        // One Transform per body of `current`, in index order — the set a renderer draws. A body present in
        // `current` but not `previous` (added since the previous buffer was taken) has no prior pose to
        // blend from, so it is shown at its current transform.
        let prior = previous.Pos.Length

        Array.init current.Pos.Length (fun i ->
            if i < prior then
                { Position =
                    { X = lerp previous.Pos.[i].X current.Pos.[i].X
                      Y = lerp previous.Pos.[i].Y current.Pos.[i].Y }
                  Rotation = lerpAngleShortest previous.Rot.[i] current.Rot.[i] a }
            else
                { Position = current.Pos.[i]
                  Rotation = current.Rot.[i] })

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
