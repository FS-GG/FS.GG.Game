---
name: fs-gg-grids
description: Name and relate the parts of a grid in a generated FS.GG.UI product — square faces/edges/vertices and their pixel↔cell map, hexagonal cube coordinates, thin walls that block movement, and disc/ring AoE templates — over the `Grids`, `Hex`, and `Edges` modules in FS.GG.Game.Core.
---

# Grid-Parts Capability

## Scope

Use this skill for the **geometry vocabulary of a grid** — not routing over cells (that is
[[fs-gg-game-core]]'s `Pathfinding`) or spatial hashing (`SpatialGrid`), but naming and relating the parts
of the grid itself. It covers four things:

- **Square-grid parts** — the **faces** (cells/tiles), **edges** (the boundary between two faces), and
  **vertices** (the corners where edges meet), each with one canonical name, the conversions between them,
  and the pixel↔cell map. The workhorse behind edge-walls, autotiling / marching-squares, region borders,
  and cursor snapping.
- **Hexagonal grids** — the `Hex` cube-coordinate system: neighbours, distance, rings, ranges, rotation,
  lines, and the offset/doubled storage conversions, for hex-map games.
- **Thin walls** — the `Edges` module: a canonical wall on a shared cell boundary that blocks movement both
  ways, with wall-aware `bfs`/`astar`.
- **Range & AoE templates** — `disc`/`ring` cell footprints for blasts and range indicators.

Everything here is pure, total, and deterministic — integer adjacency and templates returning fixed-order
lists, a straight-line pixel map guarded against non-finite input — so it is safe from a replayed
simulation step. **Triangle grids remain a deliberate non-goal.** This skill materializes for the `game`
and `sample-pack` profiles.

## Public Contract

The signatures you consume are bundled with this product:

- `docs/api-surface/Game.Core/Grids.fsi` — the `Grids` module: the `EdgeOrientation`/`Edge`/`Vertex`/
  `GridSpec` types, the six adjacency conversions, the pixel map (`cellRect`, `cellCenter`, `vertexPoint`,
  `edgeSegment`, `edgeMidpoint`, `cellAt`), and the `disc`/`ring` AoE templates. Shipped in
  `FS.GG.Game.Core`, referenced on the `game` and `sample-pack` profiles.
- `docs/api-surface/Game.Core/Hex.fsi` — the `Hex` cube-coordinate module: `create`/arithmetic,
  `neighbours`/`distance`/`range`/`ring`/`spiral`/`rotate*`/`lineDraw`, the `toOffset`/`toDoubled` storage
  converters, and `Hex.astar`/`Hex.bfs`. Same package and profiles.
- `docs/api-surface/Game.Core/Edges.fsi` — the `Edges` module: the canonical cell-pair `Edge`, the lattice
  `Vertex`, `Dir`, the addressing conversions, `isEdgePassable`, and the wall-aware `bfs`/`astar`. Same
  package and profiles.
- `docs/api-surface/Game.Core/Pathfinding.fsi` — the shared `Cell` (`{ Col; Row }`), the grid **face**;
  reused as-is, never re-rolled.
- `docs/api-surface/Game.Core/Primitives.fsi` — the shared `Point`/`Rect` that the pixel map produces.

All entry points are **total**: degenerate inputs return a documented value, they never throw and no
function here can emit a `NaN` coordinate.

## Three parts, one canonical coordinate each

A square grid is three kinds of part, each with one coordinate — and two of them look identical on
purpose:

```fsharp
open FS.GG.Game.Core

let c : Cell = { Col = 3; Row = 2 }                  // a FACE — reuse the shared Cell, never re-roll it
let corners = Grids.cellCorners c                    // Vertex list: TL, TR, BR, BL
let edges   = Grids.cellEdges c                      // Edge   list: top, right, bottom, left
```

- **Face** — the existing `Cell` (`{ Col; Row }`). A tile. Reused, never re-created.
- **Vertex** — a corner, `Grids.Vertex` (`{ Col; Row }`). `(c,r)` is the *top-left* corner of cell
  `(c,r)`, so the corner lattice is offset half a cell from the faces.
- **Edge** — the boundary between two faces, `Grids.Edge` (`{ Col; Row; Orientation }`). The one concept
  the face/vertex vocabulary lacks.

`Cell` and `Vertex` have the identical shape, and that is the trap: a `Cell` indexes a face, a `Vertex`
a corner. The two types exist precisely to stop you conflating them. Annotate at the boundary
(`let faceOf (v: Grids.Vertex) : Cell = ...`) so a bare `{ Col = ...; Row = ... }` cannot silently bind a
corner where a tile was meant.

## Every wall has exactly one name

An edge borders two cells and could be named from either — so `Grids` picks **one** canonical name and
holds to it:

- a `Vertical` edge `{ Col; Row }` is named from the cell on its **right**: it is the boundary of cells
  `(Col-1, Row)` / `(Col, Row)`, and runs from vertex `(Col, Row)` down to `(Col, Row+1)`;
- a `Horizontal` edge `{ Col; Row }` is named from the cell **below** it: the boundary of cells
  `(Col, Row-1)` / `(Col, Row)`, running from vertex `(Col, Row)` right to `(Col+1, Row)`.

```fsharp
open FS.GG.Game.Core

// Cell (3,2)'s right wall and cell (4,2)'s left wall are ONE boundary, so they carry ONE Edge value:
let wall : Grids.Edge = { Col = 4; Row = 2; Orientation = Grids.Vertical }

let standing : Set<Grids.Edge> = Set.ofList (Grids.cellEdges c)   // "which walls still stand"
let blocked  = standing.Contains wall                            // both cells agree on the key
```

This is the whole point of the canonical name: two references to the same boundary are *structurally
equal*, so a `Set<Grids.Edge>` of standing walls de-dupes and a `Map<Grids.Edge, _>` of wall state has
no aliasing. Name an edge from "whichever cell I'm iterating" and the same wall compares unequal to
itself — a `Set` that should hold one entry holds two. Watch the axis, too: a `Vertical` edge is a
*left/right* boundary (it runs vertically); a `Horizontal` edge a *top/bottom* one.

## Adjacency — the parts relationship table

Six pure, integer conversions walk between the parts. Each returns a fixed, documented order and is
**mutually consistent**: every edge or corner a cell reports reports that cell back.

```fsharp
open FS.GG.Game.Core

let touching = Grids.edgeCells wall          // [ {Col=3;Row=2}; {Col=4;Row=2} ] — left then right, c is one
let ends     = Grids.edgeVertices wall       // the edge's two endpoint Vertices, start then end
let around   = Grids.vertexCells corners.[0] // the 4 faces meeting at that corner — c is one
let spokes   = Grids.vertexEdges corners.[0] // the 4 edges meeting there: up, right, down, left
```

- `cellCorners c` / `cellEdges c` — a face's four corners (TL, TR, BR, BL) / four edges (top, right,
  bottom, left).
