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

        // Two DISTINCT claims, deliberately not fused into one property. Totality holds for arbitrary
        // input (that is what `ValueNone` is for); the defining equation is only *checkable* where the
        // check itself is numerically meaningful. Fusing them makes the harness, not the module, the
        // thing under test — a denormal `v` such as (0, 4.94e-324) has a `|v|^2` that underflows to
        // exactly 0.0, so any t recovered from `aim = target + v*t` is a fiction.

        testCase "intercept never returns a non-finite point, on unclamped input (FsCheck >=500)"
        <| fun () ->
            // Deliberately NOT clamped: NaN, infinity, and denormal inputs are exactly the cases the
            // .fsi promises to answer with ValueNone rather than a poisoned Point.
            let prop (sx: float) (sy: float) (tx: float) (ty: float) (vx: float) (vy: float) (speed: float) =
                match Ballistics.intercept (p sx sy) speed (p tx ty) (p vx vy) with
                | ValueNone -> true
                | ValueSome aim -> System.Double.IsFinite aim.X && System.Double.IsFinite aim.Y

            Check.One(Config.QuickThrowOnFailure.WithMaxTest 500, prop)

        testCase "every interception satisfies |aim - shooter| = speed * t (FsCheck >=500)"
        <| fun () ->
            // Velocities are either exactly zero or bounded away from the denormal floor, so that the
            // t recovered below is real arithmetic rather than a division by an underflowed |v|^2.
            let sane (v: float) =
                let c = clamp v
                if abs c < 1.0e-6 then 0.0 else c

            let prop (sx: float) (sy: float) (tx: float) (ty: float) (vx: float) (vy: float) (spRaw: float) =
                let shooter = p (clamp sx) (clamp sy)
                let target = p (clamp tx) (clamp ty)
                let v = p (sane vx) (sane vy)
                let speed = abs (clamp spRaw)

                match Ballistics.intercept shooter speed target v with
                | ValueNone -> true
                | ValueSome aim ->
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

                        // The tolerance tracks the magnitudes the subtraction CANCELS, not the size of
                        // the result (#89). `flight` is a difference of coordinates, and `t` is recovered
                        // from a difference of coordinates: at |shooter| ~ 1e6 both are ground down to a
                        // granularity set by the OPERANDS, while `flight` and `want` themselves can be
                        // ~1. Deriving `scale` from the result alone therefore demands an accuracy the
                        // inputs no longer carry — `shooter = (-1e6, 0.163...)`, `v = (-1e6, 0)`,
                        // `speed = 1` misses a 1e-5 absolute bound by 1.46e-5 while `intercept` is right
                        // to 2.7e-11 RELATIVE, which is the conditioning floor of a near-double root
                        // whose inputs are already rounded doubles. (Exact-rational check in #89: the
                        // error grows linearly with coordinate magnitude, 7.7e-15 at 1e2 -> 2.7e-11 at
                        // 1e6, and Kahan's FMA discriminant only buys ~8x of that. The solve is as
                        // accurate as the inputs allow; the bound was wrong.)
                        let scale =
                            [ 1.0
                              abs flight
                              abs want
                              abs shooter.X
                              abs shooter.Y
                              abs target.X
                              abs target.Y
                              abs (v.X * t)
                              abs (v.Y * t) ]
                            |> List.max

                        // 1e-5 is kept from the original bound. Over 300 seeds x 500 draws of this exact
                        // generator the worst observed |flight - want| / scale is 1.2e-7, so 1e-5 carries
                        // ~82x of headroom -- while a solve that is actually WRONG lands at a ratio of
                        // ~1 (an `aIsZero` misfire returns an aim for an interception that does not
                        // exist), five orders clear of this bound. Loose enough not to flake, tight
                        // enough to still be load-bearing.
                        t >= -1e-6 && abs (flight - want) <= 1e-5 * scale

            // FsCheck draws a fresh seed per run unless told otherwise, so an intermittently false
            // property is a coin flip in CI rather than a red build — 8 of 300 seeds (2.7%) falsify the
            // OLD bound above, which is the "one run in a dozen" #89 reports. Seed it, so that a pass and
            // a failure are alike reproducible.
            //
            // Pick the seed on evidence, not on taste. Most seeds draw 500 cases without ever reaching
            // the near-cancellation, so seeding on one of those would freeze the suite green and quietly
            // stop testing the bound. Seed 39 draws exactly one case that falsifies the result-derived
            // scale and none that falsify the operand-derived one, so `scale` stays load-bearing: revert
            // it to `max 1.0 (max (abs flight) (abs want))` and this test goes red on the next run.
            Check.One(
                Config.QuickThrowOnFailure
                    .WithMaxTest(500)
                    .WithReplay(Some { Rnd = Rnd 39UL; Size = None }),
                prop
            )

        // The shrunk counterexample from #89, pinned as a seed-independent regression: `flight` and
        // `want` are both ~1 while the coordinates that produce them are ~1e6, so a tolerance derived
        // from the result misses by 1.46e-5 against a 1e-5 bound. Survives any future reseeding above.
        testCase "the #89 counterexample: a unit-length flight between 1e6-magnitude operands" <| fun () ->
            let shooter = p -1.0e6 0.1631481419
            let target = p 0.0 1.0
            let v = p -1.0e6 0.0
            let speed = 1.0

            match Ballistics.intercept shooter speed target v with
            | ValueNone -> failtest "the round is not outrun here — an interception exists"
            | ValueSome aim ->
                let vSq = v.X * v.X + v.Y * v.Y
                let t = ((aim.X - target.X) * v.X + (aim.Y - target.Y) * v.Y) / vSq
                let flight = sqrt ((aim.X - shooter.X) ** 2.0 + (aim.Y - shooter.Y) ** 2.0)
                let want = speed * t

                // Guard the premise: if `t` ever stopped being ~1 the bound below would hold vacuously
                // for the wrong reason, exactly as an empty-ring regression passes `List.forall`.
                Expect.isTrue (t > 0.999 && t < 1.0) (sprintf "t is the near-unit root, got %.17g" t)
                Expect.isGreaterThan (abs shooter.X) 1.0e5 "the operands really are 1e6-magnitude"

                // The claim, against the operand-scaled bound. |v.X * t| ~ 1e6 dominates `scale`.
                let scale = List.max [ 1.0; abs flight; abs want; abs shooter.X; abs (v.X * t) ]
                Expect.isLessThanOrEqual
                    (abs (flight - want))
                    (1e-5 * scale)
                    "|aim - shooter| = speed * t, to the accuracy the 1e6 operands can carry"

                // ...and the bound is not vacuous: the two scales really do disagree by ~1e6 here, which
                // is the whole of #89. Asserted structurally rather than as "the old bound still fails",
                // because a future SHARPENING of the solve (a compensated discriminant, say — see #97)
                // would shrink the residual under the old bound and turn that phrasing red for a FIX.
                let resultScale = max 1.0 (max (abs flight) (abs want))
                Expect.isLessThan resultScale 2.0 "the RESULT is unit-scale — that is the trap"

                Expect.isGreaterThan
                    (scale / resultScale)
                    1.0e5
                    "the OPERANDS are ~1e6x the result, so a result-derived tolerance under-bounds them"

        test "a denormal target velocity is still solved, and the aim stays finite" {
            // |v|^2 underflows to 0.0, but v itself is not zero. The solve must not divide by it.
            let v = p 0.0 System.Double.Epsilon

            match Ballistics.intercept (p 0.0 0.0) 1.0 (p 10.0 0.0) v with
            | ValueSome aim ->
                Expect.isTrue (System.Double.IsFinite aim.X && System.Double.IsFinite aim.Y) "finite aim"
                Expect.floatClose Accuracy.high aim.X 10.0 "essentially the stationary answer"
            | ValueNone -> failtest "a target drifting at one ulp per second is still reachable"
        }

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

        // #97: intercept returned a phantom ValueSome for a target that OUTRUNS the round, whenever
        // `speed` and `|v|` were both sub-unit. `a = |v|^2 - speed^2` is a difference of squared
        // speeds; the old guard scaled its zero-test by `max 1.0 (speed*speed)`, which pins to 1.0
        // when both speeds are < 1 and degrades the test to the ABSOLUTE `|a| <= 1e-9`. Here
        // a = |v|^2 = 2.4e-11 is declared zero, the LINEAR branch solves `b*t + c = 0` (the wrong
        // equation), and an interception is invented for a target ~47,782x faster than the round.
        // The quadratic branch has disc = b^2 - 4ac = 96 - 481 < 0 and correctly returns ValueNone.
        // A seed-independent regression: the exact shipped repro from #97, reproduced against the
        // Release binary at commit 93aa640.
        testCase "the #97 phantom: a sub-unit outrunning target has no interception, not a linear-branch fiction" <| fun () ->
            let shooter = p 1.0e6 2.86122e-07
            let target = p -1.0e6 -1.0e6
            let v = p 0.0 4.90607e-06 // |v| = 4.9e-6
            let speed = 1.02677e-10 // the target is ~47,782x faster than the round

            // Guard the premise: this really is the outrun regime the .fsi answers with ValueNone,
            // AND both speeds are sub-unit -- the band the old `max 1.0` floor mis-scaled. If either
            // stopped holding, a ValueNone below would pass for the wrong reason.
            let vMag = sqrt (v.X * v.X + v.Y * v.Y)
            Expect.isGreaterThan vMag speed "|v| > speed: the target outruns the round"
            Expect.isLessThan vMag 1.0 "|v| is sub-unit"
            Expect.isLessThan speed 1.0 "speed is sub-unit -- the regime the old max-1.0 floor pinned"

            Expect.equal
                (Ballistics.intercept shooter speed target v)
                ValueNone
                "no non-negative root exists: |target + v*t - shooter| = speed*t is never met"

        // The forward direction the suite never asserted at these magnitudes: a returned ValueSome
        // must be a REAL interception. The old brute-force property asserts only the CONVERSE
        // (ValueNone => no root), which is exactly why #97 escaped -- nothing checked that a
        // ValueSome satisfies the defining equation. FsCheck's default float generator has
        // essentially no mass in 1e-8..1e-3, so #97's sub-unit outrun regime is unreachable by an
        // unclamped draw; `subUnit` places speed and |v| into (1e-10, 1] deliberately, log-uniform.
        testCase "a returned interception satisfies |aim - shooter| = speed * t at sub-unit speeds (#97, FsCheck >=500)"
        <| fun () ->
            // A raw float -> a magnitude in (1e-10, 1], on a log scale. The fractional part of an
            // arbitrary finite float is a serviceable uniform in [0,1) without depending on the
            // generator's (absent) small-magnitude mass; a non-finite draw degrades to a mid value.
            let subUnit (raw: float) =
                let f = if System.Double.IsFinite raw then abs raw else 0.5
                let frac = f - floor f
                10.0 ** (-10.0 * frac)

            let prop (tx: float) (ty: float) (ang: float) (vMagR: float) (spR: float) =
                let shooter = p 0.0 0.0
                let target = p (clamp tx % 100.0) (clamp ty % 100.0)
                let speed = subUnit spR
                let vMag = subUnit vMagR
                // A finite direction for v (magnitude carried by `vMag`); cos/sin are total and finite.
                let a = if System.Double.IsFinite ang then ang else 0.0
                let v = p (vMag * cos a) (vMag * sin a)

                match Ballistics.intercept shooter speed target v with
                | ValueNone -> true
                | ValueSome aim ->
                    let vSq = v.X * v.X + v.Y * v.Y
                    let flight = sqrt ((aim.X - shooter.X) ** 2.0 + (aim.Y - shooter.Y) ** 2.0)

                    if vSq = 0.0 then
                        aim = target
                    else
                        // Recover t from aim = target + v*t, then check |aim - shooter| = speed*t.
                        let t = ((aim.X - target.X) * v.X + (aim.Y - target.Y) * v.Y) / vSq
                        let want = speed * t

                        // Operand-scaled bound (the #89 lesson): the tolerance tracks the magnitudes
                        // the subtractions cancel, not the size of the tiny result. A phantom aim
                        // from the misfiring linear branch lands at a residual ratio of ~1, five
                        // orders past this bound.
                        let scale =
                            [ 1.0
                              abs flight
                              abs want
                              abs target.X
                              abs target.Y
                              abs (v.X * t)
                              abs (v.Y * t) ]
                            |> List.max

                        t >= -1e-6 && abs (flight - want) <= 1e-5 * scale

            // Seeded on evidence, and mutation-tested: under the OLD `max 1.0 (speed*speed)` guard
            // this seed draws sub-unit outrunning targets that the linear branch answers with a
            // phantom ValueSome, so reverting the fix turns this test RED. Most seeds reach the
            // misfire (~15% of draws trigger it), but pinning one keeps a pass and a failure alike
            // reproducible rather than a CI coin-flip.
            Check.One(
                Config.QuickThrowOnFailure
                    .WithMaxTest(500)
                    .WithReplay(Some { Rnd = Rnd 39UL; Size = None }),
                prop
            )

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
