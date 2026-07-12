---
title: "Hollowveil"
slug: hollowveil
category: games
complexity: complex
genre: "Metroidvania platformer (action-exploration)"
target_session_minutes: 45
stack: { rendering: "FS.GG.Rendering (Skia/OpenGL)", framework: "FS.GG.Game.Core (FixedStep for the tick; Rng for determinism)", arch: "Elmish/MVU", lang: "F#" }
status: spec
---

# Hollowveil

## 1. Overview
**Hollowveil** is a 2D action-exploration platformer set in a single, continuous,
hand-authored subterranean world. The player is a small, agile revenant who awakens
at the bottom of a collapsed cistern with nothing but a worn blade. The core fantasy
is *earned mobility*: every region you cannot reach today becomes reachable tomorrow
because you found a new ability — a double jump, a horizontal dash, a wall-climb, a
grapple hook. The core verb is **move precisely and fight tightly**: tight,
sub-frame-tuned platformer physics make traversal itself feel good, and a compact
melee+ranged combat kit makes encounters readable and skill-based. It is fun because
the world is a lock-and-key puzzle the size of the whole map, and because the running,
jumping, and slashing are crisp enough to be satisfying on their own, second to second.

## 2. Core Game Loop
**Moment-to-moment (combat/traversal loop, ~1–10 s):**
`observe room → move/jump/dash → engage or avoid enemy → attack (melee/ranged) → dodge & i-frame through danger → collect currency/heal → reach next door → repeat`

**Exploration loop (~2–15 min):**
`enter new room → hit an ability gate you cannot pass → note it on the map → explore
elsewhere → defeat a mini-boss or find an ability pickup → backtrack via fast-travel
to the gate → unlock new region → find next gate`

**Session loop (~30–60 min):**
`rest at Save Vein (save + refill + respawn enemies) → push into unexplored zone →
find ability or fight boss → either bank progress at next Save Vein or die → on death,
respawn at last Save Vein and drop a recoverable currency cache at the death site →
return to recover cache → continue`

**Macro loop (full game, ~6–10 h):** acquire all 4 traversal abilities + 2 combat
upgrades, defeat both major bosses, reach the sealed core, win.

## 3. Controls & Input
Keyboard is primary. Gamepad bindings are listed for parity. Input model column states
whether an action reacts to **edge** (the frame the key transitions down/up) or to the
**held** state sampled each tick.

| Action            | Keyboard        | Gamepad            | Input model        | Notes |
|-------------------|-----------------|--------------------|--------------------|-------|
| Move left/right   | A / D           | Left stick / D-pad | Held (axis -1..1)  | Analog stick clamped to -1/0/+1 in v1 |
| Jump              | Space           | A                  | Edge (press) + held| Edge starts jump; held controls variable height |
| Dash              | L-Shift         | RB                 | Edge (press)       | Buffered; gated by Dash ability |
| Attack (melee)    | J               | X                  | Edge (press)       | Directional via held Up/Down |
| Ranged (Bolt)     | K               | Y                  | Edge (press)       | Consumes mana; gated by Bolt ability |
| Grapple           | L               | LB                 | Edge (press)       | Gated by Grapple ability |
| Look up / crouch  | W / S           | Up / Down          | Held               | Pans camera; crouch shrinks hitbox |
| Interact          | E               | A (contextual)     | Edge (press)       | Save Veins, levers, fast-travel |
| Open Map          | Tab             | Select             | Edge (press)       | Pauses sim; toggles map overlay |
| Pause             | Esc             | Start              | Edge (press)       | Pause menu |

**Input buffering:** Jump and Dash presses are buffered for **6 frames (0.1 s)**.
**Edge detection:** `update` receives discrete `KeyDown`/`KeyUp` messages; the model
holds a `Keys` set and per-action `pressedThisTick` flags cleared at end of each `Tick`.

## 4. Mechanics (detailed)
All constants are given in **logical pixels** and **seconds**. The simulation runs at a
**fixed 60 Hz** step (`dt = 1/60 ≈ 0.0167 s`). The player capsule is **20 × 36 px**
(crouched: 20 × 22 px). "px/s" = pixels per second; "px/s²" = acceleration.

### 4.1 Horizontal movement
- Max run speed: **240 px/s**.
- Ground acceleration: **1800 px/s²** (reaches max in ~0.13 s).
- Ground friction (no input): **2400 px/s²** decel to 0.
- Air acceleration: **1200 px/s²** (looser, retains momentum).
- Air friction (no input): **600 px/s²** (slight, preserves drift).
- Turn-around bonus: when input opposes velocity on ground, apply **2× ground accel**
  for snappy direction changes.

### 4.2 Jump, gravity, and assists
- Gravity (rising): **2000 px/s²**.
- Gravity (falling): **2600 px/s²** (heavier fall = less floaty, classic feel).
- Jump impulse (initial up velocity): **620 px/s** → apex height ≈ **96 px** (~6 tiles).
- Max fall speed (terminal): **900 px/s**.
- **Variable jump height:** while Jump is held and `vy < 0`, rising gravity applies.
  On Jump release while still rising, set `vy = max(vy, -180 px/s)` (cut the jump short).
  Min tap-jump height ≈ **24 px**.
- **Coyote time:** **6 frames (0.1 s)** after leaving a ledge, a ground jump is still
  allowed.
- **Jump buffering:** a Jump press within **6 frames (0.1 s)** before landing triggers a
  jump on the landing frame.
- **Apex hang:** when `|vy| < 60 px/s` near apex, apply gravity at **0.5×** for **2
  frames** to add a brief floaty hang (improves air control).

### 4.3 Wall slide & wall jump (always available)
- Wall contact + horizontal input toward wall + airborne ⇒ **wall slide**: clamp fall
  speed to **120 px/s**.
- **Wall jump:** Jump while wall-sliding ⇒ `vy = -560 px/s`, `vx = ±300 px/s` away
  from wall. For **8 frames (0.13 s)** after a wall jump, horizontal input is at **40%**
  authority (prevents instantly re-hugging the wall — enforces arc).

### 4.4 Dash (ability-gated)
- Distance: **140 px** over **0.18 s** (≈ **780 px/s** constant horizontal velocity).
- During dash: gravity is **disabled**, `vy` forced to 0 (flat dash).
- **I-frames during dash:** first **10 frames (0.167 s)** grant invulnerability.
- Cooldown: **0.35 s** after dash ends before a new ground dash. **Air dash:** one per
  airborne period; refreshed on landing or wall touch.
