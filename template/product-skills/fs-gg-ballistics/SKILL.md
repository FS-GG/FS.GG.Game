---
name: fs-gg-ballistics
description: Fire projectiles in a generated FS.GG.UI product — swept casts that cannot tunnel, lead/intercept solves, and splash damage with an explicit falloff curve.
---

# Ballistics Capability

## Scope

Use this skill when anything in your product **shoots**: bullets, shells, arrows, missiles, beams. It
covers advancing a round on the fixed step without tunneling through thin targets, solving where to aim
at a moving target, and applying splash damage over an area with a falloff curve you choose. These are
pure helpers — they read no wall-clock and perform no I/O. Bucketing the targets is `fs-gg-game-core`'s
`SpatialGrid`; resolving the *response* to a hit is `fs-gg-collision`'s. This skill materializes for the
`game` and `sample-pack` profiles.

## Public Contract

The signatures you consume are bundled with this product:

- `docs/api-surface/Game.Core/Ballistics.fsi` — the `Ballistics` module plus the `Projectile` and
  `ProjectileStep` types. Shipped in `FS.GG.Game.Core`, referenced on the `game` and `sample-pack`
  profiles.
- `docs/api-surface/Game.Core/Geometry.fsi` — `segmentAabbHit` / `segmentPolygonHit` / `segmentCircleHit`,
  the casts you hand to `Ballistics.step`.
- `docs/api-surface/Game.Core/SpatialGrid.fsi` — the broad phase `splash` queries.

All helpers are **total**: degenerate inputs return a documented value, they never throw. No function
here can emit a `NaN`.

## The one rule: a moving round is a segment, never a point

A 1200 unit/s round advanced by one 60 Hz step moves 20 units. A 6-unit-thick target in its path is
never touched — before the step the round is in front of the wall, after the step it is behind it. A
`containsPoint` test on the new position reports a clean miss. **This is the single most common bug in
hand-rolled projectile code**, and clamping the timestep does not fix it: halve `dt` and you halve the
target thickness that still tunnels.

`Ballistics.step` casts the whole swept segment `Position → Position + Velocity·dt` at your occluders.

```fsharp
open FS.GG.Game.Core

let wall : Rect = { X = 10.0; Y = -50.0; Width = 1.0; Height = 100.0 }

// `cast` is the ONLY coupling to your world's geometry. Return the NEAREST hit (smallest T).
let cast (p0: Point) (p1: Point) = Geometry.segmentAabbHit p0 p1 wall

let shell = { Position = { X = 0.0; Y = 0.0 }; Velocity = { X = 1200.0; Y = 0.0 }; TicksRemaining = 120 }

match Ballistics.step cast (1.0 / 60.0) shell with
| Flew round -> // still in the air; store `round` back in the Model
    ()
| Struck hit -> // hit.Point is the impact, hit.Normal the surface, hit.T the fraction of the step used
    ()
| Expired -> // lifetime ran out (or the state went non-finite); remove it
    ()
```

Cast against several occluders by folding and keeping the smallest `T`:

```fsharp
let castAll (walls: Rect list) (p0: Point) (p1: Point) =
    walls
    |> List.choose (fun w -> Geometry.segmentAabbHit p0 p1 w)
    |> function
       | [] -> None
       | hits -> Some(hits |> List.minBy (fun h -> h.T))     // NEAREST hit, not the first found
```

Returning a non-nearest hit is a caller bug: the round would stop at the wrong surface.

## Lifetime is a tick count, not seconds

`TicksRemaining` is an `int`, decremented once per `step`. A lifetime carried as `float` seconds
accumulates a different rounding error on every machine and drifts a replay apart; an `int` decrement
cannot. The same applies to fire cooldowns and homing re-target intervals — count ticks.

Lifetime is checked **before** motion, so an exhausted round never gets a free final step.

## Hitscan vs projectile — decide, and say why

