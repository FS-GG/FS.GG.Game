---
title: "Breachpoint Tactics"
slug: turn-based-tactics
category: games
complexity: complex
genre: "Turn-based tactics (grid combat, telegraphed enemy intent)"
target_session_minutes: 25
stack: { rendering: "FS.GG.Rendering (Skia/OpenGL)", arch: "Elmish/MVU", lang: "F#" }
status: spec
---

# Breachpoint Tactics

## 1. Overview

**Breachpoint Tactics** is a deterministic, turn-based grid-combat game in the
lineage of *Into the Breach* (the marquee inspiration), *Advance Wars*, and
*Fire Emblem*. The player commands a squad of 3 units on an 8×8 tile board against
a swarm of invading "Breach" enemies. The core verb is **positioning**: every enemy
**telegraphs its intended action at the start of the player phase** (the move it will
take, the tile it will hit, for how much damage), and the player's job is to
out-think that intent — move units out of danger, body-block attacks, and above all
**shove enemies into each other, into hazards, or off objectives** using
knockback. Damage is almost never the point; *manipulation of position* is. A
mission is small (typically 5 enemy waves over a handful of turns) and fully
solvable with perfect information, so the fantasy is that of a chess problem with
explosions: no dice, no hidden rolls, no fog — just you, the board, and a puzzle
that punishes greed and rewards a clean read.

The game is a near-perfect fit for pure Elmish/MVU (see §7): there is **no
real-time simulation tick**. The entire game advances as `update : Msg -> Model ->
Model * Cmd<Msg>`, a pure function of the current state and a player/AI action.
The only place a clock is needed at all is cosmetic animation interpolation, which
is decoupled from the authoritative game state. This makes the rules trivially
testable and deterministic.

## 2. Core Game Loop

**Moment-to-moment (one player turn):**
read telegraphs → select a unit → preview reachable tiles → preview an attack's
effect (damage + knockback) → confirm or undo → repeat for other units → **End
Turn** → watch enemy phase execute the telegraphed actions → read the *new*
telegraphs → repeat.

Formally: `Read intent → Plan → (Select → Preview → Confirm | Undo)* → End Turn →
Resolve enemy phase → Spawn next wave → loop`.

**Session-level:**
`Title → Mission select → Deploy squad → Play mission (turns until objective met or
failed) → Mission result (S/A/B/C grade) → Next mission | Campaign complete |
Retry`. A run is a 4-mission campaign; losing all units or failing the objective ends
the mission (retry from mission start — state is fully re-seeded from the mission's
RNG seed so the board is identical).

## 3. Controls & Input

Keyboard is primary; mouse is fully supported and is the expected primary device for
a grid game. Input is **edge-triggered** (discrete key/click events → `Msg`); there
is no held-key polling because there is no continuous simulation.

| Input | Action | Msg emitted |
|---|---|---|
| Left-click on own unit | Select that unit | `SelectUnit unitId` |
| Left-click on highlighted tile (move range) | Preview move to that tile | `PreviewMove tile` |
| Left-click on enemy/tile while an ability is armed | Preview attack/ability at target | `PreviewAttack (abilityId, target)` |
| Left-click again on previewed destination/target | Confirm the pending action | `ConfirmAction` |
| Right-click / `Esc` | Cancel current preview / deselect | `CancelPreview` |
| `U` / `Ctrl+Z` | Undo last *unconfirmed-or-confirmed-this-turn* action | `Undo` |
| `R` (redo) | Redo an undone action | `Redo` |
| `Tab` | Cycle to next un-acted own unit | `CycleUnit` |
| `1`,`2`,`3` | Select squad unit by slot | `SelectUnit (slot n)` |
| `Q` / `E` | Arm previous / next ability of selected unit | `ArmAbility dir` |
| `Space` / `Enter` | End player phase | `EndTurn` |
| `H` | Toggle threat-overlay (show all telegraphed danger tiles) | `ToggleThreatOverlay` |
| `G` | Toggle grid coordinates | `ToggleGridLabels` |
| `P` / `Esc` (in play) | Pause menu | `OpenPause` |
| Mouse hover over tile | Highlight tile + tooltip (no state mutation; view-only) | *(no Msg; hover is local view state)* |

Mouse hover never mutates `Model`; it updates a `HoverTile` field that lives in the
view layer (or a clearly-marked transient field) so hover can't desync the game.

## 4. Mechanics (detailed)

All coordinates are integer tile coordinates `(col, row)` with origin top-left,
`col ∈ [0,7]`, `row ∈ [0,7]`. The board is **orthogonal 4-connected** for movement
and most attacks (no diagonal movement; some abilities are diagonal/AoE and say so).

### 4.1 The tile grid & terrain

The board is `8×8 = 64` tiles. Each tile has a **terrain type** and optional
**occupant** (one unit max) and optional **hazard/prop**.

| Terrain | Move cost | Passable | Cover | Effect |
|---|---|---|---|---|
| Ground | 1 | yes | 0 | Default. |
| Road | 1 | yes | 0 | Cosmetic; `move cost 0.5` only for the `Mobile` trait (rounds down per-step? no — see note). |
| Rough (rubble) | 2 | yes | +1 | Costs 2 movement points to enter. +1 cover (−1 incoming damage, min 0). |
| Forest | 2 | yes | +2 | Costs 2 MP. +2 cover. Blocks line-of-sight for ranged attacks. |
| Water | — | no (unless `Flying`/`Amphibious`) | 0 | Impassable to ground units. A non-flying unit **pushed into water dies instantly** (drowns). Flyers cross freely. |
| Chasm | — | no (unless `Flying`) | 0 | Impassable. A non-flyer **pushed into a chasm dies instantly**. |
| Mountain/Wall | — | no | — | Fully impassable to everything; blocks movement, push, and LoS. |
| Lava (hazard tile) | — | no | 0 | Impassable; any unit *standing on or pushed onto* lava takes 3 burn damage at start of its owner's phase, then is still on lava (persists). |

Notes on `Road`/`Mobile`: to keep movement-point arithmetic in integers, the v1
rule is simpler — **Road costs 1 like Ground**, and the `Mobile` trait instead grants
`+1 move range` (see roster). Drop the fractional cost. (Recorded here because the
table above tempts a 0.5; v1 forbids fractional MP.)

**Cover** reduces incoming *direct* attack damage by the defender's tile cover value
(Rough +1, Forest +2), to a minimum of 0. Cover does **not** reduce
collision/knockback/environmental damage (§4.6) — only declared attacks.

### 4.2 Units, stats, traits

A unit has: `hp / maxHp`, `moveRange` (movement points per turn), and a list of
**abilities** (each with its own range, damage, knockback, shape, cooldown). Units
also carry **traits** (e.g. `Flying`, `Armored`, `Mobile`, `Massive`). See §5 for the
full roster table.

