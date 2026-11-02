module Game.Render.Tests.GlyphsTests

// ADR GAME-RENDER-02.4 (FS.GG.Game#789): the glyph library in the portable profile and the kit's glyph section. The
// browser run of `GlyphConformance.cases` on SVG, Pixi and Babylon is tests/Render.Browser.Smoke (`runGlyphSection`).

open Expecto
open FS.GG.UI.Scene
open FS.GG.Game.Render

let private idle = GlyphState.idle
let private one: Size = { Width = 1; Height = 1 }
let private identities = [ Identity.Own; Identity.Ally; Identity.Neutral; Identity.Enemy; Identity.Unknown ]
let private theme = Theme.classic

let private unitAt (x: float) (y: float) identity =
    GlyphSpec.create GlyphFamily.unit { X = x; Y = y } one |> GlyphSpec.identity identity

/// A theme whose cell gives a frame of `w` units at size step 3 (`w = floor (cell x 0.75)`).
let private themeWithFrame (w: float) =
    let cell = System.Math.Ceiling(w / 0.75)

    match Theme.create (ThemeSeed.classic |> ThemeSeed.number "cell" cell) [ ThemeAlgorithm.standard ] [] with
    | Ok t -> t
    | Error e -> failtestf "%A" e

let rec private portable (node: SceneNode) =
    match node with
    | SceneNode.Rectangle(_, colour)
    | SceneNode.Circle(_, _, colour)
    | SceneNode.FilledEllipse(_, colour) -> colour.Alpha = 255uy
    | _ -> false

/// The kit's decision for pixel (col, row) of a scene of rectangles, circles and ellipses, at any pixel (not only at the
/// kit's samples): the colour of the last node that wholly covers it, the clear colour where none touches it, or None
/// when a node covers it in part after the last full one.
let private shownAt (nodes: SceneNode list) (col: int) (row: int) : Color option =
    let x0, y0 = float col, float row
    let x1, y1 = x0 + 1.0, y0 + 1.0
    let eps = 1e-9

    let rec cover node =
        match node with
        | SceneNode.Circle(centre, r, c) -> cover (SceneNode.FilledEllipse({ X = centre.X - r; Y = centre.Y - r; Width = 2.0 * r; Height = 2.0 * r }, c))
        | SceneNode.Rectangle((x, y, w, h), c) ->
            if x1 <= x + eps || x0 >= x + w - eps || y1 <= y + eps || y0 >= y + h - eps then None
            elif x0 >= x - eps && x1 <= x + w + eps && y0 >= y - eps && y1 <= y + h + eps then Some(Some c)
            else Some None
        | SceneNode.FilledEllipse(b, c) ->
            let cx, cy, rx, ry = b.X + b.Width / 2.0, b.Y + b.Height / 2.0, b.Width / 2.0, b.Height / 2.0
            let ux0, ux1, uy0, uy1 = (x0 - cx) / rx, (x1 - cx) / rx, (y0 - cy) / ry, (y1 - cy) / ry
            let nx = if ux0 > 0.0 then ux0 elif ux1 < 0.0 then ux1 else 0.0
            let ny = if uy0 > 0.0 then uy0 elif uy1 < 0.0 then uy1 else 0.0
            if nx * nx + ny * ny >= 1.0 - eps then None
            else
                let fx, fy = max (abs ux0) (abs ux1), max (abs uy0) (abs uy1)
                Some(if fx * fx + fy * fy <= 1.0 - 1e-6 then Some c else None)
        | _ -> Some None

    let folder (top: Color option, unsure: bool, touched: bool) node =
        match cover node with
        | None -> top, unsure, touched
        | Some(Some c) -> Some c, false, true
        | Some None -> top, true, true

    match List.fold folder (None, false, false) nodes with
    | _, true, _ -> None
    | Some c, false, _ -> Some c
    | None, false, _ -> Some { Red = 0uy; Green = 0uy; Blue = 0uy; Alpha = 255uy }

/// Every unit pixel of `box` is wholly inside one of `rects`.
let private onInk (rects: SceneNode list) (box: Rect) =
    let covers (x: float) (y: float) =
        rects
        |> List.exists (function
            | SceneNode.Rectangle((rx, ry, rw, rh), _) -> x >= rx && x + 1.0 <= rx + rw && y >= ry && y + 1.0 <= ry + rh
            | _ -> false)

    [ for y in int box.Y .. int (box.Y + box.Height) - 1 do
          for x in int box.X .. int (box.X + box.Width) - 1 -> covers (float x) (float y) ]
    |> List.forall id

