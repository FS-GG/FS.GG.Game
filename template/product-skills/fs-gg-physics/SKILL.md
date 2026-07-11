---
name: fs-gg-physics
description: Step a continuously-moving world — the double step buffer as the default, arcade resolution first, and the opt-in mini rigid-body engine with fixed iterations, sorted pairs, and a checksum tripwire.
---

# Physics (Simulation Step) Capability

## Scope

Use this skill when your product **moves things continuously between ticks**: a ball that bounces, a
rock that drifts, a block that falls, a body that gets knocked back. It covers how the world is
stepped, how the renderer sees between two steps, and — when arcade response genuinely is not enough
— how the opt-in impulse layer is configured so it stays deterministic.

Two things this skill is not. It is **not** a general physics engine, and it does not replace
[[fs-gg-collision]]: detection (`Geometry`, `SpatialGrid`) and arcade response (`Resolution`) are that
skill's, and they remain what most products should use. And it is **not** a licence to reach for
rigid-body dynamics because the word "physics" appears in a design. Most games want `Resolution.slide`
and a step timer.

This skill materializes for the `game` and `sample-pack` profiles.

## The double step buffer is the default

> **The double-buffered fixed-step loop (`StepState { Current; Previous; Accumulator }`) is the default
> for any product with a continuously-moving simulation. Stepping the world any other way MUST record
> why in the spec.**

Not a preference — a recorded decision. This mirrors constitution §IV, which already permits advanced
or non-idiomatic choices "only with a justification recorded in the plan." Here the buffer is the
idiomatic choice, and the single world is the deviation that needs the note.

The one-liner that generalizes it: **interpolate when the world moves between ticks; buffer when you
interpolate.**

Three properties fall out of the same two-world bracket, and a single world plus a step timer gives you
none of them:

1. **Sub-tick presentation.** At a 60 Hz sim on a 144 Hz display a single-buffered world visibly
   stutters. `alpha` between `Previous` and `Current` is the only thing that fixes it, and it costs one
   lerp.
2. **A decoupled sim rate.** Physics at 60 Hz while the renderer runs at whatever the monitor does — or
   the sim at 120 Hz for a fast-projectile game — without touching `view`.
3. **The buffer is the memory strategy.** `Loop.advance` does `previous <- current` before every step,
   so you are paying for a world copy either way. Double buffering is what makes that copy a `memcpy`.

### Departures that are already justified

A rule with no worked exceptions reads as a prohibition, gets quietly broken, and stops being a rule.
These are the shapes that have earned the departure:

| Departure | Where | The justification |
|---|---|---|
| Single world + step timer | `Snake.fs`, `Tetris.fs` | **Discrete-grid games.** The world only ever occupies integer cells; there is nothing between `Previous` and `Current` to show. Interpolating a Tetris piece halfway between two rows is wrong, not smooth. |
| Single world + step timer | `Pong.fs` | Continuous motion, so a **weaker** case. Pong would look better double-buffered; it predates the buffer. Legacy, not precedent. |
| No `Previous` at all | headless replay, `Board.evidence` | **Nothing renders.** `Previous` and `alpha` are dead weight in a fold that only fingerprints `Current`. |
| More than two buffers | rollback netcode | Needs a ring of N historical worlds. The sanctioned reason to *widen* the buffer, not narrow it. |
| No accumulator | turn-based games | There is no tick. A turn is a `Msg`. |

Discrete-grid and turn-based worlds do not move between ticks, so they do not buffer. Everything else
does — and a continuously-moving world that single-buffers is a bug with a plausible excuse.

### The three that are never justified

Regardless of how you buffer, these are not departures to record. They are the three ways to lose
replay:

- **Never feed `alpha` back into the simulation.** It is a presentation value. Feeding it back makes the
  sim frame-rate dependent, and no replay of the input stream reproduces the run.
- **Never step with a variable `dt`.** Fixed steps or nothing; `FixedStep.drain` exists to turn a
  variable frame time into a whole number of fixed ones.
- **Never read a wall clock below the effect interpreter.** Time enters the simulation as an argument,
  once, at the top.

## Arcade first, impulse opt-in

`Resolution.pushOut` / `slide` / `knockback` are the first-class default and stay that way. Reach for
them, and for [[fs-gg-collision]]'s narrow phase, before you reach for rigid bodies. A tank hitting a
wall should stop, not rebound; a platformer character is a kinematic capsule, not a dynamic body; a
bullet is a swept segment with no mass ([[fs-gg-ballistics]]), never a dynamic body.