- A unit may, per turn, **move once and use one ability** (in either order: you can
  attack-then-move only if the unit has the `Hit&Run`/`Mobile` trait; otherwise the
  default is **move-then-act**, and acting ends the unit's turn). v1 default: a unit
  that has acted (used an ability) cannot then move unless `Mobile`.
- A unit that only moves can be re-selected and its move re-planned until **End
  Turn** (everything pre-confirmation is freely undoable; see §4.8).

### 4.3 Turn structure (player phase → enemy phase)

A **round** = one Player Phase followed by one Enemy Phase.

1. **Start of round / Telegraph step.** For each living enemy, the AI (§4.9) computes
   its intended action *now* and stores it as a `Telegraph` (target tile, affected
   tiles, predicted damage, predicted knockback vector). These are rendered as
   overlays. Telegraphs are **locked** for the round — the enemy will execute exactly
   that action on the enemy phase *unless* the player has, by then, killed it, moved
   the would-be target, or repositioned the attacker (in which case a clearly-defined
   resolution rule applies; see §4.5).
2. **Player Phase.** Player acts with each of their units (any order, each unit at
   most one move + one ability). The player can freely preview/undo. Ends on
   `EndTurn`.
3. **Enemy Phase.** Enemies execute their telegraphed actions in a fixed,
   deterministic order: by ascending `enemyId` (spawn order). Each enemy: moves along
   its telegraphed path (if any), then performs its telegraphed attack, resolving
   damage + knockback + environmental chains immediately (§4.6) before the next enemy
   acts.
4. **End of round.** Apply persistent hazards (lava/fire ticks), tick ability
   cooldowns, check spawn schedule (§6), check win/loss (§11). Then go to step 1 for
   the next round.

### 4.4 Movement — pathfinding & reachable-tile highlight

When a unit is selected, the game computes the set of **reachable tiles** given its
`moveRange` movement points and terrain costs, and highlights them.

- Because move costs are small non-negative integers (1 or 2), reachability is a
  **uniform/weighted BFS = Dijkstra on a tiny graph**. Implementation: Dijkstra
  (priority by accumulated cost) from the unit's tile over the 4-neighbour graph,
  expanding while `costSoFar ≤ moveRange`, treating impassable terrain and
  tiles occupied by *other units* as non-traversable. Friendly units block movement
  **through**? v1 rule: you may **path through allied units but not end on them**;
  enemies block both path and destination. (Flying ignores terrain cost — every
  passable-to-flyer tile costs 1 — and ignores ground occupants for *pathing through*
  but still can't end on an occupied tile.)
- Output: `reachable : Map<Tile, {cost:int; cameFrom:Tile option}>`. The keys are the
  highlight set; `cameFrom` reconstructs the path for the move animation.
- **Preview** (`PreviewMove tile`): if `tile ∈ reachable`, store a `Pending.Move`
  with the reconstructed path. The highlight set is rendered cyan; the previewed path
  is drawn as a polyline; the destination shows a ghost of the unit.
- For ranged units the **attack range** is computed separately *from the previewed
  destination* (so the player sees, live, which enemies a planned move would let them
  hit). Attack range = all tiles within `attackRange` Manhattan distance from the
  (previewed) position, minus tiles blocked by LoS-blocking terrain for ranged
  weapons (melee range 1 ignores LoS).

Pseudocode (Dijkstra reachability):

```fsharp
let reachable (board: Board) (unit: Unit) : Map<Tile, int> =
    let start = unit.Pos
    let pq = PriorityQueue()                 // (cost, tile)
    pq.Enqueue(start, 0)
    let best = Dictionary [ start, 0 ]
    while pq.Count > 0 do
        let tile, cost = pq.Dequeue()
        if cost = best.[tile] then            // skip stale entries
            for nb in neighbors4 tile do
                match enterCost board unit nb with
                | Some step ->
                    let nc = cost + step
                    if nc <= unit.MoveRange &&
                       (not (best.ContainsKey nb) || nc < best.[nb]) then
                        best.[nb] <- nc
                        pq.Enqueue(nb, nc)
                | None -> ()                  // impassable / blocked
    best |> Seq.filter (fun kv -> canEndOn board unit kv.Key)
         |> Map.ofSeq
```

### 4.5 Attack resolution & damage

A declared attack from attacker A using ability `ab` against target tile T:

1. **Validity:** T within `ab.range` of A's position (Manhattan for orthogonal,
   Chebyshev for diagonal abilities), LoS clear if `ab.ranged` (Forest/Mountain/Wall
   block; units do **not** block LoS in v1 except `Massive` units, which do).
2. **Hit set:** compute the set of affected tiles from `ab.shape` (Single, Line N,
   Cross, Blast-radius r, etc.) anchored at T.
3. **For each occupied affected tile**, compute `dealt = max(0, ab.damage − coverOf
   defenderTile)` and subtract from defender HP. Friendly fire **is** possible
   (this matters for the puzzle: your own AoE can hit your units). Damage is applied
   simultaneously to all tiles in the hit set (no ordering effects within one attack).
4. **Knockback:** if `ab.knockback > 0`, each hit unit is pushed `ab.knockback` tiles
   directly away from T (or in `ab.pushDir` for directional abilities). Resolve
   knockback per §4.6.
5. **Death check:** any unit at HP ≤ 0 is removed. (Resolve all damage first, then
   remove, so simultaneous mutual kills both die.)

**Telegraph reconciliation** (what happens on the enemy phase if the board changed):
- If the telegraphed **attacker is dead** → action is cancelled.
- If the telegraphed **target tile is now empty** (the player moved the unit away) →
  the attack still *fires at the tile* and hits whatever (if anything) now occupies
  it — including, deliciously, another enemy the player baited into that tile. The
  telegraph shows the *tile*, not the *unit*, precisely so this is fair.
- If the telegraphed **attacker was pushed/moved** by the player, its move-portion is
  cancelled but it still attacks from its *new* position **only if the target tile is
  still in range**; otherwise the whole action fizzles. (This is the key player lever:
  shoving an enemy out of range cancels its attack.)

### 4.6 Push / knockback & environmental damage (the Into-the-Breach hook)

Knockback is the soul of the game. Resolving a push of unit U by `n` tiles in
direction `d`:

```
repeat n times:
  let next = U.pos + d
  match next with
  | off-board OR impassable terrain (wall/mountain) OR occupied by unit V ->
        // U is blocked. U takes 1 COLLISION damage.
        // If blocked by a unit V, V ALSO takes 1 collision damage.
        // Push stops here (U does not enter `next`).
        stop
  | water/chasm (and U is not Flying) ->
        // U moves onto it and DIES (drown/fall). Stop.
        U.pos <- next ; kill U ; stop
  | passable & empty ->
        U.pos <- next            // U slides one tile
  | lava ->
        U.pos <- next ; U.hp -= 3   // enters and burns; continue if n remains
continue
```

Rules:
- **Collision damage is 1** to each participant (the shoved unit and the unit/wall it
  hits — walls take none). This is independent of cover.
- A chain is possible: if U is pushed into V, U stops and both take 1; V is **not**
  further pushed (knockback does not propagate in v1 — keep it deterministic and
  simple). *(Stretch §15 adds chain-pushing.)*
- Pushing an enemy onto a hazard (water/chasm = instant kill; lava = 3 dmg/round) is
  the primary "free kill" pattern.
- **Flying** units ignore water/chasm when pushed (they hover) but still take
  collision damage from walls/units and still die if pushed off-board.
- **Massive** units cannot be pushed at all (knockback = 0 against them) but they
  deal/receive collision damage if *they* are the wall someone else is shoved into.
- **Armored** trait: reduces declared-attack damage by 1 (like permanent cover) but
  does **not** reduce collision/environmental damage.

### 4.7 Objectives & the "protect the grid" layer

Beyond killing enemies, missions usually have a **protect objective**: there are
`Buildings` (special props occupying tiles, with their own HP pool shared as a
**Grid Power** meter, default 7). If an enemy's attack would hit a building tile (and
the player failed to block it), the building takes the damage and Grid Power drops by
that amount. **Grid Power reaching 0 = mission loss** (see §11). This reframes combat:
you often *don't* need to kill the enemy, you need to make its telegraphed attack miss
the building — by body-blocking, by shoving the enemy, or by emptying the target tile.

