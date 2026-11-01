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
- #795 (GAME-RENDER-02.8) builds the encoding grammar, the palette generation, the presets and their validation on
  this ADR. Its prior-art research (issuecomment-6564559497) is folded in here as D1's slots, D4's token layers, D11
  and D12, and the licensing table.
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

### D1. The glyph model: compositional slots, on the APP-6 model

A glyph is a value that describes what it means and where it is. It never describes its colours. Glyphs are
**compositional**, in the way APP-6 / MIL-STD-2525 symbols (and milsymbol, MIT) are built. Each glyph is a fixed set
of **slots** around one anchor, and the slot geometry comes from size tokens:

| Slot | What it shows | Examples | Bertin level |
|---|---|---|---|
| **Frame** | identity: allegiance (own, ally, neutral, enemy, unknown) as **shape plus colour** | disc, square and wide frames in the portable profile; diamond, quatrefoil and dashed (planned) frames in the stroked profile | associative / selective |
| **Fill** | the frame's interior: the identity colour, or the neutral body when a team mark carries the colour | `glyph.unit.body`, `glyph.allegiance.enemy` | selective |
| **Icon** | function or class, inside the frame | an infantry, vehicle or scenery icon | associative |
| **Team mark** | team, as a band or ring in the team colour on a neutral body (the RTS mask-region convention) | ring, foot band | selective |
| **Inner modifiers** | flags in sector slots inside the frame | stale, carrying, disabled | selective |
| **Amplifiers** (outside the frame, at fixed slots) | **above:** ordinal pips (rank or echelon); **below:** a fraction bar (health, progress); **corners:** badges; **from the anchor:** facing; **right:** the label (after Q6) | | ordered (pips, length) |

**Allegiance is always double-encoded.** Its frame shape and its colour change together, so identity survives in
`monochrome` and under every colour-vision deficiency. A theme cannot map two allegiances to the same frame shape.
The portable profile has three frame shapes: disc, square and wide. When more than three identities need telling
apart (there are five identities), the stroked profile's diamond and quatrefoil are needed. Until then, the
identities beyond the third reuse a frame with a **notch modifier** built from bars: one corner notch, or two.

```fsharp
// FS.GG.Game.Render, namespace FS.GG.Game.Render.Glyphs (proposed .fsi shape; opaque types, constructors, additive)
type Identity = Own | Ally | Neutral | Enemy | Unknown
type FrameShape = Disc | Square | Wide | Diamond | Quatrefoil      // the last two are stroked-profile only
type AmplifierSlot = Above | Below | Corner of Corner | Facing | Right
type GlyphSpec =                                                   // what #795's Symbology<'Entity> builds
    { Family: GlyphFamily                                          // Unit | Structure | Terrain | Item | Marker | Path
      Anchor: Point; Footprint: Size
      Frame: FrameSlot                                              // identity -> shape + colour (both from the theme)
      Fill: FillSlot; Icon: IconRef option; TeamMark: int option
      Modifiers: ModifierRef list; Amplifiers: (AmplifierSlot * Amplifier) list }
and Amplifier = Pips of level: int * outOf: int | Bar of fraction: float * StatusRole | Badge of IconRef | Facing of radians: float
type GlyphState = { Selected: bool; Stale: bool }   // game state only; focus and hover are #787's cues (D10)
type Glyph = Spec of GlyphSpec * GlyphState | Selection of Rect | PathGlyph of Point list * PathStyle | Custom of (Theme -> GlyphDrawing)
type GlyphDrawing = { Nodes: SceneNode list; Bounds: Rect; Hit: SelectableShape }   // Bounds = union of all parts
val draw      : Theme -> Glyph -> GlyphDrawing
val scene     : Theme -> Glyph list -> Scene
val cues      : Theme -> CueContext -> SceneNode list   // = #793's withCues type; focus only (D10)
val cuesHover : Theme -> CueContext -> SceneNode list   // focus plus hover, opt-in (D10)
```

- **One function.** `draw theme glyph` is pure, total and deterministic. It returns the Scene nodes and the hit
  geometry, the union of the frame and every drawn slot, so a game's `SelectableObject` bounds and its drawing cannot
  drift apart. Today FourD reads its bounds back from the first node of each cell, and RuneScape computes its geometry
  twice.
