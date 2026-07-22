---
title: "Hollow Depths"
slug: roguelike-dungeon-crawler
category: games
complexity: complex
genre: "Top-down action roguelike / twin-stick dungeon crawler"
target_session_minutes: 35
stack: { rendering: "FS.GG.Rendering (Skia/OpenGL)", framework: "FS.GG.Game.Core (FixedStep for the tick; Rng (split for sub-streams); SpatialGrid for the broadphase)", arch: "Elmish/MVU", lang: "F#" }
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

**Heart-type edge rules (additive):**
- **Consumption order** is always soul/black *before* red, and within the temporary layer
  black *before* soul — so red containers are the last thing you lose, and a black-heart
  burst fires before you start bleeding real HP.
- **Black-heart burst:** the half-heart that empties a black heart emits a room-wide sting
  dealing `10` damage to every live enemy in the current room (a mini-bomb: no self-damage,
  no knockback, no obstacle destruction) — the synergy hook this heart type exists for.
  Emptying two black hearts in one hit fires the burst twice.
- **Display / stacking cap:** red containers hard-cap at `12`; the soul+black layer stacks
  on top, but the **combined displayed row caps at 12 hearts (24 half-hearts) wide** —
  pickups past the cap are wasted (no overflow to coins in v1).
- **Pickup grain:** a half red heart heals `+1`, a full `+2`, each only up to current max
  containers; a container item adds `+1` container (`+2` red half-hearts, healed on grant).
  Overheal is wasted.
- **Double-tap** hits (bosses / tagged elites) remove `2` half-hearts in one resolution,
  still drawing from the soul/black layer first.
- **Persistence across descent:** every heart type — red, soul, black — carries unchanged
  through `DescendFloor` (§7.3); descent neither refills nor drains hearts.
- **Overkill / ordering:** a hit larger than remaining half-hearts still resolves to death
  (no negative HP); death is committed at end-of-step in the fixed order of §13 so
  simultaneous lethal events are deterministic.

### 4.7 Currency: coins, keys, bombs

Three currencies, each capped at `99`, displayed in the HUD:

- **Coins** — spent in shops; dropped by enemies (small chance) and from coin pickups.
  Start: `0`.
- **Keys** — open locked doors (treasure rooms sometimes, golden chests, locked shop
  items). Start: `1`.
- **Bombs** — placed via Drop Bomb (§4.4); also blast open secret walls and tinted rocks.
  Start: `1`.

Pickups drop from cleared rooms and destroyed obstacles per a weighted table (§4.9).

**Currency edge rules (additive):**
- **Caps are hard:** coins/keys/bombs each cap at `99`; a pickup that would exceed the cap
  is wasted (no conversion, no overflow), matching the heart cap of §4.6.
- **Key sinks:** keys open `LockedKey` doors, golden chests (treasure/shop, §4.11), and
  key-priced shop slots. A key is consumed on use; a locked object with no key is simply
  not interactable (the `[E]` prompt still shows the cost, §9).
- **Bombs are currency *and* weapon:** a dropped bomb (§4.4) is spent from the counter on
  drop, not on detonation. A bomb caught inside another bomb's blast **chain-detonates
  immediately** (same step), so a stack reads as one larger explosion; each still deals its
  own `40` enemy damage / `1` heart self-damage independently (i-frames protect).

### 4.8 Procedural floor generation

Each floor is a **graph of rooms** placed on an integer grid of cells. Generation is a
pure function of the floor seed (§13).