### 4.8 Undo of unconfirmed (and this-turn) moves

The player phase maintains an **action history stack** for the current turn.

- Every previewed-then-confirmed action pushes an `(actorId, before, after)` snapshot.
- `Undo` pops the last action and restores `before` (unit position/HP/hasMoved/hasActed/cooldowns). It moves the entry to a **redo stack**.
- `Redo` re-applies.
- Pure previews (not yet confirmed) are just transient `Pending` state and are
  discarded by `CancelPreview` without touching the history stack.
- **End Turn clears both stacks** — you cannot undo across the enemy phase (enemy
  phase is committed and irreversible by design; this keeps the puzzle honest).
- Because everything is deterministic and the model is immutable, "undo" is literally
  "restore the previous `Model` snapshot." The simplest correct implementation keeps a
  `List<Model>` of pre-action snapshots for the current turn and pops it. (This is
  cheap: the board is 64 tiles + ≤ ~12 units.)

### 4.9 AI: target & position selection + telegraph generation

At the telegraph step, each enemy independently chooses its action via a small,
deterministic scoring search. AI is **greedy with full board knowledge** but does
**not** simulate the player's response (it telegraphs, the player counters — that's
the game).

For an enemy E:
1. Enumerate candidate **positions** = E's reachable tiles (its move range) ∪ {stay}.
2. For each candidate position, enumerate candidate **attacks** (each ability × each
   valid target tile in range from that position).
3. Score each `(position, attack)` plan:
   - `+ damage dealt to player units` (weighted ×3)
   - `+ damage dealt to a Building / Grid Power` (weighted ×5 — enemies prefer to
     wreck the grid; this is the threat the player must answer)
   - `+ killing blow bonus` (+10 if it reduces a player unit to ≤ 0)
   - `− self-exposure` (small penalty for ending on a hazard-adjacent or low-cover
     tile; minor, so AI is aggressive)
   - tie-break deterministically by `(highest score, lowest targetTile index, lowest
     position index)`.
4. The top-scoring plan becomes E's `Telegraph`. If no attack is possible from any
   position, the enemy telegraphs a **move toward the nearest objective/player**
   (BFS shortest path, advance as far as move range allows) with no attack.

Enemy archetypes vary the weights/abilities (e.g., a "Bomber" weights Building damage
even higher; a "Hunter" weights killing player units). AI is pure:
`computeTelegraph : Board -> Enemy -> Telegraph`.

## 5. Entities / Game Objects

### 5.1 Player squad roster (deploy 3 of these per mission)

