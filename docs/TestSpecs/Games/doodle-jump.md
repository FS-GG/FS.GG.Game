---
title: "Doodle Jump"
slug: doodle-jump
category: games
complexity: simple
genre: "Vertical auto-bounce platformer (endless climber)"
target_session_minutes: 3
stack: { rendering: "FS.GG.Rendering (Skia/OpenGL)", framework: "FS.GG.Game.Core (FixedStep for the tick; Rng for procedural generation)", arch: "Elmish/MVU", lang: "F#" }
status: spec
---

# Doodle Jump

## 1. Overview
You are a perpetually bouncing doodle creature climbing an endless vertical tower of
floating platforms. The character **auto-bounces** on every platform it lands on — the
player never presses a jump button. The only control is **steering left/right** (tilt or
arrow keys) to line the doodle up with the next platform as the camera scrolls relentlessly
upward. The fantasy is effortless, springy ascent punctuated by white-knuckle moments of
"will I reach the next platform?" Fun comes from the tension between the fixed bounce rhythm,
gravity pulling you back down, and platforms that move, crumble, or fling you skyward. Miss
your footing and fall off the bottom of the screen — game over. Your score is the highest
altitude you reach.

## 2. Core Game Loop
**Moment-to-moment:** `fall toward platform → steer left/right to align → land → auto-bounce
up → rise → fall again → repeat`. Higher up, the player weaves between sparse/moving platforms,
grabs springs and jetpacks for big boosts, and dodges enemies.

**Session-level:** `Title → tap/press to start → climb (camera scrolls up, score = max height)
→ fall below camera OR killed by enemy → Game Over (show score + best) → restart`. A run is
short and replayable, typically 1–4 minutes.

## 3. Controls & Input
Input is **steer-only**; bouncing is automatic. Horizontal movement is a *held* input
(velocity applied while the key is down), not edge-triggered.

| Input | Action | Model |
|---|---|---|
| `Left Arrow` / `A` | Move doodle left | Held (velocity while down) |
| `Right Arrow` / `D` | Move doodle right | Held (velocity while down) |
| Mouse X / pointer drag | Set horizontal target (optional alt control) | Continuous |
| `Space` / `Enter` | Start game (Title), Restart (Game Over) | Edge-triggered (pressed) |
| `Esc` / `P` | Pause / resume | Edge-triggered (pressed) |
| `Shoot` — `Up Arrow` / `W` / click *(stretch)* | Fire projectile upward at enemies | Edge-triggered |

"Tilt" semantics: on touch/accelerometer targets, tilt maps to the same horizontal velocity
axis as Left/Right; the spec is written against keyboard as primary.

## 4. Mechanics (detailed)

### 4.1 Coordinate system & units
Logical playfield is **720 × 1280 px (portrait)**. World Y increases **downward** in screen
space, but for gameplay we track a **world height** that increases as the player climbs. To
avoid confusion, define **`worldY`** with **up = negative** (standard physics): the doodle
starts at `worldY = 0` and ascends to increasingly negative `worldY`. Altitude (for score) is
`altitude = -worldY` clamped to its max. All px/s and px/s² constants below use this frame.

### 4.2 Gravity & vertical motion
- **Gravity `g = 2400 px/s²`**, applied to vertical velocity every tick: `vy += g * dt`.
- **Terminal fall velocity `vyMax = 1600 px/s`** (clamp downward speed).
- Vertical position integrates: `worldY += vy * dt` (with up negative, an upward bounce sets
  `vy` to a large negative value, then gravity decays it back toward 0 and positive).

### 4.3 Auto-bounce (the core rule)
- Bounce happens **only when the doodle is moving downward** (`vy > 0`) **and** its feet
  overlap the top surface of a platform within a contact band (see 4.5).
- On a normal bounce, set **`vy = -1150 px/s`** (upward impulse). This is independent of impact
  speed (fixed rhythm) — Doodle Jump's signature feel.
- Bounce never triggers while ascending (`vy <= 0`), so the doodle passes *through* platforms
  from below — platforms are **one-way (top-only) colliders**.
- Apex height of a normal bounce above the platform: `vy² / (2g) = 1150² / 4800 ≈ 275 px`.
  Tune platform vertical spacing against this (see 6.x).

### 4.4 Horizontal movement & screen-wrap
- **Move speed `vxMove = 520 px/s`** applied directly while a steer key is held (arcade feel,
  not momentum-heavy). Optional light smoothing: lerp current `vx` toward target at
  `accel = 4000 px/s²` so direction changes feel responsive but not instant.
