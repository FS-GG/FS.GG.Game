---
name: fs-gg-game-core
description: Simulate a generated FS.GG.UI product — deterministic fixed-step loop, seeded RNG, AABB collision, and entity culling.
---

# Game Core (Simulation) Capability

## Scope

Use this skill for the **simulation** half of a game/sim product: advancing world state on a
deterministic fixed timestep, drawing seeded randomness without a shared mutable generator, testing
axis-aligned collisions, and culling off-screen entities. These are pure helpers — they read no
wall-clock and perform no I/O. Rendering the resulting world is `fs-gg-scene`'s job; wiring the loop to
a window is `fs-gg-skiaviewer`'s. This skill materializes for the `game` and `sample-pack` profiles.

## Public Contract

The signatures you consume are bundled with this product:

- `docs/api-surface/Game.Core/Geometry.fsi` — the `Geometry` module (collision / containment / centering),
  on the sim `Rect`/`Point`. Shipped in `FS.GG.Game.Core`, referenced on the `game` and `sample-pack`
  profiles.
- `docs/api-surface/Game.Core/Rng.fsi`, `docs/api-surface/Game.Core/FixedStep.fsi`, and
  `docs/api-surface/Game.Core/Loop.fsi` — the `Rng` value type, the `FixedStep` accumulator drain, and the
  `Loop` double step buffer built on it. Also `FS.GG.Game.Core`, same profiles.
- `docs/api-surface/Game.Core/Pathfinding.fsi` and `docs/api-surface/Game.Core/SpatialGrid.fsi` — deterministic
  grid `Pathfinding` (A*/BFS/JPS, weighted `reachable` move-range, Dijkstra `distanceField`/`flowField`, ALT
  `Landmarks`, connectivity `Regions`, and any-angle `smooth`, all over a walkability predicate) and the
  uniform `SpatialGrid` for range/splash queries. Also `FS.GG.Game.Core`, same profiles — reuse these
  instead of hand-rolling BFS/A* or bucketing.

The rest of the simulation substrate ships in the same package, each with a skill that teaches it:
`Resolution` ([[fs-gg-game:fs-gg-collision]]), `Grids` ([[fs-gg-game:fs-gg-grids]]), `Los` ([[fs-gg-game:fs-gg-line-drawing]]), `Visibility`
and `Fov` ([[fs-gg-game:fs-gg-visibility]]), `Ballistics` ([[fs-gg-game:fs-gg-ballistics]]), `Ai` ([[fs-gg-game:fs-gg-ai]]),
`Effects` ([[fs-gg-game:fs-gg-effects]]), and the opt-in rigid-body `Physics` ([[fs-gg-game:fs-gg-physics]]).
Reach for those before writing your own — they are the authoritative implementations, not starting points
to copy.

Every draw returns a `struct` tuple `(value, nextState)` — deconstruct with `let struct(v, next) = …`.
All helpers are **total**: degenerate inputs return a documented value, they never throw.

## Fixed-timestep march

`FixedStep.drain interval frameTime accumulator` returns `struct(stepCount, newAccumulator)` — the whole
number of fixed steps to run this frame and the carried remainder (all in seconds). It is pure: a
scripted `frameTime` sequence reproduces identical results. A stalled frame is clamped to
`defaultMaxFrameTime` (0.25 s) so the loop can't spiral; pass a tighter clamp with
`drainWith maxFrameTime interval frameTime accumulator`.

Stepping on whole intervals is only half the problem. Render frames land *between* steps, so drawing
`Current` directly makes motion stutter at every frame rate that isn't an exact multiple of the sim
rate. `Loop` closes that gap: it keeps the world one step back and hands you the interpolant.

### The double step buffer is the default

> **The double-buffered fixed-step loop (`StepState { Current; Previous; Accumulator }`) is the default
> for any product with a continuously-moving simulation. Stepping the world any other way MUST record
> why in the spec.**

The one-liner that generalizes it: **interpolate when the world moves between ticks; buffer when you
interpolate.** The sanctioned departures, so the rule reads as a rule and not a prohibition:

| Departure | Where | Justification |
|---|---|---|
| Single world + step timer | `Snake.fs`, `Tetris.fs` | **Discrete-grid games.** The world only occupies integer cells; there is nothing between `Previous` and `Current` to show. Interpolating a Tetris piece halfway between two rows is wrong, not smooth. |
| Single world + step timer | `Pong.fs` | Continuous motion — a **weaker** case. Pong would look better double-buffered; it predates the buffer. Legacy, not precedent. |
| No `Previous` at all | headless replay, `Board.evidence` | **Nothing renders.** `Previous` and `alpha` are dead weight in a fold that only fingerprints `Current`. |
| More than two buffers | rollback netcode | Needs a ring of N historical worlds. The sanctioned reason to *widen* the buffer, not narrow it. |
| No accumulator | turn-based games | A turn is a `Msg`, not a tick. |

Three things stay non-negotiable regardless of buffering, because they are the three ways to lose
replay: **never feed `alpha` back into the simulation**, never step with a variable `dt`, and never read
a wall clock below the effect interpreter.

```fsharp
open FS.GG.Game.Core

// Run world-updates at a fixed 60 Hz regardless of render cadence.
let simInterval = 1.0 / 60.0

// `integrate` is your game step: world -> dt -> world.
let stepped = Loop.advance simInterval integrate dtSeconds model.Sim

// ...and at draw time, lerp the two worlds by how far the presentation clock sits between them.
let t = Loop.alpha simInterval stepped        // in [0, 1], never NaN
let shown = lerpWorld stepped.Previous stepped.Current t
```

