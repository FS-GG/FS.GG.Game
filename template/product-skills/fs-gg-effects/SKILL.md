---
name: fs-gg-effects
description: Turn a hit into a number in a generated FS.GG.UI product — a named, ordered damage pipeline with an observable early exit, per-effect stacking policies, and discrete grid pushes that report why they stopped.
---

# Effects Capability

## Scope

Use this skill when anything in your product **takes damage** or **carries a status effect**: armor,
cover, resistances, immunity, vulnerability, poison stacks, slows, stuns, and the knockback that shoves
a unit into a wall or a lake. It covers the *mitigation* of an incoming hit and the *bookkeeping* of the
effects riding on it.

It does **not** decide *who* is hit. That is a **region operator** — `fs-gg-ballistics`'s
`Ballistics.splash`, `fs-gg-game-core`'s `SpatialGrid.queryRadius`, a Tesla chain traversal you write —
and it hands you a `('T * float) list`: each target with the scalar that region assigned it. `Effects`
consumes that list. These are pure helpers: no wall-clock, no I/O, no RNG. This skill materializes for
the `game` and `sample-pack` profiles.

## Public Contract

The signatures you consume are bundled with this product:

- `docs/api-surface/Game.Core/Effects.fsi` — `Source`, `Damage<'K>`, `StageResult`, `Stage<'T,'K>`,
  `DamageTrace`, the `Effects` module, and the status types `Active<'E>` / `Policy<'E>`.
- `docs/api-surface/Game.Core/Resolution.fsi` — `push`, `CellStep`, `PushStop`, `Push`; the discrete
  grid displacement.
- `docs/api-surface/Game.Core/Ballistics.fsi` — `splash`, the region operator that most often feeds
  `Effects.applyAll`.

All helpers are **total**: no input — not a `NaN` `Base`, not a hostile `Stage`, not an infinite
falloff multiplier — can put a `NaN` in a `DamageTrace`.

## The one rule: a floor at the end of a pipeline erases every zero the pipeline produced

Here is the expression everyone writes first, straight out of a tower-defense spec — "resistance is
applied as `damage * (1 - resist)`, then floored at 1":

```fsharp
let applied = max 1.0 (damage * (1.0 - resist))     // <- WRONG
```

Fire a 5-damage Frost bolt at a Spectre whose `Frost` resist is `1.0`. The multiplier is `0.0`, the
product is `0.0`, and **the floor lifts it back to `1.0`**. Your fully-immune enemy takes damage. Worse,
nothing observed the immunity, so the bolt's rider `Slow` gets applied too.

The floor is not the mistake and the multiplier is not the mistake. **The mistake is that they are in
the same expression.** Patch it with `unless resist = 1.0` and you will need one such clause for every
zero-producing stage, and they compound.

Immunity is not a multiplier of zero. It is an **early exit** — and one the caller can *see*:

```fsharp
open FS.GG.Game.Core

let pipeline =
    [ Effects.amplify (fun u -> u.Vulnerable)                     // × (1 + bonus)
      Effects.resist (fun u k -> u.ResistTo k)                    // × (1 − r), but HALTS at r >= 1
      Effects.subtract (fun u k -> if isElemental k then 0.0 else u.Armor)
      Effects.floorAt 1.0 ]                                       // never runs after a Halt

let trace = Effects.pipeline pipeline spectre { Kind = Frost; Source = Declared; Base = 5.0 }

trace.Final              // 0.0 — not 1.0
trace.Halted             // ValueSome "resist"  <- suppress the rider Slow
trace.Steps              // [ "amplify", 5.0; "resist", 0.0 ] — armor and floorAt never ran
```

`Effects.resist` folds immunity in *on purpose*: it `Halt`s at `r >= 1.0` rather than multiplying. There
is no way to write the bug with this surface. (`>= 1.0`, not `= 1.0` — a resistance above one would
multiply by a negative and *heal* the target.)

**Always read `trace.Halted` before applying a rider status effect.**

```fsharp
let hit target damage rider =
    let trace = Effects.pipeline pipeline target damage
    let target = { target with Hp = target.Hp - int trace.Final }
    match trace.Halted with
    | ValueSome _ -> target                                        // shrugged off — no rider
    | ValueNone -> { target with Slows = Effects.applyEffect (Effects.Strongest slowMagnitude) rider target.Slows }
```