- **The theme is an input to the Scene, never game state.** `draw` and `scene` take the `Theme` as an argument. A
  game keeps its selected theme as a model field, or as view configuration, and a switch re-renders the frame. Game
  state and selection survive the switch unchanged (D4, *Runtime switching*).
- **Slot contents are token references** (`IconRef`, `ModifierRef`, `StatusRole`), never raw colours or sizes. The
  theme resolves them (D4).
- **The encoding-grammar hook (#795).** `GlyphSpec` and its slot types are public and constructible. #795's typed
  `Symbology<'Entity>` builder (`Encode.identity`, `nominal`, `ordinal`, `fraction`, `angle`, `flag`) targets these
  slots, and maps an entity to a `GlyphSpec`. Rules that #795 enforces at build time are recorded in D12, so a
  `GlyphSpec` built by hand and one built by #795 obey the same constraints.
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
| **Units** | identity frame (disc, square or wide, D1) in its allegiance colour, an icon, a team ring on a neutral body | diamond and quatrefoil frames; a dashed frame for planned; a facing amplifier |
| **Structures** | body square with a team band along its foot; footprint of n×m cells | keyline outline |
| **Terrain** | a tile inset by the theme's gap, coloured from the terrain-kind tokens (open, blocked, difficult, water, void) | a hatch per kind, so terrain does not rely on colour alone |
| **Items** | a small oval at the item anchor | — |
| **Markers** | waypoint plus, destination ring (disc plus inner disc), target crosshair (four bars, as in SC2) | stroked rings, ping |
| **Selection** | corner brackets outside the focus ring's width plus the theme gap, so both show on one object (D10) | — |
| **Focus** (cue layer, D10) | #787's two-tone ring: a dark outer band and a light inner band of filled rectangles, each at least 2 units, just outside the object's bounds, in the theme's `FocusOuter`/`FocusInner` tokens. It differs from selection by shape as well as colour. | — |
| **Hover** (cue layer, opt-in, D10) | a one-unit outline in the hover token | — |
| **Path** | dots at a theme spacing, and a step marker at the end | polyline |
| **Status** (the *below* amplifier) | track plus a fill whose length is the fraction, coloured good, warn or bad by theme thresholds; ordinal pips in the *above* amplifier | — |
| **Team colour** | the team mark: a ring or band on a neutral body, from the `glyph.team.N` aliases. Identity is never shown by team colour alone (D1). | — |

A stale veil (BAR) needs alpha. Babylon ignores alpha, so the portable profile draws a stale glyph with its body in the
`Stale` token. The veil waits for alpha support; see Q5.

### D4. Theme tokens: seed, map and alias, with composable algorithms

A theme is three layers of tokens, following Ant Design v5's layering (MIT; the layering and composition are taken,
its HSV ramp maths is not):

| Layer | What it holds | Who writes it |
|---|---|---|
| **Seed** (about 12 values) | intent: `ground`, `ink`, the allegiance hues, the team key hues, `contrastTarget` (3 or 7), `cell`, `strokeBase`, `gap`, `density` | a preset, a game, a DTCG file, or a player override |
| **Map** | derived values: an OKLCH ramp per hue, with a job per step (Radix-style: ground, states, borders and focus, solid fills); team and allegiance swatches solved for contrast; status ramps; sizes as `cell` × steps; stroke and gap widths | **algorithms** |
| **Alias** | the semantic names glyph code reads: `glyph.unit.body`, `glyph.allegiance.enemy`, `glyph.team.0`, `cue.focus.outer`, `cue.focus.inner`, `cue.hover`, `glyph.selection`, `status.bad`, `terrain.water`, `size.cell`, `stroke.outline`, `stroke.gap` | fixed references into the map; overridable |

- **Glyph code reads alias tokens only.** No glyph reads a seed or a map token, and none carries a raw colour.
- **The alias set is lean and closed.** It is the set this ADR's slots and cues need (the D4 palette, stroke and size
  names, renamed as aliases). It grows only through this layer's own review, because each alias is a value the kit
  must validate. Ant's alias layer is very large; this one is not.
