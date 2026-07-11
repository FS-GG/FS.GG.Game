---
title: "Pong"
slug: pong
category: games
complexity: simple
genre: "Arcade / Table Tennis"
target_session_minutes: 5
stack: { rendering: "FS.GG.Rendering (Skia/OpenGL)", arch: "Elmish/MVU", lang: "F#" }
status: spec
---

# Pong

## 1. Overview
Pong is the canonical 1972 arcade table-tennis game: two paddles, one ball, a center
net, and a race to 11 points. The player fantasy is pure reflex mastery — read the
ball, intercept it, and angle your return to wrong-foot the opponent. The core verb is
**deflect**: move your paddle up/down to bounce the ball back across the net. It's fun
because the rules are trivial but the skill ceiling (angling shots, reading a ball that
speeds up every rally) is high. Two modes ship in v1: **single-player vs. AI** and
**local two-player** (hot-seat).

## 2. Core Game Loop
**Moment-to-moment loop:** read ball trajectory → move paddle to intercept → deflect ball
at an angle → opponent returns (or misses) → repeat until a point is scored.

**Point loop:** serve → rally → ball passes a paddle → award point to the other side →
brief pause (1.0 s) → re-serve toward the player who was just scored on.

**Session loop:** Title screen → choose mode (1P / 2P) → play first-to-11 match →
Game Over screen showing winner and final score → restart (same mode) or return to title.

## 3. Controls & Input
Input is **held-state** for paddle movement (polled each tick, not edge-triggered), and
**edge-triggered** for menu/pause actions.

| Input | Context | Action | Model |
|-------|---------|--------|-------|
| `W` | In play | Left paddle up | Held |
| `S` | In play | Left paddle down | Held |
| `↑` (Up) | In play (2P) | Right paddle up | Held |
| `↓` (Down) | In play (2P) | Right paddle down | Held |
| `Space` | Title | Start match (selected mode) | Edge (key-down) |
| `1` | Title | Select 1-Player mode | Edge |
| `2` | Title | Select 2-Player mode | Edge |
| `P` / `Esc` | In play | Toggle pause | Edge |
| `Space` | Game Over | Restart match | Edge |
| `Esc` | Game Over / Pause | Return to title | Edge |

In **1-Player** mode the right paddle is AI-controlled; the player always controls the
left paddle with `W`/`S`. Mouse and gamepad are out of scope for v1 (see Stretch Goals).
Held keys are tracked in a `KeysDown: Set<Key>` set; movement is applied during `Tick`.

## 4. Mechanics (detailed)

### 4.1 Coordinate system & playfield
Logical playfield is **1280 × 720 px**, origin top-left, +x right, +y down. A 12 px-thick
top wall at y=0 and bottom wall at y=708 bound vertical play. Left/right edges (x=0,
x=1280) are the **goal lines** (scoring planes). A dashed center net is drawn at x=640
but is purely cosmetic (no collision).

### 4.2 Paddle movement
- Paddle size: **18 px wide × 110 px tall**.
- Left paddle center-x: **40 px**; right paddle center-x: **1240 px** (so inner faces sit
  at x=49 and x=1231).
- Movement is **velocity-based, no acceleration** (instant on/off for tight arcade feel):
  paddle speed = **600 px/s** while its up/down key is held.
- Clamp paddle so it never overlaps walls: `paddleY` (top edge) clamped to
  `[12, 720 - 12 - 110]` = `[12, 598]`.
- If both up and down are held simultaneously, net movement is 0.

### 4.3 Ball movement & physics
- Ball is a **16 × 16 px** square (classic blocky look), referenced by its center.
- No gravity, no friction — constant-velocity travel between collisions.
- **Serve speed:** 420 px/s. **Speed cap:** 1100 px/s.
- Velocity stored as `(vx, vy)` in px/s. Position integrates each tick: `pos += vel * dt`.

### 4.4 Serve logic
- On serve, ball spawns at center (640, 360).
- Direction: toward the player who was **just scored on** (so the loser receives). On the
  very first serve of a match, direction is chosen by RNG (50/50 left/right).
- Serve angle: pick a random launch angle `θ ∈ [-35°, +35°]` (measured from the horizontal
  axis toward the target side), excluding the near-flat band `|θ| < 8°` to avoid dull
  horizontal serves. Then `vx = ±420·cos θ`, `vy = 420·sin θ`.
