---
title: "Asteroids"
slug: asteroids
category: games
complexity: simple
genre: "Arcade / shoot-'em-up (vector)"
target_session_minutes: 8
stack: { rendering: "FS.GG.Rendering (Skia/OpenGL)", arch: "Elmish/MVU", lang: "F#" }
status: spec
---

# Asteroids

## 1. Overview
You pilot a fragile vector-drawn spaceship adrift in a wrap-around asteroid field.
The core verb is **shoot-and-survive**: tap the thruster to drift, rotate to aim, and
fire bullets that shatter big rocks into smaller, faster ones. Every shot makes the
field more crowded and more dangerous before it gets clearer, so the fun is the
escalating tension of momentum management — you can never fully stop, the screen wraps,
and a stray rock or a sniping UFO ends a life in one hit. It is a 45-second-per-wave
score chase that rewards precise thrust discipline and trigger control.

## 2. Core Game Loop
**Moment-to-moment:** rotate to aim → thrust to reposition → shoot asteroid → asteroid
splits → dodge the new fragments → repeat until the wave is clear.

**Session-level:** Title screen → press Start → spawn wave 1 (4 large asteroids) →
clear all rocks (and any UFO) → next wave (+1 large asteroid, faster) → keep clearing
until all 3 lives are lost → Game Over screen showing final score → press Start to
restart (RNG reseeded). Extra life awarded every 10,000 points.

## 3. Controls & Input
Keyboard is primary. Input is sampled each tick; rotation and thrust are **held**
(continuous while down), fire and hyperspace are **edge-triggered** (one action per
key-down, ignore auto-repeat).

| Input | Key | Action | Model |
|-------|-----|--------|-------|
| Rotate left | `Left Arrow` / `A` | Turn ship CCW | Held |
| Rotate right | `Right Arrow` / `D` | Turn ship CW | Held |
| Thrust | `Up Arrow` / `W` | Apply forward acceleration | Held |
| Fire | `Space` | Spawn one bullet | Edge (key-down) |
| Hyperspace | `Left Shift` / `H` | Teleport to random location | Edge (key-down) |
| Start / Restart | `Enter` | Begin game / restart from Game Over | Edge (key-down) |
| Pause | `Esc` / `P` | Toggle pause overlay | Edge (key-down) |

No mouse or gamepad in v1 (see Stretch Goals). There is **no reverse thrust and no
brake** — you bleed speed only via drag.

## 4. Mechanics (detailed)
All physics run on a fixed timestep `dt = 1/60 s`. Positions are in logical pixels on a
1280×720 toroidal playfield. Angles are stored in **radians**, rotation rates quoted in
deg/s for readability.

### 4.1 Ship movement
- **Rotation rate:** 270 deg/s (= 4.712 rad/s). Holding rotate applies
  `heading += ±rotRate * dt`. Heading 0 points to screen-right (+x); the ship nose
  vector is `(cos heading, sin heading)`.
- **Thrust acceleration:** 220 px/s² along the nose vector while Thrust held.
  `vel += noseVec * thrustAccel * dt`.
- **Drag (linear damping):** velocity multiplied by `0.99` every tick
  (≈ `0.547` per second; i.e. `vel *= dragPerTick` with `dragPerTick = 0.99`). This
  gives a long, floaty glide that never quite reaches zero — by design.
- **Max speed:** 600 px/s. After integrating, clamp `|vel|` to `maxSpeed`.
- **Position integration:** `pos += vel * dt`, then apply screen wrap (§4.6).

### 4.2 Firing & bullets
- **Bullet speed:** 700 px/s, added to the ship's current velocity at spawn so shots
  inherit ship momentum (muzzle velocity is `vel + noseVec * 700`). Speed is **not**
  clamped to ship max speed.
- **Spawn point:** ship nose tip = `pos + noseVec * 16` (ship is 16 px from center to
  nose).
- **Cooldown:** 250 ms minimum between shots (max 4 shots/s).
- **Max concurrent bullets:** 4. If 4 are already alive, Fire is ignored even if
  cooldown elapsed.
- **Lifetime:** 1.1 s, after which the bullet despawns. (At 700 px/s relative this
  travels ~770 px — a little over half the screen width, so you cannot snipe across the
  whole field.)
- **Bullet radius (hitbox):** 2 px. Bullets wrap on screen edges like everything else.

### 4.3 Asteroid splitting
Asteroids exist in three size classes. Shooting one removes it and (if not Small)
spawns **2 children** of the next size down.

