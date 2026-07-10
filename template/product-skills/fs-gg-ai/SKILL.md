---
name: fs-gg-ai
description: Give a generated FS.GG.UI product enemies that decide — perception through fog, threat maps, posture, target selection and aiming — with a difficulty ladder that never cheats and a replay that never diverges.
---

# AI Decision Capability

## Scope

Use this skill when anything in your product **decides**: enemies that chase, towers that pick a target,
units that take a turn, bosses that telegraph. It covers what an agent is allowed to know, how it scores
the plans available to it, how difficulty is expressed, and how all of that stays byte-reproducible under
a replay.

It is deliberately **not** a behaviour-tree framework, not an ECS, and not utility-AI-as-a-library. The
hard parts of game AI are rules, not algorithms, and the rules are what this skill carries. The code it
leans on — `Ai` in `FS.GG.Game.Core` — is a handful of total functions over a caller-supplied oracle.
Perception, posture, positioning and aiming are your game's policy and stay in your game.

## Public Contract

```fsharp
open FS.GG.Game.Core

// Identity. Also the iteration order of the whole decision layer.
type AgentId = AgentId of int

// One perceived entity. `Seen` is YOUR payload — the facts perception yielded, never the model.
type Sighting<'T> = { Agent: AgentId; Position: Point; Seen: 'T; LastSeenTick: int }

// The fog boundary. Abstract: no constructor, no fields, no accessor that yields the world.
type TeamView<'T>

// Difficulty: five knobs, every one of them time or precision.
type Difficulty =
    { ReactionTicks: int
      AimErrorSigma: float
      SpotCycleTicks: int
      UsesWeakPointTargeting: bool
      ThreatWeight: float }

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

    val threatField:
        hasLos: (Cell -> Cell -> bool) -> sources: (Cell * float * int) list -> cells: Cell list ->
            Map<Cell, float>

    val fleeField:
        neighbourhood: Neighbourhood -> coefficient: float -> maxPasses: int -> desire: Map<Cell, float> ->
            Map<Cell, float>

    val best: score: ('p -> float) -> tie: ('p -> struct (int * int)) -> plans: 'p list -> 'p voption

module Difficulty =
    val easy: Difficulty
    val normal: Difficulty
    val hard: Difficulty
    val clamp: Difficulty -> Difficulty
```

## The one rule: the AI reads the fog, not the model

Write your decision function so it cannot see the world:

```fsharp
let decide (view: TeamView<TankFacts>) (self: Agent) (rng: Rng) : Command * Rng = ...
```

It is handed spotted entities and decaying ghosts. It does not have the model, so it cannot consult it.

Everything good about your AI follows from this one signature. It can be flanked, because it genuinely
does not know you are there. It shoots at ghosts. A scout that blinds the enemy team is a real play, not
a cosmetic one. And when someone later "fixes" a stuck agent by passing it the full model, the compiler
stops them instead of the game silently becoming omniscient.

**The hole, named honestly.** `TeamView<'T>` guarantees that no *accessor* yields the world. It cannot
stop you choosing `'T = Model` and smuggling the world in through the payload. What it buys is that the
leak is then a single explicit type argument at the construction site — `Ai.view : ... -> Sighting<Model>
list -> _`, which is greppable and reviewable — rather than an omniscient `decide` that reads perfectly
fine. Choose `'T` to be the facts: hit points, gun, hull bearing.

## Determinism has two specific enemies here

Both are silent. Both only appear in a replay, which is the worst place to find them.

**1. Iterate agents in ascending `AgentId`.** Never in `Map` or `HashSet` order — hash order is a
function of insertion history, so a recording made when units spawned in one order replays differently
when they spawn in another.

**2. Give every agent its own RNG sub-stream, keyed on its identity.**

```fsharp
let rng = Ai.substream root agent.Id
```

An agent drawing from the root RNG makes every other agent's aim depend on how many agents are alive.
Kill one enemy a tick earlier than the recording did, and every subsequent aim roll in the match shifts.

The subtler mistake is to fix that by walking `Rng.split` down the sorted agent list, giving each agent
"its own" stream. That is keyed on **position**, not identity — so the death of a low-id agent shifts
every later agent's stream and you have the same bug one level down. `Ai.substream` derives the stream
from the `AgentId` itself, which is stable under spawn and death alike. The test
`the naive position-keyed split FAILS the roster-varied replay` in `AiTests.fs` demonstrates exactly this.

