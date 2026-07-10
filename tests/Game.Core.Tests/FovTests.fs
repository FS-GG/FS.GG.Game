module Game.Core.Tests.FovTests

// Symmetric shadowcasting (Ford/Milazzo) over a transparency predicate: 4 quadrants x row scans,
// exact integer rational slopes, Euclidean disc clipping, expansive walls. The headline property is
// SYMMETRY between transparent cells — it is the whole reason this algorithm exists rather than a
// line-of-sight ray per cell — so it is asserted, not assumed.

open Expecto
open FsCheck
open FS.GG.Game.Core

let private origin = { Col = 0; Row = 0 }

/// Everything is see-through, forever.
let private openField (_: Cell) = true

/// Nothing is see-through, anywhere.
let private solidRock (_: Cell) = false

/// Transparency over a bounded w x h grid minus a blocked set. Out of bounds is opaque, so the scan
/// is walled in (the predicate IS the map).
let private gridTransparent (w: int) (h: int) (blocked: Set<int * int>) (c: Cell) =
    c.Col >= 0
    && c.Col < w
    && c.Row >= 0
    && c.Row < h
    && not (blocked.Contains(c.Col, c.Row))

let private sqDist (a: Cell) (b: Cell) =
    let dc = a.Col - b.Col
    let dr = a.Row - b.Row
    dc * dc + dr * dr

/// Each unordered pair {a, b} with a <> b, exactly once. `aSeesB = bSeesA` is invariant under
/// swapping the operands, so checking both orderings would only re-evaluate the same boolean.
let rec private unorderedPairs (cells: Cell list) =
    match cells with
    | [] -> []
    | a :: rest -> [ for b in rest -> (a, b) ] @ unorderedPairs rest

/// Every cell of the closed disc of `radius` about `origin`.
let private disc (radius: int) =
    seq {
        for row in -radius .. radius do
            for col in -radius .. radius do
                let c = { Col = col; Row = row }
                if sqDist c origin <= radius * radius then c
    }
    |> Set.ofSeq

