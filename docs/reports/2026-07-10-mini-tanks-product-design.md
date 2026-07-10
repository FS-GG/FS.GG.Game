# Mini Tanks — top-down 2-D armored combat, product design

- **Date:** 2026-07-10
- **Owner:** FS.GG.Game
- **Status:** Design proposal (pre-ADR, pre-charter). No work item exists yet.
- **Scope:** The whole product — a real-time, top-down 2-D tanks-vs-tanks game with five armored
  vehicles, line of sight, directional armor / hit zones, breakable terrain, and AI opponents.
  Covers the simulation model, the vehicle and armor systems, the terrain model, the perception
  stack, the AI, and the module/build plan. Excludes rendering craft, audio, and netcode (§10).
- **Language/target:** F# on `net10.0`, functional-first, **byte-deterministic**, immutable,
  Elmish/MVU. Simulation is BCL-only and headless; rendering is an adapter at the edge.
- **Builds on:** [`FS.GG.Game.Core`](../../src/Game.Core) as it exists today —
  `Rng` (SplitMix64), `FixedStep`, `Pathfinding` (A*/BFS over a walkability predicate),
  `SpatialGrid`, `Geometry` (AABB/circle/SAT-OBB contacts, segment casts), `Resolution`.
- **Sibling designs:** [line of sight](2026-07-05-game-logic-line-of-sight-design.md) ·
  [field of view / visibility](2026-07-05-game-logic-field-of-view-visibility-design.md) ·
  [collision](2026-07-05-game-logic-collision-detection-design.md) ·
  [pathfinding](2026-07-05-game-logic-pathfinding-navigation-design.md) ·
  [grids](2026-07-05-game-logic-grids-spatial-partitioning-design.md).
- **Grounding:** [constitution](../../.fsgg/constitution.md) §III (declared public surface),
  §V (Model–Update–Effect), §VI (test evidence). Determinism is a *tested property*, not a hope.

---

## 0. The one-paragraph version

Two teams of tanks fight over a destructible map. A tank is **two bodies, not one** — a hull that
drives and an independently-rotating turret that shoots — and that split is what makes every other
system interesting. Where a shell lands on the hull decides whether it penetrates, so pointing your
front plate at the enemy is a skill. What you can see is not what your team can see, and neither is
what your gun can reach, so LOS, field of view, and spotting are three different questions. Walls
stop shells until they don't, and the wall that falls opens a firing lane nobody planned. The AI
plays the same game you do, through the same fog, and cannot read the model.

The whole simulation is a pure `update : Msg -> Model -> Model * Effect list` over a fixed 60 Hz
tick with a seeded RNG carried in the model. That buys replays, golden tests, a headless balance
harness that runs every vehicle matchup in CI, and — if we ever want it — lockstep multiplayer.

---

## 1. Product shape, and the decisions that fix it

Four decisions constrain everything downstream. Each is stated with its alternative, because each
is genuinely arguable and reversing one later is expensive.

### D1 — Real-time, on a fixed 60 Hz simulation tick

The existing game-logic corpus is grounded in `turn-based-tactics`, and its LOS/FOV designs assume
an integer grid with discrete actors. **We diverge: this is real-time.** "Top-down mini tanks" is an
arcade genre — aiming under a moving turret traverse rate, leading a shell in flight, and reversing
out of a bad angle are the moment-to-moment verbs, and none of them survive turn quantization.

Real-time does *not* mean nondeterministic. The sim advances only in whole ticks drained by
`FixedStep.drain` (already implemented); frame time never enters the update. All durations are
stored as **integer tick counts**, never seconds-as-float, so a reload finishing is an `int`
comparison rather than a float accumulation that drifts across platforms.

> *Alternative rejected:* turn-based, reusing the corpus grounding directly. Cheaper to build,
> reuses grid LOS as-is, and would be a different (fine) game. Say so in the ADR.

### D2 — Continuous entities on a discrete terrain grid

Tanks, shells, and their collision shapes live in continuous `Point`/`Rect`/`ConvexPolygon` space.
Terrain is a **grid of tiles**. This hybrid is not a compromise — each side gets the representation
its problem wants:

| | Representation | Why |
|---|---|---|
| Tanks, shells | Continuous OBB / segment | Smooth motion, sub-tile aiming, real impact angles |
| Terrain | `Cell`-indexed tile grid | Destruction is local and O(1); pathfinding and FOV already speak `Cell` |

Both `Pathfinding` and the designed `Los`/`Fov` modules take a *predicate over `Cell`*, so the
terrain grid plugs into them with no adapter. That is the whole reason the grid wins.

### D3 — Hull and turret are independent bodies

A tank has a hull heading `θ_h` and a turret heading `θ_t`, each with its own traverse rate. The
hull decides where you go **and which armor plate faces the enemy**; the turret decides where the
gun points. They are separate OBBs for hit detection.

This is the single highest-leverage mechanic in the design, and the user's brief didn't name it.
Without it, "different armor per hit zone" is a dice roll on impact bearing — you can't *do* anything
about it. With it, angling the hull while the turret tracks a target is the core skill of the game,
the tank destroyer's fixed casemate becomes a real trade-off, and armor stats become decisions
rather than numbers.

### D4 — Shells are entities with flight time, not hitscan

A shell is a simulated body with position, velocity, and a swept collision test each tick. Muzzle
velocity becomes a stat; leading a moving target becomes a skill; a fast scout can genuinely
outrun a slow heavy's shell at range; artillery can arc over a wall. Hitscan would collapse all of
that into "did the ray touch." The cost is ~64 extra bodies and one swept cast each per tick, which
is nothing.

---

## 2. Simulation model

