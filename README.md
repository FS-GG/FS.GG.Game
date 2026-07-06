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

- `Point` / `Rect` — BCL-only 2-D sim geometry primitives (the sim counterparts of the render
  `FS.GG.UI.Scene.Point`/`Rect`).
- `Geometry` — axis-aligned collision / containment / centering / swept-AABB helpers (moved from
  `FS.GG.UI.Scene.Geometry`, P0 Option D).
- `Rng` — seeded value-type SplitMix64 PRNG (deterministic, splittable).
- `FixedStep` — pure fixed-timestep accumulator drain.
- `Pathfinding` — deterministic grid A* / BFS over a walkability predicate.
- `SpatialGrid` — uniform spatial partition for exact range / radius queries.

All are pure, total (NaN-safe), and **byte-identical across runs and platforms** — the determinism
the game-logic design corpus specifies.

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