The impulse layer earns its place when the *momentum* is the game: restitution in a breakout paddle,
momentum transfer in asteroids, gravity and stacking in a falling-block sandbox. If you cannot name
which of those your product is, you want arcade.

## Public Contract

> **Availability.** `Physics` is the opt-in heavy layer of `FS.GG.Game.Core`, and the surface below
> ships today, in full. It rests on `Loop`, `FixedStep`, `Geometry` (including `polygonManifold`),
> `SpatialGrid`, `Resolution`, and the `Manifold` / `ConvexPolygon` primitives, which ship too.

`Physics` is `[<RequireQualifiedAccess>]`, and its types live **inside** the module: `Physics.Config`,
`Physics.Static`, `Physics.SBox`.

```fsharp
open FS.GG.Game.Core

[<RequireQualifiedAccess>]
module Physics =
    type BodyKind = Static | Kinematic | Dynamic
    type Shape    = SCircle of radius: float | SBox of halfExtents: Point | SPoly of polygon: ConvexPolygon
    type Material = { Restitution: float; Friction: float }

    type Config =
        { Gravity: Point
          VelocityIterations: int      // fixed. 8 is the usual answer.
          PositionIterations: int      // fixed. 3.
          Slop: float                  // allowed penetration before correction (~0.01)
          Correction: float            // Baumgarte percent (~0.2)
          BounceThreshold: float       // |v·n| below this ⇒ e = 0
          SleepLinearSq: float
          SleepAngular: float
          SleepTicks: int
          BroadPhaseCellSize: float }

    /// Opaque: struct-of-arrays plus a solver cache. Built and read through the module.
    type World

    /// The presentation pose of one body — what a renderer draws. Deliberately NOT a `World`: no
    /// velocity, no inverse mass, no sleep or warm-start cache. `interpolate` returns one per body.
    type Transform = { Position: Point; Rotation: float }

    val empty       : config: Config -> World
    val addBody     : kind: BodyKind -> shape: Shape -> material: Material -> position: Point -> world: World -> struct (int * World)
    val addBodies   : bodies: seq<BodyKind * Shape * Material * Point> -> world: World -> struct (int[] * World)

    /// Sorted, deduped candidate pairs over the `SpatialGrid` broad phase. `(a, b)` with `a < b`.
    val pairs       : world: World -> struct (int * int)[]

    /// THE integrate function. Fits `Loop.advance` verbatim. Pure, total, byte-deterministic.
    /// Reads `dt` and the world; never a clock, never `alpha`.
    val step        : world: World -> dt: float -> World

    /// Narrow phase for a body pair, over `Geometry.polygonManifold`. Not a second implementation.
    val manifold    : world: World -> a: int -> b: int -> Manifold voption

    /// Presentation only. Shortest-arc on rotation. Never feeds `step`.
    val interpolate : alpha: float -> previous: World -> current: World -> Transform[]

    /// The desync tripwire.
    val checksum    : world: World -> uint64
```

`Config` is baked into the `World` at `empty`, so `step : World -> float -> World` is *exactly* the
shape `Loop.advance` wants for `integrate`. That is not cosmetic — it is what lets the two compose with
no adapter:

```fsharp
open FS.GG.Game.Core

let simInterval = 1.0 / 60.0

// `Physics.step` IS the integrate function. No wrapper, no lambda.
let stepped = Loop.advance simInterval Physics.step dtSeconds model.Sim

// At draw time, ask the engine for the interpolated transforms — don't lerp the world yourself.
let t          = Loop.alpha simInterval stepped         // in [0, 1], never NaN
let transforms = Physics.interpolate t stepped.Previous stepped.Current
```

`invMass` and `invInertia` are the inverses on purpose: **zero means infinite means static**, and the
solver divides by nothing. A `Static` body is `invMass = 0.0`, not `mass = infinity`.

## Interpolation belongs to the engine

`Loop.alpha` hands you a number in `[0, 1]` — never `NaN`, never outside the interval, for any `dt` and
any hand-built `StepState`. Do not hand-roll the lerp that consumes it: two things go wrong, and both
have already gone wrong in this org.