**Algorithm (deterministic, seeded):**
1. **Seed derivation:** `floorSeed = hash(runSeed, floorIndex)`. The per-floor generator is
   `Rng.ofSeed floorSeed` (`FS.GG.Game.Core`'s **`Rng`** — splitmix64, and splittable); all
   subsequent draws use it.
2. **Room budget:** `roomCount = round(7 + 1.6 * floorIndex + n)` where
   `let struct (n, rng') = Rng.nextInt 0 2 rng` (inclusive `[0, 2]`), clamped to `[8, 20]`. Write
   `rng'` back — every draw returns the advanced generator alongside the value.
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

**Obstacle-destruction drops.** Destroying a **pot** rolls its own, more generous table (a
pot is the classic "free stuff" object); a **tinted rock** (ore) rolls a leaner one; plain
**rocks**, **spikes**, and **pits** are either indestructible or drop nothing.

| Pot outcome | Weight | · | Tinted-rock outcome | Weight |
|---|---|---|---|---|
| Nothing | 40 | · | Nothing | 60 |
| 1 coin | 28 | · | 1 coin | 30 |
| Half red heart | 12 | · | Half red heart | 10 |
| 3 coins | 6 | · | | |
| Key | 6 | · | | |
| Bomb | 6 | · | | |
| Soul heart | 2 | · | | |

Obstacle drops draw from the same `DropRng` sub-stream as room clears (§13), so they belong
to the combat-variance stream, **not** the layout stream — bombing a pot never perturbs
where the next floor's rooms land (§14.2). At most one pickup spawns per destroyed object,
and there is no double-dip when a shot and a bomb destroy it in the same step (the first
resolver claims the roll).

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

### 4.11 Shops, item pedestals & the run item pool

Items are drawn from a **run item pool** = the base pool ∪ `Profile.unlockedItems` (§4.10).
Every item carries a **quality tier** (`0..3`) and one or more **pool tags** (`treasure`,
`shop`, `boss`) that decide which fixtures can offer it. All fixture contents are drawn from
**`LayoutRng` at floor-generation time** (§4.8, §13), so *what* a floor offers is
layout-deterministic and independent of how combat unfolds (§14.2); only *drops* (§4.9)
ride `DropRng`.

- **No dupes within a run:** once an item id is placed on a pedestal, stocked in a shop, or
  granted, it is removed from that run's available pool. If a tag's pool empties, the
  fixture falls back to a currency/consumable slot rather than repeating an item.
- **Treasure room** (§4.8): exactly one **free pedestal**, drawn from the `treasure` pool.
  Standing on it and pressing Interact grants the item (modifiers applied per §4.5) and
  locks the pedestal. Some floors gate the pedestal behind a **golden chest** costing `1`
  key (§4.7).
- **Shop** (floors ≥ 2, §4.8): `3` slots, each an item (`shop` pool) or a
  currency/consumable (heart / key / bomb / soul heart). Base prices — passive item `10¢`
  (`±3¢` by tier), single heart `3¢`, key `5¢`, bomb `5¢`, soul heart `6¢` — are fixed by
  `LayoutRng` at generation. **Purchase** (§14.11) deducts coins, applies the item, and
  empties the slot; insufficient coins reject it (slot unchanged). Shops **do not restock**
  within a floor — a fresh shop appears each floor; reroll is post-v1 (§15).
- **Boss floor reward** (§5.3): on boss death the room spawns one **free** item from the
  `boss` pool (also drawn from `LayoutRng`) beside the trapdoor.

Because every fixture draws from `LayoutRng`, two runs on the same `runSeed` offer identical
treasure/shop/boss items in identical positions at identical prices, regardless of combat
pace (§14.12) — the same guarantee §14.2 gives the layout.

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

**Behavior parameters (the one-line summaries, made precise).** All enemy bullets use a
base speed `enemyBulletSpeed = 180 px/s`, scaled per floor by §6's `bulletSpeedScale`.
- **Grub split:** on death on floors ≥ 2 it spawns `2` Maggots at ±`14 px` from its corpse;
  **split Maggots do not re-split**, and the split is free (already counted in the Grub's
  own threat, §4.8), so a room's population cannot balloon past its budget.
- **Spitter:** every `1.8 s`, a `0.3 s` muzzle telegraph then one bullet aimed at the
  player's *current* position (no lead) — punishes standing still, rewards strafing.
- **Fly Swarm node:** orbits its anchor at radius `36 px`; its dive commits toward the
  player's position at dive-start over `0.5 s`, then returns to orbit (read the wind-up,
  sidestep the commit).
- **Turret:** the 4-bullet cardinal burst rotates `+22.5°` each volley so the pattern
  precesses; a patient player rides the rotating gap.
- **Caster:** its `4 s` teleport picks a destination `≥ 120 px` from the player (drawn from
  `DropRng`) so it never lands on top of you; the `6`-bullet ring fires `0.2 s` after
  arrival, evenly spaced.