- Dash cancels: landing, wall contact, or hitstun ends a dash early.

### 4.5 Grapple (ability-gated)
- Fires a hook **up to 320 px** in the aim direction (8-way; defaults to facing+up).
- On hitting a **Grapple Node** (designated anchor tiles), the player is reeled at
  **600 px/s** toward the node, then released into a momentum-preserving arc.
- If it hits a grappleable enemy, pulls the player toward it (gap-close).
- Cooldown: **0.25 s**. Max one active hook.

### 4.6 Combat — melee
- Melee swing: hitbox **34 × 28 px** in front of player (or above/below if Up/Down
  held), active for **6 frames (0.1 s)**, startup **3 frames**, recovery **7 frames**.
- Base damage: **10**. Combo: 3rd consecutive hit within 0.6 s deals **15**.
- **Hitstop:** on a successful hit, freeze both attacker and target for **3 frames** for
  impact feel.
- **Pogo:** a Down-melee that connects with an enemy/hazard bounces the player up
  (`vy = -520 px/s`) and refreshes air dash. Core traversal tech.
- Knockback to enemy: **160 px/s** away from player, decaying over 0.2 s.

### 4.7 Combat — ranged (Bolt)
- Bolt projectile: **8 × 8 px**, speed **520 px/s**, lifetime **1.2 s** or until
  collision. Damage **8**. Costs **12 mana** (see 4.9).
- Max 4 bolts live simultaneously (cap).
- Bolts pass through terrain? No — destroyed on solid tile. Pass through hazards: no.

### 4.8 Damage, i-frames, hitstun (player)
- On taking damage: lose HP, enter **hitstun** for **8 frames (0.133 s)** (input locked
  except cancel into dash if Dash owned and off cooldown), apply knockback
  **200 px/s** away from source + small upward pop **-120 px/s**.
- **Invulnerability frames after being hit:** **60 frames (1.0 s)**; player sprite
  flashes at 12 Hz. Damage and contact are ignored during i-frames.
- Dash i-frames (4.4) and hit-i-frames are tracked as a single `invulnUntil` timer
  (whichever is later wins).

### 4.9 Health & Mana
- **Health:** starts at **50 HP**, shown as 5 masks × 10 HP. +1 mask (max **+5**) per
  **4 Vessel Shards** (4 shards = 1 mask). Cap **100 HP** at full upgrade.
- **Mana (Veil):** pool of **100**, regenerates **8/s** out of combat (no damage dealt
  or taken for 1.5 s), **0/s** in combat. Melee hit grants **+6 mana** on connect
  (encourages aggression). Bolt costs ~ see 4.7 (1 mana = 1 unit; bolt = 12 mana).
- **Focus heal:** hold Interact (E) while grounded and still for **0.8 s** to spend
  **33 mana** and heal **10 HP (one mask)**. Interrupted by damage or movement.

### 4.10 Currency & upgrades
- Currency: **Embers**, dropped by enemies (2–25 each) and found in caches.
- **Death cache:** on death, all carried Embers spawn as a recoverable **Shade**
  marker at the death location; touching it returns the Embers. Dying again before
  recovery destroys the previous Shade (only one exists).
- Spend Embers at **Vendor (the Tinker)** rooms on: extra mask (Vessel Shards are found,
  but masks assembled here), Bolt damage +, dash cooldown -, map markers, etc.

### 4.11 Ability gates (lock-and-key)
Each traversal ability is the key to a class of obstacle:
- **Double Jump** → clears **128 px** vertical gaps / high ledges.
- **Dash** → crosses **140 px** horizontal pits & dash-only narrow gaps; breaks
  **dash-crystal** barriers.
- **Wall-Climb** (upgrades wall slide into climb at **180 px/s** up) → ascends tall
  shafts with no footholds.
- **Grapple** → traverses grapple-node chasms and ceilings; pulls heavy levers.
Gates are authored as tile/region metadata: a room edge is `Locked(ability)` until the
ability is owned. The map records locked edges so the player can plan backtracks.

## 5. Entities / Game Objects
Every entity is a discriminated-union variant in a flat `Entity` array per active room
(see §7). Sizes in px, HP, speeds in px/s.

### 5.1 Player
- Size 20×36, HP 50–100, see §4 for all behavior.
- State machine: `Idle | Running | Jumping | Falling | WallSlide | Dashing |
  AttackingMelee | FiringBolt | Grappling | Hitstun | Focusing | Dead`.

### 5.2 Enemy roster
| Name        | Size (px) | HP  | Move (px/s) | Damage | Behavior / state machine | Drop (Embers) |
|-------------|-----------|-----|-------------|--------|--------------------------|----------------|
| Crawler     | 24×16     | 15  | 60          | 5      | Patrol ledge, turn at edge/wall; no aggro | 2–4 |
| Lunger      | 28×28     | 25  | 0 / 360 dash| 10     | Idle → telegraph 0.4 s → lunge 0.25 s → recover 0.6 s | 5–8 |
| Floater     | 22×22     | 12  | 80 (hover)  | 6      | Sine bob, drifts toward player; pops into 3 bolts on death | 4–6 |
| Slinger     | 26×30     | 20  | 40          | 7 (proj)| Keep distance; fire 200 px/s arc shot every 1.5 s | 6–10 |
| Sentinel    | 36×40     | 60  | 90          | 12     | Shielded front (melee blocked); flank or pogo to hit; charges | 12–18 |
| Spore Pod   | 20×20     | 1   | 0           | 8      | Stationary; bursts into damaging cloud (radius 40 px, 0.5 s) | 0 |
| Wraith      | 26×34     | 35  | 140         | 9      | Phases through walls toward player; tangible only 1 s windows | 10–15 |

Enemy AI types (referenced above):
- **Patroller** (Crawler): waypoint/edge-bounded walk, no player awareness.
- **Ambusher** (Lunger): proximity trigger (aggro radius **160 px**) → telegraph →
  commit → recover. Cannot turn mid-lunge.
