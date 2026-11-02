namespace FS.GG.Game.Reference.Experimental

open FS.GG.UI.Scene
open FS.GG.Game.Reference.Experimental.Colour

/// EXPERIMENTAL (FS.GG.Game#795, GAME-RENDER-02.8): theme derivation for the glyph layer, as the design
/// `docs/roadmaps/game-render-02-themes.md` proposes.
///
/// Ownership (Root's ruling on #789): #802 owns `Theme`, `ThemeSeed`, `ThemeTokens`, `ThemeAlgorithm`, `Theme.create`,
/// `Theme.validate [ThemeCheck]`, `ThemeViolation`, `withOverrides`/`withTeamColour`, DTCG and CSS export, and
/// `ThemeColour`. #795 supplies `ThemeAlgorithm`s, `ThemeCheck`s and the preset seeds. This module therefore has two
/// parts:
/// - **STAND-INS for #802, replaced by #802's types on merge:** `Seed` (`ThemeSeed` plus algorithm parameters),
///   `MapTokens` (`ThemeTokens`), `Algorithm` (`ThemeAlgorithm`), `Theme`, `ThemeViolation`, `builtInRules` (#802's
///   built-in validation), `create`, `overrideTeam` (`Theme.withTeamColour`), `hexes`, `cssVariables`, `toDtcg`,
///   `ofDtcg` and `resolverDocument`. The alias names are #802's 35 colour aliases.
/// - **#795's own work, which becomes `ThemeAlgorithm`s, `ThemeCheck`s and preset seeds on #802:** the Leonardo-style
///   ramps and team bands, the algorithms `base`, `dark`, `light`, `highContrast`, `colourBlindSafe`, `monochrome` and
///   `repair`, the distinctness, colour-vision, lightness-gap, status-meaning and cue checks, the measured hue
///   capacity, and the five preset seeds. Root's ruling: this derivation is the source of truth for every preset,
///   `classic` included.
module Themes =

    /// The five identities: #802's `[<RequireQualifiedAccess>] Identity`.
    type Identity = FS.GG.Game.Render.Identity

    /// All five identities, in ADR D1's order.
    let identities: Identity list = [ Identity.Own; Identity.Ally; Identity.Neutral; Identity.Enemy; Identity.Unknown ]

    // =================================================================================================================
    // STAND-INS for #802 (replaced on merge)
    // =================================================================================================================

    /// STAND-IN for #802's `ThemeSeed` plus #795's algorithm parameters (#802 keeps `ThemeSeed` closed; per-band ratios
    /// and chroma are algorithm parameters, closed over by `ThemeAlgorithm.create`).
    type Seed =
        {
            Name: string
            /// `surface.ground`.
            Ground: Color
            /// The key chroma of hued tokens (OKLCH C): an algorithm parameter on #802.
            Chroma: float
            /// Allegiance hues (OKLCH h), in `identities` order: own, ally, neutral, enemy, unknown.
            AllegianceHues: float list
            /// Team key hues, in team order. The first two are the two-team default (never red against green).
            TeamHues: float list
            SelectionHue: float
            MarkerHue: float
            /// Status hues: good, warn, bad.
            StatusHues: float list
            /// `contrastTarget`: the floor against the ground, 3 (WCAG 2 SC 1.4.11) or 7 for `highContrast`
            /// (provisional).
            ContrastTarget: float
            Cell: float
            StrokeBase: float
            Gap: float
            /// The default identity-frame size in scene units.
            Frame: float
        }

    /// STAND-IN for #802's `ThemeTokens`: the parameters #795's algorithms refine, and the ramps derived from them.
    type MapTokens =
        {
            /// Lighter: hued tokens are lighter than the ground (a dark theme). Darker: a light theme.
            Polarity: Polarity
            Ground: Color
            /// `glyph.unit.body` (and `status.keyline`): white on a dark ground, near-black on a light one, as #802's
            /// standard algorithm derives it.
            Body: Color
            /// The ink drawn on fills: `glyph.badge.ink`, `glyph.icon.ink`, `glyph.modifier`, `status.track` and
            /// `cue.focus.inner`.
            OnFill: Color
            Floor: float
            Chroma: float
            AllegianceHues: float list
            TeamHues: float list
            SelectionHue: float
            MarkerHue: float
            HoverHue: float
            StatusHues: float list
            /// Twelve target contrast ratios against the ground, one per ramp step (Radix-style step jobs: 1-2 ground
            /// tints, 3-5 states, 6-8 borders, 9-12 solid fills and marks).
            StepRatios: float list
            /// Team N is solved for the contrast ratio `TeamBands.[N % length]` against the ground: alternating light
            /// and dark bands (Game Accessibility Guidelines: a light/dark split for team colours), inside the window
            /// the body leaves (E1: the ring sits on the body).
            TeamBands: float list
            AllegianceSteps: int list
            /// Status steps: good, warn, bad.
            StatusSteps: int list
            SelectionStep: int
            MarkerStep: int
            HoverStep: int
            /// False for `monochrome`: colour carries no category, hue capacity is 0, and every hued alias is a grey.
            ColourCarries: bool
            /// The hue ceiling per size bucket: frames under 24 units, 24 to 39, 40 and over.
            HueCeiling: int list
            Stroke: float
            Gap: float
            Cell: float
            Frame: float
            /// Derived: ramp name -> 12 colours; team name -> its solved colour.
            Ramps: Map<string, Color list>
            Teams: Color list
            /// The algorithms applied, in order.
            Applied: string list
        }

    /// STAND-IN for #802's `ThemeAlgorithm` (`ThemeAlgorithm.create name refine`).
    type Algorithm = { Name: string; Apply: Seed -> MapTokens -> MapTokens }

    /// The most team aliases a theme carries.
    let maxTeams = 8

    let identityName (identity: Identity) =
        match identity with
        | Identity.Own -> "own"
        | Identity.Ally -> "ally"
        | Identity.Neutral -> "neutral"
        | Identity.Enemy -> "enemy"
        | Identity.Unknown -> "unknown"

    /// #802's 35 colour aliases, in #802's order (`Theme.aliases`, without its five number aliases). Glyph code reads
    /// only these.
    let aliasNames: string list =
        [
            yield "surface.ground"
            yield "glyph.unit.body"
            for identity in identities do
                yield "glyph.allegiance." + identityName identity
            for team in 0 .. maxTeams - 1 do
                yield sprintf "glyph.team.%d" team
            yield! [ "glyph.badge.ink"; "glyph.icon.ink"; "glyph.modifier"; "glyph.selection"; "glyph.marker"; "glyph.path" ]
            yield! [ "glyph.stale"; "cue.focus.outer"; "cue.focus.inner"; "cue.hover" ]
            yield! [ "status.good"; "status.warn"; "status.bad"; "status.track"; "status.keyline" ]
            yield! [ "terrain.open"; "terrain.blocked"; "terrain.difficult"; "terrain.water"; "terrain.void" ]
        ]

    /// STAND-IN for #802's `Theme`, plus #795's measured hue capacity (which #802 reads from the seed's
    /// `hueCapacity.N`, checked by #795's capacity check).
    type Theme =
        {
            Name: string
            Seed: Seed
            Map: MapTokens
            Aliases: Map<string, Color>
            Overrides: Map<string, Color>
            /// The measured hue capacity per size bucket (under 24 units, 24-39, 40 and over).
            Capacity: int list
        }

    /// A broken rule, named so a designer knows what to change. STAND-IN for #802's `ThemeViolation` (token and
    /// reason); `explainViolation` is the reason text.
    type ThemeViolation =
        | NotOpaque of alias: string
        | UnknownAlias of alias: string
        | BelowFloor of alias: string * against: string * ratio: float * floor: float
        | NotDistinct of a: string * b: string * vision: Vision * deltaE: float * threshold: float
        | LightnessTooClose of a: string * b: string * vision: Vision * gap: float * minimum: float
        | StatusMeaningLost of alias: string * hue: float * band: string
        | TeamOutsideGame of team: int * teams: int
        | BadToken of alias: string * reason: string

    let private visionName (vision: Vision) =
        match vision with
        | Normal -> "normal vision"
        | Protan -> "protanopia (Machado 1.0)"
        | Deutan -> "deuteranopia (Machado 1.0)"
        | Tritan -> "tritanopia (Machado 1.0)"

    let explainViolation (violation: ThemeViolation) : string =
        match violation with
        | NotOpaque alias -> sprintf "%s has alpha below 255; tokens are opaque until alpha is supported (glyph ADR Q5)" alias
        | UnknownAlias alias -> sprintf "%s is not in the closed alias set; use one of the names in Themes.aliasNames" alias
        | BelowFloor(alias, against, ratio, floor) ->
            sprintf "%s is %.2f:1 against %s, under the %g:1 floor; move its lightness away from %s" alias ratio against floor against
        | NotDistinct(a, b, vision, dE, threshold) ->
            sprintf "%s and %s differ by deltaE2000 %.1f under %s, under %g; change one's hue or lightness band" a b dE (visionName vision) threshold
        | LightnessTooClose(a, b, vision, gap, minimum) ->
            sprintf "%s and %s differ by %.3f in OKLab L under %s, under %g; put them in different lightness bands" a b gap (visionName vision) minimum
        | StatusMeaningLost(alias, hue, band) ->
            sprintf "%s has hue %.0f, outside its %s band, so it no longer reads as its status; keep its hue in the band" alias hue band
        | TeamOutsideGame(team, teams) ->
            sprintf "team %d is not one of the game's %d teams; only an existing team's colour can be set" team teams
        | BadToken(alias, reason) -> sprintf "%s: %s" alias reason

    let colour (theme: Theme) (alias: string) : Color =
        match theme.Aliases.TryFind alias with
        | Some c -> c
        | None -> theme.Map.Ground

    let team (theme: Theme) (index: int) = colour theme (sprintf "glyph.team.%d" index)
    let allegiance (theme: Theme) (identity: Identity) = colour theme ("glyph.allegiance." + identityName identity)

    let private teamAliases n = [ for i in 0 .. n - 1 -> sprintf "glyph.team.%d" i ]
    let private allegianceAliases = identities |> List.map (fun i -> "glyph.allegiance." + identityName i)
    let private statusAliases = [ "status.good"; "status.warn"; "status.bad" ]

    /// STAND-IN for #802's built-in rules (Theme.fsi at 9de9044e, `Theme.create`), applied to every team the theme
    /// carries, with ONE stated deviation (amendment A1, Q-T11): team colours are held to 3:1 against the ground, not to
    /// `contrastTarget`, because under erratum E1 the team ring sits between a body-colour keyline and the body fill and
    /// never touches the ground. With a 7:1 floor and #802's 3:1 ring-on-body rule, no team colour satisfies both except
    /// luminance 0.30 exactly (black ground, white body). At a 3:1 floor A1 is #802's rule unchanged.
    let builtInRules (theme: Theme) : ThemeViolation list =
        let floor = theme.Map.Floor
        let c = colour theme
        let below (floor: float) alias against =
            let ratio = contrast (c alias) (c against)
            if ratio < floor then [ BelowFloor(alias, against, ratio, floor) ] else []
        let teams = teamAliases maxTeams
        [
            for alias in aliasNames do
                if (c alias).Alpha <> 255uy then yield NotOpaque alias
            for alias in theme.Overrides |> Map.toList |> List.map fst do
                if not (List.contains alias aliasNames) then yield UnknownAlias alias
            for alias in
                allegianceAliases
                @ [ "glyph.unit.body"; "glyph.stale"; "glyph.selection"; "glyph.marker"; "glyph.path"; "cue.focus.outer"; "cue.hover"; "status.keyline" ] do
                yield! below floor alias "surface.ground"
            for alias in teams do
                yield! below 3.0 alias "surface.ground"
            yield! below 3.0 "cue.focus.inner" "cue.focus.outer"
            for fill in "glyph.unit.body" :: teams @ allegianceAliases do
                yield! below 3.0 "glyph.badge.ink" fill
                yield! below 3.0 "glyph.modifier" fill
            for fill in "glyph.unit.body" :: allegianceAliases do
                yield! below 3.0 "glyph.icon.ink" fill
            for alias in teams do
                yield! below 3.0 alias "glyph.unit.body"
            for status in statusAliases do
                yield! below 3.0 status "status.track"
            yield! below 3.0 "status.track" "status.keyline"
        ]

    // =================================================================================================================
    // #795: ramps, algorithms, checks, capacity, presets
    // =================================================================================================================

    /// The provisional thresholds (glyph ADR Q11; calibrated as the design's Q-T1 states).
    type Thresholds =
        {
            /// CIEDE2000 between two colours of one category, normal vision.
            Normal: float
            /// CIEDE2000 under Machado protan, deutan and tritan simulation, severity 1.0.
            Simulated: float
            /// The OKLab lightness gap between the first two teams under every simulation (glyph ADR D12 as amended
            /// by Root: required for the first two teams only).
            TeamLightnessGap: float
        }

    let thresholds: Thresholds = { Normal = 12.0; Simulated = 8.0; TeamLightnessGap = 0.08 }

    /// Status hue bands (OKLCH h, degrees, with a minimum chroma), so `repair` and an override keep status meaning:
    /// bad stays red, warn stays amber, good stays green (to bluish green, for Okabe-Ito).
    let statusBands: (string * string * float * float) list =
        [ "status.good", "green", 120.0, 200.0; "status.warn", "amber", 50.0, 110.0; "status.bad", "red", -20.0, 49.0 ]

    let private inBand (lo: float) (hi: float) (h: float) =
        let h' = if h > 180.0 && lo < 0.0 then h - 360.0 else h
        h' >= lo && h' <= hi

    /// Does `colour` read as the status `alias`: its hue in the band and chroma at least 0.08, so not a pastel or a grey?
    let statusReads (alias: string) (c: Color) : bool =
        match statusBands |> List.tryFind (fun (a, _, _, _) -> a = alias) with
        | Some(_, _, lo, hi) ->
            let lch = inputOf c
            lch.C >= 0.08 && inBand lo hi lch.H
        | None -> true

    /// Amber reads lighter than red: warn's OKLab L is at least 0.05 above bad's (Okabe-Ito's orange is lighter than
    /// their vermillion too).
    let statusOrdered (warn: Color) (bad: Color) : bool = (inputOf warn).L >= (inputOf bad).L + 0.05

    let private darkRatios = [ 1.08; 1.18; 1.32; 1.5; 1.75; 2.1; 2.6; 3.2; 4.2; 5.6; 7.5; 11.0 ]
    let private highContrastRatios = [ 1.2; 1.5; 2.0; 2.6; 3.4; 4.4; 5.6; 7.2; 8.5; 10.5; 13.0; 16.0 ]

    /// The candidate team-band ratios against the ground that `repair` chooses from.
    let teamBandCandidates = [ 3.02; 3.1; 3.2; 3.4; 3.7; 4.0; 4.4; 4.8; 4.9; 4.95; 5.3; 5.9; 6.6 ]

    let private groundHue (map: MapTokens) = (inputOf map.Ground).H

    let private extreme (map: MapTokens) = match map.Polarity with Lighter -> rgb 255 255 255 | Darker -> rgb 0 0 0

    /// One 12-step ramp of hue `h` at chroma `c`: each step is the colour nearest the ground's lightness whose contrast
    /// against the ground meets the step's ratio (Leonardo's idea). A step no lightness reaches takes the extreme, and
    /// validation reports it if an alias uses it.
    let private ramp (map: MapTokens) (c: float) (h: float) : Color list =
        map.StepRatios |> List.map (fun ratio -> solve map.Ground map.Polarity ratio c h |> Option.defaultValue (extreme map))

    let private teamColour (map: MapTokens) (bands: float list) (i: int) =
        let c = if map.ColourCarries then map.Chroma else 0.0
        let h = map.TeamHues.[i % map.TeamHues.Length]
        solve map.Ground map.Polarity bands.[i % bands.Length] c h |> Option.defaultValue (extreme map)

    /// Recompute every ramp and team colour from the map's parameters. Algorithms call this after they change
    /// parameters.
    let rederive (map: MapTokens) : MapTokens =
        let c = if map.ColourCarries then map.Chroma else 0.0
        let named =
            [
                yield "grey", ramp map 0.0 (groundHue map)
                for identity, h in List.zip identities map.AllegianceHues do
                    yield identityName identity, ramp map c h
                yield "selection", ramp map c map.SelectionHue
                yield "marker", ramp map c map.MarkerHue
                yield "hover", ramp map c map.HoverHue
                for name, h in List.zip [ "good"; "warn"; "bad" ] map.StatusHues do
                    yield name, ramp map c h
                yield "difficult", ramp map (if map.ColourCarries then 0.05 else 0.0) 70.0
                yield "water", ramp map (if map.ColourCarries then 0.06 else 0.0) 240.0
            ]
        { map with Ramps = Map.ofList named; Teams = [ for i in 0 .. maxTeams - 1 -> teamColour map map.TeamBands i ] }

    let private applied name (map: MapTokens) = { map with Applied = map.Applied @ [ name ] }

    let private poles (polarity: Polarity) =
        match polarity with
        | Lighter -> rgb 255 255 255, rgb 13 13 13
        | Darker -> rgb 13 13 13, rgb 255 255 255

    /// `base`: the map a seed gives, dark polarity when the ground's OKLCH L is under 0.6.
    let baseAlgorithm: Algorithm =
        {
            Name = "base"
            Apply =
                fun seed _ ->
                    let polarity = if (inputOf seed.Ground).L < 0.6 then Lighter else Darker
                    let body, onFill = poles polarity
                    rederive
                        {
                            Polarity = polarity
                            Ground = seed.Ground
                            Body = body
                            OnFill = onFill
                            Floor = seed.ContrastTarget
                            Chroma = seed.Chroma
                            AllegianceHues = seed.AllegianceHues
                            TeamHues = seed.TeamHues
                            SelectionHue = seed.SelectionHue
                            MarkerHue = seed.MarkerHue
                            HoverHue = seed.MarkerHue
                            StatusHues = seed.StatusHues
                            StepRatios = darkRatios
                            TeamBands = [ 3.2; 4.8 ]
                            AllegianceSteps = [ 10; 12; 9; 9; 11 ]
                            StatusSteps = [ 10; 12; 9 ]
                            SelectionStep = 12
                            MarkerStep = 11
                            HoverStep = 10
                            ColourCarries = true
                            HueCeiling = [ 6; 7; 8 ]
                            Stroke = seed.StrokeBase
                            Gap = seed.Gap
                            Cell = seed.Cell
                            Frame = seed.Frame
                            Ramps = Map.empty
                            Teams = []
                            Applied = [ "base" ]
                        }
        }

    /// `dark`: a dark ground (the seed's own, if already dark), a white body, near-black ink on fills.
    let dark: Algorithm =
        {
            Name = "dark"
            Apply =
                fun seed map ->
                    let g = inputOf seed.Ground
                    let ground = if g.L < 0.6 then seed.Ground else ofOklch { L = 0.29; C = min g.C 0.02; H = g.H }
                    let body, onFill = poles Lighter
                    { map with Polarity = Lighter; Ground = ground; Body = body; OnFill = onFill } |> rederive |> applied "dark"
        }

    /// `light`: a near-white ground of the seed's hue, a near-black body, white ink on fills (FourD's look).
    let light: Algorithm =
        {
            Name = "light"
            Apply =
                fun seed map ->
                    let g = inputOf seed.Ground
                    let body, onFill = poles Darker
                    { map with
                        Polarity = Darker
                        Ground = ofOklch { L = 0.97; C = min g.C 0.01; H = g.H }
                        Body = body
                        OnFill = onFill }
                    |> rederive
                    |> applied "light"
        }

    /// `highContrast`: a black (or white) ground, the 7:1 floor (provisional), steeper ramps, thicker strokes and gaps.
    let highContrast: Algorithm =
        {
            Name = "highContrast"
            Apply =
                fun _ map ->
                    let ground = match map.Polarity with Lighter -> rgb 0 0 0 | Darker -> rgb 255 255 255
                    { map with Ground = ground; Floor = 7.0; StepRatios = highContrastRatios; HueCeiling = [ 4; 5; 6 ]; Stroke = map.Stroke + 1.0; Gap = map.Gap + 1.0 }
                    |> rederive
                    |> applied "highContrast"
        }

    /// Okabe and Ito's colour-universal-design palette (2008), as published sRGB values: orange, sky blue, bluish green,
    /// yellow, blue, vermillion, reddish purple. Only their hue angles are used; lightness is solved per ground.
    let okabeIto: Color list =
        [ rgb 230 159 0; rgb 86 180 233; rgb 0 158 115; rgb 240 228 66; rgb 0 114 178; rgb 213 94 0; rgb 204 121 167 ]

    /// `colourBlindSafe`: Okabe-Ito hue angles for teams, allegiances, status, selection and marker.
    let colourBlindSafe: Algorithm =
        {
            Name = "colourBlindSafe"
            Apply =
                fun _ map ->
                    let hue i = (inputOf okabeIto.[i]).H
                    let orange, sky, green, yellow, blue, vermillion, purple = hue 0, hue 1, hue 2, hue 3, hue 4, hue 5, hue 6
                    { map with
                        TeamHues = [ blue; orange; green; purple; sky; vermillion; yellow ]
                        AllegianceHues = [ sky; green; yellow; vermillion; purple ]
                        StatusHues = [ green; orange; vermillion ]
                        SelectionHue = yellow
                        MarkerHue = sky
                        HoverHue = sky
                        HueCeiling = [ 4; 5; 6 ] }
                    |> rederive
                    |> applied "colourBlindSafe"
        }

    /// `monochrome`: every hue removed (milsymbol's monoColor). Colour carries no category, so identity survives
    /// through frame shape alone and team through the badge; it is also the redundancy test (glyph ADR D4).
    let monochrome: Algorithm =
        {
            Name = "monochrome"
            Apply =
                fun _ map ->
                    { map with ColourCarries = false; HueCeiling = [ 0; 0; 0 ]; TeamBands = [ 3.2 ]; AllegianceSteps = [ 11; 11; 11; 11; 11 ]; StatusSteps = [ 12; 12; 12 ] }
                    |> rederive
                    |> applied "monochrome"
        }

    let private step (map: MapTokens) (rampName: string) (n: int) : Color =
        match map.Ramps.TryFind rampName with
        | Some colours -> colours.[(max 1 (min 12 n)) - 1]
        | None -> map.Ground

    /// The first grey step at or past `ratio` against the ground.
    let private greyAt (map: MapTokens) (ratio: float) =
        let i = map.StepRatios |> List.tryFindIndex (fun r -> r >= ratio) |> Option.defaultValue 11
        step map "grey" (i + 1)

    let private resolveAliases (map: MapTokens) : Map<string, Color> =
        let g = inputOf map.Ground
        let void' = match map.Polarity with Lighter -> ofOklch { L = max 0.0 (g.L - 0.06); C = 0.0; H = 0.0 } | Darker -> ofOklch { L = g.L - 0.12; C = 0.0; H = 0.0 }
        [
            yield "surface.ground", map.Ground
            yield "glyph.unit.body", map.Body
            for i, identity in List.indexed identities do
                yield "glyph.allegiance." + identityName identity, step map (identityName identity) map.AllegianceSteps.[i]
            for team in 0 .. maxTeams - 1 do
                yield sprintf "glyph.team.%d" team, map.Teams.[team]
            yield "glyph.badge.ink", map.OnFill
            yield "glyph.icon.ink", map.OnFill
            yield "glyph.modifier", map.OnFill
            yield "glyph.selection", step map "selection" map.SelectionStep
            yield "glyph.marker", step map "marker" map.MarkerStep
            yield "glyph.path", step map "marker" map.MarkerStep
            yield "glyph.stale", greyAt map (map.Floor + 0.5)
            yield "cue.focus.outer", step map "grey" 12
            yield "cue.focus.inner", map.OnFill
            yield "cue.hover", step map "hover" map.HoverStep
            for i, name in List.indexed [ "good"; "warn"; "bad" ] do
                yield "status." + name, step map name map.StatusSteps.[i]
            yield "status.track", map.OnFill
            yield "status.keyline", map.Body
            yield "terrain.open", step map "grey" 2
            yield "terrain.blocked", step map "grey" 4
            yield "terrain.difficult", step map "difficult" 3
            yield "terrain.water", step map "water" 3
            yield "terrain.void", void'
        ]
        |> Map.ofList

    let private pairs (xs: 'a list) = [ for i in 0 .. xs.Length - 1 do for j in i + 1 .. xs.Length - 1 -> xs.[i], xs.[j] ]

    let private distinct (theme: Theme) (a: string) (b: string) : ThemeViolation list =
        let ca, cb = colour theme a, colour theme b
        [
            for vision in visions do
                let threshold = if vision = Normal then thresholds.Normal else thresholds.Simulated
                let dE = gridL (deltaEUnder vision ca cb)
                if dE < threshold then yield NotDistinct(a, b, vision, dE, threshold)
        ]

    /// Do the first `n` teams pass every distinctness rule (pairwise deltaE, normal and simulated)?
    let private teamsDistinct (theme: Theme) (n: int) =
        pairs (teamAliases n) |> List.forall (fun (a, b) -> (distinct theme a b).IsEmpty)

    let private measureCapacity (theme: Theme) : int list =
        if not theme.Map.ColourCarries then [ 0; 0; 0 ]
        else
            let top = min (List.max theme.Map.HueCeiling) maxTeams
            let mutable n = 1
            while n < top && teamsDistinct theme (n + 1) do
                n <- n + 1
            theme.Map.HueCeiling |> List.map (fun ceiling -> min n (min ceiling maxTeams))

    let private measured (theme: Theme) = { theme with Capacity = measureCapacity theme }

    /// The usable team hues at a frame size (glyph ADR D12 rule 3): 0 when colour carries no category; otherwise the
    /// largest n, not above the size's ceiling, whose first n team colours pass every distinctness rule. Measured, not
    /// declared: on #802 the preset seeds set `hueCapacity.N` to these values and #795's capacity check refuses a seed
    /// that declares more than it separates.
    let hueCapacity (theme: Theme) (frameUnits: float) : int =
        let c = theme.Capacity
        if c.IsEmpty then 0 elif frameUnits < 24.0 then c.[0] elif frameUnits < 40.0 then c.[1] else c.[2]

    /// #795's checks, each a future `ThemeCheck` on #802 (name, run), for a game that tells `teams` teams apart (at
    /// least the two-team default). They run on a theme the built-in rules accept, when colour carries categories:
    /// - `distinct.deltaE2000`: teams (the game's), allegiances and status levels pairwise, by CIEDE2000 >= 12 with
    ///   normal vision and >= 8 under Machado protan, deutan and tritan (severity 1.0, linear RGB);
    /// - `distinct.cues`: selection, the outer focus tone and hover against every team and allegiance colour (>= 12,
    ///   normal vision), because they are drawn over them;
    /// - `lightness.firstTwoTeams`: the first two teams differ by >= 0.08 in OKLab L under every simulation (D12 as
    ///   amended: beyond the first two, team rests on the allegiance frame plus the mandatory badge);
    /// - `status.meaning`: good, warn and bad keep their hue bands at chroma >= 0.08, and warn stays lighter than bad (in
    ///   `monochrome`, where status reads by bar length alone, the check is skipped);
    /// - `cue.innerOnFills`: the inner focus tone on every team and allegiance colour it rings (3:1).
    let checks (teams: int) : (string * (Theme -> ThemeViolation list)) list =
        let teamList = teamAliases (max 2 (min teams maxTeams))
        let fills theme = teamList @ allegianceAliases
        [
            "distinct.deltaE2000",
            fun theme ->
                if not theme.Map.ColourCarries then []
                else [ for a, b in pairs teamList @ pairs allegianceAliases @ pairs statusAliases do yield! distinct theme a b ]
            "distinct.cues",
            fun theme ->
                if not theme.Map.ColourCarries then []
                else
                    [
                        for over in [ "glyph.selection"; "cue.focus.outer"; "cue.hover" ] do
                            for fill in fills theme do
                                let dE = gridL (deltaE2000 (labOf (colour theme over)) (labOf (colour theme fill)))
                                if dE < thresholds.Normal then yield NotDistinct(over, fill, Normal, dE, thresholds.Normal)
                    ]
            "lightness.firstTwoTeams",
            fun theme ->
                if not theme.Map.ColourCarries then []
                else
                    [
                        for vision in [ Protan; Deutan; Tritan ] do
                            let gap = gridL (lightnessGapUnder vision (colour theme "glyph.team.0") (colour theme "glyph.team.1"))
                            if gap < thresholds.TeamLightnessGap then
                                yield LightnessTooClose("glyph.team.0", "glyph.team.1", vision, gap, thresholds.TeamLightnessGap)
                    ]
            "status.meaning",
            fun theme ->
                if not theme.Map.ColourCarries then []
                else
                    [
                        for alias, band, _, _ in statusBands do
                            let c = colour theme alias
                            if not (statusReads alias c) then yield StatusMeaningLost(alias, (inputOf c).H, band)
                        let warn = colour theme "status.warn"
                        if not (statusOrdered warn (colour theme "status.bad")) then
                            yield StatusMeaningLost("status.warn", (inputOf warn).H, "amber, lighter than status.bad,")
                    ]
            "cue.innerOnFills",
            fun theme ->
                [
                    for fill in fills theme do
                        let ratio = contrast (colour theme "cue.focus.inner") (colour theme fill)
                        if ratio < 3.0 then yield BelowFloor("cue.focus.inner", fill, ratio, 3.0)
                ]
        ]

    /// Every rule a theme breaks for a game of `teams` teams: the built-in rules (stand-in for #802's), then #795's
    /// checks in order.
    let validateFor (teams: int) (theme: Theme) : ThemeViolation list =
        match builtInRules theme with
        | [] -> checks teams |> List.collect (fun (_, run) -> run theme)
        | violations -> violations

    /// Validate with the two-team default.
    let validate (theme: Theme) = validateFor 2 theme

    /// `repair`: the deterministic check-and-repair pass (the design's palette step 4). Over the Leonardo-solved ramps
    /// and team bands, it chooses the lightness of every allegiance, status level and team band, and the step of
    /// selection, the marker and hover, to maximise the smallest distinctness margin while every choice keeps the
    /// built-in contrast rules and status meaning. It changes hue only for selection, marker and hover, and only when
    /// the seed's hue fails at every step. Candidates are enumerated in a fixed order and the first best wins, so .NET
    /// and Fable choose the same values.
    let repair: Algorithm =
        let margin (labs: Map<Vision, Lab>) (other: Map<Vision, Lab>) =
            visions
            |> List.map (fun v -> gridL (deltaE2000 labs.[v] other.[v]) - (if v = Normal then thresholds.Normal else thresholds.Simulated))
            |> List.min
        let labsOf (c: Color) = visions |> List.map (fun v -> v, labOfLinear (simulate v c)) |> Map.ofList
        let worst (xs: float list) = if xs.IsEmpty then infinity else List.min xs
        {
            Name = "repair"
            Apply =
                fun _ map ->
                    if not map.ColourCarries then applied "repair" map
                    else
                        let candidates = [ 7..12 ]
                        let colourAt name n = map.Ramps.[name].[n - 1]
                        let onGround floor (c: Color) = contrast c map.Ground >= floor
                        let inked (c: Color) = contrast c map.OnFill >= 3.0
                        let labsAt = map.Ramps |> Map.map (fun _ colours -> colours |> List.map labsOf)
                        let labAt name n = labsAt.[name].[n - 1]
                        let best (score: 'a -> float option) (options: 'a list) (fallback: 'a) =
                            options
                            |> List.fold
                                (fun (bestScore, bestOption) option ->
                                    match score option with
                                    | Some s when s > bestScore -> s, option
                                    | _ -> bestScore, bestOption)
                                (-infinity, fallback)
                            |> snd
                        let rec combos n =
                            if n = 0 then [ [] ] else [ for c in candidates do for rest in combos (n - 1) -> c :: rest ]
                        // Pairwise margins are computed once per (pair, step, step) into an array and looked up per
                        // combination (candidate steps are 7 to 12, index n - 7).
                        let groupScore (names: string list) (usable: string -> Color -> bool) =
                            let arr = List.toArray names
                            let k = arr.Length
                            let nc = candidates.Length
                            let pairMargin = Array.create (k * k * nc * nc) 0.0
                            for i in 0 .. k - 1 do
                                for j in i + 1 .. k - 1 do
                                    for si in candidates do
                                        for sj in candidates do
                                            pairMargin.[((i * k + j) * nc + (si - 7)) * nc + (sj - 7)] <- margin (labAt arr.[i] si) (labAt arr.[j] sj)
                            let usableAt = Array.init (k * nc) (fun idx -> usable arr.[idx / nc] (colourAt arr.[idx / nc] (idx % nc + 7)))
                            fun (steps: int list) ->
                                let st = List.toArray steps
                                let mutable ok = true
                                for i in 0 .. k - 1 do
                                    if not usableAt.[i * nc + (st.[i] - 7)] then ok <- false
                                if not ok then None
                                else
                                    let mutable w = infinity
                                    for i in 0 .. k - 1 do
                                        for j in i + 1 .. k - 1 do
                                            let m = pairMargin.[((i * k + j) * nc + (st.[i] - 7)) * nc + (st.[j] - 7)]
                                            if m < w then w <- m
                                    Some w
                        let allegianceNames = identities |> List.map identityName
                        let allegianceSteps =
                            best (groupScore allegianceNames (fun _ c -> onGround map.Floor c && inked c)) (combos 5) map.AllegianceSteps
                        let statusNames = [ "good"; "warn"; "bad" ]
                        let amberOverRed (steps: int list) = statusOrdered (colourAt "warn" steps.[1]) (colourAt "bad" steps.[2])
                        let statusSteps =
                            let statusScore = groupScore statusNames (fun name c -> contrast c map.OnFill >= 3.0 && statusReads ("status." + name) c)
                            best (fun steps -> if amberOverRed steps then statusScore steps else None)
                                (combos 3)
                                map.StatusSteps
                        // Team bands: solved per candidate ratio once, then every ordered pair of distinct ratios.
                        let teamCount = map.TeamHues.Length
                        let solved =
                            [ for ratio in teamBandCandidates -> ratio, [ for i in 0 .. teamCount - 1 -> teamColour map [ ratio ] i ] ] |> Map.ofList
                        let teamUsable (c: Color) = onGround 3.0 c && contrast c map.Body >= 3.0 && inked c
                        let ratios = List.toArray teamBandCandidates
                        let nr = ratios.Length
                        let ratioIndex (r: float) = Array.findIndex ((=) r) ratios
                        let solvedLabs = solved |> Map.map (fun _ colours -> colours |> List.map labsOf |> List.toArray)
                        // Computed on first use (NaN marks "not yet"): most options stop at their first failing pair.
                        let teamMarginCache = Array.create (teamCount * teamCount * nr * nr) nan
                        let teamMargin (i: int) (j: int) (ri: int) (rj: int) =
                            let idx = ((i * teamCount + j) * nr + ri) * nr + rj
                            if System.Double.IsNaN teamMarginCache.[idx] then
                                teamMarginCache.[idx] <- margin solvedLabs.[ratios.[ri]].[i] solvedLabs.[ratios.[rj]].[j]
                            teamMarginCache.[idx]
                        let bandScore (bands: float list) =
                            let colourOf i = solved.[bands.[i % bands.Length]].[i]
                            let bandIdx = bands |> List.map ratioIndex |> List.toArray
                            let bandOf i = bandIdx.[i % bandIdx.Length]
                            if not ([ 0 .. teamCount - 1 ] |> List.forall (fun i -> teamUsable (colourOf i))) then None
                            else
                                let ok n =
                                    let mutable all = true
                                    for i in 0 .. n - 1 do
                                        for j in i + 1 .. n - 1 do
                                            if all && teamMargin i j (bandOf i) (bandOf j) < 0.0 then all <- false
                                    all
                                let mutable capacity = 1
                                while capacity < teamCount && ok (capacity + 1) do
                                    capacity <- capacity + 1
                                let gap = [ Protan; Deutan; Tritan ] |> List.map (fun v -> gridL (lightnessGapUnder v (colourOf 0) (colourOf 1))) |> List.min
                                let gapOk = if gap >= thresholds.TeamLightnessGap then 1.0 else 0.0
                                Some(gapOk * 100.0 + float capacity + min gap 0.5)
                        let bandOptions = [ for a in teamBandCandidates do for b in teamBandCandidates do if a <> b then yield [ a; b ] ]
                        let teamBands = best bandScore bandOptions map.TeamBands
                        let teamColours = [ for i in 0 .. teamCount - 1 -> solved.[teamBands.[i % teamBands.Length]].[i] ]
                        let fills = (teamColours |> List.map labsOf) @ List.map2 labAt allegianceNames allegianceSteps
                        // Selection, the marker and hover are drawn over fills, so each must stand apart from every fill.
                        // The seed's hue is tried first, at every step from the strongest down; only if none reaches the
                        // normal-vision threshold
                        // are the twelve 30-degree hues tried, in order. The first that reaches it wins, else the best.
                        let overPick (seedHue: float) (avoid: (float * int) list) =
                            let options = [ for h in seedHue :: [ for k in 0..11 -> 30.0 * float k ] do for n in List.rev candidates -> h, n ]
                            let score (h: float, n: int) =
                                if List.contains (h, n) avoid then None
                                else
                                    let c = solve map.Ground map.Polarity map.StepRatios.[n - 1] map.Chroma h |> Option.defaultValue map.Ground
                                    if onGround map.Floor c then Some(worst [ for f in fills -> gridL (deltaE2000 (labOf c) f.[Normal]) ]) else None
                            match options |> List.tryFind (fun o -> match score o with Some s -> s >= thresholds.Normal | None -> false) with
                            | Some o -> o
                            | None -> best score options (seedHue, 12)
                        let selectionHue, selectionStep = overPick map.SelectionHue []
                        let markerHue, markerStep = overPick map.MarkerHue []
                        let hoverHue, hoverStep = overPick map.HoverHue [ markerHue, markerStep; selectionHue, selectionStep ]
                        { map with
                            AllegianceSteps = allegianceSteps
                            StatusSteps = statusSteps
                            TeamBands = teamBands
                            SelectionHue = selectionHue
                            SelectionStep = selectionStep
                            MarkerHue = markerHue
                            MarkerStep = markerStep
                            HoverHue = hoverHue
                            HoverStep = hoverStep }
                        |> rederive
                        |> applied "repair"
        }

    // =================================================================================================================
    // STAND-INS for #802 (replaced on merge), continued
    // =================================================================================================================

    /// STAND-IN for `Theme.create seed algorithms overrides |> Result.bind (Theme.validate checks)`: run the algorithms in
    /// order, resolve the aliases, apply overrides, then the built-in rules and #795's checks for the two-team default.
    let create (seed: Seed) (algorithms: Algorithm list) (overrides: Map<string, Color>) : Result<Theme, ThemeViolation list> =
        let map = algorithms |> List.fold (fun map algorithm -> algorithm.Apply seed map) (baseAlgorithm.Apply seed Unchecked.defaultof<MapTokens>)
        let aliases = overrides |> Map.fold (fun acc k v -> Map.add k v acc) (resolveAliases map)
        let theme = measured { Name = seed.Name; Seed = seed; Map = map; Aliases = aliases; Overrides = overrides; Capacity = [] }
        match validate theme with
        | [] -> Ok theme
        | violations -> Error violations

    /// STAND-IN for `Theme.withTeamColour team colour theme` (XAG 103: presets plus free choice), validated again for the
    /// game's `teams`. Only one of the game's teams can be set (#802 E4); an override that fails is refused with its
    /// reasons.
    let overrideTeam (teams: int) (index: int) (colour: Color) (theme: Theme) : Result<Theme, ThemeViolation list> =
        if index < 0 || index >= teams || index >= maxTeams then Error [ TeamOutsideGame(index, teams) ]
        else
            let alias = sprintf "glyph.team.%d" index
            let next = measured { theme with Aliases = theme.Aliases.Add(alias, colour); Overrides = theme.Overrides.Add(alias, colour) }
            match validateFor teams next with
            | [] -> Ok next
            | violations -> Error violations

    /// The seed of every preset: the #789 proof's `standard` ground (34, 40, 49), Tableau-style hue angles with blue and
    /// orange first, APP-6-style allegiance hues.
    let classicSeed: Seed =
        {
            Name = "classic"
            Ground = rgb 34 40 49
            Chroma = 0.14
            AllegianceHues = [ 240.0; 195.0; 145.0; 25.0; 100.0 ]
            TeamHues = [ 255.0; 55.0; 175.0; 330.0; 130.0; 295.0; 95.0; 20.0 ]
            SelectionHue = 95.0
            MarkerHue = 200.0
            StatusHues = [ 150.0; 75.0; 25.0 ]
            ContrastTarget = 3.0
            Cell = 32.0
            StrokeBase = 2.0
            Gap = 2.0
            Frame = 24.0
        }

    /// A preset: a name, its seed and its algorithm list (glyph ADR Q12: presets are baked; this is the recipe the baked
    /// data is checked against). On #802 each is `Theme.create seed algorithms []` with #795's checks.
    type Preset = { Name: string; Seed: Seed; Algorithms: Algorithm list }

    let presets: Preset list =
        [
            { Name = "classic"; Seed = classicSeed; Algorithms = [ baseAlgorithm; dark; repair ] }
            { Name = "light"; Seed = { classicSeed with Name = "light" }; Algorithms = [ baseAlgorithm; light; repair ] }
            { Name = "highContrast"; Seed = { classicSeed with Name = "highContrast"; ContrastTarget = 7.0 }; Algorithms = [ baseAlgorithm; dark; highContrast; repair ] }
            { Name = "colourBlindSafe"; Seed = { classicSeed with Name = "colourBlindSafe" }; Algorithms = [ baseAlgorithm; dark; colourBlindSafe; repair ] }
            { Name = "monochrome"; Seed = { classicSeed with Name = "monochrome" }; Algorithms = [ baseAlgorithm; dark; monochrome; repair ] }
        ]

    let presetNames = presets |> List.map (fun p -> p.Name)

    /// Derive a preset at run time (the baked data is the shipped form; this is what CI and the smoke compare it with).
    let derive (preset: Preset) : Result<Theme, ThemeViolation list> = create preset.Seed preset.Algorithms Map.empty

    let deriveOrFail (preset: Preset) : Theme =
        match derive preset with
        | Ok theme -> theme
        | Error violations -> failwithf "preset %s fails: %s" preset.Name (violations |> List.map explainViolation |> String.concat "; ")

    /// STAND-IN for `Theme.hex`: every alias as hex, in alias order.
    let hexes (theme: Theme) : (string * string) list = aliasNames |> List.map (fun a -> a, hex (colour theme a))

    /// STAND-IN for `Theme.cssVariables` (#802 writes `--fsgg-` names).
    let cssVariables (theme: Theme) : string =
        aliasNames
        |> List.map (fun a -> sprintf "  --fsgg-%s: %s;" (a.Replace(".", "-")) (hex (colour theme a)))
        |> String.concat "\n"
        |> sprintf "[data-theme=\"%s\"] {\n%s\n}" theme.Name

    /// The #802 `Theme` this proof theme becomes (`toRenderTheme`), so the shipped glyph library (#789 part 3,
    /// `Glyphs.draw`) draws it. It starts from `ThemeSeed.classic` named after the preset and sets:
    /// - every one of the 35 colour aliases, by `ThemeOverride.colour`;
    /// - `hueCapacity.0` to `.3` to the measured capacities (size steps 0 to 2 frame under 24 units at cell 32, step 3
    ///   at 24). #802 requires a whole number from 1 to 8, so `monochrome`'s measured 0 is declared as 1; its team
    ///   colours are one grey, and its scenes fall back to allegiance colouring anyway (`Symbology.teamColouring` reads
    ///   the measured 0);
    /// - `strokeBase` and `gap` from the map (`highContrast`'s +1);
    /// - `contrastTarget` 3 until #809 (erratum E7, amendment A1) merges. Under #802's merged rule a 7:1 floor refuses
    ///   every pair of `highContrast` team colours; this module's `builtInRules` validate the 7:1 floor instead.
    /// When #795's implementation lands, the presets are built on #802 directly and this bridge goes away.
    let toRenderTheme (theme: Theme) : Result<FS.GG.Game.Render.Theme, string list> =
        let small, mid = hueCapacity theme 16.0, hueCapacity theme 24.0
        let seed =
            FS.GG.Game.Render.ThemeSeed.classic
            |> FS.GG.Game.Render.ThemeSeed.named theme.Name
            |> FS.GG.Game.Render.ThemeSeed.number "contrastTarget" 3.0
            |> FS.GG.Game.Render.ThemeSeed.number "strokeBase" theme.Map.Stroke
            |> FS.GG.Game.Render.ThemeSeed.number "gap" theme.Map.Gap
            |> FS.GG.Game.Render.ThemeSeed.number "hueCapacity.0" (float (max 1 small))
            |> FS.GG.Game.Render.ThemeSeed.number "hueCapacity.1" (float (max 1 small))
            |> FS.GG.Game.Render.ThemeSeed.number "hueCapacity.2" (float (max 1 small))
            |> FS.GG.Game.Render.ThemeSeed.number "hueCapacity.3" (float (max 1 mid))
        let overrides = aliasNames |> List.map (fun alias -> FS.GG.Game.Render.ThemeOverride.colour alias (colour theme alias))
        match FS.GG.Game.Render.Theme.create seed [ FS.GG.Game.Render.ThemeAlgorithm.standard ] overrides with
        | Ok rendered -> Ok rendered
        | Error violations -> Error(violations |> List.map (fun v -> v.Token + ": " + v.Reason))

    let renderOrFail (theme: Theme) : FS.GG.Game.Render.Theme =
        match toRenderTheme theme with
        | Ok rendered -> rendered
        | Error reasons -> failwithf "#802 refuses %s: %s" theme.Name (String.concat "; " reasons)

    // ---- Runtime switching (#795) -----------------------------------------------------------------------------------

    /// The theme as a Scene input through a reader (glyph ADR D1 and D10): the game keeps its selected theme outside
    /// game state, renders every frame with `Read ()`, and switches by `Select`; game state, selection and focus are
    /// untouched by a switch. The cue layer takes the same reader, so frame and cues of one Present agree.
    type ThemeSwitch = { Read: unit -> Theme; Select: Theme -> unit }

    let switchOf (initial: Theme) : ThemeSwitch =
        let current = ref initial
        { Read = (fun () -> current.Value); Select = (fun theme -> current.Value <- theme) }

    // ---- DTCG 2025.10: STAND-IN for #802's `Theme.toDtcg`, `Theme.ofDtcg` and `Theme.dtcgResolver` ----------------

    let private quote (s: string) = "\"" + s + "\""

    let private colourToken (c: Color) =
        let lch = oklchOf c
        sprintf
            "{ \"$type\": \"color\", \"$value\": { \"colorSpace\": \"oklch\", \"components\": [ %s, %s, %s ], \"alpha\": 1, \"hex\": %s } }"
            (fixedText 4 lch.L)
            (fixedText 4 lch.C)
            (fixedText 2 lch.H)
            (quote (hex c))

    /// Nest dotted alias paths into DTCG groups.
    type private Tree =
        | Leaf of string
        | Node of (string * Tree) list

    let rec private insert (path: string list) (value: string) (tree: (string * Tree) list) : (string * Tree) list =
        match path with
        | [] -> tree
        | [ last ] -> tree @ [ last, Leaf value ]
        | head :: rest ->
            match tree |> List.tryFind (fun (k, _) -> k = head) with
            | Some(_, Node children) -> tree |> List.map (fun (k, t) -> if k = head then k, Node(insert rest value children) else k, t)
            | _ -> tree @ [ head, Node(insert rest value []) ]

    let rec private render (indent: string) (tree: (string * Tree) list) : string =
        tree
        |> List.map (fun (k, t) ->
            match t with
            | Leaf v -> sprintf "%s%s: %s" indent (quote k) v
            | Node children -> sprintf "%s%s: {\n%s\n%s}" indent (quote k) (render (indent + "  ") children) indent)
        |> String.concat ",\n"

    /// `Theme.toDtcg` (glyph ADR D4): every resolved alias as a DTCG 2025.10 colour token, written as OKLCH components
    /// and hex, plus the size tokens as dimensions. Import takes the hex (glyph ADR D11).
    let toDtcg (theme: Theme) : string =
        let colours = aliasNames |> List.fold (fun tree a -> insert (List.ofArray (a.Split '.')) (colourToken (colour theme a)) tree) []
        let dimension (v: float) = sprintf "{ \"$type\": \"dimension\", \"$value\": { \"value\": %s, \"unit\": \"px\" } }" (fixedText 0 v)
        let sizes =
            [ "size", Node [ "cell", Leaf(dimension theme.Map.Cell); "frame", Leaf(dimension theme.Map.Frame) ]
              "stroke", Node [ "outline", Leaf(dimension theme.Map.Stroke); "gap", Leaf(dimension theme.Map.Gap) ] ]
        let meta =
            sprintf
                "  \"$extensions\": { \"gg.fs.theme\": { \"name\": %s, \"algorithms\": [ %s ], \"floor\": %s } }"
                (quote theme.Name)
                (theme.Map.Applied |> List.map quote |> String.concat ", ")
                (fixedText 1 theme.Map.Floor)
        "{\n" + meta + ",\n" + render "  " (colours @ sizes) + "\n}\n"

    /// A DTCG 2025.10 Resolver document over the presets' token files, with a `theme` modifier (glyph ADR D4).
    let resolverDocument (names: string list) : string =
        let contexts =
            names
            |> List.map (fun n -> sprintf "        %s: [ { \"$ref\": \"themes/%s.tokens.json\" } ]" (quote n) n)
            |> String.concat ",\n"
        sprintf
            "{\n  \"version\": \"2025-10-01\",\n  \"modifiers\": {\n    \"theme\": {\n      \"contexts\": {\n%s\n      },\n      \"default\": \"classic\"\n    }\n  },\n  \"resolutionOrder\": [ { \"$ref\": \"#/modifiers/theme\" } ]\n}\n"
            contexts

    /// A minimal JSON reader (objects, arrays, strings, numbers, literals), enough for DTCG token files, and the same
    /// on .NET and Fable.
    type Json =
        | JObject of (string * Json) list
        | JArray of Json list
        | JString of string
        | JNumber of float
        | JLiteral of string

    let parseJson (text: string) : Result<Json, string> =
        let n = text.Length
        let rec ws i = if i < n && System.Char.IsWhiteSpace text.[i] then ws (i + 1) else i
        let rec value i : Result<Json * int, string> =
            let i = ws i
            if i >= n then Error "unexpected end"
            else
                match text.[i] with
                | '{' -> members (ws (i + 1)) []
                | '[' -> items (ws (i + 1)) []
                | '"' -> str (i + 1) |> Result.map (fun (s, j) -> JString s, j)
                | ch when ch = '-' || System.Char.IsDigit ch ->
                    let mutable j = i + 1
                    while j < n && (System.Char.IsDigit text.[j] || text.[j] = '.' || text.[j] = 'e' || text.[j] = 'E' || text.[j] = '-' || text.[j] = '+') do
                        j <- j + 1
                    Ok(JNumber(float (text.Substring(i, j - i))), j)
                | _ ->
                    let mutable j = i
                    while j < n && System.Char.IsLetter text.[j] do
                        j <- j + 1
                    if j = i then Error(sprintf "unexpected '%c' at %d" text.[i] i) else Ok(JLiteral(text.Substring(i, j - i)), j)
        and str i : Result<string * int, string> =
            let sb = System.Text.StringBuilder()
            let mutable j = i
            let mutable closed = false
            while not closed && j < n do
                match text.[j] with
                | '"' -> closed <- true
                | '\\' when j + 1 < n ->
                    sb.Append(text.[j + 1]) |> ignore
                    j <- j + 1
                | ch -> sb.Append ch |> ignore
                if not closed then j <- j + 1
            if closed then Ok(sb.ToString(), j + 1) else Error "unterminated string"
        and members i acc =
            if i < n && text.[i] = '}' then Ok(JObject(List.rev acc), i + 1)
            else
                match str (ws i + 1) with
                | Error e -> Error e
                | Ok(key, j) ->
                    let j = ws j
                    if j >= n || text.[j] <> ':' then Error(sprintf "expected ':' at %d" j)
                    else
                        match value (j + 1) with
                        | Error e -> Error e
                        | Ok(v, k) ->
                            let k = ws k
                            if k < n && text.[k] = ',' then members (ws (k + 1)) ((key, v) :: acc)
                            elif k < n && text.[k] = '}' then Ok(JObject(List.rev ((key, v) :: acc)), k + 1)
                            else Error(sprintf "expected ',' or '}' at %d" k)
        and items i acc =
            if i < n && text.[i] = ']' then Ok(JArray(List.rev acc), i + 1)
            else
                match value i with
                | Error e -> Error e
                | Ok(v, k) ->
                    let k = ws k
                    if k < n && text.[k] = ',' then items (ws (k + 1)) (v :: acc)
                    elif k < n && text.[k] = ']' then Ok(JArray(List.rev (v :: acc)), k + 1)
                    else Error(sprintf "expected ',' or ']' at %d" k)
        value 0 |> Result.map fst

    /// The colour tokens of a DTCG document as alias -> colour, by #802's import rule (#789 issuecomment-6570698885):
    /// - a `hex` always wins (glyph ADR D11);
    /// - without one, `{colorSpace: "oklch", components: [L, C, H]}` goes through `ofOklch` (gamut mapping, then half
    ///   up), and `{colorSpace: "srgb", components: [r, g, b]}` through `quantise`;
    /// - refused, never silently dropped: another colour space, alpha other than the number 1, a malformed or 8-digit
    ///   hex, and a token with a `$value` but no `$type` of its own or from its nearest group.
    /// Number tokens (`dimension`) are skipped.
    let dtcgColours (json: Json) : (string * Result<Color, string>) list =
        let typeOf (fields: (string * Json) list) =
            fields |> List.tryPick (function "$type", JString t -> Some t | _ -> None)
        let components (parts: (string * Json) list) =
            match parts |> List.tryPick (function "components", JArray xs -> Some xs | _ -> None) with
            | Some [ JNumber a; JNumber b; JNumber c ] -> Some(a, b, c)
            | _ -> None
        let colourOf (parts: (string * Json) list) : Result<Color, string> =
            let alpha = parts |> List.tryFind (fun (k, _) -> k = "alpha") |> Option.map snd
            match alpha with
            | Some(JNumber a) when a <> 1.0 -> Error(sprintf "alpha %g; tokens are opaque until alpha is supported (glyph ADR Q5)" a)
            | Some(JNumber _) | None ->
                match parts |> List.tryFind (fun (k, _) -> k = "hex") |> Option.map snd with
                | Some(JString h) ->
                    match ofHex h with
                    | Some c -> Ok c
                    | None -> Error(sprintf "malformed hex %s; write #rrggbb" h)
                | Some _ -> Error "hex must be a string"
                | None ->
                    match parts |> List.tryPick (function "colorSpace", JString cs -> Some cs | _ -> None), components parts with
                    | Some "oklch", Some(l, c, h) -> Ok(ofOklch { L = l; C = c; H = h })
                    | Some "srgb", Some(r, g, b) -> Ok { Red = quantise r; Green = quantise g; Blue = quantise b; Alpha = 255uy }
                    | Some cs, Some _ -> Error(sprintf "colour space %s; import reads oklch or srgb components, or a hex" cs)
                    | _ -> Error "no hex and no oklch or srgb components"
            | Some _ -> Error "alpha must be the number 1"
        let rec walk (prefix: string) (groupType: string option) (node: Json) : (string * Result<Color, string>) list =
            match node with
            | JObject fields ->
                let declared = match typeOf fields with Some t -> Some t | None -> groupType
                match fields |> List.tryFind (fun (k, _) -> k = "$value") with
                | Some(_, value) ->
                    match declared, value with
                    | Some "color", JObject parts -> [ prefix, colourOf parts ]
                    | Some "color", _ -> [ prefix, Error "a colour token's $value must be an object" ]
                    | Some _, _ -> []
                    | None, _ -> [ prefix, Error "the token has no $type, of its own or from its group" ]
                | None ->
                    fields
                    |> List.filter (fun (k, _) -> not (k.StartsWith "$"))
                    |> List.collect (fun (k, v) -> walk (if prefix = "" then k else prefix + "." + k) declared v)
            | _ -> []
        walk "" None json

    /// STAND-IN for `Theme.ofDtcg` (glyph ADR D4): import a DTCG token file onto a base theme as alias overrides, validated again.
    /// Export followed by import gives equal resolved hex for every alias.
    let ofDtcg (baseTheme: Theme) (text: string) : Result<Theme, string list> =
        match parseJson text with
        | Error e -> Error [ "DTCG: " + e ]
        | Ok json ->
            let tokens = dtcgColours json
            let unknown = tokens |> List.map fst |> List.filter (fun a -> not (List.contains a aliasNames))
            let bad = tokens |> List.choose (fun (a, r) -> match r with Error e -> Some(explainViolation (BadToken(a, e))) | Ok _ -> None)
            if not unknown.IsEmpty || not bad.IsEmpty then Error((unknown |> List.map (fun a -> explainViolation (UnknownAlias a))) @ bad)
            else
                let colours = tokens |> List.choose (fun (a, r) -> match r with Ok c -> Some(a, c) | Error _ -> None) |> Map.ofList
                let changed = colours |> Map.filter (fun a c -> colour baseTheme a <> c)
                let theme =
                    measured
                        { baseTheme with
                            Aliases = colours |> Map.fold (fun acc k v -> Map.add k v acc) baseTheme.Aliases
                            Overrides = changed |> Map.fold (fun acc k v -> Map.add k v acc) baseTheme.Overrides }
                match validate theme with
                | [] -> Ok theme
                | violations -> Error(violations |> List.map explainViolation)