- **Lerp presentation state only.** A `World` separates body state (position, rotation — safe to
  interpolate) from the solver cache (accumulated impulses, sleep counters — meaningless to
  interpolate). A naive `lerp previous current alpha` over the whole world blends solver caches, which is
  nonsense at best. `Physics.interpolate` returns `Transform[]`, which is exactly the part that means
  something between two ticks.
- **Angles interpolate on the shortest arc.** A turret rotating through `π` has `Previous ≈ +3.13` and
  `Current ≈ -3.13`. A naive lerp spins it the long way round, every frame, at 60 fps. `shortestArc` is
  four lines and everyone forgets it.

## Determinism

The engine is byte-reproducible under a replayed input stream, and every rule below is load-bearing to
that. Break one and the failure surfaces as a desync days later, not as a test failure now.

- **Fixed iteration count.** Never "iterate until converged" — a convergence tolerance is a float
  comparison, and a float comparison is a replay divergence. `VelocityIterations` and
  `PositionIterations` are `int` config, and they do not adapt.
- **Total, content-derived order.** Manifolds solve in `(A, B, FeatureId)` order; `Physics.pairs` emits
  pairs already sorted and deduped with `a < b`. **Never iterate a `Dictionary` or `HashSet`** — hash
  order leaks into the result and the replay diverges. This is the single most common way to lose
  determinism.
- **Sleeping thresholds compare exactly.** A body sleeps when `speedSq < threshold` for `SleepTicks`
  consecutive ticks. The counter is an `int` tick count, never an accumulation of elapsed seconds.
- **Restitution needs a velocity threshold.** Restitution applies only when `|v·n| > BounceThreshold`.
  Without it a resting box jitters forever, the sleep counter never fires, and the scene never settles —
  a determinism bug and a performance bug wearing the same coat.
- **Guard every divide and `sqrt`.** Prefer squared distances. `Loop`/`FixedStep` already keep a
  non-finite frame time out of the accumulator, so a `NaN` cannot wedge the loop — which is exactly why
  this one is dangerous. It does not stop anything. It enters a body's position or velocity, nothing
  throws, and it spreads through every contact that body touches until the whole scene is `NaN`.
  `checksum` is what catches it.
- **Checksum every tick.** `Physics.checksum` is a stable hash of position, velocity, rotation and
  angular velocity — body state only, never the solver cache. Compare it across a replayed input stream:
  it is the desync tripwire, and it is how you find the divergence on the tick it happened.

**Float now, fixed-point later.** Float is byte-deterministic only on a fixed compiler and ISA. Genuine
cross-platform lockstep needs Q-format fixed-point, which is a separate ADR'd decision and not a reason
to block. Ship float, keep golden checksums green on the CI matrix.

## Performance

The levers, in order of how much they actually buy you:

1. **Sleeping.** A body under the speed threshold for `SleepTicks` stops integrating and stops generating
   contacts. In a settled scene this is the difference between O(n) and O(0), and it is the reason the
   restitution threshold above matters so much: a scene that never settles never sleeps.
2. **Warm starting.** Seed this tick's solver with last tick's accumulated impulse. A warm 4-iteration
   solver beats a cold 10-iteration one on stacks. It needs `Manifold.FeatureId` to be **stable across
   ticks** — without a stable identifier for which pair of features produced the contact, the cache seeds
   last tick's impulse into a different contact and the stack jitters.
3. **Speculative contacts.** Fixed-cost CCD: let a contact resolve before the bodies touch rather than
   substepping to catch the moment they do. No substepping means `dt` never changes, which means replay
   survives. This is why it is the CCD the engine ships.

The `World` is struct-of-arrays, so `previous <- current` is one `Array.blit` per field — a block copy of
contiguous memory — and the solver walks positions and velocities linearly with no pointer chasing.
Steady-state allocation per step is **zero**, once warm. `step` mutates its arrays internally and stays
pure at the boundary; constitution §IV sanctions exactly that, *"where they are clearer or measurably
necessary; say so in a short comment."*

Two worlds that present identically may hold different solver caches — which is why `checksum` hashes
body state only, and why it, not the `World`, is what a golden test compares.

## Common pitfalls

- **Single-buffering a continuously-moving world.** The default is the double buffer. If you carry one
  world and a step timer, the spec must say why — and "it was simpler" is not one of the justified
  departures above.
- **Feeding `alpha` into the simulation.** It is presentation. The moment `step` sees it, the sim is
  frame-rate dependent and the replay is gone.
