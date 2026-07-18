---
title: "Flappy Bird"
slug: flappy-bird
category: games
complexity: simple
genre: "Endless side-scrolling arcade"
target_session_minutes: 3
stack: { rendering: "FS.GG.Rendering (Skia/OpenGL)", framework: "FS.GG.Game.Core (FixedStep for the tick; Rng for gap placement)", arch: "Elmish/MVU", lang: "F#" }
status: spec
---

# Flappy Bird

## 1. Overview
You are a tubby bird that cannot stop falling. One button — a flap — gives you a single
upward beat of the wings, and gravity immediately starts dragging you back down. The
world scrolls left at a constant clip, hurling an endless procession of green pipes at
you, each with a narrow gap punched somewhere in it. The fantasy is pure twitch mastery:
threading a too-small gap, again and again, on nothing but timing and nerve. It is fun
because the control is brutally simple, the failure is instant and unambiguous, and the
score is a public dare — "one more try" is the whole game.

## 2. Core Game Loop
**Moment-to-moment:** fall → judge the next gap → flap to gain altitude → glide down →
pass the pipe (score) → judge the next gap → repeat. The player is constantly trading
altitude against the approaching gap, tapping just enough to stay alive.

**Session-level:** title/ready screen → tap to start → play (flap through pipes,
accumulate score) → collide (with pipe or ground) → instant death → game-over screen
showing score + best → tap to restart from the ready screen. A full session is dozens of
2-to-60-second runs.

## 3. Controls & Input
The game is single-button. The same action ("Flap") starts a run, flaps during play, and
(after a short lockout) restarts from game-over.

| Input | Device | Action | Model |
|-------|--------|--------|-------|
| `Space` | Keyboard | Flap / Start / Restart | Edge-triggered (key-down only; holding does NOT auto-repeat) |
| `↑` (Up arrow) | Keyboard | Flap (alias) | Edge-triggered |
| Left mouse click | Mouse | Flap / Start / Restart | Edge-triggered (button-down) |
| `Esc` | Keyboard | Pause / resume | Edge-triggered |

Input model notes:
- **Flap is edge-triggered.** Each physical key-down or click yields exactly one flap
  impulse. Auto-repeat from a held key MUST be ignored (track key-held state; only the
  press transition counts).
- A flap is accepted at any time during `Playing`, including when the bird is already
  rising (it overrides current vertical velocity — see 4.2).
- On the game-over screen, input is ignored for `restartLockoutMs` (default 600 ms) to
  prevent an accidental instant restart from the killing tap.

## 4. Mechanics (detailed)

All physics use a **logical pixel coordinate system** with **+Y pointing DOWN** (screen
convention). Positions/velocities are in logical px and px/s. The simulation runs on a
**fixed 60 Hz timestep** (`dt = 1/60 s ≈ 0.01667 s`); see §13.

### 4.1 Gravity & Falling
- Constant downward acceleration: **`gravity = 2400 px/s²`**.
- Applied every tick to the bird's vertical velocity: `vy ← vy + gravity * dt`.
- **Terminal velocity (downward):** `vyMax = +900 px/s`. After applying gravity, clamp
  `vy = min(vy, vyMax)`.

### 4.2 Flap Impulse
- A flap **sets** (does not add to) vertical velocity to a fixed upward value:
  **`vy ← flapImpulse = -620 px/s`** (negative = up).
- Because it overrides `vy`, rapid flaps do not stack into ever-higher jumps; each flap is
  one identical beat.
- There is no upward velocity cap beyond the impulse itself (the bird can never exceed
  `|flapImpulse|` upward, since flap sets and gravity only reduces upward speed).
- Optional ceiling rule: the bird may rise above the top of the screen (`y < 0`) but is
  **clamped** so its top never goes above `y = -birdHeight` — it cannot fly off-screen
  upward to dodge pipes. Hitting the ceiling clamps `y` and sets `vy = max(vy, 0)`.

### 4.3 Horizontal Scroll
- The bird's X position is **fixed** at `birdX = 320` (logical px from the left). The bird
  never moves horizontally.
- The world scrolls toward the bird: all pipes move left at **`scrollSpeed = 180 px/s`**
  (`pipe.x ← pipe.x - scrollSpeed * dt`). This is the effective forward speed of the bird.
- `scrollSpeed` is constant in v1 (no speed ramp — see §12 for the optional ramp tunable).

### 4.4 Pipe Spawning
- Pipes spawn as **pairs** (a top pipe and a bottom pipe sharing one gap).
- **Horizontal spacing:** a new pipe pair is created so that consecutive gaps are
  **`pipeSpacing = 360 px`** apart (measured center-to-center on X). Equivalently, spawn a
  new pair when the most-recent pair has moved `pipeSpacing` px from the spawn line.
- **Spawn line:** new pairs are created with `x = playfieldWidth + pipeWidth` (just off the
  right edge), i.e. `x = 1280 + 80 = 1360`.
