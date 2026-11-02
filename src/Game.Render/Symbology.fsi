namespace FS.GG.Game.Render

/// Typed game-state encodings, checked against every selectable theme before rendering.
/// Measures and field names are trusted declarations: accessor bodies are not reflected under Fable.
/// `spec` validates actual values and team rosters before they reach the glyph library.
module Symbology =

    type Identity = FS.GG.Game.Render.Identity

    /// Amplifier positions outside the frame (glyph ADR D1).
    type Amp =
        | Above
        | Below
        | Facing
        | Corner

    /// The glyph slots a field can be encoded on (glyph ADR D1). `Fill` is the frame's hue; `FillStep` its lightness;
    /// `SizeStep` the frame size (an area channel); `Label` is the accessible mirror label (no Scene text in v1, Q6).
    type Slot =
        | Frame
        | Fill
        | FillStep
        | SizeStep
        | Icon
        | TeamMark
        | Modifier
        | Amplifier of Amp
        | Label

    /// The measure of a field: its level of organisation (Bertin; Mackinlay's APT; Munzner's magnitude and identity
    /// channels).
    type Measure =
        | IdentityMeasure
        | Nominal of cardinality: int
        | Ordinal of levels: int
        | Fraction
        | Angle
        | Flag
        | Text
        | Team of roster: Identity list

    /// A field's value for one entity. A case or level outside the declared list reads as -1, a typed error in `spec`.
    type Value =
        | VIdentity of Identity
        | VCase of int
        | VLevel of int
        | VFraction of float
        | VAngle of float
        | VFlag of bool
        | VText of string
        | VTeam of int

    type Encoding<'Entity> =
        {
          Field: string
          Measure: Measure
          Slot: Slot
          Read: ('Entity -> Value)
        }

    /// The declaration a game writes (the builder's input).
    type Draft<'Entity> =
        {
          Family: string
          Frame: float
          Stroked: bool
          Encodings: Encoding<'Entity> list
        }

    /// Every broken rule names the field(s) involved. `explain` turns each into the change a game author makes.
    type SymbologyError =
        /// At least one selectable theme must be supplied.
        | NoThemes
        /// A frame must be finite and positive.
        | InvalidFrame of frame: float
        /// The portable grammar currently lowers unit and structure families.
        | UnsupportedFamily of family: string

        /// D12 rule 1: an ordered field (ordinal, fraction, angle) on hue alone.
        | HueOnlyOrderedData of field: string * measure: string

        /// D12 rule 2: a nominal field on hue with no non-hue channel for the same field.
        | CategoricalHueWithoutRedundancy of field: string

        /// D12 rule 3: more values than the channel holds (in `theme`, when the capacity is a theme's).
        | OverCapacity of
          field: string * slot: Slot * cardinality: int * capacity: int *
          theme: string option

        /// D12 rule 4: a fraction on area (the size step).
        | FractionAsArea of field: string

        /// D12 rule 5: a flag on a slot that is not selective.
        | FlagNotSelective of field: string * slot: Slot

        /// D12 rule 6 and D1: identity goes on the frame (shape plus colour), nowhere else.
        | IdentityNotOnFrame of field: string * slot: Slot

        /// D1: below the 12-unit minimum identity frame, the mirror label is the redundant channel.
        | IdentityBelowMinimumWithoutLabel of field: string * frame: float

        /// D12 rule 6: team is double-encoded with the allegiance frame, so a team encoding needs identity on the frame.
        | TeamWithoutIdentity of teamField: string

        /// D12 rule 6: teams share an allegiance, so the badge is mandatory; below 22 units it is omitted, and only a
        /// label can carry team.
        | TeamBadgeBelowMinimumWithoutLabel of
          field: string * frame: float * shared: Identity

        /// Root's #794 merge comment: a mandatory badge takes the inner centre of a 22-39 unit frame, so the icon slot is
        /// unavailable.
        | IconSlotTakenByBadge of
          iconField: string * teamField: string * frame: float *
          shared: Identity

        /// Two fields on one slot.
        | SlotTaken of slot: Slot * first: string * second: string

        /// The slot needs the stroked profile (#773), and the symbology targets the portable one.
        | NeedsStrokedProfile of field: string * slot: Slot

        /// The slot is not supported yet (a corner badge needs an icon per flag).
        | UnsupportedSlot of field: string * slot: Slot

        /// A measure on a slot that cannot show it; `allowed` lists the slots that can.
        | WrongSlot of
          field: string * measure: string * slot: Slot * allowed: Slot list

        /// A nominal or ordinal field with no values.
        | EmptyDomain of field: string

        /// One field encoded twice on the same slot.
        | DuplicateField of field: string

        /// A theme the player may choose cannot carry this symbology.
        | ThemeRefuses of theme: string * reason: string

        /// Run time: an entity's value is outside its field's declared domain.
        | ValueOutOfRange of field: string * value: string * domain: string

        /// Run time: an entity's identity is not its team's allegiance in the roster.
        | RosterMismatch of
          teamField: string * team: int * roster: Identity *
          entity: Identity

    val slotName: slot: Slot -> string

    val measureName: measure: Measure -> string

    /// The slots each measure may use (the design's measure table).
    val allowedSlots: measure: Measure -> Slot list

    /// The smallest frame an inside team badge fits (glyph ADR D1, measured on all five frames): the library's.
    val minBadgeFrame: float

    /// From this size the inner box holds the icon (left) and the badge (right) (glyph ADR Q15),
    /// `Glyphs.minIconAndBadgeFrame`: the library's.
    val sideBySideFrame: float

    /// The minimum identity frame (glyph ADR D1): the library's.
    val minIdentityFrame: float

    /// Pips: a design default from subitizing, not a measured limit (glyph ADR D12 rule 3).
    val maxPips: int

    /// Size and lightness steps (glyph ADR D12 rule 3).
    val maxSteps: int

    /// One seven-segment digit in the admitted grammar; team numbers never wrap (Root's #794 merge comment).
    /// Larger rosters need an explicit grammar extension for the library's two-digit badge minimum.
    val maxBadgeTeams: int

    /// The frame at the library's size step `step` (0 to 3) for a symbology whose default frame (step 3) is `frame`:
    /// `floor (cell x cells x (0.5 + step / 12))`, with `cell x cells = frame / 0.75` (part 3's `Glyphs.frameSize`).
    val frameAtStep: frame: float -> step: int -> float

    /// The library's size step of level `level` of an `n`-level ordinal on `SizeStep`: the top level is step 3, the
    /// default frame, and each lower level one step smaller.
    val stepOfLevel: n: int -> level: int -> int

    /// The portable icon range: the library's icon set (Q13: icons from primitives first).
    val iconSet: string list



    /// What a game author changes for each error.
    val explain: error: SymbologyError -> string

    /// A built symbology: the draft that passed every rule, and the notes for fallbacks (team colour falling back to
    /// allegiance colouring above a theme's hue capacity, a hue dropped in monochrome).
    type Symbology<'Entity> =
        private {
          Draft: Draft<'Entity>
          Notes: string list
          Shared: Identity option
          Roster: Identity list option
        }

    /// The build's informational fallbacks, such as team colouring above a theme's hue capacity.
    val notes: symbology: Symbology<'Entity> -> string list

    /// `Symbology.create family frame`: a portable-profile draft at a frame size in units.
    val create: family: string -> frame: float -> Draft<'Entity>

    /// Target the stroked profile (#773): unlocks Slot.Amplifier Facing.
    val stroked: draft: Draft<'Entity> -> Draft<'Entity>

    /// The `Encode` combinators (glyph ADR D12). Each names its field, so an error can name it too; F# cannot read an
    /// accessor's name without quotations, which Fable does not support.
    module Encode =

        val private indexIn:
          cases: 'v list -> value: 'v -> int when 'v: equality

        /// Identity (own, ally, neutral, enemy, unknown) on the frame: shape plus colour, double-encoded.
        val identity:
          field: string ->
            read: ('Entity -> Identity) ->
            slot: Slot -> draft: Draft<'Entity> -> Draft<'Entity>

        /// A nominal field over its closed list of cases.
        val nominal:
          field: string ->
            read: ('Entity -> 'v) ->
            cases: 'v list ->
            slot: Slot -> draft: Draft<'Entity> -> Draft<'Entity>
            when 'v: equality

        /// An ordinal field over its ordered levels, lowest first.
        val ordinal:
          field: string ->
            read: ('Entity -> 'v) ->
            levels: 'v list ->
            slot: Slot -> draft: Draft<'Entity> -> Draft<'Entity>
            when 'v: equality

        /// A fraction, 0 to 1.
        val fraction:
          field: string ->
            read: ('Entity -> float) ->
            slot: Slot -> draft: Draft<'Entity> -> Draft<'Entity>

        /// An angle in radians.
        val angle:
          field: string ->
            read: ('Entity -> float) ->
            slot: Slot -> draft: Draft<'Entity> -> Draft<'Entity>

        /// A flag.
        val flag:
          field: string ->
            read: ('Entity -> bool) ->
            slot: Slot -> draft: Draft<'Entity> -> Draft<'Entity>

        /// Text for the accessible mirror label (no Scene text in v1, glyph ADR Q6).
        val label:
          field: string ->
            read: ('Entity -> string) ->
            draft: Draft<'Entity> -> Draft<'Entity>

        /// The team index (0 to roster length - 1), with the game's roster: the allegiance of each team, by index.
        /// Whether two of the game's teams share an allegiance (so the badge is mandatory) depends on the roster, never
        /// on which are visible; `spec` refuses an entity whose identity is not its team's.
        val team:
          field: string ->
            read: ('Entity -> int) ->
            roster: Identity list ->
            slot: Slot -> draft: Draft<'Entity> -> Draft<'Entity>

    /// The allegiance two or more of the roster's teams share, if any.
    val sharedAllegiance: roster: Identity list -> Identity option

    /// The smallest and largest frames a symbology can draw (its size steps; the largest is the default frame).
    val frameRange: draft: Draft<'Entity> -> float * float

    /// Apply every rule of the glyph ADR's D12 against the themes the game lets the player choose. Theme-dependent
    /// capacities (hue) are checked at the SMALLEST frame the symbology can draw, against every theme whose colour
    /// carries categories; in `monochrome` hue is dropped and the redundant channel carries the field. Errors are
    /// de-duplicated.
    val build:
      themes: Theme list ->
        draft: Draft<'Entity> ->
        Result<Symbology<'Entity>,SymbologyError list>

    /// An entity's checked values: what `spec` returns, before they become a library `GlyphSpec`.
    type Encoded =
        private {
          Family: string
          Anchor: FS.GG.UI.Scene.Point

          /// The library size step (0 to 3) and the frame it draws.
          SizeStep: int
          Frame: float
          Identity: Identity option
          Icon: int option
          Team: int option

          /// Whether the team badge is mandatory (two of the game's teams share an allegiance).
          Badged: bool
          FillCase: int option
          FillStep: int
          Pips: (int * int) option
          Bar: float option
          Facing: float option
          Modifiers: string list
          Label: string
        }

    /// Whether a scene in `theme` shows team colours: decided once per scene (N6), from the roster and the theme's hue
    /// capacity at the symbology's smallest frame, so a size step never mixes team and allegiance colouring.
    val teamColouring: theme: Theme -> symbology: Symbology<'Entity> -> bool

    /// What `Symbology.spec` produces for one entity, or every value that breaks its declaration: a case, level,
    /// fraction or team outside its domain, or an identity that is not the entity's team's allegiance in the roster.
    val spec:
      symbology: Symbology<'Entity> ->
        anchor: FS.GG.UI.Scene.Point ->
        entity: 'Entity -> Result<Encoded,SymbologyError list>

    /// The mirror label of an entity (glyph ADR D1: the label names identity and team in every case).
    val label:
      symbology: Symbology<'Entity> ->
        entity: 'Entity -> Result<string,SymbologyError list>

    /// The library glyph of checked values: `GlyphSpec.create`, then `identity`, `team` (with `badged` when the roster
    /// shares an allegiance, and `allegianceColouring` when the scene does not show team colours), `icon`, `sizeStep`,
    /// `fillStep`, `modifier`, and the pips and bar amplifiers; `selected` is game state. A categorical `Fill` without a
    /// team is drawn as that case's team colour in the ring (`GlyphSpec.team`), unbadged.
    val glyph: teamColours: bool -> selected: bool -> e: Encoded -> Glyph

/// Combinators for named game-state fields. See `Symbology` for the checked grammar.
module Encode =

    val identity:
      field: string ->
        read: ('a -> Symbology.Identity) ->
        slot: Symbology.Slot ->
        draft: Symbology.Draft<'a> -> Symbology.Draft<'a>

    val nominal:
      field: string ->
        read: ('a -> 'b) ->
        cases: 'b list ->
        slot: Symbology.Slot ->
        draft: Symbology.Draft<'a> -> Symbology.Draft<'a> when 'b: equality

    val ordinal:
      field: string ->
        read: ('a -> 'b) ->
        levels: 'b list ->
        slot: Symbology.Slot ->
        draft: Symbology.Draft<'a> -> Symbology.Draft<'a> when 'b: equality

    val fraction:
      field: string ->
        read: ('a -> float) ->
        slot: Symbology.Slot ->
        draft: Symbology.Draft<'a> -> Symbology.Draft<'a>

    val angle:
      field: string ->
        read: ('a -> float) ->
        slot: Symbology.Slot ->
        draft: Symbology.Draft<'a> -> Symbology.Draft<'a>

    val flag:
      field: string ->
        read: ('a -> bool) ->
        slot: Symbology.Slot ->
        draft: Symbology.Draft<'a> -> Symbology.Draft<'a>

    val label:
      field: string ->
        read: ('a -> string) ->
        draft: Symbology.Draft<'a> -> Symbology.Draft<'a>

    val team:
      field: string ->
        read: ('a -> int) ->
        roster: Symbology.Identity list ->
        slot: Symbology.Slot ->
        draft: Symbology.Draft<'a> -> Symbology.Draft<'a>

