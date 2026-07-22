---
title: "Mini Tanks"
slug: mini-tanks
category: games
complexity: complex
genre: "Top-down Armored Combat / Arcade"
target_session_minutes: 15
stack: { rendering: "FS.GG.Rendering (Skia/OpenGL)", framework: "FS.GG.Game.Core (FixedStep for the tick; Rng for determinism; Geometry OBB/segment casts for hit zones; Ballistics for shell flight; Pathfinding + SpatialGrid for AI routing/broad-phase; Los/Fov/Visibility for perception; Ai for the fog-view + threat maps; Effects for the damage pipeline; Resolution for push-out)", arch: "Elmish/MVU", lang: "F#" }
status: spec
---

# Mini Tanks

## 1. Overview
Two teams of tanks fight over a destructible top-down map. The player fantasy is **armored duelling**:
a tank is **two bodies, not one** — a hull that drives and an independently-rotating turret that
shoots — and that split is the whole game. Where a shell lands on your hull decides whether it
penetrates, so angling your thick front plate at the enemy while your turret tracks a target is the
core skill. What *you* can see is not what your *gun* can reach, and neither is what your *team* can
see, so line-of-sight, field-of-view, and spotting are three different questions you play against at
once. Walls stop shells until they don't, and the wall that falls opens a firing lane nobody planned.
The core verb is **penetrate**: read the bearing, angle the hull, lead the shot, and defeat armor by
geometry rather than by luck. v1 ships **single-player skirmish** against AI teams across three match
rules (Elimination / Capture / Escort), plus a headless **balance harness** that plays every 5×5
matchup in CI (§13).

The whole simulation is a pure `update : Msg -> Model -> Model` over a fixed 60 Hz tick with a seeded
RNG carried in the model. That buys replays, golden snapshot tests, the balance harness, and — if we
ever want it — lockstep multiplayer (§15).

## 2. Core Game Loop
**Moment-to-moment (a duel):** read the enemy bearing → angle the hull so your front plate faces the
threat → traverse the turret onto the target → lead the shot for shell flight time → fire → watch for
*ricochet* / *no-pen* / *penetration* feedback → reverse out of the reload window while the gun cycles.

**Engagement loop:** spot (or lose) an enemy through the fog → maneuver for a flank or a hull-down
angle → trade shots → a module breaks, a track is knocked, or a hull dies → reposition to the next
contact.

**Match loop:** deploy → contest the objective (eliminate the enemy team / hold the capture cell /
escort the convoy) → the match clock or the objective resolves a **Winner** → after-action Stats
screen (§9.2) → rematch or return to title.

**Session loop:** Title → choose map + rules + difficulty → play the match → Victory / Defeat screen →
Stats → restart or title.

```
        ┌──────────────── one engagement ────────────────┐
DEPLOY → │ spot ▸ angle hull ▸ track turret ▸ lead ▸ FIRE │ → module/kill → reposition
        └───────────────▲─────────────────────┬───────────┘
                        └── reload window ─────┘
   objective met OR clock 0 → WINNER → after-action stats → rematch / title
```

**Tempo — the reload window is the beat.** Reloads run 5.5 s (`Cavalier`) to 22 s (`Sabot`), so
between shots a tank is a soft target that cannot answer, and the whole moment-to-moment loop is
organised around it: fire, then spend the `ReloadRemaining` window reversing to cover or re-angling the
hull so the return shot meets your front plate. A `Bastion` that stands still through its 9 s cycle is
trading its one advantage — armor you have to *aim* to defeat — for a free flank. The reverse speeds in
§5.1 (`SpeedRev` 3.5–8 m/s) exist to make that retreat a real, and slow, decision; the widest tempo gap
on the board — the `Sabot`'s 22 s versus the `Lynx`'s 0.35 s burst — is why the scout is the arty's
predator (§5.1) and why an overextended shot is the most punishable mistake in the game.

## 3. Controls & Input
Keyboard drives the hull (held-state, polled each tick); the mouse aims the turret (sampled); firing
and shell selection are edge-triggered. Held keys come from the host's `KeyboardModel.PressedKeys`
(`FS.GG.UI.KeyboardInput`); the sim never reads a device — input arrives as `Order` messages (§7).

| Input | Context | Action | Model |
|---|---|---|---|
| `W` / `S` | In play | Hull throttle forward / reverse | Held |
| `A` / `D` | In play | Hull steer left / right (traverse `HullTraverse`) | Held |
| Mouse move | In play | Aim point — turret slews toward the world cursor at `TurretTraverse` | Sampled |
| Left click | In play | Fire the chambered shell (if loaded and in `TurretArc`) | Edge (down) |
| `1`–`4` | In play | Select shell: AP / APCR / HEAT / HE (chambers on next reload) | Edge |
| `Q` | In play | Pop smoke (dynamic sight occluder, §4.11) | Edge |
| `Space` | In play | Brake / hold position (throttle 0) | Edge |
| `Tab` | In play | Cycle controlled tank (if the player commands a platoon) | Edge |
| `P` / `Esc` | In play | Pause / open pause menu | Edge |
| `Enter` / `Space` | Menus | Activate row | Edge |
| `Esc` | Menus | Back / pop menu stack | Edge |

Mouse aim is **world-space**: the turret's target heading is `atan2` from the turret pivot to the
cursor's world position, and the turret slews toward it clamped to `TurretTraverse` — you point *at*
a spot, the turret takes real time to get there. On a casemate TD (`Adder`, `TurretArc = Some 10°`)
the gun cannot pass its shoulder, so aiming past the arc means turning the hull. Gamepad and
touch are out of scope for v1 (see §15).

## 4. Mechanics (detailed)

### 4.1 Coordinate system, units, and the tick
The world is **metres**, not pixels: a **128 m × 128 m** map over a **64×64 grid of 2 m tiles**
(§14 open decision — tile size 2 m). Continuous entities (tanks, shells) live in
`Geometry.Vec2` metre-space; terrain is a `Cell`-indexed tile grid. The render layer maps metres to
the logical 1280×720 canvas with a camera (§8); the sim never knows about pixels.

The sim advances only in **whole 60 Hz ticks** drained by `FixedStep.drain`/`drainWith` (§13); frame
time never enters `update`. **All durations are integer tick counts** (`uint64`), never
seconds-as-float — a reload finishing is a `ReloadRemaining = 0UL` comparison, not a float
accumulation that drifts across platforms. `dt` inside a step is the constant `1.0/60.0` only where
metre-per-second speeds integrate to a displacement.

### 4.2 Hull and turret are independent bodies (the highest-leverage rule)
A tank stores a **hull heading** `HullHeading` and a **turret heading** `TurretHeading`, each with
its own traverse rate (`HullTraverse`, `TurretTraverse` deg/s). The hull decides where you go **and
which armor plate faces the enemy**; the turret decides where the gun points. Each tick:

- **Hull:** apply held steer, rotating `HullHeading` by up to `HullTraverse·dt`; apply throttle to
  advance along the heading (§4.3).
- **Turret:** slew `TurretHeading` toward the commanded aim heading by up to `TurretTraverse·dt`,
  taking the shortest angular direction; clamp to `TurretArc` around the hull heading for casemates.

Without this split, "different armor per hit zone" is a dice roll on impact bearing — you can't *do*
anything about it. With it, angling the hull while the turret tracks is the core skill, the tank
destroyer's fixed casemate becomes a real trade-off, and armor stats become decisions rather than
numbers.

### 4.3 Driving and terrain
- Speed is `SpeedFwd`/`SpeedRev` m/s, reached through `Accel` (m/s²); no instant velocity.
- Terrain modulates movement through the material table (§6.1): `BlocksMovement` tiles are walls;
  `Rubble` is passable but slow (0.5× speed); `Trees` with `CrushMass = Some 40.0` are driven through
  only by a hull whose `MassTonnes ≥ 40` (§4.13), otherwise they block.
- Tank-vs-tank and tank-vs-wall overlaps resolve with `Resolution.pushOut` along the
  minimum-translation vector (also the fix for rubble appearing under a tank, §6.2).

Edge rules that make driving *feel* like a tracked hull rather than a sprite:

- **Steer and throttle compose; there is no strafe.** A tank only translates along `HullHeading`, so
  repositioning is always turn-then-drive — the tension between *where the hull is going* and *which
  plate it presents* (§4.2) never resolves into a free sidestep. A stationary tank still pivots in place
  at full `HullTraverse` (neutral steer); at speed the same steer *carves* rather than spins.
- **Reverse inverts the steer sign.** Backing into cover tracks the way a driver expects — hold reverse
  and steer-left and the rear swings left — so the reload-window retreat of §2 stays legible under a
  turned turret.
- **Rubble halves both top speed and acceleration** (0.5× §6.1). Clipping a rubble tile mid-charge
  bleeds momentum through `Accel` rather than snapping to half speed, and leaving it re-accelerates the
  same way — so the debris an HE round leaves behind (§4.9) is a soft movement obstacle, not a wall.
- **Crush costs momentum.** Driving through a `CrushMass`-eligible tile (§4.13) sheds ~1 m/s per tile
  crushed (floored at 0), so a `Bastion` ploughing a treeline emerges slower than one that took the
  road — the shortcut is real but not free.

### 4.4 Shells are entities with flight time (not hitscan)
A shell is a simulated body with position and velocity, advanced by `Geometry`/`Ballistics` each tick.
Muzzle velocity is a stat, so leading a moving target is a skill and a fast scout can outrun a slow
heavy's shell at range. `FS.GG.Game.Core.Ballistics` carries the flight model directly:

- **Direct fire** (AP/APCR/HEAT and the autocannon): `Ballistics.step cast dt shell` advances the
  shell and reports the nearest hit, where `cast` is the swept segment test (§5). The broad phase is
  `SpatialGrid.queryRadius` over tank AABBs (rebuilt each tick — free at ≤16 tanks); the narrow phase
  is the five-OBB cast of §4.5. Swept, not point tests: a 1200 m/s APCR round covers 20 m in one tick
  and would tunnel through a 6 m tank — `sweptIntersects` exists precisely for this.
- **Leading a target:** the AI (and an optional aim assist) solve the intercept with
  `Ballistics.intercept shooter speed target targetVelocity`, which returns the aim point that makes a
  shell of that muzzle speed meet a moving target — `None` when the target simply outruns the shell.
- **Terrain march:** a direct shell marches the tile grid with `Los.supercover` between its endpoints,
  stopping at the first tile whose material `BlocksShells`. HEAT **detonates on that first blocking
  tile** (§4.7). This is the same integer traversal the perception layer uses (§4.10) — written once.
