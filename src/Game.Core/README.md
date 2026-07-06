# FS.GG.Game.Core

BCL-only deterministic simulation core for **FS.GG.Game** — the platform's bottom layer.
Depends on nothing but `FSharp.Core` and reaches up to no other FS-GG package.

Provides the sim vocabulary extracted from `FS.GG.UI.Canvas` / `FS.GG.UI.Scene` (ADR-0022):

- **`Rng`** — SplitMix64 seeded RNG (`ofSeed`/`nextInt`/`nextFloat`/`nextBool`/`split`), byte-identical across runs.
- **`FixedStep`** — fixed-timestep accumulator drain.
- **`Pathfinding`** — grid A*/BFS over walkable cells.
- **`SpatialGrid`** — uniform spatial partitioning (`build`/`query`/`queryRadius`).
- **`Geometry`** — AABB collision over the core's own BCL-only `Point`/`Rect` (`intersects`/`contains`/`center`/`sweptIntersects`/…).

Headlessly testable — zero Skia, zero Scene. Determinism is a tested property (integer logic / float presentation).

To project sim state onto drawables, add [`FS.GG.Game.Render`](https://www.nuget.org/packages/FS.GG.Game.Render).

House style: `.fsi` is the sole public surface; `net10.0`; `-preview` channel.
See [FS-GG/FS.GG.Game](https://github.com/FS-GG/FS.GG.Game) and [ADR-0022](https://github.com/FS-GG/.github/blob/main/docs/adr/0022-extract-fs-gg-game-as-an-sdd-driven-component.md).
