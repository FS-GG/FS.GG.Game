# GAME-RENDER-02.11 — Shaders and visual effects (design, WORK IN PROGRESS)

Status: **WIP draft**, stopped at a session end. Not ready for review. Delivery: FS.GG.Game#813. Parent Outcome:
FS-GG/.github#5218.

## Binding owner requirement (FS-GG/.github#5218, 2026-11-02)

SVG and PixiJS must be equivalent, so SVG can be used interchangeably in documentation.
- The SVG rung of the fallback ladder must equal Pixi within a measured, stated tolerance on decided pixels.
- The portable effect set is built from what SVG filter graphs can match. Any other effect is either excluded or
  marked `docSafe = false`, with a static equivalent.
- A custom shader counts as doc-safe only with an author-supplied SVG filter graph.
- #814 consumes the doc-safe set this design states.

## Findings so far

- **The Scene model** (FS.GG.Rendering `src/Scene/Types.fsi` at `2d35dc9b`): effects live on `Paint` as `BlendMode`,
  `Shader` (solid and gradients), `ColorFilter.BlendColor`, `MaskFilter.Blur`, `ImageFilter.DropShadow` and
  `PathEffect` (Dash, Discrete, Corner). There is no group or layer effect node.
- **Render.Browser** does not read any of these fields. Pixi and Babylon draw only `Rectangle`, `FilledEllipse` and
  `Circle` (`SceneShapes.collect`). Since #801, the kit undecides every pixel a blur, drop shadow or displacing path
  effect could reach.
- **Planned pass order** (glyph ADR D10): amplifiers, then a new selection-effect underlay (dilate, plus blur for the
  glow), then bodies, then brackets, then the cue layer. Because bodies draw over the underlay, frame and badge
  pixels keep their identity.
- **Planned proof route:** one GLSL ES 3.00 pass list per effect, with thin Pixi (`Filter`/`GlProgram`) and Babylon
  (`EffectRenderer`/`EffectWrapper`) preludes. The SVG side is a filter graph with `color-interpolation-filters="sRGB"`
  and a user-space region covering the board. The custom "teleport" effect is a port of the spec's `feTurbulence`
  reference code, with a lattice texture and 8-bit thresholds, equal to `feTurbulence` plus discrete
  `feComponentTransfer`.
- **Prototype:** `game-render-02-effects/proto/` (plain JS; run it with `fdev-chromium-env -- node run.mjs`). It was
  not yet run to completion: the capacity probe answered `hold` (load), so no measurement exists yet.

## Licence notes (research brief, to be folded in)

- **Permissive:** naga (MIT OR Apache-2.0), SPIRV-Cross (Apache-2.0), Tint (BSD-3-Clause), FShade (Apache-2.0),
  Slang (Apache-2.0 WITH LLVM-exception), WESL (MIT OR Apache-2.0), PixiJS and pixi-filters (MIT), Babylon.js
  (Apache-2.0), Skia (BSD-3-Clause), SkiaSharp (MIT), SwiftShader (Apache-2.0).
- **Flagged:** glslang's licence is mixed and includes a GPL-3.0 Bison section, so it needs a per-file audit.
  BabylonJS/twgsl's licence is Unknown.

## Remaining

The full design (inventory, vocabulary with ladders and doc-safe set, theme effect token proposal, custom-shader
route, determinism and kit, performance on the #792 bench, security), the F# proof in the smoke on SVG, Pixi and
Babylon, open questions, the Atlas record, and coordination comments on #789 (with #812, #810 and #802).