- **Despawn:** a pair is removed once fully off the left edge (`x + pipeWidth < 0`).
- **Pipe width:** `pipeWidth = 80 px`.

### 4.5 Gap Size & Vertical Randomization
- **Gap height (vertical opening):** `gapHeight = 200 px` in v1 (tunable; see §12).
- The gap's vertical center `gapCenterY` is randomized per pair within a safe band so the
  gap never clips the ground or the top:
  - `gapMargin = 80 px` (minimum distance from gap edge to screen top and to the ground).
  - Let `groundY = 640` (top of the ground strip; see 4.7).
  - `gapCenterY ∈ [gapMargin + gapHeight/2, groundY - gapMargin - gapHeight/2]`
    = `[80 + 100, 640 - 80 - 100]` = **`[180, 460]`**.
  - Draw `gapCenterY = rng.NextFloat(180, 460)` (inclusive range).
- Derived geometry for a pair at horizontal position `x`:
  - **Top pipe:** rect `{ x; y = 0; w = 80; h = gapCenterY - gapHeight/2 }`.
  - **Bottom pipe:** rect `{ x; y = gapCenterY + gapHeight/2; w = 80; h = groundY - (gapCenterY + gapHeight/2) }`.

### 4.6 Scoring on Pass
- Each pipe pair has a `scored: bool` flag, initially `false`.
- When the **pipe pair's right edge** passes the **bird's X** (`pipe.x + pipeWidth < birdX`)
  and `scored = false`, increment `score` by **1** and set `scored = true`.
- Score is awarded for surviving the gap, not for touching it; passing the X line without a
  collision is sufficient.

### 4.7 Collision & Ground
- **Bird collision box (AABB):** centered on the bird, `birdWidth = 34 px` ×
  `birdHeight = 24 px`. To make near-misses feel fair, the collision box is **inset** by
  `collisionInset = 2 px` on every side relative to the drawn sprite (effective box
  ~30×20). All collision uses this inset AABB.
- **Pipe collision:** AABB-vs-AABB test of the bird box against each pipe rect (top and
  bottom) of every on-screen pair. Any overlap = collision.
- **Ground:** a solid ground strip occupies `y ∈ [groundY, playfieldHeight]` =
  `[640, 720]`. If `bird.box.bottom ≥ groundY`, that is a ground collision.
- **Ceiling:** not lethal — the bird is clamped (see 4.2).
- **Instant-death model:** ANY collision (pipe or ground) ends the run immediately. There
  is no health, no invulnerability frames, no knockback. On death: freeze scrolling,
  transition to `GameOver`, the bird falls under gravity to rest on the ground (cosmetic),
  and the death SFX/score panel show.

## 5. Entities / Game Objects

### Bird (exactly one)
- **Size (sprite):** 34×24 px. **Collision box:** inset AABB (~30×20), see 4.7.
- **Properties:** `x = 320` (fixed), `y` (float), `vy` (float), `rotationDeg` (cosmetic).
- **Behavior:** integrates gravity each tick; flap sets `vy`. Rotation is derived from
  velocity for flavor: `rotationDeg = clamp(vy * 0.06, -25, +90)` (nose-up when rising,
  nose-down when diving).
- **Created:** once, at run start, at `y = playfieldHeight/2 = 360`, `vy = 0`.
- **Destroyed:** never pooled away; reset on restart.

### PipePair (0..N, typically 4–5 on screen)
- **Properties:** `x` (float, left edge), `gapCenterY` (float), `scored` (bool).
- **Size:** width 80; top/bottom rects derived from `gapCenterY` and `gapHeight` (see 4.5).
- **Behavior:** moves left at `scrollSpeed`; flags `scored` when passed.
- **Created:** by the spawner every `pipeSpacing` px of scroll, off the right edge.
- **Destroyed:** when `x + pipeWidth < 0`.

### Ground (visual + collision)
- A static strip `y ∈ [640, 720]`. Texture/pattern scrolls left at `scrollSpeed` for
  parallax but its collision top stays at `groundY = 640`.

F# type sketch:
```fsharp
type Bird =
    { CenterY: float
      Vy: float
      RotationDeg: float }   // horizontal position is the constant birdX = 320.0

type PipePair =
    { LeftX: float           // left edge, moves left over time
      GapCenterY: float
      Scored: bool }
```

Both entities are constrained to one axis, so they carry scalars rather than a `Vec2` — but the
labels still may **not** be `X`/`Y`/`Width`/`Height`. Those collide with `Scene`'s `Point`/`Rect`,
and because the durable `LayoutEvidence.fs` opens both `Scene` and your model, a bare `{ X = …; Y = … }`
literal there mis-resolves to *your* record. Anything genuinely 2-D (were pipes to move in both axes)
belongs in the scaffold's collision-safe `Geometry.Vec2` (`Vx`/`Vy`). Derive bounds at the scene
boundary instead of storing `Width`/`Height` fields — build the `Vec2` from the scalars you do hold:
`Geometry.toRect { Vx = birdX; Vy = bird.CenterY } 34.0 24.0`.