| Unit | HP | Move | Ability | Atk range | Dmg | Knockback | Notes / traits |
|---|---|---|---|---|---|---|---|
| **Vanguard (Mech)** | 4 | 3 | *Ram* (melee) | 1 | 1 | **2** (away) | Tanky bruiser. Pushes 2 — the workhorse for shoving enemies into hazards. `Armored`. |
| **Artillery (Mech)** | 2 | 2 | *Mortar* (ranged, arcs over walls — **ignores LoS**) | 2–5 (min 2, can't hit adjacent) | 1 | **1** (away from blast center, all 4 neighbours of target) | AoE: hits target + adjacent? v1: target tile only deals dmg; the 4 orthogonal neighbours of target get **knockback only** (pushed outward). Great for scattering clumps. |
| **Skirmisher (Mech)** | 3 | 4 | *Dash-Strike* (melee) | 1 | 2 | 1 (away) | `Mobile` (may move-after-attack), `+1 move from Mobile already counted`. High mobility flanker. |
| **Hornet (Flyer)** | 2 | 4 | *Strafe* (line 2, ranged) | 1–2 in a straight line | 1 | 0 | `Flying` (crosses water/chasm, ignores terrain cost). Hits up to 2 tiles in a line. |
| **Pulsar (Support)** | 3 | 3 | *Repulse* (no damage, knockback only — all 8 surrounding tiles pushed 1 away) | self/adjacent | 0 | **1** (radial) | Defensive: shove a swarm off your buildings/units. Also *Shield* (alt ability): give an adjacent ally `Armored` for 1 round. Cooldown 2. |

Movement and ability defaults assume `move-then-act` unless the unit has `Mobile`.

### 5.2 Enemy roster

| Enemy | HP | Move | Attack (telegraphed) | Range | Dmg | Knockback | Trait / behavior |
|---|---|---|---|---|---|---|---|
| **Crawler** | 1 | 3 | Bite | 1 (melee) | 1 | 0 | Cheap swarm. Hunter weighting. |
| **Spitter** | 2 | 2 | Acid Lob (ranged) | 2–4 | 1 | 0 | Stays back; prefers Building targets. |
| **Bruiser** | 3 | 2 | Slam | 1 (melee) | 2 | 1 (toward enemy) — *pulls? no, pushes player away* | Tanky front-liner. |
| **Bomber** | 2 | 3 | Detonate (blast r=1 at target tile + neighbours) | 2 | 1 (AoE) | 1 (radial) | Building-weight ×8. The grid's nemesis. |
| **Leaper (Flyer)** | 2 | 5 | Pounce (melee after long move) | 1 | 2 | 0 | `Flying`. Picks off backline units. |
| **Behemoth** | 5 | 1 | Stomp (cross shape) | 1 | 2 | 1 (radial) | `Massive` (immovable; blocks LoS). Mini-boss; mission 4. |

### 5.3 Props / hazards

| Object | Tiles | HP | Behavior |
|---|---|---|---|
| **Building** | 1 each | shared **Grid Power** pool (default 7) | Takes telegraphed enemy damage that lands on its tile. Not an obstacle to movement? v1: buildings are **impassable obstacles** AND damageable. |
| **Spawn vent** | 1 | ∞ | Marks a tile where next wave spawns; telegraphed one round ahead with a "spawn warning" overlay. A unit standing on a vent when it spawns deals/takes nothing but **blocks the spawn** (the enemy is delayed a round — a tactical option). |
| **Lava / Water / Chasm** | varies | — | Terrain hazards per §4.1/§4.6. |

### 5.4 F# type sketch

```fsharp
type Tile = { Col: int; Row: int }                     // value type, 0..7

type Terrain = Ground | Rough | Forest | Water | Chasm | Wall | Lava

type Trait = Flying | Armored | Mobile | Massive | Amphibious

type Shape =
    | Single
    | LineOf of int                 // straight line length from origin in facing dir
    | Cross                         // 4 orthogonal neighbours of target
    | Blast of radius:int           // Chebyshev radius
    | Radial                        // all 8 around source (for Repulse)

type Ability =
    { Id: string; Name: string
      MinRange: int; MaxRange: int
      Ranged: bool                  // true => obeys LoS, false => melee
      Damage: int
      Knockback: int
      Shape: Shape
      Cooldown: int                 // 0 = every turn
      CurrentCd: int }

type Faction = Player | Enemy

type Unit =
    { Id: int
      Faction: Faction
      Kind: string                  // "Vanguard", "Crawler", ...
      Pos: Tile
      Hp: int; MaxHp: int
      MoveRange: int
      Abilities: Ability list
      Traits: Set<Trait>
      HasMoved: bool; HasActed: bool }

type Telegraph =
    { EnemyId: int
      MoveTo: Tile option           // where the enemy will move first
      AttackFrom: Tile              // position it attacks from (= MoveTo or current)
      TargetTile: Tile
      AffectedTiles: Tile list      // for the overlay
      PredDamage: int
      PushDir: (int*int) option
      AbilityId: string }
```

## 6. World / Levels / Progression

The **render surface** is the FS.GG default `1280×720` logical px. The board is drawn
centered: `tileSize = 72 px`, so an `8×8` board is `576×576 px`, centered with the
remaining space used for HUD (left panel ~ 300 px for unit cards, right/top for
objective + Grid Power + turn banner).

**Mission structure (one mission):**
- A fixed `8×8` terrain layout (hand-authored per mission, stored as a `char[8][8]`
  legend, e.g. `'.'`=Ground, `'~'`=Water, `'#'`=Wall, `'^'`=Mountain, `'F'`=Forest,
  `'r'`=Rough, `'L'`=Lava, `'B'`=Building, `'v'`=spawn vent).
- A **wave schedule**: a map `round → list of (enemyKind, spawnVent)`. Default
  mission = 5 rounds, 2 enemies spawn per round (rounds 1–4), survive/clear by round 5.
- **Win**: defined per mission (§11) — usually "survive N rounds with Grid Power > 0"
  or "no enemies alive AND Grid Power > 0."

**Campaign (4 missions), difficulty ramp:**

| Mission | Board theme | New element | Enemy pool | Grid Power | Rounds |
|---|---|---|---|---|---|
| 1 — "First Contact" | Open ground, 1 chasm strip | teaches push-into-chasm | Crawler, Bruiser | 7 | 4 |
| 2 — "Floodworks" | Water channels | drown via knockback | + Spitter, Bomber | 7 | 5 |
| 3 — "The Foundry" | Lava veins + walls | lava ticks, LoS blocking | + Leaper | 6 | 5 |
| 4 — "Breachpoint" | Mixed, dense buildings | `Massive` Behemoth boss | all incl. Behemoth | 8 | 6 |

What ramps over the campaign: more simultaneous enemies (2 → 3 per wave), tougher
archetypes, more hazards available to weaponize, and a tighter Grid Power budget.

## 7. State Model (Elmish/MVU)

This genre is the cleanest possible case for Elmish: **`update` is a pure function of
`(action, state)` and needs no real-time tick** to advance the *rules*. The only
clock is for animation interpolation, which is intentionally separated from the
authoritative game state so tests never need a frame loop.

### Model

```fsharp
type Phase =
    | Title
    | Deploy
    | PlayerPhase
    | EnemyPhase of queue: int list          // remaining enemy ids to resolve
    | MissionResult of won:bool * grade:char
    | Paused of resume:Phase

type Pending =                                // the unconfirmed preview, if any
    | NoPending
    | PendingMove of unitId:int * path:Tile list * dest:Tile
    | PendingAttack of unitId:int * abilityId:string * target:Tile
                       * hitPreview:(Tile * int) list      // tile, predicted dmg
                       * pushPreview:(int * Tile * Tile) list  // unitId, from, to

type Model =
    { Phase: Phase
      Board: Map<Tile, Terrain>
      Buildings: Set<Tile>
      Units: Map<int, Unit>                  // both factions, keyed by id
      Telegraphs: Map<int, Telegraph>        // enemyId -> intent for THIS round
      SelectedUnit: int option
      ArmedAbility: string option
      Reachable: Map<Tile, int>              // cached highlight for SelectedUnit
      Pending: Pending
      GridPower: int
      Round: int
      WaveSchedule: Map<int, (string * Tile) list>
      MissionId: int
      Rng: System.Random                     // seeded per mission; deterministic
      History: Model list                    // undo stack (this turn only)
      Redo: Model list
      // ---- view-only / transient ----
      HoverTile: Tile option
      ShowThreatOverlay: bool
      Anim: AnimState }                       // cosmetic; not authoritative
```

### Msg (DU)

```fsharp
type Msg =
    // selection & preview
    | SelectUnit of int
    | CycleUnit
    | ArmAbility of string                    // ability id
    | PreviewMove of Tile
    | PreviewAttack of abilityId:string * target:Tile
    | CancelPreview
    | ConfirmAction
    // history
    | Undo
    | Redo
    // turn flow
    | EndTurn
    | ResolveNextEnemy                         // drives EnemyPhase one enemy at a time
    | StartRound                               // telegraph step + spawns
    // meta
    | ToggleThreatOverlay
    | ToggleGridLabels
    | Hover of Tile option                     // view-only
    | OpenPause | ClosePause | RestartMission | NextMission | NewGame
    // animation only (NOT used to advance rules)
    | AnimTick of dt:float
```

### update — key transitions

- `SelectUnit id` (PlayerPhase): if it's a friendly un-acted unit, set
  `SelectedUnit`, recompute `Reachable` via Dijkstra (§4.4), clear `Pending`.
- `ArmAbility ab`: set `ArmedAbility`; recompute the attack-range overlay from the
  selected unit's current-or-previewed position.
- `PreviewMove tile`: if `tile ∈ Reachable`, set `Pending = PendingMove(...)` with
  reconstructed path (purely cosmetic until confirmed). Recompute attack ranges from
  `dest`.
- `PreviewAttack (ab,target)`: validate range/LoS; compute `hitPreview` (damage after
  cover) and `pushPreview` (resolve §4.6 *on a copy* to show where things land). Store
  as `PendingAttack`. **No mutation of real units yet.**
- `ConfirmAction`: **push current `Model` onto `History`** (clear `Redo`), then apply
  the `Pending` to `Units`/`GridPower` for real, set `HasMoved`/`HasActed`, clear
  `Pending`. Recompute selection state.
- `Undo`: if `History` non-empty, `Redo := current :: Redo`, `Model := head History`,
  `History := tail`. (Whole-state snapshot swap.)
- `Redo`: symmetric.
- `EndTurn`: clear `History`/`Redo`/`Pending`/selection; set `Phase = EnemyPhase
  (sortedEnemyIds)`; issue `Cmd.ofMsg ResolveNextEnemy`.
- `ResolveNextEnemy`: pop the head enemy id from the queue, look up its `Telegraph`,
  reconcile (§4.5), apply move + attack + knockback + env chains to `Units`/`GridPower`
  (all pure). Emit an `Anim` request (cosmetic) and `Cmd.ofMsg ResolveNextEnemy` for
  the next; when queue empty → `StartRound`.
- `StartRound`: `Round += 1`; apply hazard ticks; spawn this round's wave (blocked
  vents delay); tick cooldowns; **recompute all `Telegraphs`** (§4.9); check win/loss
  (§11) → `MissionResult` if decided; else `Phase = PlayerPhase`, reset every unit's
  `HasMoved/HasActed`.