- No horizontal friction beyond releasing the key (target vx → 0).
- **Screen wrap:** the doodle's X wraps horizontally. If `x < -doodleHalfW`, set
  `x = 720 + doodleHalfW`; if `x > 720 + doodleHalfW`, set `x = -doodleHalfW`. The doodle
  visually re-enters from the opposite edge.

### 4.5 Collision (one-way platform landing)
- Doodle collision: an **AABB feet sensor** = bottom 10 px of the doodle, width = doodle width.
- Platform top surface: a horizontal line segment of width = platform width at the platform's
  top edge.
- **Land condition (per tick):** `vy > 0` AND the feet sensor's previous-frame bottom was at or
  above the platform top AND this-frame bottom is at or below platform top + `contactBand`
  (`contactBand = 12 px`) AND horizontal overlap exists. Use swept previous→current Y to avoid
  tunneling at high `vy`.
- On land: snap doodle feet to platform top, trigger bounce (4.3) or platform-specific effect
  (4.7), apply platform side-effects (e.g. break).

### 4.6 Camera (scroll up, never down)
- The camera tracks the player so the doodle sits around **40% from the top** of the screen
  when ascending.
- **`cameraY` only ever decreases** (moves up in world space). Define a **`maxClimb`** = the
  smallest (most negative) `worldY` the doodle has reached. `cameraY = maxClimb - 0.40*1280`
  is the desired top-of-view; the camera lerps toward it but **never scrolls back down** even
  if the doodle falls.
- Because the camera never follows the doodle down, falling lets the doodle drop off the bottom
  of the visible screen → death (see 11).

### 4.7 Procedural platform types
All platforms are **96 × 22 px** unless noted. Spawned above the camera as the player climbs.
- **Static** (green) — default. Normal bounce.
- **Moving** (blue) — translates horizontally at `±90 px/s`, bouncing between screen edges
  (or within a patrol span of 200 px). Normal bounce while moving; doodle does not inherit
  horizontal velocity.
- **Breakable** (brown) — bounces the doodle **zero times**: on contact it **breaks** (plays
  break animation, removed after 0.25 s) and the doodle continues falling through (no impulse).
  Forces the player to find a real platform.
- **Vanishing/one-shot** (white, *stretch tier*) — gives one normal bounce, then disappears.
- **Spring platform** (static green with a coil) — on land, instead of the normal impulse,
  apply **`vy = -1900 px/s`** (super bounce; apex ≈ 752 px). ~8% of platforms carry a spring.
- **Jetpack pickup** (item on/near a platform, not a platform itself) — see 5; grants timed
  thrust.

### 4.8 Spawn density thinning with height
- Platforms are generated in vertical bands as the camera rises. Maintain a generation cursor
  just above the highest spawned platform.
- **Vertical gap between successive platforms** scales with altitude:
  `gap = clamp(baseGap + altitude * gapGrowth, baseGap, maxGap)` where `baseGap = 90 px`,
  `gapGrowth = 0.012` (px of gap per px of altitude), `maxGap = 230 px`. (At altitude 0, gap
  ≈ 90 px; by ~11,600 px altitude, gap saturates at 230 px — just under the normal apex of
  275 px, so the climb is always *possible* but tight.)
- Each new platform's X is `rand(0, 720 - platformWidth)`, with a constraint that consecutive
  platforms differ in X by at least 40 px (avoid stacking) and at most 360 px (always
  reachable given screen wrap).
- **Type weights also shift with altitude** (see 12).

### 4.9 Enemies / obstacles
- **Enemy (UFO/monster)**, **40 × 36 px**, spawns floating at fixed or slowly drifting world
  positions starting at altitude ≥ 3000 px, frequency rising with height.
- **Lethal contact:** if the doodle's body AABB overlaps an enemy AABB **and** the doodle is
  *not* bouncing on the enemy's head, the doodle dies instantly.
- **Stomp:** if `vy > 0` (falling) and the doodle's feet sensor hits the **top** of the enemy,
  the enemy is destroyed and the doodle gets a normal bounce off it (treat enemy top as a
  platform for that tick). Springs/jetpacks pass through enemies harmlessly while active
  (*optional*).
- **Black hole obstacle** (*stretch*): instant death on overlap regardless of state.

## 5. Entities / Game Objects

| Entity | Size (px) | HP | Speed | Notes |
|---|---|---|---|---|
| Doodle (player) | 54 × 60 | 1 | vx ≤ 520, vy clamp 1600 | Feet sensor bottom 10 px; one-way collider; screen-wraps |
| Static platform | 96 × 22 | n/a | 0 | Normal bounce |
| Moving platform | 96 × 22 | n/a | 90 | Horizontal patrol |
| Breakable platform | 96 × 22 | 1 hit | 0 | Breaks on contact, no bounce |
| Spring platform | 96 × 22 | n/a | 0 | Super bounce vy = -1900 |
| Jetpack pickup | 36 × 48 | n/a | 0 | Timed thrust on grab |
| Spring item *(alt)* | 30 × 18 | n/a | 0 | Same as spring platform effect |
| Enemy | 40 × 36 | 1 | 0–60 drift | Lethal on body contact; stompable on head |
| Projectile *(stretch)* | 8 × 8 | n/a | 900 up | Destroys enemies |

