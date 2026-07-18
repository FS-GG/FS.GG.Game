---
title: "Tetris"
slug: tetris
category: games
complexity: simple
genre: "Falling-block puzzle"
target_session_minutes: 10
stack: { rendering: "FS.GG.Rendering (Skia/OpenGL)", framework: "FS.GG.Game.Core (FixedStep for gravity/DAS; Rng for the 7-bag)", arch: "Elmish/MVU", lang: "F#" }
status: spec
---

# Tetris

## 1. Overview
Tetris is a falling-block puzzle. Seven distinct four-cell pieces (tetrominoes) descend
one at a time into a narrow 10-wide, 20-tall well. The player slides and rotates each
piece as it falls, packing them into a flat, gapless stack. Completely filling a
horizontal row clears it and awards points; the rows above collapse down. The fantasy is
pure spatial mastery under escalating time pressure: the well never empties, gravity
accelerates with every level, and a single misplaced piece can cascade into a topped-out
board. The core verb is **place** — every second is a small optimization problem solved
with rotate, shift, and drop. It is fun because the rules are trivial to learn, the
inputs are instantaneous, and the difficulty curve is self-inflicted: you lose because
the board you built failed, not because the game cheated.

## 2. Core Game Loop
**Moment-to-moment loop:** spawn piece at top → fall under gravity → player shifts /
rotates / soft-drops / hard-drops → piece lands and locks → check & clear full lines →
score & maybe level-up → spawn next piece → repeat.

**Session-level loop:** title screen → press Start → play (loop above) until a new piece
cannot spawn (top-out) → game-over screen showing final score / lines / level → press
Restart → new game with reset state and a freshly shuffled bag.

A single game is one continuous descent with no levels to "complete"; the session ends
only on loss. Target session length ~10 minutes for a competent player.

**Frame resolution & piece handoff.** A piece never spawns and locks on the same frame
unless it is hard-dropped: spawn writes the new active piece, and the earliest it can lock is
the following `Tick`. There is **no entry delay (ARE)** in v1 — the next piece becomes active
the instant its predecessor's cells are committed, so a fast player can begin steering it on
the very next frame. Held state **survives the handoff**: if Left (or Right) is still down when
a piece locks, its `DasTimer` charge carries over, so the next piece spawns under an
already-charged direction and auto-shifts at `ARR` immediately rather than re-waiting the full
`DAS` — this "DAS charge" is what lets skilled play slam pieces to a wall with no per-piece
delay. A held soft drop (↓) likewise persists across the handoff, so a freshly spawned piece
falls at soft-drop speed from frame one until ↓ releases. Only `HoldUsed` resets on a genuine
spawn from the queue (§4.8), never on a swap.

**Input-vs-gravity ordering within a frame.** Input messages (`MoveLeftDown`, `RotateCW`, …)
are applied in the order the player pressed them; the frame's `Tick` — which runs the
`DasTimer`, gravity, and lock logic (§7.3) — is dispatched **after** that frame's buffered
input, so a piece is always steered before it falls that frame. This keeps a last-instant
slide-under-an-overhang honest: the shift lands before gravity re-tests collision. A hard drop
short-circuits the rest of the frame for that piece — once it teleports and locks, no further
gravity, shift, or rotate is processed until the next piece spawns.

## 3. Controls & Input
Keyboard is primary. Input model mixes **edge-triggered** (fire once on key-down) and
**held with auto-repeat** (DAS/ARR) actions.

| Input | Action | Model |
|---|---|---|
| ← Left Arrow | Move piece one cell left | Edge-trigger on press; then **DAS** auto-repeat while held |
| → Right Arrow | Move piece one cell right | Edge-trigger on press; then **DAS** auto-repeat while held |
| ↓ Down Arrow | Soft drop (accelerated fall) | Held: gravity multiplied while down is held |
| ↑ Up Arrow / X | Rotate clockwise (CW) | Edge-trigger (no auto-repeat) |
| Z / Ctrl | Rotate counter-clockwise (CCW) | Edge-trigger |
| Space | Hard drop (instant drop + lock) | Edge-trigger |
| C / Shift | Hold piece (swap with hold slot) | Edge-trigger, once per spawn |
| P / Esc | Pause / resume | Edge-trigger |
| Enter | Start / Restart (on title & game-over) | Edge-trigger |

**DAS (Delayed Auto Shift):** after a directional key is held for **DAS = 170 ms**, the
piece begins shifting repeatedly at **ARR (Auto Repeat Rate) = 50 ms** per cell until the
key releases or hits a wall. The very first shift happens immediately on key-down.

**Soft drop:** while ↓ is held, gravity interval is divided by the soft-drop factor
(20×, see §4.3); releasing returns to normal gravity. Soft drop never skips the lock
delay.

## 4. Mechanics (detailed)

### 4.1 The Well (playfield grid)
The board is a grid **10 columns × 20 visible rows**, plus **2 hidden buffer rows above
the top** where pieces spawn. Total logical grid is 10×22, with rows 0–19
visible. Each cell is either `Empty` or `Filled of color`. Coordinates: column `x`
increases rightward 0→9; row `y` increases **downward**, with **y=0 the top visible row
and y=19 the floor (bottom visible) row**. The two hidden buffer rows (where pieces spawn)
are not drawn. Gravity moves pieces toward increasing `y`.

### 4.2 Tetrominoes
Exactly 7 pieces, each 4 cells, identified by letter with a fixed Tetris-guideline color:

| Piece | Color (hex) | Spawn shape (cells, relative) |
|---|---|---|
| I | Cyan `#00F0F0` | horizontal bar, 4 wide |
| O | Yellow `#F0F000` | 2×2 square |
| T | Purple `#A000F0` | 3-wide row + 1 above center |
| S | Green `#00F000` | top two cells right-shifted |
| Z | Red `#F00000` | top two cells left-shifted |
| J | Blue `#0000F0` | 3-wide row + 1 above left |
| L | Orange `#F0A000` | 3-wide row + 1 above right |

Spawn placement: pieces spawn horizontally, centered in columns 3–6, with the piece's
bounding box straddling the hidden buffer / top visible row. The **I** and **O** spawn in
columns 3–6 / 4–5 respectively per guideline. If any spawn cell overlaps a filled cell,
the game tops out (§11).

### 4.3 Gravity & dropping
- **Gravity** moves the active piece down one cell every **gravity interval** (a function
  of level, §6.2). Each step is collision-checked; if the cell below is blocked the piece
  does not move and the **lock timer** runs.
- **Soft drop:** while ↓ held, effective gravity interval = `gravityInterval / 20`
  (clamped to a minimum of one step per frame). Awards 1 point per cell soft-dropped.
- **Hard drop:** the piece teleports to its lowest legal position instantly and **locks
  immediately** (no lock delay). Awards 2 points per cell traveled.

### 4.4 Rotation (SRS) & wall kicks
Rotation uses the **Super Rotation System (SRS)**. Each piece has 4 rotation states:
`0` (spawn), `R` (CW from spawn), `2` (180°), `L` (CCW from spawn). Rotation pivots around
the piece's SRS center.
- **O** never effectively rotates (no offset).
- On a rotation attempt, the game tests the target orientation at 5 candidate offsets (the
  SRS **kick table**) in order; the first offset with no collision is applied. If all 5
  fail, the rotation is rejected and the piece is unchanged.
- **J, L, S, T, Z** share the standard JLSTZ kick table; **I** uses its own I kick table.
  Kick offsets are the guideline values, e.g. for JLSTZ `0→R`: `(0,0) (-1,0) (-1,+1) (0,-2) (-1,-2)`
  (x right-positive, y up-positive in kick-table convention; convert to the y-down grid by
  negating the y component).