- `edgeCells e` / `edgeVertices e` — the two faces an edge separates (ascending) / its two endpoint
  vertices. `edgeCells` is the "this wall fell — which two tiles now connect?" conversion.
- `vertexCells v` / `vertexEdges v` — the four faces / four edges meeting at a corner.

A region border is exactly the edges with one filled neighbour (`edgeCells`); a marching-squares tile
keys off `cellCorners`. Reach for the conversions rather than hand-indexing `Col ± 1` — the off-by-one
between the face lattice and the half-cell-offset corner lattice is theirs to keep straight.

## Pixel mapping — and its inverse

A `Grids.GridSpec` (`{ CellSize; Origin }`) places the grid in continuous space; `Origin` is the pixel
position of vertex `(0, 0)`. The map reuses the shared `Point`/`Rect` — no hand-rolled bounds record.

```fsharp
open FS.GG.Game.Core

let spec : Grids.GridSpec = { CellSize = 32.0; Origin = { X = 0.0; Y = 0.0 } }

let box    = Grids.cellRect spec c            // Rect  — draw the tile
let mid    = Grids.cellCenter spec c          // Point — place a sprite / label
let corner = Grids.vertexPoint spec corners.[0]
let a, b   = Grids.edgeSegment spec wall      // Point * Point — stroke a fence, in edgeVertices order
let m      = Grids.edgeMidpoint spec wall     // Point — hang a door on the wall

// Inverse — snap a continuous position to the tile that contains it:
let hovered = Grids.cellAt spec { X = 90.0; Y = 40.0 }
```

`cellAt` is the floor-based inverse of `cellRect`/`cellCenter`: `cellAt spec (cellCenter spec c) = c`
for every cell. A position exactly on a boundary belongs to the cell to its **right/below**. This is
the pointer-picking and impact-to-tile projection — "which tile did this shell land in". And
`edgeSegment` is the bridge out of the tile grid: feed its two endpoints to the `Visibility` module's
`Segment` to make a tile wall occlude a continuous line-of-sight cast.

