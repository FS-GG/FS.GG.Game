namespace FS.GG.Game.Core

type Neighbourhood =
    | FourWay
    | EightWay

[<RequireQualifiedAccess>]
module Pathfinding =

    type Step =
        { Cost: int
          CameFrom: Cell option }

    type Reach =
        { Steps: Map<Cell, Step>
          Endable: Set<Cell> }

    // The integer step scale, and the ONE place it is written. Every 10 and 14 below derives from it
    // (#229): the literals used to be copied into `neighbours`, `heuristic` and the docstrings, so a
    // change to the scale had to be caught by prose.
    let baseStep = 10

    // The diagonal: `baseStep * √2`, integer-scaled — 14 when `baseStep` is 10. The 14/10 ratio IS the
    // √2 approximation, which is why the module never needs a float and equal-cost ties cannot leak
    // through floating-point equality. Derived, so it cannot drift from `baseStep`.
    let private diagStep = baseStep * 14 / 10

    // Saturating, because the failure it prevents is the one #229 is about: `int` multiplication would
    // WRAP a large `moveRange` to a negative budget, and a negative budget yields an EMPTY reach — the
    // same silent total freeze as passing `moveRange` raw. Clamping high leaves the walk bounded by
    // `maxVisited` (which is what bounds it anyway); a genuinely negative `moveRange` stays negative,
    // preserving `reachable`'s documented "negative budget ⇒ empty" totality.
    let budgetFor (moveRange: int) : int =
        let scaled = int64 baseStep * int64 moveRange

        if scaled > int64 System.Int32.MaxValue then System.Int32.MaxValue
        elif scaled < int64 System.Int32.MinValue then System.Int32.MinValue
        else int scaled

    // Fixed step offsets. The enumeration order does not affect A* output (the total (f,h,Col,Row)
    // frontier order decides), and for BFS it is a deterministic, documented order.
    let private orthoOffsets = [ (0, -1); (0, 1); (-1, 0); (1, 0) ]
    let private diagOffsets = [ (-1, -1); (1, -1); (-1, 1); (1, 1) ]

    // Walkable neighbours of `c` with their integer step cost: `baseStep` orthogonal, `diagStep`
    // diagonal. Under EightWay a diagonal is refused unless BOTH shared orthogonal neighbours are
    // walkable (no corner-cutting).
    let private neighbours (nb: Neighbourhood) (isWalkable: Cell -> bool) (c: Cell) : struct (Cell * int) list =
        let ortho =
            orthoOffsets
            |> List.choose (fun (dx, dy) ->
                let n = { Col = c.Col + dx; Row = c.Row + dy }
                if isWalkable n then Some(struct (n, baseStep)) else None)

        match nb with
        | FourWay -> ortho
        | EightWay ->
            let diag =
                diagOffsets
                |> List.choose (fun (dx, dy) ->
                    let n = { Col = c.Col + dx; Row = c.Row + dy }
                    let side1 = { Col = c.Col + dx; Row = c.Row }
                    let side2 = { Col = c.Col; Row = c.Row + dy }

                    if isWalkable n && isWalkable side1 && isWalkable side2 then
                        Some(struct (n, diagStep))
                    else
                        None)

            ortho @ diag

    // Admissible integer heuristic toward `goal`: Manhattan (4-way) / octile (8-way), both in `baseStep`
    // units. Admissible because `diagStep <= 2 * baseStep` — a diagonal never costs more than the two
    // orthogonals it replaces — so the estimate can never exceed the true remaining cost.
    let private octile (nb: Neighbourhood) (goal: Cell) (c: Cell) : int64 =
        // int64 deltas, the same hardening `Los`/`Ai` already carry (Los.fs, Ai.inRange), and for the
        // same two reasons: over the module's advertised UNBOUNDED integer cell space, `c.Col - goal.Col`
        // as `int` WRAPS on a far-apart pair (to Int32.MinValue), and `abs` of that then THROWS —
        // breaking the "pure and total" contract on `astar`'s very first `h start`. The products below
        // reach at most ~1e11 (baseStep/diagStep × a ~4.3e9 delta), well inside int64, so a coordinate
        // span that used to overflow `int` into a wrapped, non-admissible estimate now yields the true
        // admissible one. (docs/reports/2026-07-15-code-and-architecture-review.md §3.)
        let dx = abs (int64 c.Col - int64 goal.Col)
        let dy = abs (int64 c.Row - int64 goal.Row)

        match nb with
        | FourWay -> int64 baseStep * (dx + dy)
        | EightWay ->
            let lo = min dx dy
            let hi = max dx dy
            int64 diagStep * lo + int64 baseStep * (hi - lo)

    // Walk the cameFrom chain from `goal` back to the start, yielding start..goal inclusive.
    let private reconstruct (cameFrom: Map<Cell, Cell>) (goal: Cell) : Cell list =
        let rec walk acc c =
            match Map.tryFind c cameFrom with
            | Some prev -> walk (c :: acc) prev
            | None -> c :: acc

        walk [] goal

    // Shared guards for the trivial/degenerate cases before a real search runs.
    let private trivial (maxVisited: int) (isWalkable: Cell -> bool) (start: Cell) (goal: Cell) : Cell list option option =
        if maxVisited <= 0 || not (isWalkable start) || not (isWalkable goal) then Some None
        elif start = goal then Some(Some [ start ])
        else None

    // The A* engine, parameterised by the admissible heuristic `h` (cell -> estimated remaining cost to
    // `goal`, in `baseStep` units, as int64). `astar` passes the octile/Manhattan `heuristic`;
    // `Landmarks.astar` passes `max(octile, ALT)`. A tighter admissible `h` expands fewer nodes but
    // returns the SAME least cost, so swapping `h` never changes optimality — only the specific
    // equal-cost path chosen (the frontier tie-break `(f, h, Col, Row)` shifts with `h`) and the number
    // of expansions. Determinism is unaffected: `h` is integer and total.
    let private astarWith
        (h: Cell -> int64)
        (tie: Cell -> int64)
        (neighbourhood: Neighbourhood)
        (maxVisited: int)
        (isWalkable: Cell -> bool)
        (start: Cell)
        (goal: Cell)
        : Cell list option =
        match trivial maxVisited isWalkable start goal with
        | Some result -> result
        | None ->
            // Frontier is a Set keyed by the TOTAL order (f, h, tie, Col, Row) — Set.minElement is
            // deterministic, so the pop order (hence the path) is bit-identical across runs/platforms.
            // f, h, tie and the g-score are int64: `octile` returns int64, g accumulates in int64, and
            // the `tie` term is int64, so neither the estimate (wide coordinate span) nor the g-score
            // (long path) can wrap the way plain `int` did. `tie` is a pure function of the cell that
            // breaks ties AFTER f and h, so it never changes which cost is optimal — `astar` passes a
            // constant `fun _ -> 0L`, which orders identically to the historical `(f, h, Col, Row)` key
            // (byte-identical output); `astarStraight` passes the straight-line cross-product deviation.
            // Col/Row stay `int`: genuine cell coordinates, and the total order is unaffected.
            let h0 = h start
            let openSet = Set.singleton (h0, h0, tie start, start.Col, start.Row)
            let gScore = Map.ofList [ start, 0L ]

            let rec loop
                (openSet: Set<int64 * int64 * int64 * int * int>)
                (gScore: Map<Cell, int64>)
                (cameFrom: Map<Cell, Cell>)
                (expansions: int)
                : Cell list option =
                if Set.isEmpty openSet || expansions >= maxVisited then
                    None
                else
                    let (f, hCur, tieCur, col, row) = Set.minElement openSet
                    let current = { Col = col; Row = row }
                    let openSet = Set.remove (f, hCur, tieCur, col, row) openSet

                    if current = goal then
                        Some(reconstruct cameFrom current)
                    else
                        let g = gScore.[current]

                        let (openSet, gScore, cameFrom) =
                            neighbours neighbourhood isWalkable current
                            |> List.fold
                                (fun (os, gs, cf) (struct (n, cost)) ->
                                    let tentative = g + int64 cost

                                    match Map.tryFind n gs with
                                    | Some existing when existing <= tentative -> (os, gs, cf)
                                    | prior ->
                                        let hn = h n
                                        let tn = tie n
                                        // Drop any stale open entry for n (same h/tie, old g) before re-adding.
                                        let os =
                                            match prior with
                                            | Some oldG -> Set.remove (oldG + hn, hn, tn, n.Col, n.Row) os
                                            | None -> os

                                        let os = Set.add (tentative + hn, hn, tn, n.Col, n.Row) os
                                        (os, Map.add n tentative gs, Map.add n current cf))
                                (openSet, gScore, cameFrom)

                        loop openSet gScore cameFrom (expansions + 1)

            loop openSet gScore Map.empty 0

    // The zero tie-break — orders identically to the historical `(f, h, Col, Row)` key.
    let private noTie: Cell -> int64 = fun _ -> 0L

    let astar
        (neighbourhood: Neighbourhood)
        (maxVisited: int)
        (isWalkable: Cell -> bool)
        (start: Cell)
        (goal: Cell)
        : Cell list option =
        // The shipped A* — the octile/Manhattan heuristic over the shared `astarWith` engine, with the
        // zero tie-break. Output is byte-identical to before the engine was extracted (the
        // determinism/differential suite guards it).
        astarWith (octile neighbourhood goal) noTie neighbourhood maxVisited isWalkable start goal

    let astarStraight
        (neighbourhood: Neighbourhood)
        (maxVisited: int)
        (isWalkable: Cell -> bool)
        (start: Cell)
        (goal: Cell)
        : Cell list option =
        // Opt-in A* whose tie-break prefers, among equal-(f,h) frontier nodes, the one nearest the
        // straight `start -> goal` line: `cross = |dx1*dy2 - dx2*dy1|` (cell-start × goal-start),
        // int64, zero on the line and larger with perpendicular deviation. It only breaks ties, so the
        // path has the SAME least cost as `astar` — just straighter, and often found with fewer
        // expansions. `astar` itself is untouched.
        let dgx = int64 goal.Col - int64 start.Col
        let dgy = int64 goal.Row - int64 start.Row

        let cross (c: Cell) : int64 =
            let dcx = int64 c.Col - int64 start.Col
            let dcy = int64 c.Row - int64 start.Row
            abs (dcx * dgy - dgx * dcy)

        astarWith (octile neighbourhood goal) cross neighbourhood maxVisited isWalkable start goal

    let bfs
        (neighbourhood: Neighbourhood)
        (maxVisited: int)
        (isWalkable: Cell -> bool)
        (start: Cell)
        (goal: Cell)
        : Cell list option =
        match trivial maxVisited isWalkable start goal with
        | Some result -> result
        | None ->
            // Amortized-O(1) immutable FIFO queue (front list + reversed back list). Deterministic:
            // enqueue order follows the fixed neighbour offsets, dequeue is FIFO — no hashing on order.
            let rec loop
                (front: Cell list)
                (back: Cell list)
                (cameFrom: Map<Cell, Cell>)
                (visited: Set<Cell>)
                (expansions: int)
                : Cell list option =
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
                            neighbours neighbourhood isWalkable current
                            |> List.fold
                                (fun (bk, cf, vis) (struct (n, _cost)) ->
                                    if Set.contains n vis then
                                        (bk, cf, vis)
                                    else
                                        (n :: bk, Map.add n current cf, Set.add n vis))
                                (back, cameFrom, visited)

                        loop rest back cameFrom visited (expansions + 1)

            loop [ start ] [] Map.empty (Set.singleton start) 0

    // ---------------------------------------------------------------------------------------------
    // Jump Point Search (JPS): a grid-specialised A* for UNIFORM-cost grids (roadmap 1.1, work item
    // 015). It "jumps" over runs of symmetric intermediate cells, popping far fewer frontier nodes
    // than plain `astar` while returning a path of the SAME least cost. It shares astar's open-set,
    // its total `(f, h, Col, Row)` order, its int64 g/heuristic, and its `trivial` degenerate-input
    // contract — the only thing that differs is successor generation, which is the jump/prune step.
    //
    // DETERMINISM: costs are integer, the frontier order is the same total order `astar` uses
    // (`Set.minElement` is deterministic), and the direction lists (`orthoOffsets`/`diagOffsets`) are
    // fixed — no float and no hash-iteration influences the result, so `jps` is byte-identical across
    // runs and platforms.
    //
    // jps↔astar CONTRACT (DEC-001): `jps` returns a path of EQUAL COST to `astar`'s (both are optimal
    // on a uniform grid), that path is VALID, and the two AGREE on reachability. It does NOT promise
    // the identical cell SEQUENCE: on a grid with several least-cost paths, JPS's canonical jumps and
    // astar's tie-break pick different equal-cost routes, and demanding identical cells would mean
    // discarding the pruning that is JPS's whole point.
    //
    // NO CORNER CUTTING reshapes the classic rules. A diagonal step needs both shared orthogonals
    // open (as `neighbours` enforces), so the corner-cutting forced neighbours never arise — a
    // DIAGONAL move has no forced neighbours here, and stops only at the goal, a dead end, or when a
    // straight sub-jump finds something. A STRAIGHT move in direction d has a forced neighbour at the
    // perpendicular cell `n+e` exactly when `n+e` is walkable but the cell diagonally behind it
    // (`n - d + e`) is blocked: that obstacle is the only thing that makes reaching `n+e` require
    // turning at `n` rather than earlier on the run, and it is the one place a straight jump must stop.

    // A step from `c` in direction (dx,dy) is legal iff the target is walkable and — for a diagonal —
    // both shared orthogonal neighbours are walkable (the module's no-corner-cutting rule, identical
    // to `neighbours`).
    let private stepOk (isWalkable: Cell -> bool) (c: Cell) (dx: int) (dy: int) : bool =
        let t = { Col = c.Col + dx; Row = c.Row + dy }

        if not (isWalkable t) then
            false
        elif dx <> 0 && dy <> 0 then
            isWalkable { Col = c.Col + dx; Row = c.Row }
            && isWalkable { Col = c.Col; Row = c.Row + dy }
        else
            true

    // Does the STRAIGHT move that arrived at `n` in direction (dx,dy) have a forced neighbour? (Only
    // straight moves do — see the header.) The classic JPS rule: a perpendicular passage `n+e` (`e` one
    // of the two perpendiculars (dy,dx)/(-dy,-dx)) is walkable while the cell diagonally BEHIND it
    // (`n - d + e`) is blocked, so a route arriving from behind is forced to turn at `n`. This never
    // fires on open ground or against the map border (where the blocked `behind` would need `n+e` still
    // walkable *past* an obstacle), which is exactly why it is safe to use as the "real obstacle turn"
    // signal a detection jump looks for.
    let private hasForcedStraight (isWalkable: Cell -> bool) (n: Cell) (dx: int) (dy: int) : bool =
        [ (dy, dx); (-dy, -dx) ]
        |> List.exists (fun (ex, ey) ->
            let side = { Col = n.Col + ex; Row = n.Row + ey }
            let behind = { Col = n.Col - dx + ex; Row = n.Row - dy + ey }
            isWalkable side && not (isWalkable behind))

    // Scan from `from` in ONE direction (dx,dy), returning the first jump point reached, or `None` if
    // the run dead-ends before finding one. `cap` bounds the scan length so the walk is TOTAL even over
    // an unbounded `isWalkable` (e.g. `fun _ -> true` with a goal off the ray, where there is no edge
    // to stop at): `cap` is set to `maxVisited`, and because an `astar` search bounded by `maxVisited`
    // pops settles at most `maxVisited` cells, any path it finds has every straight run <= `maxVisited`
    // long — so a `cap` of `maxVisited` never hides a jump point on a path `astar` could reach within
    // the same budget.
    //
    // `top` distinguishes a *search* jump (`true`, spawned when a popped node is expanded) from a
    // *detection* jump (`false`, spawned to ask "is there a turn point along this perpendicular?").
    // Two stop conditions apply only to a top-level jump, and they must NOT apply to a detection jump:
    //  - GOAL ALIGNMENT (stop at the goal's column moving horizontally / row moving vertically). This
    //    is what carries a `FourWay` search toward an off-axis goal (no diagonal exists to do it), but
    //    on open ground a detection jump would find the goal's row/col from *every* cell, making every
    //    cell a jump point and destroying the corridor pruning — so detection jumps omit it.
    //  - PERPENDICULAR DETECTION. A `FourWay` straight run has no diagonal to notice that a turn onto a
    //    perpendicular corridor is forced (e.g. a wall two cells ahead-and-to-the-side). So at each
    //    cell a top-level straight jump asks whether a one-level detection jump down either
    //    perpendicular finds a real turn point (a forced neighbour or the goal); if so, this cell is a
    //    turn point. Detection jumps do NOT recurse into further detection, which bounds it.
    let rec private jump
        (isWalkable: Cell -> bool)
        (goal: Cell)
        (cap: int)
        (top: bool)
        (dx: int)
        (dy: int)
        (from: Cell)
        : Cell option =
        let diagonal = dx <> 0 && dy <> 0

        let rec go (c: Cell) (budget: int) : Cell option =
            if budget <= 0 || not (stepOk isWalkable c dx dy) then
                None
            else
                let n = { Col = c.Col + dx; Row = c.Row + dy }

                if n = goal then
                    Some n
                elif not diagonal then
                    // Wall dead ahead with an open perpendicular: `n` is the last cell before the wall,
                    // a mandatory turn point. Applies at any level — a detection jump must still report
                    // a turn it finds at a head-on wall.
                    let ahead = { Col = n.Col + dx; Row = n.Row + dy }

                    let perpOpen =
                        isWalkable { Col = n.Col + dy; Row = n.Row + dx }
                        || isWalkable { Col = n.Col - dy; Row = n.Row - dx }

                    // wall-ahead is a top-level-only stop: it fires at the map border too (`ahead` out
                    // of bounds), which a DETECTION jump must not treat as a turn point or every scan
                    // would "find" the border and defeat all pruning on a bounded grid.
                    let wallAhead = top && not (isWalkable ahead) && perpOpen

                    let aligned =
                        top && ((dx <> 0 && n.Col = goal.Col) || (dy <> 0 && n.Row = goal.Row))

                    let perpTurn =
                        top
                        && ((detect n dy dx).IsSome || (detect n (-dy) (-dx)).IsSome)

                    if wallAhead || aligned || perpTurn || hasForcedStraight isWalkable n dx dy then
                        Some n
                    else
                        go n (budget - 1)
                else
                    // Diagonal (EightWay only): `n` is a turn point if a straight DETECTION jump down
                    // either component finds a jump point.
                    match detect n dx 0 with
                    | Some _ -> Some n
                    | None ->
                        match detect n 0 dy with
                        | Some _ -> Some n
                        | None -> go n (budget - 1)

        // A one-level detection jump (never goal-aligned, never spawns its own detection).
        and detect (node: Cell) (sx: int) (sy: int) : Cell option = jump isWalkable goal cap false sx sy node

        go from cap

    // Cells strictly AFTER `a`, up to and including `b`, one unit step at a time along the
    // monodirectional (orthogonal or diagonal) segment `a -> b`. Used to expand a jump-point chain back
    // into the contiguous cell-by-cell path FR-002 requires.
    let private interpolate (a: Cell) (b: Cell) : Cell list =
        let sx = sign (b.Col - a.Col)
        let sy = sign (b.Row - a.Row)
        let steps = max (abs (b.Col - a.Col)) (abs (b.Row - a.Row))
        [ for k in 1..steps -> { Col = a.Col + sx * k; Row = a.Row + sy * k } ]

    let jps
        (neighbourhood: Neighbourhood)
        (maxVisited: int)
        (isWalkable: Cell -> bool)
        (start: Cell)
        (goal: Cell)
        : Cell list option =
        match trivial maxVisited isWalkable start goal with
        | Some result -> result
        | None ->
            // Every legal move direction is expanded from each popped jump point and then JUMPED — no
            // direction is pruned. Pruning directions is where a JPS bug would drop the optimal path;
            // collapsing straight runs into single jumps is what makes `jps` pop fewer nodes than
            // `astar` (FR-006). Optimality is preserved because no direction `astar` would take is
            // withheld, and the jump STOP condition is complete for no-corner-cutting grids.
            let dirs =
                match neighbourhood with
                | FourWay -> orthoOffsets
                | EightWay -> orthoOffsets @ diagOffsets

            let h = octile neighbourhood goal
            let cap = maxVisited // per-jump scan cap; see `jump`.
            let h0 = h start
            let openSet = Set.singleton (h0, h0, start.Col, start.Row)
            let gScore = Map.ofList [ start, 0L ]

            let rec loop
                (openSet: Set<int64 * int64 * int * int>)
                (gScore: Map<Cell, int64>)
                (cameFrom: Map<Cell, Cell>)
                (expansions: int)
                : Cell list option =
                if Set.isEmpty openSet || expansions >= maxVisited then
                    None
                else
                    let (f, hCur, col, row) = Set.minElement openSet
                    let current = { Col = col; Row = row }
                    let openSet = Set.remove (f, hCur, col, row) openSet

                    if current = goal then
                        // Stitch the jump-point chain (start..goal) into the contiguous cell path.
                        let chain = reconstruct cameFrom current
                        let stitched = chain |> List.pairwise |> List.collect (fun (a, b) -> interpolate a b)
                        Some(List.head chain :: stitched)
                    else
                        let g = gScore.[current]

                        let (openSet, gScore, cameFrom) =
                            dirs
                            |> List.fold
                                (fun (os, gs, cf) (dx, dy) ->
                                    match jump isWalkable goal cap true dx dy current with
                                    | None -> (os, gs, cf)
                                    | Some jp ->
                                        let steps = max (abs (jp.Col - current.Col)) (abs (jp.Row - current.Row))

                                        let segCost =
                                            int64 steps * int64 (if dx <> 0 && dy <> 0 then diagStep else baseStep)

                                        let tentative = g + segCost

                                        match Map.tryFind jp gs with
                                        | Some existing when existing <= tentative -> (os, gs, cf)
                                        | prior ->
                                            let hj = h jp
                                            // Drop any stale open entry for jp (old g) before re-adding.
                                            let os =
                                                match prior with
                                                | Some oldG -> Set.remove (oldG + hj, hj, jp.Col, jp.Row) os
                                                | None -> os

                                            let os = Set.add (tentative + hj, hj, jp.Col, jp.Row) os
                                            (os, Map.add jp tentative gs, Map.add jp current cf))
                                (openSet, gScore, cameFrom)

                        loop openSet gScore cameFrom (expansions + 1)

            loop openSet gScore Map.empty 0

    // ---------------------------------------------------------------------------------------------
    // Many-to-one navigation: Dijkstra maps / flow fields.
    // (docs/reports/2026-07-05-game-logic-pathfinding-navigation-design.md §6, §7)
    //
    // `cost c` is the cost to ENTER cell `c`; `cost c <= 0` means impassable. So the walkability
    // predicate `astar`/`bfs` take is exactly `fun c -> cost c > 0`, and one terrain function drives
    // both. A move onto `c` costs `stepCost * cost c`, where `stepCost` is the per-edge weight
    // `neighbours` yields — `baseStep` orthogonal, `diagStep` diagonal — so with `cost = fun _ -> 1`
    // these fields reproduce `astar`'s g-score exactly (the optimality cross-check).
    //
    // Every edge weight is >= `baseStep` (strictly positive), so a settled cell is final: plain Dijkstra.
    // Accumulation is int64 so a large `cost` cannot silently wrap; a relaxation that would exceed
    // Int32.MaxValue is dropped (treated as unreachable) rather than overflowing.

    // The frontier is a Set keyed by the TOTAL integer order (dist, Col, Row) — `Set.minElement` is
    // deterministic, so the settle order (hence the field) is bit-identical across runs/platforms.
    // Exactly one open entry exists per cell: a relaxation removes the stale entry before re-adding.
    //
    // `stepWeight currentCost n stepCost` decides the direction convention, which is the ONE thing
    // that differs between the reverse (`distanceField`) and forward (`reachableWithin`) walks.
    // `currentCost` is the popped cell's cost, evaluated once per pop rather than once per neighbour.
    //
    // The walk SETTLES a `Step`, keeping the predecessor that produced each cell's final cost — the
    // relaxation already knows it, and discarding it is what left callers unable to reconstruct a path
    // from a budgeted search and reaching for `astar`, which prices a different question (#212). The
    // cost-only fields project it back out; `reachable` returns the tree.
    //
    // The `CameFrom` tree is deterministic for the same reason the costs are: cells settle in the total
    // `(dist, Col, Row)` order, and a relaxation improves a cell only on a STRICTLY lower candidate
    // (`existing <= candidate` skips), so among equal-cost predecessors the earliest-settled one wins
    // and no later tie can displace it.
    let private dijkstra
        (neighbourhood: Neighbourhood)
        (cost: Cell -> int)
        (stepWeight: int -> Cell -> int -> int64)
        (admit: int64 -> bool)
        (maxVisited: int)
        (seeds: Cell list)
        : Map<Cell, Step> =
        let isWalkable c = cost c > 0

        let rec loop
            (openSet: Set<int64 * int * int>)
            (tentative: Map<Cell, int64>)
            (cameFrom: Map<Cell, Cell>)
            (settled: Map<Cell, Step>)
            (expansions: int)
            : Map<Cell, Step> =
            if Set.isEmpty openSet || expansions >= maxVisited then
                settled
            else
                let (d, col, row) = Set.minElement openSet
                let current = { Col = col; Row = row }
                let openSet = Set.remove (d, col, row) openSet
                // Popped with the minimum key and all weights > 0 => `d` is final for `current`.
                // A seed has no predecessor, so `CameFrom` is None for exactly the seeds.
                let settled =
                    Map.add
                        current
                        { Cost = int d
                          CameFrom = Map.tryFind current cameFrom }
                        settled
                // `cost` is arbitrary caller code; evaluate it once per pop, not once per neighbour.
                let currentCost = cost current

                let (openSet, tentative, cameFrom) =
                    neighbours neighbourhood isWalkable current
                    |> List.fold
                        (fun (os, tent, cf) (struct (n, stepCost)) ->
                            let candidate = d + stepWeight currentCost n stepCost

                            if candidate > int64 System.Int32.MaxValue || not (admit candidate) then
                                (os, tent, cf)
                            else
                                match Map.tryFind n tent with
                                | Some existing when existing <= candidate -> (os, tent, cf)
                                | prior ->
                                    // Drop any stale open entry for n (old distance) before re-adding.
                                    let os =
                                        match prior with
                                        | Some oldD -> Set.remove (oldD, n.Col, n.Row) os
                                        | None -> os

                                    (Set.add (candidate, n.Col, n.Row) os,
                                     Map.add n candidate tent,
                                     Map.add n current cf))
                        (openSet, tentative, cameFrom)

                loop openSet tentative cameFrom settled (expansions + 1)

        // Only cells that are actually settled are returned, so a `maxVisited` cut-off yields a
        // partial-but-correct field rather than a field with unfinalised tentative values in it.
        let seeds = seeds |> List.filter isWalkable |> List.distinct

        if maxVisited <= 0 || List.isEmpty seeds then
            Map.empty
        else
            let openSet = seeds |> List.map (fun g -> (0L, g.Col, g.Row)) |> Set.ofList
            let tentative = seeds |> List.map (fun g -> g, 0L) |> Map.ofList
            loop openSet tentative Map.empty Map.empty 0

    // The cost-only projection of a settled field. `distanceField`/`reachableWithin` predate the
    // `Step` tree and are defined in terms of it, so they cannot drift from `reachable`.
    let private costsOf (settled: Map<Cell, Step>) : Map<Cell, int> =
        settled |> Map.map (fun _ s -> s.Cost)

    let distanceField
        (neighbourhood: Neighbourhood)
        (maxVisited: int)
        (cost: Cell -> int)
        (goals: Cell list)
        : Map<Cell, int> =
        // Reverse walk: expanding goal-ward cell `current` to `n` means the AGENT steps n -> current,
        // i.e. it enters `current`. So the weight is `baseStep * cost current`.
        // The `CameFrom` tree of a reverse, multi-source walk points goal-ward and means something quite
        // different from `reachable`'s, so it is not exposed — only the costs are.
        //
        // KNOWN COST, deliberately paid: this walk therefore builds a predecessor map it throws away (a
        // `Map.add` per relaxation, a `Step` per settle). The alternative is a second Dijkstra, or a
        // flag threading "don't track" through this one — and a second engine is exactly how the cost
        // map and the move range drift apart, which IS #212. One engine, one determinism guarantee,
        // one place to be wrong. If a profile ever shows this on a hot path, add the flag then, with a
        // number to justify it — not before.
        let stepWeight (currentCost: int) (_n: Cell) (stepCost: int) = int64 stepCost * int64 currentCost
        dijkstra neighbourhood cost stepWeight (fun _ -> true) maxVisited goals |> costsOf

    // The forward, budgeted walk that BOTH `reachable` and `reachableWithin` are views of — so the move
    // range and the cost map are one search and cannot disagree about what is in budget, which is the
    // whole of #212. `reachable` adds the `Endable` split on top; `reachableWithin` projects the costs
    // out. Neither pays for the other's work.
    let private settle
        (neighbourhood: Neighbourhood)
        (maxVisited: int)
        (cost: Cell -> int)
        (budget: int)
        (start: Cell)
        : Map<Cell, Step> =
        if budget < 0 then
            Map.empty
        else
            // Forward walk: the agent steps current -> n, entering `n`. Weight is `baseStep * cost n`.
            let stepWeight (_currentCost: int) (n: Cell) (stepCost: int) = int64 stepCost * int64 (cost n)
            // `budget` prunes, but it does NOT bound: over an unbounded `cost` predicate the reachable
            // set grows quadratically in `budget`, so `maxVisited` is what makes the walk terminate —
            // the same bound, for the same reason, as `astar`/`bfs`/`distanceField`.
            let admit (candidate: int64) = candidate <= int64 budget
            dijkstra neighbourhood cost stepWeight admit maxVisited [ start ]

    let reachable
        (neighbourhood: Neighbourhood)
        (maxVisited: int)
        (cost: Cell -> int)
        (canEndOn: Cell -> bool)
        (budget: int)
        (start: Cell)
        : Reach =
        let steps = settle neighbourhood maxVisited cost budget start

        // `canEndOn` narrows the DESTINATIONS; it never narrows `Steps`. That split is the point:
        // a cell held by an ally is traversable and not endable, and one predicate cannot say so.
        // Applied to `start` too, with no exception — a `canEndOn` meaning "unoccupied" excludes the
        // unit's own cell, and whether standing still is legal is the game's call, not ours.
        let endable =
            steps |> Map.fold (fun acc c _ -> if canEndOn c then Set.add c acc else acc) Set.empty

        { Steps = steps; Endable = endable }

    let reachableWithin
        (neighbourhood: Neighbourhood)
        (maxVisited: int)
        (cost: Cell -> int)
        (budget: int)
        (start: Cell)
        : Map<Cell, int> =
        // The same settled field `reachable` returns, with the predecessors dropped. Deliberately NOT
        // routed through `reachable`: that would fold an `Endable` set over every settled cell (with
        // `canEndOn` constantly true) and then discard it — and this is the primitive the docstring
        // sends AI threat overlays and candidate-cell scoring to.
        settle neighbourhood maxVisited cost budget start |> costsOf

    let pathTo (reach: Reach) (dest: Cell) : Cell list option =
        // Walk the CameFrom chain back to the seed (the one cell whose CameFrom is None). Reads `Steps`,
        // NOT `Endable`: a legal route may pass THROUGH a cell it may not stop on, so reconstructing
        // from the filtered set would dead-end on exactly the routes `canEndOn` exists to allow.
        let rec walk acc c =
            match Map.tryFind c reach.Steps with
            | Some { CameFrom = Some prev } -> walk (c :: acc) prev
            | _ -> c :: acc // the seed (CameFrom = None); the tree has no other roots.

        if Map.containsKey dest reach.Steps then
            Some(walk [] dest)
        else
            None

    let flowField (neighbourhood: Neighbourhood) (field: Map<Cell, int>) : Map<Cell, Cell> =
        // Membership in the field IS walkability here, so the no-corner-cutting rule still applies:
        // a diagonal step is offered only when both shared orthogonals are also in the field.
        let inField c = Map.containsKey c field

        // `Map.fold` walks keys in ascending order, and ties among equally-low neighbours break on
        // (value, Col, Row) — so the flow field is a pure deterministic function of `field`.
        field
        |> Map.fold
            (fun acc c v ->
                let downhill =
                    neighbours neighbourhood inField c
                    |> List.choose (fun (struct (n, _stepCost)) ->
                        match Map.tryFind n field with
                        | Some nv when nv < v -> Some(nv, n.Col, n.Row)
                        | _ -> None)

                match downhill with
                | [] -> acc // A sink (a goal, or a local minimum): no strictly-lower neighbour, so no arrow.
                | xs ->
                    let (_, col, row) = List.min xs
                    Map.add c { Col = col; Row = row } acc)
            Map.empty

    // ---------------------------------------------------------------------------------------------
    // Connected-component early-out (roadmap 1.2, work item 016). Label each maximal walkable region
    // of a BOUNDED grid once by flood fill, then reject an unreachable `start -> goal` in O(1) via
    // `sameComponent` instead of exhausting `maxVisited` on a failed search. Bounded, because the
    // framework holds no map. Connectivity reuses the module's own `neighbours`, so it honours the
    // no-corner-cutting rule under `EightWay` and AGREES with what `astar`/`bfs` can traverse — the
    // differential oracle against `astar`. Only the boolean is exposed: internal label ids never leak,
    // so relabeling can never change a result (determinism).

    type Regions = private { Labels: Map<Cell, int> }

    [<RequireQualifiedAccess>]
    module Regions =

        let build (neighbourhood: Neighbourhood) (bounds: Cell * Cell) (isWalkable: Cell -> bool) : Regions =
            let (a, b) = bounds
            let minCol, maxCol = min a.Col b.Col, max a.Col b.Col
            let minRow, maxRow = min a.Row b.Row, max a.Row b.Row
            // `walk` is in-bounds AND caller-walkable — the predicate the flood fill and every
            // connectivity check share, so an out-of-bounds cell is simply never labelled.
            let walk c =
                c.Col >= minCol
                && c.Col <= maxCol
                && c.Row >= minRow
                && c.Row <= maxRow
                && isWalkable c

            // Row-major scan (rows outer, columns inner); flood each still-unlabeled walkable cell with
            // the next ascending id. The scan order fixes only the internal ids, which never leak.
            let cells =
                [ for row in minRow..maxRow do
                      for col in minCol..maxCol -> { Col = col; Row = row } ]

            let labels =
                cells
                |> List.fold
                    (fun (acc: Map<Cell, int>, next) c ->
                        if not (walk c) || Map.containsKey c acc then
                            (acc, next)
                        else
                            // Flood the whole component reachable from `c` under the module's neighbour
                            // rule; the reachable SET is order-independent, so `sameComponent` is stable.
                            let rec flood (acc: Map<Cell, int>) frontier =
                                match frontier with
                                | [] -> acc
                                | x :: rest ->
                                    if Map.containsKey x acc then
                                        flood acc rest
                                    else
                                        let acc = Map.add x next acc
                                        let ns = neighbours neighbourhood walk x |> List.map (fun (struct (n, _)) -> n)
                                        flood acc (ns @ rest)

                            (flood acc [ c ], next + 1))
                    (Map.empty, 0)
                |> fst

            { Labels = labels }

        let sameComponent (regions: Regions) (a: Cell) (b: Cell) : bool =
            match Map.tryFind a regions.Labels, Map.tryFind b regions.Labels with
            | Some la, Some lb -> la = lb
            | _ -> false

    // ---------------------------------------------------------------------------------------------
    // ALT landmark heuristic (roadmap 1.3, work item 017). Precompute exact integer distances from a
    // handful of pivot ("landmark") cells; the triangle inequality then gives an admissible heuristic
    // far tighter than octile on large/open maps, so `Landmarks.astar` — the same A* engine
    // (`astarWith`) fed `max(octile, ALT)` — expands fewer nodes while returning a path of the SAME
    // least cost as `astar`. Bounded, because the framework holds no map. Each landmark's table is just
    // a `distanceField` from that landmark, so there is no new search engine and the determinism
    // guarantee is inherited. Landmark selection is a pure function of the map (farthest-point
    // sampling from a fixed seed), so the whole thing is byte-deterministic.

    type Landmarks = private { Tables: Map<Cell, int> list }

    [<RequireQualifiedAccess>]
    module Landmarks =

        let build (neighbourhood: Neighbourhood) (isWalkable: Cell -> bool) (count: int) (bounds: Cell * Cell) : Landmarks =
            let (a, b) = bounds
            let minCol, maxCol = min a.Col b.Col, max a.Col b.Col
            let minRow, maxRow = min a.Row b.Row, max a.Row b.Row

            let walk c =
                c.Col >= minCol
                && c.Col <= maxCol
                && c.Row >= minRow
                && c.Row <= maxRow
                && isWalkable c

            // Uniform cost over the walkable region (0 = impassable), so each `distanceField` gives the
            // true shortest-path distance from a landmark to every cell — the exact tables ALT needs.
            let cost c = if walk c then 1 else 0
            let cap = (maxCol - minCol + 1) * (maxRow - minRow + 1) + 1

            let field (from: Cell) : Map<Cell, int> =
                distanceField neighbourhood cap cost [ from ]

            let cells =
                [ for row in minRow..maxRow do
                      for col in minCol..maxCol -> { Col = col; Row = row } ]

            // Deterministic arg-max: the cell of largest score, ties broken by the total `(Col, Row)`
            // order — never a hash-set or float tie-break.
            let pickBest (score: Cell -> int option) : Cell option =
                cells
                |> List.fold
                    (fun best c ->
                        match score c with
                        | None -> best
                        | Some s ->
                            match best with
                            | Some(bs, bc) when bs > s || (bs = s && (bc.Col, bc.Row) <= (c.Col, c.Row)) -> best
                            | _ -> Some(s, c))
                    None
                |> Option.map snd

            match cells |> List.tryFind walk with
            | None -> { Tables = [] }
            | Some seed ->
                // Farthest-point sampling: L1 is the cell farthest from a fixed seed; each further
                // landmark maximises the MINIMUM distance to the landmarks chosen so far (max-min).
                let seedField = field seed

                let firstScore c =
                    if walk c then Map.tryFind c seedField else None

                let rec grow (tables: Map<Cell, int> list) (n: int) : Map<Cell, int> list =
                    if n <= 0 then
                        List.rev tables
                    else
                        // score c = min distance from c to any chosen landmark (None if unreachable
                        // from one, so it is never chosen — keeps landmarks inside the seed's component).
                        let score c =
                            if not (walk c) then
                                None
                            else
                                let ds = tables |> List.map (fun t -> Map.tryFind c t)

                                if List.exists Option.isNone ds then
                                    None
                                else
                                    ds |> List.map Option.get |> List.min |> Some

                        match pickBest score with
                        | Some c -> grow (field c :: tables) (n - 1)
                        | None -> List.rev tables

                match (if count <= 0 then None else pickBest firstScore) with
                | None -> { Tables = [] }
                | Some l1 -> { Tables = grow [ field l1 ] (count - 1) }

        let heuristic (landmarks: Landmarks) (goal: Cell) (cell: Cell) : int =
            // max over landmarks L of |d(L, goal) - d(L, cell)|. Admissible by the triangle inequality
            // over exact shortest-path distances (a landmark that cannot reach both is skipped). Integer
            // and `baseStep`-scaled, so it plugs straight into the A* frontier with no float.
            landmarks.Tables
            |> List.choose (fun t ->
                match Map.tryFind goal t, Map.tryFind cell t with
                | Some dg, Some dc -> Some(abs (dg - dc))
                | _ -> None)
            |> function
                | [] -> 0
                | xs -> List.max xs

        let astar
            (landmarks: Landmarks)
            (neighbourhood: Neighbourhood)
            (maxVisited: int)
            (isWalkable: Cell -> bool)
            (start: Cell)
            (goal: Cell)
            : Cell list option =
            // The shared A* engine over `max(octile, ALT)`. Both components are admissible, so their
            // pointwise maximum is admissible — the path is optimal (same cost as `astar`) and, being
            // >= octile everywhere, it expands no more nodes than `astar` and strictly fewer where ALT
            // is tighter.
            let combined c = max (octile neighbourhood goal c) (int64 (heuristic landmarks goal c))
            astarWith combined noTie neighbourhood maxVisited isWalkable start goal