- **Drifter** (Floater): steers velocity toward player at capped accel; ignores terrain.
- **Ranger** (Slinger): maintains **standoff distance 180–260 px**; strafes; shoots.
- **Bruiser** (Sentinel): directional defense + charge attack on **240 px** line of
  sight; vulnerable from behind/above.

### 5.3 Hazards & interactables
- **Spike tile:** 16×16, deals **15** + knockback, respawns player at last safe ground
  if it would kill (no instant-death insta-loss; HP loss only).
- **Save Vein:** interactable; saves game, refills HP/mana, sets respawn point, respawns
  room enemies.
- **Lever / Gate door:** toggled by Interact or by hitting with melee/bolt.
- **Grapple Node:** anchor for grapple.
- **Ability Pickup:** floating relic; on touch, grants ability + plays acquire sequence.
- **Vessel Shard / Ember Cache:** pickups.

### 5.4 F#-flavored type sketch
```fsharp
open FS.GG.Game.Core
// Positions/velocities live in the scaffold's collision-safe Geometry.Vec2 ({ Vx; Vy }, from
// src/<ProductDir>/Vec2.fs) — NEVER a record you label X/Y/Width/Height, which collide with
// Scene's Point/Rect. This is a type ABBREVIATION: it adds no labels, so nothing can collide.
type Vec2 = Geometry.Vec2

type AbilityId = DoubleJump | Dash | WallClimb | Grapple | Bolt

type EnemyKind = Crawler | Lunger | Floater | Slinger | Sentinel | SporePod | Wraith

type EnemyAi =
    | Patroller of leftBound: float * rightBound: float * dir: int
    | Ambusher  of aggroR: float * phase: AmbushPhase
    | Drifter   of accel: float
    | Ranger    of minD: float * maxD: float * cooldown: float
    | Bruiser   of charging: bool

type Entity =
    | EnemyE of Enemy
    | ProjectileE of Projectile      // bolts, slinger shots
    | PickupE of Pickup
    | HazardE of Hazard
    | ShadeE of embers: int          // death cache

and Enemy =
    { Kind: EnemyKind; Ai: EnemyAi; Pos: Vec2; Vel: Vec2
      Hp: int; FacingRight: bool; HitFlashUntil: float; State: EnemyState }
```

## 6. World / Levels / Progression
- Logical resolution **1280×720**. **Tile size 16×16 px**. A room is a tilemap of
  W×H tiles; camera shows ~80×45 tiles at 1:1 (rooms may be larger and scroll).
- **World structure:** one interconnected world of **6 zones**, ~**40 rooms** total,
  joined by door edges. The world graph is authored; zones gate by ability.

| Zone | Theme | Entry gate | Key content |
|------|-------|-----------|-------------|
| Z1 Cistern | tutorial caves | none (start) | basic platforming, Crawlers, first Save Vein |
| Z2 Hollow Roots | vertical shafts | none | **Double Jump** pickup, Lungers |
| Z3 Ashen Galleries | wide horizontal | Double Jump | **Dash** pickup, Slingers, **Boss A: The Warden** |
| Z4 Weeping Climb | tall wet walls | Dash | **Wall-Climb** pickup, Wraiths |
| Z5 Spindle Reach | open chasms | Wall-Climb | **Grapple** pickup, Sentinels |
| Z6 The Sealed Core | finale | Grapple | **Boss B: Veil Echo**, ending |

- **Difficulty ramp:** enemy HP and density rise zone to zone; later zones combine AI
  types (e.g. Slinger + Sentinel rooms). Backtracking with new abilities makes early
  zones trivial (intended power-fantasy payoff) but adds optional hard rooms behind
  late gates inside early zones.
- **Fast-travel:** designated **Vein Gates** (a subset of Save Veins, one per zone)
  form a fast-travel network unlocked as each is visited; map menu lets you warp
  between any visited Vein Gates.

## 7. State Model (Elmish/MVU)
The state is **layered**: a top `Model` composes `World`, the active `Room`, the
`Player`, and per-room `Entities`. Persistent progress (`Save`) is separated from
transient per-room simulation so saving = serialize `Save`, and entering a room =
rebuild transient state from authored room data + `Save` flags.

### 7.1 Model (record sketch)
```fsharp
type Mode = Title | Playing | Paused | MapOverlay | GameOver | Win

type Save =
    { Abilities: Set<AbilityId>
      MaxHp: int; Embers: int
      VesselShards: int
      VisitedRooms: Set<RoomId>
      VisitedVeinGates: Set<RoomId>
      OpenedDoors: Set<DoorId>           // levers/gates already toggled
      DefeatedBosses: Set<BossId>
      CollectedPickups: Set<PickupId>    // so they don't respawn
      RespawnRoom: RoomId; RespawnPos: Vec2
      Shade: (RoomId * Vec2 * int) option }   // death cache

type Player =
    { Pos: Vec2; Vel: Vec2; FacingRight: bool
      Hp: int; Mana: float
      State: PlayerState
      Grounded: bool; OnWall: WallSide option
      CoyoteTimer: float; JumpBufferTimer: float
      InvulnUntil: float; DashCooldownUntil: float; AirDashAvailable: bool
      JumpHeld: bool }

type Room =
    { Id: RoomId; Tiles: TileLayer
      WidthTiles: int; HeightTiles: int   // NOT Width/Height — those collide with Scene.Rect
      Doors: Door list; SpawnTable: EnemySpawn list
      GrappleNodes: Vec2 list; Hazards: Hazard list }

type Camera = { Pos: Vec2; Mode: CameraMode; Shake: float }

type Model =
    { Mode: Mode
      Save: Save                         // persistent
      Room: Room                         // active authored room data
      Player: Player
      Entities: Entity[]                 // transient per-room sim
      Camera: Camera
      Keys: Set<Key>; Pressed: Set<Action>   // input
      Time: float                        // accumulated sim seconds
      Boss: BossState option
      Rng: Rng }                         // FS.GG.Game.Core; seeded (§13)
```

### 7.2 Msg (DU)
```fsharp
type Msg =
    | Tick of dt: float                  // fixed 1/60 from subscription
    | KeyDown of Key | KeyUp of Key
    | InteractPressed
    | EnterRoom of RoomId * fromDoor: DoorId
    | AbilityAcquired of AbilityId
    | SaveAtVein of RoomId
    | FastTravel of RoomId
    | PlayerDied
    | BossPhaseChanged of BossId * int
    | ToggleMap | TogglePause
    | NewGame | LoadGame | QuitToTitle
```

