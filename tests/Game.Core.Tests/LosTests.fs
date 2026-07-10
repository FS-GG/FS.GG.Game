module Game.Core.Tests.LosTests

// Promoted from the FS.GG.Rendering `line-drawing` product fragment (FS.GG.Game#32), which shipped
// with no tests at all. Grid line-of-sight: integer Bresenham (thin) and the 4-connected supercover
// walk behind a `LineMode` policy, over a `Cell -> bool` opacity predicate.
//
// Test priorities follow docs/reports/2026-07-05-game-logic-line-of-sight-design.md §7: the symmetry
// property first (it catches the entire asymmetry class), then the diagonal wall-join case (§2's "#1
// LOS bug"), then determinism goldens, endpoint rules, and full-`int`-domain totality.

open System
open Expecto
open FS.GG.Game.Core

let private cell c r = { Col = c; Row = r }

/// An opacity predicate over an explicit blocked set — the predicate IS the map.
let private transparentExcept (blocked: Set<int * int>) (c: Cell) = not (blocked.Contains(c.Col, c.Row))

/// Every consecutive pair of a supercover walk differs by exactly 1 in exactly one axis.
let private isFourConnected (cells: Cell list) =
    cells
    |> List.pairwise
    |> List.forall (fun (p, q) ->
        let dc = abs (q.Col - p.Col)
        let dr = abs (q.Row - p.Row)
        dc + dr = 1)

/// Every consecutive pair of a thin walk differs by at most 1 in each axis, and at least one moves.
let private isEightConnected (cells: Cell list) =
    cells
    |> List.pairwise
    |> List.forall (fun (p, q) ->
        let dc = abs (q.Col - p.Col)
        let dr = abs (q.Row - p.Row)
        dc <= 1 && dr <= 1 && dc + dr > 0)

/// The cells a walk consults for sight: everything strictly between the endpoints.
let private interior (a: Cell) (b: Cell) (cells: Cell list) =
    cells |> List.filter (fun c -> c <> a && c <> b) |> Set.ofList

let private smallGrid = [ for c in -2..2 do for r in -2..2 -> cell c r ]