A **simpler fallback** is acceptable for v1 if SRS is too costly: rotate around the
bounding-box center and try at most 3 kicks — `(0,0)`, `(-1,0)`, `(+1,0)` — rejecting the
rotation if none fit. The acceptance tests in §14 are written against the simpler model and
hold for SRS too.

### 4.5 Lock delay
When a piece rests on the stack (cell below blocked), a **lock timer = 500 ms** starts.
- If the player moves or rotates the piece into a position where it can still fall, the
  timer resets — but only up to **15 move/rotate resets** per piece; after 15 resets the
  next time it rests it locks immediately. This prevents infinite stalling.
- When the timer expires (or hard drop fires), the piece's cells are written into the grid
  permanently and line-clear evaluation runs.

### 4.6 Line clears
After a lock, scan all rows. Any row whose 10 cells are all `Filled` is **cleared**:
removed, with every row above it shifted down by one, and empty rows inserted at the top.
Clearing 1/2/3/4 rows simultaneously scores differently (§11). Four at once is a "Tetris".

### 4.7 7-bag randomizer
The piece sequence uses the **7-bag** system: take all 7 piece types, shuffle them
(Fisher–Yates with the seeded RNG), and deal them one at a time. When the bag empties,
refill and reshuffle. This guarantees every 7 spawns contain each piece exactly once — no
floods, no droughts longer than 12 pieces.

### 4.8 Hold & next queue
- **Next queue:** the upcoming pieces are previewed; show the next **5** pieces (drawn
  from the current + refilled bag).
- **Hold:** pressing Hold moves the active piece into the hold slot. If the slot was empty,
  the next piece spawns; if occupied, the held and active pieces swap and the swapped-in
  piece respawns at the top. Hold may be used **once per spawned piece** (a `holdUsed` flag
  resets when a new piece spawns from the queue, not from a swap).

### 4.9 Entry timing, initial rotation & initial hold
- **No entry delay (ARE):** the next piece is active the frame its predecessor commits (§2).
  This is the single most consequential feel choice — placements chain with zero dead time,
  so throughput is bounded only by the player and by gravity, not by an animation gate.
- **Initial Rotation System (IRS):** if a rotate key (`↑`/`X`/`Z`/`Ctrl`) is held at the
  instant a piece spawns, the piece spawns pre-rotated into that orientation. The rotation is
  applied on the spawn frame and kick-tested at the spawn cells like any rotation (§4.4); if
  the rotated spawn collides and no kick fits, the piece spawns unrotated. IRS lets an expert
  present a piece flat against a wall one frame earlier and never counts as an in-play rotate
  (it consumes no lock reset, since the piece has not yet rested).
- **Initial Hold System (IHS):** if Hold (`C`/`Shift`) is held at the instant a piece spawns,
  the swap (§4.8) fires on the spawn frame. IHS is still bounded by one hold per spawned piece
  — the spawn-frame swap consumes that piece's hold, so `HoldUsed` is set immediately.

### 4.10 Soft drop & gravity interaction
- Soft drop and natural gravity are a **maximum, not a sum**: the effective interval is
  `max(gravityInterval / softDropFactor, one frame)` — the piece falls at whichever of the two
  is faster, clamped so it never advances more than **one cell per frame** (0.0167 s) in v1.
  There is no true multi-cell-per-frame 20G.
- At high levels the two converge: from level 19 up, natural gravity is already 0.033 s/cell,
  so `gravityInterval / 20` saturates the one-frame clamp and soft drop matches natural gravity
  in *speed*. Its only remaining effect there is the **+1 point per cell** (§11), never a
  faster fall.
- Soft-drop points accrue **per cell actually descended**, not per frame or per key-press: a
  frame that carries the piece k cells adds k points.
- Soft drop into the floor or stack does nothing once the cell below is blocked, and it never
  accelerates or skips the lock timer (§4.5, §13 edge (c)). Releasing ↓ reverts to level
  gravity on the next frame — there is no carried momentum.

### 4.11 Wall-kick tables (SRS)
The five kick offsets tested per rotation (§4.4), by piece group and transition. Convention is
guideline: first component is horizontal (right-positive), second is vertical (**up-positive**);
convert to the y-down grid by negating the second component. The first offset `(0,0)` is the
naive rotation; the rest are the kicks tried in order. 180° states (`R2`) are reached only by
two successive CW or CCW rotations (there is no direct 180 input), so no 180 kick row is needed.

**JLSTZ** (shared by J, L, S, T, Z):

| Transition | k1 | k2 | k3 | k4 | k5 |
|---|---|---|---|---|---|
| 0→R | (0,0) | (-1,0) | (-1,+1) | (0,-2) | (-1,-2) |
| R→0 | (0,0) | (+1,0) | (+1,-1) | (0,+2) | (+1,+2) |
| R→2 | (0,0) | (+1,0) | (+1,-1) | (0,+2) | (+1,+2) |
| 2→R | (0,0) | (-1,0) | (-1,+1) | (0,-2) | (-1,-2) |
| 2→L | (0,0) | (+1,0) | (+1,+1) | (0,-2) | (+1,-2) |
| L→2 | (0,0) | (-1,0) | (-1,-1) | (0,+2) | (-1,+2) |
| L→0 | (0,0) | (-1,0) | (-1,-1) | (0,+2) | (-1,+2) |
| 0→L | (0,0) | (+1,0) | (+1,+1) | (0,-2) | (+1,-2) |

**I** (its own table, wider offsets because the I pivots in a 4×4 box):

| Transition | k1 | k2 | k3 | k4 | k5 |
|---|---|---|---|---|---|
| 0→R | (0,0) | (-2,0) | (+1,0) | (-2,-1) | (+1,+2) |
| R→0 | (0,0) | (+2,0) | (-1,0) | (+2,+1) | (-1,-2) |
| R→2 | (0,0) | (-1,0) | (+2,0) | (-1,+2) | (+2,-1) |
| 2→R | (0,0) | (+1,0) | (-2,0) | (+1,-2) | (-2,+1) |
| 2→L | (0,0) | (+2,0) | (-1,0) | (+2,+1) | (-1,-2) |
| L→2 | (0,0) | (-2,0) | (+1,0) | (-2,-1) | (+1,+2) |
| L→0 | (0,0) | (+1,0) | (-2,0) | (+1,-2) | (-2,+1) |
| 0→L | (0,0) | (-1,0) | (+2,0) | (-1,+2) | (+2,-1) |

The vertical `(0,±2)` / `(±_,±2)` offsets are the **floor and ceiling kicks** that let a piece
rotate out of a flush-against-floor or tucked-under-overhang position; the T's `(-1,-2)` /
`(+1,-2)` offsets are precisely what enable T-spins (§4.14). **O** has no kick table — it never
effectively rotates (§4.4). If none of the five offsets clears, the rotation is rejected and
the piece is left byte-for-byte unchanged (§13 edge (a)); it is never clipped.

### 4.12 Lock-delay edge cases
- The 500 ms timer runs **only** while the cell below the piece is blocked. If a move or
  rotation opens space below, the piece returns to **Falling** and `LockTimer` clears (§7.3.4)
  — no reset is charged for merely un-resting.
- Two distinct events touch the reset budget. A **move/rotate reset** — a successful shift or
  rotation while resting — refreshes the timer to 500 ms and increments `LockResets`, capped at
  **15**. A **downward step** that carries the piece to a **strictly lower row than any it has
  yet occupied** *forgives* the budget (resets `LockResets` toward 0), so genuine descent is
  never penalized and only in-place stalling burns the cap.
