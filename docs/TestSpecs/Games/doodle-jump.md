---
title: "Doodle Jump"
slug: doodle-jump
category: games
complexity: simple
genre: "Vertical auto-bounce platformer (endless climber)"
target_session_minutes: 3
stack: { rendering: "FS.GG.Rendering (Skia/OpenGL)", framework: "FS.GG.Game.Core (FixedStep for the tick; Rng for procedural generation)", arch: "Elmish/MVU", lang: "F#" }
status: spec
---

# Doodle Jump

## 1. Overview
You are a perpetually bouncing doodle creature climbing an endless vertical tower of
floating platforms. The character **auto-bounces** on every platform it lands on — the
player never presses a jump button. The only control is **steering left/right** (tilt or
arrow keys) to line the doodle up with the next platform as the camera scrolls relentlessly
upward. The fantasy is effortless, springy ascent punctuated by white-knuckle moments of
"will I reach the next platform?" Fun comes from the tension between the fixed bounce rhythm,
gravity pulling you back down, and platforms that move, crumble, or fling you skyward. Miss
your footing and fall off the bottom of the screen — game over. Your score is the highest
altitude you reach.

## 2. Core Game Loop
**Moment-to-moment:** `fall toward platform → steer left/right to align → land → auto-bounce
up → rise → fall again → repeat`. Higher up, the player weaves between sparse/moving platforms,
grabs springs and jetpacks for big boosts, and dodges enemies.

**Session-level:** `Title → tap/press to start → climb (camera scrolls up, score = max height)
→ fall below camera OR killed by enemy → Game Over (show score + best) → restart`. A run is
short and replayable, typically 1–4 minutes.

