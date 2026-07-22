---
title: "Asteroids"
slug: asteroids
category: games
complexity: simple
genre: "Arcade / shoot-'em-up (vector)"
target_session_minutes: 8
stack: { rendering: "FS.GG.Rendering (Skia/OpenGL)", framework: "FS.GG.Game.Core (FixedStep for the tick; Rng for determinism)", arch: "Elmish/MVU", lang: "F#" }
status: spec
---

# Asteroids

## 1. Overview
You pilot a fragile vector-drawn spaceship adrift in a wrap-around asteroid field.
The core verb is **shoot-and-survive**: tap the thruster to drift, rotate to aim, and
fire bullets that shatter big rocks into smaller, faster ones. Every shot makes the
field more crowded and more dangerous before it gets clearer, so the fun is the
escalating tension of momentum management — you can never fully stop, the screen wraps,
and a stray rock or a sniping UFO ends a life in one hit. It is a 45-second-per-wave
score chase that rewards precise thrust discipline and trigger control.

## 2. Core Game Loop
**Moment-to-moment:** rotate to aim → thrust to reposition → shoot asteroid → asteroid
splits → dodge the new fragments → repeat until the wave is clear.

**Session-level:** Title screen → press Start → spawn wave 1 (4 large asteroids) →
clear all rocks (and any UFO) → next wave (+1 large asteroid, faster) → keep clearing
until all 3 lives are lost → Game Over screen showing final score → press Start to
restart (RNG reseeded). Extra life awarded every 10,000 points.

**The density spike (why clearing feels tense):** shooting a Large does not thin the
field — it *thickens* it. One Large becomes 2 Medium becomes 4 Small before the lineage
is gone, so a fully-worked rock briefly puts up to 7 bodies where 1 was, and every
generation is faster than the last (§4.3). The field therefore reaches peak density right
after you commit to a rock, not before, and the skill is choosing *which* rock to open and
*where* you will be standing when its fragments scatter. Firing is never free: each shot is
a bet that you can out-position the debris it creates.

**Moment-to-moment cadence:** a competent player clears a wave in ~45 s. The tick order
(§7) resolves movement → UFO logic → collisions → wave-clear each frame, so within one tick
you may shoot a rock, have its children spawn, and — if you were sitting on the split point
— take those children into your hull. Position first, then fire, is the rhythm the physics
rewards; the floaty drag (§4.1) means the repositioning you did two seconds ago is still
carrying you now.

**Inter-wave beat:** clearing the last body freezes spawns for 2.0 s (§6) — the one moment
the field is provably empty. Momentum, bullets in flight, and any active invulnerability
carry across the boundary unchanged; only the asteroid/UFO population resets. This is the
built-in breather that lets the heartbeat music (§10) reset its tempo before the next wave's
faster rocks arrive.

**Failure-recovery loop:** a death does not reset the wave. Lives decrement, the field keeps
drifting, and you respawn dead-center under a 2.5 s invulnerability blink (§4.5) into whatever
the field had become — often more crowded than when you died. Recovery is its own mini-loop:
survive the grace window, re-establish a safe drift, and resume clearing the *same* wave you
were failing.

## 3. Controls & Input
Keyboard is primary. Input is sampled each tick; rotation and thrust are **held**
(continuous while down), fire and hyperspace are **edge-triggered** (one action per
key-down, ignore auto-repeat).

| Input | Key | Action | Model |
|-------|-----|--------|-------|
| Rotate left | `Left Arrow` / `A` | Turn ship CCW | Held |
| Rotate right | `Right Arrow` / `D` | Turn ship CW | Held |
| Thrust | `Up Arrow` / `W` | Apply forward acceleration | Held |
| Fire | `Space` | Spawn one bullet | Edge (key-down) |
| Hyperspace | `Left Shift` / `H` | Teleport to random location | Edge (key-down) |
| Start / Restart | `Enter` | Begin game / restart from Game Over | Edge (key-down) |
| Pause | `Esc` / `P` | Toggle pause overlay | Edge (key-down) |

No mouse or gamepad in v1 (see Stretch Goals). There is **no reverse thrust and no
brake** — you bleed speed only via drag.

## 4. Mechanics (detailed)
All physics run on a fixed timestep `dt = 1/60 s`. Positions are in logical pixels on a
1280×720 toroidal playfield. Angles are stored in **radians**, rotation rates quoted in
deg/s for readability.

### 4.1 Ship movement
- **Rotation rate:** 270 deg/s (= 4.712 rad/s). Holding rotate applies
  `heading += ±rotRate * dt`. Heading 0 points to screen-right (+x); the ship nose
  vector is `(cos heading, sin heading)`.
- **Thrust acceleration:** 220 px/s² along the nose vector while Thrust held.
  `vel += noseVec * thrustAccel * dt`.
- **Drag (linear damping):** velocity multiplied by `0.99` every tick
  (≈ `0.547` per second; i.e. `vel *= dragPerTick` with `dragPerTick = 0.99`). This
  gives a long, floaty glide that never quite reaches zero — by design.
- **Max speed:** 600 px/s. After integrating, clamp `|vel|` to `maxSpeed`.
- **Position integration:** `pos += vel * dt`, then apply screen wrap (§4.6).

**Feel & tuning detail:**
- **Decoupled aim and travel.** Rotation only changes `heading`; it never touches `vel`.
  You can therefore spin the nose 180° while still drifting the original direction — the
  classic "flip-and-burn" retro-thrust that is the *only* way to kill speed faster than
  drag. Reverse thrust does not exist (§3); flipping and thrusting is the sanctioned
  substitute and costs you the ~0.4 s it takes to turn 180° at 270 deg/s.
- **Order within the tick matters.** Rotation is applied before thrust each tick, so a
  frame that holds Rotate+Thrust together accelerates along the *already-turned* heading,
  not the pre-turn one. Curved burns thus arc slightly tighter than the raw numbers
  suggest — expected, not a bug.
- **Time-to-terminal.** From rest, holding Thrust reaches the 600 px/s cap in ~4.9 s;
  the drag term means acceleration tapers as speed rises rather than being linear, so the
  last 100 px/s take noticeably longer than the first. Most flying happens well under the
  cap, in the 150–350 px/s "controllable drift" band.
- **Drag floor.** `vel *= 0.99` never reaches exactly zero, so a ship left untouched keeps
  an imperceptible residual drift forever (sub-pixel per second within a few seconds). This
  is deliberate: there is no dead-stop state to rest in, which is what keeps every screen a
  live hazard. Rendering and collision treat sub-0.5 px/s drift as effectively parked.
- **Clamp preserves direction.** The max-speed clamp scales the whole velocity vector to
  length 600; it never zeroes a component, so a capped diagonal burn keeps its heading.
- **Wrap is speed-independent.** Even at the 600 px/s cap the ship moves only 10 px/tick,
  far less than the 120×120 respawn box or any asteroid radius, so no body can "tunnel"
  through a collision or skip the wrap band in a single step.

### 4.2 Firing & bullets
- **Bullet speed:** 700 px/s, added to the ship's current velocity at spawn so shots
  inherit ship momentum (muzzle velocity is `vel + noseVec * 700`). Speed is **not**
  clamped to ship max speed.
- **Spawn point:** ship nose tip = `pos + noseVec * 16` (ship is 16 px from center to
  nose).
- **Cooldown:** 250 ms minimum between shots (max 4 shots/s).
- **Max concurrent bullets:** 4. If 4 are already alive, Fire is ignored even if
  cooldown elapsed.
- **Lifetime:** 1.1 s, after which the bullet despawns. (At 700 px/s relative this
  travels ~770 px — a little over half the screen width, so you cannot snipe across the
  whole field.)
- **Bullet radius (hitbox):** 2 px. Bullets wrap on screen edges like everything else.

**Firing detail & edge cases:**
- **Momentum inheritance is directional, not just additive.** The muzzle velocity is the
  full vector sum `vel + noseVec * 700`, so shooting *along* your travel produces fast,
  short-lived long-range shots, while shooting *backward* while boosting can yield a bullet
  slower than 700 px/s relative to the field — occasionally a near-stationary "mine" that
  hangs where you fired it until its 1.1 s lifetime expires. Both are legal and both count.
- **Effective range varies with your motion.** The quoted ~770 px reach (§4.2) is the
  relative-to-ship figure; a bullet fired forward at full 600 px/s ship speed covers up to
  ~1430 px of field before expiring, nearly a full wrap, whereas a back-shot covers far
  less. Range is a property of the *shot*, decided at spawn, and never re-evaluated.
- **Cooldown and cap are independent gates.** A Fire edge spawns a bullet only if
  `FireCooldown = 0` **and** fewer than 4 player bullets are alive; failing either is a
  silent no-op that does not consume the edge's cooldown reset. In practice the 250 ms
  cooldown is the binding limit until bullets start expiring — with 1.1 s lifetime and a
  4-shot cap you can sustain the full 4 shots/s indefinitely only once early shots begin
  despawning.