A third, quieter enemy: **float addition is not associative**. `Ai.threatField` sums danger over sources
in a canonical order rather than the order you passed them, because otherwise the threat map depends on
`SpatialGrid` insertion order and diverges the moment an entity spawns.

## Difficulty is a knob vector, never a stat multiplier

Never give a hard AI more damage or more penetration. Give it **less time and worse hands**:

| Knob | Easy | Normal | Hard |
|---|---|---|---|
| `ReactionTicks` | 30 | 12 | 3 |
| `AimErrorSigma` | 0.12 | 0.05 | 0.01 |
| `SpotCycleTicks` | 30 | 15 | 5 |
| `UsesWeakPointTargeting` | false | false | true |
| `ThreatWeight` | 0.25 | 1.0 | 2.0 |

An easy AI is a slow, wide-shooting, centre-mass-aiming opponent. A hard one reacts in three ticks and
aims at your lower plate. **Both play by the same rules you do**, which means both *teach* those rules.
An AI that cheats on stats cannot teach the player the game, because the game it is playing is not the
one on screen.

The `Difficulty` record has no field with which to cheat. That is not an accident, and there is a test
(`the difficulty vector has no stat field`) that fails if someone adds one.

## Layer by cost, cheapest first

Recomputing a threat map per agent per tick is the default mistake. Schedule with `Ai.due`:

| Layer | Cadence | What |
|---|---|---|
| 0 Perception | every `SpotCycleTicks` | Read the `TeamView`. Nothing else. |
| 1 Threat map | every 15 ticks, and on terrain-version bump | `Ai.threatField` over a coarse, downsampled grid. |
| 2 Posture | on transition | Your FSM: `Patrol → Advance → Engage → Reposition → Retreat`. |
| 3 Positioning | every 30 ticks, or on version bump | `Pathfinding.astar` with `cost = terrainCost + ThreatWeight × danger`. |
| 4 Aiming | every tick | Solve the intercept (`Ballistics.lead`), clamp the traverse, add `Ai.aimError`. |

```fsharp
let threat =
    if Ai.due 15 tick || terrain.Version <> cachedVersion
    then Ai.threatField hasLos sources coarseCells
    else cached
```

Layer 1's `hasLos` on a downsampled grid is the dominant cost. Budget it: a 32×32 coarse grid × ≤8 known
enemies × every 15 ticks. Downsample; do not cast per fine cell.

## The threat map is a Dijkstra map, not a bespoke grid

`danger(cell) = Σ enemyDps × hasLos(cell, enemy) × inRange(cell, enemy)`, coarse-downsampled. Approach by
rolling downhill on desire; flee with the negative relaxation:

```fsharp
// distance-to-threat, from the substrate — navigation, no policy
let desire = Pathfinding.distanceField FourWay 2000 cost threatCells |> Map.map (fun _ d -> float d)

// scale by the policy coefficient and re-relax — policy, so it lives HERE
let flee = Ai.fleeField FourWay -1.2 16 desire
let step = Pathfinding.flowField FourWay (flee |> Map.map (fun _ v -> int (v * 100.0)))
```

`Pathfinding` deliberately does not ship a `fleeField`: the `-1.2` is a tuning constant, and tuning is
game policy. That separation is the entire reason this skill exists as a layer above the substrate.

## Scoring needs a total tie-break

Float scores tie. Two positions that are equally good really do score equally, and a comparison that
stops at the score leaves the winner at the mercy of list order — which is a function of whatever
enumerated the candidates. The index tail is what makes a plan reproducible.

The worked example, reproducing `turn-based-tactics` §4.9 exactly — enumerate
(reachable position ∪ stay) × abilities × target tiles, score with weighted terms, and break ties on
**(highest score, lowest target-tile index, lowest position index)**:

```fsharp
type Plan =
    { Position: Cell; PositionIndex: int
      Ability: Ability
      TargetTile: Cell; TargetIndex: int }

let score (p: Plan) =
    3.0 * expectedDamage p
    + 5.0 * expectedBuildingDamage p          // the Bomber archetype weights this 8.0
    + (if isKillingBlow p then 10.0 else 0.0)
    - selfExposure p

let plan =
    [ for pi, pos in Seq.indexed positions do
        for ability in abilities do
          for ti, tile in Seq.indexed (targetsOf pos ability) ->
            { Position = pos; PositionIndex = pi; Ability = ability; TargetTile = tile; TargetIndex = ti } ]
    |> Ai.best score (fun p -> struct (p.TargetIndex, p.PositionIndex))
```

