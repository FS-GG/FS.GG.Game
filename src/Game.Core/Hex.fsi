namespace FS.GG.Game.Core

/// Public contract type exposed by the FS.GG.Game.Core package.
/// A hexagonal-grid coordinate in **cube** form. The three axes satisfy the invariant `Q + R + S = 0`,
/// held **by construction** — the only way to build a `Hex` is `Hex.create` (or `Hex.origin` / the
/// arithmetic/converter functions), which always derives `S = -Q - R`, so an off-plane hex cannot
/// exist. Cube is the algorithm-friendly form (distance, rotation, lines are trivial); store hexes in
/// offset/doubled `Cell` form with the converters and run algorithms in cube. Integer, `[<Struct>]`,
/// structural equality — a stable key for the search frontier and set/map bookkeeping. The axial
/// coordinate is simply `(Q, R)`.
[<Struct>]
type Hex = { Q: int; R: int; S: int }

/// Public contract module exposed by the FS.GG.Game.Core package.
/// The hexagonal-grid module (roadmap 2.1): integer cube coordinates with distance, neighbours,
/// arithmetic, rotation, `range`/`ring`/`spiral`, line drawing, rounding, offset/doubled converters,
/// and hex pathfinding. Everything is integer and **byte-deterministic** — fixed enumeration orders
/// and total-order search frontiers, no hash-iteration or float tie-break leakage. The one float
/// touch-point, `round` (used by `lineDraw`), carries a documented tie-break; pixel↔hex conversion is
/// deliberately **out of Core** (a render-adapter float boundary).
[<RequireQualifiedAccess>]
module Hex =

    /// The origin hex `(0, 0, 0)`.
    val origin: Hex

    /// Build a cube hex from its axial `(q, r)`, deriving `s = -q - r` so `q + r + s = 0` holds by
    /// construction. The only primitive constructor.
    val create: q: int -> r: int -> Hex

    /// Componentwise hex addition (vector sum). Preserves the cube invariant.
    val add: a: Hex -> b: Hex -> Hex

    /// Componentwise hex subtraction (`a - b`). `subtract (add a b) b = a`.
    val subtract: a: Hex -> b: Hex -> Hex

    /// Scale a hex vector by an integer factor. `scale h 0 = origin`.
    val scale: h: Hex -> k: int -> Hex

    /// The six cube unit directions in a **fixed documented order** (index 0 = +Q/−R, rotating
    /// clockwise). `neighbours`, `ring`, and `spiral` all enumerate in this order.
    val directions: Hex list

    /// The six hexes adjacent to `h`, in `directions` order.
    val neighbours: h: Hex -> Hex list

    /// The integer cube distance `(|dq| + |dr| + |ds|) / 2` (= `max(|dq|, |dr|, |ds|)`) — the number of
    /// steps between the two hexes. Total over the coordinate space (computed in int64 so a wide span
    /// neither overflows nor throws in `abs`), and `1` between any hex and each of its `neighbours`.
    val distance: a: Hex -> b: Hex -> int

    /// Rotate a hex 60° clockwise about the origin (`[q,r,s] -> [-s,-q,-r]`). Six applications are the
    /// identity; one preserves `distance` to the origin.
    val rotateRight: h: Hex -> Hex

    /// Rotate a hex 60° counter-clockwise about the origin (`[q,r,s] -> [-r,-s,-q]`) — the inverse of
    /// `rotateRight`.
    val rotateLeft: h: Hex -> Hex

    /// All hexes within `n` steps of the origin — cardinality `3n(n+1)+1`, in a fixed order (q outer, r
    /// inner). A negative `n` yields the empty list.
    val range: n: int -> Hex list

    /// The hexes exactly `n` steps from the origin — cardinality `6n` (the single `origin` at `n = 0`,
    /// empty for negative `n`), walked around the six edges from a fixed corner in `directions` order.
    val ring: n: int -> Hex list

    /// `range n` in ring order: the origin, then `ring 1` .. `ring n`. Fixed deterministic order,
    /// empty for negative `n`.
    val spiral: n: int -> Hex list

    /// Round a **fractional** cube coordinate to the nearest valid `Hex`, resetting the component with
    /// the largest rounding error so `q + r + s = 0` is preserved. Ties on the largest error resolve in
    /// the fixed order **S, then Q, then R**, so the result is byte-deterministic. This is the module's
    /// one float touch-point (used by `lineDraw`); pixel↔hex conversion stays out of Core.
    val round: fq: float -> fr: float -> fs: float -> Hex

    /// A contiguous hex line from `a` to `b` — endpoints included, length `distance a b + 1`, each
    /// consecutive pair adjacent. Deterministic: `distance+1` cube-lerp samples fed through `round`,
    /// whose documented tie-break makes the whole line byte-identical across runs and platforms.
    val lineDraw: a: Hex -> b: Hex -> Hex list

    /// Convert a hex to **odd-r offset** storage (odd rows shifted right), on the shared integer `Cell`
    /// (`Col`/`Row`). Exact inverse of `ofOffset`.
    val toOffset: h: Hex -> Cell

    /// Recover a hex from its **odd-r offset** `Cell`. Exact inverse of `toOffset` — `ofOffset (toOffset
    /// h) = h` for every hex.
    val ofOffset: c: Cell -> Hex

    /// Convert a hex to **doubled-width** storage on the shared integer `Cell`. Exact inverse of
    /// `ofDoubled`.
    val toDoubled: h: Hex -> Cell

    /// Recover a hex from its **doubled-width** `Cell`. Exact inverse of `toDoubled` — `ofDoubled
    /// (toDoubled h) = h` for every hex.
    val ofDoubled: c: Cell -> Hex

    /// Public contract function exposed by the FS.GG.Game.Core package.
    /// Breadth-first shortest **hop** path over the hex neighbour set: `Some path` (endpoints included)
    /// or `None` when no walkable path exists. `isWalkable` is the map; the search is bounded by
    /// `maxVisited` (cells expanded) and byte-deterministic (FIFO over the fixed `directions` order, no
    /// hash-iteration leakage). Total on degenerate input: a non-walkable `start`/`goal` or
    /// `maxVisited <= 0` yields `None`; a walkable `start = goal` yields `Some [start]`.
    val bfs: maxVisited: int -> isWalkable: (Hex -> bool) -> start: Hex -> goal: Hex -> Hex list option

    /// Public contract function exposed by the FS.GG.Game.Core package.
    /// A* shortest **hop** path over the hex neighbour set, using the exact cube `distance` as the
    /// admissible heuristic — same contract, endpoint-inclusion, `maxVisited` bound, and degenerate
    /// behaviour as `bfs`, and the same shortest-hop result (every hop costs 1). The frontier is keyed
    /// by the total integer order `(f, h, Q, R)`, so the path is byte-identical across runs and
    /// platforms.
    val astar: maxVisited: int -> isWalkable: (Hex -> bool) -> start: Hex -> goal: Hex -> Hex list option
