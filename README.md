# FS.GG.Game

A render-independent, deterministic game-simulation library for .NET — the physics, pathfinding,
visibility, and AI substrate a game's logic is built on, with zero dependency on any render stack.

## What it can do

- Run a **deterministic, replayable** simulation — a seeded value-type PRNG (`Rng`) and a
  pure fixed-timestep loop (`Loop`/`FixedStep`) so a run reproduces byte-for-byte.
- **Detect and resolve collisions** — AABB / circle / convex-polygon overlap, containment and
  swept tests, plus an opt-in mini rigid-body physics engine over the arcade default.
- **Navigate grids** — A* / BFS pathfinding, distance/flow fields, and reachability over a
  walkability predicate *you* supply (the framework holds no map).
- **Compute visibility** — grid line-of-sight and shadowcast field-of-view, plus continuous-space
  exact LOS and angular-sweep visibility polygons against arbitrary occluders.
- **Drive projectile weapons** — a swept advance that cannot tunnel, lead / intercept solves, and
  splash queries with a caller-chosen falloff.
- **Model decisions and damage** — a fog-of-war AI vocabulary (team views, decaying sightings) and
  a cover / armor / damage effects pipeline generic in the damage kind.

## Acquire

Every `FS.GG.*` package is **public on [nuget.org](https://www.nuget.org) and restores with no
credential** ([ADR-0039][adr-0039]). `FS.GG.Game.Core` and `FS.GG.Game.Render` are **published** —
add the entry package:

```sh
dotnet add package FS.GG.Game.Core
```

`FS.GG.Game.Core` is the BCL-only simulation core and all you need for the quick start below; add
`FS.GG.Game.Render` when you want to project sim state onto `FS.GG.UI.Scene`. The full package map
is in the [package table](#packages) below.

## Quick start

Consume `FS.GG.Game.Core` as a plain library. Create a console app, add the package, and drop this
into `Program.fs` — a seeded generator replays the same rolls on every machine:

```fsharp
open FS.GG.Game.Core

// A seeded, value-type PRNG: the same seed replays the same rolls on every machine.
let start = Rng.ofSeed 42UL

// Roll a six-sided die three times, threading the advanced generator through each draw.
let rolls, _ =
    [ 1..3 ]
    |> List.mapFold (fun g _ ->
        let struct (roll, next) = Rng.nextInt 1 6 g
        roll, next) start

rolls |> List.iteri (fun i roll -> printfn "roll %d -> %d" (i + 1) roll)
```

Run it with `dotnet run` and you should see, on any machine:

```text
roll 1 -> 2
roll 2 -> 1
roll 3 -> 1
```

That is the whole determinism contract in six lines: `Rng` is a value you thread, not a shared
mutable `System.Random`, so a model that holds one replays exactly. (This is a *library-consumption*
quick start; for authoring a game against the repo's SDD lifecycle, see the
[TestSpec tutorial](docs/TestSpecTutorial.md) under **Go deeper**.)

## Go deeper

- [TestSpec tutorial](docs/TestSpecTutorial.md) — the repo's SDD-lifecycle getting-started guide for
  authoring a game against `FS.GG.Game`.
- [`docs/`](docs/) — module design reports and reference material for the simulation surface.
- Related components: [`FS.GG.Rendering`](https://github.com/FS-GG/FS.GG.Rendering) (the `FS.GG.UI.Scene`
  target `FS.GG.Game.Render` projects onto) and [`FS.GG.Audio`](https://github.com/FS-GG/FS.GG.Audio).

## Where this sits

`FS.GG.Game` is a render-independent bottom-layer sibling of the rendering stack ([ADR-0022][adr-0022]):
`Game.Core` reaches up to nothing, and `Game.Render` reaches *up* to `FS.GG.UI.Scene`. See the
[platform vocabulary (ADR-0020)][adr-0020] and [`docs/architecture.md`][arch] for how the whole
platform fits together.

---

<!-- Everything below the fold: for people hacking on the component itself. -->

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
dotnet fsi   scripts/refresh-surface-baselines.fsx          # regenerate the public-surface baselines
```

## House style

Mirrors the org conventions (synced build config from `FS-GG/.github` `dist/dotnet/`): `.fsi` as the
sole public surface with a committed surface baseline (`readiness/surface-baselines/` — exported types,
and their members' signatures under `members/`, so a deleted or re-signatured function is drift), pure cores,
`net10.0` + FSharp.Core `10.1.301`, central package management with locked restore, deterministic
builds, warnings-as-errors.

## Lifecycle

Per ADR-0022 this repo is developed with **`fsgg-sdd`** as its dev lifecycle (the dogfood),
**coexisting** with Spec Kit `specs/` history — not replacing it.

[adr-0020]: https://github.com/FS-GG/.github/blob/main/docs/adr/0020-platform-workspace-component-vocabulary.md
[adr-0022]: https://github.com/FS-GG/.github/blob/main/docs/adr/0022-extract-fs-gg-game-as-an-sdd-driven-component.md
[adr-0039]: https://github.com/FS-GG/.github/blob/main/docs/adr/0039-nuget-org-is-the-read-path.md
[arch]: https://github.com/FS-GG/.github/blob/main/docs/architecture.md