```fsharp
type Model =
    { Tick      : uint64              // authoritative clock; every duration is a tick count
      Rng       : Rng                 // root stream; per-entity streams split off at spawn
      Terrain   : Terrain             // §6 — carries its own Version
      Tanks     : Tank list           // iterated in ascending EntityId, always
      Shells    : Shell list
      Teams     : Map<TeamId, TeamView>   // §7 — the fog, per side
      Match     : MatchState }        // §9

type Msg =
    | Tick                                   // one fixed step
    | Command of EntityId * Command          // player or AI intent
```

**Determinism rules** (each one is a test, per constitution §VI):

1. Entities are iterated in ascending `EntityId`. Never in `Map`/`HashSet` iteration order.
2. Every entity carries its **own `Rng` sub-stream**, derived with `Rng.split` at spawn and stored
   on the entity. No entity ever draws from the root stream mid-tick — that would make every roll
   depend on the iteration order of every other entity.
3. All timers are `uint64` ticks. No float accumulators anywhere in `update`.
4. No wall-clock read below the effect interpreter. `FixedStep.drain` already guarantees this.

**Golden test:** `(seed, mapId, inputTape) → FNV-1a hash of Model at each tick`, snapshotted. Any
accidental nondeterminism — a `Map.iter`, a stray `DateTime.Now`, a float compare — turns the
snapshot red on the tick it first diverges, which is also the tick that tells you where to look.

---

## 3. The five vehicles

Stats are **data, not code**. The catalog is a record array; there is no `match vehicleClass with`
anywhere in the sim. Balance is then a table edit plus a re-run of the harness in §13.

```fsharp
type VehicleStats =
    { // chassis
      MaxHp: int; Mass: float                  // tonnes; drives ramming and terrain crush
      SpeedFwd: float; SpeedRev: float         // m/s
      Accel: float
      HullTraverse: float                      // deg/s
      // turret
      TurretTraverse: float                    // deg/s
      TurretArc: float option                  // None = full 360°; Some 10.0 = ±10° casemate
      // armor, nominal mm, before slope (§4)
      HullArmor: FaceArmor; TurretArmor: FaceArmor; TopArmor: int
      // gun
      Gun: GunStats
      // perception (§7)
      ViewRange: float; Camo: float }          // metres; 0.0–1.0 spot-resistance
```

| | **Lynx** (Scout) | **Cavalier** (Medium) | **Bastion** (Heavy) | **Adder** (TD) | **Sabot** (Artillery) |
|---|---|---|---|---|---|
| HP / Mass | 450 / 12 t | 900 / 30 t | 1600 / 60 t | 700 / 25 t | 400 / 20 t |
| Speed fwd / rev | 16 / 8 m/s | 12 / 6 | 7 / 3.5 | 10 / 5 | 8 / 4 |
| Hull traverse | 90 °/s | 55 | 28 | 40 | 30 |
| Turret traverse | 60 °/s, 360° | 40, 360° | 22, 360° | **12, ±10° casemate** | 12, 360° |
| Hull armor F/S/R | 25 / 15 / 12 | 90 / 45 / 35 | 200 / 90 / 60 | **160 / 30 / 25** | 20 / 15 / 12 |
| Turret armor F/S/R | 30 / 20 / 15 | 110 / 55 / 40 | 240 / 110 / 70 | — | 25 / 15 / 12 |
| Top armor | 15 | 25 | 40 | 25 | 12 |
| Gun | autocannon | AP | AP | AP | **HE, indirect** |
| Penetration | 45 mm | 160 | 220 | **280** | 60 (vs **top**) |
| Damage | 45 | 240 | 400 | 480 | 500 + 8 m splash |
| Reload | 0.35 s (burst) | 5.5 s | 9.0 s | 11.0 s | 22.0 s |
| Muzzle velocity | 900 m/s | 950 | 800 | 1200 | arc, 2.5 s flight |
| View range / Camo | 90 m / 0.55 | 75 / 0.30 | 60 / 0.10 | 70 / **0.70 stationary** | 45 / 0.20 |

### Why these five, and not five reskins

The stat vector is deliberately **non-dominated** — no vehicle is weakly better than another on
every axis — and the counters form a cycle rather than a ladder:

- **Bastion > Cavalier.** 200 mm of angled front simply eats a 160 mm gun. The medium must flank.
- **Adder > Bastion.** 280 mm pen defeats the heavy frontally. Nothing else in the roster does.
- **Cavalier, Lynx > Adder.** A ±10° gun arc at 12 °/s cannot track anything that gets past its
  shoulder, and its side armor is 30 mm — thinner than the *scout's* autocannon can chew through.
- **Sabot > Bastion, Adder.** Both are slow or stationary, and every roof in the game is thin.
  Artillery is the answer to a turtle.
- **Lynx > Sabot.** 16 m/s across the map versus a 22-second reload and 20 mm of hull.
- **Lynx enables Sabot.** Artillery has 45 m of view range and needs an ally's eyes. The scout is
  both the arty's predator and, on its own team, its targeting system.

Every arrow above is a consequence of the armor solver in §4 meeting the perception stack in §7 —
not a hand-tuned special case. That is the test of whether the systems are actually doing work.

---

## 4. Armor and hit zones

### 4.1 Which zone got hit is a geometry question, not a bearing question

A tank is **five convex bodies**: hull OBB, turret OBB, and two thin track OBBs flanking the hull.
Build them with the existing `Geometry.obbPolygon center halfExtents rotation` — hull and tracks at
`θ_h`, turret at `θ_t`.

Cast the shell's segment against all five, take the nearest hit. **The edge you entered through
*is* the zone.** A rectangle's four edges are exactly front / left / right / rear; there is no angle
bucketing, no `if bearing < 45.0` magic constant, and the turret's zones fall out of its own
independent rotation for free. `RayHit.Normal` gives the outward normal of that edge, and the
impact angle is

```
θ = acos( -shellDirection · faceNormal )      // 0° = perpendicular, dead-on
```