- **Indirect fire** (`Sabot` howitzer, `MuzzleMps = 0.0`): the shell is not traced through terrain. It
  is *fired*, and after `flightTicks` it *lands* at the aimpoint and splashes (`Ballistics.splash` with
  `Ballistics.linearFalloff`). It genuinely ignores occluders because it arcs over them — the honest
  model in a game with no third dimension.

### 4.5 Which zone got hit is a geometry question, not a bearing question
A tank is **five convex bodies**: the hull OBB, the turret OBB, and two thin track OBBs flanking the
hull. Build them with `Geometry.obbPolygon centre halfExtents rotation` — hull and tracks at
`HullHeading`, turret at `TurretHeading`. Cast the shell's segment against all five with
`Geometry.segmentPolygonHit` and take the nearest hit. **The edge you entered through *is* the zone:**
a rectangle's four edges are exactly front / left / right / rear, so there is no angle bucketing and no
`if bearing < 45°` magic constant — and the turret's zones fall out of its own independent rotation for
free. `RayHit.Normal` gives the outward normal of that edge, and the impact angle is
`θ = acos(−shellDir · faceNormal)` (0° = perpendicular, dead-on).

> `Geometry.segmentPolygonHit : Point -> Point -> ConvexPolygon -> RayHit option` and the whole
> `Los`/`Fov`/`Visibility`/`Grids`/`Ballistics`/`Ai`/`Effects` stack this spec composes are **already
> shipped** in `FS.GG.Game.Core` (they were the delivery slices of the mini-tanks product design, since
> landed). At `rotation = 0` an `obbPolygon` cast agrees with `segmentAabbHit`, exactly as
> `polygonContact` agrees with `aabbContact` — so the zone test is anchored to a tested convention.

### 4.6 The penetration pipeline
The armor solver is an **ordered pipeline of pure stages** — expressed with
`FS.GG.Game.Core.Effects.pipeline`, whose composable `Stage` values (`amplify`, `resist`, `subtract`,
`floorAt`, `immuneWhen`, `gatedBy`) fold a `Damage` down to a `DamageTrace`. The trace is not just the
number: it records what each stage did, which is exactly the hit-indicator narration the UI needs
(§9). Each stage is separately testable — the point of a pipeline over one 40-line function.

| # | Stage | Rule |
|---|---|---|
| 1 | **Locate** | Nearest of the five OBB casts → `(part, faceNormal, θ, distance)` (§4.5). |
| 2 | **Track absorb** | Hit a track? The track eats the shell — module damage, **zero hull damage** — unless caliber ≥ 2× track armor (overmatch). This is why brawlers aim at tracks. |
| 3 | **Ricochet** | `θ > shell.RicochetDeg` (AP 70°, APCR 75°, HEAT 85°, HE never) **and** `caliber < 2× nominal` → deflect, zero damage. Overmatch forbids ricochet: a big enough shell cannot skip off a thin enough plate. |
| 4 | **Normalization** | `caliber ≥ 3× nominal` → reduce θ toward the normal by a constant. Big gun, thin plate: the shell bites. |
| 5 | **Effective armor** | `effective = nominal / cos θ'`, clamped to `5× nominal` so grazing angles don't produce infinities. |
| 6 | **Pen roll** | `pen' = (pen − falloff × distance) × (1 + u)`, `u ~ U[−0.25, +0.25]` drawn from **the shooter's own `Rng` sub-stream**. |
| 7 | **Resolve** | `pen' > effective` → penetration. Else: HE splashes chip damage, everything else deals zero. |
| 8 | **Damage** | `dmg × (1 + v)`, `v ~ U[−0.25, +0.25]`, then apply the §4.8 module effect for the part hit. |

Properties asserted directly (constitution §VI, §14): `effective ≥ nominal` always · ricochet ⟹ zero
hull damage · penetration is monotone non-decreasing in `pen` and non-increasing in `θ` · the whole
pipeline is total on NaN input, matching the NaN-safety convention every `Geometry` function holds.

### 4.7 Shell types make the armor model legible
Four shells, each defeated by something different — what stops "shoot the thing" from being the only
verb. Loadout is limited (§4.12), so bringing the wrong round is a real cost.

| Shell | Pen | Ricochet past | Distance falloff | Terrain interaction | Beaten by |
|---|---|---|---|---|---|
| **AP** | high | 70° | mild | little | angled armor |
| **APCR** | higher | 75° | steep | little | range |
| **HEAT** | flat, high | 85° | **none** | **detonates on the first blocking tile** | any wall, fence, or bush between you and it |
| **HE** | low | never | none | **heavy destruction** | armor (chips only) |

HEAT detonating on the first blocking tile is the sharpest interaction in the design: it couples the
shell table to the terrain table, so shooting your expensive HEAT round through a wooden fence wastes
it. The player learns that by losing a round to a fence, once.

### 4.8 Modules
Penetrating a zone can hit a **module** — a sub-rect of the part, so it is the same geometry test one
level down. Module effects are the "damage" the §4.6 pipeline applies at stage 8.

| Module | Where | On hit |
|---|---|---|
| Tracks | hull flanks | Immobilized for N ticks; shell absorbed (stage 2) |
| Engine | hull rear | Fire — damage-over-time until it self-extinguishes after N ticks |
| Ammo rack | hull centre | Stacking damage; **third hit detonates → instant kill** |
| Turret ring | turret base | Traverse rate halved until repaired |
| Optics | turret front | `ViewRangeM` −30% |

Rear-engine placement is why flanking a heavy is worth the risk, and it falls out of the geometry
rather than a "flanking bonus" multiplier.

**Module timing (all integer ticks, §4.1).** The "N ticks" above are these constants, and every one
auto-recovers on a timer — v1 has no repair action (§15.3), so the clock is the only cure:

| Module | Effect on hit | Duration | Recovery |
|---|---|---|---|
| Tracks | `Immobilized` set to **300 ticks** (5 s); turret still traverses and fires | 300 ticks | auto; a re-hit **refreshes** to 300, it does not stack |
| Engine | fire drains **0.2 HP/tick** (12 HP/s) | up to **360 ticks** (6 s) | self-extinguishes; a second engine pen re-ignites and refreshes the 360 |
| Ammo rack | +normal damage, plus one stack marker | **permanent for the match** | none — the third pen detonates (§4.8) |
| Turret ring | `TurretTraverse` halved | **480 ticks** (8 s) | auto; refreshes on re-hit |
| Optics | `ViewRangeM` −30% | **600 ticks** (10 s) | auto; refreshes on re-hit |

Refresh-not-stack keeps a focus-fired track lock bounded — you can perma-track a tank only by landing a
hit every 5 s, which is itself a skill check against the reload window (§2) — and the ammo-rack stack
being the one *permanent* state is what makes the third-hit detonation a countdown that hangs over the
whole match rather than a coincidence.

### 4.9 Breakable terrain
Terrain is a grid of tiles, each with hit points and a material (§6). A shell or crush that damages a
tile steps its `TileState` (`Intact → Damaged → Rubble → Open`) and, on every mutation, **bumps
`Terrain.Version`** (§6.2). HE is the demolition round; a Brick building falling is the appeal of
destructible terrain, not the edge case.

**Per-tile HP and what damages it.** Each `Tile` carries `Hp` seeded from its `Material.MaxHp` (§6.1);
a hit that drops `Hp` past a phase threshold steps `TileState` and bumps `Terrain.Version`. Thresholds
are fractions of `MaxHp`: `Intact` above 66 %, `Damaged` 33–66 %, `Rubble` at 0, `Open` once the rubble
is itself cleared. What a hit removes depends on the round, because destruction is a *use* of the shell
table (§4.7), not a separate system:

| Round | Tile damage | Note |
|---|---|---|
| HE | full `Damage` to the tile | the demolition round — one `Sabot` HE (500) levels a 300-HP Brick tile |
| AP / APCR | ~15 % of `Damage` | kinetic rounds punch a hole, they do not demolish |
| HEAT | detonation `Damage` on the **first** blocking tile (§4.7), then stops | the shaped charge spends itself on the wall it hits |
| Crush (§4.13) | `Material.MaxHp` (instant) on a `CrushMass`-eligible tile | the treeline falls under the hull, it is not whittled |

Representative `MaxHp`: Brick **300**, Concrete bunker **3000** (the 10× of §6.1 — roughly six `Sabot`
HE), Sandbags **150**, Bushes **40**, Trees **60**. Tuned so an HE tank opens a firing lane in one or
two shots while a bunker stays cover for the whole match — the destructible-versus-permanent split of
§6.1 is these thresholds, not a boolean.

### 4.10 LOS, field of view, and spotting are three different things
Conflating these is the classic bug. The game needs all three, at three layers, each backed by a Core
module:

| | Question | Core module | Consumer |
|---|---|---|---|
| **LOS** | "Does this segment reach?" | `Los.lineOfSight isTransparent a b` (integer DDA + a `Cell -> bool` mask) | Shell paths, spot checks |
| **FOV** | "Which tiles can this tank see?" | `Fov.fov isTransparent origin radius` (symmetric shadowcasting → `Set<Cell>`) | Fog of war, rendering |
| **Spotting** | "Is *that tank* visible to my team?" | LOS + range + camo roll (§4.10.1) | Targeting, AI perception |

Do **not** build FOV by running LOS to every cell in radius — it is slow and produces asymmetric
vision artifacts. `Fov.fov` is the symmetric answer. The continuous `canShoot` gun-line uses
`Visibility.isVisible muzzle target segments` against the occluder segments the tile grid yields
through `Grids.edgeSegment` (§7.1).

#### 4.10.1 `canSee` and `canShoot` are not the same cast
They differ in **origin** (the commander's optic vs the gun muzzle — different points on a tank whose
turret may be turned 90° from its hull), in **mask** (`BlocksSight` vs `BlocksShells` — see the
bushes/sandbags inversion in §6.1), and in **what counts as reaching** (any of five sample points on
the target hull vs the specific aimpoint). A tank behind sandbags is **visible and unshootable**; a
tank in bushes is **invisible and perfectly shootable**. Both must be expressible, so both are
functions, over different masks of the same grid.

#### 4.10.2 Spotting cycle and the last-known ghost
An enemy is **spotted** when `distance ≤ ViewRangeM` **and** LOS reaches ≥1 of five sample points
(four hull corners + centre) **and** a camo roll passes (`Camo`, harder for a stationary `Adder`).
Evaluated on a **spot cycle** every N ticks (`Ai.due spotCycleTicks tick`), not every tick — it is the
expensive query, and the sub-second delay between exposure and detection is what makes scouting a skill
rather than a radar. On losing a spot, the enemy does not vanish: it becomes a **decaying last-known
ghost** (`Sighting` with `TokenState = Suspected`, §8.1) that fades over `ghostLifetimeTicks`. The
player shoots where the tank *was*; so does the AI (§8) — the same struct.

