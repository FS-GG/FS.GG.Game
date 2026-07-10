module Game.Core.Tests.VisibilityTests

// Promoted from the FS.GG.Rendering `visibility` starter fragment (template/fragments/visibility).
// Continuous-space sight: exact point-to-point LOS and the angular-sweep visibility polygon. Pure,
// total (non-finite/degenerate input degrades rather than throws), and byte-deterministic — the sweep
// uses a cross-product angular comparator with an integer-index tiebreak, never `atan2`.

open Expecto
open FsCheck
open FS.GG.Game.Core

let private pt x y : Point = { X = x; Y = y }
let private seg a b : Visibility.Segment = { A = a; B = b }
let private settings r : Visibility.Settings = { Radius = r }

let private clamp (v: float) =
    if System.Double.IsNaN v || System.Double.IsInfinity v then 0.0 else max -1.0e6 (min 1.0e6 v)

let private isFinitePt (p: Point) =
    System.Double.IsFinite p.X && System.Double.IsFinite p.Y

let private sqLenPt (p: Point) = p.X * p.X + p.Y * p.Y

// The spanning-chord fixture, verbatim from the issue: one wall from (-1000, 0) to (1000, 0), a source
// at (0, -10), radius 50. The wall crosses the sight bound clean through with BOTH endpoints far
// outside it — the case an endpoint-bucketed broad phase drops.
let private spanningWall = seg (pt -1000.0 0.0) (pt 1000.0 0.0)
let private spanningSource = pt 0.0 -10.0
let private spanningRadius = 50.0

let private spanningBound: Rect =
    { X = spanningSource.X - spanningRadius
      Y = spanningSource.Y - spanningRadius
      Width = 2.0 * spanningRadius
      Height = 2.0 * spanningRadius }

// The yardstick for `raySegment`'s precision promise (`Visibility.fsi`, #64). Every finite double is a
// dyadic rational, so the whole solve re-runs in `BigInteger` — scaled by `2^1074`, the smallest
// subnormal, which clears every fraction — where nothing overflows and nothing cancels. The float solve
// is then measured against that exact `t` rather than against itself, which is the only way to see an
// error that is *finite and wrong* (a self-comparison sees only non-determinism).
module private Oracle =
    open System.Numerics

    /// `d * 2^1074`, exact for any finite `d`. Undefined for infinity/NaN (`expBits = 2047`), which
    /// would take the normal branch and yield a large finite integer for a value that is not a number.
    /// Every caller feeds it a `t` that `raySegment` has already proved finite, or a literal coordinate.
    let scaled (d: float) : BigInteger =
        let bits = System.BitConverter.DoubleToInt64Bits d
        let sign = if bits < 0L then BigInteger.MinusOne else BigInteger.One
        let expBits = int ((bits >>> 52) &&& 0x7FFL)
        let mantissa = bits &&& 0xFFFFFFFFFFFFFL

        let struct (m, e) =
            if expBits = 0 then
                struct (mantissa, -1074) // subnormal: no implicit leading bit
            else
                struct (mantissa ||| 0x10000000000000L, expBits - 1075)

        (sign * BigInteger(m)) <<< (e + 1074) // `e >= -1074`, so the shift is never negative

    /// `|a| / |b|` as a double. Divides as `BigInteger` FIRST: these numerators run to hundreds of
    /// digits, `float` of one is `+infinity`, and `infinity / infinity` is a `NaN` that compares
    /// false against every tolerance — i.e. a differential that silently passes.
    ///
    /// Carries a resolution floor: the `<<< 64` gives 64 fractional bits, so a true ratio below
    /// `2^-64` (~5.4e-20) truncates to `0.0`. Far under every tolerance asserted here; a test that
    /// ever tightens past ~1e-19 must widen the shift or it will measure nothing.
    let private ratio (a: BigInteger) (b: BigInteger) : float =
        if a.IsZero then
            0.0
        else
            float ((BigInteger.Abs a <<< 64) / BigInteger.Abs b) / 1.8446744073709552e19 // 2^64

    /// The exact numerator and denominator of `t`, formed from the ORIGINAL inputs — not from the
    /// rounded `W` and `E` the float solve builds, which is precisely where its accuracy is lost.
    /// `None` when the ray is parallel to the segment, the one case that has no `t`.
    let tExact (o: Point) (dir: Point) (s: Visibility.Segment) : (BigInteger * BigInteger) option =
        let wx, wy = scaled s.A.X - scaled o.X, scaled s.A.Y - scaled o.Y
        let ex, ey = scaled s.B.X - scaled s.A.X, scaled s.B.Y - scaled s.A.Y
        let den = scaled dir.X * ey - scaled dir.Y * ex
        if den.IsZero then None else Some(wx * ey - wy * ex, den)

    /// `|t - tExact| * |den| * 2^1074` — an integer, so the comparison never rounds. The two callers
    /// below recover a relative or an absolute error by choosing what to divide it by.
    let private residual (t: float) (num: BigInteger) (den: BigInteger) =
        BigInteger.Abs(scaled t * den - (num <<< 1074))

    /// `|t - tExact| / |tExact|`. An exact `t` of zero admits only an exact `0.0` back — any other
    /// answer is infinitely wrong in relative terms, and `infinity` is the honest report. (Reaching it
    /// needs `origin` exactly on the segment's infinite line, i.e. an exactly-vanishing 2x2 integer
    /// determinant, which random coordinates do not produce.)
    let relErr (t: float) (num: BigInteger, den: BigInteger) =
        let r = residual t num den

        if num.IsZero then
            (if r.IsZero then 0.0 else infinity)
        else
            ratio r (BigInteger.Abs num <<< 1074)

    /// `|t - tExact|`, in units of `dir`.
    let absErr (t: float) (num: BigInteger, den: BigInteger) =
        ratio (residual t num den) (BigInteger.Abs den <<< 1074)

