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
in the Fable-only `FS.GG.Game.Render.Browser` package), so a backend nobody registered is refused with `RendererRefusal.Unavailable`.

Switching (`RendererMsg.SelectBackend`) keeps `Model.State` and emits `Unmount`/`Mount` effects.
`RendererHost` performs those effects, owns the mounted backend's resources outside the model and
disposes each mount exactly once.

Ordering contract: **unmount-then-mount, with SVG fallback.**

- A switch always unmounts the current backend before it mounts the next one, so two backends never
  own resources at the same time.
- Each `Mount` carries a new attempt token. Failure feedback from a superseded attempt is ignored.
- If a non-default backend fails to mount, or throws from `Present`, the selection falls back to SVG
  with the same game state.
- If SVG itself fails to mount, nothing is mounted and `Model.Status` is `Failed`, with every reason that led
  there. Selecting SVG again, or the next game state, retries it. An unrelated refusal never hides that failure.
- No adapter exception escapes `RendererHost.Run`. A throwing `Dispose` becomes `DisposeFailed`, and the
  pending `Mount` still runs.
- A `Present` that throws is contained (nothing escapes `Run`), the host releases that mount, and the model
  mounts again with the latest state: SVG replaces a failed canvas backend, and the default SVG backend is
  remounted itself (`RendererMsg.PresentFailed`). `Refusal` names the cause. If the remount fails too, `Status` is
  `Failed` with the cause, and the next `StateChanged` mounts again, so a throwing `Present` is never a silent blank.