- `AnimTick dt`: advances `Anim` interpolation **only**; returns `Model` with rules
  state untouched. The view reads `Anim` to lerp sprite positions; if no animation is
  in flight this is a no-op.

### view

`view` is pure: it renders the board, terrain, buildings, units (at their authoritative
tiles, offset by `Anim` lerp), highlight sets (`Reachable`, attack range), the
`Pending` ghost/path, **all enemy `Telegraphs` as danger overlays**, and the HUD.
Skia performs the actual drawing (§8). The view emits `Msg` from clicks/keys.

### Subscriptions

- **No game-logic tick.** The authoritative simulation never needs a timer.
- A single optional **animation subscription** at ~60 FPS emits `AnimTick dt` *only
  while an animation is in flight* (e.g., during the enemy phase, to slide units).
  When `Anim` is idle, the subscription can be detached entirely.
- Input subscription: keyboard/mouse events → the input `Msg`s above (edge-triggered).

The payoff: every rule in §4 is exercisable by feeding `Msg`s to `update` with **no
clock**, which is exactly what the acceptance tests in §14 do.

## 8. Rendering (Skia 2D)

Coordinate system: logical `1280×720`; board top-left at `(boardX, boardY) = (352,
72)`; `tileSize = 72`. Tile `(col,row)` → pixel rect `(boardX + col*72, boardY +
row*72, 72, 72)`.

**Draw order (back to front):**
1. **Background** — solid `#0E1116` fill.
2. **Terrain tiles** — fill each tile by type:
   Ground `#2B313B`, Rough `#3A3530`, Forest `#1F3A2A`, Water `#15406B`,
   Chasm `#000000` (with subtle inner shadow), Wall/Mountain `#4A4F57`,
   Lava `#A1300F` (animated glow via `AnimTick` modulating alpha 0.8–1.0).
   Grid lines `#000000 @ 30%`, 1 px.
3. **Buildings** — `#C8B560` rounded rect inset 8 px, with a small Grid-Power pip.
4. **Highlights (player phase):** reachable-move set fill `#1E90FF @ 35%`; attack
   range outline `#FF5A3C @ 60%`, 2 px; previewed path polyline `#7FE0FF`, 3 px;
   destination ghost unit at `@ 45%` alpha.
5. **Telegraph / danger overlays:** every affected tile of every enemy `Telegraph`
   gets a hatched red fill `#E23B3B @ 30%` plus a directional **push arrow**
   (`#FFD34D`) showing knockback, and a small numeric damage badge. If
   `ShowThreatOverlay` is off, show only telegraphs for the hovered/selected enemy.
6. **Units** — drawn as a colored rounded-rect token (player `#39C16C`, enemy
   `#E23B3B`) with a 36 px glyph for kind, a small HP pip row (filled = current),
   and trait icons. Position lerped by `Anim` during movement.
7. **Selection ring** — `#FFFFFF`, 3 px, around `SelectedUnit`.
8. **Floating combat text & particles** — damage numbers rise + fade; push impacts
   emit a 6-particle spark; deaths emit a dissolve. All driven by `Anim`, never
   authoritative.
9. **HUD** (§9) on top.

Fonts: a clean sans (e.g. `Inter`/`Roboto`), `14 px` body, `28 px` banners. Numbers
right-aligned in cards.

**Redraw strategy:** redraw on every `Msg` that changes the model **and** every
`AnimTick` while animating. When idle (mid player-phase, no animation), the scene is
static — the renderer may early-out and reuse the last frame. Skia draws into the
FS.GG surface; no partial/dirty-rect optimization needed at this entity count.

## 9. UI / HUD / Screens

**Screens:** Title → Mission Select (campaign map of 4 nodes) → Deploy (pick/place 3
units on player spawn tiles) → Play → Mission Result → (Next / Retry / Title). Pause
overlay accessible from Play.

**HUD (during Play):**
- **Top banner (center):** current phase ("PLAYER PHASE" `#39C16C` / "ENEMY PHASE"
  `#E23B3B`), `Round X / N`.