| Size | Collision radius | Speed range | Children on death | Points |
|------|------------------|-------------|-------------------|--------|
| Large | 40 px | 20–60 px/s | 2 × Medium | 20 |
| Medium | 20 px | 40–90 px/s | 2 × Small | 50 |
| Small | 10 px | 60–130 px/s | none | 100 |

**Split behavior:** each child inherits the parent's position. Its velocity direction is
the parent's velocity heading rotated by a random offset in `±[15°, 45°]` (one child
each side), with a fresh speed magnitude drawn from the child class's speed range. Each
asteroid also has a slow visual spin: random angular velocity in `±[20, 90] deg/s`
(cosmetic only, does not affect the circular hitbox).

### 4.4 Collisions
All collisions are **circle vs circle**: overlap when
`distance(a, b) < a.radius + b.radius` (using wrap-aware shortest distance, §4.6).

- **Bullet ↔ Asteroid:** asteroid splits/dies, bullet despawns, score awarded.
- **Bullet ↔ UFO:** UFO dies, bullet despawns, score awarded.
- **Ship ↔ Asteroid:** ship destroyed (lose a life).
- **Ship ↔ UFO / UFO bullet:** ship destroyed.
- **UFO bullet ↔ Asteroid:** pass through (no interaction) to keep the UFO threatening.
- **Ship ↔ ship's own bullets:** never collide.
- Ship hitbox is a single circle of **radius 11 px** (smaller than the visual triangle,
  which feels fair).

### 4.5 Death, respawn & invulnerability
- On ship death: decrement lives, emit explosion particles, freeze ship for 1.5 s, then
  respawn at screen center with zero velocity and heading `-90°` (pointing up).
- **Respawn safety:** the ship will not un-freeze until the center 120×120 px area is
  clear of asteroids/UFO; check each tick after the 1.5 s timer.
- **Spawn invulnerability:** 2.5 s after respawn the ship is invulnerable (ignores all
  Ship↔X collisions) and renders blinking (toggle visibility every 100 ms).
- If lives reach 0, transition to Game Over.

### 4.6 Screen wrapping (toroidal space)
The playfield is a torus of width `W = 1280`, height `H = 720`. Applies to ship,
bullets, asteroids, UFO, and particles.

```
// position wrap (per axis), after integration
let wrap (v: float) (size: float) =
    let m = v % size
    if m < 0.0 then m + size else m
pos.X <- wrap pos.X W
pos.Y <- wrap pos.Y H
```

**Wrap-aware distance** (shortest toroidal delta) used for all collision checks and the
UFO's aim:
```
let wrapDelta (a: float) (b: float) (size: float) =
    let d = b - a
    if d >  size / 2.0 then d - size
    elif d < -size / 2.0 then d + size
    else d
// dist² = (wrapDelta ax bx W)² + (wrapDelta ay by H)²
```

Objects must also render duplicated near edges (§8) so a body straddling a boundary is
visible on both sides.

### 4.7 Hyperspace
Pressing Hyperspace instantly teleports the ship to a uniformly random position in the
playfield and zeroes its velocity. It is an emergency escape with risk:
- **Cooldown:** 1.0 s.
- **Re-entry risk:** 12% chance the ship is destroyed on arrival (a "bad warp"),
  resolved *after* placement. Otherwise the ship is briefly (0.3 s) invulnerable on
  arrival so it never instantly dies to a rock it landed on (except via the 12% roll).
- No directional control over destination.

### 4.8 UFO enemy
A flying saucer crosses the field and shoots at the ship.
- **Spawn:** after 30 s of a wave with no UFO present, OR once the wave is down to ≤ 3
  asteroids — whichever first. Max 1 UFO at a time. At most 1 UFO spawn attempt per 20 s.
- **Two types** (chosen at spawn): **Large** (radius 18 px, 200 pts, fires in a random
  direction) and **Small** (radius 10 px, 1000 pts, aims directly at the ship with up to
  ±10° error). Small-UFO probability rises with score: `min(0.75, score / 40000)`.
- **Movement:** enters from a random vertical position on the left or right edge, travels
  horizontally at 120 px/s, and every 1.0 s may jog its vertical velocity to one of
  `{-90, 0, +90} px/s` for a zig-zag path. Despawns when it exits the opposite edge
  (UFOs do **not** wrap horizontally; they do wrap vertically).
- **Firing:** one bullet every 1.2 s. UFO bullet speed 350 px/s, lifetime 1.4 s,
  radius 2 px.