## Hexagonal grids

A hex map is a different vocabulary from a square one, and `Hex` gives it to you in **cube coordinates** —
`{ Q; R; S }` with the invariant `Q + R + S = 0` held **by construction**. No constructor can break it:
`Hex.create q r` derives `S = -q - r`, and every operation preserves the plane. Cube is the form the
algorithms want; you rarely type the `S`.

**Store in offset, compute in cube.** A rectangular hex map is convenient to store as `Cell` (`Col`/`Row`)
offset coordinates, but neighbours, distance, and rotation are only clean in cube. Convert at the boundary
and keep the two straight — the converters are **exact inverses**:

```fsharp
open FS.GG.Game.Core

let h    = Hex.create 2 (-1)                   // cube; S = -1, derived — an off-plane hex cannot exist
let cell = Hex.toOffset h                      // odd-r offset Cell for storage (ofOffset is its inverse)
// let cell = Hex.toDoubled h                  // …or doubled-width, if that suits your map (ofDoubled inverts)

let near = Hex.neighbours h                    // the 6 adjacent hexes, in the fixed `directions` order
let dist = Hex.distance h Hex.origin           // integer cube distance
```

Every enumeration walks one **fixed order** (`directions` starts at +Q/−R and rotates clockwise), so the
family is byte-deterministic:

- `range n` — every hex within `n` steps (`3n(n+1)+1` of them); `ring n` — exactly the `6n` at distance
  `n`; `spiral n` — `range n` walked ring by ring. Negative `n` yields an empty list.
- `rotateRight`/`rotateLeft` — 60° about the origin; `add`/`subtract`/`scale` build movement and offsets.
- `lineDraw a b` — the contiguous hex line, endpoints included, length `distance a b + 1`.
- `Hex.astar`/`Hex.bfs maxVisited isWalkable start goal` — the same deterministic search as the square
  `Pathfinding`, over the hex neighbour set (`astar` uses cube `distance` as its admissible heuristic).

`round fq fr fs` is the module's **one float touch-point** — it snaps a fractional cube coordinate back to a
valid `Hex`, resetting the largest-error component so `Q+R+S=0` survives, with ties broken in a fixed order
(S, then Q, then R) so `lineDraw` stays deterministic. **Pixel↔hex conversion is deliberately not here** —
that is the render adapter's float boundary, kept out of the BCL-only core (as with `Grids`' own pixel map,
which *is* here only because it is total and guarded).

## Thin walls that block movement — the `Edges` module

The square-grid `Edge` above is a **geometry** name: it maps to a pixel segment you stroke a fence along or
feed to `Visibility`. When a wall must actually **stop a unit from crossing**, that is a different job with a
different type — the `Edges` module.

`Edges.Edge` is a **sorted pair of the two cells the wall sits between** (`{ Lo; Hi }`, `Lo <= Hi`). The
sorting *is* the dedupe: the wall between `(3,2)` and `(4,2)` has one representative no matter which side
names it, so a `Set<Edges.Edge>` of standing walls holds it once and blocks the crossing **both ways**.

```fsharp
open FS.GG.Game.Core

let wall  = Edges.edgeBetween { Col = 3; Row = 2 } { Col = 4; Row = 2 }   // Some edge — the cells are adjacent
let walls = Set.ofList (Edges.borders { Col = 3; Row = 2 })               // build a wall set from the world

let canCross = Edges.isEdgePassable walls { Col = 3; Row = 2 } { Col = 4; Row = 2 }   // symmetric
let route    = Edges.astar walls 4096 isWalkable start goal               // routes AROUND the thin walls
```

`Edges.bfs`/`Edges.astar` are the [[fs-gg-game-core]] `Pathfinding` search **made wall-aware**: they never
step across an edge in `walls`, so a unit detours around a fence that leaves both its cells walkable. With an
empty `walls` set they match plain `Pathfinding.bfs`/`FourWay` exactly. The addressing functions
(`edgeBetween`/`edgeOf`, `borders`, `corners`, `edgeCells`, `edgeEndpoints`, `vertexCells`, `vertexEdges`,
`neighbours`, `step`) are the same pure, fixed-order conversions as the square parts, over the cell-pair
`Edge` and the lattice `Vertex` (`{ VCol; VRow }`, the NW corner of a cell).