**Bounce cadence (the metronome the whole game is built on):** a normal bounce reaches apex in
`bounceVy / g = 1150 / 2400 ≈ 0.479 s` and takes the same time to fall back to the launch
height, so the fundamental rhythm is a **~0.958 s up-down cycle**. Every steering decision is
made inside that window: the player has roughly half a second of rising blindness (the doodle
sails past platforms it can't land on, 4.3) followed by half a second of falling commitment,
during which the target platform must already be lined up. A spring stretches the cycle to
`1900 / 2400 ≈ 0.792 s` up (≈ 1.58 s round trip); a jetpack suspends the metronome entirely for
`jetpackDuration`. Reading, teaching, and then subverting this cadence is the core skill curve.

**Three nested tension loops** the player is always resolving:
1. **Alignment loop (~1 s):** steer to put the feet sensor over the next platform top before the
   fall lands. Horizontal reach in one full cycle at `vxMove` is `520 × 0.958 ≈ 498 px` — about
   two-thirds of the 720-wide field, so screen-wrap (4.4) is a genuine routing tool, not a gimmick.
2. **Routing loop (~5–15 s):** chain platforms into a line that also grabs springs/jetpacks and
   avoids enemies, choosing the *cheaper* of two reachable platforms rather than the nearest.
3. **Survival loop (whole run):** the camera never descends (4.6), so a single missed alignment
   that drops the doodle below the last safe platform is usually unrecoverable — there is no
   catch-up loop, which is what makes a run feel like a held breath.

**Failure-recovery micro-loop:** a fall is not instantly fatal. From apex the doodle has
`vyMax / g = 1600 / 2400 ≈ 0.667 s` before it hits terminal speed, and death only fires when the
top edge clears `cameraY + 1280` (11). That grace window — often two or three platform rows of
vertical slack early in a run when gaps are ~90 px — is where clutch saves happen: steer under a
lower platform, bounce, and climb back. High up, where gaps approach `maxGap = 230 px`, the same
window spans barely one row, so the identical mistake is fatal. Difficulty is this shrinking margin.

## 3. Controls & Input
Input is **steer-only**; bouncing is automatic. Horizontal movement is a *held* input
(velocity applied while the key is down), not edge-triggered.

| Input | Action | Model |
|---|---|---|
| `Left Arrow` / `A` | Move doodle left | Held (velocity while down) |
| `Right Arrow` / `D` | Move doodle right | Held (velocity while down) |
| Mouse X / pointer drag | Set horizontal target (optional alt control) | Continuous |
| `Space` / `Enter` | Start game (Title), Restart (Game Over) | Edge-triggered (pressed) |
| `Esc` / `P` | Pause / resume | Edge-triggered (pressed) |
| `Shoot` — `Up Arrow` / `W` / click *(stretch)* | Fire projectile upward at enemies | Edge-triggered |

"Tilt" semantics: on touch/accelerometer targets, tilt maps to the same horizontal velocity
axis as Left/Right; the spec is written against keyboard as primary.

## 4. Mechanics (detailed)

### 4.1 Coordinate system & units
Logical playfield is **720 × 1280 px (portrait)**. World Y increases **downward** in screen
space, but for gameplay we track a **world height** that increases as the player climbs. To
avoid confusion, define **`worldY`** with **up = negative** (standard physics): the doodle
starts at `worldY = 0` and ascends to increasingly negative `worldY`. Altitude (for score) is
`altitude = -worldY` clamped to its max. All px/s and px/s² constants below use this frame.

### 4.2 Gravity & vertical motion
- **Gravity `g = 2400 px/s²`**, applied to vertical velocity every tick: `vy += g * dt`.
- **Terminal fall velocity `vyMax = 1600 px/s`** (clamp downward speed).
- Vertical position integrates: `worldY += vy * dt` (with up negative, an upward bounce sets
  `vy` to a large negative value, then gravity decays it back toward 0 and positive).
- **Integration order per step is fixed** (determinism, 13): first `vy += g * dt`, then clamp to
  `[-∞, +vyMax]` (only the downward side is clamped — an upward impulse of `-1900` is never
  clipped), then `worldY += vy * dt`. Applying the half-step-before-and-after (semi-implicit
  Euler) is *not* used; the spec's apex formulas assume this plain order and the tuning table is
  fit to it.
- **Fall phases** the doodle passes through after apex: `vy = 0` at apex → accelerating fall for
  `vyMax / g ≈ 0.667 s` → terminal fall at a constant `1600 px/s` (≈ 26.7 px/fixed-step, the
  number that forces swept collision, 4.5/13). A doodle that has been falling ≥ 0.667 s is at
  terminal speed and covers a `maxGap`-sized gap in `230 / 1600 ≈ 0.144 s` — under nine fixed
  steps — which is the tunneling budget the sweep must cover.
- **Apex hang:** near `vy ≈ 0` the doodle spends several ticks at near-zero vertical speed
  (roughly `|vy| < 240 px/s` for ~0.2 s around apex). This is the natural "floaty top" of each
  bounce and the window the renderer's squash/stretch relaxes into (8); no special code, it falls
  out of the integration.

### 4.3 Auto-bounce (the core rule)
- Bounce happens **only when the doodle is moving downward** (`vy > 0`) **and** its feet
  overlap the top surface of a platform within a contact band (see 4.5).
- On a normal bounce, set **`vy = -1150 px/s`** (upward impulse). This is independent of impact
  speed (fixed rhythm) — Doodle Jump's signature feel.
- Bounce never triggers while ascending (`vy <= 0`), so the doodle passes *through* platforms
  from below — platforms are **one-way (top-only) colliders**.
- Apex height of a normal bounce above the platform: `vy² / (2g) = 1150² / 4800 ≈ 275 px`.
  Tune platform vertical spacing against this (see 6.x).
- **`vy` is *set*, never added:** the impulse assigns `vy = -1150` outright, discarding whatever
  downward speed the doodle arrived with. Landing at terminal `1600 px/s` and landing at a gentle
  `400 px/s` produce the identical `-1150` launch — this is what keeps the rhythm fixed and is the
  single most important rule for the "springy, effortless" feel. The only exceptions that read
  arrival speed are none; even a stomp (4.9) uses the same fixed `-1150`.
- **One bounce per contact, per platform:** once a bounce fires on a platform this step, that
  platform is marked resolved for the doodle until the doodle's feet leave its top band again, so
  a doodle that momentarily straddles the contact band cannot double-fire on the same surface.
- **Multi-platform tie-break (same step):** if the swept feet sensor crosses more than one
  eligible platform top in a single step (possible only when gaps are tighter than one step of
  travel, e.g. the taught starter cluster), land on the **lowest** eligible top the sensor reaches
  first on the way down (largest `worldY`, i.e. the one encountered earliest in the downward
  sweep). This guarantees the doodle never "skips" a platform it physically passed through.
- **Bounce vs. wrap in the same step:** resolve horizontal wrap (4.4) *before* the collision test,
  so the landing uses the post-wrap world X (13 edge case). A bounce is never denied because the
  doodle happened to wrap on the landing step.
- **State transition:** on any bounce/spring, `State := Rising` and `FacingRight` is left
  unchanged (facing tracks steer input, not bounce). The doodle re-enters `Falling` the tick `vy`
  first becomes `> 0` after apex.

### 4.4 Horizontal movement & screen-wrap
- **Move speed `vxMove = 520 px/s`** applied directly while a steer key is held (arcade feel,
  not momentum-heavy). Optional light smoothing: lerp current `vx` toward target at
  `accel = 4000 px/s²` so direction changes feel responsive but not instant.
- No horizontal friction beyond releasing the key (target vx → 0).
- **Screen wrap:** the doodle's X wraps horizontally. If `x < -doodleHalfW`, set
  `x = 720 + doodleHalfW`; if `x > 720 + doodleHalfW`, set `x = -doodleHalfW`. The doodle
  visually re-enters from the opposite edge.
- **Opposing keys:** if both steer keys are held (`InputLeft && InputRight`), target `vx = 0`
  (they cancel). Neither wins; this avoids a last-key-latch that would desync on rapid taps.
- **Held vs. pointer conflict:** when both a steer key and the pointer/drag alt-control (3) are
  active in the same tick, the **keyboard wins** (target `vx` from keys); the pointer target is
  ignored until no steer key is down. One authority per tick keeps steering deterministic.
- **Accel smoothing is bounded, not instant:** with `accel = 4000 px/s²`, `vx` reaches full
  `520 px/s` from rest in `520 / 4000 = 0.13 s` (≈ 8 fixed steps) and reverses direction (from
  `+520` to `-520`) in `1040 / 4000 = 0.26 s`. This tiny lag is the entire "weight" of the
  character; raising `accel` toward its config ceiling makes steering feel twitchier, lowering it
  makes reversals feel sluggish. AC #9's `≈ 520 * dt` per tick holds only once `vx` has ramped in.
- **Wrap is edge-continuous, not teleport-with-carry:** the horizontal target `vx` and any smoothed
  `vx` are **preserved** across a wrap (only `x` jumps); a doodle drifting left keeps drifting left
  after re-entering on the right. Vertical `vy` is untouched by wrap (AC #8).
- **Wrap does not carry collision state:** the resolved-platform mark (4.3) is cleared by the X jump
  only if the doodle's feet are no longer over that platform's world X after wrap — normal, since a
  wrapped doodle is on the far side of the field.

### 4.5 Collision (one-way platform landing)
- Doodle collision: an **AABB feet sensor** = bottom 10 px of the doodle, width = doodle width.
- Platform top surface: a horizontal line segment of width = platform width at the platform's
  top edge.
- **Land condition (per tick):** `vy > 0` AND the feet sensor's previous-frame bottom was at or
  above the platform top AND this-frame bottom is at or below platform top + `contactBand`
  (`contactBand = 12 px`) AND horizontal overlap exists. Use swept previous→current Y to avoid
  tunneling at high `vy`.
- On land: snap doodle feet to platform top, trigger bounce (4.3) or platform-specific effect
  (4.7), apply platform side-effects (e.g. break).
- **Horizontal overlap test** uses the doodle's full body width against the platform's full width
  (both AABBs), not just the feet-sensor center: any overlap of `≥ 1 px` counts, so a doodle
  landing on the very corner of a platform still bounces (forgiving edges, deliberate).
- **Snap direction:** on a normal/spring land, feet are snapped **up** to exactly the platform top
  before applying the impulse, so apex is measured from the surface (4.3 apex numbers assume this).
  On a breakable contact there is **no snap** — the doodle keeps its swept Y and falls through (4.7).
- **Moving-platform contact** is evaluated against the platform's position *at the end of this
  step* (after 4.7 patrol update), so a doodle landing on a platform that slid out from under it
  correctly misses. Order within Tick: move platforms, then sweep the doodle (7, step 5 vs 3 — the
  patrol update at step 5 applies to the *next* step's test; within a step, use the platform's
  current-step position consistently for both draw and collide).
- **Contact band is one-directional:** the `12 px` band extends **below** the platform top only.
  A feet sensor whose previous bottom was already below `top + contactBand` (i.e. it was under the
  platform) never lands — that is a pass-from-below, correctly ignored by the one-way rule (4.3).
- **Degenerate `dt`:** the sweep is computed from the banked fixed step (`1/60`), never the raw
  frame `dt`, so a long stall cannot produce a single giant sweep that lands on a platform the
  doodle should have passed between (13 spiral-of-death guard already caps steps at 4/frame).

### 4.6 Camera (scroll up, never down)
- The camera tracks the player so the doodle sits around **40% from the top** of the screen
  when ascending.
- **`cameraY` only ever decreases** (moves up in world space). Define a **`maxClimb`** = the
  smallest (most negative) `worldY` the doodle has reached. `cameraY = maxClimb - 0.40*1280`
  is the desired top-of-view; the camera lerps toward it but **never scrolls back down** even
  if the doodle falls.
- Because the camera never follows the doodle down, falling lets the doodle drop off the bottom
  of the visible screen → death (see 11).

### 4.7 Procedural platform types
All platforms are **96 × 22 px** unless noted. Spawned above the camera as the player climbs.
- **Static** (green) — default. Normal bounce.
- **Moving** (blue) — translates horizontally at `±90 px/s`, bouncing between screen edges
  (or within a patrol span of 200 px). Normal bounce while moving; doodle does not inherit
  horizontal velocity. Patrol reflects at `patrolMinX`/`patrolMaxX`: on reaching a bound, clamp
  X to the bound and negate `vx` (no overshoot accumulation, so two runs from the same seed stay
  phase-locked, 13). A moving platform spawned with its patrol span partly off-screen still
  reflects at its own bounds, not the screen edge — the span is authoritative. Because the doodle
  does **not** inherit the platform's `±90 px/s`, a well-timed bounce off a platform sliding
  *toward* your next target is free horizontal progress you didn't have to steer for; a platform
  sliding *away* is the read that punishes a late alignment.
- **Breakable** (brown) — bounces the doodle **zero times**: on contact it **breaks** (plays
  break animation, removed after 0.25 s) and the doodle continues falling through (no impulse).
  Forces the player to find a real platform.
- **Vanishing/one-shot** (white, *stretch tier*) — gives one normal bounce, then disappears.
- **Spring platform** (static green with a coil) — on land, instead of the normal impulse,
  apply **`vy = -1900 px/s`** (super bounce; apex ≈ 752 px). ~8% of platforms carry a spring.
  A spring is a modifier rolled onto a **non-breakable** platform (Static or Moving), never onto
  a Breakable (12) — a spring you can't bounce on would be a lie. On a Moving+Spring the super
  bounce still ignores the platform's horizontal velocity. The `≈ 752 px` apex clears roughly
  `752 / 230 ≈ 3.3` max-gap rows in one shot, so a spring is worth deliberately routing toward:
  it both saves the ~1 s of a normal cycle and skips the two or three riskiest alignments above it.
- **Jetpack pickup** (item on/near a platform, not a platform itself) — see 5; grants timed
  thrust.
- **Spawn spacing of springs/jetpacks:** springs are frequent-but-shallow relief (~8% roll);
  jetpacks are rare-and-huge. Enforce a **minimum vertical separation of 2500 px between
  successive jetpacks** so two boosts never chain into a trivialized climb, and never place a
  jetpack within 1500 px of the run start (the opening must be earned on bounces).

### 4.8 Spawn density thinning with height
- Platforms are generated in vertical bands as the camera rises. Maintain a generation cursor
  just above the highest spawned platform.
- **Vertical gap between successive platforms** scales with altitude:
  `gap = clamp(baseGap + altitude * gapGrowth, baseGap, maxGap)` where `baseGap = 90 px`,
  `gapGrowth = 0.012` (px of gap per px of altitude), `maxGap = 230 px`. (At altitude 0, gap
  ≈ 90 px; by ~11,600 px altitude, gap saturates at 230 px — just under the normal apex of
  275 px, so the climb is always *possible* but tight.)
- Each new platform's X is `rand(0, 720 - platformWidth)`, with a constraint that consecutive
  platforms differ in X by at least 40 px (avoid stacking) and at most 360 px (always
  reachable given screen wrap).
- **Reachability is a hard invariant, not an average.** The generator must never emit a row whose
  vertical gap to the platform below exceeds the normal apex minus a safety margin: enforce
  `gap ≤ 275 − reachMargin` with `reachMargin = 45 px`, i.e. an absolute ceiling of **230 px**
  (why `maxGap` equals it). The `≥ 40 px` / `≤ 360 px` horizontal rule is measured as the
  *shorter of the direct and wrapped distance* (`min(|dx|, 720 − |dx|)`), so screen-wrap always
  counts as a legal route to the next platform. A row that would violate either rule is rejected
  and re-rolled (bounded retries, then the gap is clamped) — a run can be brutally hard but is
  never mathematically impossible.
- **Guaranteed-safe insertion:** if a breakable-heavy band would leave the doodle with *no*
  landable platform in reach (all candidates within one bounce apex are Breakable), force the next
  platform's kind to Static. This keeps the breakable weight (up to 0.30, 12) from producing an
  unwinnable wall.
- **Generation is lazy and one-directional:** platforms are only ever created above
  `SpawnCursorY` and never below the camera, so the world is unbounded in memory-safe bands.
  `SpawnCursorY` advances by the just-emitted `gap`; the loop (7, step 7) runs until the cursor is
  at least 100 px above `cameraY`, guaranteeing a screen-plus-margin of platforms always exists
  ahead even after a big spring/jetpack launch skips the doodle far up in one step.
- **Type weights also shift with altitude** (see 12).

### 4.9 Enemies / obstacles
- **Enemy (UFO/monster)**, **40 × 36 px**, spawns floating at fixed or slowly drifting world
  positions starting at altitude ≥ 3000 px, frequency rising with height.
- **Lethal contact:** if the doodle's body AABB overlaps an enemy AABB **and** the doodle is
  *not* bouncing on the enemy's head, the doodle dies instantly.
- **Stomp:** if `vy > 0` (falling) and the doodle's feet sensor hits the **top** of the enemy,
  the enemy is destroyed and the doodle gets a normal bounce off it (treat enemy top as a
  platform for that tick). Springs/jetpacks pass through enemies harmlessly while active
  (*optional*).
- **Stomp uses the same fixed impulse:** a head-stomp sets `vy = -1150` (normal bounce), not a
  spring value — killing an enemy is a survival move, not a boost. The enemy's top is treated as a
  platform top for that single tick and obeys the same swept land test (4.5), so you cannot stomp
  an enemy while rising into it.
- **Drift patterns:** an enemy carries `DriftVx` in `0–60 px/s`; it drifts horizontally and
  reflects at the screen edges like a moving platform (4.7), keeping its `worldY` fixed. Stationary
  enemies (`DriftVx = 0`) sit as pure alignment hazards; drifting ones force a moving read. Enemies
  never chase the doodle and never move vertically — the *player's* climb is the only relative
  motion, preserving the pure-platforming feel.
- **Spawn placement rule:** an enemy is placed so its body does not overlap any platform's top
  landing band at spawn (it would create an ambiguous stomp/land), and no two enemies spawn within
  600 px of vertical separation, so a single steer line is always threadable between them.
- **Contact resolution order (per tick):** platform land (4.5) is resolved first, then enemy
  contact. If the same downward step both lands the doodle on a platform *and* would touch an
  enemy body, the platform bounce wins only when the platform top is reached earlier in the sweep;
  otherwise the enemy is evaluated (stomp if feet-on-top, else death). This ordering makes a
  platform tucked just under an enemy a genuine safe route.
- **Grace on spawn-in:** an enemy that spawns already overlapping the doodle's body (possible after
  a jetpack skips the doodle into a fresh band) does **not** kill on its first tick; lethal contact
  requires the overlap to *begin* on a tick the enemy was already present (no "spawned inside you"
  deaths).
- **Black hole obstacle** (*stretch*): instant death on overlap regardless of state.

### 4.10 Game feel & juice (tuning that is felt, not scored)
These are physically small effects with an outsized read on "springiness"; each is deterministic
and driven off existing state so it survives replay (13).
- **Squash/stretch coupling:** the renderer's scale-Y 1.15 for 80 ms after a bounce (8) is
  triggered on the exact tick `vy` flips negative; a spring bounce uses a stronger 1.25 for 100 ms
  so the eye reads the bigger launch before the height even resolves. This is presentation only —
  it never touches the sim.
- **Camera kick / screen shake:** on a spring or jetpack ignition the camera target gets a
  one-frame downward nudge of 6 px that lerps out over ~0.15 s (a "recoil"), gated by the §9.1
  **Screen shake** setting. It is applied to the *view* transform, never to `cameraY` itself, so
  the never-descend invariant (4.6, AC #6) and all scoring stay untouched.
- **Coyote-free by design:** there is deliberately **no** coyote-time or bounce buffering — the
  land test is exact (4.5). The forgiveness in this game lives in the `12 px` contact band and the
  corner-overlap rule (4.5), not in input leniency. Adding input buffering would blur the metronome.
- **Near-miss telegraphs:** when the doodle's feet pass within `24 px` horizontally of a platform
  edge without landing (a "just missed it"), emit a small dust particle (6, presentation) — a free
  legibility cue that the player was close, with zero gameplay effect.
- **Idle attract bounce:** on the Title screen the doodle loops a normal bounce on a fixed platform
  using the real physics constants (not a canned animation), so the menu previews the exact cadence
  the run will feel (9).

### 4.11 System-interaction priority (the one true resolution order)
When several rules could fire on a single doodle in one fixed step, resolve in this exact order so
behavior is total and testable (13). Later stages see the state the earlier stages left.
1. **Input → target `vx`** (4.4, opposing-keys and pointer rules).
2. **Horizontal integrate + screen-wrap** (4.4) — collision runs on post-wrap X.
3. **Jetpack override check:** if `State = Jetpack`, set `vy = -2200`, suppress gravity, skip
   steps 4–6 entirely (no platform land, no enemy death, no stomp — 5), then jump to step 7.
4. **Gravity integrate + `vyMax` clamp + vertical integrate** (4.2).
5. **Swept platform land** (4.5): breakable-fall-through > spring > normal, lowest-first tie-break
   (4.3). A jetpack **pickup** overlapping this step is grabbed here and *supersedes* a same-step
   normal bounce (you take the boost, not the small hop).
6. **Enemy resolution** (4.9): platform-first ordering, then stomp-vs-lethal.
7. **Death check** (11): off-bottom OR unresolved lethal enemy contact → `GameOver`.
This is why the §13 edge cases resolve as stated: "breakable + spring same tick → breakable wins"
is step 5's precedence; "jetpack overrides enemy death" is step 3 short-circuiting step 6.

## 5. Entities / Game Objects

| Entity | Size (px) | HP | Speed | Notes |
|---|---|---|---|---|
| Doodle (player) | 54 × 60 | 1 | vx ≤ 520, vy clamp 1600 | Feet sensor bottom 10 px; one-way collider; screen-wraps |
| Static platform | 96 × 22 | n/a | 0 | Normal bounce |
| Moving platform | 96 × 22 | n/a | 90 | Horizontal patrol |
| Breakable platform | 96 × 22 | 1 hit | 0 | Breaks on contact, no bounce |
| Spring platform | 96 × 22 | n/a | 0 | Super bounce vy = -1900 |
| Jetpack pickup | 36 × 48 | n/a | 0 | Timed thrust on grab |
| Spring item *(alt)* | 30 × 18 | n/a | 0 | Same as spring platform effect |
| Enemy | 40 × 36 | 1 | 0–60 drift | Lethal on body contact; stompable on head |
| Projectile *(stretch)* | 8 × 8 | n/a | 900 up | Destroys enemies |

**State machines:**
- **Doodle:** `Rising → Falling → (Landed → bounce) → Rising …`; terminal `Dead`. `Jetpack`
  is an overlay state lasting `jetpackDuration = 2.2 s` that overrides vertical control: set
  `vy = -2200 px/s` sustained, ignore platform bounces, disable gravity until it ends, then
  resume `Falling`.
- **Breakable platform:** `Intact → (contact) → Breaking (0.25 s anim) → Removed`.
- **Enemy:** `Alive → (stomped | off-screen recycled) → Dead`.

**Doodle state-transition table** (drives §4.11 and the renderer):

| From | Event | To | Side effect |
|---|---|---|---|
| `Rising` | `vy` crosses `> 0` at apex | `Falling` | begin fall (4.2) |
| `Falling` | land on platform/enemy-top | `Rising` | set `vy` (4.3/4.9), squash (4.10) |
| `Falling`/`Rising` | grab Jetpack pickup | `Jetpack` | start `JetpackTimer = 2.2`, flame trail |
| `Jetpack` | `JetpackTimer` reaches 0 | `Falling` | `vy := 0`, gravity resumes |
| `Falling` | top edge `> cameraY + 1280` | `Dead` | `GameOver` (11) |
| `Rising`/`Falling` | lethal enemy body contact | `Dead` | `GameOver` (11) |
| any | `TogglePause` | *unchanged* | Tick becomes a no-op (7) |

- **`Jetpack` is exclusive:** it cannot be re-entered while active (grabbing a second jetpack
  mid-thrust **refreshes** `JetpackTimer` to the full 2.2 s rather than stacking velocity — the
  sustained `vy = -2200` is a ceiling, not additive). It cannot transition to `Rising`/`Falling`
  via a bounce because steps 4–6 are skipped (4.11).
- **`Dead` is terminal within a run:** no transition leaves it except `Restart`/`StartGame`
  building a fresh `Model` (7). A dead doodle stops integrating (its last pose is frozen for the
  Game Over overlay).

**Enemy lifecycle timers:** a stomped enemy flashes white for 1 frame (8) then is set `Alive =
false` and removed on the same cull pass; an un-contacted enemy is recycled when it falls below the
cull line, and *that recycle is the event that increments `monstersDodged`* (9.2).

**Breakable timer:** `BreakTimer` starts at `0.25` on contact and counts down in Tick (step 5);
the platform is drawn with a progressive crack overlay (8) proportional to `1 − BreakTimer/0.25`,
then removed at zero. A breakable already `Broken` is inert to further contact (the doodle simply
falls through empty space).

**Creation/destruction:** platforms/enemies are spawned by the generator above `cameraY`
(4.8) and **culled** once they fall below `cameraY + 1280 + 100 px` (well off the bottom).
The doodle is created once at run start.

```fsharp
open FS.GG.Game.Core
// Positions/velocities live in the scaffold's collision-safe Geometry.Vec2 ({ Vx; Vy }, from
// src/<ProductDir>/Vec2.fs) — NEVER a record you label X/Y/Width/Height, which collide with
// Scene's Point/Rect. This is a type ABBREVIATION: it adds no labels, so nothing can collide.
type Vec2 = Geometry.Vec2

type PlatformKind =
    | Static
    | Moving of vx: float * patrolMinX: float * patrolMaxX: float
    | Breakable
    | Spring

type Platform =
    { Id: int
      Kind: PlatformKind
      Pos: Vec2           // Vx = left edge; Vy = worldY of top surface (up = negative)
      WidthPx: float      // default 96
      Broken: bool
      BreakTimer: float } // seconds remaining in Breaking state

type PickupKind = Jetpack
type Pickup = { Id: int; Kind: PickupKind; Pos: Vec2; Taken: bool }

type Enemy =
    { Id: int; Pos: Vec2; WidthPx: float; HeightPx: float; DriftVx: float; Alive: bool }
```

Positions live in the scaffold's `Geometry.Vec2` (`Vx`/`Vy`), and sizes carry `…Px` labels — never
`X`/`Y`/`Width`/`Height`, which collide with `Scene`'s `Point`/`Rect` and blow up in the durable
`LayoutEvidence.fs`. Cross into the scene with the scaffold's crossings — qualified, since these
sketches abbreviate `Geometry.Vec2` rather than opening `Geometry`: e.g.
`Geometry.toRect enemy.Pos enemy.WidthPx enemy.HeightPx`, `Geometry.toPoint doodle.Pos`.

## 6. World / Levels / Progression
- **Playfield:** 720 × 1280 logical px (portrait), letter/pillarboxed to the window aspect.
- **No discrete levels** — single endless vertical world. "Difficulty" is purely a function of
  **altitude** (= `-worldY` of the doodle's max).
- **Start state:** doodle resting on a guaranteed full-width starter platform at `worldY = 0`,
  centered X. The first ~6 platforms are all **Static** with `gap = 90 px` to teach the rhythm.
- **Progression knobs vs. altitude:**
  - Platform gap widens (4.8) → 90 → 230 px.
  - Moving-platform weight rises; breakable weight rises; static weight falls (12).
  - Enemies begin at 3000 px and grow more frequent.
  - Jetpack/spring frequency stays roughly constant (rare relief).
- **Milestone feedback:** subtle background hue shift every 5000 px of altitude (cosmetic).

**Altitude bands (the felt shape of a run).** Difficulty is continuous (every knob is a function
of altitude), but it reads as five informal phases a designer can tune and test against:

| Band (altitude px) | Gap | Platform mix | Hazards | Player experience |
|---|---|---|---|---|
| 0 – 600 (Tutorial) | 90, fixed | Static only (first ~6 guaranteed) | none | Learn the ~1 s cadence; no failure pressure |
| 600 – 3000 (Groove) | 90 → ~120 | Static-heavy, Moving rising to ~0.15 | none | Chain bounces, first moving reads, springs appear |
| 3000 – 6000 (Pressure) | ~120 → ~165 | Moving ~0.2, Breakable entering | Enemies begin (4.9) | First real deaths; stomps and dodges |
| 6000 – 11,600 (Squeeze) | ~165 → 230 | Static 0.40 / Moving 0.30 / Breakable 0.30 | Enemies frequent | Every alignment matters; springs are lifelines |
| 11,600+ (Ceiling) | 230, saturated | full mix | densest enemies | Gap is fixed at the reachability limit; pure execution |

- **The Squeeze→Ceiling handoff is the intended skill wall:** once `gap` saturates at `maxGap =
  230 px` (≈ altitude 11,600, 4.8) nothing gets *harder* mechanically — the numbers stop moving.
  Beyond that the run is a test of sustained precision, not escalating parameters, which keeps very
  high scores about the player rather than about a difficulty curve that eventually cheats.
- **Density stays roughly constant in reach-space:** because both the gap *and* the apex are fixed
  ceilings, the number of platforms visible on screen falls from ~11 rows early (1280/90 + margin)
  to ~5 rows high up (1280/230) — the world literally gets emptier as you climb, which is the
  visual language of altitude.
- **Relief cadence:** spring frequency and jetpack minimum-separation (4.7) are altitude-invariant,
  so the *rate* of relief is constant while the *need* for it grows — the intended tightening.
- **Cosmetic bands never gate play:** the 5000 px hue shift and any parallax (8) are read-only; a
  fixed-seed run (13) renders identical background state at identical altitudes for snapshot tests.

## 7. State Model (Elmish/MVU)

```fsharp
open FS.GG.Game.Core
// Positions/velocities live in the scaffold's collision-safe Geometry.Vec2 ({ Vx; Vy }, from
// src/<ProductDir>/Vec2.fs) — NEVER a record you label X/Y/Width/Height, which collide with
// Scene's Point/Rect. This is a type ABBREVIATION: it adds no labels, so nothing can collide.
type Vec2 = Geometry.Vec2

type Phase = Title | Playing | Paused | GameOver

type DoodleState = Rising | Falling | Jetpack | Dead

type Doodle =
    { Pos: Vec2                     // Vy = worldY, up = negative
      Vel: Vec2                     // px/s
      FacingRight: bool
      State: DoodleState
      JetpackTimer: float }

type Model =
    { Phase: Phase
      Doodle: Doodle
      Platforms: Platform list
      Pickups: Pickup list
      Enemies: Enemy list
      CameraY: float                // top-of-view worldY; only ever decreases
      MaxClimb: float               // most-negative worldY reached (for score/camera)
      Score: int                    // = int (-MaxClimb) (altitude)
      Best: int
      SpawnCursorY: float           // worldY above which we still need to generate
      NextId: int
      Rng: Rng                // FS.GG.Game.Core — a VALUE, so the Model stays one (§13)
      InputLeft: bool
      InputRight: bool
      ElapsedMs: float }

type Msg =
    | StartGame
    | Tick of dt: float             // seconds since last frame (~0.0167)
    | SteerLeft of pressed: bool    // key down / up
    | SteerRight of pressed: bool
    | TogglePause
    | Restart
    | Shoot                         // stretch
```

**update — important cases:**
- `StartGame` / `Restart`: build a fresh `Model` with the starter platform, RNG seeded
  (13), `Phase = Playing`.
- `SteerLeft/Right pressed`: set `InputLeft/InputRight`; movement is applied in `Tick`.
- `TogglePause`: `Playing ↔ Paused` (Tick is a no-op while `Paused`).
- `Tick dt` (the simulation step, only when `Playing`):
  1. Compute target `vx` from input; integrate `vx` (4.4) and horizontal screen-wrap.
  2. Apply gravity → `vy` (clamp to ±1600); integrate `worldY`.
  3. **Swept collision** vs. platform tops (4.5); on land, apply bounce / spring / break.
  4. Check pickups (jetpack) and enemies (lethal vs. stomp).
  5. Update moving platforms, breakable timers, enemy drift; cull off-screen entities below
     `cameraY + 1380`.
  6. Update `MaxClimb`, `Score`, and `CameraY` (lerp up only, 4.6).
  7. Generate new platforms/enemies above `SpawnCursorY` until it's above the camera
     (4.8, 12).
  8. **Death check:** if doodle's top edge `> cameraY + 1280` (fell off bottom) → `Dead`,
     `Phase = GameOver`, update `Best`.

**view:** pure function of `Model` → a Skia draw list (see 8). No mutation, no simulation in
the view; it reads `CameraY` to transform world→screen.

**Subscriptions:**
- A render/sim timer at **60 FPS** dispatching `Tick dt` with `dt` in seconds (clamped, 13).
- Keyboard subscription dispatching `SteerLeft/Right (down/up)`, `StartGame`, `TogglePause`,
  `Restart`, `Shoot`.

## 8. Rendering (Skia 2D)
**World→screen transform:** `screenY = worldY - cameraY`; `screenX = worldX`. Cull anything
with `screenY` outside `[-100, 1380]`.

**Draw order (back → front):**
1. **Background** — vertical gradient (top `#1B2A4A` → bottom `#2E4372`), hue-shifted slowly by
   altitude. Optional parallax dots/clouds at 0.3× scroll.
2. **Platforms** — rounded rects, 8 px corner radius:
   - Static `#5BBF5B`, Moving `#4A90E2`, Breakable `#9B6B3F` (with crack overlay when
     `Breaking`), Spring `#5BBF5B` + a gray coil glyph `#BBBBBB`.
3. **Pickups** — jetpack `#F5A623` with flame; spring item `#CCCCCC`.
4. **Enemies** — `#C0392B` body, simple eyes; flash white 1 frame when stomped.
5. **Doodle** — `54×60` green `#7ED321` blob; flip horizontally by `FacingRight`; squash/stretch
   on bounce (scale Y 1.15 for 80 ms after a bounce). Jetpack flame trail while in `Jetpack`.
6. **Particles** — break debris (brown shards), spring sparkles, jetpack smoke.
7. **HUD** — score top-left, drawn in screen space (not world-transformed).

**Fonts:** sans-serif bold; score 40 px, game-over title 64 px. **Colors** as hex above.
**Redraw strategy:** full-frame redraw every tick (clear → draw list); the scene is small
enough (<120 entities) that partial invalidation isn't needed. Coordinate origin top-left,
y-down on screen.

## 9. UI / HUD / Screens
- **Title:** game name centered, "Press Space to Start", best score, a looping idle bounce
  animation of the doodle.
- **Playing HUD:** **Score** (current altitude, integer) top-left at `(16, 16)`, 40 px.
  Optional small **Best** under it. Jetpack remaining-time bar near top when active.
- **Pause:** dim overlay (`#000000` at 50% alpha) + "Paused — press Esc to resume" centered.
- **Game Over:** dim overlay, "Game Over" 64 px centered, final **Score** and **Best** below,
  "Press Space to Restart". If a new best, show "New Best!" badge.

### 9.1 Menu system (detailed)
A single **menu stack** drives every non-play screen (Title, Settings, Stats, Pause, Run
Over). Each menu is a vertical list of rows with a cursor, so one small update handler serves
them all and navigation is identical everywhere. The game is endless with no lives, so the
death screen is framed as **Run Over** and its primary action is **New Run**, not a life-loss
continue.

**Menu tree**
```
Title ─┬─ New Run ─────────── seed a fresh run and start climbing (§7 StartGame)
       ├─ Stats ──────────── Stats & Charts screen (§9.2)
       ├─ Settings ───────┬─ Difficulty     ◄ Easy · Normal · Hard ►
       │                  ├─ Master volume  ◄ 0 – 100 ►
       │                  ├─ Sound          ◄ On · Off ►
       │                  ├─ Window scale   ◄ 1× · 2× · Fit ►
       │                  ├─ Screen shake   ◄ On · Off ►
       │                  └─ Back
       └─ Quit

Pause ─┬─ Resume
       ├─ Restart Run
       ├─ Settings ───────── (same submenu; returns to Pause)
       └─ Quit to Title

Run Over ─┬─ New Run ──────── seed and start a fresh climb
          ├─ View Stats ───── Stats & Charts (§9.2)
          └─ Title
```

**Navigation model**
- `MenuCursor: int` on the active menu; `↑`/`W` decrement, `↓`/`S` increment, both **wrap**.
- `Enter`/`Space` activates the current row; `Esc`/`Back` pops the stack (**Back**).
- **Cycler/slider rows** (Difficulty, Master volume, Sound, Window scale, Screen shake):
  `←`/`→` change the value in place; the row shows a right-aligned `◄ value ►` widget.
- Rendering reuses the §9 Title style: the selected row is inverted/highlighted; non-selected
  rows are drawn plain over the dimmed scene.

**Msg additions** (extend the §7 `Msg` type):
```fsharp
    | MenuUp | MenuDown              // move cursor (wraps)
    | MenuAdjust of dir:int          // -1 / +1 on a cycler/slider row
    | MenuActivate                   // Enter/Space on the current row
    | MenuBack                       // Esc — pop the menu stack
    | OpenStats | CloseStats         // enter / leave the Stats screen (§9.2)
```

Settings apply live and persist to local config (§13): **Difficulty** selects the §12 preset
(Easy `g 2000 / enemyStartAlt 6000`, Normal `g 2400 / enemyStartAlt 3000`, Hard
`g 2800 / enemyStartAlt 1500`, with `gapGrowth`/spring weight scaled to match);
**Master volume**/**Sound** route to `Audio.setMasterVolume` (§10, clamped `[0,1]`, mirroring
the `M` mute toggle); **Screen shake** toggles the §8 spring/jetpack camera-kick effect.

### 9.2 Stats & charts screen
The Stats screen visualizes **the last run** and **lifetime** play. It reads a `Stats`
snapshot (never live physics), so it is a pure, deterministic render reachable from Title, Run
Over, and Pause. Chart-design choices below follow the project dataviz conventions (form-first,
validated colorblind-safe categorical palette, single axis, identity by entity).

**Tracked per run** — `RunStats`, accumulated in `Tick`, snapshotted on `GameOver`:

| Field | Type | Updated |
|-------|------|---------|
| `maxHeight` | `float` (px) | max altitude reached (`-MaxClimb`, §4.6) |
| `platformsHit` | `int` by `PlatformKind` (Normal/Moving/Breakable/Spring) | ++ the landed kind on each bounce/contact (§4.3, §4.7) |
| `springsUsed` | `int` | ++ on each Spring super-bounce (§4.7) |
| `monstersDodged` | `int` | ++ when an enemy is culled below camera with no contact (§4.9) |
| `monstersKilled` | `int` | ++ on each stomp kill (§4.9) |
| `fallCause` | `FallCause` (`OffBottom` \| `Monster`) | set once at death (§11) |
| `runSeconds` | `float` | accumulated live-play seconds |

**Lifetime** — `LifetimeStats`, persisted (§13): `bestHeight`, `runsPlayed`, `avgHeight`
(rolling mean of `maxHeight`), `mostMonstersDodged`.

**Layout** (logical 720×1280 portrait): a KPI tile row across the top, two charts below.

```
┌───────────────────────── STATS ─────────────────────────┐
│ ┌ BEST HEIGHT ┐ ┌ RUNS ┐ ┌ AVG HEIGHT ┐ ┌ DODGED ┐      │  ← KPI stat tiles
│ │  18,240 px  │ │  57  │ │  6,120 px  │ │   214  │      │
│ └─────────────┘ └──────┘ └────────────┘ └────────┘      │
│                                                          │
│  Platforms hit by type          Height over time         │
│  ▇▇                        18k ┤              ╭──         │
│  ▇▇                            │          ╭──╯            │
│  ▇▇   ▇▇                       │     ╭───╯                │
│  ▇▇   ▇▇   ▇▇   ▇▇          0k ┼────────────────► sec     │
│  Nrm  Mov  Brk  Spr                                       │
└──────────────────────────────────────────────────────────┘
   ↑/↓ scope:  ▸ This Run · Lifetime            ESC — Back
```

**Charts** (rendered in Skia with the same draw-list discipline as §8):

1. **Platforms hit by type** — *form: per-category magnitude → bars.* x = platform type
   (`Normal`, `Moving`, `Breakable`, `Spring`), y = number landed on this run. **Single
   series**, so one hue and no legend. Bars are 4 px-rounded at the data end with a 2 px
   surface gap between them. Fill `#2a78d6` (light) / `#3987e5` (dark) — validated categorical
   slot 1. Comparing magnitudes across the four types is one hue, never a rainbow.
2. **Height over time** — *form: change over an ordered index → line.* x = elapsed seconds of
   the last run, y = altitude climbed; **one series** (the doodle's climb), so one hue and no
   legend. Draw a single climbing line in `#2a78d6` (light) / `#3987e5` (dark), 2 px stroke
   with an ≥ 8 px end marker at the final height, over recessive 1 px gridlines in `#3C3C3C`.

Conventions honored: **color follows the entity** (the climb series and the type bars are
always slot 1 — never repainted by the scope toggle); **one axis only** (height is a single
y-scale; no dual axis); chart **text uses ink tokens** (`#FFFFFF` primary / `#C3C2B7` muted),
never the series hue; layout is **fixed and deterministic**, so a fixed-seed run (§13) renders
byte-identical for snapshot tests. The `↑/↓` **scope** toggle swaps the data source This-Run ↔
Lifetime without changing colors (Lifetime shows aggregate type-counts and the best run's
height curve).

**Model/Msg hooks:** add `Stats: RunStats` and `Lifetime: LifetimeStats` to the §7 `Model`;
accumulate them in the `Tick` case (bump `platformsHit`/`springsUsed` on land, `monstersKilled`
on stomp, `monstersDodged` on off-camera cull, sample `maxHeight` from `MaxClimb`, advance
`runSeconds`). On the `GameOver` transition set `fallCause`, fold `RunStats` into `Lifetime`
(update `bestHeight`, `runsPlayed`, `avgHeight`, `mostMonstersDodged`), and persist (§13).
`OpenStats`/`CloseStats` switch a Stats overlay carrying a `scope:StatScope`; the render is a
no-op on physics.

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
| Auto-bounce off a platform (§4.3) | `Audio.playSfx (SoundId "bounce") 0.8` | `bounce` | Normal bounce — short "boing" (pitch slightly randomized ±5%). |
| Spring super-bounce (§4.7) | `Audio.playSfx (SoundId "spring-bounce") 0.9` | `spring-bounce` | Spring bounce — higher "spring" twang. |
| Jetpack thrust active (§5) | `Audio.playSfx (SoundId "jetpack-thrust") 0.7` | `jetpack-thrust` | Jetpack — looping thrust whoosh for its duration. |
| Breakable platform contact (§4.7) | `Audio.playSfx (SoundId "platform-break") 0.8` | `platform-break` | Breakable platform — crumble/snap. |
| Enemy stomp (§4.9) | `Audio.playSfx (SoundId "enemy-stomp") 0.8` | `enemy-stomp` | Enemy stomp — squish. |
| Lethal enemy contact (§4.9, §11) | `Audio.playSfx (SoundId "enemy-hit") 0.9` | `enemy-hit` | Enemy hit (death) — thud/zap. |
| Jetpack pickup grabbed (§5) | `Audio.playSfx (SoundId "pickup-grab") 0.8` | `pickup-grab` | Pickup grab — chime. |
| Game Over (§11) | `Audio.playSfx (SoundId "game-over") 0.9` | `game-over` | Game over — descending tone. |
| New best score (§9) | `Audio.playSfx (SoundId "new-best") 1.0` | `new-best` | New best — fanfare. |

This game has a light loopable background track, so `update` requests
`Audio.playMusic (TrackId "bg-loop") true` when a run starts (§7) and `Audio.stopMusic` when
music stops at Game Over (§11); the mute toggle (`M`) maps to `Audio.setMasterVolume` (`0.0`
to mute, `1.0` to restore). **Testing:** collect the frame's
`AudioEffect`s, `Audio.interpret` them, and assert the `AudioEvidence.Requested` sequence for
representative events (e.g. an auto-bounce on a Static platform requests exactly
`PlaySfx (SoundId "bounce", _)`).

## 11. Win / Loss / Scoring
- **Scoring:** `Score = floor(altitude) = floor(-MaxClimb)` in px of climb. Score **only ever
  increases** (tied to `MaxClimb`, which never decreases). No points for time or kills in v1
  (enemy kills are survival, not score — *optional*: +50 per stomp).
- **No win condition** — endless; the goal is a personal best.
- **Loss conditions:**
  1. Doodle falls below the bottom of the (never-descending) camera: doodle top edge
     `> cameraY + 1280`.
  2. Doodle makes lethal contact with an enemy (not a stomp).
- **Lives/continues:** none. One life per run; instant restart from Game Over.
- **Best score** persisted across sessions (13).

**Scoring edge cases & resolution:**
- **Score samples `MaxClimb`, not the doodle's current Y.** A doodle at apex sets a new
  `MaxClimb`; the subsequent fall never lowers it (AC #5). `Score = floor(-MaxClimb)` is therefore
  monotonic non-decreasing tick over tick, by construction, no clamp needed.
- **Sub-pixel altitude** is tracked as `float` `worldY` and only floored *at read time* for
  display/`Score`, so two runs that reach `-4999.6` vs `-5000.4` show `4999` vs `5000` — the floor
  is deterministic and replay-stable (13), never a rounding wobble.
- **A jetpack/spring launch can jump `MaxClimb` by hundreds of px in a few ticks**; the score
  counter should still read the true `floor(-MaxClimb)` each frame (no eased "count-up"), so the
  HUD number is always the literal altitude, matching AC #5's exactness.
- **Optional stomp bonus (`+50`)** — if enabled (12), it adds to a *separate* `bonusScore` that is
  summed into the displayed total but **does not** feed `MaxClimb`; disabling it must not change
  altitude-only comparisons or the deterministic layout, so the two are kept orthogonal.

**Death resolution (order is load-bearing, mirrors §4.11 step 7):**
- Both loss conditions are checked *after* all movement and contact resolution in the tick, so the
  final `MaxClimb` (and thus `Score`/`Best`) reflects the highest point the doodle actually
  reached this tick even on the tick it dies.
- **`FallCause` is set exactly once** at the transition into `GameOver`: `OffBottom` if the
  top-edge test fired, `Monster` if an unresolved lethal enemy contact fired (9.2). If both are
  somehow true on one tick, **`Monster` wins** (you were killed before you could fall out).
- **On the `GameOver` transition:** update `Best` if `Score > Best`, request the `game-over` cue
  and `new-best` fanfare if applicable (10), stop `bg-loop` music (10), snapshot `RunStats` and
  fold into `Lifetime` (9.2) — all in that single transition, so a paused or re-entered Game Over
  screen never double-counts.
- **A dead doodle scores no further:** Tick is effectively frozen at `GameOver` (only menu Msgs
  are live), so `MaxClimb` cannot advance post-death.

## 12. Difficulty & Balancing

| Param | Default | Range | Effect |
|---|---|---|---|
| `g` (gravity) | 2400 px/s² | 1800–3000 | Higher = faster fall, tighter timing |
| `bounceVy` | -1150 px/s | -900..-1400 | Normal bounce height (apex ≈ vy²/2g) |
| `springVy` | -1900 px/s | -1600..-2200 | Spring boost height |
| `vxMove` | 520 px/s | 350–700 | Steering responsiveness |
| `vyMax` | 1600 px/s | 1200–2200 | Terminal fall speed |
| `baseGap` | 90 px | 70–120 | Platform spacing low down |
| `maxGap` | 230 px | 180–270 | Platform spacing high up (< apex 275) |
| `gapGrowth` | 0.012 | 0.005–0.02 | How fast spacing widens with altitude |
| `cameraLerp` | 0.18 / frame | 0.1–0.3 | Camera follow smoothness |
| `enemyStartAlt` | 3000 px | 1500–6000 | When enemies begin |
| `jetpackDuration` | 2.2 s | 1.5–3.5 | Jetpack thrust length |
| Spring weight | 0.08 | 0–0.2 | Fraction of platforms with springs |
| Moving weight | f(alt): 0.05→0.30 | — | Rises with altitude |
| Breakable weight | f(alt): 0.0→0.30 | — | Rises with altitude |
| Static weight | remainder | — | Falls with altitude |

**Type-weight schedule (per spawned platform), interpolated by altitude:**
- altitude 0: Static 0.95, Moving 0.05, Breakable 0.0, (Spring 0.08 applied as an independent
  roll on whatever non-breakable platform is chosen).
- altitude 6000+: Static 0.40, Moving 0.30, Breakable 0.30 (Spring roll unchanged).
Weights are data-driven so balancing is config, not code.

**Interpolation rule:** the three base weights are linearly interpolated by
`t = clamp(altitude / 6000, 0, 1)` between the altitude-0 and altitude-6000 rows, then normalized
to sum to 1 before sampling. The **Spring roll (0.08)** is a second, independent Bernoulli draw
applied *after* a non-breakable kind is chosen (4.7), so spring frequency is decoupled from the
Static/Moving mix and stays constant across the whole climb. All four draws come from the single
`Rng` value (13), in a fixed order — kind first, then spring roll, then X — so a seed reproduces the
exact platform stream (AC #12).

**Difficulty presets (extends §9.1).** The three presets scale the *whole tuning envelope*, not
just gravity, so each is internally coherent rather than one slider yanked:

| Knob | Easy | Normal | Hard |
|---|---|---|---|
| `g` | 2000 | 2400 | 2800 |
| normal apex (derived `bounceVy²/2g`) | ≈ 330 px | ≈ 275 px | ≈ 236 px |
| `enemyStartAlt` | 6000 | 3000 | 1500 |
| `gapGrowth` | 0.009 | 0.012 | 0.016 |
| effective `maxGap` (kept < apex) | 270 | 230 | 200 |
| Spring weight | 0.11 | 0.08 | 0.05 |
| Jetpack min-separation | 2200 px | 2500 px | 3000 px |

- **Presets stay self-consistent:** each preset's `maxGap` is re-derived to sit safely under *that
  preset's* apex (Easy's higher apex tolerates a wider ceiling; Hard's lower apex forces a tighter
  one), so the reachability invariant (4.8) holds under every difficulty. Lowering `g` on Easy
  raises apex, which is why Easy can afford both wider gaps *and* more springs and still feel
  gentle. Hard front-loads enemies and starves relief.
- **Presets are data, applied live:** switching difficulty in Settings (9.1) swaps the constant set
  between runs; it never edits code paths, and a fixed seed under a fixed preset is deterministic
  (13). Changing difficulty mid-run is disallowed (it would break the reachability guarantees baked
  into already-spawned rows) — the setting takes effect on the next `New Run`.
- **Two-axis balancing intuition:** vertical difficulty is owned by `g`/`gapGrowth`/`maxGap` (can I
  *reach* the next platform?); horizontal/attention difficulty is owned by Moving weight, enemy
  frequency, and `DriftVx` (can I *find and thread* it?). Tuning them independently lets a preset be
  "twitchy but forgiving" (Easy gaps, Hard enemy start) or the reverse, which is the design surface
  the presets sample.

## 13. Technical Notes
- **Performance budget:** ≤ ~120 active entities (platforms + enemies + pickups + particles)
  on screen; target **60 FPS / 16.7 ms** per frame. Full redraw per frame is fine at this scale.
- **Timestep:** **fixed-timestep simulation** at 60 Hz, drained by **`FixedStep.drainWith`** — do not
  hand-roll the accumulator. `FixedStep.drainWith (4.0/60.0) (1.0/60.0) frameTime acc` returns
  `struct (steps, acc')`: the whole `1/60` steps this frame owes, and the remainder to bank. The cap
  is the spiral-of-death guard — `4.0/60.0` is the max-4-steps-per-frame rule as the frame-time budget
  the function takes. This keeps bounce physics and collision deterministic regardless of frame rate.
- **Swept collision required:** at `vyMax = 1600 px/s`, the doodle moves ~26.7 px per fixed
  step — comparable to platform thickness — so test the previous→current Y sweep against
  platform tops (4.5) to prevent tunneling.
- **Determinism / RNG:** all procedural generation uses a single **`Rng`** (`FS.GG.Game.Core`,
  splitmix64) in the `Model`, seeded with `Rng.ofSeed`. Seeding the run reproduces an identical
  platform layout (useful for tests and the daily-challenge stretch goal). It is a **value**, not a `System.Random`: every draw returns `struct (x, rng')` and you write
  `rng'` back to the `Model`, so the `Model` stays a value you can snapshot, replay and compare.
  A `System.Random` in the `Model` is a mutable object *shared* by every copy of it, which
  silently breaks the reproducibility this bullet promises.
- **Persistence:** `Best` score saved to local storage / app settings; loaded on Title.
- **Edge cases:**
  - First platform guaranteed reachable; doodle starts *resting* on it (vy = 0) so the run
    doesn't begin with a fall.
  - Screen-wrap must not let the doodle "wrap onto" a platform it shouldn't — collision uses
    world X after wrap is applied.
  - Breakable platform contacted while a spring on the *same* tick: breakable wins (no bounce).
  - Jetpack active overrides all platform/enemy interactions (no death by enemy while
    thrusting — *optional*, configurable).
  - Pausing freezes `Tick`; the sim accumulator resets on resume to avoid a catch-up burst.

## 14. Acceptance Criteria (test scenarios)

1. **Auto-bounce on falling contact**
   *Given* the doodle is falling (`vy > 0`) and its feet sensor overlaps a Static platform top
   within the contact band, *When* a `Tick` is processed, *Then* `vy` becomes `-1150 px/s` and
   the doodle's `State` is `Rising`.

2. **No bounce while ascending (one-way collider)**
   *Given* the doodle is rising (`vy < 0`) and passes through a platform from below, *When*
   ticks are processed, *Then* no bounce occurs and `vy` is unchanged by the platform (only
   gravity applies).

3. **Spring super-bounce**
   *Given* the doodle lands (falling) on a Spring platform, *When* the land is resolved, *Then*
   `vy` becomes `-1900 px/s` (not `-1150`), and apex height above the platform is ≈ 752 px
   (within ±5%).

4. **Breakable platform gives no bounce and is removed**
   *Given* the doodle (falling) contacts a Breakable platform, *When* the land is resolved,
   *Then* `vy` is **not** set to a bounce value (doodle keeps falling), the platform enters
   `Breaking`, and after 0.25 s it is removed from `Platforms`.

5. **Score equals max altitude and never decreases**
   *Given* the doodle has reached `MaxClimb = -5000` (altitude 5000), *When* the doodle then
   falls 800 px, *Then* `Score` remains `5000` (does not drop with the fall).

6. **Camera scrolls up, never down**
   *Given* `CameraY = C` after climbing, *When* the doodle falls, *Then* `CameraY` is `≤ C`
   for all subsequent ticks (it never increases / never scrolls back down).

7. **Death by falling below camera**
   *Given* the doodle's top edge exceeds `cameraY + 1280`, *When* the death check runs in
   `Tick`, *Then* `Doodle.State = Dead` and `Phase = GameOver`, and `Best` is updated if
   `Score > Best`.

8. **Horizontal screen-wrap**
   *Given* the doodle's `x` moves past the right edge (`x > 720 + doodleHalfW`), *When* the
   position is normalized, *Then* `x` becomes `-doodleHalfW` (re-enters from the left), with
   `vy` unchanged.

9. **Held steering input**
   *Given* `SteerRight pressed=true` was received and not yet released, *When* successive
   `Tick`s run, *Then* the doodle's `x` increases by ≈ `520 * dt` per tick (up to wrap),
   and *When* `SteerRight pressed=false` arrives, *Then* horizontal target velocity returns
   to 0.

10. **Platform spacing thins with height (stays reachable)**
    *Given* generation at altitude 0 vs. altitude 10000, *When* platforms are spawned, *Then*
    average vertical gap is ≈ 90 px near 0 and saturates at ≤ 230 px high up — and `maxGap`
    (230) is strictly less than the normal bounce apex (275 px), so a reachable platform always
    exists.

11. **Enemy lethal contact vs. stomp**
    *Given* an enemy and a doodle moving **downward** whose feet hit the enemy's top, *When*
    resolved, *Then* the enemy dies and the doodle gets a normal bounce; **but** *Given* the
    doodle's body contacts the enemy while rising or sideways, *Then* the doodle dies
    (`GameOver`).

12. **Deterministic generation from seed**
    *Given* two runs started with the same RNG seed and identical input, *When* both simulate
    N ticks, *Then* their `Platforms` (positions, kinds) are identical.

13. **Jetpack overrides bounce physics**
    *Given* the doodle grabs a Jetpack, *When* the next `jetpackDuration = 2.2 s` of ticks run,
    *Then* `State = Jetpack`, `vy ≈ -2200 px/s` sustained, gravity is suppressed, platform
    bounces are ignored, and after 2.2 s `State` returns to `Falling` with gravity resumed.

14. **Frame-rate independence (fixed timestep)**
    *Given* the same input, *When* the sim runs at variable real frame rates (e.g. 30 vs 60
    FPS) using the fixed-timestep accumulator, *Then* the doodle reaches the same `MaxClimb`
    (within ±1 px) over the same elapsed time.

15. **Bounce impulse is fixed, not impact-scaled**
    *Given* two doodles landing on a Static platform, one at terminal `vy = 1600` and one at
    `vy = 400`, *When* each land resolves, *Then* both come away with exactly `vy = -1150`
    (the impulse is *set*, discarding arrival speed, §4.3).

16. **Multi-platform sweep lands on the lowest eligible top**
    *Given* a single fixed step whose swept feet sensor crosses two eligible platform tops,
    *When* the land resolves, *Then* the doodle lands on the **lower** platform (largest
    `worldY`, reached first on the way down) and never skips it (§4.3, §4.5).

17. **Opposing steer keys cancel**
    *Given* both `InputLeft` and `InputRight` held, *When* ticks run, *Then* target `vx = 0`
    and the doodle does not drift in either direction (§4.4).

18. **No generated gap exceeds the reachability ceiling**
    *Given* platforms generated across altitudes 0–20000, *When* each successive gap is
    measured, *Then* every gap is `≤ 230 px` (strictly under the `≈ 275 px` normal apex), so a
    reachable platform always exists (§4.8).

19. **Breakable wall forces a Static insertion**
    *Given* a band where every candidate within one bounce apex is Breakable, *When* the next
    platform is generated, *Then* its kind is forced to Static so no unlandable wall is emitted
    (§4.8).

20. **Second jetpack refreshes, does not stack**
    *Given* the doodle is in `Jetpack` with `vy = -2200`, *When* it grabs a second jetpack
    mid-thrust, *Then* `JetpackTimer` resets to `2.2 s` and `vy` stays `-2200` (no additive
    velocity), §5.

21. **Fall cause is set once, Monster wins ties**
    *Given* a tick on which the doodle both falls below `cameraY + 1280` and makes lethal enemy
    contact, *When* `GameOver` is entered, *Then* `fallCause = Monster` and it is written exactly
    once (§9.2, §11).

## 15. Stretch Goals
1. **Shooting** — fire upward projectiles to kill enemies that are unsafe to stomp.
2. **Jetpack & propeller-hat variety** — different boost shapes/durations.
3. **Vanishing (one-shot) platforms** and **moving-breakable** combos at high altitude.
4. **Daily challenge** — fixed seed shared by all players; leaderboard.
5. **Tilt/accelerometer & touch controls** for mobile targets.
6. **Power-up shield** that absorbs one lethal enemy hit.
7. **Cosmetic skins** unlocked by best-score milestones.
8. **Online high-score board** with ghost replay of the run path.

## 16. Milestone Roadmap

Implementation is sequenced into milestones; each item is a colored checkbox
tracking its status. Items reference the section that specifies them.

**Legend:** 🟥 Not started · 🟨 In progress · 🟩 Done · ⬜ Deferred (post-v1)

_All items start 🟥 (spec status). Flip an item to 🟨 when work begins and 🟩 once
its acceptance test(s) pass (§14)._

### M0 — Scaffold & fixed-step loop
- 🟥 Project scaffold: `Model`/`Msg`/`update`/`view` skeleton (§7)
- 🟥 Fixed 60 Hz tick via `FixedStep.drainWith`, banked remainder (§7, §13)
- 🟥 `Rng` value seeded with `Rng.ofSeed`, threaded through `Model` (§13)
- 🟥 Logical 720×1280 portrait frame, `worldY` up = negative (§4.1)

### M1 — Steering & screen-wrap
- 🟥 Held-key steer input `InputLeft`/`InputRight`, applied in `Tick` (§3, §7)
- 🟥 Velocity-based horizontal move `vxMove = 520 px/s`, optional accel smoothing (§4.4) — AC #9
- 🟥 Horizontal screen-wrap at `±doodleHalfW`, `vy` unchanged (§4.4) — AC #8
- 🟥 Opposing keys cancel to `vx = 0`; keyboard wins over pointer per tick (§4.4) — AC #17
- 🟥 Accel ramp (`0.13 s` to full, `0.26 s` reversal); target `vx` preserved across wrap (§4.4)

### M2 — Gravity, auto-bounce & one-way collision
- 🟥 Gravity `g = 2400 px/s²`, terminal `vyMax = 1600`, integrate `worldY` (§4.2)
- 🟥 Feet-sensor AABB + swept previous→current Y land test, `contactBand = 12 px` (§4.5, §13)
- 🟥 Auto-bounce `vy = -1150 px/s` only while falling (`vy > 0`) (§4.3) — AC #1
- 🟥 One-way collider: rising doodle passes through platforms, no bounce (§4.3) — AC #2
- 🟥 Fixed integration order (gravity → clamp → integrate), fall-phase timing (§4.2)
- 🟥 Impulse is *set* not impact-scaled; identical `-1150` at any arrival speed (§4.3) — AC #15
- 🟥 One-bounce-per-contact + lowest-first multi-platform sweep tie-break (§4.3, §4.5) — AC #16
- 🟥 Corner-overlap forgiveness, one-directional `12 px` band, snap-up before impulse (§4.5)
- 🟥 Doodle state-transition table `Rising`/`Falling`/`Jetpack`/`Dead` (§5)

### M3 — Platform types & procedural generation
- 🟥 `PlatformKind` set: Static / Moving / Breakable / Spring (§4.7)
- 🟥 Spring super-bounce `vy = -1900 px/s`, apex ≈ 752 px (§4.7) — AC #3
- 🟥 Breakable: no bounce, `Breaking` anim, removed after 0.25 s (§4.7, §5) — AC #4
- 🟥 Vertical gap growth `90 → 230 px` with altitude, reachability constraint (§4.8) — AC #10
- 🟥 Altitude-interpolated type-weight schedule + spring roll (§12)
- 🟥 Moving-platform patrol reflect, no horizontal velocity inheritance (§4.7)
- 🟥 Spring as non-breakable modifier (Static/Moving+Spring), never on Breakable (§4.7)
- 🟥 Jetpack min-separation `2500 px` + no jetpack within `1500 px` of start (§4.7)
- 🟥 Hard reachability invariant: no gap `> 230 px`, wrapped-distance horizontal rule (§4.8) — AC #18
- 🟥 Guaranteed-safe Static insertion when a breakable wall would block (§4.8) — AC #19
- 🟥 Weight interpolation by `t = alt/6000`, fixed RNG draw order kind→spring→X (§12)

### M4 — Camera, scoring & death
- 🟥 Camera lerps up only (`cameraY` never increases), 40%-from-top follow (§4.6) — AC #6
- 🟥 `MaxClimb` → `Score = floor(-MaxClimb)`, never decreases (§11) — AC #5
- 🟥 Death when doodle top `> cameraY + 1280` → `GameOver`, update `Best` (§7, §11) — AC #7
- 🟥 Sub-pixel `float` altitude floored at read-time; monotonic score (§11)
- 🟥 Death resolution order; `FallCause` set once, `Monster` wins ties (§11, §9.2) — AC #21
- 🟥 Optional orthogonal `+50` stomp `bonusScore` (does not feed `MaxClimb`) (§11, §12)

### M5 — Enemies & jetpack
- 🟥 Enemy spawn from altitude ≥ 3000 px, drift, off-camera cull (§4.9, §5)
- 🟥 Lethal body contact vs. head-stomp (falling) resolution (§4.9) — AC #11
- 🟥 Jetpack pickup + `Jetpack` overlay: `vy ≈ -2200` sustained 2.2 s, gravity/bounce suppressed (§5) — AC #13
- 🟥 Enemy drift/edge-reflect, no vertical chase, `600 px` spawn separation (§4.9)
- 🟥 Platform-first contact order, stomp uses fixed `-1150`, spawn-in grace (§4.9)
- 🟥 Jetpack exclusive overlay: second pickup refreshes timer, no velocity stacking (§5) — AC #20

### M6 — Rendering (Skia)
- 🟥 World→screen transform `screenY = worldY - cameraY`, cull off-view (§8)
- 🟥 Draw order: background, platforms, pickups, enemies, doodle, particles, HUD (§8)
- 🟥 Doodle squash/stretch on bounce, facing flip, jetpack flame trail (§8)

### M7 — UI, menus & settings
- 🟥 `Title`/`Playing`/`Paused`/`GameOver` phase states + screens (§7, §9)
- 🟥 Menu stack, cursor wrap, cycler/slider rows, Back-pop (§9.1)
- 🟥 Difficulty presets + volume/shake/scale settings apply live & persist (§9.1, §12, §13)

### M8 — Stats & charts
- 🟥 `RunStats` accumulation in `Tick`, snapshot + fold to `LifetimeStats`, persist (§9.2, §13)
- 🟥 Platforms-hit-by-type bar chart + height-over-time line chart, scope toggle (§9.2)

### M9 — Audio
- 🟥 `AudioEffect` cues per event, `Audio.interpret`, volume clamp `[0,1]` (§10)
- 🟥 `bg-loop` music start on run / stop at Game Over, `M` mute toggle (§10)

### M10 — Acceptance & determinism
- 🟥 All 14 acceptance scenarios green (§14)
- 🟥 Same seed + identical input → identical `Platforms` (§13) — AC #12
- 🟥 Frame-rate independence via fixed-timestep accumulator (§13) — AC #14
- 🟥 Extended scenarios green: impulse/tie-break/steer/reachability/jetpack/fall-cause (§14) — AC #15, #16, #17, #18, #19, #20, #21

### M11 — Game feel, progression shape & resolution order
- 🟥 Bounce-cadence (~0.958 s) and three-loop tension model validated vs. constants (§2)
- 🟥 Single system-interaction resolution order (input→wrap→jetpack→gravity→land→enemy→death) (§4.11)
- 🟥 Squash/stretch + spring-scaled juice; camera-kick recoil gated by Screen-shake setting (§4.10)
- 🟥 Near-miss dust telegraph + Title attract bounce on real physics (§4.10)
- 🟥 Altitude bands, reach-space density falloff, altitude-invariant relief cadence (§6)
- 🟥 Difficulty presets scale the whole envelope, apex-safe `maxGap`, next-run only (§12)

### Stretch — deferred (post-v1)
- ⬜ Shooting: upward projectiles to kill unsafe-to-stomp enemies (§15.1)
- ⬜ Jetpack & propeller-hat boost variety (§15.2)
- ⬜ Vanishing one-shot platforms & moving-breakable combos (§15.3)
- ⬜ Daily challenge: shared fixed seed + leaderboard (§15.4)
- ⬜ Tilt/accelerometer & touch controls (§15.5)
- ⬜ Power-up shield absorbing one lethal hit (§15.6)
- ⬜ Cosmetic skins unlocked by best-score milestones (§15.7)
- ⬜ Online high-score board with ghost replay (§15.8)