## 5. Entities / Game Objects
F#-flavored sketches; final field names may differ.

### 5.1 Ship
```fsharp
type Ship =
  { Pos: Vec2
    Vel: Vec2
    Heading: float          // radians
    Thrusting: bool         // for flame rendering
    InvulnUntil: float      // game-time seconds; 0 if not invulnerable
    FireCooldown: float     // seconds remaining
    HyperCooldown: float
    Alive: bool
    RespawnTimer: float }   // >0 while frozen/dead before respawn
```
Created once at game start and on each respawn. Destroyed (Alive=false) on collision.

### 5.2 Asteroid
```fsharp
type AstSize = Large | Medium | Small
type Asteroid =
  { Pos: Vec2; Vel: Vec2
    Size: AstSize
    Radius: float
    Spin: float             // deg/s, cosmetic
    Angle: float            // current render rotation (rad)
    Shape: Vec2[] }         // pre-baked jagged polygon (8–12 verts), unit-scaled
```
Created at wave start (Large only) and on split (children). Destroyed when shot or when
it collides with the ship.

### 5.3 Bullet
```fsharp
type BulletOwner = Player | Ufo
type Bullet =
  { Pos: Vec2; Vel: Vec2
    Life: float             // seconds remaining
    Owner: BulletOwner }
```

### 5.4 Ufo
```fsharp
type UfoKind = LargeSaucer | SmallSaucer
type Ufo =
  { Pos: Vec2; Vel: Vec2
    Kind: UfoKind
    Radius: float
    FireTimer: float
    JogTimer: float
    ExitEdge: float }       // x at which it despawns
```

### 5.5 Particle (explosions / thrust)
```fsharp
type Particle =
  { Pos: Vec2; Vel: Vec2; Life: float; MaxLife: float }
```
Short-lived debris lines for ship/asteroid explosions and thrust exhaust. Cosmetic only,
no collisions; capped at 200 live particles.

## 6. World / Levels / Progression
- **Playfield:** 1280×720 logical px, toroidal (wraps both axes). No camera, no scroll.
- **Waves:** Wave `n` spawns `min(4 + (n - 1), 11)` Large asteroids (capped at 11).
  Large asteroids spawn at random edge positions, never within 150 px of the ship's
  spawn center, with random initial velocities in the Large speed range.
- **A wave clears** when there are zero asteroids AND no UFO on screen. After a 2.0 s
  pause the next wave spawns.
- **Difficulty ramp per wave:**
  - Asteroid base speed range scales by `min(1.6, 1 + 0.06 * (n - 1))`.
  - Small-UFO bias grows with score (§4.8).
  - UFO appears sooner as `n` rises: spawn delay `max(8, 30 - 2 * (n - 1))` seconds.
- Nothing else changes (ship stats are constant), keeping it "simple".

## 7. State Model (Elmish/MVU)

### Model
```fsharp
type Phase = Title | Playing | Paused | GameOver

type Model =
  { Phase: Phase
    Ship: Ship
    Asteroids: Asteroid list
    Bullets: Bullet list
    Ufo: Ufo option
    Particles: Particle list
    Score: int
    Lives: int
    Wave: int
    NextExtraLifeAt: int        // e.g. 10000, then 20000, ...
    Time: float                 // accumulated game-time seconds
    WaveClearTimer: float       // >0 during inter-wave pause
    UfoSpawnTimer: float
    Keys: Set<Key>              // currently-held keys
    Rng: System.Random
    HighScore: int }
```

### Msg
```fsharp
type Msg =
  | Tick of float               // dt in seconds (fixed 1/60)
  | KeyDown of Key
  | KeyUp of Key
  | StartGame
  | TogglePause
```

### update (key cases)
- **`Tick dt`** (only when `Phase = Playing`): the simulation step, in order:
  1. Apply held rotation/thrust to ship; integrate ship vel/pos; apply drag, clamp
     max speed, wrap.
  2. Integrate asteroids, bullets, UFO, particles; wrap; decay lifetimes/timers.
  3. UFO logic: spawn check, jog/zig-zag, fire timer.
  4. Collision resolution (bullets→rocks/UFO→split & score; ship→hazards→death).
  5. Wave-clear check → `WaveClearTimer`; next-wave spawn when it elapses.
  6. Extra-life check (`Score ≥ NextExtraLifeAt`).
  7. Respawn/invulnerability timers; `Lives = 0` → `Phase = GameOver`,
     update `HighScore`.