`Ai.best` orders a `NaN` score below every real score, so an unscoreable plan is never preferred by
accident — and a fully tied plan keeps the incumbent, so the result is stable and therefore total.

## Common pitfalls

- **Passing the model to `decide` "just for pathfinding".** Pass a `TeamView` and a cost function.
- **Drawing from the root `Rng`.** Use `Ai.substream root agent.Id`, always.
- **Splitting the RNG down a sorted agent list.** Keyed on position; breaks when an agent dies.
- **Iterating agents out of a `Map`.** Sort by `AgentId` first.
- **Storing durations as float seconds.** `ReactionTicks` and `SpotCycleTicks` are tick counts.
- **Summing a threat map in caller order.** `Ai.threatField` canonicalises for you — do not re-sum yourself.
- **Recomputing the threat map every tick, per agent.** It is per-team, and it is on a cadence.
- **Making a hard AI hit harder.** Give it less time. See the knob table.
- **A scoring function with no tie-break tail.** The plan will not survive a replay.

## Build Commands

Run `./fake.sh build -t Dev` then `./fake.sh build -t Verify` in this product.

## Test Commands

Run `./fake.sh build -t Test` to exercise product-owned AI examples.

## Evidence

Record AI evidence (golden replays with the roster varied, threat-map permutation-invariance runs,
plan-scoring tie-break goldens) under this product's `readiness/` paths. Do not copy framework readiness
reports into the product.

## Package Boundary

`Ai`, `AgentId`, `Sighting`, `TeamView` and `Difficulty` live in `FS.GG.Game.Core` (referenced only on
the `game`/`sample-pack` profiles), above the spatial substrate they consume — `Point` from `Primitives`,
`Cell`/`Neighbourhood` from `Pathfinding`, and the seeded `Rng`. `FS.GG.Game.Core` is the BCL-only bottom
layer: it depends on nothing and pulls in no viewer, layout, or widget machinery.

`Ai` takes its line of sight as a caller-supplied `Cell -> Cell -> bool` oracle rather than depending on
`Los`/`Fov`/`Visibility`, so which cast you use — and whether a bush blocks it — stays your policy.

## Generated Product

Hold each agent's `AgentId` and `Difficulty` in your `Model`. On each fixed step: build one `TeamView`
per team from the spotting pass, then fold over agents **in ascending `AgentId`**, calling `decide view
agent (Ai.substream model.Rng agent.Id)` and collecting the commands. Apply the commands as a batch.
Gate the expensive layers on `Ai.due`, and cache the threat map against your terrain's `Version`.

## Persistent problems

When a problem outlasts reasonable in-repo attempts, extensive external research is **mandatory** —
consult **official online docs first** (the F#/.NET docs and the driven library's own reference), then
community sources. If your product uses Spec Kit, record findings and resolving links under the feature's
`specs/<feature>/feedback/`; otherwise record them in this skill's **Sources** line and any product-local
`docs/`. Offline, the mandate degrades to recording "research blocked — <why>" rather than hard-failing.

## Related

- [[fs-gg-game-core]] — the fixed step agents decide on, `Pathfinding` for routes and distance fields, and
  the seeded `Rng` that `Ai.substream` derives from.
- [[fs-gg-visibility]] — build the `hasLos` oracle `Ai.threatField` and the spotting pass take.
- [[fs-gg-grids]] — downsample the fine terrain grid to the coarse cells the threat map runs on.
- [[fs-gg-ballistics]] — solve the intercept in the aiming layer; add `Ai.aimError` to the solution.
- [[fs-gg-collision]] — what happens after the agent commits to a move it should not have made.
- [[fs-gg-rendering:fs-gg-scene]] — draw the telegraph, the aim cone, and the debug threat map.

## Sources / links

- F#/.NET docs: https://learn.microsoft.com/en-us/dotnet/fsharp/
- Dijkstra maps for game AI (the flee-field relaxation): http://www.roguebasin.com/index.php/Dijkstra_Maps_Visualized
- The Total Position Score / plan-enumeration shape: `docs/TestSpecs/Games/turn-based-tactics.md` §4.9
