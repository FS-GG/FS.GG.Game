module Game.Core.Tests.BallisticsTests

// Ballistics: swept projectile advance, lead/intercept solve, splash with a caller-chosen falloff.
// The headline property is NO TUNNELING — a round is a segment, never a point — so it is asserted
// against the naive post-integration point test that every hand-rolled loop reaches for first.
// Determinism is asserted the only way that means anything: a golden replay compared byte for byte.

open Expecto
open FsCheck
open FS.GG.Game.Core

let private p x y : Point = { X = x; Y = y }

/// A thin vertical wall spanning x in [10, 11], y in [-50, 50]. Thinner than one step of a fast round.
let private wall: Rect =
    { X = 10.0
      Y = -50.0
      Width = 1.0
      Height = 100.0 }

let private wallCast (p0: Point) (p1: Point) = Geometry.segmentAabbHit p0 p1 wall

/// No occluders anywhere.
let private noCast (_: Point) (_: Point) : RayHit option = None

let private round pos vel ticks =
    { Position = pos
      Velocity = vel
      TicksRemaining = ticks }

/// Clamp an arbitrary float into a sane simulation range (FsCheck hands out NaN/infinity/1e300).
let private clamp (v: float) =
    if System.Double.IsFinite v then max -1.0e6 (min 1.0e6 v) else 0.0

