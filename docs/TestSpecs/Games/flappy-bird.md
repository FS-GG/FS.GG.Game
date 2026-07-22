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

**The flap cycle (the core rhythm).** A single flap sets `vy = -620` and gravity claws it
back at `2400 px/s²`, so the bird coasts up for `620 / 2400 ≈ 0.26 s`, gaining about
`620² / (2·2400) ≈ 80 px` of altitude before it stalls and falls again. That ~80 px lift on a
~0.26 s arc is the game's fundamental beat: hold a line by re-flapping near the top of each arc
(roughly 3–4 taps/second), climb hard by flapping *early* (before the apex), and sink through a
low gap by simply not tapping — terminal velocity (`900 px/s`, reached `0.375 s` after a standing
start) does the rest.

**The decision window.** A pair spawns at `x = 1360` while the bird sits at `birdX = 320`, so
every gap telegraphs itself `(1360 − 320) / 180 ≈ 5.8 s` before it arrives — but the *commitment*
window is far shorter: the bird occupies a gap's column for only `pipeWidth / scrollSpeed =
80 / 180 ≈ 0.44 s`, and with pairs `pipeSpacing / scrollSpeed = 360 / 180 = 2.0 s` apart the
player faces a fresh life-or-death judgement every two seconds. Reading the *next* gap's height
while still threading the current one is the skill the loop trains.

**The failure loop.** Death is instant and always legible — you can see the exact pixel that
killed you — so the restart urge is immediate; the `restartLockoutMs = 600 ms` window (§3) is the
only thing between the killing tap and the next run, kept deliberately short so "one more try"
stays frictionless.

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

### 4.8 Flap cadence & the sustainable-altitude band
- Because a flap **sets** `vy` (§4.2), the bird's maximum sustained climb rate is bounded by
  `|flapImpulse| = 620 px/s` — reachable only by flapping on (nearly) every tick — and the
  ceiling clamp (§4.2) is what stops a tap-spammer from parking above the pipes.
- Flapping once per apex holds a roughly **80 px-tall hover band** (§2): each beat recovers
  exactly the altitude the preceding fall cost, so net drift ≈ 0. Flap before the apex to climb;
  let the arc finish before re-tapping to sink. This 80 px band is a hair under half of
  `gapHeight = 200`, so a centered bird clears a normal gap on one well-timed flap.
- **Opening grace (emergent, not scripted):** at run start `Pipes` is empty and
  `DistanceSinceSpawn = 0`, so the first pair spawns only after 360 px of scroll (2.0 s) and then
  travels from `x = 1360` to the bird's column — giving ~7.8 s of pipe-free air to settle into the
  flap rhythm before the first real gap. No special-case code is required; it falls out of §4.4.

### 4.9 Near-miss detection & hitbox fairness
- The `collisionInset = 2 px` (§4.7) means the drawn sprite can visually kiss a pipe by up to 2 px
  on any side without dying — every near-miss is, by construction, a frame where the *sprite*
  overlapped but the *hitbox* did not. This forgiveness is what makes a threaded gap feel earned
  rather than cheap.
- **Near-miss:** after a pair is scored (§4.6) — i.e. the bird cleared it without the §4.7
  collision — take the smallest vertical clearance recorded between the bird's inset AABB and
  either pipe rect of that pair while the bird was inside the gap column. If that minimum was
  ≤ `nearMissPx = 12 px`, count one `RunStats.nearMisses` (§9.2). A comfortable pass (clearance
  above `nearMissPx`) counts nothing.
- **Corner rule (clarifies "any overlap = collision", §4.7):** AABB tests use half-open bounds,
  so boxes whose edges merely touch (intersection of zero area, clearance exactly 0) do **not**
  overlap and are survivable; a death requires strictly positive penetration of the inset boxes.
  A zero-clearance graze is therefore the tightest possible near-miss, not a death.

### 4.10 Pipe traversal, tick order & tunneling
- The per-tick order is **integrate → scroll → spawn → despawn → score → collide** (§7). Because
  collision is evaluated **last**, a bird that banks a point and then clips the pipe's trailing lip
  on the same tick still dies that tick (matches §13, "flap on the exact death frame still dies")
  — but the point is already counted (§11).
- **No tunneling at any tunable value:** at the maximum `scrollSpeed = 300` (§12) a pair moves
  `300 / 60 = 5 px` per tick — far less than `pipeWidth = 80 px` and the gap column — so a pipe can
  never skip the bird's column between two collision tests. No swept/continuous collision is needed.
- **Multi-pass:** the scorer loops **every** unpassed pair (§4.6, §7 step 5), so a single large
  `dt` after a stall (capped at `FixedStep.defaultMaxFrameTime`, §13) that pushes two gaps past
  `birdX` awards both points in one drain — never just the frontmost (AC #6).

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

**Bird — cosmetic state.** The bird carries no health or hit state (instant death, §4.7); its
only per-frame extras are the derived `RotationDeg` (§5 sketch) and a 2-frame wing toggle that
flips on each accepted `Flap` (§8). While `Phase = Ready` it hovers on a cosmetic sine —
`CenterY = 360 + bobAmplitude · sin(2π · t / bobPeriodS)` with `bobAmplitude = 12 px`,
`bobPeriodS = 1.6 s` — with gravity and collision disabled (§7). The first `Flap` snaps it out of
the bob into live physics at whatever `CenterY` the bob left it, with `vy = flapImpulse`.

**PipePair — lifecycle.** Every pair walks the same five stages from spawn to despawn:

| Stage | Trigger | Effect |
|-------|---------|--------|
| Spawn | `DistanceSinceSpawn ≥ pipeSpacing` (§4.4) | `LeftX = 1360`, `GapCenterY` drawn from `Rng` (§4.5), `Scored = false` |
| Approach | every `Tick` (§7) | `LeftX -= scrollSpeed·dt` |
| Threat | `LeftX < birdX + birdWidth/2` **and** `LeftX + pipeWidth > birdX − birdWidth/2` | pair is inside the collision column (§4.7); a hit here ends the run |
| Score | `LeftX + pipeWidth < birdX` **and** `not Scored` (§4.6) | `Scored ← true`, `Score += 1` |
| Despawn | `LeftX + pipeWidth < 0` (§4.4) | removed from `Pipes` |

Threat can precede Score (the bird is in the column before the pair's right edge clears `birdX`),
which is exactly why the gap must be threaded, not merely reached.

**Ground — divergent visual/collision.** The ground texture wraps every 48 px (§8) and scrolls at
`scrollSpeed`, but its collision top stays pinned at `groundY = 640`. It is the only entity whose
drawn position and collision position deliberately diverge — the parallax must never move the
lethal line.

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

**Perceived progression (flat difficulty, rising stakes).** Because every constant is fixed in v1
(§6), the run never gets mechanically harder — but it *feels* harder as the score you'd forfeit
grows. The milestones the player actually chases map to the medal thresholds (§15.1): the first
pipe (nerves), 10 (bronze), 20 (silver), 30 (gold), 40 (platinum). These are among-runs UI goals
only; they change no physics.

**Parallax stack (back to front).** The world is three scroll planes sharing one `scrollSpeed`:

| Plane | Speed | Role |
|-------|-------|------|
| Clouds / sky detail | `0.3 · scrollSpeed = 54 px/s` (§8) | depth, purely cosmetic |
| Pipes (the world) | `scrollSpeed = 180 px/s` (§4.3) | the only plane the bird collides with |
| Ground texture | `scrollSpeed = 180 px/s`, wraps every 48 px (§8) | reads the speed; collision top fixed at 640 |

Only the pipe plane is gameplay; the other two never touch physics and may be dropped on low-end
targets with zero rules impact.

**World determinism.** The entire world state is a function of `(seed, flap-timing sequence)`:
`gapCenterY` is the sole `Rng` draw (§13) and everything else is deterministic integration, so two
sessions with the same seed and inputs scroll a byte-identical world (AC #12) — the property the
ghost-replay and daily-seed stretch goals (§15.4, §15.5) lean on.

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

### 9.1 Menu & configuration — the shared game shell

Flappy Bird uses the **generic FS.GG game shell** (FS-GG/FS.GG.Rendering#991) — the same
menu/start screen and settings every FS.GG game shares — rather than a bespoke per-game menu.
The game supplies only its **name**, its **key→command map** (the rebindable actions from §3
Controls), and its play `update`/`view`; the shell provides everything below.

- **Main menu / start screen** — the game's name (**FLAPPY BIRD**) as the title label, with
  **Start**, **Config**, and **Exit**. The game is endless with no lives, so the run-end path
  uses **Retry framing** (§11): the primary action spins up a brand-new run rather than
  resuming, and stays greyed until the `restartLockoutMs` window (§12) elapses, matching the
  anti-misclick lockout in §3.
- **`Esc` from gameplay** opens the pause menu (Resume · Config · Exit to menu) over the same
  shell; `Esc` again resumes. The play surface is one-button (§3) — the shell's menu cursor
  keys are only live while a menu is open and never interfere with the edge-triggered `Flap`.
- **Config / Settings**, all applied live and persisted across restarts:
  - **Screen resolution** and **fullscreen** (windowed / borderless / fullscreen), driven
    through the SkiaViewer window-behavior + `LogicalCanvas` letterbox seam.
  - **Key rebinding** — the player remaps this game's controls (the §3 action — Flap) via the
    `Controls.KeyRebind` UI over the `KeyboardInput.Keymap` mechanism; bindings persist via
    `KeymapCodec` (JSON), beside this game's other saved config (§13).
  - Game-specific rows are added as extra Config rows over the shell: **Difficulty** (the §12
    tunable preset — Easy `gapHeight 240 / scrollSpeed 150`, Normal `200 / 180`, Hard
    `170 / 210` with the optional `*Ramp` enabled), **Master volume**/**Sound** (route to
    `Audio.setMasterVolume`, §10, clamped `[0,1]`, `0` = silence), and **Screen shake** (toggles
    the §8 optional collision-flash/shake effect). The menu, Esc routing, display settings, and
    rebind screen come from the shell.

The shell is pointer- and keyboard-navigable over the interactive Controls host (the
`fs-gg-skiaviewer` "game → pointer host" recipe). It is a shared dependency, so Flappy Bird
does **not** re-specify menu-stack/cursor/settings machinery of its own. The **Stats & charts**
screen (§9.2) is a Flappy Bird-specific screen reached as a Config/menu row.

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

**Scoring edge cases.**
- A point is awarded the instant a pair's right edge crosses `birdX` (§4.6), *before* that tick's
  collision test (§7 order, §4.10) — so a bird that clears the gap and then clips the pipe's
  trailing lip on the same tick still banks the point before dying. Deaths never retroactively
  revoke a scored point.
- Passing the X line is sufficient; grazing the pipe on the way through is fine as long as the
  inset hitboxes never overlap (§4.9). There is no clean-pass bonus and no graze penalty — a
  near-miss (§4.9) scores exactly the same 1 point as a lazy centred pass.
- `finalScore` equals the number of `Scored` pairs at the moment of death; it is frozen into
  `GameOver finalScore` (§7) and never changes while the game-over bird finishes its cosmetic fall.

**Best-score semantics.** `Best = max(Best, finalScore)` is evaluated once, on the transition into
`GameOver` (§7 step 6), so a new best is known before the panel draws — that is when the `new-best`
fanfare fires (§10) and the game-over medal (§15.1) is chosen. A run that merely *ties* the current
`Best` does not re-trigger the fanfare: the sting uses strict `>`, while the stored `max` is a
no-op on a tie.

**Run-length shape.** A session is dozens of runs (§2), most ending within a few seconds. The
floor on run length is set by a player who never flaps: from the spawn pose (`CenterY = 360`) the
inset box bottom starts ~270 px above `groundY`, so it hits the ground in
`√(2·270 / 2400) ≈ 0.47 s`. The opening grace (§4.8) guarantees the first *pipe* cannot threaten
for ~7.8 s — the early deaths are self-inflicted, not the level's fault. This short-run,
high-restart cadence is exactly why the lockout is only 600 ms (§3).

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

**Difficulty presets (from §9.1).** The Settings **Difficulty** row selects one preset; every
other tunable stays at its default above unless the preset overrides it.

| Preset | `gapHeight` | `scrollSpeed` | Ramps | Gap cadence | Feel |
|--------|-------------|---------------|-------|-------------|------|
| Easy | 240 | 150 | off | `360/150 = 2.4 s` | wide gaps, slow world |
| Normal | 200 | 180 | off | `2.0 s` | the classic tuning |
| Hard | 170 | 210 | on | `≈1.71 s` | tight gaps, fast world, tightening as you score |

**Ramp math (Hard / the escalating mode, §15.2).** With the optional ramps enabled both quantities
scale linearly with `Score` and clamp:
- `gapHeight(s) = max(gapHeightFloor, gapHeightBase − gapHeightRamp · s)`. Example at
  `gapHeightRamp = 0.5` from a Hard base of 170: the gap is 150 px at score 40 and reaches its
  `130 px` floor at score 80 — never smaller.
- `scrollSpeed(s) = min(scrollSpeedCap, scrollSpeedBase + scrollSpeedRamp · s)`. Example at
  `scrollSpeedRamp = 1.0` from a Hard base of 210: 250 px/s at score 40, hitting the `300 px/s`
  cap at score 90.
- Ramps recompute **per scored point, never mid-gap**, and a pair's geometry is frozen at spawn
  from the `gapHeight` in force at that moment (§4.5) — already-spawned pipes never resize under
  the player.

**New tunables (added).**

| Name | Default | Range | Effect |
|------|---------|-------|--------|
| `nearMissPx` | 12 px | 4–24 | Clearance at or under which a cleared gap counts a near-miss (§4.9, §9.2) |
| `bobAmplitude` | 12 px | 0–30 | Ready-screen hover height (cosmetic, §5) |
| `bobPeriodS` | 1.6 s | 0.8–3.0 | Ready-screen hover period (cosmetic, §5) |
| `gapHeightFloor` | 130 px | 110–200 | Lower clamp for `gapHeightRamp` (§12 ramp math) |
| `scrollSpeedCap` | 300 px/s | 200–360 | Upper clamp for `scrollSpeedRamp` (§12 ramp math) |

**Tuning rationale (why these numbers).**
- The `flapImpulse : gravity` ratio fixes the hover band at `620² / (2·2400) ≈ 80 px` (§2, §4.8),
  a hair under half of `gapHeight = 200`, so a centred bird needs one well-timed flap per gap —
  loose enough to feel fair, tight enough to punish a lazy tap.
- `scrollSpeed = 180` with `pipeSpacing = 360` gives the 2.0 s cadence AC #10 pins. Dropping
  `pipeSpacing` below ~260 collapses the reaction window faster than shrinking `gapHeight` does,
  which is why the Hard preset leans on `scrollSpeed` and the ramp rather than sub-260 spacing.
- `collisionInset = 2` and `nearMissPx = 12` are the two forgiveness dials: the first decides what
  *kills* you, the second decides what merely *thrills* you (§4.9).

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

16. **Near-miss is counted on a tight clearance.**
    GIVEN a `Playing` run where the bird clears a gap with a minimum inset-AABB clearance of 8 px
    (≤ `nearMissPx = 12`) to a pipe rect
    WHEN that pair is scored (§4.6)
    THEN `RunStats.nearMisses` increments by 1; AND an otherwise identical pass clearing by 20 px
    does not.

17. **A single flap lifts ~80 px before stalling.**
    GIVEN a `Playing` bird at `bird.vy = 0` that receives one `Flap` (`vy = -620`) and no further
    input
    WHEN ticks advance until `bird.vy ≥ 0` again
    THEN the apex is ~80 px above the pre-flap `CenterY` (`620² / (2·2400) ≈ 80`) reached after
    ~0.26 s, after which the bird descends.

18. **Ready-screen bob is cosmetic and non-lethal.**
    GIVEN `Phase = Ready`
    WHEN many `Tick`s advance
    THEN `bird.y` oscillates within ±`bobAmplitude` of 360, `Phase` stays `Ready`, gravity is
    never integrated, and no collision or score occurs; the first `Flap` transitions to `Playing`.

19. **Difficulty preset applies its constants.**
    GIVEN Settings Difficulty = Hard
    WHEN a run starts
    THEN `gapHeight = 170` and `scrollSpeed = 210` are in force (§9.1, §12); AND with ramps enabled
    the gap shrinks toward its `130 px` floor as `Score` climbs.

20. **Ramp respects the gap floor.**
    GIVEN the escalating mode with `gapHeightRamp = 0.5` and base `gapHeight = 170`
    WHEN `Score` reaches 80 and beyond
    THEN `gapHeight` clamps at `130 px` and never goes lower; AND already-spawned pipes keep the
    geometry they had at spawn.

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
- 🟥 Flap-cadence hover band (~80 px) + climb bounded by `|flapImpulse|` (§4.8) — AC #17

### M3 — World scroll & pipe spawning
- 🟥 Fixed `birdX`; pipes scroll left at `scrollSpeed` (§4.3)
- 🟥 Pipe-pair spawner every `pipeSpacing` px, off the right edge (§4.4) — AC #10
- 🟥 Randomized `gapCenterY` in safe band `[180, 460]` via `Rng` (§4.5) — AC #11
- 🟥 Despawn pairs once fully off the left edge (§4.4)
- 🟥 PipePair lifecycle spawn→approach→threat→score→despawn (§5)
- 🟥 Emergent ~7.8 s opening grace from empty-`Pipes` spawn timing (§4.8)

### M4 — Collisions & scoring
- 🟥 Inset bird AABB vs. pipe rects → instant death (§4.7) — AC #7
- 🟥 Ground-strip collision at `groundY` → instant death (§4.7) — AC #8
- 🟥 Pass-scoring `scored` flag, loop all unpassed pairs (§4.6) — AC #5, #6
- 🟥 Near-miss detection on cleared gaps within `nearMissPx` (§4.9) — AC #16
- 🟥 Tick order move→spawn→despawn→score→collide; no swept collision at max `scrollSpeed` (§4.10)
- 🟥 Point banked before the collision test; a scored point is never revoked by death (§11)

### M5 — Phase flow & persistence
- 🟥 `Ready` / `Playing` / `GameOver` phase transitions (§7)
- 🟥 Instant-death → `GameOver finalScore`, freeze scroll, cosmetic fall (§4.7, §11)
- 🟥 `Best = max(Best, finalScore)` persisted to `flappy.best` (§11, §13) — AC #14
- 🟥 Pause freezes physics, resumes exact state (§7) — AC #15
- 🟥 Ready-screen cosmetic bob, gravity/collision disabled (§5) — AC #18
- 🟥 `new-best` fanfare only on a strictly-greater `finalScore` (§10, §11)

### M6 — Rendering (Skia)
- 🟥 Draw order: sky, pipes, ground strip, bird, HUD, overlays (§8)
- 🟥 Pipe lip caps + parallax ground/cloud scroll (§8)
- 🟥 Optional collision flash + flap dust particles (§8)

### M7 — UI, menus & settings
- 🟥 Ready/Playing/Paused/GameOver screens + live score & best HUD (§9)
- 🟥 Adopt the generic FS.GG game shell (FS-GG/FS.GG.Rendering#991): main menu (title + Start/Config/Exit), Esc pause routing, Settings with screen resolution + fullscreen, and in-game key rebinding of the §3 controls, persisted — the game provides its name + key→command map + play update/view; the shell provides the rest, no bespoke menu system (§9.1)
- 🟥 Game-specific Config rows over the shell (difficulty preset, volume/sound, screen shake) apply live + persist (§9.1, §12)
- 🟥 Difficulty preset applies its constants (Easy/Normal/Hard) at run start (§9.1, §12) — AC #19

### M8 — Stats & charts
- 🟥 `RunStats`/`LifetimeStats` accumulation + persist, medal tiers (§9.2)
- 🟥 Score-distribution bars + monotonic best-score-so-far step line (§9.2)

### M9 — Audio
- 🟥 `AudioEffect` cues (flap/point/hit/die/new-best), `Audio.interpret`, volume clamp `[0,1]` (§10)

### M10 — Acceptance & determinism
- 🟥 All 15 acceptance scenarios green (§14)
- 🟥 Seed + `Flap`/`Tick` sequence yields byte-identical pipe layout (§13) — AC #12
- 🟥 New scenarios green: near-miss, flap apex, ready-bob, difficulty preset, ramp floor (§4.8, §4.9, §5, §9.1, §12) — AC #16, #17, #18, #19, #20

### Stretch — deferred (post-v1)
- ⬜ Medals (bronze/silver/gold/platinum at 10/20/30/40) on game-over (§15.1)
- ⬜ Difficulty ramp via `gapHeightRamp`/`scrollSpeedRamp` (§15.2)
- ⬜ Day/night theme swap every N points (§15.3)
- ⬜ Ghost replay of your best run (deterministic RNG) (§15.4)
- ⬜ Daily seed challenge + score leaderboard (§15.5)
- ⬜ Alternate birds / skins (cosmetic palette swaps) (§15.6)
- ⬜ Gamepad + touch input parity (§15.7)
