# FS.GG.Game.Core

BCL-only deterministic simulation core for **FS.GG.Game** — the platform's bottom layer.
Depends on nothing but `FSharp.Core` and reaches up to no other FS-GG package.

## The sim vocabulary

The shared types — `Point`, `Rect`, `Circle`, `ConvexPolygon`, and the two detection values `Contact` (a penetration manifold) and `RayHit` (a segment-cast result) — live directly in the `FS.GG.Game.Core` namespace, so `open FS.GG.Game.Core` is all a caller needs. The tile index `Cell` is namespace-level too. Everything below is a `[<RequireQualifiedAccess>]` module, called as `Grids.cellAt`, `Rng.nextInt`, and so on.

- **`Rng`** — SplitMix64 seeded RNG (`ofSeed`/`nextInt`/`nextFloat`/`nextBool`/`split`), byte-identical across runs.
- **`FixedStep`** — fixed-timestep accumulator drain (`drain`/`drainWith`). No wall-clock read, so a scripted frame time replays exactly.

### Collision — detection, then response

- **`Geometry`** — the detection surface: overlap and containment for AABB, circle, and convex polygon (`intersects`/`contains`/`aabbContact`/`circleContact`/`polygonContact`/`obbPolygon`), swept tests (`sweptIntersects`), and segment casts (`segmentAabbHit`/`segmentCircleHit`/`segmentPolygonHit`/…). It returns manifests as values and never resolves them.
- **`Resolution`** — the response layer that consumes a detection `Contact` and produces transforms (`pushOut`/`slide`/`push`; `knockback` is a deprecated shim over `push`). Deliberately separate from detection; it never re-detects.
- **`SpatialGrid`** — uniform spatial partitioning for the broad phase (`build`/`query`/`queryRadius`).

### The grid

- **`Pathfinding`** — grid A*/BFS over walkable cells (`astar`/`bfs`), plus the many-to-one AI substrate `distanceField`/`flowField`/`reachableWithin`.
- **`Grids`** — the parts of a square grid: `Edge`, `Vertex`, `GridSpec`, the six adjacency conversions, and the pixel↔cell map (`cellRect`/`cellCenter`/`edgeSegment`/`cellAt`). Names the boundary *between* two tiles, which is where a wall lives.

### Sight

- **`Los`** — point-to-point grid line of sight over a caller-supplied opacity predicate (`line`/`supercover`/`trace`/`lineOfSight`).
- **`Fov`** — grid field of view by symmetric shadowcasting (`fov`). Opacity is not walkability: this module consults opacity only.
- **`Visibility`** — the continuous-space sibling: exact point-to-point sight against arbitrary occluder segments, and the full angular-sweep visibility polygon (`raySegment`/`isVisible`/`polygon`). It can answer occlusion between two *moving* bodies, which a tile walk cannot.

`Pathfinding`, `Los`, and `Fov` all take terrain as a pure `Cell -> bool` predicate — the framework holds no map, so one terrain plugs into all three.

## Where it came from

Three provenances:

- **Extracted** from `FS.GG.UI.Canvas` / `FS.GG.UI.Scene` and reimplemented BCL-only, per [ADR-0022](https://github.com/FS-GG/.github/blob/main/docs/adr/0022-extract-fs-gg-game-as-an-sdd-driven-component.md) — the shared types, `Geometry`, `Rng`, `FixedStep`, `Pathfinding`, `SpatialGrid`.
- **Promoted** from the frozen starter fragments in `FS.GG.Rendering` (`template/fragments/*`), which every game that wanted them previously had to copy and diverge from — `Los`, `Visibility`, `Grids`.
- **Authored here** through the SDD lifecycle, because the org had nothing to promote — `Fov`, `Resolution`.

## Guarantees

Headlessly testable — zero Skia, zero Scene. Purity, totality (degenerate and non-finite input degrades to a documented value rather than throwing), and determinism (integer logic / float presentation) are the contract each module's `.fsi` states and its tests hold it to — so identical input yields byte-identical output across runs and platforms, and a module is safe to call from a replayed simulation step.

To project sim state onto drawables, add [`FS.GG.Game.Render`](https://www.nuget.org/packages/FS.GG.Game.Render).

House style: `.fsi` is the sole public surface; `net10.0`; `-preview` channel.
See [FS-GG/FS.GG.Game](https://github.com/FS-GG/FS.GG.Game) and [ADR-0022](https://github.com/FS-GG/.github/blob/main/docs/adr/0022-extract-fs-gg-game-as-an-sdd-driven-component.md).
