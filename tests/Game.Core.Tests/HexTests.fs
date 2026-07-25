module Game.Core.Tests.HexTests

// Hexagonal grid module (work item 019, roadmap 2.1). Integer cube coordinates: the invariant
// q+r+s=0, distance/neighbour laws, rotation cycles, range/ring/spiral cardinalities, line
// contiguity, offset/doubled converter round-trips, and hex pathfinding optimality + determinism.

open Expecto
open FsCheck
open FS.GG.Game.Core

// A hex whose axial (q,r) come from small ints — always on-plane via Hex.create.
let private hexOf (q: int) (r: int) = Hex.create ((q % 20) - 10) ((r % 20) - 10)

// Walkable iff within `n` steps of origin (a bounded open hex disc), for terminating pathfinding.
let private disc (n: int) (h: Hex) = Hex.distance Hex.origin h <= n

let private cubeSum (h: Hex) = int64 h.Q + int64 h.R + int64 h.S

let private validHexPath (isWalkable: Hex -> bool) (start: Hex) (goal: Hex) (path: Hex list) =
    match path with
    | [] -> false
    | _ ->
        List.head path = start
        && List.last path = goal
        && List.forall isWalkable path
        && path |> List.pairwise |> List.forall (fun (a, b) -> Hex.distance a b = 1)

