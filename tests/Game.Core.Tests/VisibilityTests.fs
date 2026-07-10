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
