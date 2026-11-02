# GAME-RENDER-ADOPT-01.1 — Three-adapter authoring kit

Status: source slice delivered in the routine change for FS.GG.Game#745 (#749). The packaging change makes the
browser host the `FS.GG.Game.Render.Browser` package and prepares the coherent 0.18.0 release (see "Publication"
below). The held remainder is publication through the canonical release route, then the public consumer check.
Route: routine. Parent Outcome: FS-GG/.github#5179. Builds on GAME-RENDER-MULTI-01 (`game-render-multi-01.md`).

## Goal

For a given game-state type, creating the SVG, Pixi and Babylon adapters and switching between them is one
uniform path, and every adapter is held to one shared conformance kit.

## Delivered

### Authoring API in `FS.GG.Game.Render` (additive)

- `SceneSurface` is the backend half of an adapter, built by `SceneSurface.create backend mountScene` and read
  with `SceneSurface.backend` and `SceneSurface.mountScene`. `SceneSurface.adapter view surface` makes the
  adapter for a state type. `RendererAdapter.svg` is the same function for an SVG surface.
- `SceneRenderers<'State>` is the declaration: `create view svg`, `withSurface`, `withView backend view` (a
  backend-specific representation), `view`, `backends` and `registry`. The registry offers exactly the declared
  backends, and SVG is always declared.
- `RendererConformance` is the kit: `run`, `subject`, `withStart`, `withWaitUntil`, `withLateFailure`, `host`,
  `inProcess` and `failures`. It has no test-framework dependency. `LateFailureTrigger.create arm settle`
  describes how a backend's adapter fails late.
