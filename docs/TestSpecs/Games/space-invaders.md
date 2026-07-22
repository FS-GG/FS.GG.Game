---
title: "Space Invaders"
slug: space-invaders
category: games
complexity: simple
genre: "Fixed-shooter / arcade"
target_session_minutes: 5
stack: { rendering: "FS.GG.Rendering (Skia/OpenGL)", framework: "FS.GG.Game.Core (FixedStep for the march; Rng for determinism)", arch: "Elmish/MVU", lang: "F#" }
status: spec
---

# Space Invaders

## 1. Overview
You are the last planetary defense cannon, pinned to the bottom of the screen while a
grid of 55 aliens marches relentlessly left, right, and downward toward you. The core
verb is **shoot up while dodging what falls down**. The tension is mechanical and
beautiful: every alien you kill makes the survivors march *faster*, so clearing a wave
is a race against an acceleration curve you yourself trigger. Hide behind crumbling
bunkers, snipe the bonus UFO that streaks across the top, and survive as many waves as
your three lives allow. It is simple, readable, and ruthless.

## 2. Core Game Loop
**Moment-to-moment:** scan the formation → position cannon → fire (one shot in flight)
→ dodge descending bombs → duck behind a bunker → repeat. Secondary beat: a UFO appears
periodically; break rhythm to gamble a shot on it for bonus points.

**Wave loop:** all 55 aliens destroyed → brief pause → next wave spawns one row lower and
slightly faster → repeat indefinitely (endless, score-chasing).

**Session loop:** Title → Play → (lose a life on hit/invasion, respawn if lives remain)
→ Game Over (formation reaches the cannon line OR lives exhausted) → Score + High Score
→ Restart.

**The self-inflicted clock.** The loop's signature tension is that the player *is* the
difficulty dial. There is no timer counting down — the acceleration comes entirely from
your own kills (§4.4). Clearing the bottom rows first (10-pt Octopi) strips the safe,
slow aliens and leaves the formation thin and frantic; clearing top-down (30-pt Squids
first) banks points but keeps the march slow and the screen crowded. Neither is "correct":
the loop rewards a read of your own nerve. A skilled run deliberately paces its kills so
the final few aliens don't hit their ~62 ms sprint (§4.4) until the player is set up
directly beneath them.

**Decision cadence.** With one bullet in flight (§4.2) the atomic unit of play is a
single committed shot: aim, fire, watch it travel, and only then re-plan. At 620 px/s a
shot crosses the ~500 px from cannon to formation in ~0.8 s, so the player is always
resolving *the last shot* while positioning for the next — the one-bullet rule is what
converts a twitch shooter into a rhythm of commitment. The UFO (§4.8) is the deliberate
rhythm-breaker: spending your single bullet on a streaking bonus target means ~1.5 s with
no shot covering the descending formation, a real gamble the loop asks you to price.

**The respawn beat.** A cannon hit freezes input for the 1.0 s `PlayerDying` window (§5),
during which the march, bombs, and UFO keep moving — the world does not pause for your
death. Respawn drops you back at center x=640 (§11) into whatever the formation has become,
so losing a life late in a fast wave is punishing precisely because you re-enter mid-sprint.

## 3. Controls & Input
Keyboard is primary. Movement is **held** (continuous while down); firing is
**edge-triggered** (one discrete shot per key-down, subject to cooldown + one-bullet rule).

| Input | Action | Model |
| --- | --- | --- |
| `Left Arrow` / `A` | Move cannon left | Held (continuous) |
| `Right Arrow` / `D` | Move cannon right | Held (continuous) |
| `Space` | Fire player shot | Edge-triggered (key-down only) |
| `Enter` | Start game / restart from Title or Game Over | Edge-triggered |
| `P` / `Esc` | Toggle pause | Edge-triggered |

Notes: holding `Space` does **not** auto-fire; the player must release and re-press, but
in practice the one-bullet-in-flight rule is the binding constraint. Simultaneous
left+right cancels to zero horizontal velocity.

## 4. Mechanics (detailed)
All positions in logical pixels on a 1280×720 playfield. Origin top-left, +x right,
+y down. The "ground line" (top of bunkers / cannon deck) is at **y = 660**. The
**invasion line** (game-over if any alien reaches it) is at **y = 620**.

### 4.1 Player cannon movement
- Horizontal only, on rail at **y = 640** (cannon top), sprite **48×24 px**.
- Speed: **320 px/s**, no acceleration, no friction (instant start/stop — classic feel).
- Clamped to `[24, 1256]` for the cannon center x (keeps the 48-wide sprite on screen).
- Velocity is `(RightDown − LeftDown) · 320` px/s, so the two held-key booleans sum to
  exactly one of {−320, 0, +320}. Both keys down → **0** (the §3 cancel), matching a
  neutral stick — not "last key wins".
