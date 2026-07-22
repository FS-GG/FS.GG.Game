---
title: "Bulwark: Tower Defense"
slug: tower-defense
category: games
complexity: complex
genre: "Tower Defense / Strategy"
target_session_minutes: 25
stack: { rendering: "FS.GG.Rendering (Skia/OpenGL)", framework: "FS.GG.Game.Core (FixedStep for the tick; Rng for determinism; Pathfinding for the creep route; SpatialGrid for targeting)", arch: "Elmish/MVU", lang: "F#" }
status: spec
---

# Bulwark: Tower Defense

## 1. Overview
You are the warden of a contested mountain pass. Endless columns of marauders march from a breach
in the cliffs toward the heart of your keep; if enough of them reach it, the keep falls. Between
waves you spend hard-won gold to **build, upgrade, and sell towers** along (and around) the path,
shaping a gauntlet of fire, frost, and lightning. The core verb is **placement-as-puzzle**:
each tower is a fixed-cost commitment whose value depends on where you put it, what it can reach,
and which threats are coming next. The fun comes from the escalating arithmetic of waves —
reading the incoming composition (fast/armored/flying/boss), committing scarce gold under interest
pressure, and watching a well-laid kill-zone shred a wave you nearly lost to. A run is a tight,
legible economic-tactics loop that rewards forethought and punishes greedy over-extension.

## 2. Core Game Loop
**Moment-to-moment (Build phase):** survey board → read next wave preview → select tower type →
validate placement (hover ghost) → place / upgrade / sell → repeat until gold is committed → press
*Start Wave*.

**Moment-to-moment (Combat phase):** towers auto-acquire targets per their targeting mode → fire
projectiles → projectiles fly and apply damage/effects → enemies die (award gold) or leak (cost a
life) → wave drains → return to Build phase with interest paid out.

**Session-level:** Title → Map select → Wave 1 build → run all N waves (build/combat alternating,
or continuous "endless" mode) → either **Victory** (survive final boss wave) or **Game Over**
(lives reach 0) → score screen → restart / new map.

```
       ┌──────────── Build phase ───────────┐        ┌─── Combat phase ───┐
START → │ place/upgrade/sell · read preview │ → WAVE │ towers fire · enemies│ → wave clear → (interest)
       └─────────────────▲───────────────────┘        └─────────┬───────────┘            │
                         └──────────────── next wave ◄──────────┴── lives>0 ──────────────┘
                                                    lives==0 → GAME OVER
                                          all waves cleared → VICTORY
```

## 3. Controls & Input
Keyboard-primary with mouse for placement (the genre is mouse-native). Input model noted per row:
edge-triggered = fires once on key-down; held = sampled each frame.

| Input | Action | Model |
|---|---|---|
| Mouse move | Move cursor / update placement ghost & range circle | sampled |
| Left click | Place selected tower / select existing tower / confirm | edge (down) |
| Right click | Cancel current placement / deselect | edge (down) |
| Mouse wheel | Cycle tower type in build palette | edge per detent |
| `1` `2` `3` `4` | Select tower type: Arrow / Cannon / Frost / Tesla | edge |
| `U` | Upgrade selected tower (next tier on active branch) | edge |
| `B` / `V` | Choose upgrade **branch** A / B (when tier 2→3 forks) | edge |
| `S` | Sell selected tower (refund 70%) | edge |
| `T` | Cycle targeting mode of selected tower (First→Last→Strongest→Closest→First) | edge |
| `Space` | Start next wave (Build→Combat) / call wave early | edge |
| `F` | Toggle fast-forward (1×↔2×) — scales sim dt only | edge (toggle) |
| `P` / `Esc` | Pause / open pause menu | edge |
| `Esc` (in placement) | Equivalent to right-click cancel | edge |
| `G` | Toggle build grid overlay | edge (toggle) |
| Mouse hover (≥400 ms) | Tooltip for tower/enemy under cursor | sampled |

Notes: placement is committed on left-click only when the ghost is **valid** (Section 4.7);
an invalid click is a no-op with a short error blip. Calling a wave early (`Space` during Combat,
before the previous wave fully drains, only in continuous mode) awards a time bonus (Section 11).

## 4. Mechanics (detailed)

### 4.1 Board, Grid & Path
The playfield is **1280×720 logical px**. The play area is a **40×22 tile grid** of **32×32 px**
tiles, occupying the region `x∈[0,1280), y∈[0,704)`; the bottom 16 px strip plus the right-side
**260 px build panel** overlay the grid (panel covers tile columns ≥ 32 on UI maps; on full maps the
panel floats translucently — see Section 9). Tile coordinate `(col,row)` maps to pixel center
`(col*32+16, row*32+16)`.

Each tile has a `TileKind`:
- `Buildable` — green-tinted ground; towers may be placed.
- `Path` — the lane enemies walk; **never buildable**.
- `Blocked` — rock/water/decoration; never buildable, blocks pathfinding.
- `Spawn` — single entry tile (enemies appear here).
- `Goal` — single exit/keep tile (a leak here costs a life).

Two map families are supported and selected per map via a flag `mazeBuilding: bool`:

1. **Fixed-path maps (`mazeBuilding=false`).** The path is an authored polyline of **waypoints**
   `Waypoint[]` (pixel coordinates) from Spawn to Goal. Enemies follow it; towers cannot block it.
   Pathfinding is trivial (follow the list). This is the default for the v1 shipped maps.
2. **Maze maps (`mazeBuilding=true`).** Spawn and Goal are open; the path is **computed** over the
   grid (4-neighbour, unit cost) treating `Buildable` placed-with-tower tiles as temporarily
   `Blocked`. Do not hand-roll the search — `FS.GG.Game.Core` ships it:

   - **One creep, one route:** `Pathfinding.astar Neighbourhood.FourWay maxVisited isWalkable spawn goal`
     returns `Cell list option` — `None` is precisely the "sealed" case §4.7 rejects on.
   - **Many creeps, one Goal — what this game actually is:** do **not** run one A\* per creep. Compute
     `Pathfinding.distanceField Neighbourhood.FourWay maxVisited cost [ goal ]` **once** (a cost-aware
     flood fill *from the Goal*), then `Pathfinding.flowField Neighbourhood.FourWay field` turns it into
     a `Map<Cell, Cell>` — every tile's next hop toward the Goal. All 120 enemies read that one map; a
     creep spawned mid-wave needs no search at all. One field, not N searches.

   Players build towers to *shape* the route. Placement is rejected if it would **fully seal** Spawn
   from Goal (Section 4.7). The path is **recomputed** whenever a tower
   is placed or sold, and **in-flight enemies re-target** to the nearest node on the new path
   (Section 4.3).

### 4.2 Time, Tick & Phases
The game runs at a **fixed simulation timestep of 60 Hz (dt = 1/60 s ≈ 0.0167 s)** with an
accumulator (Section 13). All speeds below are in **logical px/s**; all rates in **per-second**.
Phases: `Building` (sim paused for enemies/projectiles; UI live), `Combat` (full sim),
`Paused`, `WaveCleared` (brief 1.5 s interstitial), `Victory`, `GameOver`.
Fast-forward multiplies the number of fixed steps consumed per real frame by 2 (it does **not**
change dt — determinism preserved).

