# Effects — the damage pipeline, status policies, and what `knockback` actually is

- **Date:** 2026-07-10
- **Repo:** FS.GG.Game
- **Issue:** [#43](https://github.com/FS-GG/FS.GG.Game/issues/43) (child of #39)
- **Contract:** `game-sim-core` (Tier 1, **additive** — see §5)
- **Status:** designed — implementation is [#70](https://github.com/FS-GG/FS.GG.Game/issues/70), blocked by #42

## 1. TL;DR

`#43` was filed before `Ballistics` (#40) landed, and `Ballistics` already shipped half of it:
`splash`, `linearFalloff`, and `inverseSquareFalloff` answer *who is in the region* and *how much of
the hit reaches them*. So the gap is smaller and sharper than the issue supposed.

What is still missing is *what happens next*. Five TestSpecs describe a damage calculation and no two
agree on the order of its stages, the minimum it can produce, or whether a second application of an
effect stacks, refreshes, or does nothing. `Effects` is the place to put those decisions.

The design's load-bearing conclusion is a subtraction, not an addition: **`Effects` never queries
space.** A region operator already returns `('T * float) list` — targets, each with a transport
multiplier — and that is the module's entire input. `Ballistics.splash` is one such operator, the
Tesla chain is another, and neither belongs here. What is left is a pipeline, a status list, and one
`Resolution` function that has been wrong since it was written (§5).

## 2. The bug this module exists to prevent

**A damage floor makes a zero multiplier unrepresentable, so "immune" deals 1.**

`tower-defense.md` §4.5, verbatim:

> *elemental* sources (frost/poison/tesla) ignore armor but are modified by per-enemy **resistance**
> multipliers (`resist ∈ [0,1]`, applied as `applied = damage * (1 - resist)`), then floored at 1
> unless `resist == 1.0` (full immunity → 0).

Write that as one expression, which is the first thing anyone writes:

```fsharp
let applied = max 1.0 (damage * (1.0 - resist))
```

Now fire a 5-damage Frost bolt at a Spectre, whose `Frost` resist is `1.0`. The multiplier is `0.0`,
the product is `0.0`, and the floor lifts it back to **`1.0`**. The spec's own acceptance criterion
(§14 AC#9) demands `0`, *and* demands that no `Slow` rider is applied. The naive pipeline fails both.

The floor is not the mistake and the multiplier is not the mistake. The mistake is that they are in
the same expression: **a `max` at the end of a pipeline erases every zero the pipeline produced.**
The `unless resist == 1.0` clause in the spec is the author noticing this and patching it with a
special case. Once you have one such clause you need one per zero-producing stage, and they compound.

The fix is structural. Immunity is not a multiplier of zero — it is an **early exit**, and it must be
one that the caller can observe, because the rider status effect has to be suppressed too. So
`pipeline` returns a `DamageTrace` that records *whether* a stage halted it (and, for the trace, which
one), and `resist` halts at exactly `1.0` rather than multiplying by zero and hoping the floor is kind.

The same discontinuity is waiting one layer up and no spec mentions it: an enemy sitting exactly on
the rim of a `linearFalloff 0.0` blast gets a transport multiplier of `0.0`, and under a floor of `1`
it takes **1 damage from a blast that reached it with none**. `splash` includes it (`distance <=
radius`), so it is a target, and every mitigation stage after it is a no-op against a floor. Naming
the transport multiplier as a *seed* for the pipeline rather than a stage inside it (§4) is what makes
this visible instead of a balance mystery.

## 3. Doctrine — the parts that are invisible until they bite

### 3.1 `Effects` never queries space

Doctrine #6 of the issue asks for "three functions, composed — not one `applySplash`". Follow it
honestly and the region half disappears from this module entirely.

A **region operator** is anything of shape `world -> ('T * float) list`: a set of targets, each with a
scalar the region assigns it. `Ballistics.splash centre radius falloff position grid` is exactly that
shape and is already tested for it. So is a Tesla chain traversal, whose scalar is `chainFalloff^k` in
the jump index — which is *not* a function of distance and therefore cannot be a `falloff` curve at
all. That is worth noticing: `splash`'s `falloff: float -> float` takes the normalised distance and
could never have expressed the chain. Generalising "falloff" to "the scalar the region operator
attaches to each target" is what unifies the two.

`Effects` consumes that list. It does not build it. The immediate payoff is that the issue's
permutation-invariance property becomes **structurally true** rather than something to test for: a
`Stage` is handed one target and a running amount and nothing else, so no stage *can* observe another
target, and `applyAll` is a `List.map` of a pure function.

### 3.2 The order of operations is two pipelines, not one

The specs look like they contradict each other on ordering. They do not — they are describing
different phases, and conflating them is how the contradiction appears.

`roguelike-dungeon-crawler.md` §4.5 fixes an order for building a stat: "start from base, apply **all**
additive mods (in pickup order), then **all** multiplicative mods (in pickup order), then clamp."
Additive first. `tower-defense.md` §4.5 fixes an order for mitigating a hit: subtract armor, floor.
Subtractive last. Both are right, because:

| Phase | Owner | Order |
|---|---|---|
| **Build** the outgoing amount | the shooter's stat block | additive mods → multiplicative mods → clamp |
| **Transport** it to a target | the region operator | × the region's per-target scalar |
| **Mitigate** it at the target | `Effects.pipeline` | amplifiers → multiplicative reductions → subtractive reductions → floor |

`Effects` owns only the third row. It receives an already-built, already-transported number.

Within mitigation the rule is **scale before you subtract, and floor last**, for a reason that is
about meaning rather than taste: armor's documented meaning is "these N points never get through". That
sentence is only true if the subtraction is the last thing that touches the number. Put a multiplier
after it and armor's effective value silently varies with distance from the blast centre.

Take a Cannon (30 damage, `splashRadius` 42, `linearFalloff 0.5`) against a Brute (armor 6), at the rim:

| Order | Rim | Centre |
|---|---|---|
| scale → subtract (**chosen**) | `max(1, 0.5·30 − 6)` = **9** | `max(1, 30 − 6)` = 24 |
| subtract → scale | `0.5 · max(1, 30 − 6)` = **12** | `max(1, 30 − 6)` = 24 |

The two agree at the centre and disagree by a third at the rim. Under `subtract → scale`, falloff
*protects the attacker from armor* — the further out you are, the smaller a fraction of your damage
armor removes. That is a different game, and nobody chose it.

The same argument fixes where `Vulnerable` goes, which `tower-defense.md` never says. It is worded
"incoming damage ×(1+bonus)", and its two sources are the Cannon's `Demolition` and `Breaker`
branches — the explicitly anti-armor upgrades (`Breaker: Vulnerable +40%/3s; +50% vs armored`). An
amplifier applied *before* armor overcomes armor; applied after, it merely scales what armor already
let through. Against armor 6 with 30 damage and `+40%`: `max(1, 42 − 6) = 36` versus
`max(1, 24) · 1.4 = 33.6`. Amplifiers go first, or the anti-armor upgrade is not one.

None of this is baked in. `pipeline` takes a `Stage` **list**; the order is the caller's, and
`Effects.mitigate` is a named default. The doctrine is that the order is written down, ordered, and
individually testable — not that `Game.Core` picks the game for you.

### 3.3 The floor is a game-defining parameter

`tower-defense.md` floors at `1` (`applied = max(1, damage - armor)`). `turn-based-tactics.md` floors
at `0` (`dealt = max(0, ab.damage − coverOf defenderTile)`) and *depends* on it: §14 AC#3 has a
Crawler on a Forest tile (cover +2) survive a Ram (damage 1) because `max(0, 1 − 2) = 0`.

Run TBT's numbers through TD's floor and the Crawler takes 1 and dies. One constant, and a documented
acceptance criterion inverts. `floorAt` is a stage in the list, never a constant in the module.

### 3.4 Stacking versus refresh is a per-effect policy, and it is usually blank

Four policies cover the corpus. Naming them is the whole contribution; there is no clever code here.

| Policy | Meaning |
|---|---|
| `Refresh` | one instance; reapplying sets the remaining ticks to `max(old, new)` |
| `Strongest magnitude` | one instance; a stronger-or-equal application replaces it and refreshes; a weaker one changes nothing |
| `Stacks cap` | independent instances, additive, appended while `count < cap` |
| `Once` | the first application wins; every later one is a no-op |

`tower-defense.md` §4.5 is the only spec that states any of this, and it states it *per effect* — Slow
is "Strongest (lowest factor) active slow applies; refreshes duration if a stronger or equal slow is
reapplied"; Poison is "multiple stacks **do** add (cap 3 stacks)"; Stun is "Strongest = any active".
So `Strongest (fun e -> 1.0 - e.Factor)`, `Stacks 3`, and `Refresh` respectively.

Read that `magnitude` twice. For Slow, *strongest* means **lowest** factor — `0.35` beats `0.6` — so
`magnitude` has to be decreasing in `Factor`, and `Strongest (fun e -> e.Factor)` selects the weakest
slow in the game while looking entirely correct. `magnitude` is a projection onto "how strong", not
onto the effect's own field, and the two point in opposite directions here. Comparing with `>=` (not
`>`) is load-bearing too: it is what lets a second Frost tower re-up a slow of equal strength, which
§14 AC#7 assumes when it expires the effect `1.5 s` after the *last* hit.

**`Vulnerable` has no stated policy at all.** Neither does what happens to an existing Poison stack's
duration when a fourth is applied against the cap. Those are two real gaps in a spec that is otherwise
the most careful in the corpus, and they are invisible until two towers of the same type overlap.
We choose `Strongest (fun e -> e.Bonus)` and "append while `count < cap`, else no-op", and we write
the choice down here so the next reader is arguing with a decision instead of discovering a coin flip.

### 3.5 Durations are tick counts, and a tick is not a second

`roguelike-dungeon-crawler.md` carries `0.40 s` i-frames, `0.80 s` post-hit invulnerability, and a
`0.5 s` per-enemy contact re-tick cap. `tower-defense.md` carries `1.5 s` slows and `dps*dt` poison.
Accumulate any of those as a `float` per frame and two machines disagree by an epsilon that compounds
until a replay diverges. `Active<'E>.TicksRemaining` is an `int`, decremented once per fixed step, and
an `int` decrement cannot drift. At 60 Hz: 24, 48, 30, and 90 ticks.

The conversion happens **once**, where the content is authored. It is never re-derived per frame.

Two consequences that are easy to miss.

`Effects` never sees `dt`. `tower-defense.md` writes DoT as "deals `dps*dt` Poison damage each step",
which invites a `float` multiply inside the tick. Instead the caller stores the per-tick amount, and
`Effects` applies a constant. There is no `dt` in the module's surface at all, so there is nothing to
accumulate incorrectly.

And **a tick is whatever the caller drains.** `turn-based-tactics.md` is turn-based: its tick is a
*round*, and `lavaTickDamage` is 3 per round. `tickEffects` is unit-free. This is not a nicety — TBT
says the lava tick lands "at start of its owner's phase" (§4.1), at "End of round" (§4.3 step 4), and
"3 dmg/round" (§4.6), which are three different moments. A module that owns the clock hides that
ambiguity; one that makes the caller call `tickEffects` forces them to name the moment.

### 3.6 Damage-source typing is load-bearing, and only one spec has it

`turn-based-tactics.md` is explicit and alone: cover "does **not** reduce collision/knockback/
environmental damage (§4.6) — only declared attacks", and the `Armored` trait "reduces declared-attack
damage by 1 (like permanent cover) but does **not** reduce collision/environmental damage."

A pipeline with no source tag cannot express that, and it is not a detail: TBT's scoring
(§11) pays a bonus specifically for kills that were *not* direct damage, so the trichotomy is the
game's incentive structure. `Damage.Source` is `Declared | Collision | Environmental | Periodic`, and
`gatedBy` is a combinator that runs an inner stage only for the listed sources. TBT's cover and
`Armored` stages are `gatedBy [Declared]`, so its collision `1` and its lava `3` run the same pipeline
and are reduced by neither.

Now the trap, because it is the reason this section exists rather than a `Source` field being obvious.
`Kind` (Physical / Frost / Electric) and `Source` (declared / collision / environmental) are **different
axes**, and the corpus writes "damage type" for both. `tower-defense.md` §4.5 says "*elemental* sources
(frost/poison/tesla) ignore armor" — and it is tempting to spell that as a `Source` gate, because
Poison's DoT ticks really are `Periodic`. It looks right. It is wrong: a Frost bolt and a Tesla arc are
`Declared` attacks, so a source-gated armor stage still subtracts armor from them, and only Poison ever
gets its bypass. Armor-bypass is a property of the **`Kind`**, and it belongs inside `subtract`'s
`amountOf` — returning `0` for elemental kinds — which is exactly why `amountOf` and `resistOf` take
`'K` and not just `'T`. Gate on the axis the rule is actually about.

`'K` stays generic: `Game.Core` never learns what Frost is.

## 4. Surface

```fsharp
type Source = Declared | Collision | Environmental | Periodic

type Damage<'K> = { Kind: 'K; Source: Source; Base: float }

/// What a stage did to the running amount. `Halt` skips every later stage — including the floor.
type StageResult =
    | Continue of float
    | Halt of float

type Stage<'T, 'K> = { Name: string; Run: 'T -> Damage<'K> -> float -> StageResult }

/// Every stage's output, in order, plus the NAME of the stage that stopped the pipeline (if one did).
/// A caller suppresses a rider status effect (§2) on `Halted.IsSome` — never by matching the name,
/// which is free-form and exists for the trace and the test. Both shipped halting stages (`resist` at
/// exactly 1.0, and `immuneWhen`) halt with `0.0`, so `Halted.IsSome` implies the hit did nothing.
type DamageTrace = { Final: float; Halted: string voption; Steps: (string * float) list }

[<RequireQualifiedAccess>]
module Effects =
    val pipeline: stages: Stage<'T,'K> list -> target: 'T -> damage: Damage<'K> -> DamageTrace

    /// The whole AoE application: `targets` is what a region operator returned — each target with the
    /// transport multiplier the region assigned it. Seeds each pipeline at `Base * multiplier`, which
    /// is what puts transport before mitigation (§3.2) by construction rather than by convention.
    val applyAll:
        stages: Stage<'T,'K> list -> damage: Damage<'K> -> targets: ('T * float) list -> ('T * DamageTrace) list

    // Stages. Each is one line; the value is that they are named, ordered, and separately testable.
    val amplify:    bonusOf: ('T -> float) -> Stage<'T,'K>            // × (1 + bonus)
    val resist:     resistOf: ('T -> 'K -> float) -> Stage<'T,'K>     // × (1 − r); Halt 0.0 at r = 1.0
    val subtract:   amountOf: ('T -> 'K -> float) -> Stage<'T,'K>     // − armor / − cover
    val floorAt:    minimum: float -> Stage<'T,'K>                    // max minimum
    val immuneWhen: predicate: ('T -> Damage<'K> -> bool) -> Stage<'T,'K>   // Halt 0.0
    val gatedBy:    sources: Source list -> inner: Stage<'T,'K> -> Stage<'T,'K>

    // Status effects.
    type Active<'E> = { Effect: 'E; TicksRemaining: int }
    type Policy<'E> =
        | Refresh
        | Strongest of magnitude: ('E -> float)
        | Stacks of cap: int
        | Once

    val applyEffect: policy: Policy<'E> -> incoming: Active<'E> -> actives: Active<'E> list -> Active<'E> list
    val tickEffects: actives: Active<'E> list -> Active<'E> list
```

Three notes.

**`resist` folds immunity in.** It halts at exactly `1.0` rather than returning `Continue 0.0`. There
is no way to write the §2 bug with this surface, which is the only reason the stage exists rather than
being a `Stage` the caller assembles from `amplify`.

**`applyAll` seeds, it does not scale.** The transport multiplier is the pipeline's initial amount, not
a stage inside it. A stage could be reordered after `subtract`; a seed cannot.

**`'K` and `'T` are both generic.** `Effects` reaches up to nothing — not even `Primitives`. It joins
`Rng`, `FixedStep`, and `Pathfinding` as a module with no intra-core dependency, so it may compile
anywhere in the order. Placing it last keeps the `.fsproj` diff to two lines.

## 5. The decision on `Resolution.knockback`

`#43` asks for one of *move* / *stay* / *re-express*, and offers that the function "rode into the
BCL-only sim core on the back of the 'detection separate from response' rule" and "is not geometry, it
is a gameplay verb."

**The premise is wrong and the conclusion is right, for a different reason.**

`Resolution` describes itself as "the arcade/kinematic collision **response** layer — kept separate
from detection (`Geometry`)". A discrete displacement is a response, and it sits beside `pushOut` and
`slide`, which are its continuous siblings. Nothing rode in by accident. **Moving it to `Effects`
would be a breaking `game-sim-core` change that fixes nothing** — and it would drag `Cell` into a
module that is otherwise generic in its target (§4), making `Effects` worse.

The real defect is the signature.

```fsharp
val knockback: start: Cell -> step: Cell -> distance: int -> blocked: (Cell -> bool) -> Cell
```

`turn-based-tactics.md` §4.6 is the one spec in the corpus that needs a discrete push — "Knockback is
the soul of the game" — and this function is the wrong shape for it. Its `blocked` predicate has two
answers; TBT's terrain has three:

| TBT terrain | Required behaviour | `blocked` |
|---|---|---|
| wall / mountain / off-board / occupied | stop **before** it; 1 collision damage to both | `true` |
| lava | **enter** it, take 3, keep going | `false` |
| water / chasm | **enter** it and **die** | *neither* |

The third row is not impossible, and it is worth being precise about why, because the tempting claim
("`knockback` cannot implement TBT") is false. A caller *can* get AC#4 out of it. Pass
`fun c -> classify c <> Enter`, so water reports as a wall. The walk stops on dry land. Then compare
the result against `start + step·distance` to learn whether it was blocked at all, take `final + step`
as the blocking cell, ask your own `classify` what that cell was, and — seeing water — kill the unit
that never entered it. Sound, deterministic, pure. AC#4 passes.

Look at what that costs. The caller had to **lie to `knockback`** — water is not a wall — then **detect
its own lie** and compensate. The compensation is sound only because a drowned unit's position is
unobservable; make the halting cell non-fatal (a tar pit, a teleporter, a conveyor) and the unit
belongs *on* the cell `knockback` has just refused to enter, with no way to put it there. And the two
facts the compensation leans on — that the walk was blocked, and by which cell — are recoverable only
because the path is a straight ray with a constant `step`. Rebuilding the entered cells for the lava
tick is the same trick a third time.

That is the argument. Not that the function is unusable, but that **every caller reimplements the walk
around it**: widen the predicate, re-derive `reached`, re-classify the blocker, re-enumerate the path.
The walk establishes four facts — where the unit stopped, whether it was stopped, what stopped it, and
what it crossed — and returns one. Nothing else in the corpus uses it;
`roguelike-dungeon-crawler.md`'s knockback is a continuous `90 px/s` impulse, not this shape at all.
So the function has exactly one plausible consumer, and that consumer ends up writing the walk anyway.

**Decision: stay, and re-express — additively.**

```fsharp
/// How a cell answers a unit trying to enter it.
type CellStep =
    | Enter    // the unit moves onto it and may continue (passable ground; lava)
    | Stop     // the unit moves onto it and stops there (water; chasm)
    | Block    // the unit cannot enter; it stops on the previous cell (wall; occupied; off-board)

type PushStop =
    | Completed         // all `distance` steps were taken
    | Stopped of Cell   // entered this cell and stopped
    | Blocked of Cell   // could not enter this cell

/// Cells ENTERED, in order, excluding `start`; `Final` is the cell occupied at the end.
type Push = { Entered: Cell list; Final: Cell; Outcome: PushStop }

val push: start: Cell -> step: Cell -> distance: int -> classify: (Cell -> CellStep) -> Push
```

`classify` is to `push` what `cast` is to `Ballistics.step`: the sole coupling to the world, supplied
by the caller. It absorbs everything TBT layers on top, without `Game.Core` learning any of it —
`Flying` units hover over water, so their `classify` returns `Enter` where a ground unit's returns
`Stop`; `Massive` units are unpushable, so they are called with `distance = 0`. And the whole of §4.6
falls out of a match on `Outcome`, with the lava tick a fold over `Entered`.

`knockback` stays, deprecated, as a shim that is exactly equal to the old function on every input:

```fsharp
[<Obsolete("Use Resolution.push, which reports why the walk stopped. See docs/reports/2026-07-10-effects-damage-pipeline-design.md §5.")>]
let knockback start step distance blocked =
    (push start step distance (fun c -> if blocked c then Block else Enter)).Final
```

So `game-sim-core` takes an **additive** change now and a breaking one at the next major, with a
deprecation window. That matters beyond tidiness: [#66](https://github.com/FS-GG/FS.GG.Game/issues/66)
has just cut the `FS.GG.Game.Core` release that FS.GG.Rendering#269 was waiting on. The surface is
published. Removing `knockback` today would break a package on the day it shipped, to save a function
nobody calls; deprecating it costs one attribute.

## 6. The four AoE models, composed

`#43`'s acceptance asks that each be a `(region × falloff × pipeline)` composition, demonstrated. Under
§3.1 the first two collapse into one — a region operator `world -> ('T * float) list` — and all six
models in the corpus factor:

| Spec | Region operator → `('T * float) list` | Pipeline |
|---|---|---|
| TD cannon splash §4.10 | `Ballistics.splash c 42.0 (linearFalloff 0.5) pos grid` | `td` |
| TD Tesla chain §4.10 | k-nearest un-hit within `chainRange`; scalar `chainFalloff^k` in the **jump index**, not the distance | `td` |
| MC blast §4.4 | `SpatialGrid.queryRadius c r` at the blast's current radius; scalar `fun _ -> 1.0` | `[]` — any positive amount is lethal |
| SI bunker erosion §4.7 | the hit cell's 4-neighbourhood within 6 px (`Grids`); scalar `1.0` | `[]` — the cell is destroyed, the bullet consumed |
| TBT `Blast r` §4.5 | Chebyshev radius `r` in cells; scalar `1.0` | `tbt` |
| RL bomb §4.4 | `SpatialGrid.queryRadius c 90.0`; scalar `1.0` | `[]` at 40 for enemies; the player's i-frame gate is at the call site |

```fsharp
// One pipeline for all of tower-defense. Both closures key on the damage `Kind` (§3.6):
// `armorOf` returns 0 for elemental kinds, `resistOf` returns 0 for Physical. A Cannon shell and a
// Frost bolt walk the same four stages and take different branches inside two of them.
let td  = [ amplify vulnerableBonus; resist resistOf; subtract armorOf; floorAt 1.0 ]

// Cover and Armored are `Source`-gated, not `Kind`-gated, so collision (1) and lava (3) run this
// same list and are reduced by neither. The floor is 0, and AC#3 depends on it (§3.3).
let tbt = [ gatedBy [Declared] (subtract coverOf); gatedBy [Declared] (subtract armoredOf); floorAt 0.0 ]
```

Three things this table says out loud.

**Tower-defense needs one pipeline, not two.** Its cannon shell and its Tesla arc differ only in the
region operator and in what `armorOf`/`resistOf` return for their `Kind`. That is the whole thesis in
one row: the region is the variable, the pipeline is the constant.

Four of the six have a **uniform** scalar. Only tower-defense grades anything, and its two graded
models grade along different axes — distance for splash, jump index for the chain. The corpus has been
writing "falloff" for two unrelated ideas.

Three of the six have an **empty** pipeline. `missile-command.md` and `space-invaders.md` do not
mitigate; they decide membership and kill. That is the strongest evidence that region and pipeline are
separate concerns: half the corpus uses one of them and not the other.

## 7. Determinism

Pure, total, no wall-clock read, no shared mutable state, no floating-point tie-break — as elsewhere in
`Game.Core`.

- `pipeline` is a left fold over a `list`. No dictionary iteration, no sort, no `dt`.
- **Permutation invariance is structural, not tested-for.** `Stage.Run` is handed `'T`, the `Damage`,
  and the running amount — there is no world parameter, so a stage *cannot* observe another target.
  `applyAll` is therefore a `List.map` of a pure function, and the multiset of results cannot depend on
  `SpatialGrid` insertion order. (The set of targets is order-independent already: `splash` decides
  membership on the squared distance, and property-tests it.) The test #43 asks for is still worth
  writing — as a regression guard on the *type*, not on arithmetic.
- `tickEffects` decrements `int`s and drops the non-positive. `Loop.advance` / `FixedStep.drain` yield
  the same step count for a given accumulated time regardless of how frames sliced it, so an effect
  expires on the same step in a 30 fps and a 144 fps replay. No `float` duration crosses a frame
  boundary anywhere in the module.
- Totality: a non-finite `Base`, multiplier, or stage output contributes `0.0` rather than poisoning a
  damage total, exactly as `Ballistics.splash` treats a hostile `falloff`. `floorAt` with a non-finite
  minimum degrades to `0.0`. A hostile `Stage` cannot inject a `NaN` into a `DamageTrace`.
- `push` is a fixed-order walk of at most `distance` steps and is total for any total `classify`.

One honest exception. `push` is **per-unit**, and a chain of pushes is order-dependent *by design*:
TBT §13 says two units shoved into the same tile "resolve in id order; the second is blocked by the
first". The caller sequences them and closes `classify` over the updated occupancy. Permutation
invariance is a property of the AoE damage set, not of push resolution, and conflating the two would
make the second unimplementable.

## 8. What this deliberately does not do

Scope fence, in the spirit of the ballistics design's §6.

- **No region queries.** `Ballistics.splash`, `SpatialGrid.queryRadius`, and `Grids` own *who*. §3.1.
- **No chain traversal.** Tesla's k-nearest-un-hit walk is a region operator over the caller's target
  set; it needs the game's "already hit" bookkeeping and does not belong in a pure vocabulary.
- **No i-frame or re-tick-cap type.** `roguelike-dungeon-crawler.md`'s three windows (§4.2, §4.4) are
  `now >= openAtTick` against a `Map<sourceId, int>` at the call site. `Game.Core` owns the tick
  discipline (§3.5), not the map. Shipping a two-field record here would be a framework, not a
  vocabulary.
- **No HP, no death, no healing, no heart layers.** `pipeline` returns a number. RL's soul/black hearts
  (§4.6) are game state.
- **No RNG.** `turn-based-tactics.md` §12 has "**no random combat rolls** at all"; the mini-tanks design
  has two per shot. Both hand `Effects` an already-rolled `Base`, drawn from their own `Rng` stream.
- **No buff/debuff framework, no effect registry, no ECS components** — per #43's own scope line.
- **No continuous knockback.** RL's `90 px/s` impulse is `Resolution.slide`'s neighbourhood, not
  `push`'s. It is a velocity, and it needs nothing new.

## 9. Follow-ups

- Implementation: **[#70](https://github.com/FS-GG/FS.GG.Game/issues/70)**, `Blocked by` #42 — both add
  a `Game.Core` module and so collide on `FS.GG.Game.Core.fsproj`, the tests `.fsproj`, the skill
  manifest, and the surface baseline.
- Registering the `fs-gg-effects` skill row in the org registry (`registry/skills.yml`, owner
  `fs-gg-game`) is a change to **FS-GG/.github**, as `fs-gg-ballistics` was. Filed separately.
- Two gaps to raise against `docs/TestSpecs/Games/tower-defense.md`: `Vulnerable` has no stacking
  policy and no stated position relative to armor (§3.2, §3.4); and §4.5's floor of `1` gives a
  rim-grazing target 1 damage from a `linearFalloff 0.0` blast that reached it with none (§2).
- One against `docs/TestSpecs/Games/turn-based-tactics.md`: §4.1, §4.3 step 4, and §4.6 place the lava
  tick at three different moments (§3.5).
- `Resolution.knockback` is removed at the next `game-sim-core` major. The shim (§5) is the deprecation
  window, not the destination.