- **Algorithms** are functions `Seed -> MapTokens -> MapTokens`, and a theme is a seed plus an ordered algorithm list:
  `[base; dark]`, `[base; highContrast]`, `[base; cvdSafe; compact]`. Each algorithm refines what the earlier ones
  produced, and any map or alias token can be overridden after the list runs.
- **The `Theme` value** is opaque: `Theme.create seed algorithms overrides` resolves and validates it, and returns
  every violation or the theme. A theme that fails validation cannot be built.
- **Validation in this ADR's scope.** The WCAG 2.x non-text contrast floor (SC 1.4.11, relative luminance) is **3:1**
  for every alias drawn directly on the ground: teams, allegiances, selection, the outer focus tone, hover, marker,
  path and body. The inner focus tone is at least 3:1 against the outer one (D10). `highContrast` raises the floor to
  7:1. Every token is opaque until alpha is supported (Q5).
- **APCA is not a gate.** WCAG 2 contrast is the testable rule. APCA's polarity-aware ideas may inform presets, but
  no `apca-w3` code or constants are ported, because that package is "All Rights Reserved" (see *Licensing*).
- **Presets** (generated by #795, named here so games can depend on the names): `classic` (the proof's `standard` theme,
  dark), `light` (FourD's look), `highContrast` (7:1, thicker strokes and gaps), `colourBlindSafe` (Okabe–Ito hue
  angles with lightness lifted for a dark ground, so their blue no longer fails at 2.86:1; the proof found this), and
  `monochrome` (every hue removed; identity must survive through frame shape alone, which is also a test).
- **Player team-colour override.** At the alias layer, a player may set `glyph.team.N`. The theme is validated again
  when the override applies, and an override that fails is refused with its reason (XAG 103: presets plus free
  choice).
- **Runtime switching.** The theme is a view input (D1). Switching presets re-renders the next frame from the same
  game state, and a switch A → B → A gives the same `ScenePixels` expectations as before (a kit check, D11).
- **DTCG 2025.10.** `Theme.toDtcg` exports the resolved tokens as DTCG JSON, with each colour written as
  `{colorSpace: "oklch", components, hex}`, plus a Resolver document with `theme` and `density` modifiers.
  `Theme.ofDtcg` imports a DTCG token set as seeds or overrides, so designers can edit a preset outside the code.
  Export followed by import is lossless.
- **Export for non-Scene paths.** `Theme.cssVariables theme` gives the same tokens as CSS custom properties, and
  `Theme.hex` gives each as hex. This covers the parts of a game the Scene does not draw: SC2's DOM map, FourD's CSS
  grid, the template's `index.html` and BAR's legacy `runtime.js`. Each of those then stops keeping a second copy of
  its palette, and the CSS values equal the Scene's colours (a kit check).
- **The stroke gap is a token.** The proof's first screenshots showed why: a white focus outline touching a
  near-white structure body merged with it.
- **No meaning by colour alone.** Identity is frame shape plus colour (D1). A fraction reads by bar length. Ordinal
  values read by pips. Selection is brackets and focus is a two-tone ring. Terrain gets a hatch in the stroked profile.

**What this ADR fixes and what #795 fills in.** This ADR fixes the three layers, the algorithm signature, the alias
names, the `Theme` value as a Scene input, the validation floor, DTCG and CSS export, and colour determinism (D11).
#795 supplies the palette-generation algorithms (OKLCH ramps, contrast solving, gamut mapping), the five presets, the
ΔE2000 and colour-vision checks, and the encoding grammar (D12).

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
- **Recolouring follows the theme: the three-role template.** A sprite frame may be a three-role template, the Caves
  of Qud convention (the idea only; Qud is proprietary):
  - black pixels take the foreground role;
  - white pixels take the detail role;
  - transparent pixels take the background role.

  The roles resolve to alias tokens (for example `glyph.allegiance.enemy` and `glyph.team.0`). One piece of licensed
  art then follows every theme, the player's team override and `monochrome`, and the double-encoding rule (D1)
  still applies through the frame.
  - **Cost.** Pixi's `tint` multiplies colours, so it cannot map three roles. A template needs either per-role layers
    (three masks, each tinted) or a small shader on Pixi and Babylon. SVG uses `feColorMatrix` or the masks.
  - **Kit.** A template's recolour is probed on an open-licence test template, with the same decided-pixel model as
    glyphs.
  - **Scheduling.** This is a later part of the sprite slice.
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

   The palette becomes the `classic` preset, and `index.html` takes `Theme.cssVariables`. The generated module stops
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
3. **Later and optional:** BAR (allegiance and kind tokens; the veil waits for Q5), FourD (the `light` preset; the CSS grid
   takes `cssVariables`) and SC2 (the DOM map takes `Theme.hex`).