### 4.3 Enemy Movement & Pathfinding
Enemies are points with a movement radius (visual only). Each enemy stores `pathIndex` (the
waypoint it's heading toward) and `pos`. Per fixed step:
```
target   = waypoints[pathIndex]
toTarget = target - pos
dist     = |toTarget|
step     = effectiveSpeed * dt           // effectiveSpeed includes slow multipliers
if step >= dist:
    pos = target; pathIndex += 1         // snap & advance; if last → leak (Section 4.6)
else:
    pos = pos + (toTarget/dist) * step
```
`effectiveSpeed = baseSpeed * slowFactor` where `slowFactor∈(0,1]` is the strongest active slow
(slows do **not** stack multiplicatively; the **strongest** wins, see 4.5). Flying enemies ignore
the grid/path geometry on maze maps and travel a **straight line Spawn→Goal** (their own 2-waypoint
list); on fixed maps they follow the same polyline as ground units but are only hittable by towers
flagged `canHitAir`.

**Maze re-path:** on recompute, each ground enemy finds the path node nearest its current `pos`
(min Euclidean distance to any waypoint segment) and sets `pathIndex` to that node's successor, so it
rejoins the new route without teleporting. If no path exists (should be impossible — placement is
pre-validated), the last valid path is retained.

### 4.4 Tower Targeting & Firing
Each tower has `range` (px radius from tower center), `fireRate` (shots/s), a `cooldown` timer, a
`targetingMode`, and a `target: EnemyId option`. Per fixed step:
1. Decrement `cooldown` by dt.
2. **Acquire/validate target:** gather enemies within `range` that the tower can hit
   (`canHitAir` gate for flyers). If current target left range/died, drop it. If no target, pick one
   per `targetingMode` (Section 4.8).
3. If `cooldown ≤ 0` and a valid target exists: **fire** (spawn projectile or apply hitscan),
   reset `cooldown = 1/fireRate`, emit muzzle SFX/particle.

Tower aim/rotation is cosmetic except for the visual turret angle (`atan2` to target). Towers never
move and never collide with anything.

### 4.5 Damage, Armor, Resistances & Status Effects
**Base damage application:** `applied = max(1, damage - armor)` for *physical* sources; *elemental*
sources (frost/poison/tesla) ignore armor but are modified by per-enemy **resistance** multipliers
(`resist ∈ [0,1]`, applied as `applied = damage * (1 - resist)`), then floored at 1 unless `resist
== 1.0` (full immunity → 0).

Damage **types**: `Physical` (Arrow, Cannon), `Frost` (Frost — applies Slow), `Poison` (an upgrade —
applies DoT), `Electric` (Tesla — chains). A projectile carries `(amount, type, effectPayload)`.

**Status effects** (stored as a list on each enemy; each has a remaining timer in seconds):
- **Slow** `{ factor: 0.0–1.0, dur }` — multiplies speed. Strongest (lowest factor) active slow
  applies; refreshes duration if a stronger or equal slow is reapplied. e.g. Frost L1 = `factor
  0.6, dur 1.5 s`.
- **Poison/DoT** `{ dps, dur }` — deals `dps*dt` Poison damage each step; multiple stacks **do**
  add (cap 3 stacks). Bypasses armor.
- **Stun** `{ dur }` — `effectiveSpeed = 0` while active (Tesla L3 branch). Strongest = "any active".
- **Vulnerable** `{ bonus, dur }` — incoming damage ×(1+bonus) (Cannon L3-B "Demolition").

Effects tick before movement each step; expired effects are removed.

**Damage resolution pipeline.** A single hit resolves in a **fixed order** so stacked modifiers
compose identically every time (and every random draw is pulled in fire order — Section 13). For a
**physical** hit of base damage `d` from tower `t` on enemy `e`:
1. **Crit** — if `t` carries a crit trait (chance `c`, multiplier `x`; Sniper `c=0.15, x=2`), draw
   one `Rng.nextFloat`; on `< c`, `d ← d*x`. Exactly one draw per shot, whether or not it crits.
2. **Vs-armored** — if `t` has a "+P% vs armored" trait (Breaker +50%) **and** `e.Armor > 0`,
   `d ← d*(1+P)`.
3. **Armor** — effective armor `a = e.Armor * (1 - pierce)`, where `pierce = 0.5` for an armor-ignore
   trait (Marksman) and `0` otherwise; `d ← d - a`.
4. **Vulnerable** — if `e` carries `Vulnerable(bonus,_)`, `d ← d*(1+bonus)`.
5. **Floor** — `applied = max(1, d)`.

For an **elemental** hit (Frost/Poison/Electric), steps 1–3 are replaced by the resistance multiplier
above (`d ← d*(1 - resist)`); steps 4 (Vulnerable) and 5 (floor at 1) still apply, except a full
immunity (`resist == 1.0`) yields exactly 0 and skips the floor. Vulnerable therefore amplifies
**every** damage type, including Poison DoT ticks (`dps*dt`), which pass through steps 4–5 each step.

**Branch traits vs. stat replacements.** Section 5.1 tabulates only the numeric stats that change per
tier, and those are wholesale replacements. A **named trait** first granted at a T3 fork — crit
(Sniper), armor-ignore (Marksman line), Vulnerable-on-hit (Demolition/Breaker), pierce (Railgun),
poison-spread-on-death (Pandemic) — **persists to T4 on the same branch** unless the T4 row supersedes
it with its own trait. So Marksman keeps Sniper's crit *and* adds armor-ignore; Breaker replaces
Demolition's Vulnerable with its stronger `+40%/3s` rather than stacking a second copy.

**Healing (Healer aura).** In Tick step 3 (before movement — Section 7), every alive `Healer` heals
each **other** alive ally whose center lies within `r=60 px` by `6*dt` hp (its tabled 6 hp/s), clamped
so no enemy exceeds `MaxHp`; a Healer never heals itself. Overlapping Healers **add** (two auras =
12 hp/s to a unit in both). Healing and Poison DoT net out in the same step, so a `12 dps` Pandemic
stack out-damages a single Healer while a `6 dps` frost-burn only cancels it. Healers are ordinary
targets and killing them is the counter — which is why Section 8.1 ranks them `Threat 0.75`.

**On-death effects** resolve **after** bounty is awarded (Section 4.9) and **before** the enemy is
removed: the **Wyrm** spawns **2 Wisp** at its position (each entering the flying straight-line route
of Section 4.3; the spawn itself pays no bounty — the Wisps pay their own 9 when later killed); a
**Pandemic**-poisoned enemy (Frost T4-B) dying with an active Poison stack **spreads** that stack to
every enemy within `r=40 px` (one copy each, still subject to `dotStackCap`, Section 12); a
Juggernaut or Wyrm kill additionally requests `sfx-boss-death` (Section 10). Swarmling's "pack of 8"
is purely a scheduler `SpawnGroup` with `Count=8` (Section 6), not an on-death spawn.

### 4.6 Lives & Leaks
The player starts with **20 lives**. When an enemy reaches the Goal it "leaks": lives -= enemy's
`leakCost` (1 for normal, 2 for "heavy", boss = remaining-lives-or-10 capped at current lives → see
table). The enemy is removed (no bounty). If `lives ≤ 0` → **GameOver** immediately (current wave
abandoned).

### 4.7 Tower Placement Validation
A placement at tile `(c,r)` for tower `t` (cost `k`) is **valid** iff ALL hold:
1. `(c,r)` is in-grid and `TileKind = Buildable`.
2. No existing tower occupies `(c,r)` (towers are 1×1 tile; "large" towers reserve a 2×2 footprint —
   all four tiles must be Buildable & empty).
3. `gold ≥ k`.
4. The tile is not under the build panel / a HUD rect.
5. **(maze maps only)** After tentatively blocking the footprint, a Spawn→Goal route still exists
   **and** every currently-alive ground enemy still has one. This is the same primitive as §4.4, asked
   a yes/no question: `Pathfinding.bfs Neighbourhood.FourWay maxVisited isWalkable spawn goal` returns
   `None` exactly when the placement seals the map (unit cost, so BFS is the cheap form of it). If you
   recompute the `distanceField` for the flow field anyway, the check is free — a creep's tile simply
   is not in the field. If sealed → reject ("would seal path").
6. Not within the **no-build margin** of the Spawn tile (2-tile radius) — prevents spawn camping
   abuse on maze maps.

On valid left-click: deduct `gold`, create tower at `cooldown=0`, (maze) recompute path, play
build SFX. On invalid: no-op + error blip + brief red ghost flash.

### 4.8 Targeting Modes
Given the set `C` of in-range, hittable enemies, pick:
- **First** (default): max `progress` (distance traveled along path = `pathIndex` major key, then
  `1 - distToNextWaypoint` minor) — i.e. closest to Goal.
- **Last**: min `progress` (newest/furthest from Goal).
- **Strongest**: max current `hp` (tie → First).
- **Closest**: min Euclidean distance to tower (tie → First).
Targeting is re-evaluated only when the tower has no valid target or its target exits range/dies
(sticky targeting — avoids jitter). Cycled live with `T`.

**Sniper affinity.** The Sniper branch's "Strongest-target affinity" (Section 5.1, Arrow T3-A) means
the tower's `targetingMode` is **defaulted to `Strongest` at the moment of the upgrade** — a slow,
high-damage single shot wants the biggest target — but the player may still cycle it with `T`;
affinity is a smart default, not a lock. Every other tower **keeps its current mode** across upgrades
and sells-and-rebuilds start fresh at `First`.

### 4.9 Economy
- **Starting gold:** 250 (Easy 350 / Hard 180 — Section 12).
- **Bounty:** each enemy awards `bounty` gold on death (table Section 5.2). Leaks award nothing.
- **Interest:** at the end of each Build phase when `Start Wave` is pressed, *before* combat, the
  player earns **interest = floor(gold * 0.08)** capped at **+40/wave** (rewards banking without
  making turtling dominant). Interest is disabled in the first build phase (wave 1).
- **Sell refund:** 70% of total gold invested in that tower (base + upgrades), floored.
- **Upgrade cost:** per tier in the tower table; gold deducted on `U`.
- **Wave-clear bonus:** +`(10 + waveNumber)` gold when a wave is fully cleared with no remaining
  enemies (encourages full clears).

### 4.10 Projectiles & Flight
Two firing models:
- **Projectile** (Arrow, Cannon): spawns a moving entity with `pos`, `vel` (toward target's
  *current* position at fire time — no homing for Cannon; Arrow re-homes 1×/0.1 s toward target),
  `speed` px/s, `damage`, `type`, `effectPayload`, optional `splashRadius`. On reaching target (within
  `arriveEps = 6 px`) or target death: resolve hit. Cannon shells **splash**: all enemies within
  `splashRadius` of impact take damage (center 100%, linear falloff to 50% at edge).
- **Hitscan** (Tesla, Frost-beam upgrade): instantaneous; draws a beam for 0.08 s; applies damage &
  effect immediately. Tesla **chains**: after the primary hit, jumps to up to `chainCount` nearest
  un-hit enemies within `chainRange`, each jump dealing `damage * chainFalloff^k` (k = jump index).

Projectile lifetime cap = 2.0 s (despawn if target lost and not arrived). Max ~400 live projectiles.

**Chain target selection (Tesla).** Each jump picks the **nearest not-yet-hit** hittable enemy within
`chainRange` (Section 12, default 90) of the **previous link** — not of the tower — never revisiting an
enemy already struck this discharge, and honoring `canHitAir`. Jump `k` deals `damage * chainFalloff^k`
(with `k=1` for the first jump off the primary). A link that lands on a resistant enemy (Wisp, Electric
.6) **still counts as a used jump**: the un-hit set and the falloff exponent both advance, so a resistant
body "soaks" a link. The whole discharge stops early the moment no un-hit enemy is in range, even if
`chainCount` is not exhausted — a lone enemy takes only the primary hit.

**Pierce (Railgun, Tesla T4-B).** Railgun trades chaining for a **piercing beam**: its hitscan hits
**every** enemy whose center lies within its radius of the tower→target line segment, each taking the
full `damage` (no falloff), and rolls its `0.7 s` Stun **independently per enemy**. `chainCount` and
falloff are nominal on this branch — the line, not the jump graph, defines who is hit.

## 5. Entities / Game Objects

### 5.1 Towers (build/upgrade)
Base stats (Tier 1). `RoF` = shots/s. Range/splash in px. Damage pre-armor.

| Tower | Key | Cost | Dmg | Range | RoF | Projectile | DmgType | Special | Air? | Footprint |
|---|---|---|---|---|---|---|---|---|---|---|
| **Arrow** | 1 | 70 | 8 | 130 | 1.8 | fast arrow (homing) | Physical | cheap, fast | yes | 1×1 |
| **Cannon** | 2 | 120 | 30 | 110 | 0.6 | lobbed shell | Physical | **splash** r=42 | no | 1×1 |
| **Frost** | 3 | 100 | 5 | 120 | 1.2 | frost bolt | Frost | **slow** f=0.6/1.5s | yes | 1×1 |
| **Tesla** | 4 | 160 | 18 | 140 | 0.9 | hitscan beam | Electric | **chain** ×3, falloff .6 | yes | 1×1 |

**Upgrade trees** (each tower: T1→T2 linear, then T2→T3 forks into branch **A**/**B**, then →T4 on
the chosen branch). Costs are upgrade-only (not cumulative shown). Stats are *replacements*.

**Arrow**
| Tier | Cost | Dmg | Range | RoF | Notes |
|---|---|---|---|---|---|
| T1 | (70) | 8 | 130 | 1.8 | — |
| T2 | 60 | 12 | 145 | 2.2 | Sharper Heads |
| **T3-A Sniper** | 130 | 40 | 230 | 1.0 | +crit 15% ×2; Strongest-target affinity |
| **T3-B Volley** | 130 | 9 | 150 | 4.5 | fires 2 arrows; great vs swarms |
| T4-A Marksman | 240 | 90 | 270 | 1.1 | ignores 50% armor |
| T4-B Storm | 240 | 11 | 160 | 6.0 | fires 3 arrows |

**Cannon**
| Tier | Cost | Dmg | Range | RoF | Splash | Notes |
|---|---|---|---|---|---|---|
| T1 | (120) | 30 | 110 | 0.6 | 42 | — |
| T2 | 110 | 45 | 120 | 0.7 | 48 | Bigger Shells |
| **T3-A Mortar** | 220 | 70 | 200 | 0.45 | 70 | long range, huge splash |
| **T3-B Demolition** | 220 | 60 | 120 | 0.8 | 52 | applies **Vulnerable** +25%/2s |
| T4-A Siege | 360 | 130 | 230 | 0.45 | 85 | — |
| T4-B Breaker | 360 | 85 | 125 | 0.9 | 55 | Vulnerable +40%/3s; +50% vs armored |

**Frost**
| Tier | Cost | Dmg | Range | RoF | Slow | Notes |
|---|---|---|---|---|---|---|
| T1 | (100) | 5 | 120 | 1.2 | f0.6/1.5s | — |
| T2 | 90 | 7 | 130 | 1.3 | f0.5/1.8s | Deeper Chill |
| **T3-A Glacier** | 180 | 9 | 140 | 1.3 | f0.35/2.2s | strongest slow in game |
| **T3-B Frostbite** | 180 | 8 | 135 | 1.4 | f0.5/1.8s | adds **Poison** 6 dps/2s (frost-burn) |
| T4-A Absolute Zero | 300 | 12 | 150 | 1.4 | f0.25/2.5s | 8% chance 0.6s **Stun** |
| T4-B Pandemic | 300 | 10 | 140 | 1.6 | f0.5/2s | Poison 12 dps/3s, spreads on death r=40 |

**Tesla**
| Tier | Cost | Dmg | Range | RoF | Chain | Notes |
|---|---|---|---|---|---|---|
| T1 | (160) | 18 | 140 | 0.9 | ×3 f.6 | — |
| T2 | 140 | 26 | 150 | 1.0 | ×3 f.65 | Higher Voltage |
| **T3-A Arc** | 260 | 34 | 165 | 1.1 | ×5 f.7 | mass chain |
| **T3-B Overload** | 260 | 60 | 150 | 1.2 | ×2 f.6 | 12% chance 0.5s **Stun** |
| T4-A Tempest | 420 | 48 | 180 | 1.3 | ×7 f.75 | — |
| T4-B Railgun | 420 | 120 | 160 | 1.2 | ×2 f.6 | 20% **Stun** 0.7s; pierces |

F# sketch:
```fsharp
open FS.GG.Game.Core
// Positions/velocities live in the scaffold's collision-safe Geometry.Vec2 ({ Vx; Vy }, from
// src/<ProductDir>/Vec2.fs) — NEVER a record you label X/Y/Width/Height, which collide with
// Scene's Point/Rect. This is a type ABBREVIATION: it adds no labels, so nothing can collide.
type Vec2 = Geometry.Vec2

type TowerKind = Arrow | Cannon | Frost | Tesla
type Branch = NoBranch | A | B
type TargetingMode = First | Last | Strongest | Closest

type Tower = {
    Id: TowerId
    Kind: TowerKind
    Tile: int * int
    Pos: Vec2                 // pixel center
    Tier: int                 // 1..4
    Branch: Branch
    Range: float
    Damage: float
    FireRate: float           // shots/s
    Cooldown: float           // seconds until next shot
    DamageType: DamageType
    Special: TowerSpecial     // Splash/Slow/Chain/Poison/Vulnerable/None
    CanHitAir: bool
    Targeting: TargetingMode
    Target: EnemyId option
    Angle: float              // turret render angle
    GoldInvested: int }       // for sell refund
```

### 5.2 Enemies
`HP`, `Speed` px/s, `Armor` (flat, physical only), `Resist` map, `Fly`, `Bounty`, `LeakCost`.

| Enemy | HP | Speed | Armor | Resists | Fly | Bounty | Leak | Notes |
|---|---|---|---|---|---|---|---|---|
| **Grunt** | 40 | 55 | 0 | — | no | 4 | 1 | baseline swarm |
| **Runner** | 25 | 110 | 0 | — | no | 5 | 1 | fast, fragile |
| **Brute** | 160 | 40 | 6 | — | no | 10 | 2 | armored tank |
| **Shielded** | 120 | 50 | 12 | Physical .0 / Frost .5 | no | 12 | 1 | heavy armor, frost-resist |
| **Wisp** | 60 | 70 | 0 | Electric .6 | **yes** | 9 | 1 | flying, shrugs Tesla |
| **Spectre** | 90 | 65 | 0 | Frost 1.0 (immune) | no | 11 | 1 | cannot be slowed |
| **Healer** | 110 | 48 | 2 | — | no | 14 | 1 | heals nearby allies 6 hp/s r=60 |
| **Swarmling** | 12 | 90 | 0 | — | no | 1 | 1 | spawns in packs of 8 |
| **Juggernaut (mini-boss)** | 900 | 35 | 10 | Frost .5 / Poison .5 | no | 60 | 4 | wave 10/20 |
| **Wyrm (boss)** | 4000 | 30 | 15 | Frost .5/Elec .3/Poison .25 | no | 200 | 10 | final wave; spawns 2 Wisp on death |

F# sketch:
```fsharp
type EnemyKind = Grunt | Runner | Brute | Shielded | Wisp | Spectre | Healer | Swarmling | Juggernaut | Wyrm
type StatusEffect =
    | Slow of factor:float * remaining:float
    | Poison of dps:float * remaining:float
    | Stun of remaining:float
    | Vulnerable of bonus:float * remaining:float

type Enemy = {
    Id: EnemyId
    Kind: EnemyKind
    Hp: float
    MaxHp: float
    BaseSpeed: float
    Armor: float
    Resists: Map<DamageType, float>
    Fly: bool
    Bounty: int
    LeakCost: int
    Pos: Vec2
    PathIndex: int
    Effects: StatusEffect list }
```

### 5.3 Projectiles
```fsharp
type Projectile = {
    Id: ProjId
    Source: TowerId
    Pos: Vec2
    Vel: Vec2
    Speed: float
    Damage: float
    DamageType: DamageType
    Target: EnemyId option     // for homing/aim
    SplashRadius: float        // 0 = single target
    Effect: StatusEffect option
    Homing: bool
    Life: float }              // seconds remaining (cap 2.0)
```
Created when a tower fires; destroyed on arrival/hit, on splash resolution, on target loss + life
expiry, or off-board. Hitscan (Tesla/Frost-beam) does not create a `Projectile` — it resolves
instantly and pushes a transient `BeamFx` to the effects layer.

## 6. World / Levels / Progression
**Playfield:** 1280×720 logical px (Section 4.1). Default ships **3 maps**: *Serpentine* (fixed
S-curve path, beginner), *Crossroads* (fixed two-merge path, medium), *The Labyrinth*
(`mazeBuilding=true`, expert).

**Wave structure:** a campaign is **20 waves**; victory = survive wave 20 (the Wyrm). Each wave is a
spawn timeline (Section 4.2 / scheduler below). Difficulty ramp:
- Waves 1–4: Grunts/Runners, small counts (8–14). Teaching economy.
- Waves 5–9: introduce Brute (w5), Wisp/flying (w6), Shielded (w7), Healer (w8), Swarmling packs (w9).
- **Wave 10:** Juggernaut mini-boss + escort.
- Waves 11–14: mixed armored+flying, larger counts, Spectre (w12).
- Waves 15–19: dense mixed waves, two-front (split spawn timing), faster cadence.
- **Wave 20:** Wyrm boss + continuous trickle of Wisps.

**Escalation knobs per wave** `w`: base count `≈ 6 + 1.4*w`; enemy HP global multiplier
`hpScale(w) = 1 + 0.06*(w-1)` applied on top of table HP; spawn interval shrinks
`max(0.35, 0.9 - 0.02*w)` s. Boss waves override with hand-authored timelines.

**Wave timeline / scheduler.** A wave is a list of timed spawn groups:
```fsharp
type SpawnGroup = { At: float; Kind: EnemyKind; Count: int; Interval: float }  // At = seconds from wave start
type Wave = { Number: int; Groups: SpawnGroup list; Reward: int }
```
Example **Wave 7** (introduces Shielded):
```
At 0.0  Grunt    ×10  every 0.6
At 4.0  Runner   ×6   every 0.4
At 8.0  Shielded ×4   every 1.2
At 12.0 Brute    ×3   every 1.5
```
Scheduler keeps `waveClock` (seconds since wave start) and a per-group emit cursor; each fixed step,
for every group, while `waveClock ≥ At + emitted*Interval` and `emitted < Count`, spawn one enemy at
the Spawn tile and `emitted += 1`. A wave is **complete** when all groups exhausted **and** no enemies
remain alive on board → transition `WaveCleared` (1.5 s) → `Building` (next wave) → pay interest &
wave-clear bonus.

**Two-front waves (15–19).** With a single Spawn tile, "two-front" pressure is authored as
**overlapping `SpawnGroup`s with staggered `At`**, not a second spawn point: a dense early front (short
`Interval`) starts taxing the front-line kill-zone while a slower second group of a different kind
begins a few seconds later, so two zones are stressed at once instead of one uniform stream. This
needs no new machinery — the "front" is an emergent property of overlapping group windows the
Section 6 scheduler already runs. Skeleton for a Wave 16:
```
At 0.0  Runner   ×12  every 0.30   // fast front — punishes thin early cover
At 2.5  Brute    ×5   every 1.20   // slow armored front overlaps the Runners' tail
At 9.0  Wisp     ×6   every 0.60   // air layer arrives while ground towers are committed
```
Boss waves (10, 20) instead lean on a single overwhelming target plus a trickle escort, so their
hand-authored timelines override the count/interval knobs entirely.

## 7. State Model (Elmish/MVU)

### Model
A single layered record; sub-records keep update cases focused.
```fsharp
type Phase = Building | Combat | WaveCleared of timer:float | Paused of prev:Phase | Victory | GameOver

type BoardState = {
    Grid: TileKind[,]            // 40×22
    MazeBuilding: bool
    Waypoints: Vec2[]            // active path (fixed: authored; maze: computed)
    SpawnTile: int*int
    GoalTile: int*int }

type EconomyState = {
    Gold: int
    Lives: int
    Score: int
    InterestRate: float          // 0.08
    InterestCap: int }           // 40

type WaveState = {
    Index: int                   // 0-based into Waves
    Waves: Wave[]
    Clock: float                 // seconds since wave start
    Emitted: int[]               // per-group emit count
    AllSpawned: bool }

type Selection =
    | NoSelection
    | PlacingTower of TowerKind            // ghost follows cursor
    | TowerSelected of TowerId

type Model = {
    Phase: Phase
    Board: BoardState
    Towers: Map<TowerId, Tower>
    Enemies: Map<EnemyId, Enemy>
    Projectiles: Map<ProjId, Projectile>
    Effects: TransientFx list              // beams, explosions (render-only, ticked)
    Wave: WaveState
    Econ: EconomyState
    Selection: Selection
    Cursor: Vec2
    HoverTile: (int*int) option
    Difficulty: Difficulty
    FastForward: bool
    Rng: Rng                               // FS.GG.Game.Core; seeded (Section 13)
    NextId: int
    Stats: RunStats }                      // kills, leaks, dmg, for score screen
```

### Msg
```fsharp
type Msg =
    // input
    | MouseMoved of Vec2
    | LeftClick of Vec2
    | RightClick
    | SelectKind of TowerKind
    | UpgradeSelected
    | ChooseBranch of Branch
    | SellSelected
    | CycleTargeting
    | StartWave
    | ToggleFastForward
    | ToggleGrid
    | TogglePause
    | RestartRun | NewMap of MapId
    // sim
    | Tick of dt:float           // one or more fixed steps; see subscriptions
```

### update (important cases)
- **`SelectKind k`** → `Selection = PlacingTower k` (only meaningful; placement happens on click).
- **`LeftClick p`** → if `PlacingTower k`: validate (4.7); on valid, deduct gold, insert Tower,
  (maze) recompute waypoints + re-path enemies; on invalid, emit error blip. If clicking an existing
  tower → `TowerSelected id`. Else `NoSelection`.
- **`UpgradeSelected` / `ChooseBranch`** → resolve next tier on tower's branch; if a fork tier and no
  branch chosen, require `ChooseBranch` first; deduct cost; replace tower stats; add to `GoldInvested`.
- **`SellSelected`** → refund `floor(GoldInvested*0.7)`, remove tower, (maze) recompute + re-path.
- **`CycleTargeting`** → rotate selected tower's `Targeting`.
- **`StartWave`** → only in `Building`; pay interest (`min(cap, floor(gold*rate))`, skipped wave 1),
  reset `Wave.Clock=0`, `Emitted=0…`, `Phase=Combat`.
- **`Tick dt`** → the simulation core (Section 4): for each fixed step, in order:
  1. advance wave scheduler (spawn enemies),
  2. tick enemy status effects (slow/poison/stun/vulnerable; apply DoT, expire),
  3. healer aura heals,
  4. move enemies; detect leaks (lose lives, on `lives≤0`→GameOver),
  5. towers acquire/validate targets & fire (spawn projectiles / resolve hitscan + chains),
  6. move projectiles; resolve hits (damage, splash, effects); award bounty on kills,
  7. tick transient FX,
  8. check wave completion → `WaveCleared`/`Victory`.
- **`TogglePause`** → wrap/unwrap `Paused prev`.
- **`RestartRun`** → fresh Model with same seed family; `NewMap` loads grid+waypoints.

### view
Pure projection of Model → a Skia draw-list (no game logic). Renders board, range circle for
selected/placing tower, towers (by kind/tier/angle), enemies (hp bars, effect icons), projectiles,
beams/explosions, HUD, build panel, screen overlays. The view reads `Selection`/`HoverTile` to draw
the placement ghost (green valid / red invalid).

### Subscriptions
- **Tick subscription:** a 60 FPS timer (`requestAnimationFrame`/Skia frame callback) dispatches
  `Tick realDt`. The `update`/loop converts `realDt` into a whole number of **fixed 1/60 s steps**
  via an accumulator (Section 13); `FastForward` doubles steps consumed. Sim is frozen in
  `Building/Paused/Victory/GameOver` (Tick still arrives but no-ops the sim, keeping UI animations).
- **Input subscription:** mouse move/click/wheel and keydown events mapped to `Msg` (edge vs held per
  Section 3).

## 8. Rendering (Skia 2D)
Logical 1280×720; scale to window with letterbox. Coordinate system: origin top-left, +x right,
+y down (matches grid). **Layered draw order** (painter's algorithm), redrawn every frame
(full-clear is cheap at these counts; no dirty-rect needed):

1. **Background/terrain** — tile fills: Buildable `#2E7D32` w/ subtle 1px grid `#1B5E20`; Path
   `#6D5A3F` with `#5A4A33` edges; Blocked `#37474F`; Spawn glow `#C62828`; Goal banner `#1565C0`.
2. **Range overlay** — when placing/selected: filled circle `#FFFFFF22`, stroke `#FFFFFFAA`.
3. **Towers** — base (rounded rect 28×28, kind color: Arrow `#8D6E63`, Cannon `#455A64`, Frost
   `#4FC3F7`, Tesla `#FDD835`) + rotated turret line/triangle to `Angle`; tier pips (small dots).
4. **Enemies** — drawn by `FS.GG.UI.Symbology` in the `Badge` grammar (`Symbology.badge`) from the
   §8.1 ChannelMap: the library draws the body silhouette, the identity sigil and the health arc as
   one symbol, sized by kind via `Token.R` in **four ranked sizes** — swarm 6 (Swarmling), standard 9
   (Grunt, Runner, Wisp, Spectre, Healer), heavy 13 (Brute, Shielded), boss 24. This replaces the
   six radii this layer used to specify (Grunt 9, Runner 7, Wisp 8, …): `Size` ranks four levels and
   a 7 px circle does not read as smaller than an 8 px one — see §8.1.
   The hand-rolled **HP bar** is gone — `Token.Health` is the health channel, and a bar drawn beside
   an arc encoding the same number is the same fact twice. The four **effect glyphs**
   (snowflake=slow, droplet=poison, bolt=stun, crack=vulnerable) stay this spec's own overlay and are
   **not** a Symbology channel — see §8.1 for why.
5. **Projectiles** — arrows: 10px line `#FFE082`; shells: 5px circle `#263238` with shadow; beams:
   Tesla jagged polyline `#FFF59D` 2px for 0.08 s; explosions: expanding ring `#FF7043` fading.
6. **Particles/FX** — muzzle flash (Arrow), smoke puff (Cannon), frost sparkle, electric arcs, gold
   "+N" floats on kill, "-1" red float on leak. Pool of ≤256 particles.
7. **HUD & panel** (Section 9) — drawn last, opaque.
8. **Screen overlays** — pause/victory/game-over scrims `#000000B0` + centered text.

Fonts: HUD numerals **"Inter"/system sans** 18–22px bold; titles 48px; tooltips 14px. Antialiasing
on; enemy/projectile motion looks smooth at 60 FPS without interpolation, but the renderer **may
interpolate** entity positions between fixed steps using a stored `prevPos` for extra smoothness under
fast-forward (optional).

### 8.1 Enemy symbology (the `Enemy → Token` ChannelMap)

Ten enemy kinds, each with health, armor, flight and a threat level, is precisely the problem
`FS.GG.UI.Symbology` exists to solve: a fixed channel set (`Token`), interchangeable grammars, and a
**legibility linter** that scores a symbol set against a per-channel capacity table. The per-game
work is one **ChannelMap** from §5.2's `Enemy` to `Token`; the library draws it.

**Where the map lives.** `Symbology` depends on `Scene`, so this map belongs in
`FS.GG.Game.Render`, **never** in the sim that owns `Enemy` — `FS.GG.Game.Core` reaches up to
nothing (ADR-0022 §2).

**Ten kinds do not overload `Klass` — but they do not fit in it either.** Two separate facts, and
conflating them is how this section first went wrong. *Overload*: not a problem. Capacity is **how
many levels the eye separates, not how many the grammar can draw** — `Klass` has capacity 6 and
`Sigil` 12, so a roster using 3 of each scores `Clean`. *Separation*: a real problem, and one the
linter does **not** check. `Klass` ships three cases and `Sigil` three fixed ones (`Mark of PathSpec`
is the fourth, and needs an authored vector path), so the pair yields **9 combinations for 10 kinds**
and some kinds must share. `Legibility.score` says `Clean` either way — it scores channel *capacity
and domain*, never whether two units are told apart — so a green verdict is not evidence of a
readable board. Check separation yourself; §14 does.

The collisions this map accepts are between kinds that **play alike** (Brute/Shielded are both slow
armored walkers; Juggernaut/Wyrm are both bosses), and `Health`, `R` and `Threat` still tell them
apart. The one it must **not** accept is Grunt/Healer: a Healer restores 6 hp/s to everything near it
and is the priority target on the board, while a Grunt is trash. They share `(Mobile, Ring)`, so the
separation has to come from `Threat` — which is exactly why `threatOf` reads `Bounty` (Grunt 4,
Healer 14) instead of a hand-written kind ranking that would have to remember to say so.

**`Speed` is pips, not px/s — this one is an `Error`.** `Token.Speed` is an `int` with domain
**0..6** and capacity **4**. Passing §5.2's `BaseSpeed` raw is not a near-miss; it is ten
`Error / Speed : Speed out of domain: 110 (expected 0..6)` findings plus a
`Warning / Speed : Speed overloaded: 10 distinct levels used, capacity 4`. Quantise to three ranked
tiers (below) and the same roster scores `Clean`. `Health` is likewise a **0..1 fraction** — pass
`Hp / MaxHp`, never `Hp`, or it is an out-of-domain `Error`.

**`Size` cost this spec three radii, and it was right to.** Layer 4 used to specify six
(Swarmling 6, Runner 7, Wisp/Spectre 8, Grunt/Healer 9, Brute 13, boss 24). `Size` is `Ordered`
with capacity **4**, so that roster returns
`Warning / Size : Size overloaded: 6 distinct levels used, capacity 4` — and the linter is simply
right: 6, 7, 8 and 9 px are four sizes inside a three-pixel band, which is decorative precision, not
a channel a player reads. `radiusOf` collapses them to the four that rank (swarm / standard / heavy
/ boss). Runner and Grunt now share a radius and are still told apart by `Klass` (Scout vs Mobile)
and `Speed`, which is what those channels are for.

**What Symbology deliberately does NOT draw here.** `Sigil` is an **identity** mark — who this unit
is — not a status. The four status glyphs of layer 4 encode `StatusEffect`, which is transient
per-enemy state, and there is no status channel in the fixed set. `Motion` is close but wrong: it is
budgeted **whole-board** (more than one distinct non-`Idle` rhythm across the board is a `Warning`),
so it cannot carry four independent statuses that stack on one enemy. Status stays the spec's own
overlay, drawn on top of the badge. Stating the absence is the point: a reader who meets this
sentence learns the channel set was considered and why it stops here.

**Towers** may use the same vocabulary in `Grammar.Token` rather than `Badge`, and they are the one
place `Token.SecondaryHeading` earns its keep: a tower base does not rotate but its turret tracks a
target, which is exactly the "a turret on a hull" second rotation channel — `Heading` fixed,
`SecondaryHeading = Some tower.Angle`. That is left to the implementer; layer 3 above is unchanged.

```fsharp
// The Enemy → Token ChannelMap. In a product this lives in FS.GG.Game.Render (ADR-0022 §2):
// Symbology depends on Scene, and the sim reaches up to nothing.
type Token = FS.GG.UI.Symbology.Token
type Klass = FS.GG.UI.Symbology.Klass
type Sigil = FS.GG.UI.Symbology.Sigil
type SymFaction = FS.GG.UI.Symbology.Faction
module Sym = FS.GG.UI.Symbology.Symbology

/// Kind → body silhouette. This GROUPS the roster, it does not separate it: with 3 Klass levels and
/// the 3 fixed Sigil cases there are 9 combinations for 10 kinds, so pairs necessarily collide
/// (Brute/Shielded, Juggernaut/Wyrm). That is fine for the kinds that PLAY alike, and the remaining
/// channels — Threat, Health, R — carry the rest. See §8.1 on the one collision that would not have
/// been fine.
let klassOf (e: Enemy) : Klass =
    match e.Kind with
    | Brute | Shielded | Juggernaut | Wyrm -> Klass.Heavy
    | Runner | Swarmling -> Klass.Scout
    | _ -> if e.Fly then Klass.Scout else Klass.Mobile

let sigilOf (e: Enemy) : Sigil =
    match e.Kind with
    | Wisp | Spectre -> Sigil.Bolt            // the two that shrug a damage type
    | Juggernaut | Wyrm -> Sigil.Fang         // boss tier
    | _ -> Sigil.Ring

/// BaseSpeed (30..110 px/s) → 3 ranked pip tiers. `Speed` is an int in 0..6 with capacity 4:
/// passing px/s raw is an out-of-domain Error, not merely an overload — see §8.1.
let speedTierOf (e: Enemy) : int =
    if e.BaseSpeed <= 45.0 then 1
    elif e.BaseSpeed <= 70.0 then 2
    else 3

/// `Bounty` is the roster's own ranking of "how much does this thing matter" (§5.2: Swarmling 1,
/// Grunt 4, Healer 14, Wyrm 200), so READ IT rather than inventing a second ranking that can drift
/// out of step with the table. Quantised to 4 levels, which is exactly `Threat`'s capacity.
let threatOf (e: Enemy) : float =
    if e.Bounty >= 60 then 1.0        // Juggernaut, Wyrm
    elif e.Bounty >= 14 then 0.75     // Healer — the priority target
    elif e.Bounty >= 9 then 0.5       // Wisp, Brute, Spectre, Shielded
    else 0.25                         // Swarmling, Grunt, Runner

/// Four ranked sizes, not ten. `Size` is `Ordered` with capacity 4, and the pre-Symbology layer 4
/// asked for six radii (6/7/8/9/13/24) — which warns, because a 7 px circle does not rank against
/// an 8 px one. The four that survive are the four that MEAN something: swarm, standard, heavy,
/// boss. See §8.1.
let radiusOf (e: Enemy) : float =
    match e.Kind with
    | Juggernaut | Wyrm -> 24.0       // boss
    | Brute | Shielded -> 13.0        // heavy
    | Swarmling -> 6.0                // swarm
    | _ -> 9.0                        // standard: Grunt, Runner, Wisp, Spectre, Healer

let tokenOf (e: Enemy) : Token =
    { Sym.defaultToken with
        Cx = e.Pos.Vx
        Cy = e.Pos.Vy
        R = radiusOf e                        // R > 0 or Size is a degenerate Error
        Faction = SymFaction.Enemy            // every creep is hostile; towers are Ally
        Klass = klassOf e
        Sigil = sigilOf e
        Health = e.Hp / e.MaxHp               // 0..1 fraction, NOT raw Hp
        Shield = e.Armor > 0.0
        Speed = speedTierOf e
        Threat = threatOf e }
```

**Legibility as a test, not a hope.** The map is pure and the roster is data, so "is this wave
readable" is an ordinary assertion: `Legibility.score (wave |> List.map tokenOf)` and check
`Verdict = Clean`. See §14. Note `Legibility.Severity.Error` must be written qualified — a bare
`Error` would shadow `Result.Error` for every consumer that opens the module.

## 9. UI / HUD / Screens
**Screens:** Title (logo, *Play*, map select, difficulty), Map Select (3 thumbnails), Play
(board + HUD), Pause (Resume/Restart/Quit, sim frozen), Victory (stats + score), Game Over (wave
reached + score + Retry).

**Top HUD bar** (y 0–28, full width, `#212121E0`):
- Left: ❤ Lives `20`, ⛁ Gold `250`, ★ Score.
- Center: **Wave x/20** + a **next-wave preview** strip (icons of enemy kinds in the upcoming wave
  with counts). During Combat: live "enemies remaining" + wave progress bar.
- Right: phase indicator (BUILD / WAVE / ⏩×2), interest preview (`+34`), clock.

**Build panel** (right, x 1020–1280, y 28–720, `#263238F0`): the 4 tower buttons with icon, cost,
and hotkey; greyed if unaffordable. When a tower is **selected**, the panel swaps to an **inspector**:
tower name/tier, current stats, **Upgrade** button(s) (branch A/B with cost when at a fork),
**Targeting** toggle (shows current mode), **Sell (+refund)** button, and a DPS estimate.

**In-world widgets:** placement ghost (tower preview + range circle, green/red), selected-tower
range circle + footprint highlight, hover tooltips (enemy: hp/armor/resists; tower: stats).
**Bottom-left** transient toasts ("Wave 7 incoming!", "Not enough gold").

Formatting: gold/score thousands-separated; timers `M:SS`; DPS to 1 decimal.

### 9.1 Menu & configuration — the shared game shell

Bulwark uses the **generic FS.GG game shell** (FS-GG/FS.GG.Rendering#991) — the same menu/start
screen and settings every FS.GG game shares — rather than a bespoke per-game menu. The game
supplies only its **name**, its **key→command map** (the rebindable actions from §3 Controls),
and its play `update`/`view`; the shell provides everything below.

- **Main menu / start screen** — the game's name (**BULWARK: TOWER DEFENSE**) as the title
  label, with **Start**, **Config**, and **Exit**. Map selection (Serpentine · Crossroads · The
  Labyrinth, §6) sits alongside Start.
- **`Esc` from gameplay** opens the pause menu (Resume · Config · Exit to menu) over the same
  shell; `Esc` again resumes. (During placement, `Esc` first cancels the pending placement per §3.)
- **Config / Settings**, all applied live and persisted across restarts:
  - **Screen resolution** and **fullscreen** (windowed / borderless / fullscreen), driven
    through the SkiaViewer window-behavior + `LogicalCanvas` letterbox seam.
  - **Key rebinding** — the player remaps this game's controls (the §3 actions — tower-type
    select, upgrade, choose branch, sell, cycle targeting, start wave, fast-forward, build-grid
    toggle, pause) via the `Controls.KeyRebind` UI over the `KeyboardInput.Keymap` mechanism;
    bindings persist via `KeymapCodec` (JSON), beside this game's other saved config (§13).
  - Game-specific rows are added as extra Config rows over the shell: **Difficulty** (the §12
    preset — Easy / Normal / Hard), **Master volume**/**Sound** (route to `Audio.setMasterVolume`,
    §10, clamped `[0,1]`), and **Build grid** (toggles the §3 grid overlay). The menu, Esc
    routing, display settings, and rebind screen come from the shell.

The shell is pointer- and keyboard-navigable over the interactive Controls host (the
`fs-gg-skiaviewer` "game → pointer host" recipe). It is a shared dependency, so Bulwark does
**not** re-specify menu-stack/cursor/settings machinery of its own. The **Stats & charts**
screen (§9.2) is a Bulwark-specific screen reached as a Config/menu row.

### 9.2 Stats & charts screen
The Stats screen visualizes **the last run** and **lifetime** play. It reads a `RunStats`
snapshot (never live sim), so it is a pure, deterministic render reachable from Title, the
run-end menu, and Pause. Chart-design choices below follow the project dataviz conventions
(form-first, validated colorblind-safe categorical palette, single axis, identity by entity).

**Tracked per run** — `RunStats` (§7 Model `Stats`), accumulated in `Tick`, snapshotted on
`Victory`/`GameOver`:

| Field | Type | Updated |
|-------|------|---------|
| `wavesSurvived` | `int` | incremented on each `WaveCleared` (§4.2) |
| `enemiesLeaked` | `(wave:int * count:int) list` | +1 for the current wave on each leak (§4.6) |
| `enemiesKilled` | `int` | +1 on each enemy death (§4.5) |
| `towersBuilt` | `Map<TowerKind,int>` | +1 for the kind on each valid placement (§4.7) |
| `goldEarned` | `(wave:int * amount:int) list` | bounty + interest + clear bonus per wave (§4.9) |
| `goldSpent` | `(wave:int * amount:int) list` | placement + upgrade cost per wave (§4.9/§5.1) |
| `damageByTowerType` | `Map<TowerKind,float>` | += applied damage credited to the firing tower (§4.5) |
| `livesRemaining` | `int` | mirrors `Econ.Lives` at snapshot (§4.6) |

**Lifetime** — `LifetimeStats`, persisted (§13): `bestWave`, `mapsPlayed`, `winRate` (% of
runs ending in Victory), `totalLeaks`, `favoriteTower` (most-built `TowerKind` across runs).

**Layout** (logical 1280×720): a KPI tile row across the top, two charts below.

```
┌──────────────────────────── STATS ────────────────────────────┐
│  ┌ WAVES ┐ ┌ LEAKS ┐ ┌ GOLD EARNED ┐ ┌ BEST TOWER ┐          │  ← KPI stat tiles
│  │  14   │ │  11   │ │   3,240     │ │   Tesla    │          │
│  └───────┘ └───────┘ └─────────────┘ └────────────┘          │
│                                                                │
│  Leaks per wave                     Economy: earned vs spent   │
│  ▇                             3200 ┤            ╭── Earned    │
│  ▇        ▇                          │       ╭─╯╭╯             │
│  ▇  ▇  ▇  ▇     ▇                     │   ╭─╯╭─╯ Spent          │
│  1  3  5  7  9  11 (wave)          0 ┼───────────────► wave #  │
└────────────────────────────────────────────────────────────────┘
     ↑/↓ scope:  ▸ This Run · Lifetime            ESC — Back
```

The **WAVES SURVIVED**, **LEAKS** (total this run), and **GOLD EARNED** tiles read straight
off `RunStats`; **BEST TOWER** names the `TowerKind` with the highest `damageByTowerType`
(the Lifetime scope shows `favoriteTower` instead).

**Charts** (rendered in Skia with the same draw-list discipline as §8):

1. **Leaks-per-wave** — *form: per-category magnitude → bars.* x = wave number (`1…N`), y =
   enemies leaked that wave (from `enemiesLeaked`). **Single series**, so one hue and no
   legend — it shows where the defense broke. Bars are 4 px-rounded at the data end with a
   2 px surface gap between them. Fill `#2a78d6` (light) / `#3987e5` (dark) — categorical slot 1.
2. **Economy: earned vs spent** — *form: change over an ordered index → line.* x = wave
   number, y = **cumulative** gold; **two series** (Earned, Spent, from `goldEarned`/`goldSpent`)
   → a legend is present and both lines are direct-labeled at their right end ("Earned"/"Spent"),
   surfacing economy management. Earned `#2a78d6`, Spent `#1baf7a` (slots 1–2, adjacent-pair
   CVD-validated). 2 px lines, ≥ 8 px end markers, recessive 1 px gridlines in `#3C3C3C`.

Conventions honored: **color follows the entity** (Earned is always slot 1, Spent always slot 2
— never repainted by the scope toggle); **one axis only** (no dual-scale, though the two series
share a gold axis); chart **text uses ink tokens** (`#FFFFFF` primary / `#C3C2B7` muted), never
the series hue; layout is **fixed and deterministic**, so a fixed-seed run (§13) renders
byte-identical for snapshot tests. The `↑/↓` **scope** toggle swaps the data source This-Run ↔
Lifetime without changing colors.

**Model/Msg hooks:** the §7 Model already carries `Stats: RunStats`; extend `RunStats` with
the fields tabled above and add `Lifetime: LifetimeStats` alongside it. Accumulate in the
`Tick` cases (append an `enemiesLeaked` entry on each leak, `goldEarned`/`goldSpent` on bounty/
interest/clear and on placement/upgrade, credit `damageByTowerType` when a hit resolves); on
`Victory`/`GameOver`, fold `RunStats` into `Lifetime` and persist (§13). `OpenStats`/`CloseStats`
switch a `Screen = Stats of scope:StatScope` state; the render is a no-op on the sim.

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
| Valid tower placement (§4.7) | `Audio.playSfx` | `sfx-tower-placed` | tower placed (thunk) |
| Invalid placement (§4.7) | `Audio.playSfx` | `sfx-invalid-placement` | invalid placement (low buzz) |
| Arrow fires (§4.4) | `Audio.playSfx` | `sfx-arrow-shot` | Arrow shot (twang) |
| Cannon fires (§4.4) | `Audio.playSfx` | `sfx-cannon-fire` | Cannon fire (boom) |
| Cannon shell impact (§4.10) | `Audio.playSfx` | `sfx-cannon-splash` | splash (crunch) |
| Frost fires (§4.4) | `Audio.playSfx` | `sfx-frost-bolt` | Frost bolt (shimmer) |
| Tesla fires (§4.4) | `Audio.playSfx` | `sfx-tesla-zap` | Tesla zap (crackle) |
| Enemy killed | `Audio.playSfx` | `sfx-enemy-death` | enemy death (pop) |
| Boss/Juggernaut killed | `Audio.playSfx` | `sfx-boss-death` | boss death (roar) |
| Enemy leaks (§4.6) | `Audio.playSfx` | `sfx-life-lost` | leak/life lost (alarm thud) |
| `StartWave` (§4.9) | `Audio.playSfx` | `sfx-wave-start` | wave start (horn) |
| `WaveCleared` (§4.2) | `Audio.playSfx` | `sfx-wave-cleared` | wave cleared (chime) |
| Interest/bounty paid (§4.9) | `Audio.playSfx` | `sfx-coins` | gold/interest (coins) |
| `UpgradeSelected` (§5.1) | `Audio.playSfx` | `sfx-upgrade` | upgrade (power-up) |
| `SellSelected` (§4.9) | `Audio.playSfx` | `sfx-sell` | sell (cash register) |
| `GameOver` (§11) | `Audio.playSfx` | `sfx-game-over` | game over (descending sting) |
| `Victory` (§11) | `Audio.playSfx` | `sfx-victory` | victory (fanfare) |
| Enter `Building` phase (§4.2) | `Audio.playMusic … true` | `music-build` | calm loop during Build |
| `StartWave` → `Combat` (§4.2) | `Audio.playMusic … true` | `music-combat` | tense percussive loop during Combat |
| Boss wave enter (w10/w20, §6) | `Audio.playMusic … true` | `music-boss` | boss-wave variant |
| Enter `Victory`/`GameOver` (§11) | `Audio.stopMusic` | — | stop combat loop for the victory/defeat stinger |

Background **music loops** and switches on phase transitions: `Audio.playMusic (TrackId
"music-build") true` on entering `Building`, swapping to `music-combat` (loop `true`) on
`StartWave`, and to the `music-boss` variant on the mini-boss/boss waves; `Audio.stopMusic`
runs on entering `Victory`/`GameOver` so the defeat/victory stinger plays clean (design intent:
combat music dips 30% on boss roar via a transient duck). A **mute/settings toggle** maps to
`Audio.setMasterVolume` (e.g. `Audio.setMasterVolume 0.0` to mute, `1.0` to restore; the level
clamps to `[0.0, 1.0]`). **Testing:** collect the frame's
`AudioEffect`s, `Audio.interpret` them, and assert the `AudioEvidence.Requested` sequence for
representative events (e.g. a valid tower placement requests exactly `PlaySfx (SoundId
"sfx-tower-placed", _)`).

## 11. Win / Loss / Scoring
- **Win:** complete **Wave 20** (Wyrm killed and board cleared) with `lives > 0` → Victory.
- **Loss:** `lives ≤ 0` at any time → Game Over.
- **Lives/continues:** 20 lives (difficulty-scaled), no continues in v1.

**Scoring (additive `Score`):**
- Enemy killed: `+bounty * 2` points (boss/Juggernaut as table bounty ×2).
- Wave cleared: `+ (50 * waveNumber)`.
- **No-leak wave bonus:** `+200` if a wave is cleared with zero leaks that wave.
- **Lives remaining at victory:** `+ lives * 150`.
- **Gold banked at victory:** `+ floor(gold * 0.5)`.
- **Early-call bonus** (continuous mode): calling a wave with `t` seconds of the prior wave's timeline
  unspent → `+ floor(t * 3)`.
- **Difficulty multiplier** applied to final score: Easy ×0.8, Normal ×1.0, Hard ×1.4.
Score persists as a per-map high score (Section 13).

## 12. Difficulty & Balancing
Data-driven; all tunables in a config record so balance is code-free.

| Param | Default | Range | Effect |
|---|---|---|---|
| `startingGold` | 250 | 120–500 | opening economy |
| `startingLives` | 20 | 5–50 | error tolerance |
| `interestRate` | 0.08 | 0–0.15 | banking reward |
| `interestCap` | 40 | 0–100 | caps turtling |
| `sellRefund` | 0.70 | 0.3–1.0 | replan flexibility |
| `hpScalePerWave` | 0.06 | 0–0.15 | wave HP ramp |
| `countBasePerWave` | 1.4 | 0.5–3 | enemy count ramp |
| `spawnIntervalMin` | 0.35 | 0.2–1.0 | max spawn density |
| `globalSpeedMult` | 1.0 | 0.5–1.5 | enemy speed knob |
| `slowFloor` | 0.25 | 0.1–0.6 | strongest possible slow |
| `dotStackCap` | 3 | 1–5 | poison stacking |
| `chainRange` | 90 | 60–140 | Tesla chain reach |
| `waveClearBonusBase` | 10 | 0–50 | full-clear reward |
| `mazeNoBuildSpawnRadius` | 2 | 0–4 | anti-spawn-camp |

**Difficulty presets** scale `(startingGold, startingLives, hpScalePerWave, globalSpeedMult,
scoreMult)`: Easy `(350, 30, 0.04, 0.9, 0.8)`, Normal `(250, 20, 0.06, 1.0, 1.0)`,
Hard `(180, 12, 0.09, 1.15, 1.4)`.

## 13. Technical Notes
**Performance budget:** target **60 FPS / 16.7 ms/frame**. Worst-case live counts: ~120 enemies
(swarm waves), ~50 towers, ~400 projectiles, ~256 particles. Hot loops are O(towers×enemies) for
targeting and O(projectiles×enemies) for splash; bucket enemies into a **uniform spatial grid** —
`SpatialGrid.build 64.0 enemiesByPoint` once per step, then `SpatialGrid.queryRadius towerPos range`
for targeting and splash, so a query inspects only neighbouring cells. Do not hand-roll the buckets.
Keeps per-frame work well under budget. The maze search runs only on place/sell (rare), bounded by
40×22=880 nodes.

**Fixed timestep + accumulator** (determinism) — `FixedStep` does the banking *and* the
spiral-of-death cap, so there is no accumulator loop to get wrong:
```fsharp
// in the Tick handler. drainWith's first argument IS the spiral-of-death cap, expressed as a frame
// -time budget: maxStepsPerFrame * dtFixed is the old "at most 5 steps per frame" rule.
let struct (ticks, acc') =
    FixedStep.drainWith (float maxStepsPerFrame * dtFixed) dtFixed realDt acc

let stepsPerTick = if model.FastForward then 2 else 1  // fast-forward = more steps, same dt
let mutable m = model
for _ in 1 .. ticks do
    for _ in 1 .. stepsPerTick do m <- simStep dtFixed m
acc <- acc'
```
All sim math uses `dtFixed = 1/60`; rendering may interpolate with leftover `acc/dtFixed` alpha.

**Determinism / RNG:** a single seeded PRNG threaded through Model — `FS.GG.Game.Core`'s **`Rng`**
(splitmix64), seeded with `Rng.ofSeed`. Do not hand-roll an `RngState`: `Rng` is already a value, so
threading it through the Model is the natural thing rather than a discipline you have to maintain, and
every draw (`Rng.nextFloat`, `Rng.nextInt`, `Rng.nextBool`) returns `struct (x, rng')` to write back.
Every random draw (crit rolls, stun procs, splash falloff jitter, particle spawn) pulls from it in a
fixed order. Same seed + same inputs ⇒ identical run (enables replay & test reproducibility). Seed is
chosen at run start (default fixed `0xBULWARK` in tests).

**Persistence:** per-map high score + best wave reached stored in `localStorage`/app settings as JSON
`{ map, score, wave, difficulty, date }`. No mid-run save in v1.

**Edge cases:** placement during Combat (allowed — towers can be built mid-wave if gold allows);
selling a tower mid-wave on a maze map triggers re-path of live enemies; an enemy dying to DoT after
its killer tower was sold still awards bounty (bounty tied to enemy, not tower); simultaneous
multi-leak dropping lives below 0 → clamp to 0 and GameOver; projectile whose target dies before
arrival still resolves splash at last-known target pos (single-target projectile despawns harmlessly);
two slows applied same frame → strongest wins, no double-decrement; pause during a beam FX freezes
its timer.

## 14. Acceptance Criteria (test scenarios)
Verifiable Given/When/Then. Coordinates assume *Serpentine* (fixed map) unless noted; seed fixed.

1. **Placement — valid build deducts gold.**
   Given Phase=Building, Gold=250, an empty `Buildable` tile `(5,5)`, and `PlacingTower Arrow`
   (cost 70). When `LeftClick` at the pixel center of `(5,5)`. Then a Tower(Arrow,T1) exists at
   `(5,5)`, Gold=180, and a build SFX fires.

2. **Placement — on path is rejected.**
   Given `PlacingTower Cannon`, Gold=250, and a `Path` tile under the cursor. When `LeftClick`.
   Then no tower is created, Gold is unchanged (250), and an error blip is emitted.

3. **Placement — insufficient gold rejected.**
   Given `PlacingTower Tesla` (cost 160), Gold=100, empty Buildable tile. When `LeftClick`.
   Then no tower created, Gold=100, error blip emitted.

4. **Placement — maze seal is rejected.**
   Given a maze map and a candidate tile that is the **last** open cell of the only Spawn→Goal
   corridor. When `LeftClick` to place. Then A* finds no path, placement is rejected, Gold unchanged,
   and toast "would seal path" shows; the previously computed waypoints are unchanged.

5. **Tower kills an enemy and awards gold.**
   Given one Grunt (HP 40, bounty 4) at pixel `(170,170)` and an Arrow T2 (dmg 12) at `(170,300)`
   in range, Gold=G, Phase=Combat. When the sim runs until the Grunt's HP reaches ≤0 (≈4 hits over
   ~1.4 s at RoF 2.2). Then the Grunt is removed from `Enemies`, Gold = G+4, Score increases by 8,
   and a "+4" gold float spawns.

6. **Armor reduces physical damage.**
   Given a Brute (HP 160, armor 6) and an Arrow T1 (dmg 8, Physical). When one arrow hits.
   Then applied damage = `max(1, 8-6)` = 2, Brute HP = 158.

7. **Frost applies a slow that reduces effective speed.**
   Given a Grunt (baseSpeed 55) with no effects and a Frost T1 (slow factor 0.6, dur 1.5 s) in range.
   When the Frost bolt hits. Then the Grunt gains `Slow(0.6, 1.5)`, its `effectiveSpeed` = 33 px/s,
   and after 1.5 s with no further frost hits the effect expires and speed returns to 55.

8. **Strongest slow wins (no stacking).**
   Given a Grunt already under `Slow(0.6,_)` from Frost T1. When a Glacier (factor 0.35) also hits.
   Then `effectiveSpeed = 55*0.35 = 19.25` px/s (strongest applies), not 55*0.6*0.35.

9. **Resistance / immunity respected.**
   Given a Spectre (Frost resist 1.0) hit by a Frost bolt (dmg 5, slow 0.6). Then applied frost
   damage = 0 and **no** Slow effect is added (immune); the Spectre keeps baseSpeed 65.

10. **Cannon splash hits a cluster.**
    Given three Grunts within 30 px of impact point and a Cannon T1 (dmg 30, splash r=42). When the
    shell lands centered on the cluster. Then the center Grunt takes 30, the two within falloff take
    between 15 and 30 each per linear falloff, and all three lose HP that frame.

11. **Tesla chains to multiple targets with falloff.**
    Given a Tesla T1 (dmg 18, chain ×3, falloff 0.6) and 5 enemies spaced within `chainRange`.
    When it fires at the primary. Then primary takes 18, jump1 ≈ 10.8, jump2 ≈ 6.48, jump3 ≈ 3.89
    (the 4th enemy), and the 5th enemy is untouched (chain count exhausted).

12. **Targeting mode changes the chosen target.**
    Given two enemies in range — A nearer the Goal (higher progress), B nearer the tower (closer).
    When targeting = First, the tower targets A; when cycled to Closest (`T`), it retargets B on its
    next acquisition.

13. **Sell refunds 70% and (maze) re-paths.**
    Given a Cannon T1 (invested 120) on a maze map shaping the route, Gold=G. When `SellSelected`.
    Then the tower is removed, Gold = G + floor(120*0.7)=G+84, waypoints are recomputed, and live
    ground enemies re-target to the nearest node on the new path without teleporting.

14. **Interest is paid on Start Wave (capped, skipped on wave 1).**
    Given Phase=Building, Gold=600, rate 0.08, cap 40, wave index ≥ 1. When `StartWave`. Then
    interest = `min(40, floor(600*0.08)=48)` = 40, Gold becomes 640 before Combat. Given the same on
    **wave 1**, no interest is paid.

15. **Wave completes and transitions.**
    Given Phase=Combat with all SpawnGroups exhausted (`AllSpawned`) and the last enemy just died.
    When the sim step resolves. Then Phase=`WaveCleared 1.5`, a wave-clear bonus `+(10+wave)` gold and
    `+(50*wave)` score are awarded, and after 1.5 s Phase=`Building` with the next wave previewed.

16. **Life lost on leak.**
    Given Lives=20 and a Grunt (leakCost 1) reaching the Goal waypoint. When it snaps to the Goal.
    Then the Grunt is removed with no bounty, Lives=19, and a red "-1" float appears.

17. **Game over on lethal leak.**
    Given Lives=1 and a Brute (leakCost 2) leaking. When it reaches the Goal. Then Lives clamps to 0
    and Phase=`GameOver` immediately; the current wave is abandoned and the Game Over screen shows the
    wave reached and final score.

18. **Victory on final wave clear.**
    Given Phase=Combat on Wave 20, the Wyrm dead, board cleared, Lives>0. When the step resolves.
    Then Phase=`Victory`, score adds `lives*150 + floor(gold*0.5)`, multiplied by the difficulty
    score multiplier, and the Victory screen shows.

19. **Fixed timestep determinism.**
    Given a fixed seed and a recorded input sequence. When the run is replayed twice. Then enemy
    positions, kills, and final Score are bit-identical across runs (no real-time-dependent drift).

20. **Input — fast-forward doubles sim rate, not dt.**
    Given Phase=Combat at 1×. When `F` toggles fast-forward (2×). Then two fixed steps are consumed
    per accumulator tick (enemies advance ~2× wall-clock distance) while each step still uses
    dt=1/60, and effect timers/damage remain numerically identical to running 1× for twice as long.

21. **Crit doubles a physical hit.**
    Given an Arrow T3-A Sniper (dmg 40, crit 15% ×2, Physical) and a Grunt (armor 0), fixed seed.
    When a shot resolves with its single crit draw `Rng.nextFloat < 0.15`. Then applied =
    `max(1, 40*2 - 0)` = 80; a shot whose draw is ≥ 0.15 applies 40. Exactly one draw is pulled per
    shot regardless of outcome (§4.5, §13).

22. **Armor-ignore halves effective armor.**
    Given an Arrow T4-A Marksman (dmg 90, ignores 50% armor) and a Brute (armor 6). When a non-crit
    hit lands. Then effective armor = `6*(1-0.5)` = 3 and applied = `max(1, 90-3)` = 87 (§4.5).

23. **Vulnerable amplifies the next hit.**
    Given a Grunt carrying `Vulnerable(0.25, _)` from a Cannon T3-B Demolition and an Arrow T1 hit
    (dmg 8, armor 0). When the arrow resolves. Then applied = `max(1, (8-0)*1.25)` = 10 — Vulnerable
    applies **after** armor and **before** the floor (§4.5).

24. **On-death spawn: the Wyrm splits into Wisps.**
    Given the Wyrm at pos P reaching 0 HP. When death resolves. Then bounty 200 is awarded first, two
    Wisp enemies are created at P on the flying straight-line route, the Wyrm is removed, and
    `sfx-boss-death` is requested; the spawn event itself pays no additional bounty (§4.5, §5.2).

25. **Branch traits persist across T3→T4.**
    Given an Arrow upgraded to T3-A Sniper (15% ×2 crit), then to T4-A Marksman (row lists only
    armor-ignore). When it fires. Then the tower **both** rolls the 15% ×2 crit and ignores 50% armor:
    a T4 row supersedes only the traits it restates, and unlisted branch traits carry forward (§4.5).

26. **Railgun pierces a collinear line.**
    Given a Tesla T4-B Railgun and three enemies collinear on the tower→target segment, each within
    the beam radius. When it fires. Then all three take the full 120 damage (no chain falloff) and each
    rolls its 0.7 s Stun independently (§4.10).

## 15. Stretch Goals
Ranked, out-of-scope for v1:
1. **Endless/continuous mode** with early-wave-call bonus and infinite scaling waves + leaderboard.
2. **Targeting priority per-tower presets** saved across maps; "lock target" hotkey.
3. **Hero/abilities:** a player-controlled commander or active spells (meteor, freeze-all) on cooldown.
4. **More tower types:** Poison Sprayer, Buff/Aura tower (boosts adjacent towers), Tar pit (zone slow).
5. **Maze-building maps as the default** with destructible blockers and ground/air dual lanes.
6. **Map editor** (author grid + waypoints + waves) exporting the `Wave`/grid JSON.
7. **Enemy abilities:** burrowers (skip a path segment), splitters (spawn 2 on death), shielders
   (grant temporary armor aura).
8. **Meta-progression:** unlock tower branches/maps across runs; daily seeded challenge.
9. **Co-op two-warden** mode sharing one economy.

## 16. Milestone Roadmap

Implementation is sequenced into milestones; each item is a colored checkbox
tracking its status. Items reference the section that specifies them.

**Legend:** 🟥 Not started · 🟨 In progress · 🟩 Done · ⬜ Deferred (post-v1)

_All items start 🟥 (spec status). Flip an item to 🟨 when work begins and 🟩 once
its acceptance test(s) pass (§14)._

### M0 — Scaffold & fixed-step loop
- 🟥 Project scaffold: `Model`/`Msg`/`update`/`view` skeleton (§7)
- 🟥 Fixed 60 Hz tick via `FixedStep.drainWith`, banked remainder + spiral cap (§4.2, §13)
- 🟥 `Rng` value seeded with `Rng.ofSeed`, threaded through `Model` (§13)
- 🟥 Logical 1280×720 coordinate transform + letterbox scaling (§4.1, §8)
- 🟥 Fast-forward doubles steps consumed, dt unchanged (§4.2, §13) — AC #20

### M1 — Board, grid & path
- 🟥 40×22 tile grid of 32×32 px, `TileKind` fills, `(col,row)`→pixel center (§4.1)
- 🟥 Fixed-path maps: authored `Waypoint[]` polyline Spawn→Goal (§4.1)
- 🟥 Maze maps: `distanceField` + `flowField` from Goal via `Pathfinding` (§4.1)
- 🟥 Three shipped maps: Serpentine, Crossroads, The Labyrinth (§6)

### M2 — Enemies: movement, pathfinding & leaks
- 🟥 Per-step waypoint follow: snap/advance, `effectiveSpeed` integration (§4.3)
- 🟥 Ten enemy kinds (hp/speed/armor/resists/fly/bounty/leak) (§5.2)
- 🟥 Flying enemies straight-line Spawn→Goal, `canHitAir` gate (§4.3)
- 🟥 Maze re-path: nearest-node rejoin without teleport (§4.3) — AC #13
- 🟥 Healer aura heals nearby allies 6 hp/s r=60 (§5.2)
- 🟥 20 lives; leak removes enemy, `lives -= leakCost`, "-1" float (§4.6) — AC #16
- 🟥 `lives ≤ 0` → GameOver immediately, wave abandoned (§4.6) — AC #17
- 🟥 On-death effects: Wyrm spawns 2 Wisp, Pandemic spreads poison r=40 (§4.5, §5.2) — AC #24
- 🟥 Swarmling packs of 8 via scheduler `Count=8` group (§5.2, §6)

### M3 — Towers: targeting, firing, damage & status
- 🟥 Tower table T1 base stats, four kinds (§5.1)
- 🟥 Per-step cooldown, acquire/validate target, fire (§4.4)
- 🟥 Targeting modes First/Last/Strongest/Closest, sticky, cycle with `T` (§4.8) — AC #12
- 🟥 Physical `max(1, dmg-armor)`; elemental resist multiplier + immunity (§4.5) — AC #6, #9
- 🟥 Slow (strongest wins, no stack), Poison DoT, Stun, Vulnerable (§4.5) — AC #7, #8
- 🟥 Upgrade trees T1→T2→T3 fork A/B→T4, stat replacement (§5.1)
- 🟥 Damage pipeline order: crit → vs-armored → armor(−pierce) → Vulnerable → floor (§4.5) — AC #23
- 🟥 Crit rolls: Sniper 15% ×2 via one `Rng.nextFloat` in fire order (§4.5, §5.1) — AC #21
- 🟥 Armor-ignore (Marksman 50%) and +% vs-armored (Breaker) traits (§4.5, §5.1) — AC #22
- 🟥 Branch traits persist T3→T4; only tabulated stats are replacements (§4.5, §5.1) — AC #25
- 🟥 Sniper/Marksman Strongest-target affinity default on upgrade (§4.8)

### M4 — Projectiles & flight
- 🟥 Projectile model: `pos`/`vel`, Arrow re-homes, Cannon no homing (§4.10, §5.3)
- 🟥 Cannon splash: linear falloff 100%→50% at edge (§4.10) — AC #10
- 🟥 Hitscan Tesla chain ×N with `falloff^k` over `chainRange` (§4.10) — AC #11
- 🟥 Projectile lifetime cap 2.0 s, target-loss despawn (§4.10)
- 🟥 Tesla chain selection: nearest un-hit from last link, no revisit, canHitAir (§4.10)
- 🟥 Railgun pierce: beam hits all enemies on tower→target segment (§4.10, §5.1) — AC #26

### M5 — Economy & placement validation
- 🟥 Placement validity: in-grid Buildable, empty, `gold≥k`, not under panel (§4.7) — AC #1, #2, #3
- 🟥 Maze seal rejection via `Pathfinding.bfs` returning `None` (§4.7) — AC #4
- 🟥 On valid: deduct gold, create tower, (maze) recompute path (§4.7)
- 🟥 Bounty on kill, wave-clear bonus, interest `floor(gold*0.08)` cap 40 (§4.9) — AC #5, #14
- 🟥 Sell refund 70% floored + (maze) re-path (§4.9) — AC #13
- 🟥 Difficulty tunables config record + Easy/Normal/Hard presets (§12)
- 🟥 Maze no-build margin: 2-tile radius around Spawn (§4.7)

### M6 — Waves, win/loss & scoring
- 🟥 Wave scheduler: `SpawnGroup` timeline + per-group emit cursor (§6)
- 🟥 20-wave campaign, ramp knobs `hpScale`/count/interval (§6)
- 🟥 Wave completion → `WaveCleared` 1.5 s → `Building`, bonuses (§4.2, §6) — AC #15
- 🟥 Phase state machine Building/Combat/Paused/WaveCleared/Victory/GameOver (§4.2, §7)
- 🟥 Victory on Wave 20 clear with `lives > 0` (§11) — AC #18
- 🟥 Additive scoring: kills, wave clear, no-leak, victory + difficulty mult (§11)
- 🟥 Two-front waves 15–19: overlapping staggered `SpawnGroup`s (§6)
- 🟥 Per-map high score + best wave reached persisted as JSON (§11, §13)

### M7 — Rendering & enemy symbology
- 🟥 Layered draw order: terrain, range, towers, enemies, projectiles, FX, HUD, overlays (§8)
- 🟥 Tile/tower/projectile/beam/explosion draw specs (§8)
- 🟥 `Enemy → Token` ChannelMap in `FS.GG.Game.Render` (klass/sigil/threat/radius) (§8.1)
- 🟥 Four ranked sizes, `Speed` pip tiers, `Health` fraction (§8.1)
- 🟥 Status glyph overlay (slow/poison/stun/vulnerable) — not a Symbology channel (§8.1)
- 🟥 `Legibility.score` per wave asserts `Verdict = Clean` (§8.1)
- 🟥 Optional render interpolation between fixed steps via `prevPos` alpha (§8, §13)

### M8 — UI, menus & stats
- 🟥 Top HUD bar: lives/gold/score, wave x/20 + next-wave preview, phase/interest (§9)
- 🟥 Build panel + selected-tower inspector (upgrade/targeting/sell/DPS) (§9)
- 🟥 Adopt the generic FS.GG game shell (FS-GG/FS.GG.Rendering#991): main menu (title + Start/Config/Exit), Esc pause routing, Settings with screen resolution + fullscreen, and in-game key rebinding of the §3 controls, persisted — the game provides its name + key→command map + play update/view; the shell provides the rest, no bespoke menu system (§9.1)
- 🟥 Game-specific Config rows over the shell (difficulty preset, volume/sound, build grid) apply live + persist (§9.1, §12)
- 🟥 `RunStats` accumulation + `Lifetime` fold/persist (§9.2)
- 🟥 Stats screen: KPI tiles, leaks-per-wave bars, economy line chart (§9.2)
- 🟥 Hover tooltips: enemy hp/armor/resists, tower stats (§9)
- 🟥 Transient toasts: wave-incoming, not-enough-gold (§9)

### M9 — Audio
- 🟥 `AudioEffect` cues per event, `Audio.interpret` → `AudioEvidence` (§10)
- 🟥 Music loops switch on phase transitions incl. boss variant (§10)
- 🟥 `Audio.setMasterVolume` clamp `[0,1]`, mute toggle (§10)

### M10 — Acceptance & determinism
- 🟥 All 20 acceptance scenarios green (§14)
- 🟥 Seed + input-log replay is bit-identical (§13) — AC #19
- 🟥 Fixed-step + `SpatialGrid` perf budget under 16.7 ms/frame (§13)

### Stretch — deferred (post-v1)
- ⬜ Endless/continuous mode + early-call bonus + leaderboard (§15.1)
- ⬜ Per-tower targeting presets saved across maps + lock-target hotkey (§15.2)
- ⬜ Hero/abilities: player commander or active spells on cooldown (§15.3)
- ⬜ More tower types: Poison Sprayer, Buff/Aura, Tar pit (§15.4)
- ⬜ Maze-building maps as default + destructible blockers + dual lanes (§15.5)
- ⬜ Map editor exporting `Wave`/grid JSON (§15.6)
- ⬜ Enemy abilities: burrowers, splitters, shielders (§15.7)
- ⬜ Meta-progression: branch/map unlocks + daily seeded challenge (§15.8)
- ⬜ Co-op two-warden mode sharing one economy (§15.9)
