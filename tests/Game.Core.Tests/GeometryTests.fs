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

        // Manifold / polygonManifold (045) — the impulse counterpart of polygonContact. SAT gives the
        // axis and the depth; contact POINTS need reference-face selection plus Sutherland–Hodgman
        // clipping of the incident face. Additive: `Contact` and `polygonContact` are untouched, and
        // the manifold reuses their SAT scan, so Normal/Depth agree bit for bit and isSome agrees for
        // all inputs. Same strict edges, same detection-only discipline, same byte-determinism.
        testList "polygonManifold (contact points, pair ids, feature ids)" [

            let obb cx cy hx hy = Geometry.obbPolygon (p cx cy) (p hx hy) 0.0
            let isUnit (n: Point) = abs (sqrt (n.X * n.X + n.Y * n.Y) - 1.0) < 1e-9

            // Contact POINTS are not translation-equivariant in IEEE-754, so they are compared to a few
            // ULP rather than bit-for-bit; every other field of the manifold still is. See the translation
            // property below for why, and here for where the bound comes from.
            // 2^-52, the double's unit roundoff. NOT `System.Double.Epsilon`, which in .NET is the
            // smallest DENORMAL (~4.9e-324) and would collapse the bound below to exact equality.
            let machineEps = 2.220446049250313e-16
            // A tolerance that tracks the operands' magnitude: `eps * m` is within 2x of one ULP of `m`,
            // and the `max 1.0` keeps it meaningful as a coordinate crosses zero — where a ULP COUNT would
            // read a 1.8e-15 drift as ~4.4e18 representable steps, so no ULP-count bound is usable here.
            // A 400k-pair sweep over this generator's domain puts the worst observed drift at 16x; 64x is
            // that bound with 4x of headroom, and still ~5 orders tighter than the file's usual 1e-9.
            let near (a: float) (b: float) =
                a = b || abs (a - b) <= 64.0 * machineEps * max 1.0 (max (abs a) (abs b))
            let nearPoint (q: Point) (w: Point) = near q.X w.X && near q.Y w.Y
            // Length-guarded: `Array.forall2` RAISES on a length mismatch, and a mismatch here is a
            // failure to report, not an exception to throw.
            let nearPoints (qs: Point[]) (ws: Point[]) =
                qs.Length = ws.Length && Array.forall2 nearPoint qs ws

            // FsCheck draws a fresh seed per run unless told otherwise, which makes an intermittently false
            // property a coin flip in CI rather than a red build — this is how #87 reached `main` green.
            // Seed the determinism properties, so that a pass and a failure are alike reproducible.
            //
            // Pick the seed on evidence, not on taste. Most seeds (7, 42, 87, 2026, ...) draw 500 cases
            // WITHOUT ever hitting the translation drift, so seeding on one of those would freeze the suite
            // green and quietly stop testing the tolerance below — the flake would look fixed while a
            // regression to exact equality still passed. Seed 1 draws exactly one pair that fails
            // exact equality and none that fail the stated tolerance, so the bound stays load-bearing.
            // (Roughly 1 seed in 8 draws such a pair, which is the ~1-in-6 failure rate #87 reports.)
            let seeded (seed: uint64) =
                Config.QuickThrowOnFailure.WithReplay(Some { Rnd = Rnd seed; Size = None })

            // A rotation-0 OBB from ints (positive half-extents) + the aabbContact Rect it equals.
            let obbOf (a: int) (b: int) (c: int) (d: int) : ConvexPolygon * Rect =
                let cx, cy = float (a % 50), float (b % 50)
                let hx, hy = float (1 + abs (c % 20)), float (1 + abs (d % 20))
                Geometry.obbPolygon (p cx cy) (p hx hy) 0.0,
                { X = cx - hx; Y = cy - hy; Width = 2.0 * hx; Height = 2.0 * hy }
            // A possibly-rotated OBB from ints — for the axis-independent point/normal invariants.
            let rotObbOf (a: int) (b: int) (c: int) (d: int) (e: int) : ConvexPolygon =
                Geometry.obbPolygon
                    (p (float (a % 50)) (float (b % 50)))
                    (p (float (1 + abs (c % 20))) (float (1 + abs (d % 20))))
                    (float (e % 13) * 0.5)
            // Distance from `q` to the nearest point of the polygon's boundary (min over its edges).
            let distToBoundary (poly: ConvexPolygon) (q: Point) =
                let v = poly.Vertices
                let mutable best = System.Double.PositiveInfinity
                for i in 0 .. v.Length - 1 do
                    let s = v.[i]
                    let e = v.[(i + 1) % v.Length]
                    let ex, ey = e.X - s.X, e.Y - s.Y
                    let len2 = ex * ex + ey * ey
                    // Clamp the projection parameter to the segment, then measure to that closest point.
                    let t = if len2 > 0.0 then max 0.0 (min 1.0 (((q.X - s.X) * ex + (q.Y - s.Y) * ey) / len2)) else 0.0
                    let cx, cy = s.X + ex * t, s.Y + ey * t
                    let d = sqrt ((q.X - cx) * (q.X - cx) + (q.Y - cy) * (q.Y - cy))
                    if d < best then best <- d
                best

            testCase "isSome agrees with polygonContact for all inputs (FsCheck ≥500)" <| fun () ->
                let prop a b c d e f g h i j =
                    let x, y = rotObbOf a b c d e, rotObbOf f g h i j
                    let viaManifold = Geometry.polygonManifold x y |> ValueOption.isSome
                    let viaContact = Geometry.polygonContact x y |> Option.isSome
                    viaManifold = viaContact
                Check.One(Config.QuickThrowOnFailure.WithMaxTest 500, prop)

            testCase "Normal and Depth agree with polygonContact bit for bit (FsCheck ≥500)" <| fun () ->
                // Both read the same SAT scan, so this is exact equality, not a tolerance.
                let prop a b c d e f g h i j =
                    let x, y = rotObbOf a b c d e, rotObbOf f g h i j
                    match Geometry.polygonManifold x y, Geometry.polygonContact x y with
                    | ValueSome m, Some k -> m.Normal = k.Normal && m.Depth = k.Depth
                    | ValueNone, None -> true
                    | _ -> false
                Check.One(Config.QuickThrowOnFailure.WithMaxTest 500, prop)

            testCase "rotation-0 OBBs: Normal and Depth agree with aabbContact (FsCheck ≥500)" <| fun () ->
                // The DEC-004 cross-check one level up from polygonContact's isSome agreement. Guarded off
                // the tie-breaks the two functions deliberately resolve differently: an equal penetration
                // on both axes (polygonContact keeps generation order, aabbContact biases X), and a zero
                // centre-delta on the chosen axis (aabbContact's +bias vs the a-edge-0 normal's −y).
                let prop a b c d e f g h =
                    let pa, ra = obbOf a b c d
                    let pb, rb = obbOf e f g h
                    let dx = (rb.X + rb.Width / 2.0) - (ra.X + ra.Width / 2.0)
                    let dy = (rb.Y + rb.Height / 2.0) - (ra.Y + ra.Height / 2.0)
                    let px = (ra.Width + rb.Width) / 2.0 - abs dx
                    let py = (ra.Height + rb.Height) / 2.0 - abs dy
                    if px = py || dx = 0.0 || dy = 0.0 then
                        true
                    else
                        match Geometry.polygonManifold pa pb, Geometry.aabbContact ra rb with
                        | ValueSome m, Some k -> m.Normal = k.Normal && m.Depth = k.Depth
                        | ValueNone, None -> true
                        | _ -> false
                Check.One(Config.QuickThrowOnFailure.WithMaxTest 500, prop)

            testCase "contact points lie on the boundary of both shapes within tolerance (FsCheck ≥500)" <| fun () ->
                // The manifold's promise. Every point sits exactly on the boundary of the incident
                // polygon (it is a point of its clipped face), and no deeper than `Depth` inside the
                // reference one — so it is within `Depth` of that boundary too. The caller cannot see
                // which polygon was incident, so assert the symmetric form: one distance is ~0, and both
                // are within `Depth`. That is what "the pair met here, to within the penetration" means.
                let prop a b c d e f g h i j =
                    let x, y = rotObbOf a b c d e, rotObbOf f g h i j
                    match Geometry.polygonManifold x y with
                    | ValueNone -> true
                    | ValueSome m ->
                        m.Points
                        |> Array.forall (fun q ->
                            let da, db = distToBoundary x q, distToBoundary y q
                            min da db <= 1e-6 && max da db <= m.Depth + 1e-6)
                Check.One(Config.QuickThrowOnFailure.WithMaxTest 500, prop)

            testCase "shape invariants: A<B, 1-2 points, PointCount=Points.Length, unit normal (FsCheck ≥500)" <| fun () ->
                let prop a b c d e f g h i j =
                    match Geometry.polygonManifold (rotObbOf a b c d e) (rotObbOf f g h i j) with
                    | ValueNone -> true
                    | ValueSome m ->
                        m.A < m.B
                        && m.PointCount = m.Points.Length
                        && m.PointCount >= 1
                        && m.PointCount <= 2
                        && m.Depth > 0.0
                        && isUnit m.Normal
                Check.One(Config.QuickThrowOnFailure.WithMaxTest 500, prop)

            testCase "the manifold is a pure function of the pair (FsCheck ≥500)" <| fun () ->
                // The literal warm-start invariant: a pair that has not moved yields the identical
                // manifold on the next tick, so `(A, B, FeatureId)` keys into the same cache slot.
                let prop a b c d e f g h i j =
                    let x, y = rotObbOf a b c d e, rotObbOf f g h i j
                    Geometry.polygonManifold x y = Geometry.polygonManifold x y
                Check.One(Config.QuickThrowOnFailure.WithMaxTest 500, prop)

            testCase "the manifold rides a common translation: ids/normal/depth exactly, points to a few ULP (FsCheck ≥500)" <| fun () ->
                // Translating both polygons by a common integer delta changes NOTHING about the feature id,
                // the point count, the normal or the depth — each of those is exact, and asserted as such.
                // Every input to the feature id (vertex order, the two argmax face scans) is
                // translation-invariant by construction, and integer coordinates keep it so in float.
                //
                // The contact POINTS are the one field that only holds to a few ULP, and this is a fact
                // about the arithmetic rather than a slack assertion (#87). Sutherland–Hodgman face clipping
                // does not SELECT an endpoint, it lerps to one: `t = d0 / (d0 - d1)`, then `q0 + (q1 - q0)*t`.
                // `t` is a rounded quotient, and `q0 + (q1 - q0)*t` rounds differently at different
                // magnitudes — which the translation changes. So exactness of the INPUTS buys nothing, and
                // the clip is not translation-equivariant in IEEE-754 for integer coordinates either.
                // A worked counterexample is pinned as the regression case below.
                //
                // Rotated pairs are deliberately excluded, and not because the id is unstable: under
                // rotation the SAT scan's own axis choice is ULP-sensitive when two axes' penetrations
                // tie, so a translation can flip the CONTACT — normal, points and all — to the opposite
                // face. `polygonContact.Normal` flips with it, from the same `satScan` line. That is the
                // tie-break sensitivity the suite already documents, not a property of contact points.
                let prop a b c d e f g h (tx: int) (ty: int) =
                    let x, _ = obbOf a b c d
                    let y, _ = obbOf e f g h
                    let dx, dy = float (tx % 100), float (ty % 100)
                    let shift (poly: ConvexPolygon) =
                        { Vertices = poly.Vertices |> Array.map (fun v -> p (v.X + dx) (v.Y + dy)) }
                    match Geometry.polygonManifold x y, Geometry.polygonManifold (shift x) (shift y) with
                    | ValueSome m, ValueSome m' ->
                        m.FeatureId = m'.FeatureId
                        && m.PointCount = m'.PointCount
                        && m.Normal = m'.Normal
                        && m.Depth = m'.Depth
                        && nearPoints (m.Points |> Array.map (fun q -> p (q.X + dx) (q.Y + dy))) m'.Points
                    | ValueNone, ValueNone -> true
                    | _ -> false
                Check.One((seeded 1UL).WithMaxTest 500, prop)

            testCase "#87: the translated manifold keeps every exact claim, and its points to a few ULP" <| fun () ->
                // The counterexample from #87, pinned as a deterministic regression case. The property above
                // exercises this class of drift under its seed, but which pair it draws depends on both that
                // seed and the generator; this case depends on neither.
                //
                // Here `polygonManifold` clips the box x in [3,5], y in [-50,-24] against x in [-2,14],
                // y in [-35,-21], giving contacts at (5,-35) and (5,-24). Translated by (43,48), the second
                // is exact and the first lands one ULP low — because (5,-24) is a VERTEX of the incident
                // face, which the clip reproduces bit-for-bit, while (5,-35) is an INTERIOR CROSSING, where
                // the clip lerps with a rounded `t`. That asymmetry is why snapping the clip to its
                // endpoints (issue option 1) would not have rescued the old exact-equality assertion.
                //
                // The assertions state only what is true of ANY correct clip, so a future exactly
                // translation-equivariant one (drift 0) still passes.
                let x, _ = obbOf 56 (-28) 47 46
                let y, _ = obbOf 54 (-37) (-20) (-12)
                let dx, dy = 43.0, 48.0
                let shift (poly: ConvexPolygon) =
                    { Vertices = poly.Vertices |> Array.map (fun v -> p (v.X + dx) (v.Y + dy)) }
                match Geometry.polygonManifold x y, Geometry.polygonManifold (shift x) (shift y) with
                | ValueSome m, ValueSome m' ->
                    Expect.equal m'.FeatureId m.FeatureId "feature id survives the translation exactly"
                    Expect.equal m'.PointCount m.PointCount "point count survives the translation exactly"
                    Expect.equal m'.Normal m.Normal "normal survives the translation exactly"
                    Expect.equal m'.Depth m.Depth "depth survives the translation exactly"
                    let expected = m.Points |> Array.map (fun q -> p (q.X + dx) (q.Y + dy))
                    Expect.isTrue
                        (nearPoints expected m'.Points)
                        $"contact points agree to a few ULP: expected %A{expected}, got %A{m'.Points}"
                | _ -> failtest "both manifolds should exist for this pair"

            // Every property above rides on OBBs, and an OBB cannot see the failure this one can: a box's
            // faces are axis-aligned and antiparallel in pairs, so the reference face is always exactly
            // the SAT axis and the incident face always the box face pointing back at it. On a triangle
            // or a pentagon neither holds. These generate genuine strictly-convex CCW rings — via a
            // monotone-chain hull, verified vertex-by-vertex, so a self-intersecting "hull" cannot sneak
            // in and manufacture a false failure — and are where the reference-face query earns its keep.
            testCase "arbitrary convex rings: points on the boundary of both shapes (FsCheck ≥500)" <| fun () ->
                // Andrew's monotone chain; the `<= 0.0` pop drops collinear points, so the ring is strict.
                let hull (pts: Point[]) : Point[] =
                    let ps = pts |> Array.distinctBy (fun q -> q.X, q.Y) |> Array.sortBy (fun q -> q.X, q.Y)
                    if ps.Length < 3 then
                        [||]
                    else
                        let cross (o: Point) (u: Point) (w: Point) =
                            (u.X - o.X) * (w.Y - o.Y) - (u.Y - o.Y) * (w.X - o.X)
                        let half (src: Point[]) =
                            let st = ResizeArray<Point>()
                            for q in src do
                                while st.Count >= 2 && cross st.[st.Count - 2] st.[st.Count - 1] q <= 0.0 do
                                    st.RemoveAt(st.Count - 1)
                                st.Add q
                            st.RemoveAt(st.Count - 1)
                            st
                        Seq.append (half ps) (half (Array.rev ps)) |> Seq.toArray
                // Every vertex strictly left of every directed edge ⇒ strictly convex, CCW, and simple.
                let strictlyConvexCcw (v: Point[]) =
                    v.Length >= 3
                    && Seq.init v.Length (fun i ->
                        let s, e = v.[i], v.[(i + 1) % v.Length]
                        Seq.init v.Length (fun j ->
                            j = i
                            || j = (i + 1) % v.Length
                            || (e.X - s.X) * (v.[j].Y - s.Y) - (e.Y - s.Y) * (v.[j].X - s.X) > 1e-9)
                        |> Seq.forall id)
                       |> Seq.forall id
                let distToBoundary (poly: ConvexPolygon) (q: Point) =
                    let v = poly.Vertices
                    Seq.init v.Length (fun i ->
                        let s, e = v.[i], v.[(i + 1) % v.Length]
                        let ex, ey = e.X - s.X, e.Y - s.Y
                        let t = max 0.0 (min 1.0 (((q.X - s.X) * ex + (q.Y - s.Y) * ey) / (ex * ex + ey * ey)))
                        let cx, cy = s.X + ex * t, s.Y + ey * t
                        sqrt ((q.X - cx) * (q.X - cx) + (q.Y - cy) * (q.Y - cy)))
                    |> Seq.min
                let prop (seeds: int[]) (tx: int) (ty: int) =
                    // Build one hull from the seeds, and a second from the same seeds shifted, so both
                    // rings are non-degenerate and the pair usually overlaps.
                    let pt (i: int) = p (float (i % 17) * 0.37) (float ((i / 17) % 17) * 0.41)
                    if seeds.Length < 6 then
                        true
                    else
                        let ha = hull (seeds |> Array.map pt)
                        let dx, dy = float (tx % 5) * 0.3, float (ty % 5) * 0.3
                        let hb = hull (seeds |> Array.rev |> Array.map (fun i -> let q = pt (i + 3) in p (q.X + dx) (q.Y + dy)))
                        if not (strictlyConvexCcw ha && strictlyConvexCcw hb) then
                            true
                        else
                            let a, b = { Vertices = ha }, { Vertices = hb }
                            match Geometry.polygonManifold a b with
                            | ValueNone -> true
                            | ValueSome m ->
                                m.PointCount >= 1
                                && m.Points
                                   |> Array.forall (fun q ->
                                       let da, db = distToBoundary a q, distToBoundary b q
                                       min da db <= 1e-6 && max da db <= m.Depth + 1e-6)
                Check.One(Config.QuickThrowOnFailure.WithMaxTest 500, prop)

            test "degenerate inputs return ValueNone, never throw (total)" {
                Expect.isTrue
                    (Geometry.polygonManifold { Vertices = [| p 0. 0.; p 1. 0. |] } (obb 0. 0. 1. 1.) |> ValueOption.isNone)
                    "<3 vertices ⇒ ValueNone"
                Expect.isTrue
                    (Geometry.polygonManifold { Vertices = [| p nan 0.; p 1. 0.; p 0. 1. |] } (obb 0. 0. 1. 1.) |> ValueOption.isNone)
                    "NaN coordinate ⇒ ValueNone"
                Expect.isTrue
                    (Geometry.polygonManifold { Vertices = [| p 0. 0.; p 1. 0.; p 2. 0. |] } (obb 0. 0. 1. 1.) |> ValueOption.isNone)
                    "collinear/zero-area ⇒ ValueNone"
            }

            // Determinism golden — fixed inputs, exact expected manifolds (byte-identical). Named
            // "determinism golden" so the gate.yml zero-match guard's determinism filter covers them.
            testList "determinism golden (contact points and feature ids)" [
                test "face-on-face ⇒ two points on the shared face, feature id (refEdge 1, incEdge 3)" {
                    // The unit square at the origin, overlapped 0.5 from the right. a's right face (edge 1)
                    // is the reference — it is the axis itself — and b's left face (edge 3) is the incident
                    // one. The incident face survives both side clips whole: its endpoints ARE the contact.
                    Expect.equal (Geometry.polygonManifold (obb 0. 0. 1. 1.) (obb 1.5 0. 1. 1.))
                        (ValueSome
                            { A = 0
                              B = 1
                              Normal = p 1. 0.
                              Depth = 0.5
                              Points = [| p 0.5 1.; p 0.5 -1. |]
                              PointCount = 2
                              FeatureId = (1 <<< 15) ||| 3 })
                        "two points, +x normal, depth 0.5, refEdge 1 / incEdge 3"
                }
                test "vertex-into-face ⇒ one point, the poking vertex" {
                    // A 45°-rotated square centred at (2,0): its left vertex reaches x = 2 − √2 ≈ 0.586 and
                    // pokes into the unit square's right face. Only one clip survivor is behind that face,
                    // so the manifold carries a single contact point — the vertex itself.
                    let diamond = Geometry.obbPolygon (p 2. 0.) (p 1. 1.) (System.Math.PI / 4.0)
                    match Geometry.polygonManifold (obb 0. 0. 1. 1.) diamond with
                    | ValueNone -> failtest "rotated overlap ⇒ ValueSome"
                    | ValueSome m ->
                        Expect.equal m.PointCount 1 "a vertex poking into a face is one contact"
                        Expect.floatClose Accuracy.high m.Points.[0].X (2.0 - sqrt 2.0) "the diamond's left vertex, x"
                        Expect.floatClose Accuracy.high m.Points.[0].Y 0.0 "the diamond's left vertex, y"
                        Expect.floatClose Accuracy.high m.Depth (sqrt 2.0 - 1.0) "penetration past x = 1"
                        Expect.equal m.Normal (p 1. 0.) "+x, a → b"
                        Expect.equal m.FeatureId ((1 <<< 15) ||| 2) "a's right face (1), the diamond's upper-left face (2)"
                }
                test "the reference face flips to b when only b has a face on the axis" {
                    // Swap the previous pair: the diamond is now `a`, the square `b`. The least-penetration
                    // axis is the square's right face, and the diamond has no face parallel to it, so `b`
                    // is the reference polygon and the flip bit (30) is set. Note that swapping the two
                    // *boxes* of the previous golden would NOT flip: a's left face is antiparallel to b's
                    // right one, the alignment ties at 1.0, and a tie keeps `a` as the reference.
                    let diamond = Geometry.obbPolygon (p 2. 0.) (p 1. 1.) (System.Math.PI / 4.0)
                    match Geometry.polygonManifold diamond (obb 0. 0. 1. 1.) with
                    | ValueNone -> failtest "rotated overlap ⇒ ValueSome"
                    | ValueSome m ->
                        Expect.equal m.FeatureId ((1 <<< 30) ||| (1 <<< 15) ||| 2) "flip bit, refEdge 1 (b), incEdge 2 (a)"
                        Expect.equal m.Normal (p -1. 0.) "−x, a → b"
                        Expect.equal m.PointCount 1 "the same single vertex contact, seen from the other side"
                        Expect.floatClose Accuracy.high m.Points.[0].X (2.0 - sqrt 2.0) "the diamond's left vertex, x"
                        Expect.floatClose Accuracy.high m.Depth (sqrt 2.0 - 1.0) "penetration past x = 1"
                }
                test "touching squares are not a contact (strict edges)" {
                    Expect.isTrue
                        (Geometry.polygonManifold (obb 0. 0. 1. 1.) (obb 2. 0. 1. 1.) |> ValueOption.isNone)
                        "shared edge ⇒ zero overlap ⇒ ValueNone"
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
                        // The point sits on the entered edge's line, but only to within the corner
                        // tolerance: at a corner entry T is the true max while the normal is the first
                        // tied edge's, so the point may lie up to cornerTie × |segment| off that edge.
                        let segLen = sqrt ((p1.X - p0.X) ** 2.0 + (p1.Y - p0.Y) ** 2.0)
                        let onSomeEdgeLine =
                            poly.Vertices
                            |> Array.exists (fun v ->
                                abs ((hit.Point.X - v.X) * hit.Normal.X + (hit.Point.Y - v.Y) * hit.Normal.Y) < 1e-9 * (1.0 + segLen))
                        let inward = (p1.X - p0.X) * hit.Normal.X + (p1.Y - p0.Y) * hit.Normal.Y < 0.0
                        hit.T >= 0.0 && hit.T <= 1.0 && isUnit hit.Normal && onSomeEdgeLine && inward
                    | None -> true
                Check.One(Config.QuickThrowOnFailure.WithMaxTest 500, prop)

            testCase "a transversal entry parameter is invariant under rotating the whole scene (FsCheck ≥500)" <| fun () ->
                // Rotating p0, p1 and the polygon about the origin by θ maps the rotation-0 OBB to
                // `obbPolygon (R θ centre) h θ`, so the cast is a rigid motion and T must not move.
                //
                // The precondition — p0 strictly outside and p1 strictly inside, both by a margin — is not
                // decoration. Without it the claim is FALSE, and not merely by a rounding: a segment lying
                // along an edge enters at a corner with its chord running down the boundary, and rotation
                // nudges it off that edge line to one side or the other, so the entry jumps between the
                // corner and the far endpoint. A graze (chord = a single point) flips `isSome` the same
                // way. Both are measure-zero knife-edges on the strict `<` that no strict test can smooth,
                // and both are pinned as goldens below. The margin excludes them: a robustly-inside p1
                // forces a chord of positive length, a robustly-outside p0 a real crossing. Over the whole
                // integer domain this leaves 320,580 eligible cases, every one a hit in both frames with
                // max |ΔT| = 8.9e-16 — so `1e-9` is slack, not a fudge.
                let signedDist (poly: ConvexPolygon) (q: Point) =
                    // Greatest outward signed distance over the edges: < 0 inside every edge, > 0 outside one.
                    poly.Vertices
                    |> Array.mapi (fun k v ->
                        let w = poly.Vertices.[(k + 1) % poly.Vertices.Length]
                        let ex, ey = w.X - v.X, w.Y - v.Y
                        let len = sqrt (ex * ex + ey * ey)
                        (q.X - v.X) * (ey / len) + (q.Y - v.Y) * (-ex / len))
                    |> Array.max
                let prop a b c d e f g h i =
                    let p0 = p (float (a % 50)) (float (b % 50))
                    let p1 = p (float (c % 50)) (float (d % 50))
                    let centre = p (float (e % 50)) (float (f % 50))
                    let half = p (float (1 + abs (g % 20))) (float (1 + abs (h % 20)))
                    let theta = float (i % 13) * 0.5
                    let co, si = cos theta, sin theta
                    let rot (q: Point) : Point = { X = q.X * co - q.Y * si; Y = q.X * si + q.Y * co }
                    let flatPoly = Geometry.obbPolygon centre half 0.0
                    let spunPoly = Geometry.obbPolygon (rot centre) half theta
                    if not (signedDist flatPoly p0 > 1e-3 && signedDist flatPoly p1 < -1e-3) then
                        true // not a clean transversal cast — the knife-edges are the goldens' business
                    else
                        match Geometry.segmentPolygonHit p0 p1 flatPoly,
                              Geometry.segmentPolygonHit (rot p0) (rot p1) spunPoly with
                        | Some x, Some y -> abs (x.T - y.T) < 1e-9
                        | _ -> false // outside-to-inside must hit, in both frames
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
                test "a graze is a knife-edge: rotating the scene can turn it into a hit" {
                    // FsCheck found this. The segment passes exactly through the corner (2,-4) of the
                    // rotation-0 box, so the chord is a point and strict edges reject it. Rotated by 0.5
                    // rad the same coincidence survives only to an ULP, and the cast enters. T is invariant
                    // (1/3); isSome is not, and no strict test can make it so — the configuration is
                    // measure-zero. Documented here so the asymmetry is a decision, not a surprise.
                    let p0, p1 = p 3. -3., p 0. -6.
                    let theta = 0.5
                    let co, si = cos theta, sin theta
                    let rot (q: Point) : Point = { X = q.X * co - q.Y * si; Y = q.X * si + q.Y * co }
                    Expect.isNone (Geometry.segmentPolygonHit p0 p1 (Geometry.obbPolygon (p 0. -2.) (p 2. 2.) 0.0))
                        "the flat cast clips the corner (2,-4) exactly ⇒ graze ⇒ None"
                    match Geometry.segmentPolygonHit (rot p0) (rot p1) (Geometry.obbPolygon (rot (p 0. -2.)) (p 2. 2.) theta) with
                    | Some hit ->
                        Expect.floatClose Accuracy.high hit.T (1.0 / 3.0) "the rotated cast enters at the same T"
                        Expect.floatClose Accuracy.high hit.Point.X (rot (p 2. -4.)).X "…at the rotated corner"
                        Expect.floatClose Accuracy.high hit.Point.Y (rot (p 2. -4.)).Y "…at the rotated corner"
                    | None -> failtest "expected the rotated graze to fall on the hit side"
                }
                test "a boundary-tangent segment is a knife-edge: rotating the scene moves T" {
                    // FsCheck found this too, and it is the sharper of the two. The segment x = 1 lies
                    // exactly along the right edge of the box [-9,1]x[-1,1]: it enters at the corner
                    // (1,-1) and its chord runs down the boundary, so T = 2/3. Rotated by 2 rad the
                    // segment falls a rounding to the outside of that edge line and only touches at its
                    // own endpoint, T = 1. So T is invariant for TRANSVERSAL casts, not for tangent ones —
                    // the rotation property is preconditioned on p0 outside and p1 inside for this reason.
                    let p0, p1 = p 1. -3., p 1. 0.
                    let centre, half = p -4. 0., p 5. 1.
                    let theta = 2.0
                    let co, si = cos theta, sin theta
                    let rot (q: Point) : Point = { X = q.X * co - q.Y * si; Y = q.X * si + q.Y * co }
                    match Geometry.segmentPolygonHit p0 p1 (Geometry.obbPolygon centre half 0.0) with
                    | Some hit ->
                        Expect.equal hit.T (2.0 / 3.0) "flat: enters at the corner it reaches along the edge line"
                        Expect.equal hit.Point (p 1. -1.) "flat: the corner"
                        Expect.equal hit.Normal (p 0. -1.) "flat: the bottom edge it crossed to reach the boundary"
                    | None -> failtest "expected the tangent flat cast to enter at the corner"
                    match Geometry.segmentPolygonHit (rot p0) (rot p1) (Geometry.obbPolygon (rot centre) half theta) with
                    | Some hit -> Expect.floatClose Accuracy.high hit.T 1.0 "rotated: nudged outside, touches only at its endpoint"
                    | None -> failtest "expected the tangent rotated cast to touch at its endpoint"
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