## 6. World / Levels / Progression
- **Playfield:** `1280 × 720` logical px. Origin top-left, +Y down. The view scales this
  logical canvas to the physical window (letterboxed) so physics stays resolution-independent.
- **No discrete levels.** The game is a single endless run; "progression" is the rising
  score and the player's own improving skill.
- **Difficulty ramp (v1 = flat):** all constants (`scrollSpeed`, `gapHeight`, `pipeSpacing`)
  are constant for the whole run. The optional ramp (see §12) tightens the gap and/or
  speeds scroll as score climbs; it is OFF by default to match the classic feel.
- **What changes over time:** only the randomized `gapCenterY` per pair, the parallax
  scroll of background/ground, and the score.

## 7. State Model (Elmish/MVU)

### Model
```fsharp
type Phase =
    | Ready                  // waiting for first flap
    | Playing
    | GameOver of finalScore: int

type Model =
    { Phase: Phase
      Bird: Bird
      Pipes: PipePair list
      Score: int
      Best: int                       // persisted high score
      DistanceSinceSpawn: float       // px scrolled since last spawn, drives spawner
      Rng: Rng                        // FS.GG.Game.Core, seeded — gap placement (§13)
      GameOverElapsedMs: float        // for the restart lockout
      Paused: bool }
```

### Msg
```fsharp
type Msg =
    | Flap                  // edge-triggered: start / flap / restart (post-lockout)
    | TogglePause
    | Tick of dt: float     // seconds, fixed ~0.01667
```

### update — key cases
- **`Flap` while `Ready`:** transition to `Playing`, apply one flap impulse
  (`vy = flapImpulse`), reset score, clear pipes, reseed/keep RNG.
- **`Flap` while `Playing`:** set `bird.vy = flapImpulse` (override). Play flap SFX.
- **`Flap` while `GameOver`:** if `GameOverElapsedMs ≥ restartLockoutMs`, reset to a fresh
  `Ready` model (preserving `Best` and `Rng`); otherwise ignore.
- **`TogglePause`:** flip `Paused`; while paused, `Tick` is a no-op for physics.
- **`Tick dt` while `Playing` and not paused (in order):**
  1. Integrate bird: `vy = min(vy + gravity*dt, vyMax)`; `y += vy*dt`; clamp ceiling.
  2. Scroll pipes: `x -= scrollSpeed*dt`; advance `DistanceSinceSpawn += scrollSpeed*dt`.
  3. Spawn: while `DistanceSinceSpawn ≥ pipeSpacing`, emit a new pair (random `gapCenterY`
     from `Rng`), subtract `pipeSpacing`.
  4. Despawn pairs with `x + pipeWidth < 0`.
  5. Scoring: for each unpassed pair with `x + pipeWidth < birdX`, `score += 1; scored = true`.
  6. Collision: if bird AABB overlaps any pipe rect, or `bird.box.bottom ≥ groundY`,
     transition to `GameOver finalScore=score` and update `Best`.
- **`Tick dt` while `GameOver`:** advance `GameOverElapsedMs`; let bird keep falling to
  ground (cosmetic), do not scroll pipes.
- **`Tick dt` while `Ready`:** bird bobs gently (cosmetic sine hover), no gravity death.

### view
Pure function `Model -> Scene`. It reads phase + entities and emits draw commands
(background, pipes, ground, bird, HUD, overlays). It performs **no mutation** and no
physics; Skia draws the returned scene. Phase selects which overlay (ready prompt vs.
game-over panel) is added.

### Subscriptions
- **Tick:** a 60 FPS timer subscription dispatching `Tick (1.0/60.0)`. Implementation uses
  a fixed-timestep accumulator (see §13) so logic is frame-rate independent.
- **Input:** keyboard key-down (`Space`, `↑`, `Esc`) and mouse button-down events mapped to
  `Flap` / `TogglePause`, with key-held de-bounce so only press edges dispatch.

## 8. Rendering (Skia 2D)
Coordinate system matches physics (top-left origin, +Y down), logical 1280×720 scaled to
the window. Redraw the full canvas each frame (cheap at this entity count); no dirty-rect
optimization needed.

**Draw order (back to front):**
1. **Sky background** — solid fill `#4EC0CA` (classic cyan). Optional parallax cloud layer
   scrolling left at `0.3 * scrollSpeed`.
2. **Pipes** — for each pair, draw top + bottom rects. Body fill `#73BF2E`, 2 px darker
   outline `#558022`, and a "lip" cap rect (`88 px` wide, `26 px` tall, centered on
   `pipeWidth`) at each gap-facing end for the classic mushroom look.