- **Lerping the whole world.** Blends solver caches and sleep counters. Interpolate transforms, not state.
- **Naive angle lerp.** Rotating through `π` spins the long way round. Shortest arc, always.
- **Modelling a bullet as a dynamic body.** A projectile is a swept segment with no mass — see
  [[fs-gg-ballistics]]. Giving it mass buys tunnelling and a solver cost for nothing.
- **Iterating a `Dictionary`/`HashSet` of contacts.** Hash order enters the solve and the replay diverges.
  Sort by `(A, B, FeatureId)`.
- **Restitution with no velocity threshold.** The resting box jitters forever, never sleeps, and the
  scene costs O(n) when it should cost nothing.
- **`mass = infinity` for a static body.** The solver stores inverses: `invMass = 0.0`. Infinity divides
  into `NaN`, which then spreads through the body and every contact it takes part in — silently.
- **An unstable `FeatureId`.** Warm starting seeds the wrong contact and stacks shake. Derive it from the
  reference and incident edge indices, and keep it stable across ticks.
- **Reaching for impulse when arcade would do.** Restitution, momentum transfer and stacking justify it.
  Wanting the word "physics" in the design does not.

## Build Commands

Run `./fake.sh build -t Dev` then `./fake.sh build -t Verify` in this product.

## Test Commands

Run `./fake.sh build -t Test` to exercise product-owned simulation examples.

## Evidence

Record physics evidence under this product's `readiness/` paths: golden `Physics.checksum` sequences over
a replayed input stream (the desync tripwire), a settling scene that reaches sleep in a bounded number of
ticks, and the arcade-vs-impulse decision itself where a spec departs from the default. Do not copy
framework readiness reports into the product.

## Package Boundary

`Physics`, alongside `Loop`, `FixedStep`, `Geometry`, `SpatialGrid` and `Resolution`, lives in
`FS.GG.Game.Core` — the BCL-only bottom layer, referenced only on the `game` and `sample-pack` profiles.
It depends on nothing and pulls in no viewer, layout, or widget machinery. `Physics` reuses `SpatialGrid`
as its broad phase and `Geometry` as its narrow phase rather than shipping a second copy of either. Keep
rendering in `fs-gg-scene` and host wiring in `fs-gg-skiaviewer`.

## Generated Product

Carry a `Loop.StepState` in your `Model`, drive it with `Loop.advance simInterval Physics.step`, and at
draw time hand `View` the `Transform[]` that `Physics.interpolate` returns for `Loop.alpha`. Keep the
`Rng` in the `Model` ([[fs-gg-game-core]]), resolve non-physical contacts with `Resolution`
([[fs-gg-collision]]), and checksum the world each tick in tests.

## Persistent problems

When a problem outlasts reasonable in-repo attempts, extensive external research is **mandatory** —
consult **official online docs first** (the F#/.NET docs and the driven library's own reference), then
community sources. If your product uses Spec Kit, record findings and resolving links under the feature's
`specs/<feature>/feedback/`; otherwise record them in this skill's **Sources** line and any product-local
`docs/`. Offline, the mandate degrades to recording "research blocked — <why>" rather than hard-failing.

## Related

- [[fs-gg-game-core]] — the `Loop` double step buffer and `FixedStep.drain` this skill's doctrine is
  about, plus the seeded `Rng` a deterministic step threads through.
- [[fs-gg-collision]] — the `Geometry` narrow phase, the `SpatialGrid` broad phase, and the arcade
  `Resolution` layer that stays the first-class default.
- [[fs-gg-ballistics]] — swept projectile advance. A bullet is a segment, not a rigid body.
- [[fs-gg-ai]] — the decision layer above the simulation; agents that push bodies around.
- [[fs-gg-rendering:fs-gg-scene]] — build the `Scene` the interpolated transforms render into.
- [[fs-gg-rendering:fs-gg-skiaviewer]] — drive the fixed-step loop from the host window.

## Sources / links

- F#/.NET docs: https://learn.microsoft.com/en-us/dotnet/fsharp/
- Fixed-timestep loop background: https://gafferongames.com/post/fix_your_timestep/
- Sequential impulses and warm starting (Catto, GDC): https://box2d.org/files/ErinCatto_SequentialImpulses_GDC2006.pdf
- Continuous collision, including speculative contacts (Catto, GDC): https://box2d.org/files/ErinCatto_ContinuousCollision_GDC2013.pdf
