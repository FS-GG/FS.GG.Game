# FS.GG.Game.Render

Pure **Scene adapter** for FS.GG.Game — projects [`FS.GG.Game.Core`](https://www.nuget.org/packages/FS.GG.Game.Core)
simulation primitives (`Point`/`Rect`/`Cell` and pathfinding routes) onto `FS.GG.UI.Scene` drawables
at the render edge (ADR-0022 §2).

This is the **one** place in FS.GG.Game that reaches up to Rendering: `Game.Core` reaches up to
nothing; this adapter reaches up to `FS.GG.UI.Scene` (FSharp.Core-only, Skia-free). Deterministic,
no I/O.

```
FS.GG.Game.Render ──▶ FS.GG.UI.Scene   (adapter reaches UP — allowed)
FS.GG.Game.Core   ──▶ (nothing)        (bottom layer)
```

## Renderer backend selection

`RendererSelection` is a pure Elmish `init`/`update` for choosing how a game state is presented at
runtime. `RendererRegistry<'State>` holds the adapters for one game-state type; a backend is offered
(`RendererRegistry.available`) only when an adapter is registered for it. `RendererRegistry.svg`
registers the existing Scene path as the default SVG backend. `RendererBackend.Pixi` and
`RendererBackend.Babylon` are declared but this package ships no adapter for either (the Pixi and Babylon adapters live
in the Fable-only `src/Render.Browser` host), so a backend nobody registered is refused with `RendererRefusal.Unavailable`.

Switching (`RendererMsg.SelectBackend`) keeps `Model.State` and emits `Unmount`/`Mount` effects.
`RendererHost` performs those effects, owns the mounted backend's resources outside the model and
disposes each mount exactly once.

Ordering contract: **unmount-then-mount, with SVG fallback.**

- A switch always unmounts the current backend before it mounts the next one, so two backends never
  own resources at the same time.
- Each `Mount` carries a new attempt token. Failure feedback from a superseded attempt is ignored.
- If a non-default backend fails to mount, or throws from `Present`, the selection falls back to SVG
  with the same game state.
- If SVG itself fails, nothing is mounted and `Model.Status` is `Failed`, with every reason that led
  there. Selecting SVG again retries it. An unrelated refusal never hides that failure.
- No adapter exception escapes `RendererHost.Run`. A throwing `Dispose` becomes `DisposeFailed`, and the
  pending `Mount` still runs.
- Adapters must not dispatch messages synchronously from `Mount`, `Present` or `Dispose`, because `Run`
  is not reentrant.

## Three-adapter authoring path

For one game-state type, the SVG, Pixi and Babylon adapters differ only in the surface that presents a
`FS.GG.UI.Scene`; what the game writes is the Scene description of its state. `SceneSurface.create backend
mountScene` is a backend's surface, `SceneSurface.adapter view surface` is its adapter for a state type, and
`SceneRenderers<'State>` is the declaration a game makes once. The kit's types are opaque and built by
functions, so later additions do not break callers:

```fsharp
let renderers =
    SceneRenderers.create view svgMountScene        // the shared view; SVG is always declared
    |> SceneRenderers.withSurface pixiSurface
    |> SceneRenderers.withSurface babylonSurface
    // optional: |> SceneRenderers.withView RendererBackend.Babylon babylonView
let registry = SceneRenderers.registry renderers    // offers exactly the declared backends
```

In the browser host (`src/Render.Browser`), `BrowserRenderers.declare container options route view` declares
all three surfaces at one size, and `BrowserRenderers.start route registry state onChange` starts the
`RendererDriver` and binds the `LateFailureRoute` that Pixi reports an asynchronous initialisation failure
to, so a game never wires `failBackend` by hand. Each Pixi mount captures the driver bound when it is
created, so a late report never reaches a driver started later on the same route.

Walkthrough for a new game:

1. **Generate the module.** `scripts/new-game-renderers.sh --namespace MyGame.Client --module Renderers
   --state MyGame.Model --label "My game" --output src/Client/Renderers.fs` writes a module from
   `eng/render-kit/GameRenderers.fs.template` with `view`, `declare`, `start` and `conformance`.
2. **Write `view`.** Replace the placeholder (which shades the scene by the state's hash) with a projection
   of the state onto `SceneNode` values. The canvas backends draw `Rectangle`,
   `FilledEllipse` and `Circle` and treat `Group`, `Translate` and `Empty` as structure; other nodes are
   counted as unsupported and skipped. Use the data constructors, not the .NET-only `Scene` builder or
   `Adapter`, so the file also compiles with Fable.
3. **Start and switch.** `let driver = Renderers.start container initial onChange`. Dispatch
   `RendererMsg.StateChanged state` for each new state, and `RendererMsg.SelectBackend backend` to switch
   at runtime. Offer `RendererSelection.available driver.Model` in the selector. The game state survives
   every switch.
4. **Prove it.** Run the conformance kit in a browser test:
   `BrowserConformance.installContextProbe ()`, then
   `RendererConformance.run (Renderers.conformance container states shows)`, and require
   `RendererConformance.failures` to be empty. `states` must look different from each other, and `shows`
   reads a backend's output back (the SVG DOM, or a pixel through `BrowserConformance.readPixel`) and must
   reject a state that is not shown.

The reference game does exactly this: `src/ReferenceGame/Renderers.fs` declares its renderers, and the
browser smoke runs the kit on it on all three real backends. `tests/Render.Browser.Smoke/GeneratedRenderers.fs`
is the generator's unedited output, compiled and run through the kit too.

### Registering a ready adapter

A game with its own retained SVG surface (selectable objects, accessible labels, a camera, an input host) has no
`Scene` to hand to `RendererRegistry.svg`, and `SceneSurface` would flatten that surface into one non-selectable
object. `RendererRegistry.create adapter` registers the ready adapter as the default instead: no placeholder is
built, and `Error` says why when the adapter is not an SVG adapter (only SVG can be the default). Canvas adapters
join with `RendererRegistry.register`, and the kit runs on the result like on any other registry.

```fsharp
match RendererRegistry.create { Backend = RendererBackend.Svg; Mount = mountMyBoard } with
| Ok registry -> registry |> RendererRegistry.register myCanvasAdapter
| Error reason -> failwith reason
```

In the browser host, `RetainedSvgSurface` is that adapter for any game whose state projects onto a
`FS.GG.UI.Scene.RetainedScene`: `RetainedSvgSurface.create container label size scene`, with
`withSelection selected onSelect` and `withInput attach`, and `RetainedSvgSurface.registry`. It owns the scene
revisions, keeps the user's camera across presents, keeps the surface's selection in step with the state (and
reports the user's own selections), and releases the input host with the mount.

### The conformance kit

`RendererConformance.run` takes a `ConformanceSubject` and returns one `ConformanceResult` per requirement.
It needs no test framework, so an Expecto suite, a browser page or a script can each report the results.
It instruments the registry's adapters (mounts, presented states and disposals are counted, behaviour
unchanged) and drives a host through the Elmish messages:

| Section | Requirement |
|---|---|
| `subject` | At least two distinct states; the expected backends include SVG. |
| `registry` | Exactly the expected backends are offered, in declared order. Selecting a backend with no adapter is refused as `Unavailable` and acquires nothing. |
| `round-trip` | On every backend, each presented state comes back from the model unchanged, reaches the adapter unchanged, and is what the backend shows. The output probe also rejects at least one other state, so a view or probe that ignores the state fails. |
| `switch` | Runtime switches through `RendererMsg.SelectBackend`, covering every ordered pair of backends, keep the game state. Only the selected backend holds resources, and it shows the kept state. |
| `dispose` | Switching away and disposing the host release every resource, including a canvas's WebGL context. Each mount is disposed exactly once, and a disposed host stays inert. |
| `late failure (host)` | For each non-default backend, `FailBackend` for the live mount falls back to SVG with the state intact and releases the backend. A report for an unselected backend is ignored, and re-selecting recovers. |
| `late failure (adapter)` | For each backend with a `LateFailureTrigger` (Pixi in the browser, through `BrowserConformance.pixiLateFailure`), the trigger makes the adapter's next mount fail asynchronously. The adapter's own report (`OnFailure`, then `LateFailureRoute`, then `failBackend`) falls back to SVG with the state intact and releases the mount. A failure of a mount already disposed is not reported, so the live mount of the same backend stays: this is the live-mount rule, checked against the adapter. |

`Resources` must observe the page independently of the adapter. `BrowserConformance.resources` counts SVG
roots, canvases, Babylon engines, Pixi applications and live WebGL contexts. The kit also asserts its
subject: a probe that never sees a mounted backend fails, and so does fewer than two distinct states.
In .NET, `RendererConformance.subject` uses the in-process host (`RendererConformance.inProcess`);
`withStart`, `withWaitUntil` and `withLateFailure` adjust it. `tests/Game.Render.Tests/RendererConformanceTests.fs`
shows the shape, with negative controls.

Adapter authors keep two obligations the kit checks:

- `Mount` returns `Error` instead of acquiring half a backend, and `Dispose` releases everything, including
  the WebGL context.
- An adapter that initialises asynchronously reports a late failure only while its own mount is undisposed.
  The driver cannot tell two mounts of the same backend apart.

### Fable and the browser host

The package also ships a bounded Fable source view (`fable/RendererSelection`, `SceneRenderers` and
`RendererConformance`, each `.fsi` and `.fs`) so a Fable-compiled browser host can register adapters
against the same `RendererRegistry` and run the conformance kit. `Adapter` and
`DefaultKeymap` are .NET-only and are not in that view. The repository's `src/Render.Browser` project
(not packed) is that host: it mounts the existing SVG surface (`SvgBrowser`), hosts the BabylonJS
and PixiJS adapters (over the Templates#809 and #808 bindings), and adds `BrowserRenderers` and `BrowserConformance` for the
authoring path. `scripts/test-render-browser-host.sh` proves it in a headless browser.

House style: `.fsi` is the sole public surface; `net10.0`; `-preview` channel.
See [FS-GG/FS.GG.Game](https://github.com/FS-GG/FS.GG.Game) and [ADR-0022](https://github.com/FS-GG/.github/blob/main/docs/adr/0022-extract-fs-gg-game-as-an-sdd-driven-component.md).
