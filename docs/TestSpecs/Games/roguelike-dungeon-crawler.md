---
title: "Hollow Depths"
slug: roguelike-dungeon-crawler
category: games
complexity: complex
genre: "Top-down action roguelike / twin-stick dungeon crawler"
target_session_minutes: 35
stack: { rendering: "FS.GG.Rendering (Skia/OpenGL)", arch: "Elmish/MVU", lang: "F#" }
status: spec
---

# Hollow Depths

## 1. Overview

**Hollow Depths** is a top-down, twin-stick action roguelike in the lineage of *The
Binding of Isaac* and *Enter the Gungeon*. You play a lone delver descending through a
procedurally assembled dungeon, one floor at a time. The core verb is **shoot while
dodging**: you steer with one hand and aim a stream of projectiles ("shots") with the
other, weaving between enemy bullet patterns in tight, hand-curated-feeling rooms that
are actually stitched together by a layout algorithm. Every room you clear, every
treasure you grab, and every shop you raid feeds a **run-based build** — a stack of
passive item modifiers and active synergies that can turn a starting peashooter into a
homing, piercing, screen-clearing instrument by Floor 5.

The fantasy is *mastery through accumulation under threat of total loss*. Death is
permanent within a run (permadeath): the run ends, the build evaporates, and you start
over from Floor 1 with a fresh seed. What carries over is **meta-progression** — a small
pool of permanent unlocks (new items, new characters, new starting conditions) earned by
hitting milestones. It's fun because no two runs are alike (seeded procedural generation
+ a deep item pool + emergent synergies), because the skill ceiling on dodging is high,
and because the build lottery creates "broken run" highs that you chase across dozens of
attempts.

## 2. Core Game Loop

**Moment-to-moment (combat, ~0.1–2 s decisions):**
`assess room → move to safe space → aim at threat → fire shots → dodge incoming bullets
→ reposition → repeat until room cleared`. Layered on top: pick up hearts/coins, decide
whether to take a hit to grab a pickup.

**Room-to-room (~30–90 s):**
`enter room → doors lock → clear all enemies → doors unlock → loot drops → choose exit
door → enter next room`. Non-combat rooms (treasure, shop, secret) interrupt the rhythm
with decisions instead of reflexes.

**Floor-to-floor (~4–7 min):**
`explore room graph → find treasure room (free item) → optionally find/afford shop →
find boss room → defeat boss → take floor reward → descend trapdoor to next floor`.

**Run-to-run (session, ~10–35 min):**
`start run (seed) → descend Floors 1..N → die OR beat final boss → tally stats →
award meta-progression unlocks → return to hub → start new run`.

```
                +-------------------- new seed ---------------------+
                v                                                   |
  TITLE -> HUB -> RUN START -> [ FLOOR LOOP ] -> BOSS -> DESCEND ---/
                                  ^      |              |
                                  |      v              v
                            ROOM LOOP  DEATH ------> RESULTS -> unlocks -> HUB
```

## 3. Controls & Input

Primary input is **keyboard + mouse** (WASD move, mouse aim). Full **gamepad** (twin
analog stick) support is a first-class alternative. A keyboard-only fallback (arrow-key
aiming) is supported but secondary.

| Action | Keyboard / Mouse | Gamepad | Input model |
|---|---|---|---|
| Move | `W` `A` `S` `D` | Left stick | Held; produces a normalized move vector |
| Aim | Mouse cursor position | Right stick | Continuous; aim vector = cursor−player (normalized) |
| Fire | Left mouse button **or** `↑↓←→` | Right trigger / fire while right stick deflected | Held = auto-fire at fire-rate cadence |
| Dodge roll | `Space` or `Shift` | `A` / South button | Edge-triggered (on key-down only) |
| Use active item | `E` or right mouse button | `RB` / Right bumper | Edge-triggered |
| Drop bomb | `Q` or `F` | `LB` / Left bumper | Edge-triggered |
| Interact (shop/pickup confirm) | `E` | South button | Edge-triggered, contextual |
| Map toggle | `Tab` | Back/Select | Edge-triggered (toggle) |
| Pause | `Esc` | Start | Edge-triggered (toggle) |

Input rules:
- **Move and aim are decoupled** (twin-stick): you can strafe left while firing right.
- Fire is **auto-repeat**: holding fire emits a shot every `1 / fireRate` seconds (see §4.3).
- With keyboard arrow-key aiming, the aim vector snaps to the 8-way direction of the held
  arrows; diagonal = two arrows held. Mouse/right-stick aiming is fully analog (360°).
- Dodge roll is **edge-triggered** and ignored while already rolling or on cooldown.
- All edge-triggered actions fire once per key-down transition; the model tracks a
  `PressedThisTick` set derived from `(currentKeys − previousKeys)`.

## 4. Mechanics (detailed)

All positions in **logical pixels** on a 1280×720 logical playfield (§6). The simulation
runs on a **fixed timestep** of `dt = 1/120 s` (§7, §13); all constants below are
expressed per-second and integrated by `dt`.

### 4.1 Movement (player)

- **Top speed:** `baseSpeed = 240 px/s`, modified by the `Speed` stat (§4.5). Effective
  `moveSpeed = baseSpeed * (1 + speedMul)`, clamped to `[120, 540] px/s`.
- **Acceleration model:** velocity lerps toward `targetVel = moveDir * moveSpeed` using
  `accel = 2400 px/s²` when input present and `friction = 3000 px/s²` when input is zero.
  Concretely each tick: `vel += clampMag(targetVel − vel, rate*dt)` where `rate` is accel
  or friction. This yields a snappy ~0.1 s to top speed and a short slide on release.
- **Collision:** player hitbox is a circle, `radius = 13 px`, centered slightly below
  the sprite center. Resolved against room walls (AABB tiles) and obstacles by axis-
  separated sweep (resolve X, then Y) so you slide along walls instead of sticking.
- **Diagonal normalization:** raw `(x,y)` move input is normalized so diagonal speed
  equals cardinal speed.

### 4.2 Dodge roll (i-frames)

- On activation: player gains **invincibility frames** for `iFrameDur = 0.40 s`, during
  which all enemy contact and bullet damage is ignored (pickups still collected).
- Roll grants a velocity impulse along the current move direction (or facing, if no move
  input) of `rollSpeed = 460 px/s`, decaying back to normal control over the roll's
  `rollDur = 0.45 s`.
- **Cooldown:** `rollCooldown = 0.90 s` measured from roll start. Cannot chain rolls.
- During the i-frame window the player **cannot fire** (commitment cost).

### 4.3 Shots (projectiles / "tears")

The player's projectile is the **shot**. Shot behavior is fully derived from player stats
(§4.5), enabling item synergies.