/// The smallest size from which `fits` holds at every size up to 64.
let private smallestFrom (fits: float -> bool) =
    [ 6..64 ] |> List.filter (fun w -> [ w..64 ] |> List.forall (fun v -> fits (float v))) |> List.head |> float

/// Pixels decided for `a` that `b`'s drawing shows in another colour.
let private differing w h (a: SceneNode list) (b: SceneNode list) =
    ScenePixels.identityExpectations w h { Nodes = a }
    |> List.filter (fun e ->
        match shownAt b (int (floor e.Point.X)) (int (floor e.Point.Y)) with
        | Some other -> other <> e.Colour
        | None -> false)
    |> List.length

[<Tests>]
let tests =
    testList
        "Glyphs (FS.GG.Game#789)"
        [
            test "every starter glyph lowers to opaque rectangles, circles and ellipses only" {
                let drawings = Glyphs.samples { X = 0.0; Y = 0.0 } |> List.map (Glyphs.draw theme)

                for d in drawings do
                    Expect.isTrue (GlyphDrawing.nodes d |> List.forall portable) "portable"

                Expect.equal (GlyphDrawing.nodes drawings.Head) (GlyphDrawing.amplifiers drawings.Head @ GlyphDrawing.body drawings.Head @ GlyphDrawing.brackets drawings.Head) "nodes are the passes in order"
            }

            test "frames: drawn bounding boxes at a nominal 24 (Own and Ally 24 x 16, Neutral 24 x 24, Enemy 20 x 20, Unknown 30 x 30), snapped to whole units" {
                let sizes = identities |> List.map (fun i -> GlyphDrawing.frame (Glyphs.draw theme (Glyph.ofSpec idle (unitAt 50.0 50.0 i))))
                Expect.equal (sizes |> List.map (fun r -> r.Width, r.Height)) [ 24.0, 16.0; 24.0, 16.0; 24.0, 24.0; 20.0, 20.0; 30.0, 30.0 ] "boxes"
                Expect.equal (Glyphs.frameSize theme (unitAt 0.0 0.0 Identity.Own)) 24.0 "frame size at cell 32"
                Expect.equal [ for s in 0..3 -> Glyphs.frameSize theme (unitAt 0.0 0.0 Identity.Own |> GlyphSpec.sizeStep s) ] [ 16.0; 18.0; 21.0; 24.0 ] "size steps"

                for i in identities do
                    let snapped = GlyphDrawing.body (Glyphs.draw theme (Glyph.ofSpec idle (unitAt 50.4 49.6 i)))
                    let exact = GlyphDrawing.body (Glyphs.draw theme (Glyph.ofSpec idle (unitAt 50.0 50.0 i)))
                    Expect.equal snapped exact (sprintf "%A: an off-grid anchor snaps half up" i)
            }

            test "hit bounds are the frame only: the same whether selected, stale, badged or amplified" {
                for i in identities do
                    let plain = Glyphs.draw theme (Glyph.ofSpec idle (unitAt 50.0 50.0 i))
                    let busy =
                        unitAt 50.0 50.0 i
                        |> GlyphSpec.team 2
                        |> GlyphSpec.badged
                        |> GlyphSpec.amplifier AmplifierSlot.above (Amplifier.bar 0.4)
                        |> GlyphSpec.amplifier AmplifierSlot.below (Amplifier.pips 2 4)
                        |> Glyph.ofSpec (GlyphState.idle |> GlyphState.selected true |> GlyphState.stale true)
                        |> Glyphs.draw theme

                    Expect.equal (GlyphDrawing.frame busy) (GlyphDrawing.frame plain) (sprintf "%A frame" i)
                    Expect.equal (GlyphDrawing.hit busy) SelectableShape.Box "box hit"
                    let extent = GlyphDrawing.extent busy
                    let frame = GlyphDrawing.frame busy
                    Expect.isTrue (extent.X <= frame.X - 8.0 && extent.Y < frame.Y - 8.0 && extent.Y + extent.Height > frame.Y + frame.Height + 8.0) "the extent holds the reserve and the amplifiers"
            }

            test "ADR D1's identity matrix holds for the library's frames with the kit's identity samples, 12 to 32 units" {
                let table =
                    [
                        12.0, [ 4; 6; 16; 16; 10; 24; 28; 16; 16; 28 ]
                        16.0, [ 4; 6; 16; 16; 10; 40; 28; 16; 16; 28 ]
                        24.0, [ 4; 6; 16; 16; 10; 54; 28; 16; 16; 28 ]
                        32.0, [ 4; 6; 16; 16; 10; 93; 28; 16; 16; 28 ]
                    ]

                let frame i size = SceneNode.Rectangle((0.0, 0.0, 96.0, 96.0), theme.Palette.Ground) :: Glyphs.identityFrame i 48.0 48.0 size theme.Palette.Body

                for size, row in table do
                    let measured =
                        [
                            for x in 0..4 do
                                for y in x + 1 .. 4 do
                                    let a, b = identities.[x], identities.[y]
                                    yield min (differing 96.0 96.0 (frame a size) (frame b size)) (differing 96.0 96.0 (frame b size) (frame a size))
                        ]

                    Expect.equal measured row (sprintf "size %g" size)
            }

            test "box edges: drawn alone with no cue, the first pixel inside each edge midpoint is the frame and the first outside the ground" {
                for size in [ 12.0; 16.0; 24.0; 32.0 ] do
                    let t = themeWithFrame size

                    for i in identities do
                        let d = Glyphs.draw t (Glyph.ofSpec idle (unitAt 64.0 64.0 i))
                        let f = GlyphDrawing.frame d
                        let nodes = SceneNode.Rectangle((0.0, 0.0, 128.0, 128.0), t.Palette.Ground) :: GlyphDrawing.nodes d
                        let ink = Theme.allegiance i t
                        let midX = int (f.X + floor (f.Width / 2.0))
                        let midY = int (f.Y + floor (f.Height / 2.0))
                        let left, top, right, bottom = int f.X, int f.Y, int (f.X + f.Width) - 1, int (f.Y + f.Height) - 1

                        for (x, y), wanted in
                            [
                                (left, midY), ink
                                (left - 1, midY), t.Palette.Ground
                                (right, midY), ink
                                (right + 1, midY), t.Palette.Ground
                                (midX, top), ink
                                (midX, top - 1), t.Palette.Ground
                                (midX, bottom), ink
                                (midX, bottom + 1), t.Palette.Ground
                            ] do
                            Expect.equal (shownAt nodes x y) (Some wanted) (sprintf "%A at %g: (%d, %d)" i size x y)
            }

            test "team badge minimums, measured: one digit Neutral 12, Unknown 16, Own 18, Enemy 20, Ally 22; two digits Own 18, Neutral 16, Unknown 24, Enemy 28, Ally 33" {
                let measure number identity =
                    smallestFrom (fun w ->
                        let frame = Glyphs.identityFrame identity 100.0 100.0 w theme.Palette.Body
                        onInk frame (Glyphs.badgeBox number 100.0 100.0))

                let one = identities |> List.map (measure 8)
                let two = identities |> List.map (measure 88)
                Expect.equal one [ 18.0; 22.0; 12.0; 20.0; 16.0 ] "one digit: Own, Ally, Neutral, Enemy, Unknown"
                Expect.equal two [ 18.0; 33.0; 16.0; 28.0; 24.0 ] "two digits: Own, Ally, Neutral, Enemy, Unknown"
                Expect.equal (List.max one) Glyphs.minBadgeFrame "the one-digit minimum is the largest"
                Expect.equal (List.max two) Glyphs.minTwoDigitBadgeFrame "the two-digit minimum is the largest"
            }

            test "icon and badge side by side from 40 units: both lie wholly on frame ink for every identity" {
                for i in identities do
                    let from =
                        smallestFrom (fun w ->
                            let frame = Glyphs.identityFrame i 100.0 100.0 w theme.Palette.Body
                            let q = floor (w / 8.0)
                            onInk frame (Glyphs.badgeBox 8 (100.0 + q) 100.0) && onInk frame { X = 100.0 - q - 6.0; Y = 94.0; Width = 12.0; Height = 12.0 })

                    Expect.isLessThanOrEqual from Glyphs.minIconAndBadgeFrame (sprintf "%A fits side by side from %g" i from)
            }

            test "badges: drawn last in the body from 22 units, omitted below with a diagnostic; team numbers never wrap" {
                let badged size team = Glyphs.draw (themeWithFrame size) (unitAt 64.0 64.0 Identity.Neutral |> GlyphSpec.team team |> GlyphSpec.badged |> Glyph.ofSpec idle)
                let ink = theme.Palette.BadgeInk

                let at24 = badged 24.0 0
                let body = GlyphDrawing.body at24
                Expect.equal (body |> List.rev |> List.takeWhile (function SceneNode.Rectangle(_, c) -> c = ink | _ -> false) |> List.length) 2 "digit 1's two segments end the body"
                Expect.isEmpty (GlyphDrawing.diagnostics at24) "no diagnostic"

                let at21 = badged 21.0 0
                Expect.isFalse (GlyphDrawing.body at21 |> List.exists (function SceneNode.Rectangle(_, c) -> c = ink | _ -> false)) "omitted below 22"
                Expect.exists (GlyphDrawing.diagnostics at21) (fun d -> d.Contains "badge for team 1 omitted" && d.Contains "22") "with a diagnostic naming team and size"

                let ten24 = badged 24.0 9
                Expect.exists (GlyphDrawing.diagnostics ten24) (fun d -> d.Contains "team 10" && d.Contains "33") "team 10 at 24 is omitted: two digits need 33"
                let ten33 = badged 33.0 9
                let digits = GlyphDrawing.body ten33 |> List.filter (function SceneNode.Rectangle(_, c) -> c = ink | _ -> false)
                Expect.equal digits.Length (2 + 6) "'10': digit 1 (two segments) and digit 0 (six), never wrapped to '1'"
            }

            test "the icon slot is unavailable while a mandatory badge takes the inner centre (22 to 39 units), so a builder can refuse at build time" {
                for w, available in [ 18.0, true; 22.0, false; 24.0, false; 39.0, false; 40.0, true; 48.0, true ] do
                    let t = themeWithFrame w
                    let spec = unitAt 64.0 64.0 Identity.Own |> GlyphSpec.team 1 |> GlyphSpec.badged |> GlyphSpec.icon (IconRef.named "infantry")
                    Expect.equal (Glyphs.frameSize t spec) w "frame"
                    Expect.equal (Glyphs.iconSlotAvailable t spec) available (sprintf "at %g" w)
                    let drawn = Glyphs.draw t (Glyph.ofSpec idle spec)
                    let bare = Glyphs.draw t (Glyph.ofSpec idle (unitAt 64.0 64.0 Identity.Own |> GlyphSpec.team 1 |> GlyphSpec.badged))
                    let iconDrawn = (GlyphDrawing.body drawn).Length > (GlyphDrawing.body bare).Length
                    Expect.equal iconDrawn available (sprintf "icon drawn at %g" w)

                    if not available then
                        Expect.exists (GlyphDrawing.diagnostics drawn) (fun d -> d.StartsWith "icon slot unavailable") "diagnostic"

                Expect.isTrue (Glyphs.iconSlotAvailable theme (unitAt 0.0 0.0 Identity.Own |> GlyphSpec.icon (IconRef.named "infantry"))) "no badge, no conflict"
            }

            test "amplifiers sit outside the 8-unit reserve, brackets at its outer edge, and the cue reserve is 2 x max(band, 2 / minCssPerUnit) + gap + bracket" {
                Expect.equal (Glyphs.reserve theme) 8.0 "8 at classic"
                let d = Glyphs.draw theme (unitAt 64.0 64.0 Identity.Own |> GlyphSpec.amplifier AmplifierSlot.above (Amplifier.bar 0.5) |> GlyphSpec.amplifier AmplifierSlot.below (Amplifier.pips 3 5) |> Glyph.ofSpec (GlyphState.selected true idle))
                let f = GlyphDrawing.frame d
                let boxes nodes = nodes |> List.choose (function SceneNode.Rectangle((x, y, w, h), _) -> Some(x, y, w, h) | _ -> None)
                let amps = boxes (GlyphDrawing.amplifiers d)
                Expect.isTrue (amps |> List.forall (fun (_, y, _, h) -> y + h <= f.Y - 8.0 || y >= f.Y + f.Height + 8.0)) "every amplifier outside the reserve"
                let brackets = boxes (GlyphDrawing.brackets d)
                let l = brackets |> List.map (fun (x, _, _, _) -> x) |> List.min
                let r = brackets |> List.map (fun (x, _, w, _) -> x + w) |> List.max
                Expect.equal (l, r) (f.X - 8.0, f.X + f.Width + 8.0) "brackets end at the reserve's outer edge"
                let wide = match Theme.create (ThemeSeed.classic |> ThemeSeed.number "minCssPerUnit" 0.5) [ ThemeAlgorithm.standard ] [] with Ok t -> t | Error e -> failtestf "%A" e
                Expect.equal (Glyphs.reserve wide) 12.0 "at minCssPerUnit 0.5 the ring is 8"
            }

            test "the dense board keeps every frame's and every badge's decided pixels; its bodies-first control does not" {
                let cases = GlyphConformance.cases [ theme ]
                let dense = cases |> List.find (fun c -> c.Name.StartsWith "dense board")
                let board = (List.last dense.Scenes).Nodes
                Expect.isGreaterThan dense.Expected.Length 300 "decided pixels"

                // Each frame's and badge's own samples (drawn alone on the ground) hold on the board.
                let a = theme
                let idleGlyphs =
                    [ for r in 0..2 do
                          for c in 0..4 ->
                              let index = r * 5 + c
                              GlyphSpec.create GlyphFamily.unit { X = 32.0 * float c + 16.0; Y = 32.0 * float r + 24.0 } one
                              |> GlyphSpec.identity identities.[index % 5]
                              |> GlyphSpec.team index
                              |> GlyphSpec.badged
                              |> Glyph.ofSpec idle ]

                let mutable checkedPixels = 0

                for g in idleGlyphs do
                    let d = Glyphs.draw a g
                    let f = GlyphDrawing.frame d
                    let alone = ScenePixels.identityExpectations 160.0 104.0 { Nodes = SceneNode.Rectangle((0.0, 0.0, 160.0, 104.0), a.Palette.Ground) :: GlyphDrawing.body d }

                    for e in alone do
                        // The frame's and badge's ink: no amplifier may cover it (the ground inside a stepped frame's box may be).
                        if e.Point.X > f.X && e.Point.X < f.X + f.Width && e.Point.Y > f.Y && e.Point.Y < f.Y + f.Height && e.Colour <> a.Palette.Ground then
                            match shownAt board (int (floor e.Point.X)) (int (floor e.Point.Y)) with
                            | Some shown ->
                                checkedPixels <- checkedPixels + 1
                                Expect.equal shown e.Colour (sprintf "frame pixel (%g, %g)" e.Point.X e.Point.Y)
                            | None -> ()

                Expect.isGreaterThan checkedPixels 300 "frame and badge pixels checked"
                let control = snd dense.Controls.Head
                Expect.isTrue (control |> List.exists (fun e -> match shownAt board (int (floor e.Point.X)) (int (floor e.Point.Y)) with Some c -> c <> e.Colour | None -> false)) "the control differs"
            }

            test "the kit's glyph section: every case's control differs from its expected pixels, and the A, B, A switch shows A's pixels again" {
                let cases = GlyphConformance.cases [ theme ]
                Expect.equal cases.Length (2 + 5 + 5 + 10 + 7) "cases"
                Expect.equal (cases |> List.sumBy (fun c -> c.Controls.Length)) (2 + 20 + 20 + 90 + 7) "controls: 20 + 20 identity pairs and 90 digit pairs"
                Expect.equal (cases |> List.sumBy (fun c -> c.DrawnControls.Length)) 4 "drawn controls: dense board, ring, family, foot band"

                for c in cases do
                    for name, scene, own, differing in c.DrawnControls do
                        Expect.isGreaterThan differing 0 (sprintf "%s: drawn control %s differs" c.Name name)
                        let shown = own |> List.map (fun e -> e.Point) |> fun points -> List.zip own points
                        for e, _ in shown do
                            Expect.equal (shownAt scene.Nodes (int (floor e.Point.X)) (int (floor e.Point.Y))) (Some e.Colour) (sprintf "%s: control %s's own pixel" c.Name name)

                Expect.equal [ for m in [ 0; 1; 10; 21; 59; 251 ] -> GlyphConformance.drawnControlFloor m ] [ 0; 1; 9; 19; 54; 226 ] "the drawn-control floor is 90 %, rounded up"
                let _, _, _, denseDiffering = (cases |> List.find (fun c -> c.Name.StartsWith "dense board")).DrawnControls.Head
                Expect.isGreaterThanOrEqual (GlyphConformance.drawnControlFloor denseDiffering) 59 "the bodies-first board must differ in at least the reviewed 59 bar-over-frame pixels, floor included"

                for c in cases do
                    Expect.isNonEmpty c.Expected (sprintf "%s decides pixels" c.Name)

                    let subject = (List.last c.Scenes).Nodes

                    for e in c.Expected do
                        Expect.equal (shownAt subject (int (floor e.Point.X)) (int (floor e.Point.Y))) (Some e.Colour) (sprintf "%s: its own pixel (%g, %g)" c.Name e.Point.X e.Point.Y)

                    for name, control in c.Controls do
                        // What the subject draws, read at the control's pixels, as a backend's probe reads it.
                        let differs = control |> List.exists (fun e -> match shownAt subject (int (floor e.Point.X)) (int (floor e.Point.Y)) with Some shown -> shown <> e.Colour | None -> false)
                        Expect.isTrue differs (sprintf "%s: control %s differs" c.Name name)

                let switch = cases |> List.find (fun c -> c.Name.StartsWith "theme switch")
                let first = ScenePixels.identityExpectations switch.Width switch.Height switch.Scenes.Head |> List.map (fun e -> e.Point, e.Colour)
                Expect.equal (switch.Expected |> List.map (fun e -> e.Point, e.Colour)) first "A then B then A gives A's expectations again, cues included"
                Expect.equal switch.Scenes.Length 3 "three presents"
            }

            test "cues: the focus ring is two bands of at least 2 CSS px against the frame; hover is opt-in; the theme is read on every draw" {
                let frame = { X = 40.0; Y = 40.0; Width = 24.0; Height = 16.0 }
                let objects = [ { Id = "x"; Label = "x"; Bounds = frame; Shape = SelectableShape.Box }; { Id = "y"; Label = "y"; Bounds = { frame with X = 100.0 }; Shape = SelectableShape.Box } ]
                let context focused hovered visible css = CueContext.create (InteractionCues.create focused hovered visible) objects css
                let current = ref theme
                let read () = current.Value
                let widths nodes = nodes |> List.choose (function SceneNode.Rectangle((_, _, _, h), _) -> Some h | _ -> None)

                for css, band in [ 1.0, 2.0; 0.5, 4.0; 3.0, 2.0; 1.5, 2.0 ] do
                    let ring = Glyphs.cues read (context (Some "x") None true css)
                    Expect.equal ring.Length 8 "two tones of four bands"
                    Expect.equal (widths ring).Head band (sprintf "band at %g CSS px per unit" css)
                    Expect.isGreaterThanOrEqual (band * css) 2.0 "at least 2 CSS px"

                let ring = Glyphs.cues read (context (Some "x") None true 1.0)
                Expect.equal ring.Head (SceneNode.Rectangle((38.0, 38.0, 28.0, 2.0), theme.Palette.FocusInner)) "the inner tone touches the frame"
                Expect.isEmpty (Glyphs.cues read (context (Some "x") (Some "y") false 1.0)) "focus not visible, no hover by default"
                Expect.equal (Glyphs.cuesHover read (context None (Some "y") false 1.0)).Length 4 "hover outline when opted in"
                Expect.equal (Glyphs.cuesHover read (context (Some "x") (Some "x") true 1.0)).Length 8 "the ring covers hover on the same object"
                let both = Glyphs.cuesHover read (context (Some "x") (Some "y") true 1.0)
                Expect.equal both.Length 12 "hover on y, ring on x"
                Expect.equal (both |> List.take 4 |> List.forall (function SceneNode.Rectangle(_, c) -> c = theme.Palette.Hover | _ -> false)) true "hover draws before the ring"
                Expect.isEmpty (Glyphs.cues read (context (Some "gone") None true 1.0)) "an id not among the objects draws nothing"

                current.Value <- GlyphConformance.monochrome theme
                current.Value <- (match Theme.withOverrides [ ThemeOverride.colour "cue.focus.outer" { Red = 250uy; Green = 250uy; Blue = 210uy; Alpha = 255uy } ] theme with Ok t -> t | Error e -> failtestf "%A" e)
                let after = Glyphs.cues read (context (Some "x") None true 1.0)
                Expect.contains after (SceneNode.Rectangle((36.0, 36.0, 32.0, 2.0), { Red = 250uy; Green = 250uy; Blue = 210uy; Alpha = 255uy })) "a theme switch reaches the cues"
            }

            test "erratum E1: from the edge in, frame, body keyline, team colour, body, so team 0 on the own allegiance shows a ring" {
                let d = Glyphs.draw theme (unitAt 64.0 64.0 Identity.Own |> GlyphSpec.team 0 |> Glyph.ofSpec idle)
                let nodes = SceneNode.Rectangle((0.0, 0.0, 128.0, 128.0), theme.Palette.Ground) :: GlyphDrawing.body d
                let f = GlyphDrawing.frame d
                let row = int (f.Y + floor (f.Height / 2.0))
                let left = int f.X
                // At 24 units the band is 3: frame 52..55, keyline 55..58, ring 58..61, body from 61.
                let across = [ for x in left .. left + 11 -> shownAt nodes x row ]
                let own = Theme.allegiance Identity.Own theme
                Expect.notEqual own theme.Palette.Teams.Head "classic's team 0 is distinct from the own colour"
                Expect.equal across [ for i in 0..11 -> Some(if i < 3 then own elif i < 6 then theme.Palette.Body elif i < 9 then theme.Palette.Teams.Head else theme.Palette.Body) ] "frame, keyline, ring, body"
            }

            test "a team mark never changes the frame's shape: every node of a team-marked body lies on the bare frame's ink" {
                for i in identities do
                    for size in [ 12.0; 16.0; 22.0; 24.0; 32.0; 39.0; 48.0 ] do
                        let t = themeWithFrame size
                        for family in [ GlyphFamily.unit; GlyphFamily.structure ] do
                            let spec = GlyphSpec.create family { X = 64.0; Y = 64.0 } one |> GlyphSpec.identity i |> GlyphSpec.team 1 |> GlyphSpec.badged
                            let ink = Glyphs.identityFrame i 64.0 64.0 (Glyphs.frameSize t spec) t.Palette.Body
                            let body = GlyphDrawing.body (Glyphs.draw t (Glyph.ofSpec idle spec))
                            Expect.isTrue
                                (body |> List.forall (function SceneNode.Rectangle((x, y, w, h), _) -> onInk ink { X = x; Y = y; Width = w; Height = h } | _ -> true))
                                (sprintf "%A %s at %g" i (GlyphFamily.name family) size)
                            let bare = SceneNode.Rectangle((0.0, 0.0, 128.0, 128.0), t.Palette.Ground) :: ink
                            let marked = SceneNode.Rectangle((0.0, 0.0, 128.0, 128.0), t.Palette.Ground) :: body
                            // Every pixel the bare frame shows as ground stays ground.
                            for e in ScenePixels.identityExpectations 128.0 128.0 { Nodes = bare } do
                                if e.Colour = t.Palette.Ground then
                                    Expect.equal (shownAt marked (int (floor e.Point.X)) (int (floor e.Point.Y))) (Some t.Palette.Ground) (sprintf "%A %s at %g: (%g, %g)" i (GlyphFamily.name family) size e.Point.X e.Point.Y)
            }

            test "the foot band's team-colour area is at least half of Own's for every identity from 22 to 39 units, Enemy included" {
                for size in 22.0 .. 39.0 do
                    let t = themeWithFrame size
                    let team = t.Palette.Teams.[1]

                    let area i =
                        let body = GlyphDrawing.body (Glyphs.draw t (GlyphSpec.create GlyphFamily.structure { X = 64.0; Y = 64.0 } one |> GlyphSpec.identity i |> GlyphSpec.team 1 |> Glyph.ofSpec idle))
                        let nodes = SceneNode.Rectangle((0.0, 0.0, 128.0, 128.0), t.Palette.Ground) :: body
                        [ for y in 20..108 do for x in 20..108 do if shownAt nodes x y = Some team then yield 1 ] |> List.length

                    let own = area Identity.Own
                    Expect.isGreaterThan own 0 (sprintf "Own at %g" size)

                    for i in identities do
                        Expect.isGreaterThanOrEqual (float (area i)) (0.5 * float own) (sprintf "%A at %g against Own's %d" i size own)
            }

            test "erratum E6: a structure's team mark is a foot band, so a unit and a structure of one identity, size and badge differ, monochrome" {
                let mono = GlyphConformance.monochrome theme
                for i in identities do
                    for size in [ 22.0; 24.0; 32.0; 39.0 ] do
                        let t = match Theme.create (Theme.seed mono |> ThemeSeed.number "cell" (System.Math.Ceiling(size / 0.75))) [ ThemeAlgorithm.standard ] [] with Ok t -> t | Error e -> failtestf "%A" e
                        let of' family = SceneNode.Rectangle((0.0, 0.0, 128.0, 128.0), t.Palette.Ground) :: (Glyphs.scene t [ GlyphSpec.create family { X = 64.0; Y = 64.0 } one |> GlyphSpec.identity i |> GlyphSpec.team 0 |> GlyphSpec.badged |> Glyph.ofSpec idle ]).Nodes
                        let unit, structure = of' GlyphFamily.unit, of' GlyphFamily.structure
                        Expect.isGreaterThan (min (differing 128.0 128.0 unit structure) (differing 128.0 128.0 structure unit)) 0 (sprintf "%A at %g: both ways" i size)
                        let s = Glyphs.draw t (GlyphSpec.create GlyphFamily.structure { X = 64.0; Y = 64.0 } one |> GlyphSpec.identity i |> GlyphSpec.team 0 |> Glyph.ofSpec idle)
                        let f = GlyphDrawing.frame s
                        let ink = Glyphs.identityFrame i 64.0 64.0 (Glyphs.frameSize t (GlyphSpec.create GlyphFamily.structure { X = 64.0; Y = 64.0 } one)) t.Palette.Body
                        Expect.isTrue
                            (GlyphDrawing.body s |> List.forall (function SceneNode.Rectangle((x, y, w, h), _) -> onInk ink { X = x; Y = y; Width = w; Height = h } | _ -> true))
                            (sprintf "%A at %g: the foot band lies inside the frame" i size)
                        ignore f
            }

            test "D12: in a monochrome theme two teams that share an allegiance differ only by their badges, and identity frames still differ" {
                let mono = GlyphConformance.monochrome theme
                Expect.equal (Theme.allegiance Identity.Enemy mono) (Theme.allegiance Identity.Own mono) "one grey"
                let team t = (Glyphs.scene mono [ unitAt 24.0 24.0 Identity.Own |> GlyphSpec.team t |> GlyphSpec.badged |> Glyph.ofSpec idle ]).Nodes
                let ground = SceneNode.Rectangle((0.0, 0.0, 48.0, 48.0), mono.Palette.Ground)
                Expect.isGreaterThan (differing 48.0 48.0 (ground :: team 0) (ground :: team 1)) 0 "team 1 against team 2"
            }

            test "families: terrain, items, markers and paths draw in their tokens and report what they cannot show" {
                let draw spec = Glyphs.draw theme (Glyph.ofSpec idle spec)
                let water = draw (GlyphSpec.create GlyphFamily.terrain { X = 16.0; Y = 16.0 } one |> GlyphSpec.icon (IconRef.named "water"))
                Expect.equal (GlyphDrawing.body water) [ SceneNode.Rectangle((2.0, 2.0, 28.0, 28.0), Theme.tryColour "terrain.water" theme |> Option.get) ] "a tile inset by the gap"
                let item = draw (GlyphSpec.create GlyphFamily.item { X = 16.0; Y = 16.0 } one)
                Expect.equal (GlyphDrawing.frame item).Width 12.0 "an item is half the frame"
                Expect.equal (GlyphDrawing.hit item) SelectableShape.Ellipse "ellipse hit"
                let lava = draw (GlyphSpec.create GlyphFamily.terrain { X = 16.0; Y = 16.0 } one |> GlyphSpec.icon (IconRef.named "lava"))
                Expect.isNonEmpty (GlyphDrawing.diagnostics lava) "an unknown kind"
                let facing = draw (unitAt 16.0 16.0 Identity.Own |> GlyphSpec.amplifier AmplifierSlot.facing (Amplifier.facing 1.0))
                Expect.exists (GlyphDrawing.diagnostics facing) (fun d -> d.Contains "stroked profile") "facing waits for #773"
                let path = Glyphs.draw theme (Glyph.path [ { X = 0.0; Y = 0.0 }; { X = 64.0; Y = 0.0 } ])
                Expect.equal (GlyphDrawing.body path).Length (1 + 4 + 1) "dots every half cell and the end marker"
                let staleDrawing = Glyphs.draw theme (Glyph.ofSpec (GlyphState.stale true idle) (unitAt 16.0 16.0 Identity.Own))
                Expect.equal (GlyphDrawing.body staleDrawing).Head (SceneNode.Rectangle((4.0, 8.0, 24.0, 16.0), theme.Palette.Stale)) "a stale frame takes glyph.stale"
            }
        ]