Each adopting consumer's pixel tests change expected values in its adoption PR. That is a planned change in that game's
own PR, not a regression.

### D10. Focus and selection with the input pipeline (#787)

This decision is reconciled with the #787 ADR (PR #793, `docs/adr/0001-standard-input-pipeline.md`, first at `7ba30e53`
and again at `f0561a79`). That
ADR decides that **focus and hover are drawable state held by the input host**: `InteractionCues` holds the focused
object, the hovered object and whether focus is visible. The cues are not game state and do not survive a backend
switch. Every interactive backend draws the interaction's cue layer above the frame's Scene. The default layer is
`SceneInteraction.standardCues`, and a game replaces it with `SceneInteraction.withCues`.

The glyph layer follows that split. The 2026-11-01 review of #793 raised two conflicts with this ADR (the type of the
cue layer, and hover after a press) and added two requirements (the ring's pixel width, and unlit drawing). They are
settled as follows.

- **Selection** is game state (`InteractiveScene.Selected`). It is drawn in the frame's Scene as `Selection` brackets
  through `GlyphState.Selected`.
- **Focus and hover** are never in `GlyphState`. They are drawn only by the cue layer.

**One type, with no wrapping.** The cue layer has exactly the `withCues` type that #793 settled at `f0561a79`. Its
`CueContext` carries the cues, the frame's objects and the presented scale:

```fsharp
val cues      : Theme -> CueContext -> SceneNode list   // focus only
val cuesHover : Theme -> CueContext -> SceneNode list   // focus plus hover (opt-in)
// use: SceneInteraction.withCues (Glyphs.cues theme) interaction
```

A `SceneNode list` composes into the surface's frame without wrapping. The earlier `Scene`-returning draft is
withdrawn, and `standardCues` has the same type. `Glyphs.draw` keeps `GlyphDrawing.Nodes` for the same reason.

**Hover is opt-in.** The default `Glyphs.cues` draws focus and nothing for hover, as `standardCues` does. After a
pointer press the pressed object is hovered, and focus is not visible, so the default draws the same frame as before
the press. #793's I8 ("after a pointer press the frames do not differ") then holds unchanged for any game using it.
`Glyphs.cuesHover` adds the hover outline. A game that opts in changes its frame on hover by design, so it is tested
against #793's restated I8 with a hover-only frame (the same frame with `hovered` set) as the reference.

**Deterministic drawing.** The layer is a pure function of `(theme, CueContext)`. The focused and hovered ids are
looked up in `objects`, and an id that is not there draws nothing. Every combination of focus, hover and selection is
defined:

| Focus visible on X | Hovered | Selected (frame) | The cue layer draws | The frame draws |
|---|---|---|---|---|
| no | none | — | nothing | brackets if selected |
| no | X | — | `cues`: nothing; `cuesHover`: hover outline on X | brackets if selected |
| yes | none or X | — | the focus ring on X (it covers hover on the same object; no hover outline is drawn on X) | brackets if selected |
| yes | Y ≠ X | — | the focus ring on X; `cuesHover` also the hover outline on Y | brackets if selected |
| focused id but not visible (after a press) | any | — | as "no" above | brackets if selected |

- **Layer order.** Frame (with selection brackets), then the hover outline, then the focus ring.
- **Placement.** The focus ring is just outside the object's bounds, with #793's geometry: a dark outer band and a
  light inner band. The hover outline sits inside the band the focus ring would occupy, so the two never draw on the
  same object at once. Selection brackets are placed **outside** the focus ring's full width plus the theme gap, so an
  object that is both selected and focused shows both cues and never overdraws one with the other. This changes the
  proof, which predates #793: there, the brackets sit one gap outside the bounds.

**At least 2 CSS px after the surface transform.** Each focus band is
`max(theme band, 2 / CueContext.cssPerUnit context)` scene units wide, rounded up to whole device pixels. Neither band
falls below 2 CSS px when a board is styled smaller than its scene (SC2's 640×360 map shown at 320×180), and the drawn
width stays whole on the pixel grid. The input host supplies `cssPerUnit` in `CueContext` (#793 at `f0561a79`), which
is the mechanism proposed on #793, so no theme-declared minimum scale is needed. #793's restated I8 asserts the 2 CSS
px width for `standardCues` and for any `withCues` layer.

**Unlit on Babylon.** The cue layer is Scene data drawn through the same fill path as the frame. Once #786 lands,
Babylon draws it unlit at its declared colours, so the measured contrast of the two tones is the declared contrast.
Until #786, Babylon draws it at about 0.75 of each colour. Scaling both colours by 0.75 lowers their contrast ratio (for
example, white on (34,40,49) drops from 14.8:1 to 9.1:1), so the kit's glyph and cue checks assert the declared
colours only after #786.

**Contrast.** The theme validates both focus tones: the outer tone against the ground and the inner tone against the
outer one, each at least 3:1. This is the two-tone rule #787 takes from WCAG 2.2 focus appearance.

**The proof** predates #793. It draws focus as a single-tone outline in the frame's Scene, and its brackets sit one gap
outside the bounds. The implementation draws focus in the cue layer only, with the placement above.

### D11. Colour derivation is deterministic: OKLCH in F#, quantised with Floor

Palettes are derived in **OKLCH** (OKLab's polar form, as defined in CSS Color 4). It is far more hue-uniform than
CIELAB, where blue drifts towards purple. The derivation must give the same bytes on .NET and through Fable:

- **One implementation, in F#.** Derivation runs from the same F# source on both targets. No JavaScript colour
  library sits on the drawing path.
- **Floor, not Round, when quantising to 8-bit sRGB.** This is the same argument as D2's geometry snapping. `Math.Cbrt`
  and `Pow` can differ by an ULP between .NET and JavaScript, and an ULP can flip a half-way rounding. Floor of a value
  already clamped to [0, 255] makes such a flip far less likely, and the kit check below catches any that remain.
- **Gamut mapping** follows CSS Color 4: chroma is reduced at constant L and h until the colour is in sRGB, within
  the ΔEOK just-noticeable difference.
- **Presets are baked at build time.** Each preset's resolved hex values are generated, committed as data, and
  compared with a fresh derivation in CI. Derivation at run time is used only for custom seeds and player overrides.
- **Kit checks** (in the kit's glyph section, D6), for every preset:
  1. .NET and Fable derive identical hex for every alias;
  2. a theme switch A → B → A gives identical `ScenePixels` expectations;
  3. the theme-swap control does not match, as in the proof;
  4. DTCG export and re-import is lossless, and the CSS variables equal the Scene's colours.

### D12. The encoding-grammar hook, and the scope split with #795

#795 builds a typed `Symbology<'Entity>`: field accessors bound to D1's slots through a measure kind. The theme supplies
the ranges, and the result is a `GlyphSpec`.

```fsharp
// #795's shape, for reference only: this ADR fixes the target types, not the builder
Symbology.create<Unit> GlyphFamily.Unit
|> Encode.identity (fun u -> u.Allegiance) Slot.Frame          // shape + colour, double-encoded
|> Encode.nominal  (fun u -> u.Class)      Slot.Icon
|> Encode.team     (fun u -> u.Team)       Slot.TeamMark
|> Encode.fraction (fun u -> u.Hp)         (Slot.Amplifier Below)   // bar length
|> Encode.ordinal  (fun u -> u.Rank)       (Slot.Amplifier Above)   // pips
|> Encode.angle    (fun u -> u.Facing)     (Slot.Amplifier Facing)  // stroked profile
|> Encode.flag     (fun u -> u.Stale)      Slot.Modifier
```

**The rules #795 enforces at build time** follow Mackinlay's APT ranking and Munzner's magnitude and identity
channels. They are recorded here so that a hand-built `GlyphSpec` obeys them too, and `Theme.validate` and the kit
check the ones a theme can break:

1. **No hue-only ordered data.** An ordinal or fraction field never maps to hue alone. It maps to length (a bar),
   pips, size steps, or lightness steps.
2. **Categorical hue needs a redundant channel.** A nominal field encoded by hue must also be encoded by a non-hue
   channel: frame shape, icon or a modifier. The `monochrome` render must still tell its values apart in decided
   pixels.
3. **Channel capacity.** A field's cardinality must not exceed the capacity of its channel at the glyph's size.
   - Hue: at most 8 distinct values.
   - Portable frame shapes: 3, or 5 with the notch modifiers.
   - Pips: at most 5.

   Above capacity, the build fails and names the field.
4. **Fractions are length, not area.** A fraction encoded by area is refused.
5. **Flags are modifiers or badges**, the channels Bertin calls selective.
6. **Allegiance and team are always double-encoded** (D1).

**Scope split.**

| #789 (this ADR, its implementation and the proof) | #795 (builds on this ADR) |
|---|---|
| the slot model (`GlyphSpec`, frames, slots and amplifiers) and `draw` | `Symbology<'Entity>` and the `Encode` combinators |
| the token layers, the algorithm signature, the alias names and `Theme` as a Scene input | palette algorithms: OKLCH ramps, Leonardo-style contrast solving, gamut mapping, team hue spreading |
| the 3:1 floor, opaque-only, DTCG and CSS export, deterministic derivation (D11) | the five presets as data, and the player-override flow |
| the cue layer (D10) and the kit's glyph section (D6) | the kit's ΔE2000, CVD, redundancy and capacity checks |
| sprite atlases and the recolour template (D5) | — |

**Distinctness and colour-vision checks (#795).** Teams, allegiances and status levels must differ pairwise by at
least a ΔE2000 threshold at glyph size. The required difference grows as marks shrink (Stone, Szafir and Setlur), so
the threshold depends on size. The check runs twice:
- with normal vision, starting at ΔE ≥ 12;
- under Machado simulation of protanopia, deuteranopia and tritanopia at severity 1.0, simulated in linear RGB,
  starting at ΔE ≥ 8, together with a minimum OKLab lightness separation for each team pair.

**Both thresholds are starting points to be calibrated** (Q11). Simulation is validation, not a substitute for testing
with players.

## The proof (experimental)

The proof does not constrain the API.

- **Glyphs.** `src/ReferenceGame/ExperimentalGlyphs.fs`, namespace `FS.GG.Game.Reference.Experimental`, in a project
  that is not packed. It has six glyphs (unit, structure, waypoint, selection brackets, focus outline and status bar)
  and two themes (`standard` and `colourBlindSafe`), in the portable profile only.
  The proof predates D1's slot model, D4's token layers and D11. Its themes are flat records of colours, and its
  `standard` theme is the seed of the `classic` preset.
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

## Licensing of prior art

This is recorded so that no implementation slice ports something it may not. Code that is ported keeps its NOTICE and
licence header. Ports are written in F#, so .NET and Fable stay identical (D11).

| Source | Licence | Use |
|---|---|---|
| material-color-utilities (HCT, contrast, DynamicScheme), Adobe Leonardo, Style Dictionary | Apache-2.0 | portable, with notice |
| ColorBrewer schemes | Apache-2.0 | portable, with notice |
| @ant-design/colors, Radix Colors, culori, milsymbol, Tailwind palette | MIT | portable, with notice |
| d3-scale-chromatic (including the Tableau 10 values) | ISC | portable, with notice |
| Okabe–Ito values, Machado matrices | published facts | usable, cited |
| openstreetmap-carto, Kenney assets | CC0 | usable |
| game-icons.net | CC BY 3.0 | usable; **attribution ships with the icons** |
| DCSS tiles | reported CC0 | verify before use |
| APP-6 / MIL-STD-2525 | public standards | conventions only, cited |
| Brogue CE | AGPL-3.0 | **ideas only** |
| DCSS game code | GPL | **ideas only** |
| Cogmind, Caves of Qud | proprietary | **ideas only** |
| Mapbox GL JS ≥ v2 | proprietary | **ideas only** |
| **apca-w3** | **All Rights Reserved** (WCAG web use only) | **never port**: no code and no constants. WCAG 2 contrast is the gate, and APCA contributes ideas only. |

## Implementation plan (after acceptance)

1. FS.GG.Rendering: Scene constructors in the Fable view (D7). This is owed intake.
2. FS.GG.Game: #786 (unlit Babylon) and #773 (canvas strokes) as they are. They are prerequisites of D6 and of the
   stroked profile.
3. FS.GG.Game: `FS.GG.Game.Render.Glyphs`, in the portable profile, with tests, the kit's glyph section and bounded
   unmodelled nodes. It joins the Fable view and covers:
   - the D1 slot model and `draw`;
   - the cue layer (D10);
   - the D4 token layers: `Theme.create`, the alias set, validation, CSS export and DTCG export and import;
   - the D11 determinism checks, with the `classic` seed as the first baked preset.
4. FS.GG.Game#795, on top of 3: the encoding grammar, the palette algorithms, the other presets, and the ΔE2000,
   colour-vision, redundancy and capacity checks.
5. FS.GG.Game: the `ScenePixels` ring, band and polygon models and the stroked profile, with the diamond, quatrefoil
   and dashed frames.
6. FS.GG.Game: sprite atlases with fallbacks and custody (D5), with image support on the canvases, then the three-role
   recolour template.
7. Release through the canonical route. Then template adoption (FS.GG.Templates), then RuneScape adoption.

## Open questions, with recommended defaults

Each question has a recommended default. None blocks the implementation.

- **Q1. One lowering profile for all backends, or the best each backend can draw?**
  - *Default:* one profile for all, chosen in the theme, because identical pixels are the point.
  - *Trade-off:* until #773, SVG draws emulated outlines it could stroke natively.
- **Q2. More than three identities in the portable profile.** D1 double-encodes all five identities, but the
  portable profile has only three frame shapes (disc, square, wide).
  - *Default:* the fourth and fifth identities reuse a frame with one or two corner-notch modifiers, until the stroked
    profile adds the diamond and quatrefoil frames.
  - *Trade-off:* a notch is a weaker shape cue than a distinct frame at small sizes.
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
- **Q10. The default preset, and automatic light or dark.**
  - *Default:* `classic` (dark). A game chooses its preset, and the player may switch. There is no automatic
    `prefers-color-scheme` switch in the library: the theme is a view input that the game owns (D1).
- **Q11. Calibrating the ΔE2000 thresholds** (#795's checks). The proposed starting points are 12 with normal vision
  and 8 under simulation.
  - *Default:* ship them as provisional constants, measured at the starter set's sizes, and tighten or loosen them
    from a calibration against Stone, Szafir and Setlur's size model and one player test.
  - *Trade-off:* until then, a pair that passes may still be hard to tell apart at the smallest sizes.
- **Q12. Presets baked at build time, or derived at run time.**
  - *Default:* baked (D11). Run-time derivation only for custom seeds and overrides, which the .NET and Fable hex
    check covers.
  - *Trade-off:* a change to an algorithm means regenerating the committed preset data in the same PR.
- **Q13. The icon source for the icon slot.**
  - *Default:* the starter set's icons are vector icons drawn from Scene primitives in this repository. Kenney (CC0)
    is the first external source. game-icons.net (CC BY 3.0) is allowed only with attribution shipped beside the
    icons.
  - *Trade-off:* hand-drawn primitive icons are few and simple until the stroked profile.
- **Q14. How a sprite template is recoloured.**
  - *Default:* per-role mask layers, each tinted, because they need no shader and behave identically on Pixi,
    Babylon and SVG.
  - *Trade-off:* this draws three layers per sprite. A shader is the measured alternative if the #761 bench shows
    the cost matters.
