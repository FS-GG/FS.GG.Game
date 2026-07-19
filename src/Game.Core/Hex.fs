namespace FS.GG.Game.Core

[<Struct>]
type Hex = { Q: int; R: int; S: int }

[<RequireQualifiedAccess>]
module Hex =

    // The origin hex. Every hex satisfies the cube invariant q + r + s = 0 (see `create`).
    let origin: Hex = { Q = 0; R = 0; S = 0 }

    // The ONLY constructor: `s` is derived so the invariant q + r + s = 0 holds by construction and a
    // caller can never build an off-plane hex.
    let create (q: int) (r: int) : Hex = { Q = q; R = r; S = -q - r }

    let add (a: Hex) (b: Hex) : Hex =
        { Q = a.Q + b.Q; R = a.R + b.R; S = a.S + b.S }

    let subtract (a: Hex) (b: Hex) : Hex =
        { Q = a.Q - b.Q; R = a.R - b.R; S = a.S - b.S }

    let scale (h: Hex) (k: int) : Hex = { Q = h.Q * k; R = h.R * k; S = h.S * k }

    // The six cube unit directions in a FIXED order (index 0 = +Q/−R, rotating clockwise). The order
    // is documented and load-bearing: `neighbours`, `ring`, and `spiral` all enumerate in it.
    let directions: Hex list =
        [ { Q = 1; R = 0; S = -1 }
          { Q = 1; R = -1; S = 0 }
          { Q = 0; R = -1; S = 1 }
          { Q = -1; R = 0; S = 1 }
          { Q = -1; R = 1; S = 0 }
          { Q = 0; R = 1; S = -1 } ]

    let neighbours (h: Hex) : Hex list = directions |> List.map (add h)

    // Integer cube distance = (|dq| + |dr| + |ds|) / 2 = max(|dq|, |dr|, |ds|). int64 deltas so a wide
    // coordinate span cannot overflow / throw in `abs` (the same hardening the square Pathfinding
    // heuristic carries); the result fits `int` because it is at most the max axis delta.
    let distance (a: Hex) (b: Hex) : int =
        let dq = abs (int64 a.Q - int64 b.Q)
        let dr = abs (int64 a.R - int64 b.R)
        let ds = abs (int64 a.S - int64 b.S)
        int ((dq + dr + ds) / 2L)

    // 60° cube rotations about the origin: right (clockwise) [q,r,s] -> [-s,-q,-r]; left is its inverse
    // [q,r,s] -> [-r,-s,-q]. Six applications are the identity, and one preserves distance to origin.
    let rotateRight (h: Hex) : Hex = { Q = -h.S; R = -h.Q; S = -h.R }
    let rotateLeft (h: Hex) : Hex = { Q = -h.R; R = -h.S; S = -h.Q }

    // All hexes within `n` steps of the origin (cardinality 3n(n+1)+1), q outer / r inner — a fixed
    // deterministic order. Negative `n` yields the empty list.
    let range (n: int) : Hex list =
        if n < 0 then
            []
        else
            [ for q in -n..n do
                  for r in (max -n (-q - n)) .. (min n (-q + n)) -> create q r ]

    // The hexes exactly `n` steps from the origin (cardinality 6n; the single origin at n = 0). Walks
    // the six edges from a fixed starting corner in `directions` order — a fixed deterministic order.
    let ring (n: int) : Hex list =
        if n < 0 then
            []
        elif n = 0 then
            [ origin ]
        else
            let mutable hex = scale (List.item 4 directions) n

            [ for i in 0..5 do
                  let dir = List.item i directions

                  for _ in 1..n do
                      yield hex
                      hex <- add hex dir ]

    // `range n` in ring order: the origin, then ring 1 .. ring n. Fixed deterministic order.
    let spiral (n: int) : Hex list =
        if n < 0 then
            []
        else
            origin :: [ for k in 1..n do yield! ring k ]

    // Round a FRACTIONAL cube coordinate to the nearest valid `Hex`, resetting the component with the
    // largest rounding error to preserve q + r + s = 0. Ties on the largest error resolve in the fixed
    // order S, then Q, then R (DEC-003), so the result is byte-deterministic. This is the one float
    // touch-point; pixel↔hex conversion stays out of Core.
    let round (fq: float) (fr: float) (fs: float) : Hex =
        let rq = System.Math.Round fq
        let rr = System.Math.Round fr
        let rs = System.Math.Round fs
        let dq = abs (rq - fq)
        let dr = abs (rr - fr)
        let ds = abs (rs - fs)

        if ds >= dq && ds >= dr then
            // reset S (the largest error, or a tie won by S)
            create (int rq) (int rr)
        elif dq >= dr then
            // reset Q
            { Q = -(int rr) - (int rs); R = int rr; S = int rs }
        else
            // reset R
            { Q = int rq; R = -(int rq) - (int rs); S = int rs }

    // A contiguous hex line from `a` to `b`, endpoints included, of length `distance a b + 1` (each
    // consecutive pair adjacent). Deterministic: `distance+1` cube-lerp samples fed through `round`,
    // whose documented tie-break makes the line byte-identical across runs and platforms.
    let lineDraw (a: Hex) (b: Hex) : Hex list =
        let n = distance a b

        if n = 0 then
            [ a ]
        else
            let step = 1.0 / float n

            [ for i in 0..n ->
                  let t = step * float i
                  let fq = float a.Q + (float b.Q - float a.Q) * t
                  let fr = float a.R + (float b.R - float a.R) * t
                  let fs = float a.S + (float b.S - float a.S) * t
                  round fq fr fs ]

    // Offset (odd-r horizontal layout: odd rows shifted right) and doubled (doubled-width) storage
    // converters, on the shared integer `Cell`. Each pair is an EXACT inverse — `ofOffset (toOffset h)
    // = h` and `ofDoubled (toDoubled h) = h` — so a rectangular map can store hexes in `Cell` form and
    // convert to cube for the algorithms. The `- (row &&& 1)` term is always even, so the `/ 2` is
    // exact (no truncation) for negative coordinates too.
    let toOffset (h: Hex) : Cell =
        { Col = h.Q + (h.R - (h.R &&& 1)) / 2
          Row = h.R }

    let ofOffset (c: Cell) : Hex =
        create (c.Col - (c.Row - (c.Row &&& 1)) / 2) c.Row

    let toDoubled (h: Hex) : Cell = { Col = 2 * h.Q + h.R; Row = h.R }

    let ofDoubled (c: Cell) : Hex = create ((c.Col - c.Row) / 2) c.Row

    // Walk the cameFrom chain from `goal` back to the start, yielding start..goal inclusive.
    let private reconstruct (cameFrom: Map<Hex, Hex>) (goal: Hex) : Hex list =
        let rec walk acc c =
            match Map.tryFind c cameFrom with
            | Some prev -> walk (c :: acc) prev
            | None -> c :: acc

        walk [] goal

    // Breadth-first shortest hop path over the hex neighbour set, endpoint-inclusive, `maxVisited`-
    // bounded, `None` when unreachable. Deterministic: enqueue order follows the fixed `directions`,
    // dequeue is FIFO — no hashing on order.
    let bfs (maxVisited: int) (isWalkable: Hex -> bool) (start: Hex) (goal: Hex) : Hex list option =
        if maxVisited <= 0 || not (isWalkable start) || not (isWalkable goal) then
            None
        elif start = goal then
            Some [ start ]
        else
            let rec loop front back cameFrom visited expansions =
                match front with
                | [] ->
                    match List.rev back with
                    | [] -> None
                    | front -> loop front [] cameFrom visited expansions
                | current :: rest ->
                    if expansions >= maxVisited then
                        None
                    elif current = goal then
                        Some(reconstruct cameFrom goal)
                    else
                        let (back, cameFrom, visited) =
                            neighbours current
                            |> List.filter isWalkable
                            |> List.fold
                                (fun (bk, cf, vis) n ->
                                    if Set.contains n vis then
                                        (bk, cf, vis)
                                    else
                                        (n :: bk, Map.add n current cf, Set.add n vis))
                                (back, cameFrom, visited)

                        loop rest back cameFrom visited (expansions + 1)

            loop [ start ] [] Map.empty (Set.singleton start) 0

    // A* shortest hop path over the hex neighbour set, using the exact cube `distance` as the
    // (admissible, consistent) heuristic. Same contract as `bfs`; the frontier is a Set keyed by the
    // TOTAL integer order (f, h, Q, R), so the path is byte-identical across runs and platforms. Every
    // hop costs 1 (uniform), so the returned path is shortest-hop, equal in length to `bfs`.
    let astar (maxVisited: int) (isWalkable: Hex -> bool) (start: Hex) (goal: Hex) : Hex list option =
        if maxVisited <= 0 || not (isWalkable start) || not (isWalkable goal) then
            None
        elif start = goal then
            Some [ start ]
        else
            let h (x: Hex) : int64 = int64 (distance x goal)
            let h0 = h start
            let openSet = Set.singleton (h0, h0, start.Q, start.R)
            let gScore = Map.ofList [ start, 0L ]

            let rec loop
                (openSet: Set<int64 * int64 * int * int>)
                (gScore: Map<Hex, int64>)
                (cameFrom: Map<Hex, Hex>)
                (expansions: int)
                : Hex list option =
                if Set.isEmpty openSet || expansions >= maxVisited then
                    None
                else
                    let (f, hCur, q, r) = Set.minElement openSet
                    let current = create q r
                    let openSet = Set.remove (f, hCur, q, r) openSet

                    if current = goal then
                        Some(reconstruct cameFrom current)
                    else
                        let g = gScore.[current]

                        let (openSet, gScore, cameFrom) =
                            neighbours current
                            |> List.filter isWalkable
                            |> List.fold
                                (fun (os, gs, cf) n ->
                                    let tentative = g + 1L

                                    match Map.tryFind n gs with
                                    | Some existing when existing <= tentative -> (os, gs, cf)
                                    | prior ->
                                        let hn = h n

                                        let os =
                                            match prior with
                                            | Some oldG -> Set.remove (oldG + hn, hn, n.Q, n.R) os
                                            | None -> os

                                        let os = Set.add (tentative + hn, hn, n.Q, n.R) os
                                        (os, Map.add n tentative gs, Map.add n current cf))
                                (openSet, gScore, cameFrom)

                        loop openSet gScore cameFrom (expansions + 1)

            loop openSet gScore Map.empty 0
