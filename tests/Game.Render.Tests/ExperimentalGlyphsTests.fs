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

let private frameNodesAt identity (centre: Point) size =
    SceneNode.Rectangle((0.0, 0.0, 96.0, 96.0), Glyphs.Theme.standard.Ground)
    :: Glyphs.identityFrame identity centre size Glyphs.Theme.standard.Body

let private frameNodes identity size = frameNodesAt identity { X = 48.0; Y = 48.0 } size

/// The pixels decided for `a` (by `samples`) that `b`'s drawing shows in another colour, on a 96 x 96 board.
let private differingWith (samples: float -> float -> Scene -> PixelExpectation list) (a: SceneNode list) (b: SceneNode list) =
    samples 96.0 96.0 { Nodes = a }
    |> List.filter (fun e ->
        match colourAt b (int (floor e.Point.X)) (int (floor e.Point.Y)) with
        | Some other -> other <> e.Colour
        | None -> false)
    |> List.length

/// With today's kit samples.
let private differing a b = differingWith ScenePixels.expectations a b

/// With ADR D6's corner and quarter-edge samples (the experimental prototype).
let private differingD6 a b = differingWith Glyphs.identitySamples a b

/// How many pixels the kit decides for frame `a` that frame `b`'s drawing shows in another colour.
let private control a b size = differing (frameNodes a size) (frameNodes b size)