- After 15 move/rotate resets with no new-lowest-row progress, the next time the piece rests it
  **locks immediately** rather than starting a fresh 500 ms (§4.5).
- Hard drop bypasses the timer and the reset counter entirely: it locks on the spawn-to-floor
  teleport regardless of `LockResets`.

### 4.13 Line-clear resolution
- Clearing uses **naive row gravity**, not sticky or cascade: cleared rows are removed and each
  surviving row falls by the **count of cleared rows beneath it**; empty rows are inserted at
  the top. The board that is scanned includes the just-locked piece's cells.
- Cleared rows **need not be contiguous**: if rows 15 and 19 both fill while row 17 is partial,
  both full rows clear in one scan and the gap rows drop past the removed rows. There is no
  post-collapse re-scan — a settle can never trigger a fresh clear.
- A multi-row clear is exactly **one event** for scoring (§11) and audio (§10): a Tetris is a
  single `tetris` cue, not four `line-clear` cues (§13 edge (e)).
- **Perfect clear (all-clear):** if the well is entirely empty after a collapse, the lock was a
  Perfect Clear. It is detectable in v1 but carries **no bonus** (bonus is stretch §15.4); it
  also feeds the combo/back-to-back tracking used by stretch scoring.

### 4.14 T-spin recognition (stretch §15.3)
Specified here for scope even though scoring is deferred. A lock is a **T-spin** when: the piece
is a **T**, the action that placed it was a **rotation** (not a shift or a drop — the piece's
last successful move before lock was a `RotateCW`/`RotateCCW`), and **≥ 3 of the 4 corners** of
the T's 3×3 bounding box are filled (by stack cells or a wall). The two corners on the side the
T points toward distinguish a **full T-spin** (both front corners filled) from a **mini**
(only one). Combined with the row count this yields the classic tiers — a kicked-in placement
that uses a `(±1,-2)` offset (§4.11) is the canonical T-spin trigger:

| Placement | Rows cleared | Base points |
|---|---|---|
| T-spin (no clear) | 0 | 400 |
| T-spin Single (TSS) | 1 | 800 |
| T-spin Double (TSD) | 2 | 1200 |
| T-spin Triple (TST) | 3 | 1600 |
| Mini T-spin | 0–1 | 100–200 |

All multiplied by `level + 1` like §11 clears; the `tSpins` stat (§9.2) increments on any
recognized T-spin. In v1 (no stretch) a T that clears lines simply scores by §11 row count.

## 5. Entities / Game Objects

### 5.1 Cell
The atomic grid unit. A cell is `Empty` or `Filled of PieceColor`. No behavior; it is data
read by the renderer and the line-clear scan.

### 5.2 Active Piece
The single falling tetromino. Properties: piece kind (`I O T S Z J L`), an `(x, y)` grid
origin, a rotation state (`0 R 2 L`), and a 0–15 lock-reset counter. Behavior is a small
state machine: **Falling** (gravity steps, accepts input) → **Resting** (lock timer
running, still accepts input that may return it to Falling) → **Locked** (cells committed,
piece destroyed, next spawns). Created by spawn (from queue or hold swap); destroyed on
lock.

```fsharp
type PieceKind = I | O | T | S | Z | J | L
type Rotation  = R0 | R1 | R2 | R3            // 0, R(90 CW), 2(180), L(270)

type Piece =
    { Kind     : PieceKind
      Pos      : int * int                    // grid origin (col x, row y)
      Rotation : Rotation
      LockResets : int }                       // 0..15

// The occupied cells are DERIVED from Kind + Pos + Rotation, never stored:
//   cellsOf : Piece -> (int * int) list
// returns the 4 grid cells the piece covers at its current pos + rotation.
```

### 5.3 Board / Well
A 2D array of cells, `10 wide × 22 tall` (rows 0–21; 0–19 visible). Created at game start
(all `Empty`); mutated only at lock (write piece cells) and clear (shift rows). It is the
source of truth for collision and rendering.

### 5.4 Bag / Queue
Holds the remaining shuffled pieces of the current bag plus enough of the next bag to show
5 previews. `next` pops the head and refills/reshuffles when low.

### 5.5 Hold slot
Optional single `PieceKind` plus the `holdUsed` flag.

### 5.6 Ghost piece (derived)
Not stored and not part of the `Model` — the ghost is **derived each frame** from the active
piece, exactly like `cellsOf` (§5.2). It is the active piece projected straight down to its
hard-drop landing row (the lowest legal position, §4.3), so it always occupies the same columns
and orientation as the active piece and re-derives whenever the piece moves, rotates, or falls.
It has **no collision or scoring role** — it is a pure rendering aid (§8) that shows where a
hard drop would land, and it can be turned off via the Ghost-piece setting (§9.1, stretch §15.1).

### 5.7 Spawn origins & rotation pivots
Every piece spawns horizontally in the columns below, straddling the hidden buffer / top visible
row (§4.2). Columns are the concrete cells the piece covers at spawn (`R0`); the pivot names the
box its rotations turn inside (§4.4, §4.11):

| Piece | Bottom-row columns | Top-row columns | Rotation box / pivot |
|---|---|---|---|
| I | 3, 4, 5, 6 | — | 4×4 box, pivot between the middle two cells |
| O | 4, 5 | 4, 5 | 2×2, no effective rotation |
| T | 3, 4, 5 | 4 | 3×3, pivot on center column 4 |
| J | 3, 4, 5 | 3 | 3×3, pivot on center column 4 |
| L | 3, 4, 5 | 5 | 3×3, pivot on center column 4 |
| S | 3, 4 | 4, 5 | 3×3, pivot on center column 4 |
| Z | 4, 5 | 3, 4 | 3×3, pivot on center column 4 |

The kick tables (§4.11) are expressed relative to these pivots, so the spawn columns and the
kick offsets together fully determine every legal orientation without any per-piece special
cases beyond the JLSTZ / I / O split.

## 6. World / Levels / Progression

### 6.1 Playfield dimensions
Logical render canvas **1280×720**. The well is centered: each cell is a **32×32 px**
square, so the 10×20 visible well is **320×640 px**, drawn at origin `(480, 40)`
(centered horizontally; 40 px top margin, 40 px bottom margin). Hold panel sits left of the
well; next-queue panel and score HUD sit to the right.

### 6.2 Levels & difficulty ramp
- The game starts at **level 0** (or a chosen start level 0–19).
- **Level up:** every **10 lines cleared** total increases the level by 1 (line counter is
  cumulative; level = `totalLines / 10`, capped at the start level as a floor).
- Higher level = faster gravity. Gravity interval per level (frames at 60 FPS, classic
  guideline-style curve), converted to seconds:

| Level | Frames/cell | Seconds/cell |
|---|---|---|
| 0 | 48 | 0.800 |
| 1 | 43 | 0.717 |
| 2 | 38 | 0.633 |
| 3 | 33 | 0.550 |
| 4 | 28 | 0.467 |
| 5 | 23 | 0.383 |
| 6 | 18 | 0.300 |
| 7 | 13 | 0.217 |
| 8 | 8 | 0.133 |
| 9 | 6 | 0.100 |
| 10–12 | 5 | 0.083 |
| 13–15 | 4 | 0.067 |
| 16–18 | 3 | 0.050 |
| 19–28 | 2 | 0.033 |
| 29+ | 1 | 0.017 |