> **This needs a Core function that does not exist yet.** `Geometry` today can cast a segment at a
> `Rect` and at a `Circle`, and can *build* an OBB polygon, but there is no segment-vs-polygon cast.
> `Geometry.segmentPolygonHit : Point -> Point -> ConvexPolygon -> RayHit option` is a hard
> prerequisite for this entire section. See §12.

### 4.2 The penetration pipeline

An ordered pipeline of pure functions with early exits. Each stage is separately testable, which is
the point of writing it as a pipeline rather than one 40-line function.

| # | Stage | Rule |
|---|---|---|
| 1 | **Locate** | Nearest of the five OBB casts → `(part, faceNormal, θ, distance)` |
| 2 | **Track absorb** | Hit a track? The track eats the shell — module damage, **zero hull damage**. Unless caliber ≥ 2× track armor (overmatch). This is why brawlers aim at tracks. |
| 3 | **Ricochet** | `θ > shell.RicochetAngle` (AP 70°, APCR 75°, HEAT 85°, HE never) **and** `caliber < 2 × nominal` → deflect. Overmatch forbids ricochet: a big enough shell cannot skip off a thin enough plate. |
| 4 | **Normalization** | `caliber ≥ 3 × nominal` → reduce θ by a normalization constant. Big gun, thin plate: the shell bites. |
| 5 | **Effective armor** | `effective = nominal / cos θ'`, clamped to `5 × nominal` so grazing angles don't produce infinities. |
| 6 | **Pen roll** | `pen' = (pen − falloff × distance) × (1 + u)`, `u ~ U[−0.25, +0.25]` drawn from **the shooter's own `Rng` stream**. |
| 7 | **Resolve** | `pen' > effective` → penetration. Else: HE splashes chip damage, everything else deals zero. |
| 8 | **Damage** | `dmg × (1 + v)`, `v ~ U[−0.25, +0.25]`. Then apply module effects for the part hit. |

Properties worth asserting directly (constitution §VI):
`effective ≥ nominal` always · ricochet ⟹ zero hull damage · pen is monotone non-decreasing in
`pen` and non-increasing in `θ` · the whole pipeline is total on NaN input, matching the
NaN-safety convention every existing `Geometry` function already holds.

### 4.3 Shell types make the armor model legible

Four shells, each defeated by something different. This is what stops "shoot the thing" from being
the only verb.

| Shell | Pen | Ricochet | Distance falloff | Terrain | Beaten by |
|---|---|---|---|---|---|
| **AP** | high | > 70° | mild | little | angled armor |
| **APCR** | higher | > 75° | steep | little | range |
| **HEAT** | flat, high | > 85° | **none** | **detonates on first blocking tile** | any wall, fence, or bush between you and it |
| **HE** | low | never | none | **heavy destruction** | armor (chips only) |

HEAT detonating on the first blocking tile is the sharpest interaction in the design: it couples the
shell table to the terrain table, and it means shooting your expensive HEAT round through a wooden
fence wastes it. The player learns that by losing a round to a fence, once.

### 4.4 Modules

Penetrating a zone can hit a module — the module is a sub-rect of the part, so this is the same
geometry test one level down.

| Module | Where | On hit |
|---|---|---|
| Tracks | hull flanks | Immobilized for N ticks; shell absorbed (stage 2) |
| Engine | hull rear | Fire — damage over time until extinguished |
| Ammo rack | hull center | Stacking damage; third hit **detonates** — instant kill |
| Turret ring | turret base | Traverse rate halved until repaired |
| Optics | turret front | View range −30% |

Rear-engine placement is why flanking a heavy is worth the risk, and it falls out of the geometry
rather than a "flanking bonus" multiplier.

---

## 5. Ballistics

Each tick, a shell advances `position + velocity × dt` and resolves the swept segment:

1. **Broad phase** — `Geometry.sweptIntersects` against tank AABBs pulled from `SpatialGrid`
   (rebuilt each tick; at ≤16 tanks this is free). Existing surface, already implemented.
2. **Terrain** — march the segment through the tile grid with DDA, stopping at the first tile whose
   material has `BlocksShells`. This is the same traversal the `Los` module needs (§7, §12) — write
   it once.
3. **Narrow phase** — `segmentPolygonHit` against the five OBBs of each candidate tank (§4.1).
4. Take the nearest of (terrain hit, tank hit). Resolve through §4.2 or damage the tile.

Swept casts, not point tests: a 1200 m/s APCR round covers 20 m in one tick and would tunnel
straight through a 6 m tank. `sweptIntersects` exists precisely for this and its doc comment says so.

**Artillery** is the exception. `Sabot`'s shell is not traced through terrain during flight — it is
*fired*, and after `flightTicks` it *lands* at the aimpoint and splashes. It genuinely ignores
occluders because it arcs over them. This is the honest model, and it is much simpler than
simulating a parabola in a game with no third dimension.

---

## 6. Breakable terrain

```fsharp
type Material =
    { MaxHp: int
      BlocksMovement: bool
      BlocksSight: bool
      BlocksShells: bool
      ShellSoak: int             // pen absorbed by passing through, for spaced-armor behavior
      CrushMass: float option }  // None = never crushable; Some t = a tank ≥ t tonnes drives through

type Tile = { Material: MaterialId; Hp: int; State: TileState }
type TileState = Intact | Damaged | Rubble | Open

type Terrain =
    { Tiles: Tile[]; Width: int; Height: int
      Version: uint64 }          // ← see §6.2. This field is load-bearing.
```

### 6.1 The material table is where the tactics live

