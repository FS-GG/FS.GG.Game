namespace FS.GG.Game.Render

open FS.GG.UI.Scene

[<Sealed>]
type GlyphCase
    internal
    (
        name: string,
        width: float,
        height: float,
        scenes: Scene list,
        expected: PixelExpectation list,
        controls: (string * PixelExpectation list) list,
        drawnControls: (string * Scene * PixelExpectation list * int) list
    ) =
    member _.DrawnControls = drawnControls
    member _.Name = name
    member _.Width = width
    member _.Height = height
    member _.Scenes = scenes
    member _.Expected = expected
    member _.Controls = controls

[<RequireQualifiedAccess>]
module GlyphConformance =

    let private grey: Color = { Red = 128uy; Green = 128uy; Blue = 128uy; Alpha = 255uy }

    let private rebuild (theme: Theme) (change: ThemeSeed -> ThemeSeed) =
        match Theme.create (change (Theme.seed theme)) [ ThemeAlgorithm.standard ] [] with
        | Ok derived -> derived
        | Error violations -> failwithf "the kit's derived theme does not resolve: %s" (violations |> List.map string |> String.concat "; ")

    let monochrome (theme: Theme) =
        rebuild theme (fun seed ->
            let named = ThemeSeed.named (ThemeSeed.name seed + "-monochrome") seed

            (ThemeSeed.colourNames
             |> List.filter (fun n -> n.StartsWith "team." || n.StartsWith "allegiance.")
             |> List.filter (fun n -> (ThemeSeed.tryColour n seed).IsSome))
            |> List.fold (fun s n -> ThemeSeed.colour n grey s) named)

    /// The swap control for `theme`: its team and allegiance colours rotated by one.
    let private swapped (theme: Theme) =
        rebuild theme (fun seed ->
            let rotate (names: string list) (s: ThemeSeed) =
                let present = names |> List.filter (fun n -> (ThemeSeed.tryColour n seed).IsSome)
                let colours = present |> List.map (fun n -> (ThemeSeed.tryColour n seed).Value)
                let rotated = (List.tail colours) @ [ List.head colours ]
                List.zip present rotated |> List.fold (fun s (n, c) -> ThemeSeed.colour n c s) s

            seed
            |> ThemeSeed.named (ThemeSeed.name seed + "-swapped")
            |> rotate [ for i in 0..7 -> sprintf "team.%d" i ]
            |> rotate [ "allegiance.own"; "allegiance.ally"; "allegiance.neutral"; "allegiance.enemy"; "allegiance.unknown" ])

    let private ground (theme: Theme) (w: float) (h: float) = SceneNode.Rectangle((0.0, 0.0, w, h), theme.Palette.Ground)

    let private identities = [ Identity.Own; Identity.Ally; Identity.Neutral; Identity.Enemy; Identity.Unknown ]

    let private case name w h scenes controls =
        GlyphCase(name, w, h, scenes, ScenePixels.identityExpectations w h (List.last scenes), controls, [])

    let private pixels w h scene = ScenePixels.identityExpectations w h scene

    /// A drawn control: the control scene, its own pixels, and how many of the subject's pixels it decides otherwise.
    let private drawn name w h (subject: Scene) (control: Scene) =
        let expected = pixels w h subject
        let shown = ScenePixels.decidedAt control (expected |> List.map (fun e -> e.Point))
        let differing = List.zip expected shown |> List.filter (fun (e, s) -> match s with Some c -> c <> e.Colour | None -> false) |> List.length
        name, control, pixels w h control, differing

    let private withDrawn (controls: (string * Scene * PixelExpectation list * int) list) (c: GlyphCase) =
        GlyphCase(c.Name, c.Width, c.Height, c.Scenes, c.Expected, c.Controls, controls)

    let cases (themes: Theme list) : GlyphCase list =
        let a = List.head themes
        let b = match themes with _ :: second :: _ -> second | _ -> swapped a
        let all = match themes with [ only ] -> [ only; b ] | _ -> themes
        let idle = { Selected = false; Stale = false }

        // 1. The starter set in each theme, against the next theme.
        let starterWidth, starterHeight = 176.0, 128.0
        let starter (theme: Theme) : Scene =
            { Nodes = ground theme starterWidth starterHeight :: (Glyphs.scene theme (Glyphs.samples { X = 8.0; Y = 16.0 })).Nodes }

        let starters =
            all
            |> List.mapi (fun i theme ->
                let next = all.[(i + 1) % all.Length]
                case
                    (sprintf "starter set in %s" (Theme.name theme))
                    starterWidth
                    starterHeight
                    [ starter theme ]
                    [ sprintf "the starter set in %s" (Theme.name next), pixels starterWidth starterHeight (starter next) ])

        // 2. Identity pairs, monochrome at 24 units, bare and with a badge (team 8) inside.
        let p = a.Palette
        let frameScene (badge: bool) (identity: Identity) : Scene =
            {
                Nodes =
                    ground a 48.0 48.0
                    :: Glyphs.identityFrame identity 24.0 24.0 24.0 p.Body
                    @ (if badge then Glyphs.badgeNodes 8 24.0 24.0 p.BadgeInk else [])
            }

        let identityCases (badge: bool) =
            identities
            |> List.map (fun identity ->
                case
                    (sprintf "identity frame %A%s" identity (if badge then " with badge 8 inside" else ""))
                    48.0
                    48.0
                    [ frameScene badge identity ]
                    [
                        for other in identities |> List.filter ((<>) identity) ->
                            sprintf "%A's samples" other, pixels 48.0 48.0 (frameScene badge other)
                    ])

        // 3. Badge digits 0 to 9, each against the other nine.
        let digitScene (d: int) : Scene = { Nodes = ground a 16.0 16.0 :: Glyphs.digit d 5.0 3.0 p.Body }

        let digits =
            [ 0..9 ]
            |> List.map (fun d ->
                case
                    (sprintf "badge digit %d" d)
                    16.0
                    16.0
                    [ digitScene d ]
                    [ for other in [ 0..9 ] |> List.filter ((<>) d) -> sprintf "digit %d's samples" other, pixels 16.0 16.0 (digitScene other) ])

        // 4. Box edges: the five frames in their allegiance colours, against the strip moved one unit right.
        let stripScene (dx: float) : Scene =
            let glyphs =
                identities
                |> List.mapi (fun i identity ->
                    GlyphSpec.create GlyphFamily.unit { X = 24.0 + 48.0 * float i + dx; Y = 24.0 } { Width = 1; Height = 1 }
                    |> GlyphSpec.identity identity
                    |> GlyphSpec.sizeStep 3
                    |> Glyph.ofSpec idle)

            { Nodes = ground a 240.0 48.0 :: (Glyphs.scene a glyphs).Nodes }

        let edges = case "box edges of the five frames" 240.0 48.0 [ stripScene 0.0 ] [ "the strip one unit right", pixels 240.0 48.0 (stripScene 1.0) ]

        // 5. The dense board: amplifiers first, against bodies first.
        let columns, rows = 5, 3
        let boardWidth, boardHeight = 160.0, 104.0

        let boardGlyphs =
            [
                for r in 0 .. rows - 1 do
                    for c in 0 .. columns - 1 do
                        let index = r * columns + c

                        yield
                            GlyphSpec.create GlyphFamily.unit { X = 32.0 * float c + 16.0; Y = 32.0 * float r + 24.0 } { Width = 1; Height = 1 }
                            |> GlyphSpec.identity identities.[index % identities.Length]
                            |> GlyphSpec.team index
                            |> GlyphSpec.badged
                            |> GlyphSpec.amplifier AmplifierSlot.above (Amplifier.bar (0.25 + 0.25 * float ((r + c) % 3)))
                            |> Glyph.ofSpec idle
            ]

        let board: Scene = { Nodes = ground a boardWidth boardHeight :: (Glyphs.scene a boardGlyphs).Nodes }

        let bodiesFirst: Scene =
            let drawings = boardGlyphs |> List.map (Glyphs.draw a)
            { Nodes = ground a boardWidth boardHeight :: (drawings |> List.collect GlyphDrawing.body) @ (drawings |> List.collect GlyphDrawing.amplifiers) }

        let dense =
            case "dense board, amplifiers before bodies, badges inside" boardWidth boardHeight [ board ] [ "the board drawn bodies first", pixels boardWidth boardHeight bodiesFirst ]
            |> withDrawn [ drawn "the board drawn bodies first" boardWidth boardHeight board bodiesFirst ]

        // 6. The theme switch A, B, A with the focus ring showing.
        let samples = Glyphs.samples { X = 8.0; Y = 16.0 }

        let withCues (theme: Theme) : Scene =
            let frame = GlyphDrawing.frame (Glyphs.draw theme samples.Head)
            let objects = [ { Id = "first"; Label = "first"; Bounds = frame; Shape = SelectableShape.Box } ]
            let context = CueContext.create (InteractionCues.create (Some "first") None true) objects 1.0
            { Nodes = (starter theme).Nodes @ Glyphs.cues (fun () -> theme) context }

        let switch =
            case
                (sprintf "theme switch %s, %s, %s with the focus ring" (Theme.name a) (Theme.name b) (Theme.name a))
                starterWidth
                starterHeight
                [ withCues a; withCues b; withCues a ]
                [ sprintf "%s's pixels" (Theme.name b), pixels starterWidth starterHeight (withCues b) ]

        // 7. D12: two teams that share an allegiance, in a monochrome theme, told apart by their badges.
        let mono = monochrome a

        let teamScene (team: int) : Scene =
            let glyph =
                GlyphSpec.create GlyphFamily.unit { X = 24.0; Y = 24.0 } { Width = 1; Height = 1 }
                |> GlyphSpec.identity Identity.Own
                |> GlyphSpec.team team
                |> GlyphSpec.badged
                |> Glyph.ofSpec idle

            { Nodes = ground mono 48.0 48.0 :: (Glyphs.scene mono [ glyph ]).Nodes }

        let teams =
            case "two teams sharing an allegiance, monochrome: team 1 against team 2" 48.0 48.0 [ teamScene 0 ] [ "team 2's samples", pixels 48.0 48.0 (teamScene 1) ]

        // 8. Erratum E1: team 0 on the own allegiance (one colour in classic) shows a visible ring.
        let single (theme: Theme) (glyph: GlyphSpec) : Scene =
            { Nodes = ground theme 48.0 48.0 :: (Glyphs.scene theme [ Glyph.ofSpec idle glyph ]).Nodes }

        let own = GlyphSpec.create GlyphFamily.unit { X = 24.0; Y = 24.0 } { Width = 1; Height = 1 } |> GlyphSpec.identity Identity.Own
        let ringed = single a (own |> GlyphSpec.team 0)
        let plain = single a own

        let ring =
            case "team 0 on the own allegiance shows a visible ring" 48.0 48.0 [ ringed ] [ "the unit with no team", pixels 48.0 48.0 plain ]
            |> withDrawn [ drawn "the unit with no team" 48.0 48.0 ringed plain ]

        // 9. Erratum E6: a unit and a structure of the same identity, size and badge differ, monochrome with no icon.
        let shaped (family: GlyphFamily) =
            single mono (GlyphSpec.create family { X = 24.0; Y = 24.0 } { Width = 1; Height = 1 } |> GlyphSpec.identity Identity.Own |> GlyphSpec.team 0 |> GlyphSpec.badged)

        let family =
            case
                "a unit and a structure of one identity, size and badge, monochrome"
                48.0
                48.0
                [ shaped GlyphFamily.unit ]
                [ "the structure's samples", pixels 48.0 48.0 (shaped GlyphFamily.structure) ]
            |> withDrawn [ drawn "the structure" 48.0 48.0 (shaped GlyphFamily.unit) (shaped GlyphFamily.structure) ]

        starters @ identityCases false @ identityCases true @ digits @ [ edges; dense; switch; teams; ring; family ]