### Two `Edge` types, two jobs — do not cross them

| Type | Shape | For | Reach for it when |
| --- | --- | --- | --- |
| `Grids.Edge` | `{ Col; Row; Orientation }` | **geometry** | drawing a fence, `edgeSegment` → `Visibility`, marching-squares borders |
| `Edges.Edge` | `{ Lo; Hi }` (cell pair) | **movement** | a wall that blocks a step, `isEdgePassable`, wall-aware routing |

They name the *same physical boundary* two ways. Pick by the question: *"where is this wall on screen?"* is
`Grids.Edge`; *"can the unit cross here?"* is `Edges.Edge`. The `Set<Grids.Edge>` of standing walls shown
earlier is valid for *bookkeeping*, but you would then hand-roll a search that consults it — `Edges` ships
that search, so keep a wall's **routing** state in a `Set<Edges.Edge>` and its **rendered** fence from
`Grids.edgeSegment`, and annotate at the boundary so a bare `{ … }` cannot bind the wrong one.

## Range & AoE templates

`Grids.disc` and `Grids.ring` stamp a **cell footprint** for a blast radius, a range indicator, or a spell
template — the discrete companions to the continuous `Ballistics.splash`:

```fsharp
open FS.GG.Game.Core

let blast = Grids.disc { Col = 6; Row = 4 } 3      // filled: every cell within squared radius 3 (row-major)
let edge  = Grids.ring { Col = 6; Row = 4 } 3      // outline only: the midpoint-circle at radius 3
```

- `disc center radius` — every cell whose **integer squared** Euclidean distance is `<= radius²`, in
  row-major order. `disc c 0 = [c]`; a negative radius is `[]`. The squared test is exactly the one `Fov`
  and `SpatialGrid.queryRadius` use, so a disc template and a splash agree on their rim.
- `ring center radius` — just the ~1-cell-thick outline (an integer midpoint circle), deduped and
  `(Col, Row)`-sorted.

For a **line** template (a beam, or a wall drawn between two tiles) there is no `Grids.line` — reuse
[[fs-gg-line-drawing]]'s `Los.line`/`Los.supercover`, the same integer walk.

## Common pitfalls

- **Re-rolling `Cell`/`Point` instead of reusing them.** A bare `{ Col = ...; Row = ... }` binds to
  whichever record is in scope last — and `Cell` and `Grids.Vertex` are structurally identical. Annotate
  at the boundary and reuse `Cell` for faces, `Point`/`Rect` for pixels. `Edge`/`Vertex` are the *only*
  new parts; do not add a look-alike tile or point type.
- **Giving an edge two names.** Naming a boundary from "whichever cell I'm iterating" makes the same wall
  compare unequal to itself, so a `Set<Grids.Edge>` de-dupes wrong. Keep the one canonical `Edge`
  coordinate.
- **Confusing edge orientation.** A `Vertical` edge is a *left/right* boundary; a `Horizontal` edge a
  *top/bottom* one. `cellEdges` returns them top, right, bottom, left — index accordingly.
- **Off-by-one between faces and corners.** The corner lattice is offset half a cell: vertex `(c,r)` is
  the *top-left* corner of cell `(c,r)`, so cell `(c,r)`'s bottom-right corner is vertex `(c+1,r+1)`. Use
  the conversions, not hand arithmetic.
- **Non-finite `GridSpec`.** A zero/negative/NaN `CellSize` falls back to `1.0` and a non-finite `Origin`
  component to `0.0` (total, never a NaN pixel) — but that is a *safety net*, not a valid grid; validate
  your `GridSpec` upstream.
- **Reaching for `Grids` to route or bucket.** The `Grids` *module* names the parts and maps them to
  pixels; it does not walk them. Route over faces with `Pathfinding`, range-query them with `SpatialGrid`,
  and route around thin walls with the sibling `Edges` module.
- **Mixing hex storage and compute coordinates.** Store hexes as offset/doubled `Cell` (`toOffset`/
  `toDoubled`) and run neighbours/distance/rotation/paths in cube (`ofOffset`/`ofDoubled` back). A bare
  `{ Col; Row }` from `Hex.toOffset` is a *storage* handle, not a cube hex — do not feed it to `Hex.distance`.