| Material | Movement | Sight | Shells | Notes |
|---|---|---|---|---|
| Brick building | ✔ | ✔ | ✔ | The main destructible. Intact → Rubble → Open |
| Concrete bunker | ✔ | ✔ | ✔ | 10× HP; effectively permanent cover |
| Sandbags | ✔ | ✖ | ✔ | **Cover without concealment** — shoot over, hide behind nothing |
| Bushes | ✖ | ✔ | ✖ | **Concealment without cover** — the exact inverse. Kills HEAT (§4.3) |
| Trees | ✔ | ✔ | ✖ | `CrushMass = Some 40.0` — Bastion drives through, Lynx does not |
| Water / ditch | ✔ | ✖ | ✖ | Blocks movement only |
| Rubble | ✖ | ✖ | ✖ | Passable, slow; what a building becomes |

The sandbags/bushes pair is the design in miniature. **Cover and concealment are different flags**,
and a genre that conflates them has no stealth. It also means §7's "can I see it" and "can I shoot
it" queries read *different masks of the same grid* — which is why they must be different functions.

### 6.2 Terrain mutation is a cache-invalidation problem, and `Version` is the answer

Three systems memoize against terrain: pathfinding (cached routes), FOV (per-team fog), and the AI
threat map. **All three are wrong the instant a wall falls** — and a wall falling to open a firing
lane is the entire appeal of destructible terrain, so this is the common case, not the edge case.

Every mutation returns a terrain with `Version + 1`. Every consumer stores the version it computed
against and recomputes on mismatch. This is one `uint64` compare and it is the difference between
"the AI shoots through the hole" and "the AI walks into a wall that isn't there anymore."

Two consequences to handle explicitly:

- **Perf.** Copying a 64×64 `Tile[]` per mutation is 16 KB. Fine for a shell or two; not fine for an
  HE barrage. Mitigation, if profiling demands it: chunk the array into 8×8 blocks and copy-on-write
  only touched chunks. Do not do this preemptively.
- **Entrapment.** Rubble can appear under a tank. `Resolution.pushOut` — already implemented —
  separates it along the minimum-translation vector. This is exactly what that function is for.

---

## 7. LOS, field of view, and spotting are three different things

Conflating these is the classic bug, and the [LOS sibling design](2026-07-05-game-logic-line-of-sight-design.md)
opens by saying so. This game needs all three, at three different layers:

| | Question | Shape | Consumer |
|---|---|---|---|
| **LOS** | "Does this segment reach?" | `Point → Point → bool`, DDA over the grid + segment casts vs dynamic OBBs | Shell paths, spot checks |
| **FOV** | "Which tiles can this tank see?" | Symmetric shadowcasting, `Cell → Set<Cell>` | Fog of war, rendering |
| **Spotting** | "Is *that tank* visible to my team?" | Entity-level, LOS + range + camo roll | Targeting, AI perception |

Do not build FOV by running LOS to every cell in radius. The sibling design's hard rule; it is slow
and produces asymmetric vision artifacts.

### 7.1 `canSee` and `canShoot` are not the same cast

They differ in **origin** (commander's optic vs gun muzzle — different points on a tank whose turret
may be turned 90° from its hull), in **mask** (`BlocksSight` vs `BlocksShells` — see the
bushes/sandbags inversion in §6.1), and in **what counts as reaching** (any of five sample points on
the target hull vs the specific aimpoint). A tank behind sandbags is visible and unshootable. A tank
in bushes is invisible and perfectly shootable. Both must be expressible, so both must be functions.

### 7.2 Spotting

An enemy is spotted when `distance ≤ viewRange` **and** LOS reaches ≥1 of five sample points (four
hull corners + center) **and** a camo roll passes. Evaluated on a **spot cycle** of every N ticks,
not every tick — both because it is the expensive query and because the resulting sub-second delay
between exposure and detection is what makes scouting a skill rather than a radar.

On losing a spot, the enemy does not vanish: it leaves a **last-known-position ghost** that decays
over a few seconds. The player shoots at where the tank was. So does the AI (§8) — same struct.

---

## 8. AI

### 8.1 The AI reads the fog, not the model

```fsharp
val decide : TeamView -> Tank -> Rng -> Command * Rng
```

The AI is handed a `TeamView` — spotted enemies and decaying ghosts — and **the type system forbids
it from touching `Model.Tanks`.** Not a policy, not a code review item: it does not have the value.

Everything good about the AI follows from this. It can be flanked because it genuinely does not know
you are there. It shoots at ghosts. A scout blinding the enemy team is a real play. And the moment
someone "fixes" a stuck AI by passing it the full model, the compiler stops them.

### 8.2 Five layers, cheapest first

| Layer | Runs | What |
|---|---|---|
| 0 Perception | every spot cycle | Read `TeamView`. Nothing else. |
| 1 Threat map | every 15 ticks | Coarse (2× downsampled) grid: `danger(cell) = Σ enemyDps × hasLos(cell, enemy) × inRange`. Recompute on `Terrain.Version` bump. |
| 2 Posture | on transition | FSM: `Patrol → Advance → Engage → Reposition(reloading) → Retreat(low hp)`. Artillery gets `Support`. |
| 3 Positioning | every 30 ticks, or on version bump | Pick a cell minimizing threat while holding LOS to the primary target; `Pathfinding.astar` to it with `cost = terrainCost + threatWeight × danger`. Existing surface, `maxVisited = 2000`. |
| 4 Aiming | every tick | Solve the intercept for shell velocity; clamp the turn to `TurretTraverse`; add dispersion; pick aim zone (weak point if flanked, else center-mass); pick ammo (AP default, HEAT vs heavy front — **but check the line for a bush first**, HE vs artillery). |
| 5 Hull angling | every tick | Rotate the hull so the front-plate normal bisects the bearing to the top two threats. The `Adder` simply points its front at whatever it fears most. |

Layer 5 is the one that makes the AI feel like it is playing *this* game. An opponent that angles is
an opponent whose armor you have to think about, and it costs one `atan2` and a clamp.