## Order of operations: two pipelines, not one

Specs look like they contradict each other on ordering. They don't — they describe different phases:

| Phase | Owner | Order |
|---|---|---|
| **Build** the outgoing amount | the shooter's stat block | additive mods → multiplicative mods → clamp |
| **Transport** it to the target | the region operator | × the region's per-target scalar |
| **Mitigate** it at the target | `Effects.pipeline` | amplifiers → multiplicative → subtractive → floor |

`Effects` owns only the third row. Within it, **scale before you subtract, and floor last.**

Armor's documented meaning is "these N points never get through". That sentence is only true if the
subtraction is the last thing to touch the number. A Cannon (30 damage, `linearFalloff 0.5`) against a
Brute (armor 6), at the blast rim:

| Order | Rim | Centre |
|---|---|---|
| scale → subtract (**do this**) | `max(1, 0.5·30 − 6)` = **9** | 24 |
| subtract → scale | `0.5 · max(1, 30 − 6)` = **12** | 24 |

They agree at the centre and disagree by a third at the rim. Under `subtract → scale`, falloff
*protects the attacker from armor* — the further out you are, the smaller a fraction of your damage
armor removes. That is a different game, and nobody chose it.

The same argument places **amplifiers first**. An "incoming damage ×(1+bonus)" upgrade sold as
anti-armor only overcomes armor if it amplifies the amount armor then subtracts from: against armor 6
with 30 damage and `+40%`, `max(1, 42 − 6) = 36` versus `max(1, 24) · 1.4 = 33.6`. Put it after the
subtraction and your anti-armor upgrade isn't one.

None of this is baked in. `pipeline` takes a `Stage` **list** and the order is yours. The discipline is
that the order is written down, ordered, and individually testable — not that the library picks your
game for you.

## The floor is a game-defining parameter, never a constant

A tower-defense floors at `1` so a maxed-armor creep still chips. A tactics game floors at `0` and
*depends* on it: a Crawler on a Forest tile (cover +2) must survive a Ram (damage 1), because
`max(0, 1 − 2) = 0`. Run the tactics numbers through the tower-defense floor and the Crawler takes 1 and
dies. One constant, and a documented acceptance criterion inverts.

So `floorAt` is a stage in *your* list. Never hardcode it.

⚠️ **The floor's sharp edge, which no spec mentions.** `splash` includes the rim (`distance <= radius`),
so a target sitting exactly on the rim of a `linearFalloff 0.0` blast is a target — with a transport
multiplier of `0.0`. `applyAll` **seeds** at `Base × 0.0 = 0.0`, and a seed is *not* a `Halt`, so
`floorAt 1.0` lifts it to **1 damage from a blast that reached it with none**. Decide deliberately:

```fsharp
// (a) the region operator drops them — a rim-grazing target is not a target
let targets = region |> List.filter (fun (_, m) -> m > 0.0)

// (b) or floor at 0.0 and let the arithmetic say what it means
```

## `Source` and `Kind` are different axes — gate on the one your rule is about

The corpus writes "damage type" for both. They are orthogonal, and only `Source` is built in:

- **`Kind`** — Physical / Frost / Electric. Generic in `'K`; `Game.Core` never learns what Frost is.
- **`Source`** — `Declared | Collision | Environmental | Periodic`. *Why* the damage is being dealt.

**Cover and `Armored` are `Source` rules.** They reduce *declared attacks* and nothing else: a unit
shoved into a wall is not being attacked, and neither is one standing in lava. `gatedBy` says so, and
the collision `1` and the lava `3` then run the same pipeline and are reduced by neither.

```fsharp
let cover = Effects.gatedBy [ Declared ] (Effects.subtract (fun u _ -> coverOf u.Tile))
let armored = Effects.gatedBy [ Declared ] (Effects.subtract (fun u _ -> u.ArmoredReduction))
```

