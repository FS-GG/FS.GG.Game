---
title: "Pong"
slug: pong
category: games
complexity: simple
genre: "Arcade / Table Tennis"
target_session_minutes: 5
stack: { rendering: "FS.GG.Rendering (Skia/OpenGL)", framework: "FS.GG.Game.Core (FixedStep for the tick; Rng for determinism)", arch: "Elmish/MVU", lang: "F#" }
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

**Tick phase order (one fixed step).** Within a single 1/60 s step the update resolves in a
fixed order so the frame is deterministic and input-to-action latency is bounded at **one tick
(≤16.7 ms)**: (1) drain held-key paddle velocity → clamp; (2) AI step (1P only); (3) decrement
`PointPause` or the `Frozen` timer; (4) integrate ball + sub-step collisions; (5) scoring-plane
test; (6) emit audio/stat effects for the transitions that fired. A message that arrives
mid-frame is applied at phase 1 of the next step, never between phases.

**Loop invariants.** Exactly one ball is ever `Live`; a point ends the instant the ball crosses a
goal plane and can never be un-scored; and no loop transition skips the serve telegraph — every
rally, including the first of the match and each re-serve, passes through the full 0.8 s `Frozen`
freeze before a ball goes `Live`. The three loops nest strictly: a session contains matches, a
match contains points, a point contains exactly one rally, so no timer or counter from an inner
loop leaks outward when it ends.

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
- **Clamp is post-integration:** apply the `[12, 598]` clamp *after* moving the paddle each fixed
  step, so a paddle held into a wall rests exactly on the bound — the 600 px/s × 1/60 = 10 px of
  travel a step owes is absorbed by the clamp and never overshoots.
- **Paddle is one-way:** a paddle never imparts velocity to the ball. A paddle driven *into* a
  ball pinned against a wall still only reflects it per §4.6 — there is no crush, carry, or
  push-through; the paddle occupies its clamped `TopY` and the ball resolves against the face on
  the tick it overlaps. Consequently paddle motion imparts no spin in v1 (angle comes solely from
  the contact offset, §4.6), which keeps the collision a pure function of positions.
- **Paddles never collide with each other:** inner faces sit at x=49 and x=1231, so the two
  110-tall rects share no x-span at any `TopY`; no paddle-paddle test is ever needed.

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
- **Speed reset:** every serve relaunches at exactly `serveSpeed` (420 px/s), discarding all rally
  speed-up (§4.6); the 1.05 accumulation never carries across a point, so each rally re-starts at
  the reaction-friendly floor.
- **Freeze holds under pause:** while `Screen = Paused` the `Frozen` timer does not decrement (Tick
  is a no-op, §7.3), so a serve telegraphed just before a pause resumes with its remaining freeze
  intact and can never launch during the pause.
- **Paddle reset:** both paddles snap to center (`TopY = 305`, §5.1) at the instant a serve is set
  up, so neither side begins a rally already committed to an edge and both aces and long rallies
  start from a symmetric pose.
- **First-serve draw:** the very first serve's 50/50 direction is drawn from `Model.Rng`
  (`Rng.nextBool`, §13) and the launch angle from `Rng.nextFloat`; both draws thread `rng'` back so
  the serve is part of the replayable input-independent state.

### 4.5 Wall collision (top/bottom)
- When ball center-y reaches `12 + 8 = 20` (top) or `708 - 8 = 700` (bottom), reflect:
  `vy = -vy`. Reposition ball just inside the wall to prevent sticking.
- Speed is unchanged by wall bounces.
- **Reposition math:** on a top bounce set center-y back to 20; on a bottom bounce set center-y to
  700 (the exact contact lines) *before* flipping `vy` — the ball is placed flush so the next step
  integrates away from the wall and the same bounce cannot re-trigger next frame.
- **Sign-guarded:** only reflect when the ball is moving *into* the wall (`vy < 0` at the top,
  `vy > 0` at the bottom); a ball already moving away is left untouched, mirroring the moving-toward
  guard that prevents paddle double-hits (§4.6).
