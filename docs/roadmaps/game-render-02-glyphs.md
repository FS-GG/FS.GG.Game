# GAME-RENDER-02.4 — Shared glyph and symbology layer with theme tokens (ADR)

Status: **proposed**. This is the design PR for FS.GG.Game#789. It adds an experimental proof and no shipped API.
An implementation PR follows once the ADR is accepted.
Route: routine. Parent Outcome: FS-GG/.github#5218 (GAME-RENDER-02).
Builds on `game-render-adopt-01.md`, which covers the kit and `ScenePixels` (#745, #753), and on the renderer
selection contract in FS.GG.Game 0.19.0.
Related work:
- #773 adds canvas strokes.
- #786 makes Babylon colours unlit.
- #787 is the input-pipeline ADR (PR #793, `docs/adr/0001-standard-input-pipeline.md`). It is a separate file, and
  D10 is reconciled with its "focus and hover are drawable state" decision.
- #790 covers the renderer roles and SVG import and export.
- FS.GG.Rendering owns the Fable view of `FS.GG.UI.Scene`.

## Problem

Every game draws its own symbology, using the same few fill-only nodes. The survey below was taken on 2026-11-01 from
the `origin/main` of each repository:

| Game | Where | What it draws | Workarounds it carries |
|---|---|---|---|
| RuneScape | `src/FS.GG.RuneScape.Frontend/Scene.fs` (`RsScene`, PR #13) | tiles, scenery squares, item ovals, NPC and player discs, path dots, destination ring, selection behind the target | its own `node`, `group`, `rect`, `disc` and `oval` helpers. It keeps 15 hard-coded colours, all opaque because "Babylon ignores alpha and lights flat colours to about three quarters". Its insets avoid the kit's 0.2/0.8 hit probes (FRONTEND.md, gap 2). |
| BAR | `src/Broker.Browser.Render/BarScene.fs` | own and visual unit discs, radar and feature squares, a selection disc behind the target, a stale veil (alpha 150) | the same helpers under other names. `docs/GAME-RENDER-ADOPT-01.9.md` gap 3 says: "The Fable view of `FS.GG.UI.Scene` has the types but not the `Scene` module constructors… the constructors belong with FS.GG.Rendering". |
| FourD | `EncounterProjection.fs` / `EncounterRenderer.fs` | 40-unit cells with a light theme, area and path insets, barrier bars | an outline made of four 3-px rectangles. A third copy of the symbology lives in `web/interaction.css`. `objects` reads bounds back from the first node of each cell. |
| SC2 | `src/SC2.Client.Rendering/TacticalRenderer.fs` | a unit disc per allegiance on the ground | a cursor of four bars standing in for a stroked ring. The palette is kept twice, as hex and as `Color`, because "`Colors` is not in the Fable view". The SVG map is hand-built DOM. |
| Template | `templates/fs-gg-fable-game/Client/Renderers.fs` | arena, self disc, other squares, preview, selection | a palette copied from the `.cell` CSS classes in `index.html` |

What the games have in common:
- **Fill-only primitives.** Each game uses only `Rectangle`, `Circle` and `FilledEllipse` inside `Group`. That is the
  canvas ceiling: `src/Render.Browser/SceneShapes.fs` counts every other node as unsupported and skips it.
- **The same selection colour.** BAR, FourD and the template use `255,140,0`, and RuneScape uses `255,120,0`.
- **Duplicated helpers.** A colour constructor is written five times and node helpers twice. Every game derives its
  `SelectableObject` bounds again from its glyph geometry.
- **Strokes faked by layering.** Rings are a disc behind or on top of another disc. Outlines are four bands or an
  inset rectangle.
- **No health bars, text or team colours** appear in any Scene.

## Decisions

### D1. The glyph model: what a glyph means, separate from how it looks

A glyph is a value that describes what it means and where it is. It never describes its colours:

```fsharp
// FS.GG.Game.Render, namespace FS.GG.Game.Render.Glyphs (proposed .fsi shape; opaque records, additive)
type GlyphState = { Selected: bool; Stale: bool }   // game state only; focus and hover are #787's InteractionCues
type Glyph =
    | Unit      of UnitGlyph      // shape class, allegiance/team, anchor, footprint, facing
    | Structure of StructureGlyph // footprint in cells, allegiance/team, anchor
    | Terrain   of TerrainGlyph   // kind token, cell rect
    | Item      of ItemGlyph
    | Marker    of MarkerGlyph    // waypoint, destination, target, ping
    | PathGlyph of Point list * PathStyle
    | Status    of StatusGlyph    // bar: fraction, kind (health, progress)
    | Selection of Rect
    | Custom    of (Theme -> GlyphDrawing)
type GlyphDrawing = { Nodes: SceneNode list; Bounds: Rect; Hit: SelectableShape }
val draw  : Theme -> Glyph -> GlyphDrawing
val scene : Theme -> Glyph list -> Scene
val cues  : Theme -> (InteractionCues -> SelectableObject list -> SceneNode list)  // for SceneInteraction.withCues (#787)
```

- **One function.** `draw theme glyph` is pure, total and deterministic. It returns the Scene nodes and the hit
  geometry together, so a game's `SelectableObject` bounds and its drawing cannot drift apart. Today FourD reads its
  bounds back from the first node of each cell, and RuneScape computes its geometry twice.
- **Parameters.**
  - Every size comes from the theme's cell, scaled by the glyph's footprint.
  - Team colour is an index into the theme's team list.
  - Allegiance (own, ally, neutral or enemy) selects its own tokens, because SC2, BAR and RuneScape colour by
    allegiance or kind, not by team.
  - `GlyphState` selects the decorations that are game state: selection and stale. Focus and hover are input-host
    state, drawn by the cue layer (D10).
- **Composition.** A glyph is made of Scene primitives only. It adds no node kind and no new renderer surface.
- **Portability.** The library uses only `SceneNode` data, so it compiles for .NET and through Fable from the same
  files, and it joins the bounded Fable view of FS.GG.Game.Render.
- **`Custom`** is the escape hatch for a game-specific glyph. It still takes its colours from the theme and still
  returns its hit geometry.

### D2. Two lowering profiles, with one profile for all backends

A glyph lowers to one of two profiles. The same profile is used on every backend, so all three draw the same pixels.

| Profile | Nodes | Drawn by | Kit model |
|---|---|---|---|
| **Portable** (the default now) | opaque `Rectangle`, `Circle`, `FilledEllipse`, through `Group` and `Translate` | SVG, Pixi and Babylon today | `ScenePixels` today |
| **Stroked** (after #773) | adds `Ellipse` and `PaintedRectangle` with `Paint.stroke`, `Line`, and `Path` for polygons and polylines (filled or stroked) | SVG today; Pixi and Babylon after #773 | after the D6 extension |

- **Portable outlines.** The portable profile draws an outline as filled bands, a ring as a disc with a smaller disc
  on top, and brackets and crosses as bars. The proof uses this profile.
- **What the stroked profile changes.** It replaces those emulations with real strokes of the same geometry, so every
  pixel the kit decides stays the same. It also adds shapes that need paths: triangles, diamonds, hexagons, facing
  arrows and path polylines.
- **Switching profiles.** A game switches by setting the theme's `Profile` field. The default changes only when #773
  and the D6 kit models have shipped.
- **Pixel snapping.** At a cell of 16 units or more, glyph geometry snaps to whole scene units. Edges then fall on
  pixel boundaries, so SVG antialiasing and Pixi's lack of it cannot move a decided pixel. The proof uses `Floor`,
  never `Round`. The F# `Math.Round` and the Fable `Math.Round` might break a half-way tie differently, which would
  make the .NET and Fable drawings differ.

### D3. The starter glyph set

| Family | Portable profile | Stroked additions |
|---|---|---|
| **Units** | disc ringed in team or allegiance colour; square and wide (ellipse) shape classes | triangle, diamond and hexagon classes; a facing notch |
| **Structures** | body square with a team band along its foot; footprint of n×m cells | keyline outline |
| **Terrain** | a tile inset by the theme's gap, coloured from the terrain-kind tokens (open, blocked, difficult, water, void) | a hatch per kind, so terrain does not rely on colour alone |
| **Items** | a small oval at the item anchor | — |
| **Markers** | waypoint plus, destination ring (disc plus inner disc), target crosshair (four bars, as in SC2) | stroked rings, ping |
| **Selection** | corner brackets just outside the glyph bounds | — |
| **Focus** (cue layer, D10) | #787's two-tone ring: a dark outer band and a light inner band of filled rectangles, each at least 2 units, just outside the object's bounds, in the theme's `FocusOuter`/`FocusInner` tokens. It differs from selection by shape as well as colour. | — |
| **Hover** (cue layer, D10) | a one-unit outline in the hover token | — |
| **Path** | dots at a theme spacing, and a step marker at the end | polyline |
| **Status** | track plus a fill whose length is the fraction, coloured good, warn or bad by theme thresholds | — |
| **Team colour** | ring or band, from `Theme.Teams` or the allegiance tokens | — |

A stale veil (BAR) needs alpha. Babylon ignores alpha, so the portable profile draws a stale glyph with its body in the
`Stale` token. The veil waits for alpha support; see Q5.

### D4. Theme tokens

`Theme` is an opaque record, built and validated by functions:

- **Palette:**
  - `Ground`;
  - terrain kinds;
  - `Body`;
  - `Teams` (a list, which wraps);
  - allegiance (`Own`, `Ally`, `Neutral`, `Enemy`);
  - `Selection`, `FocusOuter`, `FocusInner`, `Hover`, `Marker`, `Path`;
  - status `Track`, `Good`, `Warn`, `Bad`;
  - `Stale`.
- **Stroke:** outline width, keyline width, and the **gap** between a glyph and its selection or focus outline. The
  proof's first screenshots showed why the gap is a token: a white focus outline touching a near-white structure body
  merged with it.
- **Sizes:** `Cell`, the unit, item and marker scales, status height, terrain gap and path dot spacing.
- **Status thresholds:** `Warn` and `Bad`.
- **Contrast** (`Theme.validate` returns every violation, and a theme that fails cannot be built):
  - Every token drawn directly on `Ground` (teams, allegiance, selection, outer focus, hover, marker, path and body) is
    at least **3:1** against it. The inner focus tone is at least 3:1 against the outer one (D10). This is the WCAG 2.x non-text contrast floor (SC 1.4.11), using relative luminance.
  - Every token is opaque until alpha is supported (Q5).
- **Shipped themes:**
  - `standard`, dark.
  - `light`, for FourD's look.
  - `colourBlindSafe`, built from the Okabe–Ito palette. Its hues stay distinct under protanopia, deuteranopia and
    tritanopia. Okabe–Ito blue (0,114,178) is 2.86:1 on the dark ground, under the floor, so team 0 is their sky blue.
    The proof's tests found this.
  - `highContrast`, whose tokens are at least 7:1 against the ground.
- **No meaning by colour alone.**
  - Selection is corner brackets and focus is a full outline.
  - A status bar reads by its length.
  - Terrain gets a hatch in the stroked profile.
  - Team identity beyond hue is Q2.
- **Export for non-Scene paths.** `Theme.cssVariables theme` gives the same tokens as CSS custom properties, and
  `Theme.hex` gives each as hex. This covers the parts of a game the Scene does not draw: SC2's DOM map, FourD's CSS
  grid, the template's `index.html` and BAR's legacy `runtime.js`. Each of those then stops keeping a second copy of
  its palette.

### D5. Sprite atlases for licensed art, with the assets kept out of repositories

- **Manifest.** A `SpriteAtlas` is a manifest, `fsgg.sprite-atlas/v1` JSON. For each frame it holds an id, a frame
  rectangle in the atlas image, an anchor, a size in cells and a **mandatory vector fallback glyph**. It also holds
  the image's sha256 and licence identity. The image itself is never in the manifest.
- **Drawing.** `Glyph.Sprite(atlas, frame, ...)` draws `SceneNode.Image` when the atlas image has been resolved, and
  draws its vector fallback otherwise.
  - Sprite drawing on canvases needs image support: a Pixi `Sprite` from a texture, and a Babylon textured plane that
    is unlit as in #786. That support is the sprite slice of the implementation.
  - SVG draws `<image>`.
- **Custody.** The manifest schema and any **open-licence** atlas may be committed. A **restricted** atlas, such as
  RuneScape's rs-sdk content ("**No game assets in git.**", `README.md`, enforced by `eng/check-no-assets.py`), is
  handled as follows:
  - Its image and any frame geometry derived from the restricted source stay in the game's gitignored fetch area
    (RuneScape's `.upstream/`). At run time the game resolves the image through a URL or loader it configures.
  - Only the manifest's schema, ids and the fallback glyphs are tracked.
  - Package payloads never contain the image.
  - This follows FS.GG.Rendering's licensed tactical-glyph fixture (#1495 and the `svg-coherence-and-instancing-01`
    roadmap). There, "exact licensed geometry stays outside public source and package payloads". That roadmap's
    private fixture is checked in private custody.
- **CI and the kit.** Public CI and the kit run on the vector fallback, so a repository without assets is fully
  playable and fully probed. Pixel probes of the sprites themselves run only in private custody, and their evidence
  is not published.

### D6. Identical on all three backends, and the kit's pixel coverage

"Identical" means that on SVG, Pixi and Babylon, every pixel `ScenePixels` decides shows the declared colour within a
per-channel tolerance.

1. **Prerequisite #786 (unlit Babylon).** Babylon is to draw fills unlit or emissive. The kit's 0.75 Babylon
   appearance factor and its tolerance of 4 are then removed, and one colour and one tolerance (2) apply to all three
   backends. The glyph library has no lighting-dependent token. Until #786 lands, the kit keeps the factor, and the
   proof uses it.
2. **Prerequisite #773 (canvas strokes)** for the stroked profile, with the same stroke geometry SVG uses: centred on
   the path, with the width in scene units.
3. **Extending `ScenePixels`** beyond rectangles, ellipses and circles. Each item below is a model of exact coverage,
   with the same Full/Partial/Clear pixel test the kit uses today:
   - a **ring**, the annulus of a stroked circle or ellipse;
   - a **band**, a stroked line drawn as a capsule or butt-capped rectangle, and a stroked rectangle as four bands;
   - a **polygon**, a filled `Path` of `MoveTo`/`LineTo`/`Close`, using nonzero or even-odd winding. A pixel is Full
     when its four corners are inside and no edge crosses it.
   - **Bounded unmodelled nodes.** Today any node the kit does not model (text, an image, a curve) is `Partial` for
     **every** pixel. One such node anywhere makes the whole scene undecided: `expectations` returns nothing, and the
     kit's subject check ("every state declares at least one pixel") then refuses the scene. A game cannot add one
     label or path without losing pixel coverage for the whole scene. With this change, a node whose bounds are known (`Path.bounds`, image bounds,
     arc bounds) is `Partial` only inside them. Text and nodes without bounds stay unbounded.
   - **Samples** for each new model: its middle, the first whole pixel inside and outside each edge, and, for rings
     and bands, one pixel across the stroke.

   The kit then gains a **glyph section**, `withGlyphs theme glyphs`. On every backend it draws the starter set in each
   shipped theme and checks every decided pixel, with a theme-swap control that must not match.

### D7. The Fable `Scene` constructor gap: fixed in FS.GG.Rendering

- **The gap.** The curated Fable project of `FS.GG.UI.Scene`, `src/Scene/Fable/FS.GG.UI.Scene.fsproj` at Rendering
  `2d35dc9b`, compiles `Types`, `RetainedSvg`, `SpatialWorkingSet`, `Svg*` and `Animation`. It does not compile
  `Scene.fs`, which holds the modules `Colors`, `Paint`, `Path` and `Scene`. Its compatibility profile grades "Scene
  constructors, text shaping, inspection, evidence, codec and wire format" as `DotNetOnly`.
- **Why.** The constructors are pure record and union builders. What is not portable in `Scene.fs` is the text
  shaping, which depends on `TextShaping.fs` and uses `System.Security.Cryptography` for fingerprints, together with
  inspection and evidence.
- **Where it is fixed.** **FS.GG.Rendering**, which owns the package and its compatibility profile. The pure
  constructors (`Colors`, `Paint`, the `Path` builders and the node builders of `Scene`) move to a portable source
  that the Fable project compiles, and the profile grades them `Portable`. Text shaping, measurement, inspection,
  evidence and the codec stay `DotNetOnly`. Rendering chooses whether that means a separate module or a split of
  `Scene.fs`.
- **Game's side.** Game does **not** publish a second `Scene` or `Colors` API, which would compete with Rendering's.
  Until the Rendering fix ships, the glyph library builds `SceneNode` data with private helpers, as the proof does.
  After the fix, the template, RuneScape, BAR and SC2 drop their copies of `node`, `group`, `rect` and `rgb`.
- **Owed intake:** a FS.GG.Rendering Delivery, "Scene constructors in the Fable view". This PR does not file it, and
  the coordinator owns that intake.

### D8. Relation to SVG export and SVG→Pixi import (#790)

- **Export.** A glyph scene is ordinary Scene data, so SVG export for documentation goes through the existing
  `FS.GG.UI.Scene` SVG path with no glyph-specific exporter. The library's role is to give docs one canonical look.
- **Import.** The **stroked profile's node set is the target vocabulary of #790's SVG→Pixi import**: opaque fill and
  stroke, rectangles, circles, ellipses, lines, and polygonal or polyline paths. An imported SVG element outside that
  set is refused with its reason, not approximated, so an imported asset is kit-probeable like a glyph.
- **Round trip.** #790 adds a round-trip test: a glyph scene is exported to SVG, imported back, and must give the same
  `ScenePixels` expectations. Curves (`QuadTo`/`CubicTo`/`ArcTo`) join the import set when the kit models them, or are
  flattened at import with a stated tolerance; see Q8.

### D9. Migration: the template and RuneScape first

The glyph layer is **additive**. No game has to adopt it in the same release.

1. **The template** (FS.GG.Templates, with FS.GG.Game's `eng/render-kit/GameRenderers.fs.template` and
   `scripts/new-game-renderers.sh`):

   | Template element | Becomes |
   |---|---|
   | arena | `Terrain` |
   | self | `Unit` (own) |
   | others | `Unit` (enemy, square class) |
   | preview | `PathGlyph` |
   | selection | `Selection` brackets |

   The palette becomes `Theme.standard`, and `index.html` takes `Theme.cssVariables`. The generated module stops
   writing `Color` records inline. The template's kit pixel expectations already come from `ScenePixels`, so they
   follow the new drawing without hand-written tables.
2. **RuneScape** (`RsScene`):

   | RuneScape element | Becomes |
   |---|---|
   | tiles | `Terrain` (walkable or blocked maps to open or blocked; void) |
   | locs | `Structure` (the gold "has options" colour becomes the `Marker` accent) |
   | items | `Item` |
   | NPCs and players | `Unit`, by allegiance (combat level above 0 means enemy, otherwise neutral) |
   | the player | `Unit` (own) |
   | path dots | `PathGlyph` |
   | destination | `Marker Destination` |
   | selection | `Selection` |

   Selectable objects come from `GlyphDrawing.Bounds` and `Hit`. Glyph insets avoid the kit's hit probes at 0.2 and
   0.8 of an object's bounds (RuneScape's gap 2): the library's hit geometry is the glyph's drawn outer bounds, not the
   cell.

   RuneScape's no-assets rule is unchanged: it adopts only vector glyphs. Its byte-identical .NET/Fable digests and
   its creature-colour probe are regenerated in the adoption PR.
3. **Later and optional:** BAR (allegiance and kind tokens; the veil waits for Q5), FourD (`Theme.light`; the CSS grid
   takes `cssVariables`) and SC2 (the DOM map takes `Theme.hex`).

Each adopting consumer's pixel tests change expected values in its adoption PR. That is a planned change in that game's
own PR, not a regression.

### D10. Focus and selection with the input pipeline (#787)

This decision is reconciled with the #787 ADR (PR #793, `docs/adr/0001-standard-input-pipeline.md` at `7ba30e53`). That
ADR decides that **focus and hover are drawable state held by the input host**: `InteractionCues` holds the focused
object, the hovered object and whether focus is visible. The cues are not game state and do not survive a backend
switch. Every interactive backend draws the interaction's cue layer above the frame's Scene. The default layer is
`SceneInteraction.standardCues`, and a game replaces it with `SceneInteraction.withCues`.

The glyph layer follows that split:

- **Selection** is game state (`InteractiveScene.Selected`). It is drawn in the frame's Scene as `Selection` brackets
  through `GlyphState.Selected`.
- **Focus and hover** are never in `GlyphState`. The library provides `Glyphs.cues theme`, a cue layer for
  `SceneInteraction.withCues`. It draws #787's two-tone focus ring and the hover outline in the theme's tokens, at the
  bounds of the cued object. The cue layer and `standardCues` keep the same geometry, so the kit's focus-cue check
  (#787 I8, the pixels just outside the focused bounds) holds for both. Only the colours come from the theme.
- **Contrast.** The theme validates both focus tones: the outer tone against the ground and the inner tone against the
  outer one, each at least 3:1. This is the two-tone rule #787 takes from WCAG 2.2 focus appearance.
- **The proof** predates that ADR. It draws focus as a single-tone outline in the frame's Scene. The implementation
  draws focus in the cue layer only.

## The proof (experimental)

The proof does not constrain the API.

- **Glyphs.** `src/ReferenceGame/ExperimentalGlyphs.fs`, namespace `FS.GG.Game.Reference.Experimental`, in a project
  that is not packed. It has six glyphs (unit, structure, waypoint, selection brackets, focus outline and status bar)
  and two themes (`standard` and `colourBlindSafe`), in the portable profile only.
- **.NET tests.** `tests/Game.Render.Tests/ExperimentalGlyphsTests.fs` checks five things:
  - every glyph lowers to the portable set;
  - every colour a glyph draws is a pixel `ScenePixels` decides;
  - the proof board decides more than 150 pixels;
  - both themes meet the 3:1 floor;
  - today one text node added to the board leaves no pixel decided. This pins the gap D6 closes, and the test is
    inverted when D6 lands.
- **Browser smoke.** `runGlyphProof` in `tests/Render.Browser.Smoke/Program.fs` draws the 256×128 proof board, one
  theme per row, on SVG, Pixi and Babylon. It requires every decided pixel to match on each backend, with a
  theme-swap control that must not match. It reports a named pixel per glyph part and takes a screenshot when
  `RENDER_SMOKE_SCREENSHOTS` is set; the driver gained a `screenshot` request.
- **Results** (2026-11-01, fdev, Chrome for Testing 153.0.8010.12): the smoke passed **490** browser checks, 484
  before this change and 6 added by the proof, both with SwiftShader and with the GPU (ANGLE Vulkan, RADV). On each
  backend all **391** pixels `ScenePixels` decides for the board matched, and the theme-swap control did not.
  - SVG and Pixi show the scene colours exactly.
  - Babylon shows 0.75 of them, within the kit's tolerance of 4, because it lights fills; #786 removes this.
  - Screenshots are in `game-render-02-glyphs/`, one per backend from the GPU run plus Babylon from the software run.
    The SVG screenshot also catches the SVG input host's "No object selected" status line: the smoke page has no
    visually-hidden style, so that text sits over the board. The renderer does not draw it.
  - The named-pixel table is in the PR.

## Implementation plan (after acceptance)

1. FS.GG.Rendering: Scene constructors in the Fable view (D7). This is owed intake.
2. FS.GG.Game: #786 (unlit Babylon) and #773 (canvas strokes) as they are. They are prerequisites of D6 and of the
   stroked profile.
3. FS.GG.Game: `FS.GG.Game.Render.Glyphs` and `Theme`, in the portable profile, with tests, the kit's glyph section
   and bounded unmodelled nodes. It joins the Fable view.
4. FS.GG.Game: the `ScenePixels` ring, band and polygon models and the stroked profile.
5. FS.GG.Game: sprite atlases with fallbacks and custody (D5), with image support on the canvases.
6. Release through the canonical route. Then template adoption (FS.GG.Templates), then RuneScape adoption.

## Open questions, with recommended defaults

Each question has a recommended default. None blocks the implementation.

- **Q1. One lowering profile for all backends, or the best each backend can draw?**
  - *Default:* one profile for all, chosen in the theme, because identical pixels are the point.
  - *Trade-off:* until #773, SVG draws emulated outlines it could stroke natively.
- **Q2. Team identity beyond hue.** With more than four teams, or under the colour-blind theme, hue alone is weak.
  - *Default:* allegiance (own, ally, neutral, enemy) is the primary semantic, and the team list is for games with
    real teams. The stroked profile adds a team notch or pattern index.
  - *Trade-off:* the portable profile has no pattern channel beyond shape class.
- **Q3. A closed union or an open glyph type?**
  - *Default:* a closed union for the starter set plus `Custom`, so the kit can enumerate the set and games are not
    blocked.
  - *Trade-off:* adding a case to the union breaks consumers that match on it exhaustively. The union is therefore
    opaque, with constructors and no public matching, under the additive `.fsi` rule.
- **Q4. Where `Theme` lives.**
  - *Default:* in FS.GG.Game.Render, beside the glyphs.
  - *Alternative:* `FS.GG.UI` (Rendering), so `Controls` share tokens. Deferred until a non-game consumer asks.
- **Q5. Alpha (BAR's stale veil, translucent overlays).**
  - *Default:* `Theme.validate` refuses alpha below 255 until Babylon draws alpha and the kit models blending. Stale is
    drawn as a token.
  - *Trade-off:* BAR's veil stays game-owned until then.
- **Q6. Text in glyphs (labels, counts).**
  - *Default:* no Scene text in v1. Labels stay in the accessible mirror, as every game does today.
  - *Reason:* canvas text, font custody and kit modelling are separate work. FS.GG.Rendering#1495 is about licensed
    tactical glyph **geometry** workloads, not font glyphs, so it does not supply text.
- **Q7. Loading a sprite-atlas image.**
  - *Default:* a game-supplied resolver from atlas id to URL. A missing or failed image draws the fallback and
    reports it as a diagnostic, never as a mount failure.
- **Q8. Curves in the stroked profile and in the import set.**
  - *Default:* polygons only at first. Curves are flattened at a stated tolerance of 0.25 scene units. Exact curve
    coverage in `ScenePixels` waits for a consumer that needs it.
- **Q9. Pixel snapping at small cells.**
  - *Default:* snap to whole units when the cell is at least 16, and below that do not snap, so the shapes keep their
    proportions.
  - *Trade-off:* below 16, fewer pixels are decided.
- **Q10. Light and dark themes.**
  - *Default:* ship `standard` (dark) and `light`, and let a game choose. There is no automatic `prefers-color-scheme`
    switch in the library, because the game state owns its theme.