Layer 1's `hasLos` on a downsampled grid is the AI's dominant cost. Budget it: 32×32 coarse cells ×
≤8 known enemies × every 15 ticks.

### 8.3 Difficulty is a knob vector, not a stat multiplier

Never give the AI more penetration. Give it less time and worse hands:

`reactionTicks` (delay between spot and first response) · `dispersionMultiplier` · `usesWeakZoneTargeting: bool` ·
`spotCycleTicks` · `threatWeight` (how much it values safety over aggression).

An easy AI is a slow, wide-shooting, center-mass-aiming tank. A hard one reacts in three ticks and
aims at your lower plate. Both play by the armor rules you do — which means both *teach* them.

### 8.4 Determinism

Iterate tanks by ascending `EntityId`; draw from each tank's own `Rng` sub-stream. An AI that draws
from the root RNG makes every tank's aim depend on how many tanks are alive, which is a heisenbug
that only appears in a replay.

---

## 9. Match layer

Tanks-vs-tanks with no win condition is a sandbox. Minimum viable rules layer:

- **Elimination** (default), **Capture point** (holding a cell region ticks a counter), and
  **Escort** (a convoy tank must reach a cell).
- Teams, spawn zones, a match clock in ticks.
- `MatchState` is part of `Model`, so a replay reproduces the result, and the balance harness (§13)
  can read a winner without a rendering layer.

Capture point is the one that makes the roster matter: elimination alone lets the `Adder` sit in a
corner forever, and the whole `Sabot` counter exists to punish exactly that. Objectives force
movement, and movement is where armor angles get exposed.

---

## 10. "What else?" — ranked

The brief named five vehicles, LOS, hit zones, breakable terrain, and AI. Everything above already
adds four things it didn't: **hull/turret separation** (D3), **shell flight time** (D4), **shell
types** (§4.3), and **spotting-distinct-from-LOS** (§7). Beyond those, ranked by
(design value ÷ cost):

**In, for v1 — each is small and each pays for a system already being built**

1. **Smoke.** A dynamic occluder that blocks sight and not shells, decaying over ~10 s. Exercises the
   LOS layer's dynamic-occluder path, is the counter to artillery and to the `Adder`'s sightline, and
   is maybe 60 lines. Highest value-per-line in this list.
2. **Ammo count and shell selection.** Without a limited HEAT loadout, §4.3's shell table degenerates
   to "always bring the best one."
3. **Ramming and terrain crush.** `Mass` is already in the stat vector and `CrushMass` in the
   material table. Momentum transfer on tank-tank contact via the existing `Resolution.slide` /
   `pushOut`. The `Bastion` driving through a treeline is a 60-tonne statement.
4. **Reload/aim-time feedback in the UI.** The armor model is invisible without a hit indicator that
   says *ricochet* / *no penetration* / *ammo rack*. The systems only teach if they narrate.

**Next, after the core lands**

5. **A coarse elevation layer** (heights 0/1/2). Unlocks **hull-down** — parking behind a rise so
   only your thick turret front is exposed. It is the deepest positional mechanic in the genre and
   it composes perfectly with §4's per-part hit zones. Deferred only because it touches LOS, FOV,
   pathfinding, and rendering at once.
6. **Crew.** Commander/gunner/driver as damageable slots behind the modules of §4.4.
7. **Repair and consumables** (fix tracks, extinguish fire) — pairs with §4.4 modules.

**Deferred, deliberately**

8. **Multiplayer.** The deterministic fixed-tick core makes lockstep netcode *possible* later at
   roughly the cost of an input-delay buffer. Do not build it now; just don't break determinism.
9. **Procedural maps.** `Rng` is already in the model. Cheap to add, adds nothing until the systems
   are tuned on hand-made maps.

**Out of scope for this document:** rendering craft, audio (see the
[audio design](2026-07-05-game-audio-library-architecture.md)), persistence, progression.

---

## 11. Architecture

```
FS.GG.Game.Core           (exists; BCL-only, reaches up to nothing)
  Primitives  Geometry  Rng  FixedStep  Pathfinding  SpatialGrid  Resolution
  + Los       ← designed 2026-07-05, NOT IMPLEMENTED  (§12)
  + Fov       ← designed 2026-07-05, NOT IMPLEMENTED  (§12)

FS.GG.Tanks.Sim           (new; depends only on Game.Core; headless, pure)
  Vehicle    stat catalog — data, no branching
  Terrain    grid, materials, damage, Version                    (§6)
  Ballistics shell entities, swept casts                         (§5)
  Armor      zone location + penetration pipeline                (§4)
  Vision     LOS/FOV/spotting → TeamView                         (§7)
  Ai         decide : TeamView -> Tank -> Rng -> Command * Rng   (§8)
  Match      rules, win conditions                               (§9)
  Sim        Model / Msg / update / Effect                       (§2)

FS.GG.Game.Render         (exists; adapter to FS.GG.UI.Scene)
```

Two boundaries are worth defending in review:

- **`Terrain` belongs in `Tanks.Sim`, not `Game.Core`.** A tile with hit points is game domain. Core
  stays a generic, BCL-only vocabulary; it exposes `Los`/`Fov` over a `Cell -> bool` predicate and
  never learns what a wall is. Pushing `Terrain` down into Core would be the first crack in ADR-0022.
- **`Ai` cannot depend on `Sim`.** It takes `TeamView`. Enforced by compile order (§8.1).

Every module ships an `.fsi` (constitution §III). The signature file *is* the design review.

---

## 12. What this design needs from `Game.Core` that isn't there

Concrete, and blocking. Nothing in §4 or §7 can be built until these land. Each is a filed issue.

