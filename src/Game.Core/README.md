# FS.GG.Game.Core

BCL-only deterministic simulation core for **FS.GG.Game** — the platform's bottom layer.
Depends on nothing but `FSharp.Core` and reaches up to no other FS-GG package.

## The sim vocabulary

The shared types — `Point`, `Rect`, `Circle`, `ConvexPolygon`, and the detection values `Contact` (a single-axis penetration manifold), `Manifold` (its multi-point form) and `RayHit` (a segment-cast result) — live directly in the `FS.GG.Game.Core` namespace, so `open FS.GG.Game.Core` is all a caller needs. The tile index `Cell` is namespace-level too. Everything below is a `[<RequireQualifiedAccess>]` module, called as `Grids.cellAt`, `Rng.nextInt`, and so on.

- **`Rng`** — SplitMix64 seeded RNG (`ofSeed`/`nextInt`/`nextFloat`/`nextBool`/`split`), byte-identical across runs.
- **`FixedStep`** — fixed-timestep accumulator drain (`drain`/`drainWith`). No wall-clock read, so a scripted frame time replays exactly.
- **`Loop`** — the fixed-step double-buffered loop built on `FixedStep.drain` (`advance`/`alpha`): the world as of the last completed step (`Current`), the one before it (`Previous`), and the carried sub-step time (`Accumulator`). Renderers interpolate `Previous → Current` by `alpha`, so motion is smooth at any frame rate while the sim only ever advances by whole fixed steps. The default for any continuously-moving simulation.
- **`InputCommand`** — the abstract, device-free vocabulary of input *intents* (`Command.MoveNorth`/…/`Fire`/`Pause`) as pure data. The **policy** half of the input split: `Game.Core` owns *which* commands exist; the render/input layer owns *how* a device makes one and never learns a game action.

### Collision — detection, then response

- **`Geometry`** — the detection surface: overlap and containment for AABB, circle, and convex polygon (`intersects`/`contains`/`aabbContact`/`circleContact`/`polygonContact`/`obbPolygon`), swept tests (`sweptIntersects`), and segment casts (`segmentAabbHit`/`segmentCircleHit`/`segmentPolygonHit`/…). It returns manifests as values and never resolves them.
- **`Resolution`** — the response layer that consumes a detection `Contact` and produces transforms (`pushOut`/`slide`/`push`; `knockback` is a deprecated shim over `push`). Deliberately separate from detection; it never re-detects.
- **`SpatialGrid`** — uniform spatial partitioning for the broad phase (`build`/`query`/`queryRadius`).
- **`Physics`** — an opt-in mini rigid-body engine: a world of bodies, a broad phase, a narrow phase, and a semi-implicit Euler step with a warm-started sequential-impulse contact solver over bodies that sleep once they settle. Mass is *derived* from a shape's area, not given. Arcade `Resolution` stays the first-class default; neither module knows the other.

### The grid

- **`Pathfinding`** — grid A*/BFS over walkable cells (`astar`/`bfs`), plus the many-to-one AI substrate `distanceField`/`flowField`/`reachableWithin`.
- **`Grids`** — the parts of a square grid: `Edge`, `Vertex`, `GridSpec`, the six adjacency conversions, and the pixel↔cell map (`cellRect`/`cellCenter`/`edgeSegment`/`cellAt`). Names the boundary *between* two tiles, which is where a wall lives.

### Sight

- **`Los`** — point-to-point grid line of sight over a caller-supplied opacity predicate (`line`/`supercover`/`trace`/`lineOfSight`).
- **`Fov`** — grid field of view by symmetric shadowcasting (`fov`). Opacity is not walkability: this module consults opacity only.
- **`Visibility`** — the continuous-space sibling: exact point-to-point sight against arbitrary occluder segments, and the full angular-sweep visibility polygon (`raySegment`/`isVisible`/`polygon`). It can answer occlusion between two *moving* bodies, which a tile walk cannot.

`Pathfinding`, `Los`, and `Fov` all take terrain as a pure `Cell -> bool` predicate — the framework holds no map, so one terrain plugs into all three.

### Combat and decision

- **`Ballistics`** — projectile weapons over continuous space: a swept advance that cannot tunnel (a fast round is a *segment*, never a point), a lead / intercept solve, and a splash query with a caller-chosen falloff. Lifetimes are counted in whole ticks, not float seconds, so a replay cannot drift.
- **`Effects`** — the mitigation layer: what one incoming hit becomes at the target. It separates the damage `Source` (a `Declared` attack — the only thing cover and armor reduce — vs `Collision`/`Environmental`/`Periodic`) from the damage *kind*, which stays generic in `'K` (`Game.Core` never learns what Frost is).
- **`Ai`** — the decision layer's vocabulary: a comparable `AgentId` that fixes iteration order (never hash order, which would diverge a replay), and the fog-of-war `TeamView` — an abstract value of what a team knows, whose sightings decay from *spotted* into *ghosts*. Its LOS is a caller-supplied oracle, so perception policy stays with the game.

## Where it came from

Three provenances:

- **Extracted** from `FS.GG.UI.Canvas` / `FS.GG.UI.Scene` and reimplemented BCL-only, per [ADR-0022](https://github.com/FS-GG/.github/blob/main/docs/adr/0022-extract-fs-gg-game-as-an-sdd-driven-component.md) — the shared types, `Geometry`, `Rng`, `FixedStep`, `Pathfinding`, `SpatialGrid`.
- **Promoted** from the frozen starter fragments in `FS.GG.Rendering` (`template/fragments/*`), which every game that wanted them previously had to copy and diverge from — `Los`, `Visibility`, `Grids`.
- **Authored here** through the SDD lifecycle, because the org had nothing to promote — `Fov`, `Resolution`, `Loop`, `InputCommand`, `Ballistics`, `Effects`, `Ai`, and `Physics`.

## Guarantees

Headlessly testable — zero Skia, zero Scene. Purity, totality (degenerate and non-finite input degrades to a documented value rather than throwing), and determinism (integer logic / float presentation) are the contract each module's `.fsi` states and its tests hold it to — so identical input replays byte-identically across runs, and a module is safe to call from a replayed simulation step. Cross-*platform* byte-identity is unconditional for the integer / grid layer; the float-heavy modules scope it (`Visibility` to `|coord| <= 1e6`) or defer full lockstep to a later fixed-point ADR (`Physics.checksum`), so trust the per-module `.fsi`, not one blanket claim.

To project sim state onto drawables, add [`FS.GG.Game.Render`](https://www.nuget.org/packages/FS.GG.Game.Render).

House style: `.fsi` is the sole public surface; `net10.0`; `-preview` channel.
See [FS-GG/FS.GG.Game](https://github.com/FS-GG/FS.GG.Game) and [ADR-0022](https://github.com/FS-GG/.github/blob/main/docs/adr/0022-extract-fs-gg-game-as-an-sdd-driven-component.md).