The only thing that changes over time is gravity speed (and thus required reaction time).
The board, scoring multipliers, and piece set are constant.

### 6.3 Progression rules & edge cases
- **Monotone, floored:** `level = max(startLevel, totalLines / linesPerLevel)` (integer
  division). The `startLevel` floor means the level never drops below where you began, and
  because `totalLines` only grows, the level is **monotone non-decreasing** — it can never fall.
- **Starting high defers the first level-up:** the line counter is always cumulative from 0, so
  from Hard's `startLevel = 9` you must clear **100** lines before `totalLines / 10` overtakes
  the floor and the level advances to 10. Below that threshold every clear scores at ×10
  (level 9 + 1) but the level number holds.
- **At most one level-up per lock** under defaults: a lock clears at most 4 lines, which can
  cross at most one `linesPerLevel = 10` boundary. The level-up cue (§10) fires once, and the
  new interval feeds `FixedStep.drain` (§13) from the next frame.
- **Gravity caps, score does not:** the §6.2 curve bottoms out at level 29 (one cell per frame,
  0.017 s). Past level 29 gravity no longer changes, but the level keeps incrementing and the
  `level + 1` score multiplier (§11) keeps growing without bound, so late survival is scored
  ever more richly even though it plays no faster.

### 6.4 Gravity feel across the curve
- **Levels 0–8** keep gravity at ≥ 0.133 s/cell — comfortable reaction time, the "learn the
  bag" band.
- **Levels 9–18** push under 0.1 s/cell, where placements must be planned during the fall using
  the next queue (§4.8) rather than improvised.
- **Levels 19+** fall effectively one row per frame; at this speed a piece reaches the stack in
  well under a second, so survival leans on **DAS charge** (§2), the hard drop, and pre-reading
  the queue. Because soft drop is clamped to one cell per frame (§4.10), from level 19 up it no
  longer outruns natural gravity — its value there is purely the soft-drop score.

## 7. State Model (Elmish/MVU)

### 7.1 Model
```fsharp
type Phase = Title | Playing | Paused | GameOver

type Model =
    { Phase        : Phase
      Board        : Cell[,]            // [22, 10]  (rows, cols); rows 0-19 visible
      Active       : Piece option        // None between lock and spawn
      Bag          : PieceKind list      // remaining shuffled pieces (>= 5 buffered)
      Hold         : PieceKind option
      HoldUsed     : bool
      Score        : int
      Lines        : int                 // cumulative cleared lines
      Level        : int
      StartLevel   : int
      // timers (seconds)
      GravityAcc   : float               // accumulates dt; steps when >= gravityInterval
      LockTimer    : float option        // Some t = resting, counting down from 0.5
      DasTimer     : float               // for held left/right auto-shift
      Dir          : int                 // -1 left, 0 none, +1 right (held direction)
      SoftDrop     : bool
      Rng          : Rng                 // FS.GG.Game.Core, seeded (§13)
      HighScore    : int }
```

### 7.2 Msg
```fsharp
type Msg =
    | Tick of float                       // dt in seconds, ~1/60
    | MoveLeftDown | MoveLeftUp
    | MoveRightDown | MoveRightUp
    | SoftDropDown | SoftDropUp
    | RotateCW | RotateCCW
    | HardDrop
    | HoldPiece
    | TogglePause
    | StartGame                           // from Title / GameOver
```

### 7.3 update (key transitions)
- **`StartGame`**: reset Board to empty, shuffle a fresh bag from `Rng`, spawn first piece,
  `Phase = Playing`, zero score/lines, level = StartLevel.
- **`Tick dt`** (only when `Playing`):
  1. Advance `DasTimer` if `Dir <> 0`; when it crosses DAS, shift one cell every ARR.
  2. Add `dt` to `GravityAcc`; while `GravityAcc >= interval` (interval = soft-drop-adjusted
     gravity for current level) subtract and step the piece down one cell if legal.
  3. If the piece cannot move down: if `LockTimer = None` start it at 0.5; else decrement by
     `dt`; on ≤ 0 → **lock** (write cells, clear lines, score, spawn next, reset `HoldUsed`).
  4. If a downward step succeeded while resting, clear `LockTimer` (reset, counting a
     lock-reset, capped at 15).
- **`MoveLeftDown`/`MoveRightDown`**: set `Dir`, immediately try one shift; reset DAS timer.
  **`...Up`**: if it matches current `Dir`, set `Dir = 0`.
- **`RotateCW`/`RotateCCW`**: attempt SRS rotation with kick tests; on success, if resting,
  reset lock timer (count a reset).
- **`HardDrop`**: drop to lowest legal y, add 2×cells-traveled to score, lock immediately.
- **`HoldPiece`**: if not `HoldUsed`, swap active with hold (or pull from queue), respawn at
  top, set `HoldUsed = true`.
- **`TogglePause`**: `Playing ↔ Paused` (ignore other gameplay msgs while Paused).
- On lock that causes a spawn collision → `Phase = GameOver`, update `HighScore`.

### 7.4 view
`view` is pure: it reads the Model and emits draw commands (it does not mutate). It renders
the well grid, the locked cells, the ghost piece, the active piece, hold panel, next-queue,
and HUD text. Phase selects the screen (title / play / pause overlay / game-over). Skia
performs the actual GPU drawing from these commands.

### 7.5 Subscriptions
- **Tick:** a 60 FPS timer dispatching `Tick dt` with `dt` in seconds (target 0.0167 s);
  clamp `dt` to ≤ 0.05 s to avoid gravity jumps after a stall.
- **Input:** keyboard key-down → the edge-triggered / `...Down` messages; key-up → `...Up`
  messages for held keys (left/right/soft-drop).

## 8. Rendering (Skia 2D)
Coordinate system: origin top-left, +x right, +y down, logical 1280×720 (Skia scales to the
window). Single full redraw each frame (the board is small; no dirty-rect optimization
needed).

**Draw order (back to front):**
1. **Background** — solid `#101018` fill over the whole canvas.
2. **Well frame** — well background `#000000` rectangle at `(480,40)` size `320×640`; a
   1 px grid of `#202028` lines between cells; a 2 px border `#404050` around the well.
3. **Locked cells** — for each filled grid cell, a 32×32 rounded-rect (corner 3 px) in the
   piece color, with a lighter top-left bevel (`+20%` lightness) and darker bottom-right
   bevel (`-20%`) for a beveled-block look.
4. **Ghost piece** — the active piece projected to its hard-drop landing row, drawn as
   outlines / 30%-alpha fills in the piece color, so the player sees where it will land.
5. **Active piece** — same block style as locked cells, at its live position.
6. **Side panels** — Hold panel (left, at `(120,40)`, 200×120) and Next-queue panel (right,
   at `(840,40)`, 200×420) each with a `#181820` background, `#404050` border, and a label.
   Pieces drawn at half scale (16 px cells) inside.
7. **HUD text** — score / lines / level (see §9), white `#FFFFFF`, monospace font.
8. **Overlays** — pause dimmer (`#000000` at 60% alpha + "PAUSED") or game-over panel.

Line-clear visual effect: on a clear, flash the cleared rows white for ~120 ms (a short
animation timer) before collapsing — optional in v1 but specified for polish.

## 9. UI / HUD / Screens

**Screens:**
- **Title:** game name centered (~64 px), "Press Enter to Start" prompt, high score shown.
- **Play:** the well + panels + HUD (below).
- **Pause:** play screen frozen under a 60% dimmer with centered "PAUSED".
- **Game Over:** centered "GAME OVER", final Score / Lines / Level, "New High Score!" if
  beaten, and "Press Enter to Restart".

