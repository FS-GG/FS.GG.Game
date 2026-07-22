---
title: "Frogger"
slug: frogger
category: games
complexity: simple
genre: "Arcade / fixed-screen crossing"
target_session_minutes: 8
stack: { rendering: "FS.GG.Rendering (Skia/OpenGL)", framework: "FS.GG.Game.Core (Rng for determinism)", arch: "Elmish/MVU", lang: "F#" }
status: spec
---

# Frogger

## 1. Overview
You are a frog trying to get home. Between you and safety lies a five-lane highway of
cars and trucks roaring left and right, then a river you cannot swim in — your only
footing is a churn of drifting logs and the backs of turtles (some of which dive and
leave you to drown). The core verb is **hop**: a single grid-snapped jump in one of
four directions, deliberate and discrete. The fun is reading traffic and current as a
shifting puzzle, timing each hop, and the nerve test of riding a log across open water
toward one of five home slots before the clock runs out. It is twitch-pattern-reading
in bite-sized, instantly-readable screens.

## 2. Core Game Loop
**Moment-to-moment:** read lanes → pick a gap/platform → hop one cell → land safely (or
ride) → re-read → repeat upward until a home slot is filled → respawn at start → fill
the next slot.

**Session-level:** title → press Start → play (fill 5 home slots = clear level) → level
advances (faster, denser traffic, more diving turtles) → lose all lives → game over →
show score + high score → restart.

A single "life attempt" loop: spawn at start row with a fresh per-life timer → cross →
either reach home (score, reset position, keep level) or die (lose a life, reset
position, timer resets) → continue while lives remain.

**Beat structure:** one *beat* is a single hop plus the read that precedes it. A crossing
is at minimum 11 beats (start row 11 → home row 0), and at `LifeTime = 30 s` the per-life
timer (§4.8) budgets ~2.7 s of reading per beat. Nothing in the loop advances on a clock
the player does not drive except the hazards themselves: the frog is stationary between
beats unless it is `Riding` (§4.4), so a stalled player loses only to the timer, never to
a forced move. This is the twitch-*reading* the genre trades in — the pressure is entirely
in the hazard cadence and the draining bar, not in the frog's own momentum.

**Economy of the two loops:** the *life* loop banks points (rows, home, time bonus) but
spends a life on failure; the *level* loop converts five bankings into a cleared board. The
two are deliberately asymmetric — a life is cheap to spend early (respawn is instant, §7)
but a filled slot is permanent for the level, so the risk gradient rises as slots fill. With
four slots taken, the only lanes to home are the ones not yet solved, and the fly (§4.7),
which only ever sits in an *empty* slot, concentrates into the few that remain — the last
crossing of a level is both the hardest and the most rewarding.

## 3. Controls & Input
Input is **edge-triggered** (one hop per key press; holding does nothing and auto-repeat
is ignored). The frog never moves continuously — every press queues exactly one
grid-snapped hop, and presses during an in-progress hop are dropped (not buffered) in v1.

| Input | Action | Model |
|---|---|---|
| Arrow Up / W | Hop one cell up (toward home) | Edge-triggered (KeyDown only) |
| Arrow Down / S | Hop one cell down | Edge-triggered |
| Arrow Left / A | Hop one cell left | Edge-triggered |
| Arrow Right / D | Hop one cell right | Edge-triggered |
| Enter / Space | Start game / confirm restart (menus only) | Edge-triggered |
| P | Pause / unpause | Edge-triggered |
| Esc | Return to title (from pause) | Edge-triggered |

Notes: no mouse or gamepad required for v1 (gamepad d-pad MAY map 1:1 to the arrows).
A hop into a wall (left edge while on the leftmost column, etc.) is rejected and consumes
no time and triggers no animation.

## 4. Mechanics (detailed)

The playfield is a **grid of cells**, `CellW = 64 px` wide and `CellH = 60 px` tall, on a
1280×720 logical canvas. That gives **20 columns** (1280 / 64) and a vertical stack of
**12 rows** (12 × 60 = 720) described in §6. Column index `0..19`; the frog's logical position is a cell
`(col, row)` for snapped state, plus a smooth pixel offset during a hop and a fractional
`subX` while riding a platform.

### 4.1 Hopping movement (grid-snapped)
- A hop moves the frog exactly one cell in the pressed direction.
- A hop is animated over `HopDuration = 0.12 s`: the sprite lerps from the source cell
  center to the destination cell center. Gameplay-wise the frog is considered to occupy
  the **destination** cell for collision the instant the hop resolves (at hop end).
- During a hop the frog ignores new input (no buffering in v1).
- Hopping up from the start row into row 1 begins the crossing. Each **distinct
  furthest row reached** awards points once (see §11).
- Horizontal hops while riding a platform are relative to the frog's current world
  position snapped to the nearest column on landing.
- **No diagonal hops.** Each press maps to exactly one of four axis directions and a hop
  never changes two axes; there is no "half hop." A hop either fully resolves to its
  destination cell or (on wall rejection, §3) never starts — the frog is never left
  straddling two cells at a beat boundary.
- **Mid-hop hazard resolution is split by hazard type.** Vehicle collision (§4.2) uses the
  frog's *interpolated* hitbox during the 0.12 s lerp, so a car can kill a frog in the air
  over a road lane — "at any time" in §4.2 includes mid-hop. Water/footing (§4.4),
  home-slot (§4.6) and turtle-submersion (§4.5) outcomes are *discrete*: evaluated only at
  hop resolve, against the destination cell, because those hazards are about where you land,
  not where you pass. Scoring and bonus checks likewise fire once, at resolve.
- **Landing column while riding:** a horizontal hop taken while `Riding` computes its
  destination from the frog's *current world column* `round(WorldX / CellW)`, then moves one
  column from there; the fractional `subX` is dropped at hop start and re-snapped on resolve.
  A vertical hop off a platform likewise departs from the rounded column, so a frog drifting
  at `subX = 0.6` that hops up aims at the column it visually overlaps most.
- **Platform-to-platform hops are ordinary hops:** the frog may hop from a plank directly
  onto an adjacent lane's plank; footing is re-evaluated at the destination (§5) with no
  carry-over of the previous platform's `Vx` — the frog stops dead in world X the instant it
  is not `Riding`, then re-inherits the new platform's drift next tick.

### 4.2 Road section (death on contact)
- Five road lanes (rows 7–11, see §6). Each lane scrolls vehicles in one direction at a
  fixed speed.
- Vehicles are **lethal**: if the frog's hitbox overlaps any vehicle hitbox at any time
  (including while standing still as a car drives into it), the frog dies instantly.
- Lane directions alternate; each lane has its own speed (see the table below). Base values (level 1):

| Lane (row) | Vehicle | Direction | Speed (px/s) | Count | Spacing (px) | Hitbox (w×h) |
|---|---|---|---|---|---|---|
| 11 (nearest start) | Car | → (right) | 80 | 3 | ~420 | 56×48 |
| 10 | Truck | ← (left) | 60 | 2 | ~620 | 120×48 |
| 9 | Car | → (right) | 120 | 3 | ~420 | 56×48 |
| 8 | Car (fast) | ← (left) | 160 | 4 | ~320 | 56×48 |
| 7 | Bulldozer/Car | → (right) | 100 | 3 | ~420 | 72×48 |

- Vehicles wrap: when a vehicle's leading edge exits one side it re-enters the opposite
  side at the same speed (toroidal in X), preserving lane spacing.
