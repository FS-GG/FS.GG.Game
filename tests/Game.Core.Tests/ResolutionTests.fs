module Game.Core.Tests.ResolutionTests

// Collision-response capsule (006) — the arcade/kinematic Resolution layer: pushOut (separate along
// the MTV), slide (kill the normal velocity component, keep the tangential), push (discrete grid
// displacement that reports why it stopped) and its deprecated predecessor knockback. Response is a
// layer separate from detection: these consume the Contact/Cell values Geometry and the grid produce.
// Pure, total, byte-deterministic.

// FS0044 (use of an [<Obsolete>] construct), file-scoped. `knockback` is deprecated in favour of
// `push`, and the ONE thing that justifies keeping it on the surface is that it is exactly equal to
// `(push …).Final` on every input. Proving that requires calling it. Suppressed here and nowhere else,
// so a real caller of the deprecated shim still fails the warnings-as-errors build.
#nowarn "44"

open Expecto
open FsCheck
open FS.GG.Game.Core

let private p x y : Point = { X = x; Y = y }
let private cell c r : Cell = { Col = c; Row = r }

let private east = cell 1 0
let private allEnter (_: Cell) = Resolution.Enter

// A turn-based-tactics board, read left to right: lava at cols 1–2, water at col 3, wall from col 6.
let private isLava (c: Cell) = c.Col = 1 || c.Col = 2

let private tacticsBoard (c: Cell) =
    if c.Col >= 6 then Resolution.Block
    elif c.Col = 3 then Resolution.Stop
    else Resolution.Enter