### 4.11 Smoke (new v1 mechanic)
Popping smoke (`Q`, one of a small per-match charge) spawns a **dynamic occluder**: a disc that sets
`BlocksSight` true over the tiles it covers for ~10 s (600 ticks), decaying at the edges, **without
blocking shells**. It exercises the LOS layer's dynamic-occluder path, is the counter to artillery and
to the `Adder`'s long sightline, and lets a spotted tank break contact. Smoke is *not* concealment you
can shoot from safely — it hides both ways.

### 4.12 Ammo count and shell selection (new v1 mechanic)
Each tank carries a **limited loadout** (`Magazine : Ammo list`, e.g. 20 AP / 8 HEAT / 6 HE for a
medium). Selecting a shell (`1`–`4`) chambers it on the **next reload**, not instantly — switching
ammo mid-fight costs a reload. Firing decrements the chambered kind; running a kind dry disables it.
Without a limited HEAT loadout, §4.7's shell table degenerates to "always bring the best one."

### 4.13 Ramming and terrain crush (new v1 mechanic)
`MassTonnes` is already in the stat vector and `CrushMass` in the material table, so momentum is nearly
free. Tank-tank contact transfers momentum through `Resolution.slide`/`pushOut`, dealing collision
damage scaled by relative mass and closing speed — a 60-tonne `Bastion` at speed shoves a `Lynx` and
hurts it. A hull whose mass meets a tile's `CrushMass` drives through it (the `Bastion` through a
treeline); a lighter tank is stopped.

