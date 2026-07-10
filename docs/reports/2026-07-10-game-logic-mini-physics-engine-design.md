# Game logic skill — a performant mini physics engine

- **Date:** 2026-07-10
- **Owner:** FS.GG.Game
- **Status:** Design proposal (pre-ADR). Cashes in **P3 of the 2026-07-05 collision design**
  (§6.2, "Impulse-based (optional heavy layer)"), which specified this and was never built.
- **Scope:** A small, deterministic, allocation-light 2-D rigid-body engine in `FS.GG.Game.Core`:
  bodies, semi-implicit integration, a sequential-impulse contact solver with friction, speculative
  CCD, sleeping, and warm starting. Plus the two prerequisites it exposes and the one bug it trips
  over. Excludes joints, ragdolls, soft bodies, and everything in §4's out-list.
- **Language/target:** F# on `net10.0`, BCL-only, byte-deterministic, pure at the boundary,
  Elmish/MVU-friendly.
- **Builds on:** `Geometry` (manifolds, swept tests, SAT/OBB) · `SpatialGrid` (broad phase) ·
  `Resolution` (the arcade layer that stays first-class) · `FixedStep` (the accumulator).
- **Sibling designs:** [collision](2026-07-05-game-logic-collision-detection-design.md) §5–§7 is the
  parent document — this deepens its §6.2 and §10 · [grids](2026-07-05-game-logic-grids-spatial-partitioning-design.md) ·
  [mini tanks](2026-07-10-mini-tanks-product-design.md) §5, §10.
- **Grounding:** `breakout` (restitution bounce), `asteroids` §4.1–4.2 (momentum, drag, velocity
  inheritance), `sandbox-survival` (gravity, falling blocks), `roguelike-dungeon-crawler` §4.4
  (knockback), `Breakout1`'s live `ResponseRule.Bounce of restitutionPercent:int`.

---

## 1. TL;DR

Nothing here is a new idea. **The collision design already specified this engine** — the impulse
formula, Coulomb friction, Baumgarte correction, a `resolveImpulse` signature, and a P3 phasing
tagged `M–L`. It was never implemented, and `Resolution.fsi` now carries a line explicitly fencing it
off: *"Impulse-based physics (mass/restitution/friction) is a separate, out-of-scope heavy layer."*

This document says: build that layer, make it fit the double-buffered fixed-step loop, and note that
doing so surfaces three things that must land first.

1. **The double-buffered loop is in the wrong repo.** `StepState`/`advance`/`alpha` live in
   `FS.GG.UI.Canvas` (Rendering). `Game.Core` reaches up to nothing (ADR-0022 §2), so a headless
   physics sim cannot use the loop it is supposed to be the default for. (§2)
2. **That loop has a permanent-freeze NaN bug** which `FixedStep.drain` was explicitly hardened
   against. (§2.2)
3. **`Contact` cannot carry an impulse.** It has a normal and a depth, no contact points and no body
   ids. Angular impulse needs both. (§5)

And one framing that makes the whole thing cheap: **double buffering and struct-of-arrays are
complementary, not in tension.** The loop already pays one world copy per step. Choose the layout so
that copy is a `memcpy` instead of N allocations, and the physics engine gets its performance from
the buffering it was going to pay for anyway. (§3)

---

## 2. The double buffer is the integration contract

`Loop.advance` takes `integrate : 'world -> dt -> 'world` and runs whole fixed steps, keeping the
last two worlds so the renderer can interpolate:

```fsharp
type StepState<'world> = { Current: 'world; Previous: 'world; Accumulator: float }
val advance : dt:float -> integrate:('world -> float -> 'world) -> frameTime:float -> StepState<'world> -> StepState<'world>
val alpha   : dt:float -> StepState<'world> -> float
```

`Physics.step : Config -> World -> float -> World` is exactly that `integrate`. Nothing needs
inventing at the seam. **The physics engine's whole job is to be a well-behaved `integrate`.**

The corpus rule that makes this safe is stated in the collision design §7 and must be repeated in the
physics `.fsi`: **`alpha` never feeds the simulation.** It is a presentation value. A physics step
that reads `alpha`, or reads a wall clock, or reads `frameTime` rather than `dt`, is not replayable.