| # | Gap | Blocks | State today |
|---|---|---|---|
| 1 | `Geometry.segmentPolygonHit : Point -> Point -> ConvexPolygon -> RayHit option` ([#31](https://github.com/FS-GG/FS.GG.Game/issues/31)) | **All of §4** | **Missing.** Core can cast a segment at a `Rect` and a `Circle`, and can *build* an OBB with `obbPolygon` — but cannot cast at one. Without this there are no hit zones. |
| 2 | `Los` — grid line-of-sight over a `Cell -> bool` transparency predicate ([#32](https://github.com/FS-GG/FS.GG.Game/issues/32)) | §5, §7, §8 | **Written, in the wrong place.** `FS.GG.Rendering/template/fragments/line-drawing` ships `line` (thin Bresenham), `supercover`, and `lineOfSight` as a *product-owned copy-paste fragment* — pure, integer-only, deterministic, already `open FS.GG.Game.Core`. No `.fsi`, no package, no tests. Promote it. |
| 3 | `Fov` — symmetric shadowcasting ([#33](https://github.com/FS-GG/FS.GG.Game/issues/33)) | §7 fog | **Genuinely missing everywhere.** Designed 2026-07-05, never implemented, and nothing in the org can be promoted into its place. The only one of the three with no existing code. |
| 4 | `Visibility` — continuous angular-sweep visibility polygon ([#34](https://github.com/FS-GG/FS.GG.Game/issues/34)) | §7.1 `canShoot` | **Written, in the wrong place, and buggy.** `FS.GG.Rendering/template/fragments/visibility` ships `raySegment`/`isVisible`/`polygon` (redblobgames sweep, no `atan2`, byte-deterministic). Its `SpatialGrid` broad-phase buckets segments by endpoint, so **a wall spanning the sight box with both ends outside it stops occluding** — a see-through-wall bug at exactly this game's wall lengths. |
| 5 | `Grids` — `Edge`/`Vertex` parts, adjacency, pixel↔cell map ([#38](https://github.com/FS-GG/FS.GG.Game/issues/38)) | §5, §6, §7 | **Written, in the wrong place.** `FS.GG.Rendering/template/fragments/grids`. Clean, no bugs. Core has `Cell` and nothing else: no `Edge`, no `cellAt` (the pixel→tile inverse), no `edgeSegment` — which is precisely how a tile grid becomes the occluder `Segment list` that item 4 consumes. |

The shape of the work is not "write five algorithms." It is **one algorithm to write (`Fov`), three to
promote, and one bug to fix on the way through.** Items 2, 4, and 5 are the code half of the
`game-starter-two-copies` registry row (`coherent: false`): the primitives moved down into
`FS.GG.Game.Core` under ADR-0022, but the four fragments that consume them stayed frozen upstream in
Rendering's game profile, where every scaffolded game copies and diverges them.

That divergence is not hypothetical. `Breakout1` — a real scaffolded game product — carries the
`Visibility` bug verbatim, *and* its copy has already drifted from upstream because the scaffold's
`sourceName` substitution rewrote the word "product" inside the comments, turning "cross-product
angular comparator" into "cross-breakout1 angular comparator"
([Rendering#264](https://github.com/FS-GG/FS.GG.Rendering/issues/264)). Copies rot. Promote them.

The fourth fragment, `collision`, is **not** worth promoting: its `Body`/`Contact`/`ResponseRule`
model overlaps `Resolution`, and its `collide`/`step` pass is discrete-overlap only — it never calls
`Geometry.sweptIntersects`, so a 1200 m/s shell tunnels straight through a wall. §5 does its own
swept narrow phase for exactly this reason.

Item 1 is a genuine new Tier-1 surface addition and needs its own spec, `.fsi`, and golden tests
against `aabbContact`'s conventions (at `rotation = 0`, an `obbPolygon` cast must agree with
`segmentAabbHit`, exactly as `polygonContact` already agrees with `aabbContact`).

### 12.1 What can be consumed as-is

Not everything is a gap. Already shipped and directly usable:

| Need | Use | Where |
|---|---|---|
| Fixed 60 Hz tick (§2) | `FixedStep.drain` | `Game.Core` |
| Seeded per-entity RNG (§2, §4.2) | `Rng.ofSeed` / `split` / `nextFloat` | `Game.Core` |
| Shell broad phase (§5) | `Geometry.sweptIntersects`, `SpatialGrid.build`/`queryRadius` | `Game.Core` |
| Shell narrow phase vs terrain (§5) | `Geometry.segmentAabbHit` | `Game.Core` |
| Tank OBBs (§4.1) | `Geometry.obbPolygon`, `polygonContact` | `Game.Core` |
| Tank-vs-tank push-out, rubble entrapment (§6.2) | `Resolution.pushOut` / `slide` | `Game.Core` |
| AI routing (§8.3) | `Pathfinding.astar` over a `Cell -> bool` | `Game.Core` |
| Sim → `Scene` projection (§11) | `Adapter.drawCell` / `drawCells` / `drawPath` | `Game.Render` |
| Render-interpolated game loop | `Canvas.Loop.advance` / `alpha` (+ your own world lerp) | `FS.GG.UI.Canvas` |
| Window, 60 fps host, fixed-resolution letterbox | `ControlsElmish.runInteractiveApp`, `ViewerOptions.LogicalSize` | `FS.GG.UI` |
| WASD held-key state | `KeyboardModel.PressedKeys` | `FS.GG.UI.KeyboardInput` |
| **Hull / turret rotation (D3)** | `Animation.Transform.toPerspectiveTransform` → `Scene.withPerspective`. **The only rotation path in the org** — there is no `Scene.rotate`. `AnimationState.retarget` gives turret slew without snap-back. | `FS.GG.UI.Scene` |
| Recoil, muzzle-flash fade | `AnimationState<float>` / `Tween` | `FS.GG.UI.Scene` |
| Camera pan/zoom | `Canvas.viewport` (build the `PerspectiveTransform` yourself — no `Camera` type exists) | `FS.GG.UI.Controls` |
| Hull polygons, turret arcs, health arcs | `Scene.path` / `vertices` / `arc` | `FS.GG.UI.Scene` |
| **Golden-replay fingerprint (§13)** | `SceneCodec.packageIdentity (SceneCodec.export scene).CanonicalBytes` — this *is* the determinism mechanism; `CanvasDemo` proves reproducibility with it | `FS.GG.UI.Scene` |
| Evidence/expectation harness | `fs-gg-testing`, `ExpectedOutcome`, `FrameInput<'msg> list` script folding | Rendering |
| Unit glyphs (§10) | `FS.GG.UI.Symbology` — see §12.2 | Rendering |
| Headless PNG for the design loop | `Render.toPng` (fail-loud) | Rendering |
| Sound as pure effects | `fs-gg-audio` / `FS.GG.Audio.Core` — real OpenAL output exists, but see the caveat below | Audio |

**The real-time skeleton to copy is `samples/CanvasDemo`**, not the `Pong`/`Snake`/`Tetris` samples.
It separates `World` (sim) from `Model = { Step: StepState<World>; ... }`, drives `Loop.advance dt
integrate elapsed`, and lerps `Previous → Current` by `Loop.alpha` at render time — a true fixed-tick
sim with smooth 60 fps presentation, which is exactly §2. `Pong` uses a hand-rolled timer accumulator
in the model; copy its continuous-physics `step` body but not its tick idiom.

`Breakout1` (in this workspace, outside the org repos) is a complete scaffolded game product with all
four fragments already materialized. It is the fastest way to see the whole stack assembled — and, as
noted above, the fastest way to see what copying costs.

**Absences, stated plainly.** No particle system, no sprite atlas or spritesheet, no texture object
(`Scene.image` takes a string id), no tilemap renderer, no ECS, no steering/behaviour library beyond
`Pathfinding` + `Rng`. Muzzle flashes, tracers, and explosions are per-frame `Scene` primitives driven
off sim state. Terrain rendering is your own loop over `cellRect`. All of that is consistent with §11
— the sim owns the truth and the view is a pure projection — but none of it is free.

**Audio caveat.** The vocabulary is deterministic and the OpenAL backend really plays sound, but
`PlaySfx` has no loop flag, no voice handle, and no pitch, and **no shipped backend implements
`IMixingBackend`** — so `Engine`'s computed pan, bus volumes, and ducking never reach hardware. A
tank engine loop and directional gunfire are not expressible today. Filed as
[Audio#11](https://github.com/FS-GG/FS.GG.Audio/issues/11).

### 12.2 Symbology

`FS.GG.UI.Symbology` ships a fixed pre-attentive channel grammar (`Token`), three interchangeable
grammars (`Grammar = Token | Badge | Ring`), and — unusually — a pure **legibility linter**
(`Legibility.score : Token list -> Report`) that scores a symbol set against a per-channel capacity
table. That makes "is this board readable" a CI check, the same trick §13 plays with balance.

The fit is close. `Klass = Mobile | Heavy | Scout` is already this roster's vocabulary, and
`Threat`/`Health`/`Speed`/`Shield`/`Faction`/`Heading` map onto `VehicleStats` nearly one-to-one.
`TokenState = Confirmed | Suspected` is exactly the spotted-vs-ghost distinction of §7.2, already
encoded on the dashed-stroke channel.

Two frictions:

- **`Token` has one `Heading`.** D3 says a tank has two. Filed as
  [FS.GG.Rendering#260](https://github.com/FS-GG/FS.GG.Rendering/issues/260) — additive opt-in
  channel, sanctioned `Sigil.Mark` workaround, or a recorded "out of scope."
- **`Symbology` depends on `Scene`.** So the `TankStats -> Token` ChannelMap belongs in
  `FS.GG.Game.Render`, never in `Tanks.Sim`. `Game.Core` reaches up to nothing (ADR-0022 §2).

### 12.3 Where this design re-invented something `docs/TestSpecs/Games/` already wrote

The fifteen game specs in this repo are a design corpus, not just examples. Several formalize, in the
house 15-section format, mechanics this document derived from scratch. Read them before implementing:

| This design | Already specified in |
|---|---|
| §6.2 terrain `Version` + cache invalidation | `sandbox-survival.md` §7.1 — mutable chunk arrays with `Version`/`Dirty` flags inside an immutable `Model`, plus an immutable `WorldEvent` stream. The honest large-world pattern, already worked out. |
| §7 fog of war | `sandbox-survival.md` §4.10 — per-tile light 0–1 by **BFS flood fill with attenuation**, recomputed incrementally in the changed region. The closest thing in the corpus to vision propagation. |
| §5 shell flight time, splash falloff | `tower-defense.md` §4.10 — projectile vs hitscan firing models, `splashRadius` with linear falloff to 50% at the edge, `arriveEps`, lifetime cap. `missile-command.md` §4.3–4.4 adds time-to-impact (`distance / speed`) and the expanding blast circle. |
| §4.2 armor formula | `tower-defense.md` §4.5 — `applied = max(1, damage − armor)` for physical, per-target `resist ∈ [0,1]` multipliers for typed damage. A flatter model than the slope/ricochet pipeline here, and worth reading as the alternative. |
| §8.4 AI target selection | `tower-defense.md` §4.4/§4.8 — sticky targeting with First/Last/Strongest/Closest modes. `turn-based-tactics.md` §4.9 — deterministic **weighted plan scoring** over (position × ability × target) with explicit tie-breaks. The latter is a better fit for §8's threat map than anything invented here. |
| §6 destructible cover | `space-invaders.md` §4.7 — bunker erosion: a hit destroys the cell plus its 4-neighbourhood within a 6 px radius, then consumes the bullet. A compact chip-away model. |
| §4 flanking | `metroidvania.md` §5 — the Sentinel's "shielded front; flank to hit". **The only facing-based vulnerability in the whole corpus.** |
| §8.2 posture FSM | `roguelike-dungeon-crawler.md` §5.2 — per-archetype FSMs with explicit telegraph → commit → recover timings, and data-driven bullet-pattern emitters. |
| §2 per-entity RNG sub-streams | `roguelike-dungeon-crawler.md` §4.8 — separate `LayoutRng` / `DropRng` streams so layout is independent of combat. Same discipline, already named. |

Also worth knowing: `turn-based-tactics.md` §4.1/§4.5 grades **cover as damage reduction** (Rough +1,
Forest +2) rather than as an occluder — a genuinely different lever from §6.1's binary
`BlocksShells`, and cheaper to tune.

**Nothing in the corpus formalizes fog of war, spotting/camo, or hull-vs-turret facing.** Those three
are this design's original contribution, and a Mini Tanks TestSpec authored in the house format would
be the first to write them down.

---

## 13. Testing and balance

Determinism is not a nice property here — it is the test strategy.

- **Property tests** on the penetration pipeline (§4.2): the monotonicity, ricochet, and
  `effective ≥ nominal` invariants, plus NaN-totality matching every existing `Geometry` function.
- **Golden replays.** `(seed, map, inputTape)` → per-tick model hash, snapshotted. Catches
  nondeterminism at the tick it enters.
- **Terrain/pathfinding coupling.** Assert `astar` fails across an intact wall, and succeeds across
  the same wall after an HE round removes it — and that the cached path was invalidated by
  `Version` rather than by luck.
- **The balance matrix.** Run all 5×5 vehicle matchups × 200 seeds, headless, in CI. Produce a
  win-rate matrix. Assert every vehicle lands in a 35–65% overall band, and that each counter arrow
  in §3 actually holds. **Balance becomes a test rather than an opinion** — which is only possible
  because the sim is pure, headless, and deterministic. This is the payoff for every constraint in
  §2, and it should be built early, not last.

### 13.1 Balance can be a governance gate, cheaply

This repo already dogfoods `FS.GG.Governance` — `.fsgg/governance.yml` calls it "the first FS-GG
framework repo to dogfood governance on its own code," with `block-on-ship` `build` and `test` checks.
The balance harness is the same shape as the `test` check: add a `tooling.yml` command, add a
`capabilities.yml` check (`maturity: block-on-ship`, `tier: focusedTests`) with a `pathMap` glob over
the sim sources, and it blocks at `Gate`/`Release`.

The honest limit: the gate only observes a process exit code. It does not understand a win-rate
matrix — the 35–65 % assertion lives inside the harness. The gate makes it *unmergeable when red*,
which is the part worth having. Governance stays advisory and is never a build dependency, so this
costs nothing until you ratchet the profile.

`Legibility.score` (§12.2) can be bound the same way, giving §10's "the systems only teach if they
narrate" a mechanical floor.

---

## 14. Open decisions

| # | Decision | Recommendation |
|---|---|---|
| 1 | Real-time (D1) diverges from the corpus's turn-based grounding | Record as an ADR. The corpus's LOS/FOV designs survive the divergence intact; only the actor model changes. |
| 2 | Does a ricocheting shell continue with a deflected vector, or despawn? | Despawn in v1. Continuation is a fun emergent kill and a nasty determinism surface. |
| 3 | Terrain copy-on-write chunking (§6.2) | Do not build until profiled. |
| 4 | Elevation / hull-down (§10 item 5) | Defer. Design the `Los` signature so a height layer can be added without breaking it. |
| 5 | Tile size | 2 m. 64×64 tiles = a 128 m map, which fits the 45–90 m view-range band with room to flank. |

---

## 15. Suggested delivery slices

Each is an SDD work item; the first three are pure `Game.Core` and unblock everything else.

| Slice | Item | Tier | Issue |
|---|---|---|---|
| 1 | `geometry-segment-polygon-hit` (§12) | 1 — contracted | [#31](https://github.com/FS-GG/FS.GG.Game/issues/31) |
| 2 | `core-los` — promote the `LineDrawing` fragment | 1 — contracted | [#32](https://github.com/FS-GG/FS.GG.Game/issues/32) |
| 3 | `core-fov` — symmetric shadowcasting | 1 — contracted | [#33](https://github.com/FS-GG/FS.GG.Game/issues/33) |
| 4 | `core-visibility` — promote the sweep, fix the chord cull | 1 — contracted | [#34](https://github.com/FS-GG/FS.GG.Game/issues/34) |
| 5 | `tanks-sim-skeleton` — Model/Msg/update, fixed tick, hull+turret kinematics | 1 | — |
| 6 | `tanks-terrain` — grid, materials, damage, `Version` | 1 | — |
| 7 | `tanks-ballistics` — shell entities, swept casts | 2 | — |
| 8 | `tanks-armor` — zones + penetration pipeline | 2 | — |
| 9 | `tanks-vision` — LOS/FOV/spotting, `TeamView` | 2 | — |
| 10 | `balance-harness` — headless 5×5 matrix (§13) | 2 — **build here, not last** | — |
| 11 | `tanks-ai` — the five layers | 2 | — |
| 12 | `tanks-match` — win conditions | 2 | — |
| 13 | `tanks-render` — Scene adapter + `TankStats -> Token` ChannelMap (§12.2) | 2 | — |

Slice 9 before slice 10 is deliberate: the harness needs *an* AI to run matches, but a trivial
"drive at the nearest enemy and shoot" AI is enough to start producing win-rate data — and having
that data on screen while building the real AI in slice 10 is worth more than having it after.