/// Deterministic sample streams for the differentials. All driven by `Rng`, which is byte-deterministic
/// by its own contract, so these can never flake — and lazily, so a 20k-sample run allocates nothing.

/// A bounded world: coordinates linear-uniform in `[-s, s]`, generic configurations.
let private worldSamples (s: float) (n: int) =
    seq {
        let mutable rng = Rng.ofSeed 20260710UL

        let draw () =
            let struct (f, rng') = Rng.nextFloat rng
            rng <- rng'
            (f * 2.0 - 1.0) * s

        for _ in 1..n do
            let ox, oy = draw (), draw ()
            let ax, ay = draw (), draw ()
            let bx, by = draw (), draw ()
            let dx, dy = draw (), draw ()
            yield pt ox oy, pt dx dy, seg (pt ax ay) (pt bx by)
    }

/// GRAZING rays: `dir` leans `sinTheta` off the wall's tangent, and `origin` sits `sinTheta * tHit` off
/// its line, so the crossing lands at `t = tHit` and the solve's denominator `dir × E` is order
/// `sinTheta * |E|` — i.e. it cancels away `-log10 sinTheta` digits. This, not a ray aimed at an
/// endpoint, is what makes `t` ill-conditioned: aiming at an endpoint merely drives `u` to zero while
/// leaving `t ≈ 1`, which is perfectly conditioned. `sinTheta` spans `1e-2` (a mild graze) down to
/// `1e-14` (a razor one), and is yielded so a test can check the error against the conditioning it implies.
let private grazingSamples (s: float) (n: int) =
    seq {
        let mutable rng = Rng.ofSeed 20260710UL

        let draw () =
            let struct (f, rng') = Rng.nextFloat rng
            rng <- rng'
            f

        for _ in 1..n do
            let ax, ay = (draw () * 2.0 - 1.0) * s, (draw () * 2.0 - 1.0) * s
            let bx, by = (draw () * 2.0 - 1.0) * s, (draw () * 2.0 - 1.0) * s
            let ex, ey = bx - ax, by - ay
            let len = sqrt (ex * ex + ey * ey)
            let sinTheta = System.Math.Pow(10.0, -2.0 - 12.0 * draw ())
            let alongWall = draw () * 0.9 + 0.05 // land strictly inside the wall
            let back = draw () * 0.5 // start behind the landing point

            if len > 0.0 then
                let tx, ty = ex / len, ey / len // unit tangent
                let nx, ny = -ty, tx // unit normal
                let a = -back * len
                let tHit = alongWall * len - a

                if tHit > 0.0 then
                    let origin = pt (ax + a * tx - sinTheta * tHit * nx) (ay + a * ty - sinTheta * tHit * ny)
                    let dir = pt (tx + sinTheta * nx) (ty + sinTheta * ny)
                    yield origin, dir, seg (pt ax ay) (pt bx by), sinTheta
    }

/// The regime the `.fsi` disclaims: coordinates log-uniform across ~14 orders of magnitude
/// (`1e-8` … `1e6`), which is what manufactures the wide dynamic range between `origin` and the
/// segment endpoints. A different seed from `worldSamples`, so the two differentials are independent.
let private wideRangeSamples (n: int) =
    seq {
        let mutable rng = Rng.ofSeed 20260711UL

        let draw () =
            let struct (f, rng') = Rng.nextFloat rng
            rng <- rng'
            f

        let coord () =
            let e = draw () * 14.0 - 8.0
            (draw () * 2.0 - 1.0) * System.Math.Pow(10.0, e)

        for _ in 1..n do
            yield pt (coord ()) (coord ()), pt (coord ()) (coord ()), seg (pt (coord ()) (coord ())) (pt (coord ()) (coord ()))
    }

[<Tests>]
let tests =
    testList "Game.Core Visibility (continuous-space sight)" [

        // --- the regression this promotion carries -------------------------------------------------

        testCase "spanning chord occludes: the sweep sees nothing past a wall crossing its bound" <| fun () ->
            let poly = Visibility.polygon (settings spanningRadius) spanningSource [ spanningWall ]
            Expect.isNonEmpty poly.Vertices "the sweep produced a ring"
            // The wall lies along y = 0 and spans the whole bound, so the visible region is the half
            // below it. A vertex above the wall means the sweep saw straight through it.
            let maxY = poly.Vertices |> List.map (fun v -> v.Y) |> List.max
            Expect.isLessThanOrEqual maxY 1e-9 "no vertex lies beyond the spanning wall"

        // Occluding is necessary but not sufficient: the ring must also turn at the two points where the
        // wall crosses the sight bound. The sweep aims rays at occluder endpoints, and this wall's own
        // endpoints are 950 units outside the bound — so unless it is CLIPPED to the bound, nothing aims
        // at (±50, 0), the ring cuts the corner, and the visible region loses a triangle on each side.
        testCase "spanning chord occludes: the ring turns at the wall's bound crossings" <| fun () ->
            let poly = Visibility.polygon (settings spanningRadius) spanningSource [ spanningWall ]

            let has (x, y) =
                poly.Vertices |> List.exists (fun v -> abs (v.X - x) < 1e-6 && abs (v.Y - y) < 1e-6)

            Expect.isTrue (has (spanningRadius, 0.0)) "vertex where the wall leaves the bound (right)"
            Expect.isTrue (has (-spanningRadius, 0.0)) "vertex where the wall leaves the bound (left)"

        testCase "spanning chord occludes: a target past the wall is not visible" <| fun () ->
            Expect.isFalse (Visibility.isVisible spanningSource (pt 0.0 40.0) [ spanningWall ]) "blocked by the wall"
            Expect.isTrue (Visibility.isVisible spanningSource (pt 0.0 -40.0) [ spanningWall ]) "clear below the wall"

        // Pins WHY the test above discriminates: the broad phase this replaced bucketed a segment by its
        // two endpoints and kept those bucketed inside the sight bound. Both of this wall's endpoints sit
        // 950 units outside it, so that cull returned nothing and the wall stopped occluding entirely.
        testCase "the endpoint-bucket broad phase this replaced would have dropped that wall" <| fun () ->
            let endpointBuckets =
                SpatialGrid.build 10.0 [ spanningWall.A, "wall"; spanningWall.B, "wall" ]

            Expect.equal
                (SpatialGrid.query spanningBound endpointBuckets)
                []
                "both endpoints fall outside the sight bound, so an endpoint-keyed cull drops the wall"

        // --- raySegment ----------------------------------------------------------------------------

        testCase "raySegment finds the crossing and its parametric distance" <| fun () ->
            let hit = Visibility.raySegment (pt 0.0 0.0) (pt 1.0 0.0) (seg (pt 5.0 -1.0) (pt 5.0 1.0))
            Expect.equal hit (Some(pt 5.0 0.0, 5.0)) "unit dir ⇒ t is the world distance"

        testCase "raySegment is None on parallel, zero-length, behind, and non-finite input" <| fun () ->
            let origin, dir = pt 0.0 0.0, pt 1.0 0.0
            Expect.isNone (Visibility.raySegment origin dir (seg (pt 1.0 1.0) (pt 5.0 1.0))) "parallel"
            Expect.isNone (Visibility.raySegment origin dir (seg (pt 5.0 0.0) (pt 5.0 0.0))) "zero-length occludes nothing"
            Expect.isNone (Visibility.raySegment origin dir (seg (pt -5.0 -1.0) (pt -5.0 1.0))) "behind the origin"
            Expect.isNone (Visibility.raySegment origin dir (seg (pt nan 0.0) (pt 5.0 1.0))) "non-finite segment"
            Expect.isNone (Visibility.raySegment (pt nan 0.0) dir (seg (pt 5.0 -1.0) (pt 5.0 1.0))) "non-finite origin"

        // --- isVisible (the "can this body shoot that body" predicate) -----------------------------

        testCase "isVisible: self, clear line, and a blocker strictly between" <| fun () ->
            let wall = seg (pt 5.0 -1.0) (pt 5.0 1.0)
            Expect.isTrue (Visibility.isVisible (pt 0.0 0.0) (pt 0.0 0.0) [ wall ]) "the source always sees itself"
            Expect.isTrue (Visibility.isVisible (pt 0.0 0.0) (pt 3.0 0.0) [ wall ]) "target short of the wall"
            Expect.isFalse (Visibility.isVisible (pt 0.0 0.0) (pt 10.0 0.0) [ wall ]) "wall strictly between"
            Expect.isTrue (Visibility.isVisible (pt 0.0 0.0) (pt 10.0 0.0) []) "no occluders"

        testCase "isVisible: a blocker exactly at the target does not block (strict 0 < t < 1)" <| fun () ->
            let wall = seg (pt 5.0 -1.0) (pt 5.0 1.0)
            Expect.isTrue (Visibility.isVisible (pt 0.0 0.0) (pt 5.0 0.0) [ wall ]) "target sits on the wall"

        testCase "isVisible: non-finite endpoints are never visible" <| fun () ->
            Expect.isFalse (Visibility.isVisible (pt nan 0.0) (pt 1.0 0.0) []) "non-finite source"
            Expect.isFalse (Visibility.isVisible (pt 0.0 0.0) (pt infinity 0.0) []) "non-finite target"

        // --- polygon: totality + determinism --------------------------------------------------------

        testCase "polygon: a non-finite source yields an empty ring" <| fun () ->
            let poly = Visibility.polygon (settings 50.0) (pt nan 0.0) [ spanningWall ]
            Expect.isEmpty poly.Vertices "empty ring"
            // `nan <> nan` structurally, so compare the source componentwise rather than by record equality.
            Expect.isTrue (System.Double.IsNaN poly.Source.X) "the source is echoed back unchanged"
            Expect.equal poly.Source.Y 0.0 "the source is echoed back unchanged"

        testCase "polygon: a non-positive or non-finite radius falls back to 1.0" <| fun () ->
            for bad in [ 0.0; -50.0; nan; infinity ] do
                let poly = Visibility.polygon (settings bad) (pt 0.0 0.0) []
                Expect.isNonEmpty poly.Vertices $"radius {bad} still produces a closed ring"

                let outside =
                    poly.Vertices
                    |> List.filter (fun v -> abs v.X > 1.0 + 1e-9 || abs v.Y > 1.0 + 1e-9)

                Expect.isEmpty outside $"radius {bad} bounds the ring at the fallback 1.0"

        testCase "polygon: non-finite and zero-length occluders are discarded, not thrown on" <| fun () ->
            let junk =
                [ seg (pt nan 0.0) (pt 1.0 1.0); seg (pt 3.0 3.0) (pt 3.0 3.0); seg (pt 0.0 infinity) (pt 1.0 1.0) ]

            let withJunk = Visibility.polygon (settings 10.0) (pt 0.0 0.0) junk
            let without = Visibility.polygon (settings 10.0) (pt 0.0 0.0) []
            Expect.equal withJunk without "degenerate occluders change nothing"

        testCase "polygon is deterministic for identical input" <| fun () ->
            let walls = [ spanningWall; seg (pt 20.0 -30.0) (pt 20.0 30.0) ]
            let a = Visibility.polygon (settings spanningRadius) spanningSource walls
            let b = Visibility.polygon (settings spanningRadius) spanningSource walls
            Expect.equal a b "byte-identical ring across calls"

        // --- overflow in the parametric cross products (#53) ----------------------------------------

        // `raySegment`'s operands are all finite here, and the intersection it is asked for is real and
        // nearby — the ray runs along y = 0 and strikes the segment's (0, 0) endpoint. But the numerator
        // `wx * ey` = 3.937 * -MaxValue overflows to -infinity while `denom` = 0.937 * -MaxValue stays
        // finite, so `t` came out +infinity, `u` came out 1.0, and the old `t >= 0.0` guard admitted it.
        // The point was then `origin + infinity * dir` = (infinity, infinity * 0.0) = (infinity, NaN).
        // Asserts the *contract* (no non-finite hit), not the current implementation (which returns None).
        // A future overflow-free solve would return the true hit `(0, 0)` at `t = 4.201` and must still
        // pass this, so `Expect.isNone` would be the wrong assertion to pin here.
        testCase "raySegment never yields a non-finite hit when the cross product overflows" <| fun () ->
            let origin = pt -3.937126736 0.0
            let dir = pt 0.937126736 0.0
            let far = seg (pt 0.0 System.Double.MaxValue) (pt 0.0 0.0)

            match Visibility.raySegment origin dir far with
            | None -> ()
            | Some(p, t) ->
                Expect.isTrue (isFinitePt p) $"raySegment returned a non-finite hit point ({p.X}, {p.Y})"
                Expect.isTrue (System.Double.IsFinite t) $"raySegment returned a non-finite t ({t})"

        // The same geometry with a representable far endpoint resolves normally — proof that the guard
        // above is scoped to the overflow regime and has not blinded `raySegment` to this configuration.
        testCase "raySegment still finds the crossing when the far endpoint is representable" <| fun () ->
            let hit = Visibility.raySegment (pt -3.937126736 0.0) (pt 0.937126736 0.0) (seg (pt 0.0 1.0e6) (pt 0.0 0.0))

            Expect.isSome hit "a segment ending at 1e6 still occludes"
            let p, t = Option.get hit
            Expect.isTrue (isFinitePt p) "hit point is finite"
            Expect.floatClose Accuracy.high t 4.201274582 "parametric distance to the crossing"

        // The shrunk counterexample from #53, pinned as a seed-independent regression: this exact input
        // put (infinity, NaN), (infinity, infinity) and (infinity, -infinity) into `Vertices`. A radius of
        // `Double.MaxValue` is what drags an occluder endpoint far enough out to overflow the sweep's rays.
        testCase "polygon emits no non-finite vertex for the #53 counterexample" <| fun () ->
            let walls =
                [ seg (pt -3.0 0.0) (pt 0.0 0.0)
                  seg (pt 0.0 System.Double.MaxValue) (pt -0.0 -0.0) ]

            let poly = Visibility.polygon (settings System.Double.MaxValue) (pt -3.937126736 0.0) walls

            // Non-empty first: `List.forall` is vacuously true on an empty ring, so without this the
            // regression would keep passing if a later guard started dropping every hit.
            Expect.isNonEmpty poly.Vertices "the #53 counterexample still produces a ring"

            Expect.isTrue
                (poly.Vertices |> List.forall isFinitePt)
                "every vertex of the #53 counterexample is finite"

        // --- recovering the representable hit the overflow guard discarded (#59) --------------------

        // The accuracy half of #53/#56. Same geometry as the guard regression above, but now asserting the
        // *answer* rather than merely its finiteness: the crossing is `(0, 0)` at `t = 3.937.../0.937... =
        // 4.201...`, ordinary doubles both, and the solve must reach it instead of saturating to `None`.
        // Pinned against the exact quotient, not a rounded literal, because that quotient IS the true `t`.
        testCase "raySegment recovers the representable hit when the numerator overflows" <| fun () ->
            let origin = pt -3.937126736 0.0
            let dir = pt 0.937126736 0.0
            let far = seg (pt 0.0 System.Double.MaxValue) (pt 0.0 0.0)

            let p, t = Expect.wantSome (Visibility.raySegment origin dir far) "the crossing is representable"
            Expect.equal t (3.937126736 / 0.937126736) "the true parametric distance, to the last bit"
            Expect.floatClose Accuracy.high p.X 0.0 "hit lies on the wall at x = 0"
            Expect.equal p.Y 0.0 "the ray runs along y = 0"

        // The far endpoint's magnitude is not part of the answer: the crossing is pinned by the near endpoint
        // and the ray. Each scale is checked against the TRUE `t`, never against another scale's output —
        // the direct and rescaled solves round independently, so cross-path byte-equality is not an invariant
        // (see the property below), and pinning one path to the other's ulps would red on an innocent change.
        testCase "raySegment: the far endpoint's magnitude does not move the hit" <| fun () ->
            let origin = pt -3.937126736 0.0
            let dir = pt 0.937126736 0.0
            let trueT = 3.937126736 / 0.937126736

            for farY in [ 1.0e6; 1.0e150; 1.0e300; System.Double.MaxValue ] do
                let hit = Visibility.raySegment origin dir (seg (pt 0.0 farY) (pt 0.0 0.0))
                let p, t = Expect.wantSome hit $"a wall ending at {farY} still occludes"
                Expect.floatClose Accuracy.veryHigh t trueT $"parametric distance, far endpoint at {farY}"
                Expect.floatClose Accuracy.high p.X 0.0 $"hit lies on the wall, far endpoint at {farY}"

        // The denominator saturates instead of the numerator, and this one the #56 guard cannot see: with
        // `dir × E` = 2 * MaxValue = infinity, `t` and `u` come back as `finite / infinity` = 0.0 — ordinary
        // numbers that pass every finiteness check. `raySegment` reported a hit AT the origin, `t = 0`, for a
        // crossing 5e-301 away. A silently wrong answer, where the numerator case at least announced itself
        // as a NaN. The rescaled solve returns the true `(0, 0)`.
        testCase "raySegment does not report a spurious t = 0 hit when the denominator overflows" <| fun () ->
            let origin = pt -1.0e-300 0.0
            let dir = pt 2.0 0.0 // 2 * MaxValue overflows; 1 * MaxValue would not
            let wall = seg (pt 0.0 0.0) (pt 0.0 System.Double.MaxValue)

            let p, t = Expect.wantSome (Visibility.raySegment origin dir wall) "the crossing is representable"
            Expect.equal t (1.0e-300 / 2.0) "the true parametric distance, not the 0.0 that infinity produced"
            Expect.equal p.X 0.0 "the hit is on the wall, not back at the origin"

        // Both endpoints extreme and opposite-signed, so `E` itself overflows on subtraction
        // (`MaxValue - (-MaxValue)` = infinity) before any cross product is formed. Previously `None`.
        testCase "raySegment solves a wall spanning -MaxValue to +MaxValue" <| fun () ->
            let origin = pt -3.937126736 0.0
            let dir = pt 0.937126736 0.0
            let wall = seg (pt 0.0 -System.Double.MaxValue) (pt 0.0 System.Double.MaxValue)

            let _, t = Expect.wantSome (Visibility.raySegment origin dir wall) "an unbounded wall still occludes"
            Expect.equal t (3.937126736 / 0.937126736) "the true parametric distance, to the last bit"

        // The `.fsi` promises the rescaled solve is replay-safe ("rescaling introduces no rounding of its
        // own"). Every other determinism test in this list uses order-1e3 fixtures, which take the direct
        // path exclusively — so without this one, swapping `ILogB`/`ScaleB` for a rounding normaliser (a
        // divide by the max component, say) would leave the whole suite green. Exercises both the ring and
        // the raw solve on inputs that can only reach the fallback.
        testCase "the rescaled solve is deterministic for identical input" <| fun () ->
            let origin = pt -3.937126736 0.0
            let dir = pt 0.937126736 0.0
            let far = seg (pt 0.0 System.Double.MaxValue) (pt 0.0 0.0)

            Expect.equal
                (Visibility.raySegment origin dir far)
                (Visibility.raySegment origin dir far)
                "byte-identical hit across calls, on the overflow path"

            let walls =
                [ seg (pt -3.0 0.0) (pt 0.0 0.0)
                  seg (pt 0.0 System.Double.MaxValue) (pt -0.0 -0.0) ]

            let ring () = Visibility.polygon (settings System.Double.MaxValue) origin walls
            Expect.equal (ring ()) (ring ()) "byte-identical ring across calls, on the overflow path"

        // ---- the precision promise, against an exact oracle (#64) ---------------------------------
        //
        // #59/#62 made the solve overflow-free; neither made it *accurate*, and the two failures look
        // nothing alike. An overflowed `t` is non-finite, so a guard can see it. A cancelled `t` is an
        // ordinary double that is simply wrong — `raySegment` reports a hit at `t = 0` for a crossing
        // that is elsewhere — and no self-consistency check can see that. Only an exact oracle can.
        //
        // `t = (W × E) / (dir × E)` degrades in two independent ways, and conflating them is the trap:
        //
        //   * the DENOMINATOR `dir × E` cancels as the ray/wall angle closes. This is the textbook
        //     conditioning of a line-line intersection — relative error grows like `u / sin θ` — and it
        //     is inherent to the parametrisation, not a defect. It is pinned below as a LAW, not a bound.
        //   * the NUMERATOR `W × E` cancels when `origin` and `seg.A` span many orders of magnitude,
        //     because `W = seg.A - origin` rounds the smaller operand away *before* the cross product
        //     exists. Anchoring at the nearer endpoint (the rescaled fallback) cannot recover a bit the
        //     subtraction already dropped — measured, it repairs fewer than half of these. This is #64.
        //
        // The `.fsi` therefore promises accuracy over a bounded world of generic configurations, states
        // the conditioning law for grazing rays, and disclaims wide dynamic range. These tests are those
        // three statements, made executable.

        testCase "raySegment's t is accurate to 1e-12 over a bounded world" <| fun () ->
            let mutable checked' = 0
            let mutable worst = 0.0

            for origin, dir, s in worldSamples 1.0e6 20_000 do
                match Visibility.raySegment origin dir s with
                | None -> ()
                | Some(_, t) ->
                    match Oracle.tExact origin dir s with
                    | None -> ()
                    | Some exact ->
                        checked' <- checked' + 1
                        worst <- max worst (Oracle.relErr t exact)

            // Guards the guard: a sampler that stopped producing hits would pass a `worst = 0` assertion
            // vacuously. ~1 in 6 random quadruples is a hit, so this floor is far below the true rate.
            Expect.isGreaterThan checked' 1_000 "the sampler must actually produce hits to measure"

            Expect.isLessThan
                worst
                1.0e-12
                $"relative error of t against the exact BigInteger solve, over {checked'} hits"

        // A grazing ray is where `t` is genuinely ill-conditioned: `dir × E` cancels away `-log10 sinθ`
        // digits, so NO bound of the form "relative error < c" can hold — at `sinθ = 1e-14` the error is
        // percent-scale, and that is arithmetic, not a bug. What *can* be pinned, and what a regression
        // would break, is the LAW: `relErr ≈ u_machine / sinθ`, i.e. `relErr * sinθ` stays at machine
        // epsilon. Measured max is 1.52e-16 (~0.7 * 2^-52); the bound below leaves ~6x headroom.
        //
        // Pinning the law rather than a number is what keeps this test honest. A `1e-9` bound on the hit
        // point, say, would pass here only because the sampler never grazed — the mistake this test
        // replaces, which reported `t ≈ 1` for all 10,096 of its "adversarial" hits.
        testCase "a grazing ray degrades exactly as its conditioning predicts, and no worse" <| fun () ->
            let mutable checked' = 0
            let mutable worst = 0.0
            let mutable degenerate = 0

            for origin, dir, s, sinTheta in grazingSamples 1.0e4 20_000 do
                match Visibility.raySegment origin dir s with
                | None -> ()
                | Some(_, t) ->
                    match Oracle.tExact origin dir s with
                    | None -> ()
                    | Some exact ->
                        let r = Oracle.relErr t exact
                        // `relErr` is `infinity` only for an exactly-collinear origin, which this
                        // generator never constructs. Count rather than silently skip.
                        if System.Double.IsFinite r then
                            checked' <- checked' + 1
                            worst <- max worst (r * sinTheta)
                        else
                            degenerate <- degenerate + 1

            Expect.isGreaterThan checked' 10_000 "the generator must actually produce grazing hits"
            Expect.equal degenerate 0 "no sample should be exactly collinear"

            Expect.isLessThan
                worst
                1.0e-15
                $"relErr * sinTheta (the conditioning law) over {checked'} grazing rays"

        // The regime the `.fsi` disclaims, measured rather than asserted. The claim it makes — that this
        // starts biting at ~14 orders of magnitude, not the ~200 that #64 assumed — is exactly the claim
        // that redirected that issue, so it gets a harness rather than a footnote. Both bounds matter: the
        // lower one fails if the defect ever gets fixed (delete this test and the caveat together), the
        // upper one fails if it ever gets meaningfully worse.
        testCase "outside the bounded world ~2% of hits carry a relative error above 1e-12" <| fun () ->
            let mutable checked' = 0
            let mutable degraded = 0

            for origin, dir, s in wideRangeSamples 20_000 do
                match Visibility.raySegment origin dir s with
                | None -> ()
                | Some(_, t) ->
                    match Oracle.tExact origin dir s with
                    | None -> ()
                    | Some exact ->
                        checked' <- checked' + 1
                        if Oracle.relErr t exact > 1.0e-12 then degraded <- degraded + 1

            Expect.isGreaterThan checked' 1_000 "the sampler must actually produce hits to measure"
            let rate = float degraded / float checked'

            Expect.isGreaterThan rate 0.01 $"degraded fraction ({degraded}/{checked'}) — 14 orders suffice"
            Expect.isLessThan rate 0.05 $"degraded fraction ({degraded}/{checked'}) — and has not worsened"

        // Guards the guard. An oracle that returns `NaN` — `float` of a several-hundred-digit numerator is
        // `+infinity`, and `infinity / infinity` is `NaN` — makes every `isLessThan` above pass on inputs it
        // never actually compared. So pin the oracle against a hit whose exact `t` is an integer: it must
        // report zero error for the true `t`, and a known relative error for a deliberately perturbed one.
        testCase "the exact oracle reports zero error on an exact t, and sees a perturbation" <| fun () ->
            let origin = pt 0.0 0.0
            let dir = pt 1.0 0.0
            let wall = seg (pt 2.0 -1.0) (pt 2.0 1.0) // crossed at (2, 0), so t = 2 exactly

            let _, t = Expect.wantSome (Visibility.raySegment origin dir wall) "the ray crosses the wall"
            Expect.equal t 2.0 "the float solve is exact here, so the oracle has a fixed point to hit"

            let exact = Expect.wantSome (Oracle.tExact origin dir wall) "not parallel"
            Expect.equal (Oracle.relErr t exact) 0.0 "zero relative error against an exactly-representable t"
            Expect.equal (Oracle.absErr t exact) 0.0 "zero absolute error against an exactly-representable t"

            // A 1e-6 relative perturbation must read back as a 1e-6 relative error, not as 0 and not as NaN.
            let seen = Oracle.relErr (2.0 * (1.0 + 1.0e-6)) exact
            Expect.floatClose Accuracy.high seen 1.0e-6 "the oracle measures a known perturbation"

        // The `.fsi`'s disclaimer, made executable: OUTSIDE the promised regime `t` is wrong, not merely
        // imprecise. Verbatim from #64 — `origin` at ~1e-9 against a segment endpoint at ~1e199, so forming
        // `W = seg.A - origin` discards `origin` entirely and the numerator then cancels to nothing.
        // `raySegment` reports a hit at `t = 0` (the origin itself) for a crossing at `t ≈ 3.07e8`, with
        // `denom`, `t` and `u` all finite — so no guard inside the solve can see it.
        //
        // This test pins the LIMITATION, not a desired behaviour. An implementation that computes `t`
        // exactly here is strictly better: delete this case, and the `.fsi` caveat with it.
        testCase "outside the promised regime raySegment is wrong, and the oracle says so" <| fun () ->
            let origin = pt 4.020794469e-09 0.4511871065
            let dir = pt 0.05228096296 -23.41834131
            let wall = seg (pt -3.292173079e+199 -9.753634222e+99) (pt 5.213872332e-09 -0.6581832746)

            let hit, t = Expect.wantSome (Visibility.raySegment origin dir wall) "a hit is still reported"
            Expect.equal t 0.0 "the cancelled numerator collapses t to zero"
            Expect.equal hit origin "so the reported hit is the origin, not the true crossing"

            let exact = Expect.wantSome (Oracle.tExact origin dir wall) "not parallel"
            Expect.isGreaterThan (Oracle.relErr t exact) 0.5 "the oracle sees the error the solve cannot"

        // Acceptance criterion: "for any finite input where the true intersection is representable,
        // raySegment returns it rather than None." Metamorphic, because a closed form for the true hit is
        // exactly what is under test. Construct the crossing `h` first and aim the ray so it lands at
        // `t = 1`, then slide the occluder's far endpoint out along its own direction by ever-larger
        // factors. The far endpoint is not on the answer: `h` is pinned by the near endpoint and the ray,
        // so every scale must still report it. The last two scales overflow the direct solve.
        //
        // The assertion is convergence on `h`, not byte-equality across scales — a longer segment changes
        // `E`, hence the last bits of the direct solve's quotient. Byte-equality is a claim about ONE input
        // across runs (the determinism tests), never across different inputs.
        testCase "raySegment: extending the occluder away from the crossing does not lose it (FsCheck)" <| fun () ->
            let prop (hx: float) (hy: float) (ox: float) (oy: float) (ex: float) (ey: float) =
                let h = pt (clamp hx) (clamp hy) // the intended crossing
                let o = pt (clamp ox) (clamp oy) // the ray origin
                let dir = pt (h.X - o.X) (h.Y - o.Y) // so the crossing sits at t = 1

                let e = pt (clamp ex) (clamp ey)
                let elen = sqrt (sqLenPt e)
                let cross = dir.X * e.Y - dir.Y * e.X

                // Skip degenerate draws: a zero-length ray, and a zero-length or near-ray-parallel occluder.
                // `cross` scales with both operands, so normalise it before calling an angle small — a grazing
                // crossing is ill-conditioned for any solve and is not what this property is about.
                if sqLenPt dir = 0.0 || elen = 0.0 || abs cross / (sqrt (sqLenPt dir) * elen) < 1.0e-3 then
                    true
                else
                    let u = pt (e.X / elen) (e.Y / elen) // unit vector along the occluder
                    let near = pt (h.X - u.X) (h.Y - u.Y) // one unit back from the crossing

                    // `h` sits at segment parameter 1/(1 + scale), strictly interior for every scale > 0.
                    [ 1.0; 1.0e6; 1.0e150; 1.0e300; System.Double.MaxValue ]
                    |> List.forall (fun scale ->
                        let far = pt (h.X + u.X * scale) (h.Y + u.Y * scale)

                        // A far endpoint that itself overflows is not the input we meant to build.
                        if not (isFinitePt far) then
                            true
                        else
                            match Visibility.raySegment o dir (seg near far) with
                            | None -> false // the representable crossing was dropped — the #59 bug
                            | Some(p, t) ->
                                let tol = 1.0e-6 * max 1.0 (sqrt (sqLenPt h))
                                abs (t - 1.0) <= 1.0e-6
                                && abs (p.X - h.X) <= tol
                                && abs (p.Y - h.Y) <= tol)

            Check.One(Config.QuickThrowOnFailure.WithMaxTest 500, prop)

        // Totality is a claim about *arbitrary* input, so this property deliberately does NOT clamp:
        // NaN and infinite coordinates, sources, and radii are exactly the cases the `.fsi` promises to
        // degrade rather than throw on. Clamping them away here would test nothing.
        testCase "raySegment never returns a non-finite hit, on unclamped input (FsCheck)" <| fun () ->
            let prop (ox: float) (oy: float) (dx: float) (dy: float) (ax: float) (ay: float) (bx: float) (by: float) =
                match Visibility.raySegment (pt ox oy) (pt dx dy) (seg (pt ax ay) (pt bx by)) with
                | None -> true
                | Some(p, t) -> isFinitePt p && System.Double.IsFinite t

            Check.One(Config.QuickThrowOnFailure.WithMaxTest 500, prop)

        testCase "polygon never throws and never emits a NaN vertex, on unclamped input (FsCheck)" <| fun () ->
            let prop (coords: (float * float * float * float) list) (sx: float) (sy: float) (r: float) =
                let walls =
                    coords |> List.map (fun (ax, ay, bx, by) -> seg (pt ax ay) (pt bx by))

                let poly = Visibility.polygon (settings r) (pt sx sy) walls
                poly.Vertices |> List.forall isFinitePt

            Check.One(Config.QuickThrowOnFailure.WithMaxTest 500, prop)

        testCase "isVisible never throws, on unclamped input (FsCheck)" <| fun () ->
            let prop (coords: (float * float * float * float) list) (sx: float) (sy: float) (tx: float) (ty: float) =
                let walls =
                    coords |> List.map (fun (ax, ay, bx, by) -> seg (pt ax ay) (pt bx by))

                Visibility.isVisible (pt sx sy) (pt tx ty) walls |> ignore
                true

            Check.One(Config.QuickThrowOnFailure.WithMaxTest 500, prop)

        testCase "polygon: every vertex lies within the sight bound (FsCheck)" <| fun () ->
            let prop (coords: (float * float * float * float) list) (r: float) =
                let source = pt 0.0 0.0

                let radius =
                    let c = abs (clamp r)
                    if c > 0.0 then min c 1.0e4 else 1.0

                let walls =
                    coords
                    |> List.map (fun (ax, ay, bx, by) -> seg (pt (clamp ax) (clamp ay)) (pt (clamp bx) (clamp by)))

                let poly = Visibility.polygon (settings radius) source walls
                // Rays terminate on the `source ± radius` box, so no hit can escape it (bar float slop).
                let tol = 1e-6 * max 1.0 radius

                poly.Vertices
                |> List.forall (fun v -> abs v.X <= radius + tol && abs v.Y <= radius + tol)

            Check.One(Config.QuickThrowOnFailure.WithMaxTest 500, prop)
    ]
