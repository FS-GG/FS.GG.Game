namespace FS.GG.Game.Reference.Experimental

open FS.GG.UI.Scene
open FS.GG.Game.Reference.Experimental.Themes

/// EXPERIMENTAL (FS.GG.Game#795, GAME-RENDER-02.8): the encoding grammar the design
/// `docs/roadmaps/game-render-02-themes.md` proposes. A game declares a `Symbology<'Entity>`: named field accessors bound
/// to the glyph slots of the glyph ADR (D1) through a typed measure, and the theme supplies the ranges. `Symbology.build`
/// enforces the glyph ADR's D12 rules at build time, and `Symbology.spec` checks each entity's values at run time; both
/// return every broken rule as a typed error that names the field and says what to change.
///
/// **What the checks can and cannot see (N5 of the #805 review).** Fable cannot read an accessor's name or body, so the
/// grammar trusts the author's declarations: the measure (`nominal` or `ordinal`) and the field name. Two encodings with
/// the same name are taken to be the same field, so redundancy (rule 2) is checked by name, and ordered data declared
/// `nominal` is not caught at build time. What the grammar does check at run time is every value against its declared
/// domain (an out-of-range case, level, fraction or team is a typed error, never case 0) and every entity's identity
/// against the team roster.
///
/// STAND-INS, replaced on merge: `GlyphSpec`, `Identity` and the drawing are #789's (`FS.GG.Game.Render.GlyphSpec`,
/// `[<RequireQualifiedAccess>] Identity`, part 3's `Glyphs.draw`, with erratum E1's ring layout); `minBadgeFrame`,
/// `minIdentityFrame` and `sideBySideFrame` are part 3's `Glyphs.minBadgeFrame`, `Glyphs.minIdentityFrame` and
/// `Glyphs.minIconAndBadgeFrame`.
module Symbology =

    type Identity = Glyphs.Identity

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

    type Encoding<'Entity> = { Field: string; Measure: Measure; Slot: Slot; Read: 'Entity -> Value }

    /// The declaration a game writes (the builder's input).
    type Draft<'Entity> = { Family: string; Frame: float; Stroked: bool; Encodings: Encoding<'Entity> list }

    /// Every broken rule names the field(s) involved. `explain` turns each into the change a game author makes.
    type SymbologyError =
        /// D12 rule 1: an ordered field (ordinal, fraction, angle) on hue alone.
        | HueOnlyOrderedData of field: string * measure: string
        /// D12 rule 2: a nominal field on hue with no non-hue channel for the same field.
        | CategoricalHueWithoutRedundancy of field: string
        /// D12 rule 3: more values than the channel holds (in `theme`, when the capacity is a theme's).
        | OverCapacity of field: string * slot: Slot * cardinality: int * capacity: int * theme: string option
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
        | TeamBadgeBelowMinimumWithoutLabel of field: string * frame: float * shared: Identity
        /// Root's #794 merge comment: a mandatory badge takes the inner centre of a 22-39 unit frame, so the icon slot is
        /// unavailable.
        | IconSlotTakenByBadge of iconField: string * teamField: string * frame: float * shared: Identity
        /// Two fields on one slot.
        | SlotTaken of slot: Slot * first: string * second: string
        /// The slot needs the stroked profile (#773), and the symbology targets the portable one.
        | NeedsStrokedProfile of field: string * slot: Slot
        /// The design accepts the slot, but the proof does not draw it yet (FillStep waits for #789's part 3).
        | SlotNotInProof of field: string * slot: Slot
        /// A measure on a slot that cannot show it; `allowed` lists the slots that can.
        | WrongSlot of field: string * measure: string * slot: Slot * allowed: Slot list
        /// A nominal or ordinal field with no values.
        | EmptyDomain of field: string
        /// One field encoded twice on the same slot.
        | DuplicateField of field: string
        /// A theme the player may choose cannot carry this symbology.
        | ThemeRefuses of theme: string * reason: string
        /// Run time: an entity's value is outside its field's declared domain.
        | ValueOutOfRange of field: string * value: string * domain: string
        /// Run time: an entity's identity is not its team's allegiance in the roster.
        | RosterMismatch of teamField: string * team: int * roster: Identity * entity: Identity

    let rec slotName (slot: Slot) =
        match slot with
        | Frame -> "Slot.Frame"
        | Fill -> "Slot.Fill (hue)"
        | FillStep -> "Slot.FillStep (lightness)"
        | SizeStep -> "Slot.SizeStep (area)"
        | Icon -> "Slot.Icon"
        | TeamMark -> "Slot.TeamMark"
        | Modifier -> "Slot.Modifier"
        | Amplifier Above -> "Slot.Amplifier Above (pips)"
        | Amplifier Below -> "Slot.Amplifier Below (bar)"
        | Amplifier Facing -> "Slot.Amplifier Facing"
        | Amplifier Corner -> "Slot.Amplifier Corner (badge)"
        | Label -> "Slot.Label (mirror label)"

    let measureName (measure: Measure) =
        match measure with
        | IdentityMeasure -> "identity"
        | Nominal _ -> "nominal"
        | Ordinal _ -> "ordinal"
        | Fraction -> "fraction"
        | Angle -> "angle"
        | Flag -> "flag"
        | Text -> "label"
        | Team _ -> "team"

    /// The slots each measure may use (the design's measure table).
    let allowedSlots (measure: Measure) : Slot list =
        match measure with
        | IdentityMeasure -> [ Frame ]
        | Nominal _ -> [ Icon; Fill ]
        | Ordinal _ -> [ Amplifier Above; SizeStep; FillStep ]
        | Fraction -> [ Amplifier Below ]
        | Angle -> [ Amplifier Facing ]
        | Flag -> [ Modifier; Amplifier Corner ]
        | Text -> [ Label ]
        | Team _ -> [ TeamMark ]

    /// The smallest frame an inside team badge fits (glyph ADR D1, measured on all five frames). STAND-IN for part 3's
    /// `Glyphs.minBadgeFrame`.
    let minBadgeFrame = Glyphs.minBadgeFrame
    /// From this size the inner box holds the icon (left) and the badge (right) (glyph ADR Q15). STAND-IN for part 3's
    /// `Glyphs.minIconAndBadgeFrame`.
    let sideBySideFrame = 40.0
    /// The minimum identity frame (glyph ADR D1). STAND-IN for part 3's `Glyphs.minIdentityFrame`.
    let minIdentityFrame = 12.0
    /// Pips: a design default from subitizing, not a measured limit (glyph ADR D12 rule 3).
    let maxPips = 5
    /// Size and lightness steps (glyph ADR D12 rule 3).
    let maxSteps = 4
    /// One seven-segment digit; team numbers never wrap (Root's #794 merge comment), so above 9 the build fails until
    /// part 3's two-digit badge (from 33 units) lands.
    let maxBadgeTeams = 9
    /// The size-step increment, in units.
    let sizeStepUnits = 4.0

    let private identityText (identity: Identity) = Themes.identityName identity

    /// What a game author changes for each error.
    let explain (error: SymbologyError) : string =
        match error with
        | HueOnlyOrderedData(field, measure) ->
            sprintf
                "%s is %s data on hue alone; hue has no order. Encode it on Slot.Amplifier Below (a bar), Slot.Amplifier Above (pips) or Slot.SizeStep."
                field
                measure
        | CategoricalHueWithoutRedundancy field ->
            sprintf
                "%s is on hue only; hue is lost in monochrome and under colour-vision deficiency. Also encode %s on Slot.Icon, or move it there."
                field
                field
        | OverCapacity(field, slot, n, capacity, Some theme) ->
            sprintf "%s has %d values but %s holds %d in theme %s. Group its values, or use Slot.Icon." field n (slotName slot) capacity theme
        | OverCapacity(field, slot, n, capacity, None) ->
            sprintf "%s has %d values but %s holds %d. Group its values, or choose a channel with more room." field n (slotName slot) capacity
        | FractionAsArea field ->
            sprintf "%s is a fraction on area; area misjudges quantities. Encode it on Slot.Amplifier Below, a bar whose length is the fraction." field
        | FlagNotSelective(field, slot) ->
            sprintf "%s is a flag on %s; flags belong on Slot.Modifier or Slot.Amplifier Corner." field (slotName slot)
        | IdentityNotOnFrame(field, slot) ->
            sprintf "%s is an identity on %s; identity is the frame's shape plus colour. Encode it on Slot.Frame." field (slotName slot)
        | IdentityBelowMinimumWithoutLabel(field, frame) ->
            sprintf
                "the frame is %g units, under the %g-unit minimum identity frame, so %s needs the mirror label. Add Encode.label, or use frames of at least %g."
                frame
                minIdentityFrame
                field
                minIdentityFrame
        | TeamWithoutIdentity field ->
            sprintf
                "%s is on Slot.TeamMark, but no field is on Slot.Frame; team is double-encoded with the allegiance frame. Add Encode.identity on Slot.Frame."
                field
        | TeamBadgeBelowMinimumWithoutLabel(field, frame, shared) ->
            sprintf
                "two or more teams of %s share the %s allegiance, so the team badge is mandatory, but a %g-unit frame is under the %g-unit badge minimum. Use frames of at least %g, give each team its own allegiance, or add Encode.label."
                field
                (identityText shared)
                frame
                minBadgeFrame
                minBadgeFrame
        | IconSlotTakenByBadge(icon, team, frame, shared) ->
            sprintf
                "%s is on Slot.Icon, but two or more teams of %s share the %s allegiance, so the mandatory team badge takes the inner centre of a %g-unit frame and the icon slot is unavailable from %g to %g units. Use frames of at least %g, give each team its own allegiance, or move %s to Slot.SizeStep (if ordered), Slot.Modifier (if a flag) or the label."
                icon
                team
                (identityText shared)
                frame
                minBadgeFrame
                (sideBySideFrame - 1.0)
                sideBySideFrame
                icon
        | SlotTaken(slot, first, second) ->
            sprintf "%s and %s are both on %s. Each slot shows one field; move %s to another slot." first second (slotName slot) second
        | NeedsStrokedProfile(field, slot) ->
            sprintf "%s is on %s, which needs the stroked profile (#773). Set Symbology.stroked, or drop the field until it lands." field (slotName slot)
        | SlotNotInProof(field, slot) ->
            sprintf "%s is on %s, which the design accepts but this proof does not draw (#789's part 3 draws it). Use Slot.Amplifier Above or Slot.SizeStep for now." field (slotName slot)
        | WrongSlot(field, measure, slot, allowed) ->
            sprintf
                "%s is %s data and %s cannot show it. Use %s."
                field
                measure
                (slotName slot)
                (allowed |> List.map slotName |> String.concat " or ")
        | EmptyDomain field -> sprintf "%s declares no values. List its cases or levels." field
        | DuplicateField field -> sprintf "%s is encoded twice on one slot. Remove one." field
        | ThemeRefuses(theme, reason) -> sprintf "theme %s cannot carry this symbology: %s" theme reason
        | ValueOutOfRange(field, value, domain) ->
            sprintf "%s is %s, outside its declared %s. Add the value to the declaration, or fix the state." field value domain
        | RosterMismatch(field, team, roster, entity) ->
            sprintf
                "%s %d is %s in the roster, but this entity's identity is %s. Make the roster match the game's teams, or fix the entity's allegiance."
                field
                team
                (identityText roster)
                (identityText entity)

    /// A built symbology: the draft that passed every rule, and the notes for fallbacks (team colour falling back to
    /// allegiance colouring above a theme's hue capacity, a hue dropped in monochrome).
    type Symbology<'Entity> = { Draft: Draft<'Entity>; Notes: string list; Shared: Identity option; Roster: Identity list option }

    /// `Symbology.create family frame`: a portable-profile draft at a frame size in units.
    let create<'Entity> (family: string) (frame: float) : Draft<'Entity> =
        { Family = family; Frame = System.Math.Floor frame; Stroked = false; Encodings = [] }

    /// Target the stroked profile (#773): unlocks Slot.Amplifier Facing.
    let stroked (draft: Draft<'Entity>) = { draft with Stroked = true }

    /// The `Encode` combinators (glyph ADR D12). Each names its field, so an error can name it too; F# cannot read an
    /// accessor's name without quotations, which Fable does not support.
    module Encode =

        let private add (encoding: Encoding<'Entity>) (draft: Draft<'Entity>) =
            { draft with Encodings = draft.Encodings @ [ encoding ] }

        let private indexIn (cases: 'v list) (value: 'v) =
            match List.tryFindIndex ((=) value) cases with
            | Some i -> i
            | None -> -1

        /// Identity (own, ally, neutral, enemy, unknown) on the frame: shape plus colour, double-encoded.
        let identity (field: string) (read: 'Entity -> Identity) (slot: Slot) (draft: Draft<'Entity>) =
            add { Field = field; Measure = IdentityMeasure; Slot = slot; Read = read >> VIdentity } draft

        /// A nominal field over its closed list of cases.
        let nominal (field: string) (read: 'Entity -> 'v) (cases: 'v list) (slot: Slot) (draft: Draft<'Entity>) =
            add { Field = field; Measure = Nominal cases.Length; Slot = slot; Read = read >> indexIn cases >> VCase } draft

        /// An ordinal field over its ordered levels, lowest first.
        let ordinal (field: string) (read: 'Entity -> 'v) (levels: 'v list) (slot: Slot) (draft: Draft<'Entity>) =
            add { Field = field; Measure = Ordinal levels.Length; Slot = slot; Read = read >> indexIn levels >> VLevel } draft

        /// A fraction, 0 to 1.
        let fraction (field: string) (read: 'Entity -> float) (slot: Slot) (draft: Draft<'Entity>) =
            add { Field = field; Measure = Fraction; Slot = slot; Read = read >> VFraction } draft

        /// An angle in radians.
        let angle (field: string) (read: 'Entity -> float) (slot: Slot) (draft: Draft<'Entity>) =
            add { Field = field; Measure = Angle; Slot = slot; Read = read >> VAngle } draft

        /// A flag.
        let flag (field: string) (read: 'Entity -> bool) (slot: Slot) (draft: Draft<'Entity>) =
            add { Field = field; Measure = Flag; Slot = slot; Read = read >> VFlag } draft

        /// Text for the accessible mirror label (no Scene text in v1, glyph ADR Q6).
        let label (field: string) (read: 'Entity -> string) (draft: Draft<'Entity>) =
            add { Field = field; Measure = Text; Slot = Label; Read = read >> VText } draft

        /// The team index (0 to roster length - 1), with the game's roster: the allegiance of each team, by index.
        /// Whether two of the game's teams share an allegiance (so the badge is mandatory) depends on the roster, never
        /// on which are visible; `spec` refuses an entity whose identity is not its team's.
        let team (field: string) (read: 'Entity -> int) (roster: Identity list) (slot: Slot) (draft: Draft<'Entity>) =
            add { Field = field; Measure = Team roster; Slot = slot; Read = read >> VTeam } draft

    /// The allegiance two or more of the roster's teams share, if any.
    let sharedAllegiance (roster: Identity list) : Identity option =
        roster |> List.countBy id |> List.tryFind (fun (_, n) -> n > 1) |> Option.map fst

    /// The smallest and largest frames a symbology can draw (its size steps).
    let frameRange (draft: Draft<'Entity>) =
        let steps =
            draft.Encodings
            |> List.tryPick (fun e -> match e.Measure, e.Slot with Ordinal n, SizeStep -> Some n | _ -> None)
            |> Option.defaultValue 1
        draft.Frame, draft.Frame + sizeStepUnits * float (max 1 steps - 1)

    /// Apply every rule of the glyph ADR's D12 against the themes the game lets the player choose. Theme-dependent
    /// capacities (hue) are checked at the SMALLEST frame the symbology can draw, against every theme whose colour
    /// carries categories; in `monochrome` hue is dropped and the redundant channel carries the field. Errors are
    /// de-duplicated.
    let build (themes: Theme list) (draft: Draft<'Entity>) : Result<Symbology<'Entity>, SymbologyError list> =
        let es = draft.Encodings
        let small, large = frameRange draft
        let hasLabel = es |> List.exists (fun e -> e.Slot = Label)
        let hasIdentity = es |> List.exists (fun e -> e.Measure = IdentityMeasure && e.Slot = Frame)
        let colourThemes = themes |> List.filter (fun t -> t.Map.ColourCarries)
        let roster = es |> List.tryPick (fun e -> match e.Measure with Team r -> Some(e.Field, r) | _ -> None)
        let shared = roster |> Option.bind (fun (_, r) -> sharedAllegiance r)
        let errors =
            [
                // Field names and slots.
                for (field, _), n in es |> List.countBy (fun e -> e.Field, e.Slot) do
                    if n > 1 then yield DuplicateField field
                let drawn = es |> List.filter (fun e -> e.Slot <> Label)
                for i in 0 .. drawn.Length - 1 do
                    for j in i + 1 .. drawn.Length - 1 do
                        if drawn.[i].Slot = drawn.[j].Slot && drawn.[i].Field <> drawn.[j].Field then
                            yield SlotTaken(drawn.[i].Slot, drawn.[i].Field, drawn.[j].Field)
                // The team ring and a categorical fill would both colour the frame's interior.
                match es |> List.tryFind (fun e -> e.Slot = Fill), es |> List.tryFind (fun e -> e.Slot = TeamMark) with
                | Some fill, Some team -> yield SlotTaken(Fill, team.Field, fill.Field)
                | _ -> ()
                for e in es do
                    let measure = measureName e.Measure
                    let wrong () = WrongSlot(e.Field, measure, e.Slot, allowedSlots e.Measure)
                    match e.Measure, e.Slot with
                    // Identity.
                    | IdentityMeasure, Frame -> if small < minIdentityFrame && not hasLabel then yield IdentityBelowMinimumWithoutLabel(e.Field, small)
                    | IdentityMeasure, slot -> yield IdentityNotOnFrame(e.Field, slot)
                    // Nominal.
                    | Nominal n, _ when n = 0 -> yield EmptyDomain e.Field
                    | Nominal n, Icon -> if n > 4 then yield OverCapacity(e.Field, Icon, n, 4, None)
                    | Nominal n, Fill ->
                        for theme in colourThemes do
                            let capacity = hueCapacity theme small
                            if n > capacity then yield OverCapacity(e.Field, Fill, n, capacity, Some theme.Name)
                    | Nominal _, _ -> yield wrong ()
                    // Ordinal.
                    | Ordinal n, _ when n = 0 -> yield EmptyDomain e.Field
                    | Ordinal _, Fill -> yield HueOnlyOrderedData(e.Field, measure)
                    | Ordinal n, Amplifier Above -> if n > maxPips then yield OverCapacity(e.Field, Amplifier Above, n, maxPips, None)
                    | Ordinal n, SizeStep -> if n > maxSteps then yield OverCapacity(e.Field, SizeStep, n, maxSteps, None)
                    | Ordinal n, FillStep ->
                        if n > maxSteps then yield OverCapacity(e.Field, FillStep, n, maxSteps, None)
                        yield SlotNotInProof(e.Field, FillStep)
                    | Ordinal _, _ -> yield wrong ()
                    // Fraction.
                    | Fraction, Fill -> yield HueOnlyOrderedData(e.Field, measure)
                    | Fraction, SizeStep -> yield FractionAsArea e.Field
                    | Fraction, Amplifier Below -> ()
                    | Fraction, _ -> yield wrong ()
                    // Angle.
                    | Angle, Fill -> yield HueOnlyOrderedData(e.Field, measure)
                    | Angle, Amplifier Facing -> if not draft.Stroked then yield NeedsStrokedProfile(e.Field, e.Slot)
                    | Angle, _ -> yield wrong ()
                    // Flag.
                    | Flag, (Modifier | Amplifier Corner) -> ()
                    | Flag, slot -> yield FlagNotSelective(e.Field, slot)
                    // Label.
                    | Text, Label -> ()
                    | Text, _ -> yield wrong ()
                    // Team.
                    | Team r, TeamMark ->
                        if r.Length > maxBadgeTeams then yield OverCapacity(e.Field, TeamMark, r.Length, maxBadgeTeams, None)
                        if not hasIdentity then yield TeamWithoutIdentity e.Field
                    | Team _, _ -> yield wrong ()
                // Rule 2: categorical hue needs a redundant non-hue channel: the same field (by name) on the icon.
                for e in es do
                    match e.Measure, e.Slot with
                    | Nominal _, Fill ->
                        let redundant = es |> List.exists (fun o -> o.Slot = Icon && o.Field = e.Field)
                        let labelled = small < minIdentityFrame && hasLabel
                        if not (redundant || labelled) then yield CategoricalHueWithoutRedundancy e.Field
                    | _ -> ()
                // The themes must carry the game's teams (those within each theme's hue capacity keep team colours).
                match roster with
                | Some(_, r) ->
                    for theme in themes do
                        for violation in validateFor (max 2 (min r.Length (hueCapacity theme small))) theme do
                            yield ThemeRefuses(theme.Name, explainViolation violation)
                | None -> ()
                // Rule 6 and the icon-slot case, for every size the symbology can reach.
                match roster, shared with
                | Some(teamField, _), Some allegiance ->
                    if small < minBadgeFrame && not hasLabel then yield TeamBadgeBelowMinimumWithoutLabel(teamField, small, allegiance)
                    match es |> List.tryFind (fun e -> e.Slot = Icon) with
                    | Some icon when large >= minBadgeFrame && small < sideBySideFrame ->
                        yield IconSlotTakenByBadge(icon.Field, teamField, small, allegiance)
                    | _ -> ()
                | _ -> ()
            ]
            |> List.distinct
        let notes =
            [
                match roster with
                | Some(field, r) ->
                    for theme in themes do
                        let capacity = hueCapacity theme small
                        if r.Length > capacity then
                            yield
                                sprintf
                                    "%s: %d teams exceed %s's hue capacity of %d at %g units; team colour falls back to allegiance colouring there, and team is read from the badge and the frame"
                                    field
                                    r.Length
                                    theme.Name
                                    capacity
                                    small
                | None -> ()
                for e in es do
                    if e.Slot = Fill && themes |> List.exists (fun t -> not t.Map.ColourCarries) then
                        yield sprintf "%s: its hue is dropped in monochrome; its icon carries it" e.Field
            ]
        match errors with
        | [] -> Ok { Draft = draft; Notes = notes; Shared = shared; Roster = roster |> Option.map snd }
        | _ -> Error errors

    // ---- The experimental glyph spec and its drawing ----------------------------------------------------------------

    /// STAND-IN for #789's opaque `GlyphSpec` (glyph ADR D1), with its builder functions' names.
    type GlyphSpec =
        {
            Family: string
            Anchor: Point
            Frame: float
            Identity: Identity option
            Icon: int option
            Team: int option
            /// The team's badge digit (team + 1), when the badge is mandatory and this glyph's frame (after its size
            /// step) reaches the badge minimum.
            Badge: int option
            FillCase: int option
            SizeStep: int
            Pips: int option
            Bar: float option
            Modifier: bool
            Corner: bool
            Label: string
        }

    /// Whether a scene in `theme` shows team colours: decided once per scene (N6), from the roster and the theme's hue
    /// capacity at the symbology's smallest frame, so a size step never mixes team and allegiance colouring.
    let teamColouring (theme: Theme) (symbology: Symbology<'Entity>) : bool =
        match symbology.Roster with
        | Some roster -> theme.Map.ColourCarries && roster.Length <= hueCapacity theme (fst (frameRange symbology.Draft))
        | None -> false

    /// What `Symbology.spec` produces for one entity, or every value that breaks its declaration: a case, level,
    /// fraction or team outside its domain, or an identity that is not the entity's team's allegiance in the roster.
    let spec (symbology: Symbology<'Entity>) (anchor: Point) (entity: 'Entity) : Result<GlyphSpec, SymbologyError list> =
        let values = symbology.Draft.Encodings |> List.map (fun e -> e, e.Read entity)
        let pick f = values |> List.tryPick f
        let errors =
            [
                for e, v in values do
                    match e.Measure, v with
                    | Nominal n, VCase c when c < 0 || c >= n -> yield ValueOutOfRange(e.Field, "not one of its cases", sprintf "%d cases" n)
                    | Ordinal n, VLevel l when l < 0 || l >= n -> yield ValueOutOfRange(e.Field, "not one of its levels", sprintf "%d levels" n)
                    | Fraction, VFraction f when not (f >= 0.0 && f <= 1.0) -> yield ValueOutOfRange(e.Field, sprintf "%g" f, "range 0 to 1")
                    | Team roster, VTeam t when t < 0 || t >= roster.Length ->
                        yield ValueOutOfRange(e.Field, sprintf "team %d" t, sprintf "roster of %d teams (team numbers never wrap)" roster.Length)
                    | Team roster, VTeam t ->
                        match pick (function { Slot = Frame }, VIdentity i -> Some i | _ -> None) with
                        | Some identity when identity <> roster.[t] -> yield RosterMismatch(e.Field, t, roster.[t], identity)
                        | _ -> ()
                    | _ -> ()
            ]
        if not errors.IsEmpty then Error errors
        else
            let team = pick (function { Slot = TeamMark }, VTeam t -> Some t | _ -> None)
            let sizeStep = pick (function { Slot = SizeStep }, VLevel l -> Some l | _ -> None) |> Option.defaultValue 0
            let frame = symbology.Draft.Frame + sizeStepUnits * float sizeStep
            let labelParts =
                values
                |> List.map (fun (e, v) ->
                    match v with
                    | VText s -> s
                    | VIdentity i -> Themes.identityName i
                    | VTeam t -> sprintf "team %d" (t + 1)
                    | VFraction f -> sprintf "%s %d%%" e.Field (int (System.Math.Floor(f * 100.0 + 0.5)))
                    | VLevel l -> sprintf "%s %d" e.Field (l + 1)
                    | VCase c -> sprintf "%s %d" e.Field (c + 1)
                    | VFlag true -> e.Field
                    | VFlag false -> ""
                    | VAngle a -> sprintf "%s %d deg" e.Field (int (System.Math.Floor(a * 180.0 / System.Math.PI + 0.5))))
                |> List.filter (fun s -> s <> "")
            Ok
                {
                    Family = symbology.Draft.Family
                    Anchor = anchor
                    Frame = frame
                    Identity = pick (function _, VIdentity i -> Some i | _ -> None)
                    Icon = pick (function { Slot = Icon }, VCase c -> Some c | _ -> None)
                    Team = team
                    Badge = (if symbology.Shared.IsSome && frame >= minBadgeFrame then team |> Option.map (fun t -> t + 1) else None)
                    FillCase = pick (function { Slot = Fill }, VCase c -> Some c | _ -> None)
                    SizeStep = sizeStep
                    Pips = pick (function { Slot = Amplifier Above }, VLevel l -> Some(l + 1) | _ -> None)
                    Bar = pick (function { Slot = Amplifier Below }, VFraction f -> Some f | _ -> None)
                    Modifier = pick (function { Slot = Modifier }, VFlag b -> Some b | _ -> None) |> Option.defaultValue false
                    Corner = pick (function { Slot = Amplifier Corner }, VFlag b -> Some b | _ -> None) |> Option.defaultValue false
                    Label = String.concat ", " labelParts
                }

    /// The mirror label of an entity (glyph ADR D1: the label names identity and team in every case).
    let label (symbology: Symbology<'Entity>) (entity: 'Entity) : Result<string, SymbologyError list> =
        spec symbology { X = 0.0; Y = 0.0 } entity |> Result.map (fun s -> s.Label)

    let private box (x: float) (y: float) (w: float) (h: float) (colour: Color) = SceneNode.Rectangle((x, y, w, h), colour)

    /// The proof's icon set: four icons on the badge's 3 x 5 grid of 2-unit cells, drawn from bars (glyph ADR Q13:
    /// icons from primitives first). The theme resolves `IconRef`s to these in #789's design.
    let iconCells (icon: int) : (float * float * float * float) list =
        match icon % 4 with
        | 0 -> [ 0.0, 0.0, 1.0, 5.0; 1.0, 0.0, 2.0, 2.0 ] // flag: goal
        | 1 -> [ 1.0, 0.0, 1.0, 3.0; 1.0, 4.0, 1.0, 1.0 ] // exclamation: hazard
        | 2 -> [ 1.0, 1.0, 1.0, 3.0; 0.0, 2.0, 3.0, 1.0 ] // plus: pickup
        | _ -> [ 0.0, 0.0, 3.0, 1.0; 0.0, 4.0, 3.0, 1.0; 0.0, 1.0, 1.0, 3.0; 2.0, 1.0, 1.0, 3.0 ] // ring: spawn

    let private icon (index: int) (topLeft: Point) (ink: Color) =
        iconCells index |> List.map (fun (c, r, w, h) -> box (topLeft.X + 2.0 * c) (topLeft.Y + 2.0 * r) (2.0 * w) (2.0 * h) ink)

    /// The cue reserve around a frame, in units (glyph ADR D10's defaults: band 2, gap 2, bracket stroke 2).
    let reserve = 8.0

    /// A drawn glyph, in the glyph ADR's passes (D10): amplifiers, the body (frame, keyline, ring, body fill, then
    /// badge or icon), brackets.
    type Drawing = { Amplifiers: SceneNode list; Body: SceneNode list; Brackets: SceneNode list; Frame: Rect }

    /// Draw one spec, portable profile (bars only), with erratum E1's layout: from the edge in, the identity frame in
    /// its identity colour (band `max 1 (floor (w / 8))`), a 1-unit keyline in the body colour, a 2-unit ring in the
    /// team colour (the allegiance colour when the scene's `teamColours` is false), or in the categorical fill hue,
    /// then the body fill (no ring without a team or a fill); the badge or icon in their ink on top. Each layer is the same identity frame, smaller. STAND-IN for part 3's
    /// `Glyphs.draw`.
    let draw (theme: Theme) (teamColours: bool) (spec: GlyphSpec) (selected: bool) : Drawing =
        let identity = spec.Identity |> Option.defaultValue Glyphs.Neutral
        let w = System.Math.Floor spec.Frame
        let band = max 1.0 (System.Math.Floor(w / 8.0))
        let identityColour = Themes.allegiance theme identity
        let body = colour theme "glyph.unit.body"
        let layer size c = Glyphs.identityFrame identity spec.Anchor size c
        let frameNodes = layer w identityColour
        let frame = Glyphs.boundsOf frameNodes
        // The ring carries the team, or a categorical fill hue (the team colours are the category range), on the body.
        let ringColour =
            match spec.Team, spec.FillCase with
            | Some t, _ -> Some(if teamColours then Themes.team theme t else identityColour)
            | None, Some c -> Some(if theme.Map.ColourCarries then Themes.team theme c else identityColour)
            | None, None -> None
        let rings =
            match ringColour with
            | Some ring -> layer (w - 2.0 * band) body @ layer (w - 2.0 * (band + 1.0)) ring @ layer (w - 2.0 * (band + 3.0)) body
            | None -> layer (w - 2.0 * band) body
        let cx, cy = Glyphs.snap spec.Anchor.X, Glyphs.snap spec.Anchor.Y
        let ink = colour theme "glyph.badge.ink"
        let inner =
            match spec.Badge, spec.Icon with
            | Some digit, Some i when w >= sideBySideFrame ->
                icon i { X = cx - 8.0; Y = cy - 5.0 } (colour theme "glyph.icon.ink")
                @ Glyphs.badge digit { X = cx + 2.0; Y = cy - 5.0 } 2.0 ink
            | Some digit, _ -> Glyphs.badge digit { X = cx - 3.0; Y = cy - 5.0 } 2.0 ink
            | None, Some i -> icon i { X = cx - 3.0; Y = cy - 5.0 } (colour theme "glyph.icon.ink")
            | None, None -> []
        // Amplifiers sit on the ground, so they take `status.keyline` (the body colour, held to the ground floor).
        let mark = colour theme "status.keyline"
        let amplifiers =
            [
                match spec.Pips with
                | Some n ->
                    let width = float n * 4.0 - 1.0
                    let x0 = cx - System.Math.Floor(width / 2.0)
                    for i in 0 .. n - 1 do
                        yield box (x0 + 4.0 * float i) (frame.Y - reserve - 4.0) 3.0 3.0 mark
                | None -> ()
                match spec.Bar with
                | Some f ->
                    let f = f |> max 0.0 |> min 1.0
                    let bw = max 16.0 frame.Width
                    let x = cx - System.Math.Floor(bw / 2.0)
                    let y = frame.Y + frame.Height + reserve
                    let status = if f > 0.6 then "status.good" elif f > 0.3 then "status.warn" else "status.bad"
                    yield box x y bw 6.0 (colour theme "status.keyline")
                    yield box (x + 1.0) (y + 1.0) (bw - 2.0) 4.0 (colour theme "status.track")
                    yield box (x + 1.0) (y + 1.0) (System.Math.Floor((bw - 2.0) * f)) 4.0 (colour theme status)
                | None -> ()
                if spec.Corner then
                    yield box (frame.X + frame.Width + reserve) (frame.Y - reserve - 4.0) 4.0 4.0 mark
                if spec.Modifier then
                    yield box (frame.X - reserve - 4.0) (frame.Y - reserve - 4.0) 4.0 4.0 mark
            ]
        let brackets =
            if not selected then []
            else
                let s = 2.0
                let l, t = frame.X - reserve, frame.Y - reserve
                let r, b = frame.X + frame.Width + reserve - s, frame.Y + frame.Height + reserve - s
                let arm = 6.0
                let c = colour theme "glyph.selection"
                [
                    box l t arm s c
                    box l t s arm c
                    box (r + s - arm) t arm s c
                    box r t s arm c
                    box l b arm s c
                    box l (b + s - arm) s arm c
                    box (r + s - arm) b arm s c
                    box r (b + s - arm) s arm c
                ]
        { Amplifiers = amplifiers; Body = frameNodes @ rings @ inner; Brackets = brackets; Frame = frame }

    /// A scene of drawings on the theme's ground, in passes: every amplifier, then every body, then every bracket.
    let scene (theme: Theme) (width: float) (height: float) (drawings: Drawing list) : Scene =
        { Nodes =
            SceneNode.Rectangle((0.0, 0.0, width, height), colour theme "surface.ground")
            :: (drawings |> List.collect (fun d -> d.Amplifiers))
            @ (drawings |> List.collect (fun d -> d.Body))
            @ (drawings |> List.collect (fun d -> d.Brackets)) }

/// EXPERIMENTAL worked example (FS.GG.Game#795): a symbology generated from a sample game-state type modelled on the
/// template's arena (`fs-gg-fable-game`: players in cells, a collectible, a hazard, a goal, health and score), rendered
/// in every preset. The view reads the theme through a reader, so a switch keeps the game state.
module ArenaExample =

    open Symbology

    type Role =
        | Runner
        | Guard
        | Scout

    type Rank =
        | Rookie
        | Veteran
        | Elite

    /// A player as the arena's state holds it.
    type Player =
        {
            Name: string
            Cell: int * int
            Allegiance: Glyphs.Identity
            Team: int
            Role: Role
            Health: float
            Rank: Rank
            Carrying: bool
        }

    type FeatureKind =
        | Goal
        | Hazard
        | Pickup
        | Spawn

    /// A fixed arena feature.
    type Feature = { Kind: FeatureKind; Cell: int * int }

    /// The game's state: the server-authoritative arena plus the client's selection. The theme is not in it.
    type ArenaState = { Players: Player list; Features: Feature list; Selected: string option }

    /// The roster: team 0 is this client's (own); the other three teams are opponents and share the enemy allegiance, so
    /// the team badge is mandatory (glyph ADR D1).
    let roster = [ Glyphs.Own; Glyphs.Enemy; Glyphs.Enemy; Glyphs.Enemy ]

    /// The players' symbology, as a game author writes it.
    let playerDraft: Draft<Player> =
        Symbology.create<Player> "unit" 24.0
        |> Encode.identity "allegiance" (fun p -> p.Allegiance) Frame
        |> Encode.team "team" (fun p -> p.Team) roster TeamMark
        |> Encode.fraction "health" (fun p -> p.Health) (Amplifier Below)
        |> Encode.ordinal "rank" (fun p -> p.Rank) [ Rookie; Veteran; Elite ] (Amplifier Above)
        |> Encode.flag "carrying" (fun p -> p.Carrying) (Amplifier Corner)
        |> Encode.label "name" (fun p -> p.Name)

    /// The same author's first attempt, which also puts the role on the icon: the build refuses it, because the
    /// mandatory badge takes the inner centre of a 24-unit frame (Root's #794 merge comment).
    let playerDraftWithRoleIcon: Draft<Player> =
        playerDraft |> Encode.nominal "role" (fun p -> p.Role) [ Runner; Guard; Scout ] Icon

    /// The features' symbology: a neutral structure frame with the kind on the icon.
    let featureDraft: Draft<Feature> =
        Symbology.create<Feature> "structure" 24.0
        |> Encode.identity "allegiance" (fun _ -> Glyphs.Neutral) Frame
        |> Encode.nominal "kind" (fun f -> f.Kind) [ Goal; Hazard; Pickup; Spawn ] Icon

    let tile = 48.0
    let columns = 6
    let rows = 3
    let width = tile * float columns
    let height = tile * float rows

    /// A sample state: four teams, every identity the roster uses, three ranks, health from low to full.
    let sample: ArenaState =
        {
            Players =
                [
                    { Name = "Ada"; Cell = 0, 0; Allegiance = Glyphs.Own; Team = 0; Role = Runner; Health = 0.9; Rank = Elite; Carrying = true }
                    { Name = "Bo"; Cell = 2, 0; Allegiance = Glyphs.Enemy; Team = 1; Role = Guard; Health = 0.5; Rank = Veteran; Carrying = false }
                    { Name = "Cy"; Cell = 4, 0; Allegiance = Glyphs.Enemy; Team = 2; Role = Scout; Health = 0.2; Rank = Rookie; Carrying = false }
                    { Name = "Di"; Cell = 1, 1; Allegiance = Glyphs.Enemy; Team = 3; Role = Runner; Health = 1.0; Rank = Veteran; Carrying = true }
                    { Name = "Ed"; Cell = 3, 1; Allegiance = Glyphs.Enemy; Team = 1; Role = Guard; Health = 0.7; Rank = Rookie; Carrying = false }
                ]
            Features = [ { Kind = Goal; Cell = 5, 1 }; { Kind = Hazard; Cell = 0, 2 }; { Kind = Pickup; Cell = 2, 2 }; { Kind = Spawn; Cell = 4, 2 } ]
            Selected = Some "Bo"
        }

    let private centre (col: int, row: int) = { X = tile * float col + tile / 2.0; Y = tile * float row + tile / 2.0 + 2.0 }

    let private unwrap (result: Result<'T, SymbologyError list>) =
        match result with
        | Ok s -> s
        | Error errors -> failwithf "symbology refused: %s" (errors |> List.map explain |> String.concat "; ")

    /// The game's view: the state and a theme reader in, a Scene out. The symbologies are built once against every
    /// preset the player may choose; whether team colours show is decided once per scene.
    let view (themes: Theme list) : (unit -> Theme) -> ArenaState -> Scene =
        let players = Symbology.build themes playerDraft |> unwrap
        let features = Symbology.build themes featureDraft |> unwrap
        fun read state ->
            let theme = read ()
            let teamColours = Symbology.teamColouring theme players
            let drawings =
                [
                    for f in state.Features -> Symbology.draw theme false (Symbology.spec features (centre f.Cell) f |> unwrap) false
                    for p in state.Players ->
                        Symbology.draw theme teamColours (Symbology.spec players (centre p.Cell) p |> unwrap) (state.Selected = Some p.Name)
                ]
            Symbology.scene theme width height drawings

    /// The mirror labels of the sample (the accessible names a game's mirror would show).
    let labels (themes: Theme list) (state: ArenaState) : string list =
        let players = Symbology.build themes playerDraft |> unwrap
        state.Players |> List.map (Symbology.label players >> unwrap)