### 2.1 The loop is stranded upstream

`Loop` lives in `FS.GG.UI.Canvas` — a Rendering package. `FS.GG.Game.Core` is BCL-only and depends on
nothing. So today:

- A headless, deterministic physics sim in `Game.Core` **cannot use the double-buffered loop**. It
  gets `FixedStep.drain : interval -> frameTime -> accumulator -> struct(int * float)`, which returns
  a step count and a carried accumulator, and it must hand-roll `Previous`/`Current`/`alpha` itself.
- Which means every game hand-rolls it. `CanvasDemo` writes its own world lerp. `Breakout1` writes
  its own. The tanks design assumes one exists.

`Loop.advance` is a strict superset of `FixedStep.drain`: the same 0.25 s spiral clamp, the same
`while acc >= dt` drain, plus the two-world bracket. **They are two implementations of one accumulator
in two repos** — the `game-starter-two-copies` pattern again, one layer down.

**Proposal.** Move the double buffer into `Game.Core` as a `Loop` module implemented *on top of*
`FixedStep.drain` (drain gives the step count; the fold gives `Previous`/`Current`). This is where the
physics engine's `integrate` contract has to live if `Game.Core` is to own physics at all.
([#44](https://github.com/FS-GG/FS.GG.Game/issues/44), [Rendering#269](https://github.com/FS-GG/FS.GG.Rendering/issues/269).)

**Not by re-export.** `src/Canvas/Canvas.Lib.fsproj` has exactly one `ProjectReference`, to `Scene`.
Re-exporting `Game.Core.Loop` from `FS.GG.UI.Canvas` would create a new
`FS.GG.UI.Canvas → FS.GG.Game.Core` package edge — and `FS.GG.UI.Canvas` ships on *every* profile,
including `app`, `headless-scene`, and `governed`. Dragging the game simulation core into every
FS.GG.UI app to serve one type and three functions is the wrong trade. Today the only
`rendering → game` edge in the registry is the template pin. Do not create a library edge.

Instead: `Canvas.Loop` is fixed in place (§2.2), deprecated pointing at `Game.Core.Loop`, and retired
at the next `FS.GG.UI.Canvas` major. Blast radius is narrower than it looks — `Loop` only matters to a
continuously-moving simulation, and the `game`/`sample-pack` profiles that have one already pin
`FS.GG.Game.Core`.

**And this needs an ADR, not just an issue.** `Canvas.Lib.fsproj` classifies `Loop` deliberately —
*"Canvas now carries only the render-adjacent surfaces: pure Elements, **the render Loop**, and the
Persistence request vocabulary"* — but **ADR-0022 itself never mentions `Loop`.** That classification
is an implementation-time judgement in a build-file comment, not a ratified decision, and it does not
survive inspection: `advance` contains no rendering, and `StepState.Previous` is the world one tick
ago — simulation state. Only `alpha` is render-adjacent, and it is `Accumulator / dt`, pure arithmetic
whose *consumer* is the renderer, exactly as the consumer of `FixedStep.drain` is the renderer. The
sim/render cut ADR-0022 P5 drew moved `drain` down and left the buffer built on top of it upstream.

### 2.2 …and it silently freezes on a single NaN

`Loop.advance` clamps with `max 0.0 (min frameTime maxFrameTime)`. F#'s generic `min`/`max`
**propagate NaN**. Verified:

```
min nan 0.25 = NaN          max 0.0 nan = NaN
```

So one `NaN` frame time makes `Accumulator = NaN`. Then `while acc >= dt` is `false` — forever, since
`NaN >= dt` is always false — and `alpha = NaN / dt = NaN`. **The simulation stops stepping, silently
and permanently, and never recovers.** No exception, no diagnostic; the world just stops while the
window keeps painting.

`FixedStep.drain` documents the exact hardening that prevents this:

> a non-finite or negative `accumulator` is treated as empty (so a stray NaN `dt` can never poison the
> loop)

The two accumulators disagree about the single most important failure mode. This is pitfall #8 of the
collision design ("NaN silently poisons every comparison and determinism"), live in shipped code. Fix
before making this loop the physics default.

### 2.3 The double step buffer is the default — departing from it must be justified

State this as doctrine in the `fs-gg-physics` skill, and in `fs-gg-game-core` alongside the loop:

> **The double-buffered fixed-step loop (`StepState { Current; Previous; Accumulator }`) is the
> default for any product with a continuously-moving simulation.** A product that steps its world any
> other way MUST record why in its spec. Not a preference — a recorded decision.

This mirrors constitution §IV, which already says advanced or non-idiomatic choices are permitted
"only with a justification recorded in the plan." The buffer is the idiomatic choice; the single
world is the deviation that needs the note.

**Why it is the default.** Three properties fall out of the same two-world bracket, and you get none
of them from a single world plus a step timer:

1. **Sub-tick presentation.** At a 60 Hz sim on a 144 Hz display, a single-buffered world visibly
   stutters. `alpha` between `Previous` and `Current` is the only thing that fixes it, and it costs
   one lerp.
2. **A decoupled sim rate.** The physics can run at 60 Hz while the renderer runs at whatever the
   monitor does — or the sim at 120 Hz for a fast-projectile game — without touching `view`.
3. **The buffer is the memory strategy** (§3). You are paying for the copy either way; double
   buffering is what makes it a `memcpy` and gives the engine zero steady-state allocation.

**Departures that are already justified, in this codebase.** The rule is not "never" — it is "say
why," and these are the shapes that have earned it:

| Departure | Where | The justification |
|---|---|---|
| Single world + step timer | `samples/.../Games/{Snake,Tetris}.fs` | **Discrete-grid games.** The world only ever occupies integer cells; there is nothing between `Previous` and `Current` to show. Interpolating a Tetris piece halfway between two rows is wrong, not smooth. |
| Single world + step timer | `samples/.../Games/Pong.fs` | Continuous motion, so this one is a **weaker** case — Pong would look better double-buffered. It predates `Canvas.Loop`. Treat it as legacy, not precedent. |
| No `Previous` at all | `SymbologyBoard`'s `Board.evidence`; any headless replay | **Nothing renders.** `Previous` and `alpha` are dead weight in a fold that only fingerprints `Current`. The evidence path is a fold over `Msg list`, not a loop. |
| More than two buffers | (none yet) | **Rollback netcode** needs a ring of N historical worlds, not two. If lockstep ever lands, this is the sanctioned reason to widen the buffer, not narrow it. |
| Turn-based | `turn-based-tactics.md` | There is no accumulator at all. A turn is a `Msg`, not a tick. |

The pattern in that table is the actual rule, and it is worth stating in the skill as a one-liner:

> **Interpolate when the world moves between ticks. Buffer when you interpolate.** Discrete-grid and
> turn-based worlds do not move between ticks, so they do not buffer. Everything else does — and a
> continuously-moving world that single-buffers is a bug with a plausible excuse.

**What the skill must forbid outright,** regardless of buffering: feeding `alpha` back into the
simulation, stepping with a variable `dt`, and reading a wall clock below the effect interpreter.
Those are not justifiable departures; they are the three ways to lose replay (§6).

---

## 3. Why "performant" and "double-buffered" are the same decision

This is the load-bearing insight, and it is the reason a *mini* engine can be fast without a mutable
scene graph.

`Loop.advance` does `previous <- current` before every step. **A world copy per step is not an
optimization choice, it is already in the contract.** The only question is what that copy costs:

| World layout | Cost of `previous <- current` | Cost of one step |
|---|---|---|
| `{ Bodies: Body list }` (idiomatic) | O(1) pointer, but the *step* rebuilds the list | N allocations, N cache misses, GC churn |
| `{ Bodies: Body[] }` (array of records) | O(1) pointer; step allocates a new array of refs | N allocations (records are heap) |
| **`{ Pos: Point[]; Vel: Point[]; … }` (SoA of structs)** | **one `Array.blit` per field — memcpy** | **zero allocations; linear scans** |

With struct-of-arrays, `Previous` and `Current` are literally the two buffers, `previous <- current`
is a block copy of contiguous memory, and the solver's inner loops walk `Pos`/`Vel` linearly with no
pointer chasing. **Double buffering stops being a tax and becomes the memory strategy.**

Two consequences to state explicitly:

- **Mutate inside `step`, stay pure at the boundary.** `Physics.step` takes a `World` and returns a
  new `World`; internally it copies the arrays once and mutates them in place across the integrate →
  broad-phase → solve → correct passes. Constitution §IV sanctions exactly this — *"Mutation and loops
  are allowed where they are clearer or measurably necessary; say so in a short comment."* Say so.
- **Ping-pong, don't allocate.** Because `Loop` keeps precisely two worlds, the engine can own two
  backing buffer sets and swap them. Steady-state allocation per step: **zero**, once warm.

Everything else in this document is standard. This section is the design.

### 3.1 The other three performance levers, in order of value

1. **Sleeping.** A body whose linear and angular speed stay under a threshold for N ticks stops
   integrating and stops generating contacts. In a settled scene this is the difference between O(n)
   and O(0). Determinism-safe if the counter is an `int` tick count and the threshold an exact
   comparison — never elapsed float seconds.
2. **Warm starting.** Cache each contact's accumulated normal and tangent impulse, keyed by
   `(idA, idB, featureId)`, and seed next tick's solver with it. This is the single biggest
   quality-per-iteration lever: a warm-started 4-iteration solver beats a cold 10-iteration one on
   stacks. The cache is cross-tick state, so it lives in the `World` — which the double buffer
   already carries.
3. **Speculative contacts for CCD.** The collision design's own table already picks this: *"O(1)/pair,
   deterministic, impulse solvers, cheap CCD."* Rather than substepping (which multiplies cost and
   changes `dt`, breaking replay), generate contacts for pairs that *will* touch within `dt` and let
   the solver refuse to let them close. Fixed cost, fixed `dt`, no tunneling.

---

## 4. What "mini" means — the scope fence

**In.** Static / kinematic / dynamic bodies. `invMass`, `invInertia` (zero ⇒ infinite ⇒ static).
Semi-implicit Euler (the only cheap integrator that is stable under contact). Sequential-impulse
contact solver with a **fixed iteration count**. Coulomb friction as a tangent impulse clamped to
`μ·j`. Baumgarte / linear positional correction with slop and percent. Restitution with a velocity
threshold. Speculative contacts. Sleeping. Warm starting.

**Out.** Joints and constraints beyond contact. Ragdolls. Soft bodies. Convex decomposition. Solver
islands and parallelism. BVH broad phase (`SpatialGrid` is the broad phase). Continuous *rotational*
CCD. Fluid, cloth, anything with the word "solver" in front of "iterations" that isn't the contact one.

**Unchanged.** *Arcade / kinematic resolution stays the first-class default.* The collision design is
unambiguous: *"Ship arcade first-class, impulse as an advanced module."* `Resolution.pushOut` /
`slide` / `knockback` remain what most games use, and `Physics` is opt-in.

### 4.1 An honesty note about the tanks game

The mini-tanks design (§11) uses the **arcade** layer, not this one. Tanks want no bounce, no
stacking, no ragdoll — a 60-tonne vehicle hitting a wall should stop, not rebound. The one place
tanks want impulse is ramming (§10 item 3), momentum transfer between two dynamic bodies.

So this engine is **not** justified by tanks. It is justified by `breakout` (restitution), `asteroids`
(momentum, drag, velocity inheritance), `sandbox-survival` (gravity, falling blocks), and
`roguelike` (knockback) — four TestSpecs, which is the recurrence bar. Building it *for* tanks would
be building it for the one consumer that mostly does not need it.

---

## 5. What `Game.Core` is missing

| # | Gap | Why it blocks |
|---|---|---|
| 1 | **`Contact` carries no contact point and no ids.** `{ Normal: Point; Depth: float }` | An angular impulse needs the lever arms `r_A`, `r_B` from each centre of mass to the contact point. Without a point there is no torque, and without torque a box lands flat and never tips. Also: warm-start caching needs a stable pair key. |
| 2 | **`polygonContact` returns depth + axis, not points.** | SAT gives the separating axis. Contact *points* need reference-face selection plus Sutherland–Hodgman clipping of the incident face. That is a real, missing chunk of narrow phase. |
| 3 | **No `Body`, no velocity, no integration.** | Core has geometry and a response layer that transforms one position. Nothing owns `(pos, vel, rot, angVel, invMass, invInertia)`. |
| 4 | **Broad phase returns items, not pairs.** `SpatialGrid.query` gives a `'T list` per region. | The design's §10 asks for `IBroadPhase.Pairs : Shape[] -> struct(int*int)[]` — **sorted and deduped**, because unsorted pair order is the #1 determinism leak (pitfall #10). |
| 5 | **No `Fixed` (Q-format).** | §6. Deferred, deliberately. |

Gap 1 is additive and cheap: a new `Manifold` type alongside `Contact`, not a change to it.

```fsharp
/// Up to two contact points for a 2-D convex pair — the impulse counterpart of `Contact`.
type Manifold =
    { A: int; B: int                 // body indices; A < B always (the canonical pair key)
      Normal: Point                  // unit, from A toward B
      Depth: float
      Points: Point[]                // 1 for circle pairs, 1–2 for polygon pairs
      FeatureId: int }               // stable across ticks — the warm-start cache key
```

`FeatureId` is the part people forget. Without a stable identifier for *which* pair of features
produced this contact, the warm-start cache seeds last tick's impulse into a different contact and
stacks jitter. Derive it from the reference/incident edge indices.

---

## 6. Determinism

The collision design §7 already wrote this section; the engine must honour it verbatim.

- **Fixed iteration count.** Never "iterate until converged." Convergence tolerance is a float
  comparison and a replay divergence. Velocity iterations and position iterations are `int` config.
- **Total, content-derived order.** Sort manifolds by `(A, B, FeatureId)` before solving. Never
  iterate a `Dictionary`/`HashSet`. The broad phase emits pairs already sorted and deduped (§5 gap 4).
- **Sleeping thresholds compare exactly.** Tick counters are `int`. A body sleeps when
  `speedSq < threshold` for `N` consecutive ticks. No elapsed-seconds accumulation.
- **Restitution needs a velocity threshold** (`e` applies only when `|v·n| > bounceThreshold`), or a
  resting box jitters forever and the sleep counter never fires. This is a determinism *and* a
  perf fix.
- **Guard every divide and `sqrt`.** Prefer squared distances. §2.2 is what a single unguarded NaN
  costs.
- **Checksum every tick.** `SceneCodec.packageIdentity` already gives the fingerprint mechanism for
  scenes; the physics world needs the same — a stable hash of `Pos`/`Vel`/`Rot`/`AngVel`, compared
  across a replayed input stream. The collision design calls this "the desync/regression tripwire."

**Float now, `Fixed` later, behind an ADR.** The collision design §7 is right that float is
byte-deterministic only on a fixed compiler + ISA, and that cross-platform lockstep needs Q-format
fixed-point. It also says the fixed-point decision *"warrants a Rendering-local ADR."* Do not block
the engine on it. Ship float — which is what `Geometry` and `Point` already are — get golden checksums
green on the CI matrix, and let the `Fixed` tier be a separate, later, ADR'd decision. Shipping
fixed-point first would mean re-typing every existing `Geometry` function.

---

## 7. Interpolation belongs to the engine

`Loop.alpha` hands the renderer a number in `[0,1)`. **Nothing in the org lerps a world with it.**
`CanvasDemo` hand-rolls its own lerp; so does `Breakout1`. Every game reinvents it, and two of them
will get the angle wrong.

Two rules the engine should encode rather than leave to callers:

- **Lerp presentation state only.** `World` must separate `BodyState` (pos, rot — safe to
  interpolate) from `SolverCache` (accumulated impulses, sleep counters — meaningless to
  interpolate). A naive `lerp prev curr alpha` over the whole world blends solver caches, which is
  nonsense at best.
- **Angles interpolate on the shortest arc.** A turret rotating through `π` has `Previous ≈ +3.13`
  and `Current ≈ -3.13`; a naive lerp spins it the long way round at 60 fps. `shortestArc` is four
  lines and everyone forgets it.

```fsharp
val interpolate : alpha: float -> previous: World -> current: World -> Transform[]
```

---

## 8. Proposed surface

```fsharp
type BodyKind = Static | Kinematic | Dynamic
type Shape    = SCircle of radius: float | SBox of halfExtents: Point | SPoly of ConvexPolygon
type Material = { Restitution: float; Friction: float }

type Config =
    { Gravity: Point
      VelocityIterations: int          // fixed. 8 is the usual answer.
      PositionIterations: int          // fixed. 3.
      Slop: float                      // allowed penetration before correction (~0.01)
      Correction: float                // Baumgarte percent (~0.2)
      BounceThreshold: float           // |v·n| below this ⇒ e = 0
      SleepLinearSq: float; SleepAngular: float; SleepTicks: int
      BroadPhaseCellSize: float }

type World  // opaque: SoA arrays + SolverCache. Built and read through the module.

[<RequireQualifiedAccess>]
module Physics =
    val empty     : Config -> World
    val addBody   : BodyKind -> Shape -> Material -> Point -> World -> struct(int * World)

    /// THE integrate function. Fits `Loop.advance` verbatim. Pure, total, byte-deterministic.
    /// Reads `dt` and the world; never a clock, never `alpha`.
    val step      : World -> dt: float -> World

    /// Sorted, deduped candidate pairs. `SpatialGrid` broad phase. `(a, b)` with `a < b`.
    val pairs     : World -> struct(int * int)[]

    /// Narrow phase with contact points — the missing half of `Geometry.polygonContact`.
    val manifold  : World -> a: int -> b: int -> Manifold voption

    /// Presentation only. Shortest-arc on rotation. Never feeds `step`.
    val interpolate : alpha: float -> previous: World -> current: World -> Transform[]

    /// The desync tripwire (§6).
    val checksum  : World -> uint64
```

Note `step : World -> float -> World` — the `Config` is baked into the `World` at `empty`, so the
signature is *exactly* `integrate`. That is not cosmetic; it is what lets `Loop.advance dt
Physics.step frameTime state` typecheck with no adapter.

---

## 9. Fit with the existing designs

| Design | Relationship |
|---|---|
| [collision](2026-07-05-game-logic-collision-detection-design.md) | **Parent.** This is its §6.2 / §10 `resolveImpulse`, and its P3 phasing. Narrow phase, sweep, and the pitfall table are reused unchanged. |
| `Resolution` | Untouched. Arcade stays the default; `Physics` is the opt-in heavy layer, exactly as `Resolution.fsi` already says. |
| `Geometry` | Reused for `aabbContact`/`circleContact`/`circleAabbContact`/`polygonContact`/`sweptIntersects`. Extended with face clipping for contact points (§5 gap 2). |
| `SpatialGrid` | Becomes the broad phase, behind `Physics.pairs` which adds the sort + dedup. |
| `FixedStep` / `Loop` | §2. The engine's `integrate` contract; the loop must come down to `Game.Core` first. |
| [ballistics #40](https://github.com/FS-GG/FS.GG.Game/issues/40) | **Adjacent, not dependent.** Projectiles are not rigid bodies — they are swept segments with no mass. Do not model a bullet as a dynamic body; that is the classic mistake this split avoids. |
| [effects #43](https://github.com/FS-GG/FS.GG.Game/issues/43) | `Resolution.knockback` is a displacement impulse. If `Physics` lands, #43's decision about where `knockback` lives gains a third option: express it as an impulse against a dynamic body. |
| [mini tanks](2026-07-10-mini-tanks-product-design.md) | §4.1 (tanks use arcade), §10 item 3 (ramming is the one impulse consumer). |

---

## 10. Risks

| # | Risk | Mitigation |
|---|---|---|
| R1 | **Scope creep into a real physics engine.** Joints, then islands, then a BVH, then it is Box2D. | The §4 out-list is a fence, not a wish list. Any addition needs a TestSpec consumer. |
| R2 | **SoA fights F# idiom.** Records and lists are the house style (constitution §IV). | The `World` is **opaque** in the `.fsi` (like `SpatialGrid<'T>` already is). Callers never see the arrays. §IV's mutation clause is invoked in a comment, as it requires. |
| R3 | **Warm-start cache makes the world non-comparable.** Structural equality over impulse caches breaks golden tests that hash the model. | `checksum` hashes body state only, not the cache. Document that two worlds equal in presentation may differ in cache. |
| R4 | **Float determinism across ISAs.** | §6. Golden checksums on the CI matrix; `Fixed` deferred behind its own ADR, per the parent design. |
| R5 | **`Loop` migration is a Rendering surface change.** `FS.GG.UI.Canvas.Loop` is public and consumed by `CanvasDemo`, `SymbologyBoard`, and scaffolded products. | Deprecate in place, migrate the samples, retire on a later major — **not** a re-export (§2.1: Canvas references only `Scene`, and a re-export would force `Game.Core` onto every profile). Cross-repo request [Rendering#269](https://github.com/FS-GG/FS.GG.Rendering/issues/269), contract `fs-gg-ui`. Needs an ADR: the classification being overturned lives in a `.fsproj` comment, not in ADR-0022. |
| R7 | **Rendering may reasonably decline.** "Canvas keeps `Loop`" is a legitimate answer. | Then `Game.Core.Loop` still lands (the physics engine needs it) and the duplication persists deliberately, with both hardened. That is worse than one loop but better than today, where only one is hardened. |
| R6 | Nobody actually needs it yet. | Four TestSpecs do (§4.1). Tanks does not. Sequence it after #40/#41, not before. |

---

## 11. Delivery slices

| # | Slice | Tier | Note |
|---|---|---|---|
| 1 | **Fix `Loop.advance`'s NaN clamp** ([Rendering#266](https://github.com/FS-GG/FS.GG.Rendering/issues/266)) | bug | Rendering. Blocks nothing, but ships a silent freeze today. Do it regardless of slices 2 and 2b. |
| 2 | `Game.Core.Loop` — `StepState`/`advance`/`alpha` over `FixedStep.drain` ([#44](https://github.com/FS-GG/FS.GG.Game/issues/44)) | 1 — contracted | The `integrate` contract has to live where the physics does. |
| 2b | Deprecate + retire `Canvas.Loop`; ADR the boundary ([Rendering#269](https://github.com/FS-GG/FS.GG.Rendering/issues/269)) | contract-change | Cross-repo. Rendering may decline (R7); slice 2 proceeds either way. |
| 3 | `Manifold` + polygon face clipping in `Geometry` | 1 — contracted | §5 gaps 1–2. Also unblocks better arcade resolution. |
| 4 | `Physics.pairs` — sorted, deduped broad phase over `SpatialGrid` | 1 | §5 gap 4. |
| 5 | `Physics.step` — integrate, solve, correct. Fixed iterations, no warm start, no sleep. | 2 | The naive-but-correct core. Golden checksums here. |
| 6 | Warm starting + sleeping | 2 | The performance slice. Checksums must not change for a scene that never sleeps. |
| 7 | Speculative contacts (CCD) | 2 | Fixed `dt`, no substepping. |
| 8 | `Physics.interpolate` + shortest-arc | 2 | §7. Retire `CanvasDemo`'s and `Breakout1`'s hand-rolled lerps. |
| 9 | `fs-gg-physics` skill | 2 | Doctrine: **the double step buffer is the default and departing from it must be justified** (§2.3); arcade first, impulse opt-in; fixed iterations; sorted pairs; `alpha` never feeds the sim. |

Slice 9's skill body carries §2.3 verbatim, including the departures table — a rule with no worked
exceptions reads as a prohibition, gets quietly broken, and stops being a rule. `fs-gg-game-core`
gains the same doctrine next to its loop section, since that is where an agent meets the accumulator
first.

Slices 1–3 are prerequisites and are worth landing even if the engine is never built: slice 1 is a
live bug, slice 2 removes a duplicated accumulator, and slice 3 makes the *arcade* layer better too.
