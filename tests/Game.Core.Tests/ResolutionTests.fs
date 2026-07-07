module Game.Core.Tests.ResolutionTests

// Collision-response capsule (006) — the arcade/kinematic Resolution layer: pushOut (separate along
// the MTV), slide (kill the normal velocity component, keep the tangential), knockback (discrete grid
// displacement stopped by a blocker). Response is a layer separate from detection: these consume the
// Contact/Cell values Geometry and the grid produce. Pure, total, byte-deterministic.

open Expecto
open FsCheck
open FS.GG.Game.Core

let private p x y : Point = { X = x; Y = y }
let private cell c r : Cell = { Col = c; Row = r }

// Two ints -> a rect with a positive size, bounded so overlaps actually happen (mirrors GeometryTests).
let private rectOf (a: int) (b: int) (c: int) (d: int) : Rect =
    { X = float (a % 50)
      Y = float (b % 50)
      Width = float (1 + abs (c % 20))
      Height = float (1 + abs (d % 20)) }

[<Tests>]
let tests =
    testList "Game.Core Resolution (response layer, FR-001..FR-005)" [

        testList "pushOut (separate along the MTV)" [

            testCase "pushing a body out by its aabbContact manifold removes the overlap (FsCheck ≥500)" <| fun () ->
                // The consumer contract: detection (aabbContact) → response (pushOut) separates. Move a's
                // min-corner by pushOut(origin, contact); the translated rect no longer intersects b.
                let prop a b c d e f g h =
                    let x = rectOf a b c d
                    let y = rectOf e f g h
                    match Geometry.aabbContact x y with
                    | Some contact ->
                        let moved = Resolution.pushOut (p x.X x.Y) contact
                        not (Geometry.intersects { x with X = moved.X; Y = moved.Y } y)
                    | None -> true
                Check.One(Config.QuickThrowOnFailure.WithMaxTest 500, prop)

            test "a zero-depth contact leaves the position unchanged" {
                Expect.equal (Resolution.pushOut (p 5. 5.) { Normal = p 1. 0.; Depth = 0. }) (p 5. 5.) "zero depth ⇒ identity"
            }

            test "NaN position flows through without throwing (total)" {
                let got = Resolution.pushOut (p nan 0.) { Normal = p 1. 0.; Depth = 2. }
                Expect.isTrue (System.Double.IsNaN got.X) "NaN flows through, no throw"
            }
        ]

        testList "slide (kill normal, keep tangential)" [

            testCase "result has zero normal component and preserves the tangential (FsCheck ≥500)" <| fun () ->
                let prop (vx: int) (vy: int) (deg: int) =
                    let v = p (float (vx % 100)) (float (vy % 100))
                    let th = float (deg % 360) * System.Math.PI / 180.0
                    let n = p (cos th) (sin th) // unit normal
                    let r = Resolution.slide v n
                    let normalComp = r.X * n.X + r.Y * n.Y
                    // tangential component preserved: project onto the tangent (-n.Y, n.X)
                    let tx, ty = -n.Y, n.X
                    let rt = r.X * tx + r.Y * ty
                    let vt = v.X * tx + v.Y * ty
                    abs normalComp < 1e-9 && abs (rt - vt) < 1e-9
                Check.One(Config.QuickThrowOnFailure.WithMaxTest 500, prop)

            test "NaN velocity flows through without throwing (total)" {
                let got = Resolution.slide (p nan 1.) (p 1. 0.)
                Expect.isTrue (System.Double.IsNaN got.X) "NaN flows through, no throw"
            }
        ]

        testList "knockback (discrete grid, stop before a blocker)" [

            testCase "never lands on a blocked cell and never exceeds distance (FsCheck ≥500)" <| fun () ->
                // step (1,0), a wall at column `wall` strictly right of start; the result stops before it
                // and within `distance` steps.
                let prop (sc: int) (sr: int) (dist: int) (w: int) =
                    let start = cell (sc % 50) (sr % 50)
                    let distance = abs (dist % 15)
                    let wall = start.Col + 1 + abs (w % 15) // strictly right of start ⇒ start is free
                    let blocked (c: Cell) = c.Col >= wall
                    let got = Resolution.knockback start (cell 1 0) distance blocked
                    not (blocked got)
                    && got.Col - start.Col <= distance
                    && got.Col >= start.Col
                Check.One(Config.QuickThrowOnFailure.WithMaxTest 500, prop)

            test "distance ≤ 0 returns the start unchanged" {
                Expect.equal (Resolution.knockback (cell 5 5) (cell 1 0) 0 (fun _ -> false)) (cell 5 5) "distance 0 ⇒ start"
                Expect.equal (Resolution.knockback (cell 5 5) (cell 1 0) -3 (fun _ -> false)) (cell 5 5) "negative distance ⇒ start"
            }

            test "an immediately-blocked first step returns the start" {
                Expect.equal (Resolution.knockback (cell 0 0) (cell 1 0) 3 (fun c -> c.Col = 1)) (cell 0 0) "next cell blocked ⇒ stay"
            }
        ]

        // Determinism golden — fixed inputs, exact expected transforms (byte-identical). Named
        // "determinism golden" so the gate.yml zero-match guard's determinism filter covers them.
        testList "determinism golden (resolution transforms)" [
            test "pushOut separates along +x by the depth" {
                Expect.equal (Resolution.pushOut (p 5. 5.) { Normal = p 1. 0.; Depth = 2. }) (p 3. 5.) "pos - normal*depth"
            }
            test "slide against a vertical wall kills X, keeps Y" {
                Expect.equal (Resolution.slide (p 3. 4.) (p 1. 0.)) (p 0. 4.) "normal (1,0) ⇒ (0,4)"
            }
            test "slide against a floor kills Y, keeps X" {
                Expect.equal (Resolution.slide (p 3. 4.) (p 0. 1.)) (p 3. 0.) "normal (0,1) ⇒ (3,0)"
            }
            test "knockback stops in the last free cell before a wall" {
                Expect.equal (Resolution.knockback (cell 0 0) (cell 1 0) 3 (fun c -> c.Col = 2)) (cell 1 0) "wall at col 2 ⇒ stop at (1,0)"
            }
            test "knockback with no blocker travels the full distance" {
                Expect.equal (Resolution.knockback (cell 0 0) (cell 1 0) 3 (fun _ -> false)) (cell 3 0) "no wall ⇒ (3,0)"
            }
        ]
    ]