- `CannonX` is a **float**; integrate as `CannonX += vel · dt` then clamp, so sub-pixel
  motion accumulates smoothly and render rounds to the nearest whole pixel for a crisp
  sprite. A frame at dt = 1/60 s moves the cannon **≈ 5.33 px**; 60 such frames sum to
  **320 px** (drives AC #1's 1.0 s → +320 px assertion).
- At a clamp wall the cannon **parks**: holding into the wall pins `CannonX` at 24 or 1256
  with zero residual velocity, and reversing direction resumes motion on the same frame
  the opposite key is read (no wind-up, no stickiness).
- Movement is **live only in `Playing`**. In `PlayerDying`, `WaveCleared`, `Paused`,
  `Title`, and `GameOver` the held-key booleans are still tracked (so a key held across a
  respawn resumes correctly) but produce no motion — the cannon is frozen at its last x.

### 4.2 Player shot ("laser")
- **One player bullet in flight at a time.** Firing is blocked until the previous bullet
  is destroyed (off-screen or on hit).
- Cooldown: **0.30 s** minimum between shots even after a bullet clears (prevents
  machine-gunning on fast kills).
- Bullet size **4×16 px**, velocity **−620 px/s** (upward), spawns at cannon muzzle
  (center x, y = 636).
- Destroyed when: hits an alien, hits a bunker block, hits the UFO, hits an enemy bomb
  (mutual cancel — see 4.6), or y < 0 (top edge).
- **Muzzle spawn** is at the cannon's *current* center x, y = 636 (4 px above the rail).
  The bullet then integrates independently of the cannon — moving the cannon after firing
  does **not** steer the shot in flight.
- **Cooldown vs one-bullet.** Both gates must clear to fire: `Bullet = None` **and**
  `FireCooldown ≤ 0`. Because a bullet typically lives longer than 0.30 s, the one-bullet
  rule is usually the binding constraint; the cooldown only bites after a *fast* clear —
  e.g. a point-blank kill on a low alien, where the bullet is destroyed almost immediately
  and the 0.30 s floor prevents machine-gunning (AC #3).
- **No pierce, no fractional travel skips.** The bullet resolves collisions each tick
  against its integrated AABB; it does not tunnel between two aliens in a single 16.7 ms
  step at 620 px/s (≈ 10.3 px/frame, well under the 24 px vertical pitch), so no
  swept-collision correction is required at v1 speeds.
- **Single-target resolution.** If an integrated bullet AABB overlaps more than one
  eligible target in the same tick, it resolves the **lowest (largest-y) alien first** —
  the one physically nearest the muzzle — awards that alien's points, and is consumed
  before any farther alien is tested. A bullet never scores two kills in one tick.

### 4.3 Alien formation
- Grid **5 rows × 11 columns = 55 aliens**.
- Cell spacing: **48 px horizontal pitch**, **48 px vertical pitch**. Alien sprites are
  sized per type (see the table below), centered in cells.
- Initial formation top-left at **(x = 180, y = 120)** for the top row; rows stack
  downward. Formation bounding box is computed from *living* aliens only.
- Row identity / point values (top to bottom of the spawned grid):
  | Rows | Type | Sprite | Points |
  | --- | --- | --- | --- |
  | Row 0 (top) | "Squid" (small) | 24×24 | **30** |
  | Rows 1–2 | "Crab" (medium) | 32×24 | **20** |
  | Rows 3–4 (bottom) | "Octopus" (large) | 36×24 | **10** |

- **Derived screen position.** An alien never stores an absolute position; its screen x is
  `FormationX + Col · 48` and screen y is `FormationY + Row · 48`, both offset to center the
  per-type sprite in its 48×48 cell. Every alien therefore moves for free when the formation
  origin steps or drops (§4.4, §4.5) — the grid is the single source of truth, aliens are
  offsets into it.
- **Living bounding box.** The formation's collision/edge box spans only cells that still
  hold an Alive or Dying alien. Clearing an entire outer column shrinks the box inward, so a
  formation with its right columns gone can march further right before an edge bounce (§4.5) —
  a deliberate consequence a player can exploit to buy horizontal room.
- **Walk animation.** Each alien holds a 2-frame walk cycle; the frame **toggles on every
  march step** (§4.4), not on a wall-clock timer, so the whole formation animates in lockstep
  with its own footfall and the animation visibly speeds up exactly as the march does.
- **Interior gaps are permanent.** Killed aliens leave holes; survivors do **not** close ranks
  or re-flow into empty cells. A column emptied from the bottom raises that column's
  "lowest living alien" (the only bomber, §4.6) to the next survivor up.

### 4.4 March step & acceleration (the heart)
The formation moves in **discrete steps**, not continuously. On each step, every living
alien jumps **8 px** in the current horizontal direction. The interval between steps
shrinks as aliens die — fewer aliens = faster march.

- Step interval is a function of living alien count `n` (0..55):
  `stepIntervalMs = lerp(48 → 800, n/55)` clamped, i.e.
  `intervalMs = 48 + (800 - 48) * (n / 55.0)`.
  - n = 55 → **800 ms/step** (slow, ominous).
  - n = 27 → **~417 ms/step**.
  - n = 1 → **~62 ms/step** (frantic single-alien sprint).
- Per **wave**, multiply the resulting interval by a wave speedup factor
  `waveFactor = 0.92 ^ (wave - 1)` (each wave ~8% faster), clamped so interval ≥ **40 ms**.
- The march is a *fixed step on a timer*, independent of frame dt; the accumulator
  carries leftover time so timing is deterministic regardless of FPS.
- **`n` is the live count** — aliens that are Alive **or** Dying (still in the formation),
  not yet culled to Dead. A Dying alien keeps marching and keeps counting for the 0.18 s of
  its explosion (§5), so the interval steps down the instant the *next* alien is culled, not
  the instant it is hit. (§14 AC #6 speaks of aliens "destroyed" = culled to Dead.)
- **Interval reference points** (wave 1, `waveFactor = 1.0`), for calibration and snapshot
  tests:
  | Living `n` | Step interval | Feel |
  | --- | --- | --- |
  | 55 | 800 ms | opening crawl |
  | 41 | ~608 ms | first row down |
  | 27 | ~417 ms | halfway |
  | 14 | ~239 ms | tense |
  | 5 | ~116 ms | scramble |
  | 1 | ~62 ms | single-alien sprint |
- **Multiple steps per frame are legal.** At the fastest intervals (≤ 40 ms floor) a single
  16.7 ms tick can still owe **at most one** step; but on a stalled frame (dt clamped to
  0.05 s, §7) the accumulator may owe several — drain them in order (§13), running an
  edge-test before each, so a lag spike never lets the formation skip a wall bounce.
- **Accumulator resets on wave transition**, not on kills: `StepAccumMs` is zeroed when a
  new wave's grid is built (§6) so wave 2 opens on a clean 800 ms · `waveFactor` beat rather
  than inheriting wave 1's banked remainder.
- **Tempo drives the music.** The `march` track's playback tempo tracks this interval (§10):
  as `n` falls the 4-note bass loop accelerates in lockstep, which is why the audio *is* the
  difficulty readout — the player hears the acceleration before reading it.

### 4.5 Edge detection & drop
- After the formation *would* step, test the living-alien bounding box against the side
  walls: left wall **x = 24**, right wall **x = 1256**.
- If the next horizontal step would push any living alien past a wall:
  1. **Do not** apply the horizontal step.
  2. Drop the entire formation **down by 24 px** (one row's worth of descent), and
  3. **Reverse** horizontal direction.
- Only one drop+reverse per step (no double-bounce in a single frame).
- If, after a drop, the bottom of any living alien reaches **y ≥ 620** (invasion line),
  the game ends immediately (loss — see 11).
- **Only the wall in the direction of travel is tested.** Moving right, test the living box's
  right edge against x = 1256; moving left, test the left edge against x = 24. The opposite
  wall is irrelevant that step, so a narrow surviving column can slide fully across.
- **The test is predictive.** Edge detection asks *"would this step's +8/−8 px push the box
  past the wall?"* before committing. On the triggering step the horizontal move is cancelled
  entirely and replaced by the drop+reverse; the aliens do **not** first nudge into the wall
  and then bounce — they descend cleanly on the step where contact would occur (AC #7).
- **Drop applies to the whole formation**, Dying aliens included, via a single change to
  `FormationY` (+24 px). Because positions are derived (§4.3) no per-alien update is needed.
- **Invasion is checked after the drop resolves**, before any bomb or bullet in that frame
  is processed (§13): a drop that crosses y ≥ 620 ends the game with no further collision
  resolution that frame — you cannot be "saved" by a same-frame kill of the invading alien.
- **A wall-parked cannon offers no shelter.** The formation descends over the cannon rail;
  reaching the invasion line is a loss regardless of horizontal alignment with the cannon.

### 4.6 Alien bombs (enemy fire)
- Only the **lowest living alien in each column** may drop bombs.
- Fire cadence: every **1.0 s** the game attempts a drop; probability scales with march
  speed and wave. Base attempt picks a random eligible column with chance
  `pFire = clamp(0.35 + 0.05*(wave-1) + (55 - n)*0.004, 0, 0.95)` per attempt.
- Max **3 enemy bombs** on screen at once.
- Bomb size **6×16 px**, velocity **+220 px/s** (downward, +10 px/s per wave).
- Two visual/behavior types alternate (cosmetic in v1): straight bomb and zig-zag bomb;
  v1 may implement straight only.
- A bomb is destroyed when: it hits the cannon (player loses a life), hits a bunker block
  (erodes it), hits the player bullet (both cancel — 50% chance the bomb survives in the
  arcade; v1 = always mutual destroy), or y > 720.
- **Spawn point.** A bomb spawns at the firing alien's center x, at the bottom edge of its
  sprite, so bombs emerge from beneath the shooter and immediately begin descending — never
  from inside the formation.
- **Eligible-column set.** Each of the 11 columns contributes at most its single lowest
  living alien as a candidate. Emptied columns contribute nothing; a column whose bottom
  aliens are killed promotes the next survivor up to bomber duty (§4.3). With the formation
  thinned to a few survivors, only those few columns can fire — sparse formations bomb less.
- **Per-attempt resolution** (every `bombInterval` = 1.0 s): if `Bombs.Length < maxBombs (3)`,
  draw one eligible column uniformly (`Rng.nextInt`, §13); roll `pFire`; on success spawn a
  bomb from that column. On a full bomb list or an empty eligible set the attempt is skipped —
  it does **not** carry over or fire early.
- **Type alternation** cycles Straight → ZigZag → Straight … on each *successful* spawn
  (`Rng.nextBool` seeds the first, §13). v1 may render/behave ZigZag as Straight, but the
  alternation state still advances so the deterministic stream is stable when §15.1 lands.
  ZigZag's intended path (deferred): a ±16 px horizontal sway with a 0.25 s half-period,
  same +y descent — a lateral wobble, never upward.
- **Bombs do not interact with each other**, the UFO, or the formation; they collide only
  with the cannon, bunkers, the player bullet, and the bottom edge (y > 720).
- **Fire is suppressed outside `Playing`** — no drops during `PlayerDying`, `WaveCleared`,
  `Paused`, or menus — but bombs already in flight keep descending through `PlayerDying`
  (the world does not stop for the player's death, §2), then are cleared at wave transition.

### 4.7 Bunkers (destructible cover)
- **4 bunkers**, evenly spaced along the bottom. Bunker centers at
  **x = 240, 520, 800, 1080**; top at **y = 560**.
- Each bunker is a grid of **destructible blocks**: **22 cols × 16 rows** of **4×4 px**
  cells (≈ 88×64 px), pre-carved into the classic arch/notch silhouette (bottom-center
  doorway, sloped top corners) by masking out cells at init.
- Erosion: any bullet (player or enemy) that overlaps a *solid* cell destroys a small
  cluster — the hit cell plus its 4-neighborhood within a **6 px radius** (a small bite),
  then the bullet is consumed.
- Bunkers do **not** regenerate between lives. They **reset fully** at the start of each
  new wave.
- Aliens that physically overlap a bunker (after enough drops) erode the cells they pass
  through.
- **Erosion bite shape.** On impact, mark false every solid cell whose center lies within a
  **6 px radius** of the impact point — roughly a 3×3-cell cluster (the hit cell plus its
  4-neighborhood and clipped diagonals). Erosion is one-directional: cells only ever go
  solid → gone within a wave; nothing regrows mid-wave. A bunker with all cells false is
  inert — bullets and bombs pass straight through the empty footprint.
- **Both fire types erode.** A player bullet climbing and an enemy bomb falling each carve
  the bunker from their own side, so a shared bunker is chewed from top and bottom at once;
  the classic result is a bunker hollowed to a thin, unreliable shell by mid-wave.
- **The pre-carved doorway is real geometry.** The bottom-center notch and sloped top
  corners are cells masked false at init (§5), so a shot can pass *through* the doorway
  without eroding anything — skilled play fires through existing gaps to preserve cover.
- **Alien erosion cadence.** A living alien overlapping bunker cells erodes the overlapped
  solid cells **once per march step** (not per frame), so a descending formation grinds
  through cover at march tempo rather than instantaneously — cover buys time, not safety.
- **Impact is resolved against the overlapped bunker only** (§13 AABB pre-filter): a
  projectile tests the single bunker whose 88×64 footprint it is inside, and against that
  bunker's solid cells; misses (all overlapped cells already false) do not consume the
  projectile, so a bullet threading a gap continues to the formation.

### 4.8 Mystery UFO
- Spawns from off-screen, traveling horizontally across the top at **y = 80**.
- Trigger: every **20–30 s** (randomized) **and** only while ≥ 8 aliens remain.
- Size **48×20 px**, speed **150 px/s**, direction random (enters left→right or
  right→left), despawns when fully off the opposite edge.
- Scoring on hit: pseudo-random but classic-flavored set
  `{50, 100, 150, 200, 300}` chosen by a seeded table; v1 may use a simple
  `[100;50;150;100;300;...]`-style lookup keyed on the player's cumulative shot count.
- **One UFO at a time.** While `Ufo` is `Some`, `UfoTimer` does not spawn a second — the
  timer only re-arms and counts toward the *next* spawn once the current UFO despawns
  (destroyed or fully off the far edge). The next interval is redrawn in `[20, 30] s`
  (`Rng.nextInt`, §13).
- **Spawn side and lane.** Entry side is a coin-flip (`Rng.nextBool`): left→right enters at
  x just off −24 heading +150 px/s, right→left at x just past 1304 heading −150 px/s, always
  on the y = 80 lane. It never changes direction and never drops; it crosses in ≈ 8.5 s of
  clear travel.
- **Only the player bullet can destroy it.** The UFO is immune to bombs, ignores bunkers,
  and never collides with the formation (it flies in a lane above everything). A hit despawns
  it, awards the table value, and re-arms `UfoTimer` (§7 step 7).
- **Suppression is a spawn gate, not a kill gate.** The `< 8` living-alien rule (§4.8) only
  blocks *spawning*; a UFO already airborne when the count drops below 8 finishes its pass
  and remains hittable for full value.
- **Bonus-table indexing.** The value is `table[ShotCount mod table.Length]` — keyed on the
  player's cumulative shot count so a fixed seed and fixed input script yield the same UFO
  value every run (AC #13, AC #17). The 300-pt entry is the coveted "perfect" hit veterans
  count shots to line up.
- **Audio.** The `ufo-warble` loop plays while the UFO is on screen and stops on despawn
  (§10), giving an audible cue to break rhythm and take the gamble (§2).

### 4.9 Collision resolution order & precedence
Collisions are resolved in a fixed per-tick order (mirroring the §7 `Tick` step list) so
outcomes are deterministic and never depend on list iteration order:

1. **Player bullet** (§7 step 3), tested in this precedence against its swept AABB:
   UFO → alien (lowest/nearest first, §4.2) → enemy bomb (mutual cancel, §4.6) → bunker
   cell → top edge. The first hit consumes the bullet; no second resolution that tick.
2. **March + invasion** (step 4): a drop crossing y ≥ 620 ends the game *before* bombs are
   integrated (§4.5), so invasion always wins a same-frame race.
3. **Bombs** (step 6), each tested: cannon → player bullet (if it survived step 1 and both
   still overlap) → bunker cell → bottom edge. A bomb hitting the cannon is resolved before
   that bomb can also erode a bunker — one bomb, one effect.

Ties within a step resolve by the precedence above, never by which entity was spawned
first. All hitboxes are **axis-aligned rectangles**; overlap is inclusive-edge AABB (a
1 px shared border counts as contact). A single projectile affects at most one target per
tick, and score is applied at the moment of resolution, before the projectile is cleared.

### 4.10 Moment-to-moment feel & tuning
- **Cannon "snap."** Zero accel/friction (§4.1) is intentional: the cannon must feel like a
  turret slaved to the key, not a vehicle with momentum. Any smoothing here would blunt the
  dodge, whose whole skill is reading a bomb's fall and stepping out on the exact frame.
- **Readable bullet travel.** At 620 px/s a shot is on screen ≈ 1.06 s from muzzle to top
  edge, long enough to *see* a miss sail past and re-aim — the one-bullet rule (§4.2) turns
  that visible travel time into the core pacing device (§2).
- **The bomb-dodge window.** A bomb at 220 px/s (wave 1) falls the ~560 px from the
  formation floor to the cannon rail in ≈ 2.5 s; per added wave (+10 px/s, §4.6) that window
  tightens by ~0.1 s. A player standing still under a fresh bomb has roughly a cannon-width
  (48 px → 0.15 s at 320 px/s) of side-step to clear it.
- **The acceleration cliff.** Because interval is linear in `n` (§4.4) but *feels*
  exponential, the danger spikes late: the last ~10 aliens cover the 116 ms → 62 ms range,
  nearly doubling tempo while you have the fewest, most scattered targets. The intended
  "oh no" moment of every wave lives here.
- **Bunker economy.** 4 bunkers × ~250 solid cells (post-carve) is a spendable resource with
  no refund until wave clear (§4.7). Hiding trades cover for time; the balance target is that
  a cautious player exhausts most cover by the time the wave is cleared, entering the next
  wave's fresh bunkers (§6) having *earned* the reset.

## 5. Entities / Game Objects

### Player cannon
- Properties: `pos: x (rail at y=640)`, size 48×24, 1 hitbox.
- States: `Alive → Hit (death anim ~1.0 s, input frozen) → Respawn (if lives>0) | GameOver`.
- Created at wave/game start at center (x = 640). Destroyed on game over.

### Player bullet
- One max. Properties: pos, vel −620 px/s, size 4×16. Created on fire; destroyed per 4.2.

### Alien
- Properties: `gridRow, gridCol, alienType, alive, screenPos (derived from formation
  origin + cell offset)`, size per type, points per 4.3.
- States: `Alive → Dying (explosion sprite ~0.18 s) → Dead`. Animates a 2-frame walk
  cycle toggled on each march step.

### Alien bomb
- Up to 3. Properties: pos, vel +220 px/s, type (straight|zigzag), size 6×16.

### Bunker block
- Many tiny cells. Properties: `solid: bool` per 4×4 cell. No motion. Eroded by bullets.

### UFO
- 0 or 1 active. Properties: pos, vel ±150 px/s, size 48×20, bonusValue.

### Formation (implicit entity)
- Not a stored object but an emergent one: `FormationX`, `FormationY`, `MarchDir`, and the
  `Aliens` list *are* the formation. Its living bounding box (§4.3), march tempo (§4.4), and
  edge behavior (§4.5) are all derived each tick. Killing aliens reshapes this implicit
  entity — the sole thing the player can act on to change the game's pace.

**Lifecycle notes.**
- **Cannon** — the `Alive → Hit → Respawn | GameOver` chain (above) runs entirely in the
  `PlayerDying` phase (§7); input is frozen but the rest of the world integrates, and the
  0.12 s white flash sprite (§8) fires at the moment of the hit, not at respawn.
- **Player bullet** — a strict `None`/`Some` singleton; there is no bullet pool, and a new
  shot cannot exist until the current one resolves to `None` (§4.2). Its removal is what
  re-opens firing, so bullet lifetime *is* the fire-rate limiter.
- **Alien** — `Dying of float` counts down 0.18 s of explosion while still marching and
  still bombing-eligible until culled to `Dead` and removed from the list (§4.3, §4.4).
  Points are awarded on the `Alive → Dying` transition, not on cull, so score is immediate.
- **Alien bomb** — capped at 3 live; a bomb is only ever created by the §4.6 attempt and
  removed on cannon/bunker/bullet/edge contact. No bomb persists across a wave transition.
- **Bunker block** — the only mutable-in-place entity: a `bool[,]` grid flipped solid→false
  by erosion, wholesale rebuilt (re-carved) at each wave start (§4.7, §6).

```fsharp
type AlienType = Squid | Crab | Octopus       // 30 / 20 / 10 pts
type AlienState = Alive | Dying of float       // remaining anim seconds

type Alien =
    { Row: int; Col: int
      Type: AlienType
      State: AlienState }

type BombKind = Straight | ZigZag
type Bomb = { Pos: float * float; Vel: float; Kind: BombKind }

type Bullet = { Pos: float * float; Vel: float }

type Ufo = { Pos: float * float; Vel: float; Bonus: int }

// Bunkers: 4 grids of bool cells (true = solid)
type Bunker = { OriginX: float; OriginY: float; Cells: bool[,] }  // [22,16]
```

## 6. World / Levels / Progression
- Playfield: **1280×720** logical px (letterboxed/scaled to the window by the renderer).
- Key lines: UFO lane y=80, formation start y=120, bunker top y=560, ground y=660,
  cannon rail y=640, invasion line y=620, side walls x=24 / x=1256.
- **Waves** are endless. Each new wave:
  - Respawns the full 5×11 grid, but with the **formation start y lowered by 24 px per
    wave** (wave 1 → y=120, wave 2 → y=144, …), capped at y=240 so it never starts
    unwinnably low.
  - Applies `waveFactor = 0.92^(wave-1)` to march interval (4.4).
  - Increases bomb fall speed (+10 px/s) and `pFire` (4.6).
  - Resets all 4 bunkers to full.
- Difficulty ramp is therefore two-layered: *within* a wave (kills accelerate the march)
  and *across* waves (lower start, faster steps, deadlier bombs).

**Per-wave progression** (Classic preset; all values derive from §12 tunables):

| Wave | Formation start y | `waveFactor` | 55-alien step | Bomb speed | `pFire` base |
| --- | --- | --- | --- | --- | --- |
| 1 | 120 | 1.000 | 800 ms | 220 px/s | 0.35 |
| 2 | 144 | 0.920 | 736 ms | 230 px/s | 0.40 |
| 3 | 168 | 0.846 | 677 ms | 240 px/s | 0.45 |
| 4 | 192 | 0.779 | 623 ms | 250 px/s | 0.50 |
| 5 | 216 | 0.716 | 573 ms | 260 px/s | 0.55 |
| 6 | 240 (cap) | 0.659 | 527 ms | 270 px/s | 0.60 |
| 7+ | 240 (cap) | 0.659^… | shrinking | +10/wave | +0.05/wave, ≤ 0.95 |

- **The start-y cap (y = 240) is the winnability floor.** From wave 6 on the formation always
  opens at the same height, so the cross-wave ramp continues purely through *tempo* and
  *bomb pressure*, never by starting the aliens so low the player can't clear a first row
  before invasion. Waves remain survivable indefinitely in principle; the run ends on skill.
- **`pFire` climbs but is clamped to 0.95** (§4.6), so even at high waves a fraction of fire
  attempts whiff — the enemy never achieves perfectly saturated fire.
- **Inter-wave state.** On `WaveCleared → Playing (Wave+1)`: rebuild the 5×11 grid, re-carve
  all 4 bunkers, clear `Bombs` and any airborne UFO, zero `StepAccumMs` and `BombTimer`, and
  redraw `UfoTimer` — but **preserve** `Score`, `Lives`, `HighScore`, `ShotCount`, and
  `CannonX`. The player carries their position and standing into the fresh formation.
- **`MarchDir` resets to +1 (rightward)** each wave, so every wave opens with a rightward
  crawl regardless of which way the previous wave ended.
- **No level geometry changes across waves** — the same walls, lines, bunker anchors, and UFO
  lane persist; only the tunables above scale. There are no alternate arenas in v1.

## 7. State Model (Elmish/MVU)

### Model
```fsharp
type Phase =
    | Title
    | Playing
    | Paused
    | PlayerDying of float          // seconds of death anim remaining
    | WaveCleared of float          // inter-wave pause remaining
    | GameOver

type Model =
    { Phase: Phase
      Wave: int
      Score: int
      HighScore: int
      Lives: int

      // Player
      CannonX: float
      FireCooldown: float           // seconds until next shot allowed
      Bullet: Bullet option         // one-in-flight rule

      // Formation
      Aliens: Alien list            // only Alive/Dying tracked; Dead removed
      FormationX: float             // top-left origin x of grid
      FormationY: float             // top-left origin y of grid
      MarchDir: int                 // -1 left, +1 right
      StepAccumMs: float            // timer accumulator for next step
      ShotCount: int                // drives UFO bonus table

      // Enemy fire
      Bombs: Bomb list              // <= 3
      BombTimer: float

      // UFO
      Ufo: Ufo option
      UfoTimer: float               // seconds until next spawn attempt

      // Cover
      Bunkers: Bunker list          // 4

      // Input (held keys)
      LeftDown: bool; RightDown: bool

      Rng: Rng }                    // FS.GG.Game.Core, seeded for determinism
```

### Msg
```fsharp
type Msg =
    | Tick of float                 // dt in seconds, ~1/60
    | KeyDown of Key
    | KeyUp of Key
    | StartGame                     // Enter on Title/GameOver
    | TogglePause
```

### update — important cases
- **`KeyDown Left/Right`** → set `LeftDown/RightDown = true`. **`KeyUp`** → false.
- **`KeyDown Space`** (Phase=Playing) → if `Bullet=None && FireCooldown<=0`, spawn bullet
  at muzzle, set `FireCooldown=0.30`, `ShotCount+1`.
- **`KeyDown Enter`** → from Title/GameOver: reset to wave 1 fresh Model, Phase=Playing.
- **`TogglePause`** → Playing↔Paused (freezes all Tick integration).
- **`Tick dt`** (Phase=Playing) does the whole simulation in order:
  1. Move cannon by `(RightDown - LeftDown) * 320 * dt`, clamp.
  2. Decrement `FireCooldown` by dt.
  3. Integrate player bullet; test collisions vs aliens / bunkers / UFO / bombs; apply
     score; clear bullet on hit/off-screen.
  4. **March step:** `StepAccumMs += dt*1000`; while `StepAccumMs >= stepInterval`:
     do edge-test → step or (drop+reverse); subtract interval; toggle walk frame. Check
     invasion line → `GameOver`.
  5. Tick `Dying` aliens; drop them to `Dead` (removed) when timer ≤ 0.
  6. **Bombs:** `BombTimer` countdown → attempt fire (4.6); integrate bombs; collisions
     vs cannon (→ lose life), bunkers (erode), player bullet (cancel), off-screen.
  7. **UFO:** `UfoTimer` countdown → maybe spawn; integrate; despawn off-screen.
  8. If `Aliens` empty → `Phase = WaveCleared 1.5`.
- **`Tick`** in `PlayerDying/WaveCleared` advances the respective timer; on
  expiry, respawn cannon (life lost) or start next wave (`Wave+1`, rebuild grid+bunkers).

### view
Pure projection of `Model` → a Skia draw list. No mutation, no I/O. Renders cannon,
bullet, every Alive/Dying alien at its derived screen pos, bombs, bunker solid cells,
UFO, and the HUD. Phase selects overlay (Title text / Paused dim / Game Over panel).

### Subscriptions
- A **60 FPS tick** subscription emits `Tick dt` with `dt` in seconds (clamped to ≤ 0.05
  to survive stalls).
- Keyboard subscription maps physical key events to `KeyDown/KeyUp/StartGame/TogglePause`.

## 8. Rendering (Skia 2D)
Coordinate system = logical 1280×720, origin top-left; renderer scales to the surface.
Single full-redraw per frame (cheap at these entity counts); no dirty-rect needed.

Draw order (back to front):
1. **Background** — solid `#000000`. Optional starfield: 60 static white dots.
2. **Ground line** — 2 px bar at y=660, color `#33FF33` (phosphor green).
3. **Bunkers** — each solid cell as a 4×4 filled rect, color `#00DD55`.
4. **Aliens** — filled rects/sprites per type. Squid `#FFFFFF`, Crab `#9AE6FF`, Octopus
   `#FF66AA`. 2-frame walk cycle = swap between two sprite glyphs on each march step.
   `Dying` aliens draw a 4-point "splat" glyph in `#FFD23F`.
5. **UFO** — `48×20` rounded rect, color `#FF3B30`, optional 3 px outline `#FFFFFF`.
6. **Player cannon** — `#33FF33`, a 48×8 base + 12×16 barrel centered on top.
7. **Bullets** — player laser `#FFFFFF` (4×16); enemy bombs `#FFD23F` (6×16),
   drawn as a 3-segment zig glyph for `ZigZag`.
8. **HUD overlay** — see section 9.

Fonts: a monospace pixel font (e.g. "PressStart2P" if available, else default monospace),
HUD text size 24 px, large titles 64 px. Particle/effects: brief alien explosion (the
Dying glyph) and a 0.12 s white flash sprite on cannon hit. No camera/scrolling.

## 9. UI / HUD / Screens

**HUD (Playing)** — drawn in `#FFFFFF` monospace, 24 px:
- **Score**: top-left, `SCORE 001230` (6-digit zero-padded) at (24, 24).
- **High score**: top-center, `HI 004500`, centered at x=640, y=24.
- **Wave**: top-right, `WAVE 03` ending at (1256, 24).
- **Lives**: bottom-left at (24, 690) — a numeral plus up to 3 small cannon icons.

**Screens:**
- **Title**: centered `SPACE INVADERS` (64 px), subtitle `PRESS ENTER` (24 px) blinking
  at 1 Hz, plus a small scoring legend (30/20/10 + `?=mystery`).
- **Paused**: freeze the play scene, dim with `#000000` @ 50% alpha, centered `PAUSED`.
- **Game Over**: dim scene, centered `GAME OVER` (64 px), `SCORE nnnnnn` and
  `HI nnnnnn` below, `PRESS ENTER` prompt. If new high score, show `NEW HIGH SCORE!` in
  `#FFD23F`.

### 9.1 Menu & configuration — the shared game shell

Space Invaders uses the **generic FS.GG game shell** (FS-GG/FS.GG.Rendering#991) — the same
menu/start screen and settings every FS.GG game shares — rather than a bespoke per-game menu.
The game supplies only its **name**, its **key→command map** (the rebindable actions from §3
Controls), and its play `update`/`view`; the shell provides everything below.

- **Main menu / start screen** — the game's name (**SPACE INVADERS**) as the title label, with
  **Start**, **Config**, and **Exit**.
- **`Esc` from gameplay** opens the pause menu (Resume · Config · Exit to menu) over the same
  shell; `Esc` again resumes.
- **Config / Settings**, all applied live and persisted across restarts (alongside the high
  score, §13):
  - **Screen resolution** and **fullscreen** (windowed / borderless / fullscreen), driven
    through the SkiaViewer window-behavior + `LogicalCanvas` letterbox seam.
  - **Key rebinding** — the player remaps this game's controls (the §3 actions) via the
    `Controls.KeyRebind` UI over the `KeyboardInput.Keymap` mechanism; bindings persist via
    `KeymapCodec` (JSON), beside this game's other saved config (§13).
  - Game-specific rows are added as extra Config rows over the shell: **Difficulty** (the §12
    tunable preset — Easy / Classic / Insane, the same table that drives `cannonSpeed`,
    `waveSpeedup`, `bombBaseFireP`, `startLives`), **Master volume**/**Sound** (route to
    `Audio.setMasterVolume`, §10, clamped `[0,1]`, `0.0` = silence), and **CRT scanlines**
    (toggles the retro post-effect, §15 stretch #4). The menu, Esc routing, display settings,
    and rebind screen come from the shell.

The shell is pointer- and keyboard-navigable over the interactive Controls host (the
`fs-gg-skiaviewer` "game → pointer host" recipe). It is a shared dependency, so Space Invaders
does **not** re-specify menu-stack/cursor/settings machinery of its own. The **Stats & charts**
screen (§9.2) is a Space-Invaders-specific screen reached as a Config/menu row.

### 9.2 Stats & charts screen
The Stats screen visualizes **the last run** and **lifetime** play. It reads a `Stats`
snapshot (never live physics), so it is a pure, deterministic render reachable from Title,
Game Over, and Pause. Chart-design choices below follow the project dataviz conventions
(form-first, validated colorblind-safe categorical palette, single axis, identity by entity).

**Tracked per run** — `MatchStats`, accumulated in the §7 `Tick` step, snapshotted on the
`GameOver` transition (§11):

| Field | Type | Updated |
|-------|------|---------|
| `killsTop` | `int` | +1 when a top-row **Squid** enters `Dying` (§4.3, step 3) |
| `killsMid` | `int` | +1 when a middle-row **Crab** enters `Dying` |
| `killsBottom` | `int` | +1 when a bottom-row **Octopus** enters `Dying` |
| `ufoHits` | `int` | +1 per **UFO** destroyed by the player bullet (§4.8) |
| `shotsFired` | `int` | +1 per player shot spawned (mirrors §7 `ShotCount`) |
| `shotsHit` | `int` | +1 per bullet that strikes an alien or the UFO |
| `firedByWave` / `hitByWave` | `int list` | per-wave `shotsFired`/`shotsHit`, appended on `WaveCleared` |
| `wavesCleared` | `int` | +1 on each `WaveCleared` transition (§6) |
| `shieldsRemaining` | `int` | solid bunker cells left across the 4 bunkers at snapshot |
| `livesLost` | `int` | +1 per cannon hit → `PlayerDying` (§4.6, §11) |

`accuracy% = 100 · shotsHit / max(1, shotsFired)` is derived (not stored). **Lifetime** —
`LifetimeStats`, persisted (§13): `highScore`, `gamesPlayed`, `bestAccuracy`, `mostWaves`,
`ufosHitTotal`.

**Layout** (logical 1280×720): a KPI tile row across the top, two charts below.

```
┌──────────────────────────── STATS ────────────────────────────┐
│ ┌ HIGH SCORE ┐ ┌ ACCURACY ┐ ┌  WAVES  ┐ ┌ UFOS HIT ┐          │  ← KPI stat tiles
│ │  012450    │ │   38 %   │ │   07    │ │    11    │          │
│ └────────────┘ └──────────┘ └─────────┘ └──────────┘          │
│                                                                │
│  Kills by alien type                Shots fired vs hit         │
│  ▇▇                             40 ┤        ╭── fired          │
│  ▇▇        ▇▇                       │    ╭─╯╭─╯                │
│  ▇▇  ▇▇    ▇▇    ▇▇                 │ ╭─╯╭─╯── hit              │
│  Top Mid  Bot   UFO              0 ┼──────────────► wave #     │
└────────────────────────────────────────────────────────────────┘
     ↑/↓ scope:  ▸ This Run · Lifetime            ESC — Back
```

**Charts** (rendered in Skia with the same draw-list discipline as §8):

1. **Kills by alien type** — *form: per-category magnitude → bars.* x = alien category
   (`Top`/Squid, `Middle`/Crab, `Bottom`/Octopus, `UFO`), y = kill count. **Single series**,
   so one hue and no legend — comparing magnitudes across categories is one color. Bars are
   4 px-rounded at the data end with a 2 px surface gap between them. Fill `#2a78d6` (light)
   / `#3987e5` (dark) — validated categorical slot 1.
2. **Shots fired vs hit** — *form: change over an ordered index → line.* x = wave number,
   y = cumulative shots; **two series** (Fired, Hit) → a legend is present and both lines are
   direct-labeled at their right end ("fired"/"hit"), the gap between them reading as
   accuracy. Fired `#2a78d6`, Hit `#1baf7a` (slots 1–2, adjacent-pair CVD-validated). 2 px
   lines, ≥ 8 px end markers, recessive 1 px gridlines in `#3C3C3C`.

Conventions honored: **color follows the entity** (Fired is always slot 1, Hit always slot 2
— never repainted by the scope toggle); **one axis only** (no dual-scale — both series share
the shot-count axis); chart **text uses ink tokens** (`#FFFFFF` primary / `#C3C2B7` muted),
never the series hue; layout is **fixed and deterministic**, so a fixed-seed run (§13)
renders byte-identical for snapshot tests. The `↑/↓` **scope** toggle swaps the data source
This-Run ↔ Lifetime without changing colors.

**Model/Msg hooks:** add `Stats: MatchStats` and `Lifetime: LifetimeStats` to the §7 Model,
and a `Stats of scope:StatScope` case to `Phase`. Accumulate in the `Tick` simulation:
bump `shotsFired` when a bullet spawns (alongside `ShotCount`, §7 step 3 KeyDown Space),
`shotsHit` and the matching `killsTop/Mid/Bottom`/`ufoHits` on bullet-collision resolution
(step 3), `livesLost` on a cannon hit (step 6), and `wavesCleared` plus a `firedByWave`/
`hitByWave` entry on the `WaveCleared` transition (step 8). Snapshot `shieldsRemaining` by
counting solid `Bunkers` cells on `GameOver`, then fold `MatchStats` into `Lifetime` and
persist (§13, beside the existing high score). `OpenStats`/`CloseStats` switch the `Phase =
Stats scope` state; the render is a no-op on physics.

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
| Wave start; tempo tracks step interval (§4.4) | `Audio.playMusic (TrackId "march") true` | `march` | 4-note descending loop whose tempo tracks the step interval (the iconic accelerating thump). |
| Player fires a shot (§4.2) | `Audio.playSfx (SoundId "player-fire") 0.8` | `player-fire` | short pew on shot. |
| Alien enters `Dying` on kill (§4.3) | `Audio.playSfx (SoundId "alien-explosion") 0.8` | `alien-explosion` | noise burst on kill. |
| Bomb/alien hits the cannon (§4.6, §11) | `Audio.playSfx (SoundId "player-death") 0.9` | `player-death` | descending tone on cannon hit. |
| UFO on screen (§4.8) | `Audio.playSfx (SoundId "ufo-warble") 0.6` | `ufo-warble` | looping warble while UFO on screen. |
| Player bullet hits the UFO (§4.8) | `Audio.playSfx (SoundId "ufo-hit") 0.8` | `ufo-hit` | jingle on UFO hit. |
| Bullet erodes a bunker cell (§4.7) | `Audio.playSfx (SoundId "bunker-hit") 0.4` | `bunker-hit` | soft tick (optional). |
| Last alien destroyed → `WaveCleared` (§6) | `Audio.playSfx (SoundId "wave-clear") 0.8` | `wave-clear` | short stings. |
| Game over (§11) | `Audio.stopMusic` + `Audio.playSfx (SoundId "game-over") 0.8` | `game-over` | short stings. |

The iconic 4-note descending marching bass **loop** that speeds up is the game's music track:
request it with `Audio.playMusic (TrackId "march") true` on wave start, and `Audio.stopMusic`
on wave end / game over (its tempo tracking is a host concern; the request is what tests
observe). A mute/settings toggle maps to `Audio.setMasterVolume` (e.g. `Audio.setMasterVolume 0.0`
to silence). **Testing:** collect the frame's
`AudioEffect`s, `Audio.interpret` them, and assert the `AudioEvidence.Requested` sequence for
representative events (e.g. a `KeyDown Space` that fires a shot requests exactly `PlaySfx (SoundId "player-fire", _)`).

## 11. Win / Loss / Scoring
- **Scoring**: Squid 30, Crab 20, Octopus 10 per kill (4.3). UFO awards a value from the
  bonus table (4.8). Score is 6-digit display, internally unbounded.
- **Extra life**: award +1 life at **10,000** points (once). Lives cap at 4 displayed.
- **Lives**: start with **3**. A cannon hit (bomb or alien contact) costs one life and
  triggers `PlayerDying` (1.0 s) then respawn at center if lives remain.
- **Loss (Game Over)** if either:
  - Lives reach 0 after a hit, **or**
  - Any living alien crosses the invasion line **y ≥ 620** (formation reached the deck).
- **"Win"**: there is no terminal win — the game is endless wave survival; clearing a wave
  advances to the next. The objective is a high score.
- **High score** persists across sessions (section 13).

**Scoring edge cases & precision.**
- **Points land on the kill transition**, `Alive → Dying` (§5), so a bullet that strikes an
  alien in the same tick the cannon is destroyed still scores — resolution order (§4.9)
  applies the bullet's points before any life-loss bookkeeping.
- **A bullet in flight during `PlayerDying` still scores.** Death freezes *input*, not the
  bullet already airborne; it continues to integrate and can kill and bank points while the
  cannon respawns (§2). This is the one window where the player scores without acting.
- **UFO points are separate from the extra-life count of kills** and use the §4.8 table, not
  the 30/20/10 schedule; a UFO hit can be the crossing that trips `extraLifeAt` (§4.8, §11).
- **Extra life fires exactly once**, on the first tick `Score` crosses `10000` (from below to
  at-or-above). Awarding it is idempotent — a later score never re-triggers it, and it does
  not re-arm on a new run's reset unless the run itself crosses the threshold again.
- **Lives display caps at 4** icons/numeral; the internal `Lives` is likewise capped so a
  second threshold-style bonus (none exists in v1) could never overflow the HUD.
- **Display is the low 6 digits.** `Score` is internally unbounded; the HUD shows it
  zero-padded to 6 digits (§9), and a score ≥ 1,000,000 shows its last six digits — the
  stored value used for high-score comparison stays full-precision.
- **High score updates only on `GameOver`**, comparing the run's final `Score` to the stored
  `HighScore`. Score is monotonic (it only ever increases), so the final score *is* the run's
  peak — no separate running-max is needed. Ties do not overwrite (`>`, not `≥`), so the
  earliest run holds a tied record. `NEW HIGH SCORE!` (§9) shows only on a strict beat.
- **Two simultaneous loss conditions** (a cannon-killing bomb on the same tick a drop crosses
  the invasion line) resolve to `GameOver` once — invasion is checked first (§4.9), so the
  game ends there and the bomb hit is not separately processed.

## 12. Difficulty & Balancing
Data-driven tunables (load from a config record so balance is editable without code):

| Name | Default | Range | Effect |
| --- | --- | --- | --- |
| `cannonSpeed` | 320 px/s | 200–500 | Player horizontal speed |
| `fireCooldown` | 0.30 s | 0.15–0.6 | Min time between shots |
| `bulletSpeed` | 620 px/s | 400–900 | Player laser velocity |
| `marchStepPx` | 8 px | 4–16 | Distance per march step |
| `marchDropPx` | 24 px | 12–32 | Descent per edge bounce |
| `stepIntervalMaxMs` | 800 | 400–1200 | Interval at 55 aliens (slowest) |
| `stepIntervalMinMs` | 48 | 30–120 | Interval floor (lerp base at n=0; n=1 is ≈62 ms) |
| `waveSpeedup` | 0.92 | 0.80–0.98 | Per-wave interval multiplier |
| `bombSpeed` | 220 px/s | 120–400 | Enemy bomb fall speed |
| `bombSpeedPerWave` | 10 px/s | 0–30 | Added bomb speed each wave |
| `maxBombs` | 3 | 1–6 | Concurrent enemy bombs |
| `bombBaseFireP` | 0.35 | 0.1–0.9 | Base per-attempt drop chance |
| `bombInterval` | 1.0 s | 0.4–2.0 | Time between fire attempts |
| `ufoIntervalMin` | 20 s | 10–60 | Min time between UFO spawns |
| `ufoIntervalMax` | 30 s | 15–90 | Max time between UFO spawns |
| `ufoSpeed` | 150 px/s | 80–300 | UFO horizontal speed |
| `startLives` | 3 | 1–5 | Starting lives |
| `extraLifeAt` | 10000 | 0–50000 | Bonus-life score threshold |
| `formationDropPerWave` | 24 px | 0–48 | Lower start per wave |

**Difficulty presets.** The §9.1 Difficulty setting (Easy · Classic · Insane) selects one
column of overrides on top of the defaults above; every unlisted tunable keeps its default.
Classic is the default column (the numbers used throughout this spec). All values stay
inside each tunable's stated range, so a preset is pure data — no code path branches on it.

| Tunable | Easy | Classic | Insane |
| --- | --- | --- | --- |
| `startLives` | 5 | 3 | 2 |
| `cannonSpeed` | 360 px/s | 320 px/s | 300 px/s |
| `fireCooldown` | 0.24 s | 0.30 s | 0.34 s |
| `waveSpeedup` | 0.96 | 0.92 | 0.86 |
| `stepIntervalMaxMs` | 900 | 800 | 700 |
| `bombBaseFireP` | 0.25 | 0.35 | 0.50 |
| `bombSpeed` | 180 px/s | 220 px/s | 280 px/s |
| `bombSpeedPerWave` | 6 px/s | 10 px/s | 16 px/s |
| `maxBombs` | 2 | 3 | 4 |
| `extraLifeAt` | 8000 | 10000 | 15000 |

- **Easy** widens every margin the player controls (more lives, faster cannon, quicker
  refire, gentler ramp, cheaper extra life) and softens enemy fire — a settling-in preset.
- **Insane** inverts each: fewer lives, a slower and slower-firing cannon, a steep
  `waveSpeedup`, denser/faster bombs, and a distant extra life. It never changes *rules*,
  only numbers, so all §14 acceptance scenarios still hold under any preset (their fixtures
  pin Classic).

**System interactions worth tuning as a set.**
- `waveSpeedup` × `stepIntervalMinMs` — a lower `waveSpeedup` reaches the 40 ms interval
  floor (§4.4) in fewer waves; past that point extra difficulty comes only from bombs and
  formation height, so pushing `waveSpeedup` down without also raising bomb pressure plateaus.
- `maxBombs` × `bombInterval` — concurrent cap and cadence together set the *screen bomb
  density*; raising `maxBombs` while keeping the 1.0 s cadence makes bombs cluster in bursts
  rather than stream, changing the dodge feel more than either knob alone.
- `cannonSpeed` × `bombSpeed` — the dodge is only fair while a full cannon-width side-step
  (§4.10) clears a bomb before it lands; if `bombSpeed` is raised, `cannonSpeed` should track
  it or the bomb-dodge window collapses below reaction time.
- `fireCooldown` × the one-bullet rule — cooldown only matters when a bullet clears faster
  than the cooldown (§4.2); on Easy's 0.24 s the rule is *almost always* the binding limit,
  on Insane's 0.34 s the cooldown bites after any close-range kill.

## 13. Technical Notes
- **Entity budget**: ≤ 55 aliens + 1 bullet + 3 bombs + 1 UFO + 4×(22×16=352) bunker
  cells = ~1408 bunker cells (≈1468 entities total) max. Trivial for Skia at 60 FPS /
  16.7 ms frame. Cull Dead
  aliens from the list; iterate bunker cells with simple AABB pre-filter (only test the
  ~1 bunker a bullet overlaps).
- **Timestep**: the *march* uses a **fixed-step accumulator** — `FixedStep.drain marchInterval dt
  StepAccumMs` returns `struct (steps, acc')`, the whole marches this frame owes and the remainder to
  bank, capped internally against a stall. Do not hand-roll it. Other motion (cannon, bullets, bombs,
  UFO) deliberately stays on **variable `dt`** clamped to ≤ 0.05 s: it is pure `pos += vel·dt`
  integration with nothing to quantise, and forcing it onto the march's step would make it jerkier, not
  more correct. That split is the point — the iconic march timing is exact, the motion stays smooth.
- **Determinism/RNG**: a single **`Rng`** (`FS.GG.Game.Core`, splitmix64) in the Model, seeded with
  `Rng.ofSeed`, drives UFO timing, UFO bonus selection, bomb column choice, and bomb-type alternation
  (`Rng.nextInt` / `Rng.nextBool`). Seeding it makes full runs reproducible for tests. It is a **value**, not a `System.Random`: every draw returns `struct (x, rng')` and you write
  `rng'` back to the `Model`, so the `Model` stays a value you can snapshot, replay and compare.
  A `System.Random` in the `Model` is a mutable object *shared* by every copy of it, which
  silently breaks the reproducibility this bullet promises.
- **Persistence**: high score stored to a local file/`localStorage`-equivalent
  (`%score%` integer); loaded on Title, written on Game Over if exceeded.
- **Edge cases**: simultaneous left+right → no move; firing with bullet in flight →
  ignored; bullet and bomb occupying same cell → mutual cancel, no score; last alien
  reaching min interval clamps at 40 ms (never 0/negative); UFO + last alien — UFO
  suppressed once aliens < 8; drop that crosses invasion line ends game before any
  further bomb resolution.

## 14. Acceptance Criteria (test scenarios)
1. **Cannon movement (input)** — *Given* Phase=Playing and cannon at x=640, *When*
   `Right` is held for 1.0 s of ticks at dt=1/60, *Then* CannonX ≈ 960 (±2 px) and is
   clamped to ≤ 1256.
2. **One bullet in flight** — *Given* a player bullet exists, *When* `Space` is pressed,
   *Then* no second bullet spawns and `ShotCount` is unchanged until the first bullet
   clears and `FireCooldown` ≤ 0.
3. **Fire cooldown** — *Given* a bullet just left the top edge at t=0, *When* `Space` is
   pressed at t=0.1 s, *Then* no shot fires; *When* pressed at t=0.31 s, *Then* a shot
   fires.
4. **Alien kill & score** — *Given* a Squid (row 0) and the player bullet overlapping it,
   *When* the tick resolves, *Then* Score increases by 30, the alien enters `Dying`, the
   bullet is removed, and the alien becomes `Dead` after ~0.18 s.
5. **Row point values** — *Given* one alien of each type is shot, *Then* Score gains
   30 (Squid) + 20 (Crab) + 10 (Octopus) = 60 total.
6. **March acceleration** — *Given* a fresh wave (55 aliens) with stepInterval≈800 ms,
   *When* 54 aliens are destroyed (n=1), *Then* the computed stepInterval is ≈ 62 ms
   (strictly monotonically decreasing as n decreases).
7. **Edge detect & drop** — *Given* the formation moving right and the rightmost living
   alien one step from x=1256, *When* the next march step is due, *Then* the formation
   does **not** move horizontally, drops 24 px, and `MarchDir` flips to −1.
8. **Invasion loss** — *Given* a living alien whose bottom is at y=600, *When* a drop
   moves it to y ≥ 620, *Then* Phase becomes `GameOver` immediately.
9. **Bomb hits cannon → lose life** — *Given* Lives=3 and a bomb overlapping the cannon,
   *When* the tick resolves, *Then* Lives=2, Phase=`PlayerDying`, and after the death
   timer the cannon respawns at x=640.
10. **Last life loss → Game Over** — *Given* Lives=1, *When* a bomb hits the cannon,
    *Then* Lives=0 and Phase=`GameOver` (no respawn).
11. **Bunker erosion** — *Given* a full bunker, *When* 3 player bullets strike the same
    column, *Then* solid cells in a ≥6 px radius around each impact become false and a
    visible gap forms; each bullet is consumed on impact.
12. **Bunker wave reset** — *Given* an eroded bunker at wave 1 clear, *When* wave 2
    begins, *Then* all 4 bunkers are fully solid again.
13. **UFO scoring** — *Given* ≥ 8 aliens remain and a UFO is on screen, *When* the player
    bullet hits it, *Then* Score increases by the table value for the current `ShotCount`
    and the UFO despawns.
14. **UFO suppression** — *Given* fewer than 8 aliens remain, *When* `UfoTimer` expires,
    *Then* no UFO spawns.
15. **Wave clear → next wave** — *Given* the last alien is destroyed, *When* the
    `WaveCleared` pause (1.5 s) elapses, *Then* Wave increments, a full 5×11 grid respawns
    one formation-drop lower, and march interval is multiplied by `0.92^(wave-1)`.
16. **Extra life** — *Given* Score 9,980 and Lives 2, *When* a 30-pt Squid is killed
    (Score→10,010 crossing 10,000), *Then* Lives become 3 exactly once.
17. **Determinism** — *Given* two runs seeded identically with an identical scripted input
    sequence, *Then* final Score, Wave, and Lives are identical.
18. **Both-keys cancel** — *Given* Phase=Playing with the cannon mid-rail, *When* `Left` and
    `Right` are held simultaneously for 0.5 s of ticks, *Then* `CannonX` is unchanged
    (velocity resolves to 0, §4.1).
19. **Wall park** — *Given* `Right` held until `CannonX` clamps at 1256, *When* it is held
    for another 0.5 s then `Left` is pressed, *Then* `CannonX` stays 1256 while pinned and
    begins decreasing on the first tick `Left` is read (no wind-up, §4.1).
20. **Single-target bullet** — *Given* a bullet whose swept AABB overlaps two stacked aliens
    in one tick, *When* the tick resolves, *Then* only the lower (nearest) alien enters
    `Dying`, only its points are scored, and the bullet is consumed (§4.2, §4.9).
21. **Bomb concurrency cap** — *Given* 3 bombs already in flight (`maxBombs`), *When* a
    `bombInterval` attempt succeeds its roll, *Then* no 4th bomb spawns and the attempt is
    skipped, not deferred (§4.6).
22. **Bombs fall through death** — *Given* a bomb in flight and the cannon entering
    `PlayerDying`, *When* ticks advance through the death window, *Then* no *new* bombs drop
    but the airborne bomb keeps descending and can strike a bunker (§4.6).
23. **Doorway pass-through** — *Given* a bunker with its center doorway cells carved false,
    *When* the player bullet travels up through the doorway column, *Then* it erodes nothing
    and is not consumed, continuing toward the formation (§4.7).
24. **Alien erosion at march tempo** — *Given* a living alien overlapping solid bunker cells,
    *When* the formation takes one march step, *Then* the overlapped solid cells erode exactly
    once for that step (not per frame) (§4.7).
25. **UFO single instance** — *Given* a UFO airborne, *When* `UfoTimer` would otherwise
    expire, *Then* no second UFO spawns and the timer re-arms only after the current UFO
    despawns (§4.8).
26. **UFO stays hittable under 8** — *Given* a UFO airborne and living aliens dropping below
    8, *When* the player bullet hits it, *Then* it still awards the table value and despawns
    (suppression gates spawning only, §4.8).
27. **Inter-wave carry-over** — *Given* Score/Lives/`CannonX`/`ShotCount` at wave clear,
    *When* wave N+1 begins, *Then* those four are preserved while the grid, all 4 bunkers,
    `Bombs`, `StepAccumMs`, and `BombTimer` are reset and `MarchDir` is +1 (§6).
28. **Difficulty preset applies as data** — *Given* Difficulty=Insane selected in Settings,
    *When* a new run starts, *Then* `startLives`=2 and the Insane column overrides load, while
    all rule-based scenarios (#1–#17) still hold (§12, §9.1).
29. **Invasion wins the tie** — *Given* a tick where a drop crosses y ≥ 620 and a bomb also
    overlaps the cannon, *When* the tick resolves, *Then* Phase=`GameOver` from invasion and
    the bomb hit is not separately processed (§4.9, §11).

## 15. Stretch Goals
1. **Splitting/zig-zag bombs** with the 50% bullet-survives rule (faithful arcade RNG).
2. **2-player alternating** mode with separate high scores.
3. **Animated UFO bonus reveal** (floating score number on hit).
4. **Color zones** (classic per-y-band tint of the green phosphor look) + CRT scanline
   shader.
5. **Difficulty presets** (Easy/Classic/Insane) driven entirely by the section-12 table.
6. **Online high-score leaderboard**.
7. **Boss wave** every 5th wave (armored alien that takes multiple hits).

## 16. Milestone Roadmap

Implementation is sequenced into milestones; each item is a colored checkbox
tracking its status. Items reference the section that specifies them.

**Legend:** 🟥 Not started · 🟨 In progress · 🟩 Done · ⬜ Deferred (post-v1)

_All items start 🟥 (spec status). Flip an item to 🟨 when work begins and 🟩 once
its acceptance test(s) pass (§14)._

### M0 — Scaffold & fixed-step loop
- 🟥 Project scaffold: `Model`/`Msg`/`update`/`view` skeleton (§7)
- 🟥 Fixed-step march via `FixedStep.drain marchInterval dt StepAccumMs`, banked remainder (§4.4, §13)
- 🟥 `Rng` value seeded with `Rng.ofSeed`, threaded through `Model` (§13)
- 🟥 Logical 1280×720 coordinate system + letterbox scaling, origin top-left (§6, §8)

### M1 — Cannon & player fire
- 🟥 Held `LeftDown`/`RightDown` + edge-triggered fire/menu keys (§3)
- 🟥 Cannon movement at 320 px/s, both-keys-held cancels, clamp `[24, 1256]` (§4.1) — AC #1
- 🟥 One-bullet-in-flight rule + 0.30 s cooldown, muzzle spawn (§4.2) — AC #2, #3
- 🟥 Both-keys cancel to 0 velocity; float sub-pixel integrate then clamp (§4.1) — AC #18
- 🟥 Wall-park: clamp holds zero residual velocity, resumes on reverse (§4.1) — AC #19
- 🟥 Movement live only in `Playing`; held keys tracked but frozen elsewhere (§4.1)
- 🟥 Single-target bullet: nearest/lowest alien only, no double-kill, no pierce (§4.2) — AC #20

### M2 — Formation march & acceleration
- 🟥 Spawn 5×11 grid, per-type sprites/points, box from living aliens (§4.3)
- 🟥 Discrete 8 px march step; interval `lerp(48→800, n/55)` × `waveFactor` (§4.4) — AC #6
- 🟥 Edge test vs walls `x=24/1256` → drop 24 px + reverse, one per step (§4.5) — AC #7
- 🟥 Invasion line `y ≥ 620` ends game immediately (§4.5, §11) — AC #8
- 🟥 Derived alien screen pos from formation origin + cell offset (§4.3)
- 🟥 Walk frame toggles per march step; interior gaps stay permanent (§4.3)
- 🟥 `n` = Alive+Dying live count; `StepAccumMs` zeroed each wave (§4.4)
- 🟥 Multi-step drain on a stalled frame, edge-test before each step (§4.4, §13)
- 🟥 Direction-of-travel wall test only; predictive edge, no wall-nudge (§4.5) — AC #7

### M3 — Enemy bombs & bunkers
- 🟥 Lowest-column bombs, `pFire` chance, max 3, `bombInterval` cadence (§4.6)
- 🟥 4 bunkers of 22×16 destructible cells, arch mask, 6 px erosion bite (§4.7) — AC #11
- 🟥 Bunkers full-reset each new wave (no per-life regen) (§4.7, §6) — AC #12
- 🟥 Bomb spawns from lowest alien; per-attempt cap skip, not deferred (§4.6) — AC #21
- 🟥 Straight/ZigZag alternation state advances on each successful spawn (§4.6)
- 🟥 Fire suppressed outside `Playing`; airborne bombs fall through death (§4.6) — AC #22
- 🟥 Doorway/carved gaps let a shot pass without eroding or being consumed (§4.7) — AC #23
- 🟥 Alien overlap erodes bunker cells once per march step (§4.7) — AC #24
- 🟥 Both fire types erode; empty-footprint bunker is inert pass-through (§4.7)

### M4 — Mystery UFO
- 🟥 UFO spawn every 20–30 s, suppressed while `< 8` aliens remain (§4.8) — AC #14
- 🟥 Bonus-table scoring on bullet hit, despawn off opposite edge (§4.8) — AC #13
- 🟥 Single UFO instance; `UfoTimer` re-arms only after despawn (§4.8) — AC #25
- 🟥 Suppression gates spawning only; airborne UFO stays hittable under 8 (§4.8) — AC #26
- 🟥 Bonus value = `table[ShotCount mod len]`; immune to bombs/bunkers/formation (§4.8)

### M5 — Collisions, scoring & lives
- 🟥 Bullet vs alien: `Alive → Dying (0.18 s) → Dead`, per-type points (§4.3, §11) — AC #4, #5
- 🟥 Bomb/alien hits cannon → `PlayerDying` (1.0 s) → respawn at x=640 (§4.6, §11) — AC #9
- 🟥 Lives reach 0 after a hit → `GameOver`, no respawn (§11) — AC #10
- 🟥 Extra life at 10,000 pts (once), lives cap at 4 (§11) — AC #16
- 🟥 Bullet/bomb mutual cancel, no score (§4.6, §13)
- 🟥 Fixed per-tick collision precedence: bullet → march/invasion → bombs (§4.9)
- 🟥 Invasion wins a same-frame tie over a cannon-killing bomb (§4.9, §11) — AC #29
- 🟥 Points on `Alive → Dying`; airborne bullet still scores during `PlayerDying` (§5, §11)

### M6 — Wave flow & phases
- 🟥 `Title`/`Playing`/`Paused`/`PlayerDying`/`WaveCleared`/`GameOver` phases (§7)
- 🟥 `WaveCleared` (1.5 s) → next wave: rebuild grid lower, `0.92^(wave-1)`, deadlier bombs (§6) — AC #15
- 🟥 `TogglePause` freezes all Tick integration, resumes exact state (§7)
- 🟥 Inter-wave carry-over: keep score/lives/`CannonX`/`ShotCount`, reset grid/bunkers/timers, `MarchDir`=+1 (§6) — AC #27
- 🟥 Per-wave ramp: start-y cap y=240, `pFire` clamp 0.95, bomb speed +10/wave (§6)

### M7 — Rendering (Skia)
- 🟥 Draw order: bg/starfield, ground, bunkers, aliens, UFO, cannon, bullets, HUD (§8)
- 🟥 2-frame walk cycle on march step, `Dying` splat glyph, 0.12 s cannon-hit flash (§8)

### M8 — UI, menus & stats
- 🟥 HUD: 6-digit score, `HI`, `WAVE`, lives icons (§9)
- 🟥 Adopt the generic FS.GG game shell (FS-GG/FS.GG.Rendering#991): main menu (title + Start/Config/Exit), Esc pause routing, Settings with screen resolution + fullscreen, and in-game key rebinding of the §3 controls, persisted — the game provides its name + key→command map + play update/view; the shell provides the rest, no bespoke menu system (§9.1)
- 🟥 Game-specific Config rows over the shell (difficulty preset, volume/sound, CRT scanlines) apply live + persist (§9.1, §12, §13)
- 🟥 Easy/Classic/Insane preset columns load as pure data over the §12 defaults (§12) — AC #28
- 🟥 `MatchStats`/`LifetimeStats` accumulation + snapshot on `GameOver` (§9.2)
- 🟥 Kills-by-type bar chart + shots fired-vs-hit line chart (§9.2)

### M9 — Audio
- 🟥 `AudioEffect` cues per event, `Audio.interpret` → `AudioEvidence` (§10)
- 🟥 `march` music loop on wave start, `stopMusic` on wave end/game over (§10)
- 🟥 `Audio.setMasterVolume` mute/settings toggle, volume clamp `[0,1]` (§10, §9.1)

### M10 — Acceptance & determinism
- 🟥 All 29 acceptance scenarios green (§14)
- 🟥 Seed + scripted input replay yields identical Score/Wave/Lives (§13) — AC #17

### Stretch — deferred (post-v1)
- ⬜ Splitting/zig-zag bombs with 50% bullet-survives rule (§15.1)
- ⬜ 2-player alternating mode with separate high scores (§15.2)
- ⬜ Animated UFO bonus reveal (floating score number) (§15.3)
- ⬜ Color zones + CRT scanline shader (§15.4)
- ⬜ Difficulty presets (Easy/Classic/Insane) from the §12 table (§15.5)
- ⬜ Online high-score leaderboard (§15.6)
- ⬜ Boss wave every 5th wave (armored multi-hit alien) (§15.7)
