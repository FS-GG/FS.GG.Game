---
name: fs-gg-visibility
description: Answer "what can be seen from here?" in a generated FS.GG.Game product — the continuous angular-sweep visibility polygon over `Point` occluders, and discrete symmetric-shadowcasting field of view over `Cell` tiles.
---

# 2D Visibility Capability

## Scope

Use this skill for **2D visibility** in a game/sim product: answering *"what can be seen from
here?"* Game.Core answers it in **two vocabularies**, and picking the wrong one is the mistake this
skill exists to prevent:

- **`Visibility`** works in **continuous space** over `Point`s and `Segment` walls: an exact
  point-to-point line-of-sight predicate, and the full **angular-sweep visibility polygon** from a
  viewpoint. This is the one that can answer occlusion between two *moving* bodies, because it never
  snaps to a grid.
- **`Fov`** works on the **discrete tile grid** over `Cell`s: **symmetric shadowcasting**
  (Ford/Milazzo) that returns the set of cells visible from an origin within a radius. This is the
  one for tile fog-of-war, roguelike sight, and anything where the map is a grid of opacity flags.

Both are pure, total, deterministic, and byte-identical across runs and platforms — safe to call
from a replayed `update`. Advancing the world on a fixed step is [[fs-gg-game-core]]'s job; rendering
the polygon or the fog mask is [[fs-gg-rendering:fs-gg-scene]]'s. This skill materializes for the
`game` and `sample-pack` profiles.

## Public Contract

The signatures you consume are bundled with this product:

- `docs/api-surface/Game.Core/Visibility.fsi` — the `Visibility` module plus its `Segment`,
  `Settings`, and `VisibilityPolygon` types. Continuous-space, `Point`-valued.
- `docs/api-surface/Game.Core/Fov.fsi` — the `Fov` module (`Fov.fov`). Discrete, `Cell`-valued.
- `docs/api-surface/Game.Core/Primitives.fsi` — the sim `Point` the continuous side is expressed in.
- `docs/api-surface/Game.Core/Pathfinding.fsi` — the `Cell` the discrete side is expressed in, and
  the `isWalkable` predicate shape `Fov`'s `isTransparent` mirrors.
- `docs/api-surface/Game.Core/Los.fsi` — `Los.lineOfSight`, the discrete point-to-point LOS query
  ([[fs-gg-line-drawing]]) that is the grid sibling of `Visibility.isVisible`.

All entry points are **total**: degenerate or non-finite input returns a documented value, never an
exception, and no function here ever emits a `NaN` coordinate.

## Which one — decide before you write a line

| You want… | Call | Space | Answer |
| --- | --- | --- | --- |
| Tile fog-of-war / roguelike sight | `Fov.fov` | `Cell` | `Set<Cell>` (a region) |
| Continuous 2D lighting / a soft sight polygon | `Visibility.polygon` | `Point` | `VisibilityPolygon` (a region) |
| Can body A see body B, exactly, off-grid? | `Visibility.isVisible` | `Point` | `bool` |
| Can tile A see tile B on the grid? | `Los.lineOfSight` | `Cell` | `bool` |

The region calls compute *all* of what is visible in one pass; the predicate calls answer one pair.
**Do not build a region by looping a predicate over every cell** — running `Los.lineOfSight` (or a
ray) to every cell in radius is `O(radius³)` *and* produces the asymmetric, artifact-ridden vision
shadowcasting was invented to fix. `Fov.fov` is `O(cells in radius)` and gets symmetry structurally.

## The continuous side: the angular sweep

An occluder is a `Segment` — a wall between two shared `Point`s (`{ A; B }`). `Segment` is
deliberately **not** a look-alike vector type; it reuses the shared `Point` vocabulary, and a
zero-length segment (`A = B`) occludes nothing. Build your wall list from your world each step (tile
edges, polygon boundaries, dynamic blockers as segments).

`Visibility.polygon` clips those occluders to the sight bound, casts a ray at every clipped endpoint
(and one either side, to slip past corners), keeps the **nearest** hit per ray, and orders the hits
into a closed, CCW ring — the `VisibilityPolygon`. It is a **region**, not a boolean: fill it as a 2D
light, rasterize it into a fog mask, or point-test against it.

```fsharp
open FS.GG.Game.Core

let source : Point = { X = 0.0; Y = 0.0 }

let walls : Visibility.Segment list =
    [ { A = { X = 5.0; Y = -5.0 }; B = { X = 5.0; Y = 5.0 } }
      // ...more wall segments from your world
    ]

let poly : Visibility.VisibilityPolygon = Visibility.polygon { Radius = 200.0 } source walls
// poly.Source is `source`; poly.Vertices is the ordered closed ring.
```