[<Tests>]
let tests =
    testList "Game.Core Hex (019, FR-001..FR-008)" [

        test "create keeps the cube invariant q+r+s=0 (FR-001)" {
            let h = Hex.create 2 -5
            Expect.equal (cubeSum h) 0L "invariant holds without Int32 wraparound"
            Expect.equal h.S 3 "S = -q - r"
            Expect.equal Hex.origin (Hex.create 0 0) "origin is (0,0,0)"
        }

        test "complete cube construction rejects off-plane triples (FR-001)" {
            Expect.isEmpty (typeof<Hex>.GetConstructors()) "Hex exposes no public record constructor"
            let valid = Hex.tryCreateCube 2 -5 3
            Expect.equal valid (Some(Hex.create 2 -5)) "an on-plane triple constructs the same Hex"
            Expect.equal (Hex.tryCreateCube 2 -5 4) None "an off-plane triple is rejected"

            Expect.equal
                (Hex.tryCreateCube System.Int32.MaxValue System.Int32.MaxValue 2)
                None
                "validation widens the sum instead of accepting an overflowing Int32 total"

            Expect.throwsT<System.OverflowException>
                (fun () -> Hex.create System.Int32.MaxValue System.Int32.MaxValue |> ignore)
                "axial construction rejects an unrepresentable S axis instead of wrapping"

            let edge = Hex.create System.Int32.MaxValue 1

            Expect.throwsT<System.OverflowException>
                (fun () -> Hex.add edge edge |> ignore)
                "arithmetic cannot wrap an axis into an off-plane Hex"

            Expect.throwsT<System.OverflowException>
                (fun () -> Hex.rotateRight edge |> ignore)
                "rotation cannot negate Int32.MinValue into an off-plane Hex"
        }

        testCase "create is always on-plane over random axials (FsCheck)" <| fun () ->
            let prop (q: int) (r: int) =
                try
                    let h = Hex.create q r
                    cubeSum h = 0L
                with :? System.OverflowException ->
                    true

            Check.One(Config.QuickThrowOnFailure.WithMaxTest 1000, prop)

        test "distance is the cube distance and is 1 to each neighbour (FR-002)" {
            Expect.equal (Hex.distance (Hex.create 0 0) (Hex.create 3 -1)) 3 "cube distance"
            let h = Hex.create 1 2
            Expect.isTrue (Hex.neighbours h |> List.forall (fun n -> Hex.distance h n = 1)) "each neighbour is 1 away"
            Expect.equal (List.length (Hex.neighbours h)) 6 "six neighbours"
        }

        test "distance saturates rather than wrapping at extreme coordinates (totality)" {
            // True distance here is 4e9 > Int32.MaxValue; a truncating cast would wrap to a NEGATIVE
            // value, silently corrupting distance/astar/lineDraw. Saturation keeps it positive & total.
            let a = Hex.create 2_000_000_000 0
            let b = Hex.create -2_000_000_000 0
            Expect.equal (Hex.distance a b) System.Int32.MaxValue "saturates to Int32.MaxValue, not a negative wrap"
            Expect.isTrue (Hex.distance a b > 0) "distance is never negative"
            // lineDraw's length is distance+1; with a wrapped-negative distance it used to return [].
            // A degenerate same-hex line still works at extreme coordinates.
            Expect.equal (Hex.lineDraw a a) [ a ] "degenerate line at extreme coords is [a], not []"
        }

        testCase "distance equals max(|dq|,|dr|,|ds|) and neighbours are unit (FsCheck)" <| fun () ->
            let prop (aq: int) (ar: int) (bq: int) (br: int) =
                let a = hexOf aq ar
                let b = hexOf bq br
                let byMax = max (abs (a.Q - b.Q)) (max (abs (a.R - b.R)) (abs (a.S - b.S)))
                Hex.distance a b = byMax && (Hex.neighbours a |> List.forall (fun n -> Hex.distance a n = 1))

            Check.One(Config.QuickThrowOnFailure.WithMaxTest 1000, prop)

        testCase "add/subtract/scale laws (FR-003, FsCheck)" <| fun () ->
            let prop (aq: int) (ar: int) (bq: int) (br: int) =
                let a = hexOf aq ar
                let b = hexOf bq br
                Hex.subtract (Hex.add a b) b = a
                && Hex.scale a 0 = Hex.origin
                && Hex.add a Hex.origin = a
                && (let s = Hex.scale a 3 in cubeSum s = 0L)

            Check.One(Config.QuickThrowOnFailure.WithMaxTest 1000, prop)

        testCase "rotating six times is the identity and preserves origin distance (FR-004, FsCheck)" <| fun () ->
            let prop (q: int) (r: int) =
                let h = hexOf q r
                let sixR = h |> Hex.rotateRight |> Hex.rotateRight |> Hex.rotateRight |> Hex.rotateRight |> Hex.rotateRight |> Hex.rotateRight
                let sixL = h |> Hex.rotateLeft |> Hex.rotateLeft |> Hex.rotateLeft |> Hex.rotateLeft |> Hex.rotateLeft |> Hex.rotateLeft
                sixR = h
                && sixL = h
                && Hex.rotateLeft (Hex.rotateRight h) = h
                && Hex.distance Hex.origin (Hex.rotateRight h) = Hex.distance Hex.origin h

            Check.One(Config.QuickThrowOnFailure.WithMaxTest 1000, prop)

        test "range/ring/spiral cardinalities and relationships (FR-005)" {
            for n in 0..6 do
                Expect.equal (List.length (Hex.range n)) (3 * n * (n + 1) + 1) (sprintf "range %d = 3n(n+1)+1" n)
                let expectedRing = if n = 0 then 1 else 6 * n
                Expect.equal (List.length (Hex.ring n)) expectedRing (sprintf "ring %d = 6n" n)
                Expect.equal (List.length (Hex.spiral n)) (List.length (Hex.range n)) (sprintf "spiral %d = |range n|" n)
                // spiral n is a permutation of range n
                Expect.equal (Hex.spiral n |> Set.ofList) (Hex.range n |> Set.ofList) (sprintf "spiral %d ≡ range n as sets" n)
            Expect.equal (Hex.range -1) [] "negative range is empty"
            Expect.equal (Hex.ring -1) [] "negative ring is empty"
        }

        test "every ring hex is exactly n from the origin (FR-005)" {
            for n in 1..5 do
                Expect.isTrue (Hex.ring n |> List.forall (fun h -> Hex.distance Hex.origin h = n)) (sprintf "ring %d all at distance n" n)
        }

        test "lineDraw is contiguous, endpoint-inclusive, length distance+1 (FR-006)" {
            let a = Hex.create 0 0
            let b = Hex.create 4 -2
            let line = Hex.lineDraw a b
            Expect.equal (List.length line) (Hex.distance a b + 1) "length = distance + 1"
            Expect.equal (List.head line) a "starts at a"
            Expect.equal (List.last line) b "ends at b"
            Expect.isTrue (line |> List.pairwise |> List.forall (fun (x, y) -> Hex.distance x y = 1)) "each step adjacent"
            Expect.equal (Hex.lineDraw a a) [ a ] "degenerate line is [a]"
        }

        test "round preserves the invariant and is deterministic (FR-006)" {
            let h = Hex.round 0.6 -1.2 0.6
            Expect.equal (cubeSum h) 0L "rounded hex is on-plane"
            Expect.equal (Hex.round 1.0 2.0 -3.0) (Hex.create 1 2) "an exact cube rounds to itself"
            Expect.equal (Hex.round 0.4 0.4 -0.8) (Hex.round 0.4 0.4 -0.8) "deterministic"
        }

        testCase "lineDraw is contiguous and correct length over random pairs (FsCheck)" <| fun () ->
            let prop (aq: int) (ar: int) (bq: int) (br: int) =
                let a = hexOf aq ar
                let b = hexOf bq br
                let line = Hex.lineDraw a b
                List.length line = Hex.distance a b + 1
                && List.head line = a
                && List.last line = b
                && (line |> List.pairwise |> List.forall (fun (x, y) -> Hex.distance x y = 1))

            Check.One(Config.QuickThrowOnFailure.WithMaxTest 1000, prop)

        testCase "offset/doubled converters round-trip (FR-007, FsCheck)" <| fun () ->
            let prop (q: int) (r: int) =
                let h = hexOf q r
                Hex.ofOffset (Hex.toOffset h) = h && Hex.ofDoubled (Hex.toDoubled h) = h

            Check.One(Config.QuickThrowOnFailure.WithMaxTest 1000, prop)

        test "offset/doubled converters check every Int32 boundary pair instead of wrapping" {
            let edge =
                [ System.Int32.MinValue
                  System.Int32.MinValue + 1
                  -1
                  0
                  1
                  System.Int32.MaxValue - 1
                  System.Int32.MaxValue ]

            let succeeds (f: unit -> 'T) =
                try
                    f () |> ignore
                    true
                with
                | :? System.OverflowException
                | :? System.ArgumentException -> false

            let fitsInt32 (value: int64) =
                value >= int64 System.Int32.MinValue && value <= int64 System.Int32.MaxValue

            for col in edge do
                for row in edge do
                    let cell = { Col = col; Row = row }
                    let row64 = int64 row
                    let offsetQ = int64 col - (row64 - (row64 &&& 1L)) / 2L
                    let offsetS = -offsetQ - row64
                    let offsetRepresentable = fitsInt32 offsetQ && fitsInt32 offsetS
                    let offsetSucceeded = succeeds (fun () -> Hex.ofOffset cell)

                    Expect.equal
                        offsetSucceeded
                        offsetRepresentable
                        $"offset boundary pair ({col}, {row}) has the documented domain"

                    if offsetSucceeded then
                        Expect.equal
                            (cell |> Hex.ofOffset |> Hex.toOffset)
                            cell
                            $"offset boundary pair ({col}, {row}) round-trips"

                    let doubledDelta = int64 col - row64
                    let doubledQ = doubledDelta / 2L
                    let doubledS = -doubledQ - row64

                    let doubledRepresentable =
                        doubledDelta % 2L = 0L && fitsInt32 doubledQ && fitsInt32 doubledS

                    let doubledSucceeded = succeeds (fun () -> Hex.ofDoubled cell)

                    Expect.equal
                        doubledSucceeded
                        doubledRepresentable
                        $"doubled boundary pair ({col}, {row}) has the documented domain"

                    if doubledSucceeded then
                        Expect.equal
                            (cell |> Hex.ofDoubled |> Hex.toDoubled)
                            cell
                            $"doubled boundary pair ({col}, {row}) round-trips"

            let validExtremeHexes =
                [ Hex.create System.Int32.MaxValue 1
                  Hex.create System.Int32.MaxValue 0
                  Hex.create (System.Int32.MinValue + 1) 0
                  Hex.create -1_073_741_824 -1 ]

            for h in validExtremeHexes do
                if succeeds (fun () -> Hex.toOffset h) then
                    Expect.equal (h |> Hex.toOffset |> Hex.ofOffset) h $"offset extreme {h} round-trips"

                if succeeds (fun () -> Hex.toDoubled h) then
                    Expect.equal (h |> Hex.toDoubled |> Hex.ofDoubled) h $"doubled extreme {h} round-trips"

            let maxOffset = Hex.create (System.Int32.MaxValue - 1) 2
            let minOffset = Hex.create System.Int32.MinValue 1
            let maxDoubled = Hex.create 1_073_741_823 1
            let minDoubled = Hex.create -1_073_741_824 0

            Expect.equal (Hex.toOffset maxOffset).Col System.Int32.MaxValue "max offset Col is exact"
            Expect.equal (Hex.toOffset minOffset).Col System.Int32.MinValue "min offset Col is exact"
            Expect.equal (Hex.toDoubled maxDoubled).Col System.Int32.MaxValue "max doubled Col is exact"
            Expect.equal (Hex.toDoubled minDoubled).Col System.Int32.MinValue "min doubled Col is exact"

            Expect.throwsT<System.OverflowException>
                (fun () -> Hex.toDoubled validExtremeHexes.[0] |> ignore)
                "positive doubled overflow is explicit"

            Expect.throwsT<System.OverflowException>
                (fun () -> Hex.toDoubled validExtremeHexes.[3] |> ignore)
                "negative doubled overflow is explicit"

            Expect.throwsT<System.ArgumentException>
                (fun () -> Hex.ofDoubled { Col = 0; Row = 1 } |> ignore)
                "an odd doubled coordinate is rejected instead of truncated"
        }

        test "Hex.astar/bfs find a shortest hop path on an open disc (FR-008)" {
            let walk = disc 6
            let start = Hex.create -3 0
            let goal = Hex.create 3 -2
            let a = Hex.astar 5000 walk start goal
            let b = Hex.bfs 5000 walk start goal
            Expect.isTrue (a |> Option.map (validHexPath walk start goal) |> Option.defaultValue false) "astar path valid"
            Expect.isTrue (b |> Option.map (validHexPath walk start goal) |> Option.defaultValue false) "bfs path valid"
            Expect.equal (a |> Option.map List.length) (Some(Hex.distance start goal + 1)) "astar length = distance + 1 on open disc"
            Expect.equal (b |> Option.map List.length) (Some(Hex.distance start goal + 1)) "bfs length = distance + 1 on open disc"
        }

        test "Hex pathfinding degenerate/totality contract (FR-008)" {
            let walk = disc 4
            let blockedStart = fun h -> h <> Hex.create 0 0
            Expect.equal (Hex.astar 100 walk (Hex.create 1 1) (Hex.create 1 1)) (Some [ Hex.create 1 1 ]) "start=goal ⇒ [start]"
            Expect.isNone (Hex.astar 0 walk (Hex.create 0 0) (Hex.create 1 0)) "maxVisited 0 ⇒ None"
            Expect.isNone (Hex.astar 100 blockedStart (Hex.create 0 0) (Hex.create 1 0)) "blocked start ⇒ None"
            // A goal outside the walkable disc is unreachable.
            Expect.isNone (Hex.astar 5000 walk (Hex.create 0 0) (Hex.create 9 0)) "goal off the disc ⇒ None"
        }

        testCase "Hex astar and bfs agree on hop count + reachability, and are deterministic (FsCheck)" <| fun () ->
            let prop (aq: int) (ar: int) (bq: int) (br: int) (blockRaw: (int * int) list) =
                let blocked = blockRaw |> List.map (fun (q, r) -> hexOf q r) |> Set.ofList
                let walk h = Hex.distance Hex.origin h <= 4 && not (Set.contains h blocked)
                let clamp x = ((x % 9) + 9) % 9 - 4
                let start = Hex.create (clamp aq) (clamp ar)
                let goal = Hex.create (clamp bq) (clamp br)
                let a = Hex.astar 5000 walk start goal
                let b = Hex.bfs 5000 walk start goal
                let a2 = Hex.astar 5000 walk start goal
                // determinism + reachability agreement + equal hop count (both shortest, uniform cost)
                a = a2
                && (a.IsSome = b.IsSome)
                && (match a, b with
                    | Some pa, Some pb -> List.length pa = List.length pb && validHexPath walk start goal pa
                    | _ -> true)

            Check.One(Config.QuickThrowOnFailure.WithMaxTest 1000, prop)
    ]