- A **0.8 s freeze** holds the ball stationary at center before it launches (telegraph).

### 4.5 Wall collision (top/bottom)
- When ball center-y reaches `12 + 8 = 20` (top) or `708 - 8 = 700` (bottom), reflect:
  `vy = -vy`. Reposition ball just inside the wall to prevent sticking.
- Speed is unchanged by wall bounces.

### 4.6 Paddle collision & angle control
- Collision test: AABB overlap between the 16×16 ball and the 18×110 paddle rect, only
  considered when the ball is moving **toward** that paddle (`vx < 0` for left, `vx > 0`
  for right). This prevents double-hits.
- On hit:
  1. **Reflect horizontally:** `vx = -vx`.
  2. **Angle from contact point:** compute normalized offset
     `u = (ballY - paddleCenterY) / (110/2)`, clamped to `[-1, 1]`. The new bounce angle is
     `θ = u · 50°` (max ±50° off horizontal — hitting the paddle edge sends the ball
     steeply; hitting center sends it flat).
  3. **Speed-up:** multiply current speed by **1.05** (5% per paddle hit), capped at
     1100 px/s.
  4. Recompute velocity from new speed `s` and angle: `vx = sign · s·cos θ`,
     `vy = s·sin θ`, where `sign = +1` off the left paddle, `-1` off the right paddle.
  5. Reposition ball flush against the paddle face to avoid overlap re-trigger.

### 4.7 Scoring plane
- When ball center-x < 0 → **right player scores**.
- When ball center-x > 1280 → **left player scores**.
- After a point: increment score, enter a 1.0 s `PointPause`, then serve toward the
  player who was scored on.

### 4.8 AI opponent (single-player)
The AI controls the right paddle. It uses **tracking with reaction delay and error** so it
is beatable:
- **Dead zone:** AI only moves when the ball is on its half (`ballX > 640`) AND moving
  toward it (`vx > 0`); otherwise it eases its paddle back toward center-y (360) at
  240 px/s (idle recentering).
- **Target:** predicted ball intercept y. v1 uses simple tracking: target = `ballY +
  aimError`, where `aimError` is a per-rally random offset in `[-aiErrorPx, +aiErrorPx]`
  re-rolled on each paddle hit. `aiErrorPx` defaults to **45 px**.
- **Move:** move paddle center toward target at **aiSpeed = 520 px/s** (slightly slower
  than the 600 px/s player), with a 16 px hysteresis band so it doesn't jitter.
- Difficulty knobs (`aiSpeed`, `aiErrorPx`, dead-zone activation x) are in §12.

## 5. Entities / Game Objects

### 5.1 Paddle
- Properties: `TopY: float` (top edge), `Side: Side` (Left | Right), fixed width 18, height 110.
- Behavior: Left/Right human paddles driven by held keys; Right may be AI in 1P.
- Created at match start (two paddles); never destroyed during a match. Reset to
  center-y (`TopY = 305`) on each serve.

### 5.2 Ball
- Properties: `Pos: Vec2`, `Vel: Vec2`, size 16×16.
- State machine: `Frozen` (during 0.8 s serve telegraph) → `Live` (in play) → on
  scoring plane it is consumed and a new serve is set up.
- Created once per match; repositioned/relaunched each serve.

### 5.3 F# type sketch
```fsharp
open FS.GG.Game.Core
// Positions/velocities live in the scaffold's collision-safe Geometry.Vec2 ({ Vx; Vy }, from
// src/<ProductDir>/Vec2.fs) — NEVER a record you label X/Y/Width/Height, which collide with
// Scene's Point/Rect. This is a type ABBREVIATION: it adds no labels, so nothing can collide.
type Vec2 = Geometry.Vec2

type Side = Left | Right

type BallState = Frozen of timer:float | Live

type Paddle =
    { Side: Side
      TopY: float }         // top edge, px

type Ball =
    { Pos: Vec2             // center, px
      Vel: Vec2             // px/s
      State: BallState }
```

Do not re-declare `Vec2` with `X`/`Y`, and do not put `X`/`Y`/`Width`/`Height` labels on `Paddle`
(hence `TopY`): those labels collide with `Scene`'s `Point`/`Rect`, and the durable
`LayoutEvidence.fs` opens both `Scene` and your model — the clash surfaces there, in a file you must
not touch. The paddle's fixed 18×110 size is a render-time constant: express it with
`toRect paddleCenter 18.0 110.0` rather than `Width`/`Height` fields.

