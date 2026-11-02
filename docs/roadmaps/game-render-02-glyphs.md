# GAME-RENDER-02.4 — Shared glyph and symbology layer with theme tokens (ADR)

Status: **accepted** (PR #794, merged at `a4c75efa`). The implementation of FS.GG.Game#789 follows in separate PRs: the
kit's identity samples and bounded unmodelled nodes, the theme tokens, then the glyph library and the kit's glyph section.
The implementation's errata are listed under *Errata* below and marked where they apply.
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

## Errata (from the implementation, FS.GG.Game#789)

These corrections were ruled by Root during the implementation review. Each one is marked in place.

- **E1. The team ring's layout (D1, D3, D12).** From the edge inwards, a team-marked glyph is drawn as:
  - the frame, in its identity colour;
  - a **keyline in the body colour**;
  - the **ring** in the team colour;
  - the body fill.

  The ring therefore touches the body on both sides and never touches the identity colour, so validating each team
  colour against the body alone is correct. Without the keyline, the first baked `classic`, whose team colours equalled
  allegiance colours, measured 1.00:1 for team 0 against the own frame. `classic`'s team colours are now distinct from
  every allegiance colour, and are a provisional stand-in: #795's derivation is the source of truth for every preset,
  `classic` included. The keyline makes the ring-to-frame contrast moot whatever the preset. The kit checks that
  team 0 on the own allegiance shows a visible ring.
- **E2. The outer focus tone (D3, D10).** The focus ring's outer tone is **whichever tone contrasts with the ground**
  (3:1), and the inner tone contrasts with the outer one. On a dark ground the outer tone is light and the inner tone
  dark. "A dark outer band and a light inner band" holds only on a light ground.
- **E3. No `Amplifier.teamIndex` (D1).** The team badge is drawn inside the frame (review M1), never as an amplifier.
  Team is set with `GlyphSpec.team`, and the badge is required by `GlyphSpec.badged`.
- **E4. Player overrides (D4).** A player changes only a team colour (`Theme.withTeamColour`). The floors
  (`contrastTarget`, `minCssPerUnit` and `hueCapacity.*`) belong to the seed, and an override of one is refused, so no
  override can lower a floor.
- **E5. The DTCG Resolver version (D4).** The Resolver Module contradicts itself on the date: its property table says
  `2025-10-01`, and section 4.1.2 says `2025-11-01`. The resolver document uses **`2025-10-01`**, matching #805's
  Q-T9. A single theme is a set, because a modifier should have two or more contexts. Sizes are derived again from the
  seed on import, so the `compact` density context changes them.

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

| Slot | What it shows | Examples |
|---|---|---|
| **Frame** | identity, as **shape plus colour**, with the shape fixed by the library (below) | friend rectangle, neutral square |
| **Fill** | the frame's interior: the identity colour, or the neutral body when a team mark carries the colour; an optional **lightness step** for an ordinal field | `glyph.unit.body`, `glyph.allegiance.enemy` |
| **Icon** | function or class, inside the frame | infantry, vehicle, scenery |
| **Size step** | an ordinal field, as one of a few frame sizes within the footprint | small, medium, large |
| **Team mark** | team, as a band or ring in the team colour on a neutral body (the RTS mask-region convention), separated from the frame by a body-colour keyline (E1) | ring, foot band |
| **Inner modifiers** | flags, in sector slots inside the frame | stale, carrying, disabled |
| **Team badge** | the team's index, at the frame's inner centre, in the body pass (from 22 units) | a seven-segment digit |
| **Amplifiers** (outside the frame, beyond the cue reserve) | **above:** ordinal pips (rank or echelon); **below:** a fraction bar (health, progress); **corners:** badges (never the team badge, which is drawn inside the frame); **from the anchor:** facing; **right:** the label (after Q6) | |

**The identity-to-frame mapping is fixed in the library, per APP-6.** Themes choose colours, never shapes, so a frame
shape carries identity everywhere and nothing else. Unit and structure **families** are told apart by icon, size and
footprint, never by frame shape.

Every identity frame is built from **opaque bars** (filled rectangles) in both profiles. Every backend draws bars, and
`ScenePixels` decides their edges, so the kit can tell the frames apart (review R3). Discs and ovals are never identity
frames; they are reserved for items and markers (D3).

**Frames are always on whole units.** Whenever an identity frame is drawn, its centre is snapped with
`Floor(v + 0.5)` (half up, as D11 does for colours, never `Math.Round`), and its size is floored. This holds at every
cell size, not only at 16 and above (D2), because the matrix below depends on it. The review measured off-grid centres
giving 0-pixel pairs at 12 and 16 units. With `w` the frame size and `band = max 1 (floor (w / 8))`:

| Identity | APP-6 frame | Frame (both profiles) |
|---|---|---|
| `Own` | friend: rectangle | rectangle `w × 2w/3` |
| `Ally` | friend: rectangle | the friend rectangle with **true holes** (`max 2 (floor (w / 6))` square) at the **quarter points** of its top and bottom edges, built from seven bars, so nothing is painted and the terrain shows through. A hole is never at an edge midpoint, where the box-edge probes sit (review N1). Its colour also differs from `Own`. |
| `Neutral` | square | square `w × w` |
| `Enemy` | hostile: diamond | **stepped diamond**: pairs of rows one *step* high (`step = max 1 (floor (w / 12))`, finer than the band so it reads as a diamond, not a blob), the middle pair two steps narrower than the square, each further pair two steps narrower again |
| `Unknown` | quatrefoil | **stepped quatrefoil**: a cross of two bars `w/2` thick, reaching one band past the square on every side |

**Distinguishability, measured.** Each frame was drawn alone, monochrome (one ink on the ground), so only shape counts.
Each cell below is the smaller of two counts:
- how many pixels the kit decides for A that B's drawing shows in another colour;
- the same with A and B swapped.

A cell of 0 would mean the kit cannot tell the pair apart in one direction: a backend drawing one frame for the other
would pass. The script is `game-render-02-glyphs/frames.fsx` (run after building `tests/Game.Render.Tests`), and the
proof's .NET tests reproduce it from 12 to 32 units.

With today's `ScenePixels` samples (middles of edges only):

| size | Own/Ally | Own/Neutral | Own/Enemy | Own/Unknown | Ally/Neutral | Ally/Enemy | Ally/Unknown | Neutral/Enemy | Neutral/Unknown | Enemy/Unknown |
|---|---|---|---|---|---|---|---|---|---|---|
| 6 | 4 | 2 | 2 | 4 | 4 | 9 | 11 | 4 | 4 | 6 |
| 8 | 0 | 2 | 3 | 4 | 2 | 12 | 5 | 4 | 4 | 4 |
| 12 | 0 | 2 | 4 | 4 | 2 | 16 | 4 | 4 | 4 | 4 |
| 16 | 0 | 2 | 4 | 4 | 2 | 26 | 4 | 4 | 4 | 4 |
| 24 | 0 | 2 | 4 | 4 | 2 | 20 | 4 | 4 | 4 | 4 |
| 32 | 0 | 2 | 4 | 4 | 2 | 31 | 4 | 4 | 4 | 4 |

With D6's corner and quarter-edge samples for identity frames (the proof prototypes them as `Glyphs.identitySamples`):

| size | Own/Ally | Own/Neutral | Own/Enemy | Own/Unknown | Ally/Neutral | Ally/Enemy | Ally/Unknown | Neutral/Enemy | Neutral/Unknown | Enemy/Unknown |
|---|---|---|---|---|---|---|---|---|---|---|
| 6 | 11 | 6 | 12 | 13 | 14 | 12 | 25 | 16 | 16 | 15 |
| 8 | 4 | 6 | 14 | 13 | 10 | 18 | 25 | 16 | 16 | 24 |
| 12 | 4 | 6 | 16 | 16 | 10 | 24 | 28 | 16 | 16 | 28 |
| 16 | 4 | 6 | 16 | 16 | 10 | 40 | 28 | 16 | 16 | 28 |
| 24 | 4 | 6 | 16 | 16 | 10 | 54 | 28 | 16 | 16 | 28 |
| 32 | 4 | 6 | 16 | 16 | 10 | 93 | 28 | 16 | 16 | 28 |

- **Own/Ally needs D6's samples.** Ally's holes sit at quarter points, deliberately away from the midpoints, so
  today's middle-of-edge samples see no difference from Own (0 from 8 units). This is the trade the box-edge rule
  forces. With D6's samples, the pair differs by 4 in both directions at every size from 8.
- **Every other pair** differs by at least 2 with today's samples and at least 6 with D6's.
- **The earlier forms, for comparison:**
  - Own (rectangle) against Unknown (ellipse): 0 at every size;
  - Neutral (square) against Enemy (disc): 0 at every size;
  - they differed only at corners, which the kit did not sample.
- **The kit's identity check uses D6's samples.** The browser smoke repeats it on every backend at 24 units, for 20
  ordered pairs (*The proof*).
- **Box edges.** Every frame passes the box-edge check on its drawn bounding box, at 12, 16, 24 and 32 units: the
  first whole pixel inside each edge midpoint is the frame, and the first outside is not. A .NET test checks this.

**`GlyphDrawing.frame` is the drawn bounding box**, not the nominal size. At a nominal 24 units:
- Own and Ally are 24 × 16;
- Neutral is 24 × 24;
- Enemy is **20 × 20**;
- Unknown is **30 × 30**.

Hit bounds, the box-edge check and the cue reserve all use this box.

**Minimum identity frame size: 12 units.** The kit tells the frames apart from 6 units, but below 12 the stepped forms
have one-unit bands and are not reliably legible. Below 12 units (or 12 CSS px at the presented scale), identity
relies on colour plus the accessible mirror label, and D12's redundancy rule is met by the label alone.

- **Identity is always double-encoded.** Its shape and its colour change together, so identity survives in
  `monochrome` and under every colour-vision deficiency. `Own` and `Ally` share APP-6's friend frame, and are
  separated by colour plus the notches.
- **Team is double-encoded too (review R2).**
  - **Team colour** is the team mark.
  - **The index badge is mandatory whenever two or more of the game's teams share an allegiance.** Presence depends on
    the game's teams, not on which teams are visible, so a badge never appears or disappears as units come into view.
  - **The badge is drawn inside its own frame (review M1).** It sits at the frame's **inner centre**, in the body pass,
    right after its own frame, so it moves with the frame and no neighbour can cover it.
    - As an amplifier it was drawn first, and neighbouring frames covered it. The review measured 0 of 6 interior
      badges intact on the dense board, with 22–92 % of each surviving.
    - The top-right inner corner was considered. It does not fit Ally, whose quarter-point holes take the corners'
      columns, or the stepped forms, whose corners are empty. The centre fits every frame.
    - **Minimum frame for an inside badge: 22 units.** The 6 × 10 badge plus a 1-unit margin all round must lie wholly
      on frame ink. Measured, the smallest size from which that always holds is: Neutral 12, Unknown 16, Own 18,
      Enemy 20, Ally 22.
    - **Below 22 units** the badge is omitted, and `draw` reports a diagnostic naming the team and the size. Team then
      relies on the allegiance frame plus the mirror label.
      *Trade-off:* on a board of small frames, two teams that share an allegiance look the same, and only the label
      tells them apart. A game that needs at-a-glance team identity there must use frames of at least 22 units, or
      keep its teams on distinct allegiances.
    - **The badge and the icon share the inner centre.** When a badge is drawn, it takes the centre and the icon is not
      drawn; the icon's class or function then rests on size, footprint and the mirror label. Side by side needs a
      larger frame (Q15).
    - **Contrast is checked against the badge's real neighbour,** the frame fill. The `glyph.badge.ink` token is at
      least 3:1 against the body and every team and allegiance colour it can sit on, in every preset. The proof's
      dark badge ink passes on the body and on every team colour of both themes.
    - **Identity sampling is unaffected.** With badges drawn inside the frames, all 81 digit combinations of every
      frame pair keep the bare-frame counts of the matrices above, at 24 and 32 units, with both samplers. The badge
      sits only in the centre, away from the edges, corners and quarter points that the identity samples read.
      `frames.fsx` prints this, and a .NET test checks digits 1 and 8.
  - **Badge geometry.** The badge is a **seven-segment numeral** (the team's index, from 1), drawn from bars.
    - It sits on a 3 × 5 grid of whole-unit cells, with the unit at least 2, so the digit is at least 10 units tall:
      the 7-unit floor, rounded up to whole cells.
    - One digit covers 9 teams, above the largest hue capacity (8); two digits cover the rest.
    - The kit check covers **all 36 pairs of digits** at the minimum size, with D6's samples. Today's samples cannot
      tell 11 pairs apart in at least one direction, for example 1 against 7 and 7 against 9. D6's samples separate all 36 (smallest difference
      1 pixel each way).
    - The dense-board kit case draws every frame with its badge inside it, and requires every badge's decided pixels
      to hold. The identity-pair check runs both without and with badges.
  - When each of the game's teams has its own allegiance, the identity frame is the second channel, and the badge is
    optional.
  - The **accessible mirror label** names the team in every case.
- **Above the hue capacity** (D12's per-size value), or whenever the player asks, team colour falls back to
  **allegiance colouring**: team marks take the viewer-relative allegiance colours, as SC2's toggle does. Team is then
  shown by the badge plus the allegiance frame.
  - *Trade-off:* colour no longer tells teams apart at a glance, so players read badges. A badge is legible only at
    or above the minimum identity frame size. Below it, team relies on the label.

```fsharp
// FS.GG.Game.Render, namespace FS.GG.Game.Render.Glyphs (proposed .fsi shape; every type opaque, built by functions)
type Identity = Own | Ally | Neutral | Enemy | Unknown            // the frame shape follows from this, in the library
[<Sealed>] type GlyphFamily      // Unit, Structure, Terrain, Item, Marker, Path: values in module GlyphFamily
[<Sealed>] type IconRef          // IconRef.named "infantry", resolved by the theme's icon set
[<Sealed>] type ModifierRef      // ModifierRef.stale, ModifierRef.named ...
[<Sealed>] type AmplifierSlot    // AmplifierSlot.above, below, corner c, facing, right
[<Sealed>] type Amplifier        // Amplifier.pips (level, outOf), bar (fraction, role), badge icon, facing radians (no teamIndex: E3)
[<Sealed>] type GlyphSpec                                         // what #795's Symbology<'Entity> builds
module GlyphSpec =
    val create     : GlyphFamily -> anchor: Point -> footprint: Size -> GlyphSpec
    val identity   : Identity -> GlyphSpec -> GlyphSpec
    val icon       : IconRef -> GlyphSpec -> GlyphSpec
    val team       : int -> GlyphSpec -> GlyphSpec
    val sizeStep   : int -> GlyphSpec -> GlyphSpec               // ordinal, 0..n-1
    val fillStep   : int -> GlyphSpec -> GlyphSpec               // ordinal lightness step
    val modifier   : ModifierRef -> GlyphSpec -> GlyphSpec
    val amplifier  : AmplifierSlot -> Amplifier -> GlyphSpec -> GlyphSpec
type GlyphState = { Selected: bool; Stale: bool }                // game state only; focus and hover are cues (D10)
[<Sealed>] type Glyph                                            // Spec, Selection, Path or Custom, by constructors
[<Sealed>] type GlyphDrawing
module GlyphDrawing =
    val nodes  : GlyphDrawing -> SceneNode list
    val frame  : GlyphDrawing -> Rect                             // = hit bounds: the frame only, independent of state
    val hit    : GlyphDrawing -> SelectableShape
    val extent : GlyphDrawing -> Rect                             // layout extent: frame, cue reserve and amplifiers
val draw      : Theme -> Glyph -> GlyphDrawing
val scene     : Theme -> Glyph list -> Scene
val cues      : (unit -> Theme) -> CueContext -> SceneNode list   // focus ring only (D10)
val Theme.hueCapacity : Theme -> sizeStep: int -> int            // D12: usable hues at a size step (module Theme)
val cuesHover : (unit -> Theme) -> CueContext -> SceneNode list   // plus hover, opt-in (D10)
```

- **Hit bounds are the frame only.** `GlyphDrawing.frame` and `hit` do not depend on state, amplifiers or brackets,
  so a game's `SelectableObject` is the same whether or not the object is selected. A separate **layout extent**
  covers the whole glyph: frame, cue reserve and amplifiers. Extents may overlap neighbours, and `Glyphs.scene` draws
  in passes (D10). With no cue showing, nothing is drawn within the theme gap outside the frame, so the kit's box-edge
  check passes by construction: the pixel just inside each frame edge is the frame, and the pixel just outside is the
  ground.
- **One function.** `draw theme glyph` is pure, total and deterministic, so a game's selectable objects and its
  drawing cannot drift apart. Today FourD reads its bounds back from the first node of each cell, and RuneScape
  computes its geometry twice.
- **The theme is an input to the Scene, never game state.** `draw` and `scene` take the `Theme` as an argument. A
  game keeps its selected theme as a model field, or as view configuration, and a switch re-renders the frame. Game
  state and selection survive the switch unchanged (D4, *Runtime switching*). The cue layer reads the same theme
  (D10).
- **Slot contents are token references** (`IconRef`, `ModifierRef`, status roles), never raw colours or sizes. The
  theme resolves them (D4).
- **The encoding-grammar hook (#795).** `GlyphSpec` is opaque and built only through `GlyphSpec` functions, so cases
  and fields can be added later without breaking consumers. #795's typed `Symbology<'Entity>` builder
  (`Encode.identity`, `nominal`, `ordinal`, `fraction`, `angle`, `flag`) calls those functions, and maps an entity to
  a `GlyphSpec`. The builders enforce D12's rules, so a hand-built spec and a #795-built one obey the same constraints.
- **Composition.** A glyph is made of Scene primitives only. It adds no node kind and no new renderer surface.
- **Portability.** The library uses only `SceneNode` data, so it compiles for .NET and through Fable from the same
  files, and it joins the bounded Fable view of FS.GG.Game.Render.
- **`Custom`** is the escape hatch for a game-specific glyph. It still takes its colours from the theme and still
  returns its frame, hit shape and extent.

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
  pixel boundaries **only when the device pixels per scene unit are a whole number**: 1 or 2 at DPR 1 or 2 with the
  board at its scene size. At other scales (SC2's 640×360 board styled to 960×540, or DPR 1.5), edges fall between
  pixels, and those pixels are left undecided by the kit's Partial rule. Snapping then keeps geometry deterministic but
  does not align it. The proof uses `Floor`
  for **geometry** whose inputs are exact multiples, never `Math.Round`, whose half-way ties might break differently on
  .NET and Fable. Colour quantisation is a different case, with its own rule (D11).

### D3. The starter glyph set

| Family | Portable profile | Stroked additions |
|---|---|---|
| **Units** | the library's identity frame (D1, bars only) in its identity colour, an icon, a team ring on a neutral body, and the index badge when teams share an allegiance; size steps for an ordinal | a dashed frame for planned; a facing amplifier |
| **Structures** | the same identity frame as units, told apart by a footprint of n×m cells and a structure icon, with a team band along its foot | keyline outline |
| **Terrain** | a tile inset by the theme's gap, coloured from the terrain-kind tokens (open, blocked, difficult, water, void) | a hatch per kind, so terrain does not rely on colour alone |
| **Items** | a small oval at the item anchor, at most half the identity frame size. Ovals are never identity frames. | — |
| **Markers** | waypoint plus, destination ring (disc plus inner disc), target crosshair (four bars, as in SC2). Markers are thin (arms one band wide) and at most half the identity frame size, so the waypoint plus is not the filled `Unknown` cross, and discs and rings are not identity frames. | stroked rings, ping |
| **Selection** (frame, game state) | corner brackets in the cue reserve, outside the largest focus ring the theme allows, so both show on one object (D10) | — |
| **Focus** (cue layer, D10) | #787's two-tone ring: an outer band in the tone that contrasts with the ground and an inner band contrasting with it (E2; dark outer and light inner on a light ground), of filled rectangles, each at least 2 CSS px, just outside the frame, in the theme's `FocusOuter`/`FocusInner` tokens. It differs from selection by shape as well as colour. | — |
| **Hover** (cue layer, opt-in, D10) | an outline at least 1 CSS px wide in the hover token | — |
| **Path** | dots at a theme spacing, and a step marker at the end | polyline |
| **Status** (the *below* amplifier) | a keyline in the body colour, a dark track inside it, and a fill whose length is the fraction, coloured good, warn or bad by theme thresholds; ordinal pips in the *above* amplifier | — |
| **Team colour** | the team mark: a ring or band on a neutral body, from the `glyph.team.N` aliases, with the index badge when teams share an allegiance (D1). | — |

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
  path and body. Contrast is also checked between the other colours that touch (the icon on the fill, the team ring
  between fill and body, the status fill on its track, the inner focus tone on the outer one; D12). `highContrast`
  raises the floor to **7:1**, a provisional value borrowed from WCAG's AAA criterion for text, not a non-text
  requirement. Every token is opaque until alpha is supported (Q5).
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
- **Themes choose colours, never shapes.** The identity-to-frame mapping is the library's (D1).
- **No meaning by colour alone.** Identity is frame shape plus colour (D1); team is colour plus frame, label and badge. A fraction reads by bar length. Ordinal
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

  - **Templates are exact.** Each pixel has alpha exactly 0 or 255 (binary transparency), and every opaque pixel is
    exactly black or exactly white. A template with any other value is refused when its atlas loads, with the first
    offending pixel named.
  - **Anti-aliasing is not in the template.** It comes from rendering at the presented scale. Art authored with
    anti-aliased edges is quantised once, at authoring time, by a tool. The tool makes alpha below 0.5 transparent,
    makes luminance below 0.5 the foreground role, and makes everything else the detail role. The tool's output, not
    the original art, is what the atlas holds.

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
     **every** pixel. A pixel stays decided only if a later opaque node wholly covers it, so the loss depends on draw
     order. On the proof board at `8cbd7bed` (391 decided pixels), the #794 review measured how many stay decided with one text node
     added:
     - drawn first: 391;
     - drawn in the middle: 234;
     - drawn last, the usual place for a label: 0. `expectations` is then empty, and the kit's subject check ("every
       state declares at least one pixel") refuses the scene.

     With this change, a node whose bounds are known (`Path.bounds`, image bounds, arc bounds) is `Partial` only inside
     them. Text and nodes without bounds stay unbounded. The proof's test pins the drawn-last case.
   - **Samples** for each new model: its middle, the first whole pixel inside and outside each edge, and, for rings
     and bands, one pixel across the stroke.
   - **Corner and edge samples for identity frames (review R3).** For each bar of an identity frame, the kit adds:
     - the pixels just inside and just outside each corner;
     - the pixels just inside and just outside each edge, at a quarter and at three quarters of its length.

     Today's samples sit only at the middles of edges, so frames that differ only at corners decided no differing
     pixel. D1's matrix gives the counts with and without these samples.

   The kit then gains a **glyph section**, `withGlyphs theme glyphs`. On every backend it:
   - draws the starter set in each shipped theme and checks every decided pixel, with a theme-swap control that must
     not match;
   - draws each identity frame alone and checks that every other frame's decided pixels differ from it (20 ordered
     pairs, with the identity-frame samples), as the proof's smoke does;
   - draws each index badge alone against the other eight digits (all 36 pairs, both directions), and the identity
     pairs again with badges inside the frames;
   - draws D10's dense-board case;
   - runs D12's team-badge check.

### D7. The Fable `Scene` constructor gap: fixed in FS.GG.Rendering

- **The gap.** The curated Fable project of `FS.GG.UI.Scene`, `src/Scene/Fable/FS.GG.UI.Scene.fsproj` at Rendering
  `2d35dc9b`, compiles `Types`, `RetainedSvg`, `SpatialWorkingSet`, `Svg*` and `Animation`. It does not compile
  `Scene.fs`, which holds the modules `Colors`, `Paint`, `Path` and `Scene`. Its compatibility profile grades "Scene
  constructors, text shaping, inspection, evidence, codec and wire format" as `DotNetOnly`.
- **Why.** The constructors are pure record and union builders. What is not portable in `Scene.fs` is the text
  shaping, which depends on `TextShaping.fs` and uses `System.Security.Cryptography` for fingerprints, together with
  inspection and evidence.
- **Where it is fixed.** **FS.GG.Rendering**, which owns the package and its compatibility profile. The scope is the
  **portable subset**, not every .NET member. The pure constructors (`Colors`, `Paint`, the `Path` builders and the
  node builders of `Scene`) move to a portable source that the Fable project compiles, and the profile grades them
  `Portable`. Text shaping, measurement, inspection,
  evidence and the codec stay `DotNetOnly`. Rendering chooses whether that means a separate module or a split of
  `Scene.fs`.
- **Game's side.** Game does **not** publish a second `Scene` or `Colors` API, which would compete with Rendering's.
  Until the Rendering fix ships, the glyph library builds `SceneNode` data with private helpers, as the proof does.
  After the fix, the template, RuneScape, BAR and SC2 drop their copies of `node`, `group`, `rect` and `rgb`.
- **Intake:** FS.GG.Rendering#1504, "Fable view of FS.GG.UI.Scene ships Scene/Colors helpers". FS.GG.Rendering#1506,
  filed from this ADR, duplicates it and is being folded into #1504. The scope of that Delivery is the portable subset
  above, not "the same constructors as .NET".

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
   | others | `Unit` (enemy: the stepped-diamond frame; the index badge if several other players share that allegiance) |
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
   | NPCs and players | `Unit`, by allegiance: combat level above 0 means `Enemy` (the stepped diamond), otherwise `Neutral` (the square) |
   | the player | `Unit` (own) |
   | path dots | `PathGlyph` |
   | destination | `Marker Destination` |
   | selection | `Selection` |

   - **Selectable objects** come from `GlyphDrawing.frame` and `hit`: the **frame only**, independent of selection,
     amplifiers and brackets (D1). The kit's hit probes at 0.2 and 0.8 of an object's bounds (RuneScape's gap 2)
     therefore fall inside the frame, never in an inset or a reserve.
   - **Layout.** A 32-unit tile holds a 24-unit frame. Its cue reserve and amplifiers overlap neighbouring tiles, which
     `Glyphs.scene`'s passes allow: amplifiers first, so a neighbour's status bar never covers a frame (D10). The
     dense-board kit case is this layout.

   RuneScape's no-assets rule is unchanged: it adopts only vector glyphs. Its byte-identical .NET/Fable digests and
   its creature-colour probe are regenerated in the adoption PR.
3. **Later and optional:** BAR (allegiance and kind tokens; the veil waits for Q5), FourD (the `light` preset; the CSS grid
   takes `cssVariables`) and SC2 (the DOM map takes `Theme.hex`).

Each adopting consumer's pixel tests change expected values in its adoption PR. That is a planned change in that game's
own PR, not a regression.

### D10. Focus and selection with the input pipeline (#787)

This decision is reconciled with the #787 ADR, `docs/adr/0001-standard-input-pipeline.md`, as merged by PR #793 at
`bfd1257d` (reconciled earlier at `7ba30e53`, `f0561a79` and `1c1e4e3f`). That
ADR decides that **focus and hover are drawable state held by the input host**: `InteractionCues` holds the focused
object, the hovered object and whether focus is visible. The cues are not game state and do not survive a backend
switch. Every interactive backend draws the interaction's cue layer above the frame's Scene. The default layer is
`SceneInteraction.standardCues`, and a game replaces it with `SceneInteraction.withCues`.

The glyph layer follows that split. Two reviews raised conflicts and requirements: the 2026-11-01 review of #793, and the
review of this ADR at `8cbd7bed` (B2 and B3). They are settled as follows.

- **Selection** is game state (`InteractiveScene.Selected`). It is drawn in the frame's Scene as `Selection` brackets
  through `GlyphState.Selected`.
- **Focus and hover** are never in `GlyphState`. They are drawn only by the cue layer.

**One type, with no wrapping.** The cue layer has exactly the `withCues` type that #793 set at `f0561a79`. Its
`CueContext` carries the cues, the frame's objects and the presented scale:

```fsharp
val cues      : (unit -> Theme) -> CueContext -> SceneNode list   // focus ring only
val cuesHover : (unit -> Theme) -> CueContext -> SceneNode list   // focus ring plus hover (opt-in)
// use: SceneInteraction.withCues (Glyphs.cues (fun () -> currentTheme ())) interaction
```

A `SceneNode list` composes into the surface's frame without wrapping, and `standardCues` has the same type. The
earlier draft that returned a `Scene` is withdrawn.

**A theme switch reaches the cues (review B3).** `withCues (Glyphs.cues theme)` would fix the theme when the registry is
built, so a later switch would recolour the frame but not the cues.
- The cue layer takes a **theme reader**, `unit -> Theme`, and reads it each time the host draws the layer.
  The game updates the theme its reader returns before it dispatches the state that shows the new theme, so the frame
  and the cues of one Present use the same theme.
- **Agreed with the #787 owner** (#793, merged at `bfd1257d`): `CueContext` stays as it is (cues, objects, `cssPerUnit`,
  with no theme field), and the reader on the glyph side is the mechanism. A state-aware `withCues` was considered and
  not adopted.
- **Kit:** D11's A → B → A switch check includes the cue layer, with focus visible on an object.

**Hover is opt-in.** The default `Glyphs.cues` draws focus and nothing for hover, as `standardCues` does. After a
pointer press the pressed object is hovered, and focus is not visible, so the default draws the same frame as before
the press. `Glyphs.cuesHover` adds the hover outline. A game that opts in changes its frame on hover by design, so it is
tested against #793's restated I8, which takes a hover-only frame as the reference.
- **Settled in #793 (merged at `bfd1257d`):** its ADR text now says that the glyph layer's default `cues` draws focus only, and
  that hover is opt-in through `cuesHover`. The earlier wording, that `Glyphs.cues` "also draws a hover outline", is
  gone.

**Deterministic drawing.** The layer is a pure function of the theme the reader returns and the `CueContext`. The
focused and hovered ids are looked up in `objects`, and an id that is not there draws nothing. Every combination of
focus, hover and selection is defined:

| Focus visible on X | Hovered | Selected (frame) | The cue layer draws | The frame draws |
|---|---|---|---|---|
| no | none | — | nothing | brackets if selected |
| no | X | — | `cues`: nothing; `cuesHover`: hover outline on X | brackets if selected |
| yes | none or X | — | the focus ring on X (it covers hover on the same object; no hover outline is drawn on X) | brackets if selected |
| yes | Y ≠ X | — | the focus ring on X; `cuesHover` also the hover outline on Y | brackets if selected |
| focused id but not visible (after a press) | any | — | as "no" above | brackets if selected |

**Layer order:** the frame (with selection brackets), then the hover outline, then the focus ring.

**Placement, and why it is not circular (reviews B2 and R1).** Hit bounds are the frame only (D1), so they do not move
with selection, brackets or the ring.

The **cue reserve** is a fixed band of scene units around the frame, sized by the theme:
- `ring = 2 × max(theme band, 2 / Theme.minCssPerUnit)`, for both tones together;
- `reserve = ring + gap + bracket stroke`;
- `Theme.minCssPerUnit` is the smallest presented scale the theme supports. It **defaults to 1**, a board shown at its
  scene size or larger. A game opts lower explicitly, and pays for it with a wider reserve.
- `gap ≥ 1 / Theme.minCssPerUnit` units, so that #793's I14 probes, which stay at least 1 CSS px inside or outside each
  edge, land on the ring or on the ground and never across a boundary.

At the defaults (band 2, gap 2, bracket stroke 2, `minCssPerUnit` 1) the reserve is 8 units per side. At 0.5 it was 14,
which the review judged too wide.

Within the reserve, everything has a fixed place, from the frame outwards:
- the **focus ring sits directly against the frame**. Its inner (light) band touches the frame's edge, so #793's I8
  probe just outside the frame lands on the ring;
- the hover outline sits in the ring's band, against the frame;
- then the gap;
- then the selection brackets, at the reserve's outer edge.

Amplifiers sit outside the reserve.

**Layout extents may overlap neighbours.** A dense board, such as RuneScape's 32-unit tiles or FourD's 40-unit cells,
cannot give every glyph its own reserve and amplifier ring. `Glyphs.scene` therefore draws in **passes** (review N2):
1. all amplifiers (status bars, pips, corner badges, facing);
2. all bodies, each glyph's frame immediately followed by its fill, icon, team mark, inner modifiers and **team
   badge**;
3. all selection brackets.

The cue layer then draws over all of them.

**Guarantee.** No amplifier ever covers a frame, its own or a neighbour's, because amplifiers draw first. An amplifier
may be covered where a neighbour's body overlaps it. A status bar that reaches into the tile above is cut by that tile's
frame, and stays readable where it lies over ground. Brackets and cues are always on top.

**Dense-board kit case.** The kit's glyph section draws D9's board: 32-unit tiles, each with a 24-unit identity frame,
its team badge inside it, and a status bar in its *above* amplifier slot, beyond the 8-unit reserve, so bars reach into
the tiles above. The case requires three things:
- every frame's decided pixels and box edges hold;
- every badge's decided pixels hold;
- a control drawn with bodies first does not match.

The proof runs this case (*The proof*).

`draw` needs no scale, because the reserve is in scene units. The kit's box-edge check runs with no cue showing. On an
isolated glyph it then passes by construction: nothing is drawn within the gap outside a frame, so the pixel just
outside each edge is the ground. On a dense board, that pixel may be a neighbour's amplifier, which still differs from
the frame, and the frame's own edge pixel still holds because bodies draw after amplifiers.

**At least 2 CSS px after the surface transform.** Each focus band is
`max(theme band, 2 / CueContext.cssPerUnit context)` scene units wide, rounded up to a whole CSS px.
- **Measured in CSS px, not device px.** `CueContext` carries no device pixel ratio, so the bands are not promised to
  align to device pixels. A DPR field would be requested only if the kit's evidence showed a need.
- **The hover outline** is `max(theme hover width, 1 / cssPerUnit)`, so it is at least 1 CSS px.
- **Below `minCssPerUnit`,** the ring keeps its 2 CSS px and may cover the brackets. Accessibility wins over layout.
  The layer reports a diagnostic, and the kit runs only at or above the theme's minimum scale.

The input host supplies `cssPerUnit` in `CueContext`, as #793 states as merged (`bfd1257d`), with cue widths in CSS px. #793's restated I8 asserts the 2 CSS px
width for `standardCues` and for any `withCues` layer.

**Unlit on Babylon.** The cue layer is Scene data drawn through the same fill path as the frame. Once #786 lands,
Babylon draws it unlit at its declared colours, so the measured contrast of the two tones is the declared contrast.
Until #786, Babylon draws it at about 0.75 of each colour. Scaling both colours by 0.75 lowers their contrast ratio (for
example, white on (34,40,49) drops from 14.8:1 to 9.1:1), so the kit's glyph and cue checks assert the declared colours
only after #786.

**Contrast.** The theme validates both focus tones: the outer tone against the ground and the inner tone against the
outer one, each at least 3:1. This is the two-tone rule #787 takes from WCAG 2.2 focus appearance. Selection and both
focus tones are also checked against every team and allegiance colour (D12).

**The proof** predates #793 and this placement. It draws focus as a single-tone outline in the frame's Scene, and its
brackets sit one gap outside the bounds.

### D11. Colour derivation is deterministic: OKLCH in F#, rounded half up after a clamp

Palettes are derived in **OKLCH** (OKLab's polar form, as defined in CSS Color 4). It is far more hue-uniform than
CIELAB, where blue drifts towards purple. The derivation must give the same bytes on .NET and through Fable.

**Quantisation clamps `v = channel × 255` to [0, 255], then takes `Floor(v + 0.5)`. It is not `Floor(v)`.** The earlier
revision of this ADR, and the research it took the idea from, said `Floor`. The review of #794 at `8cbd7bed` measured
that this is wrong, and its delta review at `0df23231` confirmed the fix (0 failures, 0 .NET-vs-Node differences):
- An OKLCH round trip of an 8-bit colour lands **within about 1e-12 of the integer, on either side**. When it lands
  below, `Floor` drops one step. That happens on
  **94.8 %** of the 16.7 million sRGB colours, against **0** with `Floor(v + 0.5)`.
- `cbrt` and `pow` differ by 1–3 ULP between .NET and Node. With accurate maths, .NET and Node hex differ on
  **44.6 %** of colours under `Floor(v)`, and on **0** under `Floor(v + 0.5)`.
- The explicit `Floor(v + 0.5)` is used, never `Math.Round`, because the two runtimes' `Math.Round` break half-way ties
  differently.

The rest of the rule:
- **One implementation, in F#.** Derivation runs from the same F# source on both targets. No JavaScript colour library
  sits on the drawing path.
- **Gamut mapping** follows CSS Color 4: chroma is reduced at constant L and h until the colour is in sRGB, within the
  ΔEOK just-noticeable difference.
- **Contrast is validated on the 8-bit values,** the ones that are drawn, through a committed 256-entry
  sRGB→linear table. There is no `pow` at validation time, so a ratio near 3:1 cannot pass on one runtime and fail on
  the other.
- **Presets are baked at build time.** Each preset's resolved hex values are generated, committed as data, and
  compared with a fresh derivation in CI. Run-time derivation is used only for custom seeds and player overrides.
- **DTCG import prefers `hex`.** When a DTCG colour carries both `components` and `hex`, the hex is taken.
  "Lossless" export followed by import means **equal resolved hex** for every alias, not equal floating-point
  components.
- **Kit checks** (in the kit's glyph section, D6), for every preset:
  1. identical hex for every alias from **.NET on Linux, .NET on Windows and Node** (through Fable). The derivation is
     also run over the full 8-bit round trip, so a regression in the quantiser is caught;
  2. a theme switch A → B → A gives identical `ScenePixels` expectations, **including the cue layer** (D10);
  3. the theme-swap control does not match, as in the proof;
  4. DTCG export and re-import gives equal resolved hex, and the CSS variables equal the Scene's colours.

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
   pips, or the ordinal **size step** or **lightness step** slots (D1).
2. **Categorical hue needs a redundant channel.** A nominal field encoded by hue must also be encoded by a non-hue
   channel drawn from bars: the identity frame, an icon, a modifier or the index badge. Whenever two values of the field
   can be visible together with the same non-hue encoding otherwise, that redundant channel is mandatory. The
   `monochrome` render must still tell the values apart in decided pixels, at or above the minimum identity frame size
   (12 units). Below it, the accessible mirror label is the redundant channel.
3. **Channel capacity.** A field's cardinality must not exceed the capacity of its channel at the glyph's size.
   - **Hue** capacity depends on size. On a dark ground at glyph sizes, about **6–7** hues are usable. The theme
     declares the capacity per size step, read with `Theme.hueCapacity theme sizeStep`, and 8 is the ceiling at the
     largest step (Munzner's 6–12 bins, fewer at small sizes).
   - **Index badge:** 9 with one seven-segment digit, at least the largest hue capacity; two digits beyond.
   - **Frame shapes** carry identity only (D1), never another field.
   - **Pips:** at most 5. This is a **design default**, from subitizing (small counts are read at a glance), not a
     measured limit.
   - **Ordinal size or lightness steps:** at most 4 by default.

   Above capacity, the build fails and names the field.
4. **Fractions are length, not area.** A fraction encoded by area is refused.
5. **Flags are modifiers or badges**, the channels Bertin calls selective.
6. **Identity and team are always double-encoded** (D1).
   - **Identity:** frame shape plus colour.
   - **Team:** team colour plus the index badge, whenever two or more of the **game's** teams share an allegiance.
     Which teams are visible does not matter. Otherwise the allegiance frame is the second channel.
   - **Above the hue capacity:** the badge plus the allegiance frame, with colour falling back to allegiance.
   - **Kit check:** a monochrome scene of two teams that share an allegiance must decide pixels that differ between
     the two teams' glyphs (the badges), on every backend. The same check runs with the five identity frames (D1).

**Scope split.**

| #789 (this ADR, its implementation and the proof) | #795 (builds on this ADR) |
|---|---|
| the slot model (the opaque `GlyphSpec` and its builders, the library's identity frames, slots and amplifiers) and `draw` | `Symbology<'Entity>` and the `Encode` combinators |
| the token layers, the algorithm signature, the alias names and `Theme` as a Scene input | palette algorithms: OKLCH ramps, Leonardo-style contrast solving, gamut mapping, team hue spreading |
| the 3:1 floor, opaque-only, DTCG and CSS export, deterministic derivation (D11) | the five presets as data, and the player-override flow |
| the cue layer (D10) and the kit's glyph section (D6) | the kit's ΔE2000, CVD, redundancy and capacity checks |
| sprite atlases and the recolour template (D5) | — |

**Contrast against real neighbours (this ADR, D4).** Contrast is checked between the colours that actually touch:
- the frame or a keyline against the ground;
- the icon against the fill;
- the team ring against the body it sits between, a body-colour keyline outside it and the body fill inside it (E1);
- the status bar's keyline against the ground, and each status fill against the track;
- the inner focus tone against the outer one.

The proof's dark status track measured **1.33:1** on the ground. The proof now keylines the bar in the body colour, and
its tests check the keyline against the ground and each fill against the track.

**Distinctness and colour-vision checks (#795).** Teams, allegiances and status levels must differ pairwise by at
least a ΔE2000 threshold at glyph size. The required difference grows as marks shrink (Stone, Szafir and Setlur), so
the threshold depends on size. The check runs twice:
- with normal vision, starting at ΔE ≥ 12;
- under Machado simulation of protanopia, deuteranopia and tritanopia at severity 1.0, simulated in linear RGB,
  starting at ΔE ≥ 8, together with a minimum OKLab lightness separation for each team pair.

**Scope of distinctness.** Pairs are checked **within** a category: teams with teams, allegiances with allegiances,
status levels with status levels. Selection and the focus tones are also checked against every team and allegiance
colour, because they are drawn over them. Other pairs across categories are not required to differ, in every preset
including `colourBlindSafe`, because shape and slot already separate them.

**Both thresholds are starting points to be calibrated** (Q11). Simulation is validation, not a substitute for testing
with players.

## The proof (experimental)

The proof does not constrain the API.

- **Glyphs.** `src/ReferenceGame/ExperimentalGlyphs.fs`, namespace `FS.GG.Game.Reference.Experimental`, in a project
  that is not packed. It has six glyphs (unit, structure, waypoint, selection brackets, focus outline and status bar)
  and two themes (`standard` and `colourBlindSafe`), in the portable profile only.
  The proof predates D1's slot model and frame mapping, D4's token layers, D10's placement and D11. Its themes are
  flat records of colours, and its `standard` theme is the seed of the `classic` preset. After the review at
  `8cbd7bed`, its status bar is keylined in the body colour: the dark track alone measured 1.33:1 on the ground.
- **.NET tests.** `tests/Game.Render.Tests/ExperimentalGlyphsTests.fs` checks twelve things:
  - every glyph lowers to the portable set;
  - the identity frames, the badges and the dense board (six tests, below);
  - each status part meets 3:1 against its real neighbour: the keyline on the ground, each fill on the track;
  - every colour a glyph draws is a pixel `ScenePixels` decides;
  - the proof board decides more than 150 pixels;
  - both themes meet the 3:1 floor;
  - today a text node drawn last on the board leaves no pixel decided. This pins the gap D6 closes, and the test is
    inverted when D6 lands.
- **Identity frames, badges and the dense board (reviews R3, N1 and N2).**
  - `Glyphs.identityFrame` builds D1's five bar-only frames, snapped to whole units. `Glyphs.badge` builds the
    seven-segment index badges. `Glyphs.denseBoard` builds D10's dense board in either pass order.
  - `Glyphs.identitySamples` prototypes D6's corner and quarter-edge samples.
  - Their .NET tests (six, with the inside-badge test) check:
    - every ordered frame pair differs by at least 2 with D6's samples, and every pair but Own/Ally with today's, at 12,
      16, 24 and 32 units;
    - every frame passes the box-edge check on its drawn bounding box, and off-grid centres snap to the same frame;
    - Enemy's and Unknown's boxes are 20 × 20 and 30 × 30 at 24;
    - all 36 badge pairs differ both ways, and the dense board keeps every frame's and every badge's pixels, while its
      control does not;
    - inside badges are drawn from 22 units and omitted below with a diagnostic, sit wholly on frame ink with a 1-unit
      margin, meet 3:1 against every fill, and leave the frame matrix unchanged.
  - The smoke repeats these on SVG, Pixi and Babylon:
    - the identity strip, and the 20 frame pairs at 24 units (`runIdentityFrames`);
    - the 20 frame pairs again, with badges inside the frames;
    - the badge strip, the 72 ordered digit pairs, the dense board with inside badges, and its bodies-first control
      (`runBadgesAndDenseBoard`).
- **Browser smoke.** `runGlyphProof` in `tests/Render.Browser.Smoke/Program.fs` draws the 256×128 proof board, one
  theme per row, on SVG, Pixi and Babylon. It requires every decided pixel to match on each backend, with a
  theme-swap control that must not match. It reports a named pixel per glyph part and takes a screenshot when
  `RENDER_SMOKE_SCREENSHOTS` is set; the driver gained a `screenshot` request.
- **Results** (fdev, Chrome for Testing 153.0.8010.12, both with SwiftShader and with the GPU (ANGLE Vulkan, RADV)):
  the smoke passed **511** browser checks. 484 come from before this change; the rest were added in four rounds:
  - 6 glyph-proof checks, on 2026-11-01;
  - 6 identity-frame checks, after the review at `0df23231`;
  - 12 badge and dense-board checks, after the review at `fe8f4ad2`;
  - 3 identity-pair checks with badges inside the frames, after the review at `12fe1183`.

  The capacity probe answered `admit` before each run.

  On each backend:
  - all **403** pixels `ScenePixels` decides for the board matched (391 before the status keyline), and the theme-swap
    control did not;
  - the identity strip drew all **160** decided pixels, and every one of the 20 ordered frame pairs differed. Each
    pair's smaller count matched D1's D6-sample row for 24 units exactly, on SVG, Pixi and Babylon in both runs:
    Own/Ally 4, Own/Neutral 6, Ally/Neutral 10, Ally/Enemy 54, Ally/Unknown and Enemy/Unknown 28, and 16 for the
    rest. With badges inside the frames the 20 pairs gave the same counts;
  - the badge strip drew all **684** samples, and all 72 ordered digit pairs differed;
  - the dense board, with a badge inside every frame, drew all **1203** decided pixels, and its bodies-first control
    did not match.
  - SVG and Pixi show the scene colours exactly.
  - Babylon shows 0.75 of them, within the kit's tolerance of 4, because it lights fills; #786 removes this.
  - Screenshots are in `game-render-02-glyphs/`, for the board (`glyphs-*`), the identity strip (`frames-*`), the badges
    (`badges-*`) and the dense board (`dense-*`): one per backend from the GPU run, plus Babylon from the software run.
    The SVG screenshot also catches the SVG input host's "No object selected" status line: the smoke page has no
    visually-hidden style, so that text sits over the board. The renderer does not draw it.
  - The named-pixel table in the PR is **informational**: its points are read and reported, not asserted. The
    asserted pixels are the ones `ScenePixels` decides.

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
2. FS.GG.Game: #786 (unlit Babylon) and #773 (canvas strokes), as they are.
   - #786 is the prerequisite of D6's **single colour and tolerance** across the three backends. Until it lands, the
     kit keeps Babylon's 0.75 factor, as the proof does.
   - #773 is the prerequisite of the **stroked profile** and its ring, band and polygon models (step 5).
   - Neither blocks step 3.
3. FS.GG.Game: `FS.GG.Game.Render.Glyphs`, in the portable profile, with tests, the kit's glyph section and bounded
   unmodelled nodes. It joins the Fable view and covers:
   - the D1 slot model and `draw`, the five bar-only identity frames, and the inside team badges;
   - **D6's corner and quarter-edge samples for identity frames and badges,** shipped **together with** the frames and
     badges. They need neither #786 nor #773, because frames and badges are opaque bars, which every backend already
     draws. Without them, the kit cannot tell Own from Ally, or separate 11 of the 36 digit pairs;
   - the dense-board and identity-pair kit cases (D10, D6);
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
- **Q2. Stepped frames, or smooth ones in the stroked profile?** D1 builds every identity frame from bars, including
  the stepped diamond and the stepped quatrefoil, so the kit can tell them apart today.
  - *Default:* the stepped forms stay the identity frames in **both** profiles. The stroked profile may draw a smooth
    diamond or quatrefoil only after the D6 polygon model lands, and only if D1's matrix, re-run with that model,
    still shows every pair apart.
  - *Trade-off:* the stepped forms look pixel-art rather than APP-6-exact at large sizes.
- **Q3. A closed union or an open glyph type?**
  - *Default:* `Glyph`, `GlyphSpec`, `GlyphDrawing`, `GlyphFamily`, `IconRef`, `ModifierRef`, `AmplifierSlot` and
    `Amplifier` are all **opaque**, built only through functions. `Custom`
    remains the escape hatch. The kit enumerates the starter set through a published list of sample specs, not by
    matching on cases.
  - *Trade-off:* consumers cannot pattern-match on glyphs. In return, adding a slot, an amplifier or a family later is
    additive under the `.fsi` rule.
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
- **Q15. Icon and badge side by side.** At 22 to 39 units the badge takes the inner centre and the icon is not drawn.
  - *Default:* from 40 units, the inner box holds the icon (left) and the badge (right). The threshold is to be
    measured per frame in the implementation, as the badge minimum was.
  - *Trade-off:* small badged glyphs lose their class icon; class then rests on size, footprint and the label.