| Shot stat | Symbol | Base | Effect |
|---|---|---|---|
| Damage | `dmg` | `3.5` | HP removed per hit |
| Fire rate | `fireRate` | `2.5 /s` | Shots per second (cadence = `1/fireRate`) |
| Shot speed | `shotSpeed` | `420 px/s` | Travel velocity magnitude |
| Range | `range` | `1.6 s` | Lifetime in seconds; distance = `shotSpeed*range` |
| Shot size | `shotRadius` | `5 px` | Projectile + collision radius |
| Knockback | `kb` | `40` | Impulse applied to hit enemy |
| Shot count | `multishot` | `1` | Projectiles emitted per fire event |
| Pierce | `pierce` | `0` | Number of enemies a shot passes through |
| Bounce | `bounce` | `0` | Wall bounces before expiry |
| Homing | `homing` | `0` | Steering strength toward nearest enemy (0 = none) |

- **Spread:** when `multishot > 1`, shots fan across a `spreadDeg = 18°` arc centered on
  the aim vector (e.g. 3 shots → −9°, 0°, +9°).
- **Velocity inheritance:** shots inherit `0.25 ×` the player's current velocity (feels
  natural when strafing).
- **Lifetime:** a shot is destroyed when its age exceeds `range`, when it leaves the room
  bounds (unless `bounce` remains), or when it has hit `pierce+1` enemies.
- **Homing:** if `homing > 0`, each tick the shot's velocity direction is steered toward
  the nearest live enemy by up to `homing * 360 °/s`.

### 4.4 Combat & collision rules

- **Shot → enemy:** circle/circle overlap (`shotRadius + enemyRadius`). Applies `dmg`,
  knockback `kb` along shot velocity, and a 0.06 s hit-flash on the enemy. Decrements
  pierce; destroys shot if pierce exhausted (and no bounce left).
- **Enemy/enemyBullet → player:** circle/circle overlap with player hitbox. If player is
  **not** in i-frames and **not** in post-hit invuln, deal damage (§4.6), apply
  knockback `90 px/s` away from source, and grant **post-hit invuln** of `0.80 s`
  (distinct from roll i-frames; player flashes).
- **Contact damage:** melee/flying enemies that touch the player deal their `contactDmg`
  (typically `1` half-heart) on overlap, subject to the same invuln gating, with a
  per-enemy 0.5 s re-tick cap so standing in an enemy doesn't drain instantly.
- **Friendly fire:** player shots do not damage the player. Enemy bullets do not damage
  enemies (no infighting in v1).
- **Bombs:** explode `1.5 s` after drop (or on remote detonate via item), radius
  `90 px`, dealing `40` damage to enemies and `1` heart to the player if caught in blast
  (i-frames protect). Destroys destructible obstacles and can open secret-room walls.

### 4.5 Player stats & stacking modifiers

The build system is a **flat stat block** that items mutate. Each item declares zero or
more **modifiers** applied at pickup; the resulting `PlayerStats` is recomputed
deterministically from `baseStats + Σ modifiers`. Two modifier kinds:

- **Additive** (`Add`): `stat += value` (e.g. `+0.5 dmg`).
- **Multiplicative** (`Mul`): tracked as a running multiplier `stat *= (1+value)`.

Recompute order is fixed for determinism: start from base, apply **all** additive mods
(in pickup order), then **all** multiplicative mods (in pickup order), then clamp.
Fire-rate uses a special curve to avoid runaway DPS: internal stat `tearDelay` (frames
between shots) is modified instead, then `fireRate = 30 / max(1, tearDelay)`; `+fireRate`
items reduce `tearDelay`.

Clamps: `dmg ≥ 0.5`, `fireRate ∈ [0.7, 15] /s`, `shotSpeed ∈ [150, 900]`,
`range ∈ [0.4, 4.0] s`, `speedMul ∈ [−0.5, 1.25]`, `multishot ∈ [1, 12]`.

### 4.6 Hearts & health

- Health is measured in **half-hearts**. Player starts with `3` red hearts = `6`
  half-hearts. Max container default `3`, hard cap `12` containers.
- **Heart types:**
  - *Red* — current/max HP. Refilled by red heart pickups (half = +1, full = +2).
  - *Soul* (blue) — temporary HP layered on top of red; consumed first, not refillable as
    "max", lost on floor descent? No — persists. Caps total displayed at 12 hearts wide.
  - *Black* — like soul but on depletion triggers a small damage burst (synergy hook).
- **Damage:** a normal hit removes `1` half-heart (`2` for "double tap" enemies/bosses).
  Soul/black hearts are consumed before red.
- **Death:** when total half-hearts reach `0`, player dies → permadeath (§4.10).

### 4.7 Currency: coins, keys, bombs

Three currencies, each capped at `99`, displayed in the HUD:

- **Coins** — spent in shops; dropped by enemies (small chance) and from coin pickups.
  Start: `0`.
- **Keys** — open locked doors (treasure rooms sometimes, golden chests, locked shop
  items). Start: `1`.
- **Bombs** — placed via Drop Bomb (§4.4); also blast open secret walls and tinted rocks.
  Start: `1`.

Pickups drop from cleared rooms and destroyed obstacles per a weighted table (§4.9).

### 4.8 Procedural floor generation

Each floor is a **graph of rooms** placed on an integer grid of cells. Generation is a
pure function of the floor seed (§13).

**Algorithm (deterministic, seeded):**
1. **Seed derivation:** `floorSeed = hash(runSeed, floorIndex)`. A per-floor `Rng`
   (splittable PRNG) is created from `floorSeed`; all subsequent draws use it.
2. **Room budget:** `roomCount = round(7 + 1.6 * floorIndex + Rng.range(0,2))`, clamped
   to `[8, 20]`.
3. **Floor-plan walk (placement):** start at grid cell `(0,0)` = START room. Maintain a
   queue of placed cells. Repeatedly: pop a cell, for each of its 4 neighbors, with
   probability `p = 0.5` (and if neighbor empty and neighbor would have ≤ `maxNeighbors=`
   varies) place a new room there and enqueue it. Stop when `roomCount` rooms placed.
   This yields the classic "Isaac" branching organic layout. Reject and re-roll the whole
   walk if it produces fewer than `roomCount` rooms after a bounded number of passes.
4. **Special-room assignment** (on the placed graph):
   - The room with grid-distance farthest from START becomes the **BOSS** room (a
     "dead-end" / single-door room is preferred).
   - Exactly **1 TREASURE** room: placed on a dead-end, far from boss.
   - **SHOP**: 1 on floors ≥ 2, placed on a dead-end if available.
   - **SECRET** room: placed in an empty cell that is adjacent to the **most** existing
     rooms (≥ 2), revealed only by bombing an adjacent wall. Optional SUPER-SECRET on
     deeper floors (adjacent to exactly 1 room).
   - Remaining rooms are **COMBAT** rooms.
5. **Room interior population:** each combat/boss room picks a **room template** by type
   and floor theme from a template table, seeded; templates define obstacle layout and
   enemy spawn anchors. Enemy roster for each combat room is drawn from the floor's
   weighted enemy pool with a **threat budget** `budget = 6 + 2*floorIndex`; enemies are
   added until budget spent (each enemy has a threat cost).