## 6. World / Levels / Progression
- **Playfield:** 1280 × 720 logical px (fixed; scaled to window preserving aspect ratio,
  letterboxed if needed).
- **No discrete levels.** Progression is intra-rally: the ball accelerates 5% per paddle
  hit (§4.6), so each rally gets faster and harder until a point ends. Speed resets to the
  420 px/s serve speed on the next serve.
- A match is **first to 11 points** (no win-by-2 in v1; see Stretch Goals). Difficulty
  effectively ramps within long rallies as the ball nears the 1100 px/s cap.

## 7. State Model (Elmish/MVU)

### 7.1 Model
```fsharp
type Mode = OnePlayer | TwoPlayer

type Screen =
    | Title of selected:Mode
    | Playing
    | Paused
    | GameOver of winner:Side

type Model =
    { Screen: Screen
      Mode: Mode
      Left: Paddle
      Right: Paddle
      Ball: Ball
      ScoreLeft: int
      ScoreRight: int
      PointPause: float        // seconds remaining in post-point pause; 0 = none
      ServeTo: Side            // side the next serve travels toward (the loser)
      AiError: float           // current per-rally AI aim error, px
      KeysDown: Set<Key>
      Rng: System.Random }
```

### 7.2 Msg
```fsharp
type Msg =
    | Tick of dt:float           // seconds since last frame (~0.0167)
    | KeyDown of Key
    | KeyUp of Key
    | StartMatch of Mode
    | TogglePause
    | Restart
    | ToTitle
```

### 7.3 update (key cases)
- `KeyDown`/`KeyUp`: add/remove key in `KeysDown`. On Title, `1`/`2` set selected mode,
  `Space` → `StartMatch`. On Playing/Paused, `P`/`Esc` → `TogglePause`.
- `StartMatch m`: reset scores to 0, center paddles, set `ServeTo` via 50/50 RNG, set
  `Ball.State = Frozen 0.8`, `Screen = Playing`.
- `Tick dt` (only when `Screen = Playing`):
  1. Apply held-key paddle velocity to Left (and Right if 2P); clamp to walls.
  2. If 1P, run AI step on Right paddle (§4.8).
  3. If `PointPause > 0`, decrement it; when it reaches 0, set up the serve
     (`Frozen 0.8`, ServeTo direction). Skip ball physics while paused.
  4. If `Ball.State = Frozen t`, decrement t; at ≤0 launch ball (§4.4) → `Live`.
  5. If `Live`: integrate position, resolve wall bounces (§4.5), resolve paddle
     collisions (§4.6).
  6. Check scoring planes (§4.7): on score, bump the right counter, set `PointPause = 1.0`,
     set `ServeTo` to the scored-on side, re-roll `AiError`. If a score reaches 11,
     `Screen = GameOver winner`.
- `TogglePause`: swap `Playing` ↔ `Paused` (Tick is a no-op while Paused).
- `Restart`: like `StartMatch` with current `Mode`. `ToTitle`: `Screen = Title Mode`.

### 7.4 view
`view` is pure — it maps `Model` to a Skia draw list (see §8). It performs no mutation and
no timing; it reads scores, paddle Ys, ball pos/state, and current `Screen` to decide what
to render (title overlay, HUD, pause dim, game-over panel).

### 7.5 Subscriptions
- **Tick:** a fixed 60 FPS timer subscription emitting `Tick dt` with `dt` in seconds
  (target 1/60 ≈ 0.0167; clamp dt to ≤ 0.05 to survive frame hitches — see §13).
- **Input:** keyboard key-down/key-up events mapped to `KeyDown`/`KeyUp`.

## 8. Rendering (Skia 2D)
- **Coordinate system:** logical 1280×720 canvas; a single scale+translate transform maps
  it to the window (uniform scale, letterbox bars where aspect differs).
- **Draw order (back to front):**
  1. Background fill: `#000000` (black).
  2. Center net: dashed vertical line at x=640, dash 18 px on / 14 px off, 4 px wide,
     color `#3C3C3C`.
  3. Top/bottom walls: filled rects, color `#FFFFFF` (optional; classic Pong omits them —
     keep thin or skip).
  4. Paddles: two filled rects, `#FFFFFF`, hard corners (no anti-alias rounding).
  5. Ball: 16×16 filled square, `#FFFFFF`.
  6. HUD scores (§9): big digits near top.
  7. Overlays (title / pause dim / game-over panel) drawn last.