`Loop.advance` delegates the drain — and with it the 0.25 s clamp and the totality — to
`FixedStep.drain`, so a `NaN` frame time can never enter the accumulator and freeze the sim. `Previous`
is the world before the **last** step, so a frame that catches up several steps still brackets exactly
the interval you interpolate across. Seed with `Loop.init world`.

Reach for `FixedStep.drain` directly only when you have taken one of the departures above — a
discrete-grid game or a headless replay, where there is no `Previous` worth keeping.

The scaffolded `game` starter ships the drain shape: `Model.SimAccumulator` carries the remainder,
`update` on `Tick dtSeconds` calls `FixedStep.drain simInterval dtSeconds` and runs `stepSim` that many
times, and the host feeds the real elapsed time in (`EvidenceCommands.tick`). Edit `stepSim` to be your
game step; if it moves anything continuously, carry a `StepState` instead of a bare accumulator.

## Collision-safe positions (`Geometry.Vec2`)

Store entity positions/velocities in the scaffold's **collision-safe** `Geometry.Vec2` (`src/<ProductDir>/Vec2.fs`),
**not** a record you name with `X`/`Y`/`Width`/`Height`. Those labels collide with `FS.GG.UI.Scene.Point`/`Rect`,
and because the durable `LayoutEvidence.fs` opens both `Scene` and your model, the collision surfaces as a wall of
errors in a file you must not touch — only after a whole model is written (see [[fs-gg-game:fs-gg-model-swap]]). `Vec2` uses
`Vx`/`Vy` (zero overlap) and crosses into the scene with `toPoint`/`toRect` (express a size via `toRect`, never
`Width`/`Height` labels). This is also the cheapest time to plan your *own* records' labels so two of them don't
share one (the consumer-vs-consumer inference footgun below).

## RNG determinism

`Rng` is a value type (`{ State: uint64 }`) — a SplitMix64 generator you **store in your MVU `Model`**
and thread through `update`. Because two `Rng` with equal state are equal and produce identical
continuations, structural equality of a `Model` implies equal RNG state, so replay and clone stay
deterministic. Never put a mutable `System.Random` in the `Model` — it breaks determinism and structural
equality.

```fsharp
open FS.GG.Game.Core

type Model = { Rng: Rng; Score: int }               // RNG state lives IN the model

let init (seed: uint64) = { Rng = Rng.ofSeed seed; Score = 0 }

// Draw and write the ADVANCED generator back into the model — never reuse the input Rng.
let spawnLane (model: Model) : int * Model =
    let struct (lane, rng') = Rng.nextInt 0 9 model.Rng
    lane, { model with Rng = rng' }

// `split` derives an independent sub-stream (e.g. per-entity) without disturbing the main one.
let forkStream (model: Model) : Rng * Model =
    let struct (child, rng') = Rng.split model.Rng
    child, { model with Rng = rng' }
```

To *test* determinism over this RNG — byte-identical fixtures for a fixed seed, and the
stream-independence property that proves a `split` sub-stream is isolated from the stream it was
derived from — see the seeded-generation section in **[[fs-gg-rendering:fs-gg-testing]]**.

## Collision

Collision detection **and** response now have a dedicated skill — see **[[fs-gg-game:fs-gg-collision]]**. It covers
narrow-phase (`Geometry` box/circle/polygon contacts and segment casts on the shared `Rect`/`Point`),
broad-phase over `SpatialGrid`, and the `Resolution` response layer — which `FS.GG.Game.Core` keeps
deliberately separate from detection. Reach for it instead of hand-rolling AABB or a duplicate bounds
record.

## Rigid-body physics (`Physics`)

The same package also ships `Physics` — an **opt-in** mini rigid-body engine: a world of bodies, a broad
phase, a narrow phase, and a semi-implicit Euler step with a warm-started sequential-impulse contact
solver, over bodies that fall asleep once they settle. It has a dedicated skill,
**[[fs-gg-game:fs-gg-physics]]**, which is where it is taught; this section exists so you know the module
is there and how it meets the loop above.

**Arcade resolution stays the default.** `Resolution.pushOut`/`slide`/`push` ([[fs-gg-game:fs-gg-collision]])
are what most games want — a platformer, a top-down shooter and a grid-sim are all better served by them.
Reach for `Physics` only when you want *simulated* dynamics: stacking, toppling, bouncing, mass and
friction. The two know nothing about each other; pick one per world.

What earns `Physics` a mention in *this* skill is that **`Physics.step` is `Loop.advance`'s `integrate`**.
It is a `World -> float -> World` — exactly the `('world -> float -> 'world)` that *Fixed-timestep march*
asks you for — because `Config` is baked into the world at `Physics.empty`. So it drops into the
double-buffered loop with no adapter, and `Physics.interpolate` is the `lerpWorld` that section leaves to
you: `Physics.interpolate (Loop.alpha simInterval stepped) stepped.Previous stepped.Current` returns one
`Physics.Transform` (`Position`, `Rotation`) per body, in index order. As everywhere here, `alpha` is read
at draw time and **never** reaches `step`.

The public surface is exactly eight vals: `Physics.empty`, `Physics.addBody`, `Physics.addBodies`,
`Physics.pairs`, `Physics.manifold`, `Physics.step`, `Physics.interpolate`, and `Physics.checksum`. Three
things about them surprise people, and the skill above covers all three:

- **Mass is derived, not given.** Neither `Material` nor `addBody` carries one — a body's mass is the area
  of its `Shape` at unit density. `Static` and `Kinematic` bodies have infinite mass and no impulse moves
  them.