6. **Door carving:** doors are placed between orthogonally adjacent placed rooms. Door
   visuals/locks set by neighbor types (boss door = special, treasure door if locked
   needs a key on some floors).

Determinism guarantee: same `runSeed` + `floorIndex` ⇒ byte-identical room graph, room
types, templates, and enemy placements (§14.1).

### 4.9 Pickups & drop tables

On room clear (combat) and on obstacle destruction, roll a weighted drop. Example
room-clear table (weights sum to 100):

| Outcome | Weight |
|---|---|
| Nothing | 45 |
| 1 coin | 22 |
| 3 coins | 8 |
| Half red heart | 12 |
| Key | 6 |
| Bomb | 5 |
| Soul heart | 2 |

Drops use the **per-floor RNG stream dedicated to drops** so combat outcomes don't perturb
layout determinism (separate sub-stream, see §13).

### 4.10 Permadeath & meta-progression

- **Permadeath:** on death, the run state is discarded. No mid-run saves, no continues.
  The only persisted artifact is the **meta-progression profile**.
- **Meta-progression** (persisted to disk, §13): a profile tracking:
  - `unlockedItems: Set<ItemId>` — items added to the global pool, unlocked by milestones.
  - `unlockedCharacters: Set<CharId>` — alternate starting stat blocks / starting items.
  - `bestFloor`, `bossKills`, `totalRuns`, `achievements`.
- **Unlock triggers (examples):** "Reach Floor 3" → unlock item *Cracked Lens*; "Defeat
  the Floor-1 boss 3 times" → unlock *Glass Cannon*; "Clear a run without taking damage on
  a floor" → unlock a character. Unlock checks run at end-of-run against run stats.
- A run can also be re-launched with an **explicit seed** (daily/shared seed) — same seed
  ⇒ same floors, but item *pool* still respects the player's unlocks (documented caveat).

## 5. Entities / Game Objects

Sizes in px (collision radius unless noted). HP in player-damage units. "Threat" = budget
cost for room population (§4.8).

### 5.1 Player

```fsharp
open FS.GG.Game.Core
// Positions/velocities live in the scaffold's collision-safe Geometry.Vec2 ({ Vx; Vy }, from
// src/<ProductDir>/Vec2.fs) — NEVER a record you label X/Y/Width/Height, which collide with
// Scene's Point/Rect. This is a type ABBREVIATION: it adds no labels, so nothing can collide.
type Vec2 = Geometry.Vec2

type Player =
  { Pos: Vec2; Vel: Vec2
    Facing: Vec2          // last aim direction
    Stats: PlayerStats    // derived from items
    Health: Health        // red/soul/black half-hearts
    Roll: RollState        // None | Rolling of since:float | Cooldown of until:float
    PostHitInvulnUntil: float
    FireCooldown: float
    Currency: Currency     // coins/keys/bombs
    ActiveItem: ActiveItem option
    ActiveCharge: int      // charges accumulated for the active
    Items: ItemId list }   // pickup-ordered passive items (for recompute)
```

### 5.2 Enemy roster

| Enemy | Radius | HP | Speed (px/s) | Threat | Contact dmg | Behavior summary |
|---|---|---|---|---|---|---|
| **Grub** | 12 | 6 | 70 | 1 | 1 | Wander/seek player, melee. Splits into 2 Maggots on death (floors ≥ 2). |
| **Maggot** | 9 | 3 | 110 | 1 | 1 | Fast erratic seek; short hop pauses. |
| **Spitter** | 14 | 10 | 40 | 2 | 1 | Stationary-ish; fires single aimed bullet every 1.8 s. |
| **Fly Swarm node** | 8 | 2 | 130 | 1 | 1 | Orbits a point; dives at player on a 2 s cycle. |
| **Charger** | 16 | 14 | 60 idle / 320 dash | 3 | 2 | Telegraphs (0.6 s wind-up), dashes in a straight line, recovers. |
| **Turret** | 18 | 18 | 0 | 3 | 1 | Fixed; fires 4-bullet cardinal burst every 2.2 s (rotates pattern). |
| **Caster** | 13 | 12 | 50 | 4 | 1 | Teleports every 4 s; casts a 6-bullet ring on arrival. |
| **Brute** | 22 | 40 | 45 | 6 | 2 | Slow tank; ground-pound shockwave when player within 80 px. |

**State machines (example — Charger):**
`Idle → (player within 260 px) → WindUp(0.6s) → Dash(until wall/0.8s) → Recover(0.7s) →
Idle`. WindUp shows a directional telegraph; Dash locks direction; collision with wall or
player ends Dash early.

**Spawn/destroy:** enemies are instantiated at template anchor points on room entry (room
not yet "active" — they animate in over 0.3 s, ungated). On death: hit-flash → death
particles → drop roll contribution → removed from list. Splitters enqueue child spawns.

### 5.3 Bosses (one per floor; pool grows by floor theme)

| Boss | HP | Phases | Signature patterns |
|---|---|---|---|
| **The Gnawer** (F1) | 220 | 2 | P1: charges + spawns Maggots. P2 (<50% HP): adds a 12-bullet spiral every 3 s. |
| **Hollow Choir** (F2) | 300 | 2 | Three linked casters; ring bursts that interleave; kill all within 4 s or they revive. |
| **The Maw** (F3) | 420 | 3 | Sweeping bullet "walls" with gaps; ground-pound; final phase adds homing orbs. |

Bosses use **bullet patterns** defined declaratively (emitter: count, arc, speed, spin
rate, cadence) so they're data-driven and testable. Boss room locks until boss dies, then
spawns the **floor reward** (a treasure-tier item) + trapdoor.

### 5.4 Projectiles

```fsharp
type Shot =
  { Pos: Vec2; Vel: Vec2; Age: float
    Dmg: float; Radius: float; Range: float
    PierceLeft: int; BounceLeft: int; Homing: float
    Owner: Owner }   // Player | Enemy
```
Player shots and enemy bullets share the structure (different `Owner`, color, collision
target). Enemy bullets ignore `pierce/homing` unless a boss pattern sets them.

### 5.5 Pickups, obstacles, doors

- **Pickup**: `{ Pos; Kind: PickupKind; }` where `PickupKind = Coin of int | Key | Bomb |
  Heart of HeartKind | Item of ItemId | Trapdoor`.
- **Obstacle**: `{ Pos; Size; Kind: Rock | TintedRock | Pot | Spikes | Pit }`. Rocks block
  movement and shots; pits block movement (not flying enemies) and shots pass over; spikes
  damage on contact.
- **Door**: `{ Side: N|S|E|W; State: Open | LockedClear | LockedKey | BossSealed;
  Target: RoomId }`.

## 6. World / Levels / Progression

- **Logical playfield:** 1280×720. A single **room** occupies the central play area
  `1160×600` with a `60 px` wall border. Tile grid: `40×40 px` tiles ⇒ playable interior
  ≈ `29×15` tiles. Doors sit at the midpoint of each wall.
