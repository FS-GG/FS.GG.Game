namespace FS.GG.Game.Reference.Experimental

open FS.GG.UI.Scene

/// EXPERIMENTAL. The proof of GAME-RENDER-02.4 (FS.GG.Game#789, ADR `docs/roadmaps/game-render-02-glyphs.md`): six
/// parameterised glyphs and two themes, lowered to the portable primitive set (opaque filled rectangles, circles and
/// ellipses, through groups and translations) that SVG, Pixi and Babylon draw today and `ScenePixels` models. It is
/// not a contract: the library the ADR specifies lives in FS.GG.Game.Render after acceptance, and this module goes away
/// with that implementation. Only `SceneNode` data is used, because the Fable view of FS.GG.UI.Scene has no `Scene`,
/// `Colors` or `Paint` constructors (the gap the ADR routes to FS.GG.Rendering).
module Glyphs =

    /// Theme tokens: every colour, stroke and size a glyph draws with comes from here, never from the glyph.
    type Theme =
        {
            Name: string
            /// Terrain the glyphs sit on.
            Ground: Color
            /// The neutral body of units and structures; team colour is the ring or band around it.
            Body: Color
            /// Team colours by team index (wraps).
            Teams: Color list
            /// Selection brackets, focus outline and waypoint marker.
            Selection: Color
            Focus: Color
            Marker: Color
            /// Status bar track and its good/warning/bad fills. The bar is keylined in `Body`, so the track and the fills are
            /// validated against their real neighbours: the keyline against the ground, each fill against the track.
            Track: Color
            /// The ink of the team index badge, drawn inside its frame (ADR D1): validated against every frame fill it can
            /// sit on.
            BadgeInk: Color
            Good: Color
            Warn: Color
            Bad: Color
            /// The outline width, in scene units (an outline is drawn as filled bands until canvas strokes land, #773).
            Stroke: float
            /// The clear gap between a glyph and its selection or focus outline, so the outline never touches (and
            /// never merges with) a body of similar lightness.
            Gap: float
            /// The edge length of one cell, the size every glyph scales from.
            Cell: float
        }

    let private rgb (r: byte) (g: byte) (b: byte) : Color = { Red = r; Green = g; Blue = b; Alpha = 255uy }

    module Theme =

        /// The default theme.
        let standard: Theme =
            {
                Name = "standard"
                Ground = rgb 34uy 40uy 49uy
                Body = rgb 225uy 228uy 232uy
                Teams = [ rgb 64uy 140uy 255uy; rgb 235uy 80uy 70uy; rgb 70uy 190uy 110uy; rgb 240uy 200uy 60uy ]
                Selection = rgb 255uy 214uy 0uy
                Focus = rgb 255uy 255uy 255uy
                Marker = rgb 0uy 220uy 220uy
                Track = rgb 10uy 10uy 10uy
                BadgeInk = rgb 10uy 10uy 10uy
                Good = rgb 70uy 200uy 100uy
                Warn = rgb 240uy 180uy 40uy
                Bad = rgb 230uy 60uy 50uy
                Stroke = 2.0
                Gap = 2.0
                Cell = 32.0
            }

        /// The colour-blind-safe theme: Okabe and Ito's palette (2008), whose hues stay distinct under protanopia,
        /// deuteranopia and tritanopia; status reads by bar length as well as colour. Their blue (0, 114, 178) is 2.86:1
        /// on this ground, under the 3:1 floor, so team 0 is their sky blue.
        let colourBlindSafe: Theme =
            { standard with
                Name = "colour-blind-safe"
                Teams = [ rgb 86uy 180uy 233uy; rgb 230uy 159uy 0uy; rgb 0uy 158uy 115uy; rgb 204uy 121uy 167uy ]
                Selection = rgb 240uy 228uy 66uy
                Focus = rgb 255uy 255uy 255uy
                Marker = rgb 213uy 94uy 0uy
                Good = rgb 0uy 158uy 115uy
                Warn = rgb 230uy 159uy 0uy
                Bad = rgb 213uy 94uy 0uy
            }

        /// The team colour of `team` (the list wraps).
        let team (theme: Theme) (team: int) : Color =
            theme.Teams.[((team % theme.Teams.Length) + theme.Teams.Length) % theme.Teams.Length]

        /// WCAG 2.x relative luminance of an sRGB colour.
        let luminance (colour: Color) : float =
            let channel (value: byte) =
                let c = float value / 255.0
                if c <= 0.03928 then c / 12.92 else ((c + 0.055) / 1.055) ** 2.4

            0.2126 * channel colour.Red + 0.7152 * channel colour.Green + 0.0722 * channel colour.Blue

        /// WCAG 2.x contrast ratio, 1 to 21.
        let contrast (a: Color) (b: Color) : float =
            let la = luminance a
            let lb = luminance b
            (max la lb + 0.05) / (min la lb + 0.05)

    /// A glyph: what it means and where it is, never how it looks (the theme decides that).
    type Glyph =
        /// A unit at a cell centre: a neutral disc ringed in its team colour.
        | Unit of team: int * centre: Point
        /// A structure at a cell centre: a neutral square with a team-colour band along its foot.
        | Structure of team: int * centre: Point
        /// A waypoint marker at a point: a plus.
        | Waypoint of centre: Point
        /// Selection: corner brackets around `bounds`, one theme gap clear of them.
        | Selection of bounds: Rect
        /// Keyboard focus: an outline around `bounds`, one theme gap clear of them; distinct from selection by colour and
        /// shape.
        | Focus of bounds: Rect
        /// A status bar over `bounds`: the track and a fill whose length is `fraction` (0 to 1).
        | Status of bounds: Rect * fraction: float

    let private box (x: float) (y: float) (w: float) (h: float) (colour: Color) =
        SceneNode.Rectangle((x, y, w, h), colour)

    let private disc (centre: Point) (radius: float) (colour: Color) = SceneNode.Circle(centre, radius, colour)

    /// The four bands of an outline of width `w` just outside `bounds`, as filled rectangles (opaque, so every pixel
    /// is decided; a canvas stroke replaces them once #773 lands).
    let private outline (bounds: Rect) (w: float) (colour: Color) =
        [
            box (bounds.X - w) (bounds.Y - w) (bounds.Width + 2.0 * w) w colour
            box (bounds.X - w) (bounds.Y + bounds.Height) (bounds.Width + 2.0 * w) w colour
            box (bounds.X - w) bounds.Y w bounds.Height colour
            box (bounds.X + bounds.Width) bounds.Y w bounds.Height colour
        ]

    let private grow (bounds: Rect) (by: float) : Rect =
        { X = bounds.X - by; Y = bounds.Y - by; Width = bounds.Width + 2.0 * by; Height = bounds.Height + 2.0 * by }

    /// The five identities (ADR D1). The frame shape follows from the identity in the library; a theme chooses colours only.
    type Identity =
        | Own
        | Ally
        | Neutral
        | Enemy
        | Unknown

    /// All five identities, in order.
    let identities = [ Own; Ally; Neutral; Enemy; Unknown ]

    /// Snap a coordinate to a whole unit, half up (`Floor(v + 0.5)`, never `Math.Round`, as for colours in ADR D11).
    let snap (v: float) = System.Math.Floor(v + 0.5)

    /// The identity frame of `identity`, `size` units across, centred on `centre`, built only from opaque bars (filled
    /// rectangles), so every backend draws it and `ScenePixels` decides its edges (ADR D1, reviews R3 and N1 of #794):
    /// - Own: the APP-6 friend rectangle, 3:2;
    /// - Ally: the friend rectangle with true holes (no paint) at the quarter points of its top and bottom edges, never
    ///   at an edge midpoint, so the box-edge probes at the midpoints still land on the frame;
    /// - Neutral: a square;
    /// - Enemy: a stepped diamond of rows one step high (`max 1 (floor (size / 12))`), two steps narrower than the square
    ///   at its middle and narrowing by two steps per row;
    /// - Unknown: a stepped quatrefoil, a cross whose arms reach one band past the square on each side.
    /// The band is `max 1 (floor (size / 8))`. The centre is snapped to whole units and the size floored, always: the
    /// distinguishability matrix holds only for whole-unit frames (off-grid centres gave 0-pixel pairs).
    let identityFrame (identity: Identity) (centre: Point) (size: float) (ink: Color) : SceneNode list =
        let cx = snap centre.X
        let cy = snap centre.Y
        let w = System.Math.Floor size
        let band = max 1.0 (System.Math.Floor(w / 8.0))
        let half = System.Math.Floor(w / 2.0)
        let at (dx: float) (dy: float) (bw: float) (bh: float) = box (cx + dx) (cy + dy) bw bh ink

        match identity with
        | Own ->
            let h = System.Math.Floor(w * 2.0 / 3.0)
            [ at -half -(System.Math.Floor(h / 2.0)) w h ]
        | Ally ->
            // Holes of n x n at the quarter points of the top and bottom edges: the top and bottom strips are three bars
            // each (left, middle, right), and the band between them is one full-width bar.
            let h = System.Math.Floor(w * 2.0 / 3.0)
            let top = -(System.Math.Floor(h / 2.0))
            let n = max 2.0 (System.Math.Floor(w / 6.0))
            let left = -half
            let q1 = left + System.Math.Floor(w / 4.0) - System.Math.Floor(n / 2.0)
            let q3 = left + System.Math.Floor(3.0 * w / 4.0) - System.Math.Floor(n / 2.0)
            let strip y =
                [
                    at left y (q1 - left) n
                    at (q1 + n) y (q3 - q1 - n) n
                    at (q3 + n) y (left + w - q3 - n) n
                ]
            [ yield! strip top; at left (top + n) w (h - 2.0 * n); yield! strip (top + h - n) ]
        | Neutral -> [ at -half -half w w ]
        | Enemy ->
            // Finer steps than the other frames' band (w / 12, not w / 8), so the outline reads as a diamond, not a blob.
            let step = max 1.0 (System.Math.Floor(w / 12.0))
            let steps = int (System.Math.Floor(half / step))

            [
                for i in 0 .. steps - 1 do
                    let rowWidth = w - 2.0 * step * float (i + 1)
                    if rowWidth > 0.0 then
                        let x = -System.Math.Floor(rowWidth / 2.0)
                        yield at x (-step * float (i + 1)) rowWidth step
                        yield at x (step * float i) rowWidth step
            ]
        | Unknown ->
            let reach = half + band
            let arm = System.Math.Floor(w / 2.0)
            let armHalf = System.Math.Floor(arm / 2.0)
            [ at -reach -armHalf (2.0 * reach) arm; at -armHalf -reach arm (2.0 * reach) ]

    /// The drawn bounding box of a list of rectangles: what `GlyphDrawing.frame` returns (ADR D1). At a nominal 24 units,
    /// Enemy's is 20 x 20 and Unknown's 30 x 30.
    let boundsOf (nodes: SceneNode list) : Rect =
        let rs = nodes |> List.choose (function SceneNode.Rectangle((x, y, w, h), _) -> Some(x, y, x + w, y + h) | _ -> None)
        let l = rs |> List.map (fun (l, _, _, _) -> l) |> List.min
        let t = rs |> List.map (fun (_, t, _, _) -> t) |> List.min
        let r = rs |> List.map (fun (_, _, r, _) -> r) |> List.max
        let b = rs |> List.map (fun (_, _, _, b) -> b) |> List.max
        { X = l; Y = t; Width = r - l; Height = b - t }

    /// The index badge (ADR D1, review N2 of #794): a seven-segment numeral 1-9 on a 3 x 5 grid of `unit`-sized cells
    /// whose top left is `topLeft`, so the digit is `5 * unit` tall. The unit is whole and at least 2, so the digit is at
    /// least 10 units tall (the 7-unit floor rounded up to whole cells).
    let badge (digit: int) (topLeft: Point) (unit: float) (ink: Color) : SceneNode list =
        let u = max 2.0 (System.Math.Floor unit)
        let x0 = snap topLeft.X
        let y0 = snap topLeft.Y
        let cell (c: float) (r: float) (cw: float) (rh: float) = box (x0 + c * u) (y0 + r * u) (cw * u) (rh * u) ink
        // Segments: a top, b upper right, c lower right, d bottom, e lower left, f upper left, g middle.
        let segment s =
            match s with
            | 'a' -> cell 0.0 0.0 3.0 1.0
            | 'b' -> cell 2.0 0.0 1.0 3.0
            | 'c' -> cell 2.0 2.0 1.0 3.0
            | 'd' -> cell 0.0 4.0 3.0 1.0
            | 'e' -> cell 0.0 2.0 1.0 3.0
            | 'f' -> cell 0.0 0.0 1.0 3.0
            | _ -> cell 0.0 2.0 3.0 1.0
        let segments =
            match digit with
            | 1 -> "bc"
            | 2 -> "abged"
            | 3 -> "abgcd"
            | 4 -> "fgbc"
            | 5 -> "afgcd"
            | 6 -> "afgedc"
            | 7 -> "abc"
            | 8 -> "abcdefg"
            | _ -> "abcdfg"
        [ for ch in segments -> segment ch ]

    /// The smallest frame size at which an index badge (unit 2: 6 x 10) fits inside every identity frame's ink, centred,
    /// with a 1-unit margin all round (measured: Neutral 12, Unknown 16, Own 18, Enemy 20, Ally 22).
    let minBadgeFrame = 22.0

    /// An identity frame with its team index badge drawn INSIDE it, right after it, in the body pass (ADR D1, review M1 of
    /// #794): the badge sits at the frame's centre, so it moves with the frame and no neighbour can cover it. Below
    /// `minBadgeFrame` the badge is omitted and a diagnostic says so; team then rests on the allegiance frame and the
    /// mirror label.
    let identityFrameWithBadge
        (identity: Identity)
        (centre: Point)
        (size: float)
        (ink: Color)
        (badgeInk: Color)
        (team: int option)
        : SceneNode list * string option =
        let frame = identityFrame identity centre size ink
        match team with
        | None -> frame, None
        | Some index when System.Math.Floor size < minBadgeFrame ->
            frame, Some(sprintf "badge for team %d omitted: frame %g is below %g units" (index + 1) (System.Math.Floor size) minBadgeFrame)
        | Some index ->
            let digit = (index % 9) + 1
            frame @ badge digit { X = snap centre.X - 3.0; Y = snap centre.Y - 5.0 } 2.0 badgeInk, None

    /// The scene nodes of one glyph in `theme`, in the portable primitive set.
    let draw (theme: Theme) (glyph: Glyph) : SceneNode list =
        let cell = theme.Cell
        let s = theme.Stroke

        match glyph with
        | Unit(team, centre) ->
            let radius = cell * 0.375
            [ disc centre radius (Theme.team theme team); disc centre (radius - 2.0 * s) theme.Body ]
        | Structure(team, centre) ->
            let half = cell * 0.375
            let band = 2.0 * s
            [
                box (centre.X - half) (centre.Y - half) (2.0 * half) (2.0 * half) theme.Body
                box (centre.X - half) (centre.Y + half - band) (2.0 * half) band (Theme.team theme team)
            ]
        | Waypoint centre ->
            let arm = cell * 0.25
            let w = 2.0 * s
            [
                box (centre.X - arm) (centre.Y - w / 2.0) (2.0 * arm) w theme.Marker
                box (centre.X - w / 2.0) (centre.Y - arm) w (2.0 * arm) theme.Marker
            ]
        | Selection inner ->
            let bounds = grow inner theme.Gap
            // Corner brackets: each corner is two bands of length `arm` and width `s`, just outside the bounds.
            let arm = max (2.0 * s) (System.Math.Floor(min bounds.Width bounds.Height * 0.3))
            let l = bounds.X - s
            let t = bounds.Y - s
            let r = bounds.X + bounds.Width
            let b = bounds.Y + bounds.Height
            let c = theme.Selection

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
        | Focus inner -> outline (grow inner theme.Gap) s theme.Focus
        | Status(bounds, fraction) ->
            let f = fraction |> max 0.0 |> min 1.0
            let fill = if f > 0.6 then theme.Good elif f > 0.3 then theme.Warn else theme.Bad
            let inset = s / 2.0

            // The bar's keyline (the body colour) separates it from the ground; the dark track inside separates the fill
            // from what is empty. A dark track alone measured 1.33:1 on the ground (the #794 review).
            [
                box bounds.X bounds.Y bounds.Width bounds.Height theme.Body
                box (bounds.X + inset) (bounds.Y + inset) (bounds.Width - 2.0 * inset) (bounds.Height - 2.0 * inset) theme.Track
                box (bounds.X + inset) (bounds.Y + inset) (System.Math.Floor((bounds.Width - 2.0 * inset) * f)) (bounds.Height - 2.0 * inset) fill
            ]

    /// A scene of glyphs on the theme's ground, `width` x `height` scene units.
    let scene (theme: Theme) (width: float) (height: float) (glyphs: Glyph list) : Scene =
        { Nodes = box 0.0 0.0 width height theme.Ground :: (glyphs |> List.collect (draw theme)) }

    /// The proof board: one row per theme, each with the six glyphs (a selected, damaged unit of team 0; a focused
    /// structure of team 1; a unit of team 2; a waypoint; a full status bar).
    let proofWidth = 256.0
    let proofHeight = 128.0

    let private row (theme: Theme) (top: float) : Glyph list =
        let c = theme.Cell
        let at col = { X = 16.0 + c * float col + c / 2.0; Y = top + 16.0 + c / 2.0 }
        let cellBounds col = { X = 16.0 + c * float col + 4.0; Y = top + 16.0 + 4.0; Width = c - 8.0; Height = c - 8.0 }
        let statusOver col fraction =
            Status({ X = 16.0 + c * float col + 4.0; Y = top + 6.0; Width = c - 8.0; Height = 6.0 }, fraction)

        [
            Unit(0, at 0)
            Selection(cellBounds 0)
            statusOver 0 0.25
            Structure(1, at 2)
            Focus(cellBounds 2)
            statusOver 2 0.5
            Unit(2, at 4)
            statusOver 4 1.0
            Waypoint(at 6)
        ]

    let proofScene (top: Theme) (bottom: Theme) : Scene =
        { Nodes =
            [
                yield box 0.0 0.0 proofWidth proofHeight top.Ground
                yield box 0.0 (proofHeight / 2.0) proofWidth (proofHeight / 2.0) bottom.Ground
                yield! row top 0.0 |> List.collect (draw top)
                yield! row bottom (proofHeight / 2.0) |> List.collect (draw bottom)
            ] }

    /// D9's dense board (review N2 of #794): a `columns` x `rows` grid of 32-unit tiles, each holding a 24-unit identity
    /// frame with a status bar above it in the amplifier slot, outside the 8-unit cue reserve, so each bar reaches into
    /// the tile above. With `amplifiersFirst` the amplifier pass draws before the bodies, so frames win over neighbours'
    /// amplifiers (the ruled order); without it, bodies draw first and the bars cover the frames above (the control). Each
    /// frame carries its team index badge inside it, drawn right after it in the body pass (review M1 of #794).
    let denseBoard (theme: Theme) (columns: int) (rows: int) (amplifiersFirst: bool) : Scene =
        let tile = 32.0
        let frames =
            [
                for r in 0 .. rows - 1 do
                    for c in 0 .. columns - 1 do
                        let identity = identities.[(r * columns + c) % identities.Length]
                        let centre = { X = tile * float c + tile / 2.0; Y = tile * float r + tile / 2.0 + 8.0 }
                        let ink = if (r + c) % 2 = 0 then theme.Body else Theme.team theme (r + c)
                        let nodes, _ = identityFrameWithBadge identity centre 24.0 ink theme.BadgeInk (Some(r * columns + c))
                        let frame = boundsOf (identityFrame identity centre 24.0 ink)
                        let bar = { X = frame.X; Y = frame.Y - 8.0 - 6.0; Width = 24.0; Height = 6.0 }
                        yield nodes, draw theme (Status(bar, 0.25 + 0.25 * float ((r + c) % 3)))
            ]
        let bodies = frames |> List.collect fst
        let amplifiers = frames |> List.collect snd
        let ground = SceneNode.Rectangle((0.0, 0.0, tile * float columns, tile * float rows + 8.0), theme.Ground)
        { Nodes = ground :: (if amplifiersFirst then amplifiers @ bodies else bodies @ amplifiers) }

    /// EXPERIMENTAL prototype of ADR D6's corner and quarter-edge samples for identity frames and badges (review R3 of
    /// #794), for scenes of opaque rectangles only: `ScenePixels.expectations` plus, for every rectangle, the pixels
    /// just inside and just outside each corner, and just inside and outside each edge at a quarter and three quarters
    /// of its length. A sample is kept when every rectangle covers its pixel wholly or not at all (the kit's Full/Clear
    /// rule), with the colour of the last rectangle covering it, or the clear colour (black) when none does.
    let identitySamples (width: float) (height: float) (scene: Scene) : FS.GG.Game.Render.PixelExpectation list =
        let rects =
            scene.Nodes
            |> List.choose (function SceneNode.Rectangle((x, y, w, h), colour) -> Some(x, y, x + w, y + h, colour) | _ -> None)
        let columns = int (System.Math.Floor width)
        let rows = int (System.Math.Floor height)
        let colourAt (col: int) (row: int) =
            let x0, y0 = float col, float row
            let x1, y1 = x0 + 1.0, y0 + 1.0
            let folder (top, partial) (l, t, r, b, colour) =
                if x1 <= l || x0 >= r || y1 <= t || y0 >= b then top, partial
                elif x0 >= l && x1 <= r && y0 >= t && y1 <= b then Some colour, false
                else top, true
            match List.fold folder (None, false) rects with
            | _, true -> None
            | Some colour, false -> Some colour
            | None, false -> Some { Red = 0uy; Green = 0uy; Blue = 0uy; Alpha = 255uy }
        let extra =
            [
                for (l, t, r, b, _) in rects do
                    let li, ti = int (System.Math.Ceiling l), int (System.Math.Ceiling t)
                    let ri, bi = int (System.Math.Floor r) - 1, int (System.Math.Floor b) - 1
                    let lo, tout = int (System.Math.Floor l) - 1, int (System.Math.Floor t) - 1
                    let ro, bout = int (System.Math.Ceiling r), int (System.Math.Ceiling b)
                    yield! [ li, ti; ri, ti; li, bi; ri, bi; lo, tout; ro, tout; lo, bout; ro, bout ]
                    for f in [ 0.25; 0.75 ] do
                        let x = int (System.Math.Floor(l + (r - l) * f))
                        let y = int (System.Math.Floor(t + (b - t) * f))
                        yield! [ x, ti; x, tout; x, bi; x, bout; li, y; lo, y; ri, y; ro, y ]
            ]
        let fromKit = FS.GG.Game.Render.ScenePixels.expectations width height scene
        let seen = System.Collections.Generic.HashSet<int * int>()
        for e in fromKit do
            seen.Add((int (System.Math.Floor e.Point.X), int (System.Math.Floor e.Point.Y))) |> ignore
        fromKit
        @ [
            for col, row in extra do
                if col >= 0 && col < columns && row >= 0 && row < rows && seen.Add((col, row)) then
                    match colourAt col row with
                    | Some colour -> yield FS.GG.Game.Render.PixelExpectation.create { X = float col + 0.5; Y = float row + 0.5 } colour
                    | None -> ()
        ]