- **Brute ground-pound:** when the player is within `80 px`, a `0.5 s` telegraph then a
  shockwave ring expanding to `140 px` over `0.25 s`, dealing `2` half-hearts (double-tap,
  §4.6) outside i-frames with `160 px/s` knockback; `2.5 s` cooldown before it can pound
  again.

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
6. **Enemies** — drawn by `FS.GG.UI.Symbology` in the `Token` grammar (`Symbology.token`) from the
   §8.1 ChannelMap; hit-flash overrides fill with `#ffffff` for 0.06 s. A themed sprite atlas
   replaces the symbol layer as a stretch (§15) — see the shapes-vs-sprites note below.
7. **Player** — body + directional indicator for facing; flashes `#ffffff80` during
   post-hit invuln, semi-transparent (`alpha 0.5`) during roll i-frames.
8. **Projectiles** — player shots `#7fe3ff` with a soft glow; enemy bullets `#ff5a5a`.
9. **Particles** — death bursts, muzzle flash, bomb explosion (additive blend).
10. **HUD** (§9) — hearts row, currency, minimap, active-item charge, floor name.
11. **Screen overlays** — pause/game-over/title dim layer `#000000b0` + text.

- **Shapes vs sprites:** v1 ships **primitive-drawn** entities so the spec is buildable without
  art; sprite atlas is a stretch (§15). Glows via blurred duplicate or `SKPaint.MaskFilter`.
  For *enemies* that primitive layer is `FS.GG.UI.Symbology` (§8.1) rather than hand-rolled
  `SKPaint` circles — "legible abstract vector symbols, no art required" is the library's
  entire purpose, and it arrives with a linter that hand-rolled circles do not have. The
  player, pickups and obstacles stay hand-drawn primitives: `Symbology` is a **unit**
  vocabulary, and a coin is not a unit.
- **Fonts:** bold pixel/condensed font for HUD numbers; a single UI font for screens.
- **Camera:** room-locked; the only camera motion is the room-transition slide
  (lerp over 0.35 s). Optional screen-shake (decaying offset) on bombs/boss hits.
- **Redraw strategy:** full redraw every frame (room-scale scene, a few hundred draw calls
  worst case — well within budget §13). No dirty-rect optimization needed at this scale.
- **Particles:** pooled; each is a colored circle/quad with velocity, lifetime, fade.
  Caps at `MAX_PARTICLES = 600`.

### 8.1 Enemy symbology (the `Enemy → Token` ChannelMap)

The shapes-vs-sprites note above commits v1 to primitive-drawn entities so the game is buildable
without art. `FS.GG.UI.Symbology` is that commitment, already built and tested: a fixed channel set
(`Token`), three interchangeable grammars, and a **legibility linter** that scores a symbol set
against a per-channel capacity table. §5.2 already specifies the roster as exactly the stats the
channel set wants — radius, HP, speed, threat — so the per-game work is one **ChannelMap** and the
library draws it.

**Grammar: `Token`.** `Symbology.token` rotates the whole body by `Heading`. That is right here:
this is a twin-stick shooter where a Charger telegraphs a dash along a locked direction and a
Caster's ring burst radiates from a facing, so *which way a thing points* is information the player
acts on. `Badge` would flatten it to an edge indicator.

**Where the map lives.** `Symbology` depends on `Scene`, so this map belongs in
`FS.GG.Game.Render`, **never** in the sim that owns `Enemy` — `FS.GG.Game.Core` reaches up to
nothing (ADR-0022 §2).

**Normalising `Threat` is not enough — quantise it.** §5.2's `Threat` column is the §6 room-budget
currency (`6 + 2*floorIndex`), and it runs 1, 1, 2, 1, 3, 3, 4, 6 across the roster. `Token.Threat`
is a `float` in **0..1**, so `float e.Threat / 6.0` fixes the *domain* — and still leaves **five
distinct levels** on an `Ordered` channel whose capacity is **4**, so `Legibility.score` returns
`Warning / Threat : Threat overloaded: 5 distinct levels used, capacity 4`. Domain and capacity are
two different checks and passing the first does not buy the second. The four-tier map below scores
`Clean` over the whole roster. The linter is documenting a real readability limit: a player cannot
rank six threat levels at a glance mid-dodge, which is the same reason §6 spends threat as a budget
rather than showing the number.

**`Health` needs `MaxHp`.** `Token.Health` is a 0..1 fraction. §5.2's HP column is the max; the
live `Enemy` carries current `Hp`. Pass `Hp / MaxHp`, never `Hp`, or it is an out-of-domain `Error`.