**HUD (during play):** right column, below the next-queue panel, monospace, right-aligned:
- `SCORE` — current score, zero-padded to 7 digits (e.g. `0012300`).
- `LINES` — cumulative lines cleared.
- `LEVEL` — current level.
- `HIGH` — session/persisted high score.
- **Hold** label above the hold panel; **Next** label above the queue panel.

### 9.1 Menu system (detailed)
A single **menu stack** drives every non-play screen (Title, Settings, Stats, Pause, Game
Over). Each menu is a vertical list of rows with a cursor, so one small update handler
serves them all and navigation is identical everywhere. The §7.1 `Phase` still selects the
active screen; the cursor and submenu stack live alongside it.

**Menu tree**
```
Title ─┬─ Play ──────────── start a new descent at the selected difficulty's start level
       ├─ Stats ─────────── Stats & Charts screen (§9.2)
       ├─ Settings ──────┬─ Difficulty     ◄ Easy · Normal · Hard ►
       │                 ├─ Master volume  ◄ 0 – 100 ►
       │                 ├─ Sound          ◄ On · Off ►
       │                 ├─ Window scale   ◄ 1× · 2× · Fit ►
       │                 ├─ Ghost piece    ◄ On · Off ►
       │                 └─ Back
       └─ Quit

Pause ─┬─ Resume
       ├─ Restart ───────── new descent, fresh 7-bag (§4.7)
       ├─ Settings ──────── (same submenu; returns to Pause)
       └─ Quit to Title

Game Over ─┬─ Retry ─────── new descent with reset board + a freshly shuffled bag
           ├─ View Stats ── Stats & Charts (§9.2)
           └─ Title
```

**Navigation model**
- `MenuCursor: int` on the active menu; `↑` decrement, `↓` increment, both **wrap** around
  the ends of the list.
- `Enter`/`Space` activates the current row; `Esc`/`Back` pops the stack (**Back**).
- **Cycler/slider rows** (Difficulty, Master volume, Sound, Window scale, Ghost piece):
  `←`/`→` change the value in place; the row shows a right-aligned `◄ value ►` widget.
- Rendering reuses the §9 title style: the selected row is inverted (white box, black text);
  non-selected rows are `#FFFFFF` on the `#101018` background, monospace.

**Msg additions** (extend §7.2):
```fsharp
    | MenuUp | MenuDown              // move cursor (wraps)
    | MenuAdjust of dir:int          // -1 / +1 on a cycler/slider row
    | MenuActivate                   // Enter/Space on the current row
    | MenuBack                       // Esc — pop the menu stack
    | OpenStats | CloseStats         // enter / leave the Stats screen (§9.2)
```