3. **Ground strip** — `y ∈ [640, 720]`, fill `#DED895` with a `#73BF2E` top edge band
   (8 px). Texture offset scrolls left at `scrollSpeed`, wrapping every 48 px.
4. **Bird** — 34×24 sprite/rounded-rect, body `#F7D51D`, drawn rotated by `bird.rotationDeg`
   about its center. Simple 2-frame wing flap toggling on each `Flap`.
5. **HUD** — score text, top-center (see §9).
6. **Overlays** — ready prompt or game-over panel (see §9), drawn last.

**Fonts:** a single bold sans (e.g. bundled "Press Start 2P"-style or system bold). Score
uses a large outlined style: white fill `#FFFFFF` with a 3 px black `#000000` outline.

**Visual effects (optional, cheap):** a brief white flash (1 frame, `#FFFFFF` at 50% alpha
fading over 150 ms) on collision; small feather/dust particles on flap (≤ 6 particles).

## 9. UI / HUD / Screens

**Screens:**
- **Ready:** title "Flappy Bird" centered-upper; the bird bobbing at center; prompt
  "Tap / Space to flap" with a tap icon. Best score shown small, top-right.
- **Playing:** only the in-world HUD (live score).
- **Paused:** dim the playfield to 40% and show "PAUSED — Esc to resume" centered.
- **Game Over:** a centered panel (`~420×260 px`) with header "Game Over", the run score,
  the medal (optional), the best score, and "Tap to play again" (greyed until lockout ends).

**HUD elements:**
- **Live score:** top-center at `(640, 80)`, large outlined digits, integer, updates on pass.
- **Best score:** small, top-right `(1180, 24)`, format `BEST 000` (right-aligned).
- All text horizontally centered on its anchor unless noted.

### 9.1 Menu system (detailed)
A single **menu stack** drives every non-play surface (the Ready/title screen, Settings,
Stats, Pause, and the run-end panel). Each menu is a vertical list of rows with a cursor, so
one small update handler serves them all and navigation is identical everywhere. Because the
game is one-button in play (§3), the menus add cursor keys that are only live while a menu is
open — they never interfere with the edge-triggered `Flap`.

**Menu tree**
```
Ready ─┬─ Play ──────────── begin a run (first flap launches the bird)
       ├─ Stats ─────────── Stats & Charts screen (§9.2)
       ├─ Settings ──────┬─ Difficulty     ◄ Easy · Normal · Hard ►
       │                 ├─ Master volume  ◄ 0 – 100 ►
       │                 ├─ Sound          ◄ On · Off ►
       │                 ├─ Window scale   ◄ 1× · 2× · Fit ►
       │                 ├─ Screen shake   ◄ On · Off ►
       │                 └─ Back
       └─ Quit

Pause ─┬─ Resume
       ├─ Retry ─────────── abandon this run, return to Ready
       ├─ Settings ──────── (same submenu; returns to Pause)
       └─ Quit to Title

Game Over ─┬─ Retry ─────── start a fresh run (endless — no lives, no continues)
           ├─ View Stats ── Stats & Charts (§9.2)
           └─ Title
```

**Navigation model**
- `MenuCursor: int` on the active menu; `↑` decrements, `↓` increments, and both **wrap**.
- `Enter`/`Space` activates the current row; `Esc`/`Back` pops the stack (**Back**).
- **Cycler/slider rows** (Difficulty, Master volume, Sound, Window scale, Screen shake):
  `←`/`→` change the value in place; the row shows a right-aligned `◄ value ►` widget.
- The run-end panel uses **Retry framing** (§11): there are no lives, so the primary row is
  **Retry**, which spins up a brand-new run rather than resuming; it stays greyed until the
  `restartLockoutMs` window (§12) elapses, matching the anti-misclick lockout in §3.
- Rendering reuses the §9 overlay style: the selected row is highlighted (inverted panel,
  dark text); non-selected rows are `#FFFFFF` on the dimmed playfield.

**Msg additions** (extend §7 `Msg`):
```fsharp
    | MenuUp | MenuDown              // move cursor (wraps)
    | MenuAdjust of dir:int          // -1 / +1 on a cycler/slider row
    | MenuActivate                   // Enter/Space on the current row
    | MenuBack                       // Esc — pop the menu stack
    | OpenStats | CloseStats         // enter / leave the Stats screen (§9.2)
```

