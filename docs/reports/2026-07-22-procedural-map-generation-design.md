# Design — Map construction & analysis (`fs-gg-mapcraft`)

- **Date:** 2026-07-22 (Part I); extended 2026-07-22 (Part II — the `fs-gg-mapcraft` reframing)
- **Owner:** `FS.GG.Game` (FS.GG.Game.Core)
- **Status:** Part I (MapGen generators, milestones M1–M6) shipped and merged (FS.GG.Game#474). Part II
  (the `fs-gg-mapcraft` reframing + `MapAnalysis` machinery, milestones M7–M12) is the active roadmap —
  see [Part II](#part-ii--map-construction--analysis-the-fs-gg-mapcraft-reframing).
- **Reframing note:** the capability is **map construction**, not merely generation. Procedural generation
  is *one producer* of a map; the analysis + validation machinery is **producer-agnostic** and shared by
  every map-building agent (procedural, authored, or agent-drawn). The skill is renamed `fs-gg-mapgen` →
  `fs-gg-mapcraft` (M7) to reflect this; `MapGen` (produce) and the new `MapAnalysis` (analyze/validate)
  are the modules under it.
- **Scope:** A comprehensive, general, **byte-identical deterministic** procedural map/level generator
  covering four algorithm families — cellular-automata caves, BSP room-and-corridor dungeons,
  room-graph (branching-walk) floors, and maze + value-noise heightmap + scatter — over a shared tile
  substrate, all seeded from `Rng`.
- **Language/target:** F# on `net10.0`, functional-first, integer-grid, immutable, BCL-only,
  Elmish/MVU-friendly. Every generator is pure, total, and reproducible from a `uint64` seed.
- **Consuming specs:** [roguelike-dungeon-crawler](../TestSpecs/Games/roguelike-dungeon-crawler.md) §4.8
  (procedural floor generation), [sandbox-survival](../TestSpecs/Games/sandbox-survival.md) (worldgen /
  heightmap / biomes), [doodle-jump](../TestSpecs/Games/doodle-jump.md) §4.7, and the seeded-map notes in
  [turn-based-tactics](../TestSpecs/Games/turn-based-tactics.md) §15.4 and
  [mini-tanks](../TestSpecs/Games/mini-tanks.md) §15.6.
- **Sibling designs:** [Red Blob algorithm roadmap](2026-07-19-redblobgames-algorithm-incorporation-roadmap.md) ·
  [pathfinding & navigation](2026-07-05-game-logic-pathfinding-navigation-design.md) ·
  [grids & spatial partitioning](2026-07-05-game-logic-grids-spatial-partitioning-design.md) ·
  [AI decision layer](2026-07-10-ai-decision-layer-design.md).
- **Grounding surface (today):** `FS.GG.Game.Core.Rng` (splitmix64, splittable), `Cell`/`Point`/`Rect`
  in `Primitives`, `Grids`/`Hex`/`Edges`, `Pathfinding` (BFS/A*/Dijkstra/flow-field), `SpatialGrid`.

---

## 0. What this document is

A design for one new Core module, `MapGen`, and its `fs-gg-mapgen` product skill. It fixes the module's
public shape, the determinism contract every generator obeys, and a milestone roadmap that delivers the
capability as independently-shippable SDD work items. It supersedes the "flagged, not planned" disposition
the algorithm roadmap gave procedural generation (§1 below explains why the disposition changed).

---

## 1. The placement decision — why `MapGen` belongs in Core, not the harness

The [algorithm roadmap](2026-07-19-redblobgames-algorithm-incorporation-roadmap.md) §3.5 deferred
procedural generation as "procedural *content* generation. Out of scope for a deterministic *logic*
library, but a **seeded** noise/map generator could live in `Game.Harness` … Flagged, not planned," under
the cross-cutting principle (§1.3) that "presentation-only work stays out of Core."

**That disposition is refined here, not contradicted.** The roadmap's concern was float-heavy,
presentation-tier *content* (terrain-from-noise textures, mapgen2 polygonal maps) whose output feeds a
renderer, not a simulation. But a **seeded, integer, byte-identical** generator is exactly the opposite: it
meets every bar `FS.GG.Game.Core` sets, and the consuming specs demand that bar explicitly — roguelike §14.1
requires "procedural generation byte-identical for a seed," and sandbox-survival requires a per-coordinate
pipeline whose noise stream is a separate seeded `Rng` from the sim.

Against the Core admission criteria:

| Core bar | `MapGen` |
|---|---|
| Pure / total | Every generator is `params -> Rng -> struct (result * Rng)`; degenerate inputs (zero size, impossible params) return a documented empty/clamped value, never throw. |
| Byte-deterministic | All randomness threads an explicit `Rng`; all iteration is fixed-order (row-major cells, ascending region/room ids); the one float touch-point (value noise) is built from the same top-53-bit construction as `Rng.nextFloat`. |
| Integer-cost, grid-based | Output is a grid of an integer tile classification plus integer-addressed structural metadata (rooms, regions, graph). |
| BCL-only | No dependency beyond `Rng`, `Primitives`, `Grids`, and `Pathfinding`, all already in Core. |

The line the roadmap drew still holds — **pixel-tier content stays out.** `MapGen` produces a *logical* map
(which cell is wall/floor, which room is the boss room, which biome a column is). Turning that into
textures, tile atlases, or smoothed meshes is the render adapter's job and does not enter Core. What moves
across the line is only the deterministic, integer generation the game *simulates over*.

Consequence: this document updates the roadmap's deferred list. `MapGen` is **planned and Core-resident**;
only the float terrain-*texture* and polygonal-mesh work remains flagged for the render tier.

---

## 2. The shared substrate (Milestone M1)

Every family below produces or consumes the same handful of types, so they land first as one module
skeleton and a connectivity toolkit.

```fsharp
namespace FS.GG.Game.Core

/// A dense, row-major rectangular grid of 'T, addressed by the shared `Cell` ({ Col; Row }).
/// Index i = Row * Width + Col. Immutable value; generators build one and return it.
[<Struct>]
type Grid<'T> = { Width: int; Height: int; Cells: 'T[] }

/// The base tile classification every wall/floor generator emits. Domain games map it onto their
/// own richer tiles at the boundary (as `Ai` keeps 'T = your facts).
[<Struct>]
type Tile = Wall | Floor

type TileMap = Grid<Tile>

/// A connected component of like tiles: a stable integer id and its cells in row-major order.
[<Struct>]
type Region = { Id: int; Cells: Cell[] }

[<RequireQualifiedAccess>]
module MapGen =

    // --- grid primitives (total; out-of-bounds get/set are documented no-ops/defaults) ---
    val filled   : width: int -> height: int -> value: 'T -> Grid<'T>
    val inBounds : Grid<'T> -> Cell -> bool
    val get      : Grid<'T> -> Cell -> 'T voption
    val set      : Grid<'T> -> Cell -> 'T -> Grid<'T>

    // --- connectivity: the toolkit every family shares ---
    /// Flood-fill the Floor cells into connected components (4- or 8-connected), ids ascending by the
    /// row-major position of each region's first cell — so the labelling is seed-independent and stable.
    val regions        : Neighbourhood -> TileMap -> Region list
    val largestRegion  : TileMap -> Region voption
    /// Carve straight corridors joining every region to the largest, so the returned map is a single
    /// connected floor. Deterministic: regions joined in ascending id order, corridor tie-broken by Cell.
    val connect        : Neighbourhood -> Rng -> TileMap -> struct (TileMap * Rng)
```

`Neighbourhood` (4-/8-way) is `Pathfinding`'s existing type — regions and connectivity reuse the same
neighbour vocabulary the routing layer already ships, never a private copy. A generated map is validated by
routing over it with `Pathfinding.bfs`, closing the loop with a module the repo already trusts.

**Determinism harness (M1 deliverable):** a reusable property — *generate twice from one seed ⇒
byte-identical `Grid`; generate from seed+1 ⇒ different* — that every later family instantiates. This is the
roadmap's §5 "determinism gate," made concrete for generation.

---

## 3. The four families

Each is one work item, depends only on M1, and is independent of the others (they can run in parallel).
Each ships params, a generator, connectivity guarantee, and a property suite.

### 3.1 Cellular-automata caves (M2)

Random-fill at a wall density, then N smoothing passes of the classic *4-5 rule* (a cell becomes wall if ≥5
of its 8 neighbours are wall; the border is always wall), then `MapGen.connect` to guarantee a single
traversable cavern. Organic caverns for roguelike interiors and sandbox caves.

```fsharp
type CaveParams = { WallChance: float; SmoothingPasses: int; Neighbourhood: Neighbourhood }
val MapGen.caves : width: int -> height: int -> CaveParams -> Rng -> struct (TileMap * Rng)
```

Red Blob / RogueBasin cellular-automata caves. Determinism: fill draws row-major from the `Rng`; smoothing
is a pure grid→grid fold; connectivity via M1.

### 3.2 BSP room-and-corridor dungeons (M3)

Recursively partition the rectangle (alternating split axis, split point drawn from `Rng` within a
min-leaf-size bound), place one room per leaf, then join sibling rooms with L-corridors walking the BSP tree
back up. Structured, rectilinear dungeons.

```fsharp
type BspParams = { MinLeaf: int; MaxLeaf: int; RoomPadding: int }
type Room = { Id: int; Bounds: Rect }                       // integer Rect in cell space
type RoomGraph = { Rooms: Room[]; Corridors: (int * int)[] } // room-id pairs a corridor connects
val MapGen.bspDungeon : width: int -> height: int -> BspParams -> Rng -> struct (TileMap * RoomGraph * Rng)
```

The `RoomGraph` is the structural metadata a game needs to place a start/exit/loot — rooms are
integer-addressed and ordered by BSP-tree traversal, so the graph is seed-reproducible.

### 3.3 Room-graph / branching-walk floors (M4)

The roguelike spec §4.8 generator exactly: a branching random walk from a start room lays out a graph of
rooms on a grid; special rooms (BOSS / TREASURE / SHOP / SECRET) are assigned by the documented rule
(boss = farthest dead-end, etc.); each room carries a template id for interior population. `floorSeed =
hash(runSeed, floorIndex)` so each floor of a run is independent yet reproducible.

```fsharp
type RoomKind = Normal | Start | Boss | Treasure | Shop | Secret
type FloorRoom = { Cell: Cell; Kind: RoomKind; TemplateId: int; Doors: Neighbourhood }
type FloorLayout = { Rooms: FloorRoom[]; Adjacency: (Cell * Cell)[] }
type FloorParams = { RoomCount: int; MaxRooms: int; SpecialRooms: RoomKind list }
val MapGen.floorLayout : FloorParams -> Rng -> struct (FloorLayout * Rng)
val MapGen.floorSeed   : runSeed: uint64 -> floorIndex: int -> uint64
```

This is the one family that is a *graph of rooms* rather than a tile grid; a game rasterizes the layout into
a `TileMap` with its own room dimensions.

### 3.4 Maze, noise heightmap, and scatter (M5)

Three general primitives that don't each merit a work item but together complete "comprehensive":

- **Maze** — recursive-backtracker (growing-tree) perfect maze on a cell grid; every cell reachable, no
  loops (optional braid pass to remove dead-ends).
  `val MapGen.maze : width: int -> height: int -> Rng -> struct (TileMap * Rng)`
- **Value-noise heightfield** — deterministic hashed-lattice value noise with fractal octaves, sampled per
  cell into an integer height, then a threshold table classifies height → biome/tile. The sandbox-survival
  pipeline's first stage. The noise `Rng` is a *separate seeded stream* (`Rng.split`) from any sim RNG.
  `val MapGen.heightField : width -> height -> NoiseParams -> Rng -> Grid<int>`
  `val MapGen.classify : (int * 'T) list -> Grid<int> -> Grid<'T>`  // ascending threshold table
- **Scatter** — Poisson-disk (Bridson) and weighted-cell scatter for props, spawns, and ore, over a
  walkability/height mask, returning cells in a deterministic order.
  `val MapGen.poissonScatter : Grid<bool> -> minDist: int -> Rng -> struct (Cell list * Rng)`

### 3.5 Skill finalize (M6)

Author the full `fs-gg-mapgen` SKILL.md (the teaching document — the *rules* of deterministic generation,
not just the API, in the style of `fs-gg-grids`/`fs-gg-ai`), regenerate the skill manifest sha256, and file
the cross-repo registry reconcile follow-up (`registry = manifest = bytes`, a new `owner: fs-gg-game` row —
the same follow-up class as `fs-gg-effects`/`fs-gg-physics`). M1 lands only a SKILL.md *skeleton* and the
manifest row so the capability is materializable early; M6 makes it authoritative.

---

## 4. Determinism contract (applies to every family)

1. **One seed in, one map out.** Every generator is `… -> Rng -> struct (result * Rng)`; the same seed
   yields a byte-identical result, forever, across machines. Verified by the M1 harness on each family.
2. **No hidden order.** Cells iterate row-major; regions/rooms carry ascending ids fixed by row-major
   first-cell position, never by draw order or hash-set enumeration. Corridor/room ties break on `Cell`.
3. **Noise is a split stream.** Content noise draws from a `Rng.split` child, never the caller's sim RNG, so
   worldgen and simulation cannot desync each other (sandbox-survival's explicit requirement).
4. **The only float is the unit-interval draw.** Value noise scales top-53-bits exactly as `Rng.nextFloat`;
   there is no `atan2`/`sqrt` on the deterministic path. Thresholds compare integers.
5. **Total on degenerate input.** Zero/negative size ⇒ empty grid; impossible params (min > max, room
   count 0) ⇒ documented clamp/empty; never an exception.

---

## 5. Relationship to existing modules

- **`Rng`** — the sole entropy source; `floorSeed` and the noise split are its `mix`/`split`.
- **`Pathfinding`** — `Neighbourhood` is reused for connectivity; `bfs` validates traversability in tests.
- **`Grids` / `Cell` / `Rect`** — the address and bounds vocabulary; `Grid<'T>` is the new dense container,
  `Room.Bounds` is the existing integer `Rect`, cells are the shared `Cell`.
- **`Ai`** — a generated `TileMap` is the terrain an `Ai.threatField`/`influenceMap` runs over; the two
  compose without either knowing the other.
- **Render tier (out of Core)** — texturing, autotiling, and mesh smoothing of the logical map stay in the
  render adapter, per §1.

---

## 6. The roadmap

Six work items. M1 is the gate; M2–M5 depend only on M1 and are mutually independent (parallelizable); M6
depends on M2–M5.

| # | Work item (`work/<id>`) | Tier | Depends | Exit criteria |
|---|---|---|---|---|
| M1 | `mapgen-substrate` | 1 | — | `Grid<'T>`/`Tile`/`TileMap`/`Region` types; grid primitives; `regions`/`largestRegion`/`connect`; the reusable determinism property; `MapGen.fs`+`.fsi` in the fsproj; `fs-gg-mapgen` SKILL.md skeleton + manifest row. Surface baseline updated. |
| M2 | `mapgen-cellular-caves` | 1 | M1 | `CaveParams` + `caves`; 4-5 smoothing; connectivity-guaranteed single cavern; determinism + traversability property suite. |
| M3 | `mapgen-bsp-dungeons` | 1 | M1 | `BspParams`/`Room`/`RoomGraph` + `bspDungeon`; leaf rooms + L-corridors; every room reachable; seed-reproducible graph. |
| M4 | `mapgen-room-graph` | 1 | M1 | `FloorParams`/`RoomKind`/`FloorLayout` + `floorLayout`/`floorSeed`; roguelike §4.8 special-room rule; per-floor seed independence. |
| M5 | `mapgen-maze-noise-scatter` | 1 | M1 | `maze`; `heightField`+`classify`; `poissonScatter`; split-stream noise; determinism suites for each. |
| M6 | `mapgen-skill-finalize` | 1 | M2–M5 | Full `fs-gg-mapgen` SKILL.md teaching the determinism rules + every family; manifest sha256 regenerated; cross-repo registry reconcile follow-up filed. |

**Milestone exit:** a generated FS.GG.UI game can produce caves, structured dungeons, roguelike floors,
mazes, noise worlds, and scattered content — all byte-identical for a seed — from one Core module, taught by
one product skill that materializes into every `game`/`sample-pack` scaffold.

Each item flows the full SDD lifecycle: charter → specify → clarify → checklist → plan → tasks → analyze →
implement → evidence → verify → ship.

### Cross-repo follow-up (open)

The `.github` org registry (`registry/skills.yml`) needs a new `owner: fs-gg-game` row for `fs-gg-mapgen`,
reconciled from this repo's regenerated `skill-manifest.json` (registry = manifest = bytes) — the same
follow-up class as `fs-gg-effects` (FS-GG/.github#328) and `fs-gg-physics` (FS-GG/.github#330). Filed via the
**cross-repo-coordination** protocol as **FS-GG/.github#1355** (recorded as the M6 accepted deferral
`work/032-mapgen-skill-finalize`, AD-001); it is another repo's change, so it is owned and resolved there.

---

## 7. Sources / links

- Red Blob Games — [Cellular-automata caves (RogueBasin)](http://www.roguebasin.com/index.php/Cellular_Automata_Method_for_Generating_Random_Cave-Like_Levels),
  [Making maps with noise functions](https://www.redblobgames.com/maps/terrain-from-noise/),
  [Maze generation](https://www.redblobgames.com/maps/mapgen4/), [Poisson-disk (Bridson)](https://www.redblobgames.com/x/1830-hexagonal-fibonacci/).
- The Binary Space Partition dungeon method (classic roguelike technique).
- Consuming specs: `docs/TestSpecs/Games/roguelike-dungeon-crawler.md` §4.8, §14.1;
  `docs/TestSpecs/Games/sandbox-survival.md` (worldgen pipeline).
- Placement precedent: `docs/reports/2026-07-19-redblobgames-algorithm-incorporation-roadmap.md` §1.3, §3.5.

---

# Part II — Map construction & analysis (the `fs-gg-mapcraft` reframing)

## 8. Why generation is only half the capability

Part I built generators. But an agent building a map — however it builds it — asks the same battery of
questions afterwards, and *reinvents the machinery to answer them* every time:

- Are all areas reachable, or did I strand a region?
- Where are the entrances/exits, and which cells are the chokepoints?
- How long is the critical path from start to exit? Is the map too small / too sprawling?
- Is the loot / spawn distribution *fair* — does every start have comparable access to resources?
- What is the tactical shape — where is it exposed, where is there cover, where are the killzones?

None of this is specific to *procedural* maps. A hand-authored level, or a map an agent draws room by
room, needs the identical analysis. So the capability is **map construction as a pipeline** —
`produce → analyze → validate → iterate` — where the producer varies (procedural / authored / agentic)
but the **analysis + validation layer is producer-agnostic and shared**. That layer is `MapAnalysis`,
and the skill that teaches the whole pipeline is `fs-gg-mapcraft`.

The keystone is **validation**: a battery that runs the analyses and returns accept/reject *with reasons*,
so an agent can loop `generate → validate → regenerate` (or `edit → validate → fix`) until the map meets
its rules. Every map-building agent hand-rolls that loop today; `MapAnalysis.validate` is it, once.

## 9. `MapAnalysis` — producer-agnostic, predicate-general

Analysis operates over **any map**, not just `MapGen` output. Following `Pathfinding`'s discipline — *the
`Cell -> bool` predicate IS the map* — the core functions take either a `TileMap` or a walkability
predicate + a cell domain, so they serve authored and agent-built maps too. All pure, total, deterministic,
integer, BCL-only, over `Cell`/`Neighbourhood`/`Pathfinding`, and (for the tactical layer) a caller-supplied
`hasLos: Cell -> Cell -> bool` oracle exactly as `Ai` takes one.

```fsharp
namespace FS.GG.Game.Core

[<RequireQualifiedAccess>]
module MapAnalysis =

    // M8 — reachability & connectivity
    val reachable    : Neighbourhood -> isWalkable: (Cell -> bool) -> start: Cell -> maxVisited: int -> Set<Cell>
    val components   : Neighbourhood -> TileMap -> Region list          // predicate-general components
    val isConnected  : Neighbourhood -> TileMap -> bool
    val stranded     : Neighbourhood -> from: Cell -> TileMap -> Cell list   // reachable-in-principle but not from `from`

    // M9 — entrances/exits & chokepoints
    val borderOpenings    : TileMap -> Cell list                        // floor cells on the map border
    val deadEnds          : Neighbourhood -> TileMap -> Cell list
    val articulationPoints: Neighbourhood -> TileMap -> Cell list       // bottlenecks whose removal splits the map

    // M10 — path & flow metrics
    val distanceField : Neighbourhood -> TileMap -> sources: Cell list -> Map<Cell, int>   // multi-source BFS
    val diameter      : Neighbourhood -> TileMap -> int                 // longest shortest-path = critical path length
    val isolation     : Neighbourhood -> TileMap -> Cell -> int         // farthest reachable distance from a cell

    // M11 — distribution, fairness & the validation report (the keystone)
    val spacing   : points: Cell list -> struct (int * float)           // min & mean nearest-neighbour spacing
    val fairness  : spawns: Cell list -> resources: Cell list -> Neighbourhood -> TileMap -> Map<Cell, int>  // per-spawn nearest-resource cost
    val coverage  : Neighbourhood -> TileMap -> points: Cell list -> radius: int -> float   // fraction of floor within `radius`
    type Rule     = { /* connected? min/max diameter? min entrances? min spawn spacing? ... */ }
    type Report   = { Passed: bool; Failures: string list; Connected: bool; Diameter: int; Entrances: int (* … *) }
    val validate  : Rule list -> Neighbourhood -> TileMap -> Report

    // M12 — tactical analysis (STATIC map shape; NOT live enemies — that is `Ai`)
    val exposureMap : hasLos: (Cell -> Cell -> bool) -> TileMap -> Map<Cell, int>   // how many floor cells can see each cell
    val coverMap    : TileMap -> Map<Cell, int>                          // adjacent-wall cover directions per floor cell
    val killzones   : hasLos: (Cell -> Cell -> bool) -> TileMap -> (Cell * Cell) list   // long mutual-sightline pairs
```

### The tactical layer vs `Ai` — the boundary that keeps them from duplicating

`Ai.threatField`/`influenceMap` answer *"given THESE live enemies, where is it dangerous / who controls
this ground THIS tick"* — dynamic, keyed on actual units. `MapAnalysis`'s tactical functions answer *"what
is the tactical SHAPE of this map"* — static, a function of geometry alone (exposure, cover, killzones),
computable at build time with no enemies present. They compose: a map-builder validates/places using the
static shape; `Ai` consumes the same geometry per tick with real sources layered on. This is the identical
substrate-vs-policy split the `Ai` skill already draws — `MapAnalysis` is more substrate, `Ai` is policy.

## 10. Part II determinism & placement

Same contract as Part I: pure, total, byte-deterministic (integer, fixed-order iteration, no hash-set
enumeration into output), BCL-only, Core-resident. `MapAnalysis` reuses `Pathfinding`'s `Neighbourhood`/BFS
and (for tactical) a caller-supplied `hasLos` oracle — it adds no float path. It is *read-only over maps*:
it never mutates a map, so it composes behind any producer.

## 11. The Part II roadmap

Six milestones. M7 (rename) is the gate; M8–M12 add `MapAnalysis` and depend on M7 + the shipped substrate;
they are largely independent of each other (parallelizable), with M11's `validate` consuming M8–M10.

| # | Work item (`work/<id>`) | Depends | Exit criteria |
|---|---|---|---|
| M7 | `mapcraft-rename` | — | Retire the `fs-gg-mapgen` skill id, introduce `fs-gg-mapcraft` (construction+analysis framing; generation is one section); manifest regenerated; cross-repo registry request updated (FS-GG/.github#1355); design doc reflects it. No `MapGen` source change. |
| M8 | `mapanalysis-reachability` | M7 | `MapAnalysis` module + `reachable`/`components`/`isConnected`/`stranded` over a `TileMap` or a `Cell -> bool` predicate; determinism + correctness suite; surface baseline. |
| M9 | `mapanalysis-chokepoints` | M8 | `borderOpenings`/`deadEnds`/`articulationPoints`; a bridge/articulation algorithm with a documented deterministic order; tests on known fixtures. |
| M10 | `mapanalysis-path-metrics` | M8 | `distanceField`/`diameter`/`isolation`; multi-source BFS; diameter = longest shortest-path; determinism tests. |
| M11 | `mapanalysis-fairness-validate` | M8–M10 | `spacing`/`fairness`/`coverage` + the `Rule`/`Report` `validate` battery driving generate→validate→regenerate; worked example against a `MapGen` cave/dungeon. |
| M12 | `mapanalysis-tactical` | M8 | `exposureMap`/`coverMap`/`killzones` over a caller `hasLos` oracle; the static-shape-vs-`Ai`-policy boundary documented; determinism tests. |

**Milestone exit:** `fs-gg-mapcraft` teaches the full pipeline — produce a map (procedural via `MapGen`, or
bring your own `TileMap`), analyze it (reachability, chokepoints, path metrics, fairness, tactical shape),
and validate it against rules — all byte-identical for a seed, over one Core surface, for every
map-building agent.

Each item flows the full SDD lifecycle (charter → ship), as Part I did.
