# Game logic skill — Grids & spatial partitioning

> **Provenance (relocated 2026-07-06, ADR-0022 open-decision #3).** This game-logic library design was authored **2026-07-05 in FS-GG/.github** — *before* the FS.GG.Game extraction — and relocated here to live with the code it designs. Where the original homed these primitives in `FS.GG.UI.Canvas` / `FS.GG.UI.Scene` / `FS.GG.Rendering`, that home is now **`FS.GG.Game.Core`** — the BCL-only bottom layer; ADR-0022 P0–P5 moved `Rng`/`FixedStep`/`Pathfinding`/`SpatialGrid` out of `FS.GG.UI.Canvas` and the `Geometry` collision module out of `FS.GG.UI.Scene`. Direct surface references below were updated to `FS.GG.Game.Core.*`; the surrounding design narrative is preserved as the dated record.

- **Date:** 2026-07-05
- **Owner:** `.github` (cross-repo coordination); **implementation home:** `FS.GG.Game` (FS.GG.Game.Core)
- **Status:** Design proposal (pre-ADR). Deepens an existing thin surface into a comprehensive skill.
- **Scope:** The **grid coordinate systems + spatial-partitioning** game-logic skill — what
  `FS.GG.Game.Core.SpatialGrid` (today: a uniform grid with exact range/radius queries) and
  the `grids` fragment should grow into, in **docs** and **source code**.
- **Language/target:** F# on `net10.0`, functional-first, **byte-deterministic** across
  runs/platforms, immutable-value-oriented, Elmish/MVU-friendly.
- **Sibling designs (this batch):** [collision](2026-07-05-game-logic-collision-detection-design.md) ·
  [line of sight](2026-07-05-game-logic-line-of-sight-design.md) ·
  [field of view / visibility](2026-07-05-game-logic-field-of-view-visibility-design.md) ·
  [pathfinding](2026-07-05-game-logic-pathfinding-navigation-design.md).
- **Grounding:** `turn-based-tactics` (grid movement/reachability), `tetris` (cell grid as
  collision truth), `tower-defense` (grid maps), Rendering #110/#132 (`SpatialGrid`,
  `Pathfinding`, grids fragment), `fs-gg-game-core` skill.

---

## 1. Why this skill needs to grow

Today the grid surface is a single uniform `SpatialGrid` plus a thin "grids" fragment. But
the game TestSpecs already lean on grid semantics that surface has no vocabulary for:
square **and** hex coordinate systems, four distance metrics tied to movement rules, chunked
maps, and broad-phase spatial queries that pathfinding/collision/FOV all consume. A grid is
the **shared substrate** under the four sibling skills — getting its coordinate math,
determinism, and query API right is a force multiplier. This document specifies the
comprehensive skill: the coordinate algebra, the storage + index structures, the determinism
rules, the API surface, the test discipline, and the pitfalls to design out.

**The organizing principle:** *integer logic, float presentation.* All game-logic coordinate
math is integer (cube/axial/square); every irrational (`√3` hex layout, `√2` diagonals,
Euclidean distance) is pushed to the Elmish **presentation edge** or replaced by an
integer-scaled surrogate. This is the grid analogue of the framework's `Json-is-contract` /
`Rich-is-projection` rule and is what makes byte-determinism achievable.

---

## 2. Coordinate systems

### 2.1 Square grids

| Concern | Content the skill must ship |
|---|---|
| Neighbourhoods | 4-way (von Neumann: `(±1,0),(0,±1)`) and 8-way (Moore: + `(±1,±1)`), in a **fixed canonical order** (determinism). |
| Distance metrics | **Manhattan** `dx+dy` (4-way), **Chebyshev** `max(dx,dy)` (8-way, diag=1), **octile** `max+(√2−1)·min` (8-way, diag=√2), **Euclidean** `√(dx²+dy²)` (heuristic/presentation only). |
| Integer surrogates | Compare **squared** Euclidean distance; scale octile to integers (orthogonal=2, diagonal=3 ≈ √2) so pathfinding costs stay byte-identical. |

