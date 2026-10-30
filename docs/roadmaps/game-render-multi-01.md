# GAME-RENDER-MULTI-01.4 — Elmish renderer selection in Game.Render

Status: Elmish switching core and SVG default delivered in the routine change for FS.GG.Game#730,
followed by the binding-independent Fable browser host with the SVG adapter wired end to end. Pixi
and Babylon adapters and the three-backend browser test remain open, waiting on the bindings.
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
It then compiles `tests/Render.Browser.Smoke` with Fable and runs 42 checks in headless Chrome:
SVG default renders two rects through the retained SvgBrowser surface; 50 presents leave one root and a
stable node count; an unavailable Pixi is refused with the DOM untouched; a registered probe adapter
owning a `<canvas>` is mounted only after the SVG root is released, receives state, is disposed exactly once
and hands the same state back to SVG; a refusing backend falls back to SVG; an adapter that dispatches from
`Mount`, `Present` and `Dispose` never nests effects (maximum callback depth 1) and the queued messages reach
the final model; a throwing model observer leaves model and host in sync; and disposal empties the container.
The script also builds the unpacked `fable/` project on its own, which exercises the packed-layout branch of
its `Exists()` conditions. The `pixi` and `babylon` adapters in that smoke are plug-in probes, not PixiJS or BabylonJS.
`.github/workflows/render-browser-host.yml` runs the script on pull requests that touch those paths.

## Remaining for #730

- Pixi and Babylon `RendererAdapter` values for at least one shared game state, in `src/Render.Browser`. These
  need the FS.GG.Templates#808 (PixiJS) and #809 (Babylon) bindings.
- A browser test proving that all three backends render and switch, built on the same driver and smoke.
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
