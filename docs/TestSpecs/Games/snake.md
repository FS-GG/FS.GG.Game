---
title: "Snake"
slug: snake
category: games
complexity: simple
genre: "Arcade / grid-based survival"
target_session_minutes: 5
stack: { rendering: "FS.GG.Rendering (Skia/OpenGL)", framework: "FS.GG.Game.Core (FixedStep for the tick; Rng for determinism)", arch: "Elmish/MVU", lang: "F#" }
status: spec
---

# Snake

## 1. Overview
You pilot an ever-lengthening snake across a fixed grid, steering it to swallow
food pellets. Each pellet makes the snake longer and the game faster, so the very
act of succeeding shrinks your room to maneuver. The core verb is **turn** —
choosing when to commit the head to a new heading before it crashes into a wall or
its own tail. The fun is the tightening pressure: an early game of lazy loops
becomes a late-game spatial puzzle where you must thread the snake through corridors
of its own body. One mistake ends the run, making every score a personal high-water
mark to beat.

## 2. Core Game Loop
**Moment-to-moment:** observe head position → queue a turn → snake steps one cell on
the tick → eat food (grow) or move into empty cell → repeat. The player is
continuously reading the board and pre-committing turns.

**Session-level:** Title screen → press Start → play (snake grows, speed ramps) →
collision → Game Over screen showing score and high score → press Restart → new run
with a fresh 1-length... (actually 3-length, see §5) snake at center.

## 3. Controls & Input
Input is **edge-triggered** (key-down events), not held-state. Pressing a direction
enqueues a turn for the *next* tick; the snake does not move faster by mashing keys.

| Input | Action | Notes |
|---|---|---|
| Arrow Up / `W` | Queue turn to Up | Ignored if it would reverse 180° |
| Arrow Down / `S` | Queue turn to Down | Ignored if it would reverse 180° |
| Arrow Left / `A` | Queue turn to Left | Ignored if it would reverse 180° |
| Arrow Right / `D` | Queue turn to Right | Ignored if it would reverse 180° |
| `Space` / `P` | Pause / Resume | Toggle; only during Play |
| `Enter` / `Space` | Start / Restart | On Title and GameOver screens |
| `Esc` | Quit to Title | From Play or Pause |

Direction inputs are pushed into a small **direction queue** (max 2 entries). The
tick handler dequeues at most one turn per step. This lets a player buffer a fast
two-step maneuver (e.g. Up then Right around a corner) within a single tick interval
without dropping the second press. See §4.3.

## 4. Mechanics (detailed)

### 4.1 Grid & coordinate system
- Logical playfield: **1280×720 px**.
- Grid: **32 columns × 18 rows** of cells.
- Cell size: **40×40 px** (1280 / 32 = 40, 720 / 18 = 40 — exact, no remainder).
- Cell coordinates are integer `(col, row)` with origin `(0,0)` at top-left, `col`
  increasing right, `row` increasing down.
- A cell's pixel rect is `(col*40, row*40, 40, 40)`.

### 4.2 Movement & stepping
- The snake is a sequence of occupied cells, head first. It moves in **discrete
  steps**, exactly one cell per **tick**, never sub-cell.
- On each step the head advances one cell in the current heading. The body follows:
  conceptually the head is prepended and the tail cell is removed — unless the snake
  grew this step, in which case the tail is retained (net +1 length).
- Heading is one of four unit vectors: Up `(0,-1)`, Down `(0,+1)`, Left `(-1,0)`,
  Right `(+1,0)`.
- Initial heading: **Right**.

### 4.3 Direction queue (180° reversal guard)
- Turns are buffered in a queue of capacity **2**.
- A turn is only enqueued if it is not the direct opposite of the *last committed or
  last queued* direction (whichever is most recent). This prevents the classic bug
  where pressing Left then Right in one tick folds the snake back onto itself.
- At the start of each tick, if the queue is non-empty, dequeue one direction and
  make it the new heading — but only if it is not the 180° opposite of the current
  heading (defense-in-depth; the enqueue guard should already prevent this).
- Repeated presses of the current heading are accepted (no-op turns) but capped by
  queue capacity.