Settings apply live and persist to local config (§13): **Difficulty** selects the §12
`startLevel` preset (Easy `0` · Normal `5` · Hard `9`), which sets the initial gravity from
the §6.2 curve; **Master volume**/**Sound** route to `Audio.setMasterVolume` (§10, clamped
`[0,1]`); **Ghost piece** toggles the §8 landing-projection outline (stretch §15.1).

### 9.2 Stats & charts screen
The Stats screen visualizes **the last run** and **lifetime** play. It reads a `MatchStats`
snapshot (never live gravity or timers), so it is a pure, deterministic render reachable
from Title, Game Over, and Pause. Chart-design choices below follow the project dataviz
conventions (form-first, validated colorblind-safe palette, single axis, identity by entity).

**Tracked per run** — `MatchStats`, accumulated in the `Tick` lock/clear cases (§7.3),
snapshotted on the `GameOver` transition:

| Field | Type | Updated |
|-------|------|---------|
| `clears` | `int * int * int * int` | `(single,double,triple,tetris)`; bump the slot for the rows cleared on each lock (§4.6) |
| `levelReached` | `int` | max `Level` reached this run (§6.2) |
| `piecesPlaced` | `int` | incremented on each piece lock (§4.5) |
| `tSpins` | `int` | incremented on a kicked-in T clear (stretch §15.3) |
| `maxCombo` | `int` | longest streak of consecutive line-clearing locks |
| `pps` | `float` | pieces per second = `piecesPlaced / playSeconds` |
| `apm` | `float` | actions per minute = input actions `× 60 / playSeconds` |
| `playSeconds` | `float` | accumulated live-play time (excludes Paused) |

**Lifetime** — `LifetimeStats`, persisted (§13): `highScore`, `gamesPlayed`, `mostLines`,
`topLevel`, `bestPPS`, `tetrisRate` (Tetrises ÷ total clears, %).

**Layout** (logical 1280×720): a KPI tile row across the top, two charts below.

```
┌──────────────────────────── STATS ────────────────────────────┐
│  ┌HIGH SCORE┐ ┌ LINES ┐ ┌TOP LEVEL ┐ ┌ BEST PPS ┐             │  ← KPI stat tiles
│  │  128400  │ │  187  │ │    12    │ │   2.4    │             │
│  └──────────┘ └───────┘ └──────────┘ └──────────┘             │
│                                                                │
│  Line clears by type                Score progression          │
│  ▇▇                          128400 ┤              ╭──         │
│  ▇▇                                 │          ╭──╯            │
│  ▇▇  ▇▇                             │      ╭──╯                │
│  ▇▇  ▇▇  ▇▇  ▇▇                   0 ┼───────────────► lines    │
│  Sgl Dbl Tpl Tet                    0                    187   │
└────────────────────────────────────────────────────────────────┘
     ↑/↓ scope:  ▸ This Run · Lifetime               ESC — Back
```

**Charts** (rendered in Skia with the same draw-list discipline as §8):

1. **Line clears by type** — *form: per-category magnitude → bars.* x = clear type
   (`Single, Double, Triple, Tetris`), y = number of clears of that type this run. **Single
   series**, so one hue and no legend. Bars are 4 px-rounded at the data end with a 2 px
   surface gap between them. Fill `#2a78d6` (light) / `#3987e5` (dark) — validated
   categorical slot 1; the four bars share the one hue (comparing magnitudes, not identities).
2. **Score progression** — *form: change over an ordered index → line.* x = cumulative lines
   cleared, y = `Score`; **one climbing series** → one hue and no legend. Colour `#2a78d6`
   (light) / `#3987e5` (dark). 2 px line, an ≥ 8 px end marker at the latest point, recessive
   1 px gridlines in `#3C3C3C`.

Conventions honored: **color follows the entity** (the score line keeps slot 1 in either
scope — never repainted by the toggle); **one axis only** (no dual-scale); chart **text uses
ink tokens** (`#FFFFFF` primary / `#C3C2B7` muted), never the series hue; layout is **fixed
and deterministic**, so a fixed-seed run (§13) renders byte-identical for snapshot tests. The
`↑/↓` **scope** toggle swaps the data source This-Run ↔ Lifetime without changing colors.

**Model/Msg hooks:** add `Stats: MatchStats` and `Lifetime: LifetimeStats` to §7.1 Model;
accumulate them in the `Tick` lock/clear cases (bump the `clears` slot and `maxCombo` on each
clear, `piecesPlaced` on each lock, `playSeconds`/`pps`/`apm` continuously); on `GameOver`,
fold `MatchStats` into `Lifetime` (raise `highScore`/`mostLines`/`topLevel`/`bestPPS`,
recompute `tetrisRate`) and persist (§13). `OpenStats`/`CloseStats` switch a `Stats` screen
state carrying the `StatScope`; the render is a no-op on gravity and timers.

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
| Piece move — left/right shift (§3, §7.3 `MoveLeftDown`/`MoveRightDown`) | `Audio.playSfx (SoundId "move") 0.4` | `move` | short tick |
| Rotate CW/CCW (§4.4, §7.3 `RotateCW`/`RotateCCW`) | `Audio.playSfx (SoundId "rotate") 0.4` | `rotate` | soft click |
| Soft drop, per cell (§4.3, §7.3 `SoftDropDown`) | `Audio.playSfx (SoundId "soft-drop") 0.3` | `soft-drop` | subtle tick per cell |
| Hard drop (§4.3, §7.3 `HardDrop`) | `Audio.playSfx (SoundId "hard-drop") 0.7` | `hard-drop` | thud |
| Lock (§4.5) | `Audio.playSfx (SoundId "lock") 0.5` | `lock` | light clack |
| Line clear, 1–3 (§4.6, §11) | `Audio.playSfx (SoundId "line-clear") 0.7` | `line-clear` | chime |
| Tetris — 4 lines (§4.6, §11) | `Audio.playSfx (SoundId "tetris") 0.9` | `tetris` | bigger fanfare |
| Level up (§6.2) | `Audio.playSfx (SoundId "level-up") 0.7` | `level-up` | ascending cue |
| Hold — swap (§4.8, §7.3 `HoldPiece`) | `Audio.playSfx (SoundId "hold") 0.5` | `hold` | swap whoosh |
| Game over — top-out (§11, §7.3) | `Audio.playSfx (SoundId "game-over") 0.8` | `game-over` | descending tone |

Tetris classically has looping background **music** — a "Korobeiniki"-style theme whose tempo
rises with level — modeled as `Audio.playMusic (TrackId "korobeiniki") true` on `StartGame`
and `Audio.stopMusic` on game over (§11). A mute/settings toggle maps to
`Audio.setMasterVolume` (e.g. `Audio.setMasterVolume 0.0` to mute, `1.0` to restore).
**Testing:** collect the frame's
`AudioEffect`s, `Audio.interpret` them, and assert the `AudioEvidence.Requested` sequence for
representative events (e.g. a hard drop that locks and clears four rows requests exactly
`PlaySfx (SoundId "hard-drop", _)` then `PlaySfx (SoundId "tetris", _)`).

## 11. Win / Loss / Scoring
**No win condition** — Tetris is endless; the goal is the highest score before topping out.

**Loss (top-out):** the game ends when a newly spawned piece overlaps an already-filled cell
(the stack has reached the spawn zone). `Phase → GameOver`.

**Scoring (line clears, multiplied by `level + 1`):**

| Event | Base points | At level n |
|---|---|---|
| Single (1 line) | 100 | `100 × (n+1)` |
| Double (2 lines) | 300 | `300 × (n+1)` |
| Triple (3 lines) | 500 | `500 × (n+1)` |
| Tetris (4 lines) | 800 | `800 × (n+1)` |
| Soft drop | 1 per cell | (not multiplied) |
| Hard drop | 2 per cell | (not multiplied) |

Lives/continues: none. One game = one descent.

**Top-out variants.** Both resolve to `Phase = GameOver`:
- **Block-out** (the v1 loss condition, §4.2, AC #14): a newly spawned piece's cells overlap
  already-filled cells. This is the rule the edge case (§13 (b)) relies on — a hard drop into
  the spawn zone locks first, and the *next* spawn's block-out ends the game.
- **Lock-out** (optional stricter detection): a piece that locks entirely within the hidden
  buffer rows, never having entered the visible field, may top out on that lock instead of
  deferring to the next spawn. It reaches the same `GameOver` one frame sooner and does not
  change any score.

**Scoring interactions:**
- **Drop points and clear points stack.** A hard drop of M cells that clears a Tetris at level n
  scores `2M + 800 × (n + 1)` on that single lock. Soft- and hard-drop points are awarded even
  when the resulting lock clears no line, and even on the topping-out lock.
- **A zero-line lock scores nothing** beyond any drop points already banked during its fall.
- **Clears are scored at the pre-level-up level.** Within a lock the sequence is *clear → score
  → level-up* (§2, §7.3), so a Tetris that carries the total across a level boundary is scored
  at the **old** level; the level-up applies to the *next* clear. A Tetris taking you from
  level 0 → 1 scores `800 × 1`, and the following clear then uses `× 2`.
- Worked example: at level 8 a Tetris scores `800 × 9 = 7200`, a Triple `500 × 9 = 4500`, a
  Single `100 × 9 = 900`.
- The score is a **non-negative, monotone-increasing integer**; nothing ever subtracts it and
  there is no cap beyond the integer range. The 7-digit HUD pad (§9) is display-only and rolls
  presentation, not the stored value.

**Combo & back-to-back (stretch §15.4).** Consecutive line-clearing locks (a **combo**) and
chained Tetrises / T-spins (**back-to-back**) grant escalating multipliers in stretch scope. The
base game already tracks `maxCombo` (§9.2) but applies **no multiplier** in v1 — every clear is
scored purely by the §11 table.

## 12. Difficulty & Balancing
Data-driven tunables:

| Parameter | Default | Range | Effect |
|---|---|---|---|
| `boardWidth` | 10 | 6–14 | Well width (cols) |
| `boardHeight` | 20 | 10–30 | Visible well height (rows) |
| `cellPx` | 32 | 16–48 | On-screen cell size |
| `dasMs` | 170 | 80–300 | Delay before auto-shift |
| `arrMs` | 50 | 0–120 | Auto-shift repeat rate |
| `lockDelayMs` | 500 | 200–1000 | Rest time before lock |
| `maxLockResets` | 15 | 5–30 | Anti-stall cap |
| `softDropFactor` | 20 | 2–40 | Gravity divisor while ↓ held |
| `linesPerLevel` | 10 | 5–20 | Lines to advance a level |
| `startLevel` | 0 | 0–19 | Initial gravity level |
| `nextPreview` | 5 | 1–6 | Visible upcoming pieces |
| `gravityCurve` | §6.2 | — | Seconds/cell per level table |

**Difficulty presets** (the §9.1 selector maps to a `startLevel` and thus a first gravity from
the §6.2 curve; nothing else about the rules changes):

| Preset | `startLevel` | First gravity (s/cell) | Feel |
|---|---|---|---|
| Easy | 0 | 0.800 | learning band; full reaction time (§6.4) |
| Normal | 5 | 0.383 | brisk; some pre-planning |
| Hard | 9 | 0.100 | fast; DAS charge and queue lookahead required |

**Tunable interactions** (constraints a tester should exercise at the range edges):
- `arrMs = 0` yields **instant DAS** — once `dasMs` elapses the piece traverses to the wall in a
  single frame; `boardWidth` caps how far one such burst can carry.
- `dasMs` versus `arrMs` trades tap-precision against hold-slide: lower `dasMs` feels twitchier;
  the 170 / 50 default balances single-cell taps against wall-slams.
- `lockDelayMs × maxLockResets` bounds the worst-case hover: `500 ms × 15 = 7.5 s`, and only
  while the player keeps producing valid moves — the new-lowest-row forgiveness (§4.12) means
  real descent never counts toward that ceiling.
- `softDropFactor` interacts with the one-cell-per-frame clamp (§4.10): once
  `gravityInterval / softDropFactor ≤ 0.0167 s`, higher factors are indistinguishable, so the
  20–40 top of its range only matters at low levels.
- `boardWidth` shifts spawn centering — pieces still spawn centered (§4.2, §5.7), so odd/even
  widths move the I and O spawn columns, while the kick tables (§4.11) stay width-independent.
- `boardHeight` resizes the well but not the spawn logic; taller boards ease survival, shorter
  boards raise top-out pressure. `nextPreview` and hold change planning headroom, not the
  difficulty math.

**Balancing philosophy.** The only *time-based* difficulty lever is `gravityCurve`; every other
tunable is player-facing preference or accessibility. This deliberately preserves the
self-inflicted-difficulty fantasy (§1) — the clock speeds up, but you still lose to the board you
built, never to a rule that changed underneath you.

## 13. Technical Notes
- **Performance budget:** at most ~220 drawn cells (board 200 + active 4 + ghost 4 + panel
  previews) plus HUD text — trivially within a 16.7 ms frame at 60 FPS.
- **Timestep:** **fixed-logic, variable-render**. Gravity is drained by **`FixedStep.drain`** — do
  not hand-roll the accumulator: `FixedStep.drain gravityInterval dt GravityAcc` returns
  `struct (drops, acc')`, the whole gravity steps this frame owes and the remainder to bank, with the
  spiral-of-death clamp built in (it supersedes the hand-written `dt ≤ 0.05 s`). Feed it the *current*
  level's interval and the speed curve falls out of the same call. DAS/ARR and lock timers are plain
  `dt` countdowns, not fixed steps — leave those as they are.
- **Determinism / RNG:** the 7-bag shuffle flows through one **`Rng`** (`FS.GG.Game.Core`,
  splitmix64), seeded with `Rng.ofSeed`; the Fisher–Yates swap index is `Rng.nextInt`. Given the same
  seed and input sequence the game is fully reproducible — essential for the acceptance tests below.
  It is a **value**, not a `System.Random`: every draw returns `struct (x, rng')` and you write
  `rng'` back to the `Model`, so the `Model` stays a value you can snapshot, replay and compare.
  A `System.Random` in the `Model` is a mutable object *shared* by every copy of it, which
  silently breaks the reproducibility this bullet promises.
- **Persistence:** high score persisted to local storage / a settings file; loaded on title,
  written on game over if beaten.
- **Edge cases:** (a) rotation against a wall/floor uses kicks; if no kick fits, rotation is
  rejected, not clipped. (b) Hard drop into the spawn zone still locks then triggers top-out
  on next spawn. (c) Soft drop must never skip the lock delay (so a row can still be cleared
  after a fast drop). (d) Hold cannot be spammed (one use per piece). (e) Simultaneous
  multi-row clears must be detected in a single scan (a Tetris is one event, not four
  singles).

## 14. Acceptance Criteria (test scenarios)
All scenarios assume a fixed RNG seed and the default tunables unless stated.

1. **Spawn & fall.** *Given* a new game, *when* the first piece spawns, *then* it appears
   centered in columns 3–6 at the top and descends one cell per gravity interval for the
   current level (level 0 → one cell every 0.800 s ± one frame).

2. **Horizontal move & wall block.** *Given* a piece mid-board, *when* the player presses
   Left, *then* it shifts exactly one cell left; *when* it is against column 0 and Left is
   pressed, *then* it does not move and stays at column 0.

3. **DAS auto-shift.** *Given* Left held continuously, *when* 170 ms have elapsed, *then* the
   piece begins shifting one cell every 50 ms until release or a wall.

4. **Rotation with wall kick.** *Given* a piece flush against the right wall in an
   orientation that would clip the wall when rotated CW, *when* Rotate CW is pressed, *then*
   the piece kicks left into a legal position and rotates; *and if* no kick offset fits,
   *then* the piece's rotation and position are unchanged.

5. **Soft drop scoring.** *Given* a piece N cells above the stack, *when* ↓ is held until it
   lands, *then* gravity is ~20× faster and the score increases by N (1 point per soft-drop
   cell).

6. **Hard drop.** *Given* a piece with M cells of clear space below, *when* Space is pressed,
   *then* the piece instantly moves to the lowest legal row, locks immediately (no 500 ms
   delay), and the score increases by `2 × M`.

7. **Lock delay & reset.** *Given* a piece resting on the stack, *when* it has rested for
   500 ms with no input, *then* it locks; *but when* the player rotates or shifts it into a
   still-fallable position before 500 ms, *then* the lock timer resets — up to 15 times,
   after which it locks on next rest regardless.

8. **Single line clear & scoring.** *Given* row 19 has 9 of 10 cells filled and a piece fills
   the gap, *when* it locks, *then* exactly that row clears, rows above shift down one, and
   the score increases by `100 × (level + 1)`.

9. **Tetris (four-line) clear.** *Given* four stacked rows each missing only column 9 and a
   vertical I piece dropped into column 9, *when* it locks, *then* all four rows clear in a
   single event and the score increases by `800 × (level + 1)`.

10. **Level up & gravity change.** *Given* the player has cleared 9 lines at level 0, *when* a
    clear brings the cumulative total to 10, *then* the level becomes 1 and the gravity
    interval drops to 0.717 s/cell.

11. **7-bag fairness.** *Given* a fresh game, *when* the first 7 pieces are spawned, *then*
    each of the 7 tetromino kinds appears exactly once (in some order); *and* no kind is
    ever more than 12 pieces apart (the guaranteed 7-bag drought bound, §4.7).

12. **Hold piece, once per spawn.** *Given* an active piece and an empty hold slot, *when*
    Hold is pressed, *then* the piece moves to hold and the next-queue piece spawns; *when*
    Hold is pressed again before the new piece locks, *then* nothing happens (one hold per
    spawned piece).

13. **Next queue preview.** *Given* play in progress, *when* the current piece locks, *then*
    the piece previously shown first in the 5-deep next queue becomes active and the queue
    shifts up by one, revealing a new 5th preview.

14. **Top-out / game over.** *Given* the stack reaches the spawn zone, *when* the next piece
    would spawn onto an already-filled cell, *then* the game transitions to GameOver, the
    final score/lines/level are shown, and the high score updates if beaten.

15. **Pause freezes state.** *Given* play in progress with an active piece mid-fall, *when*
    Pause is pressed, *then* gravity, timers, and input are suspended and the board is shown
    dimmed; *when* Pause is pressed again, *then* play resumes from the identical state.

16. **Determinism.** *Given* the same RNG seed and the same recorded input sequence with
    timestamps, *when* the game is replayed, *then* the final board, score, lines, and level
    are identical to the original run.

17. **DAS charge across handoff.** *Given* Left is held continuously while a piece locks, *when*
    the next piece spawns, *then* it auto-shifts left at ARR (50 ms/cell) immediately with no
    fresh 170 ms DAS wait, and it stops flush at column 0. (§2, §4.9)

18. **Soft-drop clamp at high gravity.** *Given* level 19 (gravity 0.033 s/cell) with ↓ held,
    *when* soft drop applies, *then* the piece descends at most one cell per frame — soft drop
    and natural gravity both clamp to one cell/frame — while still awarding 1 point per cell
    descended. (§4.10)

19. **Lock-reset new-lowest-row forgiveness.** *Given* a resting piece the player repeatedly
    rotates to refresh the lock timer, *when* it has consumed 15 resets and then a gravity step
    carries it to a strictly lower row, *then* the reset budget is forgiven and further resets
    are permitted at the new row; *but* 15 resets with no downward progress lock it on the next
    rest. (§4.12)

20. **Non-contiguous multi-line clear.** *Given* two full rows separated by a partially filled
    row, *when* a lock completes both, *then* both full rows clear in a single scan, the middle
    row falls past them, and each surviving row drops by the count of cleared rows beneath it.
    (§4.13)

21. **Clear scored before level-up.** *Given* 9 lines already cleared at level 0, *when* a Tetris
    brings the cumulative total to 13, *then* that Tetris scores `800 × 1` (still level 0), the
    level then becomes 1, and the next clear is scored at `× 2`. (§11, §6.3)

## 15. Stretch Goals
1. **Ghost piece toggle & hold-to-rotate options** — accessibility/preference settings.
2. **Line-clear and lock animations** — flash + collapse tween, particle burst on Tetris.
3. **T-spin detection & bonus scoring** — reward kicked-in T placements (TSS/TSD/TST).
4. **Back-to-back & combo multipliers** — chained Tetrises / consecutive clears bonus.
5. **Marathon / Sprint (40-line) / Ultra (2-min) modes** — alternate goals & timers.
6. **Garbage / versus mode** — sent lines for local or networked 2-player.
7. **Replays & leaderboards** — record the deterministic input stream; persist top runs.
8. **Themes & skins** — swappable block palettes and backgrounds.

## 16. Milestone Roadmap

Implementation is sequenced into milestones; each item is a colored checkbox
tracking its status. Items reference the section that specifies them.

**Legend:** 🟥 Not started · 🟨 In progress · 🟩 Done · ⬜ Deferred (post-v1)

_All items start 🟥 (spec status). Flip an item to 🟨 when work begins and 🟩 once
its acceptance test(s) pass (§14)._

### M0 — Scaffold & fixed-step loop
- 🟥 Project scaffold: `Model`/`Msg`/`update`/`view` skeleton (§7)
- 🟥 Fixed-step gravity via `FixedStep.drain`, banked remainder (§7.5, §13)
- 🟥 `Rng` value seeded with `Rng.ofSeed`, threaded through `Model` (§13)
- 🟥 10×22 grid (rows 0–19 visible, `y` increasing downward) coordinate system (§4.1)

### M1 — Tetrominoes & spawn
- 🟥 7 piece kinds + guideline colors; `cellsOf` derives cells from Kind/Pos/Rotation (§4.2, §5.2)
- 🟥 Spawn centered in columns 3–6, straddling the hidden buffer rows (§4.2) — AC #1
- 🟥 Spawn-overlap detection feeding top-out (§4.2, §11)
- 🟥 Per-piece spawn origins and SRS rotation-box pivots (§5.7)

### M2 — Input, movement & DAS
- 🟥 Edge-triggered vs held key model; key-down/up → `Msg` (§3, §7.5)
- 🟥 One-cell horizontal shift with wall block at columns 0/9 (§7.3) — AC #2
- 🟥 DAS 170 ms delay → ARR 50 ms auto-repeat while held (§3, §7.3) — AC #3
- 🟥 No entry delay; DAS/soft-drop charge retained across piece handoff (§2, §4.9) — AC #17
- 🟥 Buffered input applied before the frame's gravity step (§2)

### M3 — Gravity, rotation & lock delay
- 🟥 Per-level gravity step with collision check (§4.3, §6.2) — AC #1
- 🟥 SRS rotation with 5-offset kick tables (simpler 3-kick fallback allowed) (§4.4) — AC #4
- 🟥 Full JLSTZ + I kick tables incl. floor/ceiling kicks (§4.11) — AC #4
- 🟥 Initial Rotation / Initial Hold on keys held at spawn (§4.9)
- 🟥 Soft drop (÷20, +1/cell) and hard drop (instant, +2/cell, immediate lock) (§4.3) — AC #5, #6
- 🟥 Soft-drop/gravity as max, clamped to one cell per frame (§4.10) — AC #18
- 🟥 Lock timer 500 ms with 15 move/rotate resets cap (§4.5) — AC #7
- 🟥 Lock-reset new-lowest-row forgiveness vs stall cap (§4.12) — AC #19

### M4 — Line clears, scoring & levels
- 🟥 Single-scan full-row detection, clear + shift-down collapse (§4.6, §13) — AC #8, #9
- 🟥 Non-contiguous multi-row clear, per-row shift by cleared-rows-beneath (§4.13) — AC #20
- 🟥 Perfect-clear (all-clear) detection, no v1 bonus (§4.13)
- 🟥 Score by clear count × (level+1) plus soft/hard-drop points (§11) — AC #8, #9
- 🟥 Clears scored pre-level-up; drop and clear points stack (§11, §6.3) — AC #21
- 🟥 Level up every 10 lines, gravity interval from the §6.2 curve (§6.2) — AC #10
- 🟥 Monotone floored level; gravity cap at 29+, multiplier uncapped (§6.3)
- 🟥 Tunable interaction constraints (ARR=0, soft-drop clamp, width centering) (§12)

### M5 — Bag, hold & next queue
- 🟥 7-bag Fisher–Yates shuffle via `Rng.nextInt`, refill on empty (§4.7) — AC #11
- 🟥 5-deep next queue, shifts up and reveals a new preview on lock (§4.8) — AC #13
- 🟥 Hold swap with `holdUsed` flag, once per spawned piece (§4.8) — AC #12

### M6 — Match flow & screens
- 🟥 Title / Playing / Paused / GameOver phases select the screen (§7.1, §9)
- 🟥 Top-out on spawn collision → `GameOver`, `HighScore` update (§11) — AC #14
- 🟥 Block-out and optional lock-out top-out detection (§11) — AC #14
- 🟥 Pause suspends gravity/timers/input, resumes identical state (§7.3) — AC #15

### M7 — Rendering (Skia)
- 🟥 Draw order: background, well frame, locked cells, ghost, active, side panels, HUD (§8)
- 🟥 Beveled rounded-block style + hard-drop ghost projection (§8)
- 🟥 Ghost derived from active piece, updates on move/rotate/fall (§5.6)
- 🟥 Optional line-clear white flash before collapse (§8)

### M8 — Menus, settings & stats
- 🟥 Menu stack with wrapping cursor and cycler/slider rows (§9.1)
- 🟥 Difficulty / volume / sound / ghost settings apply live + persist (§9.1, §12, §13)
- 🟥 Difficulty presets Easy/Normal/Hard → startLevel 0/5/9 first-gravity map (§12)
- 🟥 `MatchStats`/`LifetimeStats` accumulation, KPI tiles + clears/score charts (§9.2)

### M9 — Audio
- 🟥 Per-event `AudioEffect` cues via `update`, `Audio.interpret` evidence, volume clamp `[0,1]` (§10)
- 🟥 "Korobeiniki" music on `StartGame`, `stopMusic` on top-out, master-volume mute toggle (§10)

### M10 — Acceptance & determinism
- 🟥 All 16 acceptance scenarios green (§14)
- 🟥 Deepened acceptance scenarios #17–#21 green (§14) — AC #17, #18, #19, #20, #21
- 🟥 Seed + timestamped input-log replay is identical (§13) — AC #16

### Stretch — deferred (post-v1)
- ⬜ Ghost piece toggle & hold-to-rotate options (§15.1)
- ⬜ Line-clear and lock animations, Tetris particle burst (§15.2)
- ⬜ T-spin detection & bonus scoring (TSS/TSD/TST) (§15.3)
- ⬜ Back-to-back & combo multipliers (§15.4)
- ⬜ Marathon / Sprint (40-line) / Ultra (2-min) modes (§15.5)
- ⬜ Garbage / versus mode (§15.6)
- ⬜ Replays & leaderboards (§15.7)
- ⬜ Themes & skins (§15.8)