- **Deterministic lane layout, not RNG jitter:** at level build each lane places `Count`
  vehicles at even `Spacing` from a fixed per-lane phase offset, then scrolls; the toroidal
  wrap preserves that spacing forever, so gaps are stable and *readable* rather than random.
  No two vehicles overlap within a lane, so every lane always presents exactly `Count` gaps.
- **Hitbox insets are symmetric.** A vehicle's lethal AABB is the `Hitbox` in the table
  (h = 48 within the 60 px row), inset from the drawn sprite; the frog's own 40×40 hitbox is
  inset from its 48×48 sprite (§5.1). Both insets are symmetric, so a pixel-touching
  near-miss reads as a near-miss and collision stays center-fair.
- **No road coyote grace.** A stationary frog on a road row dies the tick a vehicle's moving
  hitbox reaches it — there is no forgiveness window on the road (contrast the one-tick
  turtle-submersion grace, §13). Reading the gap is the whole skill; a frog parked in a lane
  is on borrowed time until it hops clear.

### 4.3 Median (safe row)
- Row 6 is a **safe median** strip (grass). No hazards. Frog may rest here indefinitely
  (timer still ticks).
- The median is the crossing's one mid-point checkpoint of *position* only, never of the
  timer: the frog can idle here to re-read all five road lanes below and all five river
  lanes above, but the per-life countdown (§4.8) is unaffected by standing safe. Together
  with row 11 (start), it is one of only two unconditionally safe rows at every level (§12).



### 4.4 River section (death without platform)
- Five river lanes (rows 1–5). The river water is **lethal by default**: if the frog
  ends a hop on a water cell that is **not** covered by a platform, it **drowns**.
- Platforms (logs and turtle groups) drift horizontally. Standing on a platform is the
  only safe footing in the river.
- **Riding (velocity inheritance):** while the frog is standing on a platform and not
  mid-hop, the frog's world X position advances by the platform's velocity each tick
  (`frog.x += platform.vx * dt`). The frog inherits the platform's drift exactly.
- If a ridden platform carries the frog off-screen (frog center leaves `[0, 1280]`), the
  frog dies (carried into the wall / off the world).
- After any hop the frog re-evaluates footing: it snaps its column to the nearest grid
  column but keeps a fractional `subX` for smooth riding; collision uses world pixels.
- **Footing test (center-point rule):** on a hop resolving into a river row, the frog has
  footing iff its hitbox *center* lies within some platform's AABB in world X (the row is
  fixed, so only X matters). This means ≥ ~50% of the 40-px frog must be over the plank;
  landing with a mere corner over a log but the center over open water between two planks
  **drowns**. The same center-point test governs whether `Riding` continues each tick.
- **Seams never leak:** because logs/turtles in one lane never overlap and are separated by
  `Gap` (§13), at most one platform can contain the center; if none does, it is water and
  the frog drowns. A frog *riding* toward a gap does not spontaneously drown — footing is
  re-tested only at hop boundaries and on the ride tick, and the ridden platform carries the
  frog with it, so its center stays over the plank until the frog itself hops off.
- **Boundary planks:** a platform entering from off-screen offers footing only once its AABB
  actually covers the landing center — a frog cannot hop onto a plank still 30 px out over
  water. A frog that hops up out of row 1 into row 0 leaves the ride entirely (§4.6).
- **The river is not a one-way ratchet:** hopping down toward start from a plank is legal and
  lands on the row-below platform (or drowns by the same rule); lateral hops reposition along
  a lane. Only `MaxRowReached` (§11) is monotone — the frog may retreat freely, but never
  re-earns a row it already banked.

| River lane (row) | Platform | Direction | Speed (px/s) | Platform length | Gap |
|---|---|---|---|---|---|
| 5 (nearest home) | Log (short) | ← (left) | 70 | 2 cells (128 px) | 1.5 cells |
| 4 | Turtle group (3) | → (right) | 90 | 3 turtles (192 px) | 2 cells |
| 3 | Log (long) | ← (left) | 60 | 4 cells (256 px) | 2 cells |
| 2 | Turtle group (2) | → (right) | 110 | 2 turtles (128 px) | 2.5 cells |
| 1 | Log (medium) | ← (left) | 80 | 3 cells (192 px) | 1.5 cells |

### 4.5 Diving turtles
- Turtle-group platforms cycle through a dive animation. Each group has a phase timer.
- Cycle: **Up (safe) 4.0 s → Sinking 0.6 s → Down (lethal/no footing) 2.0 s → Rising
  0.6 s → Up …** (total ~7.2 s).
- While **Down**, the turtle provides no footing — a frog standing on it drowns the
  instant the group fully submerges (end of Sinking). While **Up** or transitioning
  (Sinking/Rising) it still supports the frog.
- Not all turtle lanes dive: lane 4's 3-turtle group dives; lane 2's 2-turtle group is
  non-diving in level 1 and begins diving from level 2 onward.
- **De-synced groups:** each diving group carries its own `phaseTimer`, offset at build so
  lanes never submerge in unison — the lane-4 group and (from level ≥ 2) the lane-2 group
  hold complementary windows, so a patient reader can usually find a crossable river. The
  offsets are drawn from `Model.Rng` (§13) and are therefore replay-stable to the seed.
- **The tell:** the 0.6 s `TSinking` and 0.6 s `TRising` transitions are the player's
  warning. During `TSinking` the turtles are *still footing* while their sprite alpha fades
  1→0 (§8); a frog that hops on during a fade is safe but on notice. The lethal instant is
  the *end* of `TSinking` (fully `Down`), never its start — hop off before submersion and you
  live.
- **Submersion is per-group, not per-turtle:** all turtles in a group share one `divePhase`,
  so you cannot stand on the "up" end of a half-sunk group. `canDive = false` groups skip
  phase advancement entirely and are permanent footing (§5.4). As `Down` duration grows with
  level (§6, cap 3.5 s) while `TUp` stays 4.0 s, late-game crossings demand a hop landed and
  cleared inside a fixed generosity window that never widens — the ramp shrinks only the safe
  margin, never the tell.

### 4.6 Home slots
- Row 0 (home) has **5 home slots** at fixed columns. Between slots are lethal hedges/wall.
- The frog must hop up from row 1 into an **empty** home slot to score a "home."
- Hopping into an **occupied** slot, or into the hedge between slots, is a **death**.
- Filling all 5 slots clears the level (see §11).
- **Slot columns:** the 5 slots sit at columns 1, 5, 9, 13, and 17 (even 4-column pitch
  across the 20-column board), each one cell wide; every other row-0 column is lethal hedge.
- **Alignment on landing:** a hop up into row 0 resolves its column as `round(WorldX / CellW)`
  and that rounded column must equal a slot column *exactly*. A frog drifting on a plank must
  line its center under a slot before hopping — an off-by-one rounding lands on hedge and
  dies. This makes the final hop of every crossing a precision beat, not a formality: the
  river's drift is what makes it hard, since `WorldX` is rarely slot-aligned by luck.
- **Fill order is free:** any empty slot may be filled in any order; the level clears the
  instant the count reaches 5 (§11), regardless of sequence. Re-entering a filled slot is the
  occupied-slot death above.

### 4.7 Bonus targets
- **Fly:** periodically a fly appears in a random **empty** home slot for `FlyDuration =
  6 s`. Reaching home in that slot while the fly is present awards a bonus.
- **Lady frog:** occasionally a "lady frog" rides a log in the river. If the frog hops
  onto the lady frog's cell, she joins (escort); delivering her home awards a bonus and
  she then disappears. (Implement as a special rider entity in a river lane.)