- **`KeyDown k`**: add to `Keys`; if edge action (Fire/Hyperspace/Pause/Start) handle
  once here (respecting cooldowns). Fire spawns a bullet if `< 4` alive and cooldown 0.
- **`KeyUp k`**: remove from `Keys`.
- **`StartGame`**: reset Model to a fresh game (lives=3, score=0, wave=1, reseed Rng),
  `Phase = Playing`.
- **`TogglePause`**: `Playing ↔ Paused` (Tick is a no-op while Paused).

### view
Pure projection of `Model` → a scene description Skia draws (§8). The view holds no
mutable state and performs no physics — it reads ship/asteroid/bullet/UFO/particle lists
plus HUD numbers and emits draw commands.

### Subscriptions
- A 60 FPS timer subscription dispatching `Tick (1/60)` (fixed timestep; see §13 for
  accumulator handling under variable real frame times).
- Keyboard subscription dispatching `KeyDown`/`KeyUp`.

## 8. Rendering (Skia 2D)
Coordinate system: origin top-left, +x right, +y down, logical 1280×720 (scaled to the
window). Classic vector look: black background, thin bright strokes, no fills.

**Draw order (back to front):**
1. **Background** — solid black `#000000` full-rect clear each frame.
2. **Particles** — 1 px lines, color `#AAAAAA` fading alpha by `Life/MaxLife`.
3. **Asteroids** — closed polygons from `Shape` rotated by `Angle`, stroke `#FFFFFF`,
   1.5 px width, no fill.
4. **UFO** — saucer outline (two stacked trapezoids + dome), stroke `#FF5555`, 1.5 px.
5. **Bullets** — 2 px filled squares/dots; player `#FFFFFF`, UFO `#FF5555`.
6. **Ship** — isosceles triangle (nose 16 px ahead of center, tail corners ±10 px),
   stroke `#00FFAA`, 2 px; when `Thrusting`, draw a flickering exhaust triangle behind
   the tail in `#FFAA00`. Skip drawing on blink-off frames during invulnerability.
7. **HUD** (§9) on top.

**Wrap rendering:** for any body whose circle crosses an edge, draw it again offset by
`±W` / `±H` so the straddling portion shows on the opposite side (up to 4 duplicate
draws near a corner).

**Camera:** none (fixed full-field view). **Redraw strategy:** full-frame clear-and-draw
every tick (immediate mode); no dirty-rect optimization needed at these entity counts.
**Fonts:** a monospace/vector-style font (e.g. "Hyperspace"/fallback monospace) for HUD,
24 px score, 18 px secondary.

## 9. UI / HUD / Screens
- **Title screen:** centered game name (64 px), "PRESS ENTER TO START" (24 px), high
  score line, a few slow-drifting background asteroids for ambiance.
- **Playing HUD:**
  - **Score:** top-left at `(24, 16)`, left-aligned, e.g. `04250`.
  - **High score:** top-center, smaller.
  - **Lives:** top-left under score at `(24, 48)`, drawn as N small ship-triangle icons.
  - **Wave:** top-right at `(W-24, 16)`, right-aligned, `WAVE 3`.
- **Pause overlay:** dim the field (50% black overlay) + centered `PAUSED`.
- **Game Over screen:** centered `GAME OVER` (48 px), final score, high score,
  `PRESS ENTER TO RESTART`.

### 9.1 Menu system (detailed)
A single **menu stack** drives every non-play screen (Title, Settings, Stats, Pause,
Game Over). Each menu is a vertical list of rows with a cursor, so one small update
handler serves them all and navigation is identical everywhere. Rows render in the §8
vector/monospace HUD font; the selected row is inverted (bright box, black text).

**Menu tree**
```
Title ─┬─ Play ──────────── start a new run (lives=3, wave 1, RNG reseeded — §7 StartGame)
       ├─ Stats ─────────── Stats & Charts screen (§9.2)
       ├─ Settings ──────┬─ Difficulty     ◄ Easy · Normal · Hard ►
       │                 ├─ Master volume  ◄ 0 – 100 ►
       │                 ├─ Sound          ◄ On · Off ►
       │                 ├─ Window scale   ◄ 1× · 2× · Fit ►
       │                 ├─ CRT glow       ◄ On · Off ►
       │                 └─ Back
       └─ Quit

Pause ─┬─ Resume
       ├─ Restart Run
       ├─ Settings ──────── (same submenu; returns to Pause)
       └─ Quit to Title

Game Over ─┬─ New Run ────── reset to 3 lives, wave 1, score 0 (§7 StartGame)
           ├─ View Stats ── Stats & Charts (§9.2)
           └─ Title
```