`Settings.Radius` is the single knob: it is **both** the ray bound (a ray that hits nothing
terminates on the `source ± Radius` box, so the ring is always finite and closed) **and** the
occluder cull region, so the two can never disagree. A non-positive or non-finite `Radius` falls back
to `1.0` rather than throwing.

**Determinism is by design, not `atan2`.** The sweep orders hits with a cross-product angular
comparator and an integer-index tiebreak, and picks the nearest hit by a sqrt-free parametric
distance — no transcendental, no hash iteration. Identical input yields a byte-identical polygon
across runs and platforms, safe under replay.

## Point-to-point line of sight

For a single yes/no query — *can this body shoot that body?* — `Visibility.isVisible source target
walls` is the exact predicate built on the same `raySegment` core. **Exact**: every segment is tested
with no broad-phase cull, so there is no false "visible". Pass dynamic bodies as segments alongside
static walls; `source = target` is always visible; a blocker exactly *at* either endpoint does not
block (the strict `0 < t < 1` convention, matched to `Geometry`'s strict-edge rule).

```fsharp
open FS.GG.Game.Core

let canSee (enemy: Point) (player: Point) (walls: Visibility.Segment list) : bool =
    Visibility.isVisible enemy player walls
```

`Visibility.raySegment origin dir seg` is the underlying nearest ray-segment hit, returning the point
struck and the parametric `t >= 0` along `origin + t * dir` (so a unit-length `dir` makes `t` a true
distance). It answers `None` when the ray is parallel to the segment — a zero-length segment counts as
parallel — points away from it, misses it, or any operand is non-finite. Sqrt-free, total, and it never
returns a `NaN` coordinate.

It is also **overflow-free**. Finite operands whose magnitudes multiply past `Double.MaxValue` saturate
the parametric cross products even when the intersection they describe is an ordinary pair of doubles;
such a hit is *recovered*, not dropped. The solve falls back to re-deriving `t` with the operands
rescaled by exact powers of two — which cancel out of the ratio, introducing no rounding — anchored at
whichever segment endpoint sits nearer `origin`. The ordinary path stays bit-for-bit unchanged, so this
is safe under a replayed step. Only a genuinely unrepresentable `t` (an `origin` a `Double.MaxValue`
away from the hit) still degrades to `None`, deliberately: a missing hit degrades one ring, where a
`NaN` poisons every consumer downstream of it.

That is a guarantee about **overflow, not precision.** `t` is a ratio of cross products, and cross
products of near-parallel operands cancel: where `origin` is nearly collinear with the segment, or the
coordinates span many orders of magnitude, the ordinary path can return a `t` whose relative error far
exceeds an ulp. That error is inherent to the parametrisation, and the rescaled fallback does not
address it.

## The discrete side: symmetric shadowcasting and expansive walls

`Fov.fov isTransparent origin radius` returns the `Set<Cell>` visible from `origin` within `radius`.
The framework holds **no map**: the predicate `isTransparent` (a pure `Cell -> bool`, `true` = you
can see through it) *is* the map — the same shape as `Pathfinding.astar`'s `isWalkable`, so one
terrain plugs into both. **Opacity is not walkability**: a chasm is transparent and unwalkable, a
secret door opaque and walkable. Model them as two independent predicates, never one "wall" flag.

```fsharp
open FS.GG.Game.Core

let walls : Set<Cell> = model.Walls
let isTransparent (c: Cell) : bool = not (walls.Contains c)

let visible : Set<Cell> = Fov.fov isTransparent { Col = 12; Row = 8 } 8

// Fog-of-war: union this step's field into the ever-growing seen set.
let seenSoFar : Set<Cell> = Set.union model.Seen visible
```

Two contract properties make this worth reaching for over a ray-per-cell:

- **Symmetry** is stated over *transparent* cells: for any two transparent `a`, `b` within `radius`,
  `b ∈ fov isTransparent a radius ⇔ a ∈ fov isTransparent b radius`. This is what keeps combat fair —
  if A can see B, B can see A.
- **Walls are deliberately asymmetric** — the "expansive walls" rule. A visible wall is revealed (you
  see its near face) *even though* it blocks everything beyond it, so an opaque `b` may be in `fov a`
  while `a ∉ fov b`. This is **contract, not artifact**: it is what makes a convex room show *all* of
  its walls rather than a ragged fringe. Do not "correct" it.

The shape is a **Euclidean disc** — a cell is revealed only if `dCol² + dRow² ≤ radius²` (exact
integer squared distance, no jagged-circle rounding). Clipping applies to what is *revealed* only: an
opaque cell outside the disc still casts its shadow, so **shrinking the radius never reveals a cell a
larger radius hid**. Totality is precise: `origin` is always visible when `radius ≥ 0` *even if
opaque* (you occupy the cell you stand in), `radius = 0` yields exactly `Set.singleton origin`, and
`radius < 0` yields `Set.empty`.

## Common pitfalls

- **Building a region by looping a predicate.** Running `Los.lineOfSight` or `Visibility.isVisible`
  to every cell/point in radius is the slow path *and* the buggy (asymmetric, artifact-ridden) path.
  Use `Fov.fov` (discrete) or `Visibility.polygon` (continuous) — they compute the whole region once.
- **Reaching for the grid when bodies move between tiles.** Occlusion between two *moving* bodies is a
  continuous question; `Fov`/`Los` snap to cells and cannot answer it. Use `Visibility.isVisible`.
- **Conflating opacity with walkability.** Feeding `isWalkable` to `Fov.fov` reveals through chasms and
  blocks through secret doors — opacity is its own predicate.
- **"Fixing" expansive walls.** Seeing an opaque cell that cannot see you back is the documented
  contract, not a bug. Removing it gives you ragged room outlines.
- **Sorting the continuous sweep by `atan2`.** Its last bit can differ across runtimes and flip two
  near-collinear corners, breaking replay determinism. The cross-product comparator is the default;
  keep it. And keep `Radius` and the cull region one value (`Settings.Radius`) — they are by design.

## Build Commands

Run `./fake.sh build -t Dev` then `./fake.sh build -t Verify` in this product.

## Test Commands

Run `./fake.sh build -t Test` to exercise product-owned visibility examples (assert an occluder hides
a target and that removing it restores sight; the `Fov` symmetry and expansive-wall properties;
determinism replays; bound/totality cases).

## Evidence

Record visibility evidence (occlusion cases, `Fov` symmetry/expansive-wall property runs, determinism
replays, bound/totality) under this product's `readiness/` paths. Do not copy framework readiness
reports into the product.

## Package Boundary

`Visibility`, `Fov`, and the `Segment`/`Settings`/`VisibilityPolygon` types live in
`FS.GG.Game.Core` (referenced only on the `game`/`sample-pack` profiles), alongside the `Point`,
`Cell`, and `Los` vocabulary they build on. `FS.GG.Game.Core` is the BCL-only bottom layer — it
depends on nothing and pulls in no viewer, layout, or widget machinery. Keep drawing the light and the
fog mask in [[fs-gg-rendering:fs-gg-scene]] and host wiring in [[fs-gg-rendering:fs-gg-skiaviewer]].

## Generated Product

Each fixed step, build your occluders from the world and compute visibility once. On the grid: hold a
`Set<Cell>` seen-set in your `Model`, `Set.union` this step's `Fov.fov` into it, and hand both the
live-visible and seen sets to `View` for the fog mask. In continuous space: build a `Segment` wall
list, call `Visibility.polygon`, and fill the polygon as a light or point-test it. Pair either with
[[fs-gg-collision]] for a full geometry pass.

## Persistent problems

When a problem outlasts reasonable in-repo attempts, extensive external research is **mandatory** —
consult **official online docs first** (the F#/.NET docs and the Red Blob Games reference), then
community sources. If your product uses Spec Kit, record findings and resolving links under the
feature's `specs/<feature>/feedback/`; otherwise record them in this skill's **Sources** line and any
product-local `docs/`. Offline, the mandate degrades to recording "research blocked — <why>" rather
than hard-failing.

## Related

- [[fs-gg-game-core]] — the fixed-step loop that drives the world; owns `Pathfinding`, whose
  `isWalkable` predicate mirrors `Fov`'s `isTransparent`.
- [[fs-gg-line-drawing]] — `Los.lineOfSight`, the discrete point-to-point LOS query that is the grid
  sibling of `Visibility.isVisible`, and the supercover/thin walk beneath it.
- [[fs-gg-grids]] — maps continuous `Point`s to the `Cell`s `Fov` reasons over.
- [[fs-gg-collision]] — the sibling per-frame geometry pass sharing the `Point`/`Segment` vocabulary.
- [[fs-gg-rendering:fs-gg-scene]] — renders the visibility polygon and the fog mask.
- [[fs-gg-rendering:fs-gg-skiaviewer]] — drives the fixed-step loop from the host window.

## Sources / links

- Red Blob Games, "2D Visibility" (the angular sweep): https://www.redblobgames.com/articles/visibility/
- Symmetric shadowcasting (Ford/Milazzo): https://www.albertford.com/shadowcasting/
- F#/.NET docs: https://learn.microsoft.com/en-us/dotnet/fsharp/
- Ray-segment intersection background: https://en.wikipedia.org/wiki/Line%E2%80%93line_intersection