- **Spawn is a point, travel is continuous.** The bullet is born at the nose tip
  (`pos + noseVec * 16`) already outside the 11 px ship hitbox, so a muzzle-adjacent rock
  is hit on the *next* tick, not the firing tick; there is no same-tick point-blank kill.
- **Own bullets are inert to the ship** (§4.4) for their whole life, so the slow back-shot
  "mines" above can be flown through safely — they only threaten asteroids and UFOs.

### 4.3 Asteroid splitting
Asteroids exist in three size classes. Shooting one removes it and (if not Small)
spawns **2 children** of the next size down.

| Size | Collision radius | Speed range | Children on death | Points |
|------|------------------|-------------|-------------------|--------|
| Large | 40 px | 20–60 px/s | 2 × Medium | 20 |
| Medium | 20 px | 40–90 px/s | 2 × Small | 50 |
| Small | 10 px | 60–130 px/s | none | 100 |

**Split behavior:** each child inherits the parent's position. Its velocity direction is
the parent's velocity heading rotated by a random offset in `±[15°, 45°]` (one child
each side), with a fresh speed magnitude drawn from the child class's speed range. Each
asteroid also has a slow visual spin: random angular velocity in `±[20, 90] deg/s`
(cosmetic only, does not affect the circular hitbox).

**Split geometry & edge cases:**
- **Children fan apart, never together.** The two offsets are mirrored across the parent
  heading (one in `+[15°, 45°]`, one in `−[15°, 45°]`), so the pair always diverges into a
  15°–90° wedge. They can never spawn on identical vectors, which guarantees the fragments
  separate rather than travelling as an inseparable clump.
- **Speed is redrawn, not inherited.** A slow Large drifting at 25 px/s still yields
  Medium children in the 40–90 px/s band — every generation is *categorically* faster than
  its parent, independent of how fast the parent was moving. This is the escalation engine
  of the density spike (§2): the field speeds up as it fragments.
- **Co-located spawn is intentional.** Both children start exactly on the parent's center,
  overlapping for one tick before their divergent velocities separate them. They do not
  collide with each other (asteroid↔asteroid never collides, §4.4), so the momentary
  overlap is harmless — but a ship sitting on that point eats whichever child reaches it.
- **Splitting at a wrap seam.** If the parent's center is within the wrap band, children
  inherit the same (possibly edge-straddling) position and are immediately subject to the
  §4.6 wrap on their first integration; their §8 duplicate-draws keep both halves visible.
- **The wave counter follows fragments, not the initial rocks.** A wave is not clear until
  *every* descendant — down to the last Small — is gone (§6). Killing all the Larges early
  is the start of the work, not the end of it.
- **Spin seeds render only.** `Angle` advances by `Spin * dt`; it rotates the baked `Shape`
  polygon for the vector look but the hitbox stays the class's circle (§4.4), so a jagged
  point poking outside the circle is never a hit and a notch inside it never a miss.
- **Fragment cap is structural, not enforced.** The full tree of one Large is 1 + 2 + 4 = 7
  bodies; with the wave cap of 11 Larges (§6) the theoretical ceiling is 44 Small + 22 Med +
  11 Large simultaneously, but only if nothing is ever shot. This bound is what §13's
  performance budget rests on — no runtime clamp on asteroid count is needed.

### 4.4 Collisions
All collisions are **circle vs circle**: overlap when
`distance(a, b) < a.radius + b.radius` (using wrap-aware shortest distance, §4.6).

- **Bullet ↔ Asteroid:** asteroid splits/dies, bullet despawns, score awarded.
- **Bullet ↔ UFO:** UFO dies, bullet despawns, score awarded.
- **Ship ↔ Asteroid:** ship destroyed (lose a life).
- **Ship ↔ UFO / UFO bullet:** ship destroyed.
- **UFO bullet ↔ Asteroid:** pass through (no interaction) to keep the UFO threatening.
- **Ship ↔ ship's own bullets:** never collide.
- Ship hitbox is a single circle of **radius 11 px** (smaller than the visual triangle,
  which feels fair).
- **Asteroid ↔ Asteroid:** never collide. Rocks drift through one another freely — modelling
  rock-on-rock physics would add nothing to the shoot-and-survive verb and would let the
  field lock itself into unclearable clumps.