- **A settled body sleeps.** It stops being integrated and becomes immovable until something *actually
  moving* touches it, so a wake travels at most one body per tick.
- **Compare worlds with `Physics.checksum`, never structurally.** The checksum hashes **body state** —
  position, velocity, rotation, angular velocity — and nothing else. A `World` also carries sleep counters
  and the warm-start cache, which are derived each step, so two worlds that agree on every body can still
  differ structurally: structural equality reports a divergence that is not one. The checksum is your
  desync tripwire.

## Culling

Keep only the entities whose bounds meet the visible region — an `intersects` (or `containsPoint`)
test against the gameplay `Rect`, reusing the same shared type:

```fsharp
open FS.GG.Game.Core

let visible : Rect = { X = 0.0; Y = 0.0; Width = 1280.0; Height = 720.0 }

let onScreen (bounds: Rect list) : Rect list =
    bounds |> List.filter (fun b -> Geometry.intersects b visible)
```

## Pathfinding

`Pathfinding` routes and reasons over a tile grid using a **predicate you supply** (`Cell -> bool` IS the
map — the framework holds no grid state). Everything here is **deterministic**: integer costs and a total
frontier key `(f, h, Col, Row)` make every result byte-identical across runs and platforms, so it is safe
inside a replayed `update` — no hand-rolled BFS/A*, no `Dictionary`-frontier tie-break footgun.

### Costs speak `baseStep` units — and this trap is silent

Every cost and `budget` here is in **`baseStep` units**, not tiles: an orthogonal move costs `baseStep`, a
diagonal `baseStep * 14 / 10` (an integer √2, so equal-cost ties can never leak through float equality). A
caller whose game speaks *movement points* (a unit with "4 move") **must scale in with `budgetFor`**:

```fsharp
let reach = Pathfinding.reachable FourWay 4096 cost canEndOn (Pathfinding.budgetFor unit.Move) unit.Cell
```

Hand a raw `moveRange` in as a `budget` and it **fails silently and totally**: every step costs at least
`baseStep`, so a budget of `4` settles only the start and the "move range" highlights the one tile the unit
stands on. Nothing throws. Scaling back *out* (`Cost / baseStep`) truncates — fine for a display label,
never to re-derive a budget.

### Which search — pick before you write a line

| You want… | Call | Notes |
| --- | --- | --- |
| Shortest path, unweighted terrain | `astar` / `bfs` | Binary `isWalkable`; minimises `baseStep` distance, not terrain cost. |
| The same, far faster on big uniform maps | `jps` | Jump Point Search — **uniform cost only** (no `cost` fn). |
| The same, hugging the straight line | `astarStraight` | Opt-in; same least cost, ties prefer the straighter route. |
| The same, fewer expansions on large/open maps | `Landmarks.astar` | ALT heuristic; build the landmark tables once. |
| A weighted move-range highlight **and** its paths | `reachable` + `pathTo` | The turn-based-tactics answer; costs both in one search. |
| Distance from a set of goals, for many agents | `distanceField` → `flowField` | A Dijkstra/integration field; roll downhill with `flowField`. |
| "Can start even reach goal?" in O(1) | `Regions.build` + `sameComponent` | Reject a walled-off query without exhausting `maxVisited`. |
| Straighten a finished path to any-angle | `smooth` | Post-hoc string-pull over your LOS. |

`maxVisited` bounds every search (max frontier pops), so an unreachable goal terminates rather than
scanning the unbounded cell space.

### A*, JPS, and the two acceleration paths

`astar neighbourhood maxVisited isWalkable start goal` returns the cost-optimal `Some [start; …; goal]`
(endpoints included) or `None`. `EightWay` refuses to **cut a wall corner** (a diagonal needs both shared
orthogonals walkable); `FourWay` is orthogonal-only. `bfs` is the same shape, hop-minimal.

`jps` is a **drop-in acceleration** for the *uniform-cost* case — it pops far fewer nodes by jumping over
runs of forced-free cells. Same arguments as `astar`, and a path of the **same least cost** — but **not
necessarily the same cells**: where several least-cost routes tie, JPS's canonical jumps and `astar`'s
`(f,h,Col,Row)` tie-break pick different equal-cost paths. That is the pruning working, not a bug; JPS is
still byte-identical across its *own* runs. It has no `cost` function, so for weighted terrain reach for
`reachable`/`distanceField` — never `jps`.

`Landmarks` is the other accelerator: `Landmarks.build` picks pivot cells by deterministic farthest-point
sampling and stores an exact distance table per landmark, then `Landmarks.astar` searches over
`max(octile, ALT)` — **same least cost** as `astar`, strictly fewer expansions where a landmark tightens
the estimate. Worth it on large, open maps queried many times.

`astarStraight` is the cosmetic accelerator: identical cost to `astar`, but among equal-`(f,h)` nodes it
prefers the one nearest the straight start→goal line (an integer cross-product folded into the key). Plain
`astar` is left **byte-identical** — migrate only if you want the straighter look.

### Weighted move-range: `reachable`, and why `Steps` and `Endable` differ

`reachable neighbourhood maxVisited cost canEndOn budget start` is the turn-based-tactics move range: a
Dijkstra from `start` capped at `budget`, keeping predecessors so the highlight and every path come from
**one** search. Two predicates drive it — `cost c` (the price to *enter* `c`; `cost c <= 0` means
impassable) and `canEndOn c` (may a unit *stop* here) — and they answer different questions, which is why
the result carries two sets:

