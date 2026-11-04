# FS.GG.Game.Render.Browser

Fable browser host for [`FS.GG.Game.Render`](https://www.nuget.org/packages/FS.GG.Game.Render) renderer
selection. A game declares its SVG, PixiJS and BabylonJS renderers once, from one `view: 'State -> Scene`, switches
between them at runtime without losing state, and proves each one with the shared conformance kit.

The package contains:

- `SvgSurface`, `PixiSurface` and `BabylonSurface`, the scene surfaces and adapters over the shared
  `FS.GG.UI.Scene`. The canvas surfaces draw `Rectangle`, `FilledEllipse` and `Circle`, treat `Group`,
  `Translate` and `Empty` as structure, and count other nodes as unsupported.
- `RetainedSvgSurface`, a game's own retained SVG scene as a renderer adapter. It keeps the game's selectable
  objects, accessible labels, camera and input host.
- The `SceneInteraction` input contract on the Pixi and Babylon canvases, through an internal input host.
- `RendererDriver`, which runs the `RendererSelection` Elmish loop for a page that is not already an Elmish
  program. Dispatch is queued, so adapter callbacks never reenter the host. `failBackend` reports a failure
  of an asynchronously initialising backend.
- `BrowserRenderers`, which declares and starts all three backends for one game-state type and routes Pixi's
  late initialisation failures to the driver that mounted them.
- `BrowserConformance`, the browser side of `RendererConformance`. It provides WebGL context and resource
  probes, frame waits, pixel readback and the conformance subject.

## Use

It is a Fable library: reference it from a Fable project, and Fable compiles its sources (`fable/`). The .NET
assembly exists for editors and type checking only.

```xml
<PackageReference Include="FS.GG.Game.Render.Browser" Version="0.19.0" />
```

This also brings `FS.GG.Game.Render`, `FS.GG.UI.Scene` and `FS.GG.UI.Scene.SvgBrowser`. Install the
JavaScript packages the bindings were generated against, at these exact versions (`dotnet femto` reads them
from the package):

```sh
npm install @babylonjs/core@9.33.1 pixi.js@8.22.0
```

The exact npm closures (`package.json` and `package-lock.json`) are in the package under `js-bindings/babylon`
and `js-bindings/pixi`.

To start a game's renderer module, generate it, then replace the placeholder `view`. The generator is a repository
script, not part of this package: run `scripts/new-game-renderers.sh` from a checkout of
[FS-GG/FS.GG.Game](https://github.com/FS-GG/FS.GG.Game). It renders `eng/render-kit/GameRenderers.fs.template`.
Use the release tag of the package you reference, for example `v0.19.0`.

```sh
scripts/new-game-renderers.sh --namespace MyGame.Client --module Renderers --state MyGame.Model \
  --label "My game" --output src/Client/Renderers.fs
```

```fsharp
let driver = Renderers.start container initial onChange
driver.Dispatch(RendererMsg.SelectBackend RendererBackend.Pixi)
```

The `FS.GG.Game.Render` README has the full walkthrough and the conformance kit's requirements, under
"Three-adapter authoring path".

## Bindings

The PixiJS (`Qualification.Pixi`) and BabylonJS (`Qualification.Babylon`) bindings are the unedited output of
the `fs-gg-fable-bindings` template at the FS.GG.Templates commit pinned in the repository's
`eng/js-bindings/pin.env`. They ship as source (`fable/generated/`), because the source is what the pin
records. A compiled bindings assembly differs from one .NET SDK to another. The pack refuses bindings that
are not the pinned bytes. The two modules are public in a consumer's compilation, so a consumer must not
define its own `Qualification.Pixi` or `Qualification.Babylon` module.

## Known limits

- Babylon draws lit at about 0.75 of the scene colour and ignores `Color.Alpha`. It draws a 2D scene through an
  orthographic camera, so every node is at its declared position and size at any node count (FS.GG.Game#753); one
  mesh and one draw call per node remain (instancing would need a per-instance colour shader).
- There is no runtime `webglcontextlost` handling, and WebGPU is not exercised.
- `failBackend` cannot tell two mounts of one backend apart. An adapter that initialises asynchronously
  reports only while its own mount is undisposed, as `PixiSurface` does.

See [FS-GG/FS.GG.Game](https://github.com/FS-GG/FS.GG.Game).

## Sizing an input-host surface (FS.GG.Game#788)

`SvgSurface.mountInput`, `PixiSurface.mountInput` and `BabylonSurface.mountInput` (the hosts of
`SceneRenderers.withInput`) mount their output inside a frame the host owns: `<div data-input-frame="svg|pixi|babylon">`.
The host places the SVG root or canvas in the frame, out of layout, so its width and height attributes never size
anything.

- Without page CSS, the frame takes the scene size given in the options, in any parent: it keeps the scene's aspect
  ratio and a flex or grid parent does not stretch it.
- To size the surface, set the frame's own `width` and/or `height`. With one set, the other follows the scene's aspect
  ratio: `#stage [data-input-frame] { width: 480px; }`. A container's size does not size the frame; to follow the
  container, say so on the frame: `#stage [data-input-frame] { width: 100%; }`.
- Whatever box results, the scene is drawn at one uniform scale, centred and letterboxed (contain-fit). Pointer
  mapping, picking and the focus ring's 2 CSS px bands use the same fit.
- Style the frame by `[data-input-frame]` only. A rule such as `#stage div { … }` also reaches the frame (and the
  sizer, whose inline style ignores it); its width, height, padding or border change the frame's box. Never style the
  canvas or SVG root, which the host places.
- A canvas renders at the fitted size × the device pixel ratio, re-derived when the frame is resized or the ratio
  changes, and never over 8192 device pixels a side.

See ADR 0001, "Surface sizing".