/// The whole of turn-based-tactics §4.6, on the board above: environmental damage folds over
/// `Entered`, and the unit's fate is a match on `Outcome`. `Game.Core` supplies the walk; the game
/// supplies every bit of the meaning. Returns the cell occupied, the surviving HP, and the fate.
let private resolvePush (start: Cell) (distance: int) (hp: int) =
    let lavaTick = 3
    let r = Resolution.push start east distance tacticsBoard
    let burned = hp - lavaTick * (r.Entered |> List.filter isLava |> List.length)

    match r.Outcome with
    | Resolution.Stopped _ -> r.Final, 0, "drowned" // water is fatal regardless of HP
    | Resolution.Blocked _ -> r.Final, burned - 1, "collision" // 1 collision damage to both
    | Resolution.Completed -> r.Final, burned, "shoved"

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

        // ---------------------------------------------------------------------------------------
        // `push` — the re-expression. A binary `blocked` predicate has two answers; terrain has three.

        testList "push (discrete grid, three relations and an observable stop reason)" [

            test "an unobstructed push takes every step and reports Completed" {
                let r = Resolution.push (cell 0 0) east 3 allEnter

                Expect.equal r.Final (cell 3 0) "three steps east"
                Expect.equal r.Outcome Resolution.Completed "nothing interrupted it"
                Expect.equal r.Entered [ cell 1 0; cell 2 0; cell 3 0 ] "the cells entered, in order, excluding start"
            }

            test "Block stops the unit on the PREVIOUS cell, and names the obstacle" {
                let wall (c: Cell) = if c.Col = 2 then Resolution.Block else Resolution.Enter
                let r = Resolution.push (cell 0 0) east 3 wall

                Expect.equal r.Final (cell 1 0) "the unit never enters the wall"
                Expect.equal r.Outcome (Resolution.Blocked(cell 2 0)) "and the wall is named, so collision damage is attributable"
                Expect.equal r.Entered [ cell 1 0 ] "only the cell it actually entered"
            }

            test "Stop moves the unit ONTO the cell and stops it there — the relation `blocked` cannot express" {
                let water (c: Cell) = if c.Col = 2 then Resolution.Stop else Resolution.Enter
                let r = Resolution.push (cell 0 0) east 3 water

                Expect.equal r.Final (cell 2 0) "the unit is IN the water, not on the bank"
                Expect.equal r.Outcome (Resolution.Stopped(cell 2 0)) "and it halted there"
                Expect.equal r.Entered [ cell 1 0; cell 2 0 ] "the water is an entered cell"
            }

            test "Enter is passable-and-continue: a hazard is crossed, and every crossing is in Entered" {
                // A per-cell hazard tick is a fold over `Entered` — the part `knockback` threw away.
                let r = Resolution.push (cell 0 0) east 3 tacticsBoard

                Expect.equal (r.Entered |> List.filter isLava) [ cell 1 0; cell 2 0 ] "both lava cells were entered"
                Expect.equal r.Outcome (Resolution.Stopped(cell 3 0)) "and it came to rest in the water beyond them"
            }

            test "an immediately-blocked first step keeps the start and enters nothing" {
                let r = Resolution.push (cell 0 0) east 3 (fun c -> if c.Col = 1 then Resolution.Block else Resolution.Enter)

                Expect.equal r.Final (cell 0 0) "stayed"
                Expect.equal r.Outcome (Resolution.Blocked(cell 1 0)) "the adjacent cell refused"
                Expect.isEmpty r.Entered "nothing entered"
            }

            test "a non-positive distance is Completed at the start — a Massive unit is pushed 0" {
                [ 0; -3 ]
                |> List.iter (fun d ->
                    let r = Resolution.push (cell 5 5) east d allEnter
                    Expect.equal r.Final (cell 5 5) $"distance {d} ⇒ start"
                    Expect.equal r.Outcome Resolution.Completed $"distance {d} ⇒ Completed, unobstructed"
                    Expect.isEmpty r.Entered $"distance {d} ⇒ nothing entered")
            }

            test "classify is never asked about the start cell — it is assumed already occupied" {
                let asked = ResizeArray<Cell>()

                let record (c: Cell) =
                    asked.Add c
                    Resolution.Enter

                Resolution.push (cell 0 0) east 2 record |> ignore
                Expect.sequenceEqual asked [ cell 1 0; cell 2 0 ] "only next cells are classified"
            }

            test "a zero step re-enters the same cell and still terminates, bounded by distance" {
                let r = Resolution.push (cell 4 4) (cell 0 0) 3 allEnter

                Expect.equal r.Final (cell 4 4) "it never moves"
                Expect.equal r.Outcome Resolution.Completed "but the walk is bounded and total"
                Expect.equal r.Entered [ cell 4 4; cell 4 4; cell 4 4 ] "three degenerate entries"
            }

            testCase "Final is never a Blocked cell, and Entered's length never exceeds distance (FsCheck ≥500)"
            <| fun () ->
                let prop (sc: int) (sr: int) (dist: int) (w: int) (h: int) =
                    let start = cell (sc % 50) (sr % 50)
                    let distance = abs (dist % 15)
                    let wall = start.Col + 1 + abs (w % 15) // strictly right of start
                    let halt = start.Col + 1 + abs (h % 15)

                    let classify (c: Cell) =
                        if c.Col >= wall then Resolution.Block
                        elif c.Col >= halt then Resolution.Stop
                        else Resolution.Enter

                    let r = Resolution.push start (cell 1 0) distance classify

                    // The unit never ends up standing in a cell that refused it.
                    let neverInsideABlocker = classify r.Final <> Resolution.Block || r.Final = start

                    // The walk is bounded by `distance`, and `Entered` is a contiguous run from `start`.
                    let bounded = List.length r.Entered <= distance

                    let contiguous =
                        r.Entered |> List.mapi (fun i c -> c = cell (start.Col + i + 1) start.Row) |> List.forall id

                    // `Final` is the last cell entered, or `start` when nothing was.
                    let finalIsLastEntered =
                        match r.Entered with
                        | [] -> r.Final = start
                        | entered -> r.Final = List.last entered

                    // And `Stop` says exactly what happened.
                    let stopAgrees =
                        match r.Outcome with
                        | Resolution.Completed -> List.length r.Entered = distance
                        | Resolution.Stopped c -> r.Final = c && classify c = Resolution.Stop
                        | Resolution.Blocked c -> r.Final <> c && classify c = Resolution.Block

                    neverInsideABlocker && bounded && contiguous && finalIsLastEntered && stopAgrees

                Check.One(Config.QuickThrowOnFailure.WithMaxTest 500, prop)
        ]

        // ---------------------------------------------------------------------------------------
        // The one thing that justifies keeping `knockback` on the public surface.

        testList "knockback is EXACTLY the push shim (so the surface change is additive)" [

            testCase "knockback start step distance blocked = (push … ).Final, for every input (FsCheck ≥500)"
            <| fun () ->
                let prop (sc: int) (sr: int) (stc: int) (str: int) (dist: int) (w: int) =
                    let start = cell (sc % 50) (sr % 50)
                    let step = cell (stc % 3 - 1) (str % 3 - 1) // includes the degenerate (0,0)
                    let distance = dist % 15 // includes negatives
                    let blocked (c: Cell) = (abs c.Col + abs c.Row) % (1 + abs (w % 7)) = 0

                    let viaShim = Resolution.knockback start step distance blocked

                    let viaPush =
                        (Resolution.push start step distance (fun c -> if blocked c then Resolution.Block else Resolution.Enter)).Final

                    viaShim = viaPush

                Check.One(Config.QuickThrowOnFailure.WithMaxTest 500, prop)
        ]

        // ---------------------------------------------------------------------------------------
        // turn-based-tactics.md §14 AC#4: a Bruiser shoved into Water DIES, "regardless of its 3 HP".
        // Not implementable with `knockback` — mark Water blocked and the unit stops on dry land and
        // lives; mark it passable and it walks out the far side. This is the acceptance criterion the
        // re-expression exists to make expressible.

        testList "turn-based-tactics §4.6 falls out of a match on Outcome" [

            test "AC#4 — a Bruiser with 3 HP shoved into Water dies, regardless of its HP" {
                let final, hp, fate = resolvePush (cell 2 0) 2 3

                Expect.equal fate "drowned" "entered the water and stopped there"
                Expect.equal final (cell 3 0) "it is IN the water"
                Expect.equal hp 0 "dead — not 'took 0 damage and stood on the bank'"
            }

            test "a unit shoved across two lava tiles takes the tick twice, then keeps going" {
                let final, hp, fate = resolvePush (cell 0 0) 2 10

                Expect.equal fate "shoved" "lava is entered and left"
                Expect.equal final (cell 2 0) "walked across"
                Expect.equal hp 4 "10 − 3 − 3 — the tick folds over Entered, which knockback threw away"
            }

            test "a unit shoved into a wall stops before it and takes 1 collision damage" {
                let final, hp, fate = resolvePush (cell 4 0) 3 5

                Expect.equal fate "collision" "the wall refused entry"
                Expect.equal final (cell 5 0) "stopped on the last free cell"
                Expect.equal hp 4 "5 − 1 collision; no lava on this path"
            }

            test "a Flying unit hovers over the water, because classify is the caller's" {
                let flying (c: Cell) = if c.Col >= 6 then Resolution.Block else Resolution.Enter
                let r = Resolution.push (cell 2 0) east 2 flying

                Expect.equal r.Outcome Resolution.Completed "no halt: for a flier, water is Enter"
                Expect.equal r.Final (cell 4 0) "it crossed"
            }

            test "a Massive unit is unpushable — distance 0" {
                let r = Resolution.push (cell 2 0) east 0 tacticsBoard
                Expect.equal r.Final (cell 2 0) "unmoved, and it never touched the water"
                Expect.equal r.Outcome Resolution.Completed "Completed, vacuously"
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
            test "push across lava into water is byte-identical" {
                Expect.equal
                    (Resolution.push (cell 0 0) east 4 tacticsBoard)
                    { Entered = [ cell 1 0; cell 2 0; cell 3 0 ]
                      Final = cell 3 0
                      Outcome = Resolution.Stopped(cell 3 0) }
                    "two lava cells entered, then halted in the water at col 3"
            }
            test "push into a wall is byte-identical" {
                Expect.equal
                    (Resolution.push (cell 4 0) east 3 tacticsBoard)
                    { Entered = [ cell 5 0 ]
                      Final = cell 5 0
                      Outcome = Resolution.Blocked(cell 6 0) }
                    "stopped on col 5; the wall at col 6 is named"
            }
        ]
    ]