- **Hand-building a `Hex`.** Only `Hex.create` (and the arithmetic/converters) keeps `Q + R + S = 0`. Never
  construct the record literally or "fix up" `S` yourself; an off-plane hex breaks distance and lines.
- **Reading `disc`'s radius as tiles-per-axis.** `disc c r` is a Euclidean disc (squared distance ≤ `r²`),
  not an `r`-tile square — its corners are clipped. For a filled square use a `Cell` range, not `disc`.
- **Confusing `Grids.Edge` with `Edges.Edge`.** Geometry vs movement — see the table above. Key routing on
  `Edges.Edge`, draw fences from `Grids.Edge`; annotate at the boundary so a bare `{ … }` binds the right one.

## Build Commands

Run `./fake.sh build -t Dev` then `./fake.sh build -t Verify` in this product.

## Test Commands

Run `./fake.sh build -t Test` to exercise product-owned grid-parts examples.

## Evidence

Record grid-parts evidence (adjacency round-trips — every edge/corner of a cell reports that cell back;
the `cellAt (cellCenter c) = c` pixel round-trip; determinism replays; totality on a degenerate
`GridSpec`) under this product's `readiness/` paths. Do not copy framework readiness reports in.

## Package Boundary

`Grids` (with its `EdgeOrientation`/`Edge`/`Vertex`/`GridSpec` types and the `disc`/`ring` templates), the
`Hex` cube-coordinate module, and the `Edges` thin-wall module all live in `FS.GG.Game.Core` (referenced
only on the `game`/`sample-pack` profiles), alongside the `Cell` faces and the `Point`/`Rect` geometry they
map onto. `FS.GG.Game.Core` is the BCL-only bottom layer — it depends on nothing and pulls in no viewer,
layout, or widget machinery, which is exactly why pixel↔hex conversion is *not* here. Keep rendering the
tiles, hexes, fences, and corner markers in [[fs-gg-rendering:fs-gg-scene]].

## Generated Product

Build your world over `Cell` faces, use `Grids` to address the edges and corners between them each fixed
step, and hand the pixel geometry (`cellRect`/`edgeSegment`/`vertexPoint`) to your `View` as a Scene.
Pair it with [[fs-gg-game-core]]'s `Pathfinding`/`SpatialGrid` for routing and range queries over the
same cells, and use `cellAt` to land a projectile from [[fs-gg-ballistics]] back onto a tile. For a hex
world, store cells as `Hex.toOffset` and run neighbours, distance, and paths in cube; for thin walls, hold
a `Set<Edges.Edge>` in your `Model` and route with `Edges.astar`; stamp blasts with `Grids.disc`.

## Persistent problems

When a problem outlasts reasonable in-repo attempts, extensive external research is **mandatory** —
consult **official online docs first** (the F#/.NET docs and the Red Blob Games references), then
community sources. If your product uses Spec Kit, record findings and resolving links under the feature's
`specs/<feature>/feedback/`; otherwise record them in this skill's **Sources** line and any product-local
`docs/`. Offline, the mandate degrades to recording "research blocked — <why>" rather than hard-failing.

## Related

- [[fs-gg-game-core]] — the fixed step, plus `Pathfinding`/`SpatialGrid`, which route and bucket over the
  same `Cell` faces this skill addresses the parts of.
- [[fs-gg-ballistics]] — `cellAt` is the impact-to-tile projection that lands a `Struck` shell on a tile.
- [[fs-gg-collision]] — the sibling per-frame geometry pass over the shared `Point`/`Rect`/`SpatialGrid`.
- [[fs-gg-visibility]] — turns `edgeSegment` walls into the occluders the angular sweep consumes.
- [[fs-gg-line-drawing]] — walks the `Cell` faces this skill names the edges and vertices of.
- [[fs-gg-rendering:fs-gg-scene]] — renders the tiles, fences (`edgeSegment`), and corner markers.
- [[fs-gg-rendering:fs-gg-skiaviewer]] — drives the fixed-step loop from the host window.

## Sources / links

- Red Blob Games, "Parts of a grid": https://www.redblobgames.com/grids/parts/
- Red Blob Games, "Grid edges": https://www.redblobgames.com/grids/edges/
- Red Blob Games, "Hexagonal grids" (the cube coordinates, storage, and line/range algorithms):
  https://www.redblobgames.com/grids/hexagons/
- F#/.NET docs: https://learn.microsoft.com/en-us/dotnet/fsharp/