⚠️ **Armor-bypass is a `Kind` rule, and this is the trap.** A spec that says "*elemental* sources
(frost/poison/tesla) ignore armor" looks like a `Source` gate, because Poison's DoT ticks really are
`Periodic`. It is wrong. A Frost bolt and a Tesla arc are `Declared` attacks, so a source-gated armor
stage still subtracts armor from them and **only Poison ever gets its bypass**. Put the bypass inside
`subtract`'s closure, which is exactly why `amountOf` and `resistOf` take `'K`:

```fsharp
// RIGHT — keyed on the Kind. A Frost bolt bypasses armor whatever its Source.
let armor = Effects.subtract (fun u k -> if isElemental k then 0.0 else u.Armor)

// WRONG — a Declared Frost bolt is still armored.
let armor = Effects.gatedBy [ Declared; Collision ] (Effects.subtract (fun u _ -> u.Armor))
```

So a whole tower-defense is one ungated list — a Cannon shell and a Frost bolt walk the same four
stages and take different branches inside two of them:

```fsharp
let td = [ Effects.amplify vulnerableBonus; Effects.resist resistOf; Effects.subtract armorOf; Effects.floorAt 1.0 ]
```

If your scoring pays a bonus for kills that were *not* direct damage, the `Source` trichotomy **is** your
incentive structure. A pipeline without it cannot express the game.

## Stacking is a per-effect policy, and your spec almost certainly omits it

Four policies cover the corpus. Naming them is the whole contribution; there is no clever code here.

| Policy | Meaning | Typical |
|---|---|---|
| `Refresh` | one instance; reapplying sets ticks to `max(old, new)` | Stun |
| `Strongest magnitude` | one instance; a **stronger-or-equal** application replaces it and refreshes; a weaker one changes nothing | Slow |
| `Stacks cap` | independent, additive instances, appended while `count < cap`; at the cap, a **no-op** | Poison |
| `Once` | the first application wins; every later one is a no-op | a one-shot brand |

```fsharp
// Slow: "the strongest active slow applies; refreshes if a stronger OR EQUAL slow is reapplied."
// magnitude is oriented so larger = stronger, so a slow whose factor is lower when stronger inverts:
let slowPolicy = Effects.Strongest (fun e -> 1.0 - e.Factor)
let slows = Effects.applyEffect slowPolicy { Effect = frost; TicksRemaining = 90 } unit.Slows
```

The `>=` in `Strongest` is load-bearing: it is what lets a **second tower of the same type** re-up a
slow it cannot out-strengthen. Change it to `>` and your second Frost tower does nothing.

Two decisions your spec probably leaves blank, which you must make explicitly:

- **What policy does `Vulnerable` have?** (Reasonable default: `Strongest (fun e -> e.Bonus)`.)
- **What happens to an existing stack's duration when one is applied against the cap?** (`Stacks` is a
  no-op at the cap — it does not refresh the newest, nor evict the oldest.)

Write your answer in the spec. An unstated policy is a coin flip that only shows itself when two
sources of the same effect overlap.

## Durations are tick counts, and a tick is not a second

`Active<'E>.TicksRemaining` is an `int`, decremented once per fixed step by `Effects.tickEffects`.
Accumulate a `float` duration per frame instead and two machines disagree by an epsilon that compounds
until a replay diverges; an `int` decrement cannot drift. Convert **once**, where the content is
authored: at 60 Hz, a `1.5 s` slow is `90`.

`Effects` never sees `dt`. A spec that writes DoT as "deals `dps*dt` Poison damage each step" invites a
float multiply inside the tick — instead store the **per-tick** amount and apply a constant. There is no
`dt` in this module's surface, so there is nothing to accumulate incorrectly.

And **a tick is whatever you drain.** In a turn-based game a tick is a *round*. `tickEffects` is
unit-free, so *you* name the moment it happens — start of phase, or end of round. A module that owned
the clock would hide that ambiguity; this one forces you to resolve it.

```fsharp
let stepUnit u = { u with Effects = Effects.tickEffects u.Effects }   // once per fixed step. Not per frame.
```

## Pushing a unit: three relations, not two

`Resolution.push` displaces a unit across grid cells and **reports why it stopped**. Its `classify` is
the only coupling to your world, exactly as `cast` is for `Ballistics.step`.

A binary `blocked` predicate cannot express your terrain, and this is not a nicety:

