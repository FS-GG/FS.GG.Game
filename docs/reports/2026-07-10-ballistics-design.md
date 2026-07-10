# Ballistics — projectiles, lead, tunneling, and splash

- **Date:** 2026-07-10
- **Repo:** FS.GG.Game
- **Issue:** [#40](https://github.com/FS-GG/FS.GG.Game/issues/40) (child of #39)
- **Contract:** `game-sim-core` (Tier 1, additive)
- **Status:** implemented — `FS.GG.Game.Core.Ballistics`

## 1. TL;DR

Five TestSpecs specify projectiles, and each one invents the model from scratch. The primitives to do
it properly already ship (`Geometry.sweptIntersects`, `segmentAabbHit`, `SpatialGrid.queryRadius`), so
nothing new was strictly required to *start* — which is exactly why this is the cheapest first cut of
the skill layer, and the best proof the layer is real.

What was missing is not an algorithm. It is a **place to put four decisions** that every one of those
specs makes silently and differently:

1. hitscan or projectile,
2. what a fast round collides *with* (a point, or a segment),
3. what happens when the lead solve has no answer,
4. what shape the splash falloff is.

`Ballistics` is that place. It is ~120 lines. The value is that the four decisions are now *named*,
*documented*, and *tested*, instead of being re-derived per game and got wrong the same way each time.

## 2. The bug this module exists to prevent

**Fast projectiles tunnel unless you sweep.**

A 1200 unit/s round advanced by one 60 Hz step moves 20 units. A 6-unit-thick target sitting in its
path is never touched: the round's position before the step is in front of the wall, and after the
step it is behind it. A `containsPoint`-style test on the post-integration position — the first thing
anyone writes — reports a clean miss.

This is not hypothetical, and it is not confined to hand-rolled game code:

- **The `collision` fragment's `collide`/`step` pass does not call `sweptIntersects`.** Every
  scaffolded product that fires anything fast is already wrong.
- `missile-command.md` §13 works around it by clamping the timestep to 0.05 s. That is a *different*
  fix, and a worse one: it slows the whole simulation to hide one collision bug, and it only raises
  the speed at which tunneling resumes.

There is no timestep small enough to make a point test correct in general — halve `dt` and you halve
the distance covered, but the required target thickness halves with it. The only correct statement is
that **a moving round is a segment, not a point.** `Ballistics.step` casts the whole swept segment
`Position → Position + Velocity·dt` at the caller's occluders, and the property test asserts it: for
any speed and any timestep, a round whose step segment crosses a wall hits it.

The test is written to be **red against the naive point test** — it asserts, in the same case, that
`Geometry.containsPoint wall (position after the step)` is `false` while `step` returns `Struck`.

## 3. Doctrine — the parts that are invisible until they bite

### 3.1 Hitscan vs projectile is a determinism decision, not an optimization

Say which, and why, in the spec. Hitscan resolves damage the same step it acquires the target: cheap,
deterministic, no travel time, and a fast target cannot outrun it. A projectile has travel time, needs
Model state, and forces a mid-flight edge case — *if the target dies before impact, does the round
re-acquire or fizzle?* Whichever you choose, it must be a pure function of the world and never of
arrival order, or the replay diverges.

`Ballistics` models the projectile case. Hitscan needs no module: it is a `segmentAabbHit` call.

### 3.2 Lead/intercept is a quadratic, and its degenerate cases are the whole job

Solving `|target + targetVelocity·t − shooter| = speed·t` is three lines. The reason `intercept` is a
function is the cases where those three lines produce a `NaN` and quietly poison a `Model`:

| Case | Naive result | `intercept` |
|---|---|---|
| Target faster than the round, and fleeing | `sqrt` of a negative → `NaN` | `ValueNone` |
| Target speed *exactly* equals round speed | divide by a zero leading coefficient | solved as a **linear** equation |
| Zero relative velocity, target elsewhere | `0/0` → `NaN` | `ValueNone` |
| `speed` zero, negative, or non-finite | `NaN` / negative flight time | `ValueNone` |
| Both roots positive | picks whichever root the formula emits first | the **smaller** (earliest interception) |

`ValueNone` is a documented fallback, not an error. "You cannot hit this" is a real answer, and the
caller must handle it — usually by leading the target's current position and accepting the miss.

### 3.3 Velocity inheritance is a policy, not a law

Asteroids inherits `1.0×` the ship's velocity into the bullet, roguelike-dungeon-crawler `0.25×`,
tanks `0`. There is no correct value; there is only a *named* value. `Ballistics` therefore does not
inherit anything: the caller constructs `Projectile.Velocity` and decides. Naming it is the point.

### 3.4 Splash falloff shape is a balance lever — expose the curve

Linear-to-50%, linear-to-0, and inverse-square play completely differently. Hardcoding one and then
tuning damage numbers around it is how a game ends up unable to explain its own feel.

`splash` takes `falloff: float -> float` over the **normalised** distance `d ∈ [0,1]`, and ships two
named curves — `linearFalloff edgeScale` (the tower-defense spec's "centre 100%, 50% at the edge" is
`linearFalloff 0.5`) and `inverseSquareFalloff k`. A caller may supply its own. A curve that returns a
non-finite multiplier contributes `0.0`, so a hostile curve cannot inject a `NaN` into a damage total.

### 3.5 Durations are tick counts

Lifetime caps, fire cooldowns, and homing re-target intervals carried as `float` seconds accumulate a
different rounding error on every machine and drift a replay apart. `Projectile.TicksRemaining` is an
`int`, decremented once per step. An `int` decrement cannot drift.

Lifetime is checked **before** motion, so an exhausted round never gets a free final step.

## 4. Surface

```fsharp
type Projectile = { Position: Point; Velocity: Point; TicksRemaining: int }

type ProjectileStep =
    | Flew of Projectile     // crossed its whole step, still alive
    | Struck of RayHit       // met an occluder; T is the fraction of the step consumed
    | Expired                // lifetime ran out, or the state was not finite

[<RequireQualifiedAccess>]
module Ballistics =
    val step: cast: (Point -> Point -> RayHit option) -> dt: float -> projectile: Projectile -> ProjectileStep
    val intercept: shooter: Point -> speed: float -> target: Point -> targetVelocity: Point -> Point voption
    val linearFalloff: edgeScale: float -> (float -> float)
    val inverseSquareFalloff: k: float -> (float -> float)
    val splash: centre: Point -> radius: float -> falloff: (float -> float) -> position: ('T -> Point) -> grid: SpatialGrid<'T> -> ('T * float) list
```

Two design notes.

**`cast` is a function, not a world.** `Ballistics` does not know what an occluder is. The caller
supplies `fun p0 p1 -> Geometry.segmentAabbHit p0 p1 wall`, or `segmentPolygonHit`, or a fold over a
`SpatialGrid` broad phase returning the nearest hit. This is what keeps `Ballistics` independent of
`Geometry` and lets it sit above `SpatialGrid` in the compile order with no other coupling. It also
means the module is only as correct as the caller's "nearest hit" — returning a non-nearest hit is a
caller bug, not an unsoundness here.

**`splash` takes a `position` projection** because `SpatialGrid` returns items, not their positions.
It must agree with the position the grid was built at.

## 5. Determinism

Pure, total, no wall-clock read, no shared mutable state, no floating-point tie-break.

- `splash` decides membership with the **squared** distance, exactly as `SpatialGrid.queryRadius` does
  internally. Re-deciding it with `sqrt dSq <= radius` could disagree with the query by one ulp at the
  rim and drop an item the grid returned.
- The result of `splash` is in `SpatialGrid` insertion order, and the *set* it returns is independent
  of that order — property-tested by reversing the insertion order and comparing sorted results.
- `intercept`'s branch between the quadratic and the linear solve is taken on a scaled tolerance
  computed from the inputs by pure arithmetic, so the branch itself is deterministic.
- The golden replay test uses `dt` and velocities that are negative powers of two, so the trajectory
  is exact binary arithmetic and the expected positions are literals rather than approximations.

Totality is inherited from the rest of `Game.Core` and tested at each edge: a non-finite `dt` moves
the round nowhere (the tick is still consumed) rather than poisoning `Position`; a round whose state
is already non-finite is `Expired`, so a `NaN` never escapes into the world; and a `cast` that reports
a hit with a non-finite `T`, a `T` outside `[0,1]`, or a non-finite point is treated as a **miss** —
the module's totality does not depend on the caller being well-behaved.

## 6. What this deliberately does not do

Scope fence, in the spirit of the mini-physics design's §4:

- **No homing.** Steering (`homing * 360°/s`, re-target at 0.1 s) is a controller on top of
  `Projectile.Velocity`, not a property of the cast. It belongs to whatever owns the target set.
- **No projectile radius.** Rounds are swept *points*, not swept circles. A shot radius needs a
  segment-vs-expanded-shape cast; `sweptIntersects` already covers the box case for callers who need
  it, and widening `step` to carry a radius is additive when a game actually asks for it.
- **No lifetime in seconds, no cooldowns, no muzzle offsets, no ammo.** Those are game state.
- **No blast animation.** `missile-command.md`'s expanding blast circle (0→60 px at 160 px/s, hold
  0.25 s) is presentation, driven by the render layer off a `Struck` event.

## 7. Fit

`Projectile` advance is a plain fold over the fixed step, so it composes with `FixedStep.drain`
directly, and with the proposed `Loop.advance` (#44) once that lands — `step cast dt` *is* an
`integrate` for the projectile sub-world.

`splash` sits on `SpatialGrid.queryRadius`, which the `fs-gg-game-core` skill already tells products
to use for exactly this. The `fs-gg-ballistics` skill supersedes that one-line mention with the four
decisions above.

## 8. Follow-ups

- The `collision` fragment's `collide`/`step` pass still does not sweep (§2). That is a
  **FS.GG.Rendering** fix and needs a cross-repo issue.
- Registering the `fs-gg-ballistics` skill row in the org registry (`registry/skills.yml`, owner
  `fs-gg-game`) is a change to **FS-GG/.github** — "registry = manifest = bytes". Filed separately.
- A swept-circle cast (projectile radius) when a product needs `shotRadius`.