Asteroids has **no continues** (§11), so a lost run cannot be resumed: the run-end menu
offers a fresh **New Run** (the arcade "insert-coin" restart) rather than a Continue, and
the surviving-lives count only ever matters mid-run in the HUD (§9).

**Navigation model**
- `MenuCursor: int` on the active menu; `↑`/`W` decrement, `↓`/`S` increment, both **wrap**.
- `Enter`/`Space` activates the current row; `Esc`/`Backspace` pops the stack (**Back**).
- **Cycler/slider rows** (Difficulty, Master volume, Sound, Window scale, CRT glow):
  `←`/`→` change the value in place; the row shows a right-aligned `◄ value ►` widget.
- The Title-screen `Enter`-to-start of §9 is preserved as a shortcut for the **Play** row.

**Msg additions** (extend §7 `Msg`):
```fsharp
    | MenuUp | MenuDown              // move cursor (wraps)
    | MenuAdjust of dir:int          // -1 / +1 on a cycler/slider row
    | MenuActivate                   // Enter/Space on the current row
    | MenuBack                       // Esc — pop the menu stack
    | OpenStats | CloseStats         // enter / leave the Stats screen (§9.2)
```

Settings apply live and persist to local config (§13): **Difficulty** selects a §12 tunable
preset (Easy `startLives 5 / ufoFireInterval 2.0 / astSpeedScalePerWave 0.04`; Normal
`3 / 1.2 / 0.06`; Hard `2 / 0.8 / 0.10`); **Master volume**/**Sound** route to
`Audio.setMasterVolume` (§10, clamped `[0,1]`, `0.0` = mute); **CRT glow** toggles the
optional vector-glow post-pass (§8 draw order / §15 stretch).

### 9.2 Stats & charts screen
The Stats screen visualizes **the last run** and **lifetime** play. It reads a `Stats`
snapshot (never live physics), so it is a pure, deterministic render reachable from Title,
Game Over, and Pause. Chart-design choices below follow the project dataviz conventions
(form-first, validated colorblind-safe palette, single axis, identity by entity).

**Tracked per run** — `MatchStats`, accumulated in the §7 `Tick` step, snapshotted on the
`Lives = 0 → GameOver` transition (§7 update, step 7):

| Field | Type | Updated |
|-------|------|---------|
| `shotsFired` | `int` | +1 whenever a Fire edge spawns a player bullet (§4.2) |
| `shotsHit` | `int` | +1 on each Player-bullet ↔ asteroid/UFO hit (§4.4) |
| `accuracy` | `float` | derived: `shotsHit / shotsFired` (0 when unfired) |
| `killsLarge` / `killsMedium` / `killsSmall` | `int` | per asteroid destroyed, by size (§4.3) |
| `ufosDestroyed` | `int` | +1 per UFO killed (§4.8) |
| `wavesCleared` | `int` | +1 each wave-clear (§6) |
| `topScoreMultiplier` | `float` | peak `waveScore / parScore` over any wave (efficient small-rock/UFO kills) |
| `survivalSeconds` | `float` | accumulated live-play time (`Time` delta while `Ship.Alive`) |
| `thrustSeconds` | `float` | accumulated seconds Thrust was held (§4.1) |
| `deaths` | `int` | lives lost this run (§4.5) |

**Lifetime** — `LifetimeStats`, persisted (§13, `highscore.json`): `highScore`, `gamesPlayed`,
`bestAccuracy`, `mostWavesCleared`, `longestSurvival`.

**Layout** (logical 1280×720): a KPI tile row across the top, two charts below.

```
┌──────────────────────────── STATS ────────────────────────────┐
│  ┌HIGH SCORE┐ ┌ACCURACY┐ ┌ WAVES  ┐ ┌LONGEST LIFE┐            │  ← KPI stat tiles
│  │  48 250  │ │  37 %  │ │   14   │ │   72.4 s   │            │
│  └──────────┘ └────────┘ └────────┘ └────────────┘            │
│                                                                │
│  Kills by size                      Shots fired vs hit         │
│  ▇▇                             120 ┤         ╭── fired        │
│  ▇▇  ▇▇                             │      ╭─╯                 │
│  ▇▇  ▇▇  ▇▇                         │   ╭─╯╭──── hit           │
│  ▇▇  ▇▇  ▇▇  ▇▇                   0 ┼───────────────► wave #   │
│  Lg  Md  Sm  UFO  (kills)                                      │
└────────────────────────────────────────────────────────────────┘
     ↑/↓ scope:  ▸ This Run · Lifetime            ESC — Back
```

KPI tiles read **HIGH SCORE**, **ACCURACY %**, **WAVES** (cleared), and **LONGEST LIFE**
(the run's `longestSurvival` streak between deaths); each swaps to its lifetime counterpart
under the scope toggle.

**Charts** (rendered in Skia with the same draw-list discipline as §8):

1. **Kills by size** — *form: per-category magnitude → bars.* x = size bucket
   (`Large`, `Medium`, `Small`, `UFO`), y = kill count. **Single series**, so one hue and
   no legend. Bars are 4 px-rounded at the data end with a 2 px surface gap between them.
   Fill `#2a78d6` (light) / `#3987e5` (dark) — validated categorical slot 1. Comparing four
   magnitudes in one hue (not four colors) keeps the eye on relative counts.
2. **Shots fired vs hit** — *form: change over an ordered index → line.* x = wave number,
   y = cumulative shots; **two series** (Fired, Hit) → a legend is present and both lines are
   direct-labeled at their right end ("fired"/"hit"). Fired `#2a78d6`, Hit `#1baf7a`
   (slots 1–2, adjacent-pair CVD-validated). 2 px lines, ≥ 8 px end markers, recessive 1 px
   gridlines in `#3C3C3C`. The widening gap between the two lines is the accumulated misses —
   the visual read of accuracy over the run.

Conventions honored: **color follows the entity** (Fired is always slot 1, Hit always slot 2
— never repainted by the scope toggle); **one axis only** (no dual y-scale); chart **text uses
ink tokens** (`#FFFFFF` primary / `#C3C2B7` muted), never the series hue; layout is **fixed and
deterministic**, so a fixed-seed run (§13) renders byte-identical for snapshot tests. The
`↑/↓` **scope** toggle swaps the data source This-Run ↔ Lifetime without changing colors.

**Model/Msg hooks:** add `Stats: MatchStats` and `Lifetime: LifetimeStats` to the §7 Model,
and a `Stats of scope:StatScope` case to `Phase`. Accumulate `MatchStats` in the §7 `Tick`
step — bump `shotsFired` where Fire spawns a bullet (`KeyDown`), `shotsHit`/`killsLarge|Medium|
Small`/`ufosDestroyed` in the collision pass (step 4), `wavesCleared` at wave-clear (step 5),
`survivalSeconds`/`thrustSeconds` each tick, and `deaths` on ship death (step 7). On the
`GameOver` transition (step 7) fold `MatchStats` into `Lifetime` and persist to `highscore.json`
(§13). `OpenStats`/`CloseStats` switch `Phase` to/from `Stats`; the render is a no-op on physics.

## 10. Audio
Audio ships in v1 via the FS.GG.UI **`fs-gg-audio`** capability (`open FS.GG.UI.Canvas`).
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
| Fire bullet (§4.2) | `Audio.playSfx` | `SoundId "fire"` | short "pew" |
| Asteroid destroyed (§4.3) | `Audio.playSfx` | `SoundId "asteroid-explosion"` | three explosion pitches (low→high) for Large/Med/Small |
| Ship thrust (§4.1) | `Audio.playSfx` | `SoundId "thrust"` | looping low rumble while held |
| Ship destroyed (§4.5) | `Audio.playSfx` | `SoundId "ship-explosion"` | big explosion |
| UFO present (§4.8) | `Audio.playSfx` | `SoundId "ufo-warble"` | looping warble (Large = low, Small = high) |
| UFO destroyed (§4.8) | `Audio.playSfx` | `SoundId "ufo-explosion"` | explosion |
| Extra life (§11) | `Audio.playSfx` | `SoundId "extra-life"` | chime |
| Wave heartbeat | `Audio.playMusic` | `TrackId "heartbeat"` | two-note beat that speeds up as a wave is cleared down |

The classic background "heartbeat" is looping music: request `Audio.playMusic (TrackId "heartbeat") true`
while a wave is in progress, and `Audio.stopMusic` when the field pauses or the game ends. A
mute/settings toggle maps to `Audio.setMasterVolume` (e.g. `Audio.setMasterVolume 0.0` to silence). **Testing:** collect the frame's
`AudioEffect`s, `Audio.interpret` them, and assert the `AudioEvidence.Requested` sequence for
representative events (e.g. firing a bullet requests exactly `PlaySfx (SoundId "fire", _)`).

## 11. Win / Loss / Scoring
- **Scoring:** Large asteroid 20, Medium 50, Small 100; Large UFO 200, Small UFO 1000.
  Only the **player's** bullets (and ship ramming, which gives no points) score.
- **Extra life:** +1 life at every 10,000 points (`NextExtraLifeAt` advances by 10,000).
- **Loss condition:** lives reach 0 → Game Over.
- **Win condition:** none — it is an endless score chase. "Success" = beating the high
  score. High score persists locally (§13).
- **Lives:** start with 3. No continues.

## 12. Difficulty & Balancing
Data-driven tunables (defaults chosen above):

| Name | Default | Range | Effect |
|------|---------|-------|--------|
| `shipThrustAccel` | 220 px/s² | 120–400 | Acceleration responsiveness |
| `shipRotRate` | 270 deg/s | 150–400 | Turn speed |
| `dragPerTick` | 0.99 | 0.97–0.999 | Glide length (lower = more drag) |
| `shipMaxSpeed` | 600 px/s | 400–900 | Top speed cap |
| `bulletSpeed` | 700 px/s | 500–1000 | Shot velocity |
| `bulletLifetime` | 1.1 s | 0.6–2.0 | Effective range |
| `fireCooldown` | 250 ms | 100–500 | Rate of fire |
| `maxBullets` | 4 | 2–8 | On-screen shot cap |
| `startLives` | 3 | 1–5 | Difficulty floor |
| `waveStartLarge` | 4 | 2–8 | Wave 1 rock count |
| `astSpeedScalePerWave` | 0.06 | 0–0.15 | Per-wave rock speed ramp |
| `hyperBadWarpChance` | 0.12 | 0–0.3 | Hyperspace death risk |
| `ufoSpawnDelay` | 30 s | 5–60 | First UFO appearance |
| `ufoFireInterval` | 1.2 s | 0.5–3.0 | UFO aggression |
| `invulnDuration` | 2.5 s | 0–4 | Respawn grace period |
| `extraLifeEvery` | 10000 | 5000–25000 | Extra-life cadence |

## 13. Technical Notes
- **Timestep:** fixed `dt = 1/60 s` simulation. The render loop accumulates real frame
  time and steps the simulation in whole `1/60` increments (accumulator pattern),
  capping at e.g. 5 steps/frame to avoid a "spiral of death" if the tab stalls.
- **Determinism:** all randomness flows through `Model.Rng` (`System.Random`) seeded at
  `StartGame`; given the same seed + same input sequence the run is reproducible — useful
  for tests.
- **Performance budget:** worst-case entity count is bounded — max ~11 Large →
  effectively ≤ ~44 small fragments + 4 player bullets + 1 UFO + 1 UFO bullet + ≤200
  particles. Far under any 16.7 ms/frame concern; collision is naive O(n²) circle checks
  (a few thousand comparisons max), no spatial partitioning needed.
- **Persistence:** high score saved to local storage / a small `highscore.json`; loaded
  on Title.
- **Edge cases:** (a) firing while 4 bullets alive → no-op; (b) respawn blocked until
  center clear (could stall — center-clear check prevents instant re-death); (c)
  hyperspace landing on a rock → covered by 0.3 s arrival grace except the 12% bad-warp
  roll; (d) an asteroid and the ship spawning overlapped is prevented by the 150 px
  no-spawn radius; (e) wrap-aware distance must be used in collisions or edge-straddling
  bodies miss hits.

## 14. Acceptance Criteria (test scenarios)
Verifiable Given/When/Then. `dt` steps are `1/60 s` unless noted.

1. **Thrust accelerates along heading.**
   Given a stationary ship at heading 0 (pointing +x) at `(640, 360)`,
   When Thrust is held for 60 ticks (1.0 s),
   Then `Vel.X` is positive and `≈ 220 * 1.0` reduced by cumulative drag (within ±15%),
   `Vel.Y ≈ 0`, and the ship has moved right (`Pos.X > 640`).

2. **Drag decays velocity, never reverses it.**
   Given a ship moving at `(300, 0)` px/s with no input,
   When 120 ticks elapse,
   Then `0 < Vel.X < 300` (monotonically decreasing) and `Vel.Y = 0`.

3. **Max speed clamp.**
   Given thrust held continuously for 10 s,
   Then `|Vel|` never exceeds 600 px/s on any tick.

4. **Rotation rate.**
   Given heading 0, When Rotate-right held for 1.0 s,
   Then heading increased by `≈ 270°` (4.712 rad) within ±2°.

5. **Screen wrap (position).**
   Given the ship at `(1279, 360)` with `Vel = (120, 0)`,
   When 1 tick elapses,
   Then `Pos.X` is `≈ 1` (wrapped), not `≈ 1281`.

6. **Bullet inherits momentum and expires.**
   Given a ship at heading 0 with `Vel = (100, 0)` that fires,
   Then a Player bullet exists with `Vel.X ≈ 800` (700 + 100); And after 1.1 s
   (66 ticks) that bullet no longer exists.

7. **Fire cooldown & cap.**
   Given Fire pressed twice within 250 ms, Then only 1 bullet spawns; And given 4
   bullets already alive, pressing Fire spawns none.

8. **Large asteroid split.**
   Given one Large asteroid and a Player bullet overlapping it,
   When collision resolves,
   Then the Large is removed, exactly 2 Medium asteroids exist at its former position,
   and Score increased by 20.

9. **Full split chain & points.**
   Given a Large asteroid fully destroyed (Large → 2 Med → 4 Small → 0),
   When all fragments are shot,
   Then total Score from that lineage = `20 + 2*50 + 4*100 = 520`.

10. **Small asteroid does not split.**
    Given a Small asteroid hit by a bullet,
    Then it is removed, no children spawn, and Score += 100.

11. **Ship death and life loss.**
    Given a non-invulnerable ship overlapping an asteroid,
    When collision resolves,
    Then Lives decreases by 1, ship enters respawn state, and an explosion is emitted.

12. **Respawn invulnerability.**
    Given the ship respawns at center,
    When an asteroid overlaps it within 2.5 s of respawn,
    Then the ship is NOT destroyed (invulnerable) and renders blinking.

13. **Wave progression.**
    Given Wave 1 with all asteroids and any UFO cleared,
    When the 2.0 s inter-wave timer elapses,
    Then Wave becomes 2 and 5 Large asteroids spawn.

14. **Game over.**
    Given Lives = 1 and the ship is destroyed,
    Then Phase becomes GameOver and final Score is shown; And pressing Enter sets
    Phase = Playing with Lives = 3, Score = 0, Wave = 1.

15. **Extra life.**
    Given Score crosses 10,000 (e.g. 9,950 → 10,050),
    Then Lives increases by 1 exactly once and `NextExtraLifeAt` becomes 20,000.

16. **Hyperspace teleport.**
    Given the ship at `(200, 200)`, When Hyperspace is pressed,
    Then `Vel = (0,0)` and `Pos` differs from `(200, 200)`; And with `hyperBadWarpChance`
    forced to 0 the ship survives, with it forced to 1 the ship is destroyed.

17. **UFO scoring.**
    Given a Small UFO hit by a Player bullet, Then it is removed and Score += 1000;
    Given a Large UFO hit, Score += 200.

18. **UFO bullet kills ship; ignores rocks.**
    Given a UFO bullet overlapping a non-invulnerable ship, Then the ship is destroyed;
    Given a UFO bullet overlapping an asteroid, Then both persist unchanged.

19. **Input edge-trigger (no auto-fire).**
    Given Fire held down for 60 ticks without release,
    Then bullets spawn only on the key-down edge and subsequently no faster than the
    250 ms cooldown (i.e. ≤ 4 bullets across that second, not 60).

20. **Determinism.**
    Given two games started with the same RNG seed and the same recorded input sequence,
    Then asteroid spawn positions/velocities and final Score are identical.

## 15. Stretch Goals
1. **Gamepad support** (analog rotate via stick, triggers to fire/thrust).
2. **Mouse aim mode** (rotate ship toward cursor, click to fire).
3. **Powerups** dropped by UFOs: spread-shot, shield, rapid-fire (timed).
4. **Two-player co-op** (second ship, shared wave, separate lives).
5. **Asteroid variety**: rare large "dense" rocks needing 2 hits, or splitting into 3.
6. **CRT/vector post-processing** (glow, scanlines, line bloom) via a Skia shader pass.
7. **Online high-score leaderboard** + replay sharing (leveraging deterministic seeds).
8. **Screen-clear "smart bomb"** as a rare, limited-use panic button.
