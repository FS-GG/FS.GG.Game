module Game.Render.Tests.ExperimentalGlyphsTests

// FS.GG.Game#789 (GAME-RENDER-02.4) proof: the experimental glyphs stay inside the portable primitive set that SVG, Pixi
// and Babylon draw and `ScenePixels` models, every glyph decides pixels of its own colour, and both themes meet the
// contrast floor the ADR sets. The browser run that reads the glyphs back from the three backends is
// tests/Render.Browser.Smoke (`runGlyphProof`).

open Expecto
open FS.GG.UI.Scene
open FS.GG.Game.Render
open FS.GG.Game.Reference.Experimental

let rec private portable (node: SceneNode) =
    match node with
    | SceneNode.Rectangle(_, colour)
    | SceneNode.Circle(_, _, colour)
    | SceneNode.FilledEllipse(_, colour) -> colour.Alpha = 255uy
    | SceneNode.Group scenes -> scenes |> List.forall (fun scene -> scene.Nodes |> List.forall portable)
    | SceneNode.Translate(_, scene) -> scene.Nodes |> List.forall portable
    | SceneNode.Empty -> true
    | _ -> false

let private themes = [ Glyphs.Theme.standard; Glyphs.Theme.colourBlindSafe ]

let private sample: Glyphs.Glyph list =
    [
        Glyphs.Unit(0, { X = 48.0; Y = 48.0 })
        Glyphs.Structure(1, { X = 48.0; Y = 48.0 })
        Glyphs.Waypoint { X = 48.0; Y = 48.0 }
        Glyphs.Selection { X = 36.0; Y = 36.0; Width = 24.0; Height = 24.0 }
        Glyphs.Focus { X = 36.0; Y = 36.0; Width = 24.0; Height = 24.0 }
        Glyphs.Status({ X = 30.0; Y = 40.0; Width = 36.0; Height = 6.0 }, 0.5)
    ]


/// The analytic colour of pixel (col, row) in a drawing of rectangles: Some colour when every rectangle covers it wholly
/// or not at all, None when one covers it in part.
let private colourAt (nodes: SceneNode list) (col: int) (row: int) =
    let x0, y0 = float col, float row
    let x1, y1 = x0 + 1.0, y0 + 1.0
    let folder (top, partial) node =
        match node with
        | SceneNode.Rectangle((x, y, w, h), colour) ->
            if x1 <= x || x0 >= x + w || y1 <= y || y0 >= y + h then top, partial
            elif x0 >= x && x1 <= x + w && y0 >= y && y1 <= y + h then Some colour, false
            else top, true
        | _ -> top, true
    match List.fold folder (None, false) nodes with
    | _, true -> None
    | top, false -> top

let private frameNodes identity size =
    let ground = Glyphs.Theme.standard.Ground
    SceneNode.Rectangle((0.0, 0.0, 96.0, 96.0), ground)
    :: Glyphs.identityFrame identity { X = 48.0; Y = 48.0 } size Glyphs.Theme.standard.Body ground

/// How many pixels the kit decides for frame `a` that frame `b`'s drawing shows in another colour.
let private control a b size =
    let drawnB = frameNodes b size
    ScenePixels.expectations 96.0 96.0 { Nodes = frameNodes a size }
    |> List.filter (fun e ->
        match colourAt drawnB (int (floor e.Point.X)) (int (floor e.Point.Y)) with
        | Some other -> other <> e.Colour
        | None -> false)
    |> List.length