[<Tests>]
let tests =
    testList "Game.Core Ballistics (US-40, FR-040..FR-044)" [

        // -----------------------------------------------------------------------------------------
        // step — the swept cast. The reason this module exists.
        // -----------------------------------------------------------------------------------------

        test "a fast round hits a thin wall it crosses in one step — where a point test misses it" {
            // 1200 units/s at 1/60 s covers 20 units; the wall is 1 unit thick at x = 10.
            let r = round (p 0.0 0.0) (p 1200.0 0.0) 120
            let dt = 1.0 / 60.0
            let after = p (1200.0 * dt) 0.0

            // The bug this module exists to prevent: the naive post-integration point test.
            Expect.isFalse (Geometry.containsPoint wall after) "the naive point test lands PAST the wall — it tunnels"

            match Ballistics.step wallCast dt r with
            | Struck hit -> Expect.floatClose Accuracy.high hit.Point.X 10.0 "impact on the wall's near face"
            | other -> failtestf "expected Struck, got %A" other
        }

        testCase "no tunneling: any round whose step segment crosses the wall Strucks it (FsCheck >=500)"
        <| fun () ->
            let prop (speed: float) (dtRaw: float) =
                let speed = abs (clamp speed)
                let dt = abs (clamp dtRaw)

                if speed <= 0.0 || dt <= 0.0 then
                    true // vacuous: no motion, nothing to cross
                else
                    let r = round (p 0.0 0.0) (p speed 0.0) 1
                    let reach = speed * dt
                    // The segment (0,0) -> (reach,0) crosses the wall exactly when it reaches x = 10.
                    let crosses = reach >= 10.0

                    match Ballistics.step wallCast dt r with
                    | Struck _ -> crosses
                    | Flew _ -> not crosses
                    | Expired -> false

            Check.One(Config.QuickThrowOnFailure.WithMaxTest 500, prop)

        testCase "the swept path agrees exactly with segmentAabbHit — same hit, not merely the same verdict"
        <| fun () ->
            let prop (sx: float) (sy: float) (vx: float) (vy: float) (dtRaw: float) =
                let p0 = p (clamp sx) (clamp sy)
                let v = p (clamp vx) (clamp vy)
                let dt = abs (clamp dtRaw)
                let r = round p0 v 1

                let p1 = p (p0.X + v.X * dt) (p0.Y + v.Y * dt)
                let expected = Geometry.segmentAabbHit p0 p1 wall

                match Ballistics.step wallCast dt r, expected with
                | Struck hit, Some e -> hit = e
                | Flew _, None -> true
                | Expired, _ -> false
                | _ -> false

            Check.One(Config.QuickThrowOnFailure.WithMaxTest 500, prop)

        test "lifetime is checked before motion — an exhausted round gets no free final step" {
            Expect.equal (Ballistics.step noCast 1.0 (round (p 0.0 0.0) (p 5.0 0.0) 0)) Expired "zero ticks is Expired"
            Expect.equal (Ballistics.step noCast 1.0 (round (p 0.0 0.0) (p 5.0 0.0) -3)) Expired "negative ticks too"
        }

        test "a surviving round advances by velocity*dt and burns exactly one tick" {
            match Ballistics.step noCast 0.5 (round (p 1.0 2.0) (p 4.0 -2.0) 3) with
            | Flew r ->
                Expect.equal r.Position (p 3.0 1.0) "position integrated once"
                Expect.equal r.TicksRemaining 2 "one tick consumed"
            | other -> failtestf "expected Flew, got %A" other
        }

        test "totality: a non-finite dt moves the round nowhere rather than poisoning it with NaN" {
            for bad in [ nan; infinity; -infinity; -1.0 ] do
                match Ballistics.step noCast bad (round (p 1.0 2.0) (p 4.0 4.0) 2) with
                | Flew r ->
                    Expect.equal r.Position (p 1.0 2.0) "no motion"
                    Expect.equal r.TicksRemaining 1 "the tick is still consumed"
                | other -> failtestf "expected Flew for dt=%f, got %A" bad other
        }

        test "totality: a round that is already non-finite is Expired, so a NaN never escapes" {
            Expect.equal (Ballistics.step noCast 1.0 (round (p nan 0.0) (p 1.0 0.0) 5)) Expired "NaN position"
            Expect.equal (Ballistics.step noCast 1.0 (round (p 0.0 0.0) (p infinity 0.0) 5)) Expired "infinite velocity"
        }

        test "a hostile cast cannot inject a hit — an out-of-range or non-finite T is a miss" {
            let bogus t =
                fun (_: Point) (_: Point) -> Some { T = t; Point = p 0.0 0.0; Normal = p 1.0 0.0 }

            for t in [ nan; 2.0; -0.5; infinity ] do
                match Ballistics.step (bogus t) 1.0 (round (p 0.0 0.0) (p 1.0 0.0) 2) with
                | Flew _ -> ()
                | other -> failtestf "expected the T=%f hit to be rejected as a miss, got %A" t other

            let nanPoint =
                fun (_: Point) (_: Point) -> Some { T = 0.5; Point = p nan 0.0; Normal = p 1.0 0.0 }

            match Ballistics.step nanPoint 1.0 (round (p 0.0 0.0) (p 1.0 0.0) 2) with
            | Flew _ -> ()
            | other -> failtestf "expected a NaN hit point to be rejected, got %A" other

            // A NaN NORMAL is just as poisonous: the caller reflects velocity about it.
            let nanNormal =
                fun (_: Point) (_: Point) -> Some { T = 0.5; Point = p 0.5 0.0; Normal = p nan nan }

            match Ballistics.step nanNormal 1.0 (round (p 0.0 0.0) (p 1.0 0.0) 2) with
            | Flew _ -> ()
            | other -> failtestf "expected a NaN hit normal to be rejected, got %A" other
        }

        // -----------------------------------------------------------------------------------------
        // intercept — the lead solve. Degenerate cases return ValueNone, never NaN.
        // -----------------------------------------------------------------------------------------

        test "a stationary target is intercepted where it stands" {
            Expect.equal (Ballistics.intercept (p 0.0 0.0) 10.0 (p 3.0 4.0) (p 0.0 0.0)) (ValueSome(p 3.0 4.0)) "aim at it"
        }

        test "a crossing target is led — the round and the target arrive together" {
            // Target at (10,0) moving +y at 10; round speed 10*sqrt 2 makes t = 1 exact.
            match Ballistics.intercept (p 0.0 0.0) (10.0 * sqrt 2.0) (p 10.0 0.0) (p 0.0 10.0) with
            | ValueSome aim ->
                Expect.floatClose Accuracy.medium aim.X 10.0 "lead point x"
                Expect.floatClose Accuracy.medium aim.Y 10.0 "lead one second of target motion"
            | ValueNone -> failtest "a reachable crossing target must have an interception"
        }

        test "equal speeds collapse the quadratic to a linear solve rather than dividing by zero" {
            // a = |v|^2 - speed^2 = 0 exactly. Closing head-on: t = -c/b = 100/20 = 5.
            match Ballistics.intercept (p 0.0 0.0) 1.0 (p 10.0 0.0) (p -1.0 0.0) with
            | ValueSome aim -> Expect.floatClose Accuracy.medium aim.X 5.0 "met halfway at t=5"
            | ValueNone -> failtest "the linear branch must solve, not divide by zero"
        }

        test "a distant target closing slowly is still intercepted — the b-tolerance is a speed*length" {
            // Equal speeds (the linear branch). |d| = 1e6, closing rate 2e-4 => |b| = 200. Scaling the
            // zero-test by c = |d|^2 = 1e12 would call that "no closing rate" and report ValueNone.
            let vx = -1.0e-4
            let v = p vx (sqrt (1.0 - vx * vx)) // |v| = 1.0 exactly, so a = 0

            match Ballistics.intercept (p 0.0 0.0) 1.0 (p 1.0e6 0.0) v with
            | ValueSome _ -> ()
            | ValueNone -> failtest "a real (if distant) interception was reported impossible"
        }

        test "an outrunning target has no interception — ValueNone, not NaN" {
            Expect.equal (Ballistics.intercept (p 0.0 0.0) 1.0 (p 10.0 0.0) (p 10.0 0.0)) ValueNone "faster and fleeing"
        }

        test "a degenerate speed has no interception" {
            for bad in [ 0.0; -5.0; nan; infinity ] do
                Expect.equal (Ballistics.intercept (p 0.0 0.0) bad (p 3.0 0.0) (p 0.0 0.0)) ValueNone (sprintf "speed=%f" bad)
        }

        testCase "intercept never returns NaN, and every solution satisfies the defining equation (FsCheck >=500)"
        <| fun () ->
            let prop (sx: float) (sy: float) (tx: float) (ty: float) (vx: float) (vy: float) (spRaw: float) =
                let shooter = p (clamp sx) (clamp sy)
                let target = p (clamp tx) (clamp ty)
                let v = p (clamp vx) (clamp vy)
                let speed = abs (clamp spRaw)

                match Ballistics.intercept shooter speed target v with
                | ValueNone -> true
                | ValueSome aim ->
                    // Never a NaN — the whole point of the ValueNone contract.
                    if not (System.Double.IsFinite aim.X && System.Double.IsFinite aim.Y) then
                        false
                    else
                        let vSq = v.X * v.X + v.Y * v.Y
                        let flight = sqrt ((aim.X - shooter.X) ** 2.0 + (aim.Y - shooter.Y) ** 2.0)

                        if vSq = 0.0 then
                            // A stationary target pins t nowhere — aim = target is the whole claim.
                            aim = target
                        else
                            // Recover t from aim = target + v*t, then check |aim - shooter| = speed*t.
                            let t =
                                ((aim.X - target.X) * v.X + (aim.Y - target.Y) * v.Y) / vSq

                            let want = speed * t
                            let scale = max 1.0 (max (abs flight) (abs want))
                            t >= -1e-6 && abs (flight - want) <= 1e-5 * scale

            Check.One(Config.QuickThrowOnFailure.WithMaxTest 500, prop)

        testCase "when intercept says ValueNone, a brute-force sampler finds no interception either"
        <| fun () ->
            let prop (tx: float) (ty: float) (vx: float) (vy: float) (spRaw: float) =
                let shooter = p 0.0 0.0
                let target = p (clamp tx % 100.0) (clamp ty % 100.0)
                let v = p (clamp vx % 50.0) (clamp vy % 50.0)
                let speed = abs (clamp spRaw % 50.0)

                match Ballistics.intercept shooter speed target v with
                | ValueSome _ -> true
                | ValueNone ->
                    // Scan t for a root of |target + v*t - shooter| - speed*t. None may exist.
                    let residual t =
                        let x = target.X + v.X * t - shooter.X
                        let y = target.Y + v.Y * t - shooter.Y
                        sqrt (x * x + y * y) - speed * t

                    // A sign change over the scan implies a root the solver should have found.
                    let ts = [ 0.0 .. 0.01 .. 20.0 ]

                    ts
                    |> List.pairwise
                    |> List.forall (fun (a, b) ->
                        let ra, rb = residual a, residual b
                        not (System.Double.IsFinite ra && System.Double.IsFinite rb && ra * rb < 0.0))

            Check.One(Config.QuickThrowOnFailure.WithMaxTest 500, prop)

        // -----------------------------------------------------------------------------------------
        // splash — falloff curves and order independence.
        // -----------------------------------------------------------------------------------------

        test "linearFalloff runs from 1.0 at the centre to edgeScale at the rim" {
            let f = Ballistics.linearFalloff 0.5
            Expect.floatClose Accuracy.high (f 0.0) 1.0 "centre is full damage"
            Expect.floatClose Accuracy.high (f 0.5) 0.75 "halfway"
            Expect.floatClose Accuracy.high (f 1.0) 0.5 "rim is edgeScale"
        }

        test "falloff curves are total: a non-finite distance or parameter degrades, never throws" {
            Expect.equal (Ballistics.linearFalloff 0.5 nan) 0.0 "NaN distance"
            Expect.equal (Ballistics.linearFalloff nan 1.0) 0.0 "NaN edgeScale degrades to 0 at the rim"
            Expect.floatClose Accuracy.high (Ballistics.linearFalloff 5.0 1.0) 1.0 "edgeScale clamped to [0,1]"
            Expect.equal (Ballistics.inverseSquareFalloff 3.0 nan) 0.0 "NaN distance"
            Expect.floatClose Accuracy.high (Ballistics.inverseSquareFalloff nan 1.0) 1.0 "NaN k is a flat curve"
        }

        test "inverseSquareFalloff concentrates damage at the centre" {
            let f = Ballistics.inverseSquareFalloff 3.0
            Expect.floatClose Accuracy.high (f 0.0) 1.0 "centre"
            Expect.floatClose Accuracy.high (f 1.0) 0.25 "1/(1+3) at the rim"
        }

        test "splash pairs every item in radius with its falloff multiplier, in insertion order" {
            let items = [ p 0.0 0.0, "centre"; p 10.0 0.0, "rim"; p 100.0 0.0, "far" ]
            let grid = SpatialGrid.build 4.0 (items |> List.map (fun (pt, n) -> pt, (pt, n)))
            let hits = Ballistics.splash (p 0.0 0.0) 10.0 (Ballistics.linearFalloff 0.5) fst grid

            Expect.equal
                (hits |> List.map (fun ((_, name), m) -> name, m))
                [ "centre", 1.0; "rim", 0.5 ]
                "the far item is outside the radius; the rim takes edgeScale"
        }

        test "a non-positive or non-finite radius splashes nothing" {
            let grid = SpatialGrid.build 4.0 [ p 0.0 0.0, (p 0.0 0.0, "a") ]

            for bad in [ 0.0; -1.0; nan; infinity ] do
                Expect.isEmpty (Ballistics.splash (p 0.0 0.0) bad (Ballistics.linearFalloff 0.5) fst grid) (sprintf "radius=%f" bad)
        }

        test "a hostile falloff curve cannot inject a NaN into a damage total" {
            let grid = SpatialGrid.build 4.0 [ p 1.0 0.0, (p 1.0 0.0, "a") ]
            let hits = Ballistics.splash (p 0.0 0.0) 10.0 (fun _ -> nan) fst grid
            Expect.equal (hits |> List.map snd) [ 0.0 ] "NaN multiplier degrades to 0"
        }

        testCase "the splash SET is independent of SpatialGrid insertion order (FsCheck >=500)"
        <| fun () ->
            let prop (coords: (float * float) list) (radiusRaw: float) =
                let pts = coords |> List.map (fun (x, y) -> p (clamp x % 50.0) (clamp y % 50.0))
                let radius = abs (clamp radiusRaw % 50.0) + 1.0
                let falloff = Ballistics.linearFalloff 0.5

                let splashOf (ordered: Point list) =
                    let grid = SpatialGrid.build 4.0 (ordered |> List.map (fun pt -> pt, pt))
                    Ballistics.splash (p 0.0 0.0) radius falloff id grid |> List.sort

                splashOf pts = splashOf (List.rev pts)

            Check.One(Config.QuickThrowOnFailure.WithMaxTest 500, prop)

        // -----------------------------------------------------------------------------------------
        // Determinism — the golden replay. dt and velocity are negative powers of two, so the
        // trajectory is exact binary arithmetic and the expected values are literal, not approximate.
        // -----------------------------------------------------------------------------------------

        test "a scripted dt sequence reproduces a byte-identical trajectory (golden)" {
            let dt = 1.0 / 64.0
            let start = round (p 0.0 0.0) (p 128.0 64.0) 5

            let trajectory =
                List.replicate 6 dt
                |> List.scan
                    (fun state d ->
                        match state with
                        | Flew r -> Ballistics.step noCast d r
                        | terminal -> terminal)
                    (Flew start)

            let positions =
                trajectory
                |> List.choose (function
                    | Flew r -> Some r.Position
                    | _ -> None)

            let expected =
                [ p 0.0 0.0; p 2.0 1.0; p 4.0 2.0; p 6.0 3.0; p 8.0 4.0; p 10.0 5.0 ]

            Expect.equal positions expected "exact binary trajectory, replayable byte for byte"
            Expect.equal (List.last trajectory) Expired "the round expires on the sixth step"
        }

        test "the golden trajectory is reproduced identically on a second run" {
            let replay () =
                let dt = 1.0 / 64.0

                List.fold
                    (fun state _ ->
                        match state with
                        | Flew r -> Ballistics.step wallCast dt r
                        | terminal -> terminal)
                    // 130/64 = 2.03125 exactly: the 5th step spans x 8.125 -> 10.15625, crossing the
                    // wall's near face strictly inside the segment rather than landing on it.
                    (Flew(round (p 0.0 0.0) (p 130.0 0.0) 100))
                    [ 1..20 ]

            Expect.equal (replay ()) (replay ()) "identical input, identical output"

            match replay () with
            | Struck hit -> Expect.floatClose Accuracy.high hit.Point.X 10.0 "the round stops at the wall"
            | other -> failtestf "expected the round to reach the wall, got %A" other
        }
    ]