**State machines:**
- **Doodle:** `Rising → Falling → (Landed → bounce) → Rising …`; terminal `Dead`. `Jetpack`
  is an overlay state lasting `jetpackDuration = 2.2 s` that overrides vertical control: set
  `vy = -2200 px/s` sustained, ignore platform bounces, disable gravity until it ends, then
  resume `Falling`.
- **Breakable platform:** `Intact → (contact) → Breaking (0.25 s anim) → Removed`.
- **Enemy:** `Alive → (stomped | off-screen recycled) → Dead`.

**Creation/destruction:** platforms/enemies are spawned by the generator above `cameraY`
(4.8) and **culled** once they fall below `cameraY + 1280 + 100 px` (well off the bottom).
The doodle is created once at run start.

```fsharp
open FS.GG.Game.Core
// Positions/velocities live in the scaffold's collision-safe Geometry.Vec2 ({ Vx; Vy }, from
// src/<ProductDir>/Vec2.fs) — NEVER a record you label X/Y/Width/Height, which collide with
// Scene's Point/Rect. This is a type ABBREVIATION: it adds no labels, so nothing can collide.
type Vec2 = Geometry.Vec2

type PlatformKind =
    | Static
    | Moving of vx: float * patrolMinX: float * patrolMaxX: float
    | Breakable
    | Spring

type Platform =
    { Id: int
      Kind: PlatformKind
      Pos: Vec2           // Vx = left edge; Vy = worldY of top surface (up = negative)
      WidthPx: float      // default 96
      Broken: bool
      BreakTimer: float } // seconds remaining in Breaking state

type PickupKind = Jetpack
type Pickup = { Id: int; Kind: PickupKind; Pos: Vec2; Taken: bool }

type Enemy =
    { Id: int; Pos: Vec2; WidthPx: float; HeightPx: float; DriftVx: float; Alive: bool }
```

Positions live in the scaffold's `Geometry.Vec2` (`Vx`/`Vy`), and sizes carry `…Px` labels — never
`X`/`Y`/`Width`/`Height`, which collide with `Scene`'s `Point`/`Rect` and blow up in the durable
`LayoutEvidence.fs`. Cross into the scene with the scaffold's crossings — qualified, since these
sketches abbreviate `Geometry.Vec2` rather than opening `Geometry`: e.g.
`Geometry.toRect enemy.Pos enemy.WidthPx enemy.HeightPx`, `Geometry.toPoint doodle.Pos`.