[<Tests>]
let tests =
    testList "Game.Core Fov — symmetric shadowcasting" [

        testList "totality and degenerate input" [
            test "radius 0 sees exactly the origin" {
                Expect.equal (Fov.fov openField origin 0) (Set.singleton origin) "you always occupy your own cell"
            }
            test "a negative radius sees nothing at all" {
                Expect.equal (Fov.fov openField origin -1) Set.empty "degenerate bound ⇒ empty answer"
                Expect.equal (Fov.fov openField origin -1000) Set.empty "however negative"
            }
            test "an opaque origin is still visible to itself" {
                Expect.isTrue (Fov.fov solidRock origin 3 |> Set.contains origin) "you occupy the cell you stand in"
            }
            test "an always-opaque predicate terminates and reveals only the surrounding walls" {
                // depth 1 is all wall, so no row is ever scanned deeper. radius 2 admits the diagonals
                // (squared distance 2 <= 4), giving the origin plus its 8 neighbours and nothing else.
                let seen = Fov.fov solidRock origin 2
                let expected =
                    seq {
                        for row in -1 .. 1 do
                            for col in -1 .. 1 -> { Col = col; Row = row }
                    }
                    |> Set.ofSeq
                Expect.equal seen expected "origin + its 8 walls, scan bounded by radius not by the map"
            }
            test "radius 1 against solid rock clips the diagonals out of the disc" {
                // squared distance 2 > 1, so only the four orthogonal walls survive the disc test.
                let seen = Fov.fov solidRock origin 1
                Expect.equal (Set.count seen) 5 "origin + 4 orthogonal walls"
                Expect.isFalse (seen |> Set.contains { Col = 1; Row = 1 }) "diagonal is outside the disc"
            }
        ]

        testList "radius is an exact integer disc" [
            test "an open field reveals precisely the Euclidean disc" {
                for r in 0..6 do
                    Expect.equal (Fov.fov openField origin r) (disc r) (sprintf "open field, radius %d ⇒ the disc" r)
            }
            test "a cell at exactly radius is visible, one beyond is not" {
                let seen = Fov.fov openField origin 5
                Expect.isTrue (seen |> Set.contains { Col = 5; Row = 0 }) "distance 5 = radius ⇒ visible"
                Expect.isFalse (seen |> Set.contains { Col = 6; Row = 0 }) "distance 6 > radius ⇒ clipped"
                Expect.isTrue (seen |> Set.contains { Col = 3; Row = 4 }) "3-4-5 triangle sits exactly on the rim"
            }
            testCase "field of view is always a subset of the disc (FsCheck ≥300 cases)" <| fun () ->
                let prop (blockedRaw: (int * int) list) (rRaw: int) =
                    let blocked = blockedRaw |> List.map (fun (c, r) -> ((abs c) % 7), ((abs r) % 7)) |> Set.ofList
                    let terrain = gridTransparent 7 7 blocked
                    let o = { Col = 3; Row = 3 }
                    let radius = (abs rRaw) % 6
                    Fov.fov terrain o radius
                    |> Set.forall (fun c -> sqDist c o <= radius * radius)
                Check.One(Config.QuickThrowOnFailure.WithMaxTest 300, prop)
        ]

        testList "symmetry — the property that distinguishes shadowcasting from a ray per cell" [
            testCase "transparent a sees transparent b ⇔ b sees a (FsCheck ≥200 random maps)" <| fun () ->
                let prop (blockedRaw: (int * int) list) =
                    // Truncated so at most 20 of the 49 cells are blocked. Without a cap, a long enough
                    // generated list can cover every cell, leaving no floors — and a `forall` over no
                    // pairs passes vacuously, silently gutting the one property this module turns on.
                    let blocked =
                        blockedRaw
                        |> List.truncate 20
                        |> List.map (fun (c, r) -> ((abs c) % 7), ((abs r) % 7))
                        |> Set.ofList

                    let terrain = gridTransparent 7 7 blocked
                    let radius = 4

                    let floors =
                        [ for col in 0..6 do
                              for row in 0..6 -> { Col = col; Row = row } ]
                        |> List.filter terrain

                    // Cache one field of view per floor cell, then check each unordered pair once.
                    let views = floors |> List.map (fun c -> c, Fov.fov terrain c radius) |> Map.ofList

                    unorderedPairs floors
                    |> List.forall (fun (a, b) ->
                        if sqDist a b > radius * radius then
                            true // the guarantee is stated within radius
                        else
                            let aSeesB = views.[a] |> Set.contains b
                            let bSeesA = views.[b] |> Set.contains a
                            aSeesB = bSeesA)

                Check.One(Config.QuickThrowOnFailure.WithMaxTest 200, prop)

            test "symmetry survives a pillar field (the classic asymmetry trap)" {
                let blocked = Set.ofList [ (2, 2); (4, 2); (2, 4); (4, 4); (3, 6); (6, 3) ]
                let terrain = gridTransparent 8 8 blocked
                let radius = 6

                let floors =
                    [ for col in 0..7 do
                          for row in 0..7 -> { Col = col; Row = row } ]
                    |> List.filter terrain

                let views = floors |> List.map (fun c -> c, Fov.fov terrain c radius) |> Map.ofList

                let asymmetric =
                    unorderedPairs floors
                    |> List.filter (fun (a, b) ->
                        sqDist a b <= radius * radius
                        && (views.[a] |> Set.contains b) <> (views.[b] |> Set.contains a))

                Expect.isNonEmpty floors "guard: the pillar field must actually leave floors to compare"
                Expect.isEmpty asymmetric "no floor pair may disagree about seeing the other"
            }
        ]

        testList "blocking model" [
            test "a convex room reveals all of its walls and leaks nothing beyond (expansive walls)" {
                // A 5x5 block: 3x3 of floor, ringed by wall. Everything outside is opaque too, so the
                // only correct answer is the 5x5 block exactly — every wall seen (expansive), nothing
                // past it (no diagonal leakage).
                let inRoom (c: Cell) = abs c.Col <= 1 && abs c.Row <= 1
                let seen = Fov.fov inRoom origin 5

                let block =
                    seq {
                        for row in -2 .. 2 do
                            for col in -2 .. 2 -> { Col = col; Row = row }
                    }
                    |> Set.ofSeq

                Expect.equal seen block "the room's floors and all 16 of its wall tiles, and not one cell more"
            }

            test "a single pillar casts a clean shadow directly behind it" {
                let pillar = { Col = 2; Row = 0 }
                let terrain (c: Cell) = c <> pillar
                let seen = Fov.fov terrain origin 6

                Expect.isTrue (seen |> Set.contains pillar) "the pillar's near face is visible (expansive wall)"
                Expect.isTrue (seen |> Set.contains { Col = 1; Row = 0 }) "the cell in front of it is visible"

                for col in 3..6 do
                    Expect.isFalse (seen |> Set.contains { Col = col; Row = 0 }) (sprintf "(%d,0) is directly behind the pillar" col)

                // The shadow is a wedge, not a widening lattice of holes: cells off the axis are lit.
                Expect.isTrue (seen |> Set.contains { Col = 3; Row = 1 }) "off-axis behind the pillar stays visible"
                Expect.isTrue (seen |> Set.contains { Col = 3; Row = -1 }) "and symmetrically on the other side"
            }

            test "opacity is consulted, walkability is not — this module never sees a 'wall' flag" {
                // The two bits are independent, so a terrain that inverts them separates the module's
                // real behaviour from a "wall" flag: a chasm is transparent but unwalkable, a secret
                // door is opaque but walkable. Sight must cross the chasm and stop at the door.
                let chasm = { Col = 2; Row = 0 }
                let secretDoor = { Col = 0; Row = 2 }
                let isWalkable (c: Cell) = c <> chasm
                let isTransparent (c: Cell) = c <> secretDoor
                let seen = Fov.fov isTransparent origin 5

                Expect.isFalse (isWalkable chasm) "guard: the chasm is the unwalkable one"
                Expect.isTrue (isWalkable secretDoor) "guard: the door is the walkable one"

                Expect.isTrue (seen |> Set.contains chasm) "the chasm is transparent ⇒ visible"

                Expect.isTrue
                    (seen |> Set.contains { Col = 4; Row = 0 })
                    "and sight crosses it, unwalkable though it is — a 'wall' flag would have stopped here"

                Expect.isTrue (seen |> Set.contains secretDoor) "the door's near face is visible (expansive wall)"

                Expect.isFalse
                    (seen |> Set.contains { Col = 0; Row = 3 })
                    "but the door blocks sight beyond, walkable though it is"
            }
        ]

        testList "determinism" [
            test "repeat calls are byte-identical" {
                let blocked = Set.ofList [ (2, 1); (3, 3); (1, 4); (5, 2) ]
                let terrain = gridTransparent 7 7 blocked
                let o = { Col = 3; Row = 3 }
                Expect.equal (Fov.fov terrain o 4) (Fov.fov terrain o 4) "identical inputs ⇒ identical set"
            }
            test "enumeration follows the total (Col, Row) cell order, not hash order" {
                let seen = Fov.fov openField origin 3 |> Set.toList
                Expect.equal seen (List.sort seen) "Set<Cell> enumerates in structural (Col, Row) order"
            }
            testCase "no floating-point drift: repeated runs over random maps agree (FsCheck ≥300)" <| fun () ->
                let prop (blockedRaw: (int * int) list) (rRaw: int) =
                    let blocked = blockedRaw |> List.map (fun (c, r) -> ((abs c) % 9), ((abs r) % 9)) |> Set.ofList
                    let terrain = gridTransparent 9 9 blocked
                    let o = { Col = 4; Row = 4 }
                    let radius = (abs rRaw) % 7
                    Fov.fov terrain o radius = Fov.fov terrain o radius
                Check.One(Config.QuickThrowOnFailure.WithMaxTest 300, prop)
        ]
    ]
