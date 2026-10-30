# GAME-RENDER-MULTI-01.4 — Elmish renderer selection in Game.Render

Status: Elmish switching core and SVG default delivered in the routine change for FS.GG.Game#730,
followed by the binding-independent Fable browser host with the SVG adapter wired end to end, then the
Babylon adapter over the Templates#809 bindings, then the Pixi adapter over the Templates#808 bindings and the
three-backend browser test.
Route: routine. Parent Outcome: FS-GG/.github#4930.

## Delivered in this slice

`FS.GG.Game.Render` gains `RendererSelection.fsi` with an additive public surface. The surface
baselines are refreshed deliberately, and existing `Adapter` and `DefaultKeymap` members are
unchanged.

- `RendererBackend` (`Svg | Pixi | Babylon`) with stable tokens and `defaultBackend = Svg`.
- `RendererAdapter<'State>` is the extension point: a backend id and a `Mount` returning
  `RendererMount<'State>` (`Present`, `Dispose`) or an error. `RendererAdapter.svg` wraps a
  state-to-Scene view and a host Scene surface.
- `RendererRegistry<'State>` is opaque and keyed by the state type parameter. It is constructible
  only through `RendererRegistry.svg`, so the default always has an adapter. `register`, `tryFind`,
  `isAvailable` and `available` complete it.
- `RendererModel<'State>` (`Registry`, `Selected`, `State`, `Attempt`, `Status`, `Refusal`), with
  pure `RendererSelection.init` and `RendererSelection.update`.
- `RendererMsg<'State>` has the cases `SelectBackend`, `StateChanged`, `MountFailed` (which carries an
  attempt) and `DisposeFailed`.
- `RendererEffect<'State>` has the cases `Mount` (which carries an attempt), `Present` and `Unmount`.
- `RendererStatus` (`Active | Failed of failures`) records explicitly whether the selected backend's
  latest mount is live. Recoverability never depends on the display-only `Refusal`.
- `RendererHost<'State>` is the effect interpreter:
  - It allows at most one mounted backend.
  - It disposes each mount exactly once, including on a `Mount` with no prior `Unmount`.
  - It turns every adapter exception into a message. A failing `Mount` or `Present` becomes
    `MountFailed`, and a throwing `Present` also releases that mount. A failing `Dispose` becomes
    `DisposeFailed`, and the pending `Mount` still runs.
  - It becomes inert after disposal.

The ordering contract is unmount-then-mount, with SVG fallback. A switch unmounts the current backend
before it mounts the next one. If a non-default backend fails, the selection mounts SVG with the same
game state. If SVG fails, nothing is mounted, `Status` is `Failed` and re-selecting SVG retries.
Failure feedback from a superseded attempt is ignored. `Run` is not reentrant, so adapters must not
dispatch synchronously.

The package takes no new dependency. Elmish wiring stays with the host, which maps effects to a
command, so this core needs no `Fable.Elmish` package edge.

Evidence: `tests/Game.Render.Tests/RendererSelectionTests.fs` (24 tests) covers registry
availability, state-preserving switches, refusal of unavailable backends, exactly-once disposal,
fallback after a mount failure, a failed SVG fallback that keeps both reasons and recovers, recovery
after an unavailable selection, throwing `Dispose` and `Present` (including a throwing `Present` on the default backend, which releases its
mount, and a host `Dispose` that swallows a throwing mount `Dispose`), stale same-backend attempts, and SVG output
identical to the existing `Adapter` Scene. The existing Adapter, keymap and portal presentation tests are unchanged.

## Browser host (binding-independent part)