[<Tests>]
let tests =
    testList "Game.Core Los — grid line-of-sight (FS.GG.Game#32)" [

        // ---- Symmetry: the invariant that makes combat fair (design §3.1, §7) -------------------

        test "lineOfSightBy is symmetric in both modes over an exhaustive small grid" {
            // Walls chosen to sit on plenty of traced interiors, including diagonal joins.
            let blocked = Set.ofList [ (0, 0); (1, 0); (0, 1); (-1, 1); (2, -2) ]
            let isTransparent = transparentExcept blocked

            for mode in [ Thin; Supercover ] do
                for a in smallGrid do
                    for b in smallGrid do
                        Expect.equal
                            (Los.lineOfSightBy mode isTransparent a b)
                            (Los.lineOfSightBy mode isTransparent b a)
                            $"los {mode} must be symmetric for {a} <-> {b}"
        }

        test "the raw Thin walk IS asymmetric — so canonicalization is load-bearing, not vacuous" {
            // Guards the symmetry test above against becoming a tautology if `lineOfSightBy` ever
            // stops tracing the canonical ordered pair.
            let a = cell 0 0
            let b = cell 2 1
            Expect.equal (Los.line a b) [ cell 0 0; cell 1 0; cell 2 1 ] "forward thin walk"
            Expect.equal (Los.line b a) [ cell 2 1; cell 1 1; cell 0 0 ] "reverse thin walk visits (1,1), not (1,0)"
            Expect.notEqual (interior a b (Los.line a b)) (interior a b (Los.line b a)) "interiors differ"
        }

        test "a wall on only one direction's raw thin walk blocks BOTH directions" {
            let a = cell 0 0
            let b = cell 2 1
            // (1,0) is interior to `line a b` but not to `line b a`. Canonicalization means both agree.
            let onForward = transparentExcept (Set.ofList [ (1, 0) ])
            Expect.isFalse (Los.lineOfSightBy Thin onForward a b) "blocked a->b"
            Expect.isFalse (Los.lineOfSightBy Thin onForward b a) "and blocked b->a"
            // (1,1) is interior to the reverse walk only; the canonical walk never enters it.
            let onReverse = transparentExcept (Set.ofList [ (1, 1) ])
            Expect.isTrue (Los.lineOfSightBy Thin onReverse a b) "clear a->b"
            Expect.isTrue (Los.lineOfSightBy Thin onReverse b a) "and clear b->a"
        }

        // ---- The diagonal wall join: the "#1 LOS bug" (design §2, §3.3) -------------------------

        test "Thin leaks through a diagonal wall join; Supercover does not" {
            // Walls at (1,0) and (0,1) touch only at a corner. Sight runs (0,0) -> (1,1).
            let isTransparent = transparentExcept (Set.ofList [ (1, 0); (0, 1) ])
            let a = cell 0 0
            let b = cell 1 1
            Expect.isTrue (Los.lineOfSightBy Thin isTransparent a b) "thin passes BETWEEN the two walls"
            Expect.isFalse (Los.lineOfSightBy Supercover isTransparent a b) "supercover enters (1,0) and is blocked"
        }

        test "lineOfSight defaults to Supercover — sight and shells never leak diagonally" {
            let isTransparent = transparentExcept (Set.ofList [ (1, 0); (0, 1) ])
            let a = cell 0 0
            let b = cell 1 1
            Expect.equal (Los.lineOfSight isTransparent a b) (Los.lineOfSightBy Supercover isTransparent a b) "same as Supercover"
            Expect.isFalse (Los.lineOfSight isTransparent a b) "blocked, not leaking"
        }

        // ---- Determinism / golden walks (design §6, §7) -----------------------------------------

        test "supercover golden walk is byte-stable and 4-connected" {
            let walk = Los.supercover (cell 0 0) (cell 3 2)
            Expect.equal
                walk
                [ cell 0 0; cell 1 0; cell 1 1; cell 2 1; cell 2 2; cell 3 2 ]
                "fixed step-x-first tie-break, one orthogonal step at a time"
            Expect.isTrue (isFourConnected walk) "strictly 4-connected"
            Expect.equal (List.length walk) (3 + 2 + 1) "|dx| + |dy| + 1 cells"
        }

        test "thin golden walk is byte-stable and diagonal-connected" {
            let walk = Los.line (cell 0 0) (cell 3 2)
            Expect.equal walk [ cell 0 0; cell 1 1; cell 2 1; cell 3 2 ] "integer Bresenham"
            Expect.isTrue (isEightConnected walk) "diagonal-connected"
            Expect.equal (List.length walk) (max 3 2 + 1) "max(|dx|, |dy|) + 1 cells"
        }

        test "identical endpoints yield an identical cell list across repeated calls" {
            let a = cell -7 3
            let b = cell 11 -5
            for _ in 1..8 do
                Expect.equal (Los.line a b) (Los.line a b) "thin is a pure function of its endpoints"
                Expect.equal (Los.supercover a b) (Los.supercover a b) "supercover is too"
        }

        test "an exact lattice-corner crossing resolves x-first, deterministically" {
            // A 45° line passes through every lattice corner; the tie-break must never emit a diagonal.
            let walk = Los.supercover (cell 0 0) (cell 2 2)
            Expect.equal walk [ cell 0 0; cell 1 0; cell 1 1; cell 2 1; cell 2 2 ] "x steps before y at every tie"
            Expect.isTrue (isFourConnected walk) "no corner is cut"
        }

        // ---- Cross-algorithm oracle (design §7) --------------------------------------------------

        test "thin and supercover agree on axis-aligned lines" {
            for d in [ 1; 2; 5; 9 ] do
                let horiz = (cell 0 0, cell d 0)
                let vert = (cell 0 0, cell 0 d)
                for (a, b) in [ horiz; vert ] do
                    Expect.equal (Los.line a b) (Los.supercover a b) $"no corner to disagree about: {a} -> {b}"
        }

        test "trace dispatches to the walk its mode names" {
            let a = cell -3 4
            let b = cell 6 -2
            Expect.equal (Los.trace Thin a b) (Los.line a b) "Thin -> line"
            Expect.equal (Los.trace Supercover a b) (Los.supercover a b) "Supercover -> supercover"
        }

        // ---- Structural properties over an exhaustive small grid ---------------------------------

        test "both walks include both endpoints, `a` first, and connect correctly" {
            for a in smallGrid do
                for b in smallGrid do
                    let thin = Los.line a b
                    let super = Los.supercover a b
                    Expect.equal (List.head thin) a $"thin starts at {a}"
                    Expect.equal (List.last thin) b $"thin ends at {b}"
                    Expect.equal (List.head super) a $"supercover starts at {a}"
                    Expect.equal (List.last super) b $"supercover ends at {b}"
                    Expect.isTrue (isEightConnected thin) $"thin 8-connected {a} -> {b}"
                    Expect.isTrue (isFourConnected super) $"supercover 4-connected {a} -> {b}"
                    Expect.equal
                        (List.length super)
                        (abs (b.Col - a.Col) + abs (b.Row - a.Row) + 1)
                        $"supercover length {a} -> {b}"
                    Expect.equal (List.length thin) (max (abs (b.Col - a.Col)) (abs (b.Row - a.Row)) + 1) $"thin length {a} -> {b}"
        }

        test "a degenerate `a = b` walk is the single cell, and is always visible" {
            let a = cell 4 -9
            Expect.equal (Los.line a a) [ a ] "thin [a]"
            Expect.equal (Los.supercover a a) [ a ] "supercover [a]"
            Expect.isTrue (Los.lineOfSight (fun _ -> false) a a) "a = b sees itself through an opaque map"
        }

        // ---- Endpoint rule ------------------------------------------------------------------------

        test "endpoints are never tested — you may look FROM and AT an opaque tile" {
            let a = cell 0 0
            let b = cell 4 0
            let isTransparent = transparentExcept (Set.ofList [ (0, 0); (4, 0) ])
            Expect.isTrue (Los.lineOfSight isTransparent a b) "both endpoints opaque, interior clear"
            for mode in [ Thin; Supercover ] do
                Expect.isTrue (Los.lineOfSightBy mode isTransparent a b) $"{mode} ignores the endpoints"
        }

        test "an opaque interior tile blocks; an always-opaque map still passes adjacent cells" {
            let isTransparent = transparentExcept (Set.ofList [ (2, 0) ])
            Expect.isFalse (Los.lineOfSight isTransparent (cell 0 0) (cell 4 0)) "wall at (2,0) blocks"
            // Adjacent cells have no interior at all, so an all-opaque map cannot block them.
            Expect.isTrue (Los.lineOfSight (fun _ -> false) (cell 0 0) (cell 1 0)) "no interior to test"
        }

        test "totality on an always-false and an always-true predicate" {
            let a = cell 0 0
            let b = cell 3 3
            Expect.isFalse (Los.lineOfSight (fun _ -> false) a b) "everything opaque"
            Expect.isTrue (Los.lineOfSight (fun _ -> true) a b) "everything transparent"
        }

        // ---- Monotonicity (design §7) -------------------------------------------------------------

        test "adding a wall can only flip visibility true -> false, never the reverse" {
            let a = cell -2 -1
            let b = cell 2 2
            for mode in [ Thin; Supercover ] do
                let before = Los.lineOfSightBy mode (transparentExcept Set.empty) a b
                Expect.isTrue before "an empty map is clear"
                for w in smallGrid do
                    let after = Los.lineOfSightBy mode (transparentExcept (Set.ofList [ (w.Col, w.Row) ])) a b
                    Expect.isTrue (before || not after) $"{mode}: adding {w} may not create sight"
        }

        // ---- Totality across the `int` coordinate domain (integer-only, no wrap, no throw) ---------

        test "extreme coordinates do not wrap or throw (int64 delta promotion)" {
            // Endpoints separated by ~2^31 are bounded by MEMORY (one emitted cell per step), not by
            // arithmetic, so the reachable boundary is extreme *coordinates* with short spans. In `int`
            // these would wrap on subtraction and `abs Int32.MinValue` would throw.
            let minC = Int32.MinValue
            let maxC = Int32.MaxValue

            let loWalk = Los.supercover (cell minC minC) (cell (minC + 2) (minC + 1))
            Expect.equal
                loWalk
                [ cell minC minC; cell (minC + 1) minC; cell (minC + 1) (minC + 1); cell (minC + 2) (minC + 1) ]
                "supercover at Int32.MinValue"
            Expect.isTrue (isFourConnected loWalk) "4-connected at the low boundary"

            let hiWalk = Los.line (cell (maxC - 2) (maxC - 1)) (cell maxC maxC)
            Expect.equal (List.head hiWalk) (cell (maxC - 2) (maxC - 1)) "thin starts at the high boundary"
            Expect.equal (List.last hiWalk) (cell maxC maxC) "thin ends at Int32.MaxValue"
            Expect.isTrue (isEightConnected hiWalk) "8-connected at the high boundary"

            // Mixed-sign extremes with a short span, crossing zero in neither axis.
            Expect.equal (Los.line (cell minC maxC) (cell minC maxC)) [ cell minC maxC ] "degenerate at opposite extremes"
            Expect.isTrue (Los.lineOfSight (fun _ -> true) (cell minC minC) (cell (minC + 3) (minC + 2))) "los at the boundary"
        }
    ]
