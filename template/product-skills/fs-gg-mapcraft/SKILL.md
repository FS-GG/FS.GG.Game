---
name: fs-gg-mapcraft
description: Construct and validate the logical map a game simulates over — produce it (procedural generation, or bring your own), then analyze it (reachability, chokepoints, path metrics, fairness, tactical shape) and validate it against rules — deterministically, over the `MapGen` and `MapAnalysis` modules in FS.GG.Game.Core.
---

# Map Construction & Analysis Capability

## Scope

Use this skill to **construct the logical map a game simulates over** — as a pipeline, not just a generator:

```
produce  →  analyze  →  validate  →  (iterate)
```

- **Produce** — a `TileMap`/`RoomGraph`/`FloorLayout`. The producer is *your choice*: procedural generation
  (the `MapGen` module — caves, dungeons, floors, mazes, noise, scatter), a hand-authored level, or a map an
  agent draws room by room. Procedural generation is **one producer**, not the whole capability.
- **Analyze** — the producer-agnostic `MapAnalysis` machinery every map-builder needs regardless of how the
  map was made: reachability & connectivity, entrances/exits & chokepoints, path & flow metrics,
  distribution & fairness, and the static tactical shape (exposure, cover, killzones). *Delivered across
  milestones M8–M12 — see the design doc; the generation surface below ships today.*
- **Validate** — the keystone: run the analyses against your rules and get accept/reject *with reasons*, so
  you can loop **produce → validate → re-produce** until the map is good.

Everything here is **pure, total, and deterministic** — safe to call from a replayed simulation step — and
operates over *any* map (a `TileMap`, or, like `Pathfinding`, a `Cell -> bool` walkability predicate), not
only `MapGen` output. **Render-tier work stays out**: texturing, autotiling, and mesh smoothing belong to the
render adapter. This skill materializes for the `game` and `sample-pack` profiles.

### The generation producer (`MapGen`) — shipping today

One Core module, `MapGen`, built on the seeded `Rng`, with a shared substrate and five generator families:

- **Substrate** — `Grid<'T>` (dense, row-major, addressed by `Cell`), `Tile`/`TileMap`, `Region`, total
  addressing (`filled`/`inBounds`/`get`/`set`), and connectivity (`regions`/`largestRegion`/`connect`).
- **Caves** (`caves`) — organic cellular-automata caverns.
- **Dungeons** (`bspDungeon`) — BSP rooms + corridors, with a `RoomGraph`.
- **Floors** (`floorLayout`/`floorSeed`) — Isaac-style branching-walk room graphs with special rooms.
- **Maze / noise / scatter** (`maze`, `heightField`+`classify`, `poissonScatter`).

## Public Contract

Bundled with this product:

- `docs/api-surface/Game.Core/MapGen.fsi` — the `Grid<'T>`/`Tile`/`TileMap`/`Region` substrate; `CaveParams`,
  `BspParams`/`Room`/`RoomGraph`, `RoomKind`/`FloorRoom`/`FloorLayout`/`FloorParams`, `NoiseParams`; and the
  `MapGen` module. Shipped in `FS.GG.Game.Core`, referenced on the `game`/`sample-pack` profiles.
- `docs/api-surface/Game.Core/Rng.fsi` — the seeded splitmix64 `Rng` every generator threads.
- `docs/api-surface/Game.Core/Pathfinding.fsi` — the `Neighbourhood` fills/connectivity use, and the `bfs`
  that validates a map is traversable.
- `docs/api-surface/Game.Core/Primitives.fsi` — the shared `Cell`/`Rect`.

All entry points are **total**: a non-positive dimension is an empty grid, out-of-bounds reads as `ValueNone`
/ writes as a no-op, impossible params clamp, and nothing throws.

## The determinism contract (the rules that make a generator replay-safe)

1. **One seed in, one map out.** Every generator is `… -> Rng -> struct (result * Rng)` (or `Grid<int>` for
   `heightField`); the same seed is byte-identical forever, across machines. **Thread the returned `Rng`** —
   never reuse the input.