- **Fonts:** a monospace/blocky font (e.g. bundled "PressStart2P"-style) for scores at
  72 px and HUD/menu text at 28 px, color `#FFFFFF`.
- **Camera:** none (static single screen).
- **Effects (optional, cheap):** 1-frame white flash on the scored-against goal edge;
  brief 80 ms paddle "squash" by scaling paddle width to 22 px on a hit. No particles
  required for v1.
- **Redraw strategy:** full-screen redraw every frame (scene is tiny; cost is negligible).
  No dirty-rect optimization needed.

## 9. UI / HUD / Screens

### 9.1 Title screen
- Centered title "PONG" at 96 px.
- Mode selector: "1 PLAYER" and "2 PLAYERS" stacked; the selected one highlighted
  (inverted: white box, black text). Keys `1`/`2` select, `Space` starts.
- Footer hint: "W/S — Left   ↑/↓ — Right (2P)   SPACE — Start" at 24 px.

### 9.2 Play HUD
- Left score at (x≈480, y=60), right score at (x≈800, y=60), 72 px digits, `#FFFFFF`.
- No other persistent HUD elements.

### 9.3 Pause overlay
- 50% black dim over the frozen scene; centered "PAUSED" at 72 px and
  "P — Resume   ESC — Title" at 24 px.

### 9.4 Game Over screen
- Centered panel: "PLAYER LEFT WINS" / "PLAYER RIGHT WINS" (or "YOU WIN" / "CPU WINS" in
  1P) at 64 px, final score "11 – 7" at 48 px below.
- Hint: "SPACE — Rematch   ESC — Title" at 24 px.

### 9.5 Menu system (detailed)
A single **menu stack** drives every non-play screen (Title, Settings, Stats, Pause, Game
Over). Each menu is a vertical list of rows with a cursor, so one small update handler
serves them all and navigation is identical everywhere.

**Menu tree**
```
Title ─┬─ Play ──────────── start match in the selected mode (1P/2P)
       ├─ Stats ─────────── Stats & Charts screen (§9.6)
       ├─ Settings ──────┬─ Difficulty     ◄ Easy · Normal · Hard ►
       │                 ├─ Master volume  ◄ 0 – 100 ►
       │                 ├─ Sound          ◄ On · Off ►
       │                 ├─ Window scale   ◄ 1× · 2× · Fit ►
       │                 ├─ CRT effect     ◄ On · Off ►
       │                 └─ Back
       └─ Quit

Pause ─┬─ Resume
       ├─ Restart
       ├─ Settings ──────── (same submenu; returns to Pause)
       └─ Quit to Title

Game Over ─┬─ Rematch
           ├─ View Stats ── Stats & Charts (§9.6)
           └─ Title
```

**Navigation model**
- `MenuCursor: int` on the active menu; `↑`/`W` decrement, `↓`/`S` increment, both **wrap**.
- `Enter`/`Space` activates the current row; `Esc`/`Backspace` pops the stack (**Back**).
- **Cycler/slider rows** (Difficulty, Master volume, Sound, Window scale, CRT): `←`/`→`
  change the value in place; the row shows a right-aligned `◄ value ►` widget.
- Rendering reuses the §9.1 selector style: the selected row is inverted (white box, black
  text); non-selected rows are `#FFFFFF` on black at 28 px.

**Msg additions** (extend §7.2):
```fsharp
    | MenuUp | MenuDown              // move cursor (wraps)
    | MenuAdjust of dir:int          // -1 / +1 on a cycler/slider row
    | MenuActivate                   // Enter/Space on the current row
    | MenuBack                       // Esc — pop the menu stack
    | OpenStats | CloseStats         // enter / leave the Stats screen (§9.6)
```

