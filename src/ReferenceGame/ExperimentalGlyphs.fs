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