- **Camera:** room-locked (no scrolling within a room); the whole room is always on screen.
  Room transitions slide the camera `560/620 px` over `0.35 s` to the adjacent room.
- **Floor structure:** Floors 1..`maxFloor` (`maxFloor = 6` in v1: 5 themed floors + 1
  finale). Each floor is one room graph (§4.8). Floor themes change palette, enemy pool,
  templates, and music.
- **Difficulty ramp (over floors):**
  - Enemy threat budget per combat room: `6 + 2*floorIndex`.
  - Enemy stat scaling: enemy HP `×(1 + 0.12*floorIndex)`, bullet speed `×(1 +
    0.05*floorIndex)`.
  - Room count grows (§4.8) and more "elite" enemies (Charger/Caster/Brute) enter the pool
    on deeper floors.
  - Boss HP scales per the boss table; later floors gate behind multi-phase bosses.
- **Progression gates:** you cannot reach the trapdoor without defeating the floor boss;
  the boss door only opens after you've cleared the room adjacent to it OR is always
  enterable but the boss room itself seals on entry (design choice: always enterable,
  seals on entry). Treasure room gives one free item per floor.

## 7. State Model (Elmish/MVU)

The challenge: a **bullet-heavy real-time sim** inside pure MVU. We resolve it with a
**fixed-timestep simulation tick** message that carries elapsed real time; the `update`
function is a pure `Model -> Model` advancing the sim by whole `1/120 s` steps. The view
is pure and stateless beyond the model. RNG lives **in the model** (serializable PRNG
state), never in `view` and never via ambient randomness — that is what makes runs
reproducible (§13).

### 7.1 Model (layered: run → floor → room → entities → player)

```fsharp
type GameScreen =
  | Title | Hub
  | Playing
  | Paused
  | GameOver of RunSummary
  | Victory of RunSummary

type RoomType = Combat | Treasure | Shop | Boss | Secret | SuperSecret | Start

type Room =
  { Id: RoomId
    Cell: int * int
    Type: RoomType
    Cleared: bool
    Visited: bool
    Enemies: Enemy list
    EnemyBullets: Shot list
    Pickups: Pickup list
    Obstacles: Obstacle list
    Doors: Door list
    Boss: Boss option }

type Floor =
  { Index: int
    Seed: uint64
    Theme: FloorTheme
    Rooms: Map<RoomId, Room>
    Graph: Map<RoomId, RoomId list>   // adjacency
    CurrentRoom: RoomId
    MapRevealed: Set<RoomId> }

type RunState =
  { RunSeed: uint64
    LayoutRng: Rng                 // sub-stream: layout/template (advanced only at gen)
    DropRng: Rng                   // sub-stream: drops/AI variance (advanced in combat)
    Floor: Floor
    Player: Player
    PlayerShots: Shot list
    Particles: Particle list
    FloorIndex: int
    Stats: RunStats                // floors, time, damage taken, kills (for unlocks)
    SimTime: float }               // accumulated simulated seconds

type Model =
  { Screen: GameScreen
    Run: RunState option           // Some while Playing/Paused
    Profile: MetaProfile           // persisted unlocks (loaded at boot)
    Input: InputState              // current + previous key/mouse/pad snapshot
    Accumulator: float             // leftover real time not yet simulated
    Settings: Settings }
```

### 7.2 Msg

```fsharp
type Msg =
  // time
  | Tick of dt: float              // real elapsed seconds from the subscription
  // input (edge + state captured into InputState)
  | InputChanged of InputState
  // navigation
  | StartRun of seed: uint64 option
  | EnterRoom of RoomId
  | DescendFloor
  | TogglePause
  | TitleAction of TitleCmd
  // run lifecycle (mostly internal, fired from update via Cmd-less transitions)
  | PlayerDied
  | RunCompleted
  // persistence
  | ProfileLoaded of MetaProfile
  | SaveProfile
```

### 7.3 update — important cases

- **`Tick dt`** (the heart): add `dt` to `Accumulator`; while `Accumulator ≥ FIXED_DT`
  (`= 1/120`), run **one** pure `stepSim FIXED_DT model` and subtract `FIXED_DT`. Clamp the
  number of steps per Tick to `MAX_STEPS = 5` (avoid spiral-of-death on lag). `stepSim`
  does, in order: read latched input → integrate player movement & roll → spawn player
  shots on fire cadence → integrate all shots (player + enemy) → run enemy AI & emit
  bullets → resolve collisions (shot→enemy, bullet/enemy→player) → apply damage & i-frame
  gating → process deaths/drops → check **room-clear** → update doors → advance particles
  → advance timers. **All randomness uses `DropRng` from the model and writes the advanced
  Rng back** — purity preserved.
- **`InputChanged`**: store new snapshot; compute `PressedThisTick` for edge actions. Pure.
- **`StartRun seed`**: derive `runSeed` (given or from a seed source captured once), build
  `LayoutRng`/`DropRng`, generate Floor 1 (§4.8) using `LayoutRng`, place player at START,
  set `Screen = Playing`.
- **`EnterRoom id`**: set `CurrentRoom`, mark visited/revealed, activate room (instantiate
  enemies from template), seal doors if it's an uncleared combat/boss room.
- **room-clear (inside stepSim, not a Msg):** when `Enemies = []` in current combat room
  and not already cleared → set `Cleared = true`, open doors, roll drop (`DropRng`), spawn
  reward if boss.
- **`DescendFloor`**: increment `FloorIndex`, derive next `floorSeed`, regenerate Floor,
  carry over `Player` (stats/items/health/currency) — **not** room state.
- **`PlayerDied`**: compute `RunSummary`, evaluate unlocks against `RunStats`, merge into
  `Profile`, `Screen = GameOver`, emit `SaveProfile` cmd.
- **`TogglePause`**: flip `Playing`↔`Paused`; while Paused, `Tick` does not call `stepSim`.

### 7.4 view

`view model dispatch` is **pure** and returns a render description (scene graph of draw
commands), which the Skia layer paints (§8). It reads only `Model`: current room entities,
player, shots, particles, HUD values, minimap, and the active screen overlay. No mutation,
no time, no RNG. The same Model always renders the same frame.

### 7.5 Subscriptions

- **Animation/tick sub:** a `requestAnimationFrame`-style timer dispatches `Tick dt` each
  frame (target 60 FPS render; sim is decoupled at 120 Hz via the accumulator). `dt` is
  real seconds since last frame, clamped to `≤ 0.1 s`.
- **Input sub:** keyboard/mouse/gamepad events captured into an `InputState` snapshot,
  dispatched as `InputChanged`. Polling the gamepad happens once per frame in the sub.
- **Persistence sub:** on boot, load `MetaProfile` → `ProfileLoaded`; `SaveProfile` writes
  it back (debounced).

## 8. Rendering (Skia 2D)

Coordinate system: logical 1280×720, origin top-left, +y down. A single world→screen
transform handles the room-transition camera slide.

