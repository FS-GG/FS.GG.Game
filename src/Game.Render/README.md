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

### Fable and the browser host

The package also ships a bounded Fable source view (`fable/RendererSelection.fsi` and `.fs`) so a
Fable-compiled browser host can register adapters against the same `RendererRegistry`. `Adapter` and
`DefaultKeymap` are .NET-only and are not in that view. The repository's `src/Render.Browser` project
(not packed) is that host: it mounts the existing SVG surface (`SvgBrowser`), hosts the BabylonJS
and PixiJS adapters (over the Templates#809 and #808 bindings). `scripts/test-render-browser-host.sh` proves it in a headless browser.

House style: `.fsi` is the sole public surface; `net10.0`; `-preview` channel.
See [FS-GG/FS.GG.Game](https://github.com/FS-GG/FS.GG.Game) and [ADR-0022](https://github.com/FS-GG/.github/blob/main/docs/adr/0022-extract-fs-gg-game-as-an-sdd-driven-component.md).