Host shape: Game.Render keeps the portable selection core and ships a bounded Fable source view of it;
a separate Fable-only host project owns every adapter that needs JavaScript interop. This is the shape the
plan already implies. The PixiJS and BabylonJS bindings are Fable-only packages (Templates#808/#809), so no
.NET assembly such as `FS.GG.Game.Render` can reference them. The registry and adapter record they plug
into live in Game.Render, so the host must compile those same sources.

- `FS.GG.Game.Render` gains `FablePackageType=library` through `Fable.Package.SDK`, the pattern Game.Core and
  `FS.GG.UI.Scene.SvgBrowser` already use. The `fable/` view is deliberately bounded to
  `RendererSelection.fsi/.fs`, the only files that reach `FS.GG.UI.Scene` alone. `Adapter` reaches Game.Core's
  .NET-only `Grids` and `DefaultKeymap` reaches the .NET KeyboardInput build, so both stay out, as the
  compatibility profile records. The public `.fsi` surface is unchanged and the surface baselines
  are byte-identical.
- `src/Render.Browser` (`FS.GG.Game.Render.Browser`, not packed) is the host. `SvgSurface` is the retained SVG
  surface for `RendererAdapter.svg`: it mounts one `SvgBrowser` root per mount and reconciles each presented
  `Scene` through the portable reducer, so SVG output is the existing path. `RendererDriver` runs the
  `RendererSelection` loop with the `RendererHost` for a page that is not an Elmish program. A fallback has
  finished by the time `Dispatch` returns, and messages dispatched during a run are queued, so `Run` is never
  reentered.
- Later adapters plug in with `RendererRegistry.register` against the same `RendererAdapter<'State>` record,
  in this project and not in Game.Render: `RendererAdapter` carries no interop and the registry stays keyed by
  the state type.
- A Fable game projects state onto `SceneNode` values directly. The `Scene` builder module and `Adapter`
  are .NET-only, because the Fable view of `FS.GG.UI.Scene` ships only its data types and the SVG pipeline.

Evidence: `scripts/test-render-browser-host.sh` packs Game.Render and checks that the `fable/` payload is
exactly `RendererSelection.fsi/.fs` plus the metadata project, byte-identical to the canonical sources.
It then compiles `tests/Render.Browser.Smoke` with Fable and runs the original 42 checks (plus the Babylon checks below) in headless Chrome:
SVG default renders two rects through the retained SvgBrowser surface; 50 presents leave one root and a
stable node count; an unavailable Pixi is refused with the DOM untouched; a registered probe adapter
owning a `<canvas>` is mounted only after the SVG root is released, receives state, is disposed exactly once
and hands the same state back to SVG; a refusing backend falls back to SVG; an adapter that dispatches from
`Mount`, `Present` and `Dispose` never nests effects (maximum callback depth 1) and the queued messages reach
the final model; a throwing model observer leaves model and host in sync; and disposal empties the container.
The script also builds the unpacked `fable/` project on its own, which exercises the packed-layout branch of
its `Exists()` conditions. The `pixi` adapter and the refusing `babylon` adapter in its first sections are plug-in probes; the real Babylon adapter has its own section.
`.github/workflows/render-browser-host.yml` runs the script on pull requests that touch those paths.

### Babylon adapter (Templates#809 bindings)

- **Consumption route: the generated bindings source, compiled by a committed wrapper project; neither the packed
  NuGet nor a project reference to the generated project.** The Babylon and Pixi bindings are template-generated
  (`fs-gg-fable-bindings`, Templates PR #812 and #814) and not published. `scripts/materialize-js-bindings.sh`
  instantiates that template from the commit pinned in `eng/js-bindings/pin.env`, once per corpus, under
  `artifacts/js-bindings` (git-ignored; nothing generated is committed), and the committed
  `src/Render.Browser.Bindings` project compiles each `Bindings.fs` for the host. Two routes were tried and rejected
  by evidence. The packed NuGet: the same template compiled by SDK 10.0.401 and by SDK 10.0.403 (the CI runner) packs
  byte-different assemblies, and the NuGet lock records the archive's content hash, so a committed lock cannot hold
  for a package built on the consumer's machine. A project reference to the generated project: the org
  lock-range gate requires every project a lock names to be a committed project file. Copying the generated
  `Bindings.fs` into the repository was rejected as ad hoc vendoring. The exact `@babylonjs/core@9.33.1` closure is
  installed from the template's own `package-lock.json` and mapped with an import map.
- `BabylonSurface` (`src/Render.Browser`) is the adapter, registered with `RendererRegistry.register`. It projects
  the SAME `FS.GG.UI.Scene` the SVG adapter draws: `Rectangle`, `FilledEllipse` and `Circle` become flat meshes
  (later nodes nearer the camera), `Group`/`Translate`/`Empty` are structural and any other node is counted in
  `BabylonCounters.UnsupportedNodes` and not drawn. One mount owns one canvas, one WebGL engine, one scene and one
  render loop. `Present` reconciles meshes in place; surplus meshes are disposed and their materials pooled (the
  bindings have no material disposal). `Dispose` stops the loop, disposes meshes, scene and engine and removes the
  canvas, running every step even when one throws. A browser without WebGL, invalid options, or a failing
  engine are `Error` mounts, so the host falls back to SVG with the state intact. A throwing `Present` is
  contained by the host, which releases the mount.
- Evidence: the smoke now runs in a real-time browser (a virtual-time run delivers only two animation frames, too
  few for asynchronous shader compilation) and reports through one console line. It reads Babylon's own engine
  store for the structural counters (one engine, one render loop, one scene, mesh count equal to node count and
  stable over 101 presents, materials bounded) and reads a pixel back from the canvas. It covers SVG -> Babylon
  -> SVG with state preserved, five repeated rounds that never hold two engines, no WebGL, a refused option set, a
  throwing `Present`, and disposal leaving no canvas, engine, loop or scene.
- Known edge: Babylon's `Engine` registers itself in its engine store before it asks for a WebGL context, so a
  constructor that throws would leave a half-built engine with no handle. The adapter asks a throwaway canvas for
  a context first and refuses before Babylon is touched.

### Pixi adapter (Templates#808 bindings) and the three-backend test

- `PixiSurface` (`src/Render.Browser`) is registered with `RendererRegistry.register` and projects the same
  `FS.GG.UI.Scene` as the SVG and Babylon adapters (the projection is shared in an internal `SceneShapes`): one
  `Graphics` display object per drawable node, reconciled in place, later nodes on top. Pixi is unlit, so its pixels
  are the scene colours exactly.
- Pixi's renderer initialises asynchronously, after `Mount` has returned `Ok`. States presented meanwhile are kept
  (the latest wins) and drawn when initialisation completes. A `Dispose` during initialisation removes the canvas at
  once and destroys the application when initialisation settles. An initialisation that fails is reported by
  `PixiOptions.OnFailure`, which a host wires to the new `RendererDriver.failBackend`; the driver then falls back to
  SVG, and a host that does not wire it still gets the failure from the next `Present`.
- Both canvas adapters now lose their WebGL context on dispose. Disposing an engine or destroying an application
  does not release it, a browser keeps about sixteen, and a runtime switch would otherwise leave one live context per
  mount until garbage collection.
- The smoke reads Pixi's own books (live applications by Pixi's destroyed marker, ticker listeners, stage children),
  the browser's account of adapter WebGL contexts and the canvas pixels, and switches SVG, Pixi and Babylon in both
  directions on one game state: state preserved at every step, exactly one backend holding resources, every canvas
  mount starting with the previous backend fully released, and nothing left after dispose.

## Remaining for #730

- Publication of the host and of the new Game.Render `fable/` view is not part of this slice. It needs its own
  selected Delivery and coherent version.
- Known low edge: a fallback to SVG keeps its cause only in `RendererModel.Refusal`. If another refusal
  (for example `Unavailable` from a selection) replaces it before the SVG mount fails, the failure list loses
  the original reason. Fixing it needs a model field (public surface and baseline change), so it was not
  folded into this slice.
- Known pre-existing edge (not changed here): `RendererHost` records a mount only after `adapter.Mount` returns.
  An adapter that disposes the host re-entrantly while its own `Mount` is still running therefore leaks that mount.
  `RendererHost.Run` is documented as not reentrant and adapters must not dispatch synchronously, so
  `RendererDriver` queues such messages, but a direct `IDisposable.Dispose` from inside `Mount` is not guarded.