let private controlD6 a b size = differingD6 (frameNodes a size) (frameNodes b size)

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

            test "the five identity frames are bars only; every pair differs both ways with D6's samples, and every pair but Own/Ally with today's" {
                for size in [ 12.0; 16.0; 24.0; 32.0 ] do
                    for a in Glyphs.identities do
                        let nodes = Glyphs.identityFrame a { X = 48.0; Y = 48.0 } size Glyphs.Theme.standard.Body
                        Expect.isTrue (nodes |> List.forall (function SceneNode.Rectangle _ -> true | _ -> false)) (sprintf "%A is bars" a)

                        for b in Glyphs.identities |> List.filter ((<>) a) do
                            Expect.isGreaterThanOrEqual (controlD6 a b size) 2 (sprintf "size %g, D6 samples: %A read against %A" size a b)

                            // Own's middle-of-edge samples cannot see Ally's quarter-point holes; that is what D6 adds.
                            if set [ a; b ] <> set [ Glyphs.Own; Glyphs.Ally ] then
                                Expect.isGreaterThanOrEqual (control a b size) 2 (sprintf "size %g, today's samples: %A read against %A" size a b)
            }

            test "each identity frame passes the box-edge check on its drawn bounding box, and off-grid centres snap to the same frame" {
                let ink = Glyphs.Theme.standard.Body
                for size in [ 12.0; 16.0; 24.0; 32.0 ] do
                    for identity in Glyphs.identities do
                        let nodes = frameNodes identity size
                        let box = Glyphs.boundsOf (List.tail nodes)
                        let midCol = int (floor (box.X + box.Width / 2.0))
                        let midRow = int (floor (box.Y + box.Height / 2.0))
                        let l, t = int box.X, int box.Y
                        let r, b = int (box.X + box.Width), int (box.Y + box.Height)
                        // The first whole pixel inside each edge is the frame; the first outside is not.
                        for (inside, outside, edge) in
                            [ (l, midRow), (l - 1, midRow), "left"; (r - 1, midRow), (r, midRow), "right"
                              (midCol, t), (midCol, t - 1), "top"; (midCol, b - 1), (midCol, b), "bottom" ] do
                            Expect.equal (colourAt nodes (fst inside) (snd inside)) (Some ink) (sprintf "%A %g: just inside the %s edge" identity size edge)
                            Expect.notEqual (colourAt nodes (fst outside) (snd outside)) (Some ink) (sprintf "%A %g: just outside the %s edge" identity size edge)

                        for offset in [ 0.25; 0.4; -0.3 ] do
                            let shifted = frameNodesAt identity { X = 48.0 + offset; Y = 48.0 + offset } size
                            Expect.equal shifted nodes (sprintf "%A %g: a centre off by %g snaps to the whole-unit frame" identity size offset)
            }

            test "the drawn bounding boxes at a nominal 24 units: Enemy 20 x 20, Unknown 30 x 30" {
                let ink = Glyphs.Theme.standard.Body
                let boxOf identity = Glyphs.boundsOf (Glyphs.identityFrame identity { X = 48.0; Y = 48.0 } 24.0 ink)
                Expect.equal ((boxOf Glyphs.Enemy).Width, (boxOf Glyphs.Enemy).Height) (20.0, 20.0) "Enemy"
                Expect.equal ((boxOf Glyphs.Unknown).Width, (boxOf Glyphs.Unknown).Height) (30.0, 30.0) "Unknown"
            }

            test "the nine index badges are at least 7 units tall, and D6's samples tell all 36 pairs apart in both directions" {
                let ink = Glyphs.Theme.standard.Body
                let ground = SceneNode.Rectangle((0.0, 0.0, 96.0, 96.0), Glyphs.Theme.standard.Ground)
                let badgeNodes digit = ground :: Glyphs.badge digit { X = 40.0; Y = 40.0 } 2.0 ink
                for digit in 1 .. 9 do
                    Expect.isGreaterThanOrEqual (Glyphs.boundsOf (Glyphs.badge digit { X = 40.0; Y = 40.0 } 2.0 ink)).Height 7.0 (sprintf "%d is tall enough" digit)
                let pairs = [ for a in 1 .. 9 do for b in a + 1 .. 9 -> a, b ]
                Expect.equal pairs.Length 36 "36 pairs"
                for a, b in pairs do
                    Expect.isGreaterThanOrEqual (differingD6 (badgeNodes a) (badgeNodes b)) 1 (sprintf "%d read against %d" a b)
                    Expect.isGreaterThanOrEqual (differingD6 (badgeNodes b) (badgeNodes a)) 1 (sprintf "%d read against %d" b a)
            }

            test "on D9's dense board, amplifiers draw before bodies and badges sit inside frames, so every frame's and badge's decided pixels and box edges hold" {
                let theme = Glyphs.Theme.standard
                let board = Glyphs.denseBoard theme 5 3 true
                let control = Glyphs.denseBoard theme 5 3 false
                let width, height = 160.0, 104.0
                let decided = ScenePixels.expectations width height board
                // Each frame with its badge on its own: every pixel it decides (with D6's samples) in its ink or the badge
                // ink must show the same on the dense board.
                let glyphs =
                    [ for r in 0 .. 2 do
                        for c in 0 .. 4 do
                            let identity = Glyphs.identities.[(r * 5 + c) % 5]
                            let ink = if (r + c) % 2 = 0 then theme.Body else Glyphs.Theme.team theme (r + c)
                            let centre = { X = 32.0 * float c + 16.0; Y = 32.0 * float r + 24.0 }
                            let withBadge, _ = Glyphs.identityFrameWithBadge identity centre 24.0 ink theme.BadgeInk (Some(r * 5 + c))
                            yield identity, ink, Glyphs.identityFrame identity centre 24.0 ink, withBadge ]
                let bodyRects = glyphs |> List.map (fun (identity, ink, frame, _) -> identity, ink, frame)
                // Some amplifier really overlaps another tile's frame, or the case proves nothing.
                let overlaps =
                    [ for (_, _, a) in bodyRects do
                        for (_, _, b) in bodyRects do
                            if a <> b then
                                let fa = Glyphs.boundsOf a
                                let fb = Glyphs.boundsOf b
                                let bar = { X = fa.X; Y = fa.Y - 14.0; Width = 24.0; Height = 6.0 }
                                if bar.X < fb.X + fb.Width && fb.X < bar.X + bar.Width && bar.Y < fb.Y + fb.Height && fb.Y < bar.Y + bar.Height then
                                    yield () ]
                Expect.isNonEmpty overlaps "a status bar reaches into a neighbouring frame"
                for identity, ink, nodes in bodyRects do
                    let box = Glyphs.boundsOf nodes
                    let midCol = int (floor (box.X + box.Width / 2.0))
                    let midRow = int (floor (box.Y + box.Height / 2.0))
                    for col, row in [ int box.X, midRow; int (box.X + box.Width) - 1, midRow; midCol, int box.Y; midCol, int (box.Y + box.Height) - 1 ] do
                        Expect.equal (colourAt board.Nodes col row) (Some ink) (sprintf "%A at (%d, %d) shows its frame" identity col row)
                for identity, ink, _, withBadge in glyphs do
                    let mine = Glyphs.identitySamples width height { Nodes = SceneNode.Rectangle((0.0, 0.0, width, height), theme.Ground) :: withBadge }
                    let badgePixels = mine |> List.filter (fun e -> e.Colour = theme.BadgeInk)
                    Expect.isNonEmpty badgePixels (sprintf "%A's badge decides pixels" identity)
                    for e in mine do
                        if e.Colour = ink || e.Colour = theme.BadgeInk then
                            Expect.equal (colourAt board.Nodes (int (floor e.Point.X)) (int (floor e.Point.Y))) (Some e.Colour) (sprintf "%A's decided pixel %A" identity e.Point)
                Expect.isNonEmpty decided "the dense board decides pixels"
                let wrongInControl =
                    decided |> List.filter (fun e -> colourAt control.Nodes (int (floor e.Point.X)) (int (floor e.Point.Y)) <> Some e.Colour)
                Expect.isNonEmpty wrongInControl "control: with bodies drawn first, the bars cover frames and the pixels differ"
            }

            test "inside badges: drawn at 22 units and above, omitted below with a diagnostic, 3:1 on every fill, and the frame matrix holds with badges drawn" {
                for theme in themes do
                    for name, fill in ("body", theme.Body) :: (theme.Teams |> List.mapi (fun i c -> sprintf "team %d" i, c)) do
                        let ratio = Glyphs.Theme.contrast theme.BadgeInk fill
                        Expect.isGreaterThanOrEqual ratio 3.0 (sprintf "%s badge ink on %s: %.2f:1" theme.Name name ratio)
                let theme = Glyphs.Theme.standard
                for identity in Glyphs.identities do
                    let below, why = Glyphs.identityFrameWithBadge identity { X = 48.0; Y = 48.0 } 21.0 theme.Body theme.BadgeInk (Some 0)
                    Expect.equal below (Glyphs.identityFrame identity { X = 48.0; Y = 48.0 } 21.0 theme.Body) (sprintf "%A: no badge at 21" identity)
                    Expect.isSome why (sprintf "%A: a diagnostic at 21" identity)
                    let at22, none = Glyphs.identityFrameWithBadge identity { X = 48.0; Y = 48.0 } 22.0 theme.Body theme.BadgeInk (Some 0)
                    Expect.isNone none (sprintf "%A: no diagnostic at 22" identity)
                    // The badge lies wholly on the frame's ink, with a unit of margin: every badge cell's neighbourhood is frame.
                    let frame = Glyphs.identityFrame identity { X = 48.0; Y = 48.0 } 22.0 theme.Body
                    for col in 44 .. 51 do
                        for row in 42 .. 53 do
                            Expect.equal (colourAt (SceneNode.Rectangle((0.0, 0.0, 96.0, 96.0), theme.Ground) :: frame) col row) (Some theme.Body) (sprintf "%A: (%d, %d) under the badge and its margin is frame" identity col row)
                    Expect.isGreaterThan at22.Length frame.Length (sprintf "%A: the badge is drawn at 22" identity)
                let framed identity size team =
                    SceneNode.Rectangle((0.0, 0.0, 96.0, 96.0), theme.Ground)
                    :: fst (Glyphs.identityFrameWithBadge identity { X = 48.0; Y = 48.0 } size theme.Body theme.BadgeInk team)
                for size in [ 24.0; 32.0 ] do
                    for i, a in List.indexed Glyphs.identities do
                        for b in List.skip (i + 1) Glyphs.identities do
                            let bare = min (controlD6 a b size) (controlD6 b a size)
                            for ta in [ 0; 7 ] do
                                for tb in [ 0; 7 ] do
                                    let both = min (differingD6 (framed a size (Some ta)) (framed b size (Some tb))) (differingD6 (framed b size (Some tb)) (framed a size (Some ta)))
                                    Expect.isGreaterThanOrEqual both bare (sprintf "size %g: %A/%A with badges %d/%d keep the bare frames' %d" size a b (ta + 1) (tb + 1) bare)
            }

            test "today a text node drawn last leaves no pixel of the board decided (the gap ADR D6 closes)" {
                let board = Glyphs.proofScene Glyphs.Theme.standard Glyphs.Theme.colourBlindSafe
                let label = SceneNode.Text((240.0, 120.0), "x", Glyphs.Theme.standard.Focus)
                let withLabel: Scene = { Nodes = board.Nodes @ [ label ] }
                Expect.isNonEmpty (ScenePixels.expectations Glyphs.proofWidth Glyphs.proofHeight board) "the board alone is decided"
                Expect.isEmpty (ScenePixels.expectations Glyphs.proofWidth Glyphs.proofHeight withLabel) "one text node undecides it all"
            }
        ]