- **Fly cadence:** at most one fly exists at a time; after a fly expires or is eaten, the
  next spawns after `Fly spawn interval = 12 s` (§12) into a uniformly-random *currently
  empty* slot drawn from `Model.Rng`. If every slot is occupied, no fly spawns. A fly never
  appears in a slot the same tick it is filled, so the fly-and-fill are never a tie.
- **Lady frog rules:** she rides a *log* (never a turtle group), spawns at most one at a
  time on a leftward river lane, and rides with the frog once boarded (the frog still rides
  the underlying log and inherits its `Vx`). `Escorted` is set on boarding her cell.
  Delivering her to *any* empty home slot pays the escort bonus (§11) and clears her; dying
  while escorting simply loses her, no penalty beyond the death. She never fills a slot
  herself and never blocks one.

### 4.8 Per-life timer
- Each life attempt has a countdown timer `LifeTime = 30 s` shown as a draining bar.
- Reaching home resets the timer for the next attempt. Timer reaching 0 = death.
- Remaining time at the moment of reaching home contributes a **time bonus** (see §11).
- **Low-timer band:** color thresholds are keyed to *absolute* seconds, not bar fraction —
  green above 10 s, yellow 5–10 s, red below 5 s — so the warning always means the same real
  time even as `LifeTime` shrinks with the level (§6). Below 5 s the `timer-low` cue (§10)
  ticks. This is purely a tell: the frog is not slowed, hurried, or otherwise altered.
- **The timer never pauses for animation:** it counts down through `Hopping` and `Riding`,
  and it stops only in `Paused` and menus (§9.2 `playSeconds` excludes those). Reaching home
  resets it to the level's `LifeTime` for the next attempt; a death from any cause resets it
  on respawn (§11). There is no way to bank or carry unspent time between attempts — it is
  converted to score on a home hop, or lost.

## 5. Entities / Game Objects

All hitboxes are axis-aligned rectangles; collision is AABB overlap in world pixels.

### 5.1 Frog
- Sprite ~48×48 px, hitbox 40×40 px centered in its cell.
- State machine: `Idle` (snapped, on a cell) → `Hopping` (lerping, `HopDuration`) →
  `Idle`/`Riding`; `Riding` (on platform, inheriting `vx`); `Dying` (death anim 0.5 s)
  → respawn or game over; `Home` (slot filled, brief celebrate 0.4 s) → respawn at start.
- Created at level start and after each death/home at the **start cell** (col 9 or 10,
  start row). Destroyed only conceptually (state reset).
- **Footing resolution** each hop-resolve/ride tick classifies the frog by the row it
  occupies: on a road, median, start, or home row the outcome is vehicle-death / safe / home
  by that row's rules; on a river row it is the §4.4 center-point footing test → `Riding` or
  `Dying`. `WorldX` is the source of truth for horizontal position; the snapped `Cell.col` is
  a snapshot refreshed on each landing, used for wall/edge rejection and slot alignment.

### 5.2 Vehicle
- Properties: lane row, `vx` (signed px/s), width (per table), hitbox h = 48, sprite kind.
- Behavior: constant-velocity scroll with toroidal wrap; always lethal on overlap.

### 5.3 Log (platform)
- Properties: lane row, `vx`, length in px. Hitbox = full length × 56 h.
- Behavior: constant-velocity drift with wrap. Provides footing always.
- The footing span is the full `LengthPx`; there is no lethal segment in v1 (crocodile
  segments are stretch, §15.2). Two logs in a lane are separated by `Gap` and never overlap,
  so the center-point test (§4.4) resolves to at most one.

### 5.4 TurtleGroup (platform)
- Properties: lane row, `vx`, turtle count, `divePhase`, `phaseTimer`, `canDive: bool`.
- Behavior: drift + dive cycle (§4.5). Footing only when not fully `Down`.
- The footing span is `count * CellW` while not `Down`, and empty while `Down`.
  `canDive = false` groups (lane 2 before level 2) skip phase advancement entirely and are
  permanent footing indistinguishable from a log for footing purposes.

### 5.5 HomeSlot
- 5 fixed slots on row 0. Properties: `col`, `occupied: bool`, `hasFly: bool`.
- Created at level start (all empty); `occupied` set true on a successful home.
- `hasFly` and `occupied` are stored independently, but a fly only ever spawns into an
  `occupied = false` slot (§4.7); filling a slot with a fly present eats it and clears
  `hasFly` on the same resolve tick. Slot `col` values are 1, 5, 9, 13, 17 (§4.6).

### 5.6 Bonus riders
- `Fly` (in an empty home slot, timed) and `LadyFrog` (rides a river log, escort target).

F#-flavored sketch:

```fsharp
type Dir = Up | Down | Left | Right

type FrogState =
    | Idle
    | Hopping of fromCell: int * int * toCell: int * int * t: float   // t in [0, HopDuration]
    | Riding  of platformId: int
    | Dying   of t: float
    | Home    of slot: int * t: float

type DivePhase = TUp | TSinking | TDown | TRising

// NOT X — that label collides with Scene's Point/Rect and mis-resolves bare record literals in the
// durable LayoutEvidence.fs. These entities ride a discrete Row, so a scalar is honest; the label is
// the thing that must change. Sizes stay `…Px`, never Width/Height.
type Platform =
    { Id: int; Row: int; LeftX: float; Vx: float; LengthPx: float
      Kind: PlatformKind }
and PlatformKind =
    | Log
    | Turtles of count: int * canDive: bool * phase: DivePhase * phaseTimer: float

type Vehicle =
    { Row: int; LeftX: float; Vx: float; WidthPx: float; Kind: VehicleKind }
and VehicleKind =
    | Car                       // 56×48; the fast lane (row 8) is a Car at a higher Vx, not a kind
    | Truck                     // 120×48
    | Bulldozer                 // 72×48
```

## 6. World / Levels / Progression
Logical canvas **1280×720**. Columns are `64 px` (20 cols = 1280); the layout is a fixed
stack of **12 rows** (top = home, bottom = start). Twelve 64 px rows would be 768 px and
overrun the height, so the vertical **row pitch is 60 px** (12 × 60 = 720) and the HUD is
drawn as a top overlay (§8, §9) rather than reserving its own band. Row → y-top mapping:
`rowY(r) = r*60` (r = 0…11).

| Row | y band | Contents |
|---|---|---|
| 0 | top | **Home** (5 slots + hedges) |
| 1 | | River lane (log, ←) |
| 2 | | River lane (turtles ×2, →) |
| 3 | | River lane (log long, ←) |
| 4 | | River lane (turtles ×3 diving, →) |
| 5 | | River lane (log short, ←) |
| 6 | | **Median** (safe grass) |
| 7 | | Road lane (car, →) |
| 8 | | Road lane (car fast, ←) |
| 9 | | Road lane (car, →) |
| 10 | | Road lane (truck, ←) |
| 11 | bottom | **Start** (safe grass) + road lane (car, →) overlaps as needed |

Start cell: column 9 or 10 on the start row.

**Difficulty ramp (per level cleared):**
- Vehicle and platform speeds ×`(1 + 0.12 * (level-1))`, capped at ×1.6.
- Diving turtles: lane 2 group begins diving at level ≥ 2; dive `Down` duration grows
  +0.3 s per level (cap 3.5 s).
- `LifeTime` shrinks by 2 s per level, floor 20 s.
- Gaps between platforms tighten by ~8% per level, floor 1 cell.
- Optionally spawn an extra vehicle per road lane at levels 3 and 5.

**Home-slot columns:** the 5 slots occupy columns 1, 5, 9, 13, 17 on row 0 (§4.6); hedges
fill the remaining row-0 columns. The start cell (col 9 or 10, row 11) sits directly below
the center slot, so straight-up is the most direct route home but rarely the safest — it commits
to the busiest column of every lane.