**This map accepts one `Size` warning on purpose, and it is the interesting one.** §5.2's Radius
column has eight distinct values (8–22), and `Size` is `Ordered` with capacity **4**, so
`Legibility.score` returns `Warning / Size : Size overloaded: 8 distinct levels used, capacity 4`.
Do **not** quantise it away. `Symbology` treats `R` as a *channel* — a size the eye should rank —
but in this game the radius is **physics**: §4.3 resolves a shot against an enemy by circle/circle
overlap on `shotRadius + enemyRadius`, so the drawn symbol *is* the hitbox. Round a Charger's 16 px
to a shared "large" tier and the player is now dodging a circle that is not the thing that kills
them, which is the one unforgivable bug in a bullet-hell. Fairness outranks the linter here, and
`tower-defense` §8.1 quantises the identical channel precisely because its radii are decorative —
it resolves hits with `arriveEps`, not with the enemy circle. Same channel, opposite call, because
the underlying facts differ.

**Channels this game leaves at their default, stated plainly.** `Speed` is **not** mapped: §5.2's
movement is pattern-driven, not a single scalar the eye ranks — a Charger is "60 idle / 320 dash", a
Fly Swarm node orbits, a Caster teleports — so a speed pip would lie for half the roster, and the
*behaviour* is the read (§5.2's telegraph → commit → recover), not a number. `Shield` has no referent
(the roster carries contact damage, no armor concept), `TokenState` (`Confirmed`/`Suspected`) is a
spotted-vs-ghost distinction and this game shows the whole room (no fog), and `SecondaryHeading` needs
an independent turret this roster does not have — `Heading` already carries the single body facing.
Naming the absences is the point: a reader learns the channel set was considered and where it stops.

```fsharp
// The Enemy → Token ChannelMap. In a product this lives in FS.GG.Game.Render (ADR-0022 §2):
// Symbology depends on Scene, and the sim reaches up to nothing.
type Token = FS.GG.UI.Symbology.Token
type Klass = FS.GG.UI.Symbology.Klass
type Sigil = FS.GG.UI.Symbology.Sigil
type SymFaction = FS.GG.UI.Symbology.Faction
module Sym = FS.GG.UI.Symbology.Symbology

/// §5.2's Radius column — the roster's own sizing, reused rather than re-invented.
let radiusOf (k: EnemyKind) : float =
    match k with
    | Grub -> 12.0
    | Maggot -> 9.0
    | Spitter -> 14.0
    | FlySwarmNode -> 8.0
    | Charger -> 16.0
    | Turret -> 18.0
    | Caster -> 13.0
    | Brute -> 22.0

/// Body silhouette. The two stationary shooters read Heavy, the fast erratic ones Scout.
let klassOf (k: EnemyKind) : Klass =
    match k with
    | Brute | Turret -> Klass.Heavy
    | Maggot | FlySwarmNode -> Klass.Scout
    | _ -> Klass.Mobile

/// Identity mark. Paired with `klassOf` this separates all eight kinds while staying well inside
/// Klass's capacity-6 and Sigil's capacity-12.
let sigilOf (k: EnemyKind) : Sigil =
    match k with
    | Spitter | Turret | Caster -> Sigil.Bolt   // the shooters
    | Charger | Brute -> Sigil.Fang             // the chargers
    | _ -> Sigil.Ring

/// §5.2's Threat column (1..6) → four ranked tiers. `float threat / 6.0` would land in `Threat`'s
/// 0..1 domain but still put FIVE distinct levels on a capacity-4 channel — see §8.1.
let threatOf (e: Enemy) : float =
    if e.Threat <= 1 then 0.25
    elif e.Threat <= 2 then 0.5
    elif e.Threat <= 4 then 0.75
    else 1.0

let tokenOf (facing: Vec2) (e: Enemy) : Token =
    { Sym.defaultToken with
        Cx = e.Pos.Vx
        Cy = e.Pos.Vy
        R = radiusOf e.Kind                       // R > 0 or Size is a degenerate Error
        Heading = atan2 facing.Vy facing.Vx       // whole-body rotation in Grammar.Token
        Faction = SymFaction.Enemy
        Klass = klassOf e.Kind
        Sigil = sigilOf e.Kind
        Health = e.Hp / e.MaxHp                   // 0..1 fraction, NOT raw Hp
        Threat = threatOf e }
```

**Legibility as a test, not a hope — and `Clean` is the wrong assertion here.** The map is pure and
the roster is data, so "is this room readable" is an ordinary assertion over the §5.2 roster. But
per the `Size` note above this map is *deliberately* not `Clean`, so asserting `Verdict = Clean`
would fail on correct code and the next author would "fix" it by breaking the hitboxes. Assert the
shape you actually want — **no `Error`s at all, and no `Warning` other than the known `Size` one**:

```fsharp
module Legibility = FS.GG.UI.Symbology.Legibility

/// The §14 assertion. `Verdict = Clean` is deliberately NOT the check: the Size overload is a
/// consequence of R being the hitbox (§8.1), so it is pinned as accepted BY CHANNEL — any Error, or
/// any Warning on a channel other than Size, still fails.
let roomIsLegible (facing: Vec2) (room: Enemy list) : bool =
    (Legibility.score (room |> List.map (tokenOf facing))).Findings
    |> List.forall (fun f ->
        f.Severity <> Legibility.Severity.Error
        && f.Channel = Legibility.Channel.Size)
```

An accepted finding is pinned to its channel, so a *new* overload on any other channel still fails.
That matters more here than in a fixed-roster game, because §4.8 draws room contents from a seeded
`LayoutRng` — this turns "no seed produces an illegible room" into a property test. Note
`Legibility.Severity.Error` must be written qualified: a bare `Error` would shadow `Result.Error`
for every consumer that opens the module.

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

### 9.1 Menu & configuration — the shared game shell

Hollow Depths uses the **generic FS.GG game shell** (FS-GG/FS.GG.Rendering#991) — the same
menu/start screen and settings every FS.GG game shares — rather than a bespoke per-game menu.
The game supplies only its **name**, its **key→command map** (the rebindable actions from §3
Controls), and its play `update`/`view`; the shell provides everything below.

- **Main menu / start screen** — the game's name (**HOLLOW DEPTHS**) as the title label, with
  **Start**, **Config**, and **Exit**. The run-management entries — **New Run** (fresh seed),
  **Continue** (only while `Run = Some`), **Daily Seed** (shared/ranked seed) and
  **Meta-progression** (hub / unlock progress) — are game-specific rows shown alongside Start
  (§4.10).
- **`Esc` from gameplay** opens the pause menu (Resume · Config · Exit to menu) over the same
  shell; `Esc` again resumes. **Abandon Run** (discard the run, permadeath, §4.10) is a
  game-specific pause row, and the Game Over / Victory screen adds **Retry Seed** (§4.10).
- **Config / Settings**, all applied live and persisted (to the `MetaProfile` config file, §13)
  across restarts:
  - **Screen resolution** and **fullscreen** (windowed / borderless / fullscreen), driven
    through the SkiaViewer window-behavior + `LogicalCanvas` letterbox seam.
  - **Key rebinding** — the player remaps this game's controls (the §3 actions — move, aim,
    fire, dodge, active item, bomb, interact) via the `Controls.KeyRebind` UI over the
    `KeyboardInput.Keymap` mechanism; bindings persist via `KeymapCodec` (JSON), beside this
    game's other saved config (§13).
  - Game-specific rows are added as extra Config rows over the shell: **Difficulty** (the §12
    mode — Easy / Normal / Hard, scaling `enemyHpScale`, `postHitInvuln`, `dropNothingWeight`),
    **Master volume**/**Sound** (route to `Audio.setMasterVolume`, §10, clamped `[0,1]`, muting
    requests `0.0`), and **Screen shake** (toggles the §8 optional bomb/boss-hit shake). The
    menu, Esc routing, display settings, and rebind screen come from the shell.

The shell is pointer- and keyboard-navigable over the interactive Controls host (the
`fs-gg-skiaviewer` "game → pointer host" recipe). It is a shared dependency, so Hollow Depths
does **not** re-specify menu-stack/cursor/settings machinery of its own. The **Stats & charts**
screen (§9.2) is a Hollow-Depths-specific screen reached as a Config/menu row.

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

Concrete v1 modes, latched into `RunState` at `StartRun` (§9.1) so a run's scaling is fixed
and seed-replayable (§13); a mid-run change applies only to the next run (§14.13):

| Mode | `enemyHpScale` | `postHitInvuln` | `dropNothingWeight` | Also |
|---|---|---|---|---|
| Easy | 0.08 /floor | 1.10 s | 35 | `+1` starting container (§4.6) |
| Normal | 0.12 /floor | 0.80 s | 45 | the default column above |
| Hard | 0.18 /floor | 0.55 s | 55 | `+1` elite per combat room; no post-boss heal |

Normal reproduces the defaults; the mode only ever moves these knobs, never the seed, so a
daily seed (§4.10) stays comparable **within** a mode.

## 13. Technical Notes

- **Performance budget:** target **60 FPS render / 16.7 ms frame**. Per-room worst case:
  ≤ 30 enemies, ≤ 120 enemy bullets, ≤ 40 player shots, ≤ 600 particles. Collision is
  broad-phased with a coarse **uniform grid** (cell `64 px`) so shot↔enemy and bullet↔player are
  near-O(n) — that grid is `SpatialGrid.build 64.0`, queried with `SpatialGrid.queryRadius`; do not
  hand-roll the buckets. Total active objects per room comfortably under ~800; full redraw per frame
  fits the budget.
- **Fixed vs variable timestep:** **fixed** `FIXED_DT = 1/120 s` for the simulation (deterministic
  physics & bullets), drained by `FixedStep.drainWith (5.0 * FIXED_DT) FIXED_DT dt Accumulator` →
  `struct (steps, acc')`. Do not hand-roll the accumulator: the first argument *is* the
  `MAX_STEPS = 5` spiral-of-death guard, expressed as the frame-time budget the function takes. Render
  interpolation between sim steps is optional (v1 can render the latest sim state directly).
- **Determinism / RNG seeding:** a **splittable, serializable PRNG** — this is exactly
  `FS.GG.Game.Core`'s **`Rng`** (splitmix64, a `uint64` of state, a value), so use it rather than
  re-deriving one. `Rng.split` is what makes the sub-streams below real: it returns
  `struct (rng', branch)` — two generators that cannot perturb each other. The run derives
  **independent sub-streams**:
  - `LayoutRng` — floor generation & templates only. Advanced solely during generation, so
    the layout is independent of how combat unfolds.
  - `DropRng` — drops, AI jitter, boss-pattern variance. Advanced during combat.
  Each floor derives its seeds by `Rng.split`ting the run generator (`splitmix` *is* what `Rng` is).
  Same `runSeed` ⇒
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

**14.12 — Shop / treasure / boss contents are layout-deterministic and dupe-free.**
- **Given** two runs with the same `runSeed` in which combat unfolds differently (run A
  fast, run B slow — different `DropRng` draws),
- **When** each floor's treasure pedestal, shop slots, and boss reward are generated,
- **Then** both runs offer the **identical** item ids in identical positions at identical
  prices (contents ride `LayoutRng`, §4.11, extending §14.2), and **no item id appears
  twice** across a single run's pedestals/shops/boss rewards.

**14.13 — Difficulty mode latches at `StartRun` and scales the sim.**
- **Given** the player selects **Hard** in Settings (§9.1),
- **When** `StartRun` fires,
- **Then** the run latches `enemyHpScale = 0.18`, `postHitInvuln = 0.55 s`, and
  `dropNothingWeight = 55` (§12) into `RunState`;
- **And When** the player switches to **Easy** mid-run,
- **Then** the **active** run's scaling is unchanged (the switch applies to the next
  `StartRun` only), preserving seed-replay determinism (§13).

**14.14 — Secret room revealed by bombing an adjacent wall.**
- **Given** a bomb detonates (§4.4) against a wall segment adjacent to a hidden `Secret`
  cell (§4.8),
- **When** the blast resolves,
- **Then** within the **same step** a door is carved between the two cells, the `Secret`
  room becomes enterable, and the floor's door graph (`Floor.Graph`, §7.1) updates
  atomically — no half-open state where a door exists but the adjacency does not (§13
  edge case).

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

## 16. Milestone Roadmap

Implementation is sequenced into milestones; each item is a colored checkbox
tracking its status. Items reference the section that specifies them.

**Legend:** 🟥 Not started · 🟨 In progress · 🟩 Done · ⬜ Deferred (post-v1)

_All items start 🟥 (spec status). Flip an item to 🟨 when work begins and 🟩 once
its acceptance test(s) pass (§14)._

### M0 — Scaffold & fixed-step loop
- 🟥 Project scaffold: `Model`/`Msg`/`update`/`view` skeleton (§7)
- 🟥 Fixed 120 Hz sim via `FixedStep.drainWith`, `MAX_STEPS = 5` guard, banked accumulator (§7.3, §13) — AC #8
- 🟥 `Rng` (splitmix64) seeded, `LayoutRng`/`DropRng` sub-streams via `Rng.split` (§13)
- 🟥 Logical 1280×720 coordinate system + world→screen transform (§6, §8)

### M1 — Input & twin-stick control
- 🟥 `InputState` snapshot + `PressedThisTick` edge set `(currentKeys − previousKeys)` (§3, §7.3)
- 🟥 Keyboard/mouse + gamepad move & aim, fully decoupled (§3) — AC #9
- 🟥 Auto-repeat fire cadence + 8-way arrow-aim snap vs 360° analog aim (§3, §4.3)

### M2 — Movement, dodge & shots
- 🟥 Velocity lerp (`accel`/`friction`) + diagonal normalization, speed clamp (§4.1)
- 🟥 Axis-separated wall/obstacle sweep, circle hitbox `r = 13` (§4.1)
- 🟥 Dodge roll: i-frames, velocity impulse, `0.90 s` cooldown, fire lockout (§4.2)
- 🟥 Stat-derived shots (dmg/fireRate/shotSpeed/range/size) + velocity inheritance (§4.3)
- 🟥 Multishot `18°` spread fan centered on aim (§4.3) — AC #4
- 🟥 Shot lifetime/range, bounce, pierce & homing termination (§4.3) — AC #10

### M3 — Combat, health & currency
- 🟥 Shot→enemy circle overlap: `dmg`, knockback, hit-flash, pierce decrement (§4.4)
- 🟥 Enemy/bullet→player damage with i-frame + `0.80 s` post-hit invuln gating (§4.4, §4.6) — AC #6
- 🟥 Half-heart health (red/soul/black), damage resolution & death at `0` (§4.6)
- 🟥 Player stats recompute: additive-then-multiplicative phases + clamps (§4.5) — AC #3
- 🟥 Coins/keys/bombs currencies (cap `99`), bomb drop/blast & shop purchase (§4.4, §4.7) — AC #11
- 🟥 Contact damage on overlap: `contactDmg`, `0.5 s` per-enemy re-tick cap, knockback `90 px/s` (§4.4)
- 🟥 `SpatialGrid.build 64.0` broadphase for shot↔enemy / bullet↔player queries (§13)
- 🟥 Heart types: soul/black stacking, black-heart depletion burst, 12-wide display cap, descent persistence (§4.6)
- 🟥 Bomb chain-detonation + currency cap-overflow waste (`99` cap) (§4.7, §4.4)

### M4 — Procedural floor generation
- 🟥 Seed derivation `floorSeed = split(runSeed, floorIndex)` on `LayoutRng` stream (§4.8, §13) — AC #2
- 🟥 Room budget + branching placement walk with bounded re-roll (§4.8)
- 🟥 Special-room assignment: boss/treasure/shop/secret on the placed graph (§4.8)
- 🟥 Room interior population by template + threat budget `6 + 2*floorIndex` (§4.8)
- 🟥 Door carving between orthogonally adjacent rooms (§4.8) — AC #1
- 🟥 Secret / super-secret reveal by bombing an adjacent wall; atomic door-graph update (§4.8, §13) — AC #14
- 🟥 Floor descent: trapdoor spawns on boss clear, `DescendFloor` regenerates next floor & carries player, drops room state (§7.3, §4.8)

### M5 — Entities: enemies, bosses & rooms
- 🟥 Enemy roster + per-enemy state machines (e.g. Charger WindUp→Dash→Recover) (§5.2)
- 🟥 Boss phases & data-driven declarative bullet patterns (§5.3)
- 🟥 Room-clear gating: seal doors on entry, open + drop-roll on clear (§7.3) — AC #5
- 🟥 Weighted pickup/drop tables via `DropRng` sub-stream (§4.9)
- 🟥 Per-floor difficulty ramp: threat budget + enemy HP/bullet scaling (§6, §12)
- 🟥 Enemy behavior params: Brute ground-pound, bounded Grub split, Spitter/Turret/Caster/Fly patterns, enemy bullet base `180 px/s` (§5.2)
- 🟥 Obstacles: rock/tinted-rock/pot/spikes/pit collision, destructibles + drop tables via `DropRng`, spikes hazard, pit fly-over (§5.5, §4.1, §4.9)
- 🟥 Run item pool: treasure pedestal + boss floor-reward from `LayoutRng`, dupe-free per run (§4.11, §5.3) — AC #12
- 🟥 Shop room: item/consumable slots, `LayoutRng` pricing, key-locked items, no in-floor restock (§4.11, §4.7) — AC #11

### M6 — Rendering & enemy symbology
- 🟥 Back-to-front layer draw order (background → HUD → overlays) (§8)
- 🟥 `Enemy → Token` ChannelMap in `FS.GG.Game.Render`, `Symbology.token` grammar (§8.1)
- 🟥 Legibility linter assertion pinned to the accepted `Size` channel (§8.1)
- 🟥 Pooled particles (cap `600`) + room-transition camera slide `0.35 s` (§8)

### M7 — UI, menus & stats
- 🟥 HUD: hearts row, currency, active-item charge meter, minimap, floor name (§9)
- 🟥 Adopt the generic FS.GG game shell (FS-GG/FS.GG.Rendering#991): main menu (title + Start/Config/Exit), Esc pause routing, Settings with screen resolution + fullscreen, and in-game key rebinding of the §3 controls, persisted — the game provides its name + key→command map + play update/view; the shell provides the rest, no bespoke menu system (§9.1)
- 🟥 Game-specific rows over the shell (run management, difficulty mode, volume/sound, screen shake) apply live + persist to `MetaProfile` (§9.1, §12, §13)
- 🟥 Stats & charts screen: KPI tiles + depth histogram + damage-per-floor line (§9.2)
- 🟥 Difficulty-mode scaling table (Easy/Normal/Hard) latched at `StartRun` (§12, §9.1) — AC #13

### M8 — Audio
- 🟥 `AudioEffect` cues per event, `Audio.interpret` → `AudioEvidence.Requested` (§10)
- 🟥 Per-context music loop (one track at a time), volume clamp `[0,1]` + mute (§10)

### M9 — Win/loss & permadeath
- 🟥 Final-boss (Floor 6) defeat → `Victory` screen + unlock (§11)
- 🟥 Permadeath at `0` half-hearts → `GameOver`, run discarded (§11) — AC #7
- 🟥 Run-score tally + end-of-run meta-progression unlock evaluation (§11, §4.10)
- 🟥 `MetaProfile` JSON persistence: debounced, atomic temp-file+rename, load on boot (§13, §7.5)

### M10 — Acceptance & determinism
- 🟥 All 14 acceptance scenarios green (§14)
- 🟥 Procedural generation byte-identical for a seed (§14.1) — AC #1
- 🟥 Layout independent of combat RNG stream (§14.2) — AC #2
- 🟥 Shop/treasure/boss contents layout-deterministic & dupe-free (§14.12) — AC #12
- 🟥 Difficulty mode latches at `StartRun` and scales the sim (§14.13) — AC #13
- 🟥 Secret-room bomb-reveal updates the door graph atomically (§14.14) — AC #14
- 🟥 Seed + input-log replay is byte-identical given identical actions/timing (§13)

### Stretch — deferred (post-v1)
- ⬜ Active items & charges fully fleshed out with the HUD charge meter (§15.1)
- ⬜ Item synergy graph — bespoke pairwise synergies (§15.2)
- ⬜ Sprite/animation atlas replacing primitive shapes (§15.3)
- ⬜ Daily-seed leaderboard with shareable seeds + online submission (§15.4)
- ⬜ More floors, bosses & final-floor branching path (§15.5)
- ⬜ Curse/blessing room modifiers altering a whole floor (§15.6)
- ⬜ Multiple playable characters with distinct starts (§15.7)
- ⬜ Local 2-player co-op twin-stick (§15.8)
- ⬜ Render interpolation between fixed sim steps (§15.9)
- ⬜ Mod/data-pack support — external item/enemy/template data (§15.10)