- A mount is fixed to the viewport it was created for. `RendererRegistry.withViewport backend viewport` declares
  how a state sizes that backend (the remount rule): when the viewport changes between states, the model
  unmounts and mounts the backend with the new state instead of presenting, so a game never remounts inside its
  own `Present`. A game whose viewport comes from the window keeps it in its state. A backend with no declaration
  owns its viewport itself (it may resize in place).
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
| `present failure` | The kit makes the next `Present` of each backend throw. A canvas backend falls back to SVG with the latest state and is released. The default backend is released and mounted again with the latest state: one live mount remains and `Refusal` names the cause. If the remount fails too, `Failed` is reported with nothing mounted or held, and the next state mounts and shows. |
| `viewport` | When the subject declares one (`RendererConformance.withViewport expected actual`): every state, then the states back again, is presented at its own viewport as the backend reports it; one live mount remains, each superseded one disposed once; and where the registry declares the viewport, the mount is replaced exactly when it changes. The subject must hold two different viewports. |
| `pixels` | Only when `RendererConformance.withPixels expected probe` declares what the states must draw (FS.GG.Game#753; `ScenePixels.expectations (view state)` lists the pixels a scene decides: each node's middle, the first whole pixel inside and outside each rectangle edge, the pixel one clear of each round shape's extremes, and every pixel no node touches, which must be the clear colour so a node drawn larger than declared fails; a node that overhangs the canvas is read at its edge; a node the kit does not model leaves open only the pixels inside its bounds when they are known, such as a stroked path, line, image or clip, FS.GG.Game#789; `ScenePixels.identityExpectations` adds each rectangle's corner and quarter-edge pixels, which tell identity frames and index badges apart). On every backend each state's pixels are read back (`BrowserConformance.pixelProbe container width height`: canvases and the rasterised SVG, read the same way, with the scene's size mapped to a canvas styled to another size) and must be the scene's colours, so a node drawn at another position, size or layer fails. With `withInteraction` too, the pixels just inside and just outside each edge of every box object differ, and a real press on each lands where `SceneInteraction.hitTest` says: the hit region and the drawn edge coincide. |
| `interaction` | Only when `RendererConformance.withInteraction` declares the game's interaction (FS.GG.Game#751), with REAL input (trusted pointer and keyboard events and real focus). The states declare valid selectable objects. On every backend: the input host is attached while mounted; the accessible mirror lists every object, in order, with its label; a pointer move leaves the surface at `tabindex` 0; the arrows move the focus and Enter selects through the game's own rule, and Escape clears; a pointer press on each object a pointer can reach selects it (the backend's hit test agrees with `SceneInteraction.hitTest`), and a press where no object is asks for nothing; Tab walks the mirror entries and Space selects one; keyboard focus stays on that entry across the selection's Present and across unchanged Presents; an unchanged Present does not write the live status; the selection survives a switch away and back; switching away and disposing the host release the input host. |

`Resources` must observe the page independently of the adapter. `BrowserConformance.resources` counts SVG
roots, canvases, Babylon engines, Pixi applications and live WebGL contexts. The kit also asserts its
subject: a probe that never sees a mounted backend fails, and so does fewer than two distinct states.
In .NET, `RendererConformance.subject` uses the in-process host (`RendererConformance.inProcess`);
`withStart`, `withWaitUntil` and `withLateFailure` adjust it. `tests/Game.Render.Tests/RendererConformanceTests.fs`
shows the shape, with negative controls.

### Interaction contract (FS.GG.Game#751)

A game whose play is selection stays playable on every backend through one contract, `SceneInteraction`:

```fsharp
let selection = SelectionRoute.create ()
let interaction = SceneInteraction.create objects selected select   // 'State -> objects; 'State -> id; request -> 'State -> 'State
let renderers =
    BrowserRenderers.declare container options route view
    |> SceneRenderers.withInteraction interaction selection
// When the game runs: send each SelectionRequest to the game's own update.
selection |> SelectionRoute.bind (fun request -> game.Dispatch(SelectionChanged request))
```

- **Hit test.** A primary pointer press on the topmost object containing the point asks to `Select` it. SVG uses
  the browser's own hit test over transparent hit shapes, Pixi its `EventBoundary` over hit regions kept off the
  stage, Babylon `Scene.pick` through its camera over invisible meshes. All three agree with the reference
  `SceneInteraction.hitTest`.
- **Keyboard.** The surface is focusable (`role="application"`, `tabindex="0"`). Arrows move the focus, Enter or
  Space selects and Escape clears: the retained SVG surface's keys, through `SceneInteraction.key` on canvases.
- **Accessible mirror.** An HTML group (`data-scene-controls`, `data-renderer`) holds one button per object in
  order, labelled, with `aria-pressed` on the selected one, and a polite status naming the selection.
- **Input-host disposal.** The mount's `Dispose` removes the input host's listeners and the mirror.

The selection is game state: a backend shows it and asks to change it (only for a change), so it survives every
switch. `RendererConformance.withInteraction interaction route probe` adds the `interaction` section;
`BrowserConformance.interactionProbe container` plays the real input hosts with real input: the page asks the
test driver (`window.__fsggInput`, Playwright `page.mouse` and `page.keyboard`;
`tests/Render.Browser.Smoke/driver/run-smoke.mjs`) for each click, move and key, never dispatching synthetic events.
A page-level census (`BrowserConformance.installInputProbe`) counts the input hosts' listeners and writes to their
live status. The mirror writes only what changed, so focus and the live region stay quiet across Presents. The SVG
surface's retained host (FS.GG.UI.Scene 0.31.0) still rewrites its mirror on each Present; the smoke asserts that
known gap until FS.GG.Rendering#1504 lands. The reference game
(`ReferenceRenderers.interaction`) selects its token and aim marker and draws a ring around the selection.

Adapter authors keep two obligations the kit checks:

- `Mount` returns `Error` instead of acquiring half a backend, and `Dispose` releases everything, including
  the WebGL context.
- An adapter that initialises asynchronously reports a late failure only while its own mount is undisposed.
  The driver cannot tell two mounts of the same backend apart.

### Theme tokens (FS.GG.Game#789)

A `Theme` is the view input every glyph colour, stroke and size comes from (ADR
`docs/roadmaps/game-render-02-glyphs.md`, D4 and D11). It has three layers:

- the **seed** (`ThemeSeed`): a small closed set of named colours and numbers, such as `ground`, `ink`, the allegiance
  and team hues, `contrastTarget`, `cell` and `minCssPerUnit`;
- the **map** (`ThemeTokens`): what the algorithms derive, in order (`ThemeAlgorithm.standard` first);
- the **alias** layer: the closed set of names glyph code reads, such as `glyph.team.0`, `cue.focus.outer` and
  `size.cell`.

`Theme.create seed algorithms overrides` returns the theme or every violation: opaque colours only, a 3:1 WCAG 2 floor
against the ground (`contrastTarget` raises it), and 3:1 between colours that touch. `Theme.withOverrides` re-validates a
player's team-colour override. `Theme.validate checks theme` adds named `ThemeCheck`s, such as FS.GG.Game#795's
distinctness and colour-vision checks; the theme carries them, so `Theme.withOverrides` runs them again.
`Theme.classic` is the baked default preset. FS.GG.Game#795 adds the other presets.

Colours are derived in OKLCH and quantised with a clamp then `Floor(v + 0.5)`. Contrast is computed on the 8-bit values
through a committed table, so .NET on Linux and Windows and node (through Fable) give the same bytes.
`scripts/test-theme-determinism.sh` checks this against `tests/Game.Render.Theme.Determinism/expected.txt`, including the
full 8-bit round trip. `Theme.cssVariables` and `Theme.hex` export the aliases for the parts of a game the Scene does not
draw. `Theme.toDtcg`, `Theme.ofDtcg` and `Theme.dtcgResolver` read and write DTCG 2025.10 token files, preferring `hex` on
import. To regenerate the baked preset after an algorithm change, run
`dotnet run --project tests/Game.Render.Theme.Determinism -- --bake src/Game.Render/ThemeBaked.fs`, then regenerate
`expected.txt`.

### Fable and the browser host

The package also ships a bounded Fable source view (`fable/RendererSelection`, `SceneInteraction`,
`SceneRenderers`, `RendererConformance` and `Theme`, each `.fsi` and `.fs`, with the baked `ThemeBaked.fs`) so a Fable-compiled browser host can register adapters
against the same `RendererRegistry` and run the conformance kit. `Adapter` and
`DefaultKeymap` are .NET-only and are not in that view. The view is first published in 0.18.0 (0.17.0 carries no
Fable view). Its packed `fable/FS.GG.Game.Render.fsproj` lists its sources unconditionally, because Fable does not
evaluate MSBuild conditions in a package's project file. An earlier unreleased form, added after `v0.17.0` by #733,
used conditional item groups, and they would have compiled no sources for a package consumer. It was fixed before
publication.

The browser host is the `FS.GG.Game.Render.Browser` package (from `src/Render.Browser`, first published in
0.18.0). It mounts the existing SVG surface (`SvgBrowser`), hosts the BabylonJS and PixiJS adapters (over the
Templates#809 and #808 bindings, shipped as source), and adds `BrowserRenderers` and `BrowserConformance` for the
authoring path. A game references that one package from its Fable project. `scripts/test-render-browser-host.sh`
proves the host in a headless browser and compiles a clean consumer of both packages
(`tests/release/test-render-browser-package-consumer.sh`).

House style: `.fsi` is the sole public surface; `net10.0`; `-preview` channel.
See [FS-GG/FS.GG.Game](https://github.com/FS-GG/FS.GG.Game) and [ADR-0022](https://github.com/FS-GG/.github/blob/main/docs/adr/0022-extract-fs-gg-game-as-an-sdd-driven-component.md).
