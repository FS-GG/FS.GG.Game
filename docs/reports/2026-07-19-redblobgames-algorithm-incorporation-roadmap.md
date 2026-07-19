# Design roadmap — Red Blob Games algorithm incorporation

- **Date:** 2026-07-19
- **Owner:** `FS.GG.Game` (FS.GG.Game.Core / FS.GG.Game.Harness)
- **Status:** Design proposal (pre-charter). A menu of candidate work items, sized and sequenced.
- **Scope:** Twelve algorithm/topic candidates surveyed from [Red Blob Games](https://www.redblobgames.com/),
  filtered to what fits a **deterministic, integer-cost, grid-based game-logic library**, ranked
  into three tiers, each with a design sketch, and closed by a milestone roadmap.
- **Language/target:** F# on `net10.0`, functional-first, **byte-identical deterministic**
  (hard requirement — equal-cost alternatives resolved by a stable documented rule), integer-grid,
  immutable, Elmish/MVU-friendly.
- **Sibling designs:** [pathfinding & navigation](2026-07-05-game-logic-pathfinding-navigation-design.md) ·
  [grids & spatial partitioning](2026-07-05-game-logic-grids-spatial-partitioning-design.md) ·
  [collision](2026-07-05-game-logic-collision-detection-design.md) ·
  [line of sight](2026-07-05-game-logic-line-of-sight-design.md) ·
  [field of view / visibility](2026-07-05-game-logic-field-of-view-visibility-design.md) ·
  [AI decision layer](2026-07-10-ai-decision-layer-design.md).
- **Grounding surface (today):** `FS.GG.Game.Core.Pathfinding` (A*/BFS, forward/multi-source
  Dijkstra, flow field), `Ai` (`fleeField`), `Grids`/`SpatialGrid`/`Primitives`, `Fov`/`Los`/`Visibility`,
  `Physics`/`Ballistics`/`Collision`, `Rng`.

---

## 0. What this document is (and is not)

A sweep of every Red Blob Games article, filtered to the subset that a *deterministic game-logic*
library can actually absorb, then turned into a buildable plan. It is **not** a commitment to build
all twelve — it is the menu plus a recommended path through it. Each candidate carries the same
five fields so they can be compared and charter-scoped independently: **what it is**, **Red Blob
source**, **fit against current surface**, **design sketch** (proposed public shape), and
**determinism notes**. The [milestone roadmap](#the-roadmap) at the end sequences the ones worth
doing first.

The library already ships the whole Red Blob *getting-started* pathfinding tier — A*, BFS, Dijkstra
maps, flow fields, deterministic tie-breaking, early exit. Everything below is deliberately the
material **beyond** the introductory articles: the optimizations, the adjacent grids, and the
combat/visibility math.

---

## 1. The spine: determinism constrains every candidate

Every algorithm below inherits the organizing constraint from the
[pathfinding design](2026-07-05-game-logic-pathfinding-navigation-design.md):

> The same input must produce a byte-identical result on every run and platform, and equal-cost
> alternatives must be resolved by a **stable, documented rule — never by heap internals, hash-map
> iteration order, or floating-point tie-breaks.**

This is the single filter that reshapes Red Blob's advice, because most of it is written for
float-based engines. It has three consequences that recur in the sketches below:

1. **Ordering keys carry their own tie-break.** Any new priority-queue user folds the tie-break
   into a strict total order (as `Pathfinding` already does with `(f, h, Col, Row)`), so queue
   instability is irrelevant.
2. **No floats on the deterministic path.** Angle sorts (visibility), landmark distances (ALT),
   any-angle line-of-sight (Theta*), and dice math must be integer or exact-rational. Where Red
   Blob uses `atan2`/`sqrt`, we substitute integer cross-products, squared distances, and
   pseudo-angle keys.
3. **Presentation-only work stays out of Core.** Procedural generation, curves, and rendering-tier
   smoothing either live in a non-deterministic harness/authoring layer or are excluded.

---

## 2. Tier 1 — high value, strong fit, determinism-safe

These are drop-in accelerations and correctness aids to code that already exists. Lowest risk,
highest ROI, no new external concepts for consumers.

### 1.1 Jump Point Search (JPS)

- **What it is:** A grid-specialized A* that "jumps" over runs of symmetric intermediate nodes on
  uniform-cost grids, expanding an order of magnitude fewer nodes with **no preprocessing** and an
  identical result to A*.
- **Source:** [Grid pathfinding optimizations](https://www.redblobgames.com/pathfinding/grids/algorithms.html).
- **Fit:** Purpose-built for *exactly* our case — uniform-cost grid, 4/8-neighbour, no corner
  cutting. Highest ROI in the whole survey. Slots in beside `astar` sharing the same walkability
  predicate and neighbourhood.
- **Design sketch:** `Pathfinding.jps neighbourhood maxVisited isWalkable start goal : Cell list option`,
  a sibling to `astar` returning the *same* path under the *same* tie-break rule. Implemented as
  A* whose successor generation is the JPS jump/prune step rather than the raw neighbour set.
  Falls back to `astar` semantics on weighted terrain (JPS assumes uniform cost).
- **Determinism notes:** Jump order and forced-neighbour ordering must be a documented total order;
  the produced path must be **byte-identical to `astar`** on the same input, which becomes the
  primary property test (differential oracle against the existing A*).

### 1.2 Connected-component early-out

- **What it is:** Label each maximal walkable region once; reject an unreachable `start→goal` in
  O(1) instead of exhausting `maxVisited`.
- **Source:** [A* implementation](https://www.redblobgames.com/pathfinding/a-star/implementation.html)
  ("Preprocess with connected-component labeling to avoid full exploration on disconnected regions").
- **Fit:** Today `astar`/`bfs`/`reachable` fully explore before reporting "no path." On maps with
  walls/islands that is the worst case on every failed query.
- **Design sketch:** A `Pathfinding.Regions` value built from `(bounds, isWalkable, neighbourhood)`
  via deterministic flood fill; `Regions.sameComponent a b : bool` guards the search. Optional
  fast-path parameter on the search entry points, or a thin wrapper `astarWithin regions …`.
- **Determinism notes:** Labels assigned in scan order over a bounded region; label *ids* never
  leak into results (only the boolean predicate is exposed), so relabeling can't change output.

### 1.3 Landmark (ALT) heuristic

- **What it is:** Precompute exact distances from a handful of pivot ("landmark") cells; the
  triangle inequality then yields a far tighter admissible heuristic than octile → fewer
  expansions on large/open maps.
- **Source:** [Grid optimizations](https://www.redblobgames.com/pathfinding/grids/algorithms.html)
  (Goldberg–Harrelson pivot heuristic).
- **Fit:** Reuses the existing multi-source `distanceField` to build each landmark table. Purely
  additive: a better `heuristic` plugged into the same A* loop.
- **Design sketch:** `Pathfinding.Landmarks.build neighbourhood cost pickN bounds : Landmarks` and
  `Landmarks.heuristic landmarks goal cell : int`; passable to `astar` via an optional heuristic
  override. Landmark selection is deterministic (farthest-point sampling from a fixed seed cell).
- **Determinism notes:** All integer distances; landmark set is a pure function of the map, so the
  heuristic is reproducible. Admissibility (max over `|d(L,goal) − d(L,cell)|`) preserves optimality.

### 1.4 Straighter-path tie-break

- **What it is:** Nudge the heuristic tie-break so equal-`f` frontiers prefer nodes on the straight
  line to the goal — straighter paths *and* fewer expansions, at zero optimality cost.
- **Source:** [A* implementation](https://www.redblobgames.com/pathfinding/a-star/implementation.html)
  (cross-product / checkerboard tie-breaking).
- **Fit:** We already tie-break deterministically by `(f, h, Col, Row)`; this replaces the `h`/
  positional component with an integer cross-product term.
- **Design sketch:** Extend the ordering key to `(f, h, cross, Col, Row)` where `cross =
  |dx1*dy2 − dx2*dy1|` (all integer). Behind an opt-in `pathBias` flag to keep the existing
  byte-identical outputs stable for current consumers.
- **Determinism notes:** Integer cross-product only; still a strict total order. Because it changes
  outputs, it is **versioned/opt-in**, not a silent change to `astar`.

---

## 3. Tier 2 — new capability, clear game use, moderate effort

Genuine capability gaps. Each is a new module or a substantial extension, but each maps to a
concrete game need already visible in the test-spec catalogue (tactics, roguelike, tower-defense).

### 2.1 Hexagonal grids

- **What it is:** A full hex coordinate/algorithm module — the single biggest *capability* hole,
  since the library is square-grid only today.
- **Source:** [Hex grids](https://www.redblobgames.com/grids/hexagons/) +
  [implementation](https://www.redblobgames.com/grids/hexagons/implementation.html).
- **Fit:** New `Hex.fs`/`.fsi` alongside `Primitives`/`Grids`. The existing A*/BFS/Dijkstra work
  **unchanged** over a hex neighbour function — pathfinding is coordinate-agnostic already.
- **Design sketch:** A `Hex` type in **cube/axial** (`q, r, s` with `q+r+s=0`), plus:
  `distance`, `neighbors`, `add`/`subtract`/`scale`, `rotate` (`[q,r,s]→[-r,-s,-q]`),
  `round` (`cube_round` re-projecting the largest-error axis), `range`/`ring`/`spiral`,
  `lineDraw`, and offset/doubled `<->` axial converters for rectangular-map storage. Store in the
  format convenient to the map; run algorithms in cube. Pixel↔hex conversion lives in the render
  adapter, not Core (float boundary).
- **Determinism notes:** Cube coordinates are integer; `round` needs care only at the pixel
  boundary, which stays out of Core. Everything in `Hex` is integer and total.

### 2.2 Any-angle pathfinding (Theta* / funnel smoothing)

- **What it is:** Produce paths that cut corners along true lines of sight instead of zig-zagging
  cell-to-cell — natural movement for continuous-position units.
- **Source:** [Grid optimizations](https://www.redblobgames.com/pathfinding/grids/algorithms.html)
  (Theta*, any-angle).
- **Fit:** We have continuous `Physics`/`Collision` geometry but grid-locked paths, so units
  currently step along cell centres. Two options: **Theta*** (parent-check during search) or a
  cheaper **post-hoc string-pull/funnel** over an A* path. Recommend starting with post-hoc smoothing
  layered on `astar`, since it reuses the existing search untouched.
- **Design sketch:** `Pathfinding.smooth losClear path : Point list`, where `losClear a b` is an
  integer grid line-of-sight check (reuse `Los`). Optionally `theta` as a first-class search later
  if smoothing proves insufficient.
- **Determinism notes:** The LOS check must be the existing **integer** `Los` (Bresenham/
  supercover), not a float ray — keeps smoothed paths byte-identical.

### 2.3 Tile edges & vertices

- **What it is:** Address the *edges* and *corners* between tiles, not just the tiles — so features
  can live on boundaries: thin walls, doors, fences, rivers, borders, fortified corners.
- **Source:** [Grid parts and relationships](https://www.redblobgames.com/grids/parts/).
- **Fit:** `Grids` is tile-centric; the walkability predicate can only block whole cells. Many
  tactics/roguelike features need a wall *between* two open tiles. Complements, doesn't replace,
  the tile model.
- **Design sketch:** Edge address `(Cell, Dir)` (canonicalized to N/W to dedupe shared edges) and
  vertex address `(Cell, Corner)`; the nine Red Blob relationships (`neighbors`, `borders`,
  `corners`, `joins`, `endpoints`, `touches`, `protrudes`, `adjacent`, `continues`) as pure
  `input -> output list` functions. Pathfinding gains an edge-aware neighbour function:
  `isEdgePassable : Cell -> Cell -> bool` so movement between adjacent open cells can still be
  blocked by a wall on their shared edge.
- **Determinism notes:** Canonical edge/vertex representatives make enumeration order stable; all
  integer.

### 2.4 2D visibility polygon (segment sweep)

- **What it is:** An angular sweep over wall-segment endpoints producing a *continuous* visibility
  polygon — the right tool for light sources, vision cones, and fog-of-war over segment geometry,
  as opposed to grid shadowcasting.
- **Source:** [2D visibility](https://www.redblobgames.com/articles/visibility/).
- **Fit:** `Fov`/`Los`/`Visibility` are grid cell-flagging (roguelike shadowcasting). This is
  complementary: it operates on the continuous `Collision` line-segment world already in the
  library and yields a polygon usable for lighting and guard cones. **Highest determinism risk**
  in Tier 2.
- **Design sketch:** `Visibility.polygon origin segments : Point list` via the endpoint-sorted
  sweep (add walls starting at an endpoint, remove walls ending, emit a triangle when the nearest
  wall changes). Nearest-wall test by **squared** distance; angle ordering by an integer
  **pseudo-angle** (quadrant + slope) rather than `atan2`.
- **Determinism notes:** Exact-rational or fixed-point intersection points; documented total order
  on endpoints (angle, then distance, then start-before-end). If exact intersection proves costly,
  scope this to a harness/authoring visualization first and promote to Core once the numeric
  contract is proven.

---

## 4. Tier 3 — niche, lower priority, or presentation-adjacent

Real but narrower value, or partially subsumed, or sitting near the deterministic boundary. Pick
up opportunistically or when a specific target game asks.

### 3.1 Damage-roll probability

- **What it is:** Combat math — dice distributions, advantage/disadvantage (min/max of rolls),
  drop-lowest, exploding/critical dice, and convolution to combine independent rolls.
- **Source:** [Probability for RPG damage](https://www.redblobgames.com/articles/probability/damage-rolls.html).
- **Fit:** Sits on `Rng`. Only worth building when a target game needs combat resolution;
  `Effects`/`Ballistics` currently model damage without a distribution layer.
- **Design sketch:** A `Dice` module: `roll`, `advantage`/`disadvantage`, `dropLowest`,
  `exploding`, plus a pure `Distribution` type (integer weight map) with `convolve`, `mean`,
  `variance`, and `sample rng` for the actual draw. Distribution math is deterministic given the
  `Rng` seed.
- **Determinism notes:** Integer weights; sampling threads the existing seeded `Rng`.

### 3.2 All-pairs / distance-to-seed tables

- **What it is:** Precomputed distance tables between many source/target cells for influence maps
  and AI target selection.
- **Source:** [All-pairs shortest paths](https://www.redblobgames.com/pathfinding/all-pairs/) ·
  [Distance to any](https://www.redblobgames.com/pathfinding/distance-to-any/).
- **Fit:** Largely **subsumed** by `distanceField` (multi-source Dijkstra) already shipped. The
  incremental value is a cached, queryable table and a documented influence-map recipe for `Ai`.
- **Design sketch:** Thin wrapper `Ai.influenceMap` / cached `DistanceTable` over repeated
  `distanceField` calls; document the pattern rather than adding a new algorithm.
- **Determinism notes:** Inherits `distanceField`'s determinism.

### 3.3 Line & circle drawing (range/AoE templates)

- **What it is:** Bresenham-style grid lines and rasterized circles for range rings and AoE
  templates.
- **Source:** [Line drawing](https://www.redblobgames.com/grids/line-drawing/) ·
  [Circle drawing](https://www.redblobgames.com/grids/circle-drawing/).
- **Fit:** `Los` almost certainly already carries a line routine; **first action is to confirm and
  reuse it** rather than duplicate. Circle/disc rasterization for AoE templates is the genuinely
  new bit.
- **Design sketch:** `Grids.disc center radius : Cell seq` and `Grids.ring center radius`, sharing
  `Los`'s line primitive. Integer midpoint-circle algorithm.
- **Determinism notes:** Integer midpoint circle; stable emission order.

### 3.4 Bucketed / binned priority queue

- **What it is:** Replace the log-time PQ with O(1) integer buckets when the `f`-range is small.
- **Source:** [Grid optimizations](https://www.redblobgames.com/pathfinding/grids/algorithms.html).
- **Fit:** Only worth it if profiling shows the immutable-`Set` frontier is the bottleneck. Must
  preserve the exact `(f, h, …)` total order.
- **Design sketch:** An internal `IntBucketQueue` behind the existing frontier abstraction; swap in
  under a benchmark gate. No public surface change.
- **Determinism notes:** Buckets keyed by integer `f`, ties resolved by the same secondary key —
  identical outputs to the current queue (differential test).

### 3.5 Deferred / likely out of scope

- **[Pathfinding around circular obstacles](https://redblobgames.github.io/circular-obstacle-pathfinding/)**
  — continuous bitangent navigation; niche, float-heavy. Defer unless a game needs smooth
  circle-avoidance the grid can't express.
- **[Curved paths / Bézier](https://www.redblobgames.com/articles/curved-paths/)** — presentation-tier
  smoothing; belongs in the render adapter, not Core.
- **[Noise](https://www.redblobgames.com/articles/noise/introduction.html) /
  [terrain-from-noise](https://www.redblobgames.com/maps/terrain-from-noise/) /
  [polygonal map generation](https://www.redblobgames.com/maps/mapgen2/)** — procedural *content*
  generation. Out of scope for a deterministic *logic* library, but a **seeded** noise/map
  generator could live in `Game.Harness` to author procedural playtest maps. Flagged, not planned.

---

## 5. Cross-cutting concerns

- **Module placement.** Tier 1 extends `Pathfinding`. `Hex`, edges/vertices, line/circle extend the
  grids family. `Dice` is new under Core. Visibility polygon starts in `Game.Harness`/authoring and
  promotes to Core only once its numeric contract is proven.
- **Determinism gate.** Each candidate ships with a property test asserting reproducibility, and —
  where it mirrors existing behaviour (JPS↔A*, bucket-PQ↔Set-PQ, straighter tie-break opt-in) — a
  **differential oracle** against the current implementation.
- **SDD lifecycle.** Each chartered candidate flows charter → specify → clarify → checklist → plan
  → tasks → analyze → implement → evidence → verify → ship, with FR→AC coverage. The roadmap below
  is expressed as work items, not merged PRs.
- **No silent output changes.** Anything that alters existing byte-identical outputs (1.4) is opt-in
  and versioned; consumers migrate deliberately.

---

## 6. The roadmap

Sequenced into four milestones by dependency and value density. Effort is rough
(**S** ≤ few days, **M** ~1 week, **L** ~2+ weeks) at the work-item grain.

### Dependency graph (what must precede what)

```
1.2 Regions ─┐
1.1 JPS ─────┼─(independent, share Pathfinding internals)
1.3 ALT ─────┘   depends on: distanceField (exists)
1.4 Tie-break    independent

2.1 Hex ─────────► (unlocks hex variants of 1.1/2.3/3.3 later)
2.3 Edges/Vertices ─► edge-aware neighbour ─► feeds 2.2 smoothing quality
2.2 Any-angle ──── depends on: Los (exists)
2.4 Visibility ─── depends on: Collision segments (exists); starts in Harness

3.x                mostly independent; 3.2 wraps distanceField; 3.4 gated by profiling
```

### Milestone M1 — "Faster and safer pathfinding" (Tier 1)

*Theme: accelerate and harden the search we already ship. No new consumer concepts.*

| # | Work item | Effort | Exit criteria |
|---|---|---|---|
| 1.1 | Jump Point Search | M | `jps` returns byte-identical paths to `astar`; differential property test green; benchmark shows ≥3× fewer expansions on open maps |
| 1.2 | Connected-component early-out | S | `Regions.sameComponent` guards all searches; failed-query on disconnected map is O(1); flood-fill determinism test |
| 1.3 | ALT landmark heuristic | M | Optional heuristic override; admissibility test (paths still optimal); expansion-count reduction benchmark |
| 1.4 | Straighter-path tie-break | S | Opt-in `pathBias`; existing outputs unchanged when off; straightness metric improves when on |

**Milestone exit:** search is measurably faster on large/open/disconnected maps with **zero
regression** to existing byte-identical outputs. All four land against the current `Pathfinding`
surface with no dependency on later milestones.

### Milestone M2 — "New grids and boundaries" (Tier 2, structural half)

*Theme: the capability gaps that unlock new game genres.*

| # | Work item | Effort | Exit criteria |
|---|---|---|---|
| 2.1 | Hexagonal grid module (`Hex`) | L | Cube/axial + offset/doubled converters, distance/neighbors/rotate/round/range/ring/spiral/line; A*/BFS/Dijkstra proven over hex neighbour fn; full property suite |
| 2.3 | Tile edges & vertices | M | Nine relationships as pure functions; canonical edge/vertex addressing; edge-aware neighbour blocks walls between open tiles; tactics-style wall scenario test |

**Milestone exit:** the library supports hex-grid games and boundary features (thin walls, doors,
rivers). 2.1 and 2.3 are independent and can run in parallel.

### Milestone M3 — "Continuous movement and sight" (Tier 2, continuous half)

*Theme: bridge the grid logic to the continuous physics world.*

| # | Work item | Effort | Exit criteria |
|---|---|---|---|
| 2.2 | Any-angle path smoothing | M | `smooth` over integer `Los`; smoothed paths byte-identical & never clip walls; layered on `astar` with no search change; edge-aware once 2.3 lands |
| 2.4 | 2D visibility polygon | L | Integer/exact sweep in `Game.Harness`; documented total order on endpoints; polygon-correctness tests vs. reference scenes; **promotion-to-Core decision** recorded once numeric contract holds |

**Milestone exit:** units move on smooth any-angle paths and the engine can compute continuous
vision/lighting polygons. 2.2 depends on `Los` (exists) and improves with 2.3; 2.4 is independent
and deliberately staged through the harness to de-risk the float boundary.

### Milestone M4 — "Combat math and templates" (Tier 3, on demand)

*Theme: opportunistic — charter each when a target game asks.*

| # | Work item | Effort | Exit criteria |
|---|---|---|---|
| 3.1 | Dice / damage distributions | M | `Dice` + `Distribution` (convolve/mean/variance/sample); seeded reproducibility test |
| 3.3 | Line & circle templates | S | Confirm/reuse `Los` line; add `disc`/`ring`; range/AoE template tests |
| 3.2 | Influence-map recipe | S | `Ai.influenceMap` wrapper over `distanceField` + documented pattern |
| 3.4 | Bucketed PQ (gated) | S | **Only if** profiling flags the frontier; differential test vs. `Set`-PQ; benchmark win |

**Milestone exit:** combat-oriented games have distribution math and templates; the PQ swap is
taken only if measurement justifies it.

### Deferred (revisit on explicit demand)

Circular-obstacle pathfinding, Bézier/curved paths (render tier), and noise/procedural map
generation (a seeded `Game.Harness` map author, if pursued at all).

### Recommended first cut

If only one milestone is funded now, take **M1** — it is pure upside on code already in production
(JPS + component early-out are the standouts), needs nothing from later milestones, and cannot
regress existing behaviour. **2.1 (hex)** is the highest-value single item overall and can start in
parallel with M1 since it touches a disjoint module.

---

## 7. Sources

- [Red Blob Games — index](https://www.redblobgames.com/)
- [Grid pathfinding optimizations](https://www.redblobgames.com/pathfinding/grids/algorithms.html) (JPS, Theta*, landmarks, binned queues, hierarchical)
- [A* implementation](https://www.redblobgames.com/pathfinding/a-star/implementation.html) (tie-breaking, early exit, component labeling, storage)
- [Introduction to A*](https://www.redblobgames.com/pathfinding/a-star/introduction.html) · [Tower defense pathfinding](https://www.redblobgames.com/pathfinding/tower-defense/)
- [Hexagonal grids](https://www.redblobgames.com/grids/hexagons/) · [Hex implementation](https://www.redblobgames.com/grids/hexagons/implementation.html)
- [Grid parts and relationships](https://www.redblobgames.com/grids/parts/)
- [2D visibility](https://www.redblobgames.com/articles/visibility/)
- [Line drawing](https://www.redblobgames.com/grids/line-drawing/) · [Circle drawing](https://www.redblobgames.com/grids/circle-drawing/)
- [Probability for RPG damage](https://www.redblobgames.com/articles/probability/damage-rolls.html)
- [All-pairs shortest paths](https://www.redblobgames.com/pathfinding/all-pairs/) · [Distance to any](https://www.redblobgames.com/pathfinding/distance-to-any/)
- [Circular-obstacle pathfinding](https://redblobgames.github.io/circular-obstacle-pathfinding/) · [Curved paths](https://www.redblobgames.com/articles/curved-paths/) · [Noise](https://www.redblobgames.com/articles/noise/introduction.html) · [Terrain from noise](https://www.redblobgames.com/maps/terrain-from-noise/)