### 4.4 Food & eating
- Exactly **one** food pellet exists at any time.
- Food occupies a single cell, never one currently occupied by the snake.
- Spawn rule: pick a uniformly random cell from the set of **unoccupied** cells
  (all 576 cells minus the snake's body). If the snake fills the entire board
  (no free cell), the player **wins** (see §11).
- When the head steps onto the food cell: increment score, grow the snake, increase
  speed, and spawn a new pellet.

### 4.5 Growth
- Eating food grows the snake by **+1 cell** (default `growthPerFood = 1`). Mechanically
  this means: on the eating step, the tail is **not** removed, so the body lengthens by
  one and fills the cell the food occupied.

### 4.6 Speed & acceleration
- Time-based, not per-frame. Stepping is governed by a **step interval** in seconds.
- `baseStepSeconds = 0.18` (≈ 5.5 steps/s at start).
- Each food eaten reduces the interval: `stepSeconds = max(minStepSeconds, baseStepSeconds - (foodEaten * stepDecrement))`.
- `stepDecrement = 0.006` s per food; `minStepSeconds = 0.06` (≈ 16.6 steps/s cap).
- Reaching the floor takes `(0.18 - 0.06) / 0.006 = 20` pellets, after which speed is
  constant.
- A step accumulator (§7 / §13) decouples stepping from the 60 FPS render tick.

### 4.7 Collision & death
- **Wall collision:** in *Wall* mode, if the next head cell is outside `[0,31]×[0,17]`,
  the snake dies.
- **Self collision:** if the next head cell is currently part of the snake's body, the
  snake dies. Exception: the **current tail cell** is vacating this step (when not
  growing), so moving the head into the old tail position is **allowed** and not a
  collision. When growing, the tail does not vacate, so that cell is solid.
- Death transitions to GameOver.

### 4.8 Wrap vs. wall-death mode (option)
- Config flag `wrapWalls : bool` (default **false** = wall death).
- When `wrapWalls = true`: instead of dying at a boundary, the head wraps to the
  opposite edge — `col = (col + 32) % 32`, `row = (row + 18) % 18`. Self-collision
  still kills. This is selectable from the Title screen.

## 5. Entities / Game Objects

### 5.1 Snake
- Properties: ordered collection of cells (head at front), current heading, pending
  growth, direction queue.
- Initial state: length **3**, occupying cells `(16,9)`, `(15,9)`, `(14,9)` with head at
  `(16,9)`, heading **Right**, placed near board center (col 16 of 32, row 9 of 18).
- Rendered as 40×40 cells inset by a 2 px gap (see §8).
- Created at run start; destroyed/reset on Restart.

```fsharp
type Cell = { Col: int; Row: int }

type Direction = Up | Down | Left | Right

type Snake =
    { Body: Cell list          // head is List.head; tail is List.last
      Heading: Direction
      PendingGrowth: int        // cells still owed from recent food
      TurnQueue: Direction list } // capacity 2, FIFO
```
> Implementation note: a `System.Collections.Generic.Deque`/two-stack structure or an
> `ImmutableQueue` is preferable to `list` for O(1) tail removal at long lengths, but
> the `Body: Cell list` sketch above is the canonical conceptual model. Maintain a
> parallel `HashSet<Cell>` of occupied cells for O(1) collision and food-spawn checks.

### 5.2 Food
- Single pellet, one cell, no behavior (static).
- Created on run start and immediately after each eat; destroyed when eaten.

```fsharp
type Food = { Pos: Cell }
```

## 6. World / Levels / Progression
- Single static playfield, **1280×720** logical px, **32×18** grid. No camera, no scroll.
- There are no discrete levels; progression is the continuous speed ramp (§4.6) and the
  emergent difficulty of a longer body.
- Difficulty ramp summary: length grows +1 per pellet; step interval shrinks 0.006 s per
  pellet down to a 0.06 s floor at 20 pellets. After the floor, difficulty rises only
  through reduced free space.
- A thin border frame is drawn just inside the playfield edges as a visual wall cue
  (purely cosmetic; collision uses grid bounds).

## 7. State Model (Elmish/MVU)

### Model
```fsharp
type Screen = Title | Playing | Paused | GameOver

type Config =
    { WrapWalls: bool
      BaseStepSeconds: float
      MinStepSeconds: float
      StepDecrement: float
      GrowthPerFood: int }

type Model =
    { Screen: Screen
      Snake: Snake
      Food: Food
      Cols: int                 // 32
      Rows: int                 // 18
      FoodEaten: int            // pellets consumed this run
      Score: int
      HighScore: int
      StepSeconds: float        // current step interval
      StepAccumulator: float    // seconds banked toward next step
      Config: Config
      Rng: Rng }                // FS.GG.Game.Core — a VALUE, so the Model stays one (§13)
```

### Msg
```fsharp
type Msg =
    | StartGame
    | TurnRequested of Direction   // from key-down
    | TogglePause
    | QuitToTitle
    | ToggleWrapMode               // Title screen option
    | Tick of float                // dt in seconds, every render frame
    | Restart
```

### update — key transitions
- `StartGame` / `Restart`: build a fresh `Model` — center snake (len 3, heading Right),
  `FoodEaten = 0`, `Score = 0`, `StepSeconds = BaseStepSeconds`, accumulator 0, spawn
  first food in a non-occupied cell, `Screen = Playing`. Preserve `HighScore` and `Config`.
- `TurnRequested d`: only while `Playing`. Enqueue `d` into `Snake.TurnQueue` if queue not
  full **and** `d` is not the 180° opposite of the most-recent committed/queued direction.
- `TogglePause`: `Playing ↔ Paused`. No simulation while `Paused`.
- `ToggleWrapMode`: only on `Title`; flips `Config.WrapWalls`.
- `QuitToTitle`: `Screen = Title`.
- `Tick dt`: only while `Playing`. Add `dt` to `StepAccumulator`; while
  `StepAccumulator >= StepSeconds`, subtract `StepSeconds` and run **one** `step`. Cap
  catch-up at a few steps per frame (§13) to avoid spiral-of-death.
- `step` (internal): dequeue one turn → compute next head cell (apply wrap if configured)
  → if wall/self collision then `Screen = GameOver`, update `HighScore`; else prepend head;
  if next cell == food then `Score += 10`, `FoodEaten += 1`, recompute `StepSeconds`,
  `PendingGrowth += GrowthPerFood`, respawn food (win check if board full); if
  `PendingGrowth > 0` then decrement it and keep tail, else drop tail.

### view
Pure projection of `Model` → draw commands. Renders the grid frame, the food cell, each
snake body cell, and the HUD/overlay appropriate to `Screen`. No mutation, no timing
logic; Skia executes the draw list.

### Subscriptions
- A 60 FPS frame timer dispatching `Tick dt` with `dt` in seconds (target ~0.0167).
- Keyboard subscription mapping key-down events to `TurnRequested` / `TogglePause` /
  `StartGame` / `Restart` / `QuitToTitle` / `ToggleWrapMode`.

## 8. Rendering (Skia 2D)
Coordinate system matches the logical 1280×720 playfield; integer cell math scaled by 40.

Draw order (back to front):
1. **Background** — fill `#0E1116` (near-black slate) over the full 1280×720.
2. **Playfield frame** — 4 px stroke rect inset 6 px from edges, color `#2A3340`.
3. **Grid (optional, subtle)** — 1 px lines at every 40 px, color `#171C24`. Can be
   toggled off; off by default for a cleaner look.
4. **Food** — filled rounded rect (corner radius 8) inside the cell, inset 6 px, color
   `#E5484D` (red), with a 2 px lighter highlight `#FF6369`.
5. **Snake body** — each cell a rounded rect (corner radius 6) inset 2 px (so a 36×36
   visible block with a 4 px gap forming segment seams). Body color `#30A46C` (green);
   **head** drawn brighter `#4CC38A` with two small 4 px eye dots oriented toward the
   heading.
6. **HUD** — score text top-left (see §9).
7. **Overlays** — Title / Pause / GameOver dim the field with a `#000000` at 55% alpha
   panel, then center text.

- Font: a clean sans (e.g. "Inter"/system default), score at 28 px, titles at 56 px,
  prompts at 24 px, all `#E6EDF3`.
- **Redraw strategy:** full redraw each frame (the scene is tiny — ≤576 rects — so partial
  invalidation is unnecessary). Clear to background, then paint the draw list.
- No camera, no transforms beyond the static logical→pixel scale.

## 9. UI / HUD / Screens

**Title screen**
- Centered title "SNAKE" at y≈260.
- Prompt "Press Enter to Start" at y≈400.
- Mode line: "Walls: [Death] / [Wrap] — press M to toggle" at y≈460 reflecting `WrapWalls`.
- High score shown bottom-center: "Best: NNNN".

**Play HUD**
- Top-left: "Score: NNNN" at (24, 20).
- Top-right: "Best: NNNN" right-aligned at (1256, 20).
- Optional small "x.xx s/step" speed readout bottom-left (debug; off by default).

**Pause overlay**
- Dim panel + centered "PAUSED" and "Press P to Resume / Esc to Quit".

**Game Over overlay**
- Dim panel + centered "GAME OVER", "Score: NNNN", "Best: NNNN", and "Press Enter to
  Restart". If a new best was set, show "NEW BEST!" above the score in `#E5484D`.

### 9.1 Menu system (detailed)
A single **menu stack** drives every non-play screen (Title, Settings, Stats, Pause, Game
Over). Each menu is a vertical list of rows with a cursor, so one small update handler
serves them all and navigation is identical everywhere.

**Menu tree**
```
Title ─┬─ Play ──────────── start a run (current Walls mode & Difficulty)
       ├─ Stats ─────────── Stats & Charts screen (§9.2)
       ├─ Settings ──────┬─ Difficulty     ◄ Casual · Classic · Frenzy ►
       │                 ├─ Master volume  ◄ 0 – 100 ►
       │                 ├─ Sound          ◄ On · Off ►
       │                 ├─ Window scale   ◄ 1× · 2× · Fit ►
       │                 ├─ Grid overlay   ◄ On · Off ►
       │                 └─ Back
       └─ Quit

Pause ─┬─ Resume
       ├─ Restart run
       ├─ Settings ──────── (same submenu; returns to Pause)
       └─ Quit to Title

Game Over ─┬─ Retry ──────── new run, same Walls mode & Difficulty (single life)
           ├─ View Stats ── Stats & Charts (§9.2)
           └─ Title
```

**Navigation model**
- `MenuCursor: int` on the active menu; `↑`/`W` decrement, `↓`/`S` increment, both **wrap**.
- `Enter`/`Space` activates the current row; `Esc`/`Backspace` pops the stack (**Back**).
- **Cycler/slider rows** (Difficulty, Master volume, Sound, Window scale, Grid overlay):
  `←`/`→` change the value in place; the row shows a right-aligned `◄ value ►` widget.
- Rendering reuses the §9 overlay style: the selected row is inverted (bright box, dark
  text); non-selected rows are `#E6EDF3` on the dimmed field at 24 px.

**Msg additions** (extend §7 Msg):
```fsharp
    | MenuUp | MenuDown              // move cursor (wraps)
    | MenuAdjust of dir:int          // -1 / +1 (←/→) on a cycler/slider row
    | MenuActivate                   // Enter/Space on the current row
    | MenuBack                       // Esc — pop the menu stack
    | OpenStats | CloseStats         // enter / leave the Stats screen (§9.2)
```

Settings apply live and persist to local config (§13): **Difficulty** selects the §12
preset (Casual = no accel `stepDecrement 0`, Classic = defaults, Frenzy = faster caps —
lower `baseStepSeconds`/`minStepSeconds`, cf. §15 stretch 6); **Master volume**/**Sound**
route to `Audio.setMasterVolume` (§10, clamped `[0,1]`); **Grid overlay** toggles the §8
optional grid layer. The Walls **Death**/**Wrap** mode stays a Title-screen toggle (`M`,
§9) and continues to key the per-mode save file (§13).

### 9.2 Stats & charts screen
The Stats screen visualizes **the last run** and **lifetime** play. It reads a `Stats`
snapshot (never live simulation), so it is a pure, deterministic render reachable from
Title, Game Over, and Pause. Chart-design choices below follow the project dataviz
conventions (form-first, validated colorblind-safe categorical palette, single axis,
identity by entity).

**Tracked per run** — `RunStats`, accumulated in `Tick`/`step`, snapshotted on `GameOver`:

| Field | Type | Updated |
|-------|------|---------|
| `applesEaten` | `int` | +1 on each eat (§4.4) |
| `maxLength` | `int` | max snake body length reached |
| `survivalSeconds` | `float` | accumulated live-play time |
| `deathCause` | `Wall \| Self` | set once, on the fatal `step` (§4.7) |
| `turnsMade` | `int` | +1 per committed (dequeued) turn (§4.3) |
| `topSpeed` | `float` (tiles/s) | max `1.0 / StepSeconds` observed |
| `boardCoveragePercent` | `float` | `maxLength / 576 · 100` at run end |

**Lifetime** — `LifetimeStats`, persisted (§13): `highScore`, `gamesPlayed`,
`longestSnake`, `avgLength`, `deathsByWall`, `deathsBySelf`.

**Layout** (logical 1280×720): a KPI tile row across the top, two charts below.

```
┌──────────────────────────── STATS ────────────────────────────┐
│  ┌HIGH SCORE┐ ┌ LONGEST ┐ ┌  GAMES  ┐ ┌ AVG LENGTH┐           │  ← KPI stat tiles
│  │   340    │ │ 47 cells│ │   128   │ │ 18.6 cells│           │
│  └──────────┘ └─────────┘ └─────────┘ └───────────┘           │
│                                                                │
│  Final-score distribution           Snake length — last run    │
│         ▇▇                       47 ┤             ╭────         │
│  ▇▇     ▇▇                          │        ╭───╯             │
│  ▇▇  ▇▇ ▇▇  ▇▇                      │    ╭──╯                  │
│  ▇▇  ▇▇ ▇▇  ▇▇  ▇▇                3 ┼───────────────► seconds  │
│  0-4 5-9 ·· ··  35+ (apples)        0                          │
└────────────────────────────────────────────────────────────────┘
     ↑/↓ scope:  ▸ This Run · Lifetime            ESC — Back
```

**Charts** (rendered in Skia with the same draw-list discipline as §8):

1. **Final-score distribution** — *form: a distribution → bars.* x = a finished game's
   apples-eaten score bucketed (`0–4, 5–9, 10–19, 20–34, 35+`), y = number of games that
   landed in each bucket, drawn from lifetime finished-run scores. **Single series**, so
   one hue and no legend. Bars are 4 px-rounded at the data end with a 2 px surface gap
   between them. Fill `#2a78d6` (light) / `#3987e5` (dark) — validated categorical slot 1.
2. **Snake length — last run** — *form: change over an ordered index → line.* x = elapsed
   seconds, y = snake length, for the most recent run; a **single series** climbing then
   plateauing as the board fills, so one hue and no legend. Line `#2a78d6` (light) /
   `#3987e5` (dark); 2 px stroke, an ≥ 8 px marker at the final (death) point, recessive
   1 px gridlines in `#3C3C3C`.

Conventions honored: **color follows the entity** (both charts sit on categorical slot 1
and are never repainted by the scope toggle); **one axis only** (no dual-scale); chart
**text uses ink tokens** (`#FFFFFF` primary / `#C3C2B7` muted), never the series hue;
layout is **fixed and deterministic**, so a fixed-seed run (§13) renders byte-identical for
snapshot tests. The `↑/↓` **scope** toggle swaps the KPI tiles and the distribution's data
source This-Run ↔ Lifetime without changing colors (the length line always shows the last
run).

**Model/Msg hooks:** add `Run: RunStats` and `Lifetime: LifetimeStats` to the §7 Model;
accumulate them in the `Tick`/`step` cases (bump `applesEaten` on an eat, `turnsMade` on
each dequeued turn, track `maxLength` and `topSpeed = 1/StepSeconds`, bank `survivalSeconds`
from live time). On the fatal `step`, record `deathCause` (Wall vs. Self, §4.7), compute
`boardCoveragePercent`, fold `RunStats` into `Lifetime` (update `highScore`, `gamesPlayed`,
`longestSnake`, recompute `avgLength`, bump `deathsByWall`/`deathsBySelf`) and persist
(§13, alongside the per-mode high score). Add a `Stats` value to the §7 `Screen` type;
`OpenStats`/`CloseStats` switch into and out of it and the render is a no-op on the sim.

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
| Eat pellet (§4.4) | `Audio.playSfx (SoundId "eat") 0.8` | `eat` | short blip (rising) |
| Turn committed (§4.3) | `Audio.playSfx (SoundId "turn") 0.3` | `turn` | soft tick (very quiet; optional) |
| Death / collision (§4.7) | `Audio.playSfx (SoundId "death") 0.9` | `death` | descending buzz/thud |
| New high score (§11) | `Audio.playSfx (SoundId "high-score") 0.9` | `high-score` | 3-note jingle on Game Over |
| Menu confirm — Start/Restart (§9) | `Audio.playSfx (SoundId "menu-confirm") 0.7` | `menu-confirm` | UI click |

Background music is a low ambient loop during Play, requested via
`Audio.playMusic (TrackId "ambient") true` (loop `true`) when a run starts and
`Audio.stopMusic` when the run ends or returns to a menu — silence on menus. A mute/settings
toggle maps to `Audio.setMasterVolume` (e.g. `Audio.setMasterVolume 0.0` to mute). **Testing:**
collect the frame's `AudioEffect`s, `Audio.interpret` them, and assert the
`AudioEvidence.Requested` sequence for representative events (e.g. eating a pellet requests
exactly `PlaySfx (SoundId "eat", _)`).

## 11. Win / Loss / Scoring
- **Scoring:** +10 points per pellet eaten. `Score = FoodEaten * 10`. No combo/time bonus
  in v1.
- **High score:** `max(Score, HighScore)`, persisted (§13).
- **Loss:** wall collision (Wall mode) or self-collision (both modes). Single life, no
  continues — death ends the run immediately.
- **Win:** the snake fills all **576** cells (no free cell remains for food). This is a
  perfect game; show a distinct "PERFECT!" message on the Game Over screen and treat the
  run as a win. (Practically rare, but must be handled — see §14 scenario 12.)

## 12. Difficulty & Balancing
| Parameter | Default | Range | Effect |
|---|---|---|---|
| `cols` | 32 | 16–48 | Board width in cells |
| `rows` | 18 | 12–32 | Board height in cells |
| `baseStepSeconds` | 0.18 | 0.10–0.30 | Starting step interval (lower = faster) |
| `minStepSeconds` | 0.06 | 0.04–0.12 | Fastest step interval (speed cap) |
| `stepDecrement` | 0.006 | 0.000–0.02 | Speed-up per pellet (0 disables ramp) |
| `growthPerFood` | 1 | 1–5 | Cells gained per pellet |
| `wrapWalls` | false | bool | Wrap edges vs. wall death |
| `startLength` | 3 | 1–6 | Initial snake length |
| `pointsPerFood` | 10 | 1–100 | Score per pellet |

All live in `Config` so balance is data-driven and testable without code changes.

## 13. Technical Notes
- **Timestep:** fixed-step simulation, drained by **`FixedStep.drainWith`** — do not hand-roll the
  accumulator. `FixedStep.drainWith (4.0 * StepSeconds) StepSeconds dt StepAccumulator` returns
  `struct (steps, acc')`: how many whole `step`s this frame owes, and the remainder to bank. The
  first argument is the spiral-of-death cap, and `4.0 * StepSeconds` is exactly the "max 4 steps per
  frame" rule — expressed as the frame-time budget the function actually takes. Pass the *current*
  `StepSeconds`, so the speed-up falls out of the same call. (`FixedStep.drain` is the same thing at
  the default 0.25 s cap.)
- **Performance budget:** ≤576 cells, one food, one HUD pass — trivially within the
  16.7 ms/60 FPS budget. Full-clear redraw is fine.
- **Determinism / RNG:** food placement uses `Model.Rng` — `FS.GG.Game.Core`'s **`Rng`** (splitmix64),
  seeded with `Rng.ofSeed 12345UL`; draw the free cell with `Rng.nextInt`. It is a **value**, not a `System.Random`: every draw returns `struct (x, rng')` and you write
  `rng'` back to the `Model`, so the `Model` stays a value you can snapshot, replay and compare.
  A `System.Random` in the `Model` is a mutable object *shared* by every copy of it, which
  silently breaks the reproducibility this bullet promises.
- **Collision/spawn efficiency:** maintain a `HashSet<Cell>` mirror of the body for O(1)
  membership; spawn food by sampling the free-cell set (or rejection-sample then fall
  back to enumerating free cells when the board is dense).
- **Persistence:** high score saved to local storage / a small JSON file
  (`snake_highscore.json`) keyed per `wrapWalls` mode; loaded on Title.
- **Edge cases:** (a) eating the last free cell = win, not a normal spawn; (b) head moving
  into the vacating tail cell is legal when not growing; (c) buffered opposite-direction
  inputs must never reverse the snake; (d) pausing mid-accumulation must not lose or
  fast-forward banked time on resume (freeze the accumulator); (e) very small boards must
  still place a valid start snake and food.

## 14. Acceptance Criteria (test scenarios)

1. **Basic step (core mechanic).**
   Given a new game with the snake heading Right at head `(16,9)`,
   When one step occurs,
   Then the head is at `(17,9)`, the snake length is unchanged (3), and the old tail cell
   `(14,9)` is now empty.

2. **Eat and grow + score.**
   Given the snake head at `(16,9)` heading Right and food at `(17,9)`,
   When one step occurs,
   Then the head is at `(17,9)`, snake length becomes 4, `Score` increases by 10,
   `FoodEaten` becomes 1, and a new food appears in a cell not occupied by the snake.

3. **Speed accelerates with length.**
   Given `baseStepSeconds = 0.18`, `stepDecrement = 0.006`, after eating 5 pellets,
   When `StepSeconds` is read,
   Then it equals `0.18 - 5*0.006 = 0.15` s (and never drops below `minStepSeconds = 0.06`).

4. **180° reversal is blocked (input scenario).**
   Given the snake heading Right,
   When `TurnRequested Left` is dispatched and then a step occurs,
   Then the heading remains Right (the reversing turn was rejected, not enqueued) and the
   head advances to the next Right cell.

5. **Direction queue buffers two turns.**
   Given the snake heading Right and the tick interval not yet elapsed,
   When `TurnRequested Up` then `TurnRequested Right` are dispatched before the next step,
   Then the next step turns the head Up and the subsequent step turns it Right (both
   buffered turns are honored in order).

6. **Wall death (default mode).**
   Given `wrapWalls = false` and the head at `(31,9)` heading Right,
   When one step occurs,
   Then the head would exit the grid, the snake dies, and `Screen` becomes `GameOver`.

7. **Wrap mode survives the edge.**
   Given `wrapWalls = true` and the head at `(31,9)` heading Right,
   When one step occurs,
   Then the head wraps to `(0,9)` and the snake is still alive (`Screen = Playing`).

8. **Self-collision death (and tail-follow exception).**
   Given a snake long enough that the cell directly ahead of the head is part of its body
   (and that cell is NOT the vacating tail),
   When one step occurs,
   Then the snake dies and `Screen` becomes `GameOver`.
   And given the cell ahead is exactly the current tail and the snake is not growing,
   When one step occurs,
   Then the move is legal and the snake survives.

9. **High score persists across runs.**
   Given a run ended with `Score = 120` and previous `HighScore = 90`,
   When the GameOver screen is shown and a new game is started,
   Then `HighScore = 120` is displayed and retained.

10. **Pause freezes simulation.**
    Given `Screen = Playing` with a partially filled `StepAccumulator`,
    When `TogglePause` is dispatched and several `Tick` messages arrive,
    Then no `step` occurs and the snake/food are unchanged until `TogglePause` resumes play.

11. **Food never spawns on the snake.**
    Given any game state with N body cells,
    When food is (re)spawned,
    Then `Food.Pos` is not a member of the snake body.

12. **Perfect-game win.**
    Given the snake occupies 575 of 576 cells with food in the last free cell,
    When the head eats that pellet,
    Then there are no free cells, the game is won, and a "PERFECT!" win state is shown.

## 15. Stretch Goals
1. **Obstacles / maze walls** — static blocker cells per level layout.
2. **Multiple food types** — golden pellet (worth 50, +2 growth) with a timeout.
3. **Speed/portal power-ups** — temporary slow-mo or wall-phase pickup.
4. **Two-player co-op or versus** — second snake, shared board, collision rules.
5. **Daily seed challenge** — fixed RNG seed leaderboard for the day.
6. **Difficulty presets** — Casual (no accel), Classic (default), Frenzy (low caps).
7. **Animated interpolation** — smooth sub-cell head movement between steps for visual
   polish (purely cosmetic; sim stays grid-discrete).
8. **Replay/ghost** — record input+seed to replay a run.

## 16. Milestone Roadmap

Implementation is sequenced into milestones; each item is a colored checkbox
tracking its status. Items reference the section that specifies them.

**Legend:** 🟥 Not started · 🟨 In progress · 🟩 Done · ⬜ Deferred (post-v1)

_All items start 🟥 (spec status). Flip an item to 🟨 when work begins and 🟩 once
its acceptance test(s) pass (§14)._

### M0 — Scaffold & fixed-step loop
- 🟥 Project scaffold: `Model`/`Msg`/`update`/`view` skeleton (§7)
- 🟥 Fixed-step stepping via `FixedStep.drainWith`, banked remainder (§7, §13)
- 🟥 `Rng` value seeded with `Rng.ofSeed 12345UL`, threaded through `Model` (§13)
- 🟥 32×18 grid, 40 px cells, `(col,row)`→pixel-rect transform (§4.1, §8)

### M1 — Snake stepping & turning
- 🟥 Discrete one-cell step: prepend head, drop tail unless growing (§4.2) — AC #1
- 🟥 Four-heading unit vectors, initial heading Right (§4.2)
- 🟥 Direction queue (capacity 2, FIFO) with 180° reversal guard (§4.3) — AC #4
- 🟥 Buffer two turns, dequeue one per step in order (§4.3) — AC #5

### M2 — Food, growth & scoring
- 🟥 Single pellet spawned in a uniformly random unoccupied cell (§4.4) — AC #11
- 🟥 Eat → `PendingGrowth += 1`, tail retained so body +1 (§4.5) — AC #2
- 🟥 `+10` score per pellet, respawn food, board-full win check (§4.4, §11)
- 🟥 Speed ramp: recompute `StepSeconds` down to `minStepSeconds` floor (§4.6) — AC #3

### M3 — Collisions & death
- 🟥 Wall death when next head cell exits `[0,31]×[0,17]` (§4.7) — AC #6
- 🟥 Self-collision death with vacating-tail exception (§4.7) — AC #8
- 🟥 `wrapWalls` mode: wrap edges instead of dying (§4.8) — AC #7
- 🟥 `HashSet<Cell>` body mirror for O(1) collision/spawn checks (§13)

### M4 — Match flow & screens
- 🟥 Title / Playing / Paused / GameOver screen states (§7, §9)
- 🟥 Pause freezes sim and banked accumulator, resumes exact state (§7) — AC #10
- 🟥 Perfect-game win when snake fills all 576 cells, "PERFECT!" (§11) — AC #12
- 🟥 High score `max(Score, HighScore)`, persisted per `wrapWalls` mode (§11, §13) — AC #9

### M5 — Rendering (Skia)
- 🟥 Draw order: background, frame, grid, food, snake body, HUD, overlays (§8)
- 🟥 Head drawn brighter with eye dots oriented toward heading (§8)
- 🟥 Title / Pause / GameOver dim panels + centered text (§8, §9)

### M6 — Menus & settings
- 🟥 Menu stack, cursor wrap, cycler/slider `◄ value ►` rows (§9.1)
- 🟥 Difficulty / volume / grid-overlay settings apply live + persist (§9.1, §12, §13)

### M7 — Stats & charts
- 🟥 `RunStats`/`LifetimeStats` accumulation + persist (§9.2, §13)
- 🟥 Final-score distribution bars + last-run snake-length line chart (§9.2)

### M8 — Audio
- 🟥 `AudioEffect` cues: eat, turn, death, high-score, menu-confirm (§10)
- 🟥 Ambient music loop + `Audio.setMasterVolume` clamp `[0,1]` (§10)

### M9 — Acceptance & determinism
- 🟥 All 12 acceptance scenarios green (§14)
- 🟥 Seeded `Rng` value makes food placement + replay reproducible (§13)

### Stretch — deferred (post-v1)
- ⬜ Obstacles / maze walls (§15.1)
- ⬜ Multiple food types — golden pellet (§15.2)
- ⬜ Speed / portal power-ups (§15.3)
- ⬜ Two-player co-op or versus (§15.4)
- ⬜ Daily seed challenge leaderboard (§15.5)
- ⬜ Difficulty presets — Casual / Classic / Frenzy (§15.6)
- ⬜ Animated sub-cell interpolation (§15.7)
- ⬜ Replay / ghost from input + seed (§15.8)