- `SceneSurface`, `ConformanceSubject`, `ConformanceHost`, `ConformanceResult` and `LateFailureTrigger` are
  opaque and built by functions, so fields added later (Game#751, #752) are not breaking changes.
- The bounded Fable view now carries `RendererSelection`, `SceneRenderers` and `RendererConformance`. The
  compatibility profile records the wider scope.

### Browser host (`src/Render.Browser`, packed as `FS.GG.Game.Render.Browser` from 0.18.0)

- `SvgSurface.surface`, `PixiSurface.surface` and `BabylonSurface.surface` return `SceneSurface`. The Pixi and
  Babylon `adapter` functions are now `SceneSurface.adapter` over them, and their behaviour is unchanged.
- `BrowserRenderers` (`route`, `options`, `surfaces`, `declare`, `start`) declares all three backends at one
  size. `LateFailureRoute` carries Pixi's asynchronous initialisation failure to the driver that
  `BrowserRenderers.start` binds, which replaces the hand-wired `driverRef` knot. Each Pixi mount captures the
  driver bound when it is created, so a mount of an earlier driver never reaches a driver started later.
- `BrowserConformance` is the browser side of the kit:
  - `installContextProbe` and `liveContexts` count WebGL contexts per backend;
  - `resources` counts SVG roots, canvases, live contexts, Pixi applications and Babylon engines;
  - `waitUntil`, `readPixel`, `host` and `subject` drive and read the backends;
  - `pixiLateFailure` refuses every WebGL context of the next Pixi canvas, so that mount's asynchronous
    initialisation fails and the adapter reports it through its own `OnFailure`. `subject` declares it
    whenever the registry offers Pixi.

### Scaffolding

- `scripts/new-game-renderers.sh` generates a game's renderer module from
  `eng/render-kit/GameRenderers.fs.template`. The module has `view` (the one part to write), `declare`,
  `start` and `conformance`. The placeholder `view` shades the scene by the state's hash, so different states
  look different until the game writes its own. The script validates its arguments, because they land in F#
  source.
- `tests/Render.Browser.Smoke/GeneratedRenderers.fs` is the generator's output for fixed arguments. The smoke
  requires it to be byte-identical, compiles it with Fable and runs the kit on it.
- The walkthrough is in `src/Game.Render/README.md`, under "Three-adapter authoring path".

### Reference game

- `src/ReferenceGame/State.fs` holds the state types, with no harness dependency. `Composition` re-exports
  them as type abbreviations, so `Composition.Model` and `Composition.Screen.Playing` are the same types as
  before.
- `src/ReferenceGame/Renderers.fs` holds the reference game's Scene view and its renderer declaration. The
  .NET reference game and, through Fable, the browser smoke compile the same two files.

## The conformance kit

| Section | Checks |
|---|---|
| subject | At least two distinct states; the expected backends include SVG. |
| registry | Exactly the expected backends are offered. An unregistered backend is refused as `Unavailable` and acquires nothing. |
| round-trip | Per backend, every presented state is unchanged in the model, unchanged at the adapter, and shown by the backend. The output probe rejects at least one other state. |
| switch | A walk over every ordered pair of backends (seven switches for three backends). Each step keeps the state, holds only the selected backend's resources and shows the kept state. Superseded mounts are disposed once. |
| dispose | Per backend, the probe sees the mount. Switching away and host disposal release everything, including WebGL contexts. Each mount is disposed once, and the disposed host stays inert. |
| late failure (host) | Per non-default backend, `FailBackend` for the live mount falls back to SVG with the state intact and releases the backend. A report for an unselected backend is ignored, and re-selecting recovers. |
| late failure (adapter) | Per backend with a trigger, the adapter's own report of its live mount's failure falls back to SVG with the state intact and releases the mount. A failure of a mount already disposed is not reported, so the live mount of the same backend stays (the live-mount rule, checked against the adapter). |

For three backends, the kit returns 62 results with one trigger (Pixi, in the browser) and 65 with two (the
in-process fakes).

## Evidence

- `tests/Game.Render.Tests/RendererConformanceTests.fs` has 13 tests. The reference game passes all 65 kit
  checks on three in-process surfaces that own countable resources and fail late through their own report. An
  SVG-only declaration passes, and both undeclared backends are refused. Each negative control fails the section
  that guards it:
  - a missing adapter;
  - a leaking `Dispose`;
  - an ignored `Present`;
  - an adapter that reports after its own dispose, which fails exactly the live-mount check;
  - a view and probe that ignore the state;
  - a probe that sees nothing;
  - a single state.

  `Game.Render.Tests` has 68 tests, all passing.
- `scripts/test-render-browser-host.sh` checks 320 items (it was 187), adding:
  - 62 kit checks, plus a count check and an empty-stage check, on the reference game over the real SVG, Pixi and
    Babylon surfaces;
  - the same on the generated sample;
  - a causal control in which each backend's output probe accepts its own state and rejects another;
  - the generated `start` path: all three backends offered, a runtime switch that keeps the state, and a clean
    dispose.
- Mutation: with `PixiSurface`'s disposed guard removed (its failed initialisation reports after dispose), the
  smoke fails 3 of 320 checks. They are the live-mount check in both kit runs and the existing hardening check.
- The full .NET suite passes, and the surface baselines change only by additions.

## Compatibility

Every change to the public `.fsi` surface is an addition: new types and modules in `FS.GG.Game.Render`, and
new functions in the unpacked browser host. No existing signature changed, and the regenerated
`readiness/surface-baselines` only add lines. Under SemVer this is a minor change, to be set by the release
route at publication.

## Publication (FS.GG.Game#745 remainder)

- `src/Render.Browser` packs as `FS.GG.Game.Render.Browser`, a Fable library package. `fable/` holds its seven
  modules, the two generated bindings at the sha256 that `eng/js-bindings/pin.env` pins (`fable/generated/`), and a
  packed project that lists them unconditionally. The package depends on `FS.GG.Game.Render` at the same version.
  The bindings assembly is not a dependency. `js-bindings/` carries the exact npm closures, and the packed project
  declares `@babylonjs/core@9.33.1` and `pixi.js@8.22.0` for Femto.
- Fable does not evaluate MSBuild conditions in a package's project file. The view project #733 added used
  conditional item groups, so its packed `fable/FS.GG.Game.Render.fsproj` would have compiled no sources for a
  package consumer. That form was never published: #733 merged after `v0.17.0` (its commit `eba5ce12` is not an
  ancestor of the tag), and the published `FS.GG.Game.Render` 0.17.0 has no `fable/` entries at all. A clean
  consumer compile of the 0.18.0 candidate showed the failure, while the repository smoke had passed because it
  builds that view with MSBuild. The in-repo project is now unconditional, and the packed copy is generated from it
  at pack. The release contract refuses a conditional packed project. 0.18.0 is the first release with a
  `FS.GG.Game.Render` Fable view.