- **Top-right:** **Grid Power** meter — `▢▢▢▢▢▢▢` pips, lit `#C8B560`, drained
  `#3A3A3A`; flashes when it drops. Plus the objective text (e.g. "Survive 2 more
  rounds").
- **Left panel (300 px):** three **unit cards** (HP bar, move/attack, ability list
  with armed highlight + cooldown). Greyed when the unit has acted; check-mark when
  done. Click a card = select.
- **Bottom-center:** context action bar — buttons for `End Turn` (and `Undo`/`Redo`
  with stack-depth count), plus the current selection's hint ("Click a blue tile to
  move, an orange tile to attack").
- **Tile tooltip** on hover: terrain name, move cost, cover, occupant stats, and — if
  the tile is in a telegraph — "INCOMING: 2 dmg + push →".
- **Threat toggle (`H`)** indicator.

**Mission Result:** win/lose, turns taken, Grid Power remaining, units lost, and a
letter grade (§11), with `Next`/`Retry`.

### 9.1 Menu system (detailed)
Every non-play screen (Title, Settings, Stats, Pause, Mission Result) is driven by a single
**menu stack** of vertical rows with a cursor, so one small update handler serves them all
and navigation is identical everywhere. The board's mouse-first input (§3) is unchanged;
the menu overlay is keyboard-driven.

**Menu tree**
```
Title ─┬─ Continue Campaign ─ resume at the highest unlocked mission (§6/§13)
       ├─ New Campaign ────── fresh 4-mission run from Mission 1 (`NewGame`)
       ├─ Load ────────────── pick a saved campaign slot (§13)
       ├─ Stats ───────────── Stats & Charts screen (§9.2)
       ├─ Settings ────────┬─ Difficulty        ◄ Recruit · Veteran · Commander ►
       │                   ├─ Master volume     ◄ 0 – 100 ►
       │                   ├─ Sound             ◄ On · Off ►
       │                   ├─ Window scale      ◄ 1× · 2× · Fit ►
       │                   ├─ Grid coordinates  ◄ On · Off ►   (→ §3 `ToggleGridLabels`)
       │                   └─ Back
       └─ Quit

Pause (from Play, §3 `OpenPause`) ─┬─ Resume
                                   ├─ Restart Mission ─ (`RestartMission`, §7)
                                   ├─ Threat overlay   ◄ On · Off ►  (→ §3 `ToggleThreatOverlay`)
                                   ├─ Settings ──────── (same submenu; returns to Pause)
                                   └─ Quit to Title

Mission Result ─┬─ Next Mission ── on Victory (`NextMission`); becomes Retry Mission on Defeat
                ├─ Restart Mission ─ (`RestartMission`; re-seeds the board, §2/§13)
                ├─ View Stats ──── Stats & Charts (§9.2)
                └─ Title
```

**Navigation model**
- `MenuCursor: int` on the active menu; `↑`/`↓` move it and **wrap** top↔bottom.
- `Enter`/`Space` activates the current row; `Esc`/`Back` pops the stack (**Back**) — in
  Play, `Esc` still opens Pause (§3), and inside a menu it steps back one level.
- **Cycler rows** (Difficulty, Master volume, Sound, Window scale, Grid coordinates, Threat
  overlay): `←`/`→` change the value **in place**; the row shows a right-aligned `◄ value ►`
  widget. The top-of-stack menu is the only one that receives input.
- Rendering reuses the §9 HUD style (panels + inverted selected row); the danger/telegraph
  overlays underneath are frozen while a menu is open.

**Msg additions** (extend the §7 `Msg` DU):
```fsharp
    | MenuUp | MenuDown              // move cursor (wraps)
    | MenuAdjust of dir:int          // -1 / +1 on a cycler row (←/→)
    | MenuActivate                   // Enter/Space on the current row
    | MenuBack                       // Esc — pop the menu stack
    | OpenStats | CloseStats         // enter / leave the Stats screen (§9.2)
```

Settings **apply live and persist** to local config (§13). **Difficulty** selects the §12
tunable preset — *Recruit* eases `gridPowerStart` / `enemiesPerWave` / the AI weights
(`aiBuildingWeight`, `aiKillBonus`), *Veteran* is the §12 defaults, *Commander* tightens
them; **Master volume**/**Sound** route to `Audio.setMasterVolume` (§10, clamped `[0,1]`,
`0` mutes); **Grid coordinates** and **Threat overlay** drive the §3/§7 `ToggleGridLabels`
and `ToggleThreatOverlay` flags directly.

### 9.2 Stats & charts screen
The Stats screen visualizes **the last mission** and **lifetime/campaign** play. It reads a
`Stats` snapshot — never the live board — so it is a pure, deterministic render reachable
from Title, Pause, and Mission Result. Chart-design choices follow the project dataviz
conventions (form-first, single axis, identity by entity, a validated colorblind-safe hue).

**Tracked per mission** — `MissionStats`, accumulated in `ConfirmAction` /
`ResolveNextEnemy` / `StartRound`, snapshotted when `Phase = MissionResult` (§7):

| Field | Type | Updated |
|-------|------|---------|
| `won` | `bool` | set at `MissionResult` (§11) |
| `roundsTaken` | `int` | `Model.Round` at mission end (via `StartRound`) |
| `unitsLost` | `int` | ++ when a player `Unit` is removed (§4.5) |
| `enemyKills` | `Map<string,int>` | ++ by enemy `Kind` (Crawler…Behemoth, §5.2) on each death |
| `pushKills` | `int` | ++ on a knockback / hazard kill (§4.6) — the signature verb |
| `damageDealt` | `int` | += declared + collision damage inflicted on enemies (§4.5/§4.6) |
| `damageTaken` | `int` | += damage suffered by player units (declared + collision + env) |
| `gridPowerRemaining` | `int` | `Model.GridPower` at mission end (§4.7) |
| `grade` | `char` | S/A/B/C from the §11 score |

**Lifetime / campaign** — `LifetimeStats`, persisted (§13) alongside the highest-mission /
best-grade save: `missionsWon`, `missionsPlayed` (→ `winRate`), `unitsLostTotal`,
`pushKillsTotal`, `avgRoundsPerWin`.

**Layout** (logical 1280×720): a KPI tile row across the top, two charts below.

```
┌────────────────────────── COMBAT RECORD ──────────────────────────┐
│  ┌ MISSIONS ┐ ┌ WIN %  ┐ ┌ UNITS LOST ┐ ┌ AVG ROUNDS ┐            │  ← KPI stat tiles
│  │   WON    │ │        │ │            │ │            │            │
│  │    18    │ │  72 %  │ │     11     │ │    4.6     │            │
│  └──────────┘ └────────┘ └────────────┘ └────────────┘            │
│                                                                    │
│  Kills by enemy type               Damage dealt vs taken           │
│  ▇▇                            60 ┤             ╭── Dealt          │
│  ▇▇      ▇▇                       │         ╭─╯╭─╯                 │
│  ▇▇  ▇▇  ▇▇  ▇▇                   │     ╭─╯╭─╯───── Taken          │
│  ▇▇  ▇▇  ▇▇  ▇▇  ▇▇             0 ┼───────────────────► mission #  │
│  Crw Spt Brs Bmb Lpr              1    2    3    4                 │
└────────────────────────────────────────────────────────────────────┘
      ↑/↓ scope:  ▸ This Mission · Lifetime            ESC — Back
```

**Charts** (rendered in Skia with the same draw-list discipline as §8):

1. **Kills by enemy type** — *form: per-category magnitude → bars.* x = enemy archetype
   (one bar per `Kind` in the §5.2 roster: Crawler, Spitter, Bruiser, Bomber, Leaper,
   Behemoth), y = kills. **Single series**, so one hue and no legend. Bars are 4 px-rounded
   at the data end with a 2 px surface gap between them. Fill `#2a78d6` (light) / `#3987e5`
   (dark) — validated categorical slot 1. Comparing magnitudes across categories stays one
   hue (never a rainbow).
2. **Damage dealt vs taken** — *form: change over an ordered index → line.* x = mission
   number (Missions 1–4 of the current campaign in *This Mission* scope; the most-recent
   missions in *Lifetime*), y = damage. **Two series** (Dealt, Taken) → a legend is present
   and both lines are direct-labeled at their right end ("Dealt"/"Taken"). Dealt `#2a78d6`,
   Taken `#1baf7a` (slots 1–2, adjacent-pair CVD-validated). 2 px lines, ≥ 8 px end markers,
   recessive 1 px gridlines in `#3C3C3C`.

Conventions honored: **color follows the entity** (Dealt is always slot 1, Taken always
slot 2 — never repainted by the scope toggle); **one axis only** (no dual-scale — damage
dealt and taken share the single y); chart **text uses ink tokens** (`#FFFFFF` primary /
`#C3C2B7` muted), never a series hue; layout is **fixed and deterministic**, so a fixed-seed
campaign (§12/§13 has no combat RNG) renders byte-identical for snapshot tests. The `↑/↓`
**scope** toggle swaps the data source This-Mission ↔ Lifetime without changing colors.

**Model/Msg hooks:** add `Stats: MissionStats` and `Lifetime: LifetimeStats` to the §7
`Model`; accumulate them in `ConfirmAction`/`ResolveNextEnemy` (`damageDealt`/`damageTaken`,
`enemyKills`, `pushKills`) and in `StartRound` (`roundsTaken` from `Round`); when the mission
resolves to `MissionResult`, fold `MissionStats` into `Lifetime` and persist (§13). Add a
`StatsScreen of scope:StatScope * return:Phase` case to the §7 `Phase` DU; `OpenStats`/
`CloseStats` push/pop it (the render is a no-op on board state).

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
| `SelectUnit` (§3) resolves | `Audio.playSfx` | `select-unit` | soft tick |
| `ConfirmAction` of a move (§4.8) | `Audio.playSfx` | `move-confirm` | mechanical step/whoosh |
| `ConfirmAction` of a melee attack (§4.5) | `Audio.playSfx` | `attack-melee` | heavy thud |
| `ConfirmAction` of a ranged/mortar attack (§4.5) | `Audio.playSfx` | `attack-ranged` | launch + impact |
| Knockback collision resolves (§4.6) | `Audio.playSfx` | `knockback-impact` | metallic clang |
| Push into water/chasm kills a unit (§4.6) | `Audio.playSfx` | `hazard-kill` | splash / fading fall |
| Lava burn tick at end of round (§4.3) | `Audio.playSfx` | `lava-burn` | sizzle |
| Building hit / Grid Power drop (§4.7) | `Audio.playSfx` | `building-hit` | alarm blip |
| Telegraphs generated at `StartRound` (§4.9) | `Audio.playSfx` | `telegraph-appear` | low ominous sting (once per round) |
| Enemy phase begins (§4.3) | `Audio.playSfx` | `enemy-phase-begin` | drum hit |
| Player unit death resolves (§4.5) | `Audio.playSfx` | `unit-death` | sharp negative |
| `MissionResult` reached (§11) | `Audio.playSfx` | `mission-end` | victory chord / failure drone |

Ambient music follows the phase: `StartRound`/`EndTurn` transitions request
`Audio.playMusic (TrackId "player-phase-bed") true` for the low-tension Player-Phase bed,
`Audio.playMusic (TrackId "enemy-phase") true` for the faster percussive Enemy-Phase cue,
and a calm `Audio.playMusic (TrackId "title-loop") true` on Title/Deploy — each preceded by
`Audio.stopMusic` on the phase change. A mute/settings toggle maps to
`Audio.setMasterVolume` (`0.0` mutes). **Testing:** collect the frame's
`AudioEffect`s, `Audio.interpret` them, and assert the `AudioEvidence.Requested` sequence for
representative events (e.g. confirming a Vanguard *Ram* that shoves a Bruiser into water
requests exactly `PlaySfx (SoundId "hazard-kill", _)`).

## 11. Win / Loss / Scoring

**Per-mission win condition** (varies, set per mission):
- *Survival* (missions 1–2): reach end of `Round N` with `GridPower > 0` and ≥ 1
  player unit alive.
- *Elimination* (missions 3–4): no enemies alive at end of any round, **and**
  `GridPower > 0`.

**Loss conditions (any):**
- `GridPower ≤ 0` (the grid is destroyed) → immediate loss.
- All 3 player units dead → immediate loss.

**Scoring / grade (per mission):** start at base, compute a score, map to a letter.
```
score = 100
      + 10 * GridPowerRemaining
      + 15 * playerUnitsAlive
      + 20 * (enemiesKilled - enemiesKilledByDirectDamageOnly)   // bonus for env/push kills
      - 5  * roundsTaken
grade = S if score >= 220 | A if >= 180 | B if >= 140 | C otherwise
```
The grade rewards the intended style: **preserve the grid, lose no units, and kill via
the environment (push into hazards) rather than slow direct damage.** No lives/continues
— `Retry` restarts the mission from its deterministic seed (identical board).

## 12. Difficulty & Balancing

All combat constants live in a data table so balance is data-driven (no code change to
tune). Defaults below; ranges are sane tuning bounds.

| Param | Default | Range | Effect |
|---|---|---|---|
| `tileSize` (px) | 72 | 48–96 | Render scale only. |
| `boardSize` | 8 | 6–10 | Bigger = more positioning room. |
| `gridPowerStart` | 7 | 4–12 | Loss budget; lower = harder. |
| `collisionDamage` | 1 | 0–2 | Push-into-wall/unit damage. |
| `lavaTickDamage` | 3 | 1–4 | Per-round lava burn. |
| `coverRough` / `coverForest` | 1 / 2 | 0–3 | Incoming declared-damage reduction. |
| `enemiesPerWave` | 2 | 1–4 | Spawn pressure. |
| `aiBuildingWeight` | 5 | 1–10 | How much enemies favor wrecking the grid. |
| `aiDamageWeight` | 3 | 1–10 | How much enemies favor hurting units. |
| `aiKillBonus` | 10 | 0–20 | Aggression toward finishing kills. |
| `vanguardPush` | 2 | 1–3 | Core push lever. |
| `moveRange` (per unit) | see §5 | ±2 | Mobility. |
| `mortarMinRange` | 2 | 1–3 | Artillery dead-zone. |

**Determinism:** there are **no random combat rolls** at all — damage, knockback, and
AI choice are fully deterministic. RNG is used *only* for cosmetic particle jitter and
(optionally) for procedural mission seeds; the authoritative `Rng` is seeded per
mission so any mission is reproducible bit-for-bit. This makes balance testing exact.

## 13. Technical Notes

- **Performance:** trivial. Worst case ~ 3 player + ~12 enemy units + 64 tiles. Each
  Dijkstra reachability is over ≤ 64 nodes. AI telegraph search is `positions(≤~25) ×
  abilities(≤2) × targets(≤64)` ≈ a few thousand cheap scorings per enemy per round —
  sub-millisecond. Frame budget `16.7 ms` is never threatened; the screen is static
  most of the time.
- **Timestep:** the rules use **no timestep** (event-driven, pure `update`). Animation
  uses a **variable timestep** `AnimTick dt` purely to lerp sprite positions and fade
  text; capping `dt` at `0.05 s` avoids teleport on stalls. Authoritative state is
  always at the "settled" position regardless of animation progress.
- **Determinism / RNG:** seed `Rng` per `(missionId)`; never call it inside
  `update`'s rule logic. Enemy resolution order is sorted by id. This guarantees the
  same inputs → same board, which §14 relies on.
- **Persistence:** save campaign progress (highest mission unlocked, best grade per
  mission) to local storage as JSON. No mid-mission save needed (missions are short).
- **Edge cases to handle explicitly:**
  - Pushing a unit off the board edge = it hits the "wall" → 1 collision damage, stays
    on the last in-bounds tile.
  - Two units would be pushed into the same tile in one attack: resolve in id order;
    the second is blocked by the first (collision damage to both).
  - Telegraph target tile becomes a *friendly* unit (player baited an ally there): the
    enemy still hits it — friendly-to-player damage is the player's mistake.
  - A spawn vent occupied by a unit: spawn is delayed one round; if still occupied,
    keeps delaying (a legitimate stall tactic, but the unit can't act elsewhere).
  - Undo stack across End Turn: cleared (enemy phase is irreversible).
  - Mutual lethal damage in one attack: both removed (resolve damage, then deaths).
  - Massive unit as a push wall: pusher's victim stops against it; both take collision
    damage; Massive itself is never displaced.

## 14. Acceptance Criteria (test scenarios)

All scenarios are written against pure `update`/helper functions — **no clock
required**. Boards are given as legends; coordinates are `(col,row)`.

1. **Reachable-tile range (Dijkstra over terrain).**
   *Given* a Skirmisher (`moveRange = 4`) at `(0,0)` on otherwise-Ground board with
   `Rough` (cost 2) at `(2,0)`,
   *When* it is selected,
   *Then* `Reachable` contains `(2,0)` with cost 3 (`1 + 2` for the rough tile) and
   `(3,0)` with cost 4 (cheapest, through the rough tile), **does not contain** `(4,0)`
   (its cheapest path costs 5, exceeding `moveRange = 4`), and contains no tile whose
   cheapest path cost exceeds 4. Tiles occupied by enemies are excluded from both path
   and destination.

2. **Move preview is non-destructive; confirm mutates; undo restores.**
   *Given* a unit at `(1,1)` and `PreviewMove (3,1)` issued (valid),
   *When* the model is inspected,
   *Then* the unit is **still at `(1,1)`** and `Pending = PendingMove`.
   *When* `ConfirmAction` then `Undo` are issued,
   *Then* the unit is back at `(1,1)`, `HasMoved = false`, and `History` is empty.

3. **Attack damage with cover.**
   *Given* a Vanguard (*Ram*, damage 1) adjacent to a Crawler (HP 1) standing on
   `Ground`,
   *When* the attack is confirmed,
   *Then* the Crawler takes `max(0, 1 − 0) = 1` and dies.
   *Given* instead the Crawler stands on `Forest` (cover +2),
   *Then* it takes `max(0, 1 − 2) = 0` and **survives** (illustrating that direct
   damage is weak and positioning/push is the real tool).

4. **Push into water = instant kill (environmental).**
   *Given* a Vanguard (*Ram*, knockback 2) at `(2,2)` facing a Bruiser at `(3,2)`,
   with `Water` at `(4,2)`,
   *When* the attack is confirmed,
   *Then* the Bruiser is pushed from `(3,2)` toward `(4,2)`, enters water on the first
   push step, and **dies immediately** (regardless of its 3 HP), and no further push
   steps occur.

5. **Push into a wall/unit = collision damage to both, no displacement past it.**
   *Given* a Vanguard (*Ram*, knockback 2) shoving a Crawler (HP 1) that has a `Wall`
   directly behind it,
   *When* confirmed,
   *Then* the Crawler cannot move, takes `1` collision damage (dies), the wall takes
   none, and the Crawler never changes tiles.
   *Given* instead another enemy V is directly behind the pushed unit U,
   *Then* U stops, **both U and V take 1** collision damage, and V is **not** further
   pushed.

6. **Enemy telegraph is generated at round start and matches execution.**
   *Given* a Spitter at `(5,5)` with a player unit at `(5,3)` in range,
   *When* `StartRound` runs,
   *Then* `Telegraphs[spitterId]` has `TargetTile = (5,3)`, `PredDamage = 1`, and its
   `AffectedTiles` overlay includes `(5,3)`.
   *When* the player does nothing and `EndTurn` → enemy phase resolves,
   *Then* the player unit at `(5,3)` takes exactly `1` damage — i.e. **execution ==
   telegraph**.

7. **Telegraph reconciliation: dodging the target tile.**
   *Given* the telegraph from scenario 6 (target `(5,3)`),
   *When* the player moves that unit to `(4,3)` and ends turn,
   *Then* the Spitter still fires at tile `(5,3)`; since it is now empty, **no unit
   takes damage** (the player successfully dodged). If the player had instead lured a
   *different* enemy onto `(5,3)`, that enemy takes the damage.

8. **Telegraph reconciliation: shove the attacker out of range cancels it.**
   *Given* a Bruiser telegraphing a melee Slam on a building at `(3,3)` from `(3,4)`,
   *When* the player uses *Ram* to push the Bruiser to `(3,6)` (now > range 1 from the
   building) and ends turn,
   *Then* the Bruiser's attack **fizzles** (target no longer in range from its new
   position), and the building takes **0** damage / Grid Power is unchanged.

9. **Building damage drains Grid Power; reaching 0 loses the mission.**
   *Given* `GridPower = 1` and a Bomber telegraphing 1 damage onto a building, with
   the player unable to stop it,
   *When* the enemy phase resolves and `StartRound`'s loss check runs,
   *Then* `GridPower` becomes `0` and `Phase = MissionResult(won=false, _)`.

10. **Win by survival.**
    *Given* mission 1 (Survival, `Round N = 4`) with `GridPower > 0` and ≥ 1 unit
    alive,
    *When* the player completes round 4's enemy phase and `StartRound` runs the win
    check,
    *Then* `Phase = MissionResult(won=true, grade)` and the grade reflects §11
    (e.g. all units alive, 6/7 Grid Power, one push-kill, 4 rounds → score ≥ 180 → at
    least `A`).

11. **Win by elimination + simultaneous mutual kill edge case.**
    *Given* the last two living enemies positioned so the player's single AoE deals
    lethal damage to both at once,
    *When* the attack is confirmed,
    *Then* **both** are removed in the same resolution, no enemies remain, and the next
    `StartRound` elimination check sets `won=true`.

12. **Undo across End Turn is forbidden.**
    *Given* the player confirmed a move, then pressed `EndTurn` (enemy phase resolved),
    *When* `Undo` is issued in the new player phase,
    *Then* nothing changes (the `History` stack was cleared at `EndTurn`); the enemy
    phase is irreversible.

13. **Input scenario: click-to-move flow.**
    *Given* `PlayerPhase`, *When* the player issues `SelectUnit 3` (a friendly Hornet),
    `PreviewMove (2,2)` (in range), then `ConfirmAction`,
    *Then* the Hornet ends at `(2,2)`, `HasMoved = true`, `SelectedUnit` may remain set
    for an attack, and `Reachable` is cleared/recomputed. A subsequent `PreviewMove`
    on the same unit (no `Mobile`) after it has *acted* is rejected (no `Pending`
    created).

14. **Flying ignores terrain and survives being pushed over water.**
    *Given* a Hornet (`Flying`) pathing, *When* reachability is computed across a
    `Water` tile, *Then* the water tile is traversable at cost 1.
    *Given* the Hornet is pushed across a water tile, *Then* it does **not** drown
    (hovers) and only off-board / wall collisions damage it.

15. **Determinism.**
    *Given* two fresh instances of mission 2 with the same seed and the **same input
    `Msg` sequence**, *When* both run to completion, *Then* their final `Model`s
    (board, unit HP/positions, Grid Power, grade) are **bit-for-bit identical** — no
    RNG influences rules.

## 15. Stretch Goals

Ranked, out of scope for v1:

1. **Chain knockback** — a pushed unit that hits another transfers momentum, pushing
   the second unit the remaining distance (with collision damage along the chain). The
   single biggest depth-adder.
2. **Unit progression / squad customization** — pilots that level up, swappable
   ability loadouts, persistent upgrades across the campaign (the *Into the Breach*
   pilot/mech meta-layer).
3. **Multi-step / branching enemy telegraphs** — enemies that telegraph a 2-step plan,
   or that *react* (e.g. "if pushed, retaliate"), increasing read complexity.
4. **Procedural mission generation** with seed sharing (daily-puzzle mode), leveraging
   the deterministic engine.
5. **Reinforcement timing UI** — show *next* round's spawn vents and incoming kinds two
   rounds ahead, deepening planning.
6. **More terrain interactions** — ice (sliding/forced extra push), smoke (temporary
   LoS block), conveyor tiles, destructible walls.
7. **Replay & "ghost solution"** — record the `Msg` sequence and replay/share optimal
   solutions (trivial given determinism).
8. **Online asynchronous puzzle leaderboards** ranked by grade/rounds for shared seeds.