- **Hitscan.** Resolve damage the same step you acquire the target: one `Geometry.segmentAabbHit` from
  muzzle to target. Deterministic and cheap, but there is no travel time — a fast target cannot outrun
  it. No `Projectile` needed.
- **Projectile.** Travel time, extra `Model` state, and one edge case you must decide *before* it bites:
  **if the target dies mid-flight, does the round re-acquire the nearest target, or fizzle?** Make that
  a pure function of the world, never of arrival order, or the replay diverges.

Pick hitscan for beams; pick projectile when travel time or leading the target is part of the feel.

## Leading a moving target

`Ballistics.intercept shooter speed target targetVelocity` solves the quadratic
`|target + targetVelocity·t − shooter| = speed·t` and returns the **interception point** to aim at.

```fsharp
open FS.GG.Game.Core

let fireAt (muzzle: Point) (speed: float) (enemy: Point) (enemyVel: Point) : Projectile voption =
    match Ballistics.intercept muzzle speed enemy enemyVel with
    | ValueNone -> ValueNone                       // cannot be hit — lead the current position, or hold fire
    | ValueSome aim ->
        let dx, dy = aim.X - muzzle.X, aim.Y - muzzle.Y
        let len = sqrt (dx * dx + dy * dy)
        if len = 0.0 then ValueNone
        else
            ValueSome
                { Position = muzzle
                  Velocity = { X = dx / len * speed; Y = dy / len * speed }
                  TicksRemaining = 180 }
```

`ValueNone` is a **documented answer, not an error**: the target is faster than the round and fleeing,
or `speed` is degenerate. Handle it. The alternative — a naive `sqrt` of a negative discriminant —
returns `NaN`, which propagates into the `Model` and freezes the entity forever.

When the target's speed exactly equals the round's, the quadratic collapses to a linear equation and is
solved as one rather than divided by a zero leading coefficient. When both roots are positive, the
earliest interception is taken.

## Velocity inheritance is a policy — name it

Asteroids inherits `1.0×` the ship's velocity into the bullet, a twin-stick roguelike `0.25×`, a tank
`0`. There is no correct value, only a *named* one. `Ballistics` inherits nothing: you construct
`Projectile.Velocity`, so you decide and write it down.

```fsharp
let inheritance = 0.25                                        // <- name it in the spec, not in a literal
let muzzleVel = { X = ship.Vel.X * inheritance + aim.X * shotSpeed
                  Y = ship.Vel.Y * inheritance + aim.Y * shotSpeed }
```

## Splash — the falloff curve is a balance lever

`Ballistics.splash centre radius falloff position grid` pairs every item within `radius` with its damage
multiplier. `falloff` receives the **normalised** distance `d ∈ [0,1]`.

```fsharp
open FS.GG.Game.Core

let grid = SpatialGrid.build 32.0 [ for e in enemies -> e.Pos, e ]

// "centre 100%, linear falloff to 50% at the edge" — the tower-defense spec's curve.
let hits = Ballistics.splash blast 48.0 (Ballistics.linearFalloff 0.5) (fun e -> e.Pos) grid

let damaged = [ for enemy, mult in hits -> { enemy with Hp = enemy.Hp - int (float baseDamage * mult) } ]
```

- `Ballistics.linearFalloff edgeScale` — `1.0` at the centre, `edgeScale` at the rim, linear between.
- `Ballistics.inverseSquareFalloff k` — `1 / (1 + k·d²)`; concentrates damage hard at the centre.
- Or supply your own `float -> float`. A curve returning a non-finite multiplier contributes `0.0`.

Linear-to-50%, linear-to-0, and inverse-square play *completely* differently. Pick one deliberately and
put it in the spec; do not hardcode a curve and then tune damage numbers around it.

`position` must project an item onto the same point the grid was built at — otherwise the falloff is
measured from a different place than the one that was queried.

## Determinism