2. **No hidden order.** Cells iterate row-major; `Region`/room ids ascend by row-major or placement position,
   never by hash-set enumeration or draw order.
3. **The fill and the router agree.** `regions`/`connect` take an explicit `Neighbourhood` — the one you route
   with. Under `EightWay` the region fill honours the router's **no-corner-cutting** rule, so a `connect`ed or
   generated map is genuinely `Pathfinding.bfs`-traversable, not just fill-connected.
4. **Content noise is a split stream.** `heightField` draws its salt from the `Rng` you pass — hand it a
   `Rng.split` child so worldgen and simulation cannot desync.
5. **Validate by routing.** A generated map is correct when `bfs` reaches every floor cell (the shipped
   `traversabilityHarness`).

## Substrate — build, address, connect

```fsharp
open FS.GG.Game.Core

let g : TileMap = MapGen.filled 40 30 Wall
let g2 = MapGen.set g { Col = 5; Row = 5 } Floor      // copy-on-write; OOB set is a no-op
let here = MapGen.get g2 { Col = 5; Row = 5 }         // ValueSome Floor ; OOB get is ValueNone

let rs   = MapGen.regions FourWay someMap             // components, ascending Id by row-major first cell
let big  = MapGen.largestRegion FourWay someMap       // most cells, ties to lowest Id
let struct (connected, rng') = MapGen.connect FourWay rng someMap   // one traversable floor
```

## Caves — cellular automata

```fsharp
let p : CaveParams = { WallChance = 0.45; SmoothingPasses = 4; Neighbourhood = FourWay }
let struct (cave, rng') = MapGen.caves 48 32 p rng
```

Random-fills at `WallChance`, runs the 4-5 Moore rule `SmoothingPasses` times (a cell becomes `Wall` when
≥5 of its 8 neighbours, out-of-bounds counted as wall, are wall), forces a solid `Wall` border, and
`connect`s into one traversable cavern.

## Dungeons — BSP rooms and corridors

```fsharp
let p : BspParams = { MinLeaf = 8; MaxLeaf = 16; RoomPadding = 1 }
let struct (dungeon, graph, rng') = MapGen.bspDungeon 64 48 p rng
// graph.Rooms : Room[]  (Id ascending, Bounds a cell-space Rect) ; graph.Corridors : (int*int)[]
```

Recursively partitions the rectangle (splitting the longer side, the split position from the `Rng`), places
one padded room per leaf, joins sibling subtrees with L-corridors, and returns both the carved `TileMap` and
the `RoomGraph` you place a start/exit/loot on. Rooms in distinct leaves never overlap.

## Floors — Isaac-style room graph

```fsharp
let seed = MapGen.floorSeed runSeed floorIndex           // independent-yet-reproducible per floor
let p : FloorParams = { RoomCount = 15; MaxRooms = 20; SpecialRooms = [ Boss; Treasure; Shop ] }
let struct (floor, rng') = MapGen.floorLayout p (Rng.ofSeed seed)
// floor.Rooms.[0].Kind = Start ; special rooms land on the farthest dead-ends ; doors implied by Adjacency
```

A branching random walk from a `Start` room grows a connected graph (a candidate touching two placed rooms
is rejected, keeping it tree-like); `SpecialRooms` are assigned to the farthest dead-ends by graph distance.
A game rasterises `FloorLayout` into a `TileMap` with its own room sizes.

## Maze, noise, scatter

```fsharp
// perfect maze as a TileMap (odd-lattice cells, wall border, single traversable floor)
let struct (mz, rng1) = MapGen.maze 31 21 rng

// value-noise height field -> classify into bands. Split the stream off your sim Rng.
let struct (noiseRng, simRng) = Rng.split rng
let np : NoiseParams = { Octaves = 4; Frequency = 0.08; Persistence = 0.5 }
let heights = MapGen.heightField 64 64 np noiseRng                       // Grid<int>, heights in [0,255]
let biomes  = MapGen.classify [ (0,"water"); (60,"sand"); (110,"grass"); (200,"rock") ] heights  // Grid<string>

// Poisson-disk scatter over an eligibility mask (props / spawns / ore)
let mask : Grid<bool> = { Width = 64; Height = 64; Cells = Array.map (fun t -> t = Floor) cave.Cells }
let struct (spawns, rng2) = MapGen.poissonScatter mask 5 rng1           // cells >= 5 apart, deterministic order
```