The tactics spec already mixes these ("Manhattan for orthogonal, Chebyshev for diagonal
abilities") — the skill must name the metric per movement rule, not default to one.

### 2.2 Hexagonal grids (the canonical Red Blob Games algebra)

The skill ships the full hex coordinate algebra — this is the single largest gap today.
Four coordinate families, each with a job:

| Family | Store | Use for | Warning |
|---|---|---|---|
| **Cube** `(q,r,s), q+r+s=0` | — | all algorithms (distance, line, ring, rotation) — behaves like a 3D vector | keep the invariant |
| **Axial** `(q,r), s=−q−r` | ✅ storage | compact logic coords | — |
| **Offset** `(col,row)` × {odd/even-r, odd/even-q} | ✅ rect maps | human/rectangular layout | **no vector math** — convert to cube first |
| **Doubled** (doublewidth/height) | ✅ | parity-free neighbours | one axis doubled |

Required functions (transcribed to `[<Struct>]` value types, integer arithmetic):

- **Conversions** — `axialToCube`/`cubeToAxial`; all four offset variants × both parities
  (`oddrToAxial`, `evenrToAxial`, `oddqToAxial`, `evenqToAxial` + inverses); doubled ↔ axial.
  These parity/sign divisions are the #1 hex bug — every one gets a round-trip property test.
- **Neighbours** — 6 cube directions `(+1,−1,0)(+1,0,−1)(0,+1,−1)(−1,+1,0)(−1,0,+1)(0,−1,+1)`;
  6 diagonals; parity-branching offset neighbours (prefer converting to cube).
- **Distance** — `(|Δq|+|Δr|+|Δs|)/2 = max(|Δq|,|Δr|,|Δs|)`.
- **Rounding** — `cubeRound`: round each component, reset the max-error one to restore `q+r+s=0`.
- **Line** — `cubeLerp` + `cubeRound` over `N = distance` steps; fixed epsilon nudge for edge ties.
- **Ring / spiral / range** — `ring(c,R)` (`|ring|=6R`), `spiral(c,R)` (`1+3R(R+1)`),
  `range(c,N)` (double-loop over q, clamped r) and `rangeIntersection` (intersect the three
  cube coordinate intervals).
- **Rotation / reflection** — 60° `rotateRight (x,y,z)=(−z,−x,−y)`, `rotateLeft=(−y,−z,−x)`;
  reflects `reflectQ=(q,s,r)` etc.
- **Layout (presentation only)** — pointy/flat `hexToPixel`/`pixelToHex` (`√3` math kept in the view).

### 2.3 Isometric / staggered

Iso and diamond are a **rendering transform only** (`screenX=(x−y)·tileW/2`,
`screenY=(x+y)·tileH/2` for 2:1). The logical grid stays square/hex integer; iso offsets
never enter game logic (same offset-conversion trap as hex offset coords).

---

## 3. Storage & spatial index

### 3.1 Grid storage variants

| Type | Backing | For |
|---|---|---|
| `Grid<'T>` | dense row-major `ImmutableArray` (`index = y*width + x`) | bounded maps, best cache locality |
| `SparseGrid<'T>` | `Map<Coord,'T>` (sorted ⇒ deterministic iteration) | sparse/large, occupancy-proportional memory |
| `TorusGrid<'T>` | dense + floor-mod wrap | wrapping worlds (`((v%n)+n)%n`) |
| `ChunkedGrid<'T>` | `Map<ChunkCoord, Chunk>` | infinite/streamed worlds; empty chunks cost nothing |

Persistent ops (`get/tryGet/set/update`, `neighbors`, `map/fold` in **stable order**,
`flatten/unflatten`, `contains`). One documented row-major convention; a mutable
array-builder fast path frozen at boundaries for hot loops.

### 3.2 Spatial partitioning — structure choice

Broad-phase is the shared engine under collision (candidate pairs), FOV/pathfinding (neighbour
queries), and gameplay ("what's near X?"). Ship a small menu with clear guidance:

| Structure | Query (avg) | Update | Best for | Notes |
|---|---|---|---|---|
| **Uniform grid / spatial hash** ✅ default | ~O(1)+k | O(1) relink on cell cross | uniform density, similar sizes, moving objects | degrades if all in one cell; cell size ≈ ~2× object/query radius |
| **Hierarchical spatial hash** | ~O(1) | O(1) | **widely varying object sizes** | one cell size per level |
| **Quadtree** | O(log₄ n) | remove+reinsert | non-uniform density, static-ish | small objects can stick in big nodes |
| **Loose quadtree** | O(log n) | instant, no alloc | moving varied-size objects | nodes overlap (2×) to kill hotspots |
| **k-d tree** | O(log n) NN, O(√n+k) range 2D | rebuild | static point sets, kNN | median split |
| **Sweep-and-prune** | O(n+k) reports | incremental resort | many AABBs, temporal coherence | near-O(n) when movement is small |
| **BVH (AABB tree)** | O(log n) | refit bounds | dynamic varied shapes | grid-independent |

**Recommendation:** ship the **uniform grid / spatial hash** as the default broad phase
(matches today's `SpatialGrid`, best fit for tile games and tactics), add the
**hierarchical** variant for mixed sizes and **loose quadtree** for free-moving scenes; keep
k-d/SAP/BVH as documented options behind a common **`IBroadPhase`** interface returning
candidate pairs.

Uniform-grid mechanics the skill must specify: cell-coord `(⌊x/cs⌋,⌊y/cs⌋)`; per-cell bucket
(O(1) add, O(1) unlink); spatial-hash `h=(x·p1) XOR (y·p2) mod N` for unbounded space;
range/radius query = sweep overlapping cells then exact-distance filter; **kNN** = spiral
outward, stop when the k-th candidate is closer than the nearest unexplored ring;
**pair dedup** by canonical `idA < idB` (boundary double-count fix).

### 3.3 Indexing concerns to document

Row-major vs column-major (pick + document one); bounds policy (clamp / wrap / `option`);
floor-mod for negatives; chunk/local split `⌊world/chunk⌋`, `world mod chunk`; **Morton /
Z-order** linearization for cache-resident region scans (power-of-two cell size ⇒ shift/mask
instead of `/`,`%`); sparse `Map` over `Dictionary` for deterministic enumeration.

---

## 4. Determinism (first-class requirement)

| Rule | Why |
|---|---|
| **Ordered containers only** in sim loops (`Map`/`Set`, or sort-by-key before iterating) | `HashSet`/`Dictionary` enumeration order is unspecified across runtimes/platforms → lockstep desync |
| **Integer logic coords**; float only at presentation | `√3`/`√2`/Euclidean not byte-identical across JIT/arch |
| **Fixed neighbour order + explicit tie-breaks** | rings/lines/kNN/A* all have ties; reproducibility needs a canonical order |
| **Fixed epsilon** for hex-line nudge (not RNG) | edge ties resolve identically everywhere |
| **`cubeRound` restores `q+r+s=0`** | independent rounding breaks the invariant |
| **Persistent structures with structural sharing** (`Map`,`Set`,`ImmutableArray`) | value semantics, safe sharing, MVU-friendly; mutable array fast path frozen at edges |

---

## 5. Performance

Dense row-major arrays + Morton ordering for region scans; **struct-of-arrays** for broad-phase
streaming; O(1) grid clear via generational/frame-counter cell versioning (no realloc);
`Span<'T>`/`ReadOnlySpan<'T>` and `[<Struct>]` coordinate types to avoid heap traffic and
indirection; power-of-two cell sizes → bit ops; no LINQ/`seq` allocation in inner query loops.

---

## 6. Testing

- **Round-trip property tests** (FsCheck): `cube↔axial`, `axial↔offset` (all 4 variants ×
  parities), `axial↔doubled`, `hex↔pixel`, dense `flatten↔unflatten` — round-trip is the
  canonical PBT and catches the parity/off-by-one bugs.
- **Metric laws:** non-negativity, identity, symmetry, triangle inequality; the two
  cube-distance forms agree; `distance = length(line)`.
- **Neighbour/ring/range invariants:** neighbour symmetry, distance-1, exact counts
  (`|ring|=6R`, `|spiral|=1+3R(R+1)`, `|range|=1+3N(N+1)`), `q+r+s=0` preserved everywhere.
- **Broad-phase equivalence:** grid/quadtree candidate set ⊇ brute-force ground truth (no
  false negatives); range results == brute-force filter.
- **Determinism:** same input → byte-identical output across repeated runs and across OS/arch
  in CI; serialize + diff. Golden/snapshot tests (fixed-width text) per the repo convention.

---

## 7. API surface (what "comprehensive" means)

- **Coordinate types** (`[<Struct>]`): `Coord`, `Cube`, `Axial`, `Offset(variant)`, `Doubled` +
  vector ops on cube/axial.
- **`Hex` module:** conversions (all variants), `neighbor(s)`, `diagonals`, `distance`, `round`,
  `line`, `ring`, `spiral`, `range`, `rangeIntersection`, `rotateL/R`, `reflectQ/R/S`,
  `hexToPixel`/`pixelToHex`.
- **`Square` module:** `neighbors4/8`, `manhattan/chebyshev/octile/euclidean²`, `line`
  (Bresenham + supercover), `range`, `ring`.
- **Storage:** `Grid`/`SparseGrid`/`TorusGrid`/`ChunkedGrid` with persistent ops.
- **Index:** `SpatialHash`/`UniformGrid` (`insert/remove/move`, `queryRange/Radius`, `kNearest`,
  `pairs`), `Quadtree`/`LooseQuadtree`/`KdTree`, optional `SweepAndPrune`/`BVH`, common
  `IBroadPhase`.
- **Grid-agnostic helpers** consumed by siblings: `neighbors`/`cost` predicates feeding
  BFS/Dijkstra/A* (pathfinding), `line` (LOS), shadowcasting (FOV).
- **Presentation edge:** hex/iso/orthogonal screen projection kept in Elmish view helpers.

---

## 8. Pitfalls to design out

| Pitfall | Mitigation |
|---|---|
| Grid edge off-by-one / row-vs-col-major mix | one documented layout + flatten round-trip PBT |
| Wrong diagonal distance | metric-by-movement-rule; triangle-inequality test |
| Hex offset conversion (parity/sign) errors | logic in cube/axial; convert only at edges; round-trip all variants |
| Offset coords used for vector math | forbid arithmetic on `Offset`; convert to cube first |
| Hex line misses a hex / edge tie | fixed deterministic epsilon; distance test |
| Hash boundary pair double-count | dedupe canonical `idA<idB` / cache tested pairs |
| Small object stuck in huge quadtree node | loose quadtree / store in overlapping leaves |
| Cell size mismatch | tune to object/query size; hierarchical grid for varied sizes |
| Nondeterministic iteration | ordered `Map`/`Set` or sort-before-iterate |
| Float divergence across platforms | integer/scaled surrogates; float only in presentation |
| Negative-coordinate modulo | floor-mod `((v%n)+n)%n` |
| Rounding breaks `q+r+s=0` | `cubeRound` resets max-error component |

---

## 9. Effort & placement

- **Home:** `FS.GG.Game.Core` as `FS.GG.Game.Core.Grid` (coordinate algebra + storage) and an
  extended `FS.GG.Game.Core.SpatialGrid` (index/broad-phase); depends *down* only. Never Governance.
- **Phasing:** P0 square metrics/neighbours/line + dense/sparse storage (S); P1 full hex algebra
  (M — the biggest doc/code gap); P2 broad-phase menu behind `IBroadPhase` + kNN/pairs (M);
  P3 chunked/torus + Morton perf (S–M). Each is an additive Rendering surface-bump → skill-registry
  reconcile (`fs-gg-game-core`/grids row).
- **Contract note:** growing the `fs-gg-game-core`/grids skill body is a Rendering library-surface
  bump the `.github` skill-registry must reconcile (`registry = manifest = bytes`), same discipline
  as the 13→16 catch-up.

---

## 10. Sources

Red Blob Games — [Hexagonal Grids](https://www.redblobgames.com/grids/hexagons/) ·
[hex implementation](https://www.redblobgames.com/grids/hexagons-v1/implementation.org) ·
[Introduction to A*](https://www.redblobgames.com/pathfinding/a-star/introduction.html).
Robert Nystrom — [Spatial Partition (*Game Programming Patterns*)](https://gameprogrammingpatterns.com/spatial-partition.html).
[Thatcher Ulrich — loose octrees](https://www.tulrich.com/geekstuff/partitioning.html) ·
[Build New Games — broad-phase via spatial partitioning](http://buildnewgames.com/broad-phase-collision-detection/) ·
[Sweep and prune (Wikipedia)](https://en.wikipedia.org/wiki/Sweep_and_prune) ·
[Buss et al. — enhanced sweep-and-prune (PDF)](https://mathweb.ucsd.edu/~sbuss/ResearchWeb/EnhancedSweepPrune/SAP_paper_online.pdf) ·
[Teschner et al. — optimized spatial hashing](https://www.researchgate.net/publication/2909661_Optimized_Spatial_Hashing_for_Collision_Detection_of_Deformable_Objects) ·
[NVIDIA GPU Gems 3 ch.32 — broad-phase](https://developer.nvidia.com/gpugems/gpugems3/part-v-physics-simulation/chapter-32-broad-phase-collision-detection-cuda) ·
[k-d tree vs quadtree comparison](https://github.com/amay12/SpatialSearch) ·
[Quadtree (Wikipedia)](https://en.wikipedia.org/wiki/Quadtree) ·
[Z-order curve / Morton codes (Wikipedia)](https://en.wikipedia.org/wiki/Z-order_curve) ·
[RogueBasin — comparative FOV study](https://www.roguebasin.com/index.php/Comparative_study_of_field_of_view_algorithms_for_2D_grid_based_worlds) ·
[Voxel.Wiki — chunking](https://voxel.wiki/wiki/chunking/) ·
[Deterministic lockstep (Gaffer On Games)](https://gafferongames.com/post/deterministic_lockstep/) ·
[Game-engine determinism: iteration order](https://galsov.com/2026/01/18/game-engine-determinism-iteration-order/) ·
[Immutable data structures in F# (Compositional IT)](https://www.compositional-it.com/news-blog/immutable-data-structures-in-f/) ·
[Round-trip properties in PBT (UPenn PL Club)](https://www.cis.upenn.edu/~plclub/blog/2023-12-07-round-trip-properties/) ·
[Tulleken — Hex grid geometry (PDF)](https://gamelogic.co.za/downloads/HexMath2.pdf).