## 6. World / Levels / Progression
- **Playfield:** 720 × 1280 logical px (portrait), letter/pillarboxed to the window aspect.
- **No discrete levels** — single endless vertical world. "Difficulty" is purely a function of
  **altitude** (= `-worldY` of the doodle's max).
- **Start state:** doodle resting on a guaranteed full-width starter platform at `worldY = 0`,
  centered X. The first ~6 platforms are all **Static** with `gap = 90 px` to teach the rhythm.
- **Progression knobs vs. altitude:**
  - Platform gap widens (4.8) → 90 → 230 px.
  - Moving-platform weight rises; breakable weight rises; static weight falls (12).
  - Enemies begin at 3000 px and grow more frequent.
  - Jetpack/spring frequency stays roughly constant (rare relief).
- **Milestone feedback:** subtle background hue shift every 5000 px of altitude (cosmetic).

## 7. State Model (Elmish/MVU)

```fsharp
open FS.GG.Game.Core
// Positions/velocities live in the scaffold's collision-safe Geometry.Vec2 ({ Vx; Vy }, from
// src/<ProductDir>/Vec2.fs) — NEVER a record you label X/Y/Width/Height, which collide with
// Scene's Point/Rect. This is a type ABBREVIATION: it adds no labels, so nothing can collide.
type Vec2 = Geometry.Vec2

type Phase = Title | Playing | Paused | GameOver

type DoodleState = Rising | Falling | Jetpack | Dead

type Doodle =
    { Pos: Vec2                     // Vy = worldY, up = negative
      Vel: Vec2                     // px/s
      FacingRight: bool
      State: DoodleState
      JetpackTimer: float }

type Model =
    { Phase: Phase
      Doodle: Doodle
      Platforms: Platform list
      Pickups: Pickup list
      Enemies: Enemy list
      CameraY: float                // top-of-view worldY; only ever decreases
      MaxClimb: float               // most-negative worldY reached (for score/camera)
      Score: int                    // = int (-MaxClimb) (altitude)
      Best: int
      SpawnCursorY: float           // worldY above which we still need to generate
      NextId: int
      Rng: Rng                // FS.GG.Game.Core — a VALUE, so the Model stays one (§13)
      InputLeft: bool
      InputRight: bool
      ElapsedMs: float }

type Msg =
    | StartGame
    | Tick of dt: float             // seconds since last frame (~0.0167)
    | SteerLeft of pressed: bool    // key down / up
    | SteerRight of pressed: bool
    | TogglePause
    | Restart
    | Shoot                         // stretch
```

**update — important cases:**
- `StartGame` / `Restart`: build a fresh `Model` with the starter platform, RNG seeded
  (13), `Phase = Playing`.
- `SteerLeft/Right pressed`: set `InputLeft/InputRight`; movement is applied in `Tick`.
- `TogglePause`: `Playing ↔ Paused` (Tick is a no-op while `Paused`).
- `Tick dt` (the simulation step, only when `Playing`):
  1. Compute target `vx` from input; integrate `vx` (4.4) and horizontal screen-wrap.
  2. Apply gravity → `vy` (clamp to ±1600); integrate `worldY`.
  3. **Swept collision** vs. platform tops (4.5); on land, apply bounce / spring / break.
  4. Check pickups (jetpack) and enemies (lethal vs. stomp).
  5. Update moving platforms, breakable timers, enemy drift; cull off-screen entities below
     `cameraY + 1380`.
  6. Update `MaxClimb`, `Score`, and `CameraY` (lerp up only, 4.6).
  7. Generate new platforms/enemies above `SpawnCursorY` until it's above the camera
     (4.8, 12).
  8. **Death check:** if doodle's top edge `> cameraY + 1280` (fell off bottom) → `Dead`,
     `Phase = GameOver`, update `Best`.

**view:** pure function of `Model` → a Skia draw list (see 8). No mutation, no simulation in
the view; it reads `CameraY` to transform world→screen.

**Subscriptions:**
- A render/sim timer at **60 FPS** dispatching `Tick dt` with `dt` in seconds (clamped, 13).
- Keyboard subscription dispatching `SteerLeft/Right (down/up)`, `StartGame`, `TogglePause`,
  `Restart`, `Shoot`.

## 8. Rendering (Skia 2D)
**World→screen transform:** `screenY = worldY - cameraY`; `screenX = worldX`. Cull anything
with `screenY` outside `[-100, 1380]`.

**Draw order (back → front):**
1. **Background** — vertical gradient (top `#1B2A4A` → bottom `#2E4372`), hue-shifted slowly by
   altitude. Optional parallax dots/clouds at 0.3× scroll.
2. **Platforms** — rounded rects, 8 px corner radius:
   - Static `#5BBF5B`, Moving `#4A90E2`, Breakable `#9B6B3F` (with crack overlay when
     `Breaking`), Spring `#5BBF5B` + a gray coil glyph `#BBBBBB`.
3. **Pickups** — jetpack `#F5A623` with flame; spring item `#CCCCCC`.
4. **Enemies** — `#C0392B` body, simple eyes; flash white 1 frame when stomped.
5. **Doodle** — `54×60` green `#7ED321` blob; flip horizontally by `FacingRight`; squash/stretch
   on bounce (scale Y 1.15 for 80 ms after a bounce). Jetpack flame trail while in `Jetpack`.
6. **Particles** — break debris (brown shards), spring sparkles, jetpack smoke.
7. **HUD** — score top-left, drawn in screen space (not world-transformed).

**Fonts:** sans-serif bold; score 40 px, game-over title 64 px. **Colors** as hex above.
**Redraw strategy:** full-frame redraw every tick (clear → draw list); the scene is small
enough (<120 entities) that partial invalidation isn't needed. Coordinate origin top-left,
y-down on screen.

## 9. UI / HUD / Screens
- **Title:** game name centered, "Press Space to Start", best score, a looping idle bounce
  animation of the doodle.
- **Playing HUD:** **Score** (current altitude, integer) top-left at `(16, 16)`, 40 px.
  Optional small **Best** under it. Jetpack remaining-time bar near top when active.
- **Pause:** dim overlay (`#000000` at 50% alpha) + "Paused — press Esc to resume" centered.
- **Game Over:** dim overlay, "Game Over" 64 px centered, final **Score** and **Best** below,
  "Press Space to Restart". If a new best, show "New Best!" badge.

### 9.1 Menu system (detailed)
A single **menu stack** drives every non-play screen (Title, Settings, Stats, Pause, Run
Over). Each menu is a vertical list of rows with a cursor, so one small update handler serves
them all and navigation is identical everywhere. The game is endless with no lives, so the
death screen is framed as **Run Over** and its primary action is **New Run**, not a life-loss
continue.

**Menu tree**
```
Title ─┬─ New Run ─────────── seed a fresh run and start climbing (§7 StartGame)
       ├─ Stats ──────────── Stats & Charts screen (§9.2)
       ├─ Settings ───────┬─ Difficulty     ◄ Easy · Normal · Hard ►
       │                  ├─ Master volume  ◄ 0 – 100 ►
       │                  ├─ Sound          ◄ On · Off ►
       │                  ├─ Window scale   ◄ 1× · 2× · Fit ►
       │                  ├─ Screen shake   ◄ On · Off ►
       │                  └─ Back
       └─ Quit

Pause ─┬─ Resume
       ├─ Restart Run
       ├─ Settings ───────── (same submenu; returns to Pause)
       └─ Quit to Title

Run Over ─┬─ New Run ──────── seed and start a fresh climb
          ├─ View Stats ───── Stats & Charts (§9.2)
          └─ Title
```

**Navigation model**
- `MenuCursor: int` on the active menu; `↑`/`W` decrement, `↓`/`S` increment, both **wrap**.
- `Enter`/`Space` activates the current row; `Esc`/`Back` pops the stack (**Back**).
- **Cycler/slider rows** (Difficulty, Master volume, Sound, Window scale, Screen shake):
  `←`/`→` change the value in place; the row shows a right-aligned `◄ value ►` widget.
- Rendering reuses the §9 Title style: the selected row is inverted/highlighted; non-selected
  rows are drawn plain over the dimmed scene.

**Msg additions** (extend the §7 `Msg` type):
```fsharp
    | MenuUp | MenuDown              // move cursor (wraps)
    | MenuAdjust of dir:int          // -1 / +1 on a cycler/slider row
    | MenuActivate                   // Enter/Space on the current row
    | MenuBack                       // Esc — pop the menu stack
    | OpenStats | CloseStats         // enter / leave the Stats screen (§9.2)
```

Settings apply live and persist to local config (§13): **Difficulty** selects the §12 preset
(Easy `g 2000 / enemyStartAlt 6000`, Normal `g 2400 / enemyStartAlt 3000`, Hard
`g 2800 / enemyStartAlt 1500`, with `gapGrowth`/spring weight scaled to match);
**Master volume**/**Sound** route to `Audio.setMasterVolume` (§10, clamped `[0,1]`, mirroring
the `M` mute toggle); **Screen shake** toggles the §8 spring/jetpack camera-kick effect.

### 9.2 Stats & charts screen
The Stats screen visualizes **the last run** and **lifetime** play. It reads a `Stats`
snapshot (never live physics), so it is a pure, deterministic render reachable from Title, Run
Over, and Pause. Chart-design choices below follow the project dataviz conventions (form-first,
validated colorblind-safe categorical palette, single axis, identity by entity).

**Tracked per run** — `RunStats`, accumulated in `Tick`, snapshotted on `GameOver`:

| Field | Type | Updated |
|-------|------|---------|
| `maxHeight` | `float` (px) | max altitude reached (`-MaxClimb`, §4.6) |
| `platformsHit` | `int` by `PlatformKind` (Normal/Moving/Breakable/Spring) | ++ the landed kind on each bounce/contact (§4.3, §4.7) |
| `springsUsed` | `int` | ++ on each Spring super-bounce (§4.7) |
| `monstersDodged` | `int` | ++ when an enemy is culled below camera with no contact (§4.9) |
| `monstersKilled` | `int` | ++ on each stomp kill (§4.9) |
| `fallCause` | `FallCause` (`OffBottom` \| `Monster`) | set once at death (§11) |
| `runSeconds` | `float` | accumulated live-play seconds |

**Lifetime** — `LifetimeStats`, persisted (§13): `bestHeight`, `runsPlayed`, `avgHeight`
(rolling mean of `maxHeight`), `mostMonstersDodged`.

**Layout** (logical 720×1280 portrait): a KPI tile row across the top, two charts below.

```
┌───────────────────────── STATS ─────────────────────────┐
│ ┌ BEST HEIGHT ┐ ┌ RUNS ┐ ┌ AVG HEIGHT ┐ ┌ DODGED ┐      │  ← KPI stat tiles
│ │  18,240 px  │ │  57  │ │  6,120 px  │ │   214  │      │
│ └─────────────┘ └──────┘ └────────────┘ └────────┘      │
│                                                          │
│  Platforms hit by type          Height over time         │
│  ▇▇                        18k ┤              ╭──         │
│  ▇▇                            │          ╭──╯            │
│  ▇▇   ▇▇                       │     ╭───╯                │
│  ▇▇   ▇▇   ▇▇   ▇▇          0k ┼────────────────► sec     │
│  Nrm  Mov  Brk  Spr                                       │
└──────────────────────────────────────────────────────────┘
   ↑/↓ scope:  ▸ This Run · Lifetime            ESC — Back
```

**Charts** (rendered in Skia with the same draw-list discipline as §8):

1. **Platforms hit by type** — *form: per-category magnitude → bars.* x = platform type
   (`Normal`, `Moving`, `Breakable`, `Spring`), y = number landed on this run. **Single
   series**, so one hue and no legend. Bars are 4 px-rounded at the data end with a 2 px
   surface gap between them. Fill `#2a78d6` (light) / `#3987e5` (dark) — validated categorical
   slot 1. Comparing magnitudes across the four types is one hue, never a rainbow.
2. **Height over time** — *form: change over an ordered index → line.* x = elapsed seconds of
   the last run, y = altitude climbed; **one series** (the doodle's climb), so one hue and no
   legend. Draw a single climbing line in `#2a78d6` (light) / `#3987e5` (dark), 2 px stroke
   with an ≥ 8 px end marker at the final height, over recessive 1 px gridlines in `#3C3C3C`.

Conventions honored: **color follows the entity** (the climb series and the type bars are
always slot 1 — never repainted by the scope toggle); **one axis only** (height is a single
y-scale; no dual axis); chart **text uses ink tokens** (`#FFFFFF` primary / `#C3C2B7` muted),
never the series hue; layout is **fixed and deterministic**, so a fixed-seed run (§13) renders
byte-identical for snapshot tests. The `↑/↓` **scope** toggle swaps the data source This-Run ↔
Lifetime without changing colors (Lifetime shows aggregate type-counts and the best run's
height curve).

**Model/Msg hooks:** add `Stats: RunStats` and `Lifetime: LifetimeStats` to the §7 `Model`;
accumulate them in the `Tick` case (bump `platformsHit`/`springsUsed` on land, `monstersKilled`
on stomp, `monstersDodged` on off-camera cull, sample `maxHeight` from `MaxClimb`, advance
`runSeconds`). On the `GameOver` transition set `fallCause`, fold `RunStats` into `Lifetime`
(update `bestHeight`, `runsPlayed`, `avgHeight`, `mostMonstersDodged`), and persist (§13).
`OpenStats`/`CloseStats` switch a Stats overlay carrying a `scope:StatScope`; the render is a
no-op on physics.

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
| Auto-bounce off a platform (§4.3) | `Audio.playSfx (SoundId "bounce") 0.8` | `bounce` | Normal bounce — short "boing" (pitch slightly randomized ±5%). |
| Spring super-bounce (§4.7) | `Audio.playSfx (SoundId "spring-bounce") 0.9` | `spring-bounce` | Spring bounce — higher "spring" twang. |
| Jetpack thrust active (§5) | `Audio.playSfx (SoundId "jetpack-thrust") 0.7` | `jetpack-thrust` | Jetpack — looping thrust whoosh for its duration. |
| Breakable platform contact (§4.7) | `Audio.playSfx (SoundId "platform-break") 0.8` | `platform-break` | Breakable platform — crumble/snap. |
| Enemy stomp (§4.9) | `Audio.playSfx (SoundId "enemy-stomp") 0.8` | `enemy-stomp` | Enemy stomp — squish. |
| Lethal enemy contact (§4.9, §11) | `Audio.playSfx (SoundId "enemy-hit") 0.9` | `enemy-hit` | Enemy hit (death) — thud/zap. |
| Jetpack pickup grabbed (§5) | `Audio.playSfx (SoundId "pickup-grab") 0.8` | `pickup-grab` | Pickup grab — chime. |
| Game Over (§11) | `Audio.playSfx (SoundId "game-over") 0.9` | `game-over` | Game over — descending tone. |
| New best score (§9) | `Audio.playSfx (SoundId "new-best") 1.0` | `new-best` | New best — fanfare. |

This game has a light loopable background track, so `update` requests
`Audio.playMusic (TrackId "bg-loop") true` when a run starts (§7) and `Audio.stopMusic` when
music stops at Game Over (§11); the mute toggle (`M`) maps to `Audio.setMasterVolume` (`0.0`
to mute, `1.0` to restore). **Testing:** collect the frame's
`AudioEffect`s, `Audio.interpret` them, and assert the `AudioEvidence.Requested` sequence for
representative events (e.g. an auto-bounce on a Static platform requests exactly
`PlaySfx (SoundId "bounce", _)`).

## 11. Win / Loss / Scoring
- **Scoring:** `Score = floor(altitude) = floor(-MaxClimb)` in px of climb. Score **only ever
  increases** (tied to `MaxClimb`, which never decreases). No points for time or kills in v1
  (enemy kills are survival, not score — *optional*: +50 per stomp).
- **No win condition** — endless; the goal is a personal best.
- **Loss conditions:**
  1. Doodle falls below the bottom of the (never-descending) camera: doodle top edge
     `> cameraY + 1280`.
  2. Doodle makes lethal contact with an enemy (not a stomp).
- **Lives/continues:** none. One life per run; instant restart from Game Over.
- **Best score** persisted across sessions (13).

## 12. Difficulty & Balancing

| Param | Default | Range | Effect |
|---|---|---|---|
| `g` (gravity) | 2400 px/s² | 1800–3000 | Higher = faster fall, tighter timing |
| `bounceVy` | -1150 px/s | -900..-1400 | Normal bounce height (apex ≈ vy²/2g) |
| `springVy` | -1900 px/s | -1600..-2200 | Spring boost height |
| `vxMove` | 520 px/s | 350–700 | Steering responsiveness |
| `vyMax` | 1600 px/s | 1200–2200 | Terminal fall speed |
| `baseGap` | 90 px | 70–120 | Platform spacing low down |
| `maxGap` | 230 px | 180–270 | Platform spacing high up (< apex 275) |
| `gapGrowth` | 0.012 | 0.005–0.02 | How fast spacing widens with altitude |
| `cameraLerp` | 0.18 / frame | 0.1–0.3 | Camera follow smoothness |
| `enemyStartAlt` | 3000 px | 1500–6000 | When enemies begin |
| `jetpackDuration` | 2.2 s | 1.5–3.5 | Jetpack thrust length |
| Spring weight | 0.08 | 0–0.2 | Fraction of platforms with springs |
| Moving weight | f(alt): 0.05→0.30 | — | Rises with altitude |
| Breakable weight | f(alt): 0.0→0.30 | — | Rises with altitude |
| Static weight | remainder | — | Falls with altitude |

**Type-weight schedule (per spawned platform), interpolated by altitude:**
- altitude 0: Static 0.95, Moving 0.05, Breakable 0.0, (Spring 0.08 applied as an independent
  roll on whatever non-breakable platform is chosen).
- altitude 6000+: Static 0.40, Moving 0.30, Breakable 0.30 (Spring roll unchanged).
Weights are data-driven so balancing is config, not code.

## 13. Technical Notes
- **Performance budget:** ≤ ~120 active entities (platforms + enemies + pickups + particles)
  on screen; target **60 FPS / 16.7 ms** per frame. Full redraw per frame is fine at this scale.
- **Timestep:** **fixed-timestep simulation** at 60 Hz, drained by **`FixedStep.drainWith`** — do not
  hand-roll the accumulator. `FixedStep.drainWith (4.0/60.0) (1.0/60.0) frameTime acc` returns
  `struct (steps, acc')`: the whole `1/60` steps this frame owes, and the remainder to bank. The cap
  is the spiral-of-death guard — `4.0/60.0` is the max-4-steps-per-frame rule as the frame-time budget
  the function takes. This keeps bounce physics and collision deterministic regardless of frame rate.
- **Swept collision required:** at `vyMax = 1600 px/s`, the doodle moves ~26.7 px per fixed
  step — comparable to platform thickness — so test the previous→current Y sweep against
  platform tops (4.5) to prevent tunneling.
- **Determinism / RNG:** all procedural generation uses a single **`Rng`** (`FS.GG.Game.Core`,
  splitmix64) in the `Model`, seeded with `Rng.ofSeed`. Seeding the run reproduces an identical
  platform layout (useful for tests and the daily-challenge stretch goal). It is a **value**, not a `System.Random`: every draw returns `struct (x, rng')` and you write
  `rng'` back to the `Model`, so the `Model` stays a value you can snapshot, replay and compare.
  A `System.Random` in the `Model` is a mutable object *shared* by every copy of it, which
  silently breaks the reproducibility this bullet promises.
- **Persistence:** `Best` score saved to local storage / app settings; loaded on Title.
- **Edge cases:**
  - First platform guaranteed reachable; doodle starts *resting* on it (vy = 0) so the run
    doesn't begin with a fall.
  - Screen-wrap must not let the doodle "wrap onto" a platform it shouldn't — collision uses
    world X after wrap is applied.
  - Breakable platform contacted while a spring on the *same* tick: breakable wins (no bounce).
  - Jetpack active overrides all platform/enemy interactions (no death by enemy while
    thrusting — *optional*, configurable).
  - Pausing freezes `Tick`; the sim accumulator resets on resume to avoid a catch-up burst.

## 14. Acceptance Criteria (test scenarios)

1. **Auto-bounce on falling contact**
   *Given* the doodle is falling (`vy > 0`) and its feet sensor overlaps a Static platform top
   within the contact band, *When* a `Tick` is processed, *Then* `vy` becomes `-1150 px/s` and
   the doodle's `State` is `Rising`.

2. **No bounce while ascending (one-way collider)**
   *Given* the doodle is rising (`vy < 0`) and passes through a platform from below, *When*
   ticks are processed, *Then* no bounce occurs and `vy` is unchanged by the platform (only
   gravity applies).

3. **Spring super-bounce**
   *Given* the doodle lands (falling) on a Spring platform, *When* the land is resolved, *Then*
   `vy` becomes `-1900 px/s` (not `-1150`), and apex height above the platform is ≈ 752 px
   (within ±5%).

4. **Breakable platform gives no bounce and is removed**
   *Given* the doodle (falling) contacts a Breakable platform, *When* the land is resolved,
   *Then* `vy` is **not** set to a bounce value (doodle keeps falling), the platform enters
   `Breaking`, and after 0.25 s it is removed from `Platforms`.

5. **Score equals max altitude and never decreases**
   *Given* the doodle has reached `MaxClimb = -5000` (altitude 5000), *When* the doodle then
   falls 800 px, *Then* `Score` remains `5000` (does not drop with the fall).

6. **Camera scrolls up, never down**
   *Given* `CameraY = C` after climbing, *When* the doodle falls, *Then* `CameraY` is `≤ C`
   for all subsequent ticks (it never increases / never scrolls back down).

7. **Death by falling below camera**
   *Given* the doodle's top edge exceeds `cameraY + 1280`, *When* the death check runs in
   `Tick`, *Then* `Doodle.State = Dead` and `Phase = GameOver`, and `Best` is updated if
   `Score > Best`.

8. **Horizontal screen-wrap**
   *Given* the doodle's `x` moves past the right edge (`x > 720 + doodleHalfW`), *When* the
   position is normalized, *Then* `x` becomes `-doodleHalfW` (re-enters from the left), with
   `vy` unchanged.

9. **Held steering input**
   *Given* `SteerRight pressed=true` was received and not yet released, *When* successive
   `Tick`s run, *Then* the doodle's `x` increases by ≈ `520 * dt` per tick (up to wrap),
   and *When* `SteerRight pressed=false` arrives, *Then* horizontal target velocity returns
   to 0.

10. **Platform spacing thins with height (stays reachable)**
    *Given* generation at altitude 0 vs. altitude 10000, *When* platforms are spawned, *Then*
    average vertical gap is ≈ 90 px near 0 and saturates at ≤ 230 px high up — and `maxGap`
    (230) is strictly less than the normal bounce apex (275 px), so a reachable platform always
    exists.

11. **Enemy lethal contact vs. stomp**
    *Given* an enemy and a doodle moving **downward** whose feet hit the enemy's top, *When*
    resolved, *Then* the enemy dies and the doodle gets a normal bounce; **but** *Given* the
    doodle's body contacts the enemy while rising or sideways, *Then* the doodle dies
    (`GameOver`).

12. **Deterministic generation from seed**
    *Given* two runs started with the same RNG seed and identical input, *When* both simulate
    N ticks, *Then* their `Platforms` (positions, kinds) are identical.

13. **Jetpack overrides bounce physics**
    *Given* the doodle grabs a Jetpack, *When* the next `jetpackDuration = 2.2 s` of ticks run,
    *Then* `State = Jetpack`, `vy ≈ -2200 px/s` sustained, gravity is suppressed, platform
    bounces are ignored, and after 2.2 s `State` returns to `Falling` with gravity resumed.

14. **Frame-rate independence (fixed timestep)**
    *Given* the same input, *When* the sim runs at variable real frame rates (e.g. 30 vs 60
    FPS) using the fixed-timestep accumulator, *Then* the doodle reaches the same `MaxClimb`
    (within ±1 px) over the same elapsed time.

## 15. Stretch Goals
1. **Shooting** — fire upward projectiles to kill enemies that are unsafe to stomp.
2. **Jetpack & propeller-hat variety** — different boost shapes/durations.
3. **Vanishing (one-shot) platforms** and **moving-breakable** combos at high altitude.
4. **Daily challenge** — fixed seed shared by all players; leaderboard.
5. **Tilt/accelerometer & touch controls** for mobile targets.
6. **Power-up shield** that absorbs one lethal enemy hit.
7. **Cosmetic skins** unlocked by best-score milestones.
8. **Online high-score board** with ghost replay of the run path.