**Layer / draw order (back to front):**
1. **Floor background** — themed tiled fill (`#1b1320` deep purple base for Floor 1),
   subtle vignette.
2. **Floor decals** — blood/scorch decals, pit graphics (`#0a0710`).
3. **Obstacles** — rocks `#5a4a6e`, tinted rocks `#6e5a4a`, pots, spikes `#8a8a9a`.
4. **Pickups** — coins `#f5c542`, keys `#d9b14a`, bombs `#2b2b2b`, hearts `#e8424f`
   (red) / `#4a78e8` (soul) / `#222` (black), item pedestals glow.
5. **Shadows** — soft ellipse `#00000040` under each entity.
6. **Enemies** — themed sprites; hit-flash overrides fill with `#ffffff` for 0.06 s.
7. **Player** — body + directional indicator for facing; flashes `#ffffff80` during
   post-hit invuln, semi-transparent (`alpha 0.5`) during roll i-frames.
8. **Projectiles** — player shots `#7fe3ff` with a soft glow; enemy bullets `#ff5a5a`.
9. **Particles** — death bursts, muzzle flash, bomb explosion (additive blend).
10. **HUD** (§9) — hearts row, currency, minimap, active-item charge, floor name.
11. **Screen overlays** — pause/game-over/title dim layer `#000000b0` + text.

- **Shapes vs sprites:** v1 may ship with **primitive-drawn** entities (circles/rounded
  rects via `SKPaint`/`SKCanvas`) so the spec is buildable without art; sprite atlas is a
  stretch (§15). Glows via blurred duplicate or `SKPaint.MaskFilter`.
- **Fonts:** bold pixel/condensed font for HUD numbers; a single UI font for screens.
- **Camera:** room-locked; the only camera motion is the room-transition slide
  (lerp over 0.35 s). Optional screen-shake (decaying offset) on bombs/boss hits.
- **Redraw strategy:** full redraw every frame (room-scale scene, a few hundred draw calls
  worst case — well within budget §13). No dirty-rect optimization needed at this scale.
- **Particles:** pooled; each is a colored circle/quad with velocity, lifetime, fade.
  Caps at `MAX_PARTICLES = 600`.

## 9. UI / HUD / Screens

**Screens:**
- **Title:** game logo, "Start Run", "Daily Seed", "Stats", "Quit". Shows total runs &
  best floor.
- **Hub** (optional v1): a single safe room showing unlock progress; "Begin Descent" door.
- **Playing:** the room + HUD overlay (below).
- **Paused:** dim overlay, "Resume / Restart / Quit", current build (item list) shown.
- **Game Over:** `RunSummary` — floor reached, time, kills, coins, items collected, any
  unlocks earned this run. "New Run (new seed)" / "Retry seed" / "Title".
- **Victory:** beat final boss — richer summary + special unlock.

**HUD layout (1280×720):**
- **Hearts:** top-left at `(24, 20)`, left-to-right, each heart `32×32`, soul/black after
  red. Empty containers shown as outlines.
- **Currency:** top-left under hearts at `(24, 60)`: coin/key/bomb icons + 2-digit counts.
- **Active item:** top-right `(1180, 20)`: item icon with a radial **charge meter**
  (filled arc = charges ready).
- **Minimap:** top-right under active `(1140, 70)`, `120×120`: room graph with current
  room highlighted, special-room icons (treasure/boss/shop) once discovered.
- **Floor name:** bottom-center, fades in for 2 s on floor entry (e.g. "I — The Burrows").
- **Pickup prompts:** contextual "[E] Buy 7¢" near shop items; item-pickup name + effect
  banner appears center-top for 2.5 s on grabbing a passive item.

Formatting: counts are right-aligned 2 digits (`07`, `99`). Time as `M:SS`.

### 9.1 Menu system (detailed)
A single **menu stack** drives every non-play screen (Title, Meta-progression, Settings,
Pause, Game Over, Victory). Each menu is a vertical list of rows with a cursor, so one small
update handler serves them all and navigation is identical everywhere.

**Menu tree**
```
Title ─┬─ New Run ─────────── StartRun (fresh seed) → Playing (§7.3)
       ├─ Continue ────────── only shown while a run is in progress (Run = Some)
       ├─ Daily Seed ──────── StartRun (Some dailySeed) — shared/ranked seed (§4.10)
       ├─ Meta-progression ── Hub / unlock progress (§4.10)
       ├─ Stats ───────────── Stats & Charts screen (§9.2)
       ├─ Settings ──────┬─ Difficulty     ◄ Easy · Normal · Hard ►
       │                 ├─ Master volume  ◄ 0 – 100 ►
       │                 ├─ Sound          ◄ On · Off ►
       │                 ├─ Window scale   ◄ 1× · 2× · Fit ►
       │                 ├─ Screen shake   ◄ On · Off ►
       │                 └─ Back
       └─ Quit

Pause ─┬─ Resume
       ├─ Abandon Run ────── discard the run (permadeath, §4.10) → Title
       ├─ Settings ──────── (same submenu; returns to Pause)
       └─ Quit to Title

Game Over / Victory ─┬─ New Run ──────── fresh seed, back to Floor 1
                     ├─ Retry Seed ───── re-launch the same runSeed (§4.10)
                     ├─ View Stats ───── Stats & Charts (§9.2)
                     └─ Title
```

**Navigation model**
- `MenuCursor: int` on the active menu; `↑`/`W` decrement, `↓`/`S` increment, both **wrap**.
- `Enter`/`Space` activates the current row; `Esc`/`Back` pops the stack (**Back**).
- **Cycler/slider rows** (Difficulty, Master volume, Sound, Window scale, Screen shake):
  `←`/`→` change the value in place; the row shows a right-aligned `◄ value ►` widget.
- Rendering reuses the §9 Title selector style: the selected row is inverted (white box,
  black text); non-selected rows are `#FFFFFF` on the dim overlay.

**Msg additions** (extend §7.2):
```fsharp
    | MenuUp | MenuDown              // move cursor (wraps)
    | MenuAdjust of dir:int          // -1 / +1 on a cycler/slider row
    | MenuActivate                   // Enter/Space on the current row
    | MenuBack                       // Esc — pop the menu stack
    | OpenStats | CloseStats         // enter / leave the Stats screen (§9.2)
```