Settings apply live and persist to local config (§13): **Difficulty** selects the §12
tunable preset (Easy `gapHeight 240 / scrollSpeed 150`, Normal `200 / 180`, Hard `170 / 210`
with the optional `*Ramp` enabled); **Master volume**/**Sound** route to
`Audio.setMasterVolume` (§10, clamped `[0,1]`, `0` = silence); **Screen shake** toggles the
§8 optional collision-flash/shake effect.

### 9.2 Stats & charts screen
The Stats screen visualizes **the last run** and **lifetime** play. It reads a stats snapshot
(never live physics), so it is a pure, deterministic render reachable from Ready, the run-end
panel, and Pause. Chart-design choices below follow the project dataviz conventions
(form-first, validated colorblind-safe categorical palette, single axis, identity by entity).

**Tracked per run** — `RunStats`, accumulated in the `Tick` step (§7), snapshotted on
`GameOver`:

| Field | Type | Updated |
|-------|------|---------|
| `pipesPassed` | `int` | +1 each time a pair is scored (§4.6); mirrors `Score` at death |
| `deathCause` | `Pipe \| Ground \| Ceiling` | set once on the lethal collision (§4.7) |
| `nearMisses` | `int` | +1 when the bird clears a gap within `nearMissPx` of a pipe edge |
| `flapsCount` | `int` | +1 per accepted `Flap` impulse (§4.2) during `Playing` |
| `runSeconds` | `float` | accumulated live-play time (sum of `dt` while `Playing`) |

`deathCause` is normally `Pipe` or `Ground` — the ceiling is clamped, not lethal in v1 (§4.2),
so `Ceiling` is only produced by the optional lethal-ceiling variant.

**Lifetime** — `LifetimeStats`, persisted (§13): `bestScore`, `attempts`, `avgPipes`,
`deathsByPipe`, `deathsByGround`, and `medalTier` (bronze/silver/gold/platinum, derived from
`bestScore` at the §15 thresholds 10/20/30/40).

**Layout** (logical 1280×720): a KPI tile row across the top, two charts below.

```
┌──────────────────────────── STATS ────────────────────────────┐
│  ┌  BEST  ┐ ┌ATTEMPTS┐ ┌AVG PIPES ┐ ┌  MEDAL   ┐              │  ← KPI stat tiles
│  │   27   │ │  138   │ │   4.6    │ │  GOLD    │              │
│  └────────┘ └────────┘ └──────────┘ └──────────┘              │
│                                                                │
│  Score distribution                 Best score so far          │
│  ▇▇                             27 ┤            ╭───────        │
│  ▇▇  ▇▇                            │        ╭───╯               │
│  ▇▇  ▇▇  ▇▇                        │    ╭───╯                   │
│  ▇▇  ▇▇  ▇▇  ▇▇  ▇▇                │ ╭──╯                       │
│  0  1-2 3-5 6-9 10+ (pipes)      0 ┼──────────────► attempt #   │
└────────────────────────────────────────────────────────────────┘
     ↑/↓ scope:  ▸ This Run · Lifetime            ESC — Back
```

**Charts** (rendered in Skia with the same draw-list discipline as §8):

1. **Score distribution** — *form: a distribution → bars.* x = pipes-passed bucketed as
   `0, 1-2, 3-5, 6-9, 10+`, y = number of attempts that ended in that bucket. **Single
   series**, so one hue and no legend. Bars are 4 px-rounded at the data end with a 2 px
   surface gap between them. Fill `#2a78d6` (light) / `#3987e5` (dark) — validated
   categorical slot 1.
2. **Best score so far** — *form: change over an ordered index → line.* x = attempt number,
   y = the personal-best pipes-passed as of that attempt — a **monotonic step line** that
   only ever rises. **Single series**, so one hue and no legend; drawn in slot 1
   (`#2a78d6` light / `#3987e5` dark), 2 px line with ≥ 8 px end marker at the current best,
   over recessive 1 px gridlines in `#3C3C3C`.

Conventions honored: **color follows the entity** (the best-score series keeps slot 1 in both
scopes — never repainted by the scope toggle); **one axis only** (no dual-scale); chart
**text uses ink tokens** (`#FFFFFF` primary / `#C3C2B7` muted), never the series hue; layout
is **fixed and deterministic**, so a fixed-seed session (§13) renders byte-identical for
snapshot tests. The `↑/↓` **scope** toggle swaps the data source This-Run ↔ Lifetime — in
This-Run scope the distribution highlights the just-finished attempt's bucket and the line
marks its point — without changing the hue.

**Model/Msg hooks:** add `Run: RunStats` and `Lifetime: LifetimeStats` to the §7 `Model`;
accumulate `Run` in the `Tick dt` `Playing` step (bump `pipesPassed`/`flapsCount`/`nearMisses`,
add `dt` to `runSeconds`) and set `deathCause` when the collision transitions to `GameOver`;
on `GameOver`, fold `RunStats` into `Lifetime` (increment `attempts`, roll `avgPipes`, bump
`deathsByPipe`/`deathsByGround`, refresh `bestScore`/`medalTier`) and persist alongside `Best`
(§13, the `flappy.best` store). `OpenStats`/`CloseStats` switch a `Stats of scope:StatScope`
overlay; the render is a no-op on physics.

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
| Flap (§4.2) | `Audio.playSfx` | `SoundId "flap"` | short "wing" whoosh |
| Pass pipe / score (§4.6) | `Audio.playSfx` | `SoundId "point"` | bright "point" blip |
| Collision — pipe (§4.7) | `Audio.playSfx` | `SoundId "hit"` | "hit" thud |
| Ground impact (§4.7) | `Audio.playSfx` | `SoundId "die"` | "die" splat (after hit) |
| New best on game-over (§11) | `Audio.playSfx` | `SoundId "new-best"` | "fanfare" sting |

There is no soundtrack during play — the run is **silent between these events** (classic
feel). The optional low title-screen ambient maps to `Audio.playMusic (TrackId "title-ambient") true`
(loop true) on `Ready`, stopped with `Audio.stopMusic` on the first flap into `Playing`. A
mute/settings toggle maps to `Audio.setMasterVolume` (e.g. `0.0` to silence, clamped to
`[0.0, 1.0]`). **Testing:** collect the frame's
`AudioEffect`s, `Audio.interpret` them, and assert the `AudioEvidence.Requested` sequence for
representative events (e.g. a `Flap` while `Playing` requests exactly `PlaySfx (SoundId "flap", _)`).

## 11. Win / Loss / Scoring
- **Scoring:** +1 per pipe pair passed (see 4.6). No multipliers, no time bonus, no combo.
- **Win condition:** none — the game is endless. "Winning" is beating your `Best`.
- **Loss condition:** any collision (pipe or ground) ends the run instantly (see 4.7).
- **Lives / continues:** none. One life per run; death → game-over → manual restart.
- **Best:** `Best = max(Best, finalScore)`, persisted across runs (see §13).

## 12. Difficulty & Balancing
Data-driven tunables (defaults match classic feel). The optional `*Ramp` values are OFF
by default.

| Name | Default | Range | Effect |
|------|---------|-------|--------|
| `gravity` | 2400 px/s² | 1500–3200 | Fall acceleration; higher = heavier, harder |
| `flapImpulse` | -620 px/s | -500..-760 | Upward beat strength (set, not added) |
| `vyMax` | 900 px/s | 600–1200 | Terminal fall speed |
| `scrollSpeed` | 180 px/s | 120–300 | World/forward speed; higher = less reaction time |
| `pipeSpacing` | 360 px | 260–480 | Horizontal distance between gaps |
| `gapHeight` | 200 px | 130–260 | Vertical opening; smaller = harder |
| `gapMargin` | 80 px | 40–140 | Keeps gaps off the top/ground |
| `pipeWidth` | 80 px | 60–100 | Pipe thickness |
| `birdX` | 320 px | 200–480 | Bird's fixed screen column |
| `collisionInset` | 2 px | 0–6 | Forgiveness on the bird hitbox |
| `restartLockoutMs` | 600 ms | 0–1200 | Anti-misclick delay on game-over |
| `gapHeightRamp` | 0 (off) | 0–0.5 px/pt | Optional: shrink gap by N px per point scored (floor 130) |
| `scrollSpeedRamp` | 0 (off) | 0–2 px/s/pt | Optional: speed up scroll per point (cap 300) |

## 13. Technical Notes
- **Performance budget:** trivial — 1 bird + ~5 pipe pairs + ground + HUD ≈ < 20 draw
  calls/frame. Target 60 FPS / 16.7 ms frame with vast headroom.
- **Fixed vs. variable timestep:** **fixed** 60 Hz logic, drained by **`FixedStep.drain`** — do not
  hand-roll the accumulator. `FixedStep.drain (1.0/60.0) frameTime acc` returns `struct (steps, acc')`:
  dispatch `Tick (1.0/60.0)` exactly `steps` times and bank `acc'`. It caps the frame internally
  (`FixedStep.defaultMaxFrameTime`, 0.25 s), so a stall cannot spiral into a catch-up storm. This makes
  physics deterministic and frame-rate independent; rendering interpolation is optional for v1.
- **Determinism / RNG:** `gapCenterY` is the only randomness. Use a single **`Rng`**
  (`FS.GG.Game.Core`, splitmix64) in the model, seeded with `Rng.ofSeed`; draw the gap with
  `Rng.nextFloat`. A fixed seed + a fixed sequence of `Flap` ticks yields an identical pipe layout —
  this is what the acceptance tests rely on. It is a **value**, not a `System.Random`: every draw returns `struct (x, rng')` and you write
  `rng'` back to the `Model`, so the `Model` stays a value you can snapshot, replay and compare.
  A `System.Random` in the `Model` is a mutable object *shared* by every copy of it, which
  silently breaks the reproducibility this bullet promises.
- **Persistence:** `Best` is saved to local storage / a small JSON file (`flappy.best`) and
  loaded on boot. If absent, `Best = 0`.
- **Edge cases:** flap on the exact death frame still dies; multiple pipes can be passed in
  a single tick at high `scrollSpeed` (loop all unpassed pairs, not just the front one);
  if the window loses focus, auto-`TogglePause`; resizing only rescales the logical canvas,
  never the physics constants; a gap must always satisfy `gapHeight + 2*gapMargin ≤ groundY`
  (200 + 160 = 360 ≤ 640 ✓) or spawning asserts.

## 14. Acceptance Criteria (test scenarios)

1. **Gravity pulls the bird down.**
   GIVEN a `Playing` model with `bird.y = 360`, `bird.vy = 0`
   WHEN one `Tick(1/60)` is applied with no `Flap`
   THEN `bird.vy ≈ 40` px/s (`2400 * 1/60`) and `bird.y > 360` (moved downward).

2. **Terminal velocity is clamped.**
   GIVEN a `Playing` model with `bird.vy = 880`
   WHEN one `Tick(1/60)` is applied (no flap)
   THEN `bird.vy = 900` (clamped to `vyMax`), not 920.

3. **Flap sets a fixed upward velocity (override, not additive).**
   GIVEN a `Playing` model with `bird.vy = 300` (falling)
   WHEN `Flap` is dispatched
   THEN `bird.vy = -620` exactly; AND dispatching `Flap` again immediately also yields
   `bird.vy = -620` (no stacking).

4. **Held key does not auto-flap.**
   GIVEN `Space` is pressed and held down across 30 ticks
   WHEN no new key-down edge occurs
   THEN exactly one flap impulse was applied (only on the initial press edge).

5. **Score increments when a pipe pair passes the bird.**
   GIVEN a pipe pair at `x` such that `x + pipeWidth = birdX + 1` and `scored = false`, score = 4
   WHEN ticks advance until `x + pipeWidth < birdX`
   THEN `score = 5` and that pair's `scored = true`; AND further ticks do not increment
   again for the same pair.

6. **Multiple pipes can score in one tick.**
   GIVEN two unpassed pairs both with `x + pipeWidth` just past `birdX` after a large `dt`
   WHEN one `Tick` is applied
   THEN `score` increases by 2 (the scorer loops all unpassed pairs).

7. **Collision with a pipe is instant death.**
   GIVEN a `Playing` model where the bird AABB overlaps a top pipe rect
   WHEN one `Tick` is applied
   THEN `Phase = GameOver finalScore=score` and scrolling stops on subsequent ticks.

8. **Ground collision is instant death.**
   GIVEN `bird.y` such that `bird.box.bottom ≥ 640` (groundY)
   WHEN one `Tick` is applied
   THEN `Phase = GameOver` (regardless of pipe positions).

9. **Ceiling is clamped, not lethal.**
   GIVEN repeated `Flap`s drive the bird to `y ≤ -birdHeight`
   WHEN ticks continue
   THEN `bird.y` is clamped to `-birdHeight`, `vy ≥ 0`, and `Phase` stays `Playing`.

10. **Pipe spawning cadence.**
    GIVEN a fresh `Playing` run with empty pipes and `scrollSpeed = 180`
    WHEN the world scrolls `pipeSpacing = 360` px (i.e. 2.0 s of ticks)
    THEN exactly one new pair has spawned per 360 px of scroll, each at `x = 1360`.

11. **Gap randomization stays in the safe band.**
    GIVEN any spawned pair across 1000 seeded spawns
    WHEN `gapCenterY` is sampled
    THEN `180 ≤ gapCenterY ≤ 460` for every pair (gap never clips top or ground).

12. **Deterministic layout from a seed.**
    GIVEN two runs with the same RNG seed and the identical sequence of `Flap`/`Tick`
    WHEN both run for 30 s
    THEN the two pipe layouts (`x`, `gapCenterY` sequences) are identical and scores match.

13. **Restart lockout on game-over.**
    GIVEN `Phase = GameOver` with `GameOverElapsedMs = 200` (< 600)
    WHEN `Flap` is dispatched
    THEN the model stays `GameOver` (input ignored); AND once `GameOverElapsedMs ≥ 600`,
    a `Flap` resets to a fresh `Ready` model preserving `Best`.

14. **Best score persists and updates.**
    GIVEN `Best = 7` and a run that ends with `finalScore = 12`
    WHEN entering `GameOver`
    THEN `Best = 12`; AND a subsequent run ending at `finalScore = 5` leaves `Best = 12`.

15. **Pause freezes physics.**
    GIVEN a `Playing` model
    WHEN `TogglePause` then several `Tick`s are applied
    THEN `bird.y`, `bird.vy`, and pipe `x` are unchanged until `TogglePause` resumes.

## 15. Stretch Goals
1. **Medals** (bronze/silver/gold/platinum at score thresholds 10/20/30/40) on game-over.
2. **Difficulty ramp** — enable `gapHeightRamp`/`scrollSpeedRamp` for an escalating mode.
3. **Day/night theme swap** every N points (palette change for sky/pipes).
4. **Ghost replay** of your best run (deterministic RNG makes this nearly free).
5. **Daily seed challenge** — fixed seed of the day, leaderboard by score.
6. **Alternate birds / skins** (cosmetic palette swaps).
7. **Gamepad + touch** input parity for handheld/mobile builds.

## 16. Milestone Roadmap

Implementation is sequenced into milestones; each item is a colored checkbox
tracking its status. Items reference the section that specifies them.

**Legend:** 🟥 Not started · 🟨 In progress · 🟩 Done · ⬜ Deferred (post-v1)

_All items start 🟥 (spec status). Flip an item to 🟨 when work begins and 🟩 once
its acceptance test(s) pass (§14)._

### M0 — Scaffold & fixed-step loop
- 🟥 Project scaffold: `Model`/`Msg`/`update`/`view` skeleton (§7)
- 🟥 Fixed 60 Hz tick via `FixedStep.drain`, banked remainder (§13)
- 🟥 `Rng` value seeded with `Rng.ofSeed`, threaded through `Model` (§13)
- 🟥 Logical 1280×720 coordinate transform, +Y down, letterbox scaling (§4, §6, §8)

### M1 — Input & flap edge
- 🟥 Edge-triggered `Space`/`↑`/click flap with key-held de-bounce (§3) — AC #4
- 🟥 `Esc` → `TogglePause` edge; focus loss auto-pauses (§3, §13)
- 🟥 Game-over `restartLockoutMs` window ignores restart input (§3) — AC #13

### M2 — Bird physics
- 🟥 Gravity integration `vy += gravity*dt`, terminal clamp `vyMax` (§4.1) — AC #1, #2
- 🟥 Flap impulse sets `vy = flapImpulse` (override, no stacking) (§4.2) — AC #3
- 🟥 Ceiling clamp (non-lethal) + velocity-derived `rotationDeg` (§4.2, §5) — AC #9

### M3 — World scroll & pipe spawning
- 🟥 Fixed `birdX`; pipes scroll left at `scrollSpeed` (§4.3)
- 🟥 Pipe-pair spawner every `pipeSpacing` px, off the right edge (§4.4) — AC #10
- 🟥 Randomized `gapCenterY` in safe band `[180, 460]` via `Rng` (§4.5) — AC #11
- 🟥 Despawn pairs once fully off the left edge (§4.4)

### M4 — Collisions & scoring
- 🟥 Inset bird AABB vs. pipe rects → instant death (§4.7) — AC #7
- 🟥 Ground-strip collision at `groundY` → instant death (§4.7) — AC #8
- 🟥 Pass-scoring `scored` flag, loop all unpassed pairs (§4.6) — AC #5, #6

### M5 — Phase flow & persistence
- 🟥 `Ready` / `Playing` / `GameOver` phase transitions (§7)
- 🟥 Instant-death → `GameOver finalScore`, freeze scroll, cosmetic fall (§4.7, §11)
- 🟥 `Best = max(Best, finalScore)` persisted to `flappy.best` (§11, §13) — AC #14
- 🟥 Pause freezes physics, resumes exact state (§7) — AC #15

### M6 — Rendering (Skia)
- 🟥 Draw order: sky, pipes, ground strip, bird, HUD, overlays (§8)
- 🟥 Pipe lip caps + parallax ground/cloud scroll (§8)
- 🟥 Optional collision flash + flap dust particles (§8)

### M7 — UI, menus & settings
- 🟥 Ready/Playing/Paused/GameOver screens + live score & best HUD (§9)
- 🟥 Menu stack, cursor wrap, cycler/slider `◄ value ►` rows (§9.1)
- 🟥 Difficulty presets + volume/sound/shake settings apply live + persist (§9.1, §12)

### M8 — Stats & charts
- 🟥 `RunStats`/`LifetimeStats` accumulation + persist, medal tiers (§9.2)
- 🟥 Score-distribution bars + monotonic best-score-so-far step line (§9.2)

### M9 — Audio
- 🟥 `AudioEffect` cues (flap/point/hit/die/new-best), `Audio.interpret`, volume clamp `[0,1]` (§10)

### M10 — Acceptance & determinism
- 🟥 All 15 acceptance scenarios green (§14)
- 🟥 Seed + `Flap`/`Tick` sequence yields byte-identical pipe layout (§13) — AC #12

### Stretch — deferred (post-v1)
- ⬜ Medals (bronze/silver/gold/platinum at 10/20/30/40) on game-over (§15.1)
- ⬜ Difficulty ramp via `gapHeightRamp`/`scrollSpeedRamp` (§15.2)
- ⬜ Day/night theme swap every N points (§15.3)
- ⬜ Ghost replay of your best run (deterministic RNG) (§15.4)
- ⬜ Daily seed challenge + score leaderboard (§15.5)
- ⬜ Alternate birds / skins (cosmetic palette swaps) (§15.6)
- ⬜ Gamepad + touch input parity (§15.7)