- `reach.Steps` — every settled cell, **including pass-through-only ones**. Reconstruct paths from this
  with `pathTo reach dest`.
- `reach.Endable` — the subset a unit may legally **stop** on. This is the highlight, and the only set you
  offer a destination from.

They differ by exactly `canEndOn` (an ally-occupied tile is traversable but not endable). Highlight from
`Endable`, reconstruct the route from `Steps`, and "path *through* an ally without stopping on them" works
for free. **Do not** compose a separate `reachableWithin` + `astar` for this — they answer different
questions and the unit walks a route it cannot afford.

### Fields for flocks and AI

`distanceField neighbourhood maxVisited cost goals` floods **outward from the goals**, mapping each cell to
its cheapest cost to the nearest goal (each goal `0`). Build it once; any number of agents then roll
downhill — `flowField neighbourhood field` turns it into one arrow per cell (its strictly-lowest
neighbour; a goal or local minimum is *absent*, meaning "arrived/stuck"). **Pass `flowField` the same
`neighbourhood` that built the field** — it can't be checked, and widening `FourWay`→`EightWay` offers
diagonals whose cost was never priced in. This is the substrate the threat, flee, and influence maps in
[[fs-gg-game:fs-gg-ai]] are built on. (`reachableWithin` is the forward dual — a cost map from `start` with
no predecessors — when you want reachability but not the paths.)

### Any-angle smoothing

