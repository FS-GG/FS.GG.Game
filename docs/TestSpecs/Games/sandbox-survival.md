---
title: "Hollowreach"
slug: sandbox-survival
category: games
complexity: complex
genre: "2D sandbox survival/crafting (side-view, tile-based)"
target_session_minutes: 45
stack: { rendering: "FS.GG.Rendering (Skia/OpenGL)", framework: "FS.GG.Game.Core (FixedStep for the tick; Rng for determinism; Pathfinding for light propagation; Los for placement reach)", arch: "Elmish/MVU", lang: "F#" }
status: spec
---

# Hollowreach

## 1. Overview

**Hollowreach** is a 2D side-view sandbox survival/crafting game in the lineage of
Terraria and Starbound. The player wakes on the surface of a procedurally generated,
fully destructible tile world with nothing but their fists. The core verb is **mine →
craft → build → survive**: dig into the earth for stone, ore, and gems; refine raw
resources at crafting stations into better tools, weapons, and walls; and fortify a
shelter before nightfall, when hostile creatures crawl out of the dark to test it. Every
tile in the world can be removed and almost every tile can be placed, so the world is the
player's to reshape. The fun is the compounding loop of capability: a wooden pickaxe gets
you stone, stone tools get you iron, iron gets you deep into the cave layer where the best
ore (and the worst monsters) live. One in-game day is a tractable goal; surviving and
expanding across many days is the long arc.

The fantasy is **self-reliant frontier mastery over a hostile, malleable world**.

## 2. Core Game Loop

**Moment-to-moment loop (seconds):**
`move/jump → aim cursor at a tile → mine (hold) or place (click) → pick up drops →
manage hotbar → react to threats → repeat`

**Tactical loop (minutes):** descend for resources → return to base → craft at stations →
upgrade tools/armor → extend base/lighting → descend deeper.

**Session loop (day cycle, ~14 real minutes per full day at default):**
`dawn (gather/explore) → noon (deep mining) → dusk (retreat & fortify) → night (defend
shelter, fight enemies) → survive to dawn (resources banked, tier advanced) → repeat`

**Session-level (start → end):**
`new world (seed) → spawn on surface → survive N days / reach a tier goal →
death (respawn at bed/spawn with item loss rules) → quit saves world → resume loads world`

There is no hard "win"; the implicit objective is tier progression (see §11) and the
explicit failure state is death without a respawn anchor on permadeath worlds.

## 3. Controls & Input

Keyboard-primary with mouse for aiming/targeting. Input model column notes whether the
action is **edge-triggered** (fires once on the transition) or **level/held** (acts every
tick the key is down).

| Input | Action | Model |
|---|---|---|
| `A` / `D` (or `←`/`→`) | Walk left / right | Held |
| `Space` / `W` | Jump (and double-jump if unlocked) | Edge (press) |
| `S` (held in air) | Fast-fall (increase gravity) | Held |
| `S` (on platform tile) | Drop through one-way platform | Edge |
| Mouse move | Move tile cursor (block highlight) | Continuous |
| Left mouse (hold) | Mine targeted tile / attack toward cursor | Held |
| Right mouse (click) | Place selected tile/item at cursor | Edge |
| Mouse wheel / `1`–`0` | Cycle / select hotbar slot (10 slots) | Edge |
| `E` | Open / close inventory + crafting panel | Edge (toggle) |
| `Q` | Drop one of selected stack to world | Edge |
| Left mouse (in inventory) | Pick up / place stack (split with `Shift`) | Edge |
| `F` | Interact (open chest, use station, sleep in bed) | Edge |
| `Esc` | Pause menu / close panel | Edge |
| `R` | Use/eat selected consumable (food/potion) | Edge |
| `Ctrl` (held) + mine | Mine background **wall** instead of foreground tile | Held modifier |
| `Tab` | Toggle minimap | Edge |

**Reach rule:** placement and mining are only allowed within a **5-tile (160 px) radius**
of the player's center, and the target tile must be adjacent (4-connected) to an existing
tile or the player must have line-of-sight to it (prevents floating placement). The sight test is
`Los.lineOfSight isTransparent playerTile targetTile` (`FS.GG.Game.Core`) — do not hand-roll a
Bresenham; `Los.line` is the same walk if you want the cells rather than the verdict.

## 4. Mechanics (detailed)

All physics run on a **fixed timestep of 1/60 s (dt = 0.0167 s)**. Distances are in
**pixels (px)**; one tile = **32×32 px**. Velocities are px/s, accelerations px/s².

### 4.1 Movement & platformer physics

- **Walk:** target horizontal speed `±180 px/s`. Acceleration toward target
  `1600 px/s²` on ground, `900 px/s²` in air. Friction (no input) decelerates at
  `2000 px/s²` (ground), `300 px/s²` (air drag).
- **Gravity:** `1800 px/s²`, clamped to terminal velocity `1200 px/s`. Fast-fall (`S`)
  raises gravity to `3000 px/s²`.
- **Jump:** initial impulse sets `vy = -620 px/s` (apex ≈ 107 px ≈ 3.3 tiles).
  **Variable height:** releasing jump before apex while `vy < -250` clamps `vy = -250`
  (short hop). **Coyote time:** 6 frames (0.1 s) after leaving a ledge a jump still
  works. **Jump buffer:** a jump pressed up to 6 frames before landing fires on landing.
- **Double jump** (unlocked via Cloud Boots item): one extra `vy = -540 px/s` while
  airborne; resets on ground contact.
- **Step-up:** when walking into a single 1-tile-high ledge while grounded, auto-climb it
  (no jump needed) if the tile above the ledge is clear.

### 4.2 Tilemap collision

Player AABB is **24×46 px** (narrower than a tile so they fit 1-wide gaps). Collision is
**swept axis-separated** against the tilemap:

1. Integrate X, resolve against solid tiles overlapping the new AABB (push out, zero `vx`).
2. Integrate Y, resolve against solid tiles (push out, zero `vy`; set `grounded` if the
   correction was upward).
3. **One-way platforms:** collide only when `vy ≥ 0` (falling) and the player's previous
   bottom was above the platform top; ignored when holding `S` (drop-through) for 8 frames.

Only the up-to **~3×2 tiles** overlapping the AABB are tested per axis (O(1) per entity),
queried from the chunk store (§7.2). Sub-pixel positions are kept as `float32`.

### 4.3 Mining

- Each solid tile has **hardness `H`** (hit-points of mining progress). Mining applies
  `toolPower × dt` of progress per tick while LMB is held on that tile.
- **Tool tiers** (pickaxe/axe/drill). `toolPower` and a **tier gate** (minimum tier to
  mine at all):

  | Tool | toolPower (H/s) | Tier | Can mine up to |
  |---|---|---|---|
  | Fist | 1.5 | 0 | Dirt, Sand, Wood, Leaves |
  | Wood Pickaxe | 6 | 1 | Stone, Coal, Copper |
  | Stone Pickaxe | 10 | 2 | + Iron, Silver |
  | Iron Pickaxe | 16 | 3 | + Gold, Gems |
  | Diamond Drill | 28 | 4 | + Obsidian, Bedrockish |

  If the tile's required tier > tool tier, progress is **0** (cannot mine) and the tile
  flashes red. Mining a tile below your tier still works at full `toolPower`.
- **Breaking:** when accumulated progress ≥ `H`, the tile is set to `Air`, a **drop item**
  spawns (§4.4), a break particle burst plays, and lighting/neighbors are re-evaluated.
- **Auto-target switch:** if the cursor moves off the in-progress tile, progress on the
  old tile **decays at 2×H/s** (so flicking the mouse loses partial progress).
