---
title: "Breakout"
slug: breakout
category: games
complexity: simple
genre: "Arcade / brick-breaker"
target_session_minutes: 8
stack: { rendering: "FS.GG.Rendering (Skia/OpenGL)", framework: "FS.GG.Game.Core (Rng for determinism)", arch: "Elmish/MVU", lang: "F#" }
status: spec
---

# Breakout

## 1. Overview
Breakout is a single-screen arcade game in which the player slides a horizontal paddle
along the bottom of the screen to keep a bouncing ball in play. The ball ricochets off
the walls, the paddle, and a wall of colored bricks at the top; each brick the ball
strikes is destroyed and scores points. The core verb is **deflect** — the player reads
the ball's trajectory and positions the paddle to both survive and to *aim* the rebound
at the bricks they want to clear. It is fun because of the tight, deterministic physics:
a skilled player controls the ball's angle by where it hits the paddle, turning a
reactive defensive game into an offensive one of carving tunnels through the wall and
trapping the ball above it for a cascade of free hits.

## 2. Core Game Loop
**Moment-to-moment:** read ball trajectory → reposition paddle → deflect ball → control
rebound angle → break bricks → collect/avoid power-ups → repeat. The tension cycle is
"ball descending toward the gutter" → "successful save" → brief relief → repeat.

**Session-level:** Title screen → Serve (ball stuck to paddle, player launches) → Play
(clear the brick wall while keeping the ball alive) → either **Level Clear** (advance to
next layout, ball re-serves) or **Ball Lost** (lose a life; re-serve if lives remain) →
**Game Over** when lives reach 0 → Score summary / high-score table → Restart.

## 3. Controls & Input
Keyboard is primary. Mouse is an optional alternative for paddle movement. Input model
noted per row (held = continuous while down; pressed = edge-triggered on key-down).

| Input | Action | Model |
|---|---|---|
| `Left Arrow` / `A` | Move paddle left | Held |
| `Right Arrow` / `D` | Move paddle right | Held |
| `Space` | Launch ball from paddle (during Serve); fire laser (with Laser power-up) | Pressed (edge) |
| `Mouse X` | Move paddle to mouse X (clamped to playfield) | Continuous (optional) |
| `Left Click` | Launch ball / fire laser | Pressed (edge) |
| `P` / `Esc` | Pause / resume | Pressed (edge) |
| `Enter` | Start game (title) / restart (game over) | Pressed (edge) |
| `M` | Toggle mute | Pressed (edge) |

Notes: keyboard and mouse paddle control are mutually live — the most recent input source
wins for the current frame. Holding both Left and Right cancels to zero horizontal input.

## 4. Mechanics (detailed)

### 4.1 Coordinate system & playfield
Logical playfield is **1280×720** px, origin top-left, +x right, +y down. A **wall border**
of 16 px frames the top, left, and right edges (bricks and ball bounce off the inner
faces). The **bottom edge is open** (the "gutter"): a ball crossing y = 720 is lost. The
HUD occupies the top 48 px band; the brick field begins below it.

### 4.2 Paddle movement
- Paddle size: **104×18** px (default; the Widen power-up makes it 168 px).
- Horizontal speed: **620 px/s** (keyboard). Movement is velocity-based, no acceleration
  ramp (instant start/stop) for crisp arcade feel; friction is irrelevant.
- Paddle Y is fixed at **y = 680** (top edge of paddle).
- Clamped so the paddle stays fully inside the side walls: `x ∈ [16, 1280 − 16 − width]`.
- Mouse control: paddle center snaps to mouse X each frame, then clamped identically.

### 4.3 Ball physics
- Ball is a circle, radius **7** px (diameter 14).
- Constant speed magnitude; direction changes on bounce. **Speed is preserved exactly on
  wall and brick bounces** (perfectly elastic, no energy loss).
- Base speed **360 px/s** at level 1 serve. Speed increases with progression (see §4.5
  and §6).
- Gravity: **none**. Drag: **none**. The ball travels in straight lines between
  collisions.
- Wall bounce: reflect the velocity component normal to the wall (left/right walls flip
  `vx`; top wall flips `vy`). Reposition the ball just outside the wall to prevent
  tunneling/sticking.
- Minimum vertical speed guard: after any bounce, if `|vy| < 60 px/s` the ball is nudged
  so `|vy| = 60` (preserving total speed by recomputing `vx`). This prevents a
  near-horizontal ball from getting stuck ping-ponging between side walls forever.

### 4.4 Paddle deflection & angle control (the skill mechanic)
When the ball collides with the top face of the paddle, the rebound angle is **not** a
mirror reflection — it is determined by the **impact offset** from the paddle center:

```
offset   = (ball.x − paddleCenterX) / (paddleWidth / 2)   // ∈ [−1, 1]
maxAngle = 60°                                             // from vertical
bounceAngle = offset * maxAngle
vx = speed * sin(bounceAngle)
vy = −speed * cos(bounceAngle)   // always upward
```

So hitting the far-left edge sends the ball up-left at ~60° from vertical, dead center
sends it straight up, far-right sends it up-right at ~60°. `offset` is clamped to
[−1, 1]. The ball's `vy` is always forced negative (upward) after a paddle hit. Ball
speed magnitude is unchanged by paddle contact (control, not acceleration).

A small bonus: if the paddle is moving when it strikes the ball, add **15%** of paddle
velocity to `vx` (then renormalize to constant speed) — a subtle "english"/spin that
rewards active play. Capped so total horizontal angle never exceeds 75°.

