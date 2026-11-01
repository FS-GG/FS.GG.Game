# GAME-RENDER-ADOPT-01.1 — Three-adapter authoring kit

Status: source slice delivered in the routine change for FS.GG.Game#745. Publication through the canonical
release route is the held remainder.
Route: routine. Parent Outcome: FS-GG/.github#5179. Builds on GAME-RENDER-MULTI-01 (`game-render-multi-01.md`).

## Goal

For a given game-state type, creating the SVG, Pixi and Babylon adapters and switching between them is one
uniform path, and every adapter is held to one shared conformance kit.

## Delivered

### Authoring API in `FS.GG.Game.Render` (additive)

- `SceneSurface` is the backend half of an adapter: a backend and a `MountScene`. `SceneSurface.adapter view
  surface` makes the adapter for a state type. `RendererAdapter.svg` is the same function for an SVG surface.
- `SceneRenderers<'State>` is the declaration: `create view svg`, `withSurface`, `withView backend view` (a
  backend-specific representation), `view`, `backends` and `registry`. The registry offers exactly the declared
  backends, and SVG is always declared.
- `RendererConformance` is the kit: `run`, `subject`, `inProcess` and `failures`, with `ConformanceSubject`,
  `ConformanceHost` and `ConformanceResult`. It has no test-framework dependency.
- The bounded Fable view now carries `RendererSelection`, `SceneRenderers` and `RendererConformance`. The
  compatibility profile records the wider scope.

### Browser host (`src/Render.Browser`, not packed)

- `SvgSurface.surface`, `PixiSurface.surface` and `BabylonSurface.surface` return `SceneSurface`. The Pixi and
  Babylon `adapter` functions are now `SceneSurface.adapter` over them, and their behaviour is unchanged.
- `BrowserRenderers` (`route`, `options`, `surfaces`, `declare`, `start`) declares all three backends at one
  size. `LateFailureRoute` carries Pixi's asynchronous initialisation failure to the driver that
  `BrowserRenderers.start` binds, which replaces the hand-wired `driverRef` knot.
- `BrowserConformance` is the browser side of the kit:
  - `installContextProbe` and `liveContexts` count WebGL contexts per backend;
  - `resources` counts SVG roots, canvases, live contexts, Pixi applications and Babylon engines;
  - `waitUntil`, `readPixel`, `host` and `subject` drive and read the backends.

### Scaffolding

- `scripts/new-game-renderers.sh` generates a game's renderer module from
  `eng/render-kit/GameRenderers.fs.template`. The module has `view` (the one part to write), `declare`,
  `start` and `conformance`. The script validates its arguments, because they land in F# source.
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
| round-trip | Per backend, every presented state is unchanged in the model, unchanged at the adapter, and shown by the backend. |
| switch | A walk over every ordered pair of backends (seven switches for three backends). Each step keeps the state, holds only the selected backend's resources and shows the kept state. Superseded mounts are disposed once. |
| dispose | Per backend, the probe sees the mount. Switching away and host disposal release everything, including WebGL contexts. Each mount is disposed once, and the disposed host stays inert. |
| late failure | Per canvas backend, a report from a disposed mount is dropped (the live-mount rule), and a live report falls back to SVG with state intact and releases the backend. A report for an unselected backend is ignored, and re-selecting recovers. |

For three backends, the kit returns 58 results.

## Evidence

- `tests/Game.Render.Tests/RendererConformanceTests.fs` has 11 tests. The reference game passes all 58 kit
  checks on three in-process surfaces that own countable resources. An SVG-only declaration passes, and both
  undeclared backends are refused. Negative controls each fail the section that guards them: a missing
  adapter, a leaking `Dispose`, an ignored `Present`, a probe that sees nothing, and a single state.
  `Game.Render.Tests` has 66 tests, all passing.
- `scripts/test-render-browser-host.sh` checks 312 items (it was 187), adding:
  - 58 kit checks, plus a count check and an empty-stage check, on the reference game over the real SVG,
    Pixi and Babylon surfaces;
  - the same on the generated sample;
  - a causal control: each backend's output probe accepts its own state and rejects another;
  - the generated `start` path: all three backends offered, a runtime switch that keeps the state, and a
    clean dispose.
- The full .NET suite passes, and the surface baselines change only by additions.

## Compatibility

Every change to the public `.fsi` surface is an addition: new types and modules in `FS.GG.Game.Render`, and
new functions in the unpacked browser host. No existing signature changed, and the regenerated
`readiness/surface-baselines` only add lines. Under SemVer this is a minor change, to be set by the release
route at publication.

## For FS.GG.Templates#834 (not done here)

The `fs-gg-fable-game` template should carry the same path, so a new game does not copy machinery:

- generate the product's `Renderers.fs` from `eng/render-kit/GameRenderers.fs.template`, or vendor the
  generator, with the product namespace and state type;
- reference the `FS.GG.Game.Render` release that ships `SceneRenderers` and `RendererConformance`;
- add a browser test page that runs `RendererConformance.run` on the generated `conformance` subject;
- add the JS bindings materialization and the import map that `scripts/test-render-browser-host.sh` uses.

Template adoption depends on the first follow-up below, because the browser host is not yet a package.

## Follow-up candidates (upstream gaps found)

1. **Publish the browser host.** `FS.GG.Game.Render.Browser` and its bindings project are unpacked. A game
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
5. **Babylon colour fidelity.** Babylon still draws lit at about 0.75 of the scene colour and ignores alpha,
   so output probes need a backend-specific expectation. An unlit or emissive material would remove it. This
   is a candidate for GPU-measured work under Game#747.
