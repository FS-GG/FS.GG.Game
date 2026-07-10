# FS.GG.Game

The FS-GG platform's game-simulation component and its new **bottom layer**. Extracted from the
render core per **[ADR-0022](https://github.com/FS-GG/.github/blob/main/docs/adr/0022-extract-fs-gg-game-as-an-sdd-driven-component.md)**.

## Packages

| Project | Kind | Depends on | Purpose |
|---|---|---|---|
| `FS.GG.Game.Core` | packable lib | FSharp.Core only | BCL-only deterministic simulation core — reaches up to nothing |
| `FS.GG.Game.Render` *(P4)* | packable lib | `Game.Core` + `FS.GG.UI.Scene` | thin adapter projecting sim state onto `FS.GG.UI.Scene` |

`FS.GG.Game.Core` is a pure simulation vocabulary with **zero** dependency on the render stack (no
Skia, no `FS.GG.UI.Scene`): the one-way dependency rule is preserved — `Game.Core` is a sibling of
Rendering at the bottom of the platform, and `Game.Render` (later) reaches *up* to Scene.

### `FS.GG.Game.Core` surface

The namespace-level vocabulary and 16 `[<RequireQualifiedAccess>]` modules (each module's `.fsi` is
the authoritative one-line contract — the summaries below mirror them):

**Shared primitives** (namespace-level, so `open FS.GG.Game.Core` is all a caller needs) — `Point` /
`Rect` / `Circle` / `ConvexPolygon` 2-D sim geometry, the detection values `Contact` (a penetration
manifold), `RayHit` (a segment-cast result) and `Manifold`, and the tile index `Cell`.

- `Rng` — seeded value-type SplitMix64 PRNG (deterministic, splittable).
- `FixedStep` — pure fixed-timestep accumulator drain.
- `Loop` — the fixed-step double-buffered loop (`Current`/`Previous`/`Accumulator`) built on
  `FixedStep`, so renderers interpolate whole steps by `alpha`.
- `InputCommand` — the abstract, device-free vocabulary of input *intents* (`Command.Move*`/`Fire`/
  `Pause`); the policy half of the input split (games own *which* commands exist, not *how* a device
  makes one).
- `Geometry` — the detection surface: overlap / containment / swept tests / segment casts for AABB,
  circle, and convex polygon (moved from `FS.GG.UI.Scene.Geometry`, P0 Option D).
- `Resolution` — the response layer that consumes a detection `Contact` and produces transforms
  (`pushOut` / `slide` / `push`); deliberately separate from detection.
- `SpatialGrid` — uniform spatial partition for exact range / radius queries (the broad phase).
- `Physics` — an opt-in mini rigid-body engine: broad phase, narrow phase, and a warm-started
  sequential-impulse solver with sleeping bodies. Arcade `Resolution` stays the default.
- `Pathfinding` — deterministic grid A* / BFS over a walkability predicate, plus the many-to-one AI
  substrate (`distanceField` / `flowField` / `reachableWithin`).
- `Grids` — the parts of a square grid: edges, vertices, adjacency conversions, and the pixel↔cell map.
- `Los` — point-to-point grid line of sight over a caller-supplied opacity predicate.
- `Fov` — grid field of view by symmetric shadowcasting (opacity, not walkability).
- `Visibility` — the continuous-space sight sibling: exact point-to-point LOS and the angular-sweep
  visibility polygon against arbitrary occluder segments (answers occlusion between *moving* bodies).
- `Ballistics` — projectile weapons: a swept advance that cannot tunnel, a lead / intercept solve,
  and a splash query with a caller-chosen falloff.
- `Ai` — the decision layer's vocabulary: agent identity, the fog-of-war `TeamView`, and sightings
  that decay into ghosts. LOS is a caller-supplied oracle, so policy stays with the game.
- `Effects` — the mitigation layer: what a hit becomes at the target (cover / armor / damage
  `Source`), generic in the damage kind.

Every module is pure and total (degenerate and non-finite input degrades to a documented value
rather than throwing). Each module's `.fsi` states its own determinism contract and its tests hold
it there: the integer / grid layer (`Rng`, `Pathfinding`, `Grids`, `Los`, `Fov`, `SpatialGrid`, …)
is **byte-identical across runs and platforms**, while the float-heavy modules scope that guarantee
(`Visibility` to `|coord| <= 1e6`) or grade cross-platform lockstep down to a later fixed-point ADR
(`Physics.checksum`). Trust the per-module contract, not one slogan at the top.

## Build & test

```sh
dotnet build FS.GG.Game.slnx
dotnet test  tests/Game.Core.Tests/Game.Core.Tests.fsproj   # headless: zero Skia, zero Scene
dotnet fsi   scripts/refresh-surface-baselines.fsx          # regenerate the public-surface baseline
```

## House style

Mirrors the org conventions (synced build config from `FS-GG/.github` `dist/dotnet/`): `.fsi` as the
sole public surface with a committed surface baseline (`readiness/surface-baselines/`), pure cores,
`net10.0` + FSharp.Core `10.1.301`, central package management with locked restore, deterministic
builds, warnings-as-errors.

## Lifecycle

Per ADR-0022 this repo is developed with **`fsgg-sdd`** as its dev lifecycle (the dogfood),
**coexisting** with Spec Kit `specs/` history — not replacing it.