**The collision numbers.** On contact `Resolution.slide`/`pushOut` separates the bodies and each tank
takes `collisionDmg = rammingCoeff · (mOther / mSelf) · vClose`, clamped to `collisionDmgCap`, where
`vClose` is the closing speed along the contact normal (m/s) and masses are `MassTonnes`. With
`rammingCoeff = 2.0` and `collisionDmgCap = 250`: a 60-tonne `Bastion` closing at 10 m/s into a
12-tonne `Lynx` deals `2·(60/12)·10 = 100` (≈22 % of the scout's 450 HP) and takes `2·(12/60)·10 = 4`
back — ramming is a heavy's weapon precisely because the mass ratio makes the two directions asymmetric.
Below ~2 m/s closing the term rounds to a nudge, so parking against an ally is harmless; a `Bastion`
pinning a tracked (`Immobilized`) light against a wall, though, is a kill in a few taps. Crush
drive-through (§4.9) resolves before the collision term, so ploughing a treeline never registers as a
ram.

## 5. Entities / Game Objects
Positions and velocities use the scaffold's collision-safe `Geometry.Vec2` (`Vx`/`Vy`) so a model
label never collides with `Scene`'s `Point`/`Rect`. Ids are ints; every entity carries its **own `Rng`
sub-stream** (§13) so no draw depends on the iteration order of any other entity.

```fsharp
// Ids and the shell/zone vocabulary. These live in the sim (FS.GG.Game.Core reaches up to nothing).
type EntityId = EntityId of int
type TeamId = TeamId of int

type ShellKind = AP | APCR | HEAT | HE
type FaceHit = FrontFace | LeftFace | RightFace | RearFace | TopFace
type ModulePart = Tracks | Engine | AmmoRack | TurretRing | Optics
```

### 5.1 The vehicle catalog — data, not code
Stats are **data**: the catalog is a record array and there is no `match vehicleClass with` anywhere in
the sim. Balance is a table edit plus a re-run of the §13 harness.

```fsharp
/// Nominal armor thickness in mm, before slope (§4.6). Scalar names, never X/Y/Width/Height.
type FaceArmor = { FrontMm: int; SideMm: int; RearMm: int }

type GunStats =
    { Shell: ShellKind
      PenMm: int                    // nominal penetration at the muzzle
      Damage: int
      MuzzleMps: float              // m/s; 0.0 marks the indirect-fire howitzer (§4.4)
      ReloadTicks: uint64           // integer ticks, never seconds (§4.1, §13)
      RicochetDeg: float }          // incidence past which THIS shell skips (§4.6)

/// The whole vehicle as DATA — no branching on class in the sim (§5, §12).
type VehicleStats =
    { MaxHp: int
      MassTonnes: float
      SpeedFwd: float; SpeedRev: float; Accel: float
      HullTraverse: float           // deg/s
      TurretTraverse: float         // deg/s
      TurretArcDeg: float option    // None = full 360; Some 10.0 = ±10° casemate
      Hull: FaceArmor; Turret: FaceArmor; TopMm: int
      Gun: GunStats
      ViewRangeM: float; Camo: float }   // metres; 0.0–1.0 spot-resistance
```

The five vehicles are deliberately **non-dominated** — no vehicle is weakly better than another on
every axis — and their counters form a **cycle**, not a ladder:

| | **Lynx** (Scout) | **Cavalier** (Medium) | **Bastion** (Heavy) | **Adder** (TD) | **Sabot** (Artillery) |
|---|---|---|---|---|---|
| HP / Mass | 450 / 12 t | 900 / 30 t | 1600 / 60 t | 700 / 25 t | 400 / 20 t |
| Speed fwd / rev | 16 / 8 | 12 / 6 | 7 / 3.5 | 10 / 5 | 8 / 4 m/s |
| Hull traverse | 90 | 55 | 28 | 40 | 30 °/s |
| Turret traverse | 60, 360° | 40, 360° | 22, 360° | **12, ±10° casemate** | 12, 360° |
| Hull armor F/S/R | 25 / 15 / 12 | 90 / 45 / 35 | 200 / 90 / 60 | **160 / 30 / 25** | 20 / 15 / 12 |
| Turret armor F/S/R | 30 / 20 / 15 | 110 / 55 / 40 | 240 / 110 / 70 | — (casemate) | 25 / 15 / 12 |
| Top armor | 15 | 25 | 40 | 25 | 12 mm |
| Gun | autocannon | AP | AP | AP | **HE, indirect** |
| Penetration | 45 | 160 | 220 | **280** | 60 (vs **top**) mm |
| Damage | 45 | 240 | 400 | 480 | 500 + 8 m splash |
| Reload | 0.35 s burst | 5.5 s | 9.0 s | 11.0 s | 22.0 s |
| Muzzle vel | 900 | 950 | 800 | 1200 | arc, 2.5 s flight |
| View / Camo | 90 m / 0.55 | 75 / 0.30 | 60 / 0.10 | 70 / **0.70 stationary** | 45 / 0.20 |

- **Bastion > Cavalier** — 200 mm of angled front eats a 160 mm gun; the medium must flank.
- **Adder > Bastion** — 280 mm pen defeats the heavy frontally; nothing else does.
- **Cavalier, Lynx > Adder** — a ±10°/12 °/s gun cannot track past its shoulder, and 30 mm sides are
  thinner than the scout's autocannon chews through.
- **Sabot > Bastion, Adder** — both are slow or stationary and every roof is thin; artillery answers a
  turtle.
- **Lynx > Sabot** — 16 m/s across the map versus a 22-second reload and 20 mm of hull.
- **Lynx enables Sabot** — arty has 45 m of view and needs an ally's eyes; the scout is both the
  arty's predator and its team's targeting system.

Every arrow is a consequence of the §4.6 armor solver meeting the §4.10 perception stack — the test of
whether the systems are doing work (§13 balance matrix), not a hand-tuned special case.

### 5.2 Tank and Shell
```fsharp
type Vec2 = Geometry.Vec2

/// A magazine entry — a shell type and how many rounds remain (§4.12).
type Ammo = { Kind: ShellKind; Rounds: int }

/// A tank is TWO bodies (§4.2): one hull heading, one turret heading, each slewing at its own rate.
type Tank =
    { Id: EntityId
      Team: TeamId
      Stats: VehicleStats
      Pos: Vec2                     // hull centre, metres
      HullHeading: float            // radians
      TurretHeading: float          // radians — independent of the hull (D3)
      Hp: int
      ReloadRemaining: uint64       // ticks until the chambered gun can fire again
      Loaded: ShellKind             // the chambered round (§4.12)
      Magazine: Ammo list
      Immobilized: uint64           // ticks of track damage remaining (§4.8); 0 = mobile
      Rng: Rng }                    // this tank's OWN sub-stream (§13)

type Shell =
    { Id: EntityId
      Owner: EntityId
      Kind: ShellKind
      Pos: Vec2
      Vel: Vec2                     // m/s
      PenMm: int                    // remaining penetration (spaced armor / falloff eat into it)
      Damage: int
      LifeTicks: uint64 }           // remaining flight budget; artillery lands at 0
```

### 5.3 Terrain and smoke
The hull/turret/track OBBs are **render-time constants** derived at the geometry boundary from
`HullHeading`/`TurretHeading` via `Geometry.obbPolygon` — the model stores headings, not polygons, so
nothing here carries `Width`/`Height` labels. Smoke is a short-lived disc of `Suspected`-vision
occlusion (§4.11) tracked alongside the terrain; it mutates the *sight* mask only.

## 6. World / Levels / Progression
**Map:** 128 m × 128 m over a 64×64 grid of 2 m tiles (§4.1), authored per map. There are **no
discrete levels** — progression is intra-match (spotting, positioning, the objective) and cross-match
via the vehicle roster and difficulty. v1 ships **three hand-made maps** tuned to the 45–90 m view band
so flanking is always possible: *Rubble Row* (dense destructible town), *Two Ridges* (long sightlines,
a scout/arty map), *The Yards* (mixed cover, capture-point).

Each map ships **fixed team spawns** at opposing corners and **per-rule objective anchors** — the
capture cell sits at *The Yards'* centre crossroads, the escort route runs *Two Ridges'* long axis, and
Elimination uses the whole board. Anchors are authored so every rule is winnable on every map, but each
map *favours* a roster: dense *Rubble Row* rewards brawlers and HE demolition, open *Two Ridges* rewards
the scout/arty pair (§5.1). Spawns and anchors are map data — a tile array plus a handful of anchor
cells — so a new map is authored, never coded.

### 6.1 The material table is where the tactics live
```fsharp
type MaterialId = MaterialId of int

type Material =
    { MaxHp: int
      BlocksMovement: bool
      BlocksSight: bool
      BlocksShells: bool
      ShellSoak: int                // pen absorbed passing through — spaced-armor behavior (§4.6)
      CrushMass: float option }     // None = never crushable; Some t = a tank ≥ t tonnes drives through

type TileState = Intact | Damaged | Rubble | Open
type Tile = { Material: MaterialId; Hp: int; Phase: TileState }

/// The grid. `Version` bumps on EVERY mutation so memoized consumers recompute on mismatch (§6.2).
type Terrain =
    { Tiles: Tile[]
      ColsWide: int
      RowsTall: int
      Version: uint64 }
```

| Material | Movement | Sight | Shells | Notes |
|---|---|---|---|---|
| Brick building | ✔ | ✔ | ✔ | The main destructible. Intact → Rubble → Open |
| Concrete bunker | ✔ | ✔ | ✔ | 10× HP; effectively permanent cover |
| Sandbags | ✔ | ✖ | ✔ | **Cover without concealment** — shoot over, hide behind nothing |
| Bushes | ✖ | ✔ | ✖ | **Concealment without cover** — the exact inverse; kills HEAT (§4.7) |
| Trees | ✔ | ✔ | ✖ | `CrushMass = Some 40.0` — Bastion drives through, Lynx does not |
| Water / ditch | ✔ | ✖ | ✖ | Blocks movement only |
| Rubble | ✖ | ✖ | ✖ | Passable, slow; what a building becomes |

The sandbags/bushes pair is the design in miniature: **cover and concealment are different flags**, so
§4.10's "can I see it" and "can I shoot it" queries read *different masks of the same grid* — which is
why they must be different functions.

### 6.2 Terrain mutation is a cache-invalidation problem, and `Version` is the answer
Three systems memoize against terrain — pathfinding (cached routes), FOV (per-team fog), and the AI
threat map — and **all three are wrong the instant a wall falls**, which is the common case, not the
edge case. Every mutation returns a terrain with `Version + 1`; every consumer stores the version it
computed against and recomputes on mismatch. One `uint64` compare is the difference between "the AI
shoots through the hole" and "the AI walks into a wall that isn't there anymore." Rubble appearing
under a tank is separated with `Resolution.pushOut`.

## 7. State Model (Elmish/MVU)

### 7.1 Model, Intent, Msg
The whole simulation is `update : Msg -> Model -> Model` (effects — audio, spawned shells — are values
folded in, never devices). The AI reads the **fog**, not the model (§8); its `TeamView` is a Core
`TeamView` of spotted `Sighting`s and decaying ghosts, and the type forbids it from touching
`Model.Tanks`.

```fsharp
type Objective =
    | Elimination
    | Capture of tile: Cell * ticksHeld: uint64
    | Escort of convoy: EntityId * goal: Cell

type MatchState =
    { Objective: Objective
      Teams: TeamId list
      ClockRemaining: uint64
      Winner: TeamId option }

/// Player or AI intent for one entity. Named `Intent` so it does not shadow the ambiently-opened
/// `FS.GG.Game.Core.Command` vocabulary (§3).
type Intent =
    | DriveHull of throttle: float * steer: float    // -1..1 each, held-state (§3)
    | AimTurret of worldRadians: float               // sampled from the mouse (§3)
    | Fire
    | SelectShell of ShellKind
    | PopSmoke

type Msg =
    | Tick                                            // one fixed 60 Hz step
    | Order of EntityId * Intent                      // player or AI intent

type Model =
    { Tick: uint64                                    // authoritative clock; every duration is a tick count
      Rng: Rng                                        // root stream; per-entity streams split at spawn (§13)
      Terrain: Terrain
      Tanks: Tank list                                // iterated in ascending EntityId, ALWAYS (§13)
      Shells: Shell list
      Match: MatchState }
```

### 7.2 update — the step order
`Tick` runs the sim exactly once (the loop drains whole steps, §13). Within a step, in a fixed order so
the result is deterministic:

1. **Spot cycle** (when `Ai.due spotCycleTicks tick`): recompute each team's `TeamView` — spotted
   enemies (LOS + range + camo, §4.10.2) and decaying ghosts via `Ai.view tick ghostLifetimeTicks`.
2. **AI decide** (per team): for each AI tank in ascending `EntityId`, produce an `Intent` from its
   `TeamView` and its own `Rng` sub-stream (§8). Player tanks take their `Intent` from the frame's
   `Order` messages.
3. **Hull + turret kinematics** (§4.2): rotate headings by clamped traverse; integrate hull motion;
   resolve overlaps with `Resolution.pushOut`.
4. **Firing**: a tank with `ReloadRemaining = 0UL`, a live target intent, and rounds of `Loaded`
   spawns a `Shell` (or an indirect round), decrements the magazine, and resets `ReloadRemaining`.
5. **Ballistics** (§4.4): advance each shell (`Ballistics.step`), resolve the nearest of (terrain tile,
   tank OBB); run the §4.6 pipeline or damage the tile; splash indirect rounds on landing.
6. **Modules & deaths** (§4.8): apply module effects; a third ammo-rack hit or `Hp ≤ 0` removes the
   tank; tick fires, immobilization, and smoke.
7. **Objective & clock** (§9/§11): tick the capture counter / escort progress; decrement
   `ClockRemaining`; set `Match.Winner` when a rule resolves.

### 7.3 view (pure)
`view` maps `Model` (plus the player team's `TeamView`) to a Skia draw list (§8). It performs no
mutation and no timing: it reads tank poses, the fog, shells, terrain tiles, and the HUD state to
decide what to render. Enemy tanks the player's team cannot see are simply absent from the draw list;
ghosts render as `Suspected` tokens (§8.1).

### 7.4 Subscriptions and the real-time skeleton
- **Tick:** the fixed-tick loop is `FS.GG.Game.Core.Loop` over `FixedStep` — `Loop.advance dt integrate
  elapsed` banks a remainder and the renderer lerps `Previous → Current` by `Loop.alpha` at draw time,
  a true fixed-tick sim with smooth 60 fps presentation. This is the `samples/CanvasDemo` skeleton, not
  the hand-rolled timer accumulator of the arcade samples.
- **Input:** held keys from `KeyboardModel.PressedKeys` and the mouse position map to `Order` messages
  each frame (§3).

**MVU at real-time scale.** Rebuilding `Tanks`/`Shells` lists each of 60 ticks is fine at ≤16 tanks and
~64 shells; the lists are small and the record copies are shallow. The one array that is *not* cheap to
copy is `Terrain.Tiles` (§6.2) — copy-on-write chunking is specced but deferred until profiled (§14).

## 8. Rendering (Skia 2D)
Logical 1280×720; the world is metres, mapped by a camera the renderer builds itself (there is no
`Camera` type — build the `PerspectiveTransform` via `Canvas.viewport`). **Layered draw order**
(painter's algorithm), redrawn every frame, entity positions **interpolated** by `Loop.alpha` between
fixed steps:

1. **Terrain** — a loop over visible tiles drawing `Grids.cellRect` fills by material and `TileState`
   (Intact/Damaged/Rubble/Open shade down); destroyed tiles reveal ground.
2. **Fog** — tiles outside the player team's FOV (§4.10) are dimmed; smoke discs (§4.11) draw as
   soft translucent occluders.
3. **Tanks** — hull, turret, and tracks as rotated bodies. Rotation goes through the **only rotation
   path in the org**: `Animation.Transform.toPerspectiveTransform` → `Scene.withPerspective` (there is
   no `Scene.rotate`); `AnimationState.retarget` gives the turret its smooth slew without snap-back.
   Hull polygon at `HullHeading`, turret at `TurretHeading` — the D3 split is visible on screen.
4. **Shells & tracers** — direct rounds as short bright segments along `Vel`; the indirect round shows
   an aim marker and an expanding blast ring on landing. Muzzle flash and recoil are `AnimationState<float>`/`Tween`
   driven off sim state (per-frame `Scene` primitives — there is no particle system, §13).
5. **Hit indicators** — the §9 `DamageTrace` narration: a floating *RICOCHET* / *NO PEN* / *PENETRATION*
   / *AMMO RACK!* tag at the impact, coloured by outcome.
6. **HUD & minimap** (§9) — drawn last, opaque.
7. **Overlays** — pause / victory / defeat scrims and centred text.

### 8.1 Tank symbology (the `Tank → Token` ChannelMap)
Five vehicle kinds, two independent facings, spotted-vs-ghost state, health, armor, speed and a threat
level is precisely the problem `FS.GG.UI.Symbology` exists to solve: a fixed pre-attentive channel set
(`Token`), interchangeable grammars (`Grammar = Token | Badge | Ring`), and a **legibility linter**
(`Legibility.score : Token list -> Report`) that scores a symbol set against a per-channel capacity
table — the same trick §13 plays with balance, one channel-budget check instead of an opinion.

**Where the map lives.** `Symbology` depends on `Scene`, so this map belongs in `FS.GG.Game.Render`,
**never** in the sim that owns `Tank` — `FS.GG.Game.Core` reaches up to nothing (ADR-0022 §2).

**This roster fits the channels almost exactly, and the two frictions are the interesting part.**

- **A tank has TWO headings; `Token` has both.** The design's hardest symbology question — "a `Token`
  has one `Heading`, but D3 says a tank has two" — is answered by the channel set as shipped:
  `Heading` carries the **hull** facing and `SecondaryHeading : float option` carries the **turret**.
  This is the one place `SecondaryHeading` earns its keep — "a turret on a hull" is *exactly* the
  second-rotation channel — and it means the single most important tactical fact on the board (which
  way is the thick plate pointing, versus where is the gun aimed) is drawn by the library, not bolted
  on. A reader who angles their hull sees the token's hull mark swing away from the gun mark.
- **Spotted vs ghost is `State`, not a bespoke overlay.** `TokenState = Confirmed | Suspected` maps
  one-to-one onto §4.10.2's spotted enemy vs decaying last-known ghost — `Confirmed` for a live
  `Sighting`, `Suspected` for a ghost — and the library already renders it on the dashed-stroke
  channel. No custom "is this a ghost" flag.

**`Speed` is pips (0..6, capacity 4), not m/s — passing raw speed is an `Error`.** Quantise `SpeedFwd`
to three ranked tiers; passing metres-per-second raw is an out-of-domain `Error / Speed`, not a
near-miss. `Health` is likewise a **0..1 fraction** — pass `Hp / MaxHp`, never `Hp`.

**`Klass` groups the roster; it does not separate all five.** `Klass` ships `Mobile | Heavy | Scout` —
three levels for five vehicles, so pairs share and the remaining channels (`Threat`, `Health`, `R`,
and the two headings) carry the rest. That is fine for kinds that **play alike**; `Legibility.score`
reports capacity and domain, never whether two units are told apart, so **check separation yourself —
§14 does.** The collision this map must not accept is Scout-vs-Artillery masquerade: the `Lynx` and the
`Sabot` are both light and fast-ish, so `Threat` (read from gun penetration, not a hand ranking) keeps
the harmless-until-it-fires arty distinct from the predator scout.

**What Symbology deliberately does NOT draw here.** The hit-outcome tags (*ricochet* / *no pen* /
*ammo rack*) of layer 5 are transient per-shot events, not identity — there is no status channel in the
fixed set, and `Motion` is budgeted whole-board (more than one non-`Idle` rhythm across the board is a
`Warning`), so it cannot carry per-shell outcomes. Hit indicators stay this spec's own overlay, drawn
on top of the token. Stating the absence is the point: the channel set was considered, and here is why
it stops.

```fsharp
// The Tank → Token ChannelMap. In a product this lives in FS.GG.Game.Render (ADR-0022 §2):
// Symbology depends on Scene, and the sim reaches up to nothing.
type Token = FS.GG.UI.Symbology.Token
type Klass = FS.GG.UI.Symbology.Klass
type Sigil = FS.GG.UI.Symbology.Sigil
type SymFaction = FS.GG.UI.Symbology.Faction
type SymState = FS.GG.UI.Symbology.TokenState
module Sym = FS.GG.UI.Symbology.Symbology

/// Body silhouette. GROUPS the roster (3 Klass levels for 5 vehicles) — Threat/Health/R/headings carry
/// the rest. Casemate TDs and 60-tonne heavies both read Heavy; that is fine, they play alike.
let klassOf (t: Tank) : Klass =
    match t.Stats.TurretArcDeg with
    | Some _ -> Klass.Heavy                          // casemate TD — reads as an armored line-holder
    | None ->
        if t.Stats.MassTonnes >= 50.0 then Klass.Heavy
        elif t.Stats.SpeedFwd >= 14.0 then Klass.Scout
        else Klass.Mobile

/// Sigil marks the CHAMBERED round, so a reader can see what a gun threatens with before it fires.
let sigilOf (t: Tank) : Sigil =
    match t.Loaded with
    | HE | HEAT -> Sigil.Bolt                        // chemical / high-explosive
    | APCR -> Sigil.Fang
    | AP -> Sigil.Ring

/// Confirmed = currently spotted; Suspected = a decaying last-known ghost (§4.10.2, Sighting).
let stateOf (spotted: bool) : SymState =
    if spotted then SymState.Confirmed else SymState.Suspected

/// SpeedFwd (m/s) → 3 ranked pip tiers. `Speed` is an int in 0..6, capacity 4: raw m/s is an Error.
let speedTierOf (t: Tank) : int =
    if t.Stats.SpeedFwd <= 8.0 then 1
    elif t.Stats.SpeedFwd <= 12.0 then 2
    else 3

/// Read the gun's penetration as "how much does this thing threaten", quantised to Threat's 4 levels.
let threatOf (t: Tank) : float =
    if t.Stats.Gun.PenMm >= 260 then 1.0             // Adder
    elif t.Stats.Gun.PenMm >= 150 then 0.75          // Cavalier, Bastion
    elif t.Stats.Gun.PenMm >= 60 then 0.5            // Sabot
    else 0.25                                        // Lynx autocannon

/// Three ranked sizes, not five — `Size`/`R` reads as ordered, and a 1 m radius difference is not a
/// channel a player reads. Mass is the honest rank.
let radiusOf (t: Tank) : float =
    if t.Stats.MassTonnes >= 50.0 then 18.0          // heavy
    elif t.Stats.MassTonnes >= 25.0 then 13.0        // medium
    else 9.0                                         // light

let tokenOf (spotted: bool) (t: Tank) : Token =
    { Sym.defaultToken with
        Cx = t.Pos.Vx
        Cy = t.Pos.Vy
        R = radiusOf t                               // R > 0 or Size is a degenerate Error
        Faction = SymFaction.Enemy                   // ally tanks map to SymFaction.Ally
        Klass = klassOf t
        Sigil = sigilOf t
        State = stateOf spotted                      // Confirmed spotted vs Suspected ghost (§4.10.2)
        Heading = t.HullHeading                      // the HULL faces here — the thick plate (D3)
        SecondaryHeading = Some t.TurretHeading       // the GUN points here — turret-on-a-hull channel
        Health = float t.Hp / float t.Stats.MaxHp    // 0..1 fraction, NOT raw Hp
        Shield = t.Stats.Hull.FrontMm >= 100         // "heavily armored front" flag
        Speed = speedTierOf t
        Threat = threatOf t }
```

**Legibility as a test, not a hope.** The map is pure and the roster is data, so "is this engagement
readable" is an ordinary assertion: `Legibility.score (visibleTanks |> List.map (tokenOf true))` and
check `Verdict = Clean`. See §14. Note `Legibility.Severity.Error` must be written qualified — a bare
`Error` shadows `Result.Error` for every consumer that opens the module.

## 9. UI / HUD / Screens
**Screens:** Title (logo, *Play*, map/rules/difficulty select), Play (world + HUD), Pause (sim frozen),
Victory / Defeat, Stats & Charts (§9.2).

**In-play HUD:**
- **Bottom-left — the controlled tank:** health arc, a **reload meter** (the `ReloadRemaining` tick
  count drawn as a filling bar — an int, not a float), the current shell and per-kind **ammo counts**
  (§4.12), and module status pips (tracks / engine / optics / turret ring) that light red on damage.
- **Centre — hit feedback:** the §8 hit-indicator tags (*RICOCHET* / *NO PEN* / *PENETRATION* /
  *AMMO RACK!*) surfaced from the `DamageTrace` (§4.6) so the armor model is legible — the systems
  only teach if they narrate.
- **Top-right — minimap:** the 128 m map with the player team's fog, spotted enemies as `Confirmed`
  ticks and ghosts as `Suspected` ticks (§8.1), the objective marker, and the match clock.
- **Objective banner:** capture progress / escort distance / enemies remaining, per rule (§11).

### 9.1 Menu & configuration — the shared game shell

Mini Tanks uses the **generic FS.GG game shell** (FS-GG/FS.GG.Rendering#991) — the same
menu/start screen and settings every FS.GG game shares — rather than a bespoke per-game menu.
The game supplies only its **name**, its **key→command map** (the rebindable actions from §3
Controls), and its play `update`/`view`; the shell provides everything below.

- **Main menu / start screen** — the game's name (**MINI TANKS**) as the title label, with
  **Start**, **Config**, and **Exit**. The **Loadout** picker (player vehicle — Lynx · Cavalier ·
  Bastion · Adder · Sabot, §5.1) and the map + rules select sit alongside Start.
- **`Esc` from gameplay** opens the pause menu (Resume · Config · Exit to menu) over the same
  shell; `Esc` again resumes. The **run-end** card (Victory / Defeat per the §11 outcome) offers
  Rematch/Retry and View Stats over the same shell.
- **Config / Settings**, all applied live and persisted across restarts:
  - **Screen resolution** and **fullscreen** (windowed / borderless / fullscreen), driven
    through the SkiaViewer window-behavior + `LogicalCanvas` letterbox seam.
  - **Key rebinding** — the player remaps this game's controls (the §3 actions) via the
    `Controls.KeyRebind` UI over the `KeyboardInput.Keymap` mechanism; bindings persist via
    `KeymapCodec` (JSON), beside this game's other saved config (§13).
  - Game-specific rows are added as extra Config rows over the shell: **Difficulty** (the §12 AI
    knob preset), **Match rules** (the §11 objective), **Master volume**/**Sound** (route to
    `Audio.setMasterVolume`, §10, clamped `[0,1]`), and **Aim assist** (toggles the
    `Ballistics.intercept` lead marker, §4.4). The menu, Esc routing, display settings, and rebind
    screen come from the shell.

The shell is pointer- and keyboard-navigable over the interactive Controls host (the
`fs-gg-skiaviewer` "game → pointer host" recipe). It is a shared dependency, so Mini Tanks does
**not** re-specify menu-stack/cursor/settings machinery of its own. The **Stats & charts** screen
(§9.2) is a Mini Tanks-specific screen reached as a Config/menu row.

### 9.2 Stats & charts screen
The Stats screen visualizes **the last match** and **lifetime** play. It reads a `BattleStats` snapshot
(never live sim), so it is a pure, deterministic render reachable from Title, the run-end menu, and
Pause. Chart-design choices follow the project dataviz conventions (form-first, validated
colorblind-safe categorical palette, single axis, identity by entity).

**Tracked per match** — `BattleStats`, accumulated in the step, snapshotted on `Match.Winner`:

| Field | Type | Updated |
|---|---|---|
| `shotsFired` | `int` | +1 on each `Fire` that spawns a shell (§7.2) |
| `outcomeByKind` | `Map<ShellKind, (pens:int * ricochets:int * nopens:int)>` | folded from the `DamageTrace` (§4.6) |
| `damageDealtByZone` | `Map<FaceHit,float>` | += applied damage per zone entered (§4.5) |
| `damageTaken` | `int` | += HP lost by the player tank |
| `kills` / `deaths` | `int` | +1 on each kill / on player death (§4.8) |
| `timeSpottedTicks` | `uint64` | ticks the player tank was `Confirmed` to the enemy (§4.10.2) |

**Lifetime** — `LifetimeStats`, persisted (§13): `matchesPlayed`, `winRate` (% Victory),
`favoriteVehicle`, `bestPenRate` (penetrations ÷ shots), `longestKillStreak`.

**Layout** (logical 1280×720): a KPI tile row across the top, two charts below.

```
┌──────────────────────────── AFTER ACTION ─────────────────────────┐
│  ┌ KILLS ┐ ┌ PEN %  ┐ ┌ DMG DEALT ┐ ┌ BEST TANK ┐                 │  ← KPI stat tiles
│  │   4   │ │  58 %  │ │   2,140   │ │  Cavalier │                 │
│  └───────┘ └────────┘ └───────────┘ └───────────┘                 │
│                                                                    │
│  Shot outcomes by shell            Damage dealt by hit zone        │
│  ▇▇                          Front ┤▇▇                             │
│  ▇▇  ▇▇                       Side ┤▇▇▇▇▇▇                         │
│  ▇▇  ▇▇  ▇▇                   Rear ┤▇▇▇▇▇▇▇▇▇                     │
│  AP  APCR HEAT HE (pen/ric)   Top ┤▇▇                             │
└────────────────────────────────────────────────────────────────────┘
     ↑/↓ scope:  ▸ This Match · Lifetime            ESC — Back
```

**Charts** (rendered in Skia with the §8 draw-list discipline):

1. **Shot outcomes by shell** — *form: per-category magnitude → grouped bars.* x = shell kind
   (`AP, APCR, HEAT, HE`), y = shots, split **penetration vs ricochet/no-pen** as a two-series stack.
   Penetration `#2a78d6`, deflected `#1baf7a` (slots 1–2, adjacent-pair CVD-validated). It shows which
   round is earning its slot in the loadout (§4.12).
2. **Damage dealt by hit zone** — *form: per-category magnitude → bars.* one bar per `FaceHit`
   (Front/Side/Rear/Top), y = total applied damage. **Single series**, one hue `#2a78d6`, no legend —
   it shows whether the player is flanking (rear/side) or trading front-on. Bars 4 px-rounded at the
   data end, 2 px gaps, recessive 1 px gridlines `#3C3C3C`.

Conventions honored: **color follows the entity** (penetration is always slot 1); **one axis only**;
chart text uses ink tokens (`#FFFFFF` / `#C3C2B7`), never the series hue; layout is fixed and
deterministic, so a fixed-seed match (§13) renders byte-identical for snapshot tests. The `↑/↓` scope
toggle swaps This-Match ↔ Lifetime without changing colors.

**Model/Msg hooks:** add `Stats: BattleStats` and `Lifetime: LifetimeStats` alongside `Match`;
accumulate in the §7.2 step (fold each `DamageTrace` into `outcomeByKind`/`damageDealtByZone`); on
`Match.Winner`, fold `BattleStats` into `Lifetime` and persist (§13). `OpenStats`/`CloseStats` switch a
`Screen = Stats of scope:StatScope` state; the render is a no-op on the sim.

## 10. Audio
Audio ships in v1 via the **`fs-gg-audio`** capability (`open FS.GG.Audio.Core`). Sound is **requested
as pure values**: the step returns `AudioEffect` values alongside the model change and never touches an
audio device. A record-only interpreter (`Audio.interpret`) folds the frame's requests into
`AudioEvidence` — the requested effects in dispatch order, volumes clamped to `[0.0, 1.0]` — so cues
are **deterministic and testable with no sound hardware**. `SoundId`/`TrackId` are opaque names this
game owns; a real backend is deferred, so tests assert on `AudioEvidence.Requested`, not on output.

**Cues** — each is an `AudioEffect` requested from the step when the paired transition fires:

| Event (step transition) | Request | Id | Design intent |
|---|---|---|---|
| Gun fires (§4.4) | `Audio.playSfx` | `sfx-fire-<gun>` | main-gun report / autocannon burst |
| Shell in flight (§4.4) | `Audio.playSfx` | `sfx-shell-whoosh` | supersonic crack passing |
| Ricochet (§4.6 stage 3) | `Audio.playSfx` | `sfx-ricochet` | metallic *spang* — you bounced |
| No penetration (§4.6 stage 7) | `Audio.playSfx` | `sfx-nopen` | dull thud — armor held |
| Penetration (§4.6 stage 7) | `Audio.playSfx` | `sfx-penetrate` | sharp punch-through |
| Track hit (§4.8) | `Audio.playSfx` | `sfx-track-snap` | tread breaks |
| Ammo rack detonation (§4.8) | `Audio.playSfx` | `sfx-ammo-rack` | catastrophic blast (instant kill) |
| Engine fire starts (§4.8) | `Audio.playSfx` | `sfx-fire-start` | whoosh + crackle loop cue |
| Enemy spotted (§4.10.2) | `Audio.playSfx` | `sfx-spotted` | terse detection ping |
| Smoke deployed (§4.11) | `Audio.playSfx` | `sfx-smoke` | hiss of dispensers |
| Tank destroyed (§4.8) | `Audio.playSfx` | `sfx-kill` | explosion + hull settle |
| Objective tick (§11) | `Audio.playSfx` | `sfx-objective` | capture / escort progress chime |
| Enter `Victory` / `Defeat` (§11) | `Audio.playSfx` | `sfx-victory` / `sfx-defeat` | fanfare / sting |
| Match start (§7) | `Audio.playMusic … true` | `music-battle` | tense loop during the match |
| Enter `Victory`/`Defeat` | `Audio.stopMusic` | — | stop the loop for the stinger |

A **mute/settings toggle** maps to `Audio.setMasterVolume` (clamped `[0.0, 1.0]`). **Testing:** collect
the frame's `AudioEffect`s, `Audio.interpret` them, and assert the `AudioEvidence.Requested` sequence
for representative events (e.g. a ricochet requests exactly `PlaySfx (SoundId "sfx-ricochet", _)`).

> **Audio honesty caveat.** The vocabulary is deterministic and an OpenAL backend plays sound, but
> `PlaySfx` today has no loop flag, no voice handle, and no pitch, and **no shipped backend implements
> `IMixingBackend`** — so a computed **pan** (directional gunfire from the left) and a music **duck**
> under the ammo-rack blast never reach hardware. A tank-engine loop and true positional gunfire are
> not fully expressible yet (filed as `Audio#11`); v1 requests the cues and asserts on the evidence,
> and the spatialization lands when the backend does.

## 11. Win / Loss / Scoring
Tanks-vs-tanks with no win condition is a sandbox, so a match always carries one **objective**
(`MatchState.Objective`, §7.1), selectable in Settings (§9.1):

- **Elimination** (default) — destroy every enemy tank. Losing your last tank is the loss.
- **Capture** — hold a cell region; `ticksHeld` counts up while you occupy it uncontested and the
  first team to the threshold wins. Capture forces movement, which is where armor angles get exposed —
  Elimination alone lets the `Adder` sit in a corner forever, and the whole `Sabot` counter exists to
  punish exactly that.
- **Escort** — a convoy tank must reach a goal cell; the defender wins by destroying it or running the
  clock.

**Objective mechanics, in ticks.** Capture uses `captureThresholdTicks = 1200` (20 s): `ticksHeld`
(§7.1) increments only while your team **solely** occupies the cell — an enemy tank in the region
**contests**, freezing both teams' counters, and leaving lets your progress **decay** at half the fill
rate. A capture is a hold, not a touch, and holding it means standing in the open where your armor
angles get read (§5.1) — the whole `Sabot` counter exists to punish exactly that. Escort routes the
convoy tank along a `Pathfinding.flowField` toward its `goal` cell at `convoySpeed = 6 m/s`; the convoy
has its own `escortHp = 1200` and cannot fire, so the attacker wins by *clearing the lane* and the
defender by destroying the convoy or surviving to `ClockRemaining = 0`.

A **match clock** (`ClockRemaining` ticks) bounds every match; on expiry the objective's tiebreak
(most kills, then most damage, then the defender — a draw favours the side denying the objective)
resolves `Match.Winner`, and a still-tied Elimination is a **mutual defeat** (no `Winner`). Every
objective tick requests the `sfx-objective` cue (§10). **Scoring** (after-action, §9.2) is additive:
`+100` per kill, `+40` per penetration, `+objective bonus`, minus `damageTaken/10`; it feeds the
lifetime stats and the balance harness, not a leaderboard in v1.

## 12. Difficulty & Balancing
**Vehicle stats are data** (§5.1); balance is a table edit. Everything below is a named constant or a
config record so tuning is code-free.

**Difficulty is a knob vector, not a stat multiplier.** Never give the AI more penetration — give it
less time and worse hands. The knobs map onto `FS.GG.Game.Core.Difficulty` (`Difficulty.easy` /
`.normal` / `.hard`, `Difficulty.clamp`), plus this game's own:

| Knob | Effect |
|---|---|
| `reactionTicks` | delay between a spot and the first response |
| `dispersionMultiplier` | scales `Ai.aimError` sigma — wider = easier |
| `usesWeakZoneTargeting: bool` | aim at the lower plate / flank vs centre-mass |
| `spotCycleTicks` | how often the AI re-perceives (§4.10.2) |
| `threatWeight` | how much `Ai.threatField` cost the AI values safety over aggression (§8) |

An easy AI is a slow, wide-shooting, centre-mass tank; a hard one reacts in three ticks and aims at
your lower plate. **Both play by the armor rules you do — which means both teach them.**

Concrete presets — the §9.1 Difficulty setting selects a column:

| Knob | Easy | Normal | Hard |
|---|---|---|---|
| `reactionTicks` | 45 (0.75 s) | 18 (0.30 s) | 3 (0.05 s) |
| `dispersionMultiplier` | 2.2 | 1.3 | 0.8 |
| `usesWeakZoneTargeting` | false (centre-mass) | false | true (lower plate / flank) |
| `spotCycleTicks` | 24 | 12 | 6 |
| `threatWeight` | 0.3 (aggressive — walks into fire) | 0.6 | 1.0 (values safety — uses cover) |

Every column obeys the §5.1 armor and §4.10 perception rules unchanged — Hard is *faster hands and
better target selection*, never a pen or armor buff — so beating Hard drills the same reads that beat a
human, which is the whole point of "both teach them."

Data-driven tunables (defaults / range):

| Name | Default | Range | Effect |
|---|---|---|---|
| `tileMeters` | 2.0 | 1–4 | world scale (§4.1) |
| `mapTiles` | 64×64 | 48–96 | play area |
| `spotCycleTicks` | 12 | 4–30 | perception cadence |
| `ghostLifetimeTicks` | 180 | 60–360 | last-known ghost decay (§4.10.2) |
| `smokeTicks` | 600 | 300–900 | smoke duration (§4.11) |
| `matchClockTicks` | 18000 | 6000–36000 | 5-minute default |
| `penJitter` / `dmgJitter` | ±0.25 | 0–0.4 | §4.6 rolls |
| `overmatchTrack` | 2.0 | 1.5–3 | track overmatch multiple |
| `overmatchNoRicochet` | 2.0 | 1.5–3 | ricochet-forbidding caliber multiple |
| `trackDownTicks` | 300 | 120–600 | track immobilization (§4.8) |
| `fireDrainPerTick` | 0.2 | 0.1–0.5 | engine-fire HP/tick (§4.8) |
| `rammingCoeff` | 2.0 | 1–4 | ramming damage scale (§4.13) |
| `collisionDmgCap` | 250 | 100–400 | max collision damage per contact (§4.13) |
| `captureThresholdTicks` | 1200 | 600–2400 | capture hold to win (§11) |
| `convoySpeed` | 6.0 | 3–9 | escort convoy m/s (§11) |
| `escortHp` | 1200 | 600–2000 | escort convoy HP (§11) |

**Balance is a test, not an opinion.** The headless harness plays all **5×5 vehicle matchups × 200
seeds** in CI, produces a win-rate matrix, and asserts every vehicle lands in a **35–65 % overall
band** and that each counter arrow of §5.1 holds. This is only possible because the sim is pure,
headless, and deterministic — the payoff for every constraint in §7, and it is built early, not last
(§15). It can be bound as a **Governance** gate: the repo already dogfoods `FS.GG.Governance`, and the
harness is the same shape as a `test` check (`maturity: block-on-ship`, `tier: focusedTests`), making a
balance regression unmergeable while staying advisory. `Legibility.score` (§8.1) can be bound the same
way, giving the "systems only teach if they narrate" rule a mechanical floor.

## 13. Technical Notes
**Determinism is the test strategy, not a nice property.** The rules, each an assertion (constitution
§VI):

1. Entities are iterated in **ascending `EntityId`**, never in `Map`/`HashSet` order.
2. Every entity carries its **own `Rng` sub-stream**, derived with `Ai.substream root (AgentId n)` (or
   `Rng.split`) at spawn and stored on the entity. No entity draws from the root stream mid-step, which
   would make every roll depend on the iteration order of every other entity.
3. All timers are `uint64` **ticks**. No float accumulators in the step.
4. No wall-clock read below the effect interpreter — `FixedStep.drain` guarantees it.

**Fixed timestep.** `FixedStep.drainWith` does the banking *and* the spiral-of-death cap, so there is
no accumulator loop to get wrong:

```fsharp
// In the Tick handler. drainWith's first argument IS the spiral-of-death cap, expressed as a frame
// -time budget: maxStepsPerFrame * dtFixed. Presentation is real-time; the sim is integer-tick (D1).
let struct (ticks, acc') =
    FixedStep.drainWith (float maxStepsPerFrame * dtFixed) dtFixed realDt acc
let mutable m = model
for _ in 1 .. ticks do
    m <- simStep m
acc <- acc'
```

`FixedStep.defaultMaxFrameTime` (0.25 s) caps the frame internally, so a debugger pause cannot spiral
the sim. The renderer interpolates with the leftover `acc/dtFixed` alpha (`Loop.alpha`).

**Golden replay is the fingerprint.** `(seed, mapId, inputTape) → a per-tick model hash`, snapshotted.
The canonical mechanism is `SceneCodec.packageIdentity (SceneCodec.export scene).CanonicalBytes` — the
same reproducibility mechanism `CanvasDemo` proves — so any accidental nondeterminism (a `Map.iter`, a
stray `DateTime.Now`, a float compare) turns the snapshot red on the tick it first diverges, which is
also the tick that tells you where to look.

**Tunneling guard.** A 1200 m/s round covers 20 m per tick — far more than a 6 m tank — so ballistics
uses **swept** casts (`Geometry.sweptIntersects` broad phase, `segmentPolygonHit` narrow phase), never
point tests (§4.4/§4.5). `Ballistics.step` sub-steps internally where needed.

**Core surface consumed** (all shipped): `FixedStep`, `Loop`, `Rng`, `Geometry` (`obbPolygon`,
`segmentPolygonHit`, `sweptIntersects`, `polygonContact`), `Pathfinding` (`astar`, `distanceField`,
`flowField`), `SpatialGrid`, `Resolution` (`pushOut`, `slide`), `Los`, `Fov`, `Visibility`, `Grids`,
`Ballistics` (`step`, `intercept`, `splash`, `linearFalloff`), `Ai` (`view`, `spotted`, `ghosts`,
`substream`, `aimError`, `threatField`, `fleeField`, `best`, `due`, `Difficulty`), `Effects`
(`pipeline`, the `Stage` combinators, `DamageTrace`, `Policy`). The `Tank → Token` ChannelMap
(§8.1) lives in `FS.GG.Game.Render` because `Symbology` depends on `Scene`; the sim reaches up to
nothing (ADR-0022). Rendering rotation is `Animation.Transform.toPerspectiveTransform` →
`Scene.withPerspective`, the only rotation path in the org.

**Performance budget:** target 60 FPS / 16.7 ms. Worst case ~16 tanks (80 OBBs), ~64 shells, one 64×64
terrain array. Hot loops are ballistics narrow-phase and the spot cycle; `SpatialGrid` buckets both,
and the spot cycle runs every `spotCycleTicks`, not every tick. Copying a 64×64 `Tile[]` per mutation
(16 KB) is fine for a shell or two; copy-on-write **8×8 chunking** is the mitigation for an HE barrage —
do not build it until profiled (§14).

**Persistence:** lifetime stats + best pen-rate stored as local JSON. No mid-match save in v1.

**Edge cases:** a ricocheting shell **despawns** in v1 (continuation is a fun kill and a nasty
determinism surface — §14 open decision); a shell whose target dies before arrival still resolves at
the last-known point (indirect rounds still splash); rubble appearing under a tank separates via
`pushOut`; a turret turned past a casemate arc clamps rather than wrapping; simultaneous track + hull
hit resolves track absorb first (stage 2); pen at the `5× nominal` clamp does not produce infinities.

## 14. Acceptance Criteria (test scenarios)
Verifiable Given/When/Then. Seeds fixed; headings in radians, distances in metres.

1. **Hull and turret are independent.** *Given* a stationary tank at `HullHeading = 0`, *when* the
   player commands `AimTurret (π/2)` and holds it, *then* `TurretHeading` slews toward `π/2` at
   `TurretTraverse` while `HullHeading` stays `0` — the two facings diverge.

2. **Casemate arc clamps the gun.** *Given* an `Adder` (`TurretArcDeg = Some 10°`), *when* an aim is
   commanded 40° off the hull, *then* `TurretHeading` clamps to the hull ±10° and pointing at the
   target requires turning the hull.

3. **Zone is the edge entered, by geometry.** *Given* a shell segment crossing a tank's **left** hull
   edge, *when* `segmentPolygonHit` resolves against the five OBBs, *then* the located part is the left
   hull face (not a bearing bucket), and `RayHit.Normal` is that edge's outward normal.

4. **Ricochet on a steep angle, and zero hull damage.** *Given* an AP round (ricochet 70°) striking a
   plate at `θ = 75°` with caliber `< 2× nominal`, *when* the §4.6 pipeline runs, *then* the trace
   reports ricochet and hull HP is unchanged.

5. **Overmatch forbids ricochet.** *Given* the same 75° impact but caliber `≥ 2× nominal`, *when* the
   pipeline runs, *then* stage 3 is skipped (no ricochet) and the shell proceeds to the pen roll.

6. **Effective armor rises with angle.** *Given* a 100 mm nominal plate, *when* impacted at `θ = 60°`,
   *then* `effective = 100 / cos 60° = 200 mm` (clamped ≤ `5×`), and `effective ≥ nominal` always.

7. **HEAT detonates on the first blocking tile.** *Given* a bush or wall tile between shooter and
   target, *when* a HEAT round is fired through it, *then* it detonates on that tile and never reaches
   the tank (the round is wasted).

8. **Track absorb.** *Given* a shell entering a track OBB with caliber `< 2× track armor`, *when* the
   pipeline runs, *then* the track is damaged (`Immobilized > 0`), the shell is absorbed, and hull HP is
   unchanged.

9. **Ammo rack detonates on the third hit.** *Given* two prior ammo-rack penetrations, *when* a third
   penetrating hit lands on the ammo-rack sub-rect, *then* the tank is destroyed instantly regardless of
   remaining HP.

10. **Spotting has a cycle delay and camo.** *Given* an enemy entering `ViewRangeM` with clear LOS on a
    non-spot tick, *when* the sim runs, *then* it becomes `Confirmed` only on the next
    `Ai.due spotCycleTicks` cycle (not the same tick), and a high-`Camo` stationary `Adder` may fail the
    roll and stay unspotted.

11. **Lost spot leaves a decaying ghost.** *Given* a `Confirmed` enemy that breaks LOS, *when*
    `ghostLifetimeTicks` elapse, *then* it renders as `Suspected` and then disappears; the AI still
    fires at the ghost's last-known point until it decays.

12. **`canSee` ≠ `canShoot`.** *Given* a tank behind **sandbags** (`BlocksSight = false`,
    `BlocksShells = true`), *then* it is visible and the gun-line is blocked; *given* a tank in
    **bushes** (`BlocksSight = true`, `BlocksShells = false`), *then* it is invisible and the gun-line
    reaches — the two casts disagree.

13. **Smoke blocks sight, not shells.** *Given* a smoke disc between two tanks, *when* both queries run,
    *then* LOS/spotting is blocked through the smoke while a shell fired blind still travels through and
    can hit.

14. **Ramming and crush by mass.** *Given* a `Bastion` (60 t) driving into a treeline (`CrushMass =
    Some 40`), *then* it drives through; *given* a `Lynx` (12 t), *then* the trees block it. On tank
    contact, the heavier tank shoves the lighter and deals mass-scaled collision damage.

15. **Terrain `Version` invalidates caches.** *Given* an `astar` route that fails across an intact wall,
    *when* an HE round opens the wall (bumping `Terrain.Version`), *then* the same query now succeeds and
    the previously cached path is recomputed **because** the version changed, not by luck.

16. **Artillery arcs over occluders.** *Given* a `Sabot` firing indirect at an aimpoint behind a wall,
    *when* `flightTicks` elapse, *then* the round lands at the aimpoint and splashes with linear falloff,
    ignoring the wall (it never traced terrain).

17. **The AI reads the fog, not the model.** *Given* an AI tank and a player who breaks LOS and flanks,
    *when* the AI decides, *then* it acts only on its `TeamView` (spotted + ghosts) and can be genuinely
    flanked — it never targets an unspotted tank.

18. **The AI angles its hull.** *Given* an AI `Adder` facing two threats, *when* it decides, *then* it
    rotates its hull so the front plate bisects the bearing to the top-two threats (a real armor problem
    for the player), costing one `atan2` and a clamp.

19. **Ammo selection costs a reload.** *Given* AP chambered and `SelectShell HEAT`, *when* the tank
    fires, *then* the chambered kind changes to HEAT only after a full reload elapses (not instantly),
    and firing a kind to zero disables it.

20. **Fixed-step determinism (golden replay).** *Given* a fixed seed, map, and input tape, *when* the
    match is replayed twice, *then* tank positions, every `DamageTrace`, and `Match.Winner` are
    bit-identical (`SceneCodec` per-tick hashes match), with no real-time drift.

21. **Balance band holds.** *Given* the headless 5×5 × 200-seed harness, *when* it runs in CI, *then*
    every vehicle's overall win-rate is within 35–65 % and each §5.1 counter arrow holds.

22. **Board legibility is `Clean`.** *Given* a typical engagement's visible tanks mapped through
    `tokenOf`, *when* `Legibility.score` runs, *then* `Verdict = Clean`, and a hand check confirms the
    `Lynx`/`Sabot` pair is separated by `Threat` (§8.1).

## 15. Stretch Goals
Ranked, out of scope for v1:
1. **Elevation & hull-down** — a coarse height layer (0/1/2) so a tank can park behind a rise with only
   its thick turret front exposed. The deepest positional mechanic in the genre; deferred because it
   touches LOS, FOV, pathfinding, and rendering at once. Design the perception signatures so a height
   layer slots in without breaking them.
2. **Crew** — commander / gunner / driver as damageable slots behind the §4.8 modules.
3. **Repair & consumables** — fix tracks, extinguish an engine fire; pairs with §4.8 modules.
4. **Ricochet continuation** — a deflected shell keeps flying on the reflected vector (a fun emergent
   kill; a nasty determinism surface — §13).
5. **Platoon command** — issue orders to AI teammates; the `Tab`-cycle hull becomes a squad.
6. **Procedural maps** — `Rng` is already in the model; cheap to add, adds nothing until the systems are
   tuned on hand-made maps.
7. **Lockstep multiplayer** — the deterministic fixed-tick core makes it *possible* at roughly the cost
   of an input-delay buffer. Do not build it now; just don't break determinism.
8. **Directional/positional audio** — lands when a backend implements `IMixingBackend` (§10 caveat,
   `Audio#11`): the pan and duck the cues already request start reaching hardware.

## 16. Milestone Roadmap

Implementation is sequenced into milestones; each item is a colored checkbox
tracking its status. Items reference the section that specifies them.

**Legend:** 🟥 Not started · 🟨 In progress · 🟩 Done · ⬜ Deferred (post-v1)

_All items start 🟥 (spec status). Flip an item to 🟨 when work begins and 🟩 once
its acceptance test(s) pass (§14)._

### M0 — Scaffold & fixed-step loop
- 🟥 Project scaffold: `Model`/`Msg`/`update`/`view` skeleton, sim as `Msg -> Model -> Model` (§7)
- 🟥 Fixed 60 Hz tick via `FixedStep.drainWith` + `Loop.advance`/`alpha`, banked remainder + spiral cap (§7.4, §13)
- 🟥 `Rng` root + per-entity sub-streams (`Ai.substream`/`Rng.split`), threaded through `Model` (§13)
- 🟥 Metre world on a 64×64 / 2 m grid; camera maps metres→canvas (§4.1, §8)

### M1 — Hull, turret & driving
- 🟥 Independent `HullHeading`/`TurretHeading` slew at own traverse rates (§4.2) — AC #1
- 🟥 Casemate `TurretArcDeg` clamp around the hull (§4.2) — AC #2
- 🟥 Throttle/steer accel-based driving; `Resolution.pushOut` on overlap (§4.3)
- 🟥 Movement modifiers: rubble 0.5× speed/accel, no strafe, reverse-steer inversion, crush momentum cost (§4.3)
- 🟥 Held-key hull + sampled mouse turret → `Order` intents (§3)

### M2 — Ballistics & hit zones
- 🟥 Shells as entities; swept advance via `Ballistics.step` + `sweptIntersects` broad phase (§4.4)
- 🟥 Five-OBB build (`obbPolygon`) + `segmentPolygonHit` nearest-hit zone location (§4.5) — AC #3
- 🟥 Terrain march (`Los.supercover`) stopping at first `BlocksShells` tile (§4.4)
- 🟥 Indirect artillery: fire → land at aimpoint → `Ballistics.splash` linear falloff (§4.4) — AC #16
- 🟥 Lead solver `Ballistics.intercept` for AI + aim assist (§4.4)

### M3 — Armor & damage pipeline
- 🟥 `Effects.pipeline` of stages: locate/track/ricochet/normalize/effective/pen/resolve/damage (§4.6)
- 🟥 Ricochet + overmatch-forbids-ricochet + `effective ≥ nominal` invariants (§4.6) — AC #4, #5, #6
- 🟥 Track absorb (zero hull damage) (§4.6, §4.8) — AC #8
- 🟥 Four shell types AP/APCR/HEAT/HE; HEAT detonates on first blocking tile (§4.7) — AC #7
- 🟥 Modules: tracks/engine/ammo-rack/turret-ring/optics; ammo-rack 3rd hit kills (§4.8) — AC #9
- 🟥 Module timers: track auto-recover, engine-fire DOT self-extinguish, ring/optics timed recovery, refresh-not-stack (§4.8)
- 🟥 `DamageTrace` narration surfaced to the UI (§4.6, §9)

### M4 — Perception: LOS, FOV & spotting
- 🟥 `Los.lineOfSight` gun/spot casts over `BlocksShells`/`BlocksSight` masks (§4.10) — AC #12
- 🟥 `Fov.fov` symmetric shadowcasting per-team fog (§4.10)
- 🟥 Spot cycle (`Ai.due`) + camo roll; `canSee` ≠ `canShoot` origins (§4.10) — AC #10
- 🟥 Last-known ghost via `Ai.view`/`Sighting`, `Confirmed`→`Suspected` decay (§4.10.2) — AC #11
- 🟥 Smoke dynamic sight occluder, not shell-blocking (§4.11) — AC #13

### M5 — Terrain, ramming & new v1 mechanics
- 🟥 Material table + `Tile`/`TileState` destruction Intact→Rubble→Open (§6.1, §4.9)
- 🟥 Per-tile `Hp`/phase thresholds + shell/HE/crush tile-damage model, representative `MaxHp` (§4.9)
- 🟥 `Terrain.Version` bump on mutation; consumers recompute on mismatch (§6.2) — AC #15
- 🟥 Ammo count + shell selection chambers on next reload (§4.12) — AC #19
- 🟥 Ramming momentum + `CrushMass` drive-through by mass (§4.13) — AC #14
- 🟥 Collision damage by mass ratio × closing speed, clamped to `collisionDmgCap` (§4.13)

### M6 — AI (reads the fog)
- 🟥 `TeamView` decide loop, ascending `EntityId`, own sub-stream (§7.2, §8) — AC #17
- 🟥 Threat/flee maps (`Ai.threatField`/`fleeField`) + `Pathfinding.astar` positioning (§8)
- 🟥 Aim layer: `intercept` + `Ai.aimError`; weak-zone vs centre-mass targeting (§8, §12)
- 🟥 Hull-angling layer bisects top-two threats (§8) — AC #18
- 🟥 Difficulty knob vector over `Difficulty` presets (§12)
- 🟥 Concrete Easy/Normal/Hard knob columns (reaction/dispersion/weak-zone/spot-cycle/threat) (§12)

### M7 — Match layer, win/loss & scoring
- 🟥 `MatchState` objectives: Elimination / Capture / Escort + match clock (§7.1, §11)
- 🟥 Capture contest/decay hold to threshold; escort convoy `Pathfinding.flowField` auto-path + convoy HP (§11)
- 🟥 Winner resolution + tiebreak (kills→damage→defender, tied Elimination = mutual defeat); additive after-action scoring (§11)

### M8 — Rendering & tank symbology
- 🟥 Layered draw order: terrain, fog/smoke, tanks, shells, hit tags, HUD, overlays (§8)
- 🟥 Hull/turret rotation via `Animation.Transform.toPerspectiveTransform` → `Scene.withPerspective` (§8)
- 🟥 `Tank → Token` ChannelMap in `FS.GG.Game.Render` (§8.1)
- 🟥 Two headings: `Heading` = hull, `SecondaryHeading` = turret (§8.1)
- 🟥 `State` = `Confirmed`/`Suspected` for spotted/ghost; `Speed` pips, `Health` fraction (§8.1)
- 🟥 Hit-outcome tags as the spec's own overlay — not a Symbology channel (§8, §8.1)
- 🟥 `Legibility.score` asserts `Verdict = Clean` per engagement (§8.1) — AC #22

### M9 — UI, menus & stats
- 🟥 In-play HUD: health arc, reload meter (tick int), ammo counts, module pips (§9)
- 🟥 Objective banner: capture progress / escort distance / enemies remaining per rule (§9, §11)
- 🟥 Minimap with fog, `Confirmed`/`Suspected` ticks, objective + clock (§9)
- 🟥 Adopt the generic FS.GG game shell (FS-GG/FS.GG.Rendering#991): main menu (title + Start/Config/Exit), Esc pause routing, Settings with screen resolution + fullscreen, and in-game key rebinding of the §3 controls, persisted — the game provides its name + key→command map + play update/view; the shell provides the rest, no bespoke menu system (§9.1)
- 🟥 Loadout screen: pick the player vehicle from the roster, alongside Start (§5.1, §9.1)
- 🟥 Screen state machine: Title → Play → Victory/Defeat → Stats → restart/title (§2, §9.1)
- 🟥 Game-specific Config rows over the shell (difficulty/rules/volume/aim assist) apply live + persist (§9.1, §13)
- 🟥 `BattleStats`/`LifetimeStats` accumulation + fold/persist (§9.2)
- 🟥 Stats screen: KPI tiles, shot-outcome + damage-by-zone charts (§9.2)

### M10 — Audio
- 🟥 `AudioEffect` cues per event, `Audio.interpret` → `AudioEvidence` (§10)
- 🟥 Battle music loop + `stopMusic` on win/defeat; `setMasterVolume` clamp `[0,1]` (§10)
- 🟥 Honest caveat: request pan/duck cues; assert on evidence pending `IMixingBackend` (§10)

### M11 — Determinism, balance & acceptance
- 🟥 Golden replay: `SceneCodec` per-tick hash, seed+tape reproducible (§13) — AC #20
- 🟥 Headless 5×5 × 200-seed balance harness, 35–65 % band + counter arrows (§12) — AC #21
- 🟥 Optional Governance gate binding the balance + legibility harnesses (§12)
- 🟥 All 22 acceptance scenarios green (§14)

### Stretch — deferred (post-v1)
- ⬜ Elevation & hull-down (height layer across LOS/FOV/pathfinding/render) (§15.1)
- ⬜ Crew as damageable slots behind modules (§15.2)
- ⬜ Repair & consumables (fix tracks, extinguish fire) (§15.3)
- ⬜ Ricochet continuation on the reflected vector (§15.4)
- ⬜ Platoon command over AI teammates (§15.5)
- ⬜ Procedural maps seeded from `Rng` (§15.6)
- ⬜ Lockstep multiplayer over the deterministic core (§15.7)
- ⬜ Directional/positional audio once `IMixingBackend` ships (§15.8)