### 4.5 Brick collision & destruction
- The ball checks against bricks using swept circle-vs-AABB (or, acceptably for v1,
  discrete AABB overlap with the ball's bounding box, since dt is small).
- On overlap, determine the dominant penetration axis: smaller overlap on X → flip `vx`;
  smaller on Y → flip `vy`. Reposition out of the brick to avoid double hits.
- Each hit reduces the brick's HP by 1. At HP 0 the brick is destroyed, awards points
  (§11), and may spawn a power-up (§4.6). Most bricks are HP 1; **silver** bricks are HP 2
  and **gold** bricks are indestructible (HP ∞, bounce only, no score).
- **One brick destroyed per frame max** is *not* enforced — multiple bricks may be hit in
  a single physics step (e.g. multiball), but a single ball resolves at most one brick
  collision per substep to keep reflection sane.
- Speed bump: every time the **cumulative bricks destroyed** crosses a multiple of 8, ball
  speed increases by **+8%** up to a cap of **620 px/s**. Speed also steps up when the ball
  first touches the top wall and when it breaks an orange or red brick (classic Atari
  rule), whichever raises it — see §12 for the tunable.

### 4.6 Power-ups
When a destructible brick is destroyed there is a **12%** chance to drop a power-up capsule.
Capsules fall straight down at **140 px/s** and are collected when they overlap the paddle;
a capsule reaching the gutter is lost. At most **1 power-up active per category**; collecting
a new capsule refreshes/replaces. Capsule size: 28×14 px.

| Power-up | Color | Effect | Duration |
|---|---|---|---|
| **Sticky** (Catch) | Green | Ball sticks to paddle on contact; player re-launches with Space/Click. Aim by moving paddle, then launch. | Until 3 catches used, or level end |
| **Multiball** | Cyan | Splits the active ball into **3** balls at ±25° spread; all share current speed. | Instant (balls persist) |
| **Widen** | Blue | Paddle width 104 → **168** px. | 15 s |
| **Laser** | Red | Paddle gains twin cannons; Space/Click fires a laser bolt that travels up at 700 px/s and destroys one brick (HP −1) on contact. Fire rate capped at 4/s. | 12 s |

Power-up drop selection is uniform across the four types unless tuned (§12). Capsules are
allowed to stack on screen, but a hard cap of **3 falling capsules** prevents clutter
(excess drops are skipped).

### 4.7 Lives & serving
- Start with **3 lives**.
- A life is lost when the **last** active ball crosses y = 720. (With multiball, losing
  one ball while others are in play costs nothing.)
- On life loss (lives remain): reset to **Serve** state — a single ball is placed centered
  on the paddle, stuck, awaiting launch. Power-up timers and Widen/Laser are cleared on
  life loss; the layout (remaining bricks) persists.
- Bonus life at **20,000** points (once).

## 5. Entities / Game Objects

### 5.1 Paddle
- Size 104×18 (or 168×18 widened), HP n/a. Position (x = left edge, y = 680 fixed).
- State: `Normal | Widened | Sticky | Laser` flags can co-exist (composed in Model).
- Created at game start; never destroyed; reset on life loss.

### 5.2 Ball
- Circle r = 7. Properties: position (cx, cy), velocity (vx, vy), `stuck: bool`.
- Behavior: free-flight straight-line motion; reflects on collisions; "stuck" balls track
  the paddle and ignore physics until launched.
- Created on serve / split by multiball; destroyed when it exits the gutter.

### 5.3 Brick
- Size **64×24** px, 2 px gap between bricks. Properties: row, col, color, `hp`, `points`,
  `breakable: bool`.
- Behavior: static; decrement HP on hit; destroyed at HP 0.
- Created from level layout; destroyed by ball/laser.

### 5.4 PowerUp capsule
- Size 28×14. Properties: position, falling velocity (0, 140), `kind: PowerUpKind`.
- Behavior: falls; collected on paddle overlap; lost at gutter.

### 5.5 Laser bolt
- Size 4×16. Velocity (0, −700). Destroys one brick HP on contact; despawns on hit or at
  top wall.

F#-flavored sketch:
```fsharp
open FS.GG.Game.Core
// Positions/velocities live in the scaffold's collision-safe Geometry.Vec2 ({ Vx; Vy }, from
// src/<ProductDir>/Vec2.fs) — NEVER a record you label X/Y/Width/Height, which collide with
// Scene's Point/Rect. This is a type ABBREVIATION: it adds no labels, so nothing can collide.
type Vec2 = Geometry.Vec2

type Color = Yellow | Orange | Green | Red | Silver | Gold

type Brick =
    { Row: int; Col: int
      Bounds: Rect            // x, y, w=64, h=24
      Color: Color
      Hp: int                 // 1, 2, or Int32.MaxValue for Gold
      Points: int
      Breakable: bool }

type Ball =
    { Pos: Vec2; Vel: Vec2; Radius: float; Stuck: bool }

type PowerUpKind = Sticky | Multiball | Widen | Laser
type Capsule = { Pos: Vec2; Kind: PowerUpKind }
type Bolt = { Pos: Vec2 }
```

## 6. World / Levels / Progression

### 6.1 Brick grid layout
Playfield 1280×720. Brick field: **18 columns × 8 rows**.
- Brick cell: 64×24 with 2 px horizontal gap → 18 × 66 = 1188 px wide. Centered: left
  margin = (1280 − 1188) / 2 + 1 ≈ **47** px (inside the 16 px wall). Vertical gap 2 px.
- Field top at **y = 96** (below the 48 px HUD + 48 px breathing room). Rows occupy
  y = 96 … 96 + 8×26 = 304.

Default **Level 1** color rows (top → bottom), classic Atari point scheme:

| Rows | Color | Hex | HP | Points |
|---|---|---|---|---|
| 1–2 (top) | Red | `#D8482E` | 1 | 7 |
| 3–4 | Orange | `#E07A18` | 1 | 5 |
| 5–6 | Green | `#3FA34D` | 1 | 3 |
| 7–8 (bottom) | Yellow | `#E0C020` | 1 | 1 |

### 6.2 Level progression
At least **4** hand-authored layouts that cycle (after level 4, repeat with higher speed):

1. **Classic Wall** — full 18×8 grid as above.
2. **Fortress** — 18×8 grid with a 2-row band of **Silver** (HP 2, 50×level pts) across
   rows 3–4 and a single **Gold** (indestructible) brick in the center of row 1.
3. **Checkerboard** — alternating bricks present/absent in a checker pattern; gaps let the
   ball weave through; mixes Green/Orange.
4. **Tunnels** — three vertical Gold columns split the wall into channels, encouraging the
   tunnel-and-trap strategy; Red/Orange fill.

### 6.3 Difficulty ramp
- Ball serve speed: **360 + 20 × (level − 1)** px/s (capped contribution; total ball speed
  still capped at 620).
- Power-up drop chance falls slightly: `max(6%, 12% − 1% × (level−1))`.
- Paddle width and speed are constant across levels (player skill is the variable).
- "Speed-up triggers" (top-wall touch, orange/red brick break, every 8 bricks) apply each
  level.

## 7. State Model (Elmish/MVU)

### 7.1 Model
```fsharp
type Phase =
    | Title
    | Serve                      // ball stuck on paddle, awaiting launch
    | Playing
    | Paused
    | LevelClear of sinceMs: float
    | GameOver

// NOT X/Width — those labels collide with Scene's Point/Rect (see §5 sketch). The widen power-up
// makes the paddle's width real model state, so it stays a field; only the label changes.
type Paddle =
    { LeftX: float               // left edge
      WidthPx: float             // 104 or 168
      Vx: float                  // last-frame velocity (for english)
      StickyCharges: int         // >0 => catch mode
      LaserUntilMs: float option
      WidenUntilMs: float option }

type Model =
    { Phase: Phase
      Paddle: Paddle
      Balls: Ball list
      Bricks: Brick list
      Capsules: Capsule list
      Bolts: Bolt list
      Score: int
      Lives: int
      Level: int
      BricksDestroyed: int       // cumulative, drives speed-up
      BallSpeed: float
      HighScore: int
      AwardedBonusLife: bool
      ElapsedMs: float
      Rng: Rng                // FS.GG.Game.Core — a VALUE, so the Model stays one (§13)
      Input: InputState }        // current held-key snapshot

and InputState =
    { Left: bool; Right: bool; MouseX: float option }
```

### 7.2 Msg
```fsharp
type Msg =
    | Tick of dt: float          // seconds, ~1/60
    | KeyDown of Key
    | KeyUp of Key
    | MouseMove of x: float
    | LaunchBall                 // from Space/Click during Serve or Sticky
    | FireLaser
    | TogglePause
    | StartGame
    | Restart
    | ToggleMute
```

### 7.3 update (key transitions)
- `StartGame` (from `Title`): build Level 1, set 3 lives, score 0, enter `Serve`.
- `LaunchBall` (from `Serve`): set ball `Stuck = false`, give it an upward velocity at the
  current angle (straight up, or slight angle from offset); → `Playing`.
- `Tick dt` (in `Playing`):
  1. Integrate paddle from `Input` (clamp); record `Vx`.
  2. Move stuck balls to follow paddle; integrate free balls.
  3. Resolve ball–wall, ball–paddle (§4.4), ball–brick collisions; update score,
     `BricksDestroyed`, ball speed; roll power-up drops.
  4. Integrate capsules; collect on paddle overlap → apply power-up; cull gutter.
  5. Integrate bolts; resolve bolt–brick; cull off-screen.
  6. Expire timed power-ups by `ElapsedMs`.
  7. Remove balls past gutter; if no balls remain → lose life (→ `Serve` or `GameOver`).
  8. If no breakable bricks remain → `LevelClear`.
- `LevelClear` after a 1.5 s delay: increment `Level`, build next layout, re-serve.
- `TogglePause`: `Playing ↔ Paused` (freezes Tick integration).
- `Restart` (from `GameOver`): persist high score, → `Title`.
- Input messages mutate `Input`/edge-trigger actions only; physics never runs outside
  `Tick` (keeps it deterministic).

### 7.4 view
Pure projection of `Model` → a draw command list (no side effects). Renders bricks,
paddle, balls, capsules, bolts, HUD text, and the current screen overlay based on `Phase`.
Skia executes the draw list (§8).

### 7.5 Subscriptions
- **Tick**: a 60 FPS timer (target dt = 1/60 ≈ 0.0167 s) emits `Tick dt` with the real
  elapsed delta, clamped to a max of 0.05 s to avoid spiral-of-death after a stall.
- **Input**: keyboard down/up and mouse-move events from the FS.GG.Rendering host mapped to
  `KeyDown/KeyUp/MouseMove`.

## 8. Rendering (Skia 2D)
Coordinate system matches the logical playfield (1280×720); the host scales to the window.
**Draw order (back → front):**
1. Background fill `#101018` (near-black blue). Optional subtle vertical gradient.
2. Wall border: 16 px frame, `#3A3A48`.
3. Bricks: filled `SKRect` per brick in its color hex; 1 px inner highlight (`+15%`
   lightness top/left) and shadow (`−15%` bottom/right) for a beveled look. Silver
   `#B8B8C0`, Gold `#E8C53A`. Destroyed bricks are simply absent from the list.
4. Capsules: rounded rect (radius 4) in the power-up color with a 1-letter glyph
   (S/M/W/L).
5. Paddle: rounded rect (radius 6), `#C8D0E0`; when Laser active draw two small cannon
   nubs; when Widened it's simply longer.
6. Laser bolts: 4×16 `#FF4040` rects with additive glow.
7. Balls: filled white circles `#FFFFFF` r = 7, plus a short 3-sample motion trail
   (previous positions, alpha 0.3 → 0).
8. HUD text (§9) and screen overlays (title/pause/game-over) with a 60% black scrim.

Particles: on brick destruction, emit **6–8** small square particles in the brick's color,
initial speed 80–160 px/s in a hemisphere away from impact, gravity 400 px/s², lifetime
0.4 s, fading alpha. Font: a bundled monospace/arcade font, e.g. "PressStart2P" or fallback
sans-serif bold.

**Redraw strategy:** full-frame clear-and-redraw every Tick (the scene is small; ~150
bricks + a few dynamic objects fit comfortably in the 16.7 ms budget). No dirty-rect
optimization needed for v1.

## 9. UI / HUD / Screens

**HUD** (top 48 px band, `#101018` with bottom border line):
- **Score** — left, x = 24, baseline y = 32, format `SCORE 0001234` (zero-padded 7).
- **High** — center, `HI 0050000`.
- **Lives** — right, x = 1256 right-aligned, rendered as up to 3 small paddle icons.
- **Level** — small, under score, `LV 02`.

**Screens:**
- **Title:** game name centered (~y = 240), "PRESS ENTER TO START" blinking at y = 420,
  high score below, control hints at bottom.
- **Play:** HUD + playfield.
- **Serve overlay:** "PRESS SPACE TO LAUNCH" pulsing near the paddle.
- **Pause:** scrim + "PAUSED" centered.
- **Level Clear:** "LEVEL n CLEAR" + brief countdown, 1.5 s.
- **Game Over:** scrim + "GAME OVER", final score, "NEW HIGH SCORE!" if beaten, "PRESS
  ENTER" to return to title.

### 9.1 Menu system (detailed)
A single **menu stack** drives every non-play screen (Title, Settings, Stats, Pause, Game
Over). Each menu is a vertical list of rows with a cursor, so one small update handler
serves them all and navigation is identical everywhere; the reused row style is the §9
Title/overlay look (selected row inverted, non-selected `#FFFFFF` on the `#101018` scrim).

**Menu tree**
```
Title ─┬─ Play ──────────── build Level 1, 3 lives, enter Serve (§7.3 StartGame)
       ├─ Stats ─────────── Stats & Charts screen (§9.2)
       ├─ Settings ──────┬─ Difficulty     ◄ Casual · Standard · Expert ►
       │                 ├─ Master volume  ◄ 0 – 100 ►
       │                 ├─ Sound          ◄ On · Off ►
       │                 ├─ Window scale   ◄ 1× · 2× · Fit ►
       │                 ├─ Colorblind assist  ◄ On · Off ►
       │                 └─ Back
       └─ Quit

Pause ─┬─ Resume
       ├─ Restart ───────── new run: fresh 3 lives, Level 1
       ├─ Settings ──────── (same submenu; returns to Pause)
       └─ Quit to Title

Game Over ─┬─ New Game ──── run ended at 0 lives; start a fresh 3-life run (no continues, §11)
           ├─ View Stats ── Stats & Charts (§9.2)
           └─ Quit to Title
```

**Navigation model**
- `MenuCursor: int` on the active menu; `↑` decrements, `↓` increments, both **wrap** around
  the ends.
- `Enter`/`Space` activates the current row; `Esc`/`Backspace` (or `P` from Pause) pops the
  stack (**Back**).
- **Cycler/slider rows** (Difficulty, Master volume, Sound, Window scale, Colorblind assist):
  `←`/`→` change the value in place; the row shows a right-aligned `◄ value ►` widget.
- Rendering reuses the §9 Title-screen selector style: the selected row is inverted, the
  rest are `#FFFFFF` at HUD text size on the standard 60% black scrim.

**Msg additions** (extend §7.2, over the game's `Key` type):
```fsharp
    | MenuUp | MenuDown              // move cursor (wraps)
    | MenuAdjust of dir:int          // -1 / +1 on a cycler/slider row
    | MenuActivate                   // Enter/Space on the current row
    | MenuBack                       // Esc/Backspace/P — pop the menu stack
    | OpenStats | CloseStats         // enter / leave the Stats screen (§9.2)
```

Settings apply live and persist to local config (§13, beside `breakout.highscore`):
**Difficulty** selects a §12 tunable preset — Casual `ballBaseSpeed 300 / paddleWidth 120 /
dropChance 16% / lives 5`, Standard the §12 defaults, Expert `420 / 88 / 8% / 2`;
**Master volume**/**Sound** route to `Audio.setMasterVolume` (§10, clamped `[0,1]`; Sound Off
is volume `0.0`, matching the `M` mute toggle of §3); **Colorblind assist** swaps the §6.1
brick palette for the §8 colorblind-safe variant.

### 9.2 Stats & charts screen
The Stats screen visualizes **the last run** and **lifetime** play. It reads a stats
snapshot (never live physics), so it is a pure, deterministic render reachable from Title,
Game Over, and Pause. Chart-design choices below follow the project dataviz conventions
(form-first, validated colorblind-safe palette, single axis, identity by entity).

**Tracked per run** — `MatchStats`, accumulated in `Tick` (§7.3), snapshotted on `GameOver`:

| Field | Type | Updated |
|-------|------|---------|
| `bricksBroken` | `int` | +1 per brick destroyed (Tick step 3 / laser step 5) |
| `ballsLost` | `int` | +1 when the last active ball crosses the gutter (§4.7, step 7) |
| `levelsCleared` | `int` | +1 on each `LevelClear` transition |
| `longestNoMissStreak` | `int` | max run of paddle deflects between gutter losses |
| `powerupsByKind` | `Map<PowerUpKind,int>` | +1 on each capsule collected (§4.6, step 4) |
| `bricksPerLevel` | `int list` | bricks cleared this level, appended on `LevelClear` |
| `scorePerLevel` | `int list` | points earned this level, appended on `LevelClear` |
| `scoreTimeline` | `(brick:int * cum:int) list` | cumulative score appended per brick broken |
| `playSeconds` | `float` | accumulated unpaused Tick time |

**Lifetime** — `LifetimeStats`, persisted (§13): `highScore`, `gamesPlayed`,
`levelsClearedTotal`, `bestCombo`, `bestClearPercent` (furthest single-level brick-clear
fraction reached in a losing run).

**Layout** (logical 1280×720): a KPI tile row across the top, two charts below.

```
┌───────────────────────── STATS ─────────────────────────┐
│ ┌HIGH SCORE┐ ┌ CLEAR % ┐ ┌ LEVELS ┐ ┌BEST COMBO┐        │  ← KPI stat tiles
│ │  28 400  │ │  86 %   │ │   7    │ │   x14    │        │
│ └──────────┘ └─────────┘ └────────┘ └──────────┘        │
│                                                          │
│  Bricks cleared per level        Cumulative score        │
│  ▇▇                          28k ┤            ╭───        │
│  ▇▇  ▇▇  ▇▇                      │        ╭──╯            │
│  ▇▇  ▇▇  ▇▇  ▇▇  ▇▇              │    ╭──╯                │
│  1   2   3   4   5  (level)    0 ┼──────────────► brick # │
└──────────────────────────────────────────────────────────┘
     ↑/↓ scope:  ▸ This Run · Lifetime          ESC — Back
```

**Charts** (rendered in Skia with the same draw-list discipline as §8):

1. **Bricks cleared per level** — *form: per-category magnitude → bars.* x = level index
   (`1,2,…,N`), y = bricks cleared in that level (from `bricksPerLevel`). **Single series**,
   so one hue and no legend. Bars are 4 px-rounded at the data end with a 2 px surface gap
   between them. Fill `#2a78d6` (light) / `#3987e5` (dark) — validated categorical slot 1.
2. **Cumulative score** — *form: change over an ordered index → line.* x = brick number
   (cumulative bricks broken), y = cumulative score (from `scoreTimeline`). **One series**,
   so one hue and no legend. 2 px line, ≥ 8 px end marker, recessive 1 px gridlines in
   `#3C3C3C`; line color `#2a78d6` (light) / `#3987e5` (dark).

Conventions honored: **color follows the entity** (the run's series is always slot 1 —
never repainted by the scope toggle); **one axis only** (no dual-scale); chart **text uses
ink tokens** (`#FFFFFF` primary / `#C3C2B7` muted), never the series hue; layout is **fixed
and deterministic**, so a fixed-seed run (§13) renders byte-identical for snapshot tests.
The `↑/↓` **scope** toggle swaps the data source This-Run ↔ Lifetime without changing colors.

**Model/Msg hooks:** add `Stats: MatchStats` and `Lifetime: LifetimeStats` to §7.1 Model;
accumulate them in the `Tick` cases (bump `bricksBroken` and append a `scoreTimeline` point
in the brick-collision step 3, advance a running paddle-deflect counter on §4.4 hits and
commit its max to `longestNoMissStreak` on the gutter loss of step 7, increment
`powerupsByKind` on collect in step 4, add `dt` to `playSeconds` each unpaused Tick); on
`LevelClear` append `bricksPerLevel`/`scorePerLevel` and bump `levelsCleared`; on `GameOver`
snapshot `MatchStats`, fold it into `Lifetime` and persist (§13, beside `breakout.highscore`).
`OpenStats`/`CloseStats` switch a `Stats of scope:StatScope` phase (§7.1) reachable from
Title, Pause, and Game Over; the render is a pure no-op on physics.

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
| Ball ↔ wall bounce (§4.3) | `Audio.playSfx` | `wall-bounce` | short low blip |
| Ball ↔ paddle (§4.4) | `Audio.playSfx` | `paddle-bounce` | mid blip (pitch varies with offset) |
| Ball ↔ brick (§4.5) | `Audio.playSfx` | `brick-hit` | pitched click; pitch rises with row color (yellow→red) |
| Brick destroyed, multi-HP (§4.5) | `Audio.playSfx` | `brick-break` | heavier crunch on final hit |
| Power-up capsule collected (§4.6) | `Audio.playSfx` | `powerup-collect` | ascending chime (distinct per kind) |
| Laser fire (§4.6) | `Audio.playSfx` | `laser-fire` | pew |
| Laser hit (§4.6) | `Audio.playSfx` | `laser-hit` | zap |
| Life lost (§4.7) | `Audio.playSfx` | `life-lost` | descending tone |
| Level clear | `Audio.playSfx` | `level-clear` | short fanfare |
| Game over | `Audio.playSfx` | `game-over` | longer descending jingle |
| Bonus life (§4.7) | `Audio.playSfx` | `bonus-life` | sparkle |

Play has a looping soundtrack: entering `Playing` requests `Audio.playMusic (TrackId "bgm")
true` (looping ambient/chiptune at low volume during Play), and `Audio.stopMusic` fires
where the music stops — on Pause, and on `GameOver`/return to Title. The `M` mute toggle
(§3) maps to `Audio.setMasterVolume` (`0.0` to silence all audio, back to `1.0` to restore).
**Testing:** collect the frame's
`AudioEffect`s, `Audio.interpret` them, and assert the `AudioEvidence.Requested` sequence for
representative events (e.g. a ball–paddle deflection requests exactly `PlaySfx (SoundId "paddle-bounce", _)`).

## 11. Win / Loss / Scoring

**Scoring (per brick destroyed):**
- Yellow = 1, Green = 3, Orange = 5, Red = 7 (per §6.1).
- Silver = **50 × level** (only on the hit that destroys it, HP 2).
- Gold = 0 (indestructible).
- Combo: no multiplier in v1 (kept simple); see Stretch.

**Win condition:** clearing all *breakable* bricks → `LevelClear` → next level. The game
loops levels indefinitely (Atari-style); there is no final win screen in v1 — the goal is a
high score. (A "completed all 4 unique layouts twice = win" variant is a Stretch goal.)

**Loss condition:** lose a life when the last ball exits the gutter; **Game Over** at 0
lives. No continues in v1.

**Lives:** start 3; bonus life at 20,000 points (once); max display 3 icons (extra lives
counted but capped in the icon row).

## 12. Difficulty & Balancing
Data-driven tunables (defaults shown):

| Name | Default | Range | Effect |
|---|---|---|---|
| `ballBaseSpeed` | 360 px/s | 240–480 | Serve speed at level 1 |
| `ballSpeedCap` | 620 px/s | 480–800 | Max ball speed |
| `speedUpPerN` | +8% / 8 bricks | 0–20% | Periodic speed increase |
| `paddleSpeed` | 620 px/s | 400–900 | Keyboard paddle velocity |
| `paddleWidth` | 104 px | 72–140 | Normal paddle width |
| `widenWidth` | 168 px | 120–220 | Widened width |
| `maxBounceAngle` | 60° | 45–75 | Paddle deflection range |
| `minVerticalSpeed` | 60 px/s | 30–120 | Anti-stall vy guard |
| `lives` | 3 | 1–5 | Starting lives |
| `dropChance` | 12% | 0–30% | Power-up drop probability |
| `capsuleFallSpeed` | 140 px/s | 80–240 | Capsule descent |
| `multiballCount` | 3 | 2–5 | Balls after split |
| `widenDurationMs` | 15000 | 5k–30k | Widen lifetime |
| `laserDurationMs` | 12000 | 5k–30k | Laser lifetime |
| `bonusLifeScore` | 20000 | 5k–50k | One-time extra life |
| `levelSpeedStep` | 20 px/s | 0–60 | Serve speed added per level |

## 13. Technical Notes
- **Performance budget:** ~150 bricks + ≤8 balls + ≤3 capsules + ≤8 bolts + ≤64 particles.
  Trivial for 60 FPS / 16.7 ms; the bottleneck is brick draw (batch into one path/loop).
- **Fixed vs. variable timestep:** physics uses the variable `dt` from `Tick`, **clamped to
  ≤ 0.05 s** — **deliberately not `FixedStep.drain`**, and the reason is worth stating because most of
  this corpus goes the other way. Determinism here (§14.16) is replay of the recorded `Msg` log, and
  every `Tick` carries its own `dt`, so replaying the log reproduces the run exactly *without* a fixed
  accumulator: it would buy nothing the log does not already give. What actually prevents tunneling is
  sub-stepping — integrate the ball so no ball moves more than its radius (7 px) per substep
  (`n = ceil(speed*dt/7)`). Reach for `FixedStep.drain` the day the sim must advance independently of
  the frame (a headless run, a networked one, a fixed-rate replay); until then this is the simpler
  correct thing.
- **Determinism / RNG:** all randomness (drop rolls, drop kind, multiball spread, particles) draws
  from `Model.Rng` — `FS.GG.Game.Core`'s **`Rng`** (splitmix64), seeded with `Rng.ofSeed`. A fixed seed
  + recorded input sequence reproduces a run exactly — important for the acceptance tests below, and
  (see the timestep bullet) it is the *only* thing those tests rest on. It is a **value**, not a `System.Random`: every draw returns `struct (x, rng')` and you write
  `rng'` back to the `Model`, so the `Model` stays a value you can snapshot, replay and compare.
  A `System.Random` in the `Model` is a mutable object *shared* by every copy of it, which
  silently breaks the reproducibility this bullet promises.
- **Persistence:** high score stored to a small local file/registry key
  (`breakout.highscore`), loaded on Title, saved on Game Over if beaten.
- **Edge cases:** ball trapped between brick wall and top (tunnel trap) — allowed and
  desirable; corner hits resolve by dominant-axis rule; simultaneous multi-brick hits each
  resolve once; capsule + gutter exit on same frame counts as lost; pausing freezes all
  timers (power-up durations use `ElapsedMs` which only advances on unpaused Ticks).

## 14. Acceptance Criteria (test scenarios)

1. **Launch from serve** — *Given* the game is in `Serve` with a ball stuck to a centered
   paddle, *When* the player presses Space, *Then* the phase becomes `Playing` and the ball
   has speed ≈ 360 px/s with `vy < 0` (upward).

2. **Wall bounce preserves speed** — *Given* a ball moving up-right at 360 px/s, *When* it
   strikes the right wall, *Then* `vx` flips sign, `vy` is unchanged, and the speed
   magnitude remains 360 ± 0.5 px/s.

3. **Paddle angle control** — *Given* a ball descending onto the paddle, *When* it contacts
   the **far-right** edge (offset ≈ +1), *Then* the rebound travels up-right at ≈ 60° from
   vertical; *And When* it contacts dead center (offset ≈ 0), *Then* the rebound is within
   ±3° of straight up.

4. **Brick destroyed scores correctly** — *Given* a level-1 wall, *When* the ball destroys
   one **red** brick, *Then* score increases by exactly 7 and that brick is removed from
   `Bricks`.

5. **Silver brick takes two hits** — *Given* a Silver brick (HP 2) at level 2, *When* the
   ball hits it once, *Then* it bounces, HP becomes 1, and no points are awarded; *When*
   hit again, *Then* it is destroyed and score increases by 100 (50 × level 2).

6. **Gold brick is indestructible** — *Given* a Gold brick, *When* the ball hits it any
   number of times, *Then* the ball always bounces, the brick remains, and score is
   unchanged.

7. **Level clear advances** — *Given* one breakable brick remaining, *When* the ball
   destroys it, *Then* phase becomes `LevelClear`, and after 1.5 s `Level` increments and a
   fresh layout is built with the ball re-served.

8. **Life lost on gutter exit** — *Given* a single ball in play with 3 lives, *When* the
   ball crosses y = 720, *Then* `Lives` becomes 2 and phase returns to `Serve`.

9. **Multiball does not cost a life until last ball lost** — *Given* the Multiball power-up
   produced 3 balls, *When* one ball exits the gutter, *Then* `Lives` is unchanged and 2
   balls remain in play; *And When* the final ball exits, *Then* a life is lost.

10. **Game over at zero lives** — *Given* 1 life remaining and one ball in play, *When* the
    ball exits the gutter, *Then* `Lives` becomes 0 and phase becomes `GameOver`.

11. **Power-up drop & collect (Widen)** — *Given* a deterministic seed that forces a Widen
    drop, *When* the brick is destroyed a capsule falls at 140 px/s, *And When* the paddle
    overlaps it, *Then* paddle width becomes 168 px and reverts to 104 px after 15 s of
    unpaused play.

12. **Laser fires and breaks a brick** — *Given* the Laser power-up is active, *When* the
    player presses Space, *Then* a bolt spawns moving up at 700 px/s, *And When* it contacts
    a breakable brick, *Then* that brick loses 1 HP and the bolt despawns.

13. **Anti-stall guard** — *Given* a bounce that would leave `|vy| < 60 px/s`, *When* the
    bounce resolves, *Then* `|vy|` is corrected to ≥ 60 px/s while total speed is preserved
    within 0.5 px/s.

14. **Input: held movement & clamp** — *Given* the paddle at the left wall, *When* `Left` is
    held for 1 s, *Then* the paddle does not move past x = 16 (stays clamped at the wall);
    *And When* `Right` is held, *Then* it moves rightward at ≈ 620 px/s.

15. **Pause freezes timers** — *Given* an active Widen with 10 s remaining, *When* the game
    is paused for 5 s and resumed, *Then* the Widen still has ≈ 10 s remaining (timer used
    only unpaused Ticks) and no ball/brick state changed during the pause.

16. **Determinism** — *Given* a fixed RNG seed and a recorded input sequence, *When* the
    sequence is replayed, *Then* the final `Score`, `Lives`, `Level`, and surviving brick
    set are identical across runs.

17. **Bonus life** — *Given* score just below 20,000 with the bonus not yet awarded, *When*
    a hit pushes score to ≥ 20,000, *Then* `Lives` increases by 1 once and never again.

## 15. Stretch Goals
1. **Combo multiplier** — consecutive brick hits without a paddle touch increase a score
   multiplier (rewards tunnel-trap play).
2. **Persistent power-up loadout & shop** between runs.
3. **Boss / moving brick formations** that shift horizontally.
4. **Two-player alternating** (classic Atari) and **co-op** (two paddles) modes.
5. **Editor** for custom brick layouts (data-driven layout files already enable this).
6. **"Win" mode** — finish all unique layouts twice for a victory screen + ranking.
7. **Screen-shake & juicier particles**, ball trails scaling with speed.
8. **Accessibility:** colorblind-safe brick palette toggle, adjustable ball speed.

## 16. Milestone Roadmap

Implementation is sequenced into milestones; each item is a colored checkbox
tracking its status. Items reference the section that specifies them.

**Legend:** 🟥 Not started · 🟨 In progress · 🟩 Done · ⬜ Deferred (post-v1)

_All items start 🟥 (spec status). Flip an item to 🟨 when work begins and 🟩 once
its acceptance test(s) pass (§14)._

### M0 — Scaffold & game loop
- 🟥 Project scaffold: `Model`/`Msg`/`update`/`view` skeleton (§7)
- 🟥 Variable-`dt` `Tick` clamped to ≤ 0.05 s, sub-stepped so no ball moves > 7 px/substep (§7.5, §13)
- 🟥 `Rng` value seeded with `Rng.ofSeed`, threaded through `Model` (§13)
- 🟥 Logical 1280×720 coordinate system + host letterbox scaling (§4.1, §8)

### M1 — Paddle & input
- 🟥 Held left/right + edge-triggered actions; both-held cancels to zero (§3)
- 🟥 Velocity-based paddle at 620 px/s, wall clamp `x ∈ [16, 1280 − 16 − width]` (§4.2) — AC #14
- 🟥 Optional mouse-X paddle control, most-recent-source-wins (§3, §4.2)

### M2 — Ball physics & serve
- 🟥 Constant-speed straight-line ball integration, no gravity/drag (§4.3)
- 🟥 Serve state: ball stuck on paddle, launch upward on Space/Click (§4.7, §7.3) — AC #1
- 🟥 Wall bounce reflects normal component, speed preserved exactly (§4.3) — AC #2
- 🟥 Anti-stall guard nudges `|vy| ≥ 60 px/s`, total speed preserved (§4.3) — AC #13

### M3 — Paddle deflection (skill mechanic)
- 🟥 Impact-offset angle control, max 60° from vertical, `vy` forced upward (§4.4) — AC #3
- 🟥 Paddle-velocity "english" (+15% vx), total angle capped at 75° (§4.4)

### M4 — Bricks, collisions & scoring
- 🟥 Brick grid built from layout (18×8, color/HP/points) (§5.3, §6.1)
- 🟥 Ball–brick dominant-axis reflection + reposition, single-resolve per substep (§4.5)
- 🟥 HP decrement; Silver HP 2, Gold indestructible (§4.5) — AC #5, #6
- 🟥 Per-color scoring, Silver 50×level, Gold 0 (§11) — AC #4
- 🟥 Speed-ups (every 8 bricks / top-wall / orange-red), cap 620 px/s; tunneling sub-step guard (§4.5, §13)

### M5 — Lives, serving & power-ups
- 🟥 Lives (start 3); life lost only when last ball exits gutter, re-serve; Game Over at 0 (§4.7) — AC #8, #10
- 🟥 Bonus life at 20,000 points, once (§4.7, §11) — AC #17
- 🟥 Capsule drop roll (12%), fall at 140 px/s, collect on paddle overlap, 3-capsule cap (§4.6)
- 🟥 Sticky / Widen / Laser effects, one-per-category, timed expiry by `ElapsedMs` (§4.6) — AC #11
- 🟥 Multiball 3-ball ±25° split; life kept until last ball lost (§4.6, §4.7) — AC #9
- 🟥 Laser bolt spawns, travels up 700 px/s, breaks one brick HP (§4.6, §5.5) — AC #12

### M6 — Levels & progression
- 🟥 Level clear on last breakable brick → next layout, re-serve after 1.5 s (§7.3, §11) — AC #7
- 🟥 Four cycling layouts: Classic Wall / Fortress / Checkerboard / Tunnels (§6.2)
- 🟥 Difficulty ramp: per-level serve speed, drop-chance falloff (§6.3)

### M7 — Rendering (Skia)
- 🟥 Draw order: background, walls, bricks, capsules, paddle, bolts, balls + trail, HUD (§8)
- 🟥 Beveled bricks, capsule glyphs, ball motion trail, brick-destruction particles (§8)

### M8 — Screens, menus & stats
- 🟥 Phase screens: Title / Serve / Playing / Paused / LevelClear / GameOver + HUD (§9)
- 🟥 Menu stack, cursor wrap, cycler/slider rows; settings apply live + persist (§9.1)
- 🟥 Pause freezes world & power-up timers, resumes exact state (§7.3, §13) — AC #15
- 🟥 `MatchStats`/`LifetimeStats` accumulation + KPI tiles, bar & line charts (§9.2)

### M9 — Audio
- 🟥 `AudioEffect` cues per event, `Audio.interpret` → `AudioEvidence`, volume clamp `[0,1]` (§10)
- 🟥 Looping `bgm` on Play; stop on Pause/GameOver/Title; `M` mute → `setMasterVolume` (§10)

### M10 — Acceptance & determinism
- 🟥 All 17 acceptance scenarios green (§14)
- 🟥 Seed + recorded `Msg`-log replay reproduces Score/Lives/Level/surviving bricks (§13) — AC #16

### Stretch — deferred (post-v1)
- ⬜ Combo multiplier for consecutive brick hits without a paddle touch (§15.1)
- ⬜ Persistent power-up loadout & shop between runs (§15.2)
- ⬜ Boss / moving brick formations that shift horizontally (§15.3)
- ⬜ Two-player alternating & co-op (two-paddle) modes (§15.4)
- ⬜ Editor for custom brick layouts (§15.5)
- ⬜ "Win" mode — finish all unique layouts twice for a victory screen (§15.6)
- ⬜ Screen-shake, juicier particles, speed-scaled ball trails (§15.7)
- ⬜ Colorblind-safe brick palette toggle & adjustable ball speed (§15.8)