| Terrain | Required behaviour | `CellStep` |
|---|---|---|
| wall / mountain / off-board / occupied | stop **before** it | `Block` |
| lava | **enter** it, take damage, keep going | `Enter` |
| water / chasm | **enter** it and **die** | `Stop` |

There is no third answer with a `bool`. Mark water blocked and the unit stops on dry land and lives;
mark it passable and it walks out the far side.

```fsharp
open FS.GG.Game.Core

let classify (c: Cell) =
    if not (inBounds c) || isWall c || occupied c then Resolution.Block
    elif isWater c || isChasm c then Resolution.Stop
    else Resolution.Enter

let r = Resolution.push unit.Cell direction unit.PushDistance classify

// Environmental damage folds over the cells ENTERED; the fate is a match on Outcome.
let burned = unit.Hp - lavaTick * (r.Entered |> List.filter isLava |> List.length)

match r.Outcome with
| Resolution.Stopped _ -> die unit                              // drowned, regardless of HP
| Resolution.Blocked obstacle -> collide { unit with Cell = r.Final; Hp = burned - 1 } obstacle
| Resolution.Completed -> { unit with Cell = r.Final; Hp = burned }
```

`classify` absorbs everything without `Game.Core` learning any of it: a **Flying** unit's `classify`
returns `Enter` where a ground unit's returns `Stop`; a **Massive** unit is pushed `distance = 0`.

`Resolution.knockback` is **deprecated** — it is exactly `(push …).Final` and throws away both the stop
reason and the cells crossed. Migrate:

```fsharp
// before
let landed = Resolution.knockback start step distance blocked
// after
let landed = (Resolution.push start step distance (fun c -> if blocked c then Resolution.Block else Resolution.Enter)).Final
```