### 7.3 update (key transitions)
- `Tick dt` (the workhorse): advances the **fixed-step simulation** in order:
  1. fold buffered input into player intent; decay timers (coyote, buffer, invuln).
  2. integrate player physics (§4): accel, gravity, variable jump, wall logic, dash.
  3. resolve player↔tile collision (swept AABB, axis-separated X then Y).
  4. step every entity AI (§5.2) and integrate enemy/projectile motion.
  5. resolve combat: melee/bolt hitboxes vs enemies; enemy/hazard vs player + i-frames.
  6. process pickups, doors, Save Veins, death-cache touch.
  7. detect room-edge crossing → emit `EnterRoom`; detect death → `PlayerDied`.
  8. step boss FSM, possibly emit `BossPhaseChanged`.
  9. update camera (room-lock clamp + smooth follow + shake decay).
- `KeyDown/KeyUp`: update `Keys`; set `Pressed` for edge actions (with buffer timers).
- `EnterRoom`: load authored room, spawn enemies from `SpawnTable` minus
  `CollectedPickups`/defeated, place player at the destination door, reset transient.
- `AbilityAcquired`: add to `Save.Abilities`; this immediately re-evaluates gate edges.
- `SaveAtVein`: write `Save` to persistence, refill HP/mana, set respawn, respawn room.
- `PlayerDied`: stash `Shade`, reset player at `RespawnRoom/Pos`, restore from `Save`.
- `BossPhaseChanged`: swap boss attack pattern set (see §11/boss specs).

### 7.4 view (pure)
`view` is a **pure** projection from `Model` to a render-command list (no mutation, no
drawing). It emits ordered draw commands (§8) that the Skia host executes. It reads
`Camera` to offset world-space, `Player.State`/timers for sprite frame + flash, and
`Mode` to overlay HUD/map/pause. No game logic lives in `view`.

### 7.5 Subscriptions
- **Tick subscription:** a 60 FPS loop. To keep simulation deterministic, the
  subscription uses a **fixed-timestep accumulator**: real frame `Δ` accrues; while
  `acc ≥ 1/60`, dispatch `Tick (1/60)` and subtract. Render once per real frame. This
  decouples render rate from sim rate.
- **Input subscription:** OS keyboard events → `KeyDown`/`KeyUp`.

### 7.6 MVU scaling: the real-time tradeoff (important)
A Metroidvania is a heavy real-time sim: ~30–40 entities, swept collision, boss FSMs,
all at 60 Hz. Pushing **all** of that through a strictly pure, allocation-per-frame
`update : Msg -> Model -> Model * Cmd` is the main architectural risk.

Recommended approach — **keep MVU at the boundary, isolate the hot loop:**
- MVU owns **discrete, eventful state**: mode transitions, room loads, ability
  acquisition, saving, fast-travel, boss phase changes, menus. These are infrequent and
  benefit hugely from MVU's predictable, replayable, testable transitions.
- The **per-tick simulation** (physics + collision + AI for the current room) is
  implemented as a single, self-contained pure step function
  `simulate : float -> SimState -> SimState` operating on the transient layer
  (`Player`, `Entities`, `Camera`). It is *called from* the `Tick` case of `update`.
  Internally it may use **mutable local arrays / structs / `Span`** for collision and
  integration to avoid per-frame GC pressure — this mutation is local and does not
  escape, so `update` remains referentially transparent from the outside (same inputs →
  same `Model`).
- This preserves the MVU contract (deterministic, testable `update`; pure `view`) while
  acknowledging that **strict immutability for 60 entities × 60 Hz is a performance
  trap**. The boundary is: *persistent + discrete = idiomatic MVU; transient + hot =
  optimized pure step with internal mutation.*
- Determinism is retained by threading a seeded `Rng` through `simulate` and using the
  fixed timestep (§7.5), so runs are reproducible for tests (§14).

Tradeoff summary: we sacrifice "everything is an immutable Model diff" purity in the
inner loop for frame-budget safety, but we *gain it back* at the boundary where MVU's
strengths (debuggability, save/replay, testable transitions) actually matter.

## 8. Rendering (Skia 2D)
Coordinate system: world-space in logical px, Y-down. Camera transform = translate by
`-Camera.Pos` then clamp. Logical canvas **1280×720**, letterboxed to window.

**Draw order (back to front):**
1. **Parallax background** (2 layers, scroll at 0.3× and 0.6× camera; zone-tinted).
2. **Tilemap — back décor layer** (non-colliding ornament tiles).
3. **Tilemap — solid layer** (collision tiles; batched via Skia drawAtlas from one
   tile texture).
