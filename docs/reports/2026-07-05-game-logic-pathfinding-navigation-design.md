# Game logic skill — Pathfinding & navigation

> **Provenance (relocated 2026-07-06, ADR-0022 open-decision #3).** This game-logic library design was authored **2026-07-05 in FS-GG/.github** — *before* the FS.GG.Game extraction — and relocated here to live with the code it designs. Where the original homed these primitives in `FS.GG.UI.Canvas` / `FS.GG.UI.Scene` / `FS.GG.Rendering`, that home is now **`FS.GG.Game.Core`** — the BCL-only bottom layer; ADR-0022 P0–P5 moved `Rng`/`FixedStep`/`Pathfinding`/`SpatialGrid` out of `FS.GG.UI.Canvas` and the `Geometry` collision module out of `FS.GG.UI.Scene`. Direct surface references below were updated to `FS.GG.Game.Core.*`; the surrounding design narrative is preserved as the dated record.

- **Date:** 2026-07-05
- **Owner:** `.github` (cross-repo coordination); **implementation home:** `FS.GG.Game` (FS.GG.Game.Core)
- **Status:** Design proposal (pre-ADR). Deepens an existing thin surface into a comprehensive skill.
- **Scope:** The **pathfinding & navigation** game-logic skill — what
  `FS.GG.Game.Core.Pathfinding` (today: deterministic A*/BFS over a walkability predicate,
  4/8-neighbour, byte-identical paths) should grow into, in **docs** and **source code**.
- **Language/target:** F# on `net10.0`, functional-first, **byte-identical deterministic**
  (hard requirement — equal-cost alternatives resolved by a stable documented rule),
  integer-grid, immutable, Elmish/MVU-friendly.
- **Sibling designs (this batch):** [grids](2026-07-05-game-logic-grids-spatial-partitioning-design.md) ·
  [collision](2026-07-05-game-logic-collision-detection-design.md) ·
  [line of sight](2026-07-05-game-logic-line-of-sight-design.md) ·
  [field of view / visibility](2026-07-05-game-logic-field-of-view-visibility-design.md).
- **Grounding:** `turn-based-tactics` §4.4 (Dijkstra reachability to a MP budget, `cameFrom`
  reconstruction, blocked/occupied tiles), `tower-defense` (creep pathing), `fs-gg-game-core`
  (already ships deterministic A*/BFS with "byte-identical paths").

---

## 1. Determinism is the spine of this skill

The existing surface already promises **byte-identical paths**. That promise is the design's
organizing constraint, and it is more fragile than the algorithms:

> The same input must produce a byte-identical path on every run and platform, and equal-cost
> alternatives must be resolved by a **stable, documented rule — never by heap internals or
> hash-map iteration order.**

Two facts from the literature make this the #1 risk: (1) A*'s heuristic may not yield a
uniquely preferred node, so ties are "broken arbitrarily, meaning an implementation of A* can
be non-deterministic"; (2) a binary-heap priority queue is **unstable** — it breaks ties by
internal heap order. **Design consequence (the single most important decision in the skill):**
fold the tie-break rule *into the ordering key itself* so the queue only ever sees a strict
total order — then heap instability becomes irrelevant.

---

## 2. Core algorithms — one skeleton, four disciplines

BFS, Dijkstra, greedy best-first, and A* are the same loop (frontier + `cameFrom` +
`costSoFar`), differing only in the frontier priority:

| Algorithm | Frontier | Priority | Weighted? | Optimal? |
|---|---|---|---|---|
| **BFS** | FIFO | insertion order | no (unit cost) | yes on unit-cost grids |
| **Dijkstra** | priority queue | `g` | yes | yes |
| **Greedy best-first** | priority queue | `h` | ignores g | **no** (fast) |
| **A\*** | priority queue | `f = g + h` | yes | yes iff `h` admissible |

Canonical loop: `costSoFar[start]=0`, push start; pop lowest; **early-exit at goal** (single
target only — a full field must *not* early-exit); for each neighbour
`newCost = costSoFar[current] + cost(current,next)`; on unseen or **strict** `newCost <
costSoFar[next]` improvement, update cost + `cameFrom`, push with priority `newCost +
heuristic(goal,next)`. Reconstruct by walking `cameFrom` goal→start and reversing. Weighted
grids come from `cost(a,b)` returning terrain weight.

---

## 3. Heuristics

`h` must be in the **same units** as `g` (`D` = min edge cost):

| Heuristic | Formula | Admissible when |
|---|---|---|
| Manhattan (4-dir) | `D·(dx+dy)` | no diagonal moves |
| Octile (8-dir) ✅ | `D·(dx+dy) + (D2−2D)·min(dx,dy)`, `D=10,D2=14` | 8-connected uniform grid |
| Chebyshev (8-dir, diag=1) | `D·max(dx,dy)` | diagonal costs == cardinal |
| Euclidean | `D·√(dx²+dy²)` | any-angle (under-estimates on grid → slower) |

**Never use squared Euclidean** (overestimates → suboptimal). Admissible ⇒ optimal;
consistent/monotone ⇒ no re-expansion (Manhattan/octile/Euclidean on uniform grids are
consistent). **Weighted A\*** `f = g + w·h`, `w=1+ε` is a speed knob bounded to `(1+ε)×`
optimal — offer it as opt-in `suboptimalityBound`, **off by default** (it breaks the
"A* cost == Dijkstra cost" property test).

---

## 4. Deterministic tie-breaking — the ordering key *is* the tie-break

Two distinct tie-break concerns, both solved by one integer key:

- **Aesthetic** ("prefer straighter paths"): plain A* explores a fat diamond of equal-`f`
  nodes and returns zig-zags. The classic fixes (`h *= 1.001`, cross-product nudge) use
  **floats → determinism hazard**. Instead, express straightness as an **integer secondary
  sort key**.
- **Determinism** (unique minimum every pop): a **tertiary** key on the node.

The frontier key is a tuple compared lexicographically:

```
key = ( f = g + h ,          // primary: lower first
        h ,                   // secondary: among equal f, closer to goal ⇒ straighter (no float)
        packedCoord = y*width + x )   // tertiary: stable, guarantees a unique minimum
```

Because the key is a strict total order, **any** correct heap works — its instability is now
irrelevant. This is exactly the literature's recommendation: augment the priority with a
deterministic tie value so search order doesn't depend on the queue's own tie-breaking.

---

## 5. Grid acceleration — Jump Point Search

**JPS** (Harabor & Grastien) is an A* *accelerator returning identical optimal paths*,
expanding far fewer nodes by pruning path symmetry on the fly — **no preprocessing, no extra
memory**. **Strict applicability:** uniform-cost, 8-connected grid, cardinal=1, diagonal=√2.
It does **not** apply to weighted/terrain grids — so the skill ships general weighted A* *and*
auto-selects a JPS fast path only when the map is uniform-cost. Mechanics: prune **natural**
neighbours (reachable optimally without the current node), keep **forced** neighbours (blocked
alternatives near obstacles), and **jump** in the travel direction until an obstacle, the
goal, or a forced-neighbour jump point. **JPS+** precomputes per-direction jump distances for
**static** maps (another large speedup; precompute is a pure function of the map, cache by map
hash). The **corner-cutting policy (§7) must be baked into the jump/forced-neighbour tests**
or JPS will cut corners.

---

## 6. Many-to-one — Dijkstra maps & flow fields

When **many agents share a destination** (tower-defense creeps, monsters chasing the player),
do **not** run A* per agent. Build one field:

1. **Cost field** — per-tile terrain cost (walls impassable).
2. **Integration field / "Dijkstra map"** — multi-source Dijkstra **from the goal(s) outward**;
   each tile stores cheapest cost to a goal. **No early-exit** — fill the reachable set.
3. **Flow field** — each tile points to its lowest-integration neighbour; formally *the
   negative gradient of the Dijkstra map*.

One computation serves all agents (each just reads the vector under its feet). **Dijkstra maps
for roguelike AI** ("desire maps"): approach = roll downhill; **fleeing** = multiply every
value by ≈ **−1.2** and re-run the relaxation, then roll downhill (monsters flee toward the
nearest exit, sprint past a cornered player); the coefficient tunes global-vs-local safety;
sum weighted desire maps to combine behaviours — a cheap, expressive AI substrate the skill
should expose directly (`fleeField`, `distanceField`). **When flow fields win:** many agents,
few shared goals, frequent replans. **When they lose:** few agents with many distinct goals.

---

## 7. Constraints, hierarchy, post-processing

- **Movement constraints:** `DiagonalPolicy` (`Never | Always | NoCornerCutting |
  OnlyWhenNoObstacles`) — corner-cut guard asserts both orthogonally-adjacent cells are
  walkable before a diagonal; weighted terrain via `cost`; one-way edges; multi-tile units;
  the tactics **`reachableWithin` (movement range)** = Dijkstra capped at a MP budget
  (matches spec §4.4 exactly, incl. "path through allies but don't end on them" as a `cost`/
  `walkable` policy split).
- **Hierarchy for scale:** **HPA\*** (cluster the grid, precompute inter-cluster crossings into
  an abstract graph, search abstract then refine) and **HAA\*** (annotated for unit
  sizes/terrain classes). Precompute is pure; cache by map hash; determinism preserved if
  entrance ordering + intra-cluster A* are deterministic.
- **Post-processing:** **funnel / string-pulling** (Mononen — taut path through a portal
  corridor), **LOS simplification** (drop waypoint `i+1` when `i→i+2` has clear Bresenham LOS —
  reuses the LOS sibling), path smoothing. All deterministic: integer LOS, no float tolerances.
- **Dynamic replanning:** **D\* Lite** for changing maps (documented option).

---

## 8. Determinism & functional design (F#)

Everything is a pure `findPath : Grid → Coord → Coord → Path option` over immutable inputs
with persistent `cameFrom`/`costSoFar`.

| Rule | Why |
|---|---|
| **Integer costs only** (cardinal=10, diagonal=14; terrain multiplies) | `√2` drifts across JIT/arch; zero floats in the search |
| **Total-order key `(f, h, packedCoord)`** | equal `f` never resolved by heap internals |
| **Fixed compile-time neighbour order** (N,E,S,W,NE,SE,SW,NW) | identical-cost paths don't diverge on expansion order |
| **Strict `<` relaxation** (never `≤`) | ties never rewrite `cameFrom` ⇒ parents fully determined |
| **Persistent `Map` for `cameFrom`/`costSoFar`; index only, never iterate a `Dictionary`/`HashSet` for decisions** | iteration order is nondeterministic across runtimes |
| **Straightness via integer `h` secondary key**, not float nudges | optimal *and* byte-identical at once |

```fsharp
let CARD, DIAG = 10, 14                                  // √2 ≈ 1.4, integer-scaled
let heuristic (a: Coord) (b: Coord) =                    // octile, admissible + consistent
    let dx, dy = abs (a.X - b.X), abs (a.Y - b.Y)
    CARD * (dx + dy) + (DIAG - 2*CARD) * (min dx dy)
let orderKey width g h (c: Coord) = struct (g + h, h, c.Y * width + c.X)   // strict total order

let findPath (grid: Grid) start goal : Coord list option =
    let frontier = System.Collections.Generic.PriorityQueue<Coord, struct(int*int*int)>()
    frontier.Enqueue(start, orderKey grid.Width 0 (heuristic start goal) start)
    let mutable cameFrom  = Map.empty |> Map.add start start
    let mutable costSoFar = Map.empty |> Map.add start 0
    let mutable result = None
    while result.IsNone && frontier.Count > 0 do
        let current = frontier.Dequeue()
        if current = goal then result <- Some (reconstruct cameFrom start goal)
        else
            for next, stepCost in neighbors grid current do        // FIXED order; corner-cut guarded
                let newCost = costSoFar.[current] + stepCost
                match Map.tryFind next costSoFar with
                | Some old when newCost >= old -> ()                // STRICT '<' only
                | _ ->
                    costSoFar <- Map.add next newCost costSoFar
                    cameFrom  <- Map.add next current cameFrom
                    frontier.Enqueue(next, orderKey grid.Width newCost (heuristic next goal) next)
    result
```

`reconstruct` walks `cameFrom` goal→start and reverses. Dijkstra = `h≡0`; greedy = `h` only;
BFS = FIFO + unit cost. A closed-set `bool[,]` may be used as a *local* mutation inside the
function — the function stays observationally pure.

---

## 9. Performance

Binary heap default (ties pre-resolved in the key); **bucket queue / Dial's** for small
integer weights → O(1) ops, faster *and* trivially deterministic; cache-friendly `bool[,]`
closed set; early-exit single-target / no-exit fields; bounding box or max-cost cap
(`reachableWithin`); JPS cuts expansions 1–2 orders on uniform grids; HPA*/JPS+ precompute for
static large maps; **reuse one Dijkstra map** across all agents/frames until the map changes.

---

## 10. Testing

- **Optimality property:** `A*.cost == Dijkstra.cost` (and `JPS.cost == A*.cost`) — master
  correctness invariant.
- **Determinism:** same input → **byte-identical** path serialization across repeated runs,
  thread counts, and OS/arch (committed golden files). Also serialize node-expansion order in
  debug builds and assert it — catches tie-break regressions before they reach paths.
- **No-corner-cut invariant:** every diagonal step has both orthogonal neighbours walkable.
- **Unreachable:** walled-off goal ⇒ `None` (never exception / partial path).
- **Heuristic admissibility:** `heuristic(a,b) ≤ dijkstraDist(a,b)` for sampled pairs.
- **Flow-field consistency:** following the field from any reachable tile reaches a goal in
  exactly `dist[tile]` cost.
- **Golden maps** (mazes, open field + obstacle, choke) with committed expected paths.
- **Cross-platform CI matrix** (Linux/Windows/macOS, x64/arm64) on the golden byte-identical
  tests — the only real proof of the hard requirement.

---

## 11. API surface (what "comprehensive" means)

```fsharp
findPath        : Grid -> Coord -> Coord -> Path option           // deterministic A*/JPS auto-select
findPathWith    : SearchOptions -> Grid -> Coord -> Coord -> Path option
reachableWithin : Grid -> Coord -> budget:int -> Set<Coord>        // tactics move range (capped Dijkstra)
distanceField   : Grid -> goals:Coord list -> int[,]              // Dijkstra map (many-to-one)
flowField       : Grid -> int[,] -> Direction[,]                  // negative gradient
fleeField       : Grid -> goals:Coord list -> Direction[,]        // −1.2 rescan
smoothPath      : Grid -> Path -> Path                            // LOS-simplify / funnel
```

Config (pure, explicit): `Heuristic` (Manhattan | Octile | Chebyshev | Euclidean | Zero=Dijkstra,
integer forms), `DiagonalPolicy`, `cost`/`walkable` predicates, `suboptimalityBound` (off by
default), `tieBreak` (defaults to the coordinate-packed stable rule — the documented
determinism guarantee). Long searches can be a resumable `SearchState → SearchStep` for
cooperative scheduling without threads (MVU-friendly).

---

## 12. Pitfalls (ranked by how badly they break *this* skill)

| # | Pitfall | Mitigation |
|---|---|---|
| 1 | **Nondeterministic tie-breaking** (heap order, `Dictionary`/`HashSet` iteration, unordered neighbours) | total-order key + fixed neighbour array |
| 2 | **Float cost drift** (`√2`, `*1.001` nudges, Euclidean sqrt) | integer costs; straightness as integer secondary key |
| 3 | **Corner-cutting through walls** | `NoCornerCutting` in neighbour gen *and* JPS forced-neighbour tests |
| 4 | **Inadmissible heuristic → suboptimal** (squared Euclidean, Manhattan on 8-grid) | admissibility test; match heuristic to diagonal policy |
| 5 | **Ties overwriting `cameFrom`** (`≤` instead of `<`) | strict `<` relaxation only |
| 6 | **Early-exit on a field build** | early-exit single-target only; fields fill the reachable set |
| 7 | **JPS on a weighted grid** | auto-select JPS only for uniform-cost maps |

---

## 13. Effort & placement

- **Home:** `FS.GG.Game.Core`, extending `FS.GG.Game.Core.Pathfinding`; depends *down* only.
- **Phasing:** P0 harden the existing A*/BFS around the total-order key + integer costs +
  golden determinism tests (S — mostly locking in guarantees); P1 Dijkstra `reachableWithin`
  (tactics) + weighted terrain + diagonal policy (S–M); P2 `distanceField`/`flowField`/
  `fleeField` (M); P3 JPS/JPS+ uniform-grid fast path (M); P4 HPA*/HAA* + funnel/LOS smoothing
  (M–L).
- **Contract note:** growing the `fs-gg-game-core`/pathfinding skill body is a Rendering
  library-surface bump the `.github` skill-registry reconciles; the determinism contract
  warrants a **Rendering-local ADR** ("determinism via the ordering key, not a stable heap").

---

## 14. Sources

Red Blob Games — [Introduction to A*](https://www.redblobgames.com/pathfinding/a-star/introduction.html) ·
[Implementation of A*](https://www.redblobgames.com/pathfinding/a-star/implementation.html) ·
[Heuristics (Amit Patel/Stanford)](http://theory.stanford.edu/~amitp/GameProgramming/Heuristics.html) ·
[Flow field / Dijkstra map for tower defense](https://www.redblobgames.com/pathfinding/tower-defense/).
[Harabor & Grastien — Jump Point Search](https://harablog.wordpress.com/2011/09/07/jump-point-search/) ·
[JPS (Wikipedia)](https://en.wikipedia.org/wiki/Jump_point_search) ·
[Improving Jump Point Search (JPS+)](https://www.researchgate.net/publication/287338108_Improving_jump_point_search) ·
[GameDev.net — JPS for uniform-cost grids](https://gamedev.net/tutorials/programming/artificial-intelligence/jump-point-search-fast-a-pathfinding-for-uniform-cost-grids-r4220/).
[Emerson — Crowd Pathfinding & Steering Using Flow Field Tiles (Game AI Pro ch.23, PDF)](https://www.gameaipro.com/GameAIPro/GameAIPro_Chapter23_Crowd_Pathfinding_and_Steering_Using_Flow_Field_Tiles.pdf) ·
[Leif Node — flow field pathfinding](https://leifnode.com/2013/12/flow-field-pathfinding/) ·
[howtoRTS — basic flow fields](https://howtorts.github.io/2014/01/04/basic-flow-fields.html).
[RogueBasin — The Incredible Power of Dijkstra Maps](https://www.roguebasin.com/index.php/The_Incredible_Power_of_Dijkstra_Maps).
[Botea et al. — Near Optimal Hierarchical Path-Finding (HPA*, PDF)](http://webdocs.cs.ualberta.ca/~mmueller/ps/2004/hpastar.pdf) ·
[Mononen — Simple Stupid Funnel Algorithm](http://digestingduck.blogspot.com/2010/03/simple-stupid-funnel-algorithm.html) ·
[Koenig & Likhachev — D* Lite](https://www.semanticscholar.org/paper/D*lite-Koenig-Likhachev/89d487427c6614e033544d33a161a6f8da1d984a) ·
[The Liquid Fire — Tactics RPG pathfinding (movement range)](https://theliquidfire.com/2015/06/08/tactics-rpg-path-finding/).
