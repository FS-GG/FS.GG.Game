module Game.Core.Tests.GeometryTests

// Ported from FS.GG.Rendering tests/Scene.Tests/GeometryTests.fs (ADR-0022 / P0 Option D). Only the
// namespace open changes (FS.GG.UI.Scene → FS.GG.Game.Core). Public AABB helpers on the sim Rect/Point.
// Convention under test: `intersects` strict (touching is NOT overlap); `contains`/`containsPoint`
// inclusive of shared edges; `center`/`ofCenter` round-trip; `sweptIntersects` catches tunneling.

open Expecto
open FsCheck
open FS.GG.Game.Core

let private r x y w h : Rect = { X = x; Y = y; Width = w; Height = h }
let private p x y : Point = { X = x; Y = y }

// Two ints -> a rect with non-negative size, coords/size bounded so overlaps actually happen.
let private rectOf (a: int) (b: int) (c: int) (d: int) : Rect =
    { X = float (a % 50)
      Y = float (b % 50)
      Width = float (abs (c % 50))
      Height = float (abs (d % 50)) }

[<Tests>]
let tests =
    testList "Game.Core Geometry (US1, FR-001..FR-005)" [

        testList "intersects (strict edges)" [
            test "overlapping rectangles intersect" {
                Expect.isTrue (Geometry.intersects (r 0. 0. 10. 10.) (r 5. 5. 10. 10.)) "overlap ⇒ true"
            }
            test "disjoint rectangles do not intersect" {
                Expect.isFalse (Geometry.intersects (r 0. 0. 10. 10.) (r 20. 20. 5. 5.)) "gap ⇒ false"
            }
            test "edge-touching rectangles do NOT intersect (strict)" {
                Expect.isFalse (Geometry.intersects (r 0. 0. 10. 10.) (r 10. 0. 10. 10.)) "shared edge is zero-area ⇒ false"
            }
            test "corner-touching rectangles do NOT intersect (strict)" {
                Expect.isFalse (Geometry.intersects (r 0. 0. 10. 10.) (r 10. 10. 10. 10.)) "shared corner ⇒ false"
            }
            testCase "intersects is symmetric (FsCheck ≥500 cases)" <| fun () ->
                let prop (a: int) (b: int) (c: int) (d: int) (e: int) (f: int) (g: int) (h: int) =
                    let x = rectOf a b c d
                    let y = rectOf e f g h
                    Geometry.intersects x y = Geometry.intersects y x
                Check.One(Config.QuickThrowOnFailure.WithMaxTest 500, prop)
            test "a zero-area rectangle on a shared edge does not intersect (strict)" {
                // Consistent with the plain strict formula (no zero-area special-casing): a degenerate
                // rect touching only the boundary is not an overlap. (Strictly *interior* it would be —
                // that is the documented consequence of the strict convention, not a separate rule.)
                Expect.isFalse (Geometry.intersects (r 10. 5. 0. 0.) (r 0. 0. 10. 10.)) "on the edge ⇒ false"
            }
            test "NaN coordinates return false, never throw" {
                Expect.isFalse (Geometry.intersects (r nan 0. 10. 10.) (r 0. 0. 10. 10.)) "NaN ⇒ false, total"
            }
        ]

        testList "contains / containsPoint (inclusive edges)" [
            test "outer fully containing inner ⇒ true" {
                Expect.isTrue (Geometry.contains (r 0. 0. 20. 20.) (r 5. 5. 5. 5.)) "inner inside ⇒ true"
            }
            test "inner flush against the outer edge is contained (inclusive)" {
                Expect.isTrue (Geometry.contains (r 0. 0. 20. 20.) (r 0. 0. 20. 20.)) "flush/coincident ⇒ contained"
            }
            test "inner poking outside ⇒ false" {
                Expect.isFalse (Geometry.contains (r 0. 0. 20. 20.) (r 15. 15. 10. 10.)) "spills past edge ⇒ false"
            }
            test "point strictly inside ⇒ true" {
                Expect.isTrue (Geometry.containsPoint (r 0. 0. 10. 10.) (p 5. 5.)) "interior point"
            }
            test "point on the low edge/corner ⇒ true (inclusive)" {
                Expect.isTrue (Geometry.containsPoint (r 0. 0. 10. 10.) (p 0. 0.)) "low corner inclusive"
            }
            test "point on the high edge/corner ⇒ true (inclusive)" {
                Expect.isTrue (Geometry.containsPoint (r 0. 0. 10. 10.) (p 10. 10.)) "high corner inclusive"
            }
            test "point outside ⇒ false" {
                Expect.isFalse (Geometry.containsPoint (r 0. 0. 10. 10.) (p 11. 5.)) "beyond high edge"
            }
        ]

        testList "center / ofCenter" [
            test "center of a known rectangle" {
                Expect.equal (Geometry.center (r 10. 20. 40. 60.)) (p 30. 50.) "center = corner + half-extent"
            }
            testCase "center (ofCenter c w h) = c round-trip (FsCheck ≥500 cases)" <| fun () ->
                let prop (cx: int) (cy: int) (w: int) (h: int) =
                    let c = p (float (cx % 1000)) (float (cy % 1000))
                    let width = float (abs (w % 500))
                    let height = float (abs (h % 500))
                    let got = Geometry.center (Geometry.ofCenter c width height)
                    abs (got.X - c.X) < 1e-9 && abs (got.Y - c.Y) < 1e-9
                Check.One(Config.QuickThrowOnFailure.WithMaxTest 500, prop)
            test "ofCenter produces the requested size" {
                let rect = Geometry.ofCenter (p 100. 50.) 20. 8.
                Expect.equal rect.Width 20. "width preserved"
                Expect.equal rect.Height 8. "height preserved"
            }
        ]

        testList "sweptIntersects (tunneling)" [
            test "a fast projectile tunneling through a thin target is detected" {
                // 2x2 bullet at x=0 moving +100 in x; a 1-wide wall at x=50 — start & end never overlap it.
                let bullet = r 0. 0. 2. 2.
                let wall = r 50. 0. 1. 10.
                Expect.isFalse (Geometry.intersects bullet wall) "start does not overlap"
                Expect.isFalse (Geometry.intersects (r 100. 0. 2. 2.) wall) "end does not overlap"
                Expect.isTrue (Geometry.sweptIntersects bullet (p 100. 0.) wall) "the swept path crosses the wall ⇒ true"
            }
            test "no motion + no overlap ⇒ false" {
                Expect.isFalse (Geometry.sweptIntersects (r 0. 0. 2. 2.) (p 0. 0.) (r 50. 0. 1. 10.)) "still & apart ⇒ false"
            }
            testCase "sweptIntersects is a superset of intersects at both endpoints (FsCheck ≥500)" <| fun () ->
                let prop (a: int) (b: int) (c: int) (d: int) (vx: int) (vy: int) (e: int) (f: int) =
                    let moving = rectOf a b c d
                    let target = rectOf e f (c + 3) (d + 3)
                    let v = p (float (vx % 100)) (float (vy % 100))
                    let movedEnd = { moving with X = moving.X + v.X; Y = moving.Y + v.Y }
                    // if either endpoint overlaps, the sweep must report overlap.
                    if Geometry.intersects moving target || Geometry.intersects movedEnd target then
                        Geometry.sweptIntersects moving v target
                    else
                        true
                Check.One(Config.QuickThrowOnFailure.WithMaxTest 500, prop)
        ]

        // Collision-detection capsule — the narrow-phase manifold `aabbContact`. Detection only:
        // resolution/response is a separate layer (corpus rule). These tests ARE the "constraint
        // face" — the invariants a byte-deterministic collision core must satisfy.
        testList "aabbContact (narrow-phase manifold)" [

            let isUnitAxis (n: Point) =
                (abs n.X = 1.0 && n.Y = 0.0) || (n.X = 0.0 && abs n.Y = 1.0)

            testCase "isSome agrees with intersects, always (FsCheck ≥500)" <| fun () ->
                let prop a b c d e f g h =
                    let x = rectOf a b c d
                    let y = rectOf e f g h
                    (Geometry.aabbContact x y |> Option.isSome) = Geometry.intersects x y
                Check.One(Config.QuickThrowOnFailure.WithMaxTest 500, prop)

            testCase "a contact has positive depth and a unit-axis normal (FsCheck ≥500)" <| fun () ->
                let prop a b c d e f g h =
                    match Geometry.aabbContact (rectOf a b c d) (rectOf e f g h) with
                    | Some contact -> contact.Depth > 0.0 && isUnitAxis contact.Normal
                    | None -> true
                Check.One(Config.QuickThrowOnFailure.WithMaxTest 500, prop)

            testCase "penetration depth is symmetric in the operands (FsCheck ≥500)" <| fun () ->
                let prop a b c d e f g h =
                    let x = rectOf a b c d
                    let y = rectOf e f g h
                    match Geometry.aabbContact x y, Geometry.aabbContact y x with
                    | Some cx, Some cy -> cx.Depth = cy.Depth
                    | None, None -> true
                    | _ -> false // isSome is symmetric (intersects is), so mixed Some/None is a bug
                Check.One(Config.QuickThrowOnFailure.WithMaxTest 500, prop)

            testCase "translating by the MTV separates the pair (FsCheck ≥500)" <| fun () ->
                // The manifold's core promise: pushing `x` out along -Normal*Depth removes the
                // positive-area overlap. Integer-derived coords keep the arithmetic exact.
                let prop a b c d e f g h =
                    let x = rectOf a b c d
                    let y = rectOf e f g h
                    match Geometry.aabbContact x y with
                    | Some c ->
                        let pushed =
                            { x with
                                X = x.X - c.Normal.X * c.Depth
                                Y = x.Y - c.Normal.Y * c.Depth }
                        not (Geometry.intersects pushed y)
                    | None -> true
                Check.One(Config.QuickThrowOnFailure.WithMaxTest 500, prop)

            test "normal is anti-symmetric: contact a→b negates contact b→a" {
                let a = r 0. 0. 10. 10.
                let b = r 8. 0. 10. 10.
                Expect.equal (Geometry.aabbContact a b) (Some { Normal = p 1. 0.; Depth = 2. }) "a→b points +x"
                Expect.equal (Geometry.aabbContact b a) (Some { Normal = p -1. 0.; Depth = 2. }) "b→a points -x, same depth"
            }

            test "NaN coordinates return None, never throw (total)" {
                Expect.isNone (Geometry.aabbContact (r nan 0. 10. 10.) (r 0. 0. 10. 10.)) "NaN ⇒ None, total"
            }

            // Determinism golden — fixed inputs, exact expected manifolds. Same output on every run
            // and platform (byte-identical structural equality). This is the `determinism-golden`
            // check the capsule binds in capabilities.yml.
            testList "determinism golden (byte-identical manifolds)" [
                test "least-penetration axis is X (shallow horizontal overlap)" {
                    Expect.equal (Geometry.aabbContact (r 0. 0. 10. 10.) (r 8. 0. 10. 10.))
                        (Some { Normal = p 1. 0.; Depth = 2. }) "X axis, +x, depth 2"
                }
                test "least-penetration axis is Y (shallow vertical overlap)" {
                    Expect.equal (Geometry.aabbContact (r 0. 0. 10. 10.) (r 0. 8. 10. 10.))
                        (Some { Normal = p 0. 1.; Depth = 2. }) "Y axis, +y, depth 2"
                }
                test "negative centre-delta flips the normal" {
                    Expect.equal (Geometry.aabbContact (r 0. 0. 10. 10.) (r -8. 0. 10. 10.))
                        (Some { Normal = p -1. 0.; Depth = 2. }) "X axis, -x, depth 2"
                }
                test "equal penetration on both axes breaks toward X (documented tie-break)" {
                    Expect.equal (Geometry.aabbContact (r 0. 0. 10. 10.) (r 8. 8. 10. 10.))
                        (Some { Normal = p 1. 0.; Depth = 2. }) "px = py ⇒ X axis"
                }
                test "disjoint rectangles have no contact" {
                    Expect.isNone (Geometry.aabbContact (r 0. 0. 10. 10.) (r 20. 20. 5. 5.)) "gap ⇒ None"
                }
                test "full containment still yields a deterministic MTV" {
                    Expect.equal (Geometry.aabbContact (r 0. 0. 10. 10.) (r 3. 3. 2. 2.))
                        (Some { Normal = p -1. 0.; Depth = 5. }) "inner box: X axis tie-break, depth 5"
                }
            ]
        ]

        // Circle collision-detection capsule (004) — circle–circle and circle–AABB manifolds, the
        // same Contact shape and constraint-face discipline as aabbContact. Detection only.
        testList "circleContact / circleAabbContact (narrow-phase)" [

            let circ x y rad : Circle = { Center = { X = x; Y = y }; Radius = rad }
            // Two ints -> a circle with a positive radius, bounded so overlaps actually happen.
            let circleOf (a: int) (b: int) (rr: int) : Circle =
                { Center = { X = float (a % 50); Y = float (b % 50) }; Radius = float (1 + abs (rr % 20)) }
            let isUnit (n: Point) = abs (sqrt (n.X * n.X + n.Y * n.Y) - 1.0) < 1e-9

            testCase "circleContact isSome agrees with squared-distance overlap (FsCheck ≥500)" <| fun () ->
                let prop a b ra c d rb =
                    let x = circleOf a b ra
                    let y = circleOf c d rb
                    let dx = y.Center.X - x.Center.X
                    let dy = y.Center.Y - x.Center.Y
                    let rr = x.Radius + y.Radius
                    let overlap = dx * dx + dy * dy < rr * rr
                    (Geometry.circleContact x y |> Option.isSome) = overlap
                Check.One(Config.QuickThrowOnFailure.WithMaxTest 500, prop)

            testCase "circleContact manifold has positive depth and a unit normal (FsCheck ≥500)" <| fun () ->
                let prop a b ra c d rb =
                    match Geometry.circleContact (circleOf a b ra) (circleOf c d rb) with
                    | Some k -> k.Depth > 0.0 && isUnit k.Normal
                    | None -> true
                Check.One(Config.QuickThrowOnFailure.WithMaxTest 500, prop)

            testCase "circleContact MTV separates the pair within tolerance (FsCheck ≥500)" <| fun () ->
                let prop a b ra c d rb =
                    let x = circleOf a b ra
                    let y = circleOf c d rb
                    match Geometry.circleContact x y with
                    | Some k ->
                        let x' = { x with Center = { X = x.Center.X - k.Normal.X * k.Depth; Y = x.Center.Y - k.Normal.Y * k.Depth } }
                        match Geometry.circleContact x' y with
                        | Some k2 -> k2.Depth < 1e-6
                        | None -> true
                    | None -> true
                Check.One(Config.QuickThrowOnFailure.WithMaxTest 500, prop)

            testCase "circleAabbContact isSome agrees with clamp-distance overlap (FsCheck ≥500)" <| fun () ->
                let prop a b ra e f g h =
                    let c = circleOf a b ra
                    let box = rectOf e f g h
                    let clampedX = max box.X (min (box.X + box.Width) c.Center.X)
                    let clampedY = max box.Y (min (box.Y + box.Height) c.Center.Y)
                    let dx = c.Center.X - clampedX
                    let dy = c.Center.Y - clampedY
                    let overlap = dx * dx + dy * dy < c.Radius * c.Radius
                    (Geometry.circleAabbContact c box |> Option.isSome) = overlap
                Check.One(Config.QuickThrowOnFailure.WithMaxTest 500, prop)

            testCase "circleAabbContact MTV separates within tolerance (FsCheck ≥500)" <| fun () ->
                let prop a b ra e f g h =
                    let c = circleOf a b ra
                    let box = rectOf e f g h
                    match Geometry.circleAabbContact c box with
                    | Some k ->
                        let c' = { c with Center = { X = c.Center.X - k.Normal.X * k.Depth; Y = c.Center.Y - k.Normal.Y * k.Depth } }
                        match Geometry.circleAabbContact c' box with
                        | Some k2 -> k2.Depth < 1e-6
                        | None -> true
                    | None -> true
                Check.One(Config.QuickThrowOnFailure.WithMaxTest 500, prop)

            test "NaN / non-positive radius return None, never throw (total)" {
                Expect.isNone (Geometry.circleContact (circ nan 0. 2.) (circ 0. 0. 2.)) "NaN centre ⇒ None"
                Expect.isNone (Geometry.circleContact (circ 0. 0. 0.) (circ 0. 0. 2.)) "zero radius ⇒ None"
                Expect.isNone (Geometry.circleAabbContact (circ nan 0. 2.) (r 0. 0. 10. 10.)) "NaN centre ⇒ None"
                Expect.isNone (Geometry.circleAabbContact (circ 5. 5. 0.) (r 0. 0. 10. 10.)) "zero radius ⇒ None"
            }

            // Determinism golden — fixed inputs, exact expected manifolds (byte-identical). Named
            // "determinism golden" so the gate.yml zero-match guard's determinism filter covers them.
            testList "determinism golden (circle manifolds)" [
                test "circle-circle shallow overlap on X" {
                    Expect.equal (Geometry.circleContact (circ 0. 0. 3.) (circ 4. 0. 2.))
                        (Some { Normal = p 1. 0.; Depth = 1. }) "normal +x, depth (5-4)=1"
                }
                test "circle-circle coincident centres use the fixed fallback normal" {
                    Expect.equal (Geometry.circleContact (circ 2. 2. 3.) (circ 2. 2. 1.))
                        (Some { Normal = p 1. 0.; Depth = 4. }) "coincident ⇒ (1,0), depth rA+rB"
                }
                test "circle-circle touching is not a contact (strict)" {
                    Expect.isNone (Geometry.circleContact (circ 0. 0. 2.) (circ 4. 0. 2.)) "d = rA+rB ⇒ None"
                }
                test "circle-circle disjoint is not a contact" {
                    Expect.isNone (Geometry.circleContact (circ 0. 0. 1.) (circ 5. 0. 1.)) "gap ⇒ None"
                }
                test "circle-AABB centre outside: normal points circle to box" {
                    Expect.equal (Geometry.circleAabbContact (circ 13. 5. 4.) (r 0. 0. 10. 10.))
                        (Some { Normal = p -1. 0.; Depth = 1. }) "right of box ⇒ normal -x, depth (4-3)=1"
                }
                test "circle-AABB centre at box centre: least-penetration X face, +bias" {
                    Expect.equal (Geometry.circleAabbContact (circ 5. 5. 2.) (r 0. 0. 10. 10.))
                        (Some { Normal = p -1. 0.; Depth = 7. }) "tie ⇒ X axis, depth pen(5)+r(2)"
                }
                test "circle-AABB centre near left face: escape left, normal inward" {
                    Expect.equal (Geometry.circleAabbContact (circ 1. 5. 2.) (r 0. 0. 10. 10.))
                        (Some { Normal = p 1. 0.; Depth = 3. }) "nearest left face ⇒ normal +x, depth pen(1)+r(2)"
                }
                test "circle-AABB gap is not a contact" {
                    Expect.isNone (Geometry.circleAabbContact (circ 20. 20. 2.) (r 0. 0. 10. 10.)) "far ⇒ None"
                }
            ]
        ]

        // Raycast capsule (005) — segment-cast queries returning a RayHit (t, point, normal).
        // Entry-from-outside semantics; queries only.
        testList "segmentAabbHit / segmentCircleHit (raycast)" [

            let circR x y rad : Circle = { Center = { X = x; Y = y }; Radius = rad }
            let isUnit (n: Point) = abs (sqrt (n.X * n.X + n.Y * n.Y) - 1.0) < 1e-9

            testCase "segmentAabbHit: t in [0,1], point on the box boundary, unit-axis normal (FsCheck ≥500)" <| fun () ->
                let prop a b c d e f g h =
                    let p0 = { X = float (a % 50); Y = float (b % 50) }
                    let p1 = { X = float (c % 50); Y = float (d % 50) }
                    let box = rectOf e f g h
                    match Geometry.segmentAabbHit p0 p1 box with
                    | Some hit ->
                        let onEdge =
                            abs (hit.Point.X - box.X) < 1e-9 || abs (hit.Point.X - (box.X + box.Width)) < 1e-9
                            || abs (hit.Point.Y - box.Y) < 1e-9 || abs (hit.Point.Y - (box.Y + box.Height)) < 1e-9
                        let unitAxis = (abs hit.Normal.X = 1.0 && hit.Normal.Y = 0.0) || (hit.Normal.X = 0.0 && abs hit.Normal.Y = 1.0)
                        hit.T >= 0.0 && hit.T <= 1.0 && onEdge && unitAxis
                    | None -> true
                Check.One(Config.QuickThrowOnFailure.WithMaxTest 500, prop)

            testCase "segmentCircleHit: t in [0,1], point on the circle, unit normal (FsCheck ≥500)" <| fun () ->
                let prop a b c d cx cy rr =
                    let p0 = { X = float (a % 50); Y = float (b % 50) }
                    let p1 = { X = float (c % 50); Y = float (d % 50) }
                    let circle = circR (float (cx % 50)) (float (cy % 50)) (float (1 + abs (rr % 20)))
                    match Geometry.segmentCircleHit p0 p1 circle with
                    | Some hit ->
                        let ex = hit.Point.X - circle.Center.X
                        let ey = hit.Point.Y - circle.Center.Y
                        let dist = sqrt (ex * ex + ey * ey)
                        hit.T >= 0.0 && hit.T <= 1.0 && abs (dist - circle.Radius) < 1e-6 && isUnit hit.Normal
                    | None -> true
                Check.One(Config.QuickThrowOnFailure.WithMaxTest 500, prop)

            test "NaN / zero-length / non-positive radius return None, never throw (total)" {
                Expect.isNone (Geometry.segmentAabbHit (p nan 0.) (p 5. 5.) (r 0. 0. 10. 10.)) "NaN origin ⇒ None"
                Expect.isNone (Geometry.segmentAabbHit (p 5. 5.) (p 5. 5.) (r 0. 0. 10. 10.)) "zero-length inside ⇒ None"
                Expect.isNone (Geometry.segmentCircleHit (p nan 0.) (p 5. 0.) (circR 0. 0. 2.)) "NaN origin ⇒ None"
                Expect.isNone (Geometry.segmentCircleHit (p 0. 0.) (p 0. 0.) (circR 0. 0. 2.)) "zero-length ⇒ None"
                Expect.isNone (Geometry.segmentCircleHit (p -5. 0.) (p 5. 0.) (circR 0. 0. 0.)) "non-positive radius ⇒ None"
            }

            // Determinism golden — fixed inputs, exact expected hits (byte-identical). Named
            // "determinism golden" so the gate.yml zero-match guard's determinism filter covers them.
            testList "determinism golden (raycast)" [
                test "segment enters an AABB through the left face" {
                    Expect.equal (Geometry.segmentAabbHit (p -5. 5.) (p 5. 5.) (r 0. 0. 10. 10.))
                        (Some { T = 0.5; Point = p 0. 5.; Normal = p -1. 0. }) "enter left ⇒ t 0.5, point (0,5), normal -x"
                }
                test "segment entering an AABB at a corner resolves to the X face (tie-break)" {
                    Expect.equal (Geometry.segmentAabbHit (p -5. -5.) (p 5. 5.) (r 0. 0. 10. 10.))
                        (Some { T = 0.5; Point = p 0. 0.; Normal = p -1. 0. }) "corner ⇒ X face, t 0.5"
                }
                test "segment starting inside the AABB has no entry" {
                    Expect.isNone (Geometry.segmentAabbHit (p 5. 5.) (p 20. 5.) (r 0. 0. 10. 10.)) "origin inside ⇒ None"
                }
                test "segment missing the AABB" {
                    Expect.isNone (Geometry.segmentAabbHit (p -5. 20.) (p 5. 20.) (r 0. 0. 10. 10.)) "above box ⇒ None"
                }
                test "segment enters a circle at the near root" {
                    Expect.equal (Geometry.segmentCircleHit (p -4. 0.) (p 4. 0.) (circR 0. 0. 2.))
                        (Some { T = 0.25; Point = p -2. 0.; Normal = p -1. 0. }) "near root ⇒ t 0.25, point (-2,0), normal -x"
                }
                test "segment starting inside the circle has no entry" {
                    Expect.isNone (Geometry.segmentCircleHit (p 0. 0.) (p 10. 0.) (circR 0. 0. 2.)) "origin at centre ⇒ None"
                }
                test "segment missing the circle" {
                    Expect.isNone (Geometry.segmentCircleHit (p -5. 5.) (p 5. 5.) (circR 0. 0. 2.)) "line y=5 misses r=2 ⇒ None"
                }
            ]
        ]

        // SAT/OBB convex-polygon capsule (005b) — polygonContact narrow-phase manifolds over a new
        // ConvexPolygon primitive built via obbPolygon. Same Contact shape and constraint-face
        // discipline as aabbContact: detection only, MTV as (Normal, Depth), byte-deterministic.
        testList "obbPolygon / polygonContact (SAT convex)" [

            let obb cx cy hx hy = Geometry.obbPolygon (p cx cy) (p hx hy) 0.0
            let isUnit (n: Point) = abs (sqrt (n.X * n.X + n.Y * n.Y) - 1.0) < 1e-9
            // A rotation-0 OBB from ints (positive half-extents) + the aabbContact Rect it equals.
            let obbOf (a: int) (b: int) (c: int) (d: int) : ConvexPolygon * Rect =
                let cx, cy = float (a % 50), float (b % 50)
                let hx, hy = float (1 + abs (c % 20)), float (1 + abs (d % 20))
                Geometry.obbPolygon (p cx cy) (p hx hy) 0.0,
                { X = cx - hx; Y = cy - hy; Width = 2.0 * hx; Height = 2.0 * hy }
            // A possibly-rotated OBB from ints — for the axis-independent MTV/normal invariants.
            let rotObbOf (a: int) (b: int) (c: int) (d: int) (e: int) : ConvexPolygon =
                Geometry.obbPolygon
                    (p (float (a % 50)) (float (b % 50)))
                    (p (float (1 + abs (c % 20))) (float (1 + abs (d % 20))))
                    (float (e % 13) * 0.5)

            testCase "isSome agrees with aabbContact for rotation-0 OBBs (FsCheck ≥500)" <| fun () ->
                // Two axis-aligned OBBs collide exactly when their source rects overlap (strict edges).
                let prop a b c d e f g h =
                    let pa, ra = obbOf a b c d
                    let pb, rb = obbOf e f g h
                    (Geometry.polygonContact pa pb |> Option.isSome) = (Geometry.aabbContact ra rb |> Option.isSome)
                Check.One(Config.QuickThrowOnFailure.WithMaxTest 500, prop)

            testCase "a contact has positive depth and a unit normal (FsCheck ≥500)" <| fun () ->
                let prop a b c d e f g h i j =
                    match Geometry.polygonContact (rotObbOf a b c d e) (rotObbOf f g h i j) with
                    | Some k -> k.Depth > 0.0 && isUnit k.Normal
                    | None -> true
                Check.One(Config.QuickThrowOnFailure.WithMaxTest 500, prop)

            testCase "translating by the MTV separates the pair within tolerance (FsCheck ≥500)" <| fun () ->
                // The manifold's promise: pushing `a` by -Normal*Depth removes the overlap (SAT + MTV).
                let prop a b c d e f g h i j =
                    let x = rotObbOf a b c d e
                    let y = rotObbOf f g h i j
                    match Geometry.polygonContact x y with
                    | Some k ->
                        let x' = { Vertices = x.Vertices |> Array.map (fun v -> { X = v.X - k.Normal.X * k.Depth; Y = v.Y - k.Normal.Y * k.Depth }) }
                        match Geometry.polygonContact x' y with
                        | Some k2 -> k2.Depth < 1e-6
                        | None -> true
                    | None -> true
                Check.One(Config.QuickThrowOnFailure.WithMaxTest 500, prop)

            test "obbPolygon at rotation 0 is the axis-aligned box, CCW (FR-003)" {
                Expect.equal (Geometry.obbPolygon (p 0. 0.) (p 1. 1.) 0.0)
                    { Vertices = [| p -1. -1.; p 1. -1.; p 1. 1.; p -1. 1. |] } "CCW corners of the unit box"
            }

            test "two rotated OBBs that overlap produce a contact" {
                // A unit square at the origin and a 45°-rotated square centred at (2,0) whose left vertex
                // reaches x≈0.586 < 1 — they overlap, so detection returns a manifold.
                let diamond = Geometry.obbPolygon (p 2. 0.) (p 1. 1.) (System.Math.PI / 4.0)
                Expect.isTrue (Geometry.polygonContact (obb 0. 0. 1. 1.) diamond |> Option.isSome) "rotated overlap ⇒ Some"
            }

            test "degenerate inputs return None, never throw (total)" {
                Expect.isNone (Geometry.polygonContact { Vertices = [| p 0. 0.; p 1. 0. |] } (obb 0. 0. 1. 1.)) "<3 vertices ⇒ None"
                Expect.isNone (Geometry.polygonContact { Vertices = [| p nan 0.; p 1. 0.; p 0. 1. |] } (obb 0. 0. 1. 1.)) "NaN coordinate ⇒ None"
                Expect.isNone (Geometry.polygonContact { Vertices = [| p 0. 0.; p 1. 0.; p 2. 0. |] } (obb 0. 0. 1. 1.)) "collinear/zero-area ⇒ None"
            }

            // Determinism golden — fixed inputs, exact expected manifolds (byte-identical). Named
            // "determinism golden" so the gate.yml zero-match guard's determinism filter covers them.
            testList "determinism golden (SAT convex manifolds)" [
                test "shallow X overlap ⇒ least-overlap axis X, normal a→b +x" {
                    Expect.equal (Geometry.polygonContact (obb 0. 0. 1. 1.) (obb 1.5 0. 1. 1.))
                        (Some { Normal = p 1. 0.; Depth = 0.5 }) "X axis, +x, depth 0.5"
                }
                test "equal X/Y overlap ⇒ first axis in generation order (a edge0 = Y face)" {
                    // The convex tie-break follows the a-then-b edge-normal generation order (edge0 is the
                    // bottom face ⇒ Y axis), distinct from aabbContact's X-bias but equally deterministic.
                    Expect.equal (Geometry.polygonContact (obb 0. 0. 1. 1.) (obb 1.5 1.5 1. 1.))
                        (Some { Normal = p 0. 1.; Depth = 0.5 }) "tie ⇒ Y axis (generation order), +y, depth 0.5"
                }
                test "touching squares are not a contact (strict edges)" {
                    Expect.isNone (Geometry.polygonContact (obb 0. 0. 1. 1.) (obb 2. 0. 1. 1.)) "shared edge ⇒ zero overlap ⇒ None"
                }
                test "disjoint squares are not a contact" {
                    Expect.isNone (Geometry.polygonContact (obb 0. 0. 1. 1.) (obb 5. 0. 1. 1.)) "gap ⇒ None"
                }
                test "full containment yields the true exit depth (containment correction)" {
                    // b is nested inside a on both axes; the naive overlap would report b's half-height
                    // (1), but the real MTV pushes b out the top: depth = 5 - 2.5 = 2.5 along +y.
                    Expect.equal (Geometry.polygonContact (obb 0. 0. 5. 5.) (obb 0. 3. 1. 0.5))
                        (Some { Normal = p 0. 1.; Depth = 2.5 }) "contained box escapes +y, depth 2.5"
                }
            ]
        ]

        // Segment-vs-convex-polygon cast (031) — the raycast counterpart of polygonContact. Entry-from-
        // outside semantics, strict edges, and the entered edge's outward normal as the struck face.
        testList "segmentPolygonHit (segment vs convex polygon)" [

            let isUnit (n: Point) = abs (sqrt (n.X * n.X + n.Y * n.Y) - 1.0) < 1e-9
            // The rotation-0 OBB over (0,0,10,10) — the box every raycast golden above casts at.
            let box10 = Geometry.obbPolygon (p 5. 5.) (p 5. 5.) 0.0
            // A rotation-0 OBB from ints (positive half-extents) + the segmentAabbHit Rect it equals.
            let obbOf (a: int) (b: int) (c: int) (d: int) : ConvexPolygon * Rect =
                let cx, cy = float (a % 50), float (b % 50)
                let hx, hy = float (1 + abs (c % 20)), float (1 + abs (d % 20))
                Geometry.obbPolygon (p cx cy) (p hx hy) 0.0,
                { X = cx - hx; Y = cy - hy; Width = 2.0 * hx; Height = 2.0 * hy }

            testCase "agrees with segmentAabbHit on rotation-0 OBBs, but for the corner graze (FsCheck ≥500)" <| fun () ->
                // The cross-check DEC-004 already imposes on polygonContact/aabbContact, one level up: a
                // rotation-0 obbPolygon casts exactly like its source Rect. The one documented divergence
                // is strict edges — a segment that clips a single corner is a hit for the `<=` slab test
                // and a graze (not a hit) for the polygon's `<`. Normals are NOT compared: a corner ENTRY
                // ties, and the two resolve the tie differently (first edge in vertex order vs the X face).
                // Integer coordinates keep `cx - hx + 2*hx` and `cx + hx` the same double, so the Rect and
                // the polygon are the SAME box here; on fractional extents they need not be, and a segment
                // endpoint on the boundary can then land inside one shape and on the other.
                let prop a b c d e f g h =
                    let p0 = p (float (a % 50)) (float (b % 50))
                    let p1 = p (float (c % 50)) (float (d % 50))
                    let poly, box = obbOf e f g h
                    let isCorner (q: Point) =
                        (abs (q.X - box.X) < 1e-9 || abs (q.X - (box.X + box.Width)) < 1e-9)
                        && (abs (q.Y - box.Y) < 1e-9 || abs (q.Y - (box.Y + box.Height)) < 1e-9)
                    let key (o: RayHit option) = o |> Option.map (fun x -> x.T, x.Point)
                    match Geometry.segmentPolygonHit p0 p1 poly, Geometry.segmentAabbHit p0 p1 box with
                    | ph, ah when key ph = key ah -> true
                    | None, Some ah -> isCorner ah.Point
                    | _ -> false
                Check.One(Config.QuickThrowOnFailure.WithMaxTest 500, prop)

            testCase "a hit has t in [0,1], a point on the boundary, and an inward-facing unit normal (FsCheck ≥500)" <| fun () ->
                // `Normal` is the outward unit normal of the entered edge, so the hit point lies on that
                // edge's line ((point - vertex)·n = 0) and the segment runs against it (direction·n < 0).
                let prop a b c d e f g h i =
                    let p0 = p (float (a % 50)) (float (b % 50))
                    let p1 = p (float (c % 50)) (float (d % 50))
                    let poly =
                        Geometry.obbPolygon
                            (p (float (e % 50)) (float (f % 50)))
                            (p (float (1 + abs (g % 20))) (float (1 + abs (h % 20))))
                            (float (i % 13) * 0.5)
                    match Geometry.segmentPolygonHit p0 p1 poly with
                    | Some hit ->
                        let onSomeEdgeLine =
                            poly.Vertices
                            |> Array.exists (fun v -> abs ((hit.Point.X - v.X) * hit.Normal.X + (hit.Point.Y - v.Y) * hit.Normal.Y) < 1e-9)
                        let inward = (p1.X - p0.X) * hit.Normal.X + (p1.Y - p0.Y) * hit.Normal.Y < 0.0
                        hit.T >= 0.0 && hit.T <= 1.0 && isUnit hit.Normal && onSomeEdgeLine && inward
                    | None -> true
                Check.One(Config.QuickThrowOnFailure.WithMaxTest 500, prop)

            testCase "the entry parameter is invariant under rotating the whole scene (FsCheck ≥500)" <| fun () ->
                // Rotating p0, p1 and the polygon about the origin by θ maps the rotation-0 OBB to
                // `obbPolygon (R θ centre) h θ`, so the cast is a rigid motion and T must not move.
                let prop a b c d e f g h i =
                    let p0 = p (float (a % 50)) (float (b % 50))
                    let p1 = p (float (c % 50)) (float (d % 50))
                    let centre = p (float (e % 50)) (float (f % 50))
                    let half = p (float (1 + abs (g % 20))) (float (1 + abs (h % 20)))
                    let theta = float (i % 13) * 0.5
                    let co, si = cos theta, sin theta
                    let rot (q: Point) : Point = { X = q.X * co - q.Y * si; Y = q.X * si + q.Y * co }
                    let flat = Geometry.segmentPolygonHit p0 p1 (Geometry.obbPolygon centre half 0.0)
                    let spun = Geometry.segmentPolygonHit (rot p0) (rot p1) (Geometry.obbPolygon (rot centre) half theta)
                    match flat, spun with
                    | Some x, Some y -> abs (x.T - y.T) < 1e-9
                    | None, None -> true
                    // A hit within a rounding of the boundary may fall either side of the strict test.
                    | Some x, None
                    | None, Some x -> x.T < 1e-9 || x.T > 1.0 - 1e-9
                Check.One(Config.QuickThrowOnFailure.WithMaxTest 500, prop)

            test "degenerate inputs return None, never throw (total)" {
                let tri: ConvexPolygon = { Vertices = [| p 0. 0.; p 4. 0.; p 0. 4. |] }
                Expect.isNone (Geometry.segmentPolygonHit (p 2. -2.) (p 2. 2.) { Vertices = [| p 0. 0.; p 1. 0. |] }) "<3 vertices ⇒ None"
                Expect.isNone (Geometry.segmentPolygonHit (p 2. -2.) (p 2. 2.) { Vertices = [| p 0. 0.; p 1. 0.; p 2. 0. |] }) "collinear/zero-area ⇒ None"
                Expect.isNone (Geometry.segmentPolygonHit (p 2. -2.) (p 2. 2.) { Vertices = [| p nan 0.; p 4. 0.; p 0. 4. |] }) "NaN vertex ⇒ None"
                Expect.isNone (Geometry.segmentPolygonHit (p nan -2.) (p 2. 2.) tri) "NaN origin ⇒ None"
                Expect.isNone (Geometry.segmentPolygonHit (p 2. -2.) (p 2. -2.) tri) "zero-length outside ⇒ None"
                Expect.isNone (Geometry.segmentPolygonHit (p 1. 1.) (p 1. 1.) tri) "zero-length inside ⇒ None"
            }

            // Determinism golden — fixed inputs, exact expected hits (byte-identical). Named
            // "determinism golden" so the gate.yml zero-match guard's determinism filter covers them.
            testList "determinism golden (polygon raycast)" [
                test "segment enters a rotation-0 OBB through the left edge" {
                    Expect.equal (Geometry.segmentPolygonHit (p -5. 5.) (p 5. 5.) box10)
                        (Some { T = 0.5; Point = p 0. 5.; Normal = p -1. 0. }) "enter left ⇒ t 0.5, point (0,5), normal -x"
                }
                test "segment enters an arbitrary convex polygon through the edge it crosses" {
                    // A CCW right triangle; the cast crosses the bottom edge at its midpoint and leaves
                    // through the hypotenuse at t = 1. The normal is the bottom edge's outward -y.
                    let tri: ConvexPolygon = { Vertices = [| p 0. 0.; p 4. 0.; p 0. 4. |] }
                    Expect.equal (Geometry.segmentPolygonHit (p 2. -2.) (p 2. 2.) tri)
                        (Some { T = 0.5; Point = p 2. 0.; Normal = p 0. -1. }) "enter bottom ⇒ t 0.5, point (2,0), normal -y"
                }
                test "segment entering at a corner resolves to the first edge in vertex order (tie-break)" {
                    // Both the bottom (vertex order 0) and left (order 3) edges are entered at t = 0.5;
                    // the first wins. segmentAabbHit ties to the X face here, so the normals differ — the
                    // same principled divergence polygonContact has from aabbContact on a tie.
                    Expect.equal (Geometry.segmentPolygonHit (p -5. -5.) (p 5. 5.) box10)
                        (Some { T = 0.5; Point = p 0. 0.; Normal = p 0. -1. }) "corner ⇒ bottom edge, t 0.5"
                    Expect.equal (Geometry.segmentAabbHit (p -5. -5.) (p 5. 5.) (r 0. 0. 10. 10.))
                        (Some { T = 0.5; Point = p 0. 0.; Normal = p -1. 0. }) "…where the AABB cast ties to the X face"
                }
                test "corner tie-break holds on a rotated ring, where the parameters tie only to an ULP" {
                    // The two edges meeting at (-1,-4) are entered at the same parameter in exact
                    // arithmetic, but each t is computed through its own normalisation: edge 1 yields
                    // 0.33333333333333326 and edge 2 yields 0.33333333333333331. Vertex order — not the
                    // larger float — must pick the struck face, so the normal is edge 1's (-1,-2)/√5 and
                    // not edge 2's (0,-1). Guards the tie-break against rounding noise on a general ring.
                    let ring: ConvexPolygon = { Vertices = [| p -4. -2.; p -3. -3.; p -1. -4.; p 2. -4.; p -1. 3. |] }
                    match Geometry.segmentPolygonHit (p 0. -5.) (p -3. -2.) ring with
                    | Some hit ->
                        Expect.floatClose Accuracy.high hit.T (1.0 / 3.0) "enters at the shared vertex"
                        Expect.floatClose Accuracy.high hit.Point.X -1.0 "point is the shared vertex"
                        Expect.floatClose Accuracy.high hit.Point.Y -4.0 "point is the shared vertex"
                        Expect.floatClose Accuracy.high hit.Normal.X (-1.0 / sqrt 5.0) "edge 1's normal, not edge 2's"
                        Expect.floatClose Accuracy.high hit.Normal.Y (-2.0 / sqrt 5.0) "edge 1's normal, not edge 2's"
                    | None -> failtest "expected a corner entry on the rotated ring"
                }
                test "a segment collinear with an edge is a hit (its chord has positive length)" {
                    // Running along the bottom edge's line, entering through the left edge at t = 0.25.
                    // The bottom edge is parallel and constrains nothing; strict edges reject a zero-length
                    // chord, not a boundary-hugging one. segmentAabbHit agrees exactly here.
                    Expect.equal (Geometry.segmentPolygonHit (p -5. 0.) (p 15. 0.) box10)
                        (Some { T = 0.25; Point = p 0. 0.; Normal = p -1. 0. }) "along the bottom edge ⇒ enter left, t 0.25"
                    Expect.equal (Geometry.segmentAabbHit (p -5. 0.) (p 15. 0.) (r 0. 0. 10. 10.))
                        (Some { T = 0.25; Point = p 0. 0.; Normal = p -1. 0. }) "…and the AABB cast agrees exactly"
                    // Starting ON the boundary and staying on it never enters: no edge is crossed inward.
                    Expect.isNone (Geometry.segmentPolygonHit (p 2. 0.) (p 8. 0.) box10) "starts on the edge ⇒ None"
                    Expect.isNone (Geometry.segmentAabbHit (p 2. 0.) (p 8. 0.) (r 0. 0. 10. 10.)) "…as does the AABB cast"
                }
                test "a segment grazing one corner is not a hit (strict edges)" {
                    // Touches (0,0) and leaves; zero-length chord ⇒ no entered edge. The AABB cast's `<=`
                    // slab test calls the same graze a hit — the sole documented divergence.
                    Expect.isNone (Geometry.segmentPolygonHit (p -5. 5.) (p 5. -5.) box10) "corner graze ⇒ None"
                    Expect.isSome (Geometry.segmentAabbHit (p -5. 5.) (p 5. -5.) (r 0. 0. 10. 10.)) "…where the AABB cast reports a hit"
                }
                test "segment starting inside the polygon has no entry" {
                    Expect.isNone (Geometry.segmentPolygonHit (p 5. 5.) (p 20. 5.) box10) "origin inside ⇒ None"
                }
                test "segment missing the polygon" {
                    Expect.isNone (Geometry.segmentPolygonHit (p -5. 20.) (p 5. 20.) box10) "above the box ⇒ None"
                }
                test "polygon behind the segment is not a hit" {
                    Expect.isNone (Geometry.segmentPolygonHit (p -20. 5.) (p -15. 5.) box10) "box beyond p1 ⇒ None"
                }
                test "segment enters a rotated OBB through the rotated edge it crosses" {
                    // A 45°-rotated unit square at the origin is the diamond with vertices (0,∓√2),(±√2,0).
                    // Casting along y = x from the lower left crosses the lower-left edge (the line
                    // x + y = -√2) at its midpoint, so the struck face carries the outward normal
                    // (−1,−1)/√2 — a rotated armour zone the AABB cast cannot express.
                    let diamond = Geometry.obbPolygon (p 0. 0.) (p 1. 1.) (System.Math.PI / 4.0)
                    match Geometry.segmentPolygonHit (p -3. -3.) (p 3. 3.) diamond with
                    | Some hit ->
                        Expect.floatClose Accuracy.high hit.T ((3.0 - sqrt 0.5) / 6.0) "enters at x = -1/√2"
                        Expect.floatClose Accuracy.high hit.Point.X (-(sqrt 0.5)) "point on the lower-left edge"
                        Expect.floatClose Accuracy.high hit.Point.Y (-(sqrt 0.5)) "point on the lower-left edge"
                        Expect.isTrue (isUnit hit.Normal) "unit normal"
                        Expect.floatClose Accuracy.high hit.Normal.X (-(sqrt 0.5)) "outward normal x = -1/√2"
                        Expect.floatClose Accuracy.high hit.Normal.Y (-(sqrt 0.5)) "outward normal y = -1/√2"
                    | None -> failtest "expected a hit on the rotated diamond"
                }
            ]
        ]
    ]
