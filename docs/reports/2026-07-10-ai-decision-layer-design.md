# The AI decision layer — design

- **Date**: 2026-07-10
- **Repo**: FS.GG.Game
- **Issue**: [#42](https://github.com/FS-GG/FS.GG.Game/issues/42) — child of [#39](https://github.com/FS-GG/FS.GG.Game/issues/39) (*Game logic above the spatial substrate*)
- **Contract**: `game-sim-core`, tier 1, **additive** (5 new exported types, no removals)
- **Status**: implemented

---

## 1. TL;DR

`registry/skills.yml` carried 38 skills and **none of them was AI**. Pathfinding was a section inside
`fs-gg-game-core`; there was no vocabulary anywhere in the org for perception, target selection, posture,
threat, or difficulty. Three TestSpecs each invented one from scratch, arrived at the same shape, and
shared no word for any of it.

This lands `fs-gg-ai` (the skill) plus `Ai` (the thin pure vocabulary it needs) in `FS.GG.Game.Core`. The
skill is the deliverable; the module exists so that the two rules that cannot be enforced by prose — the
fog boundary and the replay-stable RNG — are enforced by types and tests instead.

## 2. The bugs this module exists to prevent

Three, and every one of them is silent until a replay diverges.

**The omniscient AI.** An agent handed the world model reads it. Not maliciously — a stuck enemy gets
"fixed" by passing `model` instead of `view`, the fix works, and the game quietly stops being flankable.
Nothing fails. The AI simply becomes less fun in a way no test names.

**The agent-count-dependent RNG.** An agent drawing from the root generator makes every *other* agent's
draws a function of how many agents are alive and in what order they were stepped. Kill one enemy a tick
earlier than the recording did and every subsequent aim roll in the match shifts. The recording and the
replay diverge, and the diff points at the last agent to draw rather than the first agent to die.

The subtler form, which is what a careful author writes: give each agent its own stream by walking
`Rng.split` down the sorted agent list. That is keyed on **position in the list**, not identity — so the
death of a low-id agent shifts every later agent's stream, and the bug returns one level down. This is
the mistake worth naming, because it looks exactly like the fix.

**The order-dependent threat map.** Float addition is not associative. Summing `danger(cell)` over threat
sources in whatever order `SpatialGrid` enumerated them makes the map depend on insertion order, and a
replay diverges the moment an entity spawns.

## 3. Doctrine

### 3.1 The AI reads the fog, not the model — enforced by the type system

```fsharp
val decide: TeamView<'T> -> Agent -> Rng -> Command * Rng
```

`TeamView<'T>` is abstract in the `.fsi`: no constructor, no fields, and no accessor returning anything
but `Sighting`s. There is no expression of that type from which the world can be recovered.

Everything good follows. The AI can be flanked because it genuinely does not know you are there. It
shoots at ghosts. Blinding it is a real play. And the "fix the stuck agent by passing the model" edit
stops compiling.

**The hole, named rather than papered over.** The guarantee is *no accessor yields the world*, not *the
world cannot be smuggled*. A caller who instantiates `'T = Model` puts the world inside every `Sighting`
and the compiler cannot object, because `'T` is theirs. What the abstraction buys is that the leak
becomes one explicit, greppable type argument at the construction site — `Ai.view : ... ->
Sighting<Model> list -> _` — instead of an omniscient `decide` that reads correctly. That is a real
reduction in the failure's blast radius, and it is all a parametric type can honestly promise. The `.fsi`
says so in as many words.

Ghosts are the other half of the boundary. A sighting decays: fresh at `tick` is *spotted*, older is a
*ghost* carrying its stale position, older than `ghostLifetimeTicks` is *forgotten*. Shooting at a ghost
is correct behaviour.

### 3.2 Difficulty is a knob vector, never a stat multiplier

| Knob | Easy | Normal | Hard |
|---|---|---|---|
| `ReactionTicks` | 30 | 12 | 3 |
| `AimErrorSigma` | 0.12 | 0.05 | 0.01 |
| `SpotCycleTicks` | 30 | 15 | 5 |
| `UsesWeakPointTargeting` | false | false | true |
| `ThreatWeight` | 0.25 | 1.0 | 2.0 |

Every knob takes **time** or **precision** away. None touches a game stat, and — the load-bearing part —
there is no field in `Difficulty` with which one could. An AI that cheats on stats cannot teach the player
the game's rules, because the rules it plays by are not the rules on screen. A test asserts the record's
field set by reflection, so adding `DamageMultiplier` fails CI rather than a code review.

### 3.3 Determinism: identity-keyed sub-streams

```fsharp
val substream: root: Rng -> agent: AgentId -> Rng
```

Derived from the root state and the `AgentId` **itself** — a golden-ratio mix folded in both
multiplicatively and additively (so neither `AgentId 0` nor `AgentId -1` can cancel back to the root
stream), then one `Rng.split` to decorrelate neighbouring ids. Stable under spawn and death alike.

`AgentId` is also the iteration order of the whole layer: ascending, never `Map`/`HashSet` order.

`aimError` carries a smaller determinism obligation that is easy to miss: a non-positive or non-finite
`sigma` returns `0.0` **without advancing the generator**. A perfectly-accurate agent must consume no
randomness, or turning aim error on for one agent would shift every other agent's stream.

### 3.4 Scoring needs a total tie-break

`Ai.best score tie plans` orders by *(highest score, lowest first index, lowest second index, earliest in
list)*. This reproduces `turn-based-tactics` §4.9 exactly — *(highest score, lowest targetTile index,
lowest position index)*.

Float scores tie, genuinely and often. Without the index tail the winner is at the mercy of whatever
enumerated the candidates. A `NaN` score is ordered below every real score, so an unscoreable plan is
never preferred by accident — which a naive `>` fold would allow, since every comparison against `NaN`
is false and a `NaN` incumbent can never be displaced.

### 3.5 The threat map is a Dijkstra map, and the weighting lives here

`danger(cell) = Σ enemyDps × hasLos(cell, enemy) × inRange(cell, enemy)`, coarse-downsampled, summed over
sources in a **canonical order** (ascending `(Col, Row, dps, range)`) rather than caller order. Permuting
the source list yields a byte-identical map; a property test asserts it over 500 cases.

`hasLos` is a caller-supplied oracle. `Ai` never casts a ray, so which cast you use — and whether a bush
blocks it — stays policy.

The flee field is `Pathfinding.distanceField` **composed**, not a new primitive: scale by a negative
coefficient (≈ `-1.2`) and re-relax, then roll downhill with `flowField`. `Pathfinding.fsi` already
declined to ship this, on the grounds that the coefficient is "game policy, not navigation — so it lives
in the caller, not here." `Ai.fleeField` **is** that caller. The two files agree, and this is the seam
that #39 exists to draw.

Relaxation sweeps in ascending `(Col, Row)` and is bounded by `maxPasses`, so it is total on a
pathological field and deterministic regardless.

### 3.6 Layer by cost, cheapest first

| Layer | Cadence | What |
|---|---|---|
| 0 Perception | every `SpotCycleTicks` | Read the `TeamView`. |
| 1 Threat map | every 15 ticks, or on terrain-version bump | `Ai.threatField` over a coarse grid. |
| 2 Posture | on transition | The game's FSM. |
| 3 Positioning | every 30 ticks, or on version bump | `Pathfinding.astar`, `cost = terrain + ThreatWeight × danger`. |
| 4 Aiming | every tick | `Ballistics.lead`, clamp traverse, add `Ai.aimError`. |

`Ai.due cadence tick` gates them, with a **floored** modulus so a pre-match countdown (negative ticks)
does not alias. Recomputing a threat map per agent per tick is the default mistake; it is per-team, on a
cadence, and cached against the terrain version.

## 4. Surface

```fsharp
type AgentId = AgentId of int
type Sighting<'T> = { Agent: AgentId; Position: Point; Seen: 'T; LastSeenTick: int }
type TeamView<'T>                                   // abstract: the fog boundary
type Difficulty = { ReactionTicks: int; AimErrorSigma: float; SpotCycleTicks: int
                    UsesWeakPointTargeting: bool; ThreatWeight: float }

module Ai =
    val view: tick: int -> ghostLifetimeTicks: int -> Sighting<'T> list -> TeamView<'T>
    val viewTick: TeamView<'T> -> int
    val spotted: TeamView<'T> -> Sighting<'T> list
    val ghosts: TeamView<'T> -> Sighting<'T> list
    val known: TeamView<'T> -> Sighting<'T> list
    val tryFind: AgentId -> TeamView<'T> -> Sighting<'T> voption
    val substream: root: Rng -> agent: AgentId -> Rng
    val due: cadenceTicks: int -> tick: int -> bool
    val aimError: sigma: float -> rng: Rng -> struct (float * Rng)
    val threatField: hasLos: (Cell -> Cell -> bool) -> sources: (Cell * float * int) list
                     -> cells: Cell list -> Map<Cell, float>
    val fleeField: neighbourhood: Neighbourhood -> coefficient: float -> maxPasses: int
                   -> desire: Map<Cell, float> -> Map<Cell, float>
    val best: score: ('p -> float) -> tie: ('p -> struct (int * int)) -> plans: 'p list -> 'p voption

module Difficulty =
    val easy: Difficulty
    val normal: Difficulty
    val hard: Difficulty
    val clamp: Difficulty -> Difficulty
```

Design notes:

- **`Seen: 'T`** keeps the game's stat block out of the vocabulary. `Ai` carries the payload without
  inspecting it, so this type never grows a `HitPoints` field.
- **`sources: (Cell * float * int) list`** — position, dps, range — rather than a `Sighting` list. The
  extraction from sightings is policy; the summation is not.
- **`voption`** for "no answer", matching `Ballistics`.
- **Placed last in the compile order**, after `Visibility`. It sits *above* the spatial substrate: it
  consumes `Point`, `Cell`/`Neighbourhood` and `Rng`, and nothing consumes it. Its LOS is an oracle, so it
  does not depend on `Los`/`Fov`/`Visibility` at all.

## 5. Determinism

Pure, total, no wall clock, no ambient RNG, no `Map`/`HashSet` iteration order escaping into a result.
The four obligations, and how each is discharged:

| Obligation | Mechanism | Test |
|---|---|---|
| Agent draws independent of roster | `substream` keyed on `AgentId` | `GOLDEN: an agent's draws depend on its id alone` — roster `[1;2;3;4;5]` vs `[1;3;5]` |
| The naive fix is really broken | — | `the naive position-keyed split FAILS the roster-varied replay` (asserts `notEqual`) |
| Threat map independent of source order | canonical sort before the fold | permutation property, 500 cases |
| Plan choice independent of list order | total `(score, tie, index)` order | permutation property, 500 cases |

Hostile input is total throughout: `NaN` dps contributes `0.0`; a `NaN` desire cell is dropped rather
than poisoning its neighbours; a negative range is out of range everywhere; a non-positive cadence never
fires; `Difficulty.clamp` is idempotent and the identity on every shipped rung.

## 6. What this deliberately does not do

Not a behaviour-tree framework. Not an ECS. Not utility-AI-as-a-library. No perception implementation, no
posture FSM, no positioning solver, no aiming solver — those are policy, they differ per game, and the
three TestSpecs that invented them differ for good reasons. The corpus rule holds: pure functions over a
caller-supplied oracle; policy stays with the game.

Specifically **not** shipped, and named so nobody wonders: `hasLos` (yours), the archetype weight vectors
(`turn-based-tactics` §4.9's `damage×3`, `buildings×5`, Bomber's `×8`), the telegraph→commit→recover
timings (`roguelike-dungeon-crawler` §5.2), and sticky target acquisition (`tower-defense` §4.4/§4.8).
All four are worked examples in the skill, not functions in the module.

## 7. Fit

`fs-gg-ai` is the tenth skill this repo publishes, and — like `fs-gg-ballistics` — it originates here
rather than migrating from `FS.GG.Rendering`, because it sits above the spatial substrate this repo owns
after ADR-0022. It lands on top of [#35](https://github.com/FS-GG/FS.GG.Game/issues/35)'s reference
convention, so every `[[ref]]` in its body either resolves locally or names its publishing repo, and
`scripts/check-skill-refs.sh` holds that.

Consumers: [the mini-tanks product design](2026-07-10-mini-tanks-product-design.md) §8 (the five layers)
and §8.1 (the `TeamView` fairness rule) — this module is the vocabulary that design assumed existed, and
the source of the layer cadences in §3.6 and the knob vector in §3.2.

## 8. Follow-ups

- **`registry/skills.yml` in `FS-GG/.github` needs a `fs-gg-ai` row.** The manifest here is the source of
  truth for the digest ("registry = manifest = bytes"), and the registry reconciles from it — a
  cross-repo change, so it belongs to `cross-repo-coordination`, not this PR.
- The AI is the first consumer to want a **coarse-downsampled** view of a fine terrain grid. `Grids` has
  the pixel↔cell map but no `downsample`; the threat map currently expects the caller to build the coarse
  cell list. Worth a look once a second consumer appears.
- `fleeField` takes `Map<Cell, float>` while `Pathfinding.flowField` takes `Map<Cell, int>`, so composing
  them needs a scale-and-truncate at the seam (shown in the skill). If a third float field appears, a
  float `flowField` overload is probably the right answer.