Settings apply live and persist to the `MetaProfile` config file (§13): **Difficulty**
selects the §12 mode (Easy/Normal/Hard scaling `enemyHpScale`, `postHitInvuln`, and
`dropNothingWeight`); **Master volume**/**Sound** route to `Audio.setMasterVolume` (§10,
clamped `[0,1]`, muting requests `0.0`); **Screen shake** toggles the §8 optional
bomb/boss-hit shake.

### 9.2 Stats & charts screen
The Stats screen visualizes **the last run** and **lifetime** play. It reads a snapshot
(the run's `RunStats` and the persisted `MetaProfile`), never live sim, so it is a pure,
deterministic render reachable from Title, Game Over/Victory, and Pause. Chart-design
choices below follow the project dataviz conventions (form-first, validated colorblind-safe
categorical palette, single axis, identity by entity).

**Tracked per run** — extends the `Stats: RunStats` already on §7.1 `RunState`, accumulated
in `stepSim` (the `Tick` path, §7.3), snapshotted into `RunSummary` at run end
(`PlayerDied`/`RunCompleted`):

| Field | Type | Updated |
|-------|------|---------|
| `depthReached` | `int` | max `FloorIndex` reached; set on `DescendFloor` (§7.3) |
| `killsByType` | `Map<EnemyKind, int>` | incremented on each enemy death (§5.2) |
| `itemsFound` | `int` | passive items picked up (§4.9) |
| `coinsCollected` | `int` | coins gathered this run (§4.7 — this game's gold) |
| `runSeconds` | `float` | accumulated `SimTime` of live play (§7.1) |
| `damageDealt` / `damageTaken` | `float` | run totals (HP dealt / half-hearts lost) |
| `damageByFloor` | `(dealt:float * taken:float) list` | one running pair per floor (Chart 2) |
| `deathCause` | `DeathCause` | `Enemy of EnemyKind \| Trap \| Bomb`, set at death (§4.6) |
| `character` | `CharId` | the starting character/class for this run (§4.10) |

**Lifetime** — `LifetimeStats`, persisted inside `MetaProfile` (§4.10, §13): `runsPlayed`,
`deepestFloor` (the existing `bestFloor`), `wins`, `winRatePct` (derived `wins/runsPlayed`),
`totalKills`, `deathsByCause: Map<DeathCause, int>`, alongside the existing `unlockedItems`/
`unlockedCharacters` unlocks.

**Layout** (logical 1280×720): a KPI tile row across the top, two charts below.

```
┌───────────────────────────── STATS ─────────────────────────────┐
│  ┌ DEEPEST ┐ ┌ RUNS  ┐ ┌ WIN %  ┐ ┌ KILLS  ┐                    │  ← KPI stat tiles
│  │  Fl 9   │ │  128  │ │  14 %  │ │ 3,204  │                    │
│  └─────────┘ └───────┘ └────────┘ └────────┘                    │
│                                                                  │
│  Run-depth distribution           Damage per floor (last run)    │
│  ▇▇                            420 ┤          ╭── Dealt          │
│  ▇▇  ▇▇                            │      ╭─╯╭─╯                 │
│  ▇▇  ▇▇  ▇▇                        │   ╭─╯ ╭─╯                   │
│  ▇▇  ▇▇  ▇▇  ▇▇  ▇▇                │  ╭╯ ╭─╯ ── Taken            │
│  1-3 4-6 7-9 …  13+ (floors)     0 ┼──────────────► floor #      │
└──────────────────────────────────────────────────────────────────┘
     ↑/↓ scope:  ▸ This Run · Lifetime              ESC — Back
```

**Charts** (rendered in Skia with the same draw-list discipline as §8):

1. **Run-depth histogram** — *form: a distribution → bars.* x = deepest floor reached,
   bucketed (`1-3, 4-6, 7-9, 10-12, 13+`), y = number of past runs. **Single series**, so
   one hue and no legend. Bars are 4 px-rounded at the data end with a 2 px surface gap
   between them. Fill `#2a78d6` (light) / `#3987e5` (dark) — validated categorical slot 1.
2. **Damage per floor** — *form: change over an ordered index → line.* x = floor number of
   the last run, y = damage; **two series** (Dealt, Taken) → a legend is present and both
   lines are direct-labeled at their right end ("Dealt"/"Taken"). Dealt `#2a78d6`, Taken
   `#1baf7a` (slots 1–2, adjacent-pair CVD-validated), reading the survival margin per
   floor. 2 px lines, ≥ 8 px end markers, recessive 1 px gridlines in `#3C3C3C`.

Conventions honored: **color follows the entity** (Dealt is always slot 1, Taken always
slot 2 — never repainted by the scope toggle); **one axis only** (no dual-scale); chart
**text uses ink tokens** (`#FFFFFF` primary / `#C3C2B7` muted), never the series hue; layout
is **fixed and deterministic**, so a fixed-seed run (§13, §14.1) renders byte-identical for
snapshot tests. The `↑/↓` **scope** toggle swaps the data source This-Run ↔ Lifetime without
changing colors.

**Model/Msg hooks:** extend the existing `RunStats` (§7.1 `RunState.Stats`) with the fields
above and accumulate them in `stepSim` (§7.3 `Tick` path): bump `killsByType`/`damageDealt`
on shot→enemy resolution (§4.4), `damageTaken`/`damageByFloor` on player hits (§4.6),
`coinsCollected`/`itemsFound` on pickups (§4.7, §4.9), set `depthReached` on `DescendFloor`,
and set `deathCause`/`character` at death. On `PlayerDied`/`RunCompleted`, fold `RunStats`
into `MetaProfile` (increment `runsPlayed`, update `deepestFloor`, `wins`, `totalKills`,
`deathsByCause`) and persist via `SaveProfile` (§13). `OpenStats`/`CloseStats` switch a
`Screen`-adjacent Stats overlay carrying `scope: StatScope` (`ThisRun | Lifetime`); the
render reads the snapshot only and is a no-op on physics (§7.4).

## 10. Audio
Audio ships in v1 via the **`fs-gg-audio`** capability (`open FS.GG.Audio.Core`).
Sound is **requested as pure values**: `update` returns `AudioEffect` values alongside the
model change and never touches an audio device. A record-only interpreter
(`Audio.interpret`) folds the frame's requests into `AudioEvidence` — the requested effects
in dispatch order, volumes clamped to `[0.0, 1.0]` — so cues are **deterministic and testable
with no sound hardware**. `SoundId`/`TrackId` are opaque names this game owns; the host
resolves them to real assets (a real playback backend is deferred, so tests assert on
`AudioEvidence.Requested`, not on audio output).

**Cues** — each is an `AudioEffect` requested from `update` when the paired event fires:

| Event | Request | Id | Design intent |
|---|---|---|---|
| Player fires a shot (§4.3) | `Audio.playSfx (SoundId "shot-fire") 0.55` | `shot-fire` | soft "blip" (pitch varies ±5% per shot) |
| Shot hits enemy (§4.4) | `Audio.playSfx (SoundId "shot-hit") 0.7` | `shot-hit` | wet "thunk" |
| Enemy death (§5.2) | `Audio.playSfx (SoundId "enemy-death") 0.75` | `enemy-death` | "pop"/"squelch" |
| Player takes a hit (§4.6) | `Audio.playSfx (SoundId "player-hit") 0.9` | `player-hit` | sharp "ow"/thud + low sting |
| Player dies (§4.10) | `Audio.playSfx (SoundId "player-death") 1.0` | `player-death` | descending sting |
| Dodge roll (§4.2) | `Audio.playSfx (SoundId "dodge-roll") 0.6` | `dodge-roll` | whoosh |
| Coin pickup (§4.7) | `Audio.playSfx (SoundId "pickup-coin") 0.7` | `pickup-coin` | coin "ching" |
| Key pickup (§4.7) | `Audio.playSfx (SoundId "pickup-key") 0.7` | `pickup-key` | key "clink" |
| Bomb pickup (§4.7) | `Audio.playSfx (SoundId "pickup-bomb") 0.7` | `pickup-bomb` | bomb "thud" |
| Heart pickup (§4.6) | `Audio.playSfx (SoundId "pickup-heart") 0.7` | `pickup-heart` | heart "chime" |
| Passive item pickup (§4.9) | `Audio.playSfx (SoundId "item-pickup") 0.85` | `item-pickup` | triumphant "power-up" jingle |
| Bomb explosion (§4.4) | `Audio.playSfx (SoundId "bomb-explosion") 0.95` | `bomb-explosion` | boom + screen-shake |
| Door lock / room seal (§7.3) | `Audio.playSfx (SoundId "door-lock") 0.7` | `door-lock` | stone "grind" |
| Door unlock / room clear (§7.3) | `Audio.playSfx (SoundId "door-unlock") 0.7` | `door-unlock` | "clack" |
| Boss intro (§5.3) | `Audio.playSfx (SoundId "boss-intro") 1.0` | `boss-intro` | boss intro roar |
| Boss phase transition (§5.3) | `Audio.playSfx (SoundId "boss-phase") 0.9` | `boss-phase` | boss phase transition sting |
| Boss death (§5.3) | `Audio.playSfx (SoundId "boss-death") 1.0` | `boss-death` | big boom + slow-mo (0.4 s) |
| Trapdoor / floor descend (§4.8) | `Audio.playSfx (SoundId "floor-descend") 0.8` | `floor-descend` | "fwoomp" |

Background **music** loops per context: each floor's themed track starts on floor entry
(`Audio.playMusic (TrackId "floor-1-theme") true`), and every transition — descending to
the next floor, entering the shop or boss room, or ending the run — issues `Audio.stopMusic`
before requesting the next loop (title, shop, boss, per-floor, game-over/victory stingers),
so exactly one track plays at a time. A mute/settings toggle maps to `Audio.setMasterVolume`
(muting requests `Audio.setMasterVolume 0.0`). **Testing:** collect the frame's
`AudioEffect`s, `Audio.interpret` them, and assert the `AudioEvidence.Requested` sequence for
representative events (e.g. firing a shot requests exactly `PlaySfx (SoundId "shot-fire", _)`).

## 11. Win / Loss / Scoring

- **Win condition:** defeat the **final floor boss** (Floor 6). → `Victory` screen,
  victory unlock awarded.
- **Loss condition:** player half-hearts reach `0` → permadeath, `GameOver`. No continues,
  no extra lives (lives are not a mechanic; survival is the hearts pool).
- **Scoring (run score, for leaderboards/daily seed ranking):**
  - Base: `floorsCleared * 1000`.
  - Boss kills: `+2000` each.
  - Enemy kills: `+10` each.
  - Coins collected (lifetime in run): `+5` each.
  - Items collected: `+250` each.
  - **Speed bonus:** `max(0, 30000 − floor(time_s) * 20)`.
  - **No-hit floor bonus:** `+1500` per floor cleared without taking damage.
  - Final score is shown on Game Over / Victory and recorded per seed.
- Score is purely cosmetic/ranking; it does not affect meta-progression unlocks (those are
  milestone-based, §4.10).

## 12. Difficulty & Balancing

All tunables live in a single data record so balance is data-driven and testable.

| Parameter | Default | Range | Effect |
|---|---|---|---|
| `playerBaseSpeed` | 240 px/s | 150–360 | Player top speed |
| `iFrameDur` | 0.40 s | 0.2–0.8 | Roll invuln window |
| `rollCooldown` | 0.90 s | 0.4–2.0 | Time between rolls |
| `postHitInvuln` | 0.80 s | 0.4–1.5 | Mercy invuln after a hit |
| `baseDmg` | 3.5 | 1–10 | Starting shot damage |
| `baseFireRate` | 2.5 /s | 1–6 | Starting cadence |
| `baseShotSpeed` | 420 px/s | 250–700 | Shot travel speed |
| `baseRange` | 1.6 s | 0.8–3.0 | Shot lifetime |
| `startHearts` | 3 (6 half) | 1–6 | Starting containers |
| `enemyHpScale` | 0.12 /floor | 0–0.3 | Per-floor enemy HP growth |
| `bulletSpeedScale` | 0.05 /floor | 0–0.15 | Per-floor enemy bullet speed growth |
| `threatBudgetBase` | 6 | 2–12 | Room population at Floor 0 |
| `threatBudgetPerFloor` | 2 | 0–4 | Added budget per floor |
| `dropNothingWeight` | 45 | 0–80 | Stinginess of drops |
| `roomCountBase` | 7 | 5–12 | Floor size baseline |
| `maxFloor` | 6 | 3–10 | Run length |
| `bossHpScale` | per-table | — | Boss durability |

Difficulty modes (stretch-ready): Easy/Normal/Hard scale `enemyHpScale`, `postHitInvuln`,
and `dropNothingWeight`.

## 13. Technical Notes

- **Performance budget:** target **60 FPS render / 16.7 ms frame**. Per-room worst case:
  ≤ 30 enemies, ≤ 120 enemy bullets, ≤ 40 player shots, ≤ 600 particles. Collision is
  broad-phased with a coarse **uniform grid** (cell `64 px`) so shot↔enemy and
  bullet↔player are near-O(n). Total active objects per room comfortably under ~800; full
  redraw per frame fits the budget.
- **Fixed vs variable timestep:** **fixed** `FIXED_DT = 1/120 s` for the simulation
  (deterministic physics & bullets), driven by an **accumulator** fed by the variable
  render `Tick dt`. Render interpolation between sim steps is optional (v1 can render the
  latest sim state directly). `MAX_STEPS = 5` per frame guards the spiral of death.
- **Determinism / RNG seeding:** a **splittable, serializable PRNG** (e.g. PCG/xoshiro
  stored as `uint64` state in the Model). The run derives **independent sub-streams**:
  - `LayoutRng` — floor generation & templates only. Advanced solely during generation, so
    the layout is independent of how combat unfolds.
  - `DropRng` — drops, AI jitter, boss-pattern variance. Advanced during combat.
  Each floor derives its seeds via `splitmix(runSeed, floorIndex)`. Same `runSeed` ⇒
  identical floors and identical drop sequence **given identical player actions/timing**;
  layout alone is identical regardless of play (because it uses a separate stream). All
  randomness flows through the model — no `System.Random` ambient calls, no clock reads in
  `update`/`view`.
- **Persistence:** `MetaProfile` (unlocks, stats, best score per seed, settings) serialized
  to a single JSON file in the platform app-data dir. Run state is **not** persisted
  (permadeath; no mid-run save). Profile writes are debounced and atomic (temp-file +
  rename).
- **Edge cases:**
  - Generation that can't place enough rooms → bounded re-roll, then relax constraints.
  - No valid dead-end for a special room → place on least-connected available room; log.
  - Frame spikes / tab-out → `dt` clamp + `MAX_STEPS` keep sim stable; on resume, no
    catch-up burst beyond clamp.
  - Player at `0` hearts mid-step → death resolves at end of step (deterministic order).
  - Multishot + pierce + bounce combos must not infinite-loop: bounce decrements on each
    wall hit, pierce on each enemy; shot still expires by `range`.
  - Picking up an item while a banner is showing queues banners (no overlap).
  - Bomb opening a secret-room wall must update the door graph atomically.

## 14. Acceptance Criteria (test scenarios)

> All scenarios drive `stepSim`/generation as pure functions; assertions are on resulting
> `Model`. "Tick N times" means N fixed sim steps of `1/120 s`.

**14.1 — Procedural generation is deterministic for a seed.**
- **Given** `runSeed = 0xC0FFEE` and `floorIndex = 1`,
- **When** the floor is generated twice independently,
- **Then** both produce an identical room graph: same room count, same set of grid cells,
  identical `RoomType` assignment per cell, identical boss/treasure/shop/secret placement,
  and identical per-room enemy lists (type + spawn position). A byte-for-byte serialization
  of the two floors is equal.

**14.2 — Layout is independent of combat RNG.**
- **Given** two runs with the same `runSeed`,
- **When** in run A the player clears rooms quickly and in run B slowly (different numbers
  of `DropRng` draws),
- **Then** the **floor layout and enemy placement are identical** across both runs
  (because layout uses `LayoutRng`, a separate stream). Drops may differ; layout may not.

**14.3 — Item stat modifier stacks correctly.**
- **Given** a player with base `dmg = 3.5` who picks up *Cracked Lens* (`Add dmg +1.0`)
  then *Polyphemus Shard* (`Mul dmg +1.0`, i.e. ×2),
- **When** `PlayerStats` is recomputed (additives first, then multiplicatives),
- **Then** effective `dmg = (3.5 + 1.0) * 2.0 = 9.0`. Picking them up in the reverse order
  yields the **same** result (`9.0`), proving order-independence of the additive/multiplic-
  ative phases.

**14.4 — Multishot + spread produces the right projectiles.**
- **Given** a player with `multishot = 3`, aim vector pointing right (`(1,0)`), `spreadDeg
  = 18`,
- **When** the player fires once,
- **Then** exactly 3 `Shot`s spawn with velocity directions at `−9°, 0°, +9°` from the aim
  vector (within `0.01°`), each with the player's `shotSpeed`.

**14.5 — Room-clear gating opens doors only when cleared.**
- **Given** the player enters an uncleared combat room with 4 enemies (doors auto-seal to
  `LockedClear`),
- **When** fewer than all enemies are dead,
- **Then** all doors remain `LockedClear` and the player cannot exit;
- **And When** the last enemy dies,
- **Then** within the same step `Room.Cleared` becomes `true`, all doors transition to
  `Open`, and a room-clear drop is rolled from `DropRng`.

**14.6 — Damage applies and i-frames protect.**
- **Given** a player with `6` half-hearts and no active invuln, touching an enemy bullet,
- **When** the collision is resolved,
- **Then** health becomes `5` half-hearts, `PostHitInvulnUntil = SimTime + 0.80`, and
  knockback is applied;
- **And When** another bullet hits within the next `0.80 s`,
- **Then** **no** further damage is applied (still `5` half-hearts);
- **And Given** the player instead activates a dodge roll, **When** a bullet overlaps
  during the `0.40 s` i-frame window, **Then** no damage is applied.

**14.7 — Permadeath ends the run and evaluates unlocks.**
- **Given** a player at `1` half-heart who takes a `1`-damage hit (no invuln),
- **When** the step resolves,
- **Then** half-hearts reach `0`, `Screen` becomes `GameOver` with a populated
  `RunSummary`, `Run` is cleared on transition, and the unlock evaluator runs against
  `RunStats`; **And** if `RunStats.bestFloor ≥ 3` and *Cracked Lens* was not yet unlocked,
  the resulting `MetaProfile.unlockedItems` now contains it and a `SaveProfile` is emitted.

**14.8 — Fixed-timestep accumulator advances the sim correctly.**
- **Given** `Accumulator = 0` and `FIXED_DT = 1/120`,
- **When** a `Tick 0.033` (≈ 1/30 s) is processed,
- **Then** exactly `4` sim steps run (`floor((1/30) / (1/120)) = 4`) and `Accumulator` holds
  the remainder (`(1/30) − 4/120 ≈ 0.00 s`, within float epsilon);
- **And When** a single `Tick 1.0` arrives (huge stall), **Then** at most `MAX_STEPS = 5`
  steps run and the remainder is clamped (no spiral of death).

**14.9 — Input: twin-stick decoupling.**
- **Given** the player holds `A` (move left) and the mouse cursor is to the player's right,
- **When** firing,
- **Then** the player's velocity points left while spawned shots travel right (move and aim
  are independent); shots inherit `0.25×` the leftward velocity as the documented offset.

**14.10 — Shot lifetime/range terminates projectiles.**
- **Given** a shot with `shotSpeed = 420`, `range = 1.6 s`, `bounce = 0`, `pierce = 0`,
- **When** it travels unobstructed,
- **Then** it is destroyed when `Age > 1.6 s` (≈ `672 px` traveled), or earlier on leaving
  room bounds; and a shot with `pierce = 2` is destroyed after hitting its `3rd` enemy.

**14.11 — Currency & shop purchase.**
- **Given** a player with `10` coins in a shop, standing on an item priced `7¢`,
- **When** the player presses Interact (edge-triggered),
- **Then** coins become `3`, the item is added to `Player.Items`, stats recompute, and the
  shop slot is emptied; **And** with only `5` coins the purchase is rejected (coins
  unchanged, item remains).

## 15. Stretch Goals

Ranked, out of scope for v1:
1. **Active items & charges** fully fleshed out (e.g. room-clear bomb, teleport, brief
   slow-mo) with the charge meter already in the HUD.
2. **Item synergy graph** — explicit pairwise synergies (e.g. *Homing* + *Multishot* →
   "swarm", *Pierce* + *Bounce* → "ricochet net") with bespoke behavior, not just additive
   stats.
3. **Sprite/animation atlas** replacing primitive shapes; directional animations.
4. **Daily seed leaderboard** with shareable seed strings and online score submission.
5. **More floors, bosses, and a final-floor branching path** (alternate endings).
6. **Curse/blessing room modifiers** that alter a whole floor (darkness, extra elites for
   extra loot).
7. **Multiple playable characters** with distinct starting stats/items (meta-unlocked).
8. **Co-op (local 2-player)** twin-stick.
9. **Render interpolation** between fixed sim steps for ultra-smooth motion at high refresh.
10. **Mod/data-pack support** — items, enemies, room templates as external data files.
