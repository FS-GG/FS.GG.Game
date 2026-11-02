namespace FS.GG.Game.Reference.Experimental

open FS.GG.UI.Scene
open FS.GG.Game.Reference.Experimental.Colour

/// EXPERIMENTAL (FS.GG.Game#795, GAME-RENDER-02.8): theme derivation for the glyph layer, as the design
/// `docs/roadmaps/game-render-02-themes.md` proposes. Seed tokens are expanded by an ordered list of composable
/// algorithms (Ant Design v5's layering, MIT; the idea, not its HSV maths) into map tokens: one 12-step OKLCH ramp per
/// hue, each step solved for a target WCAG 2 contrast against the ground (Adobe Leonardo's idea, Apache-2.0), gamut-mapped
/// per CSS Color 4 and quantised half up. A closed alias set names the colours glyph code reads. A theme that fails
/// validation cannot be built. Not a contract: #789 owns `Theme`, its alias names and the algorithm signature; the
/// names here follow its ADR (D4) so the swap is a rename.
module Themes =

    type Identity = Glyphs.Identity

    /// Seed tokens: intent, about a dozen values (glyph ADR D4).
    type Seed =
        {
            Name: string
            /// The ground glyphs sit on.
            Ground: Color
            /// The key chroma of hued tokens (OKLCH C).
            Chroma: float
            /// Allegiance hues (OKLCH h, degrees), in `Glyphs.identities` order: own, ally, neutral, enemy, unknown.
            AllegianceHues: float list
            /// Team key hues, in team order. The first two are the two-team default (never red against green).
            TeamHues: float list
            SelectionHue: float
            MarkerHue: float
            /// Status hues: good, warn, bad.
            StatusHues: float list
            /// The non-text contrast floor: 3 (WCAG 2 SC 1.4.11), or 7 for `highContrast` (provisional, AAA text value).
            ContrastTarget: float
            Cell: float
            StrokeBase: float
            Gap: float
            /// The default identity-frame size in scene units.
            Frame: float
        }

    /// Map tokens: the parameters algorithms refine, and the ramps derived from them.
    type MapTokens =
        {
            /// Lighter: hued tokens are lighter than the ground (a dark theme). Darker: a light theme.
            Polarity: Polarity
            Ground: Color
            /// The ink drawn on fills: badge, icon, status track and the inner focus tone.
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
            /// The ramp step (1-12) of team N is `TeamSteps.[N % length]`: alternating light and dark bands, so teams
            /// differ in lightness too (Game Accessibility Guidelines: a light/dark split for team colours).
            TeamSteps: int list
            /// The ramp step of each allegiance, in `Glyphs.identities` order.
            AllegianceSteps: int list
            /// Status steps: good, warn, bad.
            StatusSteps: int list
            /// The ramp steps of selection, the marker and hover.
            SelectionStep: int
            MarkerStep: int
            HoverStep: int
            /// False for `monochrome`: colour carries no category, hue capacity is 0, and every hued alias is a grey.
            ColourCarries: bool
            /// The hue ceiling per size step: frames under 24 units, 24 to 39, 40 and over (Munzner's 6-12 bins, fewer at
            /// small sizes; glyph ADR D12 rule 3).
            HueCeiling: int list
            Stroke: float
            Gap: float
            Cell: float
            Frame: float
            /// Derived: ramp name -> 12 colours.
            Ramps: Map<string, Color list>
            /// The algorithms applied, in order.
            Applied: string list
        }

    /// An algorithm refines the map from the seed (glyph ADR D4: `Seed -> MapTokens -> MapTokens`). A list composes.
    type Algorithm = { Name: string; Apply: Seed -> MapTokens -> MapTokens }

    /// The provisional distinctness thresholds (glyph ADR Q11; calibrated as the design's Q-T1 states).
    type Thresholds =
        {
            /// CIEDE2000 between two colours of one category, normal vision.
            Normal: float
            /// CIEDE2000 under Machado protan, deutan and tritan simulation, severity 1.0.
            Simulated: float
            /// The OKLab lightness gap between the first two teams under every simulation.
            TeamLightnessGap: float
        }

    let thresholds: Thresholds = { Normal = 12.0; Simulated = 8.0; TeamLightnessGap = 0.08 }

    /// The most team aliases a theme carries (the largest hue ceiling).
    let maxTeams = 8

    let identityName (identity: Identity) =
        match identity with
        | Glyphs.Own -> "own"
        | Glyphs.Ally -> "ally"
        | Glyphs.Neutral -> "neutral"
        | Glyphs.Enemy -> "enemy"
        | Glyphs.Unknown -> "unknown"

    /// The closed alias set (glyph ADR D4, plus the five names #795 asks #789 to add: badge and icon ink, status track
    /// and keyline, modifier). Glyph code reads only these.
    let aliasNames: string list =
        [
            yield "ground"
            yield "glyph.unit.body"
            yield "glyph.badge.ink"
            yield "glyph.icon.ink"
            for identity in Glyphs.identities do
                yield "glyph.allegiance." + identityName identity
            for team in 0 .. maxTeams - 1 do
                yield sprintf "glyph.team.%d" team
            yield "glyph.selection"
            yield "glyph.marker"
            yield "glyph.modifier"
            yield "cue.focus.outer"
            yield "cue.focus.inner"
            yield "cue.hover"
            yield "status.keyline"
            yield "status.track"
            yield "status.good"
            yield "status.warn"
            yield "status.bad"
        ]

    /// A resolved theme: the seed, the map, every alias, and the overrides the player or a DTCG file applied.
    type Theme =
        {
            Name: string
            Seed: Seed
            Map: MapTokens
            Aliases: Map<string, Color>
            Overrides: Map<string, Color>
            /// The measured hue capacity per size step (under 24 units, 24-39, 40 and over), computed when the theme is
            /// resolved, overridden or imported.
            Capacity: int list
        }

    /// A theme rule a theme breaks, named so a designer knows what to change.
    type ThemeViolation =
        | NotOpaque of alias: string
        | UnknownAlias of alias: string
        | BelowFloor of alias: string * against: string * ratio: float * floor: float
        | NotDistinct of a: string * b: string * vision: Vision * deltaE: float * threshold: float
        | LightnessTooClose of a: string * b: string * vision: Vision * gap: float * minimum: float
        | Unreachable of ramp: string * step: int * ratio: float

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
        | Unreachable(ramp, step, ratio) ->
            sprintf "ramp %s step %d cannot reach %g:1 against the ground at any lightness; lower the step's target or change the ground" ramp step ratio

    // ---- Derivation -------------------------------------------------------------------------------------------------

    let private darkRatios = [ 1.08; 1.18; 1.32; 1.5; 1.75; 2.1; 2.6; 3.2; 4.2; 5.6; 7.5; 11.0 ]
    let private highContrastRatios = [ 1.2; 1.5; 2.0; 2.6; 3.4; 4.4; 5.6; 7.2; 8.5; 10.5; 13.0; 16.0 ]

    let private groundHue (map: MapTokens) = (oklchOf map.Ground).H

    /// One 12-step ramp of hue `h` at chroma `c`: each step is the colour nearest the ground's lightness whose contrast
    /// against the ground meets the step's ratio. A step no lightness reaches takes the extreme (white or black), and the
    /// alias validation reports it if an alias uses it.
    let private ramp (map: MapTokens) (c: float) (h: float) : Color list =
        let extreme = match map.Polarity with Lighter -> rgb 255 255 255 | Darker -> rgb 0 0 0
        map.StepRatios |> List.map (fun ratio -> solve map.Ground map.Polarity ratio c h |> Option.defaultValue extreme)

    /// Recompute every ramp from the map's parameters. Algorithms call this after they change parameters.
    let rederive (map: MapTokens) : MapTokens =
        let c = if map.ColourCarries then map.Chroma else 0.0
        let named =
            [
                yield "grey", ramp map (if map.ColourCarries then 0.012 else 0.0) (groundHue map)
                for identity, h in List.zip Glyphs.identities map.AllegianceHues do
                    yield identityName identity, ramp map c h
                for i, h in List.indexed map.TeamHues do
                    yield sprintf "team%d" i, ramp map c h
                yield "selection", ramp map c map.SelectionHue
                yield "marker", ramp map c map.MarkerHue
                yield "hover", ramp map c map.HoverHue
                for name, h in List.zip [ "good"; "warn"; "bad" ] map.StatusHues do
                    yield name, ramp map c h
            ]
        { map with Ramps = Map.ofList named }

    /// `base`: the map a seed gives with a dark polarity when the ground is darker than mid grey, light otherwise.
    let baseAlgorithm: Algorithm =
        {
            Name = "base"
            Apply =
                fun seed _ ->
                    let dark = (oklchOf seed.Ground).L < 0.6
                    rederive
                        {
                            Polarity = (if dark then Lighter else Darker)
                            Ground = seed.Ground
                            OnFill = (if dark then rgb 10 10 12 else rgb 255 255 255)
                            Floor = seed.ContrastTarget
                            Chroma = seed.Chroma
                            AllegianceHues = seed.AllegianceHues
                            TeamHues = seed.TeamHues
                            SelectionHue = seed.SelectionHue
                            MarkerHue = seed.MarkerHue
                            HoverHue = seed.MarkerHue
                            StatusHues = seed.StatusHues
                            StepRatios = darkRatios
                            TeamSteps = [ 9; 11 ]
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
                            Applied = [ "base" ]
                        }
        }

    let private applied name (map: MapTokens) = { map with Applied = map.Applied @ [ name ] }

    /// `dark`: a dark ground of the seed's ground hue (the seed's own ground when it is already dark), near-black ink on
    /// fills, ramps lighter than the ground.
    let dark: Algorithm =
        {
            Name = "dark"
            Apply =
                fun seed map ->
                    let g = oklchOf seed.Ground
                    let ground = if g.L < 0.6 then seed.Ground else ofOklch { L = 0.29; C = min g.C 0.02; H = g.H }
                    { map with Polarity = Lighter; Ground = ground; OnFill = rgb 10 10 12 } |> rederive |> applied "dark"
        }

    /// `light`: a near-white ground of the seed's ground hue, white ink on fills, ramps darker than the ground
    /// (FourD's look).
    let light: Algorithm =
        {
            Name = "light"
            Apply =
                fun seed map ->
                    let g = oklchOf seed.Ground
                    let ground = ofOklch { L = 0.97; C = min g.C 0.01; H = g.H }
                    { map with
                        Polarity = Darker
                        Ground = ground
                        OnFill = rgb 255 255 255
                        TeamSteps = [ 9; 11 ]
                        AllegianceSteps = [ 10; 12; 9; 9; 11 ]
                        StatusSteps = [ 9; 11; 12 ] }
                    |> rederive
                    |> applied "light"
        }

    /// `highContrast`: a black ground, the 7:1 floor (provisional), steeper ramps, thicker strokes and gaps.
    let highContrast: Algorithm =
        {
            Name = "highContrast"
            Apply =
                fun _ map ->
                    let ground, onFill =
                        match map.Polarity with
                        | Lighter -> rgb 0 0 0, rgb 0 0 0
                        | Darker -> rgb 255 255 255, rgb 255 255 255
                    { map with
                        Ground = ground
                        OnFill = onFill
                        Floor = 7.0
                        StepRatios = highContrastRatios
                        TeamSteps = [ 8; 11 ]
                        AllegianceSteps = [ 9; 12; 8; 8; 11 ]
                        StatusSteps = [ 9; 12; 8 ]
                        HueCeiling = [ 4; 5; 6 ]
                        Stroke = map.Stroke + 1.0
                        Gap = map.Gap + 1.0 }
                    |> rederive
                    |> applied "highContrast"
        }

    /// Okabe and Ito's colour-universal-design palette (2008), as published sRGB values: orange, sky blue, bluish green,
    /// yellow, blue, vermillion, reddish purple. Only their hue angles are used; lightness is solved per ground.
    let okabeIto: Color list =
        [ rgb 230 159 0; rgb 86 180 233; rgb 0 158 115; rgb 240 228 66; rgb 0 114 178; rgb 213 94 0; rgb 204 121 167 ]

    /// `colourBlindSafe`: Okabe-Ito hue angles for teams, allegiances and status, with lightness solved for the ground
    /// (their blue is 2.86:1 on the classic ground; solving lifts it) and a wider light/dark band split.
    let colourBlindSafe: Algorithm =
        {
            Name = "colourBlindSafe"
            Apply =
                fun _ map ->
                    let hue i = (oklchOf okabeIto.[i]).H
                    let orange, sky, green, yellow, blue, vermillion, purple = hue 0, hue 1, hue 2, hue 3, hue 4, hue 5, hue 6
                    { map with
                        TeamHues = [ blue; orange; green; purple; sky; vermillion; yellow ]
                        AllegianceHues = [ sky; green; yellow; vermillion; purple ]
                        StatusHues = [ green; orange; vermillion ]
                        SelectionHue = yellow
                        MarkerHue = sky
                        HoverHue = sky
                        TeamSteps = (match map.Polarity with Lighter -> [ 8; 11 ] | Darker -> [ 8; 11 ])
                        HueCeiling = [ 4; 5; 6 ] }
                    |> rederive
                    |> applied "colourBlindSafe"
        }

    /// `monochrome`: every hue removed (milsymbol's monoColor). Colour carries no category, so identity must survive
    /// through frame shape alone and team through the badge; it is also the redundancy test (glyph ADR D4).
    let monochrome: Algorithm =
        {
            Name = "monochrome"
            Apply =
                fun _ map ->
                    { map with ColourCarries = false; HueCeiling = [ 0; 0; 0 ]; TeamSteps = [ 11 ]; AllegianceSteps = [ 11; 11; 11; 11; 11 ]; StatusSteps = [ 12; 12; 12 ] }
                    |> rederive
                    |> applied "monochrome"
        }

    /// `repair`: the deterministic check-and-repair pass (the design's palette step 4). Over the Leonardo-solved ramps,
    /// it chooses the lightness step of every allegiance, status level and team band, and of selection, the marker and
    /// hover, to maximise the smallest distinctness margin (deltaE2000 minus its threshold, under normal vision and every
    /// simulation) while every step keeps the contrast floor against the ground and the on-fill ink. Candidates are
    /// enumerated in a fixed order and the first best wins, so .NET and Fable choose the same steps. It nudges lightness
    /// only, never hue: hue is the seed's intent. Whatever still fails is reported by validation, naming the pair.
    let repair: Algorithm =
        let margin (labs: Map<Vision, Lab>) (other: Map<Vision, Lab>) =
            visions
            |> List.map (fun v -> deltaE2000 labs.[v] other.[v] - (if v = Normal then thresholds.Normal else thresholds.Simulated))
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
                        let usable (c: Color) = contrast c map.Ground >= map.Floor && contrast c map.OnFill >= map.Floor
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
                        let groupScore (names: string list) (steps: int list) =
                            if List.forall2 (fun name n -> usable (colourAt name n)) names steps then
                                let labs = List.map2 labAt names steps
                                Some(worst [ for i in 0 .. labs.Length - 1 do for j in i + 1 .. labs.Length - 1 -> margin labs.[i] labs.[j] ])
                            else None
                        let allegianceNames = Glyphs.identities |> List.map identityName
                        let allegianceSteps = best (groupScore allegianceNames) (combos 5) map.AllegianceSteps
                        let statusNames = [ "good"; "warn"; "bad" ]
                        let statusScore steps =
                            if List.forall2 (fun name n -> contrast (colourAt name n) map.OnFill >= map.Floor) statusNames steps then
                                let labs = List.map2 labAt statusNames steps
                                Some(worst [ for i in 0..2 do for j in i + 1 .. 2 -> margin labs.[i] labs.[j] ])
                            else None
                        let statusSteps = best statusScore (combos 3) map.StatusSteps
                        let teamCount = map.TeamHues.Length
                        let teamScore (bands: int list) =
                            let teamLab i = labAt (sprintf "team%d" i) bands.[i % bands.Length]
                            let teamColour i = colourAt (sprintf "team%d" i) bands.[i % bands.Length]
                            if not ([ 0 .. teamCount - 1 ] |> List.forall (fun i -> usable (teamColour i))) then None
                            else
                                let ok n = [ for i in 0 .. n - 1 do for j in i + 1 .. n - 1 -> margin (teamLab i) (teamLab j) ] |> List.forall (fun m -> m >= 0.0)
                                let mutable capacity = 1
                                while capacity < teamCount && ok (capacity + 1) do
                                    capacity <- capacity + 1
                                let gap =
                                    [ Protan; Deutan; Tritan ] |> List.map (fun v -> lightnessGapUnder v (teamColour 0) (teamColour 1)) |> List.min
                                let gapOk = if gap >= thresholds.TeamLightnessGap then 1.0 else 0.0
                                Some(gapOk * 100.0 + float capacity + min gap 0.5)
                        let teamSteps = best teamScore [ [ 8; 11 ]; [ 9; 11 ]; [ 8; 12 ]; [ 9; 12 ]; [ 10; 12 ]; [ 7; 11 ]; [ 7; 12 ] ] map.TeamSteps
                        let fills =
                            [ for i in 0 .. teamCount - 1 -> labAt (sprintf "team%d" i) teamSteps.[i % teamSteps.Length] ]
                            @ List.map2 labAt allegianceNames allegianceSteps
                        // Selection, the marker and hover are drawn over fills, so each must stand apart from every fill.
                        // The seed's hue is tried first, at every step; only if none reaches the normal-vision threshold
                        // are the twelve 30-degree hues tried, in order. The first that reaches it wins, else the best.
                        let overPick (seedHue: float) (avoid: (float * int) list) =
                            let options = [ for h in seedHue :: [ for k in 0..11 -> 30.0 * float k ] do for n in candidates -> h, n ]
                            let score (h: float, n: int) =
                                if List.contains (h, n) avoid then None
                                else
                                    let c = match map.Ramps.TryFind(sprintf "probe%g" h) with Some r -> r.[n - 1] | None -> solve map.Ground map.Polarity map.StepRatios.[n - 1] map.Chroma h |> Option.defaultValue map.Ground
                                    if usable c then Some(worst [ for f in fills -> deltaE2000 (labOf c) f.[Normal] ]) else None
                            match options |> List.tryFind (fun o -> match score o with Some s -> s >= thresholds.Normal | None -> false) with
                            | Some o -> o
                            | None -> best score options (seedHue, 12)
                        let selectionHue, selectionStep = overPick map.SelectionHue []
                        let markerHue, markerStep = overPick map.MarkerHue []
                        let hoverHue, hoverStep = overPick map.HoverHue [ markerHue, markerStep; selectionHue, selectionStep ]
                        { map with
                            AllegianceSteps = allegianceSteps
                            StatusSteps = statusSteps
                            TeamSteps = teamSteps
                            SelectionHue = selectionHue
                            SelectionStep = selectionStep
                            MarkerHue = markerHue
                            MarkerStep = markerStep
                            HoverHue = hoverHue
                            HoverStep = hoverStep }
                        |> rederive
                        |> applied "repair"
        }

    // ---- Aliases ----------------------------------------------------------------------------------------------------

    let private step (map: MapTokens) (rampName: string) (n: int) : Color =
        match map.Ramps.TryFind rampName with
        | Some colours -> colours.[(max 1 (min 12 n)) - 1]
        | None -> map.Ground

    let private resolveAliases (map: MapTokens) : Map<string, Color> =
        let teamStep n = map.TeamSteps.[n % map.TeamSteps.Length]
        let teamRamp n = if n < map.TeamHues.Length then sprintf "team%d" n else sprintf "team%d" (n % map.TeamHues.Length)
        let body = step map "grey" 11
        [
            yield "ground", map.Ground
            yield "glyph.unit.body", body
            yield "glyph.badge.ink", map.OnFill
            yield "glyph.icon.ink", map.OnFill
            for i, identity in List.indexed Glyphs.identities do
                yield "glyph.allegiance." + identityName identity, step map (identityName identity) map.AllegianceSteps.[i]
            for team in 0 .. maxTeams - 1 do
                yield sprintf "glyph.team.%d" team, step map (teamRamp team) (teamStep team)
            yield "glyph.selection", step map "selection" map.SelectionStep
            yield "glyph.marker", step map "marker" map.MarkerStep
            yield "glyph.modifier", step map "grey" 12
            yield "cue.focus.outer", step map "grey" 12
            yield "cue.focus.inner", map.OnFill
            yield "cue.hover", step map "hover" map.HoverStep
            yield "status.keyline", body
            yield "status.track", map.OnFill
            for i, name in List.indexed [ "good"; "warn"; "bad" ] do
                yield "status." + name, step map name map.StatusSteps.[i]
        ]
        |> Map.ofList

    let colour (theme: Theme) (alias: string) : Color =
        match theme.Aliases.TryFind alias with
        | Some c -> c
        | None -> theme.Map.Ground

    let team (theme: Theme) (index: int) = colour theme (sprintf "glyph.team.%d" index)
    let allegiance (theme: Theme) (identity: Identity) = colour theme ("glyph.allegiance." + identityName identity)

    // ---- Validation -------------------------------------------------------------------------------------------------

    let private pairs (xs: 'a list) = [ for i in 0 .. xs.Length - 1 do for j in i + 1 .. xs.Length - 1 -> xs.[i], xs.[j] ]

    let private distinct (theme: Theme) (a: string) (b: string) : ThemeViolation list =
        let ca, cb = colour theme a, colour theme b
        [
            for vision in visions do
                let threshold = if vision = Normal then thresholds.Normal else thresholds.Simulated
                let dE = deltaEUnder vision ca cb
                if dE < threshold then yield NotDistinct(a, b, vision, dE, threshold)
        ]

    let private teamAliases n = [ for i in 0 .. n - 1 -> sprintf "glyph.team.%d" i ]
    let private allegianceAliases = Glyphs.identities |> List.map (fun i -> "glyph.allegiance." + identityName i)

    /// Do the first `n` teams pass every distinctness rule (pairwise deltaE, normal and simulated)?
    let private teamsDistinct (theme: Theme) (n: int) =
        pairs (teamAliases n) |> List.forall (fun (a, b) -> (distinct theme a b).IsEmpty)

    /// The usable team hues at a frame size (glyph ADR D12 rule 3, `Theme.hueCapacity`): 0 when colour carries no
    /// category; otherwise the largest n not above the size's ceiling whose first n team colours pass every
    /// distinctness rule. Measured, not declared, so a theme cannot claim more hues than it separates.
    let private measureCapacity (theme: Theme) : int list =
        if not theme.Map.ColourCarries then [ 0; 0; 0 ]
        else
            let top = min (List.max theme.Map.HueCeiling) maxTeams
            let mutable n = 1
            while n < top && teamsDistinct theme (n + 1) do
                n <- n + 1
            theme.Map.HueCeiling |> List.map (fun ceiling -> min n (min ceiling maxTeams))

    let private measured (theme: Theme) = { theme with Capacity = measureCapacity theme }

    let hueCapacity (theme: Theme) (frameUnits: float) : int =
        let c = theme.Capacity
        if c.IsEmpty then 0 elif frameUnits < 24.0 then c.[0] elif frameUnits < 40.0 then c.[1] else c.[2]

    /// Every rule a theme breaks, with `teams` the number of teams whose colours must be told apart (at least the
    /// two-team default). Contrast: every alias drawn on the ground meets the floor, and each touching pair does
    /// (ink on fills, the inner focus tone on the outer one and on every fill it rings, status fills on the track).
    /// Distinctness (when colour carries categories): teams, allegiances and status levels pairwise, and selection, the
    /// outer focus tone and hover against every team and allegiance, by deltaE2000 under normal vision and Machado
    /// simulation; the first two teams also differ in OKLab lightness under every simulation.
    let validateFor (teams: int) (theme: Theme) : ThemeViolation list =
        let floor = theme.Map.Floor
        let c = colour theme
        let ground = c "ground"
        let teamList = teamAliases (max 2 (min teams maxTeams))
        let fills = teamList @ allegianceAliases
        let below alias against =
            let ratio = contrast (c alias) (c against)
            if ratio < floor then [ BelowFloor(alias, against, ratio, floor) ] else []
        [
            for alias in aliasNames do
                if (c alias).Alpha <> 255uy then yield NotOpaque alias
            for alias in theme.Overrides |> Map.toList |> List.map fst do
                if not (List.contains alias aliasNames) then yield UnknownAlias alias
            for alias in
                fills
                @ [ "glyph.unit.body"; "glyph.selection"; "glyph.marker"; "glyph.modifier"; "cue.focus.outer"; "cue.hover"; "status.keyline" ] do
                yield! below alias "ground"
            for fill in fills do
                yield! below "glyph.badge.ink" fill
                yield! below "cue.focus.inner" fill
            yield! below "cue.focus.inner" "cue.focus.outer"
            for status in [ "status.good"; "status.warn"; "status.bad" ] do
                yield! below status "status.track"
            if theme.Map.ColourCarries then
                for a, b in pairs teamList @ pairs allegianceAliases @ pairs [ "status.good"; "status.warn"; "status.bad" ] do
                    yield! distinct theme a b
                for over in [ "glyph.selection"; "cue.focus.outer"; "cue.hover" ] do
                    for fill in fills do
                        let dE = deltaE2000 (labOf (c over)) (labOf (c fill))
                        if dE < thresholds.Normal then yield NotDistinct(over, fill, Normal, dE, thresholds.Normal)
                for vision in [ Protan; Deutan; Tritan ] do
                    let gap = lightnessGapUnder vision (c "glyph.team.0") (c "glyph.team.1")
                    if gap < thresholds.TeamLightnessGap then
                        yield LightnessTooClose("glyph.team.0", "glyph.team.1", vision, gap, thresholds.TeamLightnessGap)
        ]

    /// Validate with the two-team default.
    let validate (theme: Theme) = validateFor 2 theme

    /// Resolve a theme: run the algorithms in order, resolve the aliases, apply overrides, validate. A theme that fails
    /// validation cannot be built (glyph ADR D4).
    let create (seed: Seed) (algorithms: Algorithm list) (overrides: Map<string, Color>) : Result<Theme, ThemeViolation list> =
        let map = algorithms |> List.fold (fun map algorithm -> algorithm.Apply seed map) (baseAlgorithm.Apply seed Unchecked.defaultof<MapTokens>)
        let aliases = overrides |> Map.fold (fun acc k v -> Map.add k v acc) (resolveAliases map)
        let theme = measured { Name = seed.Name; Seed = seed; Map = map; Aliases = aliases; Overrides = overrides; Capacity = [] }
        match validate theme with
        | [] -> Ok theme
        | violations -> Error violations

    /// The player's team-colour override at the alias layer (XAG 103: presets plus free choice). The theme is validated
    /// again for the game's `teams`, and an override that fails is refused with its reasons.
    let overrideTeam (teams: int) (index: int) (colour: Color) (theme: Theme) : Result<Theme, ThemeViolation list> =
        let alias = sprintf "glyph.team.%d" index
        let next = measured { theme with Aliases = theme.Aliases.Add(alias, colour); Overrides = theme.Overrides.Add(alias, colour) }
        match validateFor teams next with
        | [] -> Ok next
        | violations -> Error violations

    // ---- Presets ----------------------------------------------------------------------------------------------------

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

    /// A preset: a name, its seed and its algorithm list (glyph ADR D4 and Q12: presets are baked; this is the recipe the
    /// baked data is checked against).
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

    /// Every alias of a theme as hex, in alias order.
    let hexes (theme: Theme) : (string * string) list = aliasNames |> List.map (fun a -> a, hex (colour theme a))

    /// `Theme.cssVariables` (glyph ADR D4): the aliases as CSS custom properties, for the parts of a game the Scene does
    /// not draw.
    let cssVariables (theme: Theme) : string =
        aliasNames
        |> List.map (fun a -> sprintf "  --%s: %s;" (a.Replace(".", "-")) (hex (colour theme a)))
        |> String.concat "\n"
        |> sprintf "[data-theme=\"%s\"] {\n%s\n}" theme.Name

    // ---- Runtime switching ------------------------------------------------------------------------------------------

    /// The theme as a Scene input through a reader (glyph ADR D1 and D10): the game keeps its selected theme outside
    /// game state, renders every frame with `Read ()`, and switches by `Select`; game state, selection and focus are
    /// untouched by a switch. The cue layer takes the same reader, so frame and cues of one Present agree.
    type ThemeSwitch = { Read: unit -> Theme; Select: Theme -> unit }

    let switchOf (initial: Theme) : ThemeSwitch =
        let current = ref initial
        { Read = (fun () -> current.Value); Select = (fun theme -> current.Value <- theme) }

    // ---- DTCG 2025.10 -----------------------------------------------------------------------------------------------

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

    /// The colour tokens of a DTCG document as alias -> colour, taking `hex` when present (glyph ADR D11).
    let dtcgColours (json: Json) : Map<string, Color> =
        let rec walk (prefix: string) (node: Json) : (string * Color) list =
            match node with
            | JObject fields ->
                let isToken = fields |> List.exists (fun (k, _) -> k = "$value")
                if isToken then
                    match fields |> List.tryFind (fun (k, _) -> k = "$value") with
                    | Some(_, JObject value) ->
                        match value |> List.tryFind (fun (k, _) -> k = "hex") with
                        | Some(_, JString h) ->
                            match ofHex h with
                            | Some c -> [ prefix, c ]
                            | None -> []
                        | _ -> []
                    | _ -> []
                else
                    fields
                    |> List.filter (fun (k, _) -> not (k.StartsWith "$"))
                    |> List.collect (fun (k, v) -> walk (if prefix = "" then k else prefix + "." + k) v)
            | _ -> []
        walk "" json |> Map.ofList

    /// `Theme.ofDtcg` (glyph ADR D4): import a DTCG token file onto a base theme as alias overrides, validated again.
    /// Export followed by import gives equal resolved hex for every alias.
    let ofDtcg (baseTheme: Theme) (text: string) : Result<Theme, string list> =
        match parseJson text with
        | Error e -> Error [ "DTCG: " + e ]
        | Ok json ->
            let colours = dtcgColours json
            let unknown = colours |> Map.toList |> List.map fst |> List.filter (fun a -> not (List.contains a aliasNames))
            if not unknown.IsEmpty then Error(unknown |> List.map (fun a -> explainViolation (UnknownAlias a)))
            else
                let changed = colours |> Map.filter (fun a c -> colour baseTheme a <> c)
                let theme =
                    measured
                        { baseTheme with
                            Aliases = colours |> Map.fold (fun acc k v -> Map.add k v acc) baseTheme.Aliases
                            Overrides = changed |> Map.fold (fun acc k v -> Map.add k v acc) baseTheme.Overrides }
                match validate theme with
                | [] -> Ok theme
                | violations -> Error(violations |> List.map explainViolation)