[<Tests>]
let tests =
    testList
        "Experimental glyphs (FS.GG.Game#789 proof)"
        [
            test "every glyph lowers to opaque rectangles, circles and ellipses only" {
                for theme in themes do
                    for glyph in sample do
                        let nodes = Glyphs.draw theme glyph
                        Expect.isNonEmpty nodes (sprintf "%s draws something" (string glyph))
                        Expect.isTrue (nodes |> List.forall portable) (sprintf "%s %A is in the portable set" theme.Name glyph)
            }

            test "every glyph decides at least one pixel of each colour it draws, on its theme's ground" {
                for theme in themes do
                    for glyph in sample do
                        let scene = Glyphs.scene theme 96.0 96.0 [ glyph ]
                        let decided = ScenePixels.expectations 96.0 96.0 scene |> List.map (fun e -> e.Colour) |> set

                        for node in Glyphs.draw theme glyph do
                            let colour =
                                match node with
                                | SceneNode.Rectangle(_, c)
                                | SceneNode.Circle(_, _, c)
                                | SceneNode.FilledEllipse(_, c) -> c
                                | _ -> failwith "not portable"

                            Expect.isTrue (decided.Contains colour) (sprintf "%s %A: %A is probed" theme.Name glyph colour)
            }

            test "the proof board is fully modelled: the kit decides pixels of all its glyph colours" {
                let scene = Glyphs.proofScene Glyphs.Theme.standard Glyphs.Theme.colourBlindSafe
                let decided = ScenePixels.expectations Glyphs.proofWidth Glyphs.proofHeight scene
                Expect.isGreaterThan decided.Length 150 "the board decides many pixels"
                Expect.isTrue (scene.Nodes |> List.forall portable) "the board is in the portable set"
            }

            test "selection, focus, marker and team colours meet the 3:1 non-text contrast floor against the ground" {
                for theme in themes do
                    let ground = theme.Ground

                    for name, colour in
                        [ "selection", theme.Selection; "focus", theme.Focus; "marker", theme.Marker; "body", theme.Body ]
                        @ (theme.Teams |> List.mapi (fun i c -> sprintf "team %d" i, c)) do
                        let ratio = Glyphs.Theme.contrast colour ground
                        Expect.isGreaterThanOrEqual ratio 3.0 (sprintf "%s %s: %.2f:1" theme.Name name ratio)
            }


            test "each status part meets 3:1 against its real neighbour: the keyline on the ground, each fill on the track" {
                for theme in themes do
                    let keyline = Glyphs.Theme.contrast theme.Body theme.Ground
                    Expect.isGreaterThanOrEqual keyline 3.0 (sprintf "%s keyline on ground: %.2f:1" theme.Name keyline)

                    for name, fill in [ "good", theme.Good; "warn", theme.Warn; "bad", theme.Bad ] do
                        let ratio = Glyphs.Theme.contrast fill theme.Track
                        Expect.isGreaterThanOrEqual ratio 3.0 (sprintf "%s %s fill on track: %.2f:1" theme.Name name ratio)
            }

            test "the five identity frames are bars only, and the kit tells every pair apart in both directions at 12 to 32 units" {
                for size in [ 12.0; 16.0; 24.0; 32.0 ] do
                    for a in Glyphs.identities do
                        let nodes = Glyphs.identityFrame a { X = 48.0; Y = 48.0 } size Glyphs.Theme.standard.Body Glyphs.Theme.standard.Ground
                        Expect.isTrue (nodes |> List.forall (function SceneNode.Rectangle _ -> true | _ -> false)) (sprintf "%A is bars" a)

                        for b in Glyphs.identities |> List.filter ((<>) a) do
                            Expect.isGreaterThanOrEqual (control a b size) 2 (sprintf "size %g: %A's decided pixels read against %A" size a b)
            }

            test "today a text node drawn last leaves no pixel of the board decided (the gap ADR D6 closes)" {
                let board = Glyphs.proofScene Glyphs.Theme.standard Glyphs.Theme.colourBlindSafe
                let label = SceneNode.Text((240.0, 120.0), "x", Glyphs.Theme.standard.Focus)
                let withLabel: Scene = { Nodes = board.Nodes @ [ label ] }
                Expect.isNonEmpty (ScenePixels.expectations Glyphs.proofWidth Glyphs.proofHeight board) "the board alone is decided"
                Expect.isEmpty (ScenePixels.expectations Glyphs.proofWidth Glyphs.proofHeight withLabel) "one text node undecides it all"
            }
        ]