Pushing several units is **order-dependent by design**: sequence them (a spec usually says "in id
order") and close `classify` over the occupancy each push updates. Permutation invariance is a property
of the AoE *damage set*, not of push resolution.

## Composing a region with a pipeline

The two halves are independent, and that is the point — half the corpus uses one without the other.

```fsharp
open FS.GG.Game.Core

let grid = SpatialGrid.build 32.0 [ for e in enemies -> e.Pos, e ]

// Region operator: WHO, and with what transport multiplier.
let region = Ballistics.splash blast 42.0 (Ballistics.linearFalloff 0.5) (fun e -> e.Pos) grid

// Pipeline: FOR HOW MUCH. applyAll SEEDS each target at Base * multiplier.
let dealt = Effects.applyAll pipeline { Kind = Physical; Source = Declared; Base = 30.0 } region

for enemy, trace in dealt do
    applyDamage enemy trace.Final
    if trace.Halted = ValueNone then applyRider enemy
```

Any `world -> ('T * float) list` is a region operator. A Tesla chain's scalar is `chainFalloff^k` in the
**jump index**, not the distance — which a `falloff: float -> float` curve could never express, and
which `applyAll` consumes without noticing the difference. A blast that kills whatever it contains uses
a uniform `fun _ -> 1.0` and an **empty** pipeline.

## Determinism

- `pipeline` is a left fold over a `list`. No dictionary iteration, no sort, no `dt`, no clock.
- **Permutation invariance is structural.** `Stage.Run` is handed one target, the `Damage`, and the
  running amount — there is no world parameter, so a stage *cannot* observe another target. `applyAll`
  is a `List.map` of a pure function, so the result multiset cannot depend on `SpatialGrid` insertion
  order. (The result *list* follows the input order.)
- `tickEffects` decrements `int`s. An effect expires on the same step in a 30 fps and a 144 fps replay.
- Non-finite anything degrades to `0.0` rather than poisoning a total. A `NaN` in an HP pool freezes an
  entity forever and is unattributable; that cannot happen here.
- `push` is a fixed-order walk of at most `distance` steps, total for any total `classify`.

## Common pitfalls

- **`max floor (damage * (1.0 - resist))`.** The bug this module exists to prevent. Immunity deals the
  floor. Use `Effects.resist`, which halts.
- **Ignoring `trace.Halted`.** The damage is right and the rider `Slow` still lands on the immune target.
- **Subtracting armor before scaling.** Falloff starts protecting the target from its own armor.
- **Applying `Vulnerable` after armor.** Your anti-armor upgrade stops being one.
- **Spelling "elemental ignores armor" as `gatedBy [Periodic]`.** It looks right because poison ticks
  are `Periodic`, but a Frost bolt is a `Declared` attack and stays armored. Key the bypass on `Kind`,
  inside `subtract`'s closure. Gate on the axis the rule is actually about.
- **Hardcoding `floorAt 1.0` in a shared helper.** One constant, and a tactics acceptance criterion
  inverts. It is a game parameter.
- **Letting a rim-grazing (`multiplier = 0.0`) target reach a positive floor.** Filter the region, or
  floor at `0.0`.
- **`float` seconds in `TicksRemaining`.** Drifts a replay. Convert to ticks once, at authoring time.
- **`dps * dt` inside a tick.** Store the per-tick amount. `Effects` has no `dt`.
- **Assuming `Stacks` refreshes at the cap.** It is a no-op. Decide, and write it in the spec.
- **Using `>` in `Strongest`.** A second tower of the same type stops re-upping its slow.
- **`Resolution.knockback` for water.** It cannot express *enter and stop*. Use `push` with `Stop`.
- **Reducing collision or environmental damage by cover.** Gate the stage with `gatedBy [ Declared ]`.

## Build Commands

Run `./fake.sh build -t Dev` then `./fake.sh build -t Verify` in this product.

## Test Commands

Run `./fake.sh build -t Test` to exercise product-owned effects examples.

## Evidence

Record effects evidence (per-stage unit tests, the immunity-vs-floor regression, `applyAll` permutation
runs, tick-count replay goldens across varying frame times, `push` stop-reason cases) under this
product's `readiness/` paths. Do not copy framework readiness reports into the product.

## Package Boundary

`Effects`, `Source`, `Damage<'K>`, `Stage<'T,'K>`, and `DamageTrace` live in `FS.GG.Game.Core`
(referenced only on the `game`/`sample-pack` profiles), as do `Resolution.push` and its `CellStep` /
`PushStop` / `Push` types. `FS.GG.Game.Core` is the BCL-only bottom layer — it depends on nothing and
pulls in no viewer, layout, or widget machinery. `Effects` is generic in both the target and the damage
kind, so it reaches up to nothing at all, not even `Primitives`. Keep HP, death, healing, and the damage
numbers floating over the enemy's head in your `Model` and `fs-gg-scene`.

## Generated Product

Hold each unit's live effects in your `Model` as an `Active<'E> list`, keyed by effect kind. On each
fixed step, `tickEffects` them. On each hit: build the amount from the shooter's stat block, ask a region
operator who is caught, hand the result to `Effects.applyAll`, subtract `trace.Final` from HP, and apply
rider effects only where `trace.Halted` is `ValueNone`. On each push: `Resolution.push`, fold the hazard
tick over `Entered`, and match on `Stop`.

## Persistent problems

When a problem outlasts reasonable in-repo attempts, extensive external research is **mandatory** —
consult **official online docs first** (the F#/.NET docs and the driven library's own reference), then
community sources. If your product uses Spec Kit, record findings and resolving links under the feature's
`specs/<feature>/feedback/`; otherwise record them in this skill's **Sources** line and any product-local
`docs/`. Offline, the mandate degrades to recording "research blocked — <why>" rather than hard-failing.

## Related

- [[fs-gg-ballistics]] — `splash`, the region operator that most often feeds `Effects.applyAll`, and the
  falloff curves that become its transport multipliers.
- [[fs-gg-game-core]] — the fixed step `tickEffects` is drained on, the `SpatialGrid` a region queries,
  and the `Cell` vocabulary `push` steps across.
- [[fs-gg-collision]] — detection and the kinematic response (`pushOut` / `slide`) that sit beside `push`.
- [[fs-gg-rendering:fs-gg-scene]] — draw the damage number, the poison tint, and the knockback arc.

## Sources / links

- F#/.NET docs: https://learn.microsoft.com/en-us/dotnet/fsharp/
- Fixed-timestep loop background: https://gafferongames.com/post/fix_your_timestep/
- Design rationale (pipeline order, stacking policies, the `push` re-expression):
  `docs/reports/2026-07-10-effects-damage-pipeline-design.md`