- `src/Render.Browser` references `FS.GG.Game.Render` two ways. Fable cracks projects with a design-time build
  and compiles every referenced project's sources, so a design-time build (Fable, editors) references the bounded
  view, `src/Game.Render/Fable`. A normal restore, build and pack references `src/Game.Render`. The packed assembly
  then references `FS.GG.Game.Render`, and the nuspec depends on `FS.GG.Game.Render` at the same version, as a
  consumer resolves them. Two rejected alternatives:
  - Giving the view project that package identity collides with the org lock-range gate (one package id, one
    project).
  - A non-output reference to `src/Game.Render` makes Fable compile the .NET-only `Adapter`.
- `tests/release/test-render-browser-package-consumer.sh` creates a clean consumer with only a
  `FS.GG.Game.Render.Browser` reference, generates its renderer module with `scripts/new-game-renderers.sh`,
  restores into an empty package folder, builds it with .NET, and compiles it with Fable. It runs in three places:
  in the browser-host smoke against the PR's packs, in `release.yml` against the candidate set before
  publication, and with `--public` after both feeds read back.
- The release is the coherent 0.18.0 set of five packages. The four published packages keep their 0.17.0 API
  baseline. The browser host is validated against itself on its first release, and its surface baseline is
  `readiness/surface-baselines/FS.GG.Game.Render.Browser.txt`, refreshed with `--browser` and drift-checked by
  `render-browser-host.yml`.

## For FS.GG.Templates#834 (not done here)

The `fs-gg-fable-game` template should carry the same path, so a new game does not copy machinery:

- generate the product's `Renderers.fs` from `eng/render-kit/GameRenderers.fs.template`, or vendor the
  generator, with the product namespace and state type;
- reference the `FS.GG.Game.Render` release that ships `SceneRenderers` and `RendererConformance`;
- add a browser test page that runs `RendererConformance.run` on the generated `conformance` subject;
- add the JS bindings materialization and the import map that `scripts/test-render-browser-host.sh` uses.

Template adoption is blocked on publishing the browser host (`FS.GG.Game.Render.Browser`: `RendererDriver`,
`BrowserRenderers`, `BrowserConformance` and the surfaces). That publication is part of #745's held remainder.

## Follow-up candidates (upstream gaps found)

1. **Publish the browser host** (held remainder of #745; packaging is above, publication is pending). Until it is
   published, a game
   repository (FourD, SC2, BAR) cannot consume `BrowserRenderers`, the surfaces or `BrowserConformance` without
   copying them. A published package is needed, with a deterministic route for the generated Pixi and Babylon
   bindings. The roadmap above records why the packed bindings NuGet is not reproducible across SDKs.
2. **Canvas node coverage.** The Pixi and Babylon projections draw only `Rectangle`, `FilledEllipse` and
   `Circle`. Text, lines and paths, which most game HUDs need, are counted as unsupported. This belongs in the
   shared `SceneShapes` projection here, or upstream in FS.GG.Rendering.
3. **Fable-portable Scene builders.** FS.GG.UI.Scene's Fable view ships only the data types, so a view shared
   with the browser must use `SceneNode` constructors directly (an FS.GG.Rendering gap).
4. **Live-mount rule by construction.** `failBackend` cannot tell two mounts of one backend apart, so the rule
   is an adapter obligation. A per-mount report function, or the attempt token handed to `Mount`, would
   enforce it in the type.
5. **Babylon colour fidelity.** *Resolved by FS.GG.Game#786.* Babylon drew lit, at about 0.75 of the scene colour, so
   output probes needed a backend-specific expectation. Its materials are now unlit: the colour is in `emissiveColor`,
   with `disableLighting` and a black diffuse. The kit checks one colour at one tolerance (1) on all three backends.
   Babylon still ignores alpha.
