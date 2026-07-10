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
    // This slice fills only what the broad phase reads. `step` adds `Vel`/`Rot`/`AngVel` and the solver
    // cache as further arrays, without disturbing this type's public face.
    type World =
        { Config: Config
          Kinds: BodyKind[]
          Shapes: Shape[]
          Materials: Material[]
          Pos: Point[] }

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

    // World-space AABB of body `i`, or `ValueNone` when it collides with nothing.
    let private aabbOf (world: World) (i: int) : Rect voption =
        let p = world.Pos.[i]

        if not (finitePoint p) then
            ValueNone
        else
            match localBounds world.Shapes.[i] with
            | ValueNone -> ValueNone
            | ValueSome(struct (loX, loY, hiX, hiY)) ->
                ValueSome
                    { X = p.X + loX
                      Y = p.Y + loY
                      Width = hiX - loX
                      Height = hiY - loY }

    // Only a `Dynamic` body has finite mass, so a pair without one can never resolve.
    let private solvable (a: BodyKind) (b: BodyKind) = a = Dynamic || b = Dynamic

    let empty (config: Config) : World =
        { Config = config
          Kinds = [||]
          Shapes = [||]
          Materials = [||]
          Pos = [||] }

    let addBody
        (kind: BodyKind)
        (shape: Shape)
        (material: Material)
        (position: Point)
        (world: World)
        : struct (int * World) =
        let index = world.Pos.Length

        let grown =
            { world with
                Kinds = Array.append world.Kinds [| kind |]
                Shapes = Array.append world.Shapes [| shape |]
                Materials = Array.append world.Materials [| material |]
                Pos = Array.append world.Pos [| position |] }

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