`classify` maps each cell to the value of the highest `threshold <= its height` (lowest band below all; an
empty table yields an empty grid). `poissonScatter` returns mask-eligible cells at least `minDist` apart, via
integer rejection sampling (no trig).

## Analyze — reachability & connectivity (`MapAnalysis`)

The `MapAnalysis` module answers the questions every map-builder asks *after* producing a map — and it works
over **any** map, because `reachable` takes a `Cell -> bool` predicate (the map *is* the predicate, as in
`Pathfinding`), not only a `MapGen` output:

```fsharp
// The producer is your choice; here a generated cave, but the analysis is producer-agnostic.
let isFloor (c: Cell) = MapGen.get level c = ValueSome Floor
let reached   = MapAnalysis.reachable FourWay 4096 isFloor { Col = 1; Row = 1 }   // Set<Cell> reachable from a start
let connected = MapAnalysis.isConnected FourWay level          // is the whole floor one region?
let stray     = MapAnalysis.stranded FourWay { Col = 1; Row = 1 } level           // floor cells cut off from (1,1)
let areas     = MapAnalysis.componentCount FourWay level        // number of separate floor areas
```

`reachable` is built on `Pathfinding.distanceField`, so its set is **exactly** what `Pathfinding.bfs` can
reach — the analysis layer and the routing layer never disagree. Use the **same `Neighbourhood`** you route
with. For the component *list* (not just the count), reach for `MapGen.regions`.

**Chokepoints & shape.** `borderOpenings` lists the map's entrances/exits, `deadEnds` its tips, and
`articulationPoints` its bottleneck cells (the ones whose removal splits the map — iterative Tarjan, total on
any shape). `isolation`/`diameter` measure size as unweighted hop counts (the critical-path length).

## Validate — the keystone loop

`MapAnalysis.validate` runs the analyses against a `Rule` set and returns a `Report` with accept/reject **and
the reasons** — so an agent can loop *produce → validate → re-produce* until the map is good:

```fsharp
let rules = [ Connected; MinDiameter 20; MinBorderOpenings 2; MaxComponents 1 ]
let report = MapAnalysis.validate rules FourWay level
if not report.Passed then
    printfn "rejected: %s" (String.concat "; " report.Failures)   // ...regenerate with a new seed, else ship it
// distribution/fairness measures feed richer rules a caller checks alongside validate:
let struct (minGap, meanGap) = MapAnalysis.spacing spawns      // are spawns spread out?
let access = MapAnalysis.fairness spawns loot FourWay level     // each spawn's nearest-loot hop distance
let covered = MapAnalysis.coverage FourWay level spawns 8       // fraction of floor within 8 of a spawn
```

`Rule` is a closed set (`Connected`, `MinDiameter`/`MaxDiameter`, `MinBorderOpenings`, `MaxComponents`), so
`validate` is total and its `Failures` are deterministic (one reason per violated rule, in rule order). A
bespoke check runs alongside `validate` and combines results — the battery stays predictable.

## Tactical shape — static, not live enemies

`MapAnalysis` also measures the **static tactical shape** of a map — a property of geometry alone, computable
at build time with no units present, over a caller-supplied `hasLos: Cell -> Cell -> bool` oracle (as [[fs-gg-ai]]
takes one):

```fsharp
let hasLos (a: Cell) (b: Cell) = Los.lineOfSight (fun c -> MapGen.get level c = ValueSome Floor) a b
let exposure = MapAnalysis.exposureMap hasLos level    // per cell: how many other cells can see it (open killing ground)
let cover    = MapAnalysis.coverMap level              // per cell: how many of its 8 sides are wall (protection)
let killers  = MapAnalysis.killzones hasLos 12 level    // long mutual sightlines (>= 12 Chebyshev apart)
```

