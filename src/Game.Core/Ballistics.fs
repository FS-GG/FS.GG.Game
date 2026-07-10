namespace FS.GG.Game.Core

type Projectile =
    { Position: Point
      Velocity: Point
      TicksRemaining: int }

type ProjectileStep =
    | Flew of Projectile
    | Struck of RayHit
    | Expired

/// See Ballistics.fsi for the contract. Pure, total, deterministic. The swept cast in `step` is the
/// whole point of the module: a point test on the post-integration position tunnels through any
/// occluder thinner than `|Velocity| * dt`, and no timestep is small enough to fix that in general.
[<RequireQualifiedAccess>]
module Ballistics =

    let inline private isFinite (v: float) = System.Double.IsFinite v

    let private finitePoint (p: Point) = isFinite p.X && isFinite p.Y

    let step
        (cast: Point -> Point -> RayHit option)
        (dt: float)
        (projectile: Projectile)
        : ProjectileStep =
        // Lifetime before motion: an exhausted round never gets a free final step.
        if projectile.TicksRemaining <= 0 then
            Expired
        elif not (finitePoint projectile.Position && finitePoint projectile.Velocity) then
            Expired
        else
            // A non-finite or non-positive dt contributes no motion rather than a NaN position.
            let d = if isFinite dt && dt > 0.0 then dt else 0.0
            let p0 = projectile.Position

            let p1 =
                { X = p0.X + projectile.Velocity.X * d
                  Y = p0.Y + projectile.Velocity.Y * d }

            if not (finitePoint p1) then
                // Finite position and velocity can still overflow to infinity at absurd dt.
                Expired
            else
                match cast p0 p1 with
                // A hostile or buggy `cast` cannot inject a NaN: an out-of-range or non-finite hit
                // is a miss, not a hit at a garbage point. The NORMAL is checked too — a caller that
                // reflects velocity about a NaN normal poisons the world just as thoroughly as one
                // that reads a NaN impact point.
                | Some hit when
                    isFinite hit.T
                    && hit.T >= 0.0
                    && hit.T <= 1.0
                    && finitePoint hit.Point
                    && finitePoint hit.Normal
                    ->
                    Struck hit
                | _ ->
                    Flew
                        { projectile with
                            Position = p1
                            TicksRemaining = projectile.TicksRemaining - 1 }

    let intercept (shooter: Point) (speed: float) (target: Point) (targetVelocity: Point) : Point voption =
        if not (isFinite speed && speed > 0.0) then
            ValueNone
        elif not (finitePoint shooter && finitePoint target && finitePoint targetVelocity) then
            ValueNone
        else
            // Solve |(target - shooter) + targetVelocity*t| = speed*t for the earliest t >= 0.
            let dx = target.X - shooter.X
            let dy = target.Y - shooter.Y
            let vx = targetVelocity.X
            let vy = targetVelocity.Y

            let a = vx * vx + vy * vy - speed * speed
            let b = 2.0 * (dx * vx + dy * vy)
            let c = dx * dx + dy * dy

            // Scaled tolerances: an absolute epsilon would be meaningless at speed ~1e3 (a ~ 1e6).
            // Each is scaled by the magnitude of the term it guards -- `a` is a squared SPEED, and
            // `b` is 2*|d|*|v|, a length times a speed. Scaling `b` by `c` (a squared LENGTH) would
            // be dimensionally wrong and, at long range, would swallow a real closing rate: a target
            // 1e6 away closing at 2e-4 gives |b| = 200 against a `c`-derived tolerance of 1e3, and
            // the interception would be reported as impossible. Pure arithmetic on the inputs, so
            // the branch taken is itself deterministic.
            let aIsZero = abs a <= 1e-9 * max 1.0 (speed * speed)
            let bIsZero = abs b <= 1e-9 * max 1.0 (sqrt c * speed)

            let t =
                if aIsZero then
                    // Target speed equals projectile speed: the quadratic collapses to b*t + c = 0.
                    if bIsZero then
                        // Closing rate is zero too: only a target already on the muzzle is reachable.
                        if c = 0.0 then ValueSome 0.0 else ValueNone
                    else
                        let t = -c / b
                        if t >= 0.0 then ValueSome t else ValueNone
                else
                    let disc = b * b - 4.0 * a * c

                    if disc < 0.0 then
                        ValueNone // outrun: no real interception
                    else
                        let s = sqrt disc
                        let t1 = (-b - s) / (2.0 * a)
                        let t2 = (-b + s) / (2.0 * a)
                        let lo = min t1 t2
                        let hi = max t1 t2

                        // Earliest non-negative root. When the round is faster than the target (a < 0)
                        // the roots straddle zero, so exactly one qualifies.
                        if lo >= 0.0 then ValueSome lo
                        elif hi >= 0.0 then ValueSome hi
                        else ValueNone

            match t with
            | ValueSome t when isFinite t ->
                let p = { X = target.X + vx * t; Y = target.Y + vy * t }
                if finitePoint p then ValueSome p else ValueNone
            | _ -> ValueNone

    let linearFalloff (edgeScale: float) : float -> float =
        let e = if isFinite edgeScale then max 0.0 (min 1.0 edgeScale) else 0.0

        fun d ->
            if not (isFinite d) then
                0.0
            else
                let dd = max 0.0 (min 1.0 d)
                1.0 + (e - 1.0) * dd

    let inverseSquareFalloff (k: float) : float -> float =
        let kk = if isFinite k && k > 0.0 then k else 0.0

        fun d ->
            if not (isFinite d) then
                0.0
            else
                let dd = max 0.0 (min 1.0 d)
                1.0 / (1.0 + kk * dd * dd)

    let splash
        (centre: Point)
        (radius: float)
        (falloff: float -> float)
        (position: 'T -> Point)
        (grid: SpatialGrid<'T>)
        : ('T * float) list =
        if not (isFinite radius && radius > 0.0) || not (finitePoint centre) then
            []
        else
            grid
            |> SpatialGrid.queryRadius centre radius
            |> List.choose (fun item ->
                let p = position item

                if not (finitePoint p) then
                    None
                else
                    // Squared distance, matching `SpatialGrid.queryRadius`'s own membership test
                    // exactly. Re-deciding membership with `sqrt dSq <= radius` could disagree with
                    // the query at the rim by one ulp and drop an item the grid returned.
                    let dx = p.X - centre.X
                    let dy = p.Y - centre.Y
                    let dSq = dx * dx + dy * dy
                    let m = falloff (sqrt dSq / radius)
                    Some(item, (if isFinite m then m else 0.0)))