- **Wall mining** (`Ctrl`): mines background walls (half hardness, no tool-tier gate above
  the wall's own tier) and drops the wall item.

### 4.4 Drops & pickup

- Breaking a tile spawns a **dropped-item entity** (16×16 px) at the tile center with a
  small random pop velocity (`vx ∈ [-40,40]`, `vy = -120`). Drops obey gravity + tilemap
  collision and rest on the ground.
- **Magnet pickup:** drops within **64 px** of the player accelerate toward them at
  `1400 px/s²`; on contact they merge into inventory (stack up to item max). If inventory
  is full the drop stays in the world.
- Drops **despawn after 300 s**; identical drops within 12 px **merge** into one stack to
  cap entity count.
- Drop tables can be probabilistic (e.g. Stone → 1× Stone always; Coal Ore → 1× Coal +
  10% bonus Coal).

### 4.5 Placement / building

- Right-click places **1** of the selected placeable item at the cursor tile if: the tile
  is `Air`, it is within reach (§3), it is supported (adjacent to a solid tile, or it's a
  wall placement which only needs a neighbor wall/tile), and the player AABB does not
  overlap the target (can't trap yourself in a solid block).
- Placing decrements the stack by 1. Placing a wall fills the **background layer**;
  placing a block fills the **foreground (solid) layer**.
- **Doors, torches, platforms, chests, stations** are special placeables with their own
  footprint and interaction (door = 1×3 toggling solidity; torch = light + non-solid;
  platform = one-way; chest = 1-tile storage of 20 slots; station = crafting anchor).

### 4.6 Health, hunger, damage

- **Health:** max 100 HP. Regenerates `+1 HP/s` only when **hunger > 50%** and no damage
  taken in the last 5 s.
- **Hunger:** 0–100, drains `0.5/s` while active (`1.0/s` while mining/running). At
  **hunger 0**, lose `2 HP/s` (starvation). Eating food restores hunger (see item table).
- **Fall damage:** falling faster than `900 px/s` on landing deals
  `(vy - 900) / 15` HP.
- **Contact damage:** enemies deal damage on AABB overlap with an **invulnerability
  window of 0.7 s** after a hit; a hit also applies knockback (`vx = ±300`, `vy = -250`).
- **Drowning:** submerged in water (if water enabled) > 10 s → `5 HP/s`.
- **Death:** HP ≤ 0 → drop a configurable % of inventory as world drops, respawn at the
  player's **bed** (if placed & valid) else **world spawn**, after a 3 s fade.

### 4.7 Combat

- Melee weapons swing in a **90° arc** toward the cursor over 0.25 s; the hit-test is a
  capsule from player center to `reach` px. Damage = weapon base × (1 + 0.1·tier).
- Ranged (bow) consumes 1 arrow, fires a projectile entity (`speed 700 px/s`, gravity
  `600 px/s²`, despawn on tile/enemy hit).
- Enemies have HP, take knockback, and flash white for 0.1 s on hit.

### 4.8 Day/night cycle

- A full day is **`dayLengthSeconds = 840 s`** (14 min) at default. Phases by normalized
  time `t ∈ [0,1)`: **Dawn** `[0.0,0.1)`, **Day** `[0.1,0.45)`, **Dusk** `[0.45,0.5)`,
  **Night** `[0.5,0.95)`, **Pre-dawn** `[0.95,1.0)`.
- Ambient sky light lerps with `t` (see §8.5). Enemy spawning is gated to **Night** and
  to **dark tiles** at any time (caves are always "night" underground).
- Sleeping in a valid bed at night fast-forwards `t` to `0.0` of the next day if no
  enemies are within 480 px.

### 4.9 Enemy spawning & AI

- **Spawn cadence:** every 1.0 s the spawner attempts spawns while `enemyCount < cap`.
  `cap = 6 + floor(daysSurvived × 1.5)` capped at 30.
- **Where:** pick a random **active** (loaded) tile column 240–640 px from the player that
  is (a) dark (light < 0.15) or surface-at-night, (b) has a 2-tile-tall air pocket on a
  solid floor, and (c) is **off-screen** (outside the camera + 64 px margin). Surface
  night spawns favor flat ground; cave spawns favor open air pockets.
- **Despawn:** enemies > 1600 px from player or in daylight surface light despawn.
- **AI state machine** (per enemy archetype, see §5): `Idle → Patrol → Chase → Attack →
  (Flee | Dead)`. Walkers do simple "move toward player X; jump if blocked by a 1-tile
  wall and player is higher"; flyers ignore tile collision and steer directly; jumpers
  leap on a cooldown. Pathing is **greedy local** — and this is a **deliberate refusal of
  `Pathfinding.astar`**, not an oversight. Enemies that get stuck on terrain are part of the design:
  the player is *supposed* to be able to wall off a base with dirt and have the horde mill about
  outside it, and a horde that solved the maze would delete that. It is also the cheap option at
  hundreds of enemies. Reach for `Pathfinding.astar` (one route) or `Pathfinding.distanceField` +
  `Pathfinding.flowField` (one field, all enemies) the day an enemy is supposed to *outsmart* terrain
  — a v1.1 "hunter" that finds the door would want exactly that, and it is one call, not a project.

### 4.10 Lighting (optional in v1, specced for v1.1)

- Per-tile light value `0–1`. **Sources:** sky (top-down flood scaled by day phase),
  torches/lava (`emit = 0.9`, radius ~10 tiles). Propagation is a flood fill with attenuation
  `−0.08` per air tile and `−0.16` per solid tile — which is a **cost-aware** flood, so it is
  `Pathfinding.distanceField Neighbourhood.FourWay maxVisited cost sources`, not a plain
  `Pathfinding.bfs`: `cost` is the per-tile attenuation (air 8, solid 16, in hundredths), the `sources`
  are every emitter seeded at once, and the resulting `Map<Cell, int>` is *falloff from the nearest
  light* — subtract it from the emitter's value and you have the light map. Seeding all emitters as
  one goal list is why this is a single pass and not one flood per torch. Recompute incrementally on
  tile change within the affected region (not the whole world).
- Light drives both **rendering darkness** (§8.5) and **spawn eligibility** (§4.9).
- If disabled in v1, treat underground as uniformly dark and surface as day-phase lit.

## 5. Entities / Game Objects

```fsharp
type EntityId = int

type Facing = Left | Right

type AiState = Idle | Patrol | Chase | Attack | Flee | Dead

// Non-tile world actors (enemies, dropped items, projectiles, the player)
type Enemy =
    { Id        : EntityId
      Kind      : EnemyKind
      Pos       : Vec2          // px, world space (entity origin = AABB center-bottom)
      Vel       : Vec2          // px/s
      Hp        : float32
      Facing    : Facing
      State     : AiState
      StateTime : float32       // seconds in current state
      Cooldown  : float32 }     // attack/jump cooldown

and EnemyKind =
    | Slime    // jumper
    | Crawler  // walker, melee
    | Bat      // flyer
    | Skeleton // walker, ranged (throws bones)
    | Brute    // walker, tanky, night-only

type DroppedItem =
    { Id : EntityId; Item : ItemStack; Pos : Vec2; Vel : Vec2; Age : float32 }

type Projectile =
    { Id : EntityId; Pos : Vec2; Vel : Vec2; Damage : float32
      FromPlayer : bool; Life : float32 }
```

**Entity catalogue:**

| Entity | Size px | HP | Speed px/s | Damage | Behavior |
|---|---|---|---|---|---|
| Player | 24×46 | 100 | 180 walk | weapon | §4 |
| Slime | 32×24 | 14 | 90 (hop) | 6 | Jumper: leaps toward player every 1.4 s; arc hop `vy=-420` |
| Crawler | 28×40 | 22 | 70 | 9 | Walker: chases on X, jumps 1-tile walls |
| Bat | 28×20 | 10 | 130 | 5 | Flyer: sine-weave pursuit, ignores tiles, flees in daylight |
| Skeleton | 26×44 | 34 | 60 | 8 + ranged 7 | Walker: keeps 160 px range, throws bone projectiles |
| Brute | 40×60 | 80 | 50 | 20 | Walker: slow, heavy knockback, night-only, drops good loot |
| Dropped item | 16×16 | — | — | — | Gravity + magnet (§4.4) |
| Projectile | 8×8 | — | — | varies | Linear/gravity, despawn on hit |

**Lifecycle:** enemies created by the spawner (§4.9), destroyed on death (drop loot) or
despawn; dropped items created by mining/death/`Q`, destroyed on pickup/despawn/merge;
projectiles created on attack, destroyed on hit/timeout.

## 6. World / Levels / Progression

**Logical resolution:** 1280×720 px. **Tile:** 32×32 px → viewport shows ~40×23 tiles.

**World dimensions (default "small"):** `WorldWidth = 4200 tiles`, `WorldHeight = 1200
tiles`. Stored in **chunks of 64×64 tiles** → 66×19 ≈ **1254 chunks**.

**Vertical layer bands** (by tile Y, top = 0):

| Band | Y range (tiles) | Contents |
|---|---|---|
| Sky / Air | 0 – ~ surface | Floating islands (rare), open air |
| Surface | heightmap ±20 | Grass, dirt, trees, surface enemies at night |
| Underground (dirt) | surface → 300 | Dirt, stone pockets, small caves, copper/iron |
| Caverns (stone) | 300 – 800 | Stone, large caves, iron/silver/gold, gems, water |
| Deep | 800 – 1180 | Dense stone, obsidian, lava pools, gold/gems, Brutes |
| Underworld floor | 1180 – 1200 | Bedrockish (tier-4 gated) boundary |

**Biomes (horizontal regions on the surface)** selected by a low-frequency noise:
**Forest** (default, trees+grass), **Desert** (sand, cacti, deeper to sandstone),
**Tundra** (snow blocks, ice, slippery friction ×0.5), **Jungle** (dense vines, mud,
tougher enemies). Biome affects surface tiles, tree type, and a spawn-weight table.

**Progression / difficulty ramp:** gated by tool tier (§4.3) — you physically cannot reach
deep ore without crafting up. Difficulty also ramps with `daysSurvived` via the spawn cap
(§4.9) and unlocking the night-only Brute after day 2. No discrete "levels"; the world is
one continuous space and depth is the difficulty axis.

## 7. State Model (Elmish/MVU)

### 7.1 The central challenge: a huge mutable world inside an immutable Model

A 4200×1200 tile world is **5.04 M tiles**. Naively, MVU wants the `Model` to be an
immutable value that `update` returns a fresh copy of each tick. Copying 5 M tiles per
frame is impossible (and even structural-sharing a giant array on every mined block is
wasteful). We resolve this **honestly** rather than pretending the world is cheap:

1. **The world is not value-copied per tick.** Tiles live in **chunk arrays**
   (`Tile[]` of length 4096 per 64×64 chunk). These flat arrays are **mutated in place**
   during the simulation step. The `Model` holds *references* to chunk records; mining a
   block mutates `chunk.Tiles.[i]` and bumps `chunk.Version`, it does **not** allocate a
   new 5 M-tile structure.
2. **The Model stays "morally immutable" at the boundaries that matter.** Player,
   inventory, time, and the *set* of loaded chunks are ordinary immutable F# records/maps
   updated functionally — those are small and benefit from MVU's clarity and testability.
   The tile payload is the one deliberate, documented escape hatch: a **mutable simulation
   buffer** owned by the model, never shared, never aliased into the view.
3. **Discipline that keeps it sound:** (a) only `update`/the sim step writes tiles, on the
   single update thread; (b) the view reads tiles but never writes; (c) each chunk carries
   a `Version : int` and `Dirty : bool` so the renderer and lighting can detect changes
   without diffing arrays; (d) world mutations also emit a small immutable **`WorldEvent`**
   list (e.g. `TileChanged (x,y,old,new)`) so save/undo/networking could be layered later
   without reading the whole grid.
4. **Why this is acceptable:** the alternative (persistent immutable tile maps) costs
   allocation and GC pressure for zero gameplay benefit; the tile grid is a textbook case
   of a large, hot, locally-mutated buffer. We pay the cost of explicit mutation discipline
   to buy a flat-array performance profile, and keep MVU purity for everything small.

### 7.2 Chunk store & streaming

- `ChunkKey = (cx, cy)`. Chunks generated lazily from the seed on first access and cached.
- **Active set:** chunks whose bounds intersect `camera ∪ 2-chunk margin` are **loaded**
  (generated/simulated/rendered). Chunks beyond a larger **unload radius** are flushed to
  the save store and dropped from memory. Simulation (entities, mining, lighting) only
  runs on loaded chunks. Rendering only touches loaded **visible** chunks.
- Each chunk: `{ Key; Tiles: Tile[4096]; Walls: Tile[4096]; Light: float32[4096];
  Version: int; Dirty: bool; Generated: bool }`.

```fsharp
type TileType =
    | Air | Dirt | Grass | Stone | Sand | Wood | Leaves
    | Coal | Copper | Iron | Silver | Gold | Gem | Obsidian | Bedrockish
    | Torch | Platform | Door of bool   // bool = open
    | Chest of int      // -> chest store id
    | Station of StationKind
    | Water of byte     // 0..8 fill level

type Tile = { Type: TileType }   // 1 byte type tag in practice; packed in arrays

type StationKind = Workbench | Furnace | Anvil | Cookpot | Loom

// Collision-safe labels (Vx/Vy), matching the scaffold's Geometry.Vec2. This world is float32
// throughout (see Hp/Hunger below), so it declares its own rather than abbreviating the scaffold's,
// whose Vec2 is float; convert at the scene boundary. What must NOT change is the labels —
// X/Y/Width/Height collide with Scene's Point/Rect and mis-resolve bare record literals in the
// durable LayoutEvidence.fs.
type Vec2 = { Vx: float32; Vy: float32 }

type Player =
    { Pos: Vec2; Vel: Vec2; Facing: Facing
      Grounded: bool; CanDoubleJump: bool
      Hp: float32; Hunger: float32; InvulnTimer: float32
      MiningTile: (int*int) option; MiningProgress: float32 }

type ItemStack = { Item: ItemId; Count: int }

type Inventory =
    { Hotbar: ItemStack option []   // length 10
      Main:   ItemStack option []   // length 30 (3x10)
      Selected: int }               // 0..9 hotbar index

type TimeState = { DayTime: float32; Day: int }   // DayTime in [0,1)

type World =
    { Seed: int
      WidthTiles: int; HeightTiles: int      // NOT Width/Height — those labels collide with Scene.Rect
      Chunks: System.Collections.Generic.Dictionary<int*int, Chunk> // mutable cache
      Chests: Map<int, ItemStack option []>
      LoadedKeys: Set<int*int> }

type Model =
    { World:     World
      Player:    Player
      Inventory: Inventory
      Enemies:   Enemy list
      Drops:     DroppedItem list
      Projectiles: Projectile list
      Time:      TimeState
      Camera:    Vec2
      Input:     InputState        // current held keys + mouse tile + buttons
      Ui:        UiState           // Playing | InventoryOpen | Paused | Dead | Title
      Rng:       Rng               // FS.GG.Game.Core; sim RNG (separate from worldgen)
      DaysSurvived: int
      Events:    WorldEvent list } // transient, drained each frame
```

### 7.3 Msg

```fsharp
type Msg =
    // input (edge + held captured by subscription)
    | KeyDown of Key | KeyUp of Key
    | MouseMove of px:Vec2
    | MouseDown of Button | MouseUp of Button
    | WheelDelta of int
    // simulation
    | Tick of dt:float32
    // ui / lifecycle
    | OpenInventory | CloseInventory | Pause | Resume
    | StartNewWorld of seed:int | LoadWorld of slot:int | SaveWorld of slot:int
    | Respawn
    // async results
    | WorldSaved of slot:int | WorldLoaded of Model | ChunkGenerated of Chunk
```

### 7.4 update — key transitions

- **`Tick dt`** runs the **fixed-step simulation** (§7.5) and is the only message that
  advances physics/time. Variable display frames between ticks just re-render the latest
  model (no state change) so rendering can exceed/trail 60 Hz safely.
- **`MouseDown Left`** sets the mining target (or triggers a weapon swing if a weapon is
  selected); **held** state is read each `Tick` to apply mining progress.
- **`MouseDown Right`** attempts a placement (validates reach/support/overlap, decrements
  the stack, mutates the foreground/background chunk array, marks chunk `Dirty`, emits
  `TileChanged`).
- **`KeyDown Digit n`** / **`WheelDelta`** changes `Inventory.Selected`.
- **`OpenInventory`** sets `UiState=InventoryOpen` and **pauses simulation ticks** (or
  not, configurable); crafting commands are dispatched while open.
- **Craft message** (a UI action) checks the recipe table against combined inventory +
  in-range station, removes inputs, adds outputs (§12).
- **`StartNewWorld seed`** seeds both `worldgen` (deterministic, from `seed`) and the sim
  `Rng`, generates spawn-area chunks, places the player on the surface heightmap.
- **`SaveWorld`/`LoadWorld`** are `Cmd`s to an async persistence service (§13); the world
  array work happens off the update thread and returns `WorldSaved`/`WorldLoaded`.

### 7.5 The simulation tick (ordered, fixed dt)

Per `Tick dt` (dt = 1/60), in this order, to keep determinism:

1. **Time:** advance `DayTime`, recompute phase; increment `Day`/`DaysSurvived` on wrap.
2. **Streaming:** update active chunk set from camera; queue gen/unload (gen may be async).
3. **Input → intents:** read held keys for movement; resolve mining/placement intents.
4. **Player physics:** acceleration → integrate X (collide) → integrate Y (collide) →
   grounded/coyote/jump-buffer; fall damage on landing.
5. **Mining/placement:** apply mining progress to target tile; break → spawn drop + emit
   event + mark dirty + flag light recompute region.
6. **Survival:** hunger drain, regen/starvation, drowning, invuln timer decay.
7. **Enemies:** spawner attempt → per-enemy AI state machine → integrate + collide →
   contact damage → death/loot.
8. **Projectiles & drops:** integrate, collide, resolve hits, magnet pickup, despawn/merge.
9. **Lighting:** if any tile changed, re-run the §4.10 `Pathfinding.distanceField` over the affected
   regions only — not the whole world.
10. **Camera:** lerp toward player (`smooth = 8/s`), clamp to world bounds.
11. **Drain `Events`** into the save/undo/audio pipelines; clear for next tick.

If the renderer falls behind, run **up to 5 catch-up ticks** per frame then drop the rest
(spiral-of-death guard).

### 7.6 view (pure)

`view model dispatch` produces a **declarative scene description** (an immutable list of
draw commands / a retained scene tree) that the Skia layer renders. It reads tiles from
chunk arrays (read-only) and never mutates the model. It diffs nothing itself — it relies
on chunk `Version`/`Dirty` and entity lists; the Skia layer (§8) caches chunk surfaces.

### 7.7 Subscriptions

- **`Tick`:** a fixed 60 Hz accumulator-driven timer dispatches `Tick (1/60)` (or N of
  them) using a wall-clock accumulator; `dt` is constant for determinism.
- **Input:** keyboard/mouse events from the host window dispatch `KeyDown/Up`,
  `MouseMove/Down/Up`, `WheelDelta`. Edge vs. held is reconstructed in `InputState`.
- **Async chunk gen / save / load** completions dispatch their result messages.

## 8. Rendering (Skia 2D)

**Coordinate system:** world space in px, Y-down. Camera transform = translate by
`-Camera + (640,360)` so the player is centered; all world draws happen under this matrix.
UI draws in screen space (no camera transform).

### 8.1 Layer / draw order (back to front)

1. **Sky gradient** (full-screen, day-phase colored).
2. **Parallax background** (distant hills/cave wall, scrolls at 0.3× camera).
3. **Background wall layer** (placed walls; drawn darker, `−25%` brightness).
4. **Foreground tile layer** (solid tiles).
5. **Dropped items** (16×16 icons).
6. **Enemies & projectiles.** Enemies are drawn by `FS.GG.UI.Symbology` in the `Token` grammar
   (`Symbology.token`) from the §8.7 ChannelMap — not a per-kind sprite. Projectiles stay plain
   primitives (8×8 quads, §5); they are not units and have no channels to encode.
7. **Player** (+ held-tool swing arc).
8. **Tile cursor highlight** + mining crack overlay (progress → 1 of 4 crack frames).
9. **Lighting overlay** (multiply darkness, §8.5).
10. **Particles** (break bursts, hit sparks, footstep dust).
11. **HUD / UI / panels** (§9), screen space.

### 8.2 Chunk surface caching (key performance technique)

Each visible chunk's static tile art (layers 3–4) is rendered once into an **off-screen
Skia surface (2048×2048 px per 64×64 chunk)** and re-blitted each frame. The cache is
invalidated when `chunk.Version` changes (a tile was mined/placed) — then only that chunk's
surface (and the lighting region) is re-rendered. With ~6–9 visible chunks, per-frame work
is ~9 textured quad blits + dynamic entities, not 920 individual tile draws.

### 8.3 Tile drawing

Tiles are drawn from a **texture atlas** (`SKImage` + source rects) by `TileType`. Variant
selection: a 2-bit hash of `(x,y)` picks 1 of up to 4 variant tiles to break tiling.
**Auto-tiling** (Terraria-style 47-mask blob) chooses edge/corner sub-tiles from the
8-neighborhood so dirt/stone borders look smooth. Grass overlays a top fringe sprite where
air is above.

### 8.4 Palette (hex)

| Element | Color |
|---|---|
| Sky (day) | `#7EC0EE` |
| Sky (night) | `#0B1026` |
| Dirt | `#7A4B2B` |
| Grass top | `#4FA63B` |
| Stone | `#6E6E73` |
| Coal | `#2B2B2B` on stone |
| Copper | `#B66A3C` | 
| Iron | `#C9C0B6` |
| Silver | `#D9DCE3` |
| Gold | `#E8C24A` |
| Gem | `#46C7E0` |
| Water | `#2D6CDF` @ 60% alpha |
| Torch light | `#FFD27A` |
| Player | `#E8E0D0` |
| Mining cursor | `#FFFFFF` @ 35% |
| Tile-tier-fail flash | `#E2453C` |

Fonts: HUD numerals in a bitmap/monospace font, 16 px; panel labels 14 px.

### 8.5 Lighting render

Per-tile light `0–1` is upsampled to a low-res lightmap and drawn as a **multiply layer**
(black at light 0, transparent at light 1) over the world, smoothly interpolated (bilinear)
so light gradients are soft. Sky light scales the surface band by day phase; torches add
warm radial gradients. When lighting is disabled (v1), substitute a flat day-phase ambient
multiply over the whole screen and a constant dark multiply below the surface band.

### 8.6 Redraw strategy

Full redraw at 60 FPS (game world is always animating: day phase, entities, particles).
The expensive static tile work is amortized by §8.2 chunk caches, so a steady frame is
~10 blits + a few hundred dynamic quads, well within budget (§13).

### 8.7 Enemy symbology (the `Enemy → Token` ChannelMap)

Layer 6 above says enemies are drawn by `FS.GG.UI.Symbology` rather than by a per-kind sprite.
This is the cheapest real decision in §8: the roster in §5 is five kinds each carrying HP, a
damage figure, a walk speed and a facing, and `Symbology` already ships that exact shape as a
tested, deterministic vocabulary — a fixed channel set (`Token`), interchangeable grammars, and
a **legibility linter** that scores a symbol set against a per-channel capacity table. The
per-game work is one **ChannelMap** from `Enemy` to `Token`; the library draws it, and the game
is buildable with no art.

**Grammar: `Token`.** `Symbology.token` rotates the whole body by `Heading`, which is right here
because a sandbox enemy genuinely faces left or right (`Facing`) and that facing is a fact the
player reads to predict a lunge. `Badge` would flatten it to a discrete edge indicator; `Ring`
is a radial gauge and encodes no silhouette at all.

**Where the map lives.** `Symbology` depends on `Scene`, so this map belongs in
`FS.GG.Game.Render`, **never** in the sim that owns `Enemy` — `FS.GG.Game.Core` reaches up to
nothing (ADR-0022 §2). The sim decides what a Brute *is*; the render edge decides what it looks
like.

**`Hp` alone cannot drive `Health`.** `Token.Health` is a **0..1 fraction** and the §5 `Enemy`
record carries no `MaxHp` — only the entity-catalogue table does. So the map needs `maxHpOf`,
a lookup over that table, and passing raw `Hp` would be an out-of-domain `Error`, not a
cosmetic slip. `Threat` is likewise 0..1 with capacity **4**: the catalogue's damage figures
(5/6/8/9/20) are five distinct values and must be quantised to four ranked tiers or
`Legibility.score` warns. The tiers below score `Clean` over the whole roster.

**This map is the "scene boundary" §5 names.** §5 declares this world's own
`Vec2 = { Vx: float32; Vy: float32 }` rather than abbreviating the scaffold's float-based
`Geometry.Vec2`, because the world is `float32` throughout — and its comment says to *convert at
the scene boundary*. `Token.Cx`/`Cy` are `float`, so this ChannelMap **is** that boundary: the
widening happens here, once, and the sim never learns about it. Write `Cx = e.Pos.Vx` and it does
not compile, which is the boundary asserting itself.

**Channels this game does not have, left at their defaults.** `Shield` (no shield concept in
§4), `TokenState` (`Confirmed`/`Suspected` is a spotted-vs-ghost distinction, and this game has
no fog of war — §4.10's light level is a *tile* property, not a per-entity one), and
`SecondaryHeading` (nothing here has a turret). Naming them is the point: a reader learns the
channel set was considered and where it stops, rather than wondering what was overlooked.

```fsharp
// The Enemy → Token ChannelMap. In a product this lives in FS.GG.Game.Render (ADR-0022 §2):
// Symbology depends on Scene, and the sim reaches up to nothing.
type Token = FS.GG.UI.Symbology.Token
type Klass = FS.GG.UI.Symbology.Klass
type Sigil = FS.GG.UI.Symbology.Sigil
type SymFaction = FS.GG.UI.Symbology.Faction
module Sym = FS.GG.UI.Symbology.Symbology

/// The §5 entity catalogue's HP column. `Enemy` carries current `Hp` only, so max lives here —
/// `Token.Health` is a 0..1 fraction and raw Hp is an out-of-domain Error.
let maxHpOf (k: EnemyKind) : float =
    match k with
    | Slime -> 14.0
    | Crawler -> 22.0
    | Bat -> 10.0
    | Skeleton -> 34.0
    | Brute -> 80.0

/// The catalogue's damage column (5/6/8/9/20) quantised to four ranked tiers — `Threat` is 0..1
/// with capacity 4, so five raw levels would overload it.
let threatOf (k: EnemyKind) : float =
    match k with
    | Bat -> 0.25
    | Slime -> 0.5
    | Skeleton -> 0.5
    | Crawler -> 0.75
    | Brute -> 1.0

let klassOf (k: EnemyKind) : Klass =
    match k with
    | Brute -> Klass.Heavy          // 80 hp, heavy knockback, night-only
    | Bat -> Klass.Scout            // the only flyer
    | _ -> Klass.Mobile

let sigilOf (k: EnemyKind) : Sigil =
    match k with
    | Skeleton -> Sigil.Bolt        // the only ranged attacker
    | Brute -> Sigil.Fang
    | _ -> Sigil.Ring

/// Speed px/s (50..130) → three ranked pip tiers. `Speed` is an int in 0..6 with capacity 4;
/// passing px/s raw is an out-of-domain Error.
let speedTierOf (k: EnemyKind) : int =
    match k with
    | Brute | Skeleton -> 1
    | Crawler | Slime -> 2
    | Bat -> 3

/// Three ranked sizes from the §5 catalogue's px dimensions (Bat 28×20 … Brute 40×60). `Size` is
/// Ordered with capacity 4, so this ranks rather than transcribing: a 40×60 Brute must not draw at
/// the same radius as a 28×20 Bat, but 26×44 and 28×40 are the same size to the eye.
let radiusOf (k: EnemyKind) : float =
    match k with
    | Brute -> 32.0                 // 40×60, the night-only tank
    | Crawler | Skeleton -> 22.0    // 28×40, 26×44
    | Slime | Bat -> 16.0           // 32×24, 28×20

/// THIS FUNCTION IS THE SCENE BOUNDARY §5 NAMES. This world is `float32` throughout and declares
/// its own `Vec2 = { Vx: float32; Vy: float32 }`; `Token` is `float`. So the widening happens here,
/// once, at the edge — exactly where §5's comment says to put it — and never leaks into the sim.
let tokenOf (e: Enemy) : Token =
    { Sym.defaultToken with
        Cx = float e.Pos.Vx                             // float32 -> float: the boundary conversion
        Cy = float e.Pos.Vy
        R = radiusOf e.Kind                             // R > 0 or Size is a degenerate Error
        Heading = (match e.Facing with                  // whole-body rotation in Grammar.Token
                   | Left -> System.Math.PI
                   | Right -> 0.0)
        Faction = SymFaction.Enemy
        Klass = klassOf e.Kind
        Sigil = sigilOf e.Kind
        Health = float e.Hp / maxHpOf e.Kind            // 0..1 fraction, NOT raw Hp
        Speed = speedTierOf e.Kind
        Threat = threatOf e.Kind }
```

**Legibility as a test, not a hope.** The map is pure and the roster is data, so "is this
spawn readable" is an ordinary assertion: `Legibility.score (mobs |> List.map tokenOf)` and
check `Verdict = Clean`. Note `Legibility.Severity.Error` must be written qualified — a bare
`Error` would shadow `Result.Error` for every consumer that opens the module.

## 9. UI / HUD / Screens

**Screens:** `Title` (New World w/ seed field, Load slot list, Quit) → `Playing` →
`InventoryOpen` (overlay, sim paused) → `Paused` (Esc) → `Dead` (respawn/quit).

**HUD (Playing):**

- **Hearts bar** top-left: 100 HP as 10 heart icons (or a bar), `12px` from edges.
- **Hunger bar** under hearts: orange fill, label `Hunger`.
- **Hotbar** bottom-center: 10 slots (40×40 px each), selected slot highlighted with a
  `#FFD27A` 2px border, item count bottom-right of each slot, slot number top-left.
- **Clock / day** top-right: a small sun/moon dial showing `DayTime`, plus `Day N` text.
- **Minimap** (`Tab`): top-right inset, 200×200 px, downsampled tile colors + player dot.

**Inventory panel (`E`):** centered, shows the 30-slot main grid + 10 hotbar + the
**crafting list** (recipes the player can currently make given inventory + in-range
station) with input/output icons; clicking a craftable entry crafts one (shift = max).
Drag to move/split stacks; trash slot bottom-right.

**Tooltips:** hovering a slot/recipe shows name, stats (tool power, damage, hunger
restore), and required station.

### 9.1 Menu system (detailed)
A single **menu stack** drives every non-play screen (Title, Settings, Stats, Pause, Death).
Each menu is a vertical list of rows with a cursor, so one small update handler serves them
all and navigation is identical everywhere; the underlying world sim is paused while any
menu is on top.

**Menu tree**
```
Title ─┬─ Continue ────────── resume the most recent saved world (§13)
       ├─ Load World ──────── slot list; loads a saved world (§13)
       ├─ New World ───────── seed field → StartNewWorld (§7.3)
       ├─ Stats ──────────── Stats & Charts screen (§9.2)
       ├─ Settings ──────┬─ Difficulty       ◄ Peaceful · Normal · Hard ►
       │                 ├─ Master volume    ◄ 0 – 100 ►
       │                 ├─ Sound            ◄ On · Off ►
       │                 ├─ Window scale     ◄ 1× · 2× · Fit ►
       │                 ├─ Colorblind assist ◄ On · Off ►
       │                 └─ Back
       └─ Quit

Pause ─┬─ Resume
       ├─ Save World ─────── flush chunk deltas (§13), stay in world
       ├─ Settings ──────── (same submenu; returns to Pause)
       └─ Save & Quit to Title

Death ─┬─ Respawn ────────── softcore: at bed/spawn with item-loss rules (§4.6)
       ├─ View Stats ─────── Stats & Charts (§9.2)
       ├─ New World ──────── abandon this run, seed a fresh world
       └─ Quit to Title
```

**Navigation model**
- `MenuCursor: int` on the active menu; `↑`/`W` decrement, `↓`/`S` increment, both **wrap**.
- `Enter`/`Space` activates the current row; `Esc`/`Back` pops the stack (**Back**).
- **Cycler/slider rows** (Difficulty, Master volume, Sound, Window scale, Colorblind assist):
  `←`/`→` change the value in place; the row shows a right-aligned `◄ value ►` widget.
- Rows reuse the §9 panel style: the selected row is inverted (white box, black text);
  non-selected rows are `#FFFFFF` on the dimmed world at 28 px.

**Msg additions** (extend §7.3):
```fsharp
    | MenuUp | MenuDown              // move cursor (wraps)
    | MenuAdjust of dir:int          // -1 / +1 on a cycler/slider row
    | MenuActivate                   // Enter/Space on the current row
    | MenuBack                       // Esc — pop the menu stack
    | OpenStats | CloseStats         // enter / leave the Stats screen (§9.2)
```

Settings apply live and persist to local config (§13): **Difficulty** selects the §12
tunables preset (Peaceful `baseSpawnCap 0`/`hungerDrain 0`, Normal `6`/`0.5`, Hard
`12`/`1.0`); **Master volume**/**Sound** route to `Audio.setMasterVolume` (§10, clamped
`[0,1]`); **Colorblind assist** swaps the §8.4 ore/tier palette for a CVD-safe variant.

### 9.2 Stats & charts screen
The Stats screen visualizes **the last run** and **lifetime** play. It reads a `RunStats`
snapshot (never live physics), so it is a pure, deterministic render reachable from Title,
Death, and Pause. Chart-design choices below follow the project dataviz conventions
(form-first, validated colorblind-safe categorical palette, single axis, identity by entity).

**Tracked per run** — `RunStats`, accumulated in the `Tick` sim step (§7.5), snapshotted at
death/run-end:

| Field | Type | Updated |
|-------|------|---------|
| `daysSurvived` | `int` | mirrors `DaysSurvived`; bumped on each day wrap (§7.5) |
| `itemsCrafted` | `int` | +1 per successful craft (§12) |
| `resourcesGathered` | `Map<ResGroup,int>` | +count on pickup/mine, keyed Wood/Stone/Ore/Food |
| `structuresBuilt` | `int` | +1 on placing a station/door/chest/wall run (§4.5) |
| `enemiesKilled` | `int` | +1 on each enemy death (§4.7) |
| `deathCause` | `DeathCause option` | set once at death — `Starvation`/`Enemy`/`Fall`/`Environment` |
| `distanceExplored` | `float32` (tiles) | += horizontal distance travelled per tick |

`ResGroup = Wood | Stone | Ore | Food` buckets the raw items of §12 (all metal ores fold
into `Ore`; Apple/Mushroom/Cooked Meat fold into `Food`).

**Lifetime** — `LifetimeStats`, persisted (§13): `longestSurvivalDays`, `worldsPlayed`,
`totalItemsCrafted`, `deathsByCause` (`Map<DeathCause,int>`).

**Layout** (logical 1280×720): a KPI tile row across the top, two charts below.

```
┌──────────────────────────── STATS ────────────────────────────┐
│ ┌ LONGEST  ┐ ┌ ITEMS    ┐ ┌ STRUCTURES ┐ ┌ DEATHS ┐           │  ← KPI stat tiles
│ │ SURVIVAL │ │ CRAFTED  │ │            │ │        │           │
│ │  18 days │ │   341    │ │     27     │ │   9    │           │
│ └──────────┘ └──────────┘ └────────────┘ └────────┘           │
│                                                                │
│  Resources gathered by type       Health vs Hunger (last run)  │
│  ▇▇                           100 ┤╮        ╭─╮      Health     │
│  ▇▇  ▇▇                           │ ╰──╮╭──╯  ╰╮ Hunger         │
│  ▇▇  ▇▇  ▇▇  ▇▇                   │    ╰╯      ╰──               │
│  Wood Stone Ore Food            0 ┼───────────────► day         │
└────────────────────────────────────────────────────────────────┘
     ↑/↓ scope:  ▸ This Run · Lifetime            ESC — Back
```

**Charts** (rendered in Skia with the same draw-list discipline as §8):

1. **Resources-by-type histogram** — *form: per-category magnitude → bars.* x = resource
   group (`Wood, Stone, Ore, Food`), y = count gathered this run. **Single series**, so one
   hue and no legend. Bars are 4 px-rounded at the data end with a 2 px surface gap between
   them. Fill `#2a78d6` (light) / `#3987e5` (dark) — validated categorical slot 1.
2. **Health vs Hunger** — *form: change over an ordered index → line.* x = in-game day, y =
   level `0–100` sampled once per day for the last run; **two series** (Health, Hunger) → a
   legend is present and both lines are direct-labeled at their right end. Health `#2a78d6`,
   Hunger `#1baf7a` (slots 1–2, adjacent-pair CVD-validated). 2 px lines, ≥ 8 px end markers,
   recessive 1 px gridlines in `#3C3C3C`.

Conventions honored: **color follows the entity** (Health is always slot 1, Hunger always
slot 2 — never repainted by the scope toggle); **one axis only** (both series share the same
`0–100` scale, no dual-scale); chart **text uses ink tokens** (`#FFFFFF` primary / `#C3C2B7`
muted), never the series hue; layout is **fixed and deterministic**, so a fixed-seed run
(§13) renders byte-identical for snapshot tests. The `↑/↓` **scope** toggle swaps the data
source This-Run ↔ Lifetime without changing colors.

**Model/Msg hooks:** add `RunStats: RunStats` and `Lifetime: LifetimeStats` to the §7.2
`Model` record; accumulate them in the `Tick` sim step (§7.5) — bump `itemsCrafted` on craft,
`resourcesGathered` on pickup/mine, `structuresBuilt` on placement, `enemiesKilled` on enemy
death, and append the per-day Health/Hunger sample for chart 2 on each day wrap. On death,
set `deathCause`, fold `RunStats` into `Lifetime`, and persist (§13). `OpenStats`/`CloseStats`
switch `Ui: UiState` to a `Stats` screen; the render is a no-op on physics.

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
| Mining hit, per tile material (§4.3) | `Audio.playSfx (SoundId "mine-hit") 0.5` | `mine-hit` | dull/clink/metallic by group |
| Tile breaks (§4.3) | `Audio.playSfx (SoundId "tile-break") 0.7` | `tile-break` | crumble pop |
| Place tile (§4.5) | `Audio.playSfx (SoundId "tile-place") 0.6` | `tile-place` | soft thud |
| Pick up item (§4.4) | `Audio.playSfx (SoundId "item-pickup") 0.4` | `item-pickup` | light chime |
| Craft success (§12) | `Audio.playSfx (SoundId "craft-success") 0.7` | `craft-success` | tool-clatter |
| Player hurt (§4.6) | `Audio.playSfx (SoundId "player-hurt") 0.8` | `player-hurt` | grunt |
| Enemy hurt / die (§4.7) | `Audio.playSfx (SoundId "enemy-hit") 0.7` | `enemy-hit` | squish / shatter |
| Jump / land (§4.1) | `Audio.playSfx (SoundId "jump-land") 0.5` | `jump-land` | whoosh / thud |
| Eat food (§4.6) | `Audio.playSfx (SoundId "eat-food") 0.6` | `eat-food` | crunch |
| Open/close inventory (§9) | `Audio.playSfx (SoundId "inventory-flap") 0.5` | `inventory-flap` | leather flap |
| Low health, HP<20 (§4.6) | `Audio.playMusic (TrackId "heartbeat") true` | `heartbeat` | heartbeat loop |

**Music** runs through `Audio.playMusic (… true)`: a calm **Day** loop and a tense **Night**
loop switch on the day/night phase transition (§4.8), while an ambient **Cave** loop takes
over when the player descends into the deep band (§6). The active track is swapped with a
fresh `Audio.playMusic` on each phase/band change (cross-fade over 2 s at the backend), and
`Audio.stopMusic` clears it on death, which also fires a short **Death** sting via
`Audio.playSfx (SoundId "death-sting") 0.8`; the low-health heartbeat loop above is likewise
stopped with `Audio.stopMusic` once HP recovers to ≥ 20. The settings **mute** toggle maps to
`Audio.setMasterVolume` (`0.0` to mute, `1.0` at full). **Testing:** collect the frame's
`AudioEffect`s, `Audio.interpret` them, and assert the `AudioEvidence.Requested` sequence for
representative events (e.g. breaking a Stone tile requests exactly `PlaySfx (SoundId "tile-break", _)`).

## 11. Win / Loss / Scoring

There is no terminal win. **Progression goals** (used as the "score"/achievement spine):

| Tier goal | Requirement |
|---|---|
| T1 Settler | Craft Workbench + Wood tools, survive Night 1 |
| T2 Miner | Craft Furnace, smelt Copper/Iron, Stone→Iron tools |
| T3 Delver | Craft Anvil, reach Caverns, Iron armor |
| T4 Deepcore | Diamond Drill, reach Deep band, defeat a Brute |

**Score (optional leaderboard metric):** `score = 100·daysSurvived + 50·tierReached +
Σ(rare ore mined) − 25·deaths`.

**Loss:** HP ≤ 0. **Softcore (default):** respawn at bed/spawn, drop 50% of coins/items
as world drops at death site (recoverable). **Hardcore (toggle):** world becomes
read-only "tombstone"; run ends.

## 12. Difficulty & Balancing

All values data-driven (loaded from a tunables record; defaults shown). Range = sane
designer bounds.

| Param | Default | Range | Effect |
|---|---|---|---|
| `dayLengthSeconds` | 840 | 300–1800 | Pace of day/night |
| `gravity` | 1800 | 1200–2600 | Jump feel / fall speed |
| `walkSpeed` | 180 | 120–260 | Traversal pace |
| `jumpVelocity` | 620 | 480–760 | Jump height |
| `baseSpawnCap` | 6 | 0–20 | Night pressure floor |
| `spawnCapPerDay` | 1.5 | 0–4 | Difficulty ramp slope |
| `spawnInterval` | 1.0 s | 0.25–4 | Spawn frequency |
| `hungerDrain` | 0.5 /s | 0–2 | Survival pressure |
| `regenRate` | 1.0 HP/s | 0–5 | Forgiveness |
| `deathDropPct` | 0.5 | 0–1 | Death penalty |
| `oreRichness` | 1.0 | 0.5–2 | Ore vein density multiplier |
| `caveDensity` | 1.0 | 0.5–2 | Cave noise threshold |
| `magnetRadius` | 64 px | 0–160 | Pickup convenience |
| `mineToolPowerMult` | 1.0 | 0.5–3 | Mining speed global |

**Resource / item table (excerpt):**

| Item | Stack | Notes / use |
|---|---|---|
| Dirt | 999 | Build, fills holes |
| Stone | 999 | Furnace input, stone tools, walls |
| Wood | 999 | Workbench, wood tools, platforms, torches |
| Coal | 999 | Furnace fuel, torch craft |
| Copper/Iron/Silver/Gold Ore | 999 | Smelt → bars |
| Bar (per metal) | 999 | Tools, armor, anvil recipes |
| Gem | 99 | High-value, jewelry/lighting |
| Torch | 99 | Light source (place) |
| Apple / Mushroom | 99 | Food: +20 / +12 hunger |
| Cooked Meat | 99 | Food: +45 hunger |
| Health Potion | 30 | +40 HP instant |
| Wood/Stone/Iron Pickaxe | 1 | Tool tiers (§4.3) |
| Sword (wood→iron) | 1 | Melee, damage 8/14/22 |
| Bow + Arrow | 1 / 999 | Ranged |

**Crafting recipes (excerpt)** — inputs → output (station required):

| Output | Inputs | Station |
|---|---|---|
| Workbench | 10 Wood | (hand) |
| Wood Pickaxe | 6 Wood | Workbench |
| Torch (×4) | 1 Wood + 1 Coal | (hand) |
| Furnace | 20 Stone + 4 Wood | Workbench |
| Copper Bar | 3 Copper Ore + 1 Coal | Furnace |
| Iron Bar | 3 Iron Ore + 1 Coal | Furnace |
| Anvil | 10 Iron Bar | Workbench |
| Iron Pickaxe | 12 Iron Bar + 4 Wood | Anvil |
| Iron Sword | 8 Iron Bar + 2 Wood | Anvil |
| Iron Helmet/Chest/Boots | 8/15/6 Iron Bar | Anvil |
| Diamond Drill | 18 Iron Bar + 5 Gem | Anvil |
| Cooked Meat | 1 Raw Meat | Cookpot |
| Health Potion | 2 Gel + 1 Mushroom | (hand) |
| Door | 6 Wood | Workbench |
| Chest | 8 Wood | Workbench |

## 13. Technical Notes

**Performance budget (target 60 FPS = 16.7 ms/frame):**

- **Sim step ≤ 4 ms:** physics is O(active entities) with cap 30 enemies + ~80 drops +
  ~40 projectiles ≈ 150 dynamic bodies, each O(1) tile collision. Mining/lighting work is
  bounded to the changed region. Streaming gen is **async** (off-thread) and never blocks
  the tick — chunks pop in via `ChunkGenerated`.
- **Render ≤ 10 ms:** ~9 cached chunk surface blits (layers 3–4) + ~150 dynamic quads +
  lighting multiply + HUD. Chunk surface re-render only on `Version` bump.
- **Memory:** a loaded chunk ≈ 4096 tiles + 4096 walls (1 byte tag each) + 4096 light
  floats ≈ 24 KB data + a 2048² surface (~16 MB GPU if RGBA8 — so cap **resident chunk
  surfaces to the visible+margin set**, ~12–16, and recycle the rest). Off-screen chunk
  *data* may stay cached; their *surfaces* are evicted.

**Fixed vs. variable timestep:** **fixed** 1/60 simulation, drained by
`FixedStep.drain (1.0/60.0) frameTime acc` → `struct (steps, acc')` — it banks the real time, returns
the whole steps this frame owes, and caps the frame internally so a chunk-generation hitch cannot
spiral into a catch-up storm. Do not hand-roll the accumulator. Rendering is variable and interpolates
entity positions between the last two sim states for smoothness (store `PrevPos` per dynamic entity;
render at `lerp(prev, cur, alpha)`).

**Determinism / RNG:** two seeded streams. **Worldgen RNG** is a pure function of the
world `Seed` and tile coordinates (use a hashed-coordinate noise so any tile/chunk is
reproducible regardless of generation order — *not* a single sequential PRNG, so streaming
order can't change the world). **Sim RNG** is `FS.GG.Game.Core`'s **`Rng`** — which *is* the
"small splitmix" this spec used to describe hand-rolling, so declare `Rng: Rng` and use it: seed once
with `Rng.ofSeed`, advance it only inside the ordered tick, and a recorded input log replays
identically. It is a value, so threading it through the Model is the natural thing rather than a
discipline. If you want the worldgen stream kept genuinely independent of the sim stream,
`Rng.split` gives you two that cannot perturb each other. Floats in sim are `float32` with the fixed dt to bound divergence.

**World generation pipeline (deterministic from `Seed`):**

1. **Heightmap:** 1D fractal value-noise over X → surface Y per column (amplitude ~25
   tiles, multi-octave); biome noise selects surface material/tree set.
2. **Strata fill:** below surface fill Dirt→Stone by depth band; sprinkle Sand in desert.
3. **Caves:** 2D Perlin/Worley noise; tiles where `noise > caveThreshold(depth)` become
   Air; threshold eases with depth so caverns are bigger deeper. Connect with a few
   tunneling walks seeded by coordinate hash.
4. **Ore veins:** for each ore, a 3D-ish blob noise gated by depth band + `oreRichness`
   places veins (e.g. iron only below Y=120, gold below Y=480). Vein = small flood blob
   of 4–16 tiles.
5. **Liquids:** fill enclosed low pockets in caverns with Water; lava pools in Deep.
6. **Decor / structures:** trees on grass, cacti in desert, rare floating islands, the
   occasional small chest cave with loot.
7. **Spawn point:** flat-ish surface column near world center; clear a 6×6 air pocket.

A chunk is generated by evaluating steps 1–6 only for its tile coordinates — pure and
order-independent, satisfying the determinism acceptance test.

**Persistence (save/load):**

- Save format: header `{ seed, width, height, day, daysSurvived, player, inventory,
  time, chests }` + a **chunk delta store**: only chunks whose tiles differ from the
  deterministic worldgen baseline are serialized (RLE-compressed `TileType` runs per
  chunk, foreground + walls). Pristine chunks are regenerated from `seed` on load → tiny
  saves. Enemies/drops/projectiles are **not** persisted (re-spawn on load).
- Writing is async (`Cmd` → background); a save lock prevents mutation mid-write by
  snapshotting dirty chunk deltas under the update thread, then compressing off-thread.
- **Round-trip guarantee:** `load(save(model))` reproduces identical world tiles, player,
  inventory, time, and chests (modulo non-persisted transient entities).

**Edge cases:** placing a block on yourself (blocked); mining the tile you stand on
(allowed, you fall); falling out of the world bottom (clamp at Bedrockish, never below);
inventory full on pickup (drop persists); crafting with exactly enough materials
(consumed, no negative counts); two players' worth of input in one frame (N/A, single
player); chunk gen lag (player on ungenerated chunk → treated as solid until generated to
avoid falling through); save during low memory (stream chunk deltas, don't hold all).

## 14. Acceptance Criteria (test scenarios)

1. **Worldgen determinism (seed).**
   **Given** a new world created with seed `12345`,
   **When** I read the `TileType` at tile `(2100, 350)` and at `(2100, 350)` again after
   regenerating that chunk from the same seed (or in a second fresh world with seed
   `12345`),
   **Then** both reads return the **identical** `TileType`, and a world with seed `99999`
   differs in at least one of a sampled 1000-tile set (determinism + seed sensitivity).

2. **Mining a block yields a drop and clears the tile.**
   **Given** the player holds a Stone Pickaxe (tier 2) and targets a Stone tile (H=10) at a
   reachable, adjacent tile,
   **When** LMB is held for `H / toolPower = 10/10 = 1.0 s` of `Tick`s,
   **Then** the tile becomes `Air`, the chunk's `Version` increments and `Dirty=true`, and
   exactly one `Stone` `DroppedItem` spawns at the tile center.

3. **Tool-tier gate blocks under-tier mining.**
   **Given** the player holds a Wood Pickaxe (tier 1) targeting an Iron tile (tier 2),
   **When** LMB is held for 3 s,
   **Then** mining progress stays `0`, the tile is unchanged, and the tile shows the
   tier-fail flash; equipping a Stone Pickaxe then mines it normally.

4. **Drop pickup into inventory.**
   **Given** a `Stone` drop rests 40 px from the player and the player has an empty/partial
   Stone slot,
   **When** the next ticks run (within magnet radius 64 px),
   **Then** the drop accelerates to the player and is removed from the world, and the
   Stone stack count increases by exactly 1.

5. **Crafting consumes inputs and produces output.**
   **Given** the inventory holds 10 Wood and no Workbench exists, UI shows "Workbench"
   craftable,
   **When** the player crafts Workbench,
   **Then** Wood decreases by 10 and one Workbench item is added; **and given** 6 Wood with
   a Workbench within range, crafting Wood Pickaxe removes 6 Wood and adds 1 Wood Pickaxe;
   **and** attempting to craft with only 5 Wood is rejected with no state change.

6. **Tilemap collision (no tunneling).**
   **Given** the player stands on a solid floor with a solid wall directly to the right,
   **When** `D` is held for 2 s at walk speed,
   **Then** the player's X position never enters a solid tile (AABB stays outside), `vx`
   is zeroed at the wall, and `grounded` stays true; **and** a player falling at terminal
   velocity onto a 1-tile-thick floor lands on top of it without passing through.

7. **Jump height & coyote/buffer.**
   **Given** the player is grounded,
   **When** jump is pressed and held,
   **Then** peak height is `≈107 px` (within ±4 px); **and** pressing jump within 6 frames
   after walking off a ledge still produces a jump (coyote); **and** a short-tap releases
   early and peaks lower than a full hold.

8. **Night enemy spawn (and day suppression).**
   **Given** `DayTime` enters the Night phase (`t ≥ 0.5`) and `enemyCount < cap`,
   **When** ~1 s of ticks elapse with a valid dark, off-screen, floored spawn column in the
   active set,
   **Then** at least one surface enemy spawns within 240–640 px, off-screen; **and** when
   `DayTime` returns to Day, no new surface enemies spawn and exposed surface enemies
   despawn/flee, while cave (dark) enemies may still spawn.

9. **Enemy contact damage + invulnerability.**
   **Given** a Crawler overlaps the player AABB,
   **When** the overlap occurs,
   **Then** the player loses 9 HP once, gains a 0.7 s invuln window during which further
   overlaps deal 0 damage, and receives knockback; after the window a new overlap damages
   again.

10. **Hunger & starvation.**
    **Given** hunger is at 1 and HP at 100,
    **When** ticks run until hunger reaches 0 and 5 more seconds pass,
    **Then** HP has dropped by `≈10` (2 HP/s starvation) and HP regen does not trigger;
    eating an Apple raises hunger by 20 and (after 5 s damage-free, hunger>50) regen
    resumes.

11. **Placement validity.**
    **Given** the player selects Dirt and aims at an `Air` tile adjacent to a solid tile
    within reach,
    **When** RMB is clicked,
    **Then** the tile becomes Dirt and the Dirt stack decreases by 1; **and** clicking on a
    tile overlapping the player AABB, or out of reach, or with no solid neighbor, performs
    no placement and consumes no item.

12. **Save/load round-trip.**
    **Given** a world (seed `7`) where the player has mined a 5×5 hole, placed 3 torches,
    stored 12 Iron in a chest, and it is Day 3 at `DayTime=0.62`,
    **When** the world is saved to slot 1 and then loaded into a fresh model,
    **Then** the loaded world has the identical hole (those tiles = Air), the 3 torches,
    the chest containing 12 Iron, player position/HP/hunger/inventory, `Day=3`,
    `DayTime≈0.62` (within one tick), and all **pristine** (unmodified) chunks regenerate
    identically from seed `7`.

13. **Chunk streaming correctness.**
    **Given** the player walks 200 tiles east,
    **When** chunks enter/leave the active set,
    **Then** newly visible chunks are generated/loaded before they render (no falling
    through ungenerated tiles), unloaded chunks' surfaces are evicted, and tile reads in
    the visible region are always valid.

14. **Fixed-timestep determinism (input replay).**
    **Given** a recorded input log over 600 ticks from seed `42`,
    **When** the log is replayed twice from the same initial model,
    **Then** the resulting player position, inventory, and enemy positions are bit-identical
    across both runs (deterministic sim).

15. **Death and respawn.**
    **Given** softcore mode, a placed valid bed, and the player at 5 HP,
    **When** the player takes lethal damage,
    **Then** after a 3 s fade the player respawns at the bed with full HP, 50% of dropped
    items appear as recoverable world drops at the death site, and `deaths` increments.

## 15. Stretch Goals

Ranked, out of scope for v1:

1. **Full dynamic lighting** (§4.10/§8.5) enabled by default with colored light.
2. **Liquids simulation** — flowing water/lava with pressure, not just static pockets.
3. **Bosses & events** — a summonable boss, "blood moon" high-spawn nights.
4. **Background trees/furniture & wiring** — multi-layer decoration, logic gates/wiring.
5. **NPC villagers** that move into player-built valid houses and sell goods.
6. **More biomes & depth layers** — corruption/hallow spread, an underworld with lava sea.
7. **Multiplayer** — the `WorldEvent` stream and chunk deltas are designed to extend here.
8. **Map/quest progression**, accessory slots, buff system, fishing.
9. **Procedural structures** — dungeons, abandoned mineshafts with loot and traps.
10. **Controller support & remappable bindings.**