**Resolution rules (within collision step 4, §7):**
- **Deterministic pass order.** Player-bullet↔hazard is resolved before ship↔hazard, and
  bodies are visited in stable list order (§13's determinism guarantee depends on this). Two
  runs with the same seed and inputs therefore award score and take deaths in the identical
  order.
- **One bullet, one body.** Each bullet resolves against at most one target per tick and
  despawns on its first hit, so a single shot never scores two rocks even when they overlap
  it simultaneously; the earlier body in list order wins and the bullet is spent.
- **A rock can die and its children live in the same tick.** When a bullet splits a Large,
  the two Medium children are inserted into the same frame and are eligible to collide with
  the *ship* on that same tick's ship↔hazard sub-pass — this is the same-tick "shot yourself
  into your own debris" case §2 warns about, and it is intended, not a race.
- **Invulnerability short-circuits ship checks only.** While `Time < InvulnUntil` (§4.5) or
  during the 0.3 s hyperspace-arrival grace (§4.7), every Ship↔X test is skipped outright —
  the ship passes through rocks, UFO, and UFO bullets. Bullet↔asteroid and bullet↔UFO scoring
  are unaffected, so you can still clear the field while blinking.
- **Ramming removes the rock but does not split it.** A ship↔asteroid death destroys the
  asteroid it struck (§5.2) with no points awarded (§11) and — unlike a bullet kill —
  spawns **no** children: fragmentation is tied to *shooting* (§4.3), so ramming trades a
  life to delete exactly one body rather than multiplying it. Ramming is never a scoring
  tactic, only an accidental one.

### 4.5 Death, respawn & invulnerability
- On ship death: decrement lives, emit explosion particles, freeze ship for 1.5 s, then
  respawn at screen center with zero velocity and heading `-90°` (pointing up).
- **Respawn safety:** the ship will not un-freeze until the center 120×120 px area is
  clear of asteroids/UFO; check each tick after the 1.5 s timer.
- **Spawn invulnerability:** 2.5 s after respawn the ship is invulnerable (ignores all
  Ship↔X collisions) and renders blinking (toggle visibility every 100 ms).
- If lives reach 0, transition to Game Over.

**Death & respawn detail:**
- **Death is a clean reset of ship state.** On respawn, `vel` is zeroed, `heading` is set to
  `-90°` (up), and any held Thrust/Rotate keys are honored again *only after* the freeze ends
  — inputs pressed during the 1.5 s freeze are read from `Keys` on the first live tick, so a
  player mashing Thrust through their own death bursts forward the instant control returns.
- **Bullets and fire-cooldown survive death.** Player bullets already in flight keep travelling
  and can still clear rocks while the ship is frozen; `FireCooldown` continues ticking down, so
  the ship can fire on its first live tick if the timer had elapsed. Only the *ship body* is
  removed during the freeze.
- **The three timers are sequential, not overlapping.** 1.5 s freeze → (center-clear gate) →
  2.5 s invulnerability. The invulnerability window starts the tick the ship un-freezes, so a
  respawn stalled by a cluttered center box does not "burn" invulnerability time while waiting.
- **Center-clear can stall indefinitely — by design and bounded in practice.** If asteroids
  keep drifting through the 120×120 box the ship stays frozen; because rocks are always in
  motion (no dead-stop, §4.1) the box statistically clears within a few seconds, and the frozen
  ship cannot itself be hit, so the stall is safe rather than a soft-lock. The center-clear test
  uses wrap-aware distance (§4.6) so a rock straddling the seam at center still counts.
- **No death-during-invulnerability, ever.** Because Ship↔X is skipped entirely while
  invulnerable (§4.4), overlapping a rock during the blink can never cost a second life on the
  same respawn — the classic "spawn-camped to zero" failure is structurally impossible.
- **Explosion budget.** Ship death emits a fixed burst of debris-line particles (§5.5) obeying
  the 200-particle cap; if the cap is already near full from asteroid explosions, the newest
  particles win and the oldest are dropped, so the ship blast always shows.

### 4.6 Screen wrapping (toroidal space)
The playfield is a torus of width `W = 1280`, height `H = 720`. Applies to ship,
bullets, asteroids, UFO, and particles.

```
// position wrap (per axis), after integration
let wrap (v: float) (size: float) =
    let m = v % size
    if m < 0.0 then m + size else m
// Pos is the scaffold's collision-safe Geometry.Vec2 — the labels are Vx/Vy, never X/Y
let wrapPos (p: Vec2) : Vec2 = { Vx = wrap p.Vx W; Vy = wrap p.Vy H }
```

**Wrap-aware distance** (shortest toroidal delta) used for all collision checks and the
UFO's aim:
```
let wrapDelta (a: float) (b: float) (size: float) =
    let d = b - a
    if d >  size / 2.0 then d - size
    elif d < -size / 2.0 then d + size
    else d
// dist² = (wrapDelta ax bx W)² + (wrapDelta ay by H)²
```

Objects must also render duplicated near edges (§8) so a body straddling a boundary is
visible on both sides.

### 4.7 Hyperspace
Pressing Hyperspace instantly teleports the ship to a uniformly random position in the
playfield and zeroes its velocity. It is an emergency escape with risk:
- **Cooldown:** 1.0 s.
- **Re-entry risk:** 12% chance the ship is destroyed on arrival (a "bad warp"),
  resolved *after* placement. Otherwise the ship is briefly (0.3 s) invulnerable on
  arrival so it never instantly dies to a rock it landed on (except via the 12% roll).
- No directional control over destination.

**Hyperspace detail:**
- **Placement is uniform over the full field, hazards ignored.** The destination is drawn
  independently on each axis across the whole 1280×720 torus — it can land you on top of a
  rock, in front of the UFO, or (rarely) right back where dense traffic is. The 0.3 s arrival
  grace exists precisely because the draw does not avoid hazards; it buys you time to read the
  new position and thrust clear before Ship↔X checks resume.
- **The bad-warp roll is resolved after placement and after the grace is granted.** Sequence
  per press: consume the 1.0 s cooldown → teleport → zero velocity → grant 0.3 s grace → roll
  `hyperBadWarpChance` (§12). A failed roll destroys the ship *through* the grace (bad warps
  ignore invulnerability), counts as a normal death (§4.5), and the destination the ship warped
  to is where its explosion particles emit.
- **Cooldown gates the edge, not a queue.** Pressing Hyperspace while `HyperCooldown > 0` is a
  silent no-op that neither teleports nor re-rolls; there is no buffered second jump. At the
  1.0 s cooldown the sustainable jump rate is one per second.
- **Velocity is fully discarded.** Unlike death-respawn, hyperspace keeps your `heading` but
  zeroes `vel`, so you arrive aimed the same way you left, dead in the water — you must
  re-thrust from zero, which is the escape's hidden cost in a fast-moving late wave.
- **Draw sequence and determinism.** The two position draws and the bad-warp draw all come from
  `Model.Rng` in a fixed order (§13), so a forced seed reproduces both the landing spot and the
  warp outcome exactly — the basis for AC #16's forced-0/forced-1 assertions.
- **When it is worth it.** With a 12% death chance, hyperspace is a strictly worse survival bet
  than a clean dodge whenever a dodge exists; it is the tool for the frames when no dodge exists
  — boxed in by fragments, or a Small-UFO shot already inbound (§4.8).

### 4.8 UFO enemy
A flying saucer crosses the field and shoots at the ship.
- **Spawn:** after 30 s of a wave with no UFO present, OR once the wave is down to ≤ 3
  asteroids — whichever first. Max 1 UFO at a time. At most 1 UFO spawn attempt per 20 s.
- **Two types** (chosen at spawn): **Large** (radius 18 px, 200 pts, fires in a random
  direction) and **Small** (radius 10 px, 1000 pts, aims directly at the ship with up to
  ±10° error). Small-UFO probability rises with score: `min(0.75, score / 40000)`.
- **Movement:** enters from a random vertical position on the left or right edge, travels
  horizontally at 120 px/s, and every 1.0 s may jog its vertical velocity to one of
  `{-90, 0, +90} px/s` for a zig-zag path. Despawns when it exits the opposite edge
  (UFOs do **not** wrap horizontally; they do wrap vertically).
- **Firing:** one bullet every 1.2 s. UFO bullet speed 350 px/s, lifetime 1.4 s,
  radius 2 px.

**UFO detail & edge cases:**
- **Spawn gating stacks three conditions.** A UFO appears only when (a) none is currently
  present, (b) the per-20 s attempt window has opened, and (c) either the wave has run
  `ufoSpawnDelay` seconds (§12, shortening with wave number, §6) *or* the field is down to
  ≤ 3 asteroids. The ≤ 3-rock trigger is what stops a nearly-clear wave from ever being
  UFO-free: finish the rocks fast and you may still owe a saucer.
- **Type is rolled once, at spawn, and is fixed for that UFO.** `min(0.75, score / 40000)`
  is the Small-saucer probability; at score 0 it is a Large every time, at 30,000 it is Small
  three quarters of the time. The cap keeps a rare Large in the mix even at very high scores,
  so the low-value/easy target never fully disappears.
- **Large fires blind, Small leads you.** The Large saucer draws its shot direction
  uniformly at random — a scattergun that is dangerous only by volume. The Small saucer aims
  at the ship's *current* position (wrap-aware, §4.6) with up to ±10° error; it does not lead
  your velocity, so holding a steady drift makes its shots trivially dodgeable while sitting
  still under a Small saucer is lethal. Movement is the counter to accuracy.
- **Aim uses the shortest toroidal vector.** A Small saucer near the right edge will fire at a
  ship near the left edge *through* the seam if that is the shorter path (§4.6), so wrapping to
  the "far" side is not cover — the saucer shoots the short way around.
- **Fire cadence is on its own clock.** `FireTimer` counts down independently of ship state; a
  UFO fires every 1.2 s whether or not it currently has a clear shot, and keeps firing while
  the ship is frozen/invulnerable — those shots simply pass through the ship (§4.4) but remain
  live for their 1.4 s and can still catch the ship after the grace ends.
- **Jog is a discrete zig-zag, not steering.** Every 1.0 s the vertical component is re-picked
  uniformly from `{−90, 0, +90} px/s`; horizontal speed stays a constant 120 px/s. The saucer
  therefore crosses the field in a bounded time regardless of jogging, and cannot hover or
  reverse — you can always out-wait it.
- **Asymmetric wrap.** UFOs wrap vertically (a jog off the top re-enters at the bottom) but
  **not** horizontally: reaching `ExitEdge` despawns the UFO cleanly, ending its threat and its
  looping warble (§10) with no score awarded. Letting a Large saucer simply leave is a valid,
  if unrewarded, way to survive it.
- **UFO bullets never help you.** They pass through asteroids (§4.4), so you can never bait a
  saucer into clearing rocks for you; its fire is pure threat.
- **One saucer at a time interacts with the wave-clear.** Because a wave is not clear while a
  UFO is present (§6), a lingering saucer holds the wave open even after the last rock dies —
  you must kill it or let it exit before the 2.0 s inter-wave pause can begin.

## 5. Entities / Game Objects
F#-flavored sketches; final field names may differ.

### 5.1 Ship
```fsharp
open FS.GG.Game.Core
// Positions/velocities live in the scaffold's collision-safe Geometry.Vec2 ({ Vx; Vy }, from
// src/<ProductDir>/Vec2.fs) — NEVER a record you label X/Y/Width/Height, which collide with
// Scene's Point/Rect. This is a type ABBREVIATION: it adds no labels, so nothing can collide.
type Vec2 = Geometry.Vec2

type Ship =
  { Pos: Vec2
    Vel: Vec2
    Heading: float          // radians
    Thrusting: bool         // for flame rendering
    InvulnUntil: float      // game-time seconds; 0 if not invulnerable
    FireCooldown: float     // seconds remaining
    HyperCooldown: float
    Alive: bool
    RespawnTimer: float }   // >0 while frozen/dead before respawn
```
Created once at game start and on each respawn. Destroyed (Alive=false) on collision.

### 5.2 Asteroid
```fsharp
type AstSize = Large | Medium | Small
type Asteroid =
  { Pos: Vec2; Vel: Vec2
    Size: AstSize
    Radius: float
    Spin: float             // deg/s, cosmetic
    Angle: float            // current render rotation (rad)
    Shape: Vec2[] }         // pre-baked jagged polygon (8–12 verts), unit-scaled
```
Created at wave start (Large only) and on split (children). Destroyed when shot or when
it collides with the ship.

### 5.3 Bullet
```fsharp
type BulletOwner = Player | Ufo
type Bullet =
  { Pos: Vec2; Vel: Vec2
    Life: float             // seconds remaining
    Owner: BulletOwner }
```

### 5.4 Ufo
```fsharp
type UfoKind = LargeSaucer | SmallSaucer
type Ufo =
  { Pos: Vec2; Vel: Vec2
    Kind: UfoKind
    Radius: float
    FireTimer: float
    JogTimer: float
    ExitEdge: float }       // x at which it despawns
```

### 5.5 Particle (explosions / thrust)
```fsharp
type Particle =
  { Pos: Vec2; Vel: Vec2; Life: float; MaxLife: float }
```
Short-lived debris lines for ship/asteroid explosions and thrust exhaust. Cosmetic only,
no collisions; capped at 200 live particles.

### 5.6 Entity lifecycles & invariants
Every entity is a plain value in one of the §7 `Model` collections; there are no back-
references between them, so the whole simulation is `Model → Model` per tick.

| Entity | Born | Dies | Max live | Collides with |
|--------|------|------|----------|---------------|
| Ship | game start / respawn (§4.5) | Ship↔hazard or bad warp (§4.4, §4.7) | 1 | asteroids, UFO, UFO bullets |
| Asteroid | wave start / split (§4.3) | shot or rammed (§4.4) | 44 Sm + 22 Md + 11 Lg (§4.3) | ship, player bullets, UFO bullets pass through |
| Player bullet | Fire edge (§4.2) | first hit or 1.1 s lifetime | 4 | asteroids, UFO |
| UFO bullet | UFO fire timer (§4.8) | first hit on ship or 1.4 s lifetime | ~2 (one every 1.2 s, 1.4 s life) | ship only |
| UFO | spawn gate (§4.8) | shot or exits `ExitEdge` | 1 | ship, player bullets |
| Particle | explosion / thrust (§5.5) | `Life ≤ 0` | 200 (oldest dropped) | nothing |

**Cross-entity invariants a test can assert:**
- **Ownership is total.** Every bullet carries an `Owner` (§5.3); scoring and Ship↔bullet
  rules key off it exclusively (§4.4, §11). There is no unowned bullet.
- **Position/velocity are always `Vec2`** (`Vx`/`Vy`), never separate scalars, so wrap
  (§4.6) and collision (§4.4) apply uniformly to every mobile body without special cases.
- **Collections are the source of truth for counts.** "4 bullets alive" (§4.2), "1 UFO"
  (§4.8), and "wave clear" (§6) are all just list-length / `option` checks — nothing tracks
  a separate counter that could desync.
- **Nothing self-references time.** Every lifetime/cooldown/timer field counts *down* in
  seconds and is decremented by the same `dt` (§7 step 2), so the entire clock of the game
  advances in lockstep and is reproducible under a fixed seed (§13).

## 6. World / Levels / Progression
- **Playfield:** 1280×720 logical px, toroidal (wraps both axes). No camera, no scroll.
- **Waves:** Wave `n` spawns `min(4 + (n - 1), 11)` Large asteroids (capped at 11).
  Large asteroids spawn at random edge positions, never within 150 px of the ship's
  spawn center, with random initial velocities in the Large speed range.
- **A wave clears** when there are zero asteroids AND no UFO on screen. After a 2.0 s
  pause the next wave spawns.
- **Difficulty ramp per wave:**
  - Asteroid base speed range scales by `min(1.6, 1 + 0.06 * (n - 1))`.
  - Small-UFO bias grows with score (§4.8).
  - UFO appears sooner as `n` rises: spawn delay `max(8, 30 - 2 * (n - 1))` seconds.
- Nothing else changes (ship stats are constant), keeping it "simple".

**Wave spawn placement (detail):**
- **Edge-band spawn.** Each Large is placed at a random point within the outer wrap band of
  the field, biased away from center, then nudged out of the 150 px no-spawn disc around the
  ship's spawn center (§13 edge case (d)) by re-drawing until clear. This guarantees the wave
  never materialises on top of a freshly-spawned ship.
- **Initial headings are unconstrained.** Each Large gets a uniform-random heading and a speed
  from its class band (§4.3) scaled by the per-wave ramp below; some rocks drift toward center,
  some away. There is no "safe corridor" — the field is live the instant the wave spawns.
- **Placement draws are seeded.** All spawn positions/velocities come from `Model.Rng` in list
  order (§13), so AC #20's identical-spawn guarantee holds for every wave, not just wave 1.

**Cap & ramp saturation (endless tail):**
- **Rock count caps at wave 8.** `min(4 + (n − 1), 11)` reaches 11 at wave 8 and stays there;
  waves 8+ do not add Larges. Past that point the wave-over-wave difficulty comes entirely from
  the speed ramp and the earlier/faster UFO, not from more rocks — this is the deliberate ceiling
  that keeps the O(n²) collision cost (§13) bounded forever.
- **Speed ramp caps at wave 11.** `min(1.6, 1 + 0.06 * (n − 1))` hits its 1.6× ceiling at
  wave 11; a Large that would drift at 60 px/s tops out at 96 px/s, a Small at 130 tops out at
  208. Beyond wave 11 no tuning changes at all except the score-driven Small-UFO bias (§4.8) —
  the endless game asymptotes to a fixed, maximally-fast field and becomes a pure execution test.
- **The two caps are staggered on purpose.** Count saturates (wave 8) three waves before speed
  (wave 11), so the mid-game difficulty curve has two distinct phases: "more rocks" then "faster
  rocks", rather than both arriving at once.

**Wave-clear edge cases:**
- **A live UFO holds the wave open** even with zero rocks (§4.8); the 2.0 s pause and next-wave
  spawn wait until the saucer is killed or exits.
- **In-flight bullets do not gate the clear.** Player and UFO bullets may still be alive when the
  last rock/UFO dies; the wave clears anyway and those bullets carry into the pause and, if still
  alive, into the next wave.
- **The inter-wave pause is a spawn hold, not a freeze.** During the 2.0 s the ship, particles,
  and any surviving bullets keep integrating and wrapping; only asteroid/UFO *spawning* is
  suspended. You can reposition, and even fire, before the next wave lands.
- **Nothing resets between waves except the rock/UFO population.** Score, lives, ship momentum,
  active invulnerability, fire cooldown, and the extra-life threshold all carry across (§2).

## 7. State Model (Elmish/MVU)

### Model
```fsharp
type Phase = Title | Playing | Paused | GameOver

type Model =
  { Phase: Phase
    Ship: Ship
    Asteroids: Asteroid list
    Bullets: Bullet list
    Ufo: Ufo option
    Particles: Particle list
    Score: int
    Lives: int
    Wave: int
    NextExtraLifeAt: int        // e.g. 10000, then 20000, ...
    Time: float                 // accumulated game-time seconds
    WaveClearTimer: float       // >0 during inter-wave pause
    UfoSpawnTimer: float
    Keys: Set<Key>              // currently-held keys
    Rng: Rng                // FS.GG.Game.Core — a VALUE, so the Model stays one (§13)
    HighScore: int }
```

### Msg
```fsharp
type Msg =
  | Tick of float               // dt in seconds (fixed 1/60)
  | KeyDown of Key
  | KeyUp of Key
  | StartGame
  | TogglePause
```

### update (key cases)
- **`Tick dt`** (only when `Phase = Playing`): the simulation step, in order:
  1. Apply held rotation/thrust to ship; integrate ship vel/pos; apply drag, clamp
     max speed, wrap.
  2. Integrate asteroids, bullets, UFO, particles; wrap; decay lifetimes/timers.
  3. UFO logic: spawn check, jog/zig-zag, fire timer.
  4. Collision resolution (bullets→rocks/UFO→split & score; ship→hazards→death).
  5. Wave-clear check → `WaveClearTimer`; next-wave spawn when it elapses.
  6. Extra-life check (`Score ≥ NextExtraLifeAt`).
  7. Respawn/invulnerability timers; `Lives = 0` → `Phase = GameOver`,
     update `HighScore`.
- **`KeyDown k`**: add to `Keys`; if edge action (Fire/Hyperspace/Pause/Start) handle
  once here (respecting cooldowns). Fire spawns a bullet if `< 4` alive and cooldown 0.
- **`KeyUp k`**: remove from `Keys`.
- **`StartGame`**: reset Model to a fresh game (lives=3, score=0, wave=1, reseed Rng),
  `Phase = Playing`.
- **`TogglePause`**: `Playing ↔ Paused` (Tick is a no-op while Paused).

### view
Pure projection of `Model` → a scene description Skia draws (§8). The view holds no
mutable state and performs no physics — it reads ship/asteroid/bullet/UFO/particle lists
plus HUD numbers and emits draw commands.

### Subscriptions
- A 60 FPS timer subscription dispatching `Tick (1/60)` (fixed timestep; see §13 for
  accumulator handling under variable real frame times).
- Keyboard subscription dispatching `KeyDown`/`KeyUp`.

## 8. Rendering (Skia 2D)
Coordinate system: origin top-left, +x right, +y down, logical 1280×720 (scaled to the
window). Classic vector look: black background, thin bright strokes, no fills.

**Draw order (back to front):**
1. **Background** — solid black `#000000` full-rect clear each frame.
2. **Particles** — 1 px lines, color `#AAAAAA` fading alpha by `Life/MaxLife`.
3. **Asteroids** — closed polygons from `Shape` rotated by `Angle`, stroke `#FFFFFF`,
   1.5 px width, no fill.
4. **UFO** — saucer outline (two stacked trapezoids + dome), stroke `#FF5555`, 1.5 px.
5. **Bullets** — 2 px filled squares/dots; player `#FFFFFF`, UFO `#FF5555`.
6. **Ship** — isosceles triangle (nose 16 px ahead of center, tail corners ±10 px),
   stroke `#00FFAA`, 2 px; when `Thrusting`, draw a flickering exhaust triangle behind
   the tail in `#FFAA00`. Skip drawing on blink-off frames during invulnerability.
7. **HUD** (§9) on top.

**Wrap rendering:** for any body whose circle crosses an edge, draw it again offset by
`±W` / `±H` so the straddling portion shows on the opposite side (up to 4 duplicate
draws near a corner).

**Camera:** none (fixed full-field view). **Redraw strategy:** full-frame clear-and-draw
every tick (immediate mode); no dirty-rect optimization needed at these entity counts.
**Fonts:** a monospace/vector-style font (e.g. "Hyperspace"/fallback monospace) for HUD,
24 px score, 18 px secondary.

## 9. UI / HUD / Screens
- **Title screen:** centered game name (64 px), "PRESS ENTER TO START" (24 px), high
  score line, a few slow-drifting background asteroids for ambiance.
- **Playing HUD:**
  - **Score:** top-left at `(24, 16)`, left-aligned, e.g. `04250`.
  - **High score:** top-center, smaller.
  - **Lives:** top-left under score at `(24, 48)`, drawn as N small ship-triangle icons.
  - **Wave:** top-right at `(W-24, 16)`, right-aligned, `WAVE 3`.
- **Pause overlay:** dim the field (50% black overlay) + centered `PAUSED`.
- **Game Over screen:** centered `GAME OVER` (48 px), final score, high score,
  `PRESS ENTER TO RESTART`.

### 9.1 Menu & configuration — the shared game shell

Asteroids uses the **generic FS.GG game shell** (FS-GG/FS.GG.Rendering#991) — the same
menu/start screen and settings every FS.GG game shares — rather than a bespoke per-game menu.
The game supplies only its **name**, its **key→command map** (the rebindable actions from §3
Controls), and its play `update`/`view`; the shell provides everything below.

- **Main menu / start screen** — the game's name (**ASTEROIDS**) as the title label, with
  **Start**, **Config**, and **Exit**. The Title-screen `Enter`-to-start of §9 is preserved as
  a shortcut for **Start**.
- **`Esc` from gameplay** opens the pause menu (Resume · Config · Exit to menu) over the same
  shell; `Esc` again resumes.
- **Config / Settings**, all applied live and persisted across restarts:
  - **Screen resolution** and **fullscreen** (windowed / borderless / fullscreen), driven
    through the SkiaViewer window-behavior + `LogicalCanvas` letterbox seam.
  - **Key rebinding** — the player remaps this game's controls (the §3 actions — rotate,
    thrust, fire, hyperspace) via the `Controls.KeyRebind` UI over the `KeyboardInput.Keymap`
    mechanism; bindings persist via `KeymapCodec` (JSON), beside this game's other saved config
    (§13).
  - Game-specific rows are added as extra Config rows over the shell: **Difficulty** (a §12
    tunable preset — Easy `startLives 5 / ufoFireInterval 2.0 / astSpeedScalePerWave 0.04`,
    Normal `3 / 1.2 / 0.06`, Hard `2 / 0.8 / 0.10`), **Master volume**/**Sound** (route to
    `Audio.setMasterVolume`, §10, clamped `[0,1]`, `0.0` = mute), and **CRT glow** (toggles the
    optional vector-glow post-pass, §8 draw order / §15 stretch). The menu, Esc routing, display
    settings, and rebind screen come from the shell.

The shell is pointer- and keyboard-navigable over the interactive Controls host (the
`fs-gg-skiaviewer` "game → pointer host" recipe). It is a shared dependency, so Asteroids does
**not** re-specify menu-stack/cursor/settings machinery of its own. Asteroids has **no
continues** (§11), so the run-end path offers a fresh **New Run** (the arcade "insert-coin"
restart) rather than a Continue. The **Stats & charts** screen (§9.2) is an Asteroids-specific
screen reached as a Config/menu row.

### 9.2 Stats & charts screen
The Stats screen visualizes **the last run** and **lifetime** play. It reads a `Stats`
snapshot (never live physics), so it is a pure, deterministic render reachable from Title,
Game Over, and Pause. Chart-design choices below follow the project dataviz conventions
(form-first, validated colorblind-safe palette, single axis, identity by entity).

**Tracked per run** — `MatchStats`, accumulated in the §7 `Tick` step, snapshotted on the
`Lives = 0 → GameOver` transition (§7 update, step 7):

| Field | Type | Updated |
|-------|------|---------|
| `shotsFired` | `int` | +1 whenever a Fire edge spawns a player bullet (§4.2) |
| `shotsHit` | `int` | +1 on each Player-bullet ↔ asteroid/UFO hit (§4.4) |
| `accuracy` | `float` | derived: `shotsHit / shotsFired` (0 when unfired) |
| `killsLarge` / `killsMedium` / `killsSmall` | `int` | per asteroid destroyed, by size (§4.3) |
| `ufosDestroyed` | `int` | +1 per UFO killed (§4.8) |
| `wavesCleared` | `int` | +1 each wave-clear (§6) |
| `topScoreMultiplier` | `float` | peak `waveScore / parScore` over any wave (efficient small-rock/UFO kills) |
| `survivalSeconds` | `float` | accumulated live-play time (`Time` delta while `Ship.Alive`) |
| `thrustSeconds` | `float` | accumulated seconds Thrust was held (§4.1) |
| `deaths` | `int` | lives lost this run (§4.5) |

**Lifetime** — `LifetimeStats`, persisted (§13, `highscore.json`): `highScore`, `gamesPlayed`,
`bestAccuracy`, `mostWavesCleared`, `longestSurvival`.

**Layout** (logical 1280×720): a KPI tile row across the top, two charts below.

```
┌──────────────────────────── STATS ────────────────────────────┐
│  ┌HIGH SCORE┐ ┌ACCURACY┐ ┌ WAVES  ┐ ┌LONGEST LIFE┐            │  ← KPI stat tiles
│  │  48 250  │ │  37 %  │ │   14   │ │   72.4 s   │            │
│  └──────────┘ └────────┘ └────────┘ └────────────┘            │
│                                                                │
│  Kills by size                      Shots fired vs hit         │
│  ▇▇                             120 ┤         ╭── fired        │
│  ▇▇  ▇▇                             │      ╭─╯                 │
│  ▇▇  ▇▇  ▇▇                         │   ╭─╯╭──── hit           │
│  ▇▇  ▇▇  ▇▇  ▇▇                   0 ┼───────────────► wave #   │
│  Lg  Md  Sm  UFO  (kills)                                      │
└────────────────────────────────────────────────────────────────┘
     ↑/↓ scope:  ▸ This Run · Lifetime            ESC — Back
```

KPI tiles read **HIGH SCORE**, **ACCURACY %**, **WAVES** (cleared), and **LONGEST LIFE**
(the run's `longestSurvival` streak between deaths); each swaps to its lifetime counterpart
under the scope toggle.

**Charts** (rendered in Skia with the same draw-list discipline as §8):

1. **Kills by size** — *form: per-category magnitude → bars.* x = size bucket
   (`Large`, `Medium`, `Small`, `UFO`), y = kill count. **Single series**, so one hue and
   no legend. Bars are 4 px-rounded at the data end with a 2 px surface gap between them.
   Fill `#2a78d6` (light) / `#3987e5` (dark) — validated categorical slot 1. Comparing four
   magnitudes in one hue (not four colors) keeps the eye on relative counts.
2. **Shots fired vs hit** — *form: change over an ordered index → line.* x = wave number,
   y = cumulative shots; **two series** (Fired, Hit) → a legend is present and both lines are
   direct-labeled at their right end ("fired"/"hit"). Fired `#2a78d6`, Hit `#1baf7a`
   (slots 1–2, adjacent-pair CVD-validated). 2 px lines, ≥ 8 px end markers, recessive 1 px
   gridlines in `#3C3C3C`. The widening gap between the two lines is the accumulated misses —
   the visual read of accuracy over the run.

Conventions honored: **color follows the entity** (Fired is always slot 1, Hit always slot 2
— never repainted by the scope toggle); **one axis only** (no dual y-scale); chart **text uses
ink tokens** (`#FFFFFF` primary / `#C3C2B7` muted), never the series hue; layout is **fixed and
deterministic**, so a fixed-seed run (§13) renders byte-identical for snapshot tests. The
`↑/↓` **scope** toggle swaps the data source This-Run ↔ Lifetime without changing colors.

**Model/Msg hooks:** add `Stats: MatchStats` and `Lifetime: LifetimeStats` to the §7 Model,
and a `Stats of scope:StatScope` case to `Phase`. Accumulate `MatchStats` in the §7 `Tick`
step — bump `shotsFired` where Fire spawns a bullet (`KeyDown`), `shotsHit`/`killsLarge|Medium|
Small`/`ufosDestroyed` in the collision pass (step 4), `wavesCleared` at wave-clear (step 5),
`survivalSeconds`/`thrustSeconds` each tick, and `deaths` on ship death (step 7). On the
`GameOver` transition (step 7) fold `MatchStats` into `Lifetime` and persist to `highscore.json`
(§13). `OpenStats`/`CloseStats` switch `Phase` to/from `Stats`; the render is a no-op on physics.

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
| Fire bullet (§4.2) | `Audio.playSfx` | `SoundId "fire"` | short "pew" |
| Asteroid destroyed (§4.3) | `Audio.playSfx` | `SoundId "asteroid-explosion"` | three explosion pitches (low→high) for Large/Med/Small |
| Ship thrust (§4.1) | `Audio.playSfx` | `SoundId "thrust"` | looping low rumble while held |
| Ship destroyed (§4.5) | `Audio.playSfx` | `SoundId "ship-explosion"` | big explosion |
| UFO present (§4.8) | `Audio.playSfx` | `SoundId "ufo-warble"` | looping warble (Large = low, Small = high) |
| UFO destroyed (§4.8) | `Audio.playSfx` | `SoundId "ufo-explosion"` | explosion |
| Extra life (§11) | `Audio.playSfx` | `SoundId "extra-life"` | chime |
| Wave heartbeat | `Audio.playMusic` | `TrackId "heartbeat"` | two-note beat that speeds up as a wave is cleared down |

The classic background "heartbeat" is looping music: request `Audio.playMusic (TrackId "heartbeat") true`
while a wave is in progress, and `Audio.stopMusic` when the field pauses or the game ends. A
mute/settings toggle maps to `Audio.setMasterVolume` (e.g. `Audio.setMasterVolume 0.0` to silence). **Testing:** collect the frame's
`AudioEffect`s, `Audio.interpret` them, and assert the `AudioEvidence.Requested` sequence for
representative events (e.g. firing a bullet requests exactly `PlaySfx (SoundId "fire", _)`).

## 11. Win / Loss / Scoring
- **Scoring:** Large asteroid 20, Medium 50, Small 100; Large UFO 200, Small UFO 1000.
  Only the **player's** bullets (and ship ramming, which gives no points) score.
- **Extra life:** +1 life at every 10,000 points (`NextExtraLifeAt` advances by 10,000).
- **Loss condition:** lives reach 0 → Game Over.
- **Win condition:** none — it is an endless score chase. "Success" = beating the high
  score. High score persists locally (§13).
- **Lives:** start with 3. No continues.

**Scoring detail & the risk gradient:**
- **Points scale inversely with target size — smaller is worth more.** Small 100 > Medium 50
  > Large 20, and Small UFO 1000 > Large UFO 200. The full lineage of one Large is worth
  `20 + 2*50 + 4*100 = 520` (§14 AC #9), of which 77% comes from the four fast, hard-to-hit
  Smalls. The scoreboard therefore pays you for *finishing* rocks, not merely opening them —
  cracking Larges and fleeing the fragments is the low-scoring, low-risk line; hunting every
  Small down is where the points are.
- **Only the killing blow scores, and only if it is a player bullet.** Ramming (§4.4), UFO
  bullets, and asteroid↔asteroid contact (there is none, §4.4) award nothing. A rock that
  drifts off-screen cannot — space wraps (§4.6), so every body must be shot to be scored.
- **Kill credit is per-body, resolved in the collision pass.** Splitting a Large scores the
  Large (20) on the tick it dies; its children score later, individually, when *they* die. You
  cannot "one-shot" a whole lineage for a lump sum.

**Extra-life edge cases:**
- **The threshold is a moving target, checked once per tick.** `NextExtraLifeAt` starts at
  10,000 and advances by `extraLifeEvery` (§12) each award. The check (§7 step 6) fires when
  `Score ≥ NextExtraLifeAt`, grants exactly one life, and advances the threshold — so a single
  tick that crosses the line (e.g. 9,950 → 10,050, §14 AC #15) grants exactly one life, never
  two, even though it overshot.
- **A single big score can bank only one life per tick.** If one collision pass were to vault
  the score past *two* thresholds at once (not reachable with current values — the smallest gap
  is 10,000 and the largest single kill is 1,000), the per-tick check grants one life and leaves
  the surplus for the next tick to catch. The invariant is "at most one extra life per tick",
  which keeps the award deterministic under replay (§13).
- **Extra lives stack without cap.** There is no maximum life count; a skilled endless run can
  accumulate a large reserve, which is the only long-term counter to the asymptotically-fast
  late field (§6).

**Efficiency scoring (feeds §9.2 stats):** a wave's `parScore` is the sum of point values of
every body that *spawned* in that wave (its Larges plus all descendants they would yield if fully
worked). `topScoreMultiplier` tracks the peak `waveScore / parScore` across the run — a value near
1.0 means you finished a wave's rocks (and any UFO) almost completely, rewarding thorough,
small-rock-and-saucer clearing over crack-and-run. It is a stat, not a live score multiplier: it
never changes the points table above.

## 12. Difficulty & Balancing
Data-driven tunables (defaults chosen above):

| Name | Default | Range | Effect |
|------|---------|-------|--------|
| `shipThrustAccel` | 220 px/s² | 120–400 | Acceleration responsiveness |
| `shipRotRate` | 270 deg/s | 150–400 | Turn speed |
| `dragPerTick` | 0.99 | 0.97–0.999 | Glide length (lower = more drag) |
| `shipMaxSpeed` | 600 px/s | 400–900 | Top speed cap |
| `bulletSpeed` | 700 px/s | 500–1000 | Shot velocity |
| `bulletLifetime` | 1.1 s | 0.6–2.0 | Effective range |
| `fireCooldown` | 250 ms | 100–500 | Rate of fire |
| `maxBullets` | 4 | 2–8 | On-screen shot cap |
| `startLives` | 3 | 1–5 | Difficulty floor |
| `waveStartLarge` | 4 | 2–8 | Wave 1 rock count |
| `astSpeedScalePerWave` | 0.06 | 0–0.15 | Per-wave rock speed ramp |
| `hyperBadWarpChance` | 0.12 | 0–0.3 | Hyperspace death risk |
| `ufoSpawnDelay` | 30 s | 5–60 | First UFO appearance |
| `ufoFireInterval` | 1.2 s | 0.5–3.0 | UFO aggression |
| `invulnDuration` | 2.5 s | 0–4 | Respawn grace period |
| `extraLifeEvery` | 10000 | 5000–25000 | Extra-life cadence |

**Difficulty presets** (selected from §9.1 Settings; a preset is just an overlay of the tunables
above, applied at `StartGame`). Everything not listed keeps its default:

| Preset | `startLives` | `ufoFireInterval` | `astSpeedScalePerWave` | Net feel |
|--------|--------------|-------------------|------------------------|----------|
| Easy | 5 | 2.0 s | 0.04 | more grace, lazier saucers, gentler speed ramp |
| Normal | 3 | 1.2 s | 0.06 | the tuning every §14 acceptance test asserts against |
| Hard | 2 | 0.8 s | 0.10 | thin lives, aggressive saucers, speed ceiling reached sooner |

- **Acceptance criteria assume Normal.** All §14 numbers (3 lives, 1.2 s UFO fire, 0.06 ramp) are
  the Normal preset; Easy/Hard change only the three overlaid tunables, so the same code paths and
  tests hold — a difficulty is data, not a branch.
- **The speed ramp cap interacts with the preset.** At Hard's 0.10 ramp the `min(1.6, …)` ceiling
  (§6) is reached at wave 7 instead of wave 11, so Hard hits the maximally-fast field four waves
  earlier; the ceiling itself (1.6×) is shared across presets, so no preset can produce faster-than-
  cap rocks.
- **Lives and extra-life cadence are the two survival dials.** `startLives` sets the floor and
  `extraLifeEvery` sets the refill rate; lowering the former without raising the latter is what
  makes Hard bite. Both are independent of the moment-to-moment physics dials (thrust/rot/drag/
  bullet), which stay constant across presets — the ship *handles* identically on every difficulty,
  keeping muscle memory portable (the "simple" promise of §6).

**Tuning-interaction notes (for the balancer):**
- **`fireCooldown` × `maxBullets` set the true DPS.** Sustained fire is gated by whichever binds
  first: at the 250 ms default you can only reach the 4-bullet cap once early shots begin expiring
  (§4.2), so raising `maxBullets` alone does little until `fireCooldown` also drops. They must move
  together to change effective clear speed.
- **`bulletLifetime` × `bulletSpeed` set range, not just reach.** Range is `speed × lifetime`
  relative to the ship (§4.2); shortening lifetime to curb cross-field sniping is preferable to
  cutting speed, which would also blunt point-blank responsiveness.
- **`dragPerTick` is the master feel dial.** Nudging it toward 0.999 turns the ship into a
  low-friction puck (long, committal glides); toward 0.97 it becomes almost twitchy and stop-able.
  It is the single tunable that most changes how the whole game *feels*, which is why its live range
  is deliberately narrow.
- **`hyperBadWarpChance` prices the panic button.** At 0 hyperspace is a free teleport and
  trivialises survival; the 0.12 default keeps it a genuine gamble (§4.7). Raising it past ~0.3
  makes the button not worth pressing, which is why the range stops there.

## 13. Technical Notes
- **Timestep:** fixed `dt = 1/60 s` simulation, drained by **`FixedStep.drainWith`** — do not
  hand-roll the accumulator. `FixedStep.drainWith (5.0/60.0) (1.0/60.0) frameTime acc` returns
  `struct (steps, acc')`: the whole `1/60` steps this frame owes, and the remainder to bank. The cap
  is the "spiral of death" guard, stated as the frame-time budget the function takes — `5.0/60.0` is
  the 5-steps-per-frame ceiling. (`FixedStep.drain` is the same call at the default 0.25 s cap.)
- **Determinism:** all randomness flows through `Model.Rng` — `FS.GG.Game.Core`'s **`Rng`**
  (splitmix64), seeded at `StartGame` with `Rng.ofSeed`. Given the same seed + same input sequence the
  run is reproducible. It is a **value**, not a `System.Random`: every draw returns `struct (x, rng')` and you write
  `rng'` back to the `Model`, so the `Model` stays a value you can snapshot, replay and compare.
  A `System.Random` in the `Model` is a mutable object *shared* by every copy of it, which
  silently breaks the reproducibility this bullet promises.
- **Performance budget:** worst-case entity count is bounded — max ~11 Large →
  effectively ≤ ~44 small fragments + 4 player bullets + 1 UFO + 1 UFO bullet + ≤200
  particles. Far under any 16.7 ms/frame concern; collision is naive O(n²) circle checks
  (a few thousand comparisons max), no spatial partitioning needed.
- **Persistence:** high score saved to local storage / a small `highscore.json`; loaded
  on Title.
- **Edge cases:** (a) firing while 4 bullets alive → no-op; (b) respawn blocked until
  center clear (could stall — center-clear check prevents instant re-death); (c)
  hyperspace landing on a rock → covered by 0.3 s arrival grace except the 12% bad-warp
  roll; (d) an asteroid and the ship spawning overlapped is prevented by the 150 px
  no-spawn radius; (e) wrap-aware distance must be used in collisions or edge-straddling
  bodies miss hits.

## 14. Acceptance Criteria (test scenarios)
Verifiable Given/When/Then. `dt` steps are `1/60 s` unless noted.

1. **Thrust accelerates along heading.**
   Given a stationary ship at heading 0 (pointing +x) at `(640, 360)`,
   When Thrust is held for 60 ticks (1.0 s),
   Then `Vel.Vx` is positive and `≈ 220 * 1.0` reduced by cumulative drag (within ±15%),
   `Vel.Vy ≈ 0`, and the ship has moved right (`Pos.Vx > 640`).

2. **Drag decays velocity, never reverses it.**
   Given a ship moving at `(300, 0)` px/s with no input,
   When 120 ticks elapse,
   Then `0 < Vel.Vx < 300` (monotonically decreasing) and `Vel.Vy = 0`.

3. **Max speed clamp.**
   Given thrust held continuously for 10 s,
   Then `|Vel|` never exceeds 600 px/s on any tick.

4. **Rotation rate.**
   Given heading 0, When Rotate-right held for 1.0 s,
   Then heading increased by `≈ 270°` (4.712 rad) within ±2°.

5. **Screen wrap (position).**
   Given the ship at `(1279, 360)` with `Vel = (120, 0)`,
   When 1 tick elapses,
   Then `Pos.Vx` is `≈ 1` (wrapped), not `≈ 1281`.

6. **Bullet inherits momentum and expires.**
   Given a ship at heading 0 with `Vel = (100, 0)` that fires,
   Then a Player bullet exists with `Vel.Vx ≈ 800` (700 + 100); And after 1.1 s
   (66 ticks) that bullet no longer exists.

7. **Fire cooldown & cap.**
   Given Fire pressed twice within 250 ms, Then only 1 bullet spawns; And given 4
   bullets already alive, pressing Fire spawns none.

8. **Large asteroid split.**
   Given one Large asteroid and a Player bullet overlapping it,
   When collision resolves,
   Then the Large is removed, exactly 2 Medium asteroids exist at its former position,
   and Score increased by 20.

9. **Full split chain & points.**
   Given a Large asteroid fully destroyed (Large → 2 Med → 4 Small → 0),
   When all fragments are shot,
   Then total Score from that lineage = `20 + 2*50 + 4*100 = 520`.

10. **Small asteroid does not split.**
    Given a Small asteroid hit by a bullet,
    Then it is removed, no children spawn, and Score += 100.

11. **Ship death and life loss.**
    Given a non-invulnerable ship overlapping an asteroid,
    When collision resolves,
    Then Lives decreases by 1, ship enters respawn state, and an explosion is emitted.

12. **Respawn invulnerability.**
    Given the ship respawns at center,
    When an asteroid overlaps it within 2.5 s of respawn,
    Then the ship is NOT destroyed (invulnerable) and renders blinking.

13. **Wave progression.**
    Given Wave 1 with all asteroids and any UFO cleared,
    When the 2.0 s inter-wave timer elapses,
    Then Wave becomes 2 and 5 Large asteroids spawn.

14. **Game over.**
    Given Lives = 1 and the ship is destroyed,
    Then Phase becomes GameOver and final Score is shown; And pressing Enter sets
    Phase = Playing with Lives = 3, Score = 0, Wave = 1.

15. **Extra life.**
    Given Score crosses 10,000 (e.g. 9,950 → 10,050),
    Then Lives increases by 1 exactly once and `NextExtraLifeAt` becomes 20,000.

16. **Hyperspace teleport.**
    Given the ship at `(200, 200)`, When Hyperspace is pressed,
    Then `Vel = (0,0)` and `Pos` differs from `(200, 200)`; And with `hyperBadWarpChance`
    forced to 0 the ship survives, with it forced to 1 the ship is destroyed.

17. **UFO scoring.**
    Given a Small UFO hit by a Player bullet, Then it is removed and Score += 1000;
    Given a Large UFO hit, Score += 200.

18. **UFO bullet kills ship; ignores rocks.**
    Given a UFO bullet overlapping a non-invulnerable ship, Then the ship is destroyed;
    Given a UFO bullet overlapping an asteroid, Then both persist unchanged.

19. **Input edge-trigger (no auto-fire).**
    Given Fire held down for 60 ticks without release,
    Then bullets spawn only on the key-down edge and subsequently no faster than the
    250 ms cooldown (i.e. ≤ 4 bullets across that second, not 60).

20. **Determinism.**
    Given two games started with the same RNG seed and the same recorded input sequence,
    Then asteroid spawn positions/velocities and final Score are identical.

21. **Rotation is decoupled from velocity (flip-and-burn).**
    Given a ship drifting at `(300, 0)` px/s with no thrust,
    When Rotate is held until `Heading` has turned 180°,
    Then `Vel` is unchanged except for that tick's drag (`Vel.Vx` still positive, `Vel.Vy ≈ 0`)
    and only a subsequent Thrust begins reducing the original drift.

22. **Backward shot inherits momentum (a slow "mine").**
    Given a ship at `Vel = (600, 0)` and heading 180° (nose pointing −x) that fires,
    Then the resulting Player bullet has `Vel.Vx ≈ 600 − 700 = −100` px/s (slower and opposite
    the ship), confirming muzzle velocity is the vector sum, not a fixed 700 forward.

23. **Ramming removes a rock without splitting or scoring.**
    Given a non-invulnerable ship overlapping a Large asteroid,
    When collision resolves,
    Then the Large is removed, **no** Medium children spawn, Score is unchanged, and Lives
    decreases by 1.

24. **Invulnerability prevents a second death on the same respawn.**
    Given a ship within its 2.5 s spawn invulnerability overlapping an asteroid every tick,
    When 2.5 s of overlap elapses,
    Then Lives never decreases during the window and the ship remains Alive throughout.

25. **A live UFO holds the wave open.**
    Given a wave with zero asteroids but one UFO still on screen,
    When ticks elapse,
    Then no inter-wave pause starts and Wave does not advance until the UFO is killed or exits.

26. **Rock count and speed ramp both saturate.**
    Given Wave 8 and Wave 9,
    Then each spawns exactly 11 Large asteroids (count cap);
    And given Wave 11 and Wave 20, the asteroid speed scale equals `1.6` on both (speed cap).

27. **Small UFO aims at the ship; Large fires randomly.**
    Given a Small UFO and a stationary ship, over many shots the fired directions cluster within
    ±10° of the wrap-aware bearing to the ship;
    And given a Large UFO, the fired directions are uniformly distributed with no bias toward the
    ship.

## 15. Stretch Goals
1. **Gamepad support** (analog rotate via stick, triggers to fire/thrust).
2. **Mouse aim mode** (rotate ship toward cursor, click to fire).
3. **Powerups** dropped by UFOs: spread-shot, shield, rapid-fire (timed).
4. **Two-player co-op** (second ship, shared wave, separate lives).
5. **Asteroid variety**: rare large "dense" rocks needing 2 hits, or splitting into 3.
6. **CRT/vector post-processing** (glow, scanlines, line bloom) via a Skia shader pass.
7. **Online high-score leaderboard** + replay sharing (leveraging deterministic seeds).
8. **Screen-clear "smart bomb"** as a rare, limited-use panic button.

## 16. Milestone Roadmap

Implementation is sequenced into milestones; each item is a colored checkbox
tracking its status. Items reference the section that specifies them.

**Legend:** 🟥 Not started · 🟨 In progress · 🟩 Done · ⬜ Deferred (post-v1)

_All items start 🟥 (spec status). Flip an item to 🟨 when work begins and 🟩 once
its acceptance test(s) pass (§14)._

### M0 — Scaffold & fixed-step loop
- 🟥 Project scaffold: `Model`/`Msg`/`update`/`view` skeleton (§7)
- 🟥 Fixed 1/60 s tick via `FixedStep.drainWith`, banked remainder (§13)
- 🟥 `Rng` value seeded with `Rng.ofSeed`, threaded through `Model` (§13)
- 🟥 Logical 1280×720 toroidal playfield + collision-safe `Vec2` (§4, §5)
- 🟥 Per-collection value model: total bullet ownership, list-length counts (§5.6)

### M1 — Ship movement & input
- 🟥 Held rotate/thrust + edge-triggered Fire/Hyperspace/Pause/Start keys (§3)
- 🟥 Rotation 270 deg/s + thrust 220 px/s² along nose vector (§4.1) — AC #1, #4
- 🟥 Drag `*0.99`/tick + max-speed clamp 600 px/s (§4.1) — AC #2, #3
- 🟥 Position wrap after integration (§4.6) — AC #5
- 🟥 Decoupled aim: rotation never alters velocity (flip-and-burn) (§4.1) — AC #21
- 🟥 Drag floor (never reaches zero) + clamp preserves heading (§4.1)

### M2 — Firing & bullets
- 🟥 Fire spawns bullet at nose tip, inheriting ship velocity (§4.2) — AC #6
- 🟥 250 ms cooldown + max 4 concurrent bullets (§4.2) — AC #7, #19
- 🟥 Bullet 1.1 s lifetime despawn, 2 px hitbox, edge wrap (§4.2) — AC #6
- 🟥 Muzzle = vector sum `vel + noseVec*700` (forward/back shots) (§4.2) — AC #22
- 🟥 Cooldown and 4-bullet cap as independent silent gates (§4.2) — AC #7

### M3 — Asteroids, waves & world
- 🟥 Three size classes (radius/speed/points/spin) with baked polygons (§4.3, §5.2)
- 🟥 Wave spawn `min(4+(n-1),11)` Large at edges, 150 px no-spawn radius (§6)
- 🟥 Per-wave speed ramp `min(1.6, 1+0.06*(n-1))` (§6)
- 🟥 Wave-clear check + 2.0 s inter-wave pause → next wave (§6) — AC #13
- 🟥 Split children fan apart (mirrored ±[15°,45°]) with redrawn faster speed (§4.3)
- 🟥 Edge-band Large spawn, seeded, re-drawn out of 150 px disc (§6)
- 🟥 Count cap 11 at wave 8, speed cap 1.6× at wave 11 (§6) — AC #26
- 🟥 Wave-clear ignores in-flight bullets; live UFO holds wave open (§6, §4.8) — AC #25

### M4 — Collisions, splitting & scoring
- 🟥 Wrap-aware circle-vs-circle collision resolution (§4.4, §4.6)
- 🟥 Bullet↔asteroid split into 2 children of next size down (§4.3) — AC #8, #9
- 🟥 Small asteroid removed with no children (§4.3) — AC #10
- 🟥 Scoring 20/50/100 per size, player bullets only (§11) — AC #9, #10
- 🟥 Deterministic pass order + one-bullet-one-body resolution (§4.4)
- 🟥 Ramming removes rock, no split, no score (§4.4) — AC #23
- 🟥 Inverse point scale: a Small is worth 77% of a Large's full lineage (§11)

### M5 — Death, respawn, hyperspace & lives
- 🟥 Ship↔hazard death: lose life, freeze 1.5 s, center-clear respawn (§4.5) — AC #11
- 🟥 2.5 s spawn invulnerability with blink render (§4.5) — AC #12
- 🟥 Hyperspace teleport, zero velocity, 12% bad-warp, 1.0 s cooldown (§4.7) — AC #16
- 🟥 Extra life every 10,000 pts, `NextExtraLifeAt` advance (§11) — AC #15
- 🟥 Sequential timers: 1.5 s freeze → center-clear gate → 2.5 s invuln (§4.5)
- 🟥 Invulnerability skips all Ship↔X: no second death on a respawn (§4.4, §4.5) — AC #24
- 🟥 Hyperspace resolution order: place → 0.3 s grace → bad-warp roll, cooldown-gated (§4.7) — AC #16
- 🟥 At most one extra life per tick even on overshoot (§11) — AC #15

### M6 — UFO enemy
- 🟥 UFO spawn timing (30 s or ≤3 rocks), max 1 at a time (§4.8)
- 🟥 Large/Small saucer types, horizontal travel + zig-zag jog (§4.8)
- 🟥 UFO firing every 1.2 s; bullet kills ship, passes through rocks (§4.8, §4.4) — AC #18
- 🟥 UFO scoring 200 / 1000 (§11) — AC #17
- 🟥 Three-condition spawn gate: none present / 20 s window / delay-or-≤3-rocks (§4.8)
- 🟥 Type rolled once at spawn via `min(0.75, score/40000)` (§4.8)
- 🟥 Small aims wrap-aware ±10°; Large fires uniform-random (§4.8) — AC #27
- 🟥 Jog zig-zag {−90,0,+90}, vertical-only wrap, exit-edge despawn (§4.8)

### M7 — Rendering (Skia)
- 🟥 Vector draw order: background, particles, asteroids, UFO, bullets, ship, HUD (§8)
- 🟥 Ship triangle + thrust flame; skip draw on invuln blink-off frames (§8)
- 🟥 Wrap rendering: duplicate bodies straddling edges (§8, §4.6)
- 🟥 Explosion/thrust particle debris, capped at 200 (§5.5, §8)

### M8 — HUD, menus, stats & screens
- 🟥 Title/Playing/Paused/GameOver phases + score/lives/wave HUD (§7, §9)
- 🟥 Adopt the generic FS.GG game shell (FS-GG/FS.GG.Rendering#991): main menu (title + Start/Config/Exit), Esc pause routing, Settings with screen resolution + fullscreen, and in-game key rebinding of the §3 controls, persisted — the game provides its name + key→command map + play update/view; the shell provides the rest, no bespoke menu system (§9.1)
- 🟥 Game-specific Config rows over the shell (difficulty preset, volume/sound, CRT glow) apply live & persist (§9.1, §12, §13)
- 🟥 Game Over → Enter restarts fresh run (lives 3, wave 1, score 0) (§11) — AC #14
- 🟥 Stats screen: `MatchStats`/`LifetimeStats` + kills-by-size & shots-fired-vs-hit charts (§9.2)
- 🟥 Difficulty preset overlay (Easy/Normal/Hard tunables) applied at `StartGame` (§9.1, §12)
- 🟥 `parScore`/`topScoreMultiplier` efficiency stat folded each wave-clear (§9.2, §11)

### M9 — Audio
- 🟥 `AudioEffect` cues via `Audio.playSfx`/`playMusic` (§10)
- 🟥 Heartbeat music loop + `Audio.setMasterVolume` clamp `[0,1]` (§10)
- 🟥 `Audio.interpret` → `AudioEvidence` for deterministic, hardware-free tests (§10)

### M10 — Acceptance & determinism
- 🟥 All 20 acceptance scenarios green (§14)
- 🟥 Seed + input-log replay: identical spawns & final Score (§13) — AC #20

### Stretch — deferred (post-v1)
- ⬜ Gamepad support (analog rotate, triggers to fire/thrust) (§15.1)
- ⬜ Mouse aim mode (rotate toward cursor, click to fire) (§15.2)
- ⬜ UFO-dropped powerups: spread-shot, shield, rapid-fire (§15.3)
- ⬜ Two-player co-op (second ship, shared wave, separate lives) (§15.4)
- ⬜ Asteroid variety: dense 2-hit rocks / 3-way splits (§15.5)
- ⬜ CRT/vector post-processing (glow, scanlines, line bloom) (§15.6)
- ⬜ Online high-score leaderboard + replay sharing (§15.7)
- ⬜ Screen-clear "smart bomb" panic button (§15.8)
