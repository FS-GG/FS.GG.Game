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
    ]