**This is deliberately not [[fs-gg-ai]].** `Ai.threatField`/`influenceMap` are the *dynamic* answer — "given
THESE live enemies, where is it dangerous / who controls this ground *this tick*". `MapAnalysis`'s tactical
functions are the *static priors* — the shape of the map itself, which a builder validates and places on, and
which `Ai` then consumes per tick with real sources layered on. Substrate here; policy there.

*This completes the `fs-gg-mapcraft` analysis machinery.*

## Common pitfalls

- **Reusing the input `Rng`.** Thread the returned one, or every draw repeats.
- **Mixing neighbourhoods.** Fill/connect under the same `Neighbourhood` you route with.
- **Not splitting the noise stream.** Pass `heightField` a `Rng.split` child, or worldgen desyncs your sim.
- **Re-rolling `Cell`/`Rect`/`Grid`.** Reuse the shared vocabulary; `Grid<'T>` is the only container.
- **Reading `connect`/`RoomGraph`/`FloorLayout` as tiles to draw.** They are logical maps; texturing is the
  render adapter's job, deliberately out of Core.
- **Expecting a `RoomGraph` corridor list to include the safety-net carve.** `Corridors` records the BSP room
  joins; a rare extra `connect` corridor is a `TileMap` connectivity guarantee, not room adjacency.

## Build Commands

Run `dotnet build FS.GG.Game.slnx` then the surface/drift checks in this product.

## Test Commands

Run `dotnet run --project tests/Game.Core.Tests -- --filter MapGen` to exercise every family and the shared
determinism/traversability/totality harness.

## Evidence

Record map-generation evidence (build byte-identity replays for a seed per family; `bfs` traversability over
caves/maze/dungeon; region labelling; special-room-on-dead-end placement; scatter min-spacing; totality on
degenerate input) under this product's `readiness/` paths. Do not copy framework readiness reports in.

## Package Boundary

The `MapGen` module and its types live in `FS.GG.Game.Core` (referenced on the `game`/`sample-pack` profiles),
above the `Cell`/`Rect`/`Rng`/`Neighbourhood` vocabulary they reuse. `FS.GG.Game.Core` is the BCL-only bottom
layer — it pulls in no viewer, which is why render-tier texturing of the generated map is not here.

## Generated Product

Generate your map on a fixed seed (or a `floorSeed`/`Rng.split` content stream), hold the `TileMap`/
`RoomGraph`/`FloorLayout` in your `Model`, and route over it with [[fs-gg-game-core]]'s `Pathfinding`. Pair a
generated terrain with [[fs-gg-ai]]'s threat/influence maps and [[fs-gg-grids]] for the pixel geometry the
`View` draws.

## Persistent problems

When a problem outlasts reasonable in-repo attempts, extensive external research is **mandatory** — consult
**official online docs first** (the F#/.NET docs and the Red Blob Games references), then community sources.
If your product uses Spec Kit, record findings under the feature's `specs/<feature>/feedback/`; otherwise
record them in this skill's **Sources** line and any product-local `docs/`. Offline, the mandate degrades to
recording "research blocked — <why>" rather than hard-failing.

## Related

- [[fs-gg-game-core]] — the seeded `Rng` every generator threads, and `Pathfinding` for routing and the
  traversability check over a generated map.
- [[fs-gg-grids]] — addresses the parts (edges/vertices) of the cells this skill fills, and maps them to
  pixels for the `View`.
- [[fs-gg-ai]] — a generated `TileMap` is the terrain `Ai.threatField`/`influenceMap` runs over.

## Sources / links

- Red Blob Games, "Making maps with noise functions": https://www.redblobgames.com/maps/terrain-from-noise/
- RogueBasin, "Cellular Automata cave generation": http://www.roguebasin.com/index.php/Cellular_Automata_Method_for_Generating_Random_Cave-Like_Levels
- The Binary Space Partition dungeon method; the Binding of Isaac floor-generation algorithm.
- Bridson, "Fast Poisson Disk Sampling in Arbitrary Dimensions".
- Design: `docs/reports/2026-07-22-procedural-map-generation-design.md`
- F#/.NET docs: https://learn.microsoft.com/en-us/dotnet/fsharp/