- The set of entities `splash` returns is independent of `SpatialGrid` insertion order (membership is an
  exact squared-distance test, never bucket traversal order). The *list* is in insertion order.
- Damage multipliers are floats, but the **set** and the **order** are stable, so folding them into an
  integer HP pool is replay-identical.
- `step` reads no clock. A scripted `dt` sequence reproduces a byte-identical trajectory.
- Never feed a render-interpolation `alpha` back into `step`, and never step with a variable `dt`.

## Common pitfalls

- **Testing the post-integration position.** The tunneling bug. Use `Ballistics.step`; never
  `Geometry.containsPoint target round.Position`.
- **Clamping the timestep to "fix" tunneling.** Slows the whole simulation to hide one collision bug,
  and only raises the speed at which tunneling resumes.
- **Ignoring `ValueNone` from `intercept`.** It means "cannot be hit". A naive lead solve returns `NaN`
  here, which silently poisons every downstream position.
- **Lifetimes as `float` seconds.** Drifts a replay. `TicksRemaining` is an `int`.
- **Returning the first hit from a multi-occluder `cast`, not the nearest.** The round stops at the
  wrong surface, and which surface depends on your list order.
- **Hardcoding the splash falloff.** Expose the curve; it is the balance lever.
- **Re-deciding splash membership with `sqrt`.** `splash` matches `SpatialGrid.queryRadius`'s squared
  test exactly; comparing `sqrt dSq <= radius` yourself can disagree at the rim by one ulp.
- **A `Projectile` radius.** Rounds are swept *points*. If you need `shotRadius`, use
  `Geometry.sweptIntersects` for the box case rather than inflating the target by hand.

## Build Commands

Run `./fake.sh build -t Dev` then `./fake.sh build -t Verify` in this product.

## Test Commands

Run `./fake.sh build -t Test` to exercise product-owned ballistics examples.

## Evidence

Record ballistics evidence (no-tunneling property runs, golden trajectory replays, splash
order-independence) under this product's `readiness/` paths. Do not copy framework readiness reports
into the product.

## Package Boundary

`Ballistics`, `Projectile`, and `ProjectileStep` live in `FS.GG.Game.Core` (referenced only on the
`game`/`sample-pack` profiles), alongside the `Geometry` casts and `SpatialGrid` they build on.
`FS.GG.Game.Core` is the BCL-only bottom layer — it depends on nothing and pulls in no viewer, layout,
or widget machinery. Keep rendering the muzzle flash and the blast circle in `fs-gg-scene`.

## Generated Product

Hold live rounds in your `Model` as a `Projectile list`. On each fixed step, map `Ballistics.step` over
them with a `cast` closed over your world's occluders, partition the results into `Flew` (keep),
`Struck` (apply damage, maybe `splash`), and `Expired` (drop), then hand the surviving world to `View`.

## Persistent problems

When a problem outlasts reasonable in-repo attempts, extensive external research is **mandatory** —
consult **official online docs first** (the F#/.NET docs and the driven library's own reference), then
community sources. If your product uses Spec Kit, record findings and resolving links under the feature's
`specs/<feature>/feedback/`; otherwise record them in this skill's **Sources** line and any product-local
`docs/`. Offline, the mandate degrades to recording "research blocked — <why>" rather than hard-failing.

## Related

- [[fs-gg-game-core]] — the fixed step the round advances on, the `SpatialGrid` `splash` queries, and the
  seeded `Rng` for spread/scatter.
- [[fs-gg-collision]] — resolve the *response* once `step` reports a `Struck`.
- [[fs-gg-rendering:fs-gg-scene]] — draw the tracer, the muzzle flash, and the blast circle.

## Sources / links

- F#/.NET docs: https://learn.microsoft.com/en-us/dotnet/fsharp/
- Fixed-timestep loop background: https://gafferongames.com/post/fix_your_timestep/
- Projectile lead/intercept derivation: https://www.gamedeveloper.com/programming/shooting-a-moving-target