**Level identity:** levels differ only by the tuning ramp, never by hand-authored layout —
the row stack (rows 0–11) is fixed for every level, so difficulty is entirely a function of
speed, density, dive punishment, gap tightness, and timer. This keeps every board instantly
readable (§1) while the numbers climb; there is no memorization of level geometry, only of
the tuning trend.

**Ramp resolution order (per level cleared):** apply the speed multiplier first (clamped to
the cap), then the turtle-dive changes and lane-2 activation, then the `LifeTime` shrink,
then gap tightening, then the optional extra vehicle at levels 3 and 5. Every ramped value
derives from the *base* tables (§4.2, §4.4) times the level factor — never from the previous
level's already-ramped value — so rounding never compounds and a given level is reproducible
from its number alone.

| Level | Speed mult `(1 + 0.12·(L-1))` | `LifeTime` (floor 20) | Lane-2 diving | Turtle `Down` (cap 3.5) |
|---|---|---|---|---|
| 1 | ×1.00 | 30 s | no | 2.0 s |
| 2 | ×1.12 | 28 s | yes | 2.3 s |
| 3 | ×1.24 (+1 vehicle/lane) | 26 s | yes | 2.6 s |
| 4 | ×1.36 | 24 s | yes | 2.9 s |
| 5 | ×1.48 (+1 vehicle/lane) | 22 s | yes | 3.2 s |
| 6 | ×1.60 (cap reached) | 20 s (floor) | yes | 3.5 s (cap) |
| 7+ | ×1.60 | 20 s | yes | 3.5 s |

At level 6 the speed cap (×1.6), the `LifeTime` floor (20 s), and the turtle `Down` cap
(3.5 s) all land together, so level 6 onward is a steady-state endurance test at fixed
maximum tuning — the ramp has no further dial to turn, only the player's stamina.

## 7. State Model (Elmish/MVU)

**Model:**
```fsharp
type Phase = Title | Playing | Paused | GameOver

type Model =
    { Phase: Phase
      Level: int
      Lives: int
      Score: int
      HighScore: int
      Frog: {| Cell: int * int; State: FrogState; WorldX: float |}
      Vehicles: Vehicle list
      Platforms: Platform list
      HomeSlots: {| Col: int; Occupied: bool; HasFly: bool; FlyTimer: float |} []   // length 5
      Lady: {| PlatformId: int; Escorted: bool |} option
      LifeTimer: float            // seconds remaining, counts down from LifeTime
      MaxRowReached: int          // for per-row scoring this attempt
      Rng: Rng                // FS.GG.Game.Core — a VALUE, so the Model stays one (§13)
      ElapsedTotal: float }
```

**Msg:**
```fsharp
type Msg =
    | Tick of dt: float          // seconds, ~1/60
    | Hop of Dir                 // edge-triggered input
    | StartGame
    | TogglePause
    | ToTitle
```

**update — key transitions:**
- `Tick dt`: advance vehicles/platforms (X += Vx*dt, wrap); advance turtle dive phases;
  if `Frog.State = Riding`, `WorldX += platform.Vx*dt`; progress `Hopping`/`Dying`/`Home`
  timers; decrement `LifeTimer`; spawn/expire flies; on `LifeTimer <= 0` → death; run
  collision resolution (vehicle hit, water-without-platform, off-screen ride).
- `Hop dir` (only when `Phase=Playing`, `Frog.State=Idle|Riding`): validate target cell
  (reject walls/edges); start `Hopping`. On hop resolve: re-evaluate footing → set
  `Idle`/`Riding`/`Dying`/`Home`; if reaching a new furthest row, add row points; if into
  home slot, score home + time bonus + fly/lady bonuses, reset frog to start.
- `StartGame`: init level 1, lives 3, score 0, build entities, `Phase=Playing`.
- `TogglePause`: `Playing ↔ Paused`.
- `ToTitle`: from `Paused`/`GameOver` → `Title`.
- Death handler: `Lives-1`; if 0 → `Phase=GameOver` (commit HighScore); else respawn,
  reset `LifeTimer`, `MaxRowReached`.

**view:** pure function `Model -> Scene` describing layers/shapes for Skia to draw
(§8). No mutation, no drawing side-effects in `view`.

**Subscriptions:**
- A 60 FPS animation-frame/timer subscription emitting `Tick dt` (dt in seconds, clamped,
  see §13).
- Keyboard subscription translating edge-triggered KeyDown → `Hop`/`StartGame`/
  `TogglePause`/`ToTitle`.

## 8. Rendering (Skia 2D)
Coordinate system: logical 1280×720, origin top-left, y-down. Backbuffer scaled to window
preserving aspect (letterbox). Redraw the full scene every frame (cheap at this entity
count); no dirty-rect optimization in v1.

**Draw order (back to front):**
1. **Background bands:** road asphalt `#2B2B2B` (rows 7–11), median grass `#3FA34D`
   (row 6), river water `#1E5FA8` (rows 1–5), home strip `#1E5FA8`/`#143A2B` hedges
   (row 0), start grass `#3FA34D` (row 11).
2. **Platforms:** logs rounded rects `#6B4423` with grain lines `#5A3A1E`; turtles as
   circles `#2E8B57` (shell) with head; submerging turtles fade alpha 1→0 during Sinking.
3. **Vehicles:** rounded rects per kind — car `#E03B3B`, fast car `#F4D03F`, truck
   `#B0B0B0` cab + cargo, bulldozer `#E67E22`.
4. **Bonus:** fly = small dark `#222` dot pair in a slot; lady frog = pink `#E08AB0` frog.
5. **Frog:** body `#7CD42B`, eyes `#FFFFFF`/`#000`. Hop = scale-pop (1.0→1.15→1.0).
   Death = brief `#FFFFFF` flash + shrink, then skull/X mark for 0.5 s.
6. **Home fills:** a parked frog `#5FAE1F` drawn in each occupied slot.
7. **HUD overlay** (§9), then screen overlays (title/pause/game-over scrims `#000` @ 60%).

Particles/effects: small splash ring (water death, 6 droplet circles), squash puff on
car death. Font: a clean monospace/bitmap font, white `#FFFFFF`, sizes 16–48 px.

## 9. UI / HUD / Screens
**Screens:**
- **Title:** game name, "PRESS ENTER", high score, simple animated frog hopping in place.
- **Playing:** the board + HUD.
- **Paused:** dimmed board + "PAUSED — P resume, ESC title".
- **Game Over:** "GAME OVER", final score, high score, "PRESS ENTER to restart".

**HUD (top band, y 0–32):**
- Score (left), `SCORE 00000`, 5-digit zero-padded.
- Level (center), `LVL 1`.
- High score (right), `HI 00000`.

**Bottom strip (overlaid on start row):**
- Lives: remaining frog icons (lives-1, since one is active), bottom-left.
- **Timer bar:** bottom-right, draining green→yellow→red bar representing `LifeTimer /
  LifeTime`, ~300 px wide.

### 9.1 Menu & configuration — the shared game shell

Frogger uses the **generic FS.GG game shell** (FS-GG/FS.GG.Rendering#991) — the same
menu/start screen and settings every FS.GG game shares — rather than a bespoke per-game menu.
The game supplies only its **name**, its **key→command map** (the rebindable actions from §3
Controls), and its play `update`/`view`; the shell provides everything below.

- **Main menu / start screen** — the game's name (**FROGGER**) as the title label, with
  **Start**, **Config**, and **Exit**. The run-end path keeps the classic arcade framing: the
  panel shows the run's final score, the high score, and the depleted lives row (§9 bottom
  strip), then offers **Continue** — the insert-coin gesture that starts a fresh run at level 1
  with lives back to 3.
