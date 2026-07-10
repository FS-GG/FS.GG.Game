namespace FS.GG.Game.Core

/// Public contract module exposed by the FS.GG.Game.Core package.
/// The mini rigid-body engine. This slice ships the world and its **broad phase** only; the narrow
/// phase, the solver, interpolation and the checksum arrive with `Physics.step`.
///
/// Arcade resolution stays the first-class default: `Resolution.pushOut`/`slide`/`knockback` remain what
/// most games use, and `Physics` is opt-in. Neither module knows the other.
///
/// Every type here is scoped to `Physics` rather than promoted alongside `Point`/`Rect`/`Contact`,
/// following the rule `Grids.Edge` and `Visibility.Segment` already follow: `Physics` is their only
/// consumer, and a type moved *into* `Primitives` later is additive where one moved out of it is
/// breaking. Scoping also keeps names as collision-prone as `Static`, `Dynamic`, `Shape` and `Config`
/// out of a namespace that games `open` wholesale.
[<RequireQualifiedAccess>]
module Physics =

    /// Public contract type exposed by the FS.GG.Game.Core package.
    /// How the solver is allowed to move a body. `Static` never moves. `Kinematic` is moved by the game,
    /// never by a contact. `Dynamic` is moved by both. Only a `Dynamic` body has finite mass, so a pair
    /// with no `Dynamic` member can never resolve — `pairs` omits it.
    type BodyKind =
        | Static
        | Kinematic
        | Dynamic

    /// Public contract type exposed by the FS.GG.Game.Core package.
    /// A body's collision geometry, in body-local coordinates about its position. `SPoly`'s vertices are
    /// offsets from the position and carry `ConvexPolygon`'s convention (convex, CCW-wound) — the same
    /// input assumption `Geometry.polygonContact` makes, checked the same way.
    ///
    /// A degenerate shape — a non-positive or non-finite radius or half-extent, or a ring `Geometry`
    /// would reject as malformed (fewer than 3 vertices, a non-finite coordinate, or zero area) — is a
    /// *no-collision input*, exactly as it is to `Geometry`. `pairs` omits such a body rather than
    /// throwing, so the totality-on-degenerate-input convention holds here too.
    type Shape =
        | SCircle of radius: float
        | SBox of halfExtents: Point
        | SPoly of polygon: ConvexPolygon

    /// Public contract type exposed by the FS.GG.Game.Core package.
    /// The contact-response coefficients of a body. `Restitution` is the bounciness `e`; `Friction` is
    /// the Coulomb coefficient `μ`. Both are read by the solver, not by the broad phase.
    type Material = { Restitution: float; Friction: float }

    /// Public contract type exposed by the FS.GG.Game.Core package.
    /// Simulation-wide tuning, baked into a `World` at `empty` and never changed thereafter. Baking it in
    /// is what makes `Physics.step : World -> float -> World` *exactly* `Loop.advance`'s `integrate`
    /// contract, with no adapter.
    ///
    /// Iteration counts are `int` and fixed: the solver never "iterates until converged", because a float
    /// convergence tolerance is a replay divergence. `SleepTicks` is an `int` count of consecutive ticks,
    /// never an accumulation of elapsed seconds, for the same reason.
    ///
    /// Only `BroadPhaseCellSize` is read by this slice; the rest are the contract the solver slices fill.
    type Config =
        { Gravity: Point
          /// Fixed velocity-solver iterations per step. 8 is the usual answer.
          VelocityIterations: int
          /// Fixed position-correction iterations per step. 3 is the usual answer.
          PositionIterations: int
          /// Penetration tolerated before positional correction acts (~0.01).
          Slop: float
          /// Baumgarte correction fraction (~0.2).
          Correction: float
          /// Restitution applies only where `|v·n|` exceeds this; below it `e = 0`, or a resting box
          /// jitters forever and never sleeps.
          BounceThreshold: float
          SleepLinearSq: float
          SleepAngular: float
          SleepTicks: int
          /// Cell size handed to the `SpatialGrid` broad phase. A non-positive or non-finite value
          /// degrades to a single bucket — slower, never wrong (`SpatialGrid.build`'s own contract).
          BroadPhaseCellSize: float }

    /// Public contract type exposed by the FS.GG.Game.Core package.
    /// A rigid-body simulation world. The representation is **opaque** (hidden in the `.fsi`), exactly as
    /// `SpatialGrid<'T>` is: callers build with `empty`/`addBody` and read through this module, and never
    /// see the interior. Bodies are identified by the `int` index `addBody` returns, which is stable for
    /// the life of the world.
    ///
    /// Immutable at the boundary — every function here takes a `World` and returns a value — so a `World`
    /// is safe to hold inside a `Model`, and safe as the `'world` of `Loop.StepState`.
    type World

    /// Public contract function exposed by the FS.GG.Game.Core package.
    /// A world with no bodies, carrying `config` for its lifetime.
    val empty: config: Config -> World

    /// Public contract function exposed by the FS.GG.Game.Core package.
    /// Append a body and return `struct(index, world')`. The index is the body's identity: it is the
    /// world's previous body count, so indices are dense and ascending in insertion order, and `pairs`
    /// reports pairs in terms of them.
    ///
    /// `position` is the body's origin; `shape` is expressed relative to it. A degenerate `shape` or a
    /// non-finite `position` is accepted and indexed (so later indices do not shift), but the body
    /// collides with nothing — see `Shape`.
    ///
    /// Cost is O(body count): the world's arrays are copied. This is the *build* path, not the step path
    /// — `Physics.step` is the one that must not allocate.
    val addBody:
        kind: BodyKind -> shape: Shape -> material: Material -> position: Point -> world: World -> struct (int * World)

    /// Public contract function exposed by the FS.GG.Game.Core package.
    /// The broad phase: every candidate pair, as `struct(a, b)` with `a < b`, **sorted ascending by
    /// `(a, b)` and free of duplicates**. Unsorted pair order is the single largest determinism leak in a
    /// physics step, so the order is part of this contract rather than an accident of the container: the
    /// result is a byte-identical array for a byte-identical world.
    ///
    /// A pair is returned exactly when all three hold:
    ///
    /// - **at least one body is `Dynamic`** — no other pair can ever resolve, so emitting it would only
    ///   buy a narrow-phase call that cannot produce motion;
    /// - **both bodies are collidable** — neither shape is degenerate nor position non-finite (`Shape`);
    /// - **their world-space AABBs overlap on positive area**, decided by `Geometry.intersects` and so
    ///   inheriting its **strict-edge convention: two boxes that merely touch are not a pair.** That
    ///   agrees with the narrow phase, which likewise reports no contact on a touch.
    ///
    /// The result is therefore *exact over AABBs* — no false positives and no false negatives, the same
    /// promise `SpatialGrid.queryBounds` makes — rather than the loose superset a broad phase is normally
    /// allowed. Narrowing further (are the *shapes*, rather than their boxes, in contact?) is the narrow
    /// phase's job. Exactness holds at **any** magnitude: a body whose extent is too large to bucket
    /// costs **only itself** its spatial acceleration, never its pairs and never the rest of the world's.
    ///
    /// Pure, total and deterministic: identical bodies added in an identical order yield an identical
    /// array. Never throws.
    val pairs: world: World -> struct (int * int)[]