4. **Hazards** (spikes #C0392B, spore clouds semi-transparent #7FB069 @ 40% alpha).
5. **Pickups / Save Veins** (Save Vein glow #4EC9B0, pulsing).
6. **Enemies** (per-kind sprite; hit-flash = draw white-tinted for 3 frames).
7. **Player** (sprite; during i-frames flash alpha 30%↔100% @ 12 Hz; dash = motion
   streak using 3 trailing ghosts at decreasing alpha).
8. **Projectiles** (bolts #F4D35E with 6 px glow; slinger shots #E07A5F).
9. **Particles** (hit sparks, ember pickups, dust on land — pooled, max 200).
10. **Foreground tilemap** (occluding tiles drawn over player, e.g. grass tufts).
11. **HUD** (§9) in screen-space (no camera transform).
12. **Overlays** (map/pause/title) when `Mode` requires.

**Palette:** background #1A1A2E base; per-zone tint (Z1 #16213E, Z3 #3A2618,
Z4 #14323B, Z6 #2B0B1E). Player #E0E1DD. Tiles #495867 / outline #2F3A45.
**Fonts:** UI "JetBrains Mono" / fallback monospace, 16 px HUD, 28 px headers.
**Camera:** see §9.2 — room-locked clamp + smoothed follow; **screen shake** on boss
hits & landings (decaying offset, max 8 px).
**Redraw strategy:** full redraw every render frame (60 FPS) — Skia clears and repaints;
tile layers batched via `drawAtlas`. No dirty-rect in v1 (full clear is cheap at 720p).

## 9. UI / HUD / Screens

### 9.1 Screens
- **Title:** game logo, "New Game / Continue / Quit", animated background.
- **Playing:** HUD (below) + world.
- **Paused:** dimmed world (50% black overlay), menu (Resume / Map / Options / Quit).
- **Map overlay (Tab):** the persistent **world map** — explored rooms drawn as
  connected cells; current room highlighted; icons for Save Veins, Vein Gates (fast
  travel), bosses, **locked gates color-coded by required ability**, uncollected
  pickups (if "map markers" upgrade bought). Selecting a visited Vein Gate fast-travels.
- **Game Over:** "You fell" + Embers lost location hint + Respawn.
- **Win:** ending card + run stats (time, % map explored, deaths).

### 9.2 Camera detail
- **Room-locked:** camera clamps to current room bounds; in small rooms the room is
  centered (no scroll). Default mode.
- **Smooth follow:** in rooms larger than the viewport, camera lerps toward player at
  smoothing factor **0.12/frame** with a **48 px dead-zone box** around the player so
  small movements don't jiggle the view; look-up/crouch pans **±80 px** vertically.

### 9.3 HUD elements
- **Top-left:** Health masks (5–10 icons, 24×24, filled #E63946 / empty #6B2737).
- **Below health:** Mana/Veil bar (160×10 px, fill #4EC9B0).
- **Top-right:** Ember count with ember icon, monospace, e.g. `◆ 1240`.
- **Bottom-center (contextual):** "Press E" prompts at Save Veins/levers.
- **Mini-map (corner, optional toggle):** 180×120 px room minimap.

### 9.4 Menu system (detailed)
A single **menu stack** drives every non-play screen (Title, Load, Settings, Stats, Pause,
Death, Win). Each menu is a vertical list of rows with a cursor, so one small update handler
serves them all and navigation is identical everywhere. Because this game is save-file based
(3 slots, §13), the Title menu leads with **Continue / Load / New Game**, and the in-run
Pause menu exposes **Map / Inventory** (the §9.1 Tab overlay) alongside **Stats**.

**Menu tree**
```
Title ─┬─ Continue ────────── resume the most-recent save slot
       ├─ Load ────────────── pick a save slot (3 slots, §13) ► SelectSlot
       ├─ New Game ────────── start a fresh save (seeded Rng, §13)
       ├─ Stats ───────────── Stats & Charts screen (§9.5)
       ├─ Settings ──────┬─ Difficulty     ◄ Story · Normal · Veteran ►
       │                 ├─ Master volume  ◄ 0 – 100 ►
       │                 ├─ Sound          ◄ On · Off ►
       │                 ├─ Window scale   ◄ 1× · 2× · Fit ►
       │                 ├─ Screen shake   ◄ On · Off ►
       │                 └─ Back
       └─ Quit

Pause ─┬─ Resume
       ├─ Map / Inventory ── world map + owned abilities (the §9.1 Tab overlay)
       ├─ Stats ──────────── Stats & Charts (§9.5)
       ├─ Settings ───────── (same submenu; returns to Pause)
       └─ Quit to Title

Death ─┬─ Respawn ────────── reload at last Save Vein (Shade dropped, §4.10)
       ├─ Load ──────────── revert to a save slot
       └─ Quit to Title

Win ───┬─ View Stats ─────── Stats & Charts (§9.5)
       ├─ Continue ───────── free-roam the cleared save
       └─ Title
```

**Navigation model**
- `MenuCursor: int` on the active menu; `↑`/`W` decrement, `↓`/`S` increment, both **wrap**.
- `Enter`/`Space` activates the current row; `Esc`/`Backspace` pops the stack (**Back**).
- **Cycler/slider rows** (Difficulty, Master volume, Sound, Window scale, Screen shake):
  `←`/`→` change the value in place; the row shows a right-aligned `◄ value ►` widget.
- **Load** rows are the three save slots (`SelectSlot`), each previewing that slot's
  completion %, playtime, and best clear time; `Enter` loads the highlighted slot.
- Rendering reuses the §9.1 selector style: the selected row is inverted; non-selected rows
  render at 28 px over the dimmed (§8) world/title background.

**Msg additions** (extend §7.2):
```fsharp
    | MenuUp | MenuDown              // move cursor (wraps)
    | MenuAdjust of dir:int          // -1 / +1 on a cycler/slider row
    | MenuActivate                   // Enter/Space on the current row
    | MenuBack                       // Esc — pop the menu stack
    | OpenStats | CloseStats         // enter / leave the Stats screen (§9.5)
    | SelectSlot of slot:int         // choose a save slot on the Load menu
```
These compose with the existing `NewGame`/`LoadGame`/`QuitToTitle`/`TogglePause`/`ToggleMap`
(§7.2). Settings apply live and persist to local config (§13, stored separately from the save
slot): **Difficulty** selects the §12 mode preset (Story `0.5 / 90`, Normal `1.0 / 60`,
Veteran `1.5 / 40` — `enemyDamageMult / hitIFrames`); **Master volume**/**Sound** route to
`Audio.setMasterVolume` (§10, clamped `[0,1]`); **Screen shake** toggles the §8 shake offset.

### 9.5 Stats & charts screen
The Stats screen visualizes **the current run** and **lifetime** play across all slots. It
reads a `RunStats` snapshot (never live physics), so it is a pure, deterministic render
reachable from Title, Pause, and the Win card. Chart-design choices below follow the project
dataviz conventions (form-first, validated colorblind-safe palette, single axis, identity by
entity).

**Tracked this run** — `RunStats`, accumulated in `Tick` (and its event cases), snapshotted
on `Win` (run-end); it lives inside `Save` (§7.1) so it round-trips with the slot:

| Field | Type | Updated |
|-------|------|---------|
| `mapExploredPercent` | `float` | `visitedRooms / totalRooms`, recomputed on `EnterRoom` |
| `itemsCollected` | `int` | incremented on each pickup (Vessel Shard / Ember cache) |
| `abilitiesUnlocked` | `int` | `Save.Abilities` count, on `AbilityAcquired` |
| `bossesDefeated` | `int` | incremented when a boss FSM reaches defeat |
| `deaths` | `int` | incremented on `PlayerDied` |
| `saveCount` | `int` | incremented on `SaveAtVein` |
| `playSeconds` | `float` | accumulated live-play time in `Tick` |
| `secretsFound` | `int` | hidden-room / secret pickups discovered |
| `itemsPerZone` | `Map<Zone,int>` | pickup count bucketed by owning zone (chart 1) |
| `exploreTimeline` | `(min:float * pct:float) list` | explored-% sampled on `EnterRoom` |

**Lifetime** — `LifetimeStats`, persisted (§13): `completionPercent`, `fastestClear` (time),
`hundredPercentClear` (bool flag), `totalDeaths`.

**Layout** (logical 1280×720): a KPI tile row across the top, two charts below.

```
┌──────────────────────── STATS ── Hollowveil ───────────────────────┐
│  ┌ COMPLETION ┐ ┌ ITEMS ┐ ┌ BOSSES ┐ ┌ PLAYTIME ┐                  │  ← KPI stat tiles
│  │    73 %    │ │  28   │ │  1 / 2 │ │  4h 12m  │                  │
│  └────────────┘ └───────┘ └────────┘ └──────────┘                  │
│                                                                     │
│  Items collected per zone           Map explored over time          │
│  ▇▇                            100 ┤                 ╭─────          │
│  ▇▇  ▇▇      ▇▇                     │            ╭────╯              │
│  ▇▇  ▇▇  ▇▇  ▇▇  ▇▇                 │      ╭─────╯                   │
│  Z1  Z2  Z3  Z4  Z5  Z6          0 ┼───────────────────► min        │
└─────────────────────────────────────────────────────────────────────┘
     ↑/↓ scope:  ▸ This Run · Lifetime            ESC — Back
```

**Charts** (rendered in Skia with the same draw-list discipline as §8):

1. **Items-per-zone histogram** — *form: per-category magnitude → bars.* x = zone
   (`Z1`…`Z6`, §6), y = items collected in that zone (`itemsPerZone`). **Single series**, so
   one hue and no legend. Bars are 4 px-rounded at the data end with a 2 px surface gap
   between them. Fill `#2a78d6` (light) / `#3987e5` (dark) — validated categorical slot 1.
2. **Map exploration over time** — *form: change over an ordered index → line.* x = minutes
   played, y = `mapExploredPercent` (0–100), read from `exploreTimeline`; **one series**, so
   one hue and no legend — a single climbing curve that approaches 100 %. 2 px line, ≥ 8 px
   end marker, recessive 1 px gridlines in `#3C3C3C`.

Conventions honored: **color follows the entity** (the exploration series and each zone bar
keep their hue — never repainted by the scope toggle); **one axis only** (no dual-scale);
chart **text uses ink tokens** (`#FFFFFF` primary / `#C3C2B7` muted), never the series hue;
layout is **fixed and deterministic**, so a fixed-seed run (§13) renders byte-identical for
snapshot tests. The `↑/↓` **scope** toggle swaps the data source This-Run ↔ Lifetime without
changing colors.

**Model/Msg hooks:** add `Run: RunStats` (inside `Save`) and `Lifetime: LifetimeStats` to
§7.1; accumulate in the `Tick` cases (advance `playSeconds`, recompute `mapExploredPercent`
and append an `exploreTimeline` sample on `EnterRoom`, bump `itemsPerZone` on pickup);
increment `deaths` on `PlayerDied`, `saveCount` on `SaveAtVein`, `bossesDefeated` on a boss
FSM defeat. On `Win`, fold `RunStats` into `Lifetime` (keep the faster `fastestClear`, set
`hundredPercentClear` when `completionPercent = 100`) and persist (§13). `OpenStats`/
`CloseStats` switch a `Stats of scope:StatScope` overlay state; the render is a no-op on
physics.

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
| Jump (§4.2) | `Audio.playSfx` | `jump-soft` | Soft ground-jump launch. |
| Double jump (§4.11) | `Audio.playSfx` | `jump-air` | Airy second-jump lift, distinct from ground jump. |
| Land (§4.2) | `Audio.playSfx` | `land-dust` | Dust puff on landing. |
| Dash (§4.4) | `Audio.playSfx` | `dash-whoosh` | Whoosh on the flat i-frame dash. |
| Wall slide (§4.3) | `Audio.playSfx` | `slide-loop` | Looping friction hiss while wall-sliding. |
| Grapple fire/hit (§4.5) | `Audio.playSfx` | `grapple` | Hook fire and node-hit reel cue. |
| Melee swing (§4.6) | `Audio.playSfx` | `swing` | Blade swing whoosh. |
| Melee hit (§4.6) | `Audio.playSfx` | `hit-flesh` | Connect thud, paired with the §4.6 hitstop (+ hitstop). |
| Pogo (§4.6) | `Audio.playSfx` | `pogo-ping` | Bright ping on a down-melee bounce. |
| Bolt fire (§4.7) | `Audio.playSfx` | `bolt` | Ranged Bolt release. |
| Bolt impact (§4.7) | `Audio.playSfx` | `bolt-hit` | Bolt collision on terrain/enemy. |
| Player hurt (§4.8) | `Audio.playSfx` | `hurt` | Player takes damage. |
| Player death (§11) | `Audio.playSfx` | `death-sting` | Death sting on `PlayerDied`. |
| Enemy death (§5.2) | `Audio.playSfx` | `enemy-pop` | Enemy defeat pop. |
| Sentinel block (§5.2) | `Audio.playSfx` | `clang` | Metallic clang when a hit is blocked by the Sentinel's shielded front. |
| Pickup ember (§4.10) | `Audio.playSfx` | `coin` | Ember pickup chime. |
| Ability acquired (§4.11) | `Audio.playSfx` | `acquire-fanfare` | Fanfare on `AbilityAcquired`. |
| Save Vein (§5.3) | `Audio.playSfx` | `save-chime` | Chime when resting/saving at a Save Vein. |
| Focus heal (§4.9) | `Audio.playSfx` | `heal` | Focus-heal completion (one mask restored). |
| Boss phase change (§11) | `Audio.playSfx` | `boss-roar` | Roar on `BossPhaseChanged` threshold. |

Music is per-area: each zone has an ambient background loop (6 tracks) requested with
`Audio.playMusic (TrackId "zone-z1-ambient") true` (loop `true`) on `EnterRoom`/zone
change, and boss encounters switch to a boss theme (2 tracks) — e.g.
`Audio.playMusic (TrackId "boss-warden") true` on the arena transition, with
`Audio.stopMusic` on boss defeat/exit before the next zone loop resumes; the title theme
and victory sting are likewise `playMusic`/one-shot cues at those transitions, and the boss
intro ducks the ambient loop. A mute/settings toggle maps to `Audio.setMasterVolume` (e.g.
`Audio.setMasterVolume 0.0` to mute). **Testing:** collect the frame's
`AudioEffect`s, `Audio.interpret` them, and assert the `AudioEvidence.Requested` sequence for
representative events (e.g. landing a melee hit requests exactly
`PlaySfx (SoundId "hit-flesh", _)`).

## 11. Win / Loss / Scoring
- **Win condition:** defeat **Boss B (Veil Echo)** in Z6 and touch the core relic →
  Win screen. Requires all 4 traversal abilities to have reached Z6.
- **Loss condition (death):** HP reaches 0 → `PlayerDied`. Not a hard game-over: respawn
  at last Save Vein, drop recoverable Shade (§4.10). **Game Over screen** only appears
  flavor-wise on death; the run continues. There are no limited lives/continues.
- **Scoring (run stats, not points-based):** tracked for the Win card —
  `completionTime`, `mapExploredPercent = visitedRooms / totalRooms`, `deathCount`,
  `bossesDefeated`, `embersBanked`. No arcade score; "best time" persists per save slot.
- **Boss A — The Warden (Z3 gate boss):** HP **300**. A heavy armored sentinel.
  - **Phase 1 (100%–66% HP):** ground charges across arena (speed 300 px/s, telegraph
    0.5 s), overhead slam creating a shockwave (jump or dash through). Weak point:
    back/head (pogo). Punish window after slam recovery (0.8 s).
  - **Phase 2 (66%–33%):** adds 200 px/s arc projectiles (3-shot fan) between charges;
    charge speed → 380 px/s; arena spikes rise on edges.
  - **Phase 3 (<33%):** enraged — double charge, slam now spawns 2 Spore Pods; faster
    recovery (0.5 s windows). Transition at each threshold triggers `BossPhaseChanged`,
    a 1 s invulnerable roar, and screen shake.
- **Boss B — Veil Echo (Z6 final):** HP **480**. A mirror-revenant that uses the
  player's own abilities.
  - **Phase 1 (100%–70%):** dashes (matching player dash 780 px/s with i-frames you must
    bait out), melee combos; teleports to opposite side every 4 s.
  - **Phase 2 (70%–40%):** adds grapple-pulls (yanks player toward hazards) and bolt
    volleys (5 bolts, 520 px/s, fan); spawns Wraith adds (max 2).
  - **Phase 3 (<40%):** "Veil collapse" — arena darkens, only lit near player; Echo
    chains double-jump + air-dash strings; rapid teleports; must use pogo on its
    descending slam to reach its exposed core. Phase transitions at 70%/40% via
    `BossPhaseChanged`, 1.2 s invuln, full arena flash.

## 12. Difficulty & Balancing
Data-driven tunables (a config record loaded at startup; designers edit these):

| Parameter            | Default | Range      | Effect |
|----------------------|---------|------------|--------|
| runSpeed             | 240     | 180–320    | player top speed (px/s) |
| jumpImpulse          | 620     | 520–720    | jump height |
| gravityRise          | 2000    | 1600–2600  | rise heaviness |
| gravityFall          | 2600    | 2000–3200  | fall heaviness / floatiness |
| coyoteFrames         | 6       | 0–10       | ledge-jump forgiveness |
| jumpBufferFrames     | 6       | 0–10       | landing-jump forgiveness |
| dashDistance         | 140     | 100–200    | dash reach |
| dashIFrames          | 10      | 0–14       | dash invulnerability |
| hitIFrames           | 60      | 30–90      | post-hit invulnerability (frames) |
| meleeDamage          | 10      | 6–16       | swing damage |
| enemyDamageMult      | 1.0     | 0.5–2.0    | global incoming damage scale |
| enemyHpMult          | 1.0     | 0.5–2.0    | global enemy HP scale |
| manaRegenPerSec      | 8       | 4–16       | out-of-combat mana regen |
| emberDropMult        | 1.0     | 0.5–3.0    | currency faucet |
| bossAEnrageThreshold | 0.33    | 0.2–0.5    | Warden phase-3 HP fraction |

Optional difficulty modes scale `enemyDamageMult` / `hitIFrames`: Story (0.5 / 90),
Normal (1.0 / 60), Veteran (1.5 / 40).

## 13. Technical Notes
- **Performance budget:** target **60 FPS / 16.7 ms/frame**. Per room: ≤ **40
  simultaneous entities**, ≤ **40 live projectiles**, ≤ **200 particles**. Tilemap
  batched in one `drawAtlas` call per layer. Sim step budget ≤ **4 ms**, render ≤
  **8 ms**, slack **4 ms**.
- **Fixed vs variable timestep:** simulation is **fixed at 1/60 s**, drained by
  `FixedStep.drain (1.0/60.0) frameTime acc` → `struct (steps, acc')` (§7.5) — it banks the real time,
  returns the whole steps this frame owes, and caps the frame internally against a stall. Do not
  hand-roll the accumulator. Rendering is variable (once per real frame). Optional render interpolation
  between the two latest sim states (lerp positions by `acc / (1/60)`) for smoothness on high-refresh
  displays — off in v1.
- **Determinism / RNG:** a single seeded PRNG threaded through `simulate` — `FS.GG.Game.Core`'s
  **`Rng`** (splitmix64), seeded with `Rng.ofSeed`. Do not hand-roll an `RngState`: `Rng` is already a
  value, so threading it is the natural thing, and every draw returns `struct (x, rng')` to write back.
  Seed stored in `Save` for reproducibility. No use of wall-clock or unordered collections in sim. This
  makes acceptance tests (§14) reproducible.
- **Collision:** swept AABB, axis-separated (resolve X then Y) against the solid tile layer;
  broadphase = tile grid query of cells overlapping the swept bounds — **deliberately not
  `SpatialGrid`**. The tilemap *is* already a uniform grid, so the overlapping cells are an O(1) index
  into it; `SpatialGrid.build` would be a second index of the same data, rebuilt every step. Reach for
  it when you need to broadphase *entities against each other* (a bullet-hell room of shot↔enemy
  pairs); this game's entity counts are small enough that the tile query is the whole story. One-way
  platforms supported via Y-only-down collision flag.
- **Persistence:** `Save` serialized to JSON (one file per slot, 3 slots). Atomic write
  (temp file + rename). Saving only at Save Veins. Settings/keybinds stored separately.
- **Edge cases:** (a) crossing a room edge mid-dash — finish dash, then load room;
  (b) two enemies overlapping player on same frame — apply highest damage only, single
  i-frame trigger; (c) grapple node destroyed/out of range — hook misses, no reel;
  (d) save during boss — disallowed (no Save Vein in boss arena); (e) pogo on dying
  enemy — still grants bounce if the hit connected that frame; (f) focus-heal with
  insufficient mana — no-op, no animation.

## 14. Acceptance Criteria (test scenarios)
All scenarios run the deterministic fixed-step sim (seed fixed). "frame" = 1/60 s.

1. **Run acceleration reaches cap.**
   GIVEN the player is idle and grounded on flat ground,
   WHEN the player holds Right for 12 frames (0.2 s),
   THEN `Player.Vel.Vx` equals **240 px/s** (±1) and does not exceed it.

2. **Variable jump height — tap vs hold.**
   GIVEN the player is grounded,
   WHEN the player presses Jump and releases it after 1 frame (tap),
   THEN peak height is ≤ **30 px**;
   AND WHEN instead the player holds Jump through the rise,
   THEN peak height is **≥ 90 px** (≈96).

3. **Coyote time allows a late jump.**
   GIVEN the player walks off a ledge and is now airborne,
   WHEN the player presses Jump within **6 frames** of leaving the ledge,
   THEN a full ground jump executes (`Vel.Vy == -620`);
   AND WHEN the press is on frame **7**, THEN no jump occurs.

4. **Jump buffering triggers on landing.**
   GIVEN the player is falling toward ground,
   WHEN Jump is pressed **5 frames** before the landing frame,
   THEN on the landing frame the player jumps automatically (`Vel.Vy == -620`).

5. **Ability gate blocks then unlocks.**
   GIVEN the player lacks **Dash** and a room edge is `Locked(Dash)` across a 140 px pit,
   WHEN the player attempts to cross, THEN the edge does not transition and the player
   cannot reach the far side (falls/blocked);
   AND WHEN `AbilityAcquired Dash` is dispatched and the player dashes across,
   THEN `EnterRoom` for the far room is emitted.

6. **Dash i-frames negate damage.**
   GIVEN the player dashes through a Crawler on frame 0 of the dash,
   WHEN contact occurs within the first **10 frames** of the dash,
   THEN `Player.Hp` is unchanged and no hitstun is entered;
   AND WHEN contact occurs on frame **11**, THEN the player takes damage.

7. **Post-hit invulnerability window.**
   GIVEN the player takes a hit at time T and `hitIFrames = 60`,
   WHEN a second enemy contacts the player at T + 0.5 s (frame 30),
   THEN no additional HP is lost (still invulnerable);
   AND WHEN contact occurs at T + 1.05 s (frame 63), THEN HP is lost again.

8. **Melee deals damage and combo scales.**
   GIVEN a Lunger at 25 HP in melee range,
   WHEN the player lands 3 melee hits within 0.6 s,
   THEN damage dealt is 10 + 10 + 15 = **35** and the Lunger is dead (HP ≤ 0).

9. **Pogo bounce and air-dash refresh.**
   GIVEN the player is airborne above a Spore Pod with air-dash already used,
   WHEN the player performs a Down-melee that connects,
   THEN `Player.Vel.Vy == -520` (bounce) AND `AirDashAvailable == true`.

10. **Save / load round-trip.**
    GIVEN the player interacts with a Save Vein with Embers=1240, MaxHp=70, Dash owned,
    WHEN the game is saved, the process is restarted, and the slot is loaded,
    THEN `Save.Embers == 1240`, `Save.MaxHp == 70`, `Dash ∈ Save.Abilities`,
    AND the player spawns at the Save Vein's room/position with full HP/mana.

11. **Death drops a recoverable Shade.**
    GIVEN the player has 50 Embers and dies in room R at position P,
    WHEN respawn occurs, THEN `Save.Shade == Some(R, P, 50)`, carried Embers = 0,
    and the player is at the last Save Vein;
    AND WHEN the player returns and touches the Shade, THEN Embers = 50 and Shade = None.

12. **Boss A phase transition at threshold.**
    GIVEN Boss A (Warden) at 67% HP in Phase 1,
    WHEN damage brings it to **66%** HP,
    THEN `BossPhaseChanged(Warden, 2)` is emitted, the boss is invulnerable for **60
    frames (1.0 s)**, and afterward arc projectiles are enabled (Phase 2 pattern set).

13. **Camera room-lock clamp.**
    GIVEN a room smaller than the viewport,
    WHEN the player moves to the room's left edge,
    THEN `Camera.Pos.Vx` does not go below the room's min bound (no out-of-room void
    shown).

14. **Fixed-timestep determinism.**
    GIVEN the same seed and the same recorded input sequence,
    WHEN the sim is run twice,
    THEN both runs produce identical `Player.Pos` and `Entities` state at every frame
    (bit-identical), regardless of real frame rate.

15. **Input edge vs held (dash is edge-triggered).**
    GIVEN the player holds Dash (Shift) continuously,
    WHEN 60 frames pass,
    THEN exactly **one** dash fires (on the press edge), not a dash every frame;
    AND a second dash only fires after the **0.35 s** cooldown AND a new press edge.

## 15. Stretch Goals
Ranked, out of scope for v1:
1. **Charm/relic system:** equippable modifiers (trade health for damage, auto-Shade
   recovery) with limited slots — adds build variety.
2. **Render interpolation** between sim states for buttergi smoothness on 120/144 Hz.
3. **Map annotations & custom markers** the player can place.
4. **More abilities:** super-dash (shinespark), down-slam, charged bolt.
5. **Boss rush mode** unlocked after the ending.
6. **A third optional super-boss** behind a fully-explored-map gate.
7. **Speedrun timer + ghost replays** (leverages deterministic sim).
8. **Controller rumble + dynamic music layers** that intensify in combat.
9. **New Game+** with remixed enemy placements and a damage modifier.
10. **Lore tablets** and an in-game bestiary populated by kills.