- **Wall bounces don't reset the rally:** they preserve both speed and the accumulated 1.05 chain,
  so a ball can bounce off a wall many times within one rally while still speeding up only on
  paddle hits.

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
- **Speed is magnitude-preserving through the angle change:** take the pre-hit speed
  `s₀ = √(vx² + vy²)`, apply the ×1.05 speed-up and the cap to get `s`, then rebuild `(vx, vy)`
  from `s` and the new θ. The contact offset only *re-aims* the ball; it never adds or removes
  energy beyond the explicit 5% step, so the deflection is fully described by `(s₀, u)`.
- **Horizontal-progress invariant:** because θ is clamped to ±`maxBounceAngle` (±50°), the post-hit
  horizontal component is always `|vx| = s·cos θ ≥ s·cos 50° ≈ 0.643·s > 0`. The ball can never
  leave a paddle moving purely vertically, so a rally can never stall in a vertical lock between the
  walls — every live ball is always closing on one goal plane.
- **Edge-cap hits:** the 18×110 paddle has hard corners; *any* AABB overlap while the ball moves
  toward the paddle counts as a hit, and an extreme contact offset (`|u| > 1`, e.g. a graze on the
  top or bottom 18 px cap) is clamped to ±1 → the steepest legal ±50° return rather than clipping
  past the paddle.
- **Resolution order vs. walls:** if on one (sub-)step the ball is within both wall-bounce and
  paddle-overlap range, resolve the wall bounce first, then the paddle (§13), so a ball caught in a
  paddle-hugging-the-wall corner deflects predictably instead of double-reflecting.

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
- **AI shares the human bounds:** the AI paddle clamps to the same `[12, 598]` `TopY` range and
  drives its *center* toward the target. It therefore cannot cover a ball whose intercept y sits in
  the 55 px of paddle half-height that a wall clamp forbids — a serve laid tight to a corner can
  legitimately ace it (this is the intended beatability, not a bug).
- **Hysteresis tie-break:** when the target is within the 16 px hysteresis band the AI holds
  position (velocity 0) rather than dithering across it; it only commits to a direction once the
  target leaves the band. A fast, steeply-angled return that crosses before the AI re-commits is
  what occasionally beats it.
- **Idle recenter is clamped too:** the 240 px/s ease toward y=360 also respects `[12, 598]` and
  stops once center-y is within the hysteresis band of 360, so the paddle parks cleanly between
  rallies instead of oscillating around center.
- **Reaction budget:** the AI begins tracking only when `ballX > aiActivateX` (640) and `vx > 0`,
  so on a hard return it has at most the ball's right-half traversal to close the gap; at the
  1100 px/s cap that window (§6) shrinks toward the AI's own settling time, which is why Hard
  (640 px/s) still yields the occasional corner ace.

### 4.9 Rally escalation & degenerate cases
- **Hits to cap:** starting at 420 px/s, each paddle hit multiplies speed by 1.05, so the ball
  reaches the 1100 px/s cap after `ceil(ln(1100/420) / ln 1.05) = 20` paddle hits; every hit past
  the 20th stays clamped at 1100. A rally's speed therefore *plateaus* rather than diverging, and
  the escalation is fully deterministic from the hit count.
- **Sub-steps by speed band:** a 1/60 s step displaces `speed/60` px. Below ~480 px/s that is ≤8 px
  (a single integration); at the 1100 px/s cap it is 18.3 px → `ceil(18.3 / 8) = 3` sub-steps per
  step (§13). Collision runs each sub-step, so even a capped ball cannot tunnel the 18 px paddle.
- **No stall states:** the horizontal-progress invariant (§4.6) plus the serve flat-band exclusion
  (§4.4) guarantee every `Live` ball is always closing on one goal plane; there is no rally the
  physics cannot end, and no need for a rally-length safety timeout.
