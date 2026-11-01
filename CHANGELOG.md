# Changelog

Release notes for the FS.GG.Game coherent package set: `FS.GG.Game.Core`, `FS.GG.Game.Render`,
`FS.GG.Game.Harness`, `FS.GG.Game.Physics.Box2D` and `FS.GG.Game.Render.Browser`. They are released together at
one version through `.github/workflows/release.yml`, published to the FS-GG GitHub Packages feed first and then to
nuget.org. Each package's `PackageReleaseNotes` links to this file at its release tag.

The shared WASM packages (`FS.GG.Wasm.*`, tags `wasm/v*`) and `FS.GG.Game.Skills` are released separately and are
not listed here.

## 0.19.0

A minor release with additive public API. Every package is validated against its published 0.18.0 API.
Delivery: FS.GG.Game#753 (#778), release-route repair FS.GG.Game#764 (#781).

### FS.GG.Game.Render.Browser

- **Babylon draws 2D scenes at their declared positions and sizes, at any node count.** `BabylonSurface` used to
  stack one mesh per Scene node under a perspective camera, so later nodes were magnified about the canvas centre,
  and beyond about 400 nodes they were culled. It now views the plane through an orthographic camera, one scene unit
  per pixel. Materials take no specular term, and round shapes share one sphere's vertex buffers.
- **The scene renders when a state is presented.** The render loop draws again only while a material is still
  compiling or after a context restore, instead of every frame.
- **Picks are correct on a CSS-scaled canvas.** Babylon picks are scaled from board units to the live render size,
  so a board styled larger than its backing size, at any device pixel ratio, hits the object under the pointer.
- **`BrowserConformance` gains pixel support:**
  - `pixelProbe` and `drawnPixels` read SVG (rasterised), Pixi and Babylon output the same way, in board units.
  - `pressBoard` and `moveBoard` drive pointer input on a styled canvas.
  - `deviceScale` sets the page's device pixel ratio and verifies it.
  - `babylonActiveMeshes` counts the meshes Babylon selected for drawing.

### FS.GG.Game.Render

- **The conformance kit gains a `pixels` section:** `RendererConformance.withPixels`, `PixelProbe`,
  `PixelExpectation.create` and `ScenePixels.expectations`.
  - `ScenePixels.expectations` derives the expected pixels from the Scene at a canvas size: every node's colour where
    it is drawn, and the clear colour wherever no node touches.
  - With `withInteraction`, each box object's hit region and drawn edge must coincide.
  - The subject stays opaque, so this is additive.

### Release route

- The public consumers wait, bounded, until nuget.org's flat-container version list names the release version for
  every released id. A listing lag can no longer fail a published release, as it did for 0.18.0 (NU1102).
- The dual-feed readback evidence is kept as soon as readback passes, before the public consumers run.
- Packages carry `PackageReleaseNotes` pointing at this file.

### Unchanged

`FS.GG.Game.Core`, `FS.GG.Game.Harness` and `FS.GG.Game.Physics.Box2D` have no source change; they move with the set.

## 0.18.0

Release run 41324010304, tag `v0.18.0`. FS.GG.Game#745, #750, #751, #752.

- **First release of `FS.GG.Game.Render.Browser`,** the Fable browser host:
  - `RendererDriver`;
  - the SVG, retained SVG, Pixi and Babylon surfaces;
  - `BrowserRenderers` and `BrowserConformance`;
  - the PixiJS 8.22.0 and BabylonJS 9.33.1 bindings, as pinned source.
- **`FS.GG.Game.Render`** gains the three-adapter authoring kit (`SceneRenderers`, `RendererConformance`),
  `RendererRegistry.create`, `SceneInteraction`, `PresentFailed` with bounded recovery and the viewport remount rule.
  It publishes its bounded Fable source view for the first time.
- **Release route:** the new package id is pushed first and alone on each feed, and a NuGet verify-key HTTP 400 is
  recorded as unverifiable.

Earlier releases are recorded in the FS-GG registry changelog (`FS-GG/.github`, `registry/CHANGELOG.md`).