`smooth losClear path` is a post-hoc string-pull: it drops intermediate waypoints wherever your line-of-
sight test (`losClear`, canonically `Los.lineOfSight` from [[fs-gg-game:fs-gg-line-drawing]]) lets a unit
skip them, turning a jagged grid staircase into straight any-angle segments. The result is a
**subsequence** of the input (never a cell that wasn't on the path), keeps the endpoints, and is never
longer; a fully-visible path collapses to `[start; goal]`. Because `losClear` is an integer grid test the
smoothed path stays byte-deterministic — do **not** smooth with a float raycast.

### The unbounded-grid overflow rule

The predicate is the map, so the coordinate space is **unbounded `int`** — there is no wall to clamp
against, and any cost accumulation or cross-product is therefore computed **wide (`int64`/`bigint`) and
saturated**, never thrown: `budgetFor` saturates to `Int32.MaxValue` rather than wrapping negative, a path
cost past `Int32.MaxValue` reads as unreachable, and `astarStraight`'s cross-product is taken in `bigint`.
Hold your **own** `cost` function to the same discipline — a `cost` that can overflow `int` wraps to a
*negative* (impassable) cell and silently walls off your map.

```fsharp
open FS.GG.Game.Core

let blocked = set [ (2, 0); (2, 1); (2, 2) ]                     // a wall your Model owns
let walkable (c: Cell) = c.Col >= 0 && c.Col < 8 && c.Row >= 0 && c.Row < 8 && not (blocked.Contains(c.Col, c.Row))

let route = Pathfinding.astar EightWay 4096 walkable { Col = 0; Row = 0 } { Col = 7; Row = 3 }
```

## Spatial queries (range / splash)

`SpatialGrid` buckets positioned items once and answers "what is near here" without an O(n²) scan — the
uniform grid the perf guidance recommends, now shipped. Built from a cell size and `(Point * 'item)`
pairs (the sim `FS.GG.Game.Core.Point`); queries are **exact** (no false positives/negatives) and return
items in **insertion order**.

- `SpatialGrid.build cellSize items` — bucket once (hold the grid in your `Model` or rebuild per frame).
- `SpatialGrid.query region grid` — items inside a `Rect` (broad-phase collision / on-screen set).
- `SpatialGrid.queryRadius center radius grid` — items within a radius (splash damage / proximity).

`SpatialGrid` buckets by the sim `Point`, but your model stores positions in the collision-safe
`Geometry.Vec2` (`Vx`/`Vy`) — and the scaffold's `Vec2` only crosses into the *scene*
(`toPoint`/`toRect`), so it ships **no** `Vec2 -> Game.Core.Point` crossing. Write the one-liner
yourself; the return annotation is what stops a bare `{ X = …; Y = … }` from binding to
`Scene.Point` (the clash in *Consumer geometry records colliding with framework `Point`/`Rect`*
below).

```fsharp
open FS.GG.Game.Core

// The crossing the scaffold does not ship. `: Point` is load-bearing, not decoration.
let simPoint (v: Geometry.Vec2) : Point = { X = v.Vx; Y = v.Vy }

let grid = SpatialGrid.build 32.0 [ for e in enemies -> simPoint e.Pos, e.Id ]
let splashed = SpatialGrid.queryRadius (simPoint blast) 48.0 grid   // ids to damage
```

## Grid-sim recipe

The primitives above compose into the loop a grid game actually runs each fixed step. Nothing here reads
the clock or touches shared state, so the whole `stepWorld` stays pure and replay-identical — which is the
point of keeping the `Rng`, the grid, and the effect table *in the `Model`*.

A tower-defense-shaped step, start to finish:

```fsharp
open FS.GG.Game.Core

// Positions are stored in the collision-safe `Vec2`, per the storage rule above — so this recipe needs
// the same `simPoint` crossing as *Spatial queries*. `: Point` is load-bearing, not decoration.
type Creep = { Pos: Geometry.Vec2; Hp: int }
type Tower = { Pos: Geometry.Vec2; Range: float }

let simPoint (v: Geometry.Vec2) : Point = { X = v.Vx; Y = v.Vy }

// 1. Route each creep across the map. The blocked set lives in the Model; the predicate IS the map, so
//    recompute a path only when the walls change — not every step.
let walkable (c: Cell) =
    c.Col >= 0 && c.Col < cols && c.Row >= 0 && c.Row < rows && not (walls.Contains c)
let path = Pathfinding.astar FourWay 8192 walkable spawn goal

// 2. Bucket creeps once per step; each tower then asks "who is in range?" — no O(towers × creeps) scan.
//    `SpatialGrid` buckets by the sim `Point`, so cross at the boundary — never hand it a `Vec2`.
let grid = SpatialGrid.build cellPx [ for cr in creeps -> simPoint cr.Pos, cr ]
let inRange (tower: Tower) = SpatialGrid.queryRadius (simPoint tower.Pos) tower.Range grid
```

Two decisions every step then makes — how a hit lands, and how effects pile up.

### Instant vs projectile

- **Instant (hitscan).** Resolve damage the same step you acquire the target: pick from `inRange tower`,
  subtract HP now. Deterministic and cheap, but there is no travel time — a fast creep cannot outrun it.
- **Projectile.** Spawn a moving entity, advance it on the fixed step, and test the hit with
  `Geometry.sweptIntersects` so a fast shot cannot **tunnel** past a thin creep between steps. Costs extra
  Model state and forces a decision on the mid-flight edge case: if the target dies before impact, choose
  *re-acquire nearest* vs *fizzle* — and make that choice a pure function of the world, never of arrival
  order.

Pick instant for hitscan/beam towers; pick projectile when travel time or leading the target is part of
the game feel.

### Deterministic status-effect stacking

Slows, poisons, and buffs must combine the *same way every replay*. Two rules keep them deterministic:

- **Key by effect kind and fold with an explicit policy — don't iterate a `Dictionary`.** Hold active
  effects as a `Map<EffectKind, Effect>` (or a list you sort by a total key before folding). Iterating a
  `Dictionary`/`HashSet` leaks hash order into the result and breaks replay; a `Map` folds in key order.
- **State the stacking policy per kind and keep magnitudes integer.** *Refresh* (reset duration, keep
  magnitude), *stack-additive* (sum magnitude, cap the total), and *strongest-wins* (max magnitude) are all
  valid — but pick one per kind and compute it in integers (e.g. slow as a *percent*, not `0.35`) so two
  equal-strength effects can never tie-break through floating-point equality.

```fsharp
type EffectKind = Slow | Poison
type Effect = { Kind: EffectKind; Magnitude: int; TicksLeft: int }   // integer magnitude — no float ties

// Strongest-wins magnitude, refresh-duration on re-application — one explicit policy, applied in key order.
let applyEffect (incoming: Effect) (active: Map<EffectKind, Effect>) : Map<EffectKind, Effect> =
    match Map.tryFind incoming.Kind active with
    | Some cur ->
        active
        |> Map.add incoming.Kind
            { cur with
                Magnitude = max cur.Magnitude incoming.Magnitude
                TicksLeft = max cur.TicksLeft incoming.TicksLeft }
    | None -> active |> Map.add incoming.Kind incoming

// Age by folding the Map (key-ordered → deterministic), dropping any whose TicksLeft hit 0.
let ageEffects (active: Map<EffectKind, Effect>) : Map<EffectKind, Effect> =
    (Map.empty, active)
    ||> Map.fold (fun acc kind e ->
        let e' = { e with TicksLeft = e.TicksLeft - 1 }
        if e'.TicksLeft > 0 then Map.add kind e' acc else acc)
```

## Combat, AI, and dice surfaces (the owner-sourced Game.Core home)

`FS.GG.Game.Core` also ships four **decision-and-combat** modules — `Dice`, `Ballistics`, `Effects`, and
`Ai`/`Difficulty` — and this skill is their **owner-sourced surface home**: the canonical per-`val`
documentation lives here (it is what the `game`/`sample-pack` profiles reach), while the dedicated skills
below teach each in depth. Every function here is **pure, total, and deterministic** — no wall clock, no
ambient RNG, no `Map`/`HashSet` iteration order escaping a result — so all of it is safe to call from a
replayed `update`. Reach for these instead of hand-rolling combat math; they are the authoritative
implementations, not starting points to copy.

### Dice / damage distributions (`Dice`)

`Dice` is **exact integer distribution math** — build a distribution, combine distributions, and read
moments *analytically* rather than by sampling, so a weapon's balance is a value you can golden-test.
`Dice.constant v` is the single-outcome `{ v -> 1 }`; `Dice.die sides` and `Dice.uniform lo hi` are the
primitives; `Dice.convolve a b` is the **sum** of two independent rolls (`2d6 = convolve (die 6) (die 6)`);
and `Dice.advantage a b` / `Dice.disadvantage a b` are roll-twice-keep-the-`max`/`min`. Read a built
distribution back with `Dice.outcomes` (the `(outcome, weight)` pairs) and `Dice.totalWeight`, and read its
spread exactly with `Dice.variance` (paired with `mean`). `Dice` has **no dedicated skill** — this section
is its documentation.

```fsharp
open FS.GG.Game.Core

// A 2d6+3 attack as an EXACT integer distribution — no Monte-Carlo run to balance the weapon.
let twoD6Plus3 : Distribution =
    Dice.convolve (Dice.die 6) (Dice.die 6)
    |> Dice.convolve (Dice.constant 3)

let table : (int * int64) list = Dice.outcomes twoD6Plus3   // (outcome, weight), ascending
let weight : int64 = Dice.totalWeight twoD6Plus3
let spread : float = Dice.variance twoD6Plus3               // exact, paired with `mean`

// A d20 check with (dis)advantage is the max/min of two independent rolls.
let d20 = Dice.die 20
let checkHigh = Dice.advantage d20 d20
let checkLow = Dice.disadvantage d20 d20
let loot = Dice.uniform 1 4                                 // flat 1..4
```

### Ballistics: lead solves and splash ([[fs-gg-game:fs-gg-ballistics]])

The one non-negotiable rule — *a fast round is a segment, never a point* — and the swept `step` that
enforces it live in [[fs-gg-game:fs-gg-ballistics]]. The other three surfaces are here: `Ballistics.intercept`
is the lead solve (where to aim a `speed`-unit round so it meets a target drifting at constant velocity —
`ValueSome` the interception point, or `ValueNone` when none exists); `Ballistics.linearFalloff edgeScale`
and `Ballistics.inverseSquareFalloff k` are the two **falloff curves** (linear rim-scaling versus
centre-concentrated), each a `float -> float` over the normalised distance `d ∈ [0,1]`; and
`Ballistics.splash` pairs every item within `radius` of a centre with its multiplier under a caller-chosen
falloff, over a `SpatialGrid` broad phase.

```fsharp
open FS.GG.Game.Core

let shooter : Point = { X = 0.0; Y = 0.0 }
let target : Point = { X = 100.0; Y = 0.0 }
let targetVel : Point = { X = 0.0; Y = 20.0 }

// Lead the target: the point to aim a 600 u/s round at so it intercepts (ValueNone if impossible).
let aimPoint : Point voption = Ballistics.intercept shooter 600.0 target targetVel

// Two curves that play very differently for the SAME radius — the curve is a balance lever.
let linear = Ballistics.linearFalloff 0.5              // 100% centre, 50% at the rim
let concentrated = Ballistics.inverseSquareFalloff 3.0 // most of the damage near the centre

// Splash over a SpatialGrid; `position` MUST agree with how the grid was built.
type Foe = { Id: int; At: Point }
let grid = SpatialGrid.build 32.0 [ for f in ([] : Foe list) -> f.At, f ]
let splashed : (Foe * float) list =
    Ballistics.splash { X = 50.0; Y = 0.0 } 48.0 concentrated (fun f -> f.At) grid
```

### Effects: the mitigation pipeline and status stacking ([[fs-gg-game:fs-gg-effects]])

`Effects` is the **mitigation** layer — what an already-built, already-transported hit becomes once armor,
cover, resistances, and immunities have had their say — plus the status-effect list. Assemble a pipeline
from `Stage`s in the sanctioned order *amplify → resist → subtract → floor*: `Effects.amplify` (the
`Vulnerable` multiplier, runs first), `Effects.resist` (elemental resistance that **`Halt`s** at full
immunity rather than multiplying to a floorable `0.0`), `Effects.floorAt` (the game-defining minimum, runs
last and never after a `Halt`), `Effects.immuneWhen` (a categorical immunity reading the whole `Damage`),
and `Effects.gatedBy` (run a stage only for certain `Source`s — the combinator that makes cover mean
"against declared attacks" and lets a `Source.Periodic` poison tick bypass armor). Run one hit with
`Effects.pipeline`, or a whole region with `Effects.applyAll` (which **seeds** each target at the transport
multiplier, putting falloff *before* mitigation by construction). Status durations are tick counts, never
seconds: apply under a `Policy` with `Effects.applyEffect`, and age every active by one fixed step with
`Effects.tickEffects` (which is the shipped replacement for the hand-rolled `applyEffect`/`ageEffects` fold
sketched under *Deterministic status-effect stacking* above — reach for the module).

```fsharp
open FS.GG.Game.Core

type Unit = { Armor: float; Frost: float; Ghost: bool }
type Kind = Physical | Frost

// The mitigation pipeline, in the order the module's docs write down. Armor is GATED to declared
// attacks, so a poison tick (Source.Periodic) walks past it.
let stages : Stage<Unit, Kind> list =
    [ Effects.amplify (fun _ -> 0.0)
      Effects.immuneWhen (fun u (d: Damage<Kind>) -> u.Ghost && d.Kind = Physical)
      Effects.resist (fun u k -> match k with | Frost -> u.Frost | Physical -> 0.0)
      Effects.gatedBy [ Source.Declared ] (Effects.subtract (fun u _ -> u.Armor))
      Effects.floorAt 1.0 ]

let target = { Armor = 6.0; Frost = 0.25; Ghost = false }
let hit : Damage<Kind> = { Kind = Frost; Source = Source.Declared; Base = 30.0 }

let trace : DamageTrace = Effects.pipeline stages target hit                 // single-target swing
let areaTargets : (Unit * float) list = [ target, 1.0 ]
let traces : (Unit * DamageTrace) list = Effects.applyAll stages hit areaTargets  // the whole blast
```

```fsharp
open FS.GG.Game.Core

// Status effects are the SHIPPED stacking layer — key actives by kind (the caller does this, `'E` is
// opaque here), apply under a policy, and age one fixed step at a time.
type Debuff = { Slow: int }

let poison : Effects.Policy<Debuff> = Effects.Stacks 3                       // additive, capped at 3
let incoming : Effects.Active<Debuff> = { Effect = { Slow = 20 }; TicksRemaining = 90 }
let actives : Effects.Active<Debuff> list = []

let stacked = Effects.applyEffect poison incoming actives
let aged = Effects.tickEffects stacked                                       // decrement, drop expired
```

### AI: fog-limited perception, difficulty knobs, and decision fields ([[fs-gg-game:fs-gg-ai]])

`Ai` is a thin, pure **vocabulary** for the decision layer, not a behaviour-tree framework. Build a
fog-limited `TeamView` with `Ai.view`, then read it — never the world model — with `Ai.viewTick`,
`Ai.spotted` (visible now), `Ai.ghosts` (decaying last-known positions — shooting at these is *correct*),
`Ai.known` (both, ascending by `AgentId`), and `Ai.tryFind`. Give each agent a decorrelated RNG with
`Ai.substream` (keyed on **identity**, so a death cannot shift another agent's rolls), draw a Gaussian aim
error with `Ai.aimError` (`sigma` is a difficulty knob, not a stat), and gate expensive layers by cadence
with `Ai.due`. The decision *fields* are `Ai.threatField` (danger summed over sources — the weighting lives
here in policy, not in `Pathfinding`), `Ai.fleeField` (a scaled, re-relaxed distance field you roll downhill
with `Pathfinding.flowField`), and `Ai.influenceMap` (an integer influence map whose per-cell difference is
a friendly-vs-enemy tension map); pick among candidate plans under a **total** order with `Ai.best`.
`Difficulty` is a knob *vector*, never a stat multiplier — every field takes time or precision away —
laddered as `Difficulty.easy`, `Difficulty.normal`, and `Difficulty.hard`.

```fsharp
open FS.GG.Game.Core

// One team's fog-limited knowledge at the current tick — built from raw sightings, read back as sightings.
let sightings : Sighting<int> list = []
let view = Ai.view 120 30 sightings

let now = Ai.viewTick view
let seenNow = Ai.spotted view          // visible now
let memories = Ai.ghosts view          // decaying last-known — shoot at these
let everything = Ai.known view         // spotted ++ ghosts, ascending by AgentId
let oneFoe = Ai.tryFind (AgentId 7) view

// Per-agent RNG keyed on identity, then a difficulty-scaled aim error off it.
let agentRng = Ai.substream (Rng.ofSeed 42UL) (AgentId 7)
let struct (deviation, _rng') = Ai.aimError 0.05 agentRng

let runThreatThisTick = Ai.due 15 now  // cadence gate: the threat map every 15 ticks, not per-agent-per-tick
```

```fsharp
open FS.GG.Game.Core

let cells : Cell list = [ for c in 0..7 do for r in 0..7 -> { Col = c; Row = r } ]
let hasLos (_a: Cell) (_b: Cell) = true

// Threat map: danger summed over sources; the WEIGHTING is policy, so it lives here, not in Pathfinding.
let threat = Ai.threatField hasLos [ ({ Col = 4; Row = 4 }, 12.0, 5) ] cells

// A flee field is a scaled, re-relaxed distance field — roll downhill with Pathfinding.flowField.
let flee = Ai.fleeField EightWay (-1.2) 8 threat

// An integer influence map; a friendly-vs-enemy tension map is the per-cell difference of two of these.
let cost (_c: Cell) = 1
let influence = Ai.influenceMap EightWay 4096 cost [ ({ Col = 0; Row = 0 }, 10) ]

// Pick the best plan under a TOTAL order: highest score, then the tie tuple, then earliest in the list.
type Plan = { Score: float; Tie: struct (int * int) }
let chosen = Ai.best (fun p -> p.Score) (fun p -> p.Tie) ([] : Plan list)
```

```fsharp
open FS.GG.Game.Core

// Difficulty is a knob VECTOR — time and precision, never a stat multiplier.
let ladder : Difficulty list = [ Difficulty.easy; Difficulty.normal; Difficulty.hard ]
let hardSigma = Difficulty.hard.AimErrorSigma
```

## Common pitfalls

- **Mutable `System.Random` in the `Model`.** Breaks determinism and structural equality; use the
  value-type `Rng` and thread its returned state.
- **Hand-rolled BFS/A* with a non-deterministic tie-break.** Iterating a `Dictionary`/`HashSet` frontier
  leaks iteration order into the path and breaks replay. Use `Pathfinding.astar`/`bfs` — the tie-break is
  a total order over cells, so identical inputs give a byte-identical path.
- **Passing a raw movement range as `budget`.** Costs are in `baseStep` units; a raw "4 move" settles only
  the start cell. Scale it in with `Pathfinding.budgetFor` — the failure is silent, not an exception.
- **Reaching for `jps` on weighted terrain.** `jps` is uniform-cost only (it takes no `cost` function). For
  weighted terrain use `reachable`/`distanceField`; `jps` would ignore your terrain weights entirely.
- **Filtering `reach.Steps` down to `Endable` before reconstructing a path.** `pathTo` needs the full
  `Steps` (it routes *through* non-endable cells); guard the destination against `Endable` separately.
- **O(n²) proximity scans.** Don't test every entity against every other; `SpatialGrid.query`/`queryRadius`
  buckets them for range/splash lookups.
- **Ignoring the returned generator.** `Rng.nextInt`/`nextFloat`/`split` return `struct(value, next)`;
  drawing again from the *input* `Rng` repeats the value. Always keep `next`.
- **Unbounded accumulator (spiral of death).** Don't run `while accumulator >= interval` by hand; let
  `FixedStep.drain` clamp the catch-up, or pass a tighter `drainWith` clamp.
- **Consumer geometry records colliding with framework `Point`/`Rect`.** As in `fs-gg-scene`: if your
  product defines its own `{ X; Y }` record, F# binds a bare `{ X = …; Y = … }` to whichever record is
  in scope last. Annotate the type or qualify fields at the boundary and convert into the framework type.
- **Two of *your own* records exposing `.Pos` (consumer-vs-consumer).** Distinct from the
  framework-vs-consumer clash above: if both a `Creep` and a `Tower` record carry a `.Pos`, a bare
  `let posOf x = x.Pos` makes F# infer the *last-declared* record for `x`, so the helper silently
  type-checks against the wrong type. Annotate the parameter — `let posOf (c: Creep) = c.Pos` — at every
  such `.Pos` (or `.Id`, `.Hp`, …) access shared by two consumer records.
- **DU *case* names colliding — the same look-alike hazard one level over the record clashes above.**
  Just as F# binds a bare `{ X = … }` to the last record in scope, it binds a **bare union case** to the
  **most-recently-defined** case of that name — silently, until a type error two lines later. So a
  `RoomType.Boss` declared after `EnemyKind.Boss` makes `spawnEnemy id Boss` bind the wrong union, and a
  `match`/`fold` over `EnemyKind` that names bare `Boss` folds the wrong case. It bites consumer-vs-consumer
  (`Poison` as both `DamageType` and `StatusEffect`; `Frost` as both `TowerKind` and `DamageType`) and
  consumer-vs-framework (`Symbology.Faction.Enemy` vs a model `Enemy`; `TileKind.Path` vs
  `FS.GG.UI.Scene.Path`). The fix is the record fix one level up: **annotate the `match`/`fold` parameter's
  type** (`let describe (k: EnemyKind) = match k with …`) **and qualify any case whose bare name is shared
  by another in-scope DU** — write `EnemyKind.Boss`, not `Boss` — whether the twin is yours or a
  framework's.
- **`=` read as a named argument inside a call/tuple.** In argument position, `Some (id, dmg, id = primary.Id)`
  parses `id = primary.Id` as the *named argument* `id`, not the boolean equality you meant (and usually
  fails to compile with a confusing message). Wrap the equality in parens to force the comparison:
  `Some (id, dmg, (id = primary.Id))`.

## Build Commands

Run `./fake.sh build -t Dev` then `./fake.sh build -t Verify` in this product.

## Test Commands

Run `./fake.sh build -t Test` to exercise product-owned simulation examples.

## Evidence

Record simulation evidence (determinism replays, collision/culling cases) under this product's
`readiness/` paths. Do not copy framework readiness reports into the product.

## Package Boundary

The modules this skill teaches — `Geometry`, `Rng`, `FixedStep`, `Loop`, `Pathfinding`, `SpatialGrid` (plus
the sim `Point`/`Rect`/`Cell`) — all live in `FS.GG.Game.Core`, and so does **every module the sections
above delegate**: `Resolution`, `Grids`, `Los`, `Visibility`, `Fov`, `Ballistics`, `Ai`, `Effects`, and the
opt-in `Physics`. One package, one `open FS.GG.Game.Core`; the split is between *skills*, not assemblies.
It is referenced only on the `game`/`sample-pack` profiles, and it is the BCL-only bottom layer — it
depends on nothing and pulls in no viewer, layout, or widget machinery. Keep rendering in `fs-gg-scene` and
host wiring in `fs-gg-skiaviewer`.

## Generated Product

Thread the `Rng` through your `Model`, carry a `Loop.StepState` and drive `update` with `Loop.advance`, run
collision with `Geometry`, cull against the visible `Rect`, then hand the world your `View` interpolates by
`Loop.alpha` on as a `Scene`.

## Persistent problems

When a problem outlasts reasonable in-repo attempts, extensive external research is **mandatory** —
consult **official online docs first** (the F#/.NET docs and the driven library's own reference), then
community sources. If your product uses Spec Kit, record findings and resolving links under the feature's
`specs/<feature>/feedback/`; otherwise record them in this skill's **Sources** line and any product-local
`docs/`. Offline, the mandate degrades to recording "research blocked — <why>" rather than hard-failing.

## Related

- [[fs-gg-game:fs-gg-collision]] — detect and resolve collisions: the `Geometry` narrow phase, a `SpatialGrid` broad
  phase, and the `Resolution` response layer kept deliberately separate from detection.
- [[fs-gg-game:fs-gg-grids]] — the grid's geometry *vocabulary*: faces, edges, vertices, and the pixel↔cell map.
- [[fs-gg-game:fs-gg-line-drawing]] — `Los`: the Bresenham/supercover cell walk and discrete grid line-of-sight.
- [[fs-gg-game:fs-gg-visibility]] — `Visibility`'s continuous angular-sweep polygon and `Fov`'s symmetric shadowcasting.
- [[fs-gg-game:fs-gg-ballistics]] — swept projectile advance, lead solves, and splash over the same `Geometry` casts.
- [[fs-gg-game:fs-gg-physics]] — the opt-in rigid-body `Physics` layer: `Physics.step` as this skill's
  `integrate`, the impulse solver's determinism rules, and when *not* to reach for it.
- [[fs-gg-rendering:fs-gg-scene]] — build the `Scene` the simulated world renders into.
- [[fs-gg-rendering:fs-gg-skiaviewer]] — drive the fixed-step loop from the host window.
- [[fs-gg-rendering:fs-gg-layout]] — compute the gameplay region (the visible `Rect`) entities are culled against.
- [[fs-gg-rendering:fs-gg-keyboard-input]] — map input to the `Msg` values your `update` steps the world with.

## Sources / links

- F#/.NET docs: https://learn.microsoft.com/en-us/dotnet/fsharp/
- SplitMix64 (the RNG algorithm): https://prng.di.unimi.it/splitmix64.c
- Fixed-timestep loop background: https://gafferongames.com/post/fix_your_timestep/