- **Single-scorer guarantee:** the ball occupies exactly one x per step, so at most one goal plane
  can be crossed on a given (sub-)step — simultaneous scoring is impossible and no point is ever
  double-counted, even at the cap where displacement exceeds a paddle width.

### 4.10 Mode differences (1P vs. 2P)
Both modes share identical ball, wall, serve, scoring, and first-to-11 rules; only right-paddle
control differs.

| Aspect | 1-Player | 2-Player |
|--------|----------|----------|
| Left paddle | Human, `W`/`S` | Human, `W`/`S` |
| Right paddle | AI (§4.8) | Human, `↑`/`↓` |
| `AiError` | re-rolled each hit, in use | unused (no AI step runs) |
| Serve direction | toward loser; first serve 50/50 RNG | identical |
| Game Over label | "YOU WIN" / "CPU WINS" (§9.4) | "PLAYER LEFT/RIGHT WINS" |

Physics, tuning constants, and determinism are byte-identical across modes given the same seed and
input log; the only branch in `Tick` is whether phase 2 (the AI step) runs. Held-key handling for
the right paddle in 2P mirrors the left exactly — same 600 px/s, same `[12, 598]` clamp, same
both-keys-held net-zero rule.

## 5. Entities / Game Objects

### 5.1 Paddle
- Properties: `TopY: float` (top edge), `Side: Side` (Left | Right), fixed width 18, height 110.
- Behavior: Left/Right human paddles driven by held keys; Right may be AI in 1P.
- Created at match start (two paddles); never destroyed during a match. Reset to
  center-y (`TopY = 305`) on each serve.
- **Derived quantities (render/collision boundary only):** center-y = `TopY + 55`; inner face x =
  49 (Left) / 1231 (Right); the 18×110 rect is rebuilt from `Side` + `TopY` at the scene boundary
  (§5.3), never stored on the model. A paddle's x and size are match-constant — only `TopY` ever
  changes.
- **Motion range:** `TopY ∈ [12, 598]`, a 586 px sweep; center-y spans `[67, 653]`, so a paddle
  can reach any ball intercept except the ~55 px hugging each wall past its half-height (the seam
  aces exploit, §4.8).

### 5.2 Ball
- Properties: `Pos: Vec2`, `Vel: Vec2`, size 16×16.
- State machine: `Frozen` (during 0.8 s serve telegraph) → `Live` (in play) → on
  scoring plane it is consumed and a new serve is set up.
- Created once per match; repositioned/relaunched each serve.
- **Invariants (while `Live`):** speed is clamped to `[serveSpeed, speedCap]` = `[420, 1100]` — the
  serve sets the floor (§4.4), the cap holds the ceiling (§4.6); the horizontal-progress invariant
  (§4.6) keeps `|vx| > 0` so the ball always advances toward a goal. `Frozen` pins `Pos = (640,
  360)` with zero effective velocity until the telegraph timer expires, then the stored serve
  velocity takes effect on the launch step.
- **Consumption:** crossing a goal plane (§4.7) consumes the ball; there is no free-flight state
  past the goal line — the score resolves and the ball becomes the next serve's `Frozen` instance,
  so at most one ball object exists at any time.

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
not touch. The paddle's fixed 18×110 size is a render-time constant, so derive its rect at the scene
boundary rather than storing `Width`/`Height` fields — the centre falls out of `Side` (x = 40 or
1240) and `TopY` (+55):

```fsharp
let paddleRect (p: Paddle) : Rect =
    let cx = match p.Side with Left -> 40.0 | Right -> 1240.0
    Geometry.toRect { Vx = cx; Vy = p.TopY + 55.0 } 18.0 110.0
```

`Geometry.toRect` is qualified because this sketch abbreviates `Geometry.Vec2` rather than opening
`Geometry`.

### 5.4 Static geometry (non-entity)
Three fixed elements are not `Model` entities but define collision and scoring; they are constants,
never stored or mutated per-frame:
- **Top/bottom walls** — collision planes at ball center-y 20 (top) and 700 (bottom) (§4.5);
  reflect `vy` when the ball moves into them, preserve speed.