Settings apply live and persist to local config (§13): **Difficulty** selects the §12 AI
preset (Easy `420/90`, Normal `520/45`, Hard `640/15`); **Master volume**/**Sound** route to
`Audio.setMasterVolume` (§10, clamped `[0,1]`); **CRT** toggles the §8 optional post-effect.

### 9.6 Stats & charts screen
The Stats screen visualizes **the last match** and **lifetime** play. It reads a `Stats`
snapshot (never live physics), so it is a pure, deterministic render reachable from Title,
Game Over, and Pause. Chart-design choices below follow the project dataviz conventions
(form-first, validated colorblind-safe categorical palette, single axis, identity by entity).

**Tracked per match** — `MatchStats`, accumulated in `Tick`, snapshotted on `GameOver`:

| Field | Type | Updated |
|-------|------|---------|
| `rallyHits` | `int list` | one entry per completed rally (paddle-hit count) |
| `longestRally` | `int` | max over `rallyHits` |
| `topBallSpeed` | `float` (px/s) | max ball speed observed |
| `scoreTimeline` | `(pt:int * l:int * r:int) list` | appended on each point |
| `matchSeconds` | `float` | accumulated live-play time |
| `acesLeft` / `acesRight` | `int` | points won on an unreturned serve |

**Lifetime** — `LifetimeStats`, persisted (§13): `matchesPlayed`, `wins` (1P), `longestWinStreak`,
`longestRallyEver`, `fastestBall`.

**Layout** (logical 1280×720): a KPI tile row across the top, two charts below.

```
┌──────────────────────────── STATS ────────────────────────────┐
│  ┌ MATCHES ┐ ┌ WIN %  ┐ ┌ LONGEST  ┐ ┌ TOP SPEED ┐            │  ← KPI stat tiles
│  │   42    │ │  61 %  │ │ 23 hits  │ │ 1094 px/s │            │
│  └─────────┘ └────────┘ └──────────┘ └───────────┘            │
│                                                                │
│  Rally length distribution          Score progression         │
│  ▇▇                              11 ┤          ╭── L           │
│  ▇▇  ▇▇                             │      ╭─╯╭─╯              │
│  ▇▇  ▇▇  ▇▇  ▇▇                     │   ╭─╯ ╭─╯ R              │
│  1   3   5   7   9+  (hits)       0 ┼───────────────► point #  │
└────────────────────────────────────────────────────────────────┘
     ↑/↓ scope:  ▸ This Match · Lifetime           ESC — Back
```

**Charts** (rendered in Skia with the same draw-list discipline as §8):

1. **Rally-length histogram** — *form: a distribution → bars.* x = rally length bucketed by
   paddle-hit count (`1,2,3,…,9+`), y = number of rallies. **Single series**, so one hue and
   no legend. Bars are 4 px-rounded at the data end with a 2 px surface gap between them.
   Fill `#2a78d6` (light) / `#3987e5` (dark) — validated categorical slot 1.
2. **Score progression** — *form: change over an ordered index → line.* x = point number,
   y = cumulative score; **two series** (Left, Right) → a legend is present and both lines are
   direct-labeled at their right end ("L"/"R"). Left `#2a78d6`, Right `#1baf7a` (slots 1–2,
   adjacent-pair CVD-validated). 2 px lines, ≥ 8 px end markers, recessive 1 px gridlines
   in `#3C3C3C`.

Conventions honored: **color follows the entity** (Left is always slot 1, Right always slot 2
— never repainted by the scope toggle); **one axis only** (no dual-scale); chart **text uses
ink tokens** (`#FFFFFF` primary / `#C3C2B7` muted), never the series hue; layout is **fixed
and deterministic**, so a fixed-seed match (§13) renders byte-identical for snapshot tests.
The `↑/↓` **scope** toggle swaps the data source This-Match ↔ Lifetime without changing colors.

**Model/Msg hooks:** add `Stats: MatchStats` and `Lifetime: LifetimeStats` to §7.1 Model;
accumulate them in the `Tick` cases (append `rallyHits` and a `scoreTimeline` point on each
score, track `topBallSpeed`); on `GameOver`, fold `MatchStats` into `Lifetime` and persist
(§13). `OpenStats`/`CloseStats` switch a `Screen = Stats of scope:StatScope` state; the render
is a no-op on physics.

## 10. Audio
Audio ships in v1 via the **`fs-gg-audio`** capability (`open FS.GG.Audio.Core`).
Sound is **requested as pure values**: `update` returns `AudioEffect` values alongside the
model change and never touches an audio device. A record-only interpreter
(`Audio.interpret`) folds the frame's requests into `AudioEvidence` — the requested effects
in dispatch order, volumes clamped to `[0.0, 1.0]` — so cues are **deterministic and testable
with no sound hardware**. `SoundId`/`TrackId` are opaque names this game owns; the host
resolves them to real assets (a real playback backend is deferred, so tests assert on
`AudioEvidence.Requested`, not on audio output).

**Cues** — each is an `AudioEffect` requested from `update` when the paired transition fires:

| Event (Tick transition) | Request | Id | Design intent |
|---|---|---|---|
| Paddle hit (§4.6) | `Audio.playSfx` | `SoundId "paddle-hit"` | short blip (~120 Hz square, 60 ms) |
| Wall bounce (§4.5) | `Audio.playSfx` | `SoundId "wall-bounce"` | higher blip (~240 Hz, 50 ms) |
| Point scored (§4.7) | `Audio.playSfx` | `SoundId "score"` | descending two-tone (~200→100 Hz, 250 ms) |
| Serve launch (§4.4) | `Audio.playSfx` | `SoundId "serve"` | soft tick |
| Match win (`GameOver`) | `Audio.playSfx` | `SoundId "win"` | victory jingle (3 ascending notes) |

No background music (classic Pong is silent between events); a mute/settings toggle maps to
`Audio.setMasterVolume`, clamped at the boundary. **Testing:** collect the frame's
`AudioEffect`s, `Audio.interpret` them, and assert the `AudioEvidence.Requested` sequence for
representative events (e.g. a paddle hit requests exactly `PlaySfx (SoundId "paddle-hit", _)`).

## 11. Win / Loss / Scoring
- **Point:** the player whose goal line is NOT crossed scores 1 point when the ball exits
  past the opponent's goal line (§4.7).
- **Win condition:** first side to reach **11 points** wins the match → `GameOver winner`.
- **Loss condition:** the opponent reaches 11 first. In 1P, losing to the CPU is the loss
  state.
- **Scoring values:** exactly 1 point per rally won; no combos, no time bonus.
- **Lives/continues:** none — a match is a single first-to-11 race; Game Over offers
  rematch or title.

## 12. Difficulty & Balancing
Data-driven tunables (all defined as named constants / a config record):

| Name | Default | Range | Effect |
|------|---------|-------|--------|
| `playfieldW` × `playfieldH` | 1280 × 720 | fixed | Logical play area. |
| `paddleW` × `paddleH` | 18 × 110 px | 12–28 × 70–160 | Bigger paddle = easier defense. |
| `playerSpeed` | 600 px/s | 300–900 | Human paddle responsiveness. |
| `serveSpeed` | 420 px/s | 250–600 | Initial ball speed each serve. |
| `speedCap` | 1100 px/s | 600–1600 | Max ball speed (rally ceiling). |
| `hitSpeedUp` | 1.05 | 1.0–1.15 | Per-paddle-hit speed multiplier. |
| `maxBounceAngle` | 50° | 30–60 | Angle range from contact offset. |
| `serveAngleMax` | 35° | 15–45 | Serve launch spread. |
| `aiSpeed` | 520 px/s | 200–700 | AI paddle speed (vs. 600 player). |
| `aiErrorPx` | 45 px | 0–160 | AI aim error; higher = easier. |
| `aiActivateX` | 640 px | 500–900 | x past which AI starts tracking. |
| `winScore` | 11 | 5–21 | Points to win the match. |
| `serveFreeze` | 0.8 s | 0.3–1.5 | Telegraph delay before launch. |
| `pointPause` | 1.0 s | 0.5–2.0 | Delay after a point before serve. |

**Suggested AI difficulty presets:** Easy `aiSpeed 420 / aiErrorPx 90`; Normal
`520 / 45`; Hard `640 / 15`.

## 13. Technical Notes
- **Performance budget:** trivially within 60 FPS / 16.7 ms — fixed entity count (2
  paddles + 1 ball + static net). No allocation per frame beyond the draw list; reuse Skia
  paint objects.
- **Timestep:** logically **fixed-step physics** at 60 Hz. `Tick dt` carries dt in seconds;
  clamp `dt ≤ 0.05` to prevent tunneling on frame hitches. For robustness, an implementer
  may sub-step the ball when `|vel*dt|` exceeds half the ball size (8 px) to avoid passing
  through thin paddles at >1000 px/s — split the integration into N ≥ ceil(speed·dt/8)
  sub-steps and run collision each sub-step.
- **Determinism / RNG:** all randomness (serve direction, serve angle, AI error) draws from
  a single seeded `System.Random` stored in the model, so a match is reproducible given the
  seed and an input log. Tests should inject a fixed-seed RNG.
- **Persistence:** optional — store the single highest player win-streak (1P) in local
  config. Not required for v1.
- **Edge cases:** ball hitting the exact corner of a paddle (resolve via the §4.6 contact
  offset, clamped to ±1 → ±50°); both paddle-up and paddle-down keys held (net 0);
  simultaneous wall+paddle proximity (resolve wall bounce first, then paddle); ball at
  speed cap (clamp, do not exceed); window resize mid-rally (logical coords unaffected).

## 14. Acceptance Criteria (test scenarios)

1. **Serve telegraph & launch.** *Given* a fresh match has started, *when* the serve
   begins, *then* the ball sits frozen at (640, 360) for 0.8 s (±1 frame) and *then*
   launches at 420 px/s with `|θ| ∈ [8°, 35°]` toward `ServeTo`.

2. **Paddle movement & clamp.** *Given* the left paddle top edge is at Y=590, *when* `S`
   (down) is held for 1 s, *then* the paddle stops at Y=598 (clamped) and never overlaps
   the bottom wall.

3. **Held-key net zero.** *Given* both `W` and `S` are held, *when* a Tick is processed,
   *then* the left paddle's Y is unchanged.

4. **Wall bounce preserves speed.** *Given* the ball travels at 500 px/s with vy>0 toward
   the bottom wall, *when* its center-y reaches 700, *then* `vy` flips sign, speed stays
   500 px/s (±0.1), and the ball does not pass below y=700.

5. **Paddle deflection angle (center hit).** *Given* the ball strikes the left paddle at
   its exact center (offset u=0), *when* the collision resolves, *then* `vx` becomes
   positive and the bounce angle is ~0° (near-horizontal return).

6. **Paddle deflection angle (edge hit).** *Given* the ball strikes the left paddle near
   its top edge (u≈-1), *when* the collision resolves, *then* the bounce angle ≈ -50°
   (steeply upward) and `vx > 0`.

7. **Per-hit speed-up & cap.** *Given* a rally with the ball at 420 px/s, *when* it is
   deflected by a paddle, *then* its speed becomes 441 px/s (×1.05); *and* given speed is
   1080 px/s, *when* deflected, *then* speed is clamped to 1100 px/s (not 1134).

8. **No double-hit.** *Given* the ball has just bounced off the left paddle and is moving
   right (vx>0) but still overlaps the paddle for a frame, *when* Ticks process, *then* no
   second reflection occurs (collision ignored because ball moves away).

9. **Scoring left.** *Given* the ball center-x exceeds 1280, *when* the score is resolved,
   *then* `ScoreLeft` increments by 1, a 1.0 s `PointPause` begins, and the next serve
   travels toward Right (the scored-on side).

10. **Win condition.** *Given* `ScoreLeft = 10` in a first-to-11 match, *when* Left scores
    again, *then* `Screen` becomes `GameOver Left` and ball physics stop.

11. **AI is beatable but tracks.** *Given* 1P mode and the ball moving right (vx>0) past
    x=640, *when* Ticks process, *then* the AI right paddle moves toward `ballY ± aiError`
    at ≤520 px/s; *and* given the ball is on the left half moving left, *then* the AI
    paddle eases toward center-y (360).

12. **Pause freezes the world.** *Given* a live rally, *when* `P` is pressed, *then*
    `Screen = Paused`, subsequent Ticks do not move the ball or paddles, and pressing `P`
    again resumes from the exact prior state.

13. **Determinism.** *Given* two matches started with the same RNG seed and the same input
    log, *when* both are simulated, *then* ball positions and final score are identical.

14. **Tunneling guard.** *Given* the ball moves at 1100 px/s toward a paddle on one frame
    (displacement > 18 px), *when* the Tick integrates, *then* sub-stepping detects the
    collision and the ball does not pass through the paddle.

## 15. Stretch Goals
1. **Win-by-2 / deuce** scoring above 10–10.
2. **Mouse & gamepad** paddle control (analog speed).
3. **Difficulty select** on the title screen (Easy/Normal/Hard presets from §12).
4. **Predictive AI** (extrapolate ball trajectory through wall bounces instead of simple
   tracking).
5. **Particles & CRT post-effect** (scanlines, bloom) for retro flavor.
6. **Best-of-N matches** and a simple tournament bracket.
7. **Online two-player** via rollback netcode (deterministic core already supports it).
8. **Power-ups** (multi-ball, paddle grow/shrink) as an optional arcade variant mode.