- **`Esc` from gameplay** opens the pause menu (Resume · Config · Exit to menu) over the same
  shell; `Esc` again resumes. The frog's in-play hop keys (§3) are edge-triggered and
  unaffected — the shell's menu cursor keys are only live while a menu is open.
- **Config / Settings**, all applied live and persisted across restarts:
  - **Screen resolution** and **fullscreen** (windowed / borderless / fullscreen), driven
    through the SkiaViewer window-behavior + `LogicalCanvas` letterbox seam.
  - **Key rebinding** — the player remaps this game's controls (the §3 actions — hop up, down,
    left, right) via the `Controls.KeyRebind` UI over the `KeyboardInput.Keymap` mechanism;
    bindings persist via `KeymapCodec` (JSON), beside this game's other saved config (§13).
  - Game-specific rows are added as extra Config rows over the shell: **Difficulty** (the §12
    preset — Easy `Lives 5 / LifeTime 45 / mult +0.08`, Normal `3 / 30 / +0.12`, Hard
    `2 / 20 / +0.18`), **Master volume**/**Sound** (route to `Audio.setMasterVolume`, §10,
    clamped `[0,1]`), and **Grid overlay** (toggles the §8 cell-gridline draw that helps read
    hops). The menu, Esc routing, display settings, and rebind screen come from the shell.

The shell is pointer- and keyboard-navigable over the interactive Controls host (the
`fs-gg-skiaviewer` "game → pointer host" recipe). It is a shared dependency, so Frogger does
**not** re-specify menu-stack/cursor/settings machinery of its own. The **Stats & charts**
screen (§9.2) is a Frogger-specific screen reached as a Config/menu row.

### 9.2 Stats & charts screen
The Stats screen visualizes **the last run** and **lifetime** play. It reads a `RunStats`
snapshot (never live physics), so it is a pure, deterministic render reachable from Title,
Game Over, and Pause. Chart-design choices below follow the project dataviz conventions
(form-first, validated colorblind-safe categorical palette, single axis, identity by entity).

**Tracked per run** — `RunStats`, accumulated in `Tick`/`Hop`, snapshotted on `GameOver`:

| Field | Type | Updated |
|-------|------|---------|
| `crossingsCompleted` | `int` | +1 each time the frog reaches a home slot (§4.6), all levels |
| `homesFilled` | `int` | slots filled on the current (in-progress) level, 0–5 |
| `deaths` | `{ road:int; water:int; timer:int; creature:int }` | +1 by cause on each death (§11) |
| `timeBonusTotal` | `int` | sum of every time bonus banked on reaching home (§4.8, §11) |
| `levelsReached` | `int` | highest `Level` reached this run |
| `scoreByLevel` | `(lvl:int * cum:int) list` | appended on each level clear (+ the live level) |
| `playSeconds` | `float` | accumulated live-play time (excludes Paused/menus) |

`deaths` splits by cause: **road** (car/truck, §4.2), **water** (drown or submerged turtle,
§4.4/§4.5), **timer** (`LifeTimer` reached 0, §4.8), **creature** (gator/snake, the §15
stretch hazards — 0 until those ship).

**Lifetime** — `LifetimeStats`, persisted (§13): `highScore`, `gamesPlayed`, `crossingsTotal`,
`bestLevel`, `mostDeathsCause` (the modal death cause across all runs).

**Layout** (logical 1280×720): a KPI tile row across the top, two charts below.

```
┌──────────────────────────── STATS ────────────────────────────┐
│  ┌HIGH SCORE┐ ┌CROSSINGS┐ ┌BEST LEVEL┐ ┌ DEATHS ┐             │  ← KPI stat tiles
│  │  24 850  │ │   37    │ │    6     │ │   14   │             │
│  └──────────┘ └─────────┘ └──────────┘ └────────┘             │
│                                                                │
│  Deaths by cause                    Score by level             │
│  ▇▇                          24k ┤               ╭── score     │
│  ▇▇        ▇▇                     │          ╭──╯              │
│  ▇▇   ▇▇   ▇▇                     │      ╭──╯                  │
│  ▇▇   ▇▇   ▇▇   ▇▇                │  ╭──╯                      │
│  Road Watr Timr Crea           0 ┼──────────────────► level #  │
└────────────────────────────────────────────────────────────────┘
     ↑/↓ scope:  ▸ This Run · Lifetime            ESC — Back
```

**Charts** (rendered in Skia with the same draw-list discipline as §8):

1. **Deaths by cause** — *form: per-category magnitude → bars.* x = death cause
   (`Road, Water, Timer, Creature`), y = number of deaths. **Single series**, so one hue and
   no legend. Bars are 4 px-rounded at the data end with a 2 px surface gap between them.
   Fill `#2a78d6` (light) / `#3987e5` (dark) — validated categorical slot 1. The four causes
   are compared magnitudes of one measure, so they stay one hue (never rainbowed).
2. **Score by level** — *form: change over an ordered index → line.* x = level number
   (`1…levelsReached`), y = cumulative score at that level's clear (from `scoreByLevel`).
   **Single series** (one climbing run), so one hue and no legend; the line is direct-labeled
   ("score") at its right end. Line `#2a78d6`. 2 px line, ≥ 8 px end marker, recessive 1 px
   gridlines in `#3C3C3C`.

Conventions honored: **color follows the entity** (the death-bar hue and the score line are
each a fixed slot, never repainted by the scope toggle); **one axis only** (no dual-scale);
chart **text uses ink tokens** (`#FFFFFF` primary / `#C3C2B7` muted), never the series hue;
layout is **fixed and deterministic**, so a fixed-seed run (§13) renders byte-identical for
snapshot tests. The `↑/↓` **scope** toggle swaps the data source This-Run ↔ Lifetime without
changing colors (lifetime deaths pool by cause; lifetime score-by-level uses the best run).

**Model/Msg hooks:** add `Stats: RunStats` and `Lifetime: LifetimeStats` to §7 Model;
accumulate them in the `Tick`/`Hop` cases (bump `deaths` by cause in the death handler,
`crossingsCompleted`/`homesFilled` and `timeBonusTotal` on reaching home, `levelsReached`
and a `scoreByLevel` point on each level clear, `playSeconds` from clamped `dt`); on
`GameOver`, fold `RunStats` into `Lifetime` and persist (§13). `OpenStats`/`CloseStats`
switch a `Phase = Stats of scope:StatScope`-style state; the render is a no-op on physics.

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
| Hop resolves (§4.1) | `Audio.playSfx` | `hop` | short "boop" |
| Drown on water (§4.4) | `Audio.playSfx` | `plunk` | Plunk (water death): low splash |
| Vehicle kills (§4.2) | `Audio.playSfx` | `squash` | Squash (vehicle death): thud |
| Reach home slot (§4.6) | `Audio.playSfx` | `home` | Home reached: chime/jingle |
| Fly/lady bonus (§4.7) | `Audio.playSfx` | `bonus` | Fly/lady bonus: sparkle |
| Timer low, `LifeTimer` < 5 s (§4.8) | `Audio.playSfx` | `timer-low` | Timer low (<5 s): ticking |
| Level clear, all 5 slots (§11) | `Audio.playSfx` | `level-clear` | Level clear: fanfare |
| Game over | `Audio.playSfx` | `game-over` | Game over: descending tone |

A light looping arcade theme plays under the title and board: `Audio.playMusic (TrackId
"arcade-theme") true` (loop true) on entering `Title`/`Playing`, and `Audio.stopMusic` when
the run ends at `GameOver`. A mute/settings toggle maps to `Audio.setMasterVolume` (e.g.
`Audio.setMasterVolume 0.0` to silence, `1.0` to restore). **Testing:** collect the frame's
`AudioEffect`s, `Audio.interpret` them, and assert the `AudioEvidence.Requested` sequence for
representative events (e.g. a resolved `Hop Up` requests exactly `PlaySfx (SoundId "hop", _)`).

## 11. Win / Loss / Scoring

**Scoring:**
| Event | Points |
|---|---|
| Each new furthest row advanced (per attempt) | +10 |
| Reach a home slot | +50 |
| Time bonus on reaching home | +10 per full second remaining on `LifeTimer` |
| Eat the fly (home into slot with fly) | +200 |
| Escort lady frog home | +200 |
| Clear a level (all 5 slots) | +1000 |
| Each spare life remaining at level clear | +100 |

- "New furthest row" awards +10 only the first time the frog reaches that row in the
  current attempt (tracked via `MaxRowReached`).

**Scoring resolution on a home hop** (a single resolve tick, §13 step 6) sums in a fixed
order so totals are deterministic: (1) the final new-furthest-row +10 if row 0 is a new
furthest this attempt; (2) +50 home; (3) +10 × ⌊`LifeTimer`⌋ time bonus; (4) +200 fly if
`HasFly`; (5) +200 lady if escorting; (6) if this fills the 5th slot, +1000 level clear and
+100 × spare lives. Time bonus *floors* the timer — partial seconds are dropped — so AC #10's
`LifeTimer = 12.0` banks exactly 120.

**No double-award:** each new-furthest-row +10 fires once per attempt per row via
`MaxRowReached` (§4.1); backtracking down and re-climbing pays nothing. Home, fly, and lady
each pay once per event. The spare-life bonus counts lives *after* the clearing crossing —
the currently-active frog is never counted as spare, so at `Lives = 3` a clear pays for 2
spares (+200).

**Death refunds nothing but the life:** points already banked stay; only the life and the
per-attempt `MaxRowReached` are lost, so a run's score is monotonic non-decreasing. Dying on
the very first hop of an attempt (no new row, no home) banks nothing and simply costs the
life — there is no minimum-progress consolation.

**Win condition:** Fill all 5 home slots → level clears → next level begins (slots reset,
speeds/density increase). The game has no hard end; it ramps until the player loses.

**Loss condition (death):** any of —
- frog hitbox overlaps a vehicle;
- hop resolves on water without a platform (drown);
- ridden platform carries frog off-screen;
- standing on a fully-submerged diving turtle;
- hop into an occupied home slot or hedge;
- `LifeTimer` reaches 0.

On death: lose 1 life, respawn at start, reset timer & `MaxRowReached`. **Lives = 3**
(one active + the rest). At 0 lives → Game Over.

## 12. Difficulty & Balancing
| Param | Default | Range | Effect |
|---|---|---|---|
| `CellW`/`CellH` | 64 / 60 px | 48–80 | Grid scale (cell width / row pitch) |
| `HopDuration` | 0.12 s | 0.06–0.25 | Hop snappiness |
| `LifeTime` | 30 s | 15–45 | Time pressure |
| Road speeds | 60–160 px/s | 40–260 | Traffic difficulty |
| River speeds | 60–110 px/s | 40–180 | Current difficulty |
| Vehicle counts/lane | 2–4 | 1–6 | Traffic density |
| Platform gaps | 1.5–2.5 cells | 1–4 | River traversability |
| Turtle Up duration | 4.0 s | 2–6 | Diving generosity |
| Turtle Down duration | 2.0 s | 1–4 | Diving punishment |
| Fly spawn interval | 12 s | 6–30 | Bonus frequency |
| `FlyDuration` | 6 s | 3–12 | Bonus window |
| Level speed mult/level | +0.12 | 0–0.3 | Ramp steepness |
| Speed mult cap | 1.6 | 1.2–2.5 | Difficulty ceiling |
| Lives | 3 | 1–5 | Run length |
| Home-slot count | 5 | 3–5 | Crossings per level |
| Home-slot pitch | 4 cols | 3–5 | Slot spacing on row 0 |
| Footing rule | center-in-AABB | — | River landing strictness (§4.4) |
| Low-timer threshold | 5 s | 3–10 | When the red band + `timer-low` cue arms (§4.8) |
| Escort bonus | +200 | 100–400 | Lady-frog reward (§4.7) |
| Fly bonus | +200 | 100–400 | Fly reward (§4.7) |

**Difficulty presets** (from §9.1) are thin re-parameterizations of this table, not separate
rulesets: Easy `Lives 5 / LifeTime 45 / mult +0.08`, Normal `3 / 30 / +0.12`, Hard
`2 / 20 / +0.18`. Everything else — grid, hitboxes, dive cycle, footing rule, slot columns —
is preset-invariant, so a strategy learned on Normal transfers to Hard with only tighter
timing. The preset multiplier replaces the §6 `+0.12` per-level factor while keeping the same
×1.6 cap, so Easy reaches the ceiling around level 9 and Hard by level 5.

**Balancing intent:** road lanes are tuned so that at level 1 every lane has at least one
crossable gap open at all times (the fastest lane, 160 px/s, still leaves a frog-width dwell
in its `Spacing`), while the river is tuned so the limiting factor is footing *timing*, not
raw speed. As the ramp closes gaps (§6) and speeds rise, the two sections converge in
difficulty; the median (§4.3) and start (row 11) remain the only unconditionally safe rows at
every level, so all pressure funnels into the ten hazard rows between them.

## 13. Technical Notes
- **Entity budget:** ~5 road lanes × ≤4 vehicles + 5 river platforms (+turtles) + 1 frog +
  5 slots + ≤2 bonuses ≈ **40 entities**. Trivially within 60 FPS / 16.7 ms.
- **Timestep:** variable-`dt` `Tick` from the frame subscription, **clamped to ≤ 0.05 s** (skip huge
  frames after a pause/stall) — **deliberately not `FixedStep.drain`**. Determinism here (§14.16) is
  replay of the recorded `Tick`/`Hop` sequence, and each `Tick` carries its own `dt`, so the log alone
  reproduces the run; lane motion is pure `pos += vel·dt` with nothing to quantise. If you *do* want a
  fixed-rate sim — for a headless test harness, or to make collision repeatable independently of the
  recorded frames — use `FixedStep.drain (1.0/60.0) frameTime acc` to drive `Tick`, rather than
  hand-rolling the accumulator.
- **Determinism / RNG:** all randomness (fly slot, fly timing, lady spawns, optional level variation)
  draws from `Model.Rng` — `FS.GG.Game.Core`'s **`Rng`** (splitmix64), seeded explicitly with
  `Rng.ofSeed` so a given seed → identical run; tests pass a fixed seed. It is a **value**, not a `System.Random`: every draw returns `struct (x, rng')` and you write
  `rng'` back to the `Model`, so the `Model` stays a value you can snapshot, replay and compare.
  A `System.Random` in the `Model` is a mutable object *shared* by every copy of it, which
  silently breaks the reproducibility this bullet promises.
- **Collision order each Tick:** (1) move world; (2) advance turtle phases; (3) if Riding
  update WorldX; (4) resolve hop completion; (5) evaluate death conditions; (6) award
  scoring. Death checks are evaluated once per tick, after movement.
- **Persistence:** high score stored locally (e.g., a `highscore.json` / app-data file),
  loaded on launch, written on Game Over if beaten.
- **Edge cases:** simultaneous valid hop + lethal vehicle on the destination → death;
  hop into wall → no-op (no time/anim); frog riding the seam between two platforms uses
  whichever AABB it overlaps (logs/turtles in one lane don't overlap, so at most one);
  fly expiring exactly as frog lands → bonus granted if `HasFly` true at land-resolve
  tick; turtle submerging exactly when frog lands on it → land first, then re-check next
  tick (one-tick grace).

## 14. Acceptance Criteria (test scenarios)

1. **Grid hop up.** *Given* the frog is `Idle` at start cell (9, 11) *When* `Hop Up` is
   dispatched and `HopDuration` elapses via `Tick`s *Then* the frog is `Idle` at cell
   (9, 10) and score increased by +10 (new furthest row).

2. **Edge-triggered single hop.** *Given* the frog is mid-`Hopping` *When* another `Hop`
   is dispatched before the hop resolves *Then* the second `Hop` is ignored and the frog
   completes exactly one cell of movement.

3. **Wall rejection.** *Given* the frog is `Idle` at column 0 *When* `Hop Left` is
   dispatched *Then* the frog remains at column 0, no animation starts, and `LifeTimer`
   is unchanged for that input.

4. **Vehicle kills.** *Given* the frog is `Idle` on road row 9 and a car's hitbox moves
   to overlap the frog's hitbox *When* the next `Tick` resolves collision *Then* the frog
   enters `Dying`, lives decrement by 1, and it respawns at the start cell with
   `LifeTimer = LifeTime`.

5. **Drown on water.** *Given* the frog hops up into a river row onto a water cell **not**
   covered by any platform *When* the hop resolves *Then* the frog drowns (a splash effect
   plays) and a life is lost.

6. **Safe on log.** *Given* the frog hops onto a log whose AABB covers the destination
   cell *When* the hop resolves *Then* the frog is `Riding` and does not drown.

7. **Velocity inheritance.** *Given* the frog is `Riding` a log with `Vx = -70` px/s
   *When* `1.0 s` of `Tick`s elapse with no input *Then* the frog's `WorldX` decreased by
   ~70 px (within one frame's `dt*Vx`) and the frog is still alive.

8. **Carried off-screen.** *Given* the frog is `Riding` a leftward log near the left edge
   *When* continued `Tick`s carry the frog's center to `WorldX < 0` *Then* the frog dies
   (off-screen).

9. **Diving turtle drowns rider.** *Given* the frog is standing on a diving turtle group
   in phase `TUp` *When* the group completes `TSinking` and enters `TDown` while the frog
   has not hopped off *Then* the frog drowns at the moment it is fully submerged.

10. **Reach home + scoring.** *Given* the frog is `Idle` in river row 1 aligned with an
    empty home slot and `LifeTimer = 12.0` *When* `Hop Up` resolves into the empty slot
    *Then* that slot becomes `Occupied`, score increases by +50 (home) + 120 (time bonus,
    12 full seconds × 10), and the frog respawns at start with a reset timer.

11. **Occupied slot is lethal.** *Given* a home slot is already `Occupied` *When* the frog
    hops into that same slot *Then* the frog dies and loses a life (slot stays occupied).

12. **Fly bonus.** *Given* an empty home slot has `HasFly = true` *When* the frog reaches
    home in that slot *Then* an additional +200 is awarded and the fly is cleared.

13. **Timer death.** *Given* `Playing` with the frog idle *When* enough `Tick`s elapse to
    drive `LifeTimer` from its start to ≤ 0 *Then* the frog dies and a life is lost.

14. **Level clear.** *Given* 4 home slots are occupied *When* the frog fills the 5th slot
    *Then* +1000 (level clear) plus +100 per remaining spare life is awarded, `Level`
    increments, all slots reset to empty, and entity speeds scale by the level multiplier
    (capped at ×1.6).

15. **Game over + high score.** *Given* `Lives = 1` and `Score > HighScore` *When* the
    frog dies *Then* `Phase = GameOver`, `HighScore` is updated to `Score`, and persisted.

16. **Determinism.** *Given* two runs seeded with the same RNG value and an identical
    `Tick`/`Hop` input sequence *Then* fly spawns, lady spawns, and all positions are
    bit-identical between runs.

17. **River footing center rule.** *Given* the frog hops into a river row and lands with its
    hitbox center over open water between two logs (a corner overlaps a log but the center
    does not) *When* the hop resolves *Then* the frog drowns; the same hop landing with its
    center over the log instead survives as `Riding`.

18. **Home-slot alignment.** *Given* the frog rides a plank in river row 1 whose `WorldX`
    rounds to a hedge column *When* `Hop Up` resolves into row 0 *Then* the frog dies on the
    hedge and loses a life; the same hop with `WorldX` rounding to an empty slot column
    (1, 5, 9, 13, or 17) instead scores a home.

19. **De-synced, replay-stable turtle dives.** *Given* the lane-4 diving group (and, at
    level ≥ 2, the lane-2 group) seed their `phaseTimer` offsets from `Model.Rng` *When* two
    runs share a seed and input sequence *Then* every group's `divePhase` at every tick is
    bit-identical between the runs, and the lane-2 group does not dive at level 1.

20. **Ramp derives from base, not compounded.** *Given* a run reaching level 3 *When* lane
    speeds are computed *Then* each equals its §4.2 base × 1.24 (not level-2's value × a
    further factor), and the speed multiplier never exceeds ×1.6 at any level.

21. **Scoring resolution order on a home hop.** *Given* the frog fills the 5th slot while
    `HasFly` is true, escorting the lady, with 2 spare lives, `LifeTimer = 8.4`, and row 0 a
    new furthest row *When* the hop resolves *Then* on that single tick the award is +10 (new
    row) + 50 (home) + 80 (⌊8.4⌋ × 10 time bonus) + 200 (fly) + 200 (lady) + 1000 (clear) +
    200 (2 spare lives × 100) = +1740, with no value double-counted.

## 15. Stretch Goals
1. **Input buffering** — queue one hop during an in-progress hop for smoother play.
2. **Crocodiles** — open-jaw croc segment on a log that is lethal (front) but rideable
   (back), plus crocs that surface in empty home slots.
3. **Snake on the median** — a moving lethal hazard on the "safe" median row at higher levels.
4. **Otters / divers** — additional river hazards.
5. **Two-frog co-op** — two frogs, shared lives, split-screen-free same board.
6. **Daily seed challenge** — fixed seed leaderboard using the deterministic RNG.
7. **Variable canvas / responsive grid** — recompute cell size for non-1280×720 windows.
8. **Touch/swipe controls** — directional swipes map to hops for mobile.

## 16. Milestone Roadmap

Implementation is sequenced into milestones; each item is a colored checkbox
tracking its status. Items reference the section that specifies them.

**Legend:** 🟥 Not started · 🟨 In progress · 🟩 Done · ⬜ Deferred (post-v1)

_All items start 🟥 (spec status). Flip an item to 🟨 when work begins and 🟩 once
its acceptance test(s) pass (§14)._

### M0 — Scaffold & fixed-step loop
- 🟥 Project scaffold: `Model`/`Msg`/`update`/`view` skeleton (§7)
- 🟥 Variable-`dt` `Tick` subscription, clamped to ≤ 0.05 s (§7, §13)
- 🟥 `Rng` value seeded with `Rng.ofSeed`, threaded through `Model` as a value (§13)
- 🟥 Logical 1280×720 grid: 64 px columns, 60 px row pitch, letterbox scaling (§4, §6, §8)

### M1 — Hopping & input
- 🟥 Edge-triggered `KeyDown` → `Hop`; hold/auto-repeat ignored (§3)
- 🟥 Grid-snapped hop lerp over `HopDuration` 0.12 s, occupy destination at resolve (§4.1) — AC #1
- 🟥 Mid-hop input dropped, no buffering in v1 (§4.1) — AC #2
- 🟥 Wall/edge hop rejection, consumes no time or animation (§3, §4.1) — AC #3
- 🟥 Per-attempt `MaxRowReached` furthest-row tracking (§4.1, §11)
- 🟥 No diagonal hops; single-axis resolve, never straddling two cells (§4.1)
- 🟥 Mid-hop hazard split: interpolated hitbox for vehicles, discrete footing/home/turtle at resolve (§4.1)
- 🟥 Riding hop departs from `round(WorldX/CellW)`, drops `subX`, re-snaps on resolve (§4.1)

### M2 — Road & vehicles
- 🟥 `Vehicle` entities: constant-velocity scroll with toroidal X wrap (§4.2, §5.2)
- 🟥 Five road lanes with per-lane speed/direction/count/spacing (§4.2, §6)
- 🟥 Vehicle AABB overlap = instant death (§4.2) — AC #4
- 🟥 Deterministic per-lane spawn layout: even `Spacing` from fixed phase offset, no in-lane overlap (§4.2, §6)
- 🟥 Symmetric hitbox insets (vehicle h 48, frog 40×40 of 48) for center-fair collision (§4.2, §5.1)
- 🟥 No road coyote grace: stationary frog dies the tick a vehicle reaches it (§4.2)

### M3 — River, platforms & riding
- 🟥 Median safe row, frog may rest indefinitely (§4.3)
- 🟥 `Log`/`TurtleGroup` platform drift with toroidal wrap (§4.4, §5.3, §5.4)
- 🟥 Drown when a hop resolves on uncovered water (§4.4) — AC #5
- 🟥 Land `Riding` on a platform whose AABB covers the cell (§4.4) — AC #6
- 🟥 Velocity inheritance: `WorldX += platform.Vx*dt` while `Riding` (§4.4) — AC #7
- 🟥 Off-screen ride death (frog center leaves `[0, 1280]`) (§4.4) — AC #8
- 🟥 Center-point footing test: center-in-AABB grants footing / continues `Riding` (§4.4, §5.3) — AC #17
- 🟥 Boundary planks give footing only once their AABB covers the landing center (§4.4)
- 🟥 Backward/lateral river hops legal; only `MaxRowReached` is monotone (§4.4, §11)

### M4 — Diving turtles
- 🟥 Dive cycle `TUp → TSinking → TDown → TRising` phase timer (§4.5)
- 🟥 Footing removed when fully `Down`; lane 4 dives, lane 2 from level ≥ 2 (§4.5, §6)
- 🟥 Rider on a fully-submerged turtle drowns (§4.5) — AC #9
- 🟥 RNG-seeded per-group phase offsets, replay-stable, lane 2 static at level 1 (§4.5, §6) — AC #19
- 🟥 Group-wide submersion; `TSinking`/`TRising` are the tell, lethal only at fully `Down` (§4.5)

### M5 — Home, bonuses & timer
- 🟥 5 home slots + lethal hedges; hop into empty slot scores home (§4.6) — AC #10
- 🟥 Hop into occupied slot or hedge = death (§4.6) — AC #11
- 🟥 Fly bonus in a random empty slot, `FlyDuration` 6 s (§4.7) — AC #12
- 🟥 Lady-frog rider escort bonus (§4.7, §5.6)
- 🟥 Per-life `LifeTime` 30 s draining timer, reaching 0 = death (§4.8) — AC #13
- 🟥 Slot columns 1/5/9/13/17; hop resolves via `round(WorldX/CellW)` alignment (§4.6, §6) — AC #18
- 🟥 One fly at a time, 12 s cadence into a random empty slot from `Model.Rng`, free fill order (§4.7)
- 🟥 Lady rides a leftward log, delivered to any slot, cleared on death, never fills a slot (§4.7, §5.6)
- 🟥 Absolute low-timer band (<5 s red + `timer-low`); timer runs through hop/ride, resets on respawn (§4.8, §9)

### M6 — Scoring, win/loss & progression
- 🟥 Row / home / time-bonus / fly / lady scoring table (§11)
- 🟥 Level clear: 5 slots → +1000, spare-life bonus, slots reset, speed ramp cap ×1.6 (§4.6, §6, §11) — AC #14
- 🟥 Death handler: lose life, respawn, reset timer & `MaxRowReached`; 0 lives → `GameOver` + persist `HighScore` (§7, §11) — AC #15
- 🟥 Per-tick collision order: move → phases → ride → hop resolve → death → score (§13)
- 🟥 Fixed home-hop scoring resolution order, floored time bonus, no double-award (§11) — AC #21
- 🟥 Score monotonic; death refunds nothing but the life and `MaxRowReached` (§11)
- 🟥 Ramp derives from base tables × level factor (no compounding), cap ×1.6 (§6, §12) — AC #20
- 🟥 Level identity is tuning-only over a fixed row stack; steady-state max from level 6 (§6)

### M7 — Rendering (Skia)
- 🟥 Background bands + back-to-front draw order (§8)
- 🟥 Platform / vehicle / frog / bonus sprites, submerging-turtle alpha fade (§8)
- 🟥 Splash + squash particles, hop scale-pop, death flash (§8)
- 🟥 HUD overlay: score / level / high score, lives icons, timer bar (§9)

### M8 — Menus & stats
- 🟥 Adopt the generic FS.GG game shell (FS-GG/FS.GG.Rendering#991): main menu (title + Start/Config/Exit), Esc pause routing, Settings with screen resolution + fullscreen, and in-game key rebinding of the §3 controls, persisted — the game provides its name + key→command map + play update/view; the shell provides the rest, no bespoke menu system (§9.1)
- 🟥 Game-specific Config rows over the shell (difficulty preset, volume/sound, grid overlay) apply live + persist (§9.1, §12, §13)
- 🟥 `RunStats`/`LifetimeStats` accumulation + persist (§9.2, §13)
- 🟥 Deaths-by-cause bar + score-by-level line charts (§9.2)
- 🟥 Difficulty presets re-parameterize the tuning table; rules stay preset-invariant (§9.1, §12)

### M9 — Audio
- 🟥 `AudioEffect` cues per event table, `Audio.interpret`, volume clamp `[0,1]` (§10)
- 🟥 Looping `arcade-theme` music on `Title`/`Playing`, stop on `GameOver` (§10)

### M10 — Acceptance & determinism
- 🟥 All 21 acceptance scenarios green (§14)
- 🟥 Seed + `Tick`/`Hop` replay is bit-identical (§13) — AC #16

### Stretch — deferred (post-v1)
- ⬜ Input buffering: queue one hop during an in-progress hop (§15.1)
- ⬜ Crocodiles: lethal-front/rideable-back log segment + home-slot crocs (§15.2)
- ⬜ Snake on the median hazard at higher levels (§15.3)
- ⬜ Otters / divers as additional river hazards (§15.4)
- ⬜ Two-frog co-op, shared lives on one board (§15.5)
- ⬜ Daily seed challenge leaderboard via deterministic RNG (§15.6)
- ⬜ Variable canvas / responsive grid for non-1280×720 windows (§15.7)
- ⬜ Touch/swipe directional controls for mobile (§15.8)