- **Goal lines** — scoring planes tested with strict comparisons: center-x `< 0` → Right scores,
  center-x `> 1280` → Left scores (§4.7). A ball resting exactly on x=0 or x=1280 has *not* yet
  scored, so a paddle can still legally intercept it on that plane.
- **Center net** — a cosmetic dashed line at x=640 (§8); it has no collision and is never consulted
  by physics, so a ball passes through it freely.

## 6. World / Levels / Progression
- **Playfield:** 1280 × 720 logical px (fixed; scaled to window preserving aspect ratio,
  letterboxed if needed).
- **No discrete levels.** Progression is intra-rally: the ball accelerates 5% per paddle
  hit (§4.6), so each rally gets faster and harder until a point ends. Speed resets to the
  420 px/s serve speed on the next serve.
- A match is **first to 11 points** (no win-by-2 in v1; see Stretch Goals). Difficulty
  effectively ramps within long rallies as the ball nears the 1100 px/s cap.

**Intra-rally difficulty curve.** The only escalation in a level-less game is ball speed. As speed
rises, the defender's reaction window — the time the ball needs to cross the 1182 px between paddle
faces (49 → 1231) at its *steepest* legal (worst-case, minimum-horizontal) angle — compresses:

| Ball speed | Steepest legal angle | Min horizontal `s·cos θ` | Full-width crossing | Sub-steps/step |
|-----------|----------------------|--------------------------|---------------------|----------------|
| 420 (serve) | 35° (`serveAngleMax`) | ≈344 px/s | ≈3.4 s | 1 |
| 700 (mid-rally) | 50° (`maxBounceAngle`) | ≈450 px/s | ≈2.6 s | 2 |
| 1100 (cap) | 50° | ≈707 px/s | ≈1.7 s | 3 |

The window roughly halves from serve to cap (~3.4 s → ~1.7 s), and since the cap is reached by the
20th paddle hit (§4.9), a rally's peak difficulty is bounded — it never becomes unreadable.

**Match pacing.** A first-to-11 match ends after 11 points (an 11–0 shutout) to 21 points (an
11–10 finish). Budgeting ~1.0 s `pointPause` + 0.8 s serve freeze per point plus rallies of
typically 2–5 s, a full match lands near the 5-minute `target_session_minutes` (front-matter).
There are no discrete levels, checkpoints, or unlocks — the whole progression is the speed curve
above, reset to 420 px/s on every serve.

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
      Rng: Rng }                // FS.GG.Game.Core — a VALUE, so the Model stays one (§13)
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

### 9.5 Menu & configuration — the shared game shell

