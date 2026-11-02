namespace FS.GG.Game.Render

/// Public contract type exposed by the FS.GG.Game.Render package.
///
/// A glyph family (ADR GAME-RENDER-02.4 D1, FS.GG.Game#789): unit, structure, terrain, item, marker or path. Values
/// in `GlyphFamily`. Families are told apart by icon, size and footprint, never by frame shape.
[<Sealed>]
type GlyphFamily

/// Public contract module exposed by the FS.GG.Game.Render package.
[<RequireQualifiedAccess>]
module GlyphFamily =

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Units: the identity frame, a team ring (inside a body-colour keyline that also lines Ally's holes, on the body),
    /// an icon or the index badge, inner modifiers and amplifiers.
    val unit: GlyphFamily

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Structures: drawn as units, on a footprint of n x m cells, with the team mark as a foot band (the ring
    /// layer's lower rows in the team colour, the body above) instead of a ring.
    val structure: GlyphFamily

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Terrain: a tile over the footprint, inset by the theme's gap, coloured by its kind (the icon names it: open,
    /// blocked, difficult, water or void).
    val terrain: GlyphFamily

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Items: a small oval at the anchor, at most half the identity frame size.
    val item: GlyphFamily

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Markers: the icon names it (waypoint, destination or target).
    val marker: GlyphFamily

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The family's name.
    val name: family: GlyphFamily -> string

/// Public contract type exposed by the FS.GG.Game.Render package.
///
/// A reference to an icon, resolved by the theme's icon set (`IconRef.named`).
[<Sealed>]
type IconRef

/// Public contract module exposed by the FS.GG.Game.Render package.
[<RequireQualifiedAccess>]
module IconRef =

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The icon named `name`. The library draws `infantry`, `armour`, `artillery`, `support` and `structure` inside
    /// frames; the terrain kinds `open`, `blocked`, `difficult`, `water` and `void`; and the markers `waypoint`,
    /// `destination` and `target`. Another name draws no icon and reports a diagnostic.
    val named: name: string -> IconRef

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The icon's name.
    val name: icon: IconRef -> string

/// Public contract type exposed by the FS.GG.Game.Render package.
///
/// A reference to an inner modifier, a flag drawn in `glyph.modifier` in a corner-notch slot inside the frame (Q2).
[<Sealed>]
type ModifierRef

/// Public contract module exposed by the FS.GG.Game.Render package.
[<RequireQualifiedAccess>]
module ModifierRef =

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Stale information.
    val stale: ModifierRef

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Carrying something.
    val carrying: ModifierRef

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Disabled.
    val disabled: ModifierRef

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// A game's own flag.
    val named: name: string -> ModifierRef

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The modifier's name.
    val name: modifier: ModifierRef -> string

/// Public contract type exposed by the FS.GG.Game.Render package.
///
/// Where an amplifier sits, outside the frame and its cue reserve (ADR D1, D10).
[<Sealed>]
type AmplifierSlot

/// Public contract module exposed by the FS.GG.Game.Render package.
[<RequireQualifiedAccess>]
module AmplifierSlot =

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Above the frame.
    val above: AmplifierSlot

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Below the frame.
    val below: AmplifierSlot

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Corner `index`, clockwise from the top left (0 to 3).
    val corner: index: int -> AmplifierSlot

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// From the anchor, for facing (the stroked profile).
    val facing: AmplifierSlot

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Right of the frame.
    val right: AmplifierSlot

/// Public contract type exposed by the FS.GG.Game.Render package.
///
/// What an amplifier shows (ADR D1). The team index badge is never an amplifier: it is drawn inside its frame.
[<Sealed>]
type Amplifier

/// Public contract module exposed by the FS.GG.Game.Render package.
[<RequireQualifiedAccess>]
module Amplifier =

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Ordinal pips: `level` filled of `outOf`, at most 5 (a design default from subitizing).
    val pips: level: int -> outOf: int -> Amplifier

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// A fraction bar (health, progress): its length is the fraction, from 0 to 1. Its fill is `status.good` above 0.6,
    /// `status.warn` above 0.3 and `status.bad` otherwise, on `status.track`, keylined in `status.keyline`.
    val bar: fraction: float -> Amplifier

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// A small icon badge (a corner slot).
    val badge: icon: IconRef -> Amplifier

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Facing, in radians: drawn in the stroked profile only, so the portable profile reports a diagnostic.
    val facing: radians: float -> Amplifier

/// Public contract type exposed by the FS.GG.Game.Render package.
///
/// What a glyph means and where it is, never its colours (ADR D1): a family, an anchor (the centre) and a footprint in
/// cells, with slots set by the `GlyphSpec` functions. What FS.GG.Game#795's `Symbology` builds.
[<Sealed>]
type GlyphSpec

/// Public contract module exposed by the FS.GG.Game.Render package.
[<RequireQualifiedAccess>]
module GlyphSpec =

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// A glyph of `family` centred on `anchor`, `footprint` cells wide and high, with identity `Unknown`, size step 3
    /// (the largest) and no other slot set.
    val create: family: GlyphFamily -> anchor: FS.GG.UI.Scene.Point -> footprint: FS.GG.UI.Scene.Size -> GlyphSpec

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The identity, which fixes the frame shape and its colour.
    val identity: identity: Identity -> spec: GlyphSpec -> GlyphSpec

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The icon slot.
    val icon: icon: IconRef -> spec: GlyphSpec -> GlyphSpec

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The team, from 0: its colour is the team mark (`glyph.team.N`), and its badge shows `team + 1`.
    val team: team: int -> spec: GlyphSpec -> GlyphSpec

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The team index badge is mandatory: the game sets this when two or more of its teams share an allegiance
    /// (presence depends on the game's teams, never on which are visible), or when team colour falls back to allegiance.
    val badged: spec: GlyphSpec -> GlyphSpec

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Team colour falls back to the allegiance colour (above the hue capacity, or when the player asks).
    val allegianceColouring: spec: GlyphSpec -> GlyphSpec

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The ordinal size step, 0 to 3: the identity frame is `cell x footprint x (0.5 + step / 12)`, so 16, 18, 21 and
    /// 24 units at a 32-unit cell.
    val sizeStep: step: int -> spec: GlyphSpec -> GlyphSpec

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The ordinal lightness step of the fill, 0 to 3 (0 is the colour itself; each step moves it 0.06 towards the
    /// ground in OKLCH lightness).
    val fillStep: step: int -> spec: GlyphSpec -> GlyphSpec

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Add an inner modifier (at most four, one per corner-notch slot).
    val modifier: modifier: ModifierRef -> spec: GlyphSpec -> GlyphSpec

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Add an amplifier in `slot`; amplifiers that share a slot stack outwards in the order added.
    val amplifier: slot: AmplifierSlot -> amplifier: Amplifier -> spec: GlyphSpec -> GlyphSpec

/// Public contract type exposed by the FS.GG.Game.Render package.
///
/// Game state a glyph shows: opaque, built from `GlyphState.idle` with `GlyphState.selected` and `GlyphState.stale`, so
/// a later state (planned, dashed...) is additive. Focus and hover are never here: they are cues the input host holds
/// (ADR D10).
[<Sealed>]
type GlyphState

/// Public contract module exposed by the FS.GG.Game.Render package.
[<RequireQualifiedAccess>]
module GlyphState =

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Neither selected nor stale.
    val idle: GlyphState

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Whether the object is selected: corner brackets in the cue reserve.
    val selected: selected: bool -> state: GlyphState -> GlyphState

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Whether the information is stale: the frame is drawn in `glyph.stale`.
    val stale: stale: bool -> state: GlyphState -> GlyphState

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Whether the state is selected.
    val isSelected: state: GlyphState -> bool

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Whether the state is stale.
    val isStale: state: GlyphState -> bool

/// Public contract type exposed by the FS.GG.Game.Render package.
///
/// A glyph to draw: a spec with its state, selection brackets, a path, or a game's own drawing. Built by the `Glyph`
/// functions.
[<Sealed>]
type Glyph

/// Public contract type exposed by the FS.GG.Game.Render package.
///
/// A drawn glyph: its Scene nodes in their passes, its frame (the hit bounds) and its layout extent. Built by
/// `Glyphs.draw`, or by `GlyphDrawing.create` for a custom glyph.
[<Sealed>]
type GlyphDrawing

/// Public contract module exposed by the FS.GG.Game.Render package.
[<RequireQualifiedAccess>]
module GlyphDrawing =

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// A custom glyph's drawing: its body nodes, its frame and hit shape, and its layout extent (which contains the
    /// frame).
    val create:
        body: FS.GG.UI.Scene.SceneNode list ->
        frame: FS.GG.UI.Scene.Rect ->
        hit: SelectableShape ->
        extent: FS.GG.UI.Scene.Rect ->
            GlyphDrawing

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Every node, in pass order: amplifiers, then the body, then selection brackets.
    val nodes: drawing: GlyphDrawing -> FS.GG.UI.Scene.SceneNode list

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The amplifier pass.
    val amplifiers: drawing: GlyphDrawing -> FS.GG.UI.Scene.SceneNode list

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The body pass: the frame, then its fill, icon, team mark, inner modifiers and, last, the team badge.
    val body: drawing: GlyphDrawing -> FS.GG.UI.Scene.SceneNode list

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The selection-bracket pass.
    val brackets: drawing: GlyphDrawing -> FS.GG.UI.Scene.SceneNode list

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The drawn bounding box of the frame, on whole units: the hit bounds, independent of state, amplifiers and
    /// brackets. At a nominal 24 units Own and Ally are 24 x 16, Neutral 24 x 24, Enemy 20 x 20 and Unknown 30 x 30.
    val frame: drawing: GlyphDrawing -> FS.GG.UI.Scene.Rect

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The hit shape inside `frame`.
    val hit: drawing: GlyphDrawing -> SelectableShape

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The layout extent: the frame, its cue reserve and its amplifiers. Extents may overlap neighbours.
    val extent: drawing: GlyphDrawing -> FS.GG.UI.Scene.Rect

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// What the drawing could not show, and why (an omitted badge, an unavailable icon slot, an unknown icon, a slot the
    /// portable profile does not draw...).
    val diagnostics: drawing: GlyphDrawing -> string list

/// Public contract module exposed by the FS.GG.Game.Render package.
[<RequireQualifiedAccess>]
module Glyph =

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// A spec in a state.
    val ofSpec: state: GlyphState -> spec: GlyphSpec -> Glyph

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Selection brackets around `bounds`, in the cue reserve.
    val selection: bounds: FS.GG.UI.Scene.Rect -> Glyph

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// A path through `points`: dots at half-cell spacing in `glyph.path`, and a step marker at the end.
    val path: points: FS.GG.UI.Scene.Point list -> Glyph

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// A game's own glyph: it still takes its colours from the theme and still returns its frame, hit shape and extent.
    val custom: draw: (Theme -> GlyphDrawing) -> Glyph

/// Public contract module exposed by the FS.GG.Game.Render package.
///
/// The glyph library (ADR GAME-RENDER-02.4, FS.GG.Game#789) in the portable profile: opaque rectangles, circles and
/// ellipses only, so SVG, Pixi and Babylon draw it and `ScenePixels` decides it.
[<RequireQualifiedAccess>]
module Glyphs =

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The smallest identity frame, in units: below it the stepped forms have one-unit bands, and identity rests on
    /// colour and the mirror label.
    val minIdentityFrame: float

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The smallest frame that holds a one-digit team badge (teams 1 to 9) inside it, for every identity: 22 units.
    /// Measured per identity: Neutral 12, Unknown 16, Own 18, Enemy 20, Ally 22.
    val minBadgeFrame: float

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The smallest frame that holds a two-digit team badge (teams 10 and up; team numbers never wrap): 33 units.
    /// Measured per identity: Own 18, Neutral 16, Unknown 24, Enemy 28, Ally 33.
    val minTwoDigitBadgeFrame: float

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The smallest frame that holds the icon and the badge side by side (Q15): 40 units.
    val minIconAndBadgeFrame: float

    /// ADR D1's identity frame, from bars, centred on whole units.
    val internal identityFrame:
        identity: Identity -> cx: float -> cy: float -> size: float -> ink: FS.GG.UI.Scene.Color -> FS.GG.UI.Scene.SceneNode list

    /// The team-mark layers of a frame (keyline, ring, body), as rectangles relative to its centre.
    val internal teamLayers:
        identity: Identity ->
        w: float ->
        band: int ->
        keyline: int ->
            (float * float * float * float) list * (float * float * float * float) list * (float * float * float * float) list

    /// One seven-segment digit (6 x 10) whose top left is (x, y).
    val internal digit: d: int -> x: float -> y: float -> ink: FS.GG.UI.Scene.Color -> FS.GG.UI.Scene.SceneNode list

    /// The team badge of team number `number`, centred on (cx, cy).
    val internal badgeNodes: number: int -> cx: float -> cy: float -> ink: FS.GG.UI.Scene.Color -> FS.GG.UI.Scene.SceneNode list

    /// The box a badge needs on frame ink: the digits plus a 1-unit margin.
    val internal badgeBox: number: int -> cx: float -> cy: float -> FS.GG.UI.Scene.Rect

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The identity frame size of `spec` in `theme`, in whole units.
    val frameSize: theme: Theme -> spec: GlyphSpec -> float

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Whether `spec`'s icon slot is available in `theme`: false when a mandatory badge takes the frame's inner centre
    /// (a frame of `minBadgeFrame` up to below `minIconAndBadgeFrame`), so a builder such as FS.GG.Game#795's
    /// `Symbology` can refuse an icon encoding at build time instead of losing it when drawn.
    val iconSlotAvailable: theme: Theme -> spec: GlyphSpec -> bool

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The cue reserve around a frame, in scene units: `2 x max(band, 2 / minCssPerUnit) + gap + bracket stroke`, 8 at
    /// `classic`'s defaults.
    val reserve: theme: Theme -> float

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Draw one glyph: pure, total and deterministic. Never reads state other than its arguments.
    val draw: theme: Theme -> glyph: Glyph -> GlyphDrawing

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// A Scene of glyphs, drawn in passes: every amplifier, then every body (each frame right before its fill, icon, team
    /// mark, modifiers and badge), then every selection bracket. No amplifier covers a frame, its own or a neighbour's.
    /// The ground is the game's: the Scene draws none.
    val scene: theme: Theme -> glyphs: Glyph list -> FS.GG.UI.Scene.Scene

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The themed cue layer for `SceneInteraction.withCues` (ADR D10): the two-tone focus ring on the focused object
    /// when focus is visible, its inner band (`cue.focus.inner`) against the frame and its outer band
    /// (`cue.focus.outer`) outside it, each `max(band, 2 / cssPerUnit)` units rounded up to a whole CSS px. Nothing for
    /// hover. The theme is read from `theme ()` each time the layer is drawn, so a switch reaches the cues.
    val cues: theme: (unit -> Theme) -> context: CueContext -> FS.GG.UI.Scene.SceneNode list

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// `cues` plus the hover outline (`cue.hover`, `max(hover width, 1 / cssPerUnit)` units) against the hovered
    /// object's frame, unless that object has the visible focus ring. The hover outline draws before the ring.
    val cuesHover: theme: (unit -> Theme) -> context: CueContext -> FS.GG.UI.Scene.SceneNode list

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The starter set: one sample spec of each family, identity and slot, at 32-unit cells from `origin`, which the
    /// kit draws in each theme (Q3: the kit enumerates glyphs through this list, never by matching on cases).
    val samples: origin: FS.GG.UI.Scene.Point -> Glyph list