Pong uses the **generic FS.GG game shell** (FS-GG/FS.GG.Rendering#991) — the same menu/start
screen and settings every FS.GG game shares — rather than a bespoke per-game menu. The game
supplies only its **name**, its **key→command map** (the rebindable actions from §3 Controls),
and its play `update`/`view`; the shell provides everything below.

- **Main menu / start screen** — the game's name (**PONG**) as the title label, with
  **Start**, **Config**, and **Exit**. Mode selection (1P/2P, §9.1) sits alongside Start.
- **`Esc` from gameplay** opens the pause menu (Resume · Config · Exit to menu) over the same
  shell; `Esc` again resumes.
- **Config / Settings**, all applied live and persisted across restarts:
  - **Screen resolution** and **fullscreen** (windowed / borderless / fullscreen), driven
    through the SkiaViewer window-behavior + `LogicalCanvas` letterbox seam.
  - **Key rebinding** — the player remaps this game's controls (the §3 actions) via the
    `Controls.KeyRebind` UI over the `KeyboardInput.Keymap` mechanism; bindings persist via
    `KeymapCodec` (JSON), beside this game's other saved config (§13).
  - Game-specific rows are added as extra Config rows over the shell: **Difficulty** (the §12
    AI preset — Easy / Normal / Hard), **Master volume**/**Sound** (route to
    `Audio.setMasterVolume`, §10, clamped `[0,1]`), and **CRT effect** (toggles the §8 optional
    post-effect). The menu, Esc routing, display settings, and rebind screen come from the shell.

The shell is pointer- and keyboard-navigable over the interactive Controls host (the
`fs-gg-skiaviewer` "game → pointer host" recipe). It is a shared dependency, so Pong does
**not** re-specify menu-stack/cursor/settings machinery of its own. The **Stats & charts**
screen (§9.6) is a Pong-specific screen reached as a Config/menu row.

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
- **Ace:** a point won when the served ball crosses the opponent's goal with **zero paddle hits
  that rally** (`rallyHits` entry = 0, §9.6), tallied as `acesLeft`/`acesRight`. An ace is the
  fastest possible point — still at 420 px/s, since no hit ever sped it up — and rewards a serve
  angled where the receiver (or AI, §4.8) cannot reach.
- **No deuce in v1:** the match ends the instant a side reaches 11, including an 11–10 finish;
  there is no win-by-2 or sudden death (deferred, §15.1). A draw is impossible — every point moves
  exactly one counter and the race is strictly monotonic, so the two scores can never both be 11.
- **Boundary attribution:** scoring uses the strict `< 0` / `> 1280` comparisons (§4.7, §5.4); a
  ball resting exactly on a goal line has not scored, so a paddle may still legally intercept it on
  that plane before it crosses.
- **Score ceiling:** the HUD shows 0–11 and a counter never exceeds `winScore`, because the win
  test fires on the same step the 11th point lands and immediately freezes physics (§7.3) — no
  further points can be scored after Game Over.
- **Symmetry of the race:** because serve goes to the loser (§4.4) and geometry mirrors about
  x=640 (§12), neither side gets a structural scoring advantage; in 1P the only asymmetry is the
  AI's speed/error preset (§12), not the scoring rules.

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

**Preset feel.** The three presets trade reaction speed against aim precision, both relative to the
600 px/s player:

| Preset | `aiSpeed` | `aiErrorPx` | Feel |
|--------|-----------|-------------|------|
| Easy | 420 px/s | 90 px | slower than the player and wide aim scatter — loses most steep returns |
| Normal | 520 px/s | 45 px | just under player speed; the occasional corner-tight return aces it |
| Hard | 640 px/s | 15 px | faster than the player; only steep, fast, corner-tight returns beat it |

Speed sets *whether* the AI arrives; error sets *where* it aims once there. `aiSpeed < playerSpeed`
(Easy/Normal) means a human wins a straight scramble; `aiSpeed > playerSpeed` (Hard) inverts that,
so the human must instead exploit the corner-seam aces (§4.8) the aim error alone cannot close.

**Balancing invariants (must hold for any tuning within range):** `serveSpeed < speedCap` (a rally
has room to escalate); `hitSpeedUp ≥ 1.0` (the ball never slows); `maxBounceAngle < 90°` (so
`cos θ > 0` and the horizontal-progress invariant §4.6 holds — a rally cannot stall); and the serve
flat-band exclusion `|θ| ≥ 8°` (§4.4) stands regardless of `serveAngleMax`. A config that violates
these can produce an unendable rally and is outside the supported range even where a single knob's
row above would otherwise allow the value.

**Side symmetry:** the playfield, paddle geometry, and serve-to-loser rule are mirror-symmetric
about x=640, so 2P has no built-in side advantage and difficulty within a match ramps *only* via
the intra-rally speed curve (§6). In 1P the sole asymmetry is that the right paddle runs the AI step
(§4.10) under the selected preset.

## 13. Technical Notes
- **Performance budget:** trivially within 60 FPS / 16.7 ms — fixed entity count (2
  paddles + 1 ball + static net). No allocation per frame beyond the draw list; reuse Skia
  paint objects.
- **Timestep:** logically **fixed-step physics** at 60 Hz — drained by `FixedStep.drain`, not by a
  hand-rolled accumulator. `FixedStep.drain (1.0/60.0) frameTime acc` returns `struct (steps, acc')`:
  how many whole steps this frame owes, and the remainder to bank. It caps the frame internally
  (`FixedStep.defaultMaxFrameTime`, 0.25 s), so a debugger pause cannot spiral the sim — which is what
  the old hand-written `dt ≤ 0.05` clamp was reaching for. Run the §4 update `steps` times with a
  constant `dt = 1/60`. For robustness, an implementer may sub-step the ball *within* a step when
  `|vel*dt|` exceeds half the ball size (8 px) to avoid passing through thin paddles at >1000 px/s —
  split the integration into N ≥ ceil(speed·dt/8) sub-steps and run collision each sub-step.
- **Determinism / RNG:** all randomness (serve direction, serve angle, AI error) draws from
  `Model.Rng` — `FS.GG.Game.Core`'s **`Rng`** (splitmix64), seeded with `Rng.ofSeed`; the 50/50 serve
  is `Rng.nextBool` and the launch angle `Rng.nextFloat`. It is a **value**, not a `System.Random`:
  every draw returns `struct (x, rng')` and you write `rng'` back to the `Model`, so the `Model` stays
  a value you can snapshot, replay and compare. A `System.Random` in the `Model` is a mutable object
  *shared* by every copy of it, which silently breaks the reproducibility this bullet promises. A match
  is reproducible given the seed and an input log.
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

15. **Horizontal-progress invariant.** *Given* any paddle deflection at pre-hit speed `s`, *when*
    the collision resolves, *then* `|vx| ≥ s·cos 50° > 0` and the ball never leaves the paddle
    moving purely vertically — no rally can stall in a vertical lock between the walls.

16. **Serve resets rally speed.** *Given* a rally has accelerated the ball to 700 px/s, *when* a
    point is scored and the next serve launches, *then* the ball launches at exactly 420 px/s — the
    5%-per-hit accumulation does not carry across the point.

17. **Freeze holds under pause.** *Given* a serve is `Frozen` with t seconds of telegraph
    remaining, *when* the game is paused for any duration and then resumed, *then* the freeze timer
    resumes from t and the ball never launches while `Screen = Paused`.

18. **Ace accounting.** *Given* a served ball crosses the opponent's goal line with zero paddle
    hits that rally, *when* the point resolves, *then* the scorer's ace counter (`acesLeft`/
    `acesRight`, §9.6) increments and that rally records `rallyHits = 0`.

19. **Rally reaches the cap deterministically.** *Given* `serveSpeed = 420` and `hitSpeedUp =
    1.05`, *when* a rally sustains 20 paddle hits, *then* ball speed is clamped to 1100 px/s by the
    20th hit (never exceeding it) and integration uses 3 sub-steps per step at that speed (§13).

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

## 16. Milestone Roadmap

Implementation is sequenced into milestones; each item is a colored checkbox
tracking its status. Items reference the section that specifies them.

**Legend:** 🟥 Not started · 🟨 In progress · 🟩 Done · ⬜ Deferred (post-v1)

_All items start 🟥 (spec status). Flip an item to 🟨 when work begins and 🟩 once
its acceptance test(s) pass (§14)._

### M0 — Scaffold & fixed-step loop
- 🟥 Project scaffold: `Model`/`Msg`/`update`/`view` skeleton (§7)
- 🟥 Fixed 60 Hz tick via `FixedStep.drain`, banked remainder (§7.5, §13)
- 🟥 `Rng` value seeded with `Rng.ofSeed`, threaded through `Model` (§13)
- 🟥 Logical 1280×720 coordinate transform + letterbox scaling (§4.1, §8)

### M1 — Paddles & input
- 🟥 Held-key `KeysDown` set + edge-triggered menu keys (§3)
- 🟥 Velocity-based paddle movement, wall clamp `[12, 598]` (§4.2)
- 🟥 Both-keys-held → net-zero movement (§4.2) — AC #3
- 🟥 One-way paddle (no crush/spin) + post-integration wall clamp (§4.2)

### M2 — Ball & serve physics
- 🟥 Constant-velocity ball integration (§4.3)
- 🟥 Serve telegraph (0.8 s freeze) + angle launch, flat-band exclusion (§4.4) — AC #1
- 🟥 Top/bottom wall bounce, speed preserved (§4.5) — AC #4
- 🟥 Serve resets rally speed to 420 px/s each point (§4.4) — AC #16
- 🟥 Serve freeze holds under pause, no launch while paused (§4.4, §2) — AC #17

### M3 — Collisions & scoring
- 🟥 Paddle AABB + contact-offset angle control (§4.6) — AC #5, #6
- 🟥 Per-hit 5% speed-up, cap at 1100 px/s (§4.6) — AC #7
- 🟥 No-double-hit guard (moving-away test) (§4.6) — AC #8
- 🟥 Scoring planes, point pause, serve-to-loser (§4.7) — AC #9
- 🟥 Sub-step tunneling guard at high speed (§13) — AC #14
- 🟥 Horizontal-progress invariant, no vertical-lock stall (§4.6, §4.9) — AC #15
- 🟥 Per-hit speed-up reaches cap by 20th hit, then plateaus (§4.9) — AC #19
- 🟥 Ace accounting on unreturned serve (`rallyHits = 0`) (§11, §9.6) — AC #18
- 🟥 Strict goal-plane comparison + single-scorer guarantee (§4.9, §5.4)

### M4 — AI opponent (1P)
- 🟥 Tracking with dead zone, per-rally aim error, idle recenter (§4.8) — AC #11
- 🟥 AI wall-clamp `[12, 598]` + hysteresis tie-break + clamped recenter (§4.8)

### M5 — Match flow & screens
- 🟥 Title / Playing / Paused / GameOver screen states (§7.1, §9)
- 🟥 First-to-11 win → `GameOver winner`, physics stop (§11) — AC #10
- 🟥 Pause freezes world, resumes exact state (§7.3) — AC #12
- 🟥 1P/2P mode branch: AI step runs only in 1P, else mirrored human control (§4.10)

### M6 — Rendering (Skia)
- 🟥 Draw order: net, walls, paddles, ball, HUD scores (§8, §9.2)
- 🟥 Optional goal-flash + paddle-squash effects (§8)
- 🟥 Static geometry: wall/goal planes + cosmetic non-collide net (§5.4)

### M7 — Menus & settings
- 🟥 Adopt the generic FS.GG game shell (FS-GG/FS.GG.Rendering#991): main menu (title + Start/Config/Exit), Esc pause routing, Settings with screen resolution + fullscreen, and in-game key rebinding of the §3 controls, persisted — the game provides its name + key→command map + play update/view; the shell provides the rest, no bespoke menu system (§9.5)
- 🟥 Game-specific Config rows over the shell (difficulty preset, volume/sound, CRT effect) apply live + persist (§9.5, §12)

### M8 — Stats & charts
- 🟥 `MatchStats`/`LifetimeStats` accumulation + persist (§9.6)
- 🟥 Rally-length histogram + score-progression line chart (§9.6)

### M9 — Audio
- 🟥 `AudioEffect` cues, `Audio.interpret`, volume clamp `[0,1]` (§10)

### M10 — Acceptance & determinism
- 🟥 All 14 acceptance scenarios green (§14)
- 🟥 Seed + input-log replay is byte-identical (§13) — AC #13

### Stretch — deferred (post-v1)
- ⬜ Win-by-2 / deuce scoring (§15.1)
- ⬜ Mouse & gamepad analog control (§15.2)
- ⬜ Title-screen difficulty select (§15.3)
- ⬜ Predictive AI through wall bounces (§15.4)
- ⬜ Particles & CRT post-effect (§15.5)
- ⬜ Best-of-N matches / tournament bracket (§15.6)
- ⬜ Online rollback netcode (§15.7)
- ⬜ Power-ups arcade variant (§15.8)
